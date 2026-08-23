using HarmonyLib;
using Object = UnityEngine.Object;
using StartingAbilityPicker;
using System.Collections.Generic;
using System.Collections;
using System;
using TeamCherry.Localization;
using UnityEngine.SceneManagement;
using UnityEngine;

// MapStationRewards.cs - 地图与车站（Bellway）随机奖励定义
// 二代的地图和车站都不是 SavedItem，只是 PlayerData 布尔值：
//   地图：28 个 HasXMap（PlayerData.cs:377-404），原版由沙克拉商店 FSM SetPlayerDataBool 解锁
//   车站：10 个 UnlockedXStation（PlayerData.cs:1407-1416），原版由车站收费机
//         （BellBench 的 rosaryMachine FSM）解锁；铃兽总开关 UnlockedFastTravel
//         不单独入池，抽到任意车站时附带解锁
// 因此用 VirtualReward 实现：Give = SetBool，IsAtMax = 已拥有该 bool。

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


namespace SilksongItemRandomizer
{
    /// <summary>
    /// 日志（地方志/Lore 石碑）随机化逻辑。
    /// 目标是"直接交互即可阅读"的石碑/铭文日志（非拉琴 Needolin 类）。
    /// 显示链路复刻原生: PlayMaker RunDialogue -> DialogueBox.StartConversation，
    /// 无实体 NPC 时用 PlayMakerNPC.GetNewTemp 创建临时 NPC（与 RunDialogueBase.DoActionNoComponent 一致）。
    /// 已接入随机奖励池（LoreReward，作为普通有限奖励参与抽取）。
    /// </summary>
    public static class LoreRandomizer
    {
        /// <summary>直接交互石碑文本所在语言表</summary>
        public const string LoreSheet = "Lore";

        /// <summary>单条 lore 条目：来源场景 + 场景内对象名 + 语言表 sheet/key（原生流程 Language.Get 直接命中）</summary>
        public readonly struct LoreEntry
        {
            public readonly string Scene;
            public readonly string Name;
            public readonly string Sheet;
            public readonly string Key;

            public LoreEntry(string scene, string name, string sheet, string key)
            {
                Scene = scene;
                Name = name;
                Sheet = sheet;
                Key = key;
            }
        }

