using BepInEx.Configuration;
using GlobalEnums;
using Random = System.Random;   // 明确使用 System.Random
using StartingAbilityPicker;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Reflection;
using System;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine;

// ItemRandomizer.cs - 完整修复版（消除 Random 歧义，使用 System.Random 明确命名）

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 存档数据访问接口（由 Plugin 实现，用于读写持久化数据）
    /// </summary>
    public interface ISaveDataAccessor
    {
        Dictionary<string, int> GetItemGivenCounts();
        Dictionary<string, string> GetTotalMappings();
        void SaveItemGivenCounts(Dictionary<string, int> counts);
        void SaveTotalMappings(Dictionary<string, string> mappings);
    }

    /// <summary>
    /// 物品随机核心逻辑
    /// 改造后：所有配置通过 Initialize 传入，不再依赖 Plugin 静态字段或 ConfigEntry
    /// 保留所有原有功能：物品池、虚拟奖励、纹章槽位解锁、技能给予等
    /// </summary>
    public static class ItemRandomizer
    {
        // ========== 状态（全部由 Initialize 设置） ==========
        private static List<IRandomReward> _unlimitedRewards = new();
        private static List<IRandomReward> _limitedRewards = new();
        private static IRandomReward _cachedCrestUnlocker;
        private static Dictionary<string, ToolCrest> _crestCache;  // 按 name 索引的纹章缓存，避免重复 FindObjectsOfTypeAll

        // 能力奖励 Id 集合（PreGeneratedMap 构建映射副本时按 GetAbilityLimit 加副本；与动态 IsAtMax 共用同一 limit）
        private static readonly HashSet<string> _abilityRewardIds = new(StringComparer.OrdinalIgnoreCase);
        public static IReadOnlyCollection<string> AbilityRewardIds => _abilityRewardIds;

        private static Random _globalRng;
        private static int _seed;

        // 通过接口访问持久化数据
        private static ISaveDataAccessor _saveData;

        // ========== 配置缓存 ==========
        private static float _virtualUnlimitedProbability = 0.1f;
        private static float _crestUnlockerProbability = 0.1f;
        private static float _normalLimitedProbability = 0.8f;
        private static bool _fullRandomModeActive = false;

        /// <summary>
        /// LoreReward 在有限池中的抽取权重：等于可用 lore 条目（地点）数，
        /// 使"随机日志"出现概率按 lore 地点数分配（其余奖励权重恒为 1）。
        /// </summary>
        private static int _loreRewardWeight = 1;

        // ========== 对外只读属性 ==========
        public static Random Rng => _globalRng;
        public static bool IsInitialized { get; private set; } = false;
        public static IReadOnlyList<IRandomReward> LimitedRewards => _limitedRewards;
        public static IReadOnlyList<IRandomReward> UnlimitedRewards => _unlimitedRewards;

        // ========== 初始化 ==========
        public static void Initialize(int seed, ItemRandomizerConfig config, ISaveDataAccessor saveDataAccessor, bool fullRandomMode)
        {
            _seed = seed;
            _globalRng = seed == 0 ? new Random() : new Random(seed);
            _saveData = saveDataAccessor;
            _fullRandomModeActive = fullRandomMode;

            if (config != null)
            {
                _virtualUnlimitedProbability = config.VirtualUnlimitedProbability;
                _crestUnlockerProbability = config.CrestUnlockerProbability;
                _normalLimitedProbability = config.NormalLimitedProbability;
            }

            BuildRewardPools(config, fullRandomMode);
            BuildCrestCache();
            RestoreAbilityGatesFromSave();
            IsInitialized = true;
        }

        /// <summary>
        /// 读档/重启后按发放记录重放 SAP 运行时权限位（AllowHeal/抗寒/游泳/二段跳）。
        /// 这些位是静态内存状态，不随游戏存档持久化；perm:heal 等已发放记录在
        /// ItemGivenCounts 中，据此恢复，否则读档后已获能力会静默失效。
        /// </summary>
        private static void RestoreAbilityGatesFromSave()
        {
            try
            {
                if (PlayerData.instance == null) return;
                var counts = GetGivenCountsDict();

                StartingAbilityPicker.StartingAbilityPickerAPI.SetColdResist(
                    counts.TryGetValue("ColdResist", out var c) && c > 0);
                StartingAbilityPicker.StartingAbilityPickerAPI.SetSwimOwned(
                    counts.TryGetValue("Swim", out var s) && s > 0);

                // 二段跳字段本身随游戏存档持久化（GiveSkill 写入 PlayerData），以字段为准
                StartingAbilityPicker.StartingAbilityPickerAPI.SetDoubleJumpOwned(
                    PlayerData.instance.hasDoubleJump);

                if (counts.TryGetValue("perm:heal", out var h) && h > 0)
                {
                    var perms = StartingAbilityPicker.StartingAbilityPickerAPI.GetMovementPermissions();
                    perms.AllowHeal = true;
                    StartingAbilityPicker.StartingAbilityPickerAPI.SetMovementPermissions(perms);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning($"[RewardCore] 能力门控状态恢复异常: {ex}");
            }
        }

        public static void RefreshPool()
        {
            if (!IsInitialized) return;
            BuildRewardPools(null, _fullRandomModeActive);
            BuildCrestCache();
        }

        // ========== 奖励池构建 ==========
        private static void BuildRewardPools(ItemRandomizerConfig config, bool fullRandomMode)
        {
            _limitedRewards.Clear();
            _unlimitedRewards.Clear(); 

            var allSaved = Resources.FindObjectsOfTypeAll<SavedItem>();
            var validSaved = new List<SavedItem>();
            foreach (var item in allSaved)
            {
                if (item == null) continue;
                
                Type t = item.GetType();
                if (item is EnemyJournalRecord) continue;
                if (t.Name.StartsWith("QuestTarget")) continue;
                if (t == typeof(Quest) || t == typeof(MainQuest) || t == typeof(SubQuest) || t == typeof(QuestRumour) ||
                    t == typeof(FullQuestBase) || t == typeof(BasicQuestBase) ||
                    t == typeof(QuestGroup) || t == typeof(QuestGroupBase))
                    continue;
                if (!IsValidItem(item)) continue;
                validSaved.Add(item);
            }

            var directionSkillItemNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Swift Step", "Harpoon Dash", "Drifter's Cloak", "Cling Grip", "EvaHeal"
            };

            foreach (var item in validSaved)
            {
                if (fullRandomMode && directionSkillItemNames.Contains(item.name))
                    continue;
                _limitedRewards.Add(new SavedItemReward(item));
            }

            BuildAbilityVirtualRewards(fullRandomMode);
            if (fullRandomMode) AddDirectionPermissionRewards();
            BuildUnlimitedVirtualRewards();
            BuildPreciousVirtualRewards();
            BuildLoreReward();
            BuildInspectPermitRewards();
            BuildProgressRewards();
            if (ItemLimitConfig.EnableMapRewards)
                _limitedRewards.AddRange(MapStationRewards.BuildMapRewards());
            if (ItemLimitConfig.EnableStationRewards)
                _limitedRewards.AddRange(MapStationRewards.BuildStationRewards());

            _cachedCrestUnlocker = _limitedRewards.FirstOrDefault(r => r.Id == "virt:UnlockCrestSlot");

            _rewardById = new Dictionary<string, IRandomReward>(_limitedRewards.Count + _unlimitedRewards.Count);
            foreach (var r in _limitedRewards)
                if (r != null && !_rewardById.ContainsKey(r.Id)) _rewardById[r.Id] = r;
            foreach (var r in _unlimitedRewards)
                if (r != null && !_rewardById.ContainsKey(r.Id)) _rewardById[r.Id] = r;

            Plugin.Log.LogInfo($"随机奖励池构建完成：无限类 {_unlimitedRewards.Count} 个，有限类 {_limitedRewards.Count} 个");
        }

        private static void BuildAbilityVirtualRewards(bool fullRandomMode)
        {
            var allSkillFields = new[]
            {
                "hasNeedleThrow", "hasThreadSphere", "hasSilkCharge", "hasSilkBomb", "hasSilkBossNeedle", "hasParry",
                "hasHarpoonDash", "hasNeedolin", "hasDash", "hasBrolly", "hasDoubleJump", "hasChargeSlash",
                "hasSuperJump", "hasWalljump", "HasSeenEvaHeal", "hasNeedolinMemoryPowerup", "hasFastTravelTeleport"
            };

            _abilityRewardIds.Clear();
            foreach (var field in allSkillFields)
            {
                if (fullRandomMode && IsDirectionSkillField(field))
                    continue;

                var (displayName, icon) = GetAbilityInfo(field);
                if (icon == null) icon = GetFallbackIcon();
                if (string.IsNullOrEmpty(displayName)) displayName = field;

                // 单实例入池；"双倍"由 PreGeneratedMap.BuildAllMappings 按 GetAbilityLimit(field)（默认 2）加副本，
                // 动态模式由 IsAtMax 按同一 limit 判断——两套模式共用同一配置，杜绝硬编码翻倍
                _limitedRewards.Add(new VirtualReward(field, displayName, icon,
                    () => GiveSkillWithMenuCheck(field),
                    () => GetGivenCount(field) >= ItemLimitConfig.GetAbilityLimit(field)
                ));
                _abilityRewardIds.Add(field);
            }

            // 丝之心：三梦境剧情道具（钟兽/蕾丝塔/守卫），效果 = 丝线恢复上限 +1
            // （AddToMaxSilkRegen，成就 ALL_SILK_HEARTS 目标 3）。完整给予逻辑在 SAP
            // （GiveSkill("SilkHeart") 分支 = 数值 + 官方弹窗一体），此处仅调用并放行梦境数值阻断。
            var silkHeartIcon = SpriteCache.Find("silk_heart_inv_icon");
            _limitedRewards.Add(new VirtualReward("SilkHeart", Locale.Get("丝之心"), silkHeartIcon,
                () => GrantSilkHeartViaPicker(),
                () => GetGivenCount("SilkHeart") >= ItemLimitConfig.GetAbilityLimit("SilkHeart")
            ));
            _abilityRewardIds.Add("SilkHeart");

            // 猎人日志：halfway_01 Nuu 剧情赠送（解锁图鉴系统 hasJournal）。
            // 原生演出由 RegionReplacePatch 拦截（弹窗+字段写入），数值只由此发放。
            // 图标 = prompts 图集 Journal_Prompt（官方 Journal 弹窗分支同款图）。
            var journalIcon = SpriteCache.Find("Journal_Prompt");
            _limitedRewards.Add(new VirtualReward("Journal", Locale.Get("猎人日志"), journalIcon,
                () => GiveJournal(),
                () => GetGivenCount("Journal") >= ItemLimitConfig.GetAbilityLimit("Journal")
            ));
            _abilityRewardIds.Add("Journal");

            // 寒冷抗性：雪绫披风（hasDoubleJump）拆分出的独立功能——寒冷积累封顶永不冻僵
            // （原生 TickFrostEffect 以 hasDoubleJump 同时控制二段跳+抗寒，AbilityGatePatch 已解耦）。
            // 图标复用披风技能图标。
            var (_, coldIcon) = GetAbilityInfo("hasDoubleJump");
            _limitedRewards.Add(new VirtualReward("ColdResist", Locale.Get("寒冷抗性"), coldIcon,
                () => GrantColdResist(),
                () => GetGivenCount("ColdResist") >= ItemLimitConfig.GetAbilityLimit("ColdResist")
            ));
            _abilityRewardIds.Add("ColdResist");

            // 游泳权限：未持有时触碰水面受陷阱伤弹回（与一代 water 陷阱一致，见 AbilityGatePatch）。
            // 游戏无原生生效道具，图标用通用回退图标。
            var swimIcon = GetFallbackIcon();
            _limitedRewards.Add(new VirtualReward("Swim", Locale.Get("游泳"), swimIcon,
                () => GrantSwim(),
                () => GetGivenCount("Swim") >= ItemLimitConfig.GetAbilityLimit("Swim")
            ));
            _abilityRewardIds.Add("Swim");
        }

        /// <summary>寒冷抗性发放：SAP 运行时权限位置位 + 顶部通知。</summary>
        private static void GrantColdResist()
        {
            StartingAbilityPicker.StartingAbilityPickerAPI.SetColdResist(true);
            Plugin.ShowNotification(Locale.Get("寒冷抗性"), 3f);
        }

        /// <summary>游泳发放：SAP 运行时权限位置位 + 顶部通知。</summary>
        private static void GrantSwim()
        {
            StartingAbilityPicker.StartingAbilityPickerAPI.SetSwimOwned(true);
            Plugin.ShowNotification(Locale.Get("游泳"), 3f);
        }

        /// <summary>丝之心发放：丝线恢复上限 +1 + 官方 UI Msg 全屏黑幕获得弹窗（每次发放都弹）。</summary>
        /// <summary>随机池发放丝之心进行中（RegionReplacePatch 的 AddToMaxSilkRegen
        /// 梦境阻断对此放行，否则随机到丝之心时数值会被误拦）。</summary>
        internal static bool GrantingSilkHeartFromPool;

        /// <summary>丝之心发放：调 SAP 统一入口（silkRegenMax +1 + 官方全屏弹窗），
        /// 调用期间置 GrantingSilkHeartFromPool 放行梦境数值阻断。图标 SAP 侧扫不到，由此外部传入。</summary>
        private static void GrantSilkHeartViaPicker()
        {
            GrantingSilkHeartFromPool = true;
            try
            {
                StartingAbilityPicker.StartingAbilityPickerAPI.GiveSilkHeart(
                    SpriteCache.Find("prompt_silkheart"));
            }
            finally
            {
                GrantingSilkHeartFromPool = false;
            }
        }

        /// <summary>猎人日志发放：解锁图鉴系统（hasJournal=true，EnemyJournalManager 击杀
        /// 记录从此生效）+ 官方 UI Msg 全屏获得弹窗（每次发放都弹）。</summary>
        public static void GiveJournal()
        {
            try
            {
                PlayerData.instance?.SetBool("hasJournal", true);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[技能随机] 猎人日志生效异常: {ex}");
            }
            // 官方全屏黑幕弹窗（统一集合入口：UI Msg Get Item.prefab + Msg Control FSM
            // Item="Journal" → Set Journal 分支，语言表 INV_NAME_JOURNAL/GET_JOURNAL_1/2 自动填入）
            bool shown = StartingAbilityPicker.OfficialMsgHelper.ShowJournal(
                SpriteCache.Find("Journal_Prompt"));
            if (!shown)
                Plugin.ShowNotification(Locale.Get("猎人日志"), 3f);
        }

        private static void AddDirectionPermissionRewards()
        {
            _limitedRewards.Add(new DirectionPermissionReward(Locale.Get("冲刺左"), "hasDash", false, true));
            _limitedRewards.Add(new DirectionPermissionReward(Locale.Get("冲刺右"), "hasDash", true, false));
            _limitedRewards.Add(new DirectionPermissionReward(Locale.Get("飞针左"), "hasHarpoonDash", false, true));
            _limitedRewards.Add(new DirectionPermissionReward(Locale.Get("飞针右"), "hasHarpoonDash", true, false));
            _limitedRewards.Add(new DirectionPermissionReward(Locale.Get("漂浮左"), "hasBrolly", false, true));
            _limitedRewards.Add(new DirectionPermissionReward(Locale.Get("漂浮右"), "hasBrolly", true, false));
            _limitedRewards.Add(new DirectionPermissionReward(Locale.Get("壁跳左"), "hasWalljump", false, true));
            _limitedRewards.Add(new DirectionPermissionReward(Locale.Get("壁跳右"), "hasWalljump", true, false));

            // 攻击方向/回血权限：单实例入池，双倍副本由 BuildAllMappings 按 GetDirectionLimit（默认 2）控制；
            // 攻击/回血无方向语义，allowRight/allowLeft 传 false（Id 生成已按 isAttack/isHeal 去掉后缀）
            _limitedRewards.Add(new DirectionPermissionReward(Locale.Get("上劈权限"), "upward", false, false, true));
            _limitedRewards.Add(new DirectionPermissionReward(Locale.Get("左劈权限"), "left", false, false, true));
            _limitedRewards.Add(new DirectionPermissionReward(Locale.Get("右劈权限"), "right", false, false, true));
            _limitedRewards.Add(new DirectionPermissionReward(Locale.Get("回血权限"), "heal", false, false, false, true));
        }

        private static void BuildUnlimitedVirtualRewards()
        {
            // 1. 灵丝3格（固定）
            if (ItemLimitConfig.EnableInfSilk)
                _unlimitedRewards.Add(new VirtualReward("virt:Silk_3", Locale.Get("灵丝(3)"), SpriteCache.Find("icon_silk_materium"), () =>
                {
                    var hero = HeroController.instance;
                    if (hero != null) for (int i = 0; i < 3; i++) hero.AddSilk(1, false);
                }, () => false));

            // 2. 甲壳300（固定）
            if (ItemLimitConfig.EnableInfShards300)
                _unlimitedRewards.Add(new VirtualReward("virt:Shards_300", Locale.Get("甲壳300"), SpriteCache.Find("Shell_shard_icon"), () => HeroController.instance?.AddShards(300), () => false));

            // 3. 随机蓝血（1~6格，逐格给予，使用协程）
            if (ItemLimitConfig.EnableInfBlueHealth)
                _unlimitedRewards.Add(new VirtualReward("virt:BlueHealth_Random", Locale.Get("随机蓝血"), SpriteCache.Find("Icon_Inv_Blue_Health_Blood"), () =>
                {
                    int total = UnityEngine.Random.Range(1, 7); // 1~6
                    Plugin.Instance?.StartCoroutine(AddBlueHealthOverTime(total, 0.5f));
                }, () => false));

            // 4. 随机货币（金额0~90，商店风格）
            if (ItemLimitConfig.EnableInfGeo300)  // 复用 EnableInfGeo300 作为总开关，但金额随机
                _unlimitedRewards.Add(new VirtualReward("virt:RandomCoin", Locale.Get("随机念珠"), SpriteCache.Find("I_rosary_icon_clean"), () =>
                {
                    int amount = Rng.Next(0, 91);
                    HeroController.instance?.AddGeo(amount);
                }, () => false));
        }

        private static IEnumerator AddBlueHealthOverTime(int total, float interval)
        {
            for (int i = 0; i < total; i++)
            {
                EventRegister.SendEvent(EventRegisterEvents.AddBlueHealth, null);
                if (i < total - 1)
                    yield return new WaitForSeconds(interval);
            }
        }

        private static void BuildPreciousVirtualRewards()
        {
            _limitedRewards.Add(new VirtualReward("virt:HeartPiece", Locale.Get("面具碎片"), SpriteCache.Find("Heart_Piece_01"), () =>
            {
                NativePickupGiver.GiveHeartPiece();
            }, () => GetGivenCount("virt:HeartPiece") >= ItemLimitConfig.GetPreciousLimit("virt:HeartPiece")));

            _limitedRewards.Add(new VirtualReward("virt:SpoolPart", Locale.Get("丝轴碎片"), SpriteCache.Find("spool_upgrade_pickup"), () =>
            {
                Plugin.Instance?.StartCoroutine(NativePickupGiver.GiveSpoolPart());
            }, () => GetGivenCount("virt:SpoolPart") >= ItemLimitConfig.GetPreciousLimit("virt:SpoolPart")));

            // 丝线恢复上限由随机池 SilkHeart（丝之心，SAP 完整给予 = 数值 + 官方弹窗）承担，
            // 不再设简易版 virt:MaxSilkRegenUp（曾与丝之心效果重复且无弹窗）。

            _limitedRewards.Add(new VirtualReward(
                "virt:UnlockCrestSlot",
                Locale.Get("纹章槽位解锁器"),
                SpriteCache.Find("Tool_slot_lock_ring"),
                () => TryUnlockCrestSlot(),
                () => GetGivenCount("virt:UnlockCrestSlot") >= ItemLimitConfig.GetPreciousLimit("virt:UnlockCrestSlot")
            ));

            // 额外工具槽（F4 同款：UnlockedExtraYellowSlot/UnlockedExtraBlueSlot + 官方全屏演出）。
            // 放入随机池：映射中的任何点位可能抽到，先黄后蓝各一次。
            _limitedRewards.Add(new VirtualReward(
                "virt:ExtraYellowSlot",
                Locale.Get("额外工具槽 (黄)"),
                SpriteCache.Find("Tool_slot_lock_ring"),
                () => TryGiveExtraSlot(ToolItemType.Yellow),
                () => PlayerData.instance != null && ExtraSlotUnlocked(ToolItemType.Yellow)
            ));

            _limitedRewards.Add(new VirtualReward(
                "virt:ExtraBlueSlot",
                Locale.Get("额外工具槽 (蓝)"),
                SpriteCache.Find("Tool_slot_lock_ring"),
                () => TryGiveExtraSlot(ToolItemType.Blue),
                () => PlayerData.instance != null && ExtraSlotUnlocked(ToolItemType.Blue)
            ));
        }

        private static void BuildLoreReward()
        {
            if (!ItemLimitConfig.EnableLoreReward)
                return;
            // 权重 = 可用 lore 条目数；取不到时退化为 1（保持原行为）
            int keyCount = LoreRandomizer.LoreTable.Length;
            _loreRewardWeight = keyCount > 0 ? keyCount : 1;
            _limitedRewards.Add(new LoreReward());
        }

        /// <summary>
        /// 进度类奖励：监狱三把钥匙（叠加权限）+ 五圣钟解锁标志。
        /// 钥匙 = HasSlabKeyA/B/C 三个 bool（原版由 Slab 区域拾取点设置，最终审判门检查三者全 true）；
        /// 五圣钟解锁 = 一次性置位五个 bellShrine* bool，让最终审判者挑战直接可进
        /// （房间随机复杂，钟椅不再逐个找，给一个整体解锁标志）。
        /// </summary>
        private static void BuildProgressRewards()
        {
            // 三把监狱钥匙（叠加权限，各只能获得一次）
            _limitedRewards.Add(new VirtualReward("virt:SlabKeyA", Locale.Get("怠惰之钥"), SpriteCache.Find("I_slab_key"),
                () => { GiveProgressBool("HasSlabKeyA"); ShowFakeCollectablePopup("Slab Key A"); },
                () => HasProgressBool("HasSlabKeyA")));

            _limitedRewards.Add(new VirtualReward("virt:SlabKeyB", Locale.Get("异端之钥"), SpriteCache.Find("I_slab_key_brass"),
                () => { GiveProgressBool("HasSlabKeyB"); ShowFakeCollectablePopup("Slab Key B"); },
                () => HasProgressBool("HasSlabKeyB")));

            _limitedRewards.Add(new VirtualReward("virt:SlabKeyC", Locale.Get("叛教之钥"), SpriteCache.Find("I_slab_key_gold"),
                () => { GiveProgressBool("HasSlabKeyC"); ShowFakeCollectablePopup("Slab Key C"); },
                () => HasProgressBool("HasSlabKeyC")));

            // 五圣钟解锁标志（一次性整体解锁，最终审判者挑战可进）
            _limitedRewards.Add(new VirtualReward("virt:Bellshrines", Locale.Get("五圣钟解锁"), SpriteCache.Find("QI_Main_bellshrines"),
                () =>
                {
                    foreach (var b in FiveBellBools)
                        GiveProgressBool(b);
                },
                () =>
                {
                    var pd = PlayerData.instance;
                    return pd != null && Array.TrueForAll(FiveBellBools, b => pd.GetBool(b));
                }));
        }
        private static AssetBundle _fakeCollectablesBundle = null;
        private static readonly string AssetPathPrefix = "Assets/Data Assets/Collectables/Fake Collectables/";
        private static bool _bundleSearchDone = false;

        private static void ShowFakeCollectablePopup(string itemName)
        {
            try
            {
                // 1. 如果还没找到 bundle，遍历一次并缓存
                if (!_bundleSearchDone)
                {
                    var bundles = AssetBundle.GetAllLoadedAssetBundles();
                    foreach (var bundle in bundles)
                    {
                        if (bundle == null) continue;
                        // 测试加载一个已知资源来验证是否是目标 bundle
                        string testPath = AssetPathPrefix + "Slab Key A.asset";
                        var testItem = bundle.LoadAsset<SavedItem>(testPath);
                        if (testItem != null)
                        {
                            _fakeCollectablesBundle = bundle;
                            Plugin.Log.LogInfo($"[钥匙] 缓存 bundle: {bundle.name}");
                            break;
                        }
                    }
                    _bundleSearchDone = true;
                }

                // 2. 用缓存的 bundle 加载资源
                if (_fakeCollectablesBundle != null)
                {
                    string path = AssetPathPrefix + itemName + ".asset";
                    var item = _fakeCollectablesBundle.LoadAsset<SavedItem>(path);
                    if (item != null)
                    {
                        item.Get(true);
                        Plugin.Log.LogInfo($"[钥匙] 弹窗显示: {itemName}");
                        return;
                    }
                    else
                    {
                        Plugin.Log.LogWarning($"[钥匙] 从缓存 bundle 加载失败: {path}");
                    }
                }
                else
                {
                    Plugin.Log.LogWarning($"[钥匙] 未找到 FakeCollectables bundle");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[钥匙] 弹窗失败 {itemName}: {ex}");
            }
        }
        /// <summary>五圣钟（最终审判者挑战的 5 个钟椅 bool；Enclave 涉及剧情不做改动）</summary>
        private static readonly string[] FiveBellBools =
        {
            "bellShrineBoneForest", "bellShrineWilds", "bellShrineGreymoor",
            "bellShrineShellwood", "bellShrineBellhart"
        };

        private static void GiveProgressBool(string boolName)
        {
            var pd = PlayerData.instance;
            if (pd == null) return;
            try
            {
                pd.SetBool(boolName, true);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[进度奖励] 置位 {boolName} 失败: {ex}");
            }
        }

        private static bool HasProgressBool(string boolName)
        {
            var pd = PlayerData.instance;
            return pd != null && pd.GetBool(boolName);
        }

        /// <summary>
        /// 权限类 inspect 地点权限物入池：每个地点一个"允许走原生流程"的许可（virt:Permit:scene:name），
        /// 玩家随机获得后该地点放行原生流程（记录二）；inspect 每地只触发一次随机（记录一，见 LoreTriggerPatch）。
        /// </summary>
        private static void BuildInspectPermitRewards()
        {
            if (!ItemLimitConfig.EnableInspectPermit)
                return;
            foreach (var e in InspectPermitRewards.Permits)
                _limitedRewards.Add(new InspectPermissionReward(e.Scene, e.Name));
        }

        // ========== 辅助方法 ==========
        private static void BuildCrestCache()
        {
            var allCrests = Resources.FindObjectsOfTypeAll<ToolCrest>();
            _crestCache = new Dictionary<string, ToolCrest>(allCrests.Length, StringComparer.OrdinalIgnoreCase);
            foreach (var c in allCrests)
                if (c != null && !string.IsNullOrEmpty(c.name))
                    _crestCache[c.name] = c;
        }

        private static ToolCrest FindCrestCached(string crestName)
        {
            if (_crestCache == null) BuildCrestCache();
            _crestCache.TryGetValue(crestName, out var crest);
            return crest;
        }

        private static bool IsDirectionSkillField(string field)
        {
            return field == "hasDash" || field == "hasHarpoonDash" || field == "hasBrolly" ||
                   field == "hasWalljump" || field == "HasSeenEvaHeal";
        }

        private static bool IsSystemMenuOpen()
        {
            try
            {
                if (UIManager.instance != null && UIManager.instance.uiState == UIState.PAUSED)
                    return true;
                if (GameObject.Find("PauseMenu")?.activeInHierarchy == true)
                    return true;
            }
            catch { }
            return false;
        }

        private static IEnumerator WaitForSystemMenuThenGive(string skillField)
        {
            while (IsSystemMenuOpen())
                yield return null;
            yield return null;
            GiveSkillNow(skillField);
        }

        /// <summary>能力字段是否已拥有（三个特殊字段用各自的真实解锁字段判断）。</summary>
        private static bool IsSkillOwned(string field)
        {
            var pd = PlayerData.instance;
            if (pd == null) return false;
            switch (field)
            {
                case "hasNeedolinMemoryPowerup": return pd.hasNeedolinMemoryPowerup;
                case "hasFastTravelTeleport": return pd.UnlockedFastTravelTeleport;
                case "HasSeenEvaHeal": return pd.HasBoundCrestUpgrader;
                default:
                    var fi = typeof(PlayerData).GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                    return fi != null && fi.FieldType == typeof(bool) && (bool)fi.GetValue(pd);
            }
        }

        /// <summary>
        /// 直接发放：已拥有时 SkillRandomizer.GiveSkill 会静默返回（无任何反馈），
        /// 这里检测到已拥有就直接补弹获得弹窗，保证抽到必有感知。
        /// </summary>
        private static void GiveSkillNow(string skillField)
        {
            if (IsSkillOwned(skillField))
            {
                var (name, icon) = GetAbilityInfo(skillField);
                StartingAbilityPicker.Plugin.ShowSkillPopup(icon, name);
                return;
            }
            StartingAbilityPicker.StartingAbilityPickerAPI.GiveSkill(skillField);
        }

        private static void GiveSkillWithMenuCheck(string skillField)
        {
            if (IsSystemMenuOpen())
            {
                Plugin.Instance.StartCoroutine(WaitForSystemMenuThenGive(skillField));
            }
            else
            {
                GiveSkillNow(skillField);
            }
        }

        private static (string displayName, Sprite icon) GetAbilityInfo(string abilityField)
        {
            // 通过 StartingAbilityPickerAPI 获取（已添加 GetDisplayName/GetIcon 方法）
            try
            {
                string name = StartingAbilityPicker.StartingAbilityPickerAPI.GetDisplayName(abilityField);
                Sprite icon = StartingAbilityPicker.StartingAbilityPickerAPI.GetIcon(abilityField);
                if (!string.IsNullOrEmpty(name) && icon != null)
                    return (name, icon);
            }
            catch { }

            string fallbackName = abilityField switch
            {
                "hasDash" => Locale.Get("疾风步"),
                "hasBrolly" => Locale.Get("流浪者披风"),
                "hasDoubleJump" => Locale.Get("雪绒披风"),
                "hasSuperJump" => Locale.Get("灵丝升腾"),
                "hasWalljump" => Locale.Get("蛛攀术"),
                "HasSeenEvaHeal" => Locale.Get("风铃摇"),
                _ => abilityField
            };
            Sprite fallbackIcon = GetFallbackIcon();
            return (fallbackName, fallbackIcon);
        }

        private static Sprite _fallbackIcon;
        private static Sprite GetFallbackIcon() => _fallbackIcon ??= CreateFallbackIcon();
        private static Sprite CreateFallbackIcon()
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.zero);
        }

        // ========== 持久化数据访问 ==========
        private static Dictionary<string, int> GetGivenCountsDict() => _saveData?.GetItemGivenCounts() ?? new Dictionary<string, int>();
        private static Dictionary<string, string> GetTotalMappingsDict() => _saveData?.GetTotalMappings() ?? new Dictionary<string, string>();
        private static void SaveGivenCounts() => _saveData?.SaveItemGivenCounts(GetGivenCountsDict());
        private static void SaveTotalMappings() => _saveData?.SaveTotalMappings(GetTotalMappingsDict());

        public static int GetGivenCount(string id)
        {
            var dict = GetGivenCountsDict();
            return dict.TryGetValue(id, out int count) ? count : 0;
        }

        /// <summary>是否存在任一以指定前缀开头的已给予记录（用于场景加载期短路全量扫描）。</summary>
        public static bool HasAnyGivenCountWithPrefix(string prefix)
        {
            foreach (var key in GetGivenCountsDict().Keys)
            {
                if (!string.IsNullOrEmpty(key) && key.StartsWith(prefix, System.StringComparison.Ordinal))
                    return true;
            }
            return false;
        }

        public static void AddGivenCount(string id)
        {
            var dict = GetGivenCountsDict();
            if (dict.ContainsKey(id)) dict[id]++;
            else dict[id] = 1;
            SaveGivenCounts();
        }

        public static void RecordMapping(string sourceKey, string targetKey)
        {
            var dict = GetTotalMappingsDict();
            if (dict.TryGetValue(sourceKey, out var existing) && existing == targetKey) return;
            dict[sourceKey] = targetKey;
            SaveTotalMappings();
        }

        public static string GetMapping(string sourceKey)
        {
            var dict = GetTotalMappingsDict();
            dict.TryGetValue(sourceKey, out var target);
            return target;
        }

        // ========== 公共接口 ==========
        public static IRandomReward GetRandomReward()
        {
            double rand = _globalRng.NextDouble();

            if (rand < _virtualUnlimitedProbability)
            {
                if (_unlimitedRewards.Count > 0)
                    return _unlimitedRewards[_globalRng.Next(_unlimitedRewards.Count)];
                else
                    rand = _virtualUnlimitedProbability + 0.01;
            }

            if (rand < _virtualUnlimitedProbability + _crestUnlockerProbability)
            {
                if (_cachedCrestUnlocker != null && !_cachedCrestUnlocker.IsAtMax())
                    return _cachedCrestUnlocker;
                else
                    rand = _virtualUnlimitedProbability + _crestUnlockerProbability + 0.01;
            }

            // 有限池按权重抽取：LoreReward 权重 = lore 地点数，其余奖励权重恒为 1。
            // 这样"随机日志"的出现概率随 lore 地点数线性放大，而不是只占 1 个名额。
            int totalWeight = 0;
            foreach (var r in _limitedRewards)
                if (r != _cachedCrestUnlocker && !r.IsAtMax())
                    totalWeight += (r is LoreReward ? _loreRewardWeight : 1);

            if (totalWeight > 0)
            {
                int target = _globalRng.Next(totalWeight);
                foreach (var r in _limitedRewards)
                {
                    if (r != _cachedCrestUnlocker && !r.IsAtMax())
                    {
                        int w = (r is LoreReward ? _loreRewardWeight : 1);
                        if (target < w)
                            return r;
                        target -= w;
                    }
                }
            }

            if (_unlimitedRewards.Count > 0)
                return _unlimitedRewards[_globalRng.Next(_unlimitedRewards.Count)];

            return new VirtualReward("virt:FallbackGeo", Locale.Get("保底念珠"), null, () => HeroController.instance?.AddGeo(10), () => false);
        }

        public static SavedItem GetRandomItem()
        {
            // 仅统计真实物品奖励（SavedItemReward）；跳过 LoreReward 等纯效果奖励，
            // 避免包成 ProxySavedItem 返回破损物品
            int availableCount = 0;
            foreach (var r in _limitedRewards)
                if (r is SavedItemReward && !r.IsAtMax())
                    availableCount++;

            if (availableCount == 0) return null;
            int targetIndex = _globalRng.Next(availableCount);
            foreach (var r in _limitedRewards)
            {
                if (r is SavedItemReward saved && !saved.IsAtMax())
                {
                    if (targetIndex == 0)
                        return saved.Item;
                    targetIndex--;
                }
            }
            return null;
        }

        public static SavedItem PeekRandomItem(Random externalRng)
        {
            var candidates = _limitedRewards.OfType<SavedItemReward>().ToList();
            if (candidates.Count == 0) return null;
            return candidates[externalRng.Next(candidates.Count)].Item;
        }

        public static List<SavedItem> GetAllItems() => _limitedRewards.OfType<SavedItemReward>().Select(r => r.Item).Where(i => i != null).ToList();

        public static bool GiveRandomReward()
        {
            var reward = GetRandomReward();
            if (reward == null) return false;
            reward.Give();
            AddGivenCount(reward.Id);
            return true;
        }

        public static void ResetAllData()
        {
            GetGivenCountsDict().Clear();
            GetTotalMappingsDict().Clear();
            SaveGivenCounts();
            SaveTotalMappings();
            PreGeneratedMap.Reset();
            Plugin.Log.LogInfo("所有随机奖励次数和映射表已重置");
        }

        /// <summary>
        /// 按奖励 Id 反查奖励实例（预生成映射表使用）。
        /// Id 体系：SavedItemReward=物品名，VirtualReward/LoreReward="virt:xxx"，DirectionPermissionReward="perm:xxx"。
        /// </summary>
        private static Dictionary<string, IRandomReward> _rewardById;
        public static IRandomReward FindRewardById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (_rewardById != null && _rewardById.TryGetValue(id, out var hit)) return hit;
            foreach (var r in _limitedRewards)
                if (r != null && r.Id == id) return r;
            foreach (var r in _unlimitedRewards)
                if (r != null && r.Id == id) return r;
            return null;
        }

        // ========== 纹章槽位解锁器辅助 ==========
        public static ToolItemType? LastUnlockedSlotType { get; private set; }

        public static string GetSlotTypeName(ToolItemType type)
        {
            if (type.ToString().Contains("Attack")) return Locale.Get("攻击槽");
            if (type.ToString().Contains("Tool") || type.ToString().Contains("Item")) return Locale.Get("工具槽");
            if (type.ToString().Contains("Spell")) return Locale.Get("法术槽");
            return type.ToString();
        }

        public static void TryUnlockCrestSlot()
        {
            LastUnlockedSlotType = null;
            var pd = PlayerData.instance;
            if (pd == null) return;

            string crestId = pd.CurrentCrestID;
            if (string.IsNullOrEmpty(crestId))
            {
                var unlockedCrests = new List<string>();
                foreach (var kv in pd.ToolEquips.Enumerate())
                    if (kv.Value.IsUnlocked) unlockedCrests.Add(kv.Key);
                if (unlockedCrests.Count == 0) return;
                crestId = unlockedCrests[UnityEngine.Random.Range(0, unlockedCrests.Count)];
            }

            var crest = FindCrestCached(crestId);
            if (crest == null) return;

            var data = crest.SaveData;
            if (data.Slots == null || data.Slots.Count == 0)
            {
                data.Slots = new List<ToolCrestsData.SlotData>();
                for (int i = 0; i < crest.Slots.Length; i++)
                    data.Slots.Add(new ToolCrestsData.SlotData { IsUnlocked = !crest.Slots[i].IsLocked });
                crest.SaveData = data;
            }

            int targetIndex = -1;
            ToolItemType? slotType = null;
            for (int i = 0; i < data.Slots.Count && i < crest.Slots.Length; i++)
            {
                if (!data.Slots[i].IsUnlocked)
                {
                    string typeStr = crest.Slots[i].Type.ToString();
                    if (!typeStr.Contains("Spell"))
                    {
                        targetIndex = i;
                        slotType = crest.Slots[i].Type;
                        break;
                    }
                }
            }

            if (targetIndex == -1)
            {
                HeroController.instance?.AddGeo(100);
                Plugin.ShowNotification(Locale.Get("纹章槽位已满，获得 100 念珠"), 2f);
                return;
            }

            var slotData = data.Slots[targetIndex];
            slotData.IsUnlocked = true;
            data.Slots[targetIndex] = slotData;
            crest.SaveData = data;

            LastUnlockedSlotType = slotType;
            EventRegister.SendEvent(EventRegisterEvents.EquipsChangedEvent, null);
            string typeName = slotType.HasValue ? GetSlotTypeName(slotType.Value) : Locale.Get("未知");
            Plugin.ShowNotification(string.Format(Locale.Get("纹章槽位 {0} ({1}) 已解锁！"), targetIndex + 1, typeName), 3f);
        }

        /// <summary>额外工具槽（F4 同款）是否已解锁。</summary>
        public static bool ExtraSlotUnlocked(ToolItemType slotType)
        {
            var pd = PlayerData.instance;
            if (pd == null) return false;
            string field = slotType == ToolItemType.Blue ? "UnlockedExtraBlueSlot" : "UnlockedExtraYellowSlot";
            var fi = typeof(PlayerData).GetField(field, BindingFlags.Instance | BindingFlags.Public);
            if (fi == null || fi.FieldType != typeof(bool)) return false;
            return (bool)fi.GetValue(pd);
        }

        /// <summary>发放额外工具槽奖励：解锁字段 + 官方全屏演出（ExtraToolSlotUIMsg.Spawn）。</summary>
        public static void TryGiveExtraSlot(ToolItemType slotType)
        {
            try
            {
                var pd = PlayerData.instance;
                if (pd == null)
                {
                    Plugin.Log.LogError("[额外槽] PlayerData 不可用");
                    return;
                }
                string field = slotType == ToolItemType.Blue ? "UnlockedExtraBlueSlot" : "UnlockedExtraYellowSlot";
                var fi = typeof(PlayerData).GetField(field, BindingFlags.Instance | BindingFlags.Public);
                if (fi == null || fi.FieldType != typeof(bool))
                {
                    Plugin.Log.LogError($"[额外槽] 字段不存在: {field}");
                    return;
                }
                if ((bool)fi.GetValue(pd))
                {
                    Plugin.Log.LogInfo($"[额外槽] {field} 已解锁过，给予 100 念珠替代");
                    HeroController.instance?.AddGeo(100);
                    return;
                }
                fi.SetValue(pd, true);
                Plugin.Log.LogInfo($"[额外槽] 发放: {field} = true");
                // 官方弹窗统一集合入口（UI Msg Crest Evolve prefab，官方 FSM 演出）
                StartingAbilityPicker.OfficialMsgHelper.ShowExtraSlot(slotType);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[额外槽] 发放异常: {ex}");
            }
        }

        public static bool IsAllCrestSlotsUnlocked()
        {
            var pd = PlayerData.instance;
            if (pd == null) return true;
            string crestId = pd.CurrentCrestID;
            if (string.IsNullOrEmpty(crestId)) return false;
            var crest = FindCrestCached(crestId);
            if (crest == null) return false;
            var data = crest.SaveData;
            if (data.Slots == null || data.Slots.Count == 0) return false;
            for (int i = 0; i < data.Slots.Count && i < crest.Slots.Length; i++)
            {
                if (!crest.Slots[i].Type.ToString().Contains("Spell") && !data.Slots[i].IsUnlocked)
                    return false;
            }
            return true;
        }

        private static bool IsValidItem(SavedItem item) => !(item == null || string.IsNullOrEmpty(item.name) || ExcludedNames.Contains(item.name));

        public static readonly HashSet<string> ExcludedNames = new()
        {
            "Steel Spines", "Common Spine", "Plasmium", "Sliver Bell",
            "Seared Organ", "Shredded Organ", "Skewered Organ", "Ragpelt",
            "Invalid Item Template",
        };
    }
}

