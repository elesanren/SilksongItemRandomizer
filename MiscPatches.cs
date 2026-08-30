using GlobalEnums;
using HarmonyLib;
using System.Collections.Generic;
using System.Collections;
using System.Reflection;
using System;
using UnityEngine;


namespace SilksongItemRandomizer
{
    /// <summary>
    /// 拦截第三方 mod FsmMaster 在 GameManager 未就绪（启动/场景切换空窗期）时
    /// 每帧访问 GameManager.instance 导致的 "Couldn't find a Game Manager" 刷屏。
    /// 只跳过空窗期的 TryUnlockUiInput，GameManager 就绪后 FsmMaster 功能不受影响。
    /// 通过反射加载 FsmMaster 类型，不引入编译依赖；FsmMaster 缺失/加载晚于本插件时自动跳过。
    /// </summary>
    public static class FsmMasterGuard
    {
        private static bool _installed;
        private static bool _givingUp;
        private static int _tickCount;

        public static void Tick()
        {
            if (_installed || _givingUp) return;
            if (++_tickCount < 3) return; // 等插件全部加载完再尝试

            TryInstall();
        }

        private static void TryInstall()
        {
            try
            {
                Type fsmType = Type.GetType("FsmMaster.FsmMasterPlugin, FsmMaster");
                if (fsmType == null)
                {
                    _givingUp = true; // FsmMaster 未安装，永久跳过
                    return;
                }

                MethodInfo target = fsmType.GetMethod("TryUnlockUiInput",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (target == null)
                {
                    Plugin.Log.LogWarning("[FsmMasterGuard] 找不到 TryUnlockUiInput，跳过拦截");
                    _givingUp = true;
                    return;
                }

                Harmony harmony = new Harmony("SilksongItemRandomizer.FsmMasterGuard");
                harmony.Patch(target, prefix: new HarmonyMethod(
                    typeof(FsmMasterGuard).GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic)));

                _installed = true;
                Plugin.Log.LogInfo("[FsmMasterGuard] 已拦截 FsmMaster 空窗期 GameManager 访问");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[FsmMasterGuard] 拦截失败: {ex.Message}");
                _givingUp = true;
            }
        }

        // 返回 false 表示跳过原方法（仅当 GameManager 尚未就绪时）
        // 使用 SilentInstance 判断，避免访问 GameManager.instance getter 自身再次触发日志
        private static bool Prefix()
        {
            return GameManager.SilentInstance != null;
        }
    }
}


namespace SilksongItemRandomizer
{
    /// <summary>
    /// 英雄重生重置补丁
    /// 功能：重生时重置主角的各种状态（控制权、受击、无敌保护等）
    /// 此补丁独立于随机系统，无需配置开关，始终生效
    ///
    /// 出梦境重生卡死修复（基于调用栈诊断）：
    /// 根因：出梦境走 EnterScene 普通入场路径，FinishedEnteringScene 的 AcceptInput 未能生效，
    ///       hero 停在 acceptingInput=false / 无动画的冻结状态。
    /// 修复：仅在出梦境场景（Memory → 非Memory），检测到上述冻结状态时，
    ///       温和调用 AcceptInput() + StartAnimationControlToIdle() 恢复，不碰控制权/状态机。
    /// </summary>
    [HarmonyPatch(typeof(HeroController))]
    public static class HeroRespawnReset
    {
        private const float ProtectionDuration = 4f;
        private static float _protectionEndTime;

        // 出梦境检测：记录上一个场景名
        private static string _lastSceneName = "";

        // 反射缓存（懒加载一次，避免每次场景进入都 GetField）
        private static readonly FieldInfo _acceptingInputField;
        private static readonly FieldInfo _inputHandlerField;
        private static readonly FieldInfo _inputBlockersField;
        private static readonly bool _reflectReady;

        // inputHandler/inputActions 运行时类型才可知，懒加载缓存（同类实例可复用）
        private static System.Reflection.PropertyInfo _inputActionsProp;
        private static System.Reflection.MethodInfo _clearInputStateMethod;

        static HeroRespawnReset()
        {
            try
            {
                _acceptingInputField = typeof(HeroController).GetField("acceptingInput", BindingFlags.Instance | BindingFlags.NonPublic);
                _inputHandlerField = typeof(HeroController).GetField("inputHandler", BindingFlags.Instance | BindingFlags.NonPublic);
                _inputBlockersField = typeof(HeroController).GetField("inputBlockers", BindingFlags.Instance | BindingFlags.NonPublic);
                _reflectReady = _acceptingInputField != null && _inputHandlerField != null;
            }
            catch
            {
                _acceptingInputField = null;
                _inputHandlerField = null;
                _inputBlockersField = null;
                _reflectReady = false;
            }
        }

