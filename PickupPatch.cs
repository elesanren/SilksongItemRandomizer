// PickupPatch.cs - 修复后的完整版本
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        public static string GetPickupKey(CollectableItemPickup pickup)
        {
            var pbi = pickup.GetComponent<PersistentBoolItem>();
            if (pbi != null)
            {
                var itemDataField = typeof(PersistentBoolItem).GetField("itemData", BindingFlags.Instance | BindingFlags.NonPublic);
                if (itemDataField != null)
                {
                    var itemData = itemDataField.GetValue(pbi);
                    if (itemData != null)
                    {
                        var idField = itemData.GetType().GetField("ID", BindingFlags.Instance | BindingFlags.Public);
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
            var pickups = Resources.FindObjectsOfTypeAll<CollectableItemPickup>()
                .Where(p => p.gameObject.scene == scene).ToList();
            var pickedKeys = _saveData?.GetPickedPickupKeys() ?? new HashSet<string>();
            foreach (var p in pickups)
            {
                var key = GetPickupKey(p);
                bool isPicked = pickedKeys.Contains(key);
                p.gameObject.SetActive(!isPicked);
            }
            Plugin.Log.LogInfo($"应用拾取点状态: 场景 {scene.name}, 共 {pickups.Count} 个点, 已捡 {pickedKeys.Count} 个");
        }

        public static void ApplyStateToSceneWithDelay(Scene scene, float delay = 0.2f)
        {
            if (!_isEnabled) return;
            ApplyStateToScene(scene);
            if (Plugin.Instance != null)
                Plugin.Instance.StartCoroutine(DelayedApply(scene, delay));
        }

        private static IEnumerator DelayedApply(Scene scene, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (SceneManager.GetActiveScene().name == scene.name)
            {
                ApplyStateToScene(scene);
                Plugin.Log.LogInfo("强制重生：延迟应用拾取点状态完成");
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
                Plugin.Log.LogInfo($"标记点为已捡: {key}");
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

        /// <summary>
        /// 单独传送回最后椅子（供外部需要时调用）
        /// </summary>
        public static void WarpToLastBench()
        {
            try
            {
                var pd = PlayerData.instance;
                if (pd == null) return;
                var sceneName = pd.respawnScene;
                if (string.IsNullOrEmpty(sceneName)) return;
                Plugin.Log.LogInfo($"[Warp] 传送至重生点: {sceneName}");
                var gm = GameManager.instance;
                gm.SaveGame(success =>
                {
                    if (success) gm.LoadGameFromUI(gm.profileID);
                });
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"传送失败: {ex}");
            }
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

        [HarmonyPatch(typeof(CollectableItemPickup), "DoPickupAction")]
        [HarmonyPrefix]
        private static void Prefix_DoPickupAction(CollectableItemPickup __instance, ref bool __runOriginal)
        {
            try
            {
                if (!__runOriginal || __instance == null) return;
                if (!_isEnabled) return;
                var originalItem = __instance.Item;
                if (originalItem == null || ItemRandomizer.ExcludedNames.Contains(originalItem.name)) return;
                var key = $"{__instance.gameObject.scene.name}_{__instance.transform.position.x:F1}_{__instance.transform.position.y:F1}_{__instance.transform.position.z:F1}";
                Plugin.AddDestroyedPickupKey(key);
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