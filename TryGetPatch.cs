// TryGetPatch.cs - 修复后的完整版本（添加调用栈追踪）
using HarmonyLib;
using System;
using UnityEngine;

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 物品获得补丁（TryGet 拦截）
    /// 改造后：通过 SilksongItemRandomizerAPI.IsEnabled() 和 ItemTypeRandomFilter 判断是否随机
    /// 保留所有原有功能：物品随机替换、黑名单过滤、次数记录、UI 通知等
    /// </summary>
    [HarmonyPatch(typeof(SavedItem), "TryGet")]
    public static class TryGetPatch
    {
        // 外部可设置的旁路标志（用于保底等场景，强制放行原物品）
        public static bool BypassRandom = false;
        private static bool _isProcessing = false;

        [HarmonyPrefix]
        private static bool Prefix(SavedItem __instance, ref bool __result)
        {
            // ========== ★★★ 新增：详细追踪日志（调用栈）★★★ ==========
            // 使用 LogError 级别，方便在控制台中高亮显示
            Plugin.Log.LogError($"========================================");
            Plugin.Log.LogError($"[TryGetPatch] === 调用开始 ===");
            Plugin.Log.LogError($"[TryGetPatch] 物品名称: {__instance?.name ?? "null"}");
            Plugin.Log.LogError($"[TryGetPatch] 物品类型: {__instance?.GetType().Name ?? "null"}");
            Plugin.Log.LogError($"[TryGetPatch] 时间戳: {Time.time:F3}");
            Plugin.Log.LogError($"[TryGetPatch] 调用栈:\n{Environment.StackTrace}");
            Plugin.Log.LogError($"========================================");

            try
            {
                // 代理物品直接放行（由随机池内部创建）
                if (__instance is ProxySavedItem)
                {
                    Plugin.Log.LogInfo($"[TryGetPatch] 代理物品，放行");
                    return true;
                }

                // 旁路开关开启时，放行原逻辑
                if (BypassRandom)
                {
                    Plugin.Log.LogInfo($"[TryGetPatch] BypassRandom 开启，放行原物品: {__instance.name}");
                    return true;
                }

                // 总开关关闭时，放行原逻辑
                if (!SilksongItemRandomizerAPI.IsEnabled())
                {
                    Plugin.Log.LogInfo($"[TryGetPatch] 物品随机总开关关闭，放行原物品: {__instance.name}");
                    return true;
                }

                // 黑名单物品直接放行
                if (ItemRandomizer.ExcludedNames.Contains(__instance.name))
                {
                    Plugin.Log.LogInfo($"[TryGetPatch] 黑名单物品 {__instance.name}，放行原版获得");
                    return true;
                }

                // 根据物品类型判断是否应该随机
                if (!ItemTypeRandomFilter.ShouldRandomize(__instance))
                {
                    Plugin.Log.LogInfo($"[TryGetPatch] 类型开关关闭，放行原物品: {__instance.name}");
                    return true;
                }

                // 防止递归
                if (_isProcessing)
                {
                    Plugin.Log.LogInfo($"[TryGetPatch] 检测到递归调用，放行原物品: {__instance.name}");
                    return true;
                }

                string originalName = __instance.name;

                // 获取随机奖励
                var reward = ItemRandomizer.GetRandomReward();
                if (reward == null)
                {
                    Plugin.Log.LogInfo($"[TryGetPatch] 没有可用奖励，放行原物品 {originalName}");
                    return true;
                }

                _isProcessing = true;
                try
                {
                    reward.Give();
                    __result = true;

                    // 记录获得次数和映射
                    ItemRandomizer.AddGivenCount(reward.Id);
                    ItemRandomizer.RecordMapping($"item:{originalName}", $"reward:{reward.Id}");

                    // 添加到 UI 显示
                    RecentItemsUI.AddItem(reward);

                    Plugin.Log.LogInfo($"[TryGetPatch] 获得: {reward.DisplayName} (已获得次数: {ItemRandomizer.GetGivenCount(reward.Id)})");
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[TryGetPatch] 给予奖励 {reward?.Id} 失败: {ex.Message}");
                    __result = false;
                }
                finally
                {
                    _isProcessing = false;
                }

                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[TryGetPatch] 异常: {ex}");
                return true;
            }
        }
    }
}