// RewardTypes.cs - 补充缺失的 Item 属性和 LimitedVirtualReward

namespace SilksongItemRandomizer
{
    // ========== 奖励接口 ==========
    public interface IRandomReward
    {
        string Id { get; }
        string DisplayName { get; }
        Sprite Icon { get; }
        void Give();
        bool IsAtMax();
    }

    // ========== 配置类 ==========
    // 默认值与 ItemLimitConfig.CodeDefaultInts 保持完全一致（能力/攻击回血=2 双倍，其余=1）
    public class ItemLimitSettings
    {
        public int SkillItem = 1, Relic = 1, OtherItem = 1;
        public int UpSlash = 2, LeftSlash = 2, RightSlash = 2;
        public int DashLeft = 1, DashRight = 1;
        public int HarpoonLeft = 1, HarpoonRight = 1;
        public int FloatLeft = 1, FloatRight = 1;
        public int WallJumpLeft = 1, WallJumpRight = 1;
        public int Heal = 2;
        public int NeedleThrow = 2, ThreadSphere = 2, HarpoonDash = 2;
        public int SilkCharge = 2, SilkBomb = 2, SilkBossNeedle = 2;
        public int Needolin = 2, Parry = 2, NeedolinMemory = 2, FastTravel = 2, EvaHeal = 2;
        public int Dash = 2, Brolly = 2, DoubleJump = 2, SuperJump = 2, WallJump = 2, ChargeSlash = 2;
        public int SimpleKey = 4;
        public int HeartPiece = 20, SpoolPart = 18, MaxSilkRegenUp = 2;
        public int UnlockCrestSlot = 2;
    }

