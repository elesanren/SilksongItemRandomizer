using HarmonyLib;
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 商店物品购买补丁：阻止重复购买，购买后隐藏槽位
    /// 改造后：通过 ShopMenuStock_BuildItemList_Patch 的计数接口判断是否已购买
    /// 保留所有原有功能：购买后槽位消失、布局刷新
    /// </summary>
    [HarmonyPatch(typeof(ShopItemStats), "SetPurchased")]
    public static class ShopItemStats_Purchase_Patch
    {
        private static MethodInfo _buildItemListMethod;

        static ShopItemStats_Purchase_Patch()
        {
            _buildItemListMethod = typeof(ShopMenuStock).GetMethod("BuildItemList", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        [HarmonyPrefix]
        private static bool Prefix(ShopItemStats __instance, Action onComplete, int subItemIndex)
        {
            if (!SilksongItemRandomizerAPI.IsEnabled()) return true;

            string permanentId = ShopSlotHelper.GetSlotId(__instance);
            if (string.IsNullOrEmpty(permanentId)) return true;

            // 如果已经购买过（计数 <= 0），阻止再次购买
            if (ShopMenuStock_BuildItemList_Patch.GetCount(permanentId) <= 0)
                return false;

            // 标记为已购买（计数设为 0）
            ShopMenuStock_BuildItemList_Patch.SetCount(permanentId, 0);
            return true;
        }

        [HarmonyPostfix]
        private static void Postfix(ShopItemStats __instance)
        {
            if (!SilksongItemRandomizerAPI.IsEnabled()) return;

            // 隐藏购买后的槽位（与原版行为一致）
            __instance.gameObject.SetActive(false);

            // 刷新布局，避免空白间隙
            var shop = __instance.GetComponentInParent<ShopMenuStock>();
            if (shop != null)
            {
                var layout = shop.GetComponent<LayoutGroup>();
                if (layout != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(layout.GetComponent<RectTransform>());
            }
        }
    }

    /// <summary>
    /// 辅助类：获取槽位的永久 ID
    /// </summary>
    public static class ShopSlotHelper
    {
        private static FieldInfo _spawnedStockField;

        static ShopSlotHelper()
        {
            _spawnedStockField = AccessTools.Field(typeof(ShopMenuStock), "spawnedStock");
        }

        public static string GetSlotId(ShopItemStats stats)
        {
            var shop = stats.GetComponentInParent<ShopMenuStock>();
            if (shop == null || _spawnedStockField == null) return null;
            var spawnedStock = _spawnedStockField.GetValue(shop) as IList;
            if (spawnedStock == null) return null;
            int index = spawnedStock.IndexOf(stats);
            if (index < 0) return null;
            string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            // 与构建补丁共用同一店主标识，保证购买时的永久 ID 一致
            string disc = ShopMenuStock_BuildItemList_Patch.CurrentShopDisc;
            return disc == null ? $"{sceneName}_{index}" : $"{sceneName}_{disc}_{index}";
        }
    }
}