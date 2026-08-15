// BenchRespawnPatch.cs - 重生（坐椅子）时纹章替换补丁
// 双通道事件驱动版：
//   1) SetBenchRespawn（坐椅/死亡存档）登记待办并立即启动"稳定即换"协程；
//   2) FinishedEnteringScene（入场/趴地演出结束）作为快速通道加速执行。
// 忆境守卫：忆境中或忆境恢复窗口内直接取消（不在忆境中换装、不与恢复链冲突）。
// 演出守卫：控制权被接管或过渡中顺延，待稳定后再执行（兜底 180 帧）。
using HarmonyLib;
using System;
using System.Collections;
using UnityEngine;

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 重生（坐椅子）时纹章替换补丁
    /// 改造后：通过接口访问最后解锁的纹章，不再直接依赖 Plugin.SaveData
    /// 保留所有原有功能：冷却间隔、重生后强制装备最后解锁的纹章
    /// </summary>
    [HarmonyPatch]
    public static class BenchRespawnPatch
    {
        // 纹章替换数据访问接口
        public interface IBenchRespawnSaveDataAccessor
        {
            string GetLastUnlockedCrest();
        }

        private static IBenchRespawnSaveDataAccessor _saveData;
        private static float _lastExecutionTime = 0f;
        private const float MinInterval = 10f;
        private const int MaxRetryFrames = 180;
        private static string _pendingCrestId = null;
        private static Coroutine _retryCoroutine = null;

        public static void Initialize(IBenchRespawnSaveDataAccessor accessor)
        {
            _saveData = accessor;
        }

        [HarmonyPatch(typeof(HeroController), "SetBenchRespawn",
            new Type[] { typeof(RespawnMarker), typeof(string), typeof(int) })]
        [HarmonyPostfix]
        private static void SetBenchRespawnPostfix(HeroController __instance)
        {
            try
            {
                if (__instance == null || PlayerData.instance == null) return;
                if (Time.time - _lastExecutionTime < MinInterval) return;

                string originalCrestId = PlayerData.instance.CurrentCrestID;
                if (string.IsNullOrEmpty(originalCrestId)) return;

                _pendingCrestId = originalCrestId;
                Plugin.Log.LogInfo($"重生触发: 已登记待替换纹章 {originalCrestId}，等待稳定窗口执行");
                TryStart();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"BenchRespawnPatch异常: {ex}");
            }
        }

        [HarmonyPatch(typeof(HeroController), "FinishedEnteringScene",
            new[] { typeof(bool), typeof(bool) })]
        [HarmonyPostfix]
        private static void FinishedEnteringScenePostfix(HeroController __instance)
        {
            TryExecutePending(__instance);
        }

        internal static void TryExecutePending(HeroController hero)
        {
            try
            {
                if (hero == null || string.IsNullOrEmpty(_pendingCrestId)) return;

                GameManager gm = GameManager.instance;
                PlayerData playerData = PlayerData.instance;
                if (gm == null || playerData == null) return;

                // 忆境守卫：正处于忆境或忆境状态存档未恢复时，直接取消（不在忆境中换装、不与恢复链冲突）
                if (gm.IsMemoryScene() || playerData.HasStoredMemoryState)
                {
                    CancelPending("当前处于忆境/忆境恢复窗口，取消重生纹章替换");
                    return;
                }

                // 演出守卫：控制权被接管或场景过渡中，交由重试协程顺延
                if (hero.controlReqlinquished || hero.cState.transitioning)
                {
                    TryStart();
                    return;
                }

                DoReplace(hero);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"BenchRespawnPatch.TryExecutePending异常: {ex}");
            }
        }

        private static void TryStart()
        {
            if (_retryCoroutine != null) return;
            if (string.IsNullOrEmpty(_pendingCrestId)) return;
            _retryCoroutine = Plugin.Instance.StartCoroutine(RetryUntilStable());
        }

        private static IEnumerator RetryUntilStable()
        {
            for (int i = 0; i < MaxRetryFrames && !string.IsNullOrEmpty(_pendingCrestId); i++)
            {
                yield return null;
                GameManager gm = GameManager.instance;
                PlayerData playerData = PlayerData.instance;
                if (gm == null || playerData == null) break;
                if (gm.IsMemoryScene() || playerData.HasStoredMemoryState)
                {
                    CancelPending("等待期间进入忆境/忆境恢复窗口，取消重生纹章替换");
                    break;
                }
                HeroController hero = HeroController.instance;
                if (hero == null) break;
                if (hero.controlReqlinquished || hero.cState.transitioning) continue;
                DoReplace(hero);
                break;
            }
            if (!string.IsNullOrEmpty(_pendingCrestId))
            {
                Plugin.Log.LogWarning("重生纹章替换等待超时，放弃本次替换");
                _pendingCrestId = null;
            }
            _retryCoroutine = null;
        }

        private static void DoReplace(HeroController hero)
        {
            string originalCrestId = _pendingCrestId;
            _pendingCrestId = null;

            // 总开关关闭时 LastUnlockedCrest 会返回空字符串，直接跳过
            string targetCrest = _saveData?.GetLastUnlockedCrest() ?? "";
            if (string.IsNullOrEmpty(targetCrest))
            {
                Plugin.Log.LogInfo("纹章随机总开关关闭或无最后解锁记录，跳过重生替换");
                return;
            }

            Plugin.Log.LogInfo($"重生强制替换: {originalCrestId} -> {targetCrest}");
            ToolItemManager.SetEquippedCrest(targetCrest);
            hero.ResetAllCrestState();

            ToolItemManager.SendEquippedChangedEvent(true);

            _lastExecutionTime = Time.time;
        }

        private static void CancelPending(string reason)
        {
            _pendingCrestId = null;
        }

        public static void ResetCooldown()
        {
            _pendingCrestId = null;
            _lastExecutionTime = 0f;
        }
    }
}