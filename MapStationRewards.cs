// MapStationRewards.cs - 地图与车站（Bellway）随机奖励定义
// 二代的地图和车站都不是 SavedItem，只是 PlayerData 布尔值：
//   地图：28 个 HasXMap（PlayerData.cs:377-404），原版由沙克拉商店 FSM SetPlayerDataBool 解锁
//   车站：10 个 UnlockedXStation（PlayerData.cs:1407-1416），原版由车站收费机
//         （BellBench 的 rosaryMachine FSM）解锁；铃兽总开关 UnlockedFastTravel
//         不单独入池，抽到任意车站时附带解锁
// 因此用 VirtualReward 实现：Give = SetBool，IsAtMax = 已拥有该 bool。
using StartingAbilityPicker;
using System;
using System.Collections.Generic;
using UnityEngine;

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
                    null,
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
        /// 图标暂为 null，如需商店/弹窗图标可用 SpriteCache.Find 补一个。
        /// </summary>
        private static IRandomReward CreateBoolReward(string id, string boolName, string displayName)
        {
            return new VirtualReward(
                id,
                Locale.Get(displayName),
                null,
                () => GiveBoolInternal(boolName),
                () =>
                {
                    var pd = PlayerData.instance;
                    return pd != null && pd.GetBool(boolName);
                }
            );
        }
    }
}