    public class InfinitePoolSettings
    {
        public bool Silk = true;
        public bool BlueHealth = true;
        public bool Geo300 = true;
        public bool Shards300 = true;
    }

    // ========== 物品/技能奖励的具体实现 ==========
    public class SavedItemReward : IRandomReward
    {
        private SavedItem _item;
        private readonly int _limit;
        private static bool _displayNameErrorLogged = false;
        private static bool _iconErrorLogged = false;

        public string Id => _item?.name ?? "null";
        public string DisplayName
        {
            get
            {
                if (_item == null) return Locale.Get("空物品");
                if (_item is ToolCrest) return _item.name;
                try { return _item.GetPopupName(); }
                catch { return _item.name; }
            }
        }
        public Sprite Icon
        {
            get
            {
                if (_item == null) return null;
                try { return _item.GetPopupIcon(); }
                catch { return null; }
            }
        }
        public SavedItem Item => _item;  // 添加公开属性
        public SavedItemReward(SavedItem item) { _item = item; _limit = ItemLimitConfig.GetItemTypeLimit(item); }

        /// <summary>
        /// 定向给予 _item 本体（拾取点按表 / 商店货架 / 保底等「已确定给哪个物品」的场景）。
        /// 必须走 TryGetPatch.BypassRandom 保护：否则 _item.TryGet 会被 TryGetPatch 拦截并
        /// 重新随机成一个新物品（表现为「买到的和货架不是同一个」）。
        /// 保存/恢复 BypassRandom 与 PendingKey，对调用方完全透明，不影响外部 PendingKey 分发。
        /// </summary>
        public void Give()
        {
            if (_item == null) return;
            bool prevBypass = TryGetPatch.BypassRandom;
            string prevPending = PreGeneratedMap.PendingKey;
            TryGetPatch.BypassRandom = true;
            try
            {
                _item.TryGet(false, true);
                // 随机奖励为纹章：原生 ToolCrest.Get() 只解锁不弹窗（弹窗入口仅神社 FSM），
                // 这里补全原生"获得纹章"完整演出（与神社演出顺序一致）：
                //   弹窗（跳过键控制，自实例化自管理）-> 弹窗结束后自动装配（ToolItemManager.AutoEquip
                //   换当前纹章 + 发 TOOL EQUIPS CHANGED + POST 事件 -> BindOrbHudFrame 播 HUD 轮盘
                //   装配动画），参数 (false, true) 与原生 AutoEquipCrestV2.OnEnter 完全一致
                if (_item is ToolCrest crest)
                    StartingAbilityPicker.OfficialMsgHelper.ShowToolCrest(crest, () =>
                    {
                        try
                        {
                            ToolItemManager.AutoEquip(crest, false, true);
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.LogError($"[纹章弹窗] 自动装配 {crest.name} 失败: {ex}");
                        }
                    });
            }
            finally
            {
                TryGetPatch.BypassRandom = prevBypass;
                PreGeneratedMap.PendingKey = prevPending;
            }
        }

