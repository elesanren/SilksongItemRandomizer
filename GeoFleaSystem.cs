using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using HutongGames.PlayMaker;
using TeamCherry.SharedUtils;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

namespace SilksongItemRandomizer
{
    // ======================================================================
    // 念珠钱堆（GeoRock）+ 跳蚤救援（Flea Rescue）统一系统
    // ======================================================================

    /// <summary>27 个原生跳蚤场景映射：flea:01~27 → SavedFlea_{SceneName}。</summary>
    public static class FleaSceneMap
    {
        public const int Count = 27;

        public static readonly string[] Scenes = new string[]
        {
            "Bone_06",           // flea:01
            "Dock_16",           // flea:02
            "Bone_East_05",      // flea:03
            "Bone_East_17b",     // flea:04
            "Ant_03",            // flea:05
            "Greymoor_15b",      // flea:06
            "Greymoor_06",       // flea:07
            "Shellwood_03",      // flea:08
            "Bone_East_10_Church",// flea:09
            "Coral_35",          // flea:10
            "Dust_12",           // flea:11
            "Dust_09",           // flea:12
            "Belltown_04",       // flea:13
            "Crawl_06",          // flea:14
            "Slab_Cell",         // flea:15
            "Shadow_28",         // flea:16
            "Dock_03d",          // flea:17
            "Under_23",          // flea:18
            "Shadow_10",         // flea:19
            "Song_14",           // flea:20
            "Coral_24",          // flea:21
            "Peak_05c",          // flea:22
            "Library_09",        // flea:23
            "Song_11",           // flea:24
            "Library_01",        // flea:25
            "Under_21",          // flea:26
            "Slab_06",           // flea:27
        };

        private static readonly Dictionary<string, int> _sceneToIndex = new Dictionary<string, int>();
        private static readonly Dictionary<int, string> _indexToField = new Dictionary<int, string>();

        static FleaSceneMap()
        {
            for (int i = 0; i < Scenes.Length; i++)
            {
                _sceneToIndex[Scenes[i]] = i + 1;
                _indexToField[i + 1] = "SavedFlea_" + Scenes[i];
            }
        }

        public static int SceneToIndex(string sceneName)
        {
            return _sceneToIndex.TryGetValue(sceneName, out int idx) ? idx : -1;
        }

        public static string IndexToFieldName(int index)
        {
            return _indexToField.TryGetValue(index, out string field) ? field : null;
        }

        public static string IndexToScene(int index)
        {
            return (index >= 1 && index <= Count) ? Scenes[index - 1] : null;
        }

        public static void RecordFleaRescueByScene(string sceneName)
        {
            var pd = PlayerData.instance;
            if (pd == null) return;

            int idx = SceneToIndex(sceneName);
            if (idx < 0) return;

            string fieldName = IndexToFieldName(idx);
            var field = pd.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
            if (field == null || field.FieldType != typeof(bool)) return;

            if (!(bool)field.GetValue(pd))
            {
                field.SetValue(pd, true);
                Plugin.Log.LogInfo($"[FleaSceneMap] 已记录救援: {fieldName} = true, SavedFleasCount = {pd.SavedFleasCount}");
            }
        }

        public static void RecordFleaRescueNextAvailable()
        {
            var pd = PlayerData.instance;
            if (pd == null) return;

            for (int i = 0; i < Count; i++)
            {
                string fieldName = "SavedFlea_" + Scenes[i];
                var field = pd.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public);
                if (field == null || field.FieldType != typeof(bool)) continue;

                if (!(bool)field.GetValue(pd))
                {
                    field.SetValue(pd, true);
                    Plugin.Log.LogInfo($"[FleaSceneMap] 已记录救援(随机): {fieldName} = true, SavedFleasCount = {pd.SavedFleasCount}");
                    return;
                }
            }

