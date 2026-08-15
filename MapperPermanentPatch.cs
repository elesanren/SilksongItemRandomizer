using HarmonyLib;
using UnityEngine;

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 沙克拉（Mapper）商人常驻补丁。
    ///
    /// 设计原则：一律用 Postfix 在游戏原生逻辑执行完后再把字段拉回"在场"值，
    /// 绝不拦截 Prefix（拦截会让 evaluate_location 等 FSM 状态机收不到事件而中断，
    /// 导致"点交互没反应"）。所有对话/商店/任务流程完全原样运行。
    ///
    /// 封堵的 C# 写入路径（均为直接字段赋值，不经 PlayMaker action）：
    ///   1) GameManager.TimePasses()        → mapperAway（50% 概率置 true，商人 AWAY 不可交互）
    ///   2) GameManager.MapperLeavePreviousLocations(seenBool)
    ///   3) SceneTravelerTempEval.OnEnter   → MapperLeft*
    ///   4) PlayerData.MapperLeaveAll()     → 14 区全置 true
    /// 以及场景隐藏组件 DeactivateIfPlayerdataTrue（MapperLeft* 为 true 时 SetActive(false)）。
    ///
    /// 结构：后置回写类嵌套于此统一管理，注册由 SilksongItemRandomizerAPI.AlwaysOnPatchTypes 逐类 PatchAll。
    /// </summary>
    public static class MapperPermanentPatch
    {
        /// <summary>一次性兜底重置：清存档残留的离开/暂离标记（不打断任何流程）。</summary>
        public static void ForceResetMapperFields()
        {
            var pd = PlayerData.instance;
            if (pd == null) return;

            try { pd.mapperAway = false; } catch { }

            var fields = typeof(PlayerData).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            foreach (var fi in fields)
            {
                if (fi.FieldType != typeof(bool)) continue;
                if (!(fi.Name.StartsWith("MapperLeft") || fi.Name.StartsWith("SeenMapper"))) continue;
                try { fi.SetValue(pd, false); } catch { }
            }
        }

        /// <summary>
        /// 根因：TimePasses 每次 50% 概率把 mapperAway 置 true（非
        /// Bonetown/Belltown），商人 FSM 读到即走 AWAY 分支 → 商人可见但点交互没反应。
        /// Postfix 拉回，不打断 TimePasses 其余逻辑。
        /// </summary>
        [HarmonyPatch(typeof(GameManager), "TimePasses")]
        public static class TimePassesPatch
        {
            [HarmonyPostfix]
            private static void Postfix() => ForceResetMapperFields();
        }

        /// <summary>
        /// MapperLeavePreviousLocations（被 evaluate_location FSM 的 CallMethodProper 调用）：
        /// Postfix 把被置 true 的 MapperLeft* 拉回，CallMethodProper 的 FINISHED 事件照常发出。
        /// </summary>
        [HarmonyPatch(typeof(GameManager), "MapperLeavePreviousLocations")]
        public static class MapperLeavePrevPatch
        {
            [HarmonyPostfix]
            private static void Postfix() => ForceResetMapperFields();
        }

        /// <summary>
        /// SceneTravelerTempEval.OnEnter：评估当前区域并置 MapperLeft*、
        /// 发 LeftEvent/StillHereEvent。Postfix 拉回字段，事件链不被中断（避免对话 FSM 卡死）。
        /// </summary>
        [HarmonyPatch("HutongGames.PlayMaker.Actions.SceneTravelerTempEval", "OnEnter")]
        public static class SceneTravelerEvalPatch
        {
            [HarmonyPostfix]
            private static void Postfix() => ForceResetMapperFields();
        }

        /// <summary>
        /// MapperLeaveAll（TimePasses 终局分支调用）：Postfix 全量拉回。
        /// </summary>
        [HarmonyPatch(typeof(PlayerData), "MapperLeaveAll")]
        public static class MapperLeaveAllPatch
        {
            [HarmonyPostfix]
            private static void Postfix() => ForceResetMapperFields();
        }

        /// <summary>
        /// 场景加载后一次性兜底（清存档残留），无高频循环。
        /// </summary>
        [HarmonyPatch(typeof(GameManager), "FinishedEnteringScene")]
        public static class ResetOnEnterPatch
        {
            [HarmonyPostfix]
            private static void Postfix() => ForceResetMapperFields();
        }
    }

    /// <summary>
    /// 场景隐藏组件兜底：DeactivateIfPlayerdataTrue 因 MapperLeft* 判 true 时跳过隐藏并强制激活。
    /// </summary>
    [HarmonyPatch(typeof(DeactivateIfPlayerdataTrue), "ForceEvaluate")]
    public static class DeactivateIfPlayerdataTruePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(DeactivateIfPlayerdataTrue __instance)
        {
            try
            {
                if (string.IsNullOrEmpty(__instance.boolName)) return true;
                if (!__instance.boolName.StartsWith("MapperLeft")) return true;

                var target = __instance.objectToDeactivate != null
                    ? __instance.objectToDeactivate
                    : __instance.gameObject;
                if (target != null && !target.activeSelf)
                    target.SetActive(true);
                return false;
            }
            catch { }
            return true;
        }
    }
}