        public bool IsAtMax() => ItemRandomizer.GetGivenCount(Id) >= _limit;
    }

    public class VirtualReward : IRandomReward
    {
        private readonly string _id, _displayName;
        private readonly Sprite _icon;
        private readonly Action _giveAction;
        private readonly Func<bool> _isAtMax;
        public VirtualReward(string id, string displayName, Sprite icon, Action giveAction, Func<bool> isAtMax)
        { _id = id; _displayName = displayName; _icon = icon; _giveAction = giveAction; _isAtMax = isAtMax; }
        public string Id => _id;
        public string DisplayName => _displayName;
        public Sprite Icon => _icon;
        public void Give() => _giveAction?.Invoke();
        public bool IsAtMax() => _isAtMax?.Invoke() ?? false;
    }

    /// <summary>
    /// 权限类 inspect 地点的权限物：放进随机池，玩家获得后该地点"允许"走原生流程
    /// （把存储的不允许变成允许）。纯记录型奖励，发放记录由触发方统一 AddGivenCount。
    /// </summary>
    public class InspectPermissionReward : IRandomReward
    {
        private readonly string _id, _displayName;
        public InspectPermissionReward(string scene, string name)
        {
            _id = "virt:Permit:" + scene + ":" + name;
            _displayName = $"地点权限:{scene}/{name}";
        }
        public string Id => _id;
        public string DisplayName => _displayName;
        public Sprite Icon => SpriteCache.Find("Map_prompt");
        public void Give() { }
        public bool IsAtMax() => ItemRandomizer.GetGivenCount(Id) >= 1;
    }