        [HarmonyPatch("FinishedEnteringScene", typeof(bool), typeof(bool))]
        [HarmonyPostfix]
        private static void FinishedEnteringScenePostfix(HeroController __instance)
        {
            // 重置控制权
            __instance.controlReqlinquished = false;
            __instance.cState.recoiling = false;
            __instance.cState.recoilingLeft = false;
            __instance.cState.recoilingRight = false;
            __instance.cState.floating = false;

            // 接受输入
            if (_reflectReady)
                _acceptingInputField.SetValue(__instance, true);

            // 清除输入状态
            ClearInputState(__instance);

            // 重置伤害模式，开启重力
            __instance.damageMode = 0;
            __instance.AffectedByGravity(true);

            // 无敌保护：仅陷阱随机开启时生效（对抗出生点踩陷阱即死），
            // 陷阱随机关闭时保持原生行为（重生无额外无敌）
            if (TrapRandomizer.Enabled)
            {
                _protectionEndTime = Time.time + ProtectionDuration;
                __instance.cState.invulnerable = true;
                __instance.StartCoroutine(ClearInvincibleAfterDelay(__instance, ProtectionDuration));
            }
        }

        private static void ClearInputState(HeroController hero)
        {
            try
            {
                if (_inputHandlerField == null) return;
                var inputHandler = _inputHandlerField.GetValue(hero);
                if (inputHandler == null) return;

                // 懒加载缓存 inputActions 属性（同类实例共享，缓存一次即可）
                if (_inputActionsProp == null)
                {
                    _inputActionsProp = inputHandler.GetType().GetProperty("inputActions");
                    if (_inputActionsProp == null) return;
                }
                var inputActions = _inputActionsProp.GetValue(inputHandler);
                if (inputActions == null) return;

                if (_clearInputStateMethod == null)
                {
                    _clearInputStateMethod = inputActions.GetType().GetMethod("ClearInputState");
                    if (_clearInputStateMethod == null) return;
                }
                _clearInputStateMethod.Invoke(inputActions, null);
            }
            catch { /* 清理输入状态失败不影响主流程 */ }
        }

        /// <summary>
        /// 场景加载后调用（Plugin.OnSceneLoaded）。
        /// 检测"出梦境"（Memory → 非Memory），若是则启动冻结恢复检测。
        /// </summary>
        public static void CheckAfterSceneLoad(string sceneName)
        {
            if (Plugin.Instance == null) return;

            bool isMemory = IsMemorySceneName(sceneName);
            bool wasMemory = IsMemorySceneName(_lastSceneName);
            _lastSceneName = sceneName;

            // 出梦境：上一个场景是 Memory，当前不是
            if (wasMemory && !isMemory && !string.IsNullOrEmpty(sceneName))
            {
                Plugin.Log.LogInfo($"[HeroRespawnReset] 检测到出梦境: {_lastSceneName} -> {sceneName}，启动冻结恢复检测");
                Plugin.Instance.StartCoroutine(FrozenRecovery(sceneName));
            }
        }

        /// <summary>
        /// 出梦境后延迟检测 hero 是否冻结（不接收输入），温和恢复。
        /// </summary>
        private static IEnumerator FrozenRecovery(string sceneName)
        {
            // 给原生入场流程（EnterScene / FinishedEnteringScene）留出完成时间
            yield return new WaitForSeconds(2f);
            if (Plugin.Instance == null) yield break;

            HeroController hero = HeroController.instance;
            if (hero == null) yield break;

            string currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (currentScene != sceneName) yield break; // 场景已切换，放弃

            // 冻结判定：不接收输入 + 无阻塞器 + 已就位 + 非过渡 + 非危境重生
            // （全部满足才温和恢复，避免干扰正常流程）
            bool frozen = !hero.acceptingInput
                && !hero.controlReqlinquished
                && hero.isHeroInPosition
                && !hero.cState.transitioning
                && !hero.cState.hazardRespawning
                && GetBlockerCount(hero) == 0;

            if (frozen)
            {
                Plugin.Log.LogWarning($"[HeroRespawnReset] 出梦境 {sceneName} 检测到 hero 冻结，温和恢复输入与动画控制");
                TryRecover(hero);

                // 二次确认
                yield return new WaitForSeconds(0.8f);
                if (hero != null && !hero.acceptingInput)
                {
                    Plugin.Log.LogWarning("[HeroRespawnReset] 温和恢复未生效，再次尝试");
                    TryRecover(hero);
                }
            }
        }

