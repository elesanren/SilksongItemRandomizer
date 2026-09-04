using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 跳蚤自动生成器：进场景时，对场景内所有"奖励映射为跳蚤救援"的触发点，
    /// 在该点位自动生成救援跳蚤（不用人物触发）。
    /// 覆盖所有非商店映射点：坐标拾取点 / heart / spool / moss / flower / check(车站) / lore / event。
    /// 商店(shop:)映射不在场景生成（保持 SpawnAtHero 人物处）。flea:NN 场景由 FleaRescueReplacer 处理。
    /// 已救走（FleaRescuedKeys 判重，跳蚤特有记录）的点位不重复生成；跳蚤被救走后仍走原有消失记录逻辑。
    /// </summary>
    public static class FleaAutoSpawner
    {
        private const string FleaRescueId = "flea:Rescue";
        private const string RandomFleaId = "virt:RandomFlea";

        /// <summary>开始游戏后预扫描映射表：仅含"映射为跳蚤救援的位置类型键"所属的场景（小写）。</summary>
        private static HashSet<string> _fleaScenes = null;
        private static Dictionary<string, string> _planRef = null;

        /// <summary>本次场景已生成过的跳蚤 pointKey（ProcessScene 开头清空），防止同帧重复生成。</summary>
        private static readonly HashSet<string> _spawnedThisScene = new(StringComparer.Ordinal);

        /// <summary>局内（进存档后）是否已克隆过模板，防止后续场景重复克隆。回主菜单（局外）时重置为 false。</summary>
        private static bool _ingameCloneDone;

        /// <summary>局内（进存档后）已进入的场景计数。第一个场景常为长黑幕剧情，此时克隆模板会渲染成黑，
        /// 故延迟到第二个局内可玩场景（_ingameSceneCount >= 2）才克隆一次模板。</summary>
        private static int _ingameSceneCount;

        public static void OnSceneLoaded(Scene scene)
        {
            if (!ItemRandomizer.IsInitialized) return;
            if (!SilksongItemRandomizerAPI.IsEnabled()) return;

            // 回到主菜单/加载页 = 局外会话结束：重置"局内场景计数与被克隆标志"，
            // 下次进存档时第二个场景会再次克隆模板。
            if (IsMenuOrLoading(scene.name))
            {
                _ingameSceneCount = 0;
                _ingameCloneDone = false;
                return;
            }

            _ingameSceneCount++;

            Plugin.Instance?.StartCoroutine(ProcessScene(scene));
        }

        /// <summary>局外场景：主菜单/加载页。参照 UIManager 反编译中 IsMenuScene / Loading 判定。</summary>
        private static bool IsMenuOrLoading(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return true;
            var gm = GameManager.instance;
            if (gm != null && gm.IsMenuScene()) return true;
            return sceneName == "Loading"
                || sceneName == "Menu_Title"
                || sceneName == "Menu"
                || sceneName == "Pre_Menu_Intro";
        }

        private static bool IsFleaRescueId(string id)
            => id == FleaRescueId || id == RandomFleaId;

        /// <summary>该场景是否为"跳蚤生成场景"：坐标/收集类(heavyScan) 或 check 车站或 event 映射为跳蚤救援。</summary>
        private static bool IsFleaSpawnScene(Scene scene, string sceneName, string sceneLower, bool heavyScan)
        {
            if (heavyScan) return true;

            // check：车站收费机映射为跳蚤
            if (MapStationUnlockPatch.TryGetStationBoolForScene(sceneName, out string boolName))
            {
                string checkId = PreGeneratedMap.ResolveRawRewardId("check:" + boolName);
                if (IsFleaRescueId(checkId)) return true;
            }

            // event：剧情演出映射为跳蚤
            string eventId = PreGeneratedMap.ResolveRawRewardId("event:" + sceneName);
            if (IsFleaRescueId(eventId)) return true;

            return false;
        }

        /// <summary>
        /// 预扫描映射表，按键类型分流，记录"含跳蚤奖励触发点"的场景（小写）。
        /// 坐标拾取点/收集类(heart/spool/moss/flower)走重型对象扫描，必须列场景门控；
        /// check/lore/event 自门控且开销小，可不入列（仍按各自键判跳蚤）。
        /// </summary>
        private static void BuildFleaScenePlan()
        {
            var mapping = Plugin.SaveData?.PreGeneratedMappings;
            if (mapping == null) return;
            if (ReferenceEquals(_planRef, mapping)) return;
            _planRef = mapping;

            var scenes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in mapping)
            {
                string stored = kv.Value;
                string id = stored.StartsWith("reward:", StringComparison.Ordinal)
                    ? stored.Substring("reward:".Length)
                    : stored;
                if (!IsFleaRescueId(id)) continue;

                string key = kv.Key;
                if (key.StartsWith("heart:", StringComparison.Ordinal)
                    || key.StartsWith("spool:", StringComparison.Ordinal)
                    || key.StartsWith("moss:", StringComparison.Ordinal)
                    || key.StartsWith("flower:", StringComparison.Ordinal))
                {
                    scenes.Add(key.Substring(key.IndexOf(':') + 1).ToLowerInvariant());
                }
                else if (key.StartsWith("lore:", StringComparison.Ordinal))
                {
                    string rest = key.Substring("lore:".Length);
                    int colon = rest.IndexOf(':');
                    if (colon > 0) scenes.Add(rest.Substring(0, colon).ToLowerInvariant());
                }
                else if (key.StartsWith("event:", StringComparison.Ordinal))
                {
                    string rest = key.Substring("event:".Length);
                    int colon = rest.IndexOf(':');
                    string scene = colon > 0 ? rest.Substring(0, colon) : rest;
                    if (!string.IsNullOrEmpty(scene)) scenes.Add(scene.ToLowerInvariant());
                }
                else if (!key.StartsWith("shop:", StringComparison.Ordinal)
                    && !key.StartsWith("check:", StringComparison.Ordinal))
                {
                    // flea:NN 键：对应机制一(FleaRescueReplacer)将原生跳蚤替换出的拾取点，本身有坐标锚点。
                    // 该拾取点天然承载 flea:NN 键的顺序奖励（拾取时走 PickupPatch 顺序消费 + 通用 pickup 消失机制），
                    // 机制三对 flea:NN 键【不额外生成跳蚤】，仅纳入场景门控以便覆盖；
                    // 实际生成由机制一拾取点承接，机制三靠 FleaSequentialPickupPoint 标记排除避免重复。
                    // （机制三与机制一 FleaRescueReplacer 是完全无关的两条独立链路。）
                    string scene = ExtractSceneLower(key);
                    if (scene == null && key.StartsWith("flea:", StringComparison.Ordinal))
                    {
                        // flea:NN 无坐标后缀，经 FleaSceneMap 反查场景
                        string rest = key.Substring("flea:".Length);
                        if (int.TryParse(rest, out int idx) && idx >= 1 && idx <= FleaSceneMap.Count)
                            scene = FleaSceneMap.IndexToScene(idx);
                    }
                    if (!string.IsNullOrEmpty(scene)) scenes.Add(scene.ToLowerInvariant());
                }
            }
            _fleaScenes = scenes;
        }

        /// <summary>从坐标键（场景_x_y_z）提取场景名（小写）。非坐标键返回 null。</summary>
        private static string ExtractSceneLower(string key)
        {
            try
            {
                int last = key.LastIndexOf('_');
                if (last <= 0) return null;
                int second = key.LastIndexOf('_', last - 1);
                if (second <= 0) return null;
                int third = key.LastIndexOf('_', second - 1);
                if (third <= 0) return null;
                return key.Substring(0, third).ToLowerInvariant();
            }
            catch { return null; }
        }

        /// <summary>
        /// 判定该跳蚤生成点是否已被救走：FleaRescuedKeys 记录的是【人物坐标】，
        /// 人物触发跳蚤时站在跳蚤身上（两坐标几乎重合），故用"同场景 + 坐标容差"匹配。
        /// 容差对齐 FleaRescueReplacer.IsCoordinatePicked（2.0）。
        /// </summary>
        private static bool IsRescuedNear(Vector3 pos, string pointKey, float tolerance = 2.0f)
        {
            var save = Plugin.SaveData;
            if (save == null || save.FleaRescuedKeys == null || save.FleaRescuedKeys.Count == 0) return false;

            string scene = PreGeneratedMap.ResolveSceneOfKey(pointKey);
            if (string.IsNullOrEmpty(scene)) return false;

            foreach (string key in save.FleaRescuedKeys)
            {
                if (string.IsNullOrEmpty(key) || !key.StartsWith(scene + "_", StringComparison.OrdinalIgnoreCase)) continue;
                string rest = key.Substring(scene.Length + 1);
                string[] p = rest.Split('_');
                if (p.Length < 3) continue;
                if (float.TryParse(p[0], out float px)
                    && float.TryParse(p[1], out float py)
                    && float.TryParse(p[2], out float pz))
                {
                    if (Mathf.Abs(pos.x - px) <= tolerance
                        && Mathf.Abs(pos.y - py) <= tolerance)
                        return true;
                }
            }
            return false;
        }

        private static bool SpawnFlea(Vector3 pos, string pointKey)
        {
            var save = Plugin.SaveData;
            if (save == null || string.IsNullOrEmpty(pointKey)) return false;
            if (IsRescuedNear(pos, pointKey)) return false;               // 已救走：遇到容差圈内的人物坐标即不再生成
            if (!_spawnedThisScene.Add(pointKey)) return false;           // 本场景本帧已生成，去重

            // 持久化记录该点坐标（跨场景重进仍能按此再生；跳蚤未救走前每进场景都生成）
            // 即便模板暂未就绪也先记录，后续重进场景由 RespawnPersisted 补生成。
            string coordStr = $"{pos.x:F2}_{pos.y:F2}_{pos.z:F2}";
            if (!save.FleaSpawnPoints.TryGetValue(pointKey, out string prev) || prev != coordStr)
            {
                save.FleaSpawnPoints[pointKey] = coordStr;
                Plugin.SaveGlobalData();
            }

            var go = FleaRescueBuilder.SpawnAtPosition(pos);
            if (go == null)
            {
                Plugin.Log.LogWarning($"[FleaAutoSpawner] 跳蚤生成失败（模板未就绪或异常）: {pointKey} 坐标 {coordStr}");
                return false;
            }
            var marker = go.GetComponent<FleaRescueSpawned>();
            if (marker == null) marker = go.AddComponent<FleaRescueSpawned>();
            marker.PointKey = pointKey;
            Plugin.Log.LogInfo($"[FleaAutoSpawner] 跳蚤已生成: {pointKey} 坐标 {coordStr}");
            return true;
        }

        /// <summary>
        /// 重进场景：按 FleaSpawnPoints 持久化坐标，补生成当前场景尚未被救走的跳蚤。
        /// 触发点失效后分支扫描找不到对象，只有靠此持久化坐标才能持续再生；
        /// 已被救走（FleaRescuedKeys）的点不再生成。先于此场景的分支扫描执行。
        /// </summary>
        private static int RespawnPersisted(string sceneLower)
        {
            var save = Plugin.SaveData;
            if (save == null || save.FleaSpawnPoints == null || save.FleaSpawnPoints.Count == 0)
                return 0;

            int n = 0;
            foreach (var kv in save.FleaSpawnPoints)
            {
                if (string.IsNullOrEmpty(kv.Key)) continue;
                string scene = PreGeneratedMap.ResolveSceneOfKey(kv.Key);
                if (scene == null || !string.Equals(scene, sceneLower, StringComparison.OrdinalIgnoreCase))
                    continue;
                string[] parts = kv.Value.Split('_');
                if (parts.Length < 3) continue;
                if (!float.TryParse(parts[0], out float x)
                    || !float.TryParse(parts[1], out float y)
                    || !float.TryParse(parts[2], out float z))
                    continue;

                if (SpawnFlea(new Vector3(x, y, z), kv.Key)) n++;
            }
            return n;
        }

        private static IEnumerator ProcessScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrEmpty(scene.name))
                yield break;
            yield return new WaitForSeconds(0.3f);
            if (!scene.IsValid() || !scene.isLoaded || string.IsNullOrEmpty(scene.name))
                yield break;

            string sceneName = scene.name;
            string sceneLower = sceneName.ToLowerInvariant();
            int spawned = 0;
            _spawnedThisScene.Clear();   // 每场景重开去重集

            // 预扫描映射表，仅在有跳蚤位置类触发点的场景做重型对象扫描
            BuildFleaScenePlan();
            bool heavyScan = _fleaScenes != null && _fleaScenes.Contains(sceneLower);

            // 局内第 2 个可玩场景才当场克隆一次模板（无视模板是否已就绪），后续场景复用不再克隆。
            // 第一个场景常为长黑幕剧情，若在此时克隆，跳蚤模板会渲染成黑，
            // 故延迟到 _ingameSceneCount >= 2（跳过首个黑幕场景）。
            // 局外（菜单/加载）已在 OnSceneLoaded 重置 _ingameSceneCount 与 _ingameCloneDone。
            if (!_ingameCloneDone && _ingameSceneCount >= 2)
            {
                _ingameCloneDone = true;
                yield return FleaRescueBuilder.CloneTemplateNowAsync();
            }

            // 重进场景：先按已持久化的跳蚤生成点坐标补生成当前场景未救走的跳蚤
            // （触发点已失效、分支扫描可能找不到对象，靠此持久化坐标再生）
            try { spawned += RespawnPersisted(sceneLower); }
            catch (Exception ex) { Plugin.Log.LogWarning($"[FleaAutoSpawner] 持久化生成点重生成异常: {ex.Message}"); }

            if (heavyScan)
            {
                try
                {
                    spawned += ProcessCoordinatePickups(scene);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[FleaAutoSpawner] 坐标拾取点扫描异常: {ex.Message}");
                }

                try
                {
                    spawned += ProcessNamedCollectibles(scene, sceneName, sceneLower);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[FleaAutoSpawner] 收集类扫描异常: {ex.Message}");
                }

                try
                {
                    spawned += ProcessLore(scene, sceneName);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[FleaAutoSpawner] lore 扫描异常: {ex.Message}");
                }
            }

            try
            {
                spawned += ProcessCheckStation(sceneName);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[FleaAutoSpawner] check 车站扫描异常: {ex.Message}");
            }

            try
            {
                spawned += ProcessEvent(sceneName);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[FleaAutoSpawner] event 扫描异常: {ex.Message}");
            }

            if (spawned > 0)
                Plugin.Log.LogInfo($"[FleaAutoSpawner] 场景 {sceneName} 共自动生成 {spawned} 只跳蚤");
        }

        // ---------- 坐标拾取点 ----------
        private static int ProcessCoordinatePickups(Scene scene)
        {
            int n = 0;
            foreach (var p in Resources.FindObjectsOfTypeAll<CollectableItemPickup>())
            {
                if (p == null || p.gameObject.scene != scene) continue;
                if (p.GetComponent<FleaSequentialPickupPoint>() != null) continue;
                string id = PreGeneratedMap.ResolveRawRewardIdAt(p.gameObject.scene.name, p.transform.position);
                if (!IsFleaRescueId(id)) continue;
                // 生成本点跳蚤：立即停用该拾取点对象本身，防止"跳蚤+原触发点"双份交互
                if (p.gameObject.activeSelf) p.gameObject.SetActive(false);
                if (SpawnFlea(p.transform.position, PreGeneratedMap.PickupKeyOf(p))) n++;
            }
            return n;
        }

        // ---------- heart / spool / moss / flower（按场景键） ----------
        private static int ProcessNamedCollectibles(Scene scene, string sceneName, string sceneLower)
        {
            int n = 0;

            string heartId = PreGeneratedMap.ResolveRawRewardId($"heart:{sceneLower}");
            string spoolId = PreGeneratedMap.ResolveRawRewardId($"spool:{sceneLower}");
            string mossId = PreGeneratedMap.ResolveRawRewardId($"moss:{sceneLower}");
            string flowerId = PreGeneratedMap.ResolveRawRewardId($"flower:{sceneLower}");

            if (!IsFleaRescueId(heartId) && !IsFleaRescueId(spoolId)
                && !IsFleaRescueId(mossId) && !IsFleaRescueId(flowerId))
                return 0;

            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null || go.transform == null || go.scene != scene) continue;
                string name = go.name;
                Vector3 pos;
                string pointKey = null;
                if (IsFleaRescueId(heartId) && string.Equals(name, "Heart Piece", StringComparison.OrdinalIgnoreCase))
                {
                    pos = go.transform.position;
                    pointKey = $"heart:{sceneLower}_{pos.x:F2}_{pos.y:F2}_{pos.z:F2}";
                }
                else if (IsFleaRescueId(spoolId) && string.Equals(name, "Silk Spool", StringComparison.OrdinalIgnoreCase))
                {
                    pos = go.transform.position;
                    pointKey = $"spool:{sceneLower}_{pos.x:F2}_{pos.y:F2}_{pos.z:F2}";
                }
                else if (IsFleaRescueId(mossId)
                    && (string.Equals(name, "moss_berry_fruit", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(name, "Mossberry Pickup", StringComparison.OrdinalIgnoreCase)))
                {
                    pos = go.transform.position;
                    pointKey = $"moss:{sceneLower}_{pos.x:F2}_{pos.y:F2}_{pos.z:F2}";
                }
                else if (IsFleaRescueId(flowerId)
                    && name.IndexOf("shell_flower_purple", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    pos = go.transform.position;
                    pointKey = $"flower:{sceneLower}_{pos.x:F2}_{pos.y:F2}_{pos.z:F2}";
                }
                else
                {
                    continue;
                }

                // 生成本点跳蚤：立即停用该收集对象本身，防止"跳蚤+原触发点"双份交互
                if (go.activeSelf) go.SetActive(false);
                if (SpawnFlea(pos, pointKey)) n++;
            }
            return n;
        }

        // ---------- check：车站收费机（键绑场景） ----------
        private static int ProcessCheckStation(string sceneName)
        {
            if (!MapStationUnlockPatch.TryGetStationBoolForScene(sceneName, out string boolName))
                return 0;
            string id = PreGeneratedMap.ResolveRawRewardId("check:" + boolName);
            if (!IsFleaRescueId(id)) return 0;

            int n = 0;
            var active = SceneManager.GetActiveScene();
            if (!active.IsValid()) return 0;

            foreach (var bench in Resources.FindObjectsOfTypeAll<BellBench>())
            {
                if (bench == null || bench.gameObject.scene != active) continue;
                var tollField = AccessTools_Field("tollMachine", bench);
                Vector3 pos;
                if (tollField is GameObject toll && toll != null)
                {
                    pos = toll.transform.position;
                    // 生成本点跳蚤：停用收费机，防止"跳蚤+原收费机"双份交互
                    if (toll.activeSelf) toll.SetActive(false);
                    if (SpawnFlea(pos, $"check:{boolName}")) n++;
                }
                else
                {
                    pos = bench.transform.position;
                    if (bench.gameObject.activeSelf) bench.gameObject.SetActive(false);
                    if (SpawnFlea(pos, $"check:{boolName}")) n++;
                }
            }
            if (n > 0) return n;

            foreach (var root in active.GetRootGameObjects())
                n += SpawnAtMachineNames(root.transform, sceneName, boolName);
            return n;
        }

        private static int SpawnAtMachineNames(Transform t, string sceneName, string boolName)
        {
            int n = 0;
            if (t == null) return 0;
            if (t.name.IndexOf("tol", StringComparison.OrdinalIgnoreCase) >= 0
                || t.name.IndexOf("rosary_string_machine", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // 生成本点跳蚤：停用收费机，防止双份交互
                if (t.gameObject.activeSelf) t.gameObject.SetActive(false);
                if (SpawnFlea(t.position, $"check:{boolName}")) n++;
                return n;
            }
            for (int i = 0; i < t.childCount; i++)
                n += SpawnAtMachineNames(t.GetChild(i), sceneName, boolName);
            return n;
        }

        // ---------- lore：石碑 ----------
        private static int ProcessLore(Scene scene, string sceneName)
        {
            int n = 0;
            foreach (var npc in Resources.FindObjectsOfTypeAll<NPCControlBase>())
            {
                if (npc == null || npc.gameObject.scene != scene) continue;
                if (npc.InteractLabel != InteractableBase.PromptLabels.Inspect) continue;
                string objName = npc.gameObject.name;
                if (!LoreRandomizer.IsLoreObject(sceneName, objName)) continue;
                string key = $"lore:{sceneName}:{objName}";
                string id = PreGeneratedMap.ResolveRawRewardId(key);
                if (!IsFleaRescueId(id)) continue;
                // 生成本点跳蚤：停用石碑对象本身，防止"跳蚤+原石碑"双份交互
                if (npc.gameObject.activeSelf) npc.gameObject.SetActive(false);
                if (SpawnFlea(npc.transform.position, key)) n++;
            }
            return n;
        }

        // ---------- event：剧情演出（以人物位置代表演出点） ----------
        private static int ProcessEvent(string sceneName)
        {
            string id = PreGeneratedMap.ResolveRawRewardId("event:" + sceneName);
            if (!IsFleaRescueId(id)) return 0;
            var hero = HeroController.instance;
            Vector3 pos = hero != null ? hero.transform.position : Vector3.zero;
            if (pos == Vector3.zero) return 0;
            return SpawnFlea(pos, $"event:{sceneName}") ? 1 : 0;
        }

        private static object AccessTools_Field(string fieldName, object instance)
        {
            try
            {
                var f = HarmonyLib.AccessTools.Field(instance.GetType(), fieldName);
                return f?.GetValue(instance);
            }
            catch { return null; }
        }
    }
}