            Plugin.Log.LogWarning("[FleaSceneMap] 所有 27 个 SavedFlea_* 字段已为 true，无法记录更多救援");
        }
    }

    /// <summary>念珠钱堆生成器：自建+克隆双模式，任意场景生成。</summary>
    public static class GeoRockBuilder
    {
        private static GameObject _template;
        private static tk2dSpriteCollectionData _cachedCollection;
        private static bool _autoPrimed;

        private static readonly string Tk2dSourceScene = "Aspid_01";
        private static readonly string GeoRockSpriteName = "rosary_rock_type_020000";

        public static bool HasTemplate => _template != null;

        public static void Init()
        {
            Plugin.Log.LogInfo("[GeoRockBuilder] 初始化完成");
        }

        public static void OnSceneLoaded(Scene scene)
        {
            if (_template == null)
            {
                var rocks = UnityEngine.Object.FindObjectsOfType<GeoRock>();
                if (rocks.Length > 0)
                    BuildTemplateFromScene(rocks[0].gameObject, scene.name);
            }

            if (!_autoPrimed && _cachedCollection == null)
            {
                _autoPrimed = true;
                Plugin.Instance.StartCoroutine(WaitAndPrimeAsync());
            }
        }

        private static IEnumerator WaitAndPrimeAsync()
        {
            int waitFrames = 0;
            while (HeroController.instance == null)
            {
                yield return null;
                waitFrames++;
                if (waitFrames > 300)
                {
                    Plugin.Log.LogWarning("[GeoRockBuilder] 等待 HeroController 超时(300帧)，强制 Prime");
                    break;
                }
            }
            yield return new WaitForSeconds(0.5f);
            yield return PrimeCollectionAsync();
        }

        private static IEnumerator PrimeCollectionAsync()
        {
            if (_cachedCollection != null) yield break;

            Plugin.BlackoutShow();

            var op = Addressables.LoadSceneAsync("Scenes/" + Tk2dSourceScene, LoadSceneMode.Additive, true, 100);
            yield return op;

            if (op.Status != AsyncOperationStatus.Succeeded)
            {
                Plugin.Log.LogWarning($"[GeoRockBuilder] PrimeCollection 异步加载失败: {op.OperationException?.Message}");
                yield break;
            }

            var scene = op.Result.Scene;

            tk2dSpriteCollectionData col = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var tk2d in root.GetComponentsInChildren<tk2dSprite>(true))
                {
                    var c = tk2d.Collection;
                    if (c == null || c.spriteDefinitions == null) continue;
                    for (int i = 0; i < c.spriteDefinitions.Length; i++)
                    {
                        if (c.spriteDefinitions[i]?.name == GeoRockSpriteName)
                        {
                            col = c;
                            break;
                        }
                    }
                    if (col != null) break;
                }
                if (col != null) break;
            }

            UnloadAdditiveScene(op, scene);

            if (col != null)
                _cachedCollection = col;

            yield return new WaitForSeconds(0.2f);
            Plugin.BlackoutHide();
        }

        private static void UnloadAdditiveScene(AsyncOperationHandle<SceneInstance> handle, Scene scene)
        {
            try
            {
                Addressables.UnloadSceneAsync(handle);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[GeoRockBuilder] Addressables 卸载失败: {ex.Message}，尝试 SceneManager");
                try
                {
                    if (scene.IsValid() && scene.isLoaded)
                        SceneManager.UnloadSceneAsync(scene);
                }
                catch { }
            }
        }

        private static void BuildTemplateFromScene(GameObject src, string sceneName)
        {
            var srcTk2d = src.GetComponentInChildren<tk2dSprite>(true);
            if (srcTk2d != null && srcTk2d.Collection != null)
            {
                _cachedCollection = srcTk2d.Collection;
            }

            _template = UnityEngine.Object.Instantiate(src);
            _template.name = "Geo Rock Template";
            UnityEngine.Object.DontDestroyOnLoad(_template);
            foreach (var fsm in _template.GetComponentsInChildren<PlayMakerFSM>(true))
                fsm.enabled = false;
            foreach (var r in _template.GetComponentsInChildren<Renderer>(true))
                r.enabled = false;
            foreach (var c in _template.GetComponentsInChildren<Collider2D>(true))
                c.enabled = false;
            _template.SetActive(true);
            var gr = _template.GetComponent<GeoRock>();
            if (gr != null && gr.geoRockData != null)
            {
                gr.geoRockData.id = "__template__";
                gr.geoRockData.sceneName = "";
                gr.geoRockData.hitsLeft = 0;
            }
            Plugin.Log.LogInfo($"[GeoRockBuilder] 克隆模板建立 (来源: {sceneName})");
        }

        public static GameObject SpawnAtHero()
        {
            var hero = HeroController.instance;
            if (hero == null)
            {
                Plugin.Log.LogWarning("[GeoRockBuilder] HeroController.instance == null");
                return null;
            }
            return SpawnAtPosition(new Vector3(hero.transform.position.x, hero.transform.position.y - 1f, 0f));
        }

        public static GameObject SpawnAtPosition(Vector3 position)
        {
            if (_template != null)
                return SpawnFromTemplate(position);
            return SpawnFromCode(position);
        }

        private static GameObject SpawnFromTemplate(Vector3 position)
        {
            var go = UnityEngine.Object.Instantiate(_template);
            go.name = "Geo Rock";
            go.transform.position = position;
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                t.gameObject.SetActive(true);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                r.enabled = true;
            foreach (var c in go.GetComponentsInChildren<Collider2D>(true))
                c.enabled = true;
            foreach (var fsm in go.GetComponentsInChildren<PlayMakerFSM>(true))
            { fsm.enabled = false; fsm.enabled = true; }
            foreach (var s in go.GetComponentsInChildren<tk2dSprite>(true))
            { s.enabled = true; s.ForceBuild(); }
            foreach (var a in go.GetComponentsInChildren<tk2dSpriteAnimator>(true))
            { a.enabled = true; if (a.DefaultClip != null) a.Play(a.DefaultClip); }
            var geoRock = go.GetComponent<GeoRock>();
            if (geoRock != null && geoRock.geoRockData != null)
            {
                geoRock.geoRockData.id = $"geo_clone_{Time.frameCount}_{UnityEngine.Random.Range(1000, 9999)}";
                geoRock.geoRockData.sceneName = SceneManager.GetActiveScene().name;
                geoRock.geoRockData.hitsLeft = 0;
            }
            return go;
        }

        private static GameObject SpawnFromCode(Vector3 position)
        {
            if (_cachedCollection == null)
            {
                Plugin.Log.LogWarning("[GeoRockBuilder] tk2d collection 未就绪，跳过生成");
                return null;
            }

            int spriteId = -1;
            for (int i = 0; i < _cachedCollection.Count; i++)
            {
                var def = _cachedCollection.spriteDefinitions[i];
                if (def != null && def.name == GeoRockSpriteName)
                {
                    spriteId = i;
                    break;
                }
            }
            if (spriteId < 0)
            {
                Plugin.Log.LogWarning($"[GeoRockBuilder] collection '{_cachedCollection.name}' 中未找到 '{GeoRockSpriteName}'");
                return null;
            }

            var go = new GameObject("Geo Rock (tk2d)");
            go.SetActive(false);
            go.layer = 19;
            go.transform.position = position;

            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();
            var tk2d = go.AddComponent<tk2dSprite>();
            tk2d.SetSprite(_cachedCollection, spriteId);
            tk2d.ForceBuild();

            go.AddComponent<BoxCollider2D>().size = new Vector2(1.2f, 1.2f);
            var breakable = go.AddComponent<Breakable>();
            InitBreakableFields(breakable, tk2d.GetComponent<Renderer>());

            var persistent = go.AddComponent<PersistentBoolItem>();
            persistent.OnGetSaveState += (PersistentItem<bool>.GetValueEvent)((out bool val) =>
            { val = breakable != null && breakable.IsBroken; });
            persistent.OnSetSaveState += (PersistentItem<bool>.SetValueEvent)(val =>
            { if (val && breakable != null) breakable.SetAlreadyBroken(); });

            go.SetActive(true);
            return go;
        }

        private static void InitBreakableFields(Breakable breakable, Renderer renderer)
        {
            try
            {
                var type = typeof(Breakable);
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;

                SetField(type, "flingDrops", flags, breakable, new GameObject[0]);
                SetField(type, "wholeParts", flags, breakable, new GameObject[0]);
                SetField(type, "remnantParts", flags, breakable, new GameObject[0]);
                SetField(type, "debrisParts", flags, breakable, new List<GameObject>());
                SetField(type, "debrisPartsAlt", flags, breakable, new List<GameObject>());
                SetField(type, "containingParticles", flags, breakable, new Probability.ProbabilityGameObject[0]);
                SetField(type, "flingObjectRegister", flags, breakable, new Breakable.FlingObject[0]);
                SetField(type, "requireBroken", flags, breakable, new Breakable[0]);
                var itemDropField = type.GetField("itemDropGroups", flags);
                if (itemDropField != null)
                    SetField(type, "itemDropGroups", flags, breakable,
                        Array.CreateInstance(itemDropField.FieldType.GetElementType(), 0));

                SetField(type, "wholeRenderer", flags, breakable, renderer);

                SetField(type, "ignorePersistence", flags, breakable, false);
                SetField(type, "resetOnEnable", flags, breakable, false);
                SetField(type, "useAltDebris", flags, breakable, false);
                SetField(type, "useHeroPlane", flags, breakable, false);
                SetField(type, "flingSelfOnHit", flags, breakable, false);
                SetField(type, "preventParticleRotation", flags, breakable, false);
                SetField(type, "forwardBreakEvent", flags, breakable, false);
                SetField(type, "ignoreDamagers", flags, breakable, false);
                SetField(type, "firstHitOnly", flags, breakable, false);
                SetField(type, "emitNoise", flags, breakable, false);
                SetField(type, "autoAddJitterComponent", flags, breakable, true);
                SetField(type, "megaFlingGeo", flags, breakable, false);
                SetField(type, "deparentOnBreak", flags, breakable, false);
                SetField(type, "immuneToBreakableBreaker", flags, breakable, false);

                SetField(type, "altDebrisChance", flags, breakable, 100f);
                SetField(type, "inertBackgroundThreshold", flags, breakable, 0f);
                SetField(type, "inertForegroundThreshold", flags, breakable, 0f);
                SetField(type, "angleOffset", flags, breakable, -60f);
                SetField(type, "effectOffset", flags, breakable, new Vector3(0f, 0.5f, 0f));
                SetField(type, "hitsToBreak", flags, breakable, 3);
                SetField(type, "hitCoolDown", flags, breakable, 0.15f);
                SetField(type, "noiseRadius", flags, breakable, 3f);
                SetField(type, "flingSpeedMin", flags, breakable, 10f);
                SetField(type, "flingSpeedMax", flags, breakable, 17f);
                SetField(type, "silkGain", flags, breakable, 0);
                SetField(type, "freezeMoment", flags, breakable, 0);

                SetField(type, "smallGeoDrops", flags, breakable, new MinMaxInt(5, 5));
                SetField(type, "mediumGeoDrops", flags, breakable, new MinMaxInt(0, 0));
                SetField(type, "largeGeoDrops", flags, breakable, new MinMaxInt(0, 0));
                SetField(type, "largeSmoothGeoDrops", flags, breakable, new MinMaxInt(0, 0));
                SetField(type, "shellShardDrops", flags, breakable, new MinMaxInt(0, 0));
                SetField(type, "silkDrops", flags, breakable, new MinMaxInt(0, 0));

                SetField(type, "hitFreezeMoment", flags, breakable, (int)0);
                SetField(type, "breakFreezeMoment", flags, breakable, (int)0);

                SetField(type, "audioSourcePrefab", flags, breakable, (AudioSource)null);
                SetField(type, "dustHitRegularPrefab", flags, breakable, (Transform)null);
                SetField(type, "dustHitDownPrefab", flags, breakable, (Transform)null);
                SetField(type, "breakEffectPrefab", flags, breakable, (GameObject)null);
                SetField(type, "strikeEffectPrefab", flags, breakable, (Transform)null);
                SetField(type, "nailHitEffectPrefab", flags, breakable, (Transform)null);
                SetField(type, "spellHitEffectPrefab", flags, breakable, (Transform)null);
                SetField(type, "hitEventReciever", flags, breakable, (GameObject)null);
                SetField(type, "passHitToEffects", flags, breakable, (object)null);
                SetField(type, "breakableRange", flags, breakable, (object)null);
                SetField(type, "silkDropEffect", flags, breakable, (GameObject)null);
                SetField(type, "silkGetEffect", flags, breakable, (GameObject)null);
                SetField(type, "silkDropsCondition", flags, breakable, (GameObject)null);
                SetField(type, "threadThinObject", flags, breakable, (GameObject)null);
                SetField(type, "silkDropsPersistent", flags, breakable, (PersistentBoolItem)null);

                SetField(type, "hitShake", flags, breakable, new CameraShakeTarget());
                SetField(type, "breakShake", flags, breakable, new CameraShakeTarget());

                SetField(type, "hitAudioEvent", flags, breakable, default(AudioEvent));
                SetField(type, "breakAudioEvent", flags, breakable, default(AudioEvent));

                SetField(type, "hitAudioClipTable", flags, breakable, (RandomAudioClipTable)null);
                SetField(type, "breakAudioClipTable", flags, breakable, (RandomAudioClipTable)null);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[GeoRockBuilder] InitBreakableFields 异常: {ex}");
            }
        }

        private static void SetField(Type type, string name, BindingFlags flags, object target, object value)
        {
            type.GetField(name, flags)?.SetValue(target, value);
        }
    }

    /// <summary>钱堆替换器：清除原生钱堆 → 原位生成随机奖励拾取点，持久化防复活。</summary>
    public static class GeoRockReplacer
    {
        public static void OnSceneLoaded(Scene scene)
        {
            Plugin.Instance?.StartCoroutine(ProcessScene(scene));
        }

        private static IEnumerator ProcessScene(Scene scene)
        {
            yield return new WaitForSeconds(0.3f);
            var rocks = UnityEngine.Object.FindObjectsOfType<GeoRock>();
            if (rocks.Length == 0) yield break;

            foreach (var rock in rocks)
            {
                if (rock == null) continue;
                var go = rock.gameObject;
                if (go.scene != scene) continue;

                var pos = go.transform.position;
                string key = $"{scene.name}_{pos.x:F2}_{pos.y:F2}_{pos.z:F2}";

                if (Plugin.SaveData.GeoRockReplacedPositions.Contains(key))
                {
                    UnityEngine.Object.Destroy(go);
                    continue;
                }

                var grd = rock.geoRockData;
                if (grd != null)
                {
                    string rockScene = string.IsNullOrEmpty(grd.sceneName) ? scene.name : grd.sceneName;
                    string rockId = string.IsNullOrEmpty(grd.id) ? go.name : grd.id;
                    try
                    {
                        var sceneData = SceneData.instance;
                        if (sceneData != null)
                        {
                            var f = typeof(SceneData).GetField("geoRocks",
                                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                            if (f != null && f.GetValue(sceneData) is object coll)
                            {
                                var remove = coll.GetType().GetMethod("Remove");
                                remove?.Invoke(coll, new object[] { rockScene, rockId });
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning($"[GeoRockReplacer] 清除持久化失败: {ex.Message}");
                    }
                }

                Plugin.SaveData.GeoRockReplacedPositions.Add(key);
                Plugin.SaveGlobalData();
                UnityEngine.Object.Destroy(go);

                Extracurrencypickup.SpawnPickupAtPosition(pos);
            }
        }
    }

    /// <summary>跳蚤救援生成器：从 Dock_16 克隆模板，任意场景生成可互动跳蚤。</summary>
    public static class FleaRescueBuilder
    {
        private static GameObject _template;
        private static bool _autoPrimed;
        private static tk2dSpriteCollectionData _cachedCollection;
        private static int _cachedSpriteId = -1;

        private static readonly string SourceScene = "Dock_16";

        public static bool HasTemplate => _template != null;

        /// <summary>缓存的中跳蚤精灵数据（供拾取点显示等复用）。</summary>
        public static tk2dSpriteCollectionData CachedCollection => _cachedCollection;
        public static int CachedSpriteId => _cachedSpriteId;

        public static void Init()
        {
            Plugin.Log.LogInfo("[FleaRescueBuilder] 初始化完成");
        }

        public static void OnSceneLoaded(Scene scene)
        {
            // ★ 不扫描当前场景的原生跳蚤——原生跳蚤由 FleaRescueReplacer 处理（替换为拾取点）。
            // 模板仅通过异步加载 Dock_16 获取（用于随机池/F4 生成救援跳蚤）。
            if (!_autoPrimed && _template == null)
            {
                _autoPrimed = true;
                Plugin.Instance.StartCoroutine(WaitAndPrimeAsync());
            }
        }

        private static IEnumerator WaitAndPrimeAsync()
        {
            int waitFrames = 0;
            while (HeroController.instance == null)
            {
                yield return null;
                waitFrames++;
                if (waitFrames > 300)
                {
                    Plugin.Log.LogWarning("[FleaRescueBuilder] 等待 HeroController 超时(300帧)，强制 Prime");
                    break;
                }
            }
            yield return new WaitForSeconds(0.5f);
            yield return PrimeTemplateAsync();
        }

        private static IEnumerator PrimeTemplateAsync()
        {
            if (_template != null) yield break;

            Plugin.BlackoutShow();

            var op = Addressables.LoadSceneAsync("Scenes/" + SourceScene, LoadSceneMode.Additive, true, 100);
            yield return op;

            if (op.Status != AsyncOperationStatus.Succeeded)
            {
                Plugin.Log.LogWarning($"[FleaRescueBuilder] PrimeTemplate 异步加载失败: {op.OperationException?.Message}");
                yield break;
            }

            var scene = op.Result.Scene;

            GameObject src = null;
            GameObject fallback = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name.IndexOf("flea", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (fallback == null) fallback = t.gameObject;
                        if (t.name.IndexOf("sleeping", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            src = t.gameObject;
                            break;
                        }
                    }
                }
                if (src != null) break;
            }
            if (src == null) src = fallback;

            if (src != null)
            {
                BuildTemplate(src, scene.name);
            }
            else
            {
                Plugin.Log.LogWarning($"[FleaRescueBuilder] 场景 '{scene.name}' 中未找到任何含 flea 的对象");
            }

            UnloadAdditiveScene(op, scene);

            yield return new WaitForSeconds(0.2f);
            Plugin.BlackoutHide();
        }

        private static void UnloadAdditiveScene(AsyncOperationHandle<SceneInstance> handle, Scene scene)
        {
            try
            {
                Addressables.UnloadSceneAsync(handle);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[FleaRescueBuilder] Addressables 卸载失败: {ex.Message}，尝试 SceneManager");
                try
                {
                    if (scene.IsValid() && scene.isLoaded)
                        SceneManager.UnloadSceneAsync(scene);
                }
                catch { }
            }
        }

        private static void BuildTemplate(GameObject src, string sceneName)
        {
            var srcTk2d = src.GetComponent<tk2dSprite>();
            if (srcTk2d != null && srcTk2d.Collection != null)
            {
                _cachedCollection = srcTk2d.Collection;
                _cachedSpriteId = srcTk2d.spriteId;
            }

            _template = UnityEngine.Object.Instantiate(src);
            _template.name = "Flea Rescue Template";
            UnityEngine.Object.DontDestroyOnLoad(_template);

            if (_cachedCollection != null)
            {
                var tk2d = _template.GetComponent<tk2dSprite>();
                if (tk2d != null)
                {
                    tk2d.SetSprite(_cachedCollection, _cachedSpriteId);
                    tk2d.ForceBuild();
                }
            }

            foreach (Transform t in _template.GetComponentsInChildren<Transform>(true))
            {
                foreach (var comp in t.gameObject.GetComponents<Component>())
                {
                    if (comp is Transform) continue;
                    if (comp is Renderer r) { r.enabled = false; continue; }
                    // 保留 PlayMakerFSM/PlayMakerTriggerEnter2D/tk2dSpriteAnimator/AudioSource：跳蚤互动逻辑依赖它们
                    if (comp is PlayMakerFSM || comp is PlayMakerTriggerEnter2D || comp is tk2dSpriteAnimator || comp is AudioSource) continue;
                    if (comp is Behaviour b) b.enabled = false;
                }
            }

            // 模板不应发声：停止并禁用所有 AudioSource（模板仅作克隆源，克隆时再按需启用），
            // 避免 DontDestroyOnLoad 常驻模板让跳蚤叫声跨场景持续播放。
            foreach (var audioSrc in _template.GetComponentsInChildren<AudioSource>(true))
            {
                if (audioSrc == null) continue;
                try { audioSrc.Stop(); } catch { }
                audioSrc.playOnAwake = false;
                audioSrc.enabled = false;
            }

            _template.SetActive(true);
            Plugin.Log.LogInfo($"[FleaRescueBuilder] 克隆模板建立 (来源: {sceneName})");
        }

        public static GameObject SpawnAtHero()
        {
            var hero = HeroController.instance;
            if (hero == null)
            {
                Plugin.Log.LogWarning("[FleaRescueBuilder] HeroController.instance == null");
                return null;
            }
            return SpawnAtPosition(new Vector3(hero.transform.position.x, hero.transform.position.y - 1f, 0f));
        }

        public static GameObject SpawnAtPosition(Vector3 position)
        {
            if (_template == null)
            {
                Plugin.Log.LogWarning("[FleaRescueBuilder] 模板未就绪，跳过生成");
                return null;
            }
            return SpawnFromTemplate(position);
        }

        private static GameObject SpawnFromTemplate(Vector3 position)
        {
            var go = UnityEngine.Object.Instantiate(_template);
            go.name = "Spawned Rescue";
            go.transform.position = position;

            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
                t.gameObject.SetActive(true);

            foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
            {
                foreach (var comp in t.gameObject.GetComponents<Component>())
                {
                    if (comp is Transform) continue;
                    if (comp is tk2dSprite s)
                    {
                        if (_cachedCollection != null && _cachedSpriteId >= 0)
                        {
                            s.SetSprite(_cachedCollection, _cachedSpriteId);
                            s.ForceBuild();
                        }
                        s.enabled = true;
                        continue;
                    }
                    if (comp is tk2dSpriteAnimator a) { a.enabled = true; if (a.DefaultClip != null) a.Play(a.DefaultClip); continue; }
                    if (comp is Renderer r) { r.enabled = true; continue; }
                    if (comp is Collider2D c) { c.enabled = true; continue; }
                    if (comp is PlayMakerFSM || comp is PlayMakerTriggerEnter2D || comp is tk2dSpriteAnimator) { ((Behaviour)comp).enabled = true; continue; }
                    if (comp is AudioSource audioComp)
                    {
                        // 模板为防跨场景响铃把 playOnAwake 关了；生成时恢复原样让叫声按原生逻辑播放
                        audioComp.enabled = true;
                        audioComp.playOnAwake = true;
                        continue;
                    }
                    if (comp is Behaviour b) b.enabled = false;
                }
            }

            go.AddComponent<FleaRescueSpawned>();
            go.SetActive(true);
            return go;
        }
    }

    /// <summary>
    /// 跳蚤替换器：整场景所有原生跳蚤对象合并为一组，替换为一个普通拾取点。
    /// 玩家捡起该拾取点时，按见到顺序消费 flea:01~27 映射奖励（仿苔莓 moss 模式）。
    /// </summary>
    public static class FleaRescueReplacer
    {
        public static void OnSceneLoaded(Scene scene)
        {
            Plugin.Instance?.StartCoroutine(ProcessScene(scene));
        }

        private static IEnumerator ProcessScene(Scene scene)
        {
            yield return new WaitForSeconds(0.3f);
            Plugin.Log.LogInfo($"[FleaRescueReplacer] ProcessScene 开始: {scene.name}, isLoaded={scene.isLoaded}");

            // 第一轮：列出场景中所有含 "flea" 的对象（诊断用）
            var fleaObjects = new List<GameObject>();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name.IndexOf("flea", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        fleaObjects.Add(t.gameObject);
                        Plugin.Log.LogInfo($"[FleaRescueReplacer] 发现 flea 对象: {t.gameObject.name}, path={GetPath(t)}, activeSelf={t.gameObject.activeSelf}, hasSpawned={t.gameObject.GetComponent<FleaRescueSpawned>() != null}");
                    }
                }
            }
            Plugin.Log.LogInfo($"[FleaRescueReplacer] 共发现 {fleaObjects.Count} 个 flea 对象");

            if (fleaObjects.Count == 0) yield break;

            // 取 anchor（优先 sleeping，否则取第一个）
            GameObject anchor = null;
            foreach (var go in fleaObjects)
            {
                if (go.name.IndexOf("sleeping", StringComparison.OrdinalIgnoreCase) >= 0)
                { anchor = go; break; }
            }
            if (anchor == null) anchor = fleaObjects[0];
            Plugin.Log.LogInfo($"[FleaRescueReplacer] anchor: {anchor.name}");

            // 本场景跳蚤已被处理过：清场不再生成
            bool sceneProcessed = Plugin.SaveData.FleaRescueReplacedPositions.Contains($"fleascene:{scene.name}")
                || Plugin.SaveData.PickedPickupKeys.Contains($"fleascene:{scene.name}");
            Plugin.Log.LogInfo($"[FleaRescueReplacer] sceneProcessed={sceneProcessed}");

            // 销毁所有原生 flea 对象
            int destroyed = 0;
            foreach (var go in fleaObjects)
            {
                if (go == null) continue;
                if (go.GetComponent<FleaRescueSpawned>() != null) continue;
                Plugin.Log.LogInfo($"[FleaRescueReplacer] 销毁: {go.name}");
                UnityEngine.Object.Destroy(go);
                destroyed++;
            }
            Plugin.Log.LogInfo($"[FleaRescueReplacer] 已销毁 {destroyed} 个对象");

            if (sceneProcessed) yield break;

            // ★ 原生跳蚤地点 → 用 Extracurrencypickup 生成拾取点（全局 prefab，不克隆）
            var pos = anchor.transform.position;
            string key = $"{scene.name}_{pos.x:F2}_{pos.y:F2}_{pos.z:F2}";

            Plugin.SaveData.FleaRescueReplacedPositions.Add($"fleascene:{scene.name}");
            Plugin.SaveGlobalData();

            Extracurrencypickup.SpawnPickupAtPosition(pos, "Simple Key");
            // 找到刚生成的拾取点并挂载我们的映射逻辑
            float bestDist = float.MaxValue;
            CollectableItemPickup bestPickup = null;
            foreach (var p in Resources.FindObjectsOfTypeAll<CollectableItemPickup>())
            {
                if (p == null || !p.gameObject.activeInHierarchy) continue;
                if (p.GetComponent<FleaSequentialPickupPoint>() != null) continue;
                float d = Vector3.SqrMagnitude(p.transform.position - pos);
                if (d < bestDist) { bestDist = d; bestPickup = p; }
            }
            if (bestPickup != null)
            {
                bestPickup.gameObject.AddComponent<FleaSequentialPickupPoint>().PickupKey = key;
                Plugin.Log.LogInfo($"[FleaRescueReplacer] 场景 '{scene.name}' 跳蚤点 {key} 替换为拾取点, pos=({pos.x:F2},{pos.y:F2})");
            }
            else
            {
                Plugin.Log.LogWarning($"[FleaRescueReplacer] 未能定位刚生成的拾取点");
            }
        }

        private static string GetPath(Transform t)
        {
            string path = t.name;
            while (t.parent != null) { t = t.parent; path = t.name + "/" + path; }
            return path;
        }
    }

    /// <summary>标记组件：标识跳蚤替换拾取点，由 PickupPatch 拦截 DoPickupAction 时走 flea:01~27 映射。</summary>
    public class FleaSequentialPickupPoint : MonoBehaviour
    {
        public string PickupKey;
    }

    /// <summary>标记组件：标识随机池/F4 生成的跳蚤，防止 FleaRescueReplacer 误清理；救援消失后计入游戏内救援任务。</summary>
    public class FleaRescueSpawned : MonoBehaviour
    {
        private string _spawnScene;
        private bool _recorded;

        private void Awake()
        {
            _spawnScene = gameObject.scene.name;
        }

        private void Start()
        {
            StartCoroutine(WatchDisappear());
        }

        private System.Collections.IEnumerator WatchDisappear()
        {
            while (!_recorded)
            {
                yield return new WaitForSeconds(0.5f);
                if (this == null) yield break;

                if (!gameObject.activeSelf)
                {
                    if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == _spawnScene)
                    {
                        _recorded = true;
                        FleaSceneMap.RecordFleaRescueNextAvailable();
                    }
                    yield break;
                }
            }
        }
    }
}