        /// <summary>
        /// 温和恢复：只调用原生 AcceptInput + StartAnimationControlToIdle（非 Force），
        /// 不碰 controlReqlinquished / hero_state / 重力 / 伤害模式。
        /// </summary>
        private static void TryRecover(HeroController hero)
        {
            try
            {
                hero.AcceptInput();
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"[HeroRespawnReset] AcceptInput 异常: {ex.Message}");
            }
            try
            {
                hero.StartAnimationControlToIdle();
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogError($"[HeroRespawnReset] StartAnimationControlToIdle 异常: {ex.Message}");
            }
            Plugin.Log.LogInfo("[HeroRespawnReset] 温和恢复完成");
        }

        private static int GetBlockerCount(HeroController hero)
        {
            try
            {
                if (_inputBlockersField == null) return 0;
                var blockers = _inputBlockersField.GetValue(hero) as System.Collections.Generic.HashSet<object>;
                return blockers?.Count ?? 0;
            }
            catch { return 0; }
        }

        private static bool IsMemorySceneName(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return false;
            return sceneName.StartsWith("Memory", System.StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerator ClearInvincibleAfterDelay(HeroController hero, float duration)
        {
            yield return new WaitForSeconds(duration);
            if (hero != null)
                hero.cState.invulnerable = false;
        }

        [HarmonyPatch("TakeDamage", typeof(GameObject), typeof(CollisionSide), typeof(int), typeof(HazardType), typeof(DamagePropertyFlags))]
        [HarmonyPrefix]
        private static bool TakeDamagePrefix(HeroController __instance)
        {
            return Time.time >= _protectionEndTime;
        }
    }
}


namespace SilksongItemRandomizer
{
    [HarmonyPatch]
    public static class BenchwarpTranslator
    {
        // 完整中文翻译字典（与之前相同，省略）
        private static readonly Dictionary<string, string> ChineseMap = new()
        {
            // 此处省略，请复制您之前提供的完整字典
        };

        private static bool _chineseDetected = false;
        private static float _chineseDetectTime = float.MinValue;
        private static readonly float ChineseRefreshInterval = 30f;

        // 字典为空时该功能未启用：完全短路，零开销
        private static bool IsChineseOverrideEnabled => ChineseMap.Count > 0;

        private static void EnsureChineseDetected()
        {
            if (!IsChineseOverrideEnabled) return;
            float now = UnityEngine.Time.realtimeSinceStartup;
            if (_chineseDetectTime != float.MinValue && now - _chineseDetectTime < ChineseRefreshInterval)
                return;
            _chineseDetectTime = now;
            _chineseDetected = DetectChinese();
        }

        private static bool DetectChinese()
        {
            // 与您的 DetectChinese 逻辑一致
            try
            {
                var fmType = Type.GetType("FontManager, Assembly-CSharp");
                if (fmType != null)
                {
                    var field = fmType.GetField("_currentLanguage", BindingFlags.Static | BindingFlags.NonPublic);
                    if (field != null)
                    {
                        var val = field.GetValue(null);
                        if (val != null)
                        {
                            string code = val.ToString().ToUpper();
                            if (code == "ZH" || code == "ZH_TW") return true;
                        }
                    }
                }
            }
            catch { }
            try
            {
                var gm = GameManager.instance;
                var gs = gm?.gameSettings;
                if (gs != null)
                {
                    var field = gs.GetType().GetField("language", BindingFlags.Instance | BindingFlags.Public);
                    if (field != null)
                    {
                        var val = field.GetValue(gs);
                        if (val != null)
                        {
                            string code = val.ToString().ToUpper();
                            if (code == "ZH" || code == "ZH_TW") return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private static MethodBase TargetMethod()
        {
            Type localizationType = Type.GetType("Benchwarp.Util.Localization, Benchwarp");
            return localizationType?.GetMethod("GetLanguageString", BindingFlags.Static | BindingFlags.Public);
        }

        [HarmonyPrefix]
        private static bool Prefix(string key, ref string __result)
        {
            EnsureChineseDetected();
            if (_chineseDetected && ChineseMap.TryGetValue(key, out string chinese))
            {
                __result = chinese;
                return false;
            }
            return true;
        }

        public static void RefreshUI()
        {
            GameObject menu = GameObject.Find("WarpMenu") ?? GameObject.Find("BenchwarpMenu");
            if (menu != null && menu.activeInHierarchy)
            {
                menu.SetActive(false);
                menu.SetActive(true);
                Debug.Log("[Benchwarp] UI refreshed.");
            }
        }
    }
}