        /// <summary>
        /// 固定 lore 条目表：全场景扫描筛选出的石碑/铭文/文告/环境描述，
        /// sheet/key 均为语言表真实键（0 条取不到文本）。重复场景条目原样保留，
        /// 后续可据此生成 loretrig:scene:name 映射。
        /// </summary>
        public static readonly LoreEntry[] LoreTable =
        {
            new LoreEntry("Abyss_02b", "Inspect Region", "Inspect", "ABYSS_LORE_STONE_TOP"),
            new LoreEntry("Abyss_06", "Inspect Region", "Inspect", "ABYSS_LORE_STONE_BASE"),
            new LoreEntry("Abyss_08", "Inspect Region - Void Tendrils", "Inspect", "WEAVE_DARK"),
            new LoreEntry("Aqueduct_05", "Mr Mushroom Tablet", "Lore", "MR_MUSH_RIDDLE_TAB"),
            new LoreEntry("Aqueduct_05_festival", "Inspect Region", "Inspect", "WEAVE_WHITE_LAKE"),
            new LoreEntry("Aqueduct_05_pre", "Inspect Region", "Inspect", "WEAVE_WHITE_LAKE"),
            new LoreEntry("Arborium_01", "Inspect Region", "Inspect", "ARBORIUM_PLAQUE"),
            new LoreEntry("Arborium_03", "Inspect Region", "Inspect", "ARBORIUM_ORDERS"),
            new LoreEntry("Belltown", "Inspect Region", "Inspect", "PINSMITH_SIGN"),
            new LoreEntry("Belltown_06", "Inspect Region", "Inspect", "BELLTOWN_OUTER_SIGN"),
            new LoreEntry("Belltown_07", "Inspect Region", "Inspect", "BELLTOWN_OUTER_SIGN"),
            new LoreEntry("Bone_01c", "Inspect Region", "Lore", "MARROW_START_SIGN"),
            new LoreEntry("Bone_18", "Inspect Region", "Inspect", "CURIOUS_PILGRIM_DIARY"),
            new LoreEntry("Bone_East_10", "Inspect Region", "Inspect", "PILGRIM_REST_SIGN"),
            new LoreEntry("Bone_East_Weavehome", "Inspect Region (2)", "Inspect", "WEAVE_WILDS"),
            new LoreEntry("Bonetown", "Inspect Region (1)", "Inspect", "BONETOWN_TOP_SIGN"),
            new LoreEntry("Clover_10", "Inspect Region", "Inspect", "CLOVER_OATH_PLAQUE"),
            new LoreEntry("Clover_18", "Inspect Region", "Inspect", "CLOVER_LAKE_PLAQUE"),
            new LoreEntry("Clover_18", "Plinth Inspect", "Inspect", "CLOVER_LAKE_PLINTH"),
            new LoreEntry("Coral_19", "Inspect Region", "Inspect", "CORAL_JUDGEMENT_SIGN"),
            new LoreEntry("Coral_25", "Interact", "Lore", "CORAL_CRUST_TAB_1"),
            new LoreEntry("Coral_Tower_01", "Interact", "Lore", "CORAL_CRUST_TAB_2"),
            new LoreEntry("Coral_36", "Inspect Region", "Inspect", "JUDGE_NURSERY"),
            new LoreEntry("Cradle_02b", "Inspect Region", "Inspect", "CRADLE_CAGE_01"),
            new LoreEntry("Cradle_02b", "Inspect Region (1)", "Inspect", "CRADLE_CAGE_02"),
            new LoreEntry("Cradle_02b", "Inspect Region (2)", "Inspect", "CRADLE_CAGE_03"),
            new LoreEntry("Greymoor_03", "Inspect Region", "Inspect", "GREY_ORDERS"),
            new LoreEntry("Greymoor_16", "Inspect Region", "Inspect", "GREY_LORE"),
            new LoreEntry("Library_13", "Inspect Region", "Inspect", "TROBBIO_SIGN"),
            new LoreEntry("Library_13b", "Inspect Region Act 2", "Inspect", "LIBRARY_THEATRE_LINES"),
            new LoreEntry("Library_13b", "Inspect Region Act 3", "Inspect", "LIBRARY_THEATRE_LINES_ACT3"),
            new LoreEntry("Mosstown_02", "Inspect Region", "Inspect", "MOSSTOWN_STONE"),
            new LoreEntry("Mosstown_02", "Inspect Region", "Inspect", "WEAVE_HARP_MOSSTOWN"),
            new LoreEntry("Peak_10", "Inspect Region (2)", "Inspect", "WEAVE_PEAK"),
            new LoreEntry("Room_Forge", "Inspect Region", "Inspect", "DOCKS_NOTE_1"),
            new LoreEntry("Room_Pinstress", "Inspect Region", "Inspect", "PINSTRESS_SUMMONS"),
            new LoreEntry("Shadow_08", "Inspect Region", "Inspect", "SWAMP_STOREROOM"),
            new LoreEntry("Shadow_18", "Inspect Region", "Inspect", "BILEHAVEN_PLAQUE"),
            new LoreEntry("Shadow_Weavehome", "Inspect Region (1)", "Inspect", "WEAVE_WORKSHOP_SCROLL"),
            new LoreEntry("Shellgrave", "Inspect Region", "Inspect", "SHELLGRAVE"),
            new LoreEntry("Shellwood_10", "Inspect Region", "Inspect", "WEAVE_HARP_SHELLWOOD"),
            new LoreEntry("Shellwood_11b", "Inspect Region", "Inspect", "SHELLWOOD_SHRINE_SIGN"),
            new LoreEntry("Slab_08", "Inspect Region", "Inspect", "SLAB_ORDERS_1"),
            new LoreEntry("Slab_08", "Inspect Region", "Inspect", "SLAB_ORDERS_2"),
            new LoreEntry("Slab_10c", "Inspect Region", "Inspect", "SLAB_WEAVER_GATE"),
            new LoreEntry("Tube_Hub", "Inspect Region", "Inspect", "TUBE_HUB_NOTICE"),
            new LoreEntry("Tut_04", "Inspect Region", "Inspect", "SHAMAN_STORE_ROOM"),
            new LoreEntry("Tut_05", "Inspect Region (1)", "Inspect", "SHAMAN_STONE_CHAPEL"),
            new LoreEntry("Ward_07", "Inspect Region", "Inspect", "WARD_OATH"),
            new LoreEntry("Weave_08", "Inspect Region (1)", "Inspect", "WEAVE_ARCHIVE_RIGHT"),
            new LoreEntry("Dock_02", "Inspect Region", "Inspect", "DOCKS_FLINTSTONE_BUCKET"),
            new LoreEntry("Dust_05", "Inspect Region", "Inspect", "DUSTROACH_GUTS_INSPECT"),
            new LoreEntry("Coral_29", "Inspect Region", "Inspect", "CORAL_ZAP_CORPSE"),
            new LoreEntry("Halfway_01", "Inspect Region", "Wanderers", "HUNTER_FAN_SCROLL_INSPECT"),
            new LoreEntry("Peak_05e", "Inspect Region", "Inspect", "MAGNETITE_OUTCROP"),
        };

