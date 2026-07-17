using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SilksongItemRandomizer
{
    [HarmonyPatch]
    public static class BenchwarpTranslator
    {
        // 完整中文翻译字典（与之前相同，省略）
        private static readonly Dictionary<string, string> ChineseMap = new()
        {
            // 此处省略，请复制您之前提供的完整字典
        };

        private static bool DetectChinese()
        {
            // 与您的 DetectChinese 逻辑一致
            try
            {
                var fmType = Type.GetType("FontManager, Assembly-CSharp");
                if (fmType != null)
                {
                    var field = fmType.GetField("_currentLanguage", BindingFlags.Static | BindingFlags.NonPublic);
                    if (field != null)
                    {
                        var val = field.GetValue(null);
                        if (val != null)
                        {
                            string code = val.ToString().ToUpper();
                            if (code == "ZH" || code == "ZH_TW") return true;
                        }
                    }
                }
            }
            catch { }
            try
            {
                var gm = GameManager.instance;
                var gs = gm?.gameSettings;
                if (gs != null)
                {
                    var field = gs.GetType().GetField("language", BindingFlags.Instance | BindingFlags.Public);
                    if (field != null)
                    {
                        var val = field.GetValue(gs);
                        if (val != null)
                        {
                            string code = val.ToString().ToUpper();
                            if (code == "ZH" || code == "ZH_TW") return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private static MethodBase TargetMethod()
        {
            Type localizationType = Type.GetType("Benchwarp.Util.Localization, Benchwarp");
            return localizationType?.GetMethod("GetLanguageString", BindingFlags.Static | BindingFlags.Public);
        }

        [HarmonyPrefix]
        private static bool Prefix(string key, ref string __result)
        {
            if (DetectChinese() && ChineseMap.TryGetValue(key, out string chinese))
            {
                __result = chinese;
                return false;
            }
            return true;
        }

        public static void RefreshUI()
        {
            GameObject menu = GameObject.Find("WarpMenu") ?? GameObject.Find("BenchwarpMenu");
            if (menu != null && menu.activeInHierarchy)
            {
                menu.SetActive(false);
                menu.SetActive(true);
                Debug.Log("[Benchwarp] UI refreshed.");
            }
        }
    }
}