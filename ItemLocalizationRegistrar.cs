using System;
using System.Collections.Generic;
using System.Reflection;
using TeamCherry.Localization;
using UnityEngine;

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 负责将所有物品的显示名称注册到游戏的本地化系统中，
    /// 以便在商店等 UI 中使用 LocalisedString 正确显示中文名称。
    /// </summary>
    public static class ItemLocalizationRegistrar
    {
        public const string CustomSheetName = "SilksongItemRandomizer";

        private static readonly HashSet<string> _registeredKeys = new HashSet<string>();

        private static Dictionary<string, Dictionary<string, string>> _localizationDict;

        private static bool EnsureLocalizationDict()
        {
            if (_localizationDict != null)
                return true;

            var field = typeof(Language).GetField("_currentEntrySheets", BindingFlags.Static | BindingFlags.NonPublic);
            if (field == null)
            {
                Plugin.Log.LogError("[ItemLocalization] 无法获取 Language._currentEntrySheets 字段");
                return false;
            }

            _localizationDict = field.GetValue(null) as Dictionary<string, Dictionary<string, string>>;
            if (_localizationDict == null)
            {
                Plugin.Log.LogError("[ItemLocalization] Language._currentEntrySheets 字段不是预期类型");
                return false;
            }

            return true;
        }

        public static bool RegisterItemName(string key, string displayName)
        {
            if (!EnsureLocalizationDict())
                return false;

            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(displayName))
                return false;

            if (_registeredKeys.Contains(key))
                return true;

            if (!_localizationDict.TryGetValue(CustomSheetName, out var sheet))
            {
                sheet = new Dictionary<string, string>();
                _localizationDict[CustomSheetName] = sheet;
            }

            sheet[key] = displayName;
            _registeredKeys.Add(key);

            Plugin.Log.LogDebug($"[ItemLocalization] 注册物品名称: {key} -> {displayName}");
            return true;
        }

        public static string GetLocalizationKey(string itemInternalName)
        {
            return $"item_{itemInternalName}";
        }

        public static LocalisedString GetLocalisedString(string itemInternalName)
        {
            return new LocalisedString(CustomSheetName, GetLocalizationKey(itemInternalName));
        }

        public static void RegisterAllKnownItems()
        {
            if (!EnsureLocalizationDict())
                return;

            var allSavedItems = Resources.FindObjectsOfTypeAll<SavedItem>();
            int registeredCount = 0;

            foreach (var item in allSavedItems)
            {
                if (item == null) continue;

                string internalName = item.name;
                string displayName = GetItemDisplayNameSafe(item);

                if (string.IsNullOrEmpty(displayName))
                    continue;

                string key = GetLocalizationKey(internalName);
                if (RegisterItemName(key, displayName))
                    registeredCount++;
            }

            Plugin.Log.LogInfo($"[ItemLocalization] 已注册 {registeredCount} 个物品的本地化名称");
        }

        /// <summary>
        /// 安全获取显示名称，避免调用未实现的 GetPopupName 抛出异常。
        /// </summary>
        private static string GetItemDisplayNameSafe(SavedItem item)
        {
            // 通过反射检查 GetPopupName 方法是否被重写（即 DeclaringType 不是 SavedItem）
            var method = typeof(SavedItem).GetMethod("GetPopupName", BindingFlags.Instance | BindingFlags.Public);
            if (method != null)
            {
                // 获取 item 实际类型的方法
                var actualMethod = item.GetType().GetMethod("GetPopupName", BindingFlags.Instance | BindingFlags.Public);
                if (actualMethod != null && actualMethod.DeclaringType == typeof(SavedItem))
                {
                    // 未重写，直接返回 item.name
                    return item.name;
                }
            }

            // 尝试调用，但捕获所有异常（包括 NotImplementedException 和其他）
            try
            {
                return item.GetPopupName();
            }
            catch
            {
                return item.name;
            }
        }

        public static void Reset()
        {
            if (!EnsureLocalizationDict())
                return;

            if (_localizationDict.ContainsKey(CustomSheetName))
                _localizationDict[CustomSheetName].Clear();

            _registeredKeys.Clear();
            Plugin.Log.LogInfo("[ItemLocalization] 已清空所有自定义本地化条目");
        }
    }
}