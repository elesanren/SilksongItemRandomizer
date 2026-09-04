using GlobalSettings;
using HarmonyLib;
using Object = UnityEngine.Object;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Reflection;
using System;
using UnityEngine.SceneManagement;
using UnityEngine;

// PickupPatch.cs - 修复后的完整版本

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 拾取点接管补丁
    /// 改造后：不再直接依赖 Plugin.SaveData，改为通过 IPickupSaveDataAccessor 接口访问持久化数据
    /// 保留所有原有功能：禁用原生 PersistentBoolItem、标记已捡、应用状态等
    /// 修改：ResetAll 中不再调用 WarpToLastBench，重置种子世界时不传送回重生点
    /// </summary>
    [HarmonyPatch]
    public static class PickupPatch
    {
        // ========== 持久化数据访问接口 ==========
        public interface IPickupSaveDataAccessor
        {
            HashSet<string> GetPickedPickupKeys();
            void SavePickedPickupKeys(HashSet<string> keys);
        }

        private static IPickupSaveDataAccessor _saveData;

        private static bool _isDirty = false;
        private static bool _isEnabled = false;

        // ========== 初始化 ==========
        public static void Initialize(IPickupSaveDataAccessor saveDataAccessor)
        {
            _saveData = saveDataAccessor;
        }

        /// <summary>
        /// 启用拾取点接管（由 API 在总开关开启时调用）
        /// </summary>
        public static void EnableRandomizer()
        {
            if (_isEnabled) return;
            _isEnabled = true;
            DisablePersistentBoolItems();
            var scene = SceneManager.GetActiveScene();
            ApplyStateToScene(scene);
            Plugin.Log.LogInfo("物品随机器已启用，拾取点已接管");
        }

        /// <summary>
        /// 禁用拾取点接管（恢复原生行为）
        /// </summary>
        public static void DisableRandomizer()
        {
            if (!_isEnabled) return;
            _isEnabled = false;
            RestorePersistentBoolItems();
            Plugin.Log.LogInfo("物品随机器已禁用，拾取点已恢复原生行为");
        }

        /// <summary>
        /// 获取拾取点唯一标识 Key
        /// </summary>
        private static FieldInfo _cachedItemDataField;
        private static readonly System.Collections.Generic.Dictionary<System.Type, FieldInfo> _cachedIdFieldByType = new();

        public static string GetPickupKey(CollectableItemPickup pickup)
        {
            var pbi = pickup.GetComponent<PersistentBoolItem>();
            if (pbi != null)
            {
                if (_cachedItemDataField == null)
                    _cachedItemDataField = typeof(PersistentBoolItem).GetField("itemData", BindingFlags.Instance | BindingFlags.NonPublic);
                if (_cachedItemDataField != null)
                {
                    var itemData = _cachedItemDataField.GetValue(pbi);
                    if (itemData != null)
                    {
                        var dataType = itemData.GetType();
                        if (!_cachedIdFieldByType.TryGetValue(dataType, out var idField))
                        {
                            idField = dataType.GetField("ID", BindingFlags.Instance | BindingFlags.Public);
                            _cachedIdFieldByType[dataType] = idField;
                        }
                        if (idField != null && idField.GetValue(itemData) is string id && !string.IsNullOrEmpty(id))
                            return $"{pickup.gameObject.scene.name}_{id}";
                    }
                }
            }
            var pos = pickup.transform.position;
            return $"{pickup.gameObject.scene.name}_{pos.x:F2}_{pos.y:F2}_{pos.z:F2}";
        }

        private static void SavePickedKeys()
        {
            if (!_isDirty) return;
            if (_saveData != null)
                _saveData.SavePickedPickupKeys(_saveData.GetPickedPickupKeys());
            _isDirty = false;
        }

        private static void DisablePersistentBoolItems()
        {
            var allPickups = Resources.FindObjectsOfTypeAll<CollectableItemPickup>();
            foreach (var p in allPickups)
            {
                var pbi = p.GetComponent<PersistentBoolItem>();
                if (pbi != null && pbi.enabled)
                    pbi.enabled = false;
            }
            Plugin.Log.LogInfo("已禁用所有 PersistentBoolItem");
        }

        private static void RestorePersistentBoolItems()
        {
            var allPickups = Resources.FindObjectsOfTypeAll<CollectableItemPickup>();
            foreach (var p in allPickups)
            {
                var pbi = p.GetComponent<PersistentBoolItem>();
                if (pbi != null && !pbi.enabled)
                    pbi.enabled = true;
            }
            Plugin.Log.LogInfo("已恢复所有 PersistentBoolItem");
        }

        public static void ApplyStateToScene(Scene scene)
        {
            if (!_isEnabled) return;
            ApplyStateToSceneCore(scene, null);
        }

        private static HashSet<CollectableItemPickup> ApplyStateToSceneCore(Scene scene, HashSet<CollectableItemPickup> seen)
        {
            var pickups = Resources.FindObjectsOfTypeAll<CollectableItemPickup>()
                .Where(p => p.gameObject.scene == scene).ToList();
            var processed = seen ?? new HashSet<CollectableItemPickup>();
            var pickedKeys = _saveData?.GetPickedPickupKeys() ?? new HashSet<string>();
            foreach (var p in pickups)
            {
                if (seen != null && !processed.Add(p)) continue;
                var key = GetPickupKey(p);
                bool isPicked = pickedKeys.Contains(key);
                p.gameObject.SetActive(!isPicked);
            }
            Plugin.Log.LogInfo($"应用拾取点状态: 场景 {scene.name}, 共 {pickups.Count} 个点, 已捡 {pickedKeys.Count} 个");
            return processed;
        }

        public static void ApplyStateToSceneWithDelay(Scene scene, float delay = 0.2f)
        {
            if (!_isEnabled) return;
            var firstPass = ApplyStateToSceneCore(scene, null);
            if (Plugin.Instance != null)
                Plugin.Instance.StartCoroutine(DelayedApply(scene, delay, firstPass));
        }

        private static IEnumerator DelayedApply(Scene scene, float delay, HashSet<CollectableItemPickup> firstPass)
        {
            yield return new WaitForSeconds(delay);
            if (SceneManager.GetActiveScene().name == scene.name)
            {
                ApplyStateToSceneCore(scene, firstPass);
                Plugin.Log.LogInfo("强制重生：延迟增量应用拾取点状态完成");
            }
        }

        public static void MarkAsPicked(CollectableItemPickup pickup)
        {
            if (!_isEnabled) return;
            var key = GetPickupKey(pickup);
            var pickedKeys = _saveData?.GetPickedPickupKeys();
            if (pickedKeys != null && pickedKeys.Add(key))
            {
                _isDirty = true;
                SavePickedKeys();
                pickup.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 重置所有拾取点记录（清空已捡记录，但不传送）
        /// </summary>
        public static void ResetAll()
        {
            var pickedKeys = _saveData?.GetPickedPickupKeys();
            pickedKeys?.Clear();
            _isDirty = true;
            SavePickedKeys();
            Plugin.Log.LogInfo("已捡记录已清空，所有拾取点将重新出现");
            // 注释掉传送回重生点的调用，重置种子世界时不再自动传送
            // WarpToLastBench();
        }

        // ========== Harmony 补丁 ==========
        [HarmonyPatch(typeof(CollectableItemPickup), "CheckActivation")]
        [HarmonyPrefix]
        private static bool Prefix_CheckActivation(CollectableItemPickup __instance)
        {
            if (!_isEnabled) return true;
            return false;
        }

        [HarmonyPatch(typeof(CollectableItemPickup), "SetPlayerDataBool")]
        [HarmonyPrefix]
        private static bool Prefix_SetPlayerDataBool(CollectableItemPickup __instance, string boolName)
        {
            if (!_isEnabled) return true;
            return false;
        }

        // ★★★ 核心修改：只认原生调用，Architect Hook 全部放行 ★★★
        [HarmonyPatch(typeof(CollectableItemPickup), "DoPickupAction")]
        [HarmonyPrefix]
        private static void Prefix_DoPickupAction(CollectableItemPickup __instance, ref bool __runOriginal)
        {
            try
            {
                if (!__runOriginal || __instance == null) return;
                if (!_isEnabled) return;

                // ★ 跳蚤替换拾取点：走 flea:01~27 顺序映射，阻断原生 Simple Key
                var fleaMarker = __instance.GetComponent<FleaSequentialPickupPoint>();
                if (fleaMarker != null)
                {
                    var save = Plugin.SaveData;
                    if (save != null)
                    {
                        IRandomReward reward = PreGeneratedMap.ResolveSequentialReward("flea", save.FleaSeq + 1);
                        if (reward != null)
                        {
                            save.FleaSeq++;
                            Plugin.SaveGlobalData();
                            reward.Give();
                            ItemRandomizer.AddGivenCount(reward.Id);
                            ItemRandomizer.RecordMapping($"item:{__instance.name}", $"reward:{reward.Id}");
                            RecentItemsUI.AddItem(reward);
                            Plugin.Log.LogInfo($"[FleaRescue] 跳蚤拾取点已捡起，消费 flea:{save.FleaSeq:D2} -> {reward.Id}");
                        }
                        // 已捡记录以「拾取点坐标」为准（与机制一重复生成判断一致，对齐 Extracurrencypickup）
                        string pickedKey = !string.IsNullOrEmpty(fleaMarker.PickupKey)
                            ? fleaMarker.PickupKey
                            : $"fleascene:{__instance.gameObject.scene.name}";
                        save.PickedPickupKeys.Add(pickedKey);
                        Plugin.SaveGlobalData();
                        FleaSceneMap.RecordFleaRescueByScene(__instance.gameObject.scene.name);
                    }
                    Plugin.Instance.StartCoroutine(DelayedCleanup(__instance));
                    __runOriginal = false;
                    return;
                }

                // ★★★ 检查调用栈：如果来自 Architect 或 CustomPickup，直接放行 ★★★
                // 使用 StackFrame(false) 替代 Environment.StackTrace，避免构建完整字符串导致高开销
                bool isArchitectCall = false;
                for (int i = 1; i <= 5; i++)
                {
                    var frame = new System.Diagnostics.StackFrame(i, false);
                    var method = frame.GetMethod();
                    if (method == null || method.DeclaringType == null) break;
                    string typeName = method.DeclaringType.Name;
                    if (typeName.Contains("Architect") || typeName.Contains("CustomPickup"))
                    {
                        isArchitectCall = true;
                        break;
                    }
                }
                if (isArchitectCall)
                {
                    return;  // 放行，不执行随机逻辑
                }

                // ★★★ 只有原生调用才会执行到这里 ★★★
                var originalItem = __instance.Item;
                if (originalItem == null || ItemRandomizer.ExcludedNames.Contains(originalItem.name)) return;

                var key = $"{__instance.gameObject.scene.name}_{__instance.transform.position.x:F2}_{__instance.transform.position.y:F2}_{__instance.transform.position.z:F2}";
                Plugin.AddDestroyedPickupKey(key);
                // ★ 预生成映射：把该点的 key 传给 TryGetPatch（F2 坐标，与预生成表一致），命中则按表给予
                PreGeneratedMap.PendingKey = PreGeneratedMap.PickupKeyOf(__instance);
                originalItem.TryGet(false, true);
                Plugin.Instance.StartCoroutine(DelayedCleanup(__instance));
                __runOriginal = false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"PickupPatch.Prefix 出错: {ex}");
            }
        }

        private static IEnumerator DelayedCleanup(CollectableItemPickup pickup)
        {
            yield return new WaitForSeconds(0.2f);
            if (pickup != null)
            {
                MarkAsPicked(pickup);
                UnityEngine.Object.Destroy(pickup.gameObject);
            }
        }

        [HarmonyPatch(typeof(CollectableItemPickup), "DoPickupAction")]
        [HarmonyPostfix]
        private static void Postfix_DoPickupAction(CollectableItemPickup __instance) { }

        public static bool IsKeyPicked(string key)
        {
            if (!_isEnabled) return false;
            var pickedKeys = _saveData?.GetPickedPickupKeys();
            return pickedKeys != null && pickedKeys.Contains(key);
        }
    }
}

