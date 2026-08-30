using System;
using System.Collections.Generic;

namespace SilksongItemRandomizer
{
    [Serializable]
    public class GlobalSaveData
    {
        public int Version = 2;  // 版本升级到2

        // CrestRandomizer
        public Dictionary<string, string> CrestMappings = new();
        public HashSet<string> UnlockedCrests = new();
        public string LastUnlockedCrest = "";

        // ChurchRandomizePatch：已完成平替（随机奖励已触发）的教堂纹章，用于门场景关门判定
        public HashSet<string> ChapelRandomizedCrests = new();

        // Extracurrencypickup
        public HashSet<string> PickedPositions = new();

        // PickupPatch
        public HashSet<string> PickedPickupKeys = new();

        // CurrencyCollectPatch
        public int CurrencyTotalCollectCount = 0;
        public bool CurrencyFirstKeyGiven = false;
        public bool CurrencySecondKeyGiven = false;

        // 新增：货币保底阈值配置
        public int CurrencyFirstThreshold = 30;
        public int CurrencySecondThreshold = 1000;

        // ShopMenuStock_BuildItemList_Patch
        public Dictionary<string, int> ShopSlotCounts = new();

        // ItemRandomizer
        public Dictionary<string, int> ItemGivenCounts = new();
        public Dictionary<string, string> TotalMappings = new();

        // 新增：物品随机概率配置
        public float VirtualUnlimitedProbability = 0.1f;   // 虚拟无限池概率
        public float CrestUnlockerProbability = 0.1f;      // 纹章槽位解锁器概率
        public float NormalLimitedProbability = 0.8f;      // 普通有限池概率
        public int MaxGivenPerItem = 2;                    // 每件物品最大获得次数

        // Plugin
        public HashSet<string> DestroyedPickupKeys = new();

        // SilkSpearPityPatch
        public int SilkSpearTryGetCount = 0;
        public bool SilkSpearGiven = false;
        // 新增：丝矛保底触发次数配置
        public int SilkSpearPityCount = 5;

        // TrapRandomizer
        public bool TrapEnabled = false;
        public string TrapDifficulty = "Beginner";
        public bool TrapMovementEnabled = true;
        public HashSet<string> TrapFrostBannedScenes = new();

        // ToolEffectRandomizer
        public Dictionary<string, Dictionary<string, float>> CrestEffects = new();

        // ShopRandomizer (新增)
        public HashSet<string> ShopAssignedItemIds = new();
        // ===== 来自 StartingAbilityPicker =====
        public Dictionary<int, bool> ProfileCompletionDisplay = new Dictionary<int, bool>();
        public bool AbilityUpward = true;
        public bool AbilityLeft = true;
        public bool AbilityRight = true;
        public bool AttackDirectionsSet = false;

        // ===== 来自 SkillTriggerMod =====
        public HashSet<string> SkillTriggerRecords = new HashSet<string>();

        // ===== 预生成映射表（新增部分）：所有检查点 key -> "reward:Id" =====
        // 进入游戏后从随机池提前摸出具体奖励并落盘，遇点时直接按表给予。
        // key 格式：拾取点=场景_F2坐标，Lore=场景:对象名，车站=check:bool
        public Dictionary<string, string> PreGeneratedMappings = new();

        // 生成 PreGeneratedMappings 时使用的配置指纹。
        // 启动时若当前配置（珍贵额度、种子等影响映射的内容）与此不一致，
        // PreGeneratedMap 会自动失效旧映射并重新生成，保证配置改动自动生效。
        public string MappingsConfigStamp = "";

        // MossberryRandomizer：捡过苔莓的房间名集合（房间级：捡过后该房间藤蔓不再长苔莓）
        public HashSet<string> MossberryCollectedRooms = new();

        // ShellFlowerRandomizer：捡过花芯(Shell Flower)的房间名集合（房间级：捡过后该房间紫花花蕾消失）
        public HashSet<string> ShellFlowerCollectedRooms = new();

        // SkillRegionRandomizePatch：已完成技能地区平替（随机奖励已触发）的场景名集合，
        // 已平替区域持久化标记，跨会话永久不再触发（只走原生演出收尾）。
        public HashSet<string> SkillRandomizedRegions = new();

        // 原生碎片/苔莓顺序发放计数器（按获取顺序依次给对应编号的映射奖励）
        public int HeartSeq = 0; // 已发放面具碎片映射个数（heart:01~20）
        public int SpoolSeq = 0; // 已发放丝轴碎片映射个数（spool:01~18）
        public int MossSeq = 0;  // 已发放苔莓映射个数（moss:01~06）
        public int FlowerSeq = 0; // 已发放花芯映射个数（flower:01~06）
        public int FleaSeq = 0;  // 已发放跳蚤救援映射个数（flea:01~27）

        // RecentItemsUI：最近获得物品列表（图标名+文本，最多保留 10 条，进游戏后重放到右上角列表）
        public List<RecentItemsEntry> RecentItemList = new();

        // GeoRockReplacer：已替换为拾取点的钱堆坐标 key（"场景名_坐标"，防重进场景复活）
        public HashSet<string> GeoRockReplacedPositions = new();

        // FleaRescueReplacer：已替换为拾取点的跳蚤坐标 key
        public HashSet<string> FleaRescueReplacedPositions = new();

        // MapMachineGetPatch：已"拿过"（随机奖励已触发）的地图收费机场景名集合，
        // 用于购买后隐藏收费机并跨场景重载保持隐藏。
        public HashSet<string> MapMachineClaimedScenes = new();
    }

    [Serializable]
    public class RecentItemsEntry
    {
        public string IconName = "";
        public string Text = "";
    }

    // 仅用于旧数据迁移的内部类
    [Serializable]
    public class CurrencyState
    {
        public int TotalCollectCount = 0;
        public bool FirstKeyGiven = false;
        public bool SecondKeyGiven = false;
    }

}