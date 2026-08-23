using Random = System.Random;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System;
using UnityEngine;


namespace SilksongItemRandomizer
{
    /// <summary>
    /// 陷阱配置类（独立传入）
    /// </summary>
    public class TrapRandomConfig
    {
        public int Seed = 0;
        public bool Enabled = false;
        public bool MovementEnabled = true;
        public int Difficulty = 0;           // 0=Beginner, 1=Focused, 2=Overflow
        public HashSet<string> FrostBannedScenes = new HashSet<string>();  // 已禁用冰冻的场景
    }

    /// <summary>
    /// 陷阱随机核心逻辑
    /// 改造后：所有配置通过 Initialize 传入，不再依赖 Plugin.SaveData
    /// 保留所有原有功能：陷阱生成、移动、难度控制、冻结机制等
    /// </summary>
    public static class TrapRandomizer
    {
        // ========== 运行时状态 ==========
        private static bool _initialized = false;
        private static bool _enabled = false;
        private static bool _movementEnabled = true;
        private static int _difficulty = 0;           // 0=Beginner, 1=Focused, 2=Overflow
        private static int _masterSeed;
        private static Random _rng;

        private static readonly List<GameObject> ActiveTraps = new();
        private static HashSet<string> _frostBannedScenes = new();

        // 场景扫描缓存
        private static List<Vector3> _surfacePoints = new();
        private static List<Vector3> _ceilingPoints = new();
        private static List<Vector3> _wallPoints = new();
        private static List<(Vector3 center, float minX, float maxX, float waterY)> _waterRegions = new();
        private static List<Vector2> _doorPositions = new();
        private static List<(Vector3 pos, Bounds? bounds)> _pickupData = new();
        private static string _lastScene = "";

        // 缓存已生成的陷阱（场景名 -> 陷阱列表）
        private static readonly Dictionary<string, List<(Vector3, string)>> _cachedTraps = new();
        private static readonly Dictionary<string, List<(Vector3, string)>> _cachedBounceObjects = new();
        private static readonly Dictionary<string, List<(Vector3, string)>> _cachedPlatforms = new();
        private static readonly Dictionary<string, bool> _lastSceneFrostRecord = new();

        // 追逐型陷阱计数
        private static int _chasingTrapsInScene = 0;

