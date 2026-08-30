// SilksongItemRandomizerAPI.cs - 修复陷阱开关不同步问题
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using static SilksongItemRandomizer.CurrencyCollectPatch;
using static SilksongItemRandomizer.Extracurrencypickup;
using static SilksongItemRandomizer.PickupPatch;
using static SilksongItemRandomizer.ShopRandomizer;
using static SilksongItemRandomizer.SilkSpearPityPatch;
using static SilksongItemRandomizer.ToolEffectRandomizer;

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 物品随机模块的完整配置（所有可调节参数）
    /// </summary>
    public class ItemRandomizerConfig
    {
        public int Seed = 0;
        public bool Enabled = false;
        public bool CrestEnabled = false;
        public bool TrapEnabled = false;
        public bool TrapMovementEnabled = false;
        public int TrapDifficulty = 0;
        public bool CrestRandomEnabled = true;
        public bool SkillItemRandomEnabled = true;
        public bool RelicRandomEnabled = true;
        public int CurrencyFirstThreshold = 30;
        public int CurrencySecondThreshold = 1000;
        public int SilkSpearPityCount = 5;
        public float VirtualUnlimitedProbability = 0.1f;
        public float CrestUnlockerProbability = 0.1f;
        public float NormalLimitedProbability = 0.8f;
        public int MaxGivenPerItem = 2;
        public ItemLimitSettings Limits = new ItemLimitSettings();
        public InfinitePoolSettings InfinitePool = new InfinitePoolSettings();
    }

    /// <summary>
    /// 物品随机模块的统一公开 API
    /// </summary>
    public static class SilksongItemRandomizerAPI
    {
        private static ItemRandomizerConfig _cachedConfig;
        private static bool _pendingChanges = false;
        private static bool _isGameReady = false;
        private static bool _initialized = false;
        private static bool _harmonyPatched = false;

        private static Harmony _harmony;
        private static readonly string HarmonyId = "SilksongItemRandomizer.Core";

        // ========== 初始化与生命周期 ==========
        public static void Initialize(ItemRandomizerConfig config)
        {
            if (_initialized) return;
            _cachedConfig = config ?? new ItemRandomizerConfig();
            _initialized = true;
            // 常驻补丁（不随总开关切换）在此尽早注册，保证游戏一开始就生效
            ApplyAlwaysOnPatches();
            _pendingChanges = true;
            SyncToInternalFields();
        }

        public static void MarkGameReady()
        {
            if (_isGameReady) return;
            _isGameReady = true;
            // 启动早期 EnemyRando 尚未初始化时其配置写入会被跳过，此时游戏已就绪，重放一次
            EnemyRandoAdjuster.ReapplyConfig();
            if (_pendingChanges) ApplyPending();
        }

        // ========== 配置修改 ==========
        public static void SetEnabled(bool enabled) { _cachedConfig.Enabled = enabled; MarkPendingAndApply(); }
        public static void SetCrestEnabled(bool enabled) { _cachedConfig.CrestEnabled = enabled; MarkPendingAndApply(); }
        public static void SetTrapEnabled(bool enabled) { _cachedConfig.TrapEnabled = enabled; MarkPendingAndApply(); }
        public static void SetTrapMovementEnabled(bool enabled) { _cachedConfig.TrapMovementEnabled = enabled; MarkPendingAndApply(); }
        public static void SetTrapDifficulty(int difficulty) { _cachedConfig.TrapDifficulty = difficulty; MarkPendingAndApply(); }
        public static void SetSeed(int seed) { _cachedConfig.Seed = seed; MarkPendingAndApply(); }
        public static void SetCrestRandomEnabled(bool enabled) { _cachedConfig.CrestRandomEnabled = enabled; MarkPendingAndApply(); }
        public static void SetSkillItemRandomEnabled(bool enabled) { _cachedConfig.SkillItemRandomEnabled = enabled; MarkPendingAndApply(); }
        public static void SetRelicRandomEnabled(bool enabled) { _cachedConfig.RelicRandomEnabled = enabled; MarkPendingAndApply(); }
        public static void SetCurrencyThresholds(int first, int second) { _cachedConfig.CurrencyFirstThreshold = first; _cachedConfig.CurrencySecondThreshold = second; MarkPendingAndApply(); }
        public static void SetSilkSpearPityCount(int count) { _cachedConfig.SilkSpearPityCount = count; MarkPendingAndApply(); }
        public static void SetItemLimits(ItemLimitSettings limits) { _cachedConfig.Limits = limits; MarkPendingAndApply(); }
        public static void SetInfinitePoolSettings(InfinitePoolSettings settings) { _cachedConfig.InfinitePool = settings; MarkPendingAndApply(); }
        public static void SetVirtualProbabilities(float virtualProb, float crestProb, float normalProb)
        {
            _cachedConfig.VirtualUnlimitedProbability = virtualProb;
            _cachedConfig.CrestUnlockerProbability = crestProb;
            _cachedConfig.NormalLimitedProbability = normalProb;
            MarkPendingAndApply();
        }

        public static void ApplyNow()
        {
            if (!_isGameReady && !_initialized) return;
            ApplyPending();
        }

        // ========== 查询方法 ==========
        public static bool IsEnabled() => _cachedConfig.Enabled;
        public static bool IsCrestEnabled() => _cachedConfig.CrestEnabled;
        public static bool IsTrapEnabled() => _cachedConfig.TrapEnabled;
        public static bool IsTrapMovementEnabled() => _cachedConfig.TrapMovementEnabled;
        public static int GetTrapDifficulty() => _cachedConfig.TrapDifficulty;
        public static int GetSeed() => _cachedConfig.Seed;
        public static bool IsCrestRandomEnabled() => _cachedConfig.CrestRandomEnabled;
        public static bool IsSkillItemRandomEnabled() => _cachedConfig.SkillItemRandomEnabled;
        public static bool IsRelicRandomEnabled() => _cachedConfig.RelicRandomEnabled;
        public static (int first, int second) GetCurrencyThresholds() => (_cachedConfig.CurrencyFirstThreshold, _cachedConfig.CurrencySecondThreshold);
        public static int GetSilkSpearPityCount() => _cachedConfig.SilkSpearPityCount;

        // ========== 灵丝获得/消耗随机化开关（独立于总开关，默认关闭） ==========
        public static bool IsSilkRandomEnabled() => Plugin.SilkRandomizerEnabled != null && Plugin.SilkRandomizerEnabled.Value;
        public static void SetSilkRandomEnabled(bool enabled)
        {
            if (Plugin.SilkRandomizerEnabled == null) return;
            Plugin.SilkRandomizerEnabled.Value = enabled;
        }

        // ========== 运行时操作 ==========
        public static void RegenerateTrapsNow()
        {
            if (!_isGameReady || !_cachedConfig.TrapEnabled) return;
            TrapRandomizer.RespawnTraps();
        }

        /// <summary>
        /// 面板直接修改 ItemLimitConfig 静态字段后调用：
        /// 同步回 _cachedConfig（防止 ApplyPending 用旧快照把面板新值覆盖回退）、
        /// 并置脏标，由 Update 防抖后统一持久化 cfg + 重建一次全量映射。
        /// </summary>
        public static void SaveLimitsAndRegenerateMappings()
        {
            SyncLimitsBackToCachedConfig();
            ItemLimitConfig.MarkLimitsDirty();
        }

        /// <summary>
        /// 主线程每帧调用（HotkeyHandler.Update）：
        /// 防抖窗口到且存在脏标时，真正执行「写回 cfg + 强制重建映射」。
        /// </summary>
        public static void FlushLimitsRegenerate()
        {
            if (!ItemLimitConfig.TryFlushLimitsRegenerate()) return;

            ItemLimitConfig.SaveToConfigFile();
            PreGeneratedMap.Reset();   // 清空映射、恢复未初始化状态
            PreGeneratedMap.Initialize(); // 按当前 limit 重新全量生成
        }

        /// <summary>
        /// 把 ItemLimitConfig 当前静态字段同步回 _cachedConfig.Limits，
        /// 使后续 ApplyPending 的 ItemLimitConfig.Apply 不会用旧默认值覆盖面板改动。
        /// </summary>
        private static void SyncLimitsBackToCachedConfig()
        {
            if (_cachedConfig == null) return;
            _cachedConfig.Limits = ItemLimitConfig.CaptureSettings();
        }

        public static void ResetAllData()
        {
            // 使用公开方法重置存档，而不是直接赋值
            Plugin.ResetSaveData();

            // ★ 新开随机档：攻击方向权限重置为全未获得，并标记方向系统已启用，
            //   否则 GlobalSaveData 默认 Ability* = true + AttackDirectionsSet=false
            //   会让重进存档时所有分裂权限全部解锁。
            var saveData = Plugin.SaveData;
            if (saveData != null)
            {
                saveData.AbilityUpward = false;
                saveData.AbilityLeft = false;
                saveData.AbilityRight = false;
                saveData.AttackDirectionsSet = true;
                Plugin.SaveGlobalData();
            }

            // 重建缓存
            SpriteCache.Reset();

            // ★ 预生成表随存档清空，并重置生成标记（供下面按新种子重新全量生成）
            PreGeneratedMap.Reset();

            ItemRandomizer.Initialize(_cachedConfig.Seed, _cachedConfig, new PluginSaveDataAccessor(), GetFullRandomMode());
            CrestRandomizer.Initialize(_cachedConfig.Seed, _cachedConfig.CrestEnabled, new CrestSaveDataAccessor());
            // ★ 存档已重建，预生成表清空：重新从随机池摸全部检查点奖励
            PreGeneratedMap.Initialize();

            CurrencyCollectPatch.ResetCounters();
            SilkSpearPityPatch.ResetSilkSpearState();
            PickupPatch.ResetAll();
            ShopRandomizer.ResetCache();
            Extracurrencypickup.ResetAll();
            ShopMenuStock_BuildItemList_Patch.ResetAllCounts();
            TrapRandomizer.ClearAll();
        }

        // ========== 内部实现 ==========
        private static void MarkPendingAndApply()
        {
            _pendingChanges = true;
            ApplyPending();
        }

        private static void ApplyPending()
        {
            if (!_isGameReady && !_pendingChanges) return;

            SyncToInternalFields();

            string limitStampBefore = ItemLimitConfig.BuildLimitsStamp();
            ItemLimitConfig.Apply(_cachedConfig.Limits);
            ItemLimitConfig.ApplyInfinitePoolSettings(_cachedConfig.InfinitePool);
            ItemTypeRandomFilter.Apply(_cachedConfig.CrestRandomEnabled, _cachedConfig.SkillItemRandomEnabled, _cachedConfig.RelicRandomEnabled);

            // ★ limit 实际变化时才写回 cfg 持久化，并置脏标让 Update 防抖重建映射
            if (ItemLimitConfig.BuildLimitsStamp() != limitStampBefore)
            {
                ItemLimitConfig.SaveToConfigFile();
                ItemLimitConfig.MarkLimitsDirty();
            }

            var saveData = Plugin.SaveData;
            saveData.CurrencyFirstThreshold = _cachedConfig.CurrencyFirstThreshold;
            saveData.CurrencySecondThreshold = _cachedConfig.CurrencySecondThreshold;
            saveData.SilkSpearPityCount = _cachedConfig.SilkSpearPityCount;
            saveData.VirtualUnlimitedProbability = _cachedConfig.VirtualUnlimitedProbability;
            saveData.CrestUnlockerProbability = _cachedConfig.CrestUnlockerProbability;
            saveData.NormalLimitedProbability = _cachedConfig.NormalLimitedProbability;
            saveData.MaxGivenPerItem = _cachedConfig.MaxGivenPerItem;

            // ★★★ 修复：同步陷阱状态到 Plugin.SaveData ★★★
            saveData.TrapEnabled = _cachedConfig.TrapEnabled;
            saveData.TrapMovementEnabled = _cachedConfig.TrapMovementEnabled;
            saveData.TrapDifficulty = _cachedConfig.TrapDifficulty switch
            {
                0 => "Beginner",
                1 => "Focused",
                2 => "Overflow",
                _ => "Beginner"
            };
            // ★★★★★★★★★★★★★★★★★★★★★★★★★★

            Plugin.SaveGlobalData();

            bool fullRandom = GetFullRandomMode();
            ItemRandomizer.Initialize(_cachedConfig.Seed, _cachedConfig, new PluginSaveDataAccessor(), fullRandom);
            CrestRandomizer.Initialize(_cachedConfig.Seed, _cachedConfig.CrestEnabled, new CrestSaveDataAccessor());

            if (_cachedConfig.TrapEnabled)
            {
                var trapConfig = new TrapRandomConfig
                {
                    Seed = _cachedConfig.Seed,
                    Enabled = _cachedConfig.TrapEnabled,
                    MovementEnabled = _cachedConfig.TrapMovementEnabled,
                    Difficulty = _cachedConfig.TrapDifficulty,
                    FrostBannedScenes = Plugin.SaveData.TrapFrostBannedScenes
                };
                TrapRandomizer.Initialize(trapConfig);
            }
            else
            {
                TrapRandomizer.ClearAll();
            }

            ToolEffectRandomizer.SetEnabled(_cachedConfig.CrestEnabled);
            ShopRandomizer.ResetCache();

            // 能力门控（回血/二段跳/抗寒/游泳）：仅在「全随机模式」下启用。
            // 这些分裂型权限是稀缺能力，随机池在非全随机模式不含它们，若门控开启会造成无法获得的死局
            //（如未持游泳权限时水一直有伤害）。非全随机模式一律关闭门控，保持原生行为。
            StartingAbilityPicker.StartingAbilityPickerAPI.SetAbilityGates(
                fullRandom, fullRandom, fullRandom, fullRandom);

            if (_cachedConfig.Enabled && !_harmonyPatched)
            {
                ApplyHarmonyPatches();
                _harmonyPatched = true;
                PickupPatch.EnableRandomizer();
            }
            else if (!_cachedConfig.Enabled && _harmonyPatched)
            {
                RemoveHarmonyPatches();
                _harmonyPatched = false;
                PickupPatch.DisableRandomizer();
            }

            _pendingChanges = false;
        }

        private static void SyncToInternalFields()
        {
            // 仅当值确实变化才写回 ConfigEntry，避免每次 ApplyPending 都重复赋值 +
            // 触发 SettingChanged 事件 + 全量 Config.Save()，消除配置同步的抖动与反复事件。
            bool changed = false;

            if (Plugin.RandomSeed != null && Plugin.RandomSeed.Value != _cachedConfig.Seed)
            {
                Plugin.RandomSeed.Value = _cachedConfig.Seed;
                changed = true;
            }
            if (Plugin.ItemRandomEnabled != null && Plugin.ItemRandomEnabled.Value != _cachedConfig.Enabled)
            {
                Plugin.ItemRandomEnabled.Value = _cachedConfig.Enabled;
                changed = true;
            }
            if (Plugin.CrestRandomEnabled != null && Plugin.CrestRandomEnabled.Value != _cachedConfig.CrestRandomEnabled)
            {
                Plugin.CrestRandomEnabled.Value = _cachedConfig.CrestRandomEnabled;
                changed = true;
            }

            if (changed)
                Plugin.Instance?.Config.Save();  // 值有变化才保存一次
        }

        // ========== 补丁类型清单（单一事实来源） ==========

        /// <summary>
        /// 跟随「物品随机总开关」切换的动态补丁。
        /// 启用时批量 PatchAll，禁用时按类逐个 Unpatch。
        /// 包含商店店主识别等所有需要随开关切换的类型。
        /// </summary>
        private static readonly System.Type[] ToggleablePatchTypes =
        {
            typeof(PickupPatch),
            typeof(CurrencyCollectPatch),
            typeof(TryGetPatch),
            typeof(ChurchRandomizePatch),
            typeof(ChurchGateCheckPatch),
            typeof(SkillRegionRandomizePatch),
            typeof(ShopOwnerBase_SpawnUpdateShop_Patch),
            typeof(ShopMenuStock_BuildItemList_Patch),
            typeof(ShopItemStats_Purchase_Patch),
            typeof(SilkSpearPityPatch),
            typeof(SpoolPartPatch),
            typeof(SetPlayerDataVariable_OnEnter_Patch),
            typeof(PlayerData_IncrementInt_Patch),
            typeof(CreateObjectV2_OnEnter_Patch),
            typeof(CreateObject_OnEnter_Patch),
            typeof(IntOperator_OnEnter_Patch),
            typeof(SetAnimator_OnEnter_Patch),
            typeof(AnimatorPlayStateWait_OnEnter_Patch),
            typeof(ListenForAnimationEvent_OnEnter_Patch),
            typeof(Tk2dPlayAnimation_OnEnter_Patch),
            typeof(Tk2dPlayAnimationWithEvents_OnEnter_Patch),
            typeof(Wait_OnEnter_Patch),
            typeof(EaseColor_OnEnter_Patch),
            typeof(Tk2dSpriteSetColor_OnEnter_Patch),
            typeof(SetMeshRenderer_OnEnter_Patch),
            typeof(iTweenMoveTo_OnEnter_Patch),
            typeof(iTweenScaleTo_OnEnter_Patch),
            typeof(AudioPlayerOneShotSingle_OnEnter_Patch),
            typeof(SetGravity2dScale_OnEnter_Patch),
            typeof(SetGravity2dScaleV2_OnEnter_Patch),
            typeof(PlayParticleEmitter_OnEnter_Patch),
            typeof(StopParticleEmitter_OnEnter_Patch),
            typeof(PlayerDataVariableTest_OnEnter_Patch),
            typeof(SendEventToRegister_OnEnter_Patch),
            typeof(SendEventByName_OnEnter_Patch),
            typeof(HeroController_AddToMaxSilk_Patch),
            typeof(HeroController_AddToMaxHealth_Patch),
            typeof(AddHeroInputBlocker_OnEnter_Patch),
            typeof(CheckIsCharacterGrounded_OnEnter_Patch),
            typeof(MossberryRandomizer.CollectableItem_Collect_Patch),
            typeof(SilkRandomizerPatch),
            typeof(LoreTriggerPatch),
            typeof(MapStationUnlockPatch),
            typeof(Extracurrencypickup),
        };

        /// <summary>
        /// 常驻补丁：不受总开关控制，始终注册、永不卸载。
        /// 它们内部以 SilksongItemRandomizerAPI.IsEnabled() 自守卫，禁用时不做任何事，
        /// 因此始终挂载是安全的。
        /// 拆分独立补丁类：每个单独 PatchAll + try/catch，避免单个目标缺失导致一票否决。
        /// </summary>
        private static readonly System.Type[] AlwaysOnPatchTypes =
        {
            typeof(MapperPermanentPatch.TimePassesPatch),
            typeof(MapperPermanentPatch.MapperLeavePrevPatch),
            typeof(MapperPermanentPatch.SceneTravelerEvalPatch),
            typeof(MapperPermanentPatch.MapperLeaveAllPatch),
            typeof(MapperPermanentPatch.ResetOnEnterPatch),
            typeof(DeactivateIfPlayerdataTruePatch),
            typeof(SkillRegionRandomizePatch.GiveSilkHeartAllowPatch),
        };

        /// <summary>常驻补丁是否已注册（避免重复 PatchAll）</summary>
        private static bool _alwaysOnPatchesApplied = false;

        /// <summary>
        /// 注册常驻补丁（幂等，仅执行一次）。由 Initialize 与 ApplyHarmonyPatches 调用。
        /// 独立 try/catch：个别目标方法缺失不影响其余补丁。
        /// </summary>
        public static void ApplyAlwaysOnPatches()
        {
            if (_harmony == null)
                _harmony = new Harmony(HarmonyId);
            if (_alwaysOnPatchesApplied) return;

            foreach (var type in AlwaysOnPatchTypes)
            {
                try { _harmony.PatchAll(type); }
                catch (Exception ex) { Plugin.Log.LogWarning($"常驻补丁注册失败 {type.Name}（目标方法可能不存在）: {ex.Message}"); }
            }
            _alwaysOnPatchesApplied = true;
        }

        private static void ApplyHarmonyPatches()
        {
            if (_harmony == null)
                _harmony = new Harmony(HarmonyId);

            ApplyAlwaysOnPatches();

            foreach (var type in ToggleablePatchTypes)
            {
                try { _harmony.PatchAll(type); }
                catch (Exception ex) { Plugin.Log.LogWarning($"补丁注册失败 {type.Name}: {ex.Message}"); }
            }

            // 按类注册持久化数据访问器
            PickupPatch.Initialize(new PluginSaveDataAccessor());
            ShopMenuStock_BuildItemList_Patch.Initialize(new PluginSaveDataAccessor());
            SilkSpearPityPatch.Initialize(new PluginSaveDataAccessor());
            Extracurrencypickup.Initialize(new PluginSaveDataAccessor());

            try { EnemyRandoAdjuster.TryPatch(_harmony); }
            catch (Exception ex) { Plugin.Log.LogWarning($"EnemyRandoAdjuster 补丁跳过（可能缺少 EnemyRando）: {ex.Message}"); }
        }

        private static void RemoveHarmonyPatches()
        {
            if (_harmony == null) return;

            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            foreach (var type in ToggleablePatchTypes)
            {
                try
                {
                    // HarmonyLib 的 Unpatch 需要 MethodBase，按类卸载时遍历其静态方法逐个 Unpatch。
                    // 未补丁过的方法 Unpatch 无副作用，安全。
                    foreach (var method in type.GetMethods(flags))
                        _harmony.Unpatch(method, HarmonyPatchType.All, HarmonyId);
                }
                catch (Exception ex) { Plugin.Log.LogWarning($"补丁卸载失败 {type.Name}: {ex.Message}"); }
            }
        }

        public static bool GetFullRandomMode()
        {
            try
            {
                var directionPatchType = Type.GetType("StartingAbilityPicker.DirectionPatch, StartingAbilityPicker");
                if (directionPatchType == null) return false;
                var prop = directionPatchType.GetProperty("FullRandomMode", BindingFlags.Public | BindingFlags.Static);
                if (prop != null && prop.GetValue(null) is bool b)
                    return b;
            }
            catch { }
            return false;
        }

        // ========== 内部数据访问适配器 ==========
        public class PluginSaveDataAccessor : ISaveDataAccessor, IShopSaveDataAccessor, ICurrencySaveDataAccessor,
            ISilkSpearSaveDataAccessor, ICrestSaveDataAccessor, IPickupSaveDataAccessor, IExtraPickupSaveDataAccessor,
            IToolEffectSaveDataAccessor,
            ShopMenuStock_BuildItemList_Patch.IShopSlotCountAccessor
        {
            public Dictionary<string, int> GetItemGivenCounts() => Plugin.SaveData.ItemGivenCounts;
            public void SaveItemGivenCounts(Dictionary<string, int> counts) { Plugin.SaveData.ItemGivenCounts = counts; Plugin.SaveGlobalData(); }
            public Dictionary<string, string> GetTotalMappings() => Plugin.SaveData.TotalMappings;
            public void SaveTotalMappings(Dictionary<string, string> mappings) { Plugin.SaveData.TotalMappings = mappings; Plugin.SaveGlobalData(); }

            public HashSet<string> GetAssignedItemIds() => Plugin.SaveData.ShopAssignedItemIds;
            public void SaveAssignedItemIds(HashSet<string> ids) { Plugin.SaveData.ShopAssignedItemIds = ids; Plugin.SaveGlobalData(); }

            public int GetTotalCollectCount() => Plugin.SaveData.CurrencyTotalCollectCount;
            public void SetTotalCollectCount(int count) { Plugin.SaveData.CurrencyTotalCollectCount = count; Plugin.SaveGlobalData(); }
            public bool GetFirstKeyGiven() => Plugin.SaveData.CurrencyFirstKeyGiven;
            public void SetFirstKeyGiven(bool given) { Plugin.SaveData.CurrencyFirstKeyGiven = given; Plugin.SaveGlobalData(); }
            public bool GetSecondKeyGiven() => Plugin.SaveData.CurrencySecondKeyGiven;
            public void SetSecondKeyGiven(bool given) { Plugin.SaveData.CurrencySecondKeyGiven = given; Plugin.SaveGlobalData(); }
            public int GetFirstThreshold() => Plugin.SaveData.CurrencyFirstThreshold;
            public void SetFirstThreshold(int threshold) { Plugin.SaveData.CurrencyFirstThreshold = threshold; Plugin.SaveGlobalData(); }
            public int GetSecondThreshold() => Plugin.SaveData.CurrencySecondThreshold;
            public void SetSecondThreshold(int threshold) { Plugin.SaveData.CurrencySecondThreshold = threshold; Plugin.SaveGlobalData(); }

            public int GetTryGetCount() => Plugin.SaveData.SilkSpearTryGetCount;
            public void SetTryGetCount(int count) { Plugin.SaveData.SilkSpearTryGetCount = count; Plugin.SaveGlobalData(); }
            public bool GetSilkSpearGiven() => Plugin.SaveData.SilkSpearGiven;
            public void SetSilkSpearGiven(bool given) { Plugin.SaveData.SilkSpearGiven = given; Plugin.SaveGlobalData(); }
            public int GetPityCount() => Plugin.SaveData.SilkSpearPityCount;
            public void SetPityCount(int count) { Plugin.SaveData.SilkSpearPityCount = count; Plugin.SaveGlobalData(); }

            public Dictionary<string, string> GetCrestMappings() => Plugin.SaveData.CrestMappings;
            public void SaveCrestMappings(Dictionary<string, string> mappings) { Plugin.SaveData.CrestMappings = mappings; Plugin.SaveGlobalData(); }
            public HashSet<string> GetUnlockedCrests() => Plugin.SaveData.UnlockedCrests;
            public void SaveUnlockedCrests(HashSet<string> unlockedCrests) { Plugin.SaveData.UnlockedCrests = unlockedCrests; Plugin.SaveGlobalData(); }
            public string GetLastUnlockedCrest() => Plugin.SaveData.LastUnlockedCrest;
            public void SetLastUnlockedCrest(string crestName) { Plugin.SaveData.LastUnlockedCrest = crestName; Plugin.SaveGlobalData(); }

            public HashSet<string> GetPickedPickupKeys() => Plugin.SaveData.PickedPickupKeys;
            public void SavePickedPickupKeys(HashSet<string> keys) { Plugin.SaveData.PickedPickupKeys = keys; Plugin.SaveGlobalData(); }

            public HashSet<string> GetPickedPositions() => Plugin.SaveData.PickedPositions;
            public void SavePickedPositions(HashSet<string> positions) { Plugin.SaveData.PickedPositions = positions; Plugin.SaveGlobalData(); }

            public string GetLastUnlockedCrestForRespawn() => Plugin.SaveData.LastUnlockedCrest;

            public Dictionary<string, Dictionary<string, float>> GetCrestEffects() => Plugin.SaveData.CrestEffects;
            public void SaveCrestEffects(Dictionary<string, Dictionary<string, float>> effects) { Plugin.SaveData.CrestEffects = effects; Plugin.SaveGlobalData(); }

            // 实现 IShopSlotCountAccessor
            public int GetCount(string permanentId)
            {
                return Plugin.SaveData.ShopSlotCounts.TryGetValue(permanentId, out int count) ? count : 1;
            }

            public void SetCount(string permanentId, int count)
            {
                Plugin.SaveData.ShopSlotCounts[permanentId] = count;
                Plugin.SaveGlobalData();
            }
        }

        public class CrestSaveDataAccessor : ICrestSaveDataAccessor
        {
            public Dictionary<string, string> GetCrestMappings() => Plugin.SaveData.CrestMappings;
            public void SaveCrestMappings(Dictionary<string, string> mappings) { Plugin.SaveData.CrestMappings = mappings; Plugin.SaveGlobalData(); }
            public HashSet<string> GetUnlockedCrests() => Plugin.SaveData.UnlockedCrests;
            public void SaveUnlockedCrests(HashSet<string> unlockedCrests) { Plugin.SaveData.UnlockedCrests = unlockedCrests; Plugin.SaveGlobalData(); }
            public string GetLastUnlockedCrest() => Plugin.SaveData.LastUnlockedCrest;
            public void SetLastUnlockedCrest(string crestName) { Plugin.SaveData.LastUnlockedCrest = crestName; Plugin.SaveGlobalData(); }
        }
    }
}