// Extracurrencypickup.cs - 修复后的完整版本

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 额外钥匙点生成：在指定坐标生成 Simple Key 拾取点
    /// 改造后：通过接口访问已捡坐标持久化数据，不再直接依赖 Plugin.SaveData
    /// 保留所有原有功能：坐标表、已捡记录、延迟生成等
    /// </summary>
    [HarmonyPatch]
    public static class Extracurrencypickup
    {
        // ========== 持久化数据访问接口 ==========
        public interface IExtraPickupSaveDataAccessor
        {
            HashSet<string> GetPickedPositions();
            void SavePickedPositions(HashSet<string> positions);
        }

        private static IExtraPickupSaveDataAccessor _saveData;
        private static bool _isInitialized = false;
        private static Dictionary<string, SavedItem> _cachedItems;  // 缓存 SavedItem 按 name 索引，避免每次 spawn 扫描

        // ========== 预定义额外钥匙点的坐标表 ==========
        private static readonly Dictionary<string, List<(Vector3 pos, string itemId)>> pickupTable = new()
        {
            ["Tut_01"] = new() { (new Vector3(26f, 77f, 0f), "Simple Key") },
            ["Bonetown"] = new() { (new Vector3(224f, 80f, 0f), "Simple Key"), (new Vector3(151f, 69f, 0f), "Simple Key") },
            ["Bone_01c"] = new() { (new Vector3(176f, 58f, 0f), "Simple Key"), (new Vector3(139f, 7.5f, 0f), "Simple Key") },
            ["Bone_01"] = new() { (new Vector3(118f, 7.5f, 0f), "Simple Key") },
            ["Bone_04"] = new() { (new Vector3(204f, 5.5f, 0f), "Simple Key") },
            ["Mosstown_01"] = new() { (new Vector3(48f, 23.5f, 0f), "Simple Key") },
            ["Mosstown_02"] = new() { (new Vector3(154f, 55.5f, 0f), "Simple Key") },
            ["Bone_14"] = new() { (new Vector3(129f, 8.5f, 0f), "Simple Key") },
            ["Bone_19"] = new() { (new Vector3(28f, 13.5f, 0f), "Simple Key") },
            ["Belltown_basement_03"] = new() { (new Vector3(65.5f, 5.5f, 0f), "Simple Key"), (new Vector3(71f, 116.5f, 0f), "Simple Key") },
            ["Bone_08"] = new() { (new Vector3(30.5f, 47.5f, 0f), "Simple Key") },
            ["Bone_09"] = new() { (new Vector3(6.5f, 36.5f, 0f), "Simple Key") },
            ["Bone_East_03"] = new() { (new Vector3(153f, 22.5f, 0f), "Simple Key") },
            ["Ant_04_left"] = new() { (new Vector3(7f, 29f, 0f), "Simple Key") },
            ["Ant_21"] = new() { (new Vector3(48.5f, 74f, 0f), "Simple Key") },
            ["Dock_06_Church"] = new() { (new Vector3(11f, 21.5f, 0f), "Simple Key") },
            ["Bone_10"] = new() { (new Vector3(8f, 43.5f, 0f), "Simple Key"), (new Vector3(112.5f, 66.5f, 0f), "Simple Key"), (new Vector3(30f, 45.5f, 0f), "Simple Key") },
            ["Bone_11"] = new() { (new Vector3(8f, 9.5f, 0f), "Simple Key") },
            ["Aspid_01"] = new() { (new Vector3(10.5f, 7.5f, 0f), "Simple Key") },
            ["Bonegrave"] = new() { (new Vector3(270.5f, 72.5f, 0f), "Simple Key") },
            ["Chapel_Wanderer"] = new() { (new Vector3(79.5f, 106.4f, 0f), "Simple Key") },
            ["Shellwood_26"] = new() { (new Vector3(98.5f, 75.5f, 0f), "Simple Key") },
            ["Belltown_04"] = new() { (new Vector3(77f, 48.5f, 0f), "Simple Key"), (new Vector3(63f, 19.5f, 0f), "Simple Key") },
            ["Bone_East_17"] = new() { (new Vector3(7f, 84.5f, 0f), "Simple Key"), (new Vector3(42f, 96.5f, 0f), "Simple Key") },
            ["Bone_East_17b"] = new() { (new Vector3(12f, 31.5f, 0f), "Simple Key") },
            ["Bone_East_16"] = new() { (new Vector3(11f, 16.5f, 0f), "Simple Key") },
            ["Bone_East_08"] = new() { (new Vector3(59f, 21.5f, 0f), "Simple Key") },
            ["Bone_East_14"] = new() { (new Vector3(88f, 40.5f, 0f), "Simple Key") },
            ["Bone_East_14b"] = new() { (new Vector3(250f, 52.5f, 0f), "Simple Key") },
            ["Bone_East_07"] = new() { (new Vector3(10f, 165.5f, 0f), "Simple Key") },
            ["Bone_East_09b"] = new() { (new Vector3(61f, 141.5f, 0f), "Simple Key") },
            ["Greymoor_15"] = new() { (new Vector3(57.3f, 74.5f, 0f), "Simple Key") },
            ["Greymoor_15b"] = new() { (new Vector3(205f, 61.5f, 0f), "Simple Key"), (new Vector3(198f, 41.5f, 0f), "Simple Key") },
            ["Greymoor_22"] = new() { (new Vector3(88f, 25.5f, 0f), "Simple Key") },
            ["Greymoor_02"] = new() { (new Vector3(52f, 90.5f, 0f), "Simple Key") },
            ["Greymoor_01"] = new() { (new Vector3(8.61f, 17.5f, 0f), "Simple Key") },
            ["Greymoor_04"] = new() { (new Vector3(32f, 36.5f, 0f), "Simple Key") },
            ["Greymoor_05"] = new() { (new Vector3(95f, 62.5f, 0f), "Simple Key"), (new Vector3(7f, 19f, 0f), "Simple Key") },
            ["Greymoor_06"] = new() { (new Vector3(6f, 79.5f, 0f), "Simple Key") },
            ["Greymoor_07"] = new() { (new Vector3(28f, 10.5f, 0f), "Simple Key") },
            ["Greymoor_08"] = new() { (new Vector3(140f, 30.5f, 0f), "Simple Key") },
            ["Shellwood_11"] = new() { (new Vector3(59f, 21.5f, 0f), "Simple Key") },
            ["Coral_12"] = new() { (new Vector3(87f, 34.5f, 0f), "Simple Key") },
            ["Song_01"] = new() { (new Vector3(19.5f, 80.5f, 0f), "Simple Key"), (new Vector3(109f, 129.5f, 0f), "Simple Key") },
            ["Song_11"] = new() { (new Vector3(44f, 44.5f, 0f), "Simple Key") },
            ["Song_03"] = new() { (new Vector3(130f, 4.5f, 0f), "Simple Key") },
            ["Song_15"] = new() { (new Vector3(6.5f, 6.5f, 0f), "Simple Key") },
            ["Song_17"] = new() { (new Vector3(34.5f, 100.5f, 0f), "Simple Key") },
            ["Hang_08"] = new() { (new Vector3(19.5f, 195f, 0f), "Simple Key") },
            ["Library_04"] = new() { (new Vector3(51f, 77f, 0f), "Simple Key") },
            ["Library_06"] = new() { (new Vector3(36.5f, 63f, 0f), "Simple Key") },
            ["Library_07"] = new() { (new Vector3(39f, 139.5f, 0f), "Simple Key") },
            ["Dock_02"] = new() { (new Vector3(103f, 51.5f, 0f), "Simple Key") },
            ["Bone_East_24"] = new() { (new Vector3(246f, 65.5f, 0f), "Simple Key") },
            ["Bone_East_18"] = new() { (new Vector3(139f, 36.5f, 0f), "Simple Key") },
            ["Bone_East_18b"] = new() { (new Vector3(133f, 6.5f, 0f), "Simple Key") },
            ["Bone_01b"] = new() { (new Vector3(7f, 85.5f, 0f), "Simple Key") },
            ["Bone_East_15"] = new() { (new Vector3(47f, 8.5f, 0f), "Simple Key") },
            ["Song_09"] = new() { (new Vector3(11f, 56.5f, 0f), "Simple Key") },
        };

        /// <summary>
        /// 枚举全部额外货币点（场景名 + 生成坐标），供预生成映射表一次性生成映射。
        /// 坐标与拾取时 PickupPatch 计算的 F2 坐标 key 一致。
        /// </summary>
        public static IEnumerable<(string scene, Vector3 pos)> EnumerateAllPickupPoints()
        {
            foreach (var kv in pickupTable)
                foreach (var (pos, _) in kv.Value)
                    yield return (kv.Key, pos);
        }

        // ========== 初始化 ==========
        public static void Initialize(IExtraPickupSaveDataAccessor saveDataAccessor)
        {
            _saveData = saveDataAccessor;
            _isInitialized = true;
            Plugin.Log.LogInfo("[Extracurrencypickup] 初始化完成");
        }

        public static void RegisterAll()
        {
            Plugin.Log.LogInfo("[Extracurrencypickup] 已注册 Harmony 补丁（由 API 管理）");
        }

        public static void ResetAll()
        {
            if (_saveData != null)
                _saveData.GetPickedPositions().Clear();
            Plugin.SaveGlobalData();
            Plugin.Log.LogInfo("[Extracurrencypickup] 所有坐标记录已重置");
        }

        // ========== 已捡坐标判断 ==========
        private static SavedItem GetCachedSavedItem(string itemId)
        {
            if (_cachedItems == null)
            {
                var all = Resources.FindObjectsOfTypeAll<SavedItem>();
                _cachedItems = new Dictionary<string, SavedItem>(all.Length);
                foreach (var s in all)
                    if (s != null && !string.IsNullOrEmpty(s.name))
                        _cachedItems[s.name] = s;
            }
            _cachedItems.TryGetValue(itemId, out var item);
            return item;
        }

        private static bool IsPositionPicked(string sceneName, Vector3 pos, float tolerance = 2.0f)
        {
            if (_saveData == null) return false;
            var pickedKeys = _saveData.GetPickedPositions();
            foreach (string key in pickedKeys)
            {
                if (!key.StartsWith(sceneName + "_")) continue;
                string after = key.Substring(sceneName.Length + 1);
                int u1 = after.IndexOf('_');
                if (u1 < 0) continue;
                string xStr = after.Substring(0, u1);
                string rest = after.Substring(u1 + 1);
                int u2 = rest.IndexOf('_');
                string yStr = (u2 >= 0) ? rest.Substring(0, u2) : rest;
                if (float.TryParse(xStr, out float px) && float.TryParse(yStr, out float py))
                {
                    if (Mathf.Abs(pos.x - px) <= tolerance && Mathf.Abs(pos.y - py) <= tolerance)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        // ========== 生成拾取点 ==========
        public static void SpawnPickupsForScene(Scene scene)
        {
            if (!SilksongItemRandomizerAPI.IsEnabled()) return;

            string sceneName = scene.name;
            if (!pickupTable.TryGetValue(sceneName, out var points)) return;

            foreach (var (pos, itemId) in points)
            {
                if (IsPositionPicked(sceneName, pos)) continue;

                if (Plugin.Instance != null)
                    Plugin.Instance.StartCoroutine(DelayedSpawn(pos, itemId));
            }
        }

        private static IEnumerator DelayedSpawn(Vector3 position, string itemId)
        {
            yield return new WaitForSeconds(0.2f);
            SpawnPickupAt(position, itemId);
        }

        private static GameObject SpawnPickupAt(Vector3 position, string itemId)
        {
            CollectableItemPickup prefabComp = Gameplay.CollectableItemPickupPrefab;
            if (prefabComp == null)
            {
                var anyPickup = Resources.FindObjectsOfTypeAll<CollectableItemPickup>()
                    .FirstOrDefault(p => p.gameObject.scene.isLoaded);
                if (anyPickup == null)
                {
                    Plugin.Log.LogError("[Extracurrencypickup] No pickup prefab found.");
                    return null;
                }
                prefabComp = anyPickup;
            }

            GameObject newObj = Object.Instantiate(prefabComp.gameObject, position, Quaternion.identity);
            CollectableItemPickup pickup = newObj.GetComponent<CollectableItemPickup>();
            if (pickup == null)
            {
                Plugin.Log.LogError("[Extracurrencypickup] Instantiated object has no CollectableItemPickup.");
                Object.Destroy(newObj);
                return null;
            }

            // 缓存 SavedItem 查找，避免每次 spawn 扫描全部资源
            SavedItem item = GetCachedSavedItem(itemId);
            if (item == null)
            {
                Plugin.Log.LogError($"[Extracurrencypickup] Item '{itemId}' not found.");
                Object.Destroy(newObj);
                return null;
            }

            pickup.SetItem(item, false);
            var pbi = pickup.GetComponent<PersistentBoolItem>();
            if (pbi != null) pbi.enabled = false;

            newObj.SetActive(true);
            return newObj;
        }

        /// <summary>对外接口：在任意坐标生成拾取点（供 GeoRock 替换等场景使用）。直接返回刚生成的对象，避免二次全资源扫描。</summary>
        public static GameObject SpawnPickupAtPosition(Vector3 position, string itemId = "Simple Key")
        {
            return SpawnPickupAt(position, itemId);
        }

        // ========== Harmony 补丁（记录物品获得坐标） ==========
        [HarmonyPatch(typeof(SavedItem), "TryGet")]
        [HarmonyPostfix]
        public static void OnItemTryGet(SavedItem __instance, bool __result)
        {
            if (!__result) return;
            if (_saveData == null) return;
            var hero = HeroController.instance;
            if (hero == null) return;
            var scene = SceneManager.GetActiveScene();
            Vector3 pos = hero.transform.position;
            string key = $"{scene.name}_{pos.x:F2}_{pos.y:F2}_{pos.z:F2}";
            if (_saveData.GetPickedPositions().Add(key))
            {
                _saveData.SavePickedPositions(_saveData.GetPickedPositions());
            }
        }
    }
}

// CurrencyCollectPatch.cs - 修复后的完整版本

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 货币收集保底（简单钥匙）补丁
    /// 改造后：通过接口访问持久化数据，不再直接依赖 Plugin.SaveData
    /// 保留所有原有功能：收集计数、阈值触发钥匙给予
    /// </summary>
    [HarmonyPatch(typeof(CurrencyObjectBase), "Collect")]
    public static class CurrencyCollectPatch
    {
        // ========== 持久化数据访问接口 ==========
        public interface ICurrencySaveDataAccessor
        {
            int GetTotalCollectCount();
            void SetTotalCollectCount(int count);
            bool GetFirstKeyGiven();
            void SetFirstKeyGiven(bool given);
            bool GetSecondKeyGiven();
            void SetSecondKeyGiven(bool given);
            int GetFirstThreshold();
            void SetFirstThreshold(int threshold);
            int GetSecondThreshold();
            void SetSecondThreshold(int threshold);
        }

        private static ICurrencySaveDataAccessor _saveData;
        private static SavedItem _cachedSimpleKey;  // 缓存 Simple Key 引用，避免每次 GiveKey 扫描
        private const string KeyName = "Simple Key";

        // ========== 初始化 ==========
        public static void Initialize(ICurrencySaveDataAccessor saveDataAccessor)
        {
            _saveData = saveDataAccessor;
        }

        // ========== 对外接口（用于外部重置） ==========
        public static void ResetCounters()
        {
            if (_saveData != null)
            {
                _saveData.SetTotalCollectCount(0);
                _saveData.SetFirstKeyGiven(false);
                _saveData.SetSecondKeyGiven(false);
            }
            Plugin.Log.LogInfo("货币保底计数器已重置");
        }

        public static void ResetKeyState() => ResetCounters();

        // ========== Harmony 补丁 ==========
        [HarmonyPostfix]
        private static void Postfix(bool __result)
        {
            if (!__result) return;
            if (_saveData == null) return;

            int newCount = _saveData.GetTotalCollectCount() + 1;
            _saveData.SetTotalCollectCount(newCount);

            int firstThreshold = _saveData.GetFirstThreshold();
            int secondThreshold = _saveData.GetSecondThreshold();

            if (!_saveData.GetFirstKeyGiven() && newCount >= firstThreshold)
            {
                GiveKey();
                _saveData.SetFirstKeyGiven(true);
                Plugin.Log.LogInfo($"第一次钥匙保底触发，当前货币收集次数: {newCount}");
            }
            else if (!_saveData.GetSecondKeyGiven() && newCount >= secondThreshold)
            {
                GiveKey();
                _saveData.SetSecondKeyGiven(true);
                Plugin.Log.LogInfo($"第二次钥匙保底触发，当前货币收集次数: {newCount}");
            }
        }

        private static void GiveKey()
        {
            if (_cachedSimpleKey == null)
                _cachedSimpleKey = Resources.FindObjectsOfTypeAll<SavedItem>().FirstOrDefault(i => i.name == KeyName);
            if (_cachedSimpleKey != null)
            {
                TryGetPatch.BypassRandom = true;
                try
                {
                    _cachedSimpleKey.TryGet(false, true);
                }
                finally
                {
                    TryGetPatch.BypassRandom = false;
                }
            }
            else
            {
                Plugin.Log.LogError($"钥匙保底失败：找不到物品 {KeyName}");
            }
        }
    }
}

// MossberryRandomizer.cs - 苔莓随机化拦截
// 机制（来自 areamoss prefab 反编译）：
//   枝头苔莓 moss_berry_fruit（场景实例）
//     - PersistentBoolItem（场景填充，判定是否已拾取）
//     - PlayMakerFSM "Control"（Pause→Init→Idle；Idle 收到 DAMAGED → Break）
//     - DroppableItem items=[Mossberry.asset]
//   打落：moss_berry_fruit Break 状态 FlingObject 抛落，生成可拾取物 Mossberry Pickup
//   拾取：Mossberry Pickup FSM Collect 状态 -> CollectableItemCollect(Item=Mossberry)
//         -> Mossberry.Collect(1) -> CollectableItemManager.AddItem(Mossberry,1)（计数+1、弹UI）
//   重生根源：CollectableItem.CanGetMore() = IsConsumable() || !IsAtMax()（CollectableItem.cs:331）
//             苔莓可售卖（consumable 恒 true）-> 恒可重生 -> 场景重进苔莓重新激活
//
// 本模块两个功能：
//   1. 拾取拦截：CollectableItem.Collect 命中 Mossberry -> 跳过原生 Collect，改发随机奖励，并记录当前房间
//   2. 房间消失：场景加载时，若该场景已记录过苔莓，隐藏场景内全部 moss_berry_fruit / Mossberry Pickup
//      （等同原生"已捡过"效果：枝头不再显示苔莓）

namespace SilksongItemRandomizer
{
    /// <summary>苔莓随机化：拦截 Mossberry 拾取为随机奖励，并做房间级"已捡"持久化。</summary>
    public static class MossberryRandomizer
    {
        public const string MossberryName = "Mossberry";

        private static bool _isGiving = false;

        private static readonly HashSet<string> HideNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "moss_berry_fruit", "Mossberry Pickup"
        };

        /// <summary>当前场景名（无 GameManager 时回退到 active scene）。</summary>
        public static string CurrentSceneName()
        {
            try
            {
                var gm = GameManager.instance;
                if (gm != null && !string.IsNullOrEmpty(gm.sceneName))
                    return gm.sceneName;
            }
            catch { }
            var scene = SceneManager.GetActiveScene();
            return scene.IsValid() ? scene.name : "";
        }

        /// <summary>场景名规范化（小写，与 collectible_scene_map.txt 键一致；空返回 null）。</summary>
        private static string NormalizeScene(string name)
            => string.IsNullOrEmpty(name) ? null : name.ToLowerInvariant();

        public static bool IsRoomCollected(string sceneName)
        {
            var rooms = Plugin.SaveData?.MossberryCollectedRooms;
            return rooms != null && !string.IsNullOrEmpty(sceneName) && rooms.Contains(sceneName);
        }

        public static void MarkRoomCollected(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return;
            var rooms = Plugin.SaveData?.MossberryCollectedRooms;
            if (rooms == null) return;
            if (rooms.Add(sceneName))
                Plugin.SaveGlobalData(); // 去抖落盘
        }

        /// <summary>按当前场景名从预生成映射取苔莓奖励（键=moss:场景名，Keys 由 collectible_scene_map.txt 生成）。</summary>
        private static IRandomReward ResolveSequential()
        {
            string scene = CurrentSceneName();
            if (string.IsNullOrEmpty(scene)) return null;
            return PreGeneratedMap.ResolveReward($"moss:{NormalizeScene(scene)}");
        }

        /// <summary>拾取拦截：CollectableItem.Collect 命中 Mossberry 时改发随机奖励。</summary>
        [HarmonyPatch(typeof(CollectableItem), "Collect", new Type[] { typeof(int), typeof(bool) })]
        public static class CollectableItem_Collect_Patch
        {
            [HarmonyPrefix]
            private static bool Prefix(CollectableItem __instance, ref bool __runOriginal)
            {
                try
                {
                    if (_isGiving) return true;
                    if (TryGetPatch.BypassRandom) return true; // ★ 自发发放（物品奖励池里的苔莓浆果/丝矛保底等）：放行原生 Collect，不被误伤
                    if (__instance == null) return true;
                    if (!SilksongItemRandomizerAPI.IsEnabled()) return true;

                    // ★ 分派：苔莓 / 花芯(Shell Flower) 共用同一拦截点
                    if (string.Equals(__instance.name, MossberryName, StringComparison.OrdinalIgnoreCase))
                        return MossberryRandomizer.InterceptMossberry(__instance, ref __runOriginal);
                    if (string.Equals(__instance.name, ShellFlowerRandomizer.ShellFlowerName, StringComparison.OrdinalIgnoreCase))
                        return ShellFlowerRandomizer.InterceptShellFlower(__instance, ref __runOriginal);

                    return true;
                }
                catch (Exception ex)
                {
                    try { Plugin.Log.LogError($"[Collect] 拦截异常: {ex}"); } catch { }
                    return true;
                }
            }
        }

        /// <summary>苔莓命中处理：跳过原生 Collect，发随机奖励并记录房间。</summary>
        private static bool InterceptMossberry(CollectableItem __instance, ref bool __runOriginal)
        {
            string scene = CurrentSceneName();
            var reward = ResolveSequential();
            if (reward == null)
                reward = ItemRandomizer.GetRandomReward();
            if (reward == null) return true;

            _isGiving = true;
            try
            {
                reward.Give();
                ItemRandomizer.AddGivenCount(reward.Id);
                ItemRandomizer.RecordMapping("item:Mossberry", "reward:" + reward.Id);
                RecentItemsUI.AddItem(reward);
            }
            finally
            {
                _isGiving = false;
            }

            MarkRoomCollected(scene);
            Plugin.Log.LogInfo($"[Mossberry] 场景 {scene} 拦截苔莓 → 随机奖励 {reward.Id}");
            __runOriginal = false;
            return false; // 跳过原生 Collect
        }

        /// <summary>场景加载：已记录的房间隐藏全部苔莓（枝头果实 + 可拾取物）。</summary>
        public static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            try
            {
                if (scene == null || string.IsNullOrEmpty(scene.name)) return;
                if (!IsRoomCollected(scene.name)) return;

                int hidden = 0;
                var roots = scene.GetRootGameObjects();
                foreach (var root in roots)
                {
                    if (root == null) continue;
                    hidden += HideMatchingRecursive(root.transform, HideNames);
                }
                if (hidden > 0)
                    Plugin.Log.LogInfo($"[Mossberry] 场景 {scene.name} 已捡过苔莓，隐藏 {hidden} 个苔莓对象");
            }
            catch (Exception ex)
            {
                try { Plugin.Log.LogError($"[Mossberry] OnSceneLoaded 异常: {ex}"); } catch { }
            }
        }

        private static int HideMatchingRecursive(Transform t, HashSet<string> names)
        {
            int count = 0;
            if (t == null) return 0;
            if (names.Contains(t.name))
            {
                t.gameObject.SetActive(false);
                count++;
            }
            for (int i = 0; i < t.childCount; i++)
                count += HideMatchingRecursive(t.GetChild(i), names);
            return count;
        }
    }

    // ShellFlowerRandomizer.cs - 花芯(Shell Flower)随机化拦截
    // 机制（来自 shellwood 场景 dump + quests.bundle 反编译）：
    //   可砍紫花 _0015/_0016/_0018_shell_flower_purple（SlashableSpriteSwapper：砍击切换sprite+粒子）
    //     → 打碎掉落花芯可拾取物
    //     → 拾取走 CollectableItem.Collect（与苔莓同一入口）
    //   任务物品资产 = collectableitems.bundle "Shell Flower"（INV_NAME_SHELL_FLOWER）
    //     - uniqueCollectBool 为空 → 可连捡，customMaxAmount=0（默认上限由任务控制）
    //     - 灰根任务内部名 "Shell Flowers"（quests.bundle）：targets[0].Counter=Shell Flower, Count=6
    //     - consumeTargetIfApplicable=1：交付时按 Collectables 数据里的 Amount 扣除
    //
    // 本模块三个功能：
    //   1. 拾取拦截：CollectableItem.Collect 命中 Shell Flower → 发随机奖励
    //      并静默补 CollectableItemManager.AddItem(1)：Amount+1 使灰根任务进度照常推进（无原生UI弹窗）
    //   2. 房间消失：场景加载时若该场景已记录过花芯，隐藏全部 *shell_flower_purple* 紫花（含花蕾形态）
    //   3. 随机池自发发放的花芯（SavedItemReward 奖励池命中 flower:xx 映射）经 BypassRandom 放行，
    //      不被本补丁误伤造成循环

    /// <summary>花芯随机化：拦截 Shell Flower 拾取为随机奖励，静默保任务计数，并做房间级"已捡"持久化。</summary>
    public static class ShellFlowerRandomizer
    {
        public const string ShellFlowerName = "Shell Flower";

        /// <summary>紫花对象名包含匹配关键字（_0015/_0016/_0018_shell_flower_purple 及编号后缀变体）。</summary>
        private const string FlowerNameKeyword = "shell_flower_purple";

        /// <summary>苔莓拦截点复用入口（CollectableItem_Collect_Patch 分派调用，防重入由其 _isGiving 统一处理）。</summary>
        public static bool InterceptShellFlower(CollectableItem __instance, ref bool __runOriginal)
        {
            try
            {
                string scene = MossberryRandomizer.CurrentSceneName();
                var reward = ResolveSequential();
                if (reward == null)
                    reward = ItemRandomizer.GetRandomReward();
                if (reward == null) return true;

                reward.Give();
                ItemRandomizer.AddGivenCount(reward.Id);
                ItemRandomizer.RecordMapping("item:Shell Flower", "reward:" + reward.Id);
                RecentItemsUI.AddItem(reward);

                // ★ 静默补任务计数：灰根任务 target 直接读 Shell Flower 的 Amount，
                //   不补则任务永远无法交付。AddItem→AffectItemData 内部 SetData+IncrementVersion，无 UI。
                try
                {
                    CollectableItemManager.AddItem(__instance, 1);
                }
                catch (Exception exAdd)
                {
                    try { Plugin.Log.LogError($"[ShellFlower] 补计数失败: {exAdd}"); } catch { }
                }

                MarkRoomCollected(scene);
                Plugin.Log.LogInfo($"[ShellFlower] 场景 {scene} 拦截花芯 → 随机奖励 {reward.Id}（已补任务计数）");
                __runOriginal = false;
                return false; // 跳过原生 Collect（避免原生 UI 弹窗/双计数）
            }
            catch (Exception ex)
            {
                try { Plugin.Log.LogError($"[ShellFlower] 拦截异常: {ex}"); } catch { }
                return true;
            }
        }

        /// <summary>按当前场景名从预生成映射取花芯奖励（键=flower:场景名，Keys 由 collectible_scene_map.txt 生成）。</summary>
        private static IRandomReward ResolveSequential()
        {
            string scene = MossberryRandomizer.CurrentSceneName();
            if (string.IsNullOrEmpty(scene)) return null;
            return PreGeneratedMap.ResolveReward($"flower:{NormalizeScene(scene)}");
        }

        /// <summary>场景名规范化（小写，与 collectible_scene_map.txt 键一致；空返回 null）。</summary>
        private static string NormalizeScene(string name)
            => string.IsNullOrEmpty(name) ? null : name.ToLowerInvariant();

        public static bool IsRoomCollected(string sceneName)
        {
            var rooms = Plugin.SaveData?.ShellFlowerCollectedRooms;
            return rooms != null && !string.IsNullOrEmpty(sceneName) && rooms.Contains(sceneName);
        }

        public static void MarkRoomCollected(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return;
            var rooms = Plugin.SaveData?.ShellFlowerCollectedRooms;
            if (rooms == null) return;
            if (rooms.Add(sceneName))
                Plugin.SaveGlobalData(); // 去抖落盘
        }

        /// <summary>场景加载：已记录的房间隐藏全部紫花（花蕾不再出现）。</summary>
        public static void OnSceneLoaded(Scene scene)
        {
            try
            {
                if (scene == null || string.IsNullOrEmpty(scene.name)) return;
                if (!IsRoomCollected(scene.name)) return;

                int hidden = 0;
                var roots = scene.GetRootGameObjects();
                foreach (var root in roots)
                {
                    if (root == null) continue;
                    hidden += HideContainsRecursive(root.transform, FlowerNameKeyword);
                }
                if (hidden > 0)
                    Plugin.Log.LogInfo($"[ShellFlower] 场景 {scene.name} 已捡过花芯，隐藏 {hidden} 个紫花对象");
            }
            catch (Exception ex)
            {
                try { Plugin.Log.LogError($"[ShellFlower] OnSceneLoaded 异常: {ex}"); } catch { }
            }
        }

        /// <summary>名称包含匹配隐藏（紫花对象带编号后缀，精确名单不可穷举）。</summary>
        private static int HideContainsRecursive(Transform t, string keyword)
        {
            int count = 0;
            if (t == null) return 0;
            if (t.name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                t.gameObject.SetActive(false);
                count++;
            }
            for (int i = 0; i < t.childCount; i++)
                count += HideContainsRecursive(t.GetChild(i), keyword);
            return count;
        }
    }

    /// <summary>
    /// 丝轴碎片世界触发点接管：
    /// 游戏原生在世界场景放置 7+ 个 ID=="Silk Spool" 的 PrefabCollectable 触发点，
    /// 玩家到达时场景 FSM 调用该资产的 Get()/TryGet() 发放丝轴碎片。
    /// 本补丁拦截 PrefabCollectable.Get(bool)，当实例名为 "Silk Spool" 且随机器启用时，
    /// 跳过原生发放，改为给予随机奖励（复用随机池），实现"触发点随机化"。
    ///
    /// 隔离说明：
    /// - virt:SpoolPart 虚拟奖励发放（NativePickupGiver.GiveSpoolPart）时设置 Bypass=true，
    ///   放行原生 Get，避免把"奖励池发放的真丝轴"再随机成别的物品造成循环。
    /// - TryGetPatch 已拦截 TryGet 路径；本补丁兜底拦截直接调用 Get() 的世界触发路径。
    /// </summary>
    [HarmonyPatch(typeof(PrefabCollectable), "Get", new Type[] { typeof(bool) })]
    public static class SpoolPartPatch
    {
        /// <summary>旁路标志：虚拟奖励发放时置 true，放行原生丝轴发放。</summary>
        public static bool Bypass = false;

        private static bool _isGiving = false;

        [HarmonyPrefix]
        private static bool Prefix(PrefabCollectable __instance, ref bool __runOriginal)
        {
            try
            {
                if (_isGiving) return true;
                if (Bypass) return true;
                if (SilkSpoolState.IsSelfGiving) return true;
                if (!SilksongItemRandomizerAPI.IsEnabled()) return true;
                if (__instance == null) return true;

                bool isSpool = string.Equals(__instance.name, "Silk Spool", StringComparison.OrdinalIgnoreCase);
                bool isHeartPiece = string.Equals(__instance.name, "Heart Piece", StringComparison.OrdinalIgnoreCase);
                if (!isSpool && !isHeartPiece) return true;

                // ★ 碎片世界点触发（丝轴/面具）：跳过原生发放，给随机
                // 标记静默窗口：后续被驱动的碎片 UI 动画流程不播动画/不加碎片/不加上限，但正常走完
                SilkSpoolState.MarkNativeIntercept(5f);
                var reward = ItemRandomizer.GetRandomReward();
                if (reward == null) return true;

                _isGiving = true;
                try
                {
                    reward.Give();
                    ItemRandomizer.AddGivenCount(reward.Id);
                    ItemRandomizer.RecordMapping("item:" + __instance.name, "reward:" + reward.Id);
                    RecentItemsUI.AddItem(reward);
                }
                finally
                {
                    _isGiving = false;
                }
                __runOriginal = false;
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SpoolPart] 异常: {ex}");
                return true;
            }
        }
    }
}