        // ========== 公共属性（供外部查询） ==========
        public static bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                if (_enabled)
                    SpawnTraps();
                else
                    ClearAll();
            }
        }

        public static bool MovementEnabled
        {
            get => _movementEnabled;
            set
            {
                if (_movementEnabled == value) return;
                _movementEnabled = value;
                // 重新生成陷阱（会重新应用移动组件）
                if (_enabled)
                {
                    ClearAll();
                    SpawnTraps();
                }
            }
        }

        public static int CurrentDifficulty
        {
            get => _difficulty;
            set
            {
                if (_difficulty == value) return;
                _difficulty = value;
                if (_enabled)
                {
                    ClearAll();
                    SpawnTraps();
                }
            }
        }

        // ========== 初始化 ==========
        public static void Initialize(TrapRandomConfig config)
        {
            if (config == null) return;
            _masterSeed = config.Seed;
            _enabled = config.Enabled;
            _movementEnabled = config.MovementEnabled;
            _difficulty = config.Difficulty;
            _frostBannedScenes = config.FrostBannedScenes ?? new HashSet<string>();
            _rng = new Random(_masterSeed);

            _initialized = true;
            Plugin.Log.LogInfo($"陷阱随机系统初始化: 种子={_masterSeed}, 难度={_difficulty}, 移动={_movementEnabled}");
        }

        /// <summary>
        /// 设置冻结黑名单（用于冰冻交替机制）
        /// </summary>
        public static void SetFrostBannedScenes(HashSet<string> bannedScenes)
        {
            _frostBannedScenes = bannedScenes ?? new HashSet<string>();
        }

        public static void ResetFrostBannedScenes()
        {
            _frostBannedScenes.Clear();
        }

        // ========== 外部调用接口 ==========
        public static void SpawnTraps()
        {
            if (!_initialized || !_enabled) return;

            var hero = HeroController.instance;
            if (hero == null) return;

            var scene = GameManager.instance?.sceneName ?? "";
            if (TrapPreloader.ExcludedScenes.Contains(scene))
            {
                _cachedTraps.Remove(scene);
                return;
            }

            ClearAll();  // 清除当前场景的陷阱
            if (scene != _lastScene)
                ScanSceneSurfaces();  // 仅场景变化时全量重扫地形（复用上次扫描结果）

            var rng = new Random(_masterSeed ^ scene.GetHashCode());
            var minY = _surfacePoints.Count > 0 ? _surfacePoints.Min(p => p.y) : 0f;
            var frostProb = GetFrostProbability(_difficulty);
            _chasingTrapsInScene = 0;

            var allPoints = new List<(Vector3 pt, bool isCeiling)>();
            allPoints.AddRange(_surfacePoints.Select(p => (p, false)));
            allPoints.AddRange(_ceilingPoints.Select(p => (p, true)));

            var quotas = TrapPreloader.GetCategoryQuotas((TrapPreloader.TrapDifficulty)_difficulty);

            // 冰冻交替决策
            bool hasFrostInCache = _cachedTraps.TryGetValue(scene, out var cachedTrapsForScene) && cachedTrapsForScene.Any(c => c.Item2 == "frost_marker");
            bool allowFrostThisTime;
            if (hasFrostInCache)
            {
                bool lastHadFrost = _lastSceneFrostRecord.ContainsKey(scene) && _lastSceneFrostRecord[scene];
                allowFrostThisTime = !lastHadFrost;
            }
            else
            {
                allowFrostThisTime = !_lastSceneFrostRecord.ContainsKey(scene) && frostProb > 0 && !_frostBannedScenes.Contains(scene) && rng.NextDouble() < frostProb;
            }

            // 尝试使用缓存
            if (_cachedTraps.TryGetValue(scene, out var cacheFromDict))
            {
                var filteredCache = allowFrostThisTime ? cacheFromDict : cacheFromDict.Where(c => c.Item2 != "frost_marker").ToList();
                var validated = new List<(Vector3 pos, string trapId)>();
                var tempUsed = new List<Vector3>();
                bool frostActuallyGenerated = false;

                foreach (var (pos, trapId) in filteredCache)
                {
                    bool isCeiling = _ceilingPoints.Any(p => Vector3.Distance(p, pos) < 0.5f);
                    var cat = GetCategoryForTrapId(trapId);
                    if (tempUsed.Any(u => Vector3.Distance(pos, u) < TrapPreloader.MinDistance)) continue;
                    if (_doorPositions.Any(d => Vector2.Distance(pos, d) < TrapPreloader.DoorSafeRadius)) continue;
                    if (!IsTrapAllowed(trapId, pos, scene, minY, isCeiling, cat)) continue;
                    validated.Add((pos, trapId));
                    tempUsed.Add(pos);
                    if (trapId == "frost_marker") frostActuallyGenerated = true;
                }

                var totalQuota = TrapPreloader.CategoryOrder.Where(c => c != "场景伤害").Sum(c => quotas.ContainsKey(c) ? quotas[c] : 0);
                if (validated.Count >= totalQuota * 0.8f)
                {
                    ShuffleList(validated, rng);
                    foreach (var (pos, trapId) in validated)
                        ArchitectSpawn(trapId, pos);
                    _lastSceneFrostRecord[scene] = frostActuallyGenerated;
                    SpawnBounceObjects(rng, scene);
                    SpawnPlatforms(rng, scene);
                    return;
                }
                _cachedTraps.Remove(scene);
            }

            // 全新生成陷阱
            var selected = new List<(string trapId, string category)>();
            foreach (var cat in TrapPreloader.CategoryOrder)
            {
                if (!TrapPreloader.TrapCategories.TryGetValue(cat, out var pool) || pool.Count == 0) continue;
                var quota = quotas.ContainsKey(cat) ? quotas[cat] : 0;
                if (cat == "追逐型" && _chasingTrapsInScene >= 1) continue;

                for (int i = 0; i < quota; i++)
                {
                    if (cat == "场景伤害" && _waterRegions.Count == 0) continue;
                    string trapId;
                    if (cat == "场景伤害")
                    {
                        if (rng.NextDouble() >= 0.05) continue;
                        trapId = pool[rng.Next(pool.Count)];
                    }
                    else if (rng.NextDouble() < 0.7)
                    {
                        trapId = pool[rng.Next(pool.Count)];
                    }
                    else
                    {
                        trapId = TrapPreloader.TrapPoolNoLava[rng.Next(TrapPreloader.TrapPoolNoLava.Count)];
                    }

                    if (trapId == "frost_marker" && !allowFrostThisTime) continue;
                    selected.Add((trapId, cat));
                }
            }

            if (_waterRegions.Count > 0 && rng.NextDouble() < 0.3 && !TrapPreloader.NoLavaScenes.Contains(scene))
                selected.Add((TrapPreloader.LavaTrapId, "场景伤害"));

            ShuffleList(selected, rng);

            var usedPositions = new List<Vector3>();
            var darkThunderPositions = new List<Vector3>();
            var newCache = new List<(Vector3, string)>();
            bool newFrostActuallyGenerated = false;

            foreach (var (trapId, category) in selected)
            {
                if (category == "暗雷" && darkThunderPositions.Count >= TrapPreloader.MaxDarkThunderCount) continue;

                var candidatePoints = new List<Vector3>();
                bool isCeilingTrap = category == "天花板";
                foreach (var (pt, isCeil) in allPoints)
                {
                    if (isCeilingTrap != isCeil) continue;
                    candidatePoints.Add(pt);
                }

                if (category == "暗雷" && darkThunderPositions.Count > 0)
                {
                    var nearbyPoints = new List<Vector3>();
                    foreach (var darkPos in darkThunderPositions)
                    {
                        nearbyPoints.AddRange(candidatePoints.Where(pt => Vector2.Distance(pt, darkPos) <= TrapPreloader.DarkThunderChainDistance));
                    }
                    candidatePoints = nearbyPoints.Distinct().ToList();
                }

                var validPoints = new List<Vector3>();
                foreach (var pt in candidatePoints)
                {
                    if (_doorPositions.Any(d => Vector2.Distance(pt, d) < TrapPreloader.DoorSafeRadius)) continue;
                    if (IsTooCloseToPickup(pt)) continue;
                    bool isCeil = _ceilingPoints.Any(cp => Vector2.Distance(cp, pt) < 0.5f);
                    if (!IsTrapAllowed(trapId, pt, scene, minY, isCeil, category)) continue;
                    if (usedPositions.Any(u => Vector2.Distance(pt, u) < TrapPreloader.MinDistance)) continue;
                    validPoints.Add(pt);
                }

                if (validPoints.Count == 0) continue;
                var chosen = validPoints[rng.Next(validPoints.Count)];
                if (trapId == TrapPreloader.LavaTrapId && _waterRegions.Count > 0)
                {
                    var water = _waterRegions.FirstOrDefault(w => chosen.x >= w.minX && chosen.x <= w.maxX);
                    chosen.y = (water != default) ? water.waterY - 3f : _waterRegions[0].waterY - 3f;
                }

                ArchitectSpawn(trapId, chosen);
                usedPositions.Add(chosen);
                if (category == "暗雷") darkThunderPositions.Add(chosen);
                newCache.Add((chosen, trapId));
                if (trapId == "frost_marker") newFrostActuallyGenerated = true;
                if (category == "追逐型") _chasingTrapsInScene++;
            }

            if (newCache.Count > 0) _cachedTraps[scene] = newCache;
            _lastSceneFrostRecord[scene] = newFrostActuallyGenerated;
            SpawnBounceObjects(rng, scene);
            SpawnPlatforms(rng, scene);
        }

        public static void ClearAll()
        {
            foreach (var t in ActiveTraps)
                if (t) UnityEngine.Object.Destroy(t);
            ActiveTraps.Clear();
            CleanupArchitectDict();
        }

        // 本 mod 经 ArchitectSpawn 写入 PlacementManager.Objects 的 pid 记录，
        // ClearAll 销毁对象后同步移除，避免字典滞留已销毁对象的引用（无界增长）。
        private static readonly System.Collections.Generic.List<string> _spawnedArchitectPids = new System.Collections.Generic.List<string>();

        private static void CleanupArchitectDict()
        {
            if (_spawnedArchitectPids.Count == 0) return;
            try
            {
                var pmType = Type.GetType("Architect.Placements.PlacementManager, Architect");
                var objDict = pmType?.GetField("Objects", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as System.Collections.IDictionary;
                if (objDict != null)
                {
                    foreach (var pid in _spawnedArchitectPids)
                        objDict.Remove(pid);
                }
            }
            catch { }
            _spawnedArchitectPids.Clear();
        }

        public static void ClearAndRescan()
        {
            ClearAll();
            _surfacePoints.Clear();
            _ceilingPoints.Clear();
            _wallPoints.Clear();
            _waterRegions.Clear();
            _doorPositions.Clear();
            _pickupData.Clear();
            _lastScene = "";
        }

        public static void RespawnTraps()
        {
            if (!_enabled) return;
            ClearAndRescan();
            SpawnTraps();
        }

        // ========== 场景扫描 ==========
        private static void ScanSceneSurfaces()
        {
            _surfacePoints.Clear();
            _ceilingPoints.Clear();
            var hero = HeroController.instance;
            var center = hero ? (Vector2)hero.transform.position : Vector2.zero;
            var scene = GameManager.instance?.sceneName ?? "";
            var isLarge = TrapPreloader.LargeRooms.Contains(scene);
            var sw = isLarge ? 60f : 120f;
            var sh = isLarge ? 120f : 80f;
            var min = center + new Vector2(-sw, -sh);
            var max = center + new Vector2(sw, sh);
            var cols = Physics2D.OverlapAreaAll(min, max, LayerMask.GetMask("Terrain"));
            var terrainMask = LayerMask.GetMask("Terrain");

            foreach (var col in cols)
            {
                var bounds = col.bounds;
                if (bounds.size.y > bounds.size.x * 2f || bounds.min.y > center.y + 120f) continue;

                for (var x = bounds.min.x + 0.5f; x <= bounds.max.x - 0.5f; x += 1f)
                {
                    var surfacePoint = new Vector2(x, bounds.max.y + 0.5f);
                    if (Physics2D.Raycast(surfacePoint, Vector2.up, 1.5f, terrainMask).collider) continue;
                    if (Physics2D.Raycast(surfacePoint, Vector2.down, 0.5f, terrainMask).collider != col) continue;
                    _surfacePoints.Add(new Vector3(x, bounds.max.y + 0.5f, 0));

                    var ceilingPoint = new Vector2(x, bounds.min.y - 0.5f);
                    if (Physics2D.Raycast(ceilingPoint, Vector2.up, 0.5f, terrainMask).collider != col) continue;
                    if (Physics2D.Raycast(ceilingPoint, Vector2.down, 1.5f, terrainMask).collider) continue;
                    _ceilingPoints.Add(new Vector3(x, bounds.min.y - 0.5f, 0));
                }
            }

            _doorPositions.Clear();
            foreach (var tp in UnityEngine.Object.FindObjectsOfType<TransitionPoint>())
                _doorPositions.Add(tp.transform.position);

            ScanPickups();
            ScanWaterRegions();
            _lastScene = scene;
        }

        private static void ScanPickups()
        {
            _pickupData.Clear();
            foreach (var p in Resources.FindObjectsOfTypeAll<CollectableItemPickup>())
            {
                if (!p || !p.gameObject.scene.isLoaded) continue;
                var collider = p.GetComponent<Collider2D>();
                _pickupData.Add((p.transform.position, collider ? collider.bounds : (Bounds?)null));
            }
        }

        private static void ScanWaterRegions()
        {
            _waterRegions.Clear();
            foreach (var w in UnityEngine.Object.FindObjectsOfType<SurfaceWaterRegion>())
            {
                var col = w.GetComponent<BoxCollider2D>();
                if (!col) continue;
                var bounds = col.bounds;
                _waterRegions.Add((new Vector3((bounds.min.x + bounds.max.x) / 2, w.transform.position.y + 0.4f, 0),
                    bounds.min.x, bounds.max.x, w.transform.position.y + 0.4f));
            }
        }

        // ========== 辅助方法 ==========
        private static float GetPlatformWidth(Vector2 pt)
        {
            var checkY = pt.y - 0.2f;
            var left = pt.x;
            for (int i = 0; i < 20; i++)
            {
                var hit = Physics2D.Raycast(new Vector2(left - 0.5f, checkY), Vector2.down, 1f, LayerMask.GetMask("Terrain"));
                if (hit.collider) left -= 0.5f;
                else break;
            }
            var right = pt.x;
            for (int i = 0; i < 20; i++)
            {
                var hit = Physics2D.Raycast(new Vector2(right + 0.5f, checkY), Vector2.down, 1f, LayerMask.GetMask("Terrain"));
                if (hit.collider) right += 0.5f;
                else break;
            }
            return right - left;
        }

        private static bool IsTooCloseToPickup(Vector3 p)
        {
            return _pickupData.Any(x => Vector2.Distance(p, x.pos) < TrapPreloader.PickupSafeRadius);
        }

        private static bool IsInsidePickupBounds(Vector3 p)
        {
            return _pickupData.Any(x => x.bounds.HasValue && x.bounds.Value.Contains(p));
        }

        private static bool CanPlaceLargeTrap(Vector2 origin)
        {
            var floor = Physics2D.Raycast(origin, Vector2.down, 2f, LayerMask.GetMask("Terrain")).collider;
            var hits = Physics2D.OverlapCircleAll(origin, TrapPreloader.LargeTrapRadius, LayerMask.GetMask("Terrain"));
            bool upper = false, lower = false;
            foreach (var c in hits)
            {
                if (c == floor) continue;
                var bounds = c.bounds;
                if (bounds.max.y > origin.y) upper = true;
                if (bounds.min.y < origin.y) lower = true;
                if (upper && lower) return false;
            }
            return !(upper && lower);
        }

        private static bool IsTrapAllowed(string id, Vector3 pos, string scene, float minY, bool isCeiling, string category)
        {
            if (scene == "Bonetown" && pos.x >= 45f && pos.x <= 82f && pos.y >= 3f && pos.y <= 14f)
                return false;

            if (id == TrapPreloader.LavaTrapId && TrapPreloader.NoLavaScenes.Contains(scene)) return false;
            if (id == TrapPreloader.LavaTrapId && _waterRegions.Count == 0) return false;
            if (id == TrapPreloader.FallingLavaId && _doorPositions.Any(d => Mathf.Abs(pos.x - d.x) < 9f)) return false;
            if (id == "frost_marker" && _frostBannedScenes.Contains(scene)) return false;
            if (TrapPreloader.LargeTraps.Contains(id) && !CanPlaceLargeTrap(pos)) return false;
            if (TrapPreloader.HammerTraps.Contains(id) && (pos.y - minY) < TrapPreloader.HammerTrapMinHeight) return false;

            if (category != "墙壁" && TrapPreloader.ThornTraps.Contains(id))
            {
                if (GetPlatformWidth(pos) < TrapPreloader.ThornTrapMinWidth || !CanPlaceLargeTrap(pos) || IsInsidePickupBounds(pos))
                    return false;
            }

            if (id == "cradle_spikes" && GetPlatformWidth(pos) < 9f) return false;
            if (category == "天花板" && !isCeiling) return false;
            if (category != "天花板" && isCeiling) return false;
            return true;
        }

        private static string GetCategoryForTrapId(string trapId)
        {
            foreach (var c in TrapPreloader.CategoryOrder)
            {
                if (TrapPreloader.TrapCategories.TryGetValue(c, out var pool) && pool.Contains(trapId))
                    return c;
            }
            return "";
        }

        private static void ShuffleList<T>(IList<T> list, Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        private static double GetFrostProbability(int difficulty)
        {
            return difficulty switch
            {
                1 => 0.10,
                2 => 0.20,
                _ => 0.0
            };
        }

        // ========== 弹跳物生成 ==========
        private static void SpawnBounceObjects(Random rng, string scene)
        {
            if (!TrapPreloader.TrapCategories.TryGetValue("跳跳乐1", out var bouncePool) || bouncePool.Count == 0)
                return;

            if (_cachedBounceObjects.TryGetValue(scene, out var cached))
            {
                foreach (var (pos, id) in cached)
                    ArchitectSpawn(id, pos);
                return;
            }

            var sourceTraps = new List<GameObject>(ActiveTraps);
            var usedPositions = new List<Vector3>();
            var newCache = new List<(Vector3, string)>();
            var terrainMask = LayerMask.GetMask("Terrain");

            foreach (var trap in sourceTraps)
            {
                if (trap == null) continue;
                var trapPos = trap.transform.position;
                bool placed = false;
                for (int attempt = 0; attempt < 10 && !placed; attempt++)
                    placed = TryPlaceBounceObject(trapPos, rng, bouncePool, usedPositions, terrainMask, 0f, 30f, newCache);
                for (int attempt = 0; attempt < 10 && !placed; attempt++)
                    placed = TryPlaceBounceObject(trapPos, rng, bouncePool, usedPositions, terrainMask, -30f, 0f, newCache);
            }

            if (newCache.Count > 0)
                _cachedBounceObjects[scene] = newCache;
        }

        private static bool TryPlaceBounceObject(Vector3 trapPos, Random rng, List<string> pool, List<Vector3> used,
            int terrainMask, float angleMin, float angleMax, List<(Vector3, string)> newCache)
        {
            var angle = (float)(rng.NextDouble() * (angleMax - angleMin) + angleMin);
            var rad = angle * Mathf.Deg2Rad;
            var targetPos = trapPos + new Vector3(Mathf.Cos(rad) * 6f, Mathf.Sin(rad) * 6f, 0);
            var target2D = (Vector2)targetPos;
            var origin2D = new Vector2(trapPos.x, trapPos.y);
            var dir = target2D - origin2D;
            if (Physics2D.Raycast(origin2D, dir.normalized, dir.magnitude, terrainMask).collider != null)
                return false;
            if (Physics2D.OverlapCircle(target2D, 0.5f, terrainMask) != null)
                return false;
            var groundHit = Physics2D.Raycast(target2D + Vector2.up * 5f, Vector2.down, 10f, terrainMask);
            if (groundHit.collider == null) return false;
            var groundedPos = new Vector3(groundHit.point.x, groundHit.point.y + 3f, targetPos.z);
            if (IsTooCloseToPickup(groundedPos)) return false;
            if (used.Any(p => Vector2.Distance(p, groundedPos) < TrapPreloader.MinDistance)) return false;

            var id = pool[rng.Next(pool.Count)];
            ArchitectSpawn(id, groundedPos);
            used.Add(groundedPos);
            newCache.Add((groundedPos, id));
            return true;
        }

        // ========== 平台生成 ==========
        private static void SpawnPlatforms(Random rng, string scene)
        {
            if (!TrapPreloader.TrapCategories.TryGetValue("平台类1", out var platformPool) || platformPool.Count == 0)
                return;

            if (_cachedPlatforms.TryGetValue(scene, out var cached))
            {
                foreach (var (pos, id) in cached)
                    ArchitectSpawn(id, pos);
                return;
            }

            var sourceTraps = new List<GameObject>(ActiveTraps);
            var usedPositions = new List<Vector3>();
            var newCache = new List<(Vector3, string)>();
            var terrainMask = LayerMask.GetMask("Terrain");

            foreach (var trap in sourceTraps)
            {
                if (trap == null) continue;
                var trapPos = trap.transform.position;
                int placedCount = 0;
                for (int i = 0; i < 2; i++)
                {
                    bool success = false;
                    for (int attempt = 0; attempt < 15; attempt++)
                    {
                        var angle = (float)(rng.NextDouble() * 360.0);
                        var dist = 6f + (float)rng.NextDouble() * 4f;
                        var rad = angle * Mathf.Deg2Rad;
                        var targetPos = trapPos + new Vector3(Mathf.Cos(rad) * dist, Mathf.Sin(rad) * dist, 0);
                        var target2D = (Vector2)targetPos;
                        if (Physics2D.OverlapBox(target2D, new Vector2(10f, 10f), 0f, terrainMask) != null)
                            continue;
                        if (usedPositions.Any(p => Vector2.Distance(p, targetPos) < TrapPreloader.MinDistance))
                            continue;
                        if (IsTooCloseToPickup(targetPos)) continue;
                        var id = platformPool[rng.Next(platformPool.Count)];
                        ArchitectSpawn(id, targetPos);
                        usedPositions.Add(targetPos);
                        newCache.Add((targetPos, id));
                        success = true;
                        placedCount++;
                        break;
                    }
                    if (!success) break;
                }
            }

            if (newCache.Count > 0)
                _cachedPlatforms[scene] = newCache;
        }

        // ========== Architect 生成 ==========
        private static void ArchitectSpawn(string id, Vector3 pos)
        {
            try
            {
                var meta = TrapPreloader.TrapMetaDict.TryGetValue(id, out var m) ? m : new TrapMeta();

                var trapPos = pos;
                if (!meta.NeedsActivator)
                {
                    if (TrapPreloader.LoweredSpikeTraps.Contains(id)) trapPos.y -= TrapPreloader.SpikeYOffset;
                    if (id == "wp_trap_spikes") trapPos.y += 5f;
                    if (TrapPreloader.LargeTraps.Contains(id)) trapPos.y -= TrapPreloader.LargeTrapYOffset;
                    if (TrapPreloader.ThornTraps.Contains(id)) trapPos.y -= 2.5f;
                }

                string uniqueEvent = null;
                Vector3 activatorPos = Vector3.zero;
                bool hasActivator = meta.NeedsActivator && !string.IsNullOrEmpty(meta.ActivatorId);

                if (hasActivator)
                {
                    if (id == "swing_trap_spike")
                    {
                        var s = _surfacePoints.OrderBy(p => Mathf.Abs(p.x - pos.x)).FirstOrDefault();
                        activatorPos = s != null ? new Vector3(pos.x, s.y, 0) : pos + new Vector3(1.5f, 0, 0);
                    }
                    else
                    {
                        activatorPos = pos + new Vector3(1.5f, 0, 0);
                    }

                    uniqueEvent = "Activate_" + Guid.NewGuid().ToString().Substring(0, 8);
                    SpawnActivatorWithEvent(meta.ActivatorId, activatorPos, uniqueEvent);
                    if (meta.PositionOffset != Vector3.zero)
                        trapPos = activatorPos + meta.PositionOffset;
                    else
                        trapPos = pos;
                }

                // 反射调用 Architect 创建物体
                var regType = Type.GetType("Architect.Objects.Placeable.PlaceableObject, Architect");
                var dict = regType?.GetField("RegisteredObjects", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as System.Collections.IDictionary;
                if (dict == null || !dict.Contains(id)) return;

                var placementType = Type.GetType("Architect.Placements.ObjectPlacement, Architect");
                var configValueType = Type.GetType("Architect.Config.Types.ConfigValue, Architect");

                // 构建配置数组
                Array configs = null;
                if (meta.Config?.Count > 0 && configValueType != null)
                {
                    var deser = Type.GetType("Architect.Config.ConfigurationManager, Architect")?.GetMethod("DeserializeConfigValue", BindingFlags.Public | BindingFlags.Static);
                    if (deser != null)
                    {
                        var list = new List<object>();
                        foreach (var kv in meta.Config)
                        {
                            var c = deser.Invoke(null, new object[] { kv.Key, kv.Value });
                            if (c != null) list.Add(c);
                        }
                        if (list.Count > 0)
                        {
                            configs = Array.CreateInstance(configValueType, list.Count);
                            for (int i = 0; i < list.Count; i++)
                                configs.SetValue(list[i], i);
                        }
                    }
                }
                if (configs == null) configs = configValueType != null ? Array.CreateInstance(configValueType, 0) : Array.Empty<object>();

                var receivers = uniqueEvent != null ? new (string, string, int)[] { (uniqueEvent, "activate_trap", 0) } : Array.Empty<(string, string, int)>();

                var placement = Activator.CreateInstance(placementType, new object[]
                {
                    dict[id], trapPos, Guid.NewGuid().ToString().Substring(0, 8),
                    false, 0f, 1f, false, 0,
                    Array.Empty<(string, string)>(), receivers, configs
                });
                var spawn = placementType.GetMethod("SpawnObject");
                var obj = spawn?.Invoke(placement, new object[] { Vector3.zero, null, 0f, 1f, false }) as GameObject;

                if (obj)
                {
                    if (TrapPreloader.ThornTraps.Contains(id)) obj.transform.rotation = Quaternion.Euler(0, 0, 90);
                    if (meta.PositionRotate != Vector3.zero)
                        obj.transform.rotation *= Quaternion.Euler(meta.PositionRotate);

                    if (TrapPreloader.LargeTraps.Contains(id))
                        obj.transform.localScale = new Vector3(0.75f, 0.75f, 0.75f);
                    if (TrapPreloader.ThornTraps.Contains(id))
                        obj.transform.localScale = new Vector3(0.5f, 1f, 0.33f);

                    if (TrapPreloader.TrapCategories.TryGetValue("平台类1", out var platformList) && platformList.Contains(id))
                    {
                        if (!TrapPreloader.SmallPlatforms.Contains(id))
                            obj.transform.localScale = new Vector3(0.33f, 0.33f, 0.33f);
                    }

                    if (id == "abyss_tendrils")
                        obj.AddComponent<OneTimeTrap>();

                    var pmType = Type.GetType("Architect.Placements.PlacementManager, Architect");
                    var objDict = pmType?.GetField("Objects", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as System.Collections.IDictionary;
                    var pid = placementType.GetProperty("ID")?.GetValue(placement) as string;
                    if (objDict != null && pid != null)
                    {
                        objDict[pid] = obj;
                        _spawnedArchitectPids.Add(pid);
                    }
                    ActiveTraps.Add(obj);

                    if (obj != null && _movementEnabled)
                    {
                        var moveRng = new Random(_masterSeed ^ id.GetHashCode() ^ pos.GetHashCode());
                        TrapMovement.ApplyTrapMovement(obj, id, moveRng);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[TrapRandomizer] ArchitectSpawn 失败: {ex}");
            }
        }

        private static void SpawnActivatorWithEvent(string activatorId, Vector3 pos, string eventName)
        {
            try
            {
                var regType = Type.GetType("Architect.Objects.Placeable.PlaceableObject, Architect");
                var dict = regType?.GetField("RegisteredObjects", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as System.Collections.IDictionary;
                if (dict == null || !dict.Contains(activatorId)) return;
                var placementType = Type.GetType("Architect.Placements.ObjectPlacement, Architect");
                var configValueType = Type.GetType("Architect.Config.Types.ConfigValue, Architect");
                var emptyConfigs = configValueType != null ? Array.CreateInstance(configValueType, 0) : Array.Empty<object>();
                var triggerName = activatorId == "trigger_zone" ? "ZoneEnter" : "OnActivate";

                var placement = Activator.CreateInstance(placementType, new object[]
                {
                    dict[activatorId], pos, Guid.NewGuid().ToString().Substring(0, 8),
                    false, 0f, 1f, false, 0,
                    new (string, string)[] { (triggerName, eventName) },
                    Array.Empty<(string, string, int)>(), emptyConfigs
                });
                var spawn = placementType.GetMethod("SpawnObject");
                var obj = spawn?.Invoke(placement, new object[] { Vector3.zero, null, 0f, 1f, false }) as GameObject;
                if (obj)
                {
                    var pmType = Type.GetType("Architect.Placements.PlacementManager, Architect");
                    var objDict = pmType?.GetField("Objects", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as System.Collections.IDictionary;
                    var pid = placementType.GetProperty("ID")?.GetValue(placement) as string;
                    if (objDict != null && pid != null) objDict[pid] = obj;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[TrapRandomizer] SpawnActivatorWithEvent 失败: {ex}");
            }
        }
        public class OneTimeTrap : MonoBehaviour
        {
            private bool _triggered;
            private const float Lifetime = 3f;

            private void Start() => Destroy(gameObject, Lifetime);

            private void OnTriggerEnter2D(Collider2D other)
            {
                if (_triggered) return;
                if (!other.CompareTag("Player")) return;
                DestroySelf();
            }

            private void OnCollisionEnter2D(Collision2D collision)
            {
                if (_triggered) return;
                if (!collision.gameObject.CompareTag("Player")) return;
                DestroySelf();
            }

            private void DestroySelf()
            {
                if (_triggered) return;
                _triggered = true;
                StartCoroutine(DestroyNextFrame());
            }

            private System.Collections.IEnumerator DestroyNextFrame()
            {
                yield return null;
                Destroy(gameObject);
            }
        }
    }
}

// TrapPreloader.cs

namespace SilksongItemRandomizer
{

public static class TrapPreloader
{
    public enum TrapDifficulty { Beginner, Focused, Overflow }

    // 完整陷阱池
    public static readonly List<string> TrapPool = new()
    {
        "fan_hazard", "spike_cog_1", "spike_cog_2", "spike_cog_3", "spike_cog_4", "spike_cog_5",
        "hot_coal", "lava_area", "falling_lava", "bone_boulder",
        "hunter_landmine", "pilgrim_trap_spike", "wisp_flame_lantern",
        "falling_bell", "shellwood_thorns",
        "coral_lightning_rock", "coral_lightning_orb", "voltgrass",
        "coral_crust_s", "coral_crust_m", "coral_crust_l",
        "coral_spike", "coral_spike_fall", "stomp_spire",
        "rubble_field", "steam_vent", "junk_pipe",
        "slab_trap", "slab_spike_ball", "slab_prob_blade", "hunter_sickle_trap",
        "bilewater_trap", "falling_spike_ball", "swing_trap_small", "swing_trap_spike",
        "dust_trap_spike_plate", "dust_trap_spike_dropper", "mite_trap",
        "organ_spikes", "cradle_spikes",
        "brown_vines", "abyss_tendrils", "void_wave",
        "mill_trap", "craw_chain",
        "frost_marker", "white_thorns", "jelly_egg", "wp_trap_spikes"
    };

    // 场景黑名单
    public static readonly HashSet<string> ExcludedScenes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Belltown_04",
        "Bellshrine", "Bellshrine_02", "Bellshrine_03", "Bellshrine_05",
        "Bone_East_Umbrella",
        "Room_Pinstress",
    };

    public static readonly HashSet<string> NoLavaScenes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Hang_01", "Hang_02", "Hang_10"
    };

    public static readonly HashSet<string> LargeRooms = new(StringComparer.OrdinalIgnoreCase)
    {
        "Song_20", "Arborium_01", "Cog_04", "Song_11", "Song_05", "Song_01", "Coral_35b"
    };

    // 特殊陷阱集
    public static readonly HashSet<string> LargeTraps = new(StringComparer.OrdinalIgnoreCase)
    {
        "fan_hazard", "steam_vent", "mill_trap",
        "spike_cog_2", "spike_cog_3", "spike_cog_1", "spike_cog_4", "spike_cog_5", "voltgrass",
        "junk_pipe"
    };

    public static List<string> TrapPoolNoLava => TrapPool.Where(t => t != LavaTrapId).ToList();

    public static readonly HashSet<string> ThornTraps = new(StringComparer.OrdinalIgnoreCase)
    {
        "brown_vines", "shellwood_thorns", "white_thorns"
    };

    // 小平台（不缩放）
    public static readonly HashSet<string> SmallPlatforms = new(StringComparer.OrdinalIgnoreCase)
    {
        "small_grey_coral_plat",
        "small_red_coral_plat",
        "shell_small"
    };

    public static readonly HashSet<string> LoweredSpikeTraps = new(StringComparer.OrdinalIgnoreCase)
    {
        "pilgrim_trap_spike", "organ_spikes", "cradle_spikes"
    };

    public static readonly HashSet<string> HammerTraps = new(StringComparer.OrdinalIgnoreCase)
    {
        "slab_spike_ball"
    };

    // 常量
    public const string LavaTrapId = "lava_area";
    public const string FallingLavaId = "falling_lava";
    public const float MinDistance = 8f;
    public const float PickupSafeRadius = 5f;
    public const float DoorSafeRadius = 7f;
    public const int LargeTrapRadius = 5;
    public const float LargeTrapYOffset = 1.5f;
    public const int ThornTrapMinWidth = 4;
    public const float SpikeYOffset = 1.8f;
    public const int HammerTrapMinHeight = 7;
    public const float WallPointMatchDistance = 1f;
    public const float DarkThunderChainDistance = 4f;
    public const int MaxDarkThunderCount = 4;

    // 陷阱元数据
    public static readonly Dictionary<string, TrapMeta> TrapMetaDict = new()
    {
        // 无特殊配置的陷阱
        ["fan_hazard"] = new(),
        ["spike_cog_1"] = new(),
        ["spike_cog_2"] = new(),
        ["spike_cog_3"] = new(),
        ["spike_cog_4"] = new(),
        ["spike_cog_5"] = new(),
        ["hot_coal"] = new(),
        ["lava_area"] = new(),
        ["falling_lava"] = new(),
        ["voltgrass"] = new(),
        ["steam_vent"] = new(),
        ["slab_trap"] = new(),
        ["slab_prob_blade"] = new(),
        ["slab_spike_ball"] = new(),
        ["dust_trap_spike_plate"] = new(),
        ["dust_trap_spike_dropper"] = new(),
        ["mite_trap"] = new(),
        ["organ_spikes"] = new(),
        ["cradle_spikes"] = new(),
        ["mill_trap"] = new(),
        ["coral_lightning_rock"] = new(),
        ["coral_crust_s"] = new(),
        ["coral_crust_m"] = new(),
        ["coral_crust_l"] = new(),
        ["abyss_tendrils"] = new(),
        ["bone_boulder"] = new(),
        ["void_wave"] = new(),
        ["coral_lightning_orb"] = new(),

        // 有配置的陷阱
        ["wisp_flame_lantern"] = new(new() { ["breakable_on"] = "True" }),
        ["falling_bell"] = new(new() { ["bell_reset"] = "1" }),
        ["shellwood_thorns"] = new(new() { ["vines_hurt_player"] = "True" }),
        ["brown_vines"] = new(new() { ["vines_hurt_player"] = "True" }),
        ["white_thorns"] = new(new() { ["vines_hurt_player"] = "True" }),
        ["junk_pipe"] = new(new() { ["junk_pipe_terrain"] = "True" }),
        ["frost_marker"] = new(new() { ["frost_speed"] = "10" }),
        ["jelly_egg"] = new(new() { ["egg_regen"] = "-1" }),
        ["wp_trap_spikes"] = new(
            new() { ["wp_spikes_up"] = "True", ["wp_spikes_delay"] = "0", ["wp_spikes_speed"] = "1" },
            positionOffset: new Vector3(0f, 5f, 0f)
        ),

        // 需要触发器的陷阱
        ["pilgrim_trap_spike"] = new(needsActivator: true, activatorId: "pilgrim_trap_wire"),
        ["rubble_field"] = new(needsActivator: true, activatorId: "slab_pressure_plate", positionOffset: new Vector3(0f, 0.5f, 0f)),
        ["bilewater_trap"] = new(needsActivator: true, activatorId: "slab_pressure_plate", positionOffset: new Vector3(10f, 0f, 0f)),
        ["falling_spike_ball"] = new(needsActivator: true, activatorId: "slab_pressure_plate"),
        ["swing_trap_small"] = new(needsActivator: true, activatorId: "slab_pressure_plate", positionOffset: new Vector3(0f, 8f, 0f)),
        ["swing_trap_spike"] = new(needsActivator: true, activatorId: "slab_pressure_plate", positionOffset: new Vector3(0f, 15f, 0f)),
        ["hunter_landmine"] = new(needsActivator: true, activatorId: "hunter_trap_plate", positionOffset: new Vector3(0f, -1f, 0f), positionRotate: new Vector3(0f, 0f, 180f)),
        ["hunter_sickle_trap"] = new(needsActivator: true, activatorId: "hunter_trap_plate", positionOffset: new Vector3(0f, 6f, 0f)),
        ["craw_chain"] = new(needsActivator: true, activatorId: "trigger_zone"),
        ["coral_spike"] = new(needsActivator: true, activatorId: "trigger_zone"),
        ["coral_spike_fall"] = new(needsActivator: true, activatorId: "trigger_zone"),
        ["stomp_spire"] = new(needsActivator: true, activatorId: "trigger_zone"),
    };

    // 功能分类（墙壁已禁用）
    public static readonly Dictionary<string, List<string>> TrapCategories = new()
    {
        ["暗雷"] = new() { "hunter_landmine", "dust_trap_spike_plate", "slab_trap", "slab_prob_blade", "hunter_sickle_trap" },
        ["跳跳乐"] = new() { "spike_cog_1", "spike_cog_2", "spike_cog_3", "spike_cog_4", "spike_cog_5" },
        ["平台类"] = new() { "spike_cog_1", "spike_cog_2", "spike_cog_3", "spike_cog_4", "spike_cog_5", "fan_hazard", "mill_trap" },
        ["尖刺类"] = new() { "wp_trap_spikes", "organ_spikes", "coral_spike", "pilgrim_trap_spike", "cradle_spikes", "slab_spike_ball" },
        ["墙壁"] = new(),
        ["天花板"] = new() { "falling_bell", "bone_boulder", "dust_trap_spike_dropper", "falling_lava", "coral_lightning_orb", "coral_lightning_rock", "steam_vent", "junk_pipe" },
        ["障碍物"] = new()
        {
            "coral_crust_s", "coral_crust_m", "coral_crust_l",
            "hot_coal",
            "march_pogo", "bounce_bloom", "wisp_bounce_pod",
            "sprintmaster_pod", "swap_bounce_pod", "celeste_bumper"
        },
        ["装饰物"] = new()
        {
            "clover_pod", "abyss_pod",
            "lilypad", "cradle_nut",
            "karaka_statue", "judge_statue", "clover_statue",
            "shard_statue_1", "shard_statue_2", "shard_statue_3",
            "shard_statue_4", "shard_statue_5", "flick_statue",
            "fayforn_npc", "snow_chunk", "float_crystal",
            "white_palace_fly", "pond_skipper_body", "winged_lifeseed",
            "life_pustule", "bounce_flea", "dodge_flea",
            "hornet_cocoon", "bellbeast_child",
            "bell_s", "bell_l", "bell_lock",
            "greymoor_balloon_small", "greymoor_balloon_mid", "greymoor_balloon_large",
            "swamp_mosquito", "swamp_mosquito_skinny", "mothleaf",
            "imoba", "garpid",
            "crystal_drifter", "crystal_drifter_giant",
            "stilkin", "stilkin_trapper", "dock_bomber"
        },
        ["触发型"] = new() { "swing_trap_small", "coral_spike_fall", "stomp_spire", "falling_spike_ball", "rubble_field", "mite_trap", "bilewater_trap", "craw_chain", "swing_trap_spike" },
        ["追逐型"] = new() { "wisp_flame_lantern" },
        ["场景伤害"] = new() { "abyss_tendrils", "voltgrass", "void_wave", "frost_marker" },

        ["跳跳乐1"] = new()
        {
            "march_pogo", "bounce_bloom", "wisp_bounce_pod", "sprintmaster_pod",
            "swap_bounce_pod", "clover_pod", "abyss_pod", "celeste_bumper",
            "lilypad", "cradle_nut",
            "karaka_statue", "judge_statue", "clover_statue",
            "shard_statue_1", "shard_statue_2", "shard_statue_3",
            "shard_statue_4", "shard_statue_5", "flick_statue",
            "fayforn_npc", "snow_chunk", "float_crystal",
            "white_palace_fly", "pond_skipper_body", "winged_lifeseed",
            "life_pustule", "bounce_flea", "dodge_flea",
            "hornet_cocoon", "bellbeast_child",
            "bell_s", "bell_l", "bell_lock",
            "greymoor_balloon_small", "greymoor_balloon_mid", "greymoor_balloon_large",
            "swamp_mosquito", "swamp_mosquito_skinny", "mothleaf",
            "imoba", "garpid",
            "crystal_drifter", "crystal_drifter_giant","stilkin", "stilkin_trapper", "dock_bomber"
        },

        ["平台类1"] = new()
        {
            "coral_plat_float", "small_grey_coral_plat", "small_red_coral_plat",
            "mid_red_coral_plat", "large_red_coral_plat",
            "abyss_plat_mid", "abyss_plat_wide",
            "deepnest_platform_01", "deepnest_platform_02",
            "deepnest_platform_03", "deepnest_platform_04", "deepnest_platform_05",
            "hive_platform_01", "hive_platform_02", "hive_platform_03",
            "shell_small", "shell_mid", "shell_large",
        },
    };

    // 难度配额
    private static readonly Dictionary<TrapDifficulty, Dictionary<string, int>> Quotas = new()
    {
        [TrapDifficulty.Beginner] = new()
        {
            ["暗雷"] = 2,
            ["跳跳乐"] = 1,
            ["平台类"] = 2,
            ["尖刺类"] = 2,
            ["墙壁"] = 0,
            ["天花板"] = 5,
            ["障碍物"] = 4,
            ["装饰物"] = 3,
            ["触发型"] = 5,
            ["追逐型"] = 0,
            ["场景伤害"] = 0
        },
        [TrapDifficulty.Focused] = new()
        {
            ["暗雷"] = 4,
            ["跳跳乐"] = 3,
            ["平台类"] = 4,
            ["尖刺类"] = 4,
            ["墙壁"] = 0,
            ["天花板"] = 6,
            ["障碍物"] = 5,
            ["装饰物"] = 4,
            ["触发型"] = 6,
            ["追逐型"] = 1,
            ["场景伤害"] = 1
        },
        [TrapDifficulty.Overflow] = new()
        {
            ["暗雷"] = 6,
            ["跳跳乐"] = 5,
            ["平台类"] = 6,
            ["尖刺类"] = 6,
            ["墙壁"] = 0,
            ["天花板"] = 8,
            ["障碍物"] = 6,
            ["装饰物"] = 5,
            ["触发型"] = 8,
            ["追逐型"] = 2,
            ["场景伤害"] = 2
        }
    };

    public static Dictionary<string, int> GetCategoryQuotas(TrapDifficulty difficulty)
    {
        return Quotas.TryGetValue(difficulty, out var quota) ? new Dictionary<string, int>(quota) : new Dictionary<string, int>(Quotas[TrapDifficulty.Beginner]);
    }

    // 分类顺序
    public static readonly string[] CategoryOrder =
    {
        "暗雷", "跳跳乐", "平台类", "尖刺类", "墙壁", "天花板", "障碍物", "装饰物", "触发型", "追逐型", "场景伤害"
    };

    public static double GetFrostProbability(TrapDifficulty difficulty) =>
        difficulty switch
        {
            TrapDifficulty.Focused => 0.10,
            TrapDifficulty.Overflow => 0.20,
            _ => 0.0
        };
}

public class TrapMeta
{
    public Dictionary<string, string> Config { get; set; }
    public bool NeedsActivator { get; set; }
    public string ActivatorId { get; set; }
    public Vector3 PositionOffset { get; set; }
    public Vector3 PositionRotate { get; set; }

    public TrapMeta(
        Dictionary<string, string> config = null,
        bool needsActivator = false,
        string activatorId = null,
        Vector3? positionOffset = null,
        Vector3? positionRotate = null)
    {
        Config = config ?? new Dictionary<string, string>();
        NeedsActivator = needsActivator;
        ActivatorId = activatorId;
        PositionOffset = positionOffset ?? Vector3.zero;
        PositionRotate = positionRotate ?? Vector3.zero;
    }
}
}

namespace SilksongItemRandomizer
{

public enum TrapPersonality { Friendly, Playful, Sinister }

public enum MoveStyle
{
    SurfacePatrol,
    ZigzagLoop,
    NeedleSwing,
    Spin,
    Pulse,
    Flicker,
    Jitter,
    Bounce
}

public class TrapMover : MonoBehaviour
{
    public MoveStyle style;
    public TrapPersonality personality;
    public float baseSpeed = 1f;

    public float SwingMinAngle { get; set; } = -30f;
    public float SwingMaxAngle { get; set; } = 30f;

    private float _currentSpeed;
    private int _cycleCount;
    private int _speedTick;
    private Vector2 _startPos;
    private Vector2 _leftEdge, _rightEdge;
    private int _moveDir = 1;
    private List<Vector2> _path;
    private int _pathIndex;
    private float _pathProgress;
    private float _swingTimer;
    private float _startAngle;
    private float _spinAngle;
    private int _spinDir = 1;
    private Vector3 _originalScale;
    private float _pulseTimer;
    private SpriteRenderer _spriteRenderer;
    private float _flickerTimer;
    private bool _isVisible = true;
    private Vector3 _originalPos;
    private float _jitterTimer;
    private float _bounceTimer;

    private void Start()
    {
        _startPos = transform.position;
        _startAngle = transform.eulerAngles.z;
        _originalScale = transform.localScale;
        _originalPos = transform.position;
        _spriteRenderer = GetComponent<SpriteRenderer>();

        switch (style)
        {
            case MoveStyle.SurfacePatrol:
                (_leftEdge, _rightEdge) = ScanPlatformEdges(_startPos);
                break;
            case MoveStyle.ZigzagLoop:
                if (UnityEngine.Random.value < 0.5f)
                    _path = GeneratePresetPath(_startPos);
                else
                    _path = GenerateZigzagLoop(_startPos, 5, 10, 8f, 6f);
                break;
            case MoveStyle.Spin:
                _spinDir = UnityEngine.Random.Range(0, 2) == 0 ? 1 : -1;
                break;
            case MoveStyle.Pulse:
                _pulseTimer = UnityEngine.Random.Range(0f, 2f);
                break;
            case MoveStyle.Flicker:
                _flickerTimer = UnityEngine.Random.Range(0f, 3f);
                break;
            case MoveStyle.Jitter:
                _jitterTimer = UnityEngine.Random.Range(0f, 1f);
                break;
            case MoveStyle.Bounce:
                _bounceTimer = UnityEngine.Random.Range(0f, Mathf.PI * 2);
                break;
        }
        UpdateSpeed();
    }

    private void Update()
    {
        UpdateSpeed();
        switch (style)
        {
            case MoveStyle.SurfacePatrol: DoSurfacePatrol(); break;
            case MoveStyle.ZigzagLoop: DoZigzagLoop(); break;
            case MoveStyle.NeedleSwing: DoNeedleSwing(); break;
            case MoveStyle.Spin: DoSpin(); break;
            case MoveStyle.Pulse: DoPulse(); break;
            case MoveStyle.Flicker: DoFlicker(); break;
            case MoveStyle.Jitter: DoJitter(); break;
            case MoveStyle.Bounce: DoBounce(); break;
        }
    }

    private void UpdateSpeed()
    {
        _speedTick++;
        if ((_speedTick & 31) != 0 && _currentSpeed > 0f) return; // 每 32 帧刷新一次随机速度，避免每帧 Random 分配（_currentSpeed=0 时为首帧初始化，必须执行）
        _currentSpeed = personality switch
        {
            TrapPersonality.Friendly => baseSpeed * (0.15f + UnityEngine.Random.value * 0.1f),
            TrapPersonality.Playful => baseSpeed * (0.6f + UnityEngine.Random.value * 0.4f),
            TrapPersonality.Sinister => UpdateSinisterSpeed(),
            _ => _currentSpeed
        };
    }

    private float UpdateSinisterSpeed()
    {
        _cycleCount++;
        if (_cycleCount >= 180)
        {
            _cycleCount = 0;
            return baseSpeed * (UnityEngine.Random.value > 0.5f ? 0.75f : 0.15f);
        }
        return _currentSpeed;
    }

    private void DoSurfacePatrol()
    {
        var target = _moveDir > 0 ? _rightEdge : _leftEdge;
        transform.position = Vector2.MoveTowards(transform.position, target, _currentSpeed * Time.deltaTime);
        if (Vector2.Distance(transform.position, target) < 0.1f) _moveDir *= -1;
    }

    private (Vector2 left, Vector2 right) ScanPlatformEdges(Vector2 origin)
    {
        var mask = LayerMask.GetMask("Terrain");
        var y = origin.y + 0.5f;
        var left = origin.x;
        var right = origin.x;

        for (var i = 0; i < 40; i++)
        {
            if (Physics2D.Raycast(new Vector2(left - 0.5f, y), Vector2.down, 1f, mask))
                left -= 0.5f;
            else break;
        }
        for (var i = 0; i < 40; i++)
        {
            if (Physics2D.Raycast(new Vector2(right + 0.5f, y), Vector2.down, 1f, mask))
                right += 0.5f;
            else break;
        }
        return (new Vector2(left, origin.y), new Vector2(right, origin.y));
    }

    private void DoZigzagLoop()
    {
        if (_path == null || _path.Count < 2) return;
        _pathProgress += _currentSpeed * Time.deltaTime;
        while (_pathProgress > 1f && _pathIndex < _path.Count - 1)
        {
            _pathProgress -= 1f;
            _pathIndex++;
        }
        if (_pathIndex >= _path.Count - 1)
        {
            _pathIndex = 0;
            _pathProgress = 0f;
        }
        transform.position = Vector2.Lerp(_path[_pathIndex], _path[_pathIndex + 1], _pathProgress);
    }

    private void DoNeedleSwing()
    {
        _swingTimer += Time.deltaTime * _currentSpeed;
        var angle = Mathf.Lerp(SwingMinAngle, SwingMaxAngle, (Mathf.Sin(_swingTimer) + 1f) / 2f);
        transform.rotation = Quaternion.Euler(0, 0, _startAngle + angle);
    }

    private void DoSpin()
    {
        _spinAngle += 90f * _currentSpeed * Time.deltaTime * _spinDir;
        transform.rotation = Quaternion.Euler(0, 0, _spinAngle);
    }

    private void DoPulse()
    {
        _pulseTimer += Time.deltaTime * _currentSpeed * 2f;
        var scale = 1f + Mathf.Sin(_pulseTimer) * 0.2f;
        transform.localScale = _originalScale * scale;
    }

    private void DoFlicker()
    {
        _flickerTimer += Time.deltaTime * _currentSpeed;
        var period = 0.5f;
        var shouldBeVisible = (Mathf.FloorToInt(_flickerTimer / period) % 2) == 0;
        if (shouldBeVisible == _isVisible) return;
        _isVisible = shouldBeVisible;
        if (_spriteRenderer != null)
            _spriteRenderer.enabled = _isVisible;
        else
            gameObject.SetActive(_isVisible);
    }

    private void DoJitter()
    {
        _jitterTimer += Time.deltaTime * 10f;
        var offsetX = (Mathf.PerlinNoise(_jitterTimer, 0) - 0.5f) * 0.2f;
        var offsetY = (Mathf.PerlinNoise(0, _jitterTimer) - 0.5f) * 0.2f;
        transform.position = _originalPos + new Vector3(offsetX, offsetY, 0);
    }

    private void DoBounce()
    {
        _bounceTimer += Time.deltaTime * _currentSpeed * 2f;
        var offsetY = Mathf.Sin(_bounceTimer) * 0.3f;
        transform.position = new Vector3(_originalPos.x, _originalPos.y + offsetY, _originalPos.z);
    }

    // ----- 路径生成（静态方法，无状态）-----
    private static List<Vector2> GeneratePresetPath(Vector2 origin)
    {
        const float radius = 4f;
        var shapes = new List<System.Func<List<Vector2>>>
        {
            () => Square(origin, radius),
            () => Hexagon(origin, radius),
            () => Hexagram(origin, radius),
            () => Triangle(origin, radius),
            () => Pentagram(origin, radius),
            () => Circle(origin, radius, 14),
            () => Figure8(origin, radius, 12),
            () => Diamond(origin, radius),
            () => Cross(origin, radius),
        };
        return shapes[UnityEngine.Random.Range(0, shapes.Count)]();
    }

    private static List<Vector2> Square(Vector2 o, float r) =>
        new() { o + new Vector2(-r, r), o + new Vector2(r, r), o + new Vector2(r, -r), o + new Vector2(-r, -r), o + new Vector2(-r, r), o };

    private static List<Vector2> Hexagon(Vector2 o, float r)
    {
        var pts = new List<Vector2>();
        for (var i = 0; i <= 6; i++)
        {
            var angle = Mathf.Deg2Rad * (60f * i - 30f);
            pts.Add(o + new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r));
        }
        pts.Add(o);
        return pts;
    }

    private static List<Vector2> Hexagram(Vector2 o, float r)
    {
        var pts = new List<Vector2>();
        for (var i = 0; i < 6; i++)
        {
            var angle = Mathf.Deg2Rad * (60f * i - 90f);
            pts.Add(o + new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r));
        }
        pts.Add(o);
        return pts;
    }

    private static List<Vector2> Triangle(Vector2 o, float r)
    {
        var pts = new List<Vector2>();
        for (var i = 0; i < 3; i++)
        {
            var angle = Mathf.Deg2Rad * (120f * i - 90f);
            pts.Add(o + new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r));
        }
        pts.Add(o);
        return pts;
    }

    private static List<Vector2> Pentagram(Vector2 o, float r)
    {
        var pts = new List<Vector2>();
        var r2 = r * 0.382f;
        for (var i = 0; i < 5; i++)
        {
            var angle = Mathf.Deg2Rad * (72f * i - 90f);
            pts.Add(o + new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r));
            pts.Add(o + new Vector2(Mathf.Cos(angle + Mathf.Deg2Rad * 36f) * r2, Mathf.Sin(angle + Mathf.Deg2Rad * 36f) * r2));
        }
        pts.Add(o);
        return pts;
    }

    private static List<Vector2> Circle(Vector2 o, float r, int segments)
    {
        var pts = new List<Vector2>();
        for (var i = 0; i <= segments; i++)
        {
            var angle = Mathf.Deg2Rad * (360f * i / segments);
            pts.Add(o + new Vector2(Mathf.Cos(angle) * r, Mathf.Sin(angle) * r));
        }
        pts.Add(o);
        return pts;
    }

    private static List<Vector2> Figure8(Vector2 o, float r, int segments)
    {
        var pts = new List<Vector2>();
        for (var i = 0; i <= segments / 2; i++)
        {
            var angle = Mathf.Deg2Rad * (360f * i / segments);
            pts.Add(o + new Vector2(r + Mathf.Cos(angle) * r, Mathf.Sin(angle) * r * 1.5f));
        }
        for (var i = segments / 2; i <= segments; i++)
        {
            var angle = Mathf.Deg2Rad * (360f * i / segments);
            pts.Add(o + new Vector2(-r + Mathf.Cos(angle) * r, Mathf.Sin(angle) * r * 1.5f));
        }
        pts.Add(o);
        return pts;
    }

    private static List<Vector2> Diamond(Vector2 o, float r) =>
        new() { o + new Vector2(0, r), o + new Vector2(r, 0), o + new Vector2(0, -r), o + new Vector2(-r, 0), o + new Vector2(0, r), o };

    private static List<Vector2> Cross(Vector2 o, float r)
    {
        var half = r * 0.4f;
        return new List<Vector2>
        {
            o + new Vector2(0, r),
            o + new Vector2(half, r * 0.6f),
            o + new Vector2(half, half),
            o + new Vector2(r * 0.6f, half),
            o + new Vector2(r, 0),
            o + new Vector2(r * 0.6f, -half),
            o + new Vector2(half, -half),
            o + new Vector2(half, -r * 0.6f),
            o + new Vector2(0, -r),
            o + new Vector2(-half, -r * 0.6f),
            o + new Vector2(-half, -half),
            o + new Vector2(-r * 0.6f, -half),
            o + new Vector2(-r, 0),
            o + new Vector2(-r * 0.6f, half),
            o + new Vector2(-half, half),
            o + new Vector2(-half, r * 0.6f),
            o
        };
    }

    private static List<Vector2> GenerateZigzagLoop(Vector2 start, int minSeg, int maxSeg, float maxW, float maxH)
    {
        var rng = new System.Random();
        var n = rng.Next(minSeg, maxSeg);
        var list = new List<Vector2> { start };
        var x = start.x;
        var y = start.y;
        for (var i = 0; i < n - 1; i++)
        {
            var dx = (rng.Next(2) == 0 ? 1 : -1) * (5f + (float)rng.NextDouble() * 7f);
            var dy = (rng.Next(2) == 0 ? 1 : -1) * (3f + (float)rng.NextDouble() * 5f);
            x += dx;
            y += dy;
            list.Add(new Vector2(x, y));
        }
        list.Add(start);
        return list;
    }
}

public static class TrapMovement
{
    private const float GlobalMoveChance = 0.4f;

    private static readonly List<MoveStyle> AllStyles = new()
    {
        MoveStyle.SurfacePatrol,
        MoveStyle.ZigzagLoop,
        MoveStyle.NeedleSwing,
        MoveStyle.Spin,
        MoveStyle.Pulse,
        MoveStyle.Flicker,
        MoveStyle.Jitter,
        MoveStyle.Bounce
    };

    private static readonly HashSet<string> ExcludedTraps = new() { "lava_area" };

    public static void ApplyTrapMovement(GameObject trapObj, string trapId, Random rng)
    {
        if (ExcludedTraps.Contains(trapId)) return;
        if (rng.NextDouble() > GlobalMoveChance) return;

        var style = AllStyles[rng.Next(AllStyles.Count)];
        var personality = (TrapPersonality)rng.Next(0, 3);

        var mover = trapObj.AddComponent<TrapMover>();
        mover.style = style;
        mover.personality = personality;
        mover.baseSpeed = 2f;

        if (style == MoveStyle.NeedleSwing)
        {
            mover.SwingMinAngle = -30f + (float)rng.NextDouble() * 10f - 5f;
            mover.SwingMaxAngle = 30f + (float)rng.NextDouble() * 10f - 5f;
        }
    }
}
}
