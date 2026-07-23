// CurrencyCollectPatch.cs - 修复后的完整版本
using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 货币收集保底（简单钥匙）补丁
    /// 改造后：通过接口访问持久化数据，不再直接依赖 Plugin.SaveData
    /// 保留所有原有功能：收集计数、阈值触发钥匙给予
    /// </summary>
    [HarmonyPatch(typeof(CurrencyObjectBase), "Collect")]
    public static class CurrencyCollectPatch
    {
        // ========== 持久化数据访问接口 ==========
        public interface ICurrencySaveDataAccessor
        {
            int GetTotalCollectCount();
            void SetTotalCollectCount(int count);
            bool GetFirstKeyGiven();
            void SetFirstKeyGiven(bool given);
            bool GetSecondKeyGiven();
            void SetSecondKeyGiven(bool given);
            int GetFirstThreshold();
            void SetFirstThreshold(int threshold);
            int GetSecondThreshold();
            void SetSecondThreshold(int threshold);
        }

        private static ICurrencySaveDataAccessor _saveData;
        private static SavedItem _cachedSimpleKey;  // 缓存 Simple Key 引用，避免每次 GiveKey 扫描
        private const string KeyName = "Simple Key";

        // ========== 初始化 ==========
        public static void Initialize(ICurrencySaveDataAccessor saveDataAccessor)
        {
            _saveData = saveDataAccessor;
        }

        // ========== 对外接口（用于外部重置） ==========
        public static void ResetCounters()
        {
            if (_saveData != null)
            {
                _saveData.SetTotalCollectCount(0);
                _saveData.SetFirstKeyGiven(false);
                _saveData.SetSecondKeyGiven(false);
            }
            Plugin.Log.LogInfo("货币保底计数器已重置");
        }

        public static void ResetKeyState() => ResetCounters();

        // ========== Harmony 补丁 ==========
        [HarmonyPostfix]
        private static void Postfix(bool __result)
        {
            if (!__result) return;
            if (_saveData == null) return;

            int newCount = _saveData.GetTotalCollectCount() + 1;
            _saveData.SetTotalCollectCount(newCount);

            int firstThreshold = _saveData.GetFirstThreshold();
            int secondThreshold = _saveData.GetSecondThreshold();

            if (!_saveData.GetFirstKeyGiven() && newCount >= firstThreshold)
            {
                GiveKey();
                _saveData.SetFirstKeyGiven(true);
                Plugin.Log.LogInfo($"第一次钥匙保底触发，当前货币收集次数: {newCount}");
            }
            else if (!_saveData.GetSecondKeyGiven() && newCount >= secondThreshold)
            {
                GiveKey();
                _saveData.SetSecondKeyGiven(true);
                Plugin.Log.LogInfo($"第二次钥匙保底触发，当前货币收集次数: {newCount}");
            }
        }

        private static void GiveKey()
        {
            if (_cachedSimpleKey == null)
                _cachedSimpleKey = Resources.FindObjectsOfTypeAll<SavedItem>().FirstOrDefault(i => i.name == KeyName);
            if (_cachedSimpleKey != null)
            {
                TryGetPatch.BypassRandom = true;
                try
                {
                    _cachedSimpleKey.TryGet(false, true);
                }
                finally
                {
                    TryGetPatch.BypassRandom = false;
                }
            }
            else
            {
                Plugin.Log.LogError($"钥匙保底失败：找不到物品 {KeyName}");
            }
        }
    }
}