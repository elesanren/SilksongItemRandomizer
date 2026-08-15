using HarmonyLib;
using System;
using System.Reflection;
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
