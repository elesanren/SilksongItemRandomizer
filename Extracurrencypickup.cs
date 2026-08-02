// Extracurrencypickup.cs - 修复后的完整版本
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using GlobalSettings;

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
                        Plugin.Log.LogInfo($"[Extracurrencypickup] 坐标 ({pos.x:F2},{pos.y:F2}) 匹配已记录 ({px:F2},{py:F2})，跳过生成");
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

        private static void SpawnPickupAt(Vector3 position, string itemId)
        {
            CollectableItemPickup prefabComp = Gameplay.CollectableItemPickupPrefab;
            if (prefabComp == null)
            {
                var anyPickup = Resources.FindObjectsOfTypeAll<CollectableItemPickup>()
                    .FirstOrDefault(p => p.gameObject.scene.isLoaded);
                if (anyPickup == null)
                {
                    Plugin.Log.LogError("[Extracurrencypickup] No pickup prefab found.");
                    return;
                }
                prefabComp = anyPickup;
            }

            GameObject newObj = Object.Instantiate(prefabComp.gameObject, position, Quaternion.identity);
            CollectableItemPickup pickup = newObj.GetComponent<CollectableItemPickup>();
            if (pickup == null)
            {
                Plugin.Log.LogError("[Extracurrencypickup] Instantiated object has no CollectableItemPickup.");
                Object.Destroy(newObj);
                return;
            }

            // 缓存 SavedItem 查找，避免每次 spawn 扫描全部资源
            SavedItem item = GetCachedSavedItem(itemId);
            if (item == null)
            {
                Plugin.Log.LogError($"[Extracurrencypickup] Item '{itemId}' not found.");
                Object.Destroy(newObj);
                return;
            }

            pickup.SetItem(item, false);
            var pbi = pickup.GetComponent<PersistentBoolItem>();
            if (pbi != null) pbi.enabled = false;

            newObj.SetActive(true);
            Plugin.Log.LogInfo($"[Extracurrencypickup] Spawned pickup at {position}, item {itemId}");
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
                Plugin.Log.LogInfo($"[Extracurrencypickup] 记录物品获得坐标: {key}");
            }
        }
    }
}