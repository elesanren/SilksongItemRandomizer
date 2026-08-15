// CrestRandomizePatch.cs - 修复后的完整版本
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 纹章解锁补丁：拦截 ToolCrest.Unlock，将纹章映射为目标纹章并解锁
    /// 改造后：通过 CrestRandomizer API 获取映射，通过接口记录已禁用教堂场景
    /// 保留所有原有功能：纹章替换、禁用教堂物体、记录已禁用场景持久化
    /// </summary>
    [HarmonyPatch(typeof(ToolCrest), "Unlock")]
    public static class CrestRandomizePatch
    {
        // 持久化接口：记录已禁用的教堂场景
        public interface ICrestPatchSaveDataAccessor
        {
            HashSet<string> GetDisabledChapels();
            void SaveDisabledChapels(HashSet<string> disabledChapels);
        }

        private static ICrestPatchSaveDataAccessor _saveData;
        private static List<ToolCrest> _allCrests;
        private static readonly HashSet<int> ProcessedInstanceIds = new();

        private static readonly string[] DisableKeywords = {
            "shrine", "crest", "church", "chapel", "door", "gate", "altar", "pedestal",
            "weaver", "bell", "bind", "orb", "rune", "memory", "statue", "pillar",
            "candle", "bench", "lever", "switch", "transition", "entrance", "exit"
        };

        public static void Initialize(ICrestPatchSaveDataAccessor accessor)
        {
            _saveData = accessor;
            _allCrests = Resources.FindObjectsOfTypeAll<ToolCrest>().ToList();
        }

        public static void ResetProcessedIds() => ProcessedInstanceIds.Clear();

        public static void ResetPersistentData()
        {
            if (_saveData != null)
            {
                _saveData.GetDisabledChapels().Clear();
                _saveData.SaveDisabledChapels(_saveData.GetDisabledChapels());
            }
            Plugin.Log.LogInfo("已清除教堂禁用持久化数据");
        }

        // 场景加载时禁用教堂物体（仅在开关开启时执行）
        public static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (!CrestRandomizer.IsEnabled) return;
            DisableChurchObjectsInScene(scene);
        }

        private static void DisableChurchObjectsInScene(Scene scene)
        {
            if (_saveData == null) return;
            if (!_saveData.GetDisabledChapels().Contains(scene.name)) return;

            Plugin.Log.LogInfo($"禁用场景 '{scene.name}' 中的教堂相关物体");
            int disabledCount = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
                {
                    string name = t.gameObject.name.ToLower();
                    if (DisableKeywords.Any(kw => name.Contains(kw)))
                    {
                        foreach (var c in t.GetComponents<Collider2D>()) c.enabled = false;
                        foreach (var c in t.GetComponents<Collider>()) c.enabled = false;
                        foreach (var fsm in t.GetComponents<PlayMakerFSM>()) fsm.enabled = false;
                        foreach (var anim in t.GetComponents<Animator>()) anim.enabled = false;
                        foreach (var rend in t.GetComponents<Renderer>()) rend.enabled = false;
                        disabledCount++;
                    }
                }
            }
            Plugin.Log.LogInfo($"共禁用 {disabledCount} 个物体");
        }

        [HarmonyPrefix]
        private static bool Prefix(ToolCrest __instance)
        {
            // 总开关关闭 → 直接放行原解锁
            if (!CrestRandomizer.IsEnabled)
            {
                return true;
            }

            // ★ 只在教堂类场景触发纹章随机替换，避免梦境/剧情等场景误触发（如 Hunter 升级链被当作新纹章映射）
            string activeScene = SceneManager.GetActiveScene().name;
            if (!activeScene.StartsWith("Chapel", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            try
            {
                int instanceId = __instance.GetInstanceID();
                if (ProcessedInstanceIds.Contains(instanceId)) return true;
                ProcessedInstanceIds.Add(instanceId);

                string sourceName = __instance.name;
                string targetName = CrestRandomizer.GetMappedCrestName(sourceName);
                if (string.IsNullOrEmpty(targetName) || targetName == sourceName)
                {
                    return true;
                }

                if (_allCrests == null || _allCrests.Count == 0)
                    _allCrests = Resources.FindObjectsOfTypeAll<ToolCrest>().ToList();
                ToolCrest targetCrest = _allCrests.FirstOrDefault(c => c.name == targetName);
                if (targetCrest == null)
                {
                    Plugin.Log.LogWarning($"无法找到目标纹章 {targetName}，放行原解锁");
                    return true;
                }

                CrestRandomizer.AddUnlockedCrest(targetName);

                // 临时禁用随机，然后解锁目标纹章
                CrestRandomizer.DisableRandom();
                targetCrest.Unlock();
                CrestRandomizer.EnableRandom();

                string currentScene = SceneManager.GetActiveScene().name;
                if (currentScene.Contains("Chapel") && _saveData != null && !_saveData.GetDisabledChapels().Contains(currentScene))
                {
                    _saveData.GetDisabledChapels().Add(currentScene);
                    _saveData.SaveDisabledChapels(_saveData.GetDisabledChapels());
                    Plugin.Log.LogInfo($"标记教堂场景 '{currentScene}'，下次进入时将禁用所有教堂物体");
                }

                Plugin.Log.LogInfo($"纹章替换: {sourceName} -> {targetName}");
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"CrestRandomizePatch异常: {ex}");
                return true;
            }
        }
    }
}