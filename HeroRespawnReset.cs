using GlobalEnums;
using HarmonyLib;
using System.Collections;
using System.Reflection;
using UnityEngine;

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 英雄重生重置补丁
    /// 功能：重生时重置主角的各种状态（控制权、受击、无敌保护等）
    /// 此补丁独立于随机系统，无需配置开关，始终生效
    /// </summary>
    [HarmonyPatch(typeof(HeroController))]
    public static class HeroRespawnReset
    {
        private const float ProtectionDuration = 4f;
        private static float _protectionEndTime;

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
            var acceptingField = typeof(HeroController).GetField("acceptingInput", BindingFlags.Instance | BindingFlags.NonPublic);
            acceptingField?.SetValue(__instance, true);

            // 清除输入状态
            var inputHandlerField = typeof(HeroController).GetField("inputHandler", BindingFlags.Instance | BindingFlags.NonPublic);
            var inputHandler = inputHandlerField?.GetValue(__instance);
            if (inputHandler != null)
            {
                var inputActionsProp = inputHandler.GetType().GetProperty("inputActions");
                var inputActions = inputActionsProp?.GetValue(inputHandler);
                (inputActions?.GetType().GetMethod("ClearInputState"))?.Invoke(inputActions, null);
            }

            // 重置伤害模式，开启重力
            __instance.damageMode = 0;
            __instance.AffectedByGravity(true);

            // 无敌保护
            _protectionEndTime = Time.time + ProtectionDuration;
            __instance.cState.invulnerable = true;
            __instance.StartCoroutine(ClearInvincibleAfterDelay(__instance, ProtectionDuration));
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