    public class DirectionPermissionReward : IRandomReward
    {
        private readonly string _id;
        private readonly string _displayName;
        private readonly Sprite _icon;
        private readonly string _skillField;
        private readonly bool _allowRight, _allowLeft;
        private readonly bool _isAttack, _isHeal;
        public DirectionPermissionReward(string displayName, string skillField, bool allowRight, bool allowLeft, bool isAttack = false, bool isHeal = false)
        {
            // 攻击/回血权限无方向语义，Id 不带方向后缀（与 ItemLimitConfig.GetDirectionLimit 的
            // perm:upward/left/right/heal 键一致，否则 LimitUpSlash 等四项配置永不生效）。
            // ★ 必须用【参数】isAttack/isHeal 判断：用字段 _isAttack/_isHeal 会在赋值前读到
            //   默认 false（字段赋值在 _id 之后），导致永远走带下划线的分支生成 perm:upward_。
            _id = isAttack || isHeal
                ? $"perm:{skillField}"
                : $"perm:{skillField}_{(allowRight ? "R" : "")}{(allowLeft ? "L" : "")}";
            _displayName = displayName;
            _skillField = skillField;
            _allowRight = allowRight;
            _allowLeft = allowLeft;
            _isAttack = isAttack;
            _isHeal = isHeal;
            _icon = null;
        }
        public string Id => _id;
        public string DisplayName => _displayName;
        private Sprite _cachedIcon;
        private bool _iconCached;
        public Sprite Icon
        {
            get
            {
                if (!_iconCached) { _cachedIcon = GetIcon(); _iconCached = true; }
                return _cachedIcon;
            }
        }
        public void Give()
        {
            if (_isAttack)
            {
                // 累加语义：读当前权限 OR 上新方向，避免覆盖之前获得的其他攻击方向
                var (up, left, right) = StartingAbilityPicker.StartingAbilityPickerAPI.GetAttackPermissions();
                if (_skillField == "upward") StartingAbilityPicker.StartingAbilityPickerAPI.SetAttackPermissions(true, left, right);
                else if (_skillField == "left") StartingAbilityPicker.StartingAbilityPickerAPI.SetAttackPermissions(up, true, right);
                else if (_skillField == "right") StartingAbilityPicker.StartingAbilityPickerAPI.SetAttackPermissions(up, left, true);
            }
            else if (_isHeal)
            {
                // 基于当前权限改 AllowHeal，禁止 new 全新对象（会清掉所有已获移动方向）
                var perms = StartingAbilityPicker.StartingAbilityPickerAPI.GetMovementPermissions();
                perms.AllowHeal = true;
                StartingAbilityPicker.StartingAbilityPickerAPI.SetMovementPermissions(perms);
            }
            else
            {
                // 累加语义：只置 true 不置 false，保留之前获得的方向
                var perms = StartingAbilityPicker.StartingAbilityPickerAPI.GetMovementPermissions();
                if (_skillField == "hasDash") { if (_allowLeft) perms.DashLeft = true; if (_allowRight) perms.DashRight = true; }
                else if (_skillField == "hasHarpoonDash") { if (_allowLeft) perms.HarpoonLeft = true; if (_allowRight) perms.HarpoonRight = true; }
                else if (_skillField == "hasBrolly") { if (_allowLeft) perms.FloatLeft = true; if (_allowRight) perms.FloatRight = true; }
                else if (_skillField == "hasWalljump") { if (_allowLeft) perms.WallJumpLeft = true; if (_allowRight) perms.WallJumpRight = true; }
                StartingAbilityPicker.StartingAbilityPickerAPI.SetMovementPermissions(perms);

                // 补发本体能力（幂等：SkillRandomizer.GiveSkill 对已拥有字段直接返回，不重复弹窗）
                StartingAbilityPicker.StartingAbilityPickerAPI.GiveSkill(_skillField);
            }
        }
        public bool IsAtMax() => ItemRandomizer.GetGivenCount(Id) >= ItemLimitConfig.GetDirectionLimit(Id);

