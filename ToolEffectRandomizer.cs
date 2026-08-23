// ToolEffectRandomizer.cs - 修复后的完整版本
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Random = System.Random;   // 明确使用 System.Random

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 纹章诅咒效果：每个纹章拥有不同的属性修正（速度、冷却时间等）
    /// 改造后：通过接口访问乘数配置，不再依赖 Plugin.SaveData
    /// 保留所有原有功能：纹章切换时应用修正，可动态开关
    /// </summary>
    public static class ToolEffectRandomizer
    {
        // ========== 持久化数据访问接口 ==========
        public interface IToolEffectSaveDataAccessor
        {
            Dictionary<string, Dictionary<string, float>> GetCrestEffects();
            void SaveCrestEffects(Dictionary<string, Dictionary<string, float>> effects);
        }

        private static IToolEffectSaveDataAccessor _saveData;

        // ========== 运行时状态 ==========
        private static bool _initialized = false;
        private static bool _enabled = false;
        private static Random _rng;
        private static int _seed;

        // 原始英雄属性备份
        private static Dictionary<string, float> _originalHeroValues = new();
        private static Dictionary<string, FieldInfo> _heroFieldCache;

        // 可修正的属性列表及修正范围
        private static readonly List<string> HeroFields = new()
        {
            "attack_cooldown", "throwToolCooldown", "NAIL_CHARGE_TIME", "NAIL_CHARGE_TIME_QUICK",
            "RUN_SPEED", "WALK_SPEED", "DASH_SPEED", "DASH_TIME", "AIR_DASH_TIME", "MAX_FALL_VELOCITY"
        };

        private static readonly Dictionary<string, (float positive, float negativeMin, float negativeMax)> FieldRanges = new()
        {
            ["attack_cooldown"] = (0.25f, 1.2f, 2.0f),
            ["throwToolCooldown"] = (0.25f, 1.2f, 2.0f),
            ["NAIL_CHARGE_TIME"] = (0.25f, 1.2f, 2.0f),
            ["NAIL_CHARGE_TIME_QUICK"] = (0.25f, 1.2f, 2.0f),
            ["RUN_SPEED"] = (1.5f, 0.7f, 0.9f),
            ["WALK_SPEED"] = (1.5f, 0.7f, 0.9f),
            ["DASH_SPEED"] = (1.5f, 0.7f, 0.9f),
            ["DASH_TIME"] = (1.5f, 0.7f, 0.9f),
            ["AIR_DASH_TIME"] = (1.5f, 0.7f, 0.9f),
            ["MAX_FALL_VELOCITY"] = (1.5f, 0.7f, 0.9f)
        };

        // ========== 初始化 ==========
        public static void Initialize(IToolEffectSaveDataAccessor saveDataAccessor, int seed)
        {
            if (_initialized) return;
            _saveData = saveDataAccessor;
            _seed = seed;
            _rng = new Random(_seed ^ 0x7E57C0E);
            _heroFieldCache = new Dictionary<string, FieldInfo>();
            var heroType = typeof(HeroController);
            foreach (var fieldName in HeroFields)
            {
                var field = heroType.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                    _heroFieldCache[fieldName] = field;
            }
            _initialized = true;
        }

        // ========== 开关控制 ==========
        public static void SetEnabled(bool enabled)
        {
            if (_enabled == enabled) return;
            _enabled = enabled;

            if (!_enabled)
            {
                RemoveCrestEffects();
                ResetHeroToOriginal();
            }
            else
            {
                ApplyCurrentCrest();
            }
            Plugin.Log.LogInfo($"纹章诅咒效果: {(_enabled ? "启用" : "禁用")}");
        }

        // ========== 生成所有纹章的乘数（懒加载） ==========
        private static void EnsureAllCrestMultipliers()
        {
            var crestEffects = _saveData.GetCrestEffects();
            if (crestEffects.Count > 0) return;

            var allCrests = Resources.FindObjectsOfTypeAll<ToolCrest>()
                .Where(c => c != null && !string.IsNullOrEmpty(c.name))
                .Select(c => c.name)
                .Distinct()
                .ToList();

            if (allCrests.Count == 0) return;

            foreach (var crestId in allCrests)
            {
                if (crestEffects.ContainsKey(crestId)) continue;

                var shuffled = HeroFields.OrderBy(x => _rng.Next()).ToList();
                var positiveFields = shuffled.Take(3).ToList();

                var mults = new Dictionary<string, float>();
                foreach (var field in HeroFields)
                {
                    var range = FieldRanges[field];
                    double r = _rng.NextDouble();
                    float mult = (float)(range.negativeMin + r * (range.negativeMax - range.negativeMin));
                    mults[field] = mult;
                }
                foreach (var field in positiveFields)
                {
                    if (FieldRanges.TryGetValue(field, out var range))
                        mults[field] = range.positive;
                }
                crestEffects[crestId] = mults;
            }
            _saveData.SaveCrestEffects(crestEffects);
        }

        // ========== 英雄属性备份与恢复 ==========
        private static void SaveOriginalHeroValues()
        {
            if (_originalHeroValues.Count > 0) return;
            HeroController hero = HeroController.instance;
            if (hero == null) return;

            foreach (var kv in _heroFieldCache)
            {
                try
                {
                    float val = (float)kv.Value.GetValue(hero);
                    _originalHeroValues[kv.Key] = val;
                }
                catch { }
            }
        }

        private static void ResetHeroToOriginal()
        {
            if (_originalHeroValues.Count == 0) return;
            HeroController hero = HeroController.instance;
            if (hero == null) return;

            foreach (var kv in _originalHeroValues)
            {
                if (_heroFieldCache.TryGetValue(kv.Key, out var field))
                    field.SetValue(hero, kv.Value);
            }
        }

        // ========== 应用/移除纹章效果 ==========
        public static void ApplyCrestEffects(string crestId)
        {
            if (!_enabled) return;
            if (!_initialized || !SilksongItemRandomizerAPI.IsEnabled()) return;
            if (string.IsNullOrEmpty(crestId)) return;

            HeroController hero = HeroController.instance;
            if (hero == null) return;

            SaveOriginalHeroValues();

            EnsureAllCrestMultipliers();

            var crestEffects = _saveData.GetCrestEffects();
            if (!crestEffects.TryGetValue(crestId, out var mults)) return;

            foreach (var kv in _heroFieldCache)
            {
                string fieldName = kv.Key;
                var field = kv.Value;
                if (!_originalHeroValues.TryGetValue(fieldName, out float original)) continue;
                if (!mults.TryGetValue(fieldName, out var mult)) continue;

                float newValue = original * mult;
                if (fieldName.Contains("Cooldown") || fieldName.Contains("Time"))
                    newValue = Mathf.Max(0.05f, newValue);
                if (fieldName.Contains("SPEED"))
                    newValue = Mathf.Max(0.3f, newValue);
                field.SetValue(hero, newValue);
            }
        }

        public static void RemoveCrestEffects()
        {
            if (_originalHeroValues.Count == 0) return;
            HeroController hero = HeroController.instance;
            if (hero == null) return;

            foreach (var kv in _originalHeroValues)
            {
                if (_heroFieldCache.TryGetValue(kv.Key, out var field))
                    field.SetValue(hero, kv.Value);
            }
        }

        public static void ApplyCurrentCrest()
        {
            if (!_enabled) return;
            var pd = PlayerData.instance;
            if (pd != null && !string.IsNullOrEmpty(pd.CurrentCrestID))
                ApplyCrestEffects(pd.CurrentCrestID);
        }

        // ========== 重置所有乘数（重新生成） ==========
        public static void ResetAllEffects()
        {
            _saveData.GetCrestEffects().Clear();
            _saveData.SaveCrestEffects(new Dictionary<string, Dictionary<string, float>>());
            _originalHeroValues.Clear();
            EnsureAllCrestMultipliers();
            ApplyCurrentCrest();
        }
    }
}