        /// <summary>
        /// lore 触发白名单：由 LoreTable 自动生成（"scene|name" 键，同名对象自动去重，
        /// 如 Mosstown_02 两个 Inspect Region 合并为一个键）。
        /// 仅白名单内对象触发随机奖励并禁用；其余 inspect（权限类）触发后放行原生流程。
        /// </summary>
        public static readonly HashSet<string> LoreObjectKeys = BuildLoreObjectKeys();

        private static HashSet<string> BuildLoreObjectKeys()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in LoreTable)
                if (!string.IsNullOrEmpty(entry.Name))
                    set.Add(entry.Scene + "|" + entry.Name);
            return set;
        }

        /// <summary>运行时应答：scene+name 是否 lore 白名单对象（触发随机奖励；否则权限类放行原生流程）</summary>
        public static bool IsLoreObject(string scene, string name)
            => !string.IsNullOrEmpty(scene) && !string.IsNullOrEmpty(name)
               && LoreObjectKeys.Contains(scene + "|" + name);

        /// <summary>
        /// lore 触发对象按用途分类并给对应图标（区分混入的功能类交互物）：
        /// 蘑菇石碑/门锁/地图收费机/收费机 各有专用图标，其余一律归 lore 图标。
        /// </summary>
        public static Sprite GetLoreTypeIcon(string name)
        {
            if (string.IsNullOrEmpty(name)) return SpriteCache.Find("bell_sub_sign");

            // 蘑菇石碑
            if (name.IndexOf("Mushroom", StringComparison.OrdinalIgnoreCase) >= 0)
                return SpriteCache.Find("Mr_Mushroom_quest_notch");
            // 收费机 / 收费门 / 收费长椅（椅子收费机）——必须优先于 door 判断，toll door 是收费门
            if (name.IndexOf("toll", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Bench", StringComparison.OrdinalIgnoreCase) >= 0)
                return SpriteCache.Find("bellbench_toll_machine");
            // 地图收费机 / 地图机器（与椅子收费机区分）
            if (name.IndexOf("map", StringComparison.OrdinalIgnoreCase) >= 0)
                return SpriteCache.Find("bellbench_toll_machine_map");
            // 门锁 / 开关 / 钥匙口
            if (name.IndexOf("lock", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("door", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("lever", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("bank_door", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("key_machine", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("receptacle", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Receptactle", StringComparison.OrdinalIgnoreCase) >= 0)
                return SpriteCache.Find("trapdoor_lever");

            // 其余一律 lore 图标
            return SpriteCache.Find("bell_sub_sign");
        }

        /// <summary>
        /// 从固定 lore 表中随机取一条（供随机奖励池使用）。表为空时返回 null。
        /// </summary>
        public static LoreEntry? GetRandomLoreEntry()
        {
            if (LoreTable.Length == 0)
            {
                Plugin.Log.LogWarning("[LoreRandomizer] lore 表为空，跳过");
                return null;
            }
            return LoreTable[UnityEngine.Random.Range(0, LoreTable.Length)];
        }

        /// <summary>
        /// 用原生石碑逻辑显示指定 key 的日志（DialogueBox 对话框，玩家按键翻页/关闭）
        /// </summary>
        public static void ShowLoreDialogue(string key, string sheet = LoreSheet)
        {
            var text = Language.Get(key, sheet);
            if (string.IsNullOrEmpty(text))
            {
                Plugin.Log.LogWarning($"[LoreRandomizer] 取不到文本: {sheet}/{key}");
                return;
            }

            // 复刻 RunDialogueBase.DoActionNoComponent: 临时 FSM 宿主 + 临时 PlayMakerNPC
            var host = new GameObject("LoreRandomizer_TempDialogue");
            var fsm = host.AddComponent<PlayMakerFSM>();
            var npc = PlayMakerNPC.GetNewTemp(fsm);
            if (npc == null)
            {
                Plugin.Log.LogError("[LoreRandomizer] 创建临时 PlayMakerNPC 失败");
                Object.Destroy(host);
                return;
            }

            npc.StartDialogueMove();

            void Cleanup()
            {
                try
                {
                    npc.ForceEndDialogue();
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[LoreRandomizer] 结束对话异常: {ex.Message}");
                }
                Object.Destroy(host, 1f);
            }

            // 复刻原生"检视石碑"排版（InteractEvents.OnStartDialogue）：
            // Alignment=Top（顶部居中）而非 Default 的 TopLeft，铭文观感更规整
            var displayOptions = new DialogueBox.DisplayOptions
            {
                Alignment = TMProOld.TextAlignmentOptions.Top,
                ShowDecorators = true,
                TextColor = Color.white,
            };

            DialogueBox.StartConversation(
                text,
                npc,
                overrideContinue: false,
                displayOptions,
                onDialogueEnd: Cleanup,
                onDialogueCancelled: () => Object.Destroy(host, 1f));
        }
    }

    /// <summary>
    /// 作为随机奖励池的一份子：发放时随机挑一条"直接交互即可阅读"的普通日志，
    /// 用原生石碑对话框（DialogueBox）显示，玩家按键翻页/关闭。
    /// 属于信息类奖励，可重复发放（IsAtMax 永远为 false）。
    /// </summary>
    public class LoreReward : IRandomReward
    {
        public string Id => "virt:LoreLog";
        public string DisplayName => "随机日志";
        public Sprite Icon => SpriteCache.Find("bell_sub_sign");

        public void Give()
        {
            try
            {
                var entry = LoreRandomizer.GetRandomLoreEntry();
                if (entry.HasValue)
                    LoreRandomizer.ShowLoreDialogue(entry.Value.Key, entry.Value.Sheet);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[LoreReward] 发放日志失败: {ex}");
            }
        }

        public bool IsAtMax() => false;
    }

    /// <summary>
    /// 检测游戏内「检查」交互（石碑等）触发一次性随机奖励。
    /// 挂钩 NPCControlBase.StartDialogue —— 所有交互类型共用的 funnel。
    /// 判定完全基于「交互键类型」：InteractLabel == PromptLabels.Inspect，不检测内容。
    /// 所有 inspect 交互物按两个本地存储记录分支处理：
    /// - 记录一（loretrig:scene:name）：该地点是否已触发过随机——每地只触发一次，种子重置才清。
    /// - 记录二（virt:Permit:scene:name）：权限物（进随机池）是否已获得。
    /// 分支：
    /// - lore 白名单（LoreRandomizer.LoreObjectKeys）：发放随机奖励，跳过原内容并原地禁用
    ///   （lore 内容已入随机池，原地后续不需要恢复）。
    /// - 车站收费机（InspectPermitRewards.Permits 登记的地点）：无权限物时——首次交互发放随机奖励（记录一），随后拦截；
    ///   获得权限物（记录二）后放行原地原生流程（把存储的不允许变成允许，不看具体种类）。
    /// - 其余权限类地点（白名单外且未登记）：一律放行原生流程，不触发随机、不拦截（已移除随机化，避免卡剧情）。
    /// 受「物品随机总开关」SilksongItemRandomizerAPI.IsEnabled() 控制。
    /// 我们自己的 LoreRandomizer.ShowLoreDialogue 走 DialogueBox 字符串重载，不经过 StartDialogue，不会自触发。
    /// </summary>
    [HarmonyPatch(typeof(NPCControlBase), "StartDialogue")]
    public static class LoreTriggerPatch
    {
        private const string TriggerIdPrefix = "loretrig:";

        // 不触发奖励的交互物：
        // - 功能性机器（钟 / 念珠机），触发会结束交互、打断其本身功能，故排除
        // - 记忆球（memory_orb_inspect，梦境剧情入口，如黑寡妇针刺梦境 Memory_Needolin / 首罪人 Memory_First_Sinner）：
        //   交互即启动记忆演出，是必需剧情步骤。拦截会把 StartDialogue 吞掉 → 演出 FSM 永远收不到
        //   INTERACT → 动画/出梦境/后续剧情全断。整类梦境入口一律豁免。
        private static readonly HashSet<string> ExcludedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bell_toll_machine",     // 钟
            "rosary_string_machine", // 念珠机
            "memory_orb_inspect"     // 梦境记忆球（剧情必需，非随机检查点）
        };

        /// <summary>是否为收费机类排除名（供 CheckPointScanner 使用）</summary>
        public static bool IsExcludedTollName(string name) => name != null && ExcludedNames.Contains(name);

        [HarmonyPrefix]
        private static bool Prefix(NPCControlBase __instance)
        {
            try
            {
                if (__instance == null)
                    return true;
                if (!SilksongItemRandomizerAPI.IsEnabled())
                    return true;
                if (!ItemLimitConfig.EnableLoreTrigger)
                    return true;

                // 仅「检查 / inspect」交互键触发，不检测内容
                if (__instance.InteractLabel != InteractableBase.PromptLabels.Inspect)
                    return true;

                // lore 白名单对象：触发后原地禁用（内容已入随机池，后续不需要恢复）；
                // 权限类（白名单外）：权限物决定能否走原生流程（记录二），随机奖励每地一次（记录一）
                bool isLore = LoreRandomizer.IsLoreObject(__instance.gameObject.scene.name, __instance.gameObject.name);

                // 功能性机器不触发（保持原功能可用）
                if (ExcludedNames.Contains(__instance.gameObject.name))
                    return true;

                // 每个地点（交互物）只触发一次随机，跟随存档持久化（本地存储）
                string locationId = $"{__instance.gameObject.scene.name}:{__instance.gameObject.name}";
                string triggerId = TriggerIdPrefix + locationId;

                if (isLore)
                {
                    if (ItemRandomizer.GetGivenCount(triggerId) > 0)
                    {
                        // 已触发过：禁用交互（正常不会被调用到——场景加载时已 Deactivate，这里是兜底）
                        __instance.Deactivate(false);
                        return false;
                    }
                }
                else
                {
                    // ★ 权限类：仅保留车站收费机（InspectPermitRewards 中登记的地点）走权限逻辑；
                    // 其余权限类地点已移除随机化，直接放行原生流程（不触发随机、不拦截，避免卡剧情）。
                    if (!InspectPermitRewards.IsPermitObject(__instance.gameObject.scene.name, __instance.gameObject.name))
                        return true;

                    // ★ 权限类：记录二 = 权限物（virt:Permit:locationId）是否已获得
                    string permitId = "virt:Permit:" + locationId;
                    if (ItemRandomizer.GetGivenCount(permitId) > 0)
                        return true; // 已获得权限：放行原生流程（原地后续照常）

                    // 无权限物：已触发过随机则拦截（等待从随机池获得权限物才能使用）
                    if (ItemRandomizer.GetGivenCount(triggerId) > 0)
                        return false;
                }

                // ★ 预生成映射优先：进入场景时已从随机池摸好结果，直接按表给予；
                // 跳过 LoreReward——发放它会再弹一个 DialogueBox，可能打断阅读
                string triggerKey = $"lore:{locationId}";
                // 全量映射已覆盖所有 Lore 键，直接按表给予
                IRandomReward reward = PreGeneratedMap.ResolveReward(triggerKey);

                // 兜底：预生成不可用（如奖励池为空）时回退现有动态抽取
                if (reward == null)
                {
                    for (int i = 0; i < 10; i++)
                    {
                        var candidate = ItemRandomizer.GetRandomReward();
                        if (candidate == null)
                            break;
                        if (candidate is LoreReward)
                            continue;
                        reward = candidate;
                        break;
                    }
                }
                if (reward == null)
                {
                    return true; // 无奖励则正常显示原内容
                }

                reward.Give();

                // 触发成功后才标记，避免奖励池为空时白白消耗一次机会
                ItemRandomizer.AddGivenCount(triggerId);
                ItemRandomizer.AddGivenCount(reward.Id);
                ItemRandomizer.RecordMapping($"lore:{locationId}", $"reward:{reward.Id}");

                // 按交互物类型展示图标（收费机/地图机/门锁/蘑菇 各自专用，lore 石碑一律 lore 图标）
                RecentItemsUI.AddItem(new IconOverrideReward(reward, LoreRandomizer.GetLoreTypeIcon(__instance.gameObject.name)));

                // 已发放奖励：lore 禁用该交互物（此后不可再交互）并跳过原内容；
                // 权限类跳过原内容并拦截（无权限物时不能用，等玩家从随机池获得权限物后放行）
                if (isLore)
                    __instance.Deactivate(false);
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[LoreTriggerPatch] 异常: {ex}");
                return true; // 异常时回退到原交互，避免卡死
            }
        }

        /// <summary>
        /// 场景加载时调用（Plugin.OnSceneLoaded）：
        /// 把本场景已触发过的交互物重新 Deactivate（场景重载后组件状态会复位）。
        /// 已触发记录存在 ItemGivenCounts 里，种子重置（ResetAllData）后自动恢复。
        /// </summary>
        public static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene)
        {
            if (!SilksongItemRandomizerAPI.IsEnabled()) return;
            if (!ItemLimitConfig.EnableLoreTrigger) return;
            if (!ItemRandomizer.IsInitialized) return;
            if (Plugin.Instance == null) return;
            Plugin.Instance.StartCoroutine(DisableTriggeredInteractablesDelayed(scene));
        }

        private static System.Collections.IEnumerator DisableTriggeredInteractablesDelayed(
            UnityEngine.SceneManagement.Scene scene)
        {
            // 等场景内 FSM 初始化完再禁用，避免被其自身逻辑重新激活
            yield return new UnityEngine.WaitForSeconds(0.2f);
            try
            {
                // 短路：本存档从未触发过任何 lore 点时跳过全量 NPCControlBase 扫描
                if (!ItemRandomizer.HasAnyGivenCountWithPrefix(TriggerIdPrefix))
                    yield break;
                int disabled = 0;
                foreach (var npc in UnityEngine.Resources.FindObjectsOfTypeAll<NPCControlBase>())
                {
                    if (npc == null || npc.gameObject.scene != scene) continue;
                    if (npc.InteractLabel != InteractableBase.PromptLabels.Inspect) continue;
                    // 权限类不做任何处理（原地放行原生流程）
                    if (!LoreRandomizer.IsLoreObject(scene.name, npc.gameObject.name)) continue;
                    if (ExcludedNames.Contains(npc.gameObject.name)) continue;

                    string locationId = $"{scene.name}:{npc.gameObject.name}";
                    if (ItemRandomizer.GetGivenCount(TriggerIdPrefix + locationId) <= 0) continue;

                    npc.Deactivate(false);
                    disabled++;
                }
                if (disabled > 0)
                    Plugin.Log.LogInfo($"[LoreTriggerPatch] 场景 {scene.name} 已禁用 {disabled} 个已触发交互物");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[LoreTriggerPatch] 场景重扫异常: {ex}");
            }
        }
    }
}
