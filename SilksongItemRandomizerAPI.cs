// SilksongItemRandomizerAPI.cs - 修复陷阱开关不同步问题
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using static SilksongItemRandomizer.BenchRespawnPatch;
using static SilksongItemRandomizer.CrestRandomizePatch;
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
        public bool TrapMovementEnabled = true;
        public int TrapDifficulty = 0;
        public bool CrestRandomEnabled = true;
        public bool SkillItemRandomEnabled = true;
        public bool RelicRandomEnabled = true;
        public int CurrencyFirstThreshold = 30;
        public int CurrencySecondThreshold = 1000;
        public int SilkSpearPityCount = 10;
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
            _pendingChanges = true;
            SyncToInternalFields();
        }

        public static void MarkGameReady()
        {
            if (_isGameReady) return;
            _isGameReady = true;
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

        // ========== 运行时操作 ==========
        public static void RegenerateTrapsNow()
        {
            if (!_isGameReady || !_cachedConfig.TrapEnabled) return;
            TrapRandomizer.RespawnTraps();
        }

        public static void ResetAllData()
        {
            // 使用公开方法重置存档，而不是直接赋值
            Plugin.ResetSaveData();

            ItemRandomizer.Initialize(_cachedConfig.Seed, _cachedConfig, new PluginSaveDataAccessor(), GetFullRandomMode());
            CrestRandomizer.Initialize(_cachedConfig.Seed, _cachedConfig.CrestEnabled, new CrestSaveDataAccessor());

            CrestRandomizePatch.ResetProcessedIds();
            CurrencyCollectPatch.ResetCounters();
            SilkSpearPityPatch.ResetSilkSpearState();
            PickupPatch.ResetAll();
            ShopRandomizer.ResetCache();
            Extracurrencypickup.ResetAll();
            ShopMenuStock_BuildItemList_Patch.ResetAllCounts();
            TrapRandomizer.ClearAll();
            BenchRespawnPatch.ResetCooldown();
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

            ItemLimitConfig.Apply(_cachedConfig.Limits);
            ItemLimitConfig.ApplyInfinitePoolSettings(_cachedConfig.InfinitePool);
            ItemTypeRandomFilter.Apply(_cachedConfig.CrestRandomEnabled, _cachedConfig.SkillItemRandomEnabled, _cachedConfig.RelicRandomEnabled);

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
            Plugin.Instance.Config.Save();  // ← 添加
        }

        private static void SyncToInternalFields()
        {
            if (Plugin.RandomSeed != null)
            {
                Plugin.RandomSeed.Value = _cachedConfig.Seed;
                Plugin.Instance.Config.Save();  // ← 添加
            }
            if (Plugin.ItemRandomEnabled != null)
            {
                Plugin.ItemRandomEnabled.Value = _cachedConfig.Enabled;
                Plugin.Instance.Config.Save();  // ← 添加
            }
            if (Plugin.CrestRandomEnabled != null)
            {
                Plugin.CrestRandomEnabled.Value = _cachedConfig.CrestRandomEnabled;
                Plugin.Instance.Config.Save();  // ← 添加
            }
        }

        private static void ApplyHarmonyPatches()
        {
            if (_harmony == null)
                _harmony = new Harmony(HarmonyId);

            _harmony.PatchAll(typeof(PickupPatch));
            _harmony.PatchAll(typeof(CurrencyCollectPatch));
            _harmony.PatchAll(typeof(TryGetPatch));
            _harmony.PatchAll(typeof(CrestRandomizePatch));
            _harmony.PatchAll(typeof(ShopMenuStock_BuildItemList_Patch));
            _harmony.PatchAll(typeof(ShopItemStats_Purchase_Patch));
            _harmony.PatchAll(typeof(SilkSpearPityPatch));
            _harmony.PatchAll(typeof(BenchRespawnPatch));
            _harmony.PatchAll(typeof(SilkRandomizerPatch));
            _harmony.PatchAll(typeof(Extracurrencypickup));
            try { EnemyRandoAdjuster.TryPatch(_harmony); } catch { }
        }

        private static void RemoveHarmonyPatches()
        {
            _harmony?.UnpatchSelf();
            _harmony = null;
        }

        private static bool GetFullRandomMode()
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
            IBenchRespawnSaveDataAccessor, ICrestPatchSaveDataAccessor, IToolEffectSaveDataAccessor,
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

            public HashSet<string> GetDisabledChapels() => Plugin.SaveData.DisabledChapels;
            public void SaveDisabledChapels(HashSet<string> disabledChapels) { Plugin.SaveData.DisabledChapels = disabledChapels; Plugin.SaveGlobalData(); }

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