// BenchRespawnPatch.cs - 修复后的完整版本
using HarmonyLib;
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 重生（坐椅子）时纹章替换补丁
    /// 改造后：通过接口访问最后解锁的纹章，不再直接依赖 Plugin.SaveData
    /// 保留所有原有功能：延迟7秒替换、冷却间隔、重生后强制装备最后解锁的纹章
    /// </summary>
    [HarmonyPatch(typeof(HeroController), "SetBenchRespawn", new Type[] { typeof(RespawnMarker), typeof(string), typeof(int) })]
    public static class BenchRespawnPatch
    {
        // 纹章替换数据访问接口
        public interface IBenchRespawnSaveDataAccessor
        {
            string GetLastUnlockedCrest();
        }

        private static IBenchRespawnSaveDataAccessor _saveData;
        private static Coroutine _activeCoroutine = null;
        private static float _lastExecutionTime = 0f;
        private const float MinInterval = 10f;

        public static void Initialize(IBenchRespawnSaveDataAccessor accessor)
        {
            _saveData = accessor;
        }

        [HarmonyPostfix]
        private static void Postfix(HeroController __instance)
        {
            try
            {
                if (__instance == null || PlayerData.instance == null) return;
                if (_activeCoroutine != null) return;
                if (Time.time - _lastExecutionTime < MinInterval) return;

                string originalCrestId = PlayerData.instance.CurrentCrestID;
                if (string.IsNullOrEmpty(originalCrestId)) return;

                Plugin.Log.LogInfo($"重生触发: 延迟7秒处理纹章 {originalCrestId}");
                _activeCoroutine = Plugin.Instance.StartCoroutine(DelayedReplace(originalCrestId, __instance));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"BenchRespawnPatch异常: {ex}");
            }
        }

        private static IEnumerator DelayedReplace(string originalCrestId, HeroController hero)
        {
            yield return new WaitForSeconds(7f);
            _activeCoroutine = null;

            // 总开关关闭时 LastUnlockedCrest 会返回空字符串，直接跳过
            string targetCrest = _saveData?.GetLastUnlockedCrest() ?? "";
            if (string.IsNullOrEmpty(targetCrest))
            {
                Plugin.Log.LogInfo("纹章随机总开关关闭或无最后解锁记录，跳过重生替换");
                yield break;
            }

            Plugin.Log.LogInfo($"重生强制替换: {originalCrestId} -> {targetCrest}");
            typeof(ToolItemManager).GetMethod("SetEquippedCrest", new[] { typeof(string) })
                ?.Invoke(null, new object[] { targetCrest });
            hero?.ResetAllCrestState();

            yield return null;
            ToolItemManager.SendEquippedChangedEvent(true);

            _lastExecutionTime = Time.time;
            Plugin.Log.LogInfo($"替换完成，当前装备: {targetCrest}");
        }

        public static void ResetCooldown()
        {
            if (_activeCoroutine != null)
                _activeCoroutine = null;
            _lastExecutionTime = 0f;
        }
    }
}