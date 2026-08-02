using System;
using System.Collections.Generic;
using System.Linq;
using TeamCherry.Localization;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 日志（地方志/Lore 石碑）随机化逻辑。
    /// 目标是"直接交互即可阅读"的石碑/铭文日志（非拉琴 Needolin 类）。
    /// 显示链路复刻原生: PlayMaker RunDialogue -> DialogueBox.StartConversation，
    /// 无实体 NPC 时用 PlayMakerNPC.GetNewTemp 创建临时 NPC（与 RunDialogueBase.DoActionNoComponent 一致）。
    /// 已接入随机奖励池（LoreReward，作为普通有限奖励参与抽取）。
    /// </summary>
    public static class LoreRandomizer
    {
        /// <summary>直接交互石碑文本所在语言表</summary>
        public const string LoreSheet = "Lore";

        /// <summary>排除的 key 前缀（梦境/记忆类，非普通石碑）</summary>
        private static readonly string[] ExcludedPrefixes =
        {
            "SILK_HEART_MEMORY_",
        };

        /// <summary>
        /// 枚举 Lore 表中全部"普通日志" key（排除梦境/记忆类）
        /// </summary>
        public static List<string> GetLoreKeys()
        {
            var result = new List<string>();
            if (!Language.HasSheet(LoreSheet))
            {
                Plugin.Log.LogWarning($"[LoreRandomizer] 语言表 {LoreSheet} 不存在");
                return result;
            }

            foreach (var key in Language.GetKeys(LoreSheet))
            {
                if (ExcludedPrefixes.Any(p => key.StartsWith(p, StringComparison.Ordinal)))
                    continue;
                result.Add(key);
            }
            return result;
        }

        /// <summary>
        /// 从普通日志 key 列表中随机取一个（供随机奖励池使用）。无可用 key 时返回 null。
        /// </summary>
        public static string GetRandomLoreKey()
        {
            var keys = GetLoreKeys();
            if (keys.Count == 0)
            {
                Plugin.Log.LogWarning("[LoreRandomizer] 没有可用日志 key，跳过");
                return null;
            }
            return keys[UnityEngine.Random.Range(0, keys.Count)];
        }

        /// <summary>
        /// 把 Lore 表全部 key 及文本预览转储到控制台，便于人工筛选白名单
        /// </summary>
        public static void DumpLoreKeys()
        {
            var keys = GetLoreKeys();
            Plugin.Log.LogInfo($"[LoreRandomizer] === Lore 表共 {keys.Count} 个普通日志 key ===");
            foreach (var key in keys)
            {
                var text = Language.Get(key, LoreSheet) ?? string.Empty;
                var preview = text.Replace("\n", " ").Replace("\r", "");
                if (preview.Length > 40)
                    preview = preview.Substring(0, 40) + "...";
                Plugin.Log.LogInfo($"[LoreRandomizer] {key} => {preview}");
            }
        }

        /// <summary>
        /// 用原生石碑逻辑显示指定 key 的日志（DialogueBox 对话框，玩家按键翻页/关闭）
        /// </summary>
        public static void ShowLoreDialogue(string key, string sheet = LoreSheet)
        {
            var text = Language.Get(key, sheet);
            if (string.IsNullOrEmpty(text))
            {
                Plugin.Log.LogWarning($"[LoreRandomizer] 取不到文本: {sheet}/{key}");
                return;
            }

            // 复刻 RunDialogueBase.DoActionNoComponent: 临时 FSM 宿主 + 临时 PlayMakerNPC
            var host = new GameObject("LoreRandomizer_TempDialogue");
            var fsm = host.AddComponent<PlayMakerFSM>();
            var npc = PlayMakerNPC.GetNewTemp(fsm);
            if (npc == null)
            {
                Plugin.Log.LogError("[LoreRandomizer] 创建临时 PlayMakerNPC 失败");
                Object.Destroy(host);
                return;
            }

            npc.StartDialogueMove();

            void Cleanup()
            {
                try
                {
                    npc.ForceEndDialogue();
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[LoreRandomizer] 结束对话异常: {ex.Message}");
                }
                Object.Destroy(host, 1f);
            }

            // 复刻原生"检视石碑"排版（InteractEvents.OnStartDialogue）：
            // Alignment=Top（顶部居中）而非 Default 的 TopLeft，铭文观感更规整
            var displayOptions = new DialogueBox.DisplayOptions
            {
                Alignment = TMProOld.TextAlignmentOptions.Top,
                ShowDecorators = true,
                TextColor = Color.white,
            };

            DialogueBox.StartConversation(
                text,
                npc,
                overrideContinue: false,
                displayOptions,
                onDialogueEnd: Cleanup,
                onDialogueCancelled: () => Object.Destroy(host, 1f));

            Plugin.Log.LogInfo($"[LoreRandomizer] 显示日志: {sheet}/{key}");
        }
    }

    /// <summary>
    /// 作为随机奖励池的一份子：发放时随机挑一条"直接交互即可阅读"的普通日志，
    /// 用原生石碑对话框（DialogueBox）显示，玩家按键翻页/关闭。
    /// 属于信息类奖励，可重复发放（IsAtMax 永远为 false）。
    /// </summary>
    public class LoreReward : IRandomReward
    {
        public string Id => "virt:LoreLog";
        public string DisplayName => "随机日志";
        public Sprite Icon => null;

        public void Give()
        {
            try
            {
                var key = LoreRandomizer.GetRandomLoreKey();
                if (!string.IsNullOrEmpty(key))
                    LoreRandomizer.ShowLoreDialogue(key);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[LoreReward] 发放日志失败: {ex}");
            }
        }

        public bool IsAtMax() => false;
    }
}