        /// <summary>方向权限图标：优先取对应技能图标（冲刺/飞针/漂浮/壁跳），攻击/回血权限用通用图标。</summary>
        private Sprite GetIcon()
        {
            try
            {
                if (_isAttack || _isHeal)
                {
                    string iconName = _isHeal ? "prompt_silkheart" : "cross_slash_attack_circle";
                    return SpriteCache.Find(iconName);
                }
                return StartingAbilityPicker.StartingAbilityPickerAPI.GetIcon(_skillField);
            }
            catch { return null; }
        }
    }

    public class ProxySavedItem : SavedItem
    {
        private IRandomReward _reward;
        public void Init(IRandomReward reward) { _reward = reward; name = _reward.Id; }
        public IRandomReward InnerReward => _reward;
        public override string GetPopupName() => _reward.DisplayName;
        public override Sprite GetPopupIcon() => _reward.Icon;
        public override void Get(bool showPopup = true) => _reward.Give();
        public override bool CanGetMore() => !_reward.IsAtMax();
        public override int GetSavedAmount() => 0;
        public override bool IsUnique => false;
    }

    /// <summary>
    /// 图标覆盖包装：显示用指定图标，其余行为完全委托给被包装奖励（给予/上限判断/名称）。
    /// 用于 lore 触发时按交互物类型（收费机/地图/音乐/蘑菇/门锁）展示对应图标。
    /// </summary>
    public class IconOverrideReward : IRandomReward
    {
        private readonly IRandomReward _inner;
        private readonly Sprite _icon;
        public IconOverrideReward(IRandomReward inner, Sprite icon) { _inner = inner; _icon = icon; }
        public string Id => _inner.Id;
        public string DisplayName => _inner.DisplayName;
        public Sprite Icon => _icon;
        public void Give() => _inner.Give();
        public bool IsAtMax() => _inner.IsAtMax();
    }

