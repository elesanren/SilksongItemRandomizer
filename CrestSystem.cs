using Random = System.Random;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System;
using UnityEngine;

// CrestRandomizer.cs - 修复后的完整版本

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 纹章随机器所需的持久化数据访问接口
    /// </summary>
    public interface ICrestSaveDataAccessor
    {
        Dictionary<string, string> GetCrestMappings();
        void SaveCrestMappings(Dictionary<string, string> mappings);
        HashSet<string> GetUnlockedCrests();
        void SaveUnlockedCrests(HashSet<string> unlockedCrests);
        string GetLastUnlockedCrest();
        void SetLastUnlockedCrest(string crestName);
    }

    /// <summary>
    /// 纹章随机核心逻辑
    /// 改造后：所有配置通过 Initialize 传入，持久化通过接口访问
    /// 保留所有原有功能：纹章映射、已解锁纹章记录、最后解锁纹章等
    /// </summary>
    public static class CrestRandomizer
    {
        private static bool _initialized = false;
        private static List<ToolCrest> _allCrests;
        private static Dictionary<string, ToolCrest> _crestDict;  // name → ToolCrest 快速查找
        private static ICrestSaveDataAccessor _saveData;
        private static Random _rng;
        private static int _seed;
        private static bool _enabled = true;      // 纹章随机总开关（外部控制）
        private static bool _temporaryDisable = false;  // 临时禁用（防止递归）

        /// <summary>
        /// 总开关（外部只读）
        /// 逻辑：启用 && !临时禁用
        /// </summary>
        public static bool IsEnabled => _enabled && !_temporaryDisable;

        /// <summary>
        /// 排除池的纹章名称（永远不参与随机目标）
        /// </summary>
        public static readonly HashSet<string> ExcludeFromPool = new(StringComparer.OrdinalIgnoreCase)
        {
            "Hunter", "Hunter_v2", "Hunter_v3", "Witch_v2", "Cursed", "Cloakless"
        };

        /// <summary>
        /// 是否 Hunter 系列纹章（初始纹章升级链）
        /// 剧情/梦境进出时游戏会自动设置这些纹章，参与随机会导致纹章被意外替换
        /// </summary>
        private static bool IsHunterSeries(string crestName)
        {
            if (string.IsNullOrEmpty(crestName)) return false;
            return string.Equals(crestName, "Hunter", StringComparison.OrdinalIgnoreCase)
                || string.Equals(crestName, "Hunter_v2", StringComparison.OrdinalIgnoreCase)
                || string.Equals(crestName, "Hunter_v3", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 初始化纹章随机系统
        /// </summary>
        /// <param name="seed">随机种子</param>
        /// <param name="enabled">是否启用纹章随机</param>
        /// <param name="saveDataAccessor">持久化数据访问接口</param>
        public static void Initialize(int seed, bool enabled, ICrestSaveDataAccessor saveDataAccessor)
        {
            _seed = seed;
            _rng = seed == 0 ? new Random() : new Random(seed);
            _enabled = enabled;
            _saveData = saveDataAccessor;

            // 加载所有纹章资源
            _allCrests = Resources.FindObjectsOfTypeAll<ToolCrest>().ToList();
            _crestDict = new Dictionary<string, ToolCrest>(_allCrests.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var c in _allCrests)
                if (c != null && !string.IsNullOrEmpty(c.name))
                    _crestDict[c.name] = c;

            // 确保初始纹章（猎人）已解锁
            EnsureInitialCrests();

            _initialized = true;
            Plugin.Log.LogInfo($"纹章随机初始化完成，已有 {_saveData.GetCrestMappings().Count} 个映射，已解锁 {_saveData.GetUnlockedCrests().Count} 个纹章");
        }

        /// <summary>
        /// 设置纹章随机开关（运行时动态修改）
        /// </summary>
        public static void SetEnabled(bool enabled)
        {
            _enabled = enabled;
            Plugin.Log.LogInfo($"纹章随机开关: {(_enabled ? "启用" : "禁用")}");
        }

        /// <summary>
        /// 临时禁用随机（解锁过程中避免递归）
        /// </summary>
        public static void DisableRandom() => _temporaryDisable = true;
        public static void EnableRandom() => _temporaryDisable = false;

        /// <summary>
        /// 获取所有纹章列表
        /// </summary>
        public static List<ToolCrest> CrestList => _allCrests;

        /// <summary>
        /// 获取映射表（只读）- 返回副本避免外部修改
        /// </summary>
        public static IReadOnlyDictionary<string, string> CrestMappings => new Dictionary<string, string>(_saveData.GetCrestMappings());

        /// <summary>
        /// 获取已解锁纹章集合（只读）- 返回副本避免外部修改
        /// </summary>
        public static IEnumerable<string> UnlockedCrests => _saveData.GetUnlockedCrests().ToList();

        /// <summary>
        /// 最后解锁的纹章名称
        /// </summary>
        public static string LastUnlockedCrest
        {
            get
            {
                if (!IsEnabled) return "";
                return _saveData.GetLastUnlockedCrest();
            }
            private set => _saveData.SetLastUnlockedCrest(value);
        }

        // ========== 内部辅助方法 ==========
        private static ToolCrest FindCrestByName(string name)
        {
            if (_crestDict == null) return null;
            _crestDict.TryGetValue(name, out var crest);
            return crest;
        }

        private static void EnsureInitialCrests()
        {
            var unlocked = _saveData.GetUnlockedCrests();
            if (unlocked.Count == 0)
            {
                unlocked.Add("Hunter");
                _saveData.SaveUnlockedCrests(unlocked);
                LastUnlockedCrest = "Hunter";
            }

            // 同步 PlayerData 中的解锁状态（确保工具纹章已解锁）
            foreach (string crestName in unlocked)
            {
                var crest = FindCrestByName(crestName);
                if (crest != null && !crest.IsUnlocked)
                    UnlockCrestDirectly(crest);
            }
        }

        private static void UnlockCrestDirectly(ToolCrest crest)
        {
            var data = crest.SaveData;
            data.IsUnlocked = true;
            crest.SaveData = data;
        }

        /// <summary>
        /// 记录纹章已解锁（由补丁调用）
        /// </summary>
        public static void AddUnlockedCrest(string crestName)
        {
            var unlocked = _saveData.GetUnlockedCrests();
            if (unlocked.Add(crestName))
            {
                _saveData.SaveUnlockedCrests(unlocked);
                var crest = FindCrestByName(crestName);
                if (crest != null && !crest.IsUnlocked)
                    UnlockCrestDirectly(crest);
                LastUnlockedCrest = crestName;
                Plugin.Log.LogInfo($"记录最后解锁纹章: {LastUnlockedCrest}");
            }
        }

        /// <summary>
        /// 获取纹章的映射目标名称（如果开关关闭，返回原名称）
        /// </summary>
        public static string GetMappedCrestName(string sourceCrestName)
        {
            if (!IsEnabled)
                return sourceCrestName;

            // Hunter 系列是初始纹章升级链（剧情/梦境进出会由游戏自动设置），不作为源参与随机映射
            if (IsHunterSeries(sourceCrestName))
                return sourceCrestName;

            // 已经解锁的纹章不需要再次随机
            if (_saveData.GetUnlockedCrests().Contains(sourceCrestName))
                return sourceCrestName;

            var mappings = _saveData.GetCrestMappings();
            if (mappings.TryGetValue(sourceCrestName, out var existing))
                return existing;

            // 构建候选池（排除排除列表、带 _v 的纹章、以及自身）
            var candidates = _allCrests
                .Where(c => !ExcludeFromPool.Contains(c.name) && !c.name.Contains("_v") && c.name != sourceCrestName)
                .Select(c => c.name)
                .ToList();

            string targetName;
            if (candidates.Count == 0)
                targetName = sourceCrestName;
            else
            {
                var localRng = new Random(_seed ^ sourceCrestName.GetHashCode());
                targetName = candidates[localRng.Next(candidates.Count)];
            }

            mappings[sourceCrestName] = targetName;
            _saveData.SaveCrestMappings(mappings);
            Plugin.Log.LogInfo($"新映射: {sourceCrestName} -> {targetName}");
            return targetName;
        }

        /// <summary>
        /// 重生专用：获取纹章映射名称
        /// </summary>
        public static string GetMappedCrestNameForRespawn(string sourceCrestName) => GetMappedCrestName(sourceCrestName);

        /// <summary>
        /// 重置所有映射（不清除已解锁纹章）
        /// </summary>
        public static void ResetMappings()
        {
            _saveData.GetCrestMappings().Clear();
            _saveData.SaveCrestMappings(new Dictionary<string, string>());
            _saveData.GetUnlockedCrests().Clear();
            EnsureInitialCrests();
            Plugin.Log.LogInfo("纹章映射已重置");
        }

        /// <summary>
        /// 重置已解锁纹章（保留初始 Hunter）
        /// </summary>
        public static void ResetUnlockedCrests()
        {
            _saveData.GetUnlockedCrests().Clear();
            EnsureInitialCrests();
            Plugin.Log.LogInfo("纹章解锁记录已重置");
        }

        /// <summary>
        /// 根据目标纹章名称查找源纹章名称
        /// </summary>
        public static string GetSourceCrestByTarget(string targetCrestName)
        {
            foreach (var kvp in _saveData.GetCrestMappings())
                if (kvp.Value == targetCrestName)
                    return kvp.Key;
            return null;
        }
    }
}

