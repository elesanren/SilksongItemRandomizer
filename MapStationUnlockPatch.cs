// MapStationUnlockPatch.cs - 拦截原版地图/车站解锁，转为随机奖励检查点
// 原理：二代所有地图购买（沙克拉商店）与车站开通（收费机 FSM）最终都走
// PlayerData.SetBool("HasXMap"/"UnlockedXStation", true)，在此处统一拦截：
//   首次触发 -> 阻止原版置位，发放随机奖励（mapping 持久化，spoiler 可查），
//               并直接禁用当前场景的收费机，禁止后续重复交互；
//   再次触发 -> 直接吞掉（双保险，正常不会走到，因为交互已被禁用）。
// 已领取记录复用 ItemRandomizer 的 mapping 存档，ItemRandomizer.ResetAllData
// （种子世界重置）时自动清空，收费机/交互随之恢复。
// mod 自己发奖励走的 SetBool 由 MapStationRewards.IsInternalGive 放行。
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SilksongItemRandomizer
{
    [HarmonyPatch(typeof(PlayerData), "SetBool")]
    public static class MapStationUnlockPatch
    {
        /// <summary>车站场景 -> 对应 PlayerData bool（来自 FastTravelScenes 的映射反查）</summary>
        private static readonly Dictionary<string, string> SceneToStationBool = new()
        {
            { "Bellway_02", "UnlockedDocksStation" },
            { "Bellway_03", "UnlockedBoneforestEastStation" },
            { "Bellway_04", "UnlockedGreymoorStation" },
            { "Belltown_basement", "UnlockedBelltownStation" },
            { "Bellway_08", "UnlockedCoralTowerStation" },
            { "Bellway_City", "UnlockedCityStation" },
            { "Slab_06", "UnlockedPeakStation" },
            { "Shellwood_19", "UnlockedShellwoodStation" },
            { "Bellway_Shadow", "UnlockedShadowStation" },
            { "Bellway_Aqueduct", "UnlockedAqueductStation" },
        };

        /// <summary>收费机对象名（与 LoreTriggerPatch.ExcludedNames 一致）</summary>
        private static readonly HashSet<string> TollMachineNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "bell_toll_machine",
            "rosary_string_machine"
        };

        /// <summary>查询场景对应的车站 bool（供 CheckPointScanner 使用）</summary>
        public static bool TryGetStationBoolForScene(string sceneName, out string boolName)
            => SceneToStationBool.TryGetValue(sceneName ?? "", out boolName);

        [HarmonyPrefix]
        private static bool Prefix(string boolName, bool value)
        {
            try
            {
                if (!value || string.IsNullOrEmpty(boolName)) return true;
                if (MapStationRewards.IsInternalGive) return true;
                if (!SilksongItemRandomizerAPI.IsEnabled()) return true;
                if (!ItemRandomizer.IsInitialized) return true;

                bool isStation = MapStationRewards.IsStationBool(boolName);
                bool isMap = !isStation && MapStationRewards.IsMapBool(boolName);
                if (!isStation && !isMap) return true;

                if (isStation && !ItemLimitConfig.EnableStationCheckIntercept) return true;
                if (isMap && !ItemLimitConfig.EnableMapCheckIntercept) return true;

                // 已领取过的检查点：吞掉原版置位，不发奖励（双保险；正常已被禁交互不会走到）
                string checkKey = "check:" + boolName;
                if (ItemRandomizer.GetMapping(checkKey) != null)
                {
                    Plugin.Log.LogInfo($"[地图/车站] {boolName} 已领取过，吞掉原版置位");
                    return false;
                }

                // ★ 预生成映射优先：进入场景时已从随机池摸好结果，直接按表给予；
                // 再延迟一帧发放，避免打断收费机/商店 FSM
                var reward = PreGeneratedMap.ResolveReward(checkKey) ?? ItemRandomizer.GetRandomReward();
                string rewardId = reward?.Id ?? "none";
                ItemRandomizer.RecordMapping(checkKey, rewardId);
                Plugin.Log.LogInfo($"[地图/车站] 拦截原版解锁 {boolName} -> 发放 {rewardId}");

                if (Plugin.Instance != null)
                    Plugin.Instance.StartCoroutine(GiveRewardAndDisableTollMachine(reward, isStation));

                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[地图/车站] 拦截补丁异常（放行原版）: {ex}");
                return true;
            }
        }

        private static IEnumerator GiveRewardAndDisableTollMachine(IRandomReward reward, bool disableToll)
        {
            yield return null;
            try
            {
                if (reward != null)
                {
                    reward.Give();
                    ItemRandomizer.AddGivenCount(reward.Id);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[地图/车站] 发放奖励失败: {ex}");
            }

            // 发奖后禁用当前场景收费机，防止再次付费交互
            if (disableToll)
                DisableTollMachinesInScene(SceneManager.GetActiveScene());
        }

        /// <summary>场景加载时调用（Plugin.OnSceneLoaded）：已领取的车站收费机重新禁用</summary>
        public static void OnSceneLoaded(Scene scene)
        {
            if (!SilksongItemRandomizerAPI.IsEnabled()) return;
            if (!ItemRandomizer.IsInitialized) return;
            if (!SceneToStationBool.TryGetValue(scene.name, out string boolName)) return;
            if (ItemRandomizer.GetMapping("check:" + boolName) == null) return;

            if (Plugin.Instance != null)
                Plugin.Instance.StartCoroutine(DisableTollMachinesDelayed(scene));
        }

        private static IEnumerator DisableTollMachinesDelayed(Scene scene)
        {
            // 等场景内 FSM 初始化完再禁用，避免被其自身逻辑重新激活
            yield return new WaitForSeconds(0.2f);
            DisableTollMachinesInScene(scene);
        }

        /// <summary>禁用场景内的车站收费机（按对象名 + BellBench 字段双通道）</summary>
        private static void DisableTollMachinesInScene(Scene scene)
        {
            try
            {
                int disabled = 0;

                // 通道 1：BellBench 上序列化的收费机引用（最精确）
                foreach (var bench in Resources.FindObjectsOfTypeAll<BellBench>())
                {
                    if (bench == null || bench.gameObject.scene != scene) continue;

                    var tollField = AccessTools.Field(typeof(BellBench), "tollMachine");
                    if (tollField?.GetValue(bench) is GameObject toll && toll.activeSelf)
                    {
                        toll.SetActive(false);
                        disabled++;
                    }
                    var rosaryField = AccessTools.Field(typeof(BellBench), "rosaryMachine");
                    if (rosaryField?.GetValue(bench) is PlayMakerFSM rosary && rosary.gameObject.activeSelf)
                    {
                        rosary.gameObject.SetActive(false);
                        disabled++;
                    }
                }

                // 通道 2：按对象名兜底（覆盖没有 BellBench 引用的场景形态）
                foreach (var root in scene.GetRootGameObjects())
                    disabled += DisableTollMachinesRecursive(root.transform);

                if (disabled > 0)
                    Plugin.Log.LogInfo($"[地图/车站] 场景 {scene.name} 已禁用 {disabled} 个收费机对象");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[地图/车站] 禁用收费机异常: {ex}");
            }
        }

        private static int DisableTollMachinesRecursive(Transform t)
        {
            int count = 0;
            if (TollMachineNames.Contains(t.gameObject.name))
            {
                // 是交互物则先 Deactivate，再整体隐藏
                var interactable = t.GetComponent<InteractableBase>();
                if (interactable != null) interactable.Deactivate(false);
                if (t.gameObject.activeSelf) t.gameObject.SetActive(false);
                count++;
            }
            foreach (Transform child in t)
                count += DisableTollMachinesRecursive(child);
            return count;
        }
    }
}