    // 将 LimitedVirtualReward 移到公共位置
    public class LimitedVirtualReward : IRandomReward
    {
        private readonly string _id, _displayName;
        private readonly Sprite _icon;
        private readonly Action _giveAction;
        private readonly int _maxCount;
        public LimitedVirtualReward(string id, string displayName, Sprite icon, Action giveAction, int maxCount)
        { _id = id; _displayName = displayName; _icon = icon; _giveAction = giveAction; _maxCount = maxCount; }
        public string Id => _id;
        public string DisplayName => _displayName;
        public Sprite Icon => _icon;
        public void Give() { _giveAction?.Invoke(); ItemRandomizer.AddGivenCount(Id); }
        public bool IsAtMax() => ItemRandomizer.GetGivenCount(Id) >= _maxCount;
    }
}


namespace SilksongItemRandomizer
{
    /// <summary>
    /// 物品随机类型过滤器（纹章、技能类物品、遗物等是否参与随机）
    /// 改为普通静态布尔属性，由 API 通过 Apply 方法设置，不再依赖 ConfigEntry
    /// </summary>
    public static class ItemTypeRandomFilter
    {
        // ========== 开关属性 ==========
        public static bool EnableCrestRandom { get; private set; } = true;
        public static bool EnableSkillItemRandom { get; private set; } = true;
        public static bool EnableRelicRandom { get; private set; } = true;

        /// <summary>
        /// 从 BepInEx 配置文件初始化（仅在游戏启动时调用一次）
        /// </summary>
        public static void Init(ConfigFile config)
        {
            EnableSkillItemRandom = config.Bind<bool>("ItemRandom", "EnableSkillItemRandom", true,
                "是否随机技能类物品（ToolItemSkill 等）").Value;
            EnableRelicRandom = config.Bind<bool>("ItemRandom", "EnableRelicRandom", true,
                "是否随机遗物（CollectableRelic）").Value;
            // 注意：EnableCrestRandom 没有在配置文件中直接绑定，而是通过 SetCrestRandomEntry 从外部传入
            // 初始值设为 true，后续由 API 或 Plugin 设置
        }

        /// <summary>
        /// 设置纹章随机开关（外部传入，因为纹章开关可能来自总控配置）
        /// </summary>
        public static void SetCrestRandomEnabled(bool enabled)
        {
            EnableCrestRandom = enabled;
        }

        /// <summary>
        /// 批量应用所有开关（由 API 调用）
        /// </summary>
        public static void Apply(bool crestEnabled, bool skillItemEnabled, bool relicEnabled)
        {
            EnableCrestRandom = crestEnabled;
            EnableSkillItemRandom = skillItemEnabled;
            EnableRelicRandom = relicEnabled;
        }

        // ========== 判断方法（供 TryGetPatch 等使用） ==========
        private static readonly Dictionary<Type, int> _typeCategoryCache = new();
        private static int GetTypeCategory(SavedItem item)
        {
            Type t = item.GetType();
            if (_typeCategoryCache.TryGetValue(t, out int cached)) return cached;
            int cat;
            if (t == typeof(ToolCrest) || t.IsSubclassOf(typeof(ToolCrest)))
                cat = 0;
            else if (t == typeof(ToolItemSkill) || t == typeof(ToolItem) || t.IsSubclassOf(typeof(ToolItemSkill)))
                cat = 1;
            else if (typeof(CollectableRelic).IsAssignableFrom(t))
                cat = 2;
            else
                cat = 3;
            _typeCategoryCache[t] = cat;
            return cat;
        }

        public static bool ShouldRandomize(SavedItem item)
        {
            if (item == null) return false;
            return GetTypeCategory(item) switch
            {
                0 => EnableCrestRandom,
                1 => EnableSkillItemRandom,
                2 => EnableRelicRandom,
                _ => true
            };
        }

        public static bool ShouldRandomizeCrest()
        {
            return EnableCrestRandom;
        }

        public static bool ShouldRandomizeRelic()
        {
            return EnableRelicRandom;
        }

        public static bool ShouldRandomizeSkillItem(SavedItem item)
        {
            if (item == null) return false;
            Type t = item.GetType();
            return (t == typeof(ToolItemSkill) || t == typeof(ToolItem) || item is ToolItemSkill) && EnableSkillItemRandom;
        }
    }
}


namespace SilksongItemRandomizer
{
    public static class NativePickupGiver
    {
        // 丝轴原生场景资产 key（bone_11b 场景内的 Silk Spool prefab，与旧版一致）
        private const string SpoolSceneAssetKey = "AssetHelper-RepackedScenes/Assets/bone_11b/Silk Spool";

        private static SavedItem _heartItem;
        private static SavedItem _spoolItem;
        private static bool _spoolMissing;
        private static bool _heartMissing;

        public static void GiveHeartPiece()
        {
            var heartPiece = GetHeartPiece();
            if (heartPiece != null)
            {
                try
                {
                    SilkSpoolState.MarkSelfGiving();
                    heartPiece.Get(true);
                    return;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[NativeGive] HeartPiece Get 异常，回退 ++: {ex.Message}");
                }
            }
            var pd = PlayerData.instance;
            if (pd != null)
            {
                pd.heartPieces++;
                EventRegister.SendEvent(EventRegisterEvents.EquipsChangedEvent, null);
            }
        }

