// SilkSpoolPartsPatch.cs - 丝轴/面具碎片获得拦截（全链路 C# 底层拦截，单文件）
//
// 关键链路（自资产导出确认，勿改勿删）：
//   1) 丝轴碎片 silkSpoolParts 真实写入链：Silk Spool UI FSM "Increase Silk" 态
//        IntOperator(+1 本地 Spool Parts) -> SetPlayerDataVariable("silkSpoolParts")
//      该动作 OnEnter 调 TeamCherry.SharedUtils.VariableExtensions.SetVariable 反射直写字段，
//      不经过 PlayerData 方法 → 必须拦 SetPlayerDataVariable.OnEnter（兜底 1）。
//   2) 面具碎片 heartPieces 真实写入链：PlayerData.IncrementInt（兜底 2）。
//   3) 丝轴与面具世界收集点共用同一 UI prefab（FSM 名 "Silk Spool UI"，m_PathID=-2208192167316267631）：
//      收集点被随机化替换后，UI 动画流程仍会被驱动 → 需在静默窗口内跳过动画/写入/上限，且 FSM 正常走完。
//   4) 自发（NativePickupGiver 置 Bypass/MarkSelfGiving）→ 全部放行，原生判定播对应动画。
using System;
using HarmonyLib;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace SilksongItemRandomizer
{
    /// <summary>丝轴/面具碎片拦截共享状态。</summary>
    public static class SilkSpoolState
    {
        /// <summary>旁路：virt:SpoolPart/virt:HeartPiece 发放时放行原生碎片计数写入。</summary>
        public static bool Bypass = false;

        /// <summary>自发发放的放行窗口（覆盖异步流程，防 Get 后延迟回调被拦）。</summary>
        public static float SelfGiveUntil = -1f;

        public static bool IsSelfGiving => UnityEngine.Time.realtimeSinceStartup < SelfGiveUntil;

        public static void MarkSelfGiving(float seconds = 30f) => SelfGiveUntil = UnityEngine.Time.realtimeSinceStartup + seconds;

        /// <summary>原生碎片获得被拦截的静默窗口：此窗口内 UI 动画/写入/上限全部静默跳过，但 FSM 正常走完不卡死。</summary>
        /// <remarks>8 秒足以覆盖面具 UI 全流程（Darken→Fleur→Get N→Piece Fade→Check Max→Fuse→Max Up→Fade，动画被静默后仅剩 Wait 计时）。
        /// 自发发放（F4/随机奖励）有 30 秒 SelfGive 窗口 + Bypass 双重保护，不受此窗口影响。</remarks>
        public static float NativeInterceptUntil = -1f;

        public static bool IsNativeIntercept => UnityEngine.Time.realtimeSinceStartup < NativeInterceptUntil;

        public static void MarkNativeIntercept(float seconds = 8f) => NativeInterceptUntil = UnityEngine.Time.realtimeSinceStartup + seconds;

        /// <summary>是否为「原生碎片获得被拦截」的静默窗口（且非自发）——此时碎片 UI 动画/写入/上限全部静默跳过。</summary>
        public static bool IsNativeInterceptActive => IsNativeIntercept && !IsSelfGiving && !Bypass;

        /// <summary>碎片获得共用 UI 的 FSM 名（丝轴/面具共用同一套 UI 流程，动画需一并静默）。</summary>
        public static readonly string[] PieceUiFsmNames = { "Silk Spool UI", "Heart Container UI" };

        /// <summary>判断某个 PlayMakerFSM 名是否属于碎片获得 UI。</summary>
        public static bool IsPieceUiFsm(string fsmName)
        {
            if (string.IsNullOrEmpty(fsmName)) return false;
            foreach (var n in PieceUiFsmNames)
                if (string.Equals(fsmName, n, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>窗口内静默跳过动作：显式 Finish（被跳过 OnEnter 的动作不 Finish 会让 PlayMaker 认为动作仍在运行 → 状态不转换 → 卡死）+ 跳过原方法。</summary>
        public static bool SilentSkip(HutongGames.PlayMaker.FsmStateAction action, ref bool __runOriginal)
        {
            try
            {
                if (action?.Fsm?.ActiveStateName != null)
                    Plugin.Log.LogWarning($"[Silent] 跳过: FSM={action.Fsm.Name} 状态={action.Fsm.ActiveStateName} 动作={action.GetType().Name}");
            }
            catch { }
            action.Finish();
            __runOriginal = false;
            return false;
        }

        /// <summary>窗口内 + 碎片UI FSM 守卫（命中则静默跳过）。</summary>
        public static bool TrySilentSkip(HutongGames.PlayMaker.FsmStateAction action, ref bool __runOriginal)
        {
            try
            {
                if (!IsNativeInterceptActive) return true;
                if (action?.Fsm == null || !IsPieceUiFsm(action.Fsm.Name)) return true;
                return SilentSkip(action, ref __runOriginal);
            }
            catch (Exception e) { try { Plugin.Log.LogWarning("[Silent] 动作异常: " + e); } catch { } }
            return true;
        }

        private static bool _isGiving = false;

        /// <summary>命中拦截字段则返回规范名，否则返回 null（放行）。</summary>
        public static string GetInterceptedField(string fieldName)
        {
            if (fieldName == null) return null;
            if (string.Equals(fieldName, "silkSpoolParts", StringComparison.OrdinalIgnoreCase))
                return "silkSpoolParts";
            if (string.Equals(fieldName, "heartPieces", StringComparison.OrdinalIgnoreCase))
                return "heartPieces";
            return null;
        }

        public static bool ShouldIntercept(string fieldName)
        {
            return
                !_isGiving &&
                !Bypass &&
                !IsSelfGiving &&
                SilksongItemRandomizerAPI.IsEnabled() &&
                GetInterceptedField(fieldName) != null;
        }

        // 同字段双写去重（Increment/Set 双路径会先后写同一字段，只发一次奖励）
        private static string _lastGiveField = null;
        private static float _lastGiveTime = -1f;

        public static bool IsDeduped(string fieldName)
        {
            string canonical = GetInterceptedField(fieldName) ?? fieldName;
            if (string.Equals(_lastGiveField, canonical, StringComparison.OrdinalIgnoreCase) &&
                UnityEngine.Time.realtimeSinceStartup - _lastGiveTime < 3f)
                return true;
            _lastGiveField = canonical;
            _lastGiveTime = UnityEngine.Time.realtimeSinceStartup;
            return false;
        }

        public static bool GiveRandom(string via, string fieldName)
        {
            string canonical = GetInterceptedField(fieldName) ?? fieldName;
            string itemField = string.Equals(canonical, "heartPieces", StringComparison.OrdinalIgnoreCase)
                ? "item:Heart Piece"
                : "item:Silk Spool";
            Plugin.Log.LogInfo($"[SpoolPart] 检测到世界碎片写入（{canonical}@{via}），拦截并改为随机奖励");

            var reward = ResolveSequential(canonical);
            if (reward == null)
                reward = ItemRandomizer.GetRandomReward();
            if (reward == null) return false;

            _isGiving = true;
            try
            {
                reward.Give();
                ItemRandomizer.AddGivenCount(reward.Id);
                ItemRandomizer.RecordMapping(itemField, "reward:" + reward.Id);
                RecentItemsUI.AddItem(reward);
                Plugin.Log.LogInfo($"[SpoolPart] {canonical}@{via} 触发点给予: {reward.DisplayName}");
            }
            finally
            {
                _isGiving = false;
            }
            return true;
        }

        /// <summary>按顺序从预生成映射取面具/丝轴奖励，命中则递增计数器。</summary>
        private static IRandomReward ResolveSequential(string canonical)
        {
            var save = Plugin.SaveData;
            if (save == null) return null;
            if (string.Equals(canonical, "heartPieces", StringComparison.OrdinalIgnoreCase))
            {
                var r = PreGeneratedMap.ResolveSequentialReward("heart", save.HeartSeq + 1);
                if (r != null) { save.HeartSeq++; Plugin.SaveGlobalData(); }
                return r;
            }
            if (string.Equals(canonical, "silkSpoolParts", StringComparison.OrdinalIgnoreCase))
            {
                var r = PreGeneratedMap.ResolveSequentialReward("spool", save.SpoolSeq + 1);
                if (r != null) { save.SpoolSeq++; Plugin.SaveGlobalData(); }
                return r;
            }
            return null;
        }

        /// <summary>统一命中判定+执行逻辑，避免各 patch 重复。命中原生碎片时开启静默窗口。</summary>
        public static bool TryIntercept(string prefix, string name, ref bool __runOriginal)
        {
            if (GetInterceptedField(name) == null) return true;
            // 原生碎片获得被拦 → 开启静默窗口（后续 UI 动画/上限静默跳过，自发时 IsNativeInterceptActive 为 false 不受影响）
            if (!IsNativeInterceptActive)
                MarkNativeIntercept();
            if (!ShouldIntercept(name)) return true;
            if (IsDeduped(name))
            {
                // 3 秒内同字段二次命中 = UI 内部补写/清零（风险例：Heart Container UI Check Max 会 IncrementPlayerDataInt 补写 heartPieces，
                // 不拦则碎片进度被 UI 虚增到 4/4 却无合成，与丝轴上"半满+上限虚增"同源）。
                // 静默拦下（不写数据、不再发奖励），UI 流程本身正常走完不卡。
                __runOriginal = false;
                return false;
            }
            if (GiveRandom(prefix, name))
            {
                __runOriginal = false;
                return false; // 跳过原方法
            }
            return true;
        }
    }

    // ==================== 兜底 1：FSM 反射直写动作入口（丝轴真实写入链） ====================

    [HarmonyPatch(typeof(SetPlayerDataVariable), "OnEnter")]
    internal static class SetPlayerDataVariable_OnEnter_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(SetPlayerDataVariable __instance, ref bool __runOriginal)
        {
            try
            {
                var variableName = __instance?.VariableName?.Value;
                if (!string.IsNullOrEmpty(variableName))
                {
                    string canonical = SilkSpoolState.GetInterceptedField(variableName);
                    if (canonical != null)
                    {
                        SilkSpoolState.TryIntercept("[SetPlayerDataVariable]", variableName, ref __runOriginal);
                        if (!__runOriginal)
                        {
                            // ★ 必须显式 Finish：跳过原方法后其原生 OnEnter 末尾的 Finish() 不会执行，
                            //   PlayMaker 认为动作仍在运行 → UI FSM 永久卡死在 Part 态 →
                            //   原生恢复链（Destroy Self 的 isInvincible=false / Return Control 的 RegainControl /
                            //   RemoveHeroInputBlocker）永不执行 → 角色永久无敌且技能/法术输入被锁。
                            __instance.Finish();
                            return false;
                        }
                    }
                }
            }
            catch (Exception e) { try { Plugin.Log.LogWarning("[Intercept] SetPlayerDataVariable.OnEnter 异常: " + e); } catch { } }
            return true;
        }
    }

    // ========== 兜底 2：面具（heartPieces）真实链路 ==========

    [HarmonyPatch(typeof(PlayerData), "IncrementInt")]
    public static class PlayerData_IncrementInt_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(PlayerData __instance, string intName, ref bool __runOriginal)
        {
            try { if (__instance == PlayerData.instance) { return SilkSpoolState.TryIntercept("[IncrementInt]", intName, ref __runOriginal); } }
            catch (Exception e) { try { Plugin.Log.LogWarning("[Intercept] IncrementInt prefix 异常: " + e); } catch { } }
            return true;
        }
    }

    // ==================== 原生碎片点被拦截时：静默完成共用 UI（Silk Spool UI）动画流程 ====================
    // 世界丝轴/面具收集点被随机化替换后，共用 UI（FSM 名 "Silk Spool UI" / "Heart Container UI"）仍会被驱动走动画流程。
    // 静默窗口内：不播动画、不加碎片、不加上限，但各等待动作立即 Finish 让 FSM 正常走完，不卡死。
    // 性能：窗口外全部立即放行（仅一次属性访问），字符串匹配只在窗口内执行。
    // 时序关键：丝轴在 Get 时（写入时）已开窗，动画在其后；但面具场景 FSM「先创建 UI 播动画、后写 heartPieces」，
    // 动画先于窗口发生 → 必须在创建碎片 UI 的瞬间（CreateObjectV2/CreateObject）预开静默窗口，动画才会落在窗口内。

    [HarmonyPatch(typeof(HutongGames.PlayMaker.Actions.CreateObjectV2), "OnEnter")]
    internal static class CreateObjectV2_OnEnter_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(HutongGames.PlayMaker.Actions.CreateObjectV2 __instance)
        {
            try
            {
                if (SilkSpoolState.Bypass || SilkSpoolState.IsSelfGiving) return;
                var go = __instance?.gameObject?.Value;
                if (go == null) return;
                if (go.name.IndexOf("Silk Spool UI", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    go.name.IndexOf("Heart Container UI", StringComparison.OrdinalIgnoreCase) >= 0)
                    SilkSpoolState.MarkNativeIntercept();
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(HutongGames.PlayMaker.Actions.CreateObject), "OnEnter")]
    internal static class CreateObject_OnEnter_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(HutongGames.PlayMaker.Actions.CreateObject __instance)
        {
            try
            {
                if (SilkSpoolState.Bypass || SilkSpoolState.IsSelfGiving) return;
                var go = __instance?.gameObject?.Value;
                if (go == null) return;
                if (go.name.IndexOf("Silk Spool UI", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    go.name.IndexOf("Heart Container UI", StringComparison.OrdinalIgnoreCase) >= 0)
                    SilkSpoolState.MarkNativeIntercept();
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(HutongGames.PlayMaker.Actions.IntOperator), "OnEnter")]
    internal static class IntOperator_OnEnter_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(HutongGames.PlayMaker.Actions.IntOperator __instance, ref bool __runOriginal)
        {
            try
            {
                if (!SilkSpoolState.IsNativeInterceptActive) return true;
                // 只拦碎片 UI FSM 里写入 "Spool Parts" 的 +1，避免本地碎片变量被污染
                if (__instance?.Fsm == null || !SilkSpoolState.IsPieceUiFsm(__instance.Fsm.Name)) return true;
                if (__instance?.storeResult == null || !string.Equals(__instance.storeResult.Name, "Spool Parts", StringComparison.OrdinalIgnoreCase)) return true;
                return SilkSpoolState.SilentSkip(__instance, ref __runOriginal);
            }
            catch (Exception e) { try { Plugin.Log.LogWarning("[Silent] IntOperator 异常: " + e); } catch { } }
            return true;
        }
    }

    [HarmonyPatch(typeof(HutongGames.PlayMaker.Actions.SetAnimator), "OnEnter")]
    internal static class SetAnimator_OnEnter_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(HutongGames.PlayMaker.Actions.SetAnimator __instance, ref bool __runOriginal)
        {
            try
            {
                if (!SilkSpoolState.IsNativeInterceptActive) return true;
                if (__instance?.Fsm == null || !SilkSpoolState.IsPieceUiFsm(__instance.Fsm.Name)) return true;
                return SilkSpoolState.SilentSkip(__instance, ref __runOriginal);
            }
            catch (Exception e) { try { Plugin.Log.LogWarning("[Silent] SetAnimator 异常: " + e); } catch { } }
            return true;
        }
    }

    [HarmonyPatch(typeof(HutongGames.PlayMaker.Actions.AnimatorPlayStateWait), "OnEnter")]
    internal static class AnimatorPlayStateWait_OnEnter_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(HutongGames.PlayMaker.Actions.AnimatorPlayStateWait __instance, ref bool __runOriginal)
        {
            try
            {
                if (!SilkSpoolState.IsNativeInterceptActive) return true;
                if (__instance?.Fsm == null || !SilkSpoolState.IsPieceUiFsm(__instance.Fsm.Name)) return true;
                // 不播放、不等待，直接结束该动作，让 FSM 继续推进
                __instance.Finish();
                __runOriginal = false;
                return false;
            }
            catch (Exception e) { try { Plugin.Log.LogWarning("[Silent] AnimatorPlayStateWait 异常: " + e); } catch { } }
            return true;
        }
    }

    [HarmonyPatch(typeof(HutongGames.PlayMaker.Actions.ListenForAnimationEvent), "OnEnter")]
    internal static class ListenForAnimationEvent_OnEnter_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(HutongGames.PlayMaker.Actions.ListenForAnimationEvent __instance, ref bool __runOriginal)
        {
            try
            {
                if (!SilkSpoolState.IsNativeInterceptActive) return true;
                if (__instance?.Fsm == null || !SilkSpoolState.IsPieceUiFsm(__instance.Fsm.Name)) return true;
                // 不等动画事件，直接发出响应事件并结束（驱动后续状态推进）
                if (__instance.Response != null)
                    __instance.Fsm.Event(__instance.Response);
                __instance.Finish();
                __runOriginal = false;
                return false;
            }
            catch (Exception e) { try { Plugin.Log.LogWarning("[Silent] ListenForAnimationEvent 异常: " + e); } catch { } }
            return true;
        }
    }

    // ==================== 面具 UI 全流程静默（Heart Container UI 中间过程全部跳掉） ====================
    // 依据导出确认的 Heart Container UI 动作清单：Tk2dPlayAnimation(9) Tk2dPlayAnimationWithEvents(3) Wait(11)
    // EaseColor(7) Tk2dSpriteSetColor(8) SetMeshRenderer(4) iTweenMoveTo/ScaleTo(各2) AudioPlayerOneShotSingle(10)
    // PlayParticleEmitter/StopParticleEmitter(各2) 等。
    // 原则（与丝轴一致）：窗口内（IsNativeInterceptActive + IsPieceUiFsm）把会出视觉/卡流程/发响应的动作
    // 全部静默跳过；被跳过的动作必须显式 Finish（否则 PlayMaker 认为动作未完成 → 状态不转换 → 卡死），
    // 带事件回调的动作（Tk2dPlayAnimationWithEvents）补发配置事件驱动后续状态，FSM 其余逻辑正常走完。

    [HarmonyPatch(typeof(HutongGames.PlayMaker.Actions.Tk2dPlayAnimationWithEvents), "OnEnter")]
    internal static class Tk2dPlayAnimationWithEvents_OnEnter_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(HutongGames.PlayMaker.Actions.Tk2dPlayAnimationWithEvents __instance, ref bool __runOriginal)
        {
            try
            {
                if (!SilkSpoolState.IsNativeInterceptActive) return true;
                if (__instance?.Fsm == null || !SilkSpoolState.IsPieceUiFsm(__instance.Fsm.Name)) return true;
                // 动画事件型等待：补发配置的事件（trigger + complete）驱动后续状态，再 Finish，防卡死
                if (__instance.animationTriggerEvent != null)
                    __instance.Fsm.Event(__instance.animationTriggerEvent);
                if (__instance.animationCompleteEvent != null)
                    __instance.Fsm.Event(__instance.animationCompleteEvent);
                return SilkSpoolState.SilentSkip(__instance, ref __runOriginal);
            }
            catch (Exception e) { try { Plugin.Log.LogWarning("[Silent] Tk2dPlayAnimationWithEvents 异常: " + e); } catch { } }
            return true;
        }
    }

    [HarmonyPatch(typeof(HutongGames.PlayMaker.Actions.Wait), "OnEnter")]
    internal static class Wait_OnEnter_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(HutongGames.PlayMaker.Actions.Wait __instance, ref bool __runOriginal)
        {
            // 关键：Wait 超时后原逻辑会 Finish() + Fsm.Event(finishEvent)（如 Fuse/Tween Mover
            // 的转场事件 "WAIT"）。被拦后若不补发，FSM 会永远停在等待该事件 → 流程走不完。
            // 跳过 = 时间立即流逝 → 补发 finishEvent 与原逻辑完全等价。
            try
            {
                if (SilkSpoolState.IsNativeInterceptActive
                    && __instance?.Fsm != null && SilkSpoolState.IsPieceUiFsm(__instance.Fsm.Name)
                    && __instance.finishEvent != null)
                    __instance.Fsm.Event(__instance.finishEvent);
            }
            catch (Exception e) { try { Plugin.Log.LogWarning("[Silent] Wait 补发事件异常: " + e); } catch { } }
            return SilkSpoolState.TrySilentSkip(__instance, ref __runOriginal);
        }
    }

    [HarmonyPatch(typeof(HutongGames.PlayMaker.Actions.EaseColor), "OnEnter")]
    internal static class EaseColor_OnEnter_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(HutongGames.PlayMaker.Actions.EaseColor __instance, ref bool __runOriginal)
        {
            try
            {
                if (SilkSpoolState.IsNativeInterceptActive
                    && __instance?.Fsm != null && SilkSpoolState.IsPieceUiFsm(__instance.Fsm.Name)
                    && __instance.finishEvent != null)
                    __instance.Fsm.Event(__instance.finishEvent);
            }
            catch (Exception e) { try { Plugin.Log.LogWarning("[Silent] EaseColor 补发事件异常: " + e); } catch { } }
            return SilkSpoolState.TrySilentSkip(__instance, ref __runOriginal);
        }
    }

    [HarmonyPatch(typeof(HutongGames.PlayMaker.Actions.Tk2dSpriteSetColor), "OnEnter")]
    internal static class Tk2dSpriteSetColor_OnEnter_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(HutongGames.PlayMaker.Actions.Tk2dSpriteSetColor __instance, ref bool __runOriginal)
        {
            return SilkSpoolState.TrySilentSkip(__instance, ref __runOriginal);
        }
    }

    [HarmonyPatch(typeof(HutongGames.PlayMaker.Actions.SetMeshRenderer), "OnEnter")]
    internal static class SetMeshRenderer_OnEnter_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(HutongGames.PlayMaker.Actions.SetMeshRenderer __instance, ref bool __runOriginal)
        {
            return SilkSpoolState.TrySilentSkip(__instance, ref __runOriginal);
        }
    }

    [HarmonyPatch(typeof(HutongGames.PlayMaker.Actions.iTweenMoveTo), "OnEnter")]
    internal static class iTweenMoveTo_OnEnter_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(HutongGames.PlayMaker.Actions.iTweenMoveTo __instance, ref bool __runOriginal)
        {
            return SilkSpoolState.TrySilentSkip(__instance, ref __runOriginal);
        }
    }

    [HarmonyPatch(typeof(HutongGames.PlayMaker.Actions.iTweenScaleTo), "OnEnter")]
    internal static class iTweenScaleTo_OnEnter_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(HutongGames.PlayMaker.Actions.iTweenScaleTo __instance, ref bool __runOriginal)
        {
            return SilkSpoolState.TrySilentSkip(__instance, ref __runOriginal);
        }
    }

    [HarmonyPatch(typeof(HutongGames.PlayMaker.Actions.AudioPlayerOneShotSingle), "OnEnter")]
    internal static class AudioPlayerOneShotSingle_OnEnter_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(HutongGames.PlayMaker.Actions.AudioPlayerOneShotSingle __instance, ref bool __runOriginal)
        {
            return SilkSpoolState.TrySilentSkip(__instance, ref __runOriginal);
        }
    }

    [HarmonyPatch(typeof(HutongGames.PlayMaker.Actions.PlayParticleEmitter), "OnEnter")]
    internal static class PlayParticleEmitter_OnEnter_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(HutongGames.PlayMaker.Actions.PlayParticleEmitter __instance, ref bool __runOriginal)
        {
            return SilkSpoolState.TrySilentSkip(__instance, ref __runOriginal);
        }
    }

    [HarmonyPatch(typeof(HutongGames.PlayMaker.Actions.StopParticleEmitter), "OnEnter")]
    internal static class StopParticleEmitter_OnEnter_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(HutongGames.PlayMaker.Actions.StopParticleEmitter __instance, ref bool __runOriginal)
        {
            return SilkSpoolState.TrySilentSkip(__instance, ref __runOriginal);
        }
    }

