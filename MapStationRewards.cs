// MapStationRewards.cs - 地图与车站（Bellway）随机奖励定义
// 二代的地图和车站都不是 SavedItem，只是 PlayerData 布尔值：
//   地图：28 个 HasXMap（PlayerData.cs:377-404），原版由沙克拉商店 FSM SetPlayerDataBool 解锁
//   车站：10 个 UnlockedXStation（PlayerData.cs:1407-1416），原版由车站收费机
//         （BellBench 的 rosaryMachine FSM）解锁；铃兽总开关 UnlockedFastTravel
//         不单独入池，抽到任意车站时附带解锁
// 因此用 VirtualReward 实现：Give = SetBool，IsAtMax = 已拥有该 bool。
using StartingAbilityPicker;
using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SilksongItemRandomizer
{
    public static class MapStationRewards
    {
        /// <summary>
        /// 内部给予标志。mod 自己发奖励时置 true，
        /// 防止 MapStationUnlockPatch 把这次 SetBool 再次当成"原版解锁"拦截。
        /// </summary>
        public static bool IsInternalGive { get; private set; }

        /// <summary>发放地图/车站解锁（绕过拦截补丁的置位通道）</summary>
        public static void GiveBoolInternal(string boolName)
        {
            var pd = PlayerData.instance;
            if (pd == null || string.IsNullOrEmpty(boolName)) return;
            try
            {
                IsInternalGive = true;
                pd.SetBool(boolName, true);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[地图/车站] 发放 {boolName} 失败: {ex}");
            }
            finally
            {
                IsInternalGive = false;
            }
        }

        

        /// <summary>28 张地图（对应 PlayerData 的 HasXMap 字段）</summary>
        public static readonly (string BoolName, string DisplayName)[] Maps =
        {
            ("HasMossGrottoMap", "Moss Grotto Map"),
            ("HasWildsMap", "Wilds Map"),
            ("HasBoneforestMap", "Boneforest Map"),
            ("HasDocksMap", "Docks Map"),
            ("HasGreymoorMap", "Greymoor Map"),
            ("HasBellhartMap", "Bellhart Map"),
            ("HasShellwoodMap", "Shellwood Map"),
            ("HasCrawlMap", "Crawl Map"),
            ("HasHuntersNestMap", "Hunters Nest Map"),
            ("HasJudgeStepsMap", "Judge Steps Map"),
            ("HasDustpensMap", "Dustpens Map"),
            ("HasSlabMap", "Slab Map"),
            ("HasPeakMap", "Peak Map"),
            ("HasCitadelUnderstoreMap", "Citadel Understore Map"),
            ("HasCoralMap", "Coral Map"),
            ("HasSwampMap", "Swamp Map"),
            ("HasCloverMap", "Clover Map"),
            ("HasAbyssMap", "Abyss Map"),
            ("HasHangMap", "Hang Map"),
            ("HasSongGateMap", "Song Gate Map"),
            ("HasHallsMap", "Halls Map"),
            ("HasWardMap", "Ward Map"),
            ("HasCogMap", "Cog Map"),
            ("HasLibraryMap", "Library Map"),
            ("HasCradleMap", "Cradle Map"),
            ("HasArboriumMap", "Arborium Map"),
            ("HasAqueductMap", "Aqueduct Map"),
            ("HasWeavehomeMap", "Weavehome Map"),
        };

        /// <summary>10 个车站（UnlockedFastTravel 不入池，由车站奖励附带解锁）</summary>
       

        /// <summary>
        /// 快速旅行总开关（救铃兽解锁）。不单独入池：
        /// 抽到任意车站奖励时附带解锁它，保证"有站即可用"；
        /// 原版救铃兽剧情也不拦截，两个来源都能拿到。
        /// </summary>
        public const string FastTravelMasterBool = "UnlockedFastTravel";

        /// <summary>10 个车站（UnlockedFastTravel 不入池，由车站奖励附带解锁）</summary>
        
        public static readonly (string BoolName, string DisplayName)[] Stations =
        {
            ("UnlockedDocksStation", "Docks Station"),
            ("UnlockedBoneforestEastStation", "Boneforest East Station"),
            ("UnlockedGreymoorStation", "Greymoor Station"),
            ("UnlockedBelltownStation", "Belltown Station"),
            ("UnlockedCoralTowerStation", "Coral Tower Station"),
            ("UnlockedCityStation", "City Station"),
            ("UnlockedPeakStation", "Peak Station"),
            ("UnlockedShellwoodStation", "Shellwood Station"),
            ("UnlockedShadowStation", "Shadow Station"),
            ("UnlockedAqueductStation", "Aqueduct Station"),
        };

        private static readonly HashSet<string> _mapBools = BuildSet(Maps);
        private static readonly HashSet<string> _stationBools = BuildSet(Stations);

        private static HashSet<string> BuildSet((string BoolName, string DisplayName)[] table)
        {
            var set = new HashSet<string>();
            foreach (var (b, _) in table) set.Add(b);
            return set;
        }

        public static bool IsMapBool(string boolName) => boolName != null && _mapBools.Contains(boolName);
        public static bool IsStationBool(string boolName) => boolName != null && _stationBools.Contains(boolName);

        // ========== 奖励构建（由 ItemRandomizer.BuildRewardPools 调用） ==========

        public static IEnumerable<IRandomReward> BuildMapRewards()
        {
            foreach (var (boolName, displayName) in Maps)
                yield return CreateBoolReward("virt:Map_" + boolName, boolName, displayName);
        }

        public static IEnumerable<IRandomReward> BuildStationRewards()
        {
            foreach (var (boolName, displayName) in Stations)
            {
                string capturedBool = boolName;
                yield return new VirtualReward(
                    "virt:Station_" + capturedBool,
                    Locale.Get(displayName),
                    SpriteCache.Find("pin_tube_station_shop_icon"),
                    () =>
                    {
                        GiveBoolInternal(capturedBool);
                        // 附带解锁铃兽总开关，避免"车站解锁了但没铃兽用不了"
                        GiveBoolInternal(FastTravelMasterBool);
                    },
                    () =>
                    {
                        var pd = PlayerData.instance;
                        return pd != null && pd.GetBool(capturedBool);
                    }
                );
            }
        }

        /// <summary>
        /// 地图/车站奖励：Give = 置位对应 PlayerData bool；IsAtMax = 已拥有（读档后自动出池）。
        /// </summary>
        private static IRandomReward CreateBoolReward(string id, string boolName, string displayName)
        {
            return new VirtualReward(
                id,
                Locale.Get(displayName),
                SpriteCache.Find("I_map"),
                () => GiveBoolInternal(boolName),
                () =>
                {
                    var pd = PlayerData.instance;
                    return pd != null && pd.GetBool(boolName);
                }
            );
        }
    }

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
                    return false;
                }

                // ★ 预生成映射优先：进入场景时已从随机池摸好结果，直接按表给予；
                // 再延迟一帧发放，避免打断收费机/商店 FSM
                var reward = PreGeneratedMap.ResolveReward(checkKey) ?? ItemRandomizer.GetRandomReward();
                ItemRandomizer.RecordMapping(checkKey, reward?.Id ?? "none");

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