        public static IEnumerator GiveSpoolPart()
        {
            if (_spoolItem != null)
            {
                Plugin.Log.LogInfo("[NativeGive] 丝轴：缓存命中，直接 Get");
                DoSpoolGet(_spoolItem);
                yield break;
            }

            if (_spoolMissing)
            {
                Plugin.Log.LogWarning("[NativeGive] 丝轴：此前已判定缺失，走回落 ++");
                FallbackSpool();
                yield break;
            }

            _spoolItem = Resources.FindObjectsOfTypeAll<SavedItem>()
                .FirstOrDefault(s => s != null && string.Equals(s.name, "Silk Spool", StringComparison.OrdinalIgnoreCase));
            if (_spoolItem != null)
            {
                _spoolMissing = false;
                Plugin.Log.LogInfo($"[NativeGive] 丝轴：Resources 命中 '{_spoolItem.name}' (type={_spoolItem.GetType().Name})，Get");
                DoSpoolGet(_spoolItem);
                yield break;
            }
            _spoolMissing = true;
            Plugin.Log.LogWarning("[NativeGive] 丝轴：Resources 未命中，加载 bone_11b 原生丝轴 prefab");

            // 丝轴真身在 bone_11b 场景资产（AssetHelper-RepackedScenes/Assets/bone_11b/Silk Spool），
            // 加载其 GameObject 后依赖进内存 → 二次扫 Resources；仍无则直接取 PrefabCollectable 组件。
            AsyncOperationHandle<GameObject> goHandle = default;
            try
            {
                goHandle = Addressables.LoadAssetAsync<GameObject>(SpoolSceneAssetKey);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[NativeGive] 丝轴 prefab 加载调用失败: {ex.Message}");
                FallbackSpool();
                yield break;
            }
            float deadline = Time.realtimeSinceStartup + 20f;
            while (!goHandle.IsDone && Time.realtimeSinceStartup <= deadline)
                yield return null;
            if (!goHandle.IsDone || goHandle.Status != AsyncOperationStatus.Succeeded || goHandle.Result == null)
            {
                Plugin.Log.LogWarning("[NativeGive] 丝轴 prefab 加载失败/超时，回退 ++");
                if (goHandle.IsValid()) Addressables.Release(goHandle);
                FallbackSpool();
                yield break;
            }

            GameObject result = goHandle.Result;
            _spoolItem = Resources.FindObjectsOfTypeAll<SavedItem>()
                .FirstOrDefault(s => s != null && string.Equals(s.name, "Silk Spool", StringComparison.OrdinalIgnoreCase));
            if (_spoolItem == null)
                _spoolItem = result.GetComponent<PrefabCollectable>();
            try { if (goHandle.IsValid()) Addressables.Release(goHandle); } catch { }

            if (_spoolItem != null)
            {
                _spoolMissing = false;
                Plugin.Log.LogInfo($"[NativeGive] 丝轴：prefab 命中 (type={_spoolItem.GetType().Name})，Get");
                DoSpoolGet(_spoolItem);
            }
            else
            {
                FallbackSpool();
            }
        }

        private static void DoSpoolGet(SavedItem item)
        {
            SilkSpoolState.MarkSelfGiving(30f);
            SpoolPartPatch.Bypass = true;
            SilkSpoolState.Bypass = true;
            try
            {
                Plugin.Log.LogInfo($"[NativeGive] Silk Spool Get() 开始: type={item.GetType().Name}, SelfGiving={SilkSpoolState.IsSelfGiving}, SpoolBypass={SpoolPartPatch.Bypass}");
                item.Get(true);
                Plugin.Log.LogInfo("[NativeGive] Silk Spool Get() 返回（同步部分完成，动画/协程若异步则后续原生执行）");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[NativeGive] Spool Get 异常，回退 ++: {ex}");
                FallbackSpool();
            }
            finally
            {
                SpoolPartPatch.Bypass = false;
                SilkSpoolState.Bypass = false;
                Plugin.Log.LogInfo("[NativeGive] Bypass 标志已还原（SelfGiving 保留 30s）");
            }
        }

        private static void FallbackSpool()
        {
            var pd = PlayerData.instance;
            if (pd == null) return;
            pd.silkSpoolParts++;
            GameCameras.instance?.silkSpool?.RefreshSilk();
            EventRegister.SendEvent(EventRegisterEvents.EquipsChangedEvent, null);
        }

        private static SavedItem GetHeartPiece()
        {
            if (_heartItem != null) return _heartItem;
            if (_heartMissing) return null;
            _heartItem = Resources.FindObjectsOfTypeAll<SavedItem>()
                .FirstOrDefault(s => s != null && string.Equals(s.name, "Heart Piece", StringComparison.OrdinalIgnoreCase));
            _heartMissing = _heartItem == null;
            return _heartItem;
        }
    }
}

// InspectPermitRewards.cs

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 权限类 inspect 地点（总表 Lore 行 - lore 白名单）的权限物：
    /// 每个地点一个"允许走原生流程"的许可放进随机池（记录二），
    /// 玩家随机获得后该地点 inspect 放行原生流程；inspect 每地只触发一次随机（记录一）。
    /// 车站权限（Station_Unlocked*）已有独立机制，不在本表。
    /// </summary>
    public static class InspectPermitRewards
    {
        public readonly struct InspectPermitEntry
        {
            public readonly string Scene;
            public readonly string Name;
            public InspectPermitEntry(string scene, string name)
            {
                Scene = scene;
                Name = name;
            }
        }

        /// <summary>权限类地点清单（scene|name，与 lore 白名单互补，仅保留车站收费机 26 处）
        /// 其余权限类 inspect 地点已移除随机化，回归原生交互流程。</summary>
        public static readonly InspectPermitEntry[] Permits =
        {
            new InspectPermitEntry("Arborium_Tube", "tube_toll_machine"),
            new InspectPermitEntry("Belltown_basement", "Bellway Toll Machine"),
            new InspectPermitEntry("Bellway_02", "Bellway Toll Machine"),
            new InspectPermitEntry("Bellway_03", "Bellway Toll Machine"),
            new InspectPermitEntry("Bellway_04", "Bellway Toll Machine"),
            new InspectPermitEntry("Bellway_08", "Bellway Toll Machine"),
            new InspectPermitEntry("Bellway_Aqueduct", "Bellway Toll Machine"),
            new InspectPermitEntry("Bellway_City", "Bellway Toll Machine"),
            new InspectPermitEntry("Bellway_City", "Bellway Toll Machine (1)"),
            new InspectPermitEntry("Bellway_City", "tube_toll_machine"),
            new InspectPermitEntry("Bellway_Shadow", "Bellway Toll Machine"),
            new InspectPermitEntry("Bone_East_10", "toll door interactible"),
            new InspectPermitEntry("Hang_06b", "tube_toll_machine"),
            new InspectPermitEntry("Shellwood_19", "Bellway Toll Machine"),
            new InspectPermitEntry("Slab_06", "Bellway Toll Machine"),
            new InspectPermitEntry("Song_01b", "tube_toll_machine"),
            new InspectPermitEntry("Song_28", "Toll_machine_silk_ration"),
            new InspectPermitEntry("Song_29", "Toll_machine_silk_ration"),
            new InspectPermitEntry("Song_Enclave_Tube", "tube_toll_machine"),
            new InspectPermitEntry("Tube_Hub", "tube_toll_machine"),
            new InspectPermitEntry("Under_01b", "Understore Toll Bench"),
            new InspectPermitEntry("Under_01b", "Understore Toll Bench (1)"),
            new InspectPermitEntry("Under_08", "Understore Toll Bench"),
            new InspectPermitEntry("Under_08", "Understore Toll Bench (1)"),
            new InspectPermitEntry("Under_08", "Understore Toll Bench (2)"),
            new InspectPermitEntry("Under_22", "tube_toll_machine"),
        };

        /// <summary>运行时应答：该地点是否为权限类（有权限物）</summary>
        public static bool IsPermitObject(string scene, string name)
            => !string.IsNullOrEmpty(scene) && !string.IsNullOrEmpty(name) && PermitKeys.Contains(scene + "|" + name);

        private static readonly HashSet<string> PermitKeys = BuildPermitKeys();

        private static HashSet<string> BuildPermitKeys()
        {
            var set = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var e in Permits)
                set.Add(e.Scene + "|" + e.Name);
            return set;
        }
    }
}