[HarmonyPatch(typeof(HutongGames.PlayMaker.Actions.Tk2dPlayAnimation), "OnEnter")]
    internal static class Tk2dPlayAnimation_OnEnter_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(HutongGames.PlayMaker.Actions.Tk2dPlayAnimation __instance, ref bool __runOriginal)
        {
            try
            {
                if (!SilkSpoolState.IsNativeInterceptActive) return true;
                if (__instance?.Fsm == null || !SilkSpoolState.IsPieceUiFsm(__instance.Fsm.Name)) return true;
                // 拦 tk2d 碎片动画（面具 Heart Container UI 用 Tk2dPlayAnimation 播碎片获得动画）
                // 必须显式 Finish：被跳过 OnEnter 的动作不 Finish 会让 PlayMaker 认为动作仍在运行 → 状态卡死
                __instance.Finish();
                __runOriginal = false;
                return false;
            }
            catch (Exception e) { try { Plugin.Log.LogWarning("[Silent] Tk2dPlayAnimation 异常: " + e); } catch { } }
            return true;
        }
    }

    [HarmonyPatch(typeof(HeroController), "AddToMaxSilk")]
    internal static class HeroController_AddToMaxSilk_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(HeroController __instance, ref bool __runOriginal)
        {
            try
            {
                // 原生丝轴点被拦截：不上限（静默窗口内）；自发放行
                if (SilkSpoolState.IsNativeInterceptActive)
                {
                    __runOriginal = false;
                    return false;
                }
                return true;
            }
            catch (Exception e) { try { Plugin.Log.LogWarning("[Silent] AddToMaxSilk 异常: " + e); } catch { } }
            return true;
        }
    }

    [HarmonyPatch(typeof(HeroController), "AddToMaxHealth")]
    internal static class HeroController_AddToMaxHealth_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(HeroController __instance, ref bool __runOriginal)
        {
            try
            {
                // 原生面具点被拦截：上限不加（静默窗口内）；自发真碎片合成（SelfGive/Bypass）放行
                if (SilkSpoolState.IsNativeInterceptActive)
                {
                    __runOriginal = false;
                    return false;
                }
                return true;
            }
            catch (Exception e) { try { Plugin.Log.LogWarning("[Silent] AddToMaxHealth 异常: " + e); } catch { } }
            return true;
        }
    }
}
