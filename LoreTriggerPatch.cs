// LoreTriggerPatch.cs
using System;
using System.Collections.Generic;
using HarmonyLib;

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 检测游戏内「检查」交互（石碑等）触发一次性随机奖励。
    /// 挂钩 NPCControlBase.StartDialogue —— 所有交互类型共用的 funnel。
    /// 判定完全基于「交互键类型」：InteractLabel == PromptLabels.Inspect，不检测内容。
    /// 命中后：发放随机奖励，并跳过原交互内容（不显示原文本），直接结束交互 ——
    /// 即「随机替换」：用奖励替换原本的石碑/检查内容。
    /// 每个地点（交互物）只触发一次，按「场景名 + 物体名」持久化到存档。
    /// 受「物品随机总开关」SilksongItemRandomizerAPI.IsEnabled() 控制。
    /// 我们自己的 LoreRandomizer.ShowLoreDialogue 走 DialogueBox 字符串重载，不经过 StartDialogue，不会自触发。
    /// </summary>
    [HarmonyPatch(typeof(NPCControlBase), "StartDialogue")]
    public static class LoreTriggerPatch
    {
        private const string TriggerIdPrefix = "loretrig:";

        // 不触发奖励的交互物：功能性机器（钟 / 念珠机），触发会结束交互、打断其本身功能，故排除
        private static readonly HashSet<string> ExcludedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bell_toll_machine",     // 钟
            "rosary_string_machine"  // 念珠机
        };

        /// <summary>是否为收费机类排除名（供 CheckPointScanner 使用）</summary>
        public static bool IsExcludedTollName(string name) => name != null && ExcludedNames.Contains(name);

        [HarmonyPrefix]
        private static bool Prefix(NPCControlBase __instance)
        {
            try
            {
                if (__instance == null)
                    return true;
                if (!SilksongItemRandomizerAPI.IsEnabled())
                    return true;
                if (!ItemLimitConfig.EnableLoreTrigger)
                    return true;

                // 仅「检查 / inspect」交互键触发，不检测内容
                if (__instance.InteractLabel != InteractableBase.PromptLabels.Inspect)
                    return true;

                // 功能性机器不触发（保持原功能可用）
                if (ExcludedNames.Contains(__instance.gameObject.name))
                    return true;

                // 每个地点（交互物）只触发一次，跟随存档持久化
                string locationId = $"{__instance.gameObject.scene.name}:{__instance.gameObject.name}";
                string triggerId = TriggerIdPrefix + locationId;
                if (ItemRandomizer.GetGivenCount(triggerId) > 0)
                {
                    // 已触发过：禁用交互，不再显示原内容（正常不会被调用到——
                    // 场景加载时已 Deactivate，这里是兜底）
                    __instance.Deactivate(false);
                    return false;
                }

                // ★ 预生成映射优先：进入场景时已从随机池摸好结果，直接按表给予；
                // 跳过 LoreReward——发放它会再弹一个 DialogueBox，可能打断阅读
                string triggerKey = $"lore:{locationId}";
                // 全量映射已覆盖所有 Lore 键，直接按表给予
                IRandomReward reward = PreGeneratedMap.ResolveReward(triggerKey);

                // 兜底：预生成不可用（如奖励池为空）时回退现有动态抽取
                if (reward == null)
                {
                    for (int i = 0; i < 10; i++)
                    {
                        var candidate = ItemRandomizer.GetRandomReward();
                        if (candidate == null)
                            break;
                        if (candidate is LoreReward)
                            continue;
                        reward = candidate;
                        break;
                    }
                }
                if (reward == null)
                {
                    Plugin.Log.LogWarning($"[LoreTriggerPatch] 无可用奖励，跳过检查触发: {locationId}");
                    return true; // 无奖励则正常显示原内容
                }

                reward.Give();

                // 触发成功后才标记，避免奖励池为空时白白消耗一次机会
                ItemRandomizer.AddGivenCount(triggerId);
                ItemRandomizer.AddGivenCount(reward.Id);
                ItemRandomizer.RecordMapping($"lore:{locationId}", $"reward:{reward.Id}");
                RecentItemsUI.AddItem(reward);

                Plugin.Log.LogInfo($"[LoreTriggerPatch] 检查交互 {locationId} 触发奖励: {reward.DisplayName}");

                // 已发放奖励：禁用该交互物（此后不可再交互），并跳过原交互内容
                __instance.Deactivate(false);
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[LoreTriggerPatch] 异常: {ex}");
                return true; // 异常时回退到原交互，避免卡死
            }
        }

        /// <summary>
        /// 场景加载时调用（Plugin.OnSceneLoaded）：
        /// 把本场景已触发过的交互物重新 Deactivate（场景重载后组件状态会复位）。
        /// 已触发记录存在 ItemGivenCounts 里，种子重置（ResetAllData）后自动恢复。
        /// </summary>
        public static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene)
        {
            if (!SilksongItemRandomizerAPI.IsEnabled()) return;
            if (!ItemLimitConfig.EnableLoreTrigger) return;
            if (!ItemRandomizer.IsInitialized) return;
            if (Plugin.Instance == null) return;
            Plugin.Instance.StartCoroutine(DisableTriggeredInteractablesDelayed(scene));
        }

        private static System.Collections.IEnumerator DisableTriggeredInteractablesDelayed(
            UnityEngine.SceneManagement.Scene scene)
        {
            // 等场景内 FSM 初始化完再禁用，避免被其自身逻辑重新激活
            yield return new UnityEngine.WaitForSeconds(0.2f);
            try
            {
                int disabled = 0;
                foreach (var npc in UnityEngine.Resources.FindObjectsOfTypeAll<NPCControlBase>())
                {
                    if (npc == null || npc.gameObject.scene != scene) continue;
                    if (npc.InteractLabel != InteractableBase.PromptLabels.Inspect) continue;
                    if (ExcludedNames.Contains(npc.gameObject.name)) continue;

                    string locationId = $"{scene.name}:{npc.gameObject.name}";
                    if (ItemRandomizer.GetGivenCount(TriggerIdPrefix + locationId) <= 0) continue;

                    npc.Deactivate(false);
                    disabled++;
                }
                if (disabled > 0)
                    Plugin.Log.LogInfo($"[LoreTriggerPatch] 场景 {scene.name} 已禁用 {disabled} 个已触发交互物");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[LoreTriggerPatch] 场景重扫异常: {ex}");
            }
        }
    }
}
