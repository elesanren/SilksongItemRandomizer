using HarmonyLib;
using HutongGames.PlayMaker.Actions;
using System.Collections;
using System;
using UnityEngine;

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

        public static void MarkNativeIntercept(float seconds = 8f)
        {
            NativeInterceptUntil = UnityEngine.Time.realtimeSinceStartup + seconds;
        }

        /// <summary>是否为「原生碎片获得被拦截」的静默窗口（且非自发）——此时碎片 UI 动画/写入/上限全部静默跳过。</summary>
        public static bool IsNativeInterceptActive => IsNativeIntercept && !IsSelfGiving && !Bypass;

        /// <summary>
        /// 最小必要恢复：仅解除导致"不能动/上升下落缓慢"的三项，不做暴力物理重置。
        /// 诊断确认：收集点对象（Heart Piece/Silk Spool）挂在 inputBlockers 上导致 inputBlocked=True，
        /// 面具 controlReqlinquished=True + gravityScale=0 未恢复。用公开 API 逐一解除。
        /// </summary>
        public static void MinimalRestore(string tag)
        {
            try
            {
                MinimalRestoreCore(tag);
                // 延迟 1.5s 二次兜底：场景收尾可能覆盖本次恢复，到时再补一次
                ScheduleDelayedRestore(tag);
            }
            catch (Exception e) { try { Plugin.Log.LogWarning("[Restore] MinimalRestore 异常: " + e); } catch { } }
        }

        private static void MinimalRestoreCore(string tag)
        {
                var hero = HeroController.instance;
                if (hero == null) return;
                // 1. 归还控制权（面具）
                if (hero.controlReqlinquished)
                {
                    hero.RegainControl();
                    Plugin.Log.LogInfo($"[Restore] {tag}: RegainControl");
                }
                // 2. 移除输入锁（面具/丝轴共用，blocker=收集点对象）
                try
                {
                    var field = typeof(HeroController).GetField("inputBlockers", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (field?.GetValue(hero) is System.Collections.Generic.HashSet<object> set && set.Count > 0)
                    {
                        var items = new System.Collections.Generic.List<object>(set);
                        foreach (var item in items)
                        {
                            hero.RemoveInputBlocker(item);
                        }
                        Plugin.Log.LogInfo($"[Restore] {tag}: 移除 {items.Count} 个输入锁");
                    }
                }
                catch (Exception e) { Plugin.Log.LogWarning($"[Restore] {tag}: 移除输入锁异常: " + e); }
                // 3. 恢复重力（面具）+ 同步 prevGravityScale（否则后续 AffectedByGravity(true) 会用 0 覆盖 → 下落缓慢）
                var rb = hero.Body;
                if (rb != null && rb.gravityScale != hero.DEFAULT_GRAVITY)
                {
                    try { hero.ResetGravity(); } catch { rb.gravityScale = hero.DEFAULT_GRAVITY; }
                    Plugin.Log.LogInfo($"[Restore] {tag}: 重置重力");
                }
                try
                {
                    var pf = typeof(HeroController).GetField("prevGravityScale", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (pf != null)
                    {
                        var pv = pf.GetValue(hero);
                        if (pv is float f && f != hero.DEFAULT_GRAVITY)
                        {
                            pf.SetValue(hero, hero.DEFAULT_GRAVITY);
                            Plugin.Log.LogInfo($"[Restore] {tag}: 修正 prevGravityScale {f} -> {hero.DEFAULT_GRAVITY}");
                        }
                    }
                }
                catch (Exception e) { Plugin.Log.LogWarning($"[Restore] {tag}: 修正 prevGravityScale 异常: " + e); }
        }

        /// <summary>
        /// 终止卡死在 "UI" 态的残留 Heart Container Control FSM（收集点场景侧演出对象）。
        /// 病理：静默流程跳过了 UI→场景侧的推进事件，该 FSM 永远停在 "UI" 态逐帧执行
        /// SetVelocity2d(0,-1.2)/SetGravity2dScale → 英雄悬浮、无惯性、下落极慢，切场景才恢复。
        /// 只终止 Active 且 ActiveStateName=="UI" 的，不碰正常流程。
        /// </summary>
        public static void StopLingeringPieceFsms(string tag)
        {
            try
            {
                var fsms = UnityEngine.Object.FindObjectsOfType<PlayMakerFSM>();
                foreach (var f in fsms)
                {
                    if (f == null || f.Fsm == null || !f.Fsm.Active) continue;
                    if (!string.Equals(f.FsmName, "Heart Container Control", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!string.Equals(f.ActiveStateName, "UI", StringComparison.OrdinalIgnoreCase)) continue;
                    Plugin.Log.LogWarning($"[Silent] {tag}: 终止残留演出FSM {f.gameObject.name}/{f.FsmName}@{f.ActiveStateName}");
                    try { f.Fsm.Stop(); } catch (Exception e) { Plugin.Log.LogWarning($"[Silent] Fsm.Stop 异常: {e.Message}"); }
                    try { f.enabled = false; } catch { }
                }
            }
            catch (Exception e) { try { Plugin.Log.LogWarning($"[Silent] {tag}: StopLingeringPieceFsms 异常: " + e); } catch { }
            }
        }

        private static bool _delayedScheduled = false;

        private static void ScheduleDelayedRestore(string tag)
        {
            if (_delayedScheduled) return;
            _delayedScheduled = true;
            try
            {
                var plugin = Plugin.Instance;
                if (plugin != null)
                    plugin.StartCoroutine(DelayedRestoreRoutine(tag));
            }
            catch { _delayedScheduled = false; }
        }

        private static System.Collections.IEnumerator DelayedRestoreRoutine(string tag)        {
            yield return new UnityEngine.WaitForSeconds(1.5f);
            _delayedScheduled = false;
            try { StopLingeringPieceFsms(tag + "延迟"); MinimalRestoreCore(tag + "延迟"); }
            catch (Exception e) { try { Plugin.Log.LogWarning("[Restore] 延迟恢复异常: " + e); } catch { } }
            // 兜底：再等 3s 终止可能迟到的残留演出 FSM
            yield return new UnityEngine.WaitForSeconds(3f);
            try { StopLingeringPieceFsms(tag + "核查"); }
            catch (Exception e) { try { Plugin.Log.LogWarning("[Restore] 残留FSM核查异常: " + e); } catch { } }
        }

        /// <summary>碎片获得共用 UI 的 FSM 名（丝轴/面具共用同一套 UI 流程，动画需一并静默）。
        /// 含满管演出对象（Silk Spool Instant / Heart Container Control / Clear Spool / Max Spool Event）：
        /// 满管演出与碎片 UI 同窗口启动，动作不静默则会播放动画 + 卡流程。</summary>
        public static readonly string[] PieceUiFsmNames = {
            "Silk Spool UI", "Heart Container UI",
            "Heart Container Control", "Clear Spool", "Max Spool", "Silk Spool Instant"
        };

        /// <summary>判断某个 PlayMakerFSM 名是否属于碎片获得 UI。</summary>
        public static bool IsPieceUiFsm(string fsmName)
        {
            if (string.IsNullOrEmpty(fsmName)) return false;
            foreach (var n in PieceUiFsmNames)
                if (string.Equals(fsmName, n, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>碎片演出对象名表（创建时即拦，不让动画入场）。满格（丝轴）/满管（面具）共用。</summary>
        public static readonly string[] PieceShowObjectNames = {
            "Silk Spool UI", "Heart Container UI",
            "Silk Spool Instant", "Heart Container Control",
            "Max Spool Event", "Clear Spool"
        };

        /// <summary>
        /// 碎片获得演出对象创建时开窗（仅标记窗口，不阻止创建）。
        /// 与半格原型一致：动画对象正常创建入场，窗口内动作被静默跳过 → 内容被跳、
        /// 流程正常走到结束（不会卡死，随机奖励照发）。满格共用同一逻辑。
        /// 自发（SelfGiving/Bypass）不开窗，原生动画正常播放。
        /// </summary>
        public static void MarkWindowOnShowCreate(UnityEngine.GameObject prefab, string via)
        {
            try
            {
                if (prefab == null) return;
                var name = prefab.name;
                bool hit = false;
                if (name.IndexOf("Silk Spool", StringComparison.OrdinalIgnoreCase) >= 0) hit = true;
                else if (name.IndexOf("Heart Container", StringComparison.OrdinalIgnoreCase) >= 0) hit = true;
                if (!hit) return;
                if (Bypass || IsSelfGiving) return; // 自发不开窗，原生动画正常播
                MarkNativeIntercept();
                try { Plugin.Log.LogWarning($"[Silent] 碎片演出入场(仅开窗不拦创建): {name} @ {via} → 窗口内动作静默跳过，流程正常走完"); } catch { }
            }
            catch (Exception e) { try { Plugin.Log.LogWarning("[Silent] MarkWindowOnShowCreate 异常: " + e); } catch { } }
            _ = via;
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
                MinimalRestore("给奖后");
            }
            finally
            {
                _isGiving = false;
            }
            // ★ 销毁记录兜底：本路径（写字段拦截）未经过 PrefabCollectable.Get/TryGet 入口，
            // 场景级 key 与 Plugin.DestroyMarkedPickups 的 killAllSpools/killAllHearts 匹配，
            // 重进场景整场景销毁碎片点，防重生。
            try
            {
                var activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                if (!string.IsNullOrEmpty(activeScene.name))
                {
                    string sceneKey = string.Equals(canonical, "heartPieces", StringComparison.OrdinalIgnoreCase)
                        ? $"heartpiecescene:{activeScene.name}"
                        : $"spoolscene:{activeScene.name}";
                    Plugin.AddDestroyedPickupKey(sceneKey);
                    Plugin.Log.LogInfo($"[SpoolPart] {canonical}@{via} 场景级销毁标记已写入: {sceneKey}");
                }
            }
            catch (Exception e) { try { Plugin.Log.LogWarning("[SpoolPart] 场景级销毁标记异常: " + e); } catch { } }
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
            SilkSpoolState.MarkWindowOnShowCreate(__instance?.gameObject?.Value, "CreateObjectV2");
        }
    }

    [HarmonyPatch(typeof(HutongGames.PlayMaker.Actions.CreateObject), "OnEnter")]
    internal static class CreateObject_OnEnter_Patch
    {
        [HarmonyPrefix]
        private static void Prefix(HutongGames.PlayMaker.Actions.CreateObject __instance)
        {
            SilkSpoolState.MarkWindowOnShowCreate(__instance?.gameObject?.Value, "CreateObject");
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

    // ==================== 重力锁定拦截 ====================
    // 满管/满格演出（Heart Container Control Get 态等）会 SetGravity2dScale(0?) 悬浮角色做演出，
    // 恢复重力动作在后续 SendEventByName 里——而我们窗口内静默 SendEventByName → 恢复永远不来
    // → 演出(已被拦)结束后角色重力仍异常。修复：窗口内直接静默 SetGravity2dScale（演出本被跳，
    // 悬浮无意义），让重力根本不被改；流程正常走完由原生收尾态恢复。

    [HarmonyPatch(typeof(HutongGames.PlayMaker.Actions.SetGravity2dScale), "OnEnter")]
    internal static class SetGravity2dScale_OnEnter_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(HutongGames.PlayMaker.Actions.SetGravity2dScale __instance, ref bool __runOriginal)
        {
            return SilkSpoolState.TrySilentSkip(__instance, ref __runOriginal);
        }
    }

    [HarmonyPatch(typeof(HutongGames.PlayMaker.Actions.SetGravity2dScaleV2), "OnEnter")]
    internal static class SetGravity2dScaleV2_OnEnter_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(HutongGames.PlayMaker.Actions.SetGravity2dScaleV2 __instance, ref bool __runOriginal)
        {
            return SilkSpoolState.TrySilentSkip(__instance, ref __runOriginal);
        }
    }

    // ==================== 丝轴满管（第2块）Wait 态推进 ====================
    // 根因：Silk Spool UI 的 "Full" 态 CallMethodProper(HeroController.AddToMaxSilk) 被拦截
    // （不得加上限），导致满管演出（Heart Container Control → 完成后发 SPOOL MAX UP ENDED）
    // 从未启动 → Full 态后的 "Wait" 态（PlayerDataVariableTest IsAnyCursed）在 IsAnyCursed=true
    // 时永远等不到 SPOOL MAX UP ENDED → UI FSM 永久卡在 Wait 态（角色无敌/锁输入）。
    // 修复：窗口内且处于 Silk Spool UI 的 Wait 态时，跳过 IsAnyCursed 判定并直接补发 SPOOL MAX UP ENDED
    // 推进到 Full End，让 FSM 正常走完（Full End → Hero Anim End → Return Control → 还原控制权）。

    [HarmonyPatch(typeof(HutongGames.PlayMaker.Actions.PlayerDataVariableTest), "OnEnter")]
    internal static class PlayerDataVariableTest_OnEnter_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(HutongGames.PlayMaker.Actions.PlayerDataVariableTest __instance, ref bool __runOriginal)
        {
            try
            {
                if (!SilkSpoolState.IsNativeInterceptActive) return true;
                if (__instance?.Fsm == null || !SilkSpoolState.IsPieceUiFsm(__instance.Fsm.Name)) return true;
                if (!string.Equals(__instance.Fsm.ActiveStateName, "Wait", StringComparison.OrdinalIgnoreCase)) return true;
                // Wait 态：跳过 IsAnyCursed 判定，直接发满管结束事件推进
                Plugin.Log.LogWarning($"[Silent] Silk Spool UI Wait 态：补发 SPOOL MAX UP ENDED 推进（满管演出已拦）");
                __instance.Fsm.Event("SPOOL MAX UP ENDED");
                __instance.Finish();
                __runOriginal = false;
                return false;
            }
            catch (Exception e) { try { Plugin.Log.LogWarning("[Silent] PlayerDataVariableTest 异常: " + e); } catch { } }
            return true;
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

    // 满管（第2块）时 Silk Spool UI Full 态 SendEventToRegister 会把满管事件发给
    // Heart Container Control（面具/丝轴共用满管演出），演出一旦启动其动画全靠 Full 演出的
    // SetPlayerDataVariable/Animator 完成——但演出被我们拦（超时/上限），演出中途卡死。
    // 窗口内静默该事件，防满管演出被错误启动。
    // 例外：Destroy Self 态（收尾清理）的 SendEventToRegister 必须放行——事件=SILK SPOOL UI END，
    // 场景丝轴点 FSM 靠它收尾销毁 UI 对象（触发 DISABLE 系统事件 → State 1 → RemoveHeroInputBlocker
    // 解锁输入）。此态等于「播放完毕」，顺带立即强制恢复角色状态。
    [HarmonyPatch(typeof(HutongGames.PlayMaker.Actions.SendEventToRegister), "OnEnter")]
    internal static class SendEventToRegister_OnEnter_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(HutongGames.PlayMaker.Actions.SendEventToRegister __instance, ref bool __runOriginal)
        {
            try
            {
                if (!SilkSpoolState.IsNativeInterceptActive) return true;
                if (__instance?.Fsm != null && SilkSpoolState.IsPieceUiFsm(__instance.Fsm.Name))
                {
                    string state = __instance.Fsm.ActiveStateName;
                    if (string.Equals(state, "Destroy Self", StringComparison.OrdinalIgnoreCase))
                    {
                        // 收尾事件放行（场景靠它销毁 UI 解锁输入）+ 最小必要恢复（解除输入锁/控制权/重力）
                        try { Plugin.Log.LogInfo("[Silent] Destroy Self 态收尾事件放行"); SilkSpoolState.StopLingeringPieceFsms("DestroySelf"); SilkSpoolState.MinimalRestore("DestroySelf"); } catch { }
                        return true;
                    }
                }
                return SilkSpoolState.TrySilentSkip(__instance, ref __runOriginal);
            }
            catch (Exception e) { try { Plugin.Log.LogWarning("[Silent] SendEventToRegister 异常: " + e); } catch { } }
            return true;
        }
    }

    [HarmonyPatch(typeof(HutongGames.PlayMaker.Actions.SendEventByName), "OnEnter")]
    internal static class SendEventByName_OnEnter_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(HutongGames.PlayMaker.Actions.SendEventByName __instance, ref bool __runOriginal)
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

    // ==================== 面具（Heart Container UI）输入锁链静默 ====================
    // 根因：面具收集流程 Init Phase 2 态 AddHeroInputBlocker 锁输入 → 走到 Wait For Grounded
    // 检测落地（CheckIsCharacterGrounded）→ 若角色未落地则循环等待 → Continue Deposit 的
    // RemoveHeroInputBlocker 永不执行 → 输入被永久锁死 → 玩家移动/跳跃被锁，表现为下降上升缓慢。
    // 方案（与丝轴一致：流程正常走完但内容跳过）：窗口内直接跳过 AddHeroInputBlocker（不锁输入）、
    // 跳过 CheckIsCharacterGrounded（直接发落地事件推进）、跳过 RemoveHeroInputBlocker（配对不误删）。

    [HarmonyPatch(typeof(HutongGames.PlayMaker.Actions.AddHeroInputBlocker), "OnEnter")]
    internal static class AddHeroInputBlocker_OnEnter_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(HutongGames.PlayMaker.Actions.AddHeroInputBlocker __instance, ref bool __runOriginal)
        {
            try
            {
                if (!SilkSpoolState.IsNativeInterceptActive) return true;
                if (__instance?.Fsm == null || !SilkSpoolState.IsPieceUiFsm(__instance.Fsm.Name)) return true;
                // 内容被跳过，无需锁输入（源头解决锁死/移动缓慢）
                Plugin.Log.LogWarning($"[Silent] 跳过 AddHeroInputBlocker: FSM={__instance.Fsm.Name}");
                __instance.Finish();
                __runOriginal = false;
                return false;
            }
            catch (Exception e) { try { Plugin.Log.LogWarning("[Silent] AddHeroInputBlocker 异常: " + e); } catch { } }
            return true;
        }
    }

    [HarmonyPatch(typeof(CheckIsCharacterGrounded), "OnEnter")]
    internal static class CheckIsCharacterGrounded_OnEnter_Patch
    {
        [HarmonyPrefix]
        private static bool Prefix(CheckIsCharacterGrounded __instance, ref bool __runOriginal)
        {
            try
            {
                if (!SilkSpoolState.IsNativeInterceptActive) return true;
                if (__instance?.Fsm == null || !SilkSpoolState.IsPieceUiFsm(__instance.Fsm.Name)) return true;
                // 内容被跳过（角色状态未受演出影响），直接判定已落地推进流程，防止卡在等待落地
                if (__instance.GroundedEvent != null)
                    __instance.Fsm.Event(__instance.GroundedEvent);
                if (__instance.StoreResult != null)
                    __instance.StoreResult.Value = true;
                __instance.Finish();
                __runOriginal = false;
                return false;
            }
            catch (Exception e) { try { Plugin.Log.LogWarning("[Silent] CheckIsCharacterGrounded 异常: " + e); } catch { } }
            return true;
        }
    }
}


namespace SilksongItemRandomizer
{
    /// <summary>
    /// 灵丝获得/消耗随机化补丁
    /// 改造后：通过 SilksongItemRandomizerAPI 的总开关 + 独立开关（SilkRandomEnabled，默认关闭）双重控制
    /// 保留所有原有功能：获得量随机（90%几率1，10%几率2-9），消耗量按权重随机（1-9，分布偏大值）
    /// </summary>
    [HarmonyPatch(typeof(PlayerData))]
    public static class SilkRandomizerPatch
    {
        private static System.Random _rng;

        private static readonly int[] CostWeights = new int[9] { 1, 1, 1, 1, 2, 2, 2, 2, 2 };

        private static void EnsureRng()
        {
            if (_rng != null) return;
            var seed = SilksongItemRandomizerAPI.GetSeed();
            _rng = new System.Random(seed ^ 0x5A5A5A5A);
        }

        private static int GetRandomGainAmount()
        {
            if (_rng.NextDouble() < 0.9)
                return 1;
            else
                return _rng.Next(2, 10);
        }

        private static int GetRandomCostAmount()
        {
            int total = 0;
            foreach (int w in CostWeights) total += w;
            int roll = _rng.Next(total);
            int cumulative = 0;
            for (int i = 0; i < CostWeights.Length; i++)
            {
                cumulative += CostWeights[i];
                if (roll < cumulative) return i + 1;
            }
            return 5;
        }

        [HarmonyPatch(nameof(PlayerData.AddSilk))]
        [HarmonyPrefix]
        private static bool PrefixAddSilk(PlayerData __instance, ref int amount)
        {
            if (!SilksongItemRandomizerAPI.IsEnabled()) return true;
            if (!SilksongItemRandomizerAPI.IsSilkRandomEnabled()) return true;
            EnsureRng();
            amount = GetRandomGainAmount();
            return true;
        }

        [HarmonyPatch(nameof(PlayerData.TakeSilk))]
        [HarmonyPrefix]
        private static bool PrefixTakeSilk(PlayerData __instance, ref int amount)
        {
            if (!SilksongItemRandomizerAPI.IsEnabled()) return true;
            if (!SilksongItemRandomizerAPI.IsSilkRandomEnabled()) return true;
            EnsureRng();
            amount = GetRandomCostAmount();
            return true;
        }
    }
}

// SilkSpearPityPatch.cs - 修复后的完整版本

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 丝矛保底补丁：每获得 N 个物品（排除丝矛自身）后强制给予丝矛
    /// 改造后：通过接口访问持久化数据，不再直接依赖 Plugin.SaveData
    /// 保留所有原有功能：计数、保底触发、原生弹窗、回满血丝等
    /// </summary>
    [HarmonyPatch(typeof(SavedItem), "TryGet")]
    public static class SilkSpearPityPatch
    {
        // ========== 持久化数据访问接口 ==========
        public interface ISilkSpearSaveDataAccessor
        {
            int GetTryGetCount();
            void SetTryGetCount(int count);
            bool GetSilkSpearGiven();
            void SetSilkSpearGiven(bool given);
            int GetPityCount();
            void SetPityCount(int count);   // 保底触发次数阈值
        }

        private static ISilkSpearSaveDataAccessor _saveData;

        // ========== 初始化 ==========
        public static void Initialize(ISilkSpearSaveDataAccessor saveDataAccessor)
        {
            _saveData = saveDataAccessor;
            if (_saveData != null)
            {
                Plugin.Log.LogInfo($"[丝矛保底] 加载状态: 已给予={_saveData.GetSilkSpearGiven()}, 当前计数={_saveData.GetTryGetCount()}");
            }
        }

        public static void ResetSilkSpearState()
        {
            if (_saveData != null)
            {
                _saveData.SetTryGetCount(0);
                _saveData.SetSilkSpearGiven(false);
                Plugin.Log.LogInfo("[丝矛保底] 保底状态已重置");
            }
        }

        // ========== Harmony 补丁 ==========
        [HarmonyPostfix]
        private static void Postfix(SavedItem __instance, bool __result)
        {
            if (!__result) return;
            if (_saveData == null) return;
            if (_saveData.GetSilkSpearGiven()) return;

            // 排除丝矛自身
            if (__instance.name == "Silk Spear") return;

            int newCount = _saveData.GetTryGetCount() + 1;
            _saveData.SetTryGetCount(newCount);
            int requiredCount = _saveData.GetPityCount();

            if (newCount < requiredCount) return;

            Plugin.Log.LogInfo($"[丝矛保底] 保底触发（第{newCount}次物品获得）");

            GiveSilkSpearWithNativePopup();

            _saveData.SetSilkSpearGiven(true);
        }

        private static void GiveSilkSpearWithNativePopup()
        {
            try
            {
                // 直接走 SkillRandomizer 官方给予流程（弹窗/装配/法术槽），
                // 不再做任何槽位解锁初始化（EnsureCrestSlots 曾把全部槽位解锁，属过度处理已移除）
                StartingAbilityPicker.StartingAbilityPickerAPI.GiveSkill("hasNeedleThrow");
                Plugin.Instance.StartCoroutine(DelayedHealAndSilk());
                Plugin.Log.LogInfo("[丝矛保底] 已通过 SkillRandomizer 给予丝矛（官方弹窗），5秒后回满血丝");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[丝矛保底] 给予丝矛失败: {ex}");
            }
        }

        private static IEnumerator DelayedHealAndSilk()
        {
            yield return new WaitForSeconds(5f);
            ForceFullHealAndSilk();
        }

        public static void ForceFullHealAndSilk()
        {
            try
            {
                var hero = HeroController.instance;
                var pd = PlayerData.instance;
                if (hero == null || pd == null)
                {
                    Plugin.Log.LogWarning("[丝矛保底] HeroController 或 PlayerData 为空，无法回满");
                    return;
                }

                int healthNeeded = pd.CurrentMaxHealth - pd.health;
                if (healthNeeded > 0)
                {
                    for (int i = 0; i < healthNeeded; i++)
                        hero.AddHealth(1);
                    Plugin.Log.LogInfo($"[丝矛保底] 已回满血量 (+{healthNeeded})");
                }

                int silkNeeded = pd.CurrentSilkMax - pd.silk;
                if (silkNeeded > 0)
                {
                    for (int i = 0; i < silkNeeded; i++)
                        hero.AddSilk(1, false);
                    Plugin.Log.LogInfo($"[丝矛保底] 已回满丝线 (+{silkNeeded})");
                }

                if (healthNeeded <= 0 && silkNeeded <= 0)
                {
                    EventRegister.SendEvent(EventRegisterEvents.HealthUpdate, null);
                    GameCameras.instance?.silkSpool?.RefreshSilk();
                    Plugin.Log.LogInfo("[丝矛保底] 血量/丝线已满，仅刷新UI");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[丝矛保底] ForceFullHealAndSilk 异常: {ex}");
            }
        }
    }
}