// TryGetPatch.cs
using HarmonyLib;
using StartingAbilityPicker;
using System;
using System.Collections.Generic;
using TeamCherry.Localization;
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

        // 防重复：Architect 的 CustomPickup 会嵌套调用 DoPickupAction 两次，
        // 导致同帧内同一物品的 TryGet 被触发两次。记录已随机化的物品名，第二次直接跳过。
        private static readonly HashSet<string> _randomizedThisFrame = new();
        private static float _lastRandomizeFrameTime = -1f;

        [HarmonyPrefix]
        private static bool Prefix(SavedItem __instance, ref bool __result)
        {
            try
            {
                // 代理物品直接放行（由随机池内部创建）
                if (__instance is ProxySavedItem)
                    return true;

                // 旁路开关开启时，放行原逻辑
                if (BypassRandom)
                {
                    PreGeneratedMap.PendingKey = null;
                    return true;
                }

                // 总开关关闭时，放行原逻辑
                if (!SilksongItemRandomizerAPI.IsEnabled())
                {
                    PreGeneratedMap.PendingKey = null;
                    return true;
                }

                // 黑名单物品直接放行
                if (ItemRandomizer.ExcludedNames.Contains(__instance.name))
                {
                    PreGeneratedMap.PendingKey = null;
                    return true;
                }

                // 根据物品类型判断是否应该随机
                if (!ItemTypeRandomFilter.ShouldRandomize(__instance))
                {
                    PreGeneratedMap.PendingKey = null;
                    return true;
                }

                // ★ 帧内防重复：同帧同物品名已随机化过，直接跳过（针对 Architect 嵌套调用）
                // 必须先于 _isProcessing 检查，因为嵌套调用发生在 reward.Give() 过程中
                // 此时 _isProcessing 已为 true，若让 _isProcessing 先拦截会 return true 导致原物品被放行
                float now = Time.time;
                if (Mathf.Abs(now - _lastRandomizeFrameTime) > 0.001f)
                {
                    _randomizedThisFrame.Clear();
                    _lastRandomizeFrameTime = now;
                }
                if (_randomizedThisFrame.Contains(__instance.name))
                {
                    __result = true;
                    return false;
                }

                // 防止递归（在帧内防重之后，作为兜底）
                if (_isProcessing)
                    return true;

                string originalName = __instance.name;

                // ★ 丝轴/面具碎片类收集物被随机化替换时，标记原生碎片静默窗口：
                //   后续被驱动的碎片 UI 动画流程不播动画/不加碎片/不加上限，但正常走完不卡死。
                if (originalName.IndexOf("Spool", StringComparison.OrdinalIgnoreCase) >= 0
                    || string.Equals(originalName, "Heart Piece", StringComparison.OrdinalIgnoreCase))
                    SilkSpoolState.MarkNativeIntercept(5f);

                // ★ 预生成映射：拾取点（PickupPatch）已设置 PendingKey，命中则直接按表给予，
                // 不消耗随机池；其余来源（商店/任务等无点 key）仍走动态随机。
                IRandomReward reward = null;
                string preKey = PreGeneratedMap.PendingKey;
                if (!string.IsNullOrEmpty(preKey))
                {
                    PreGeneratedMap.PendingKey = null;
                    reward = PreGeneratedMap.ResolveReward(preKey);
                }

                // 获取随机奖励（预生成未命中时回退动态随机）
                if (reward == null)
                    reward = ItemRandomizer.GetRandomReward();
                if (reward == null)
                    return true;

                // ★ 重复能力：已满的能力类型（纹章/丝技能/工具/额外槽/四心/三旋律/丝之心）替换为 200 念珠
                if (reward.IsAtMax() && IsAbilityReward(reward))
                {
                    string displayName = reward.DisplayName;
                    Plugin.Log.LogInfo($"[TryGetPatch] 能力 {reward.Id} 已达上限，替换为 200 念珠");
                    reward = new VirtualReward(
                        "virt:DuplicateGeo",
                        $"{displayName} (200)",
                        null,
                        () => HeroController.instance?.AddGeo(200),
                        () => false);
                }

                // ★ 在 Give 之前标记已随机化，防止同帧嵌套调用再次触发
                _randomizedThisFrame.Add(originalName);

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

        /// <summary>判断奖励是否为 STA 弹窗覆盖的能力类型（纹章/丝技能/工具/额外槽/四心/三旋律/丝之心）。</summary>
        private static bool IsAbilityReward(IRandomReward reward)
        {
            if (reward is SavedItemReward savedReward && savedReward.Item != null)
            {
                var t = savedReward.Item.GetType();
                return t == typeof(ToolCrest) || t.IsSubclassOf(typeof(ToolCrest))
                    || t == typeof(ToolItemSkill) || t.IsSubclassOf(typeof(ToolItemSkill))
                    || t == typeof(ToolItem) || t.IsSubclassOf(typeof(ToolItem))
                    || t == typeof(ToolItemType) || t.IsSubclassOf(typeof(ToolItemType));
            }
            if (reward is VirtualReward virtualReward)
            {
                string id = virtualReward.Id;
                return id == "SilkHeart" || id == "Journal" || id == "ColdResist" || id == "Swim"
                    || id == "virt:HeartPiece" || id == "virt:SpoolPart";
            }
            return false;
        }
    }
}