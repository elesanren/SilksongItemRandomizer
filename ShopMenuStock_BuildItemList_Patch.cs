// ShopMenuStock_BuildItemList_Patch.cs - 修复后的完整版本
using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using TeamCherry.Localization;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 商店菜单构建补丁：动态生成最多 12 个槽位，每个槽位随机分配物品
    /// 改造后：通过 ShopRandomizer 获取物品，槽位计数通过接口持久化
    /// 保留所有原有功能：槽位数量（12）、本地化注册、价格显示、滚动条调整等
    /// </summary>
    [HarmonyPatch(typeof(ShopMenuStock), "BuildItemList")]
    public static class ShopMenuStock_BuildItemList_Patch
    {
        private const string LocalizationSheet = "SilksongItemRandomizer";
        private const int TARGET_SLOT_COUNT = 12;

        private static FieldInfo _availableStockField;
        private static FieldInfo _spawnedStockField;
        private static FieldInfo _yDistanceField;
        private static FieldInfo _displayNameField;
        private static FieldInfo _descriptionField;

        // 槽位计数持久化接口 - 更新为直接使用 PluginSaveDataAccessor 实现的接口
        // 不再需要单独的 IShopSlotCountAccessor，因为 PluginSaveDataAccessor 已经实现了需要的接口
        public interface IShopSlotCountAccessor
        {
            int GetCount(string permanentId);
            void SetCount(string permanentId, int count);
        }

        private static IShopSlotCountAccessor _slotCountAccessor;

        /// <summary>当前商店菜单解析出的店主标识（多店主场景非 null），购买补丁取槽位 ID 时共用</summary>
        public static string CurrentShopDisc;

        public static void Initialize(IShopSlotCountAccessor accessor)
        {
            _slotCountAccessor = accessor;
        }

        private static void RegisterTranslation(string key, string value)
        {
            try
            {
                var field = typeof(Language).GetField("_currentEntrySheets", BindingFlags.Static | BindingFlags.NonPublic);
                var dict = field?.GetValue(null) as Dictionary<string, Dictionary<string, string>>;
                if (dict == null) return;
                if (!dict.TryGetValue(LocalizationSheet, out var sheet))
                {
                    sheet = new Dictionary<string, string>();
                    dict[LocalizationSheet] = sheet;
                }
                sheet[key] = value;
            }
            catch { }
        }

        private static string GetItemDisplayName(SavedItem item)
        {
            if (item == null) return "???";
            try { return item.GetPopupName(); }
            catch { return item.name; }
        }

        private static string GetItemDescription(SavedItem item)
        {
            if (item == null) return "";
            try
            {
                if (item is CollectableItem c)
                    return c.GetDescription((CollectableItem.ReadSource)3);
                if (item is CollectableRelic r)
                {
                    var prop = typeof(CollectableRelic).GetProperty("Description", BindingFlags.Instance | BindingFlags.Public);
                    if (prop != null) return prop.GetValue(r)?.ToString() ?? "";
                }
                if (item is ToolItem t)
                    return t.Description;
                if (item is ToolCrest cr)
                    return cr.Description;
                var method = item.GetType().GetMethod("GetDescription", BindingFlags.Instance | BindingFlags.Public);
                if (method != null && method.DeclaringType != typeof(SavedItem))
                    return (string)method.Invoke(item, new object[] { 3 });
            }
            catch { }
            return "";
        }

        private static void EnsureShopItemFields()
        {
            if (_displayNameField == null)
            {
                var type = typeof(ShopItem);
                _displayNameField = type.GetField("displayName", BindingFlags.Instance | BindingFlags.NonPublic);
                _descriptionField = type.GetField("description", BindingFlags.Instance | BindingFlags.NonPublic);
            }
        }

        private static void EnsureReflectionFields()
        {
            if (_spawnedStockField == null)
            {
                _availableStockField = AccessTools.Field(typeof(ShopMenuStock), "availableStock");
                _spawnedStockField = AccessTools.Field(typeof(ShopMenuStock), "spawnedStock");
                _yDistanceField = AccessTools.Field(typeof(ShopMenuStock), "yDistance");
            }
            EnsureShopItemFields();
        }

        public static int GetCount(string permanentId)
        {
            if (_slotCountAccessor == null) return 1;
            return _slotCountAccessor.GetCount(permanentId);
        }

        public static void SetCount(string permanentId, int count)
        {
            if (_slotCountAccessor == null) return;
            _slotCountAccessor.SetCount(permanentId, count);
        }

        public static void ResetAllCounts()
        {
            // 无需额外操作，由外部通过接口清空
            Plugin.Log.LogInfo("商店槽位计数已重置（需要外部调用清空）");
        }

        private static ShopItem CreateShopItem(SavedItem savedItem, int price, string permanentId)
        {
            var temp = ShopItem.CreateTemp(savedItem.name);
            temp.name = permanentId;

            string boolName = $"ShopBought_{permanentId}";
            var playerDataBoolField = typeof(ShopItem).GetField("playerDataBoolName", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (playerDataBoolField != null)
                playerDataBoolField.SetValue(temp, boolName);

            var savedItemField = typeof(ShopItem).GetField("savedItem", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (savedItemField == null) return null;
            savedItemField.SetValue(temp, savedItem);

            var costField = typeof(ShopItem).GetField("cost", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (costField == null) return null;
            costField.SetValue(temp, price);

            var costRefField = typeof(ShopItem).GetField("costReference", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            costRefField?.SetValue(temp, null);

            var currencyField = typeof(ShopItem).GetField("currencyType", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            currencyField?.SetValue(temp, 0);

            ClearField(temp, "requiredItem");
            ClearField(temp, "upgradeFromItem");

            var questsField = typeof(ShopItem).GetField("questsAppearConditions", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (questsField != null && questsField.GetValue(temp) == null)
            {
                var elementType = questsField.FieldType.GetElementType();
                questsField.SetValue(temp, Array.CreateInstance(elementType ?? typeof(object), 0));
            }

            var itemSpriteScaleField = typeof(ShopItem).GetField("itemSpriteScale", BindingFlags.Instance | BindingFlags.NonPublic);
            if (itemSpriteScaleField != null)
            {
                float scale = 1f;
                string name = savedItem.name;
                if (name == "virt:HeartPiece" || name == "virt:SpoolPart" || name == "virt:MaxSilkRegenUp")
                    scale = 0.5f;
                itemSpriteScaleField.SetValue(temp, scale);
            }

            return temp;
        }

        private static void ClearField(object obj, string fieldName)
        {
            var field = obj.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field != null && field.FieldType.IsClass)
                field.SetValue(obj, null);
        }

        private static IEnumerator AdjustScrollRectDelayed(ScrollRect scrollRect, float yDistanceRaw, int visibleCount)
        {
            yield return null;
            yield return null;
            float itemHeight = Mathf.Abs(yDistanceRaw);
            float totalHeight = visibleCount * itemHeight + 40f;
            var contentRt = scrollRect.content.GetComponent<RectTransform>();
            if (contentRt != null)
            {
                contentRt.sizeDelta = new Vector2(contentRt.sizeDelta.x, totalHeight);
                LayoutRebuilder.ForceRebuildLayoutImmediate(contentRt);
                Canvas.ForceUpdateCanvases();
            }
        }

        [HarmonyPostfix]
        private static void Postfix(ShopMenuStock __instance)
        {
            if (!SilksongItemRandomizerAPI.IsEnabled()) return;

            try
            {
                EnsureReflectionFields();

                string sceneName = SceneManager.GetActiveScene().name;
                // 店主区分：多店主场景解析当前店主标识，永久 ID 带上该标识
                string disc = ShopOwnerRegistry.ResolveDiscriminator(sceneName);
                CurrentShopDisc = disc;
                var spawnedStock = _spawnedStockField.GetValue(__instance) as IList<ShopItemStats>;
                var availableStock = _availableStockField.GetValue(__instance) as IList;
                float yDistanceRaw = (float)_yDistanceField.GetValue(__instance);

                if (spawnedStock == null || spawnedStock.Count == 0) return;

                Transform container = spawnedStock[0].transform.parent;
                if (container == null)
                {
                    Plugin.Log.LogWarning("[商店] 无法找到商品容器，将使用原有槽位数");
                }

                // 补足槽位数量到 TARGET_SLOT_COUNT (12)
                if (container != null)
                {
                    int currentCount = spawnedStock.Count;
                    for (int i = currentCount; i < TARGET_SLOT_COUNT; i++)
                    {
                        GameObject newObj = UnityEngine.Object.Instantiate(spawnedStock[0].gameObject, container);
                        newObj.SetActive(true);
                        ShopItemStats newStats = newObj.GetComponent<ShopItemStats>();
                        if (newStats != null)
                        {
                            spawnedStock.Add(newStats);
                            availableStock?.Add(newStats);
                            newStats.ItemNumber = i;
                        }
                    }
                }

                float yDistance = -Mathf.Abs(yDistanceRaw);
                availableStock?.Clear();
                float yOffset = 0f;
                var usedItems = new HashSet<string>();

                for (int i = 0; i < spawnedStock.Count; i++)
                {
                    var stats = spawnedStock[i];
                    if (stats == null) continue;

                    string permanentId = disc == null ? $"{sceneName}_{i}" : $"{sceneName}_{disc}_{i}";
                    bool isPurchased = GetCount(permanentId) <= 0;

                    var shiftFsm = stats.GetComponent<PlayMakerFSM>();
                    if (shiftFsm != null && shiftFsm.FsmName == "Shift_pos")
                        shiftFsm.enabled = false;

                    if (isPurchased)
                    {
                        stats.gameObject.SetActive(false);
                        continue;
                    }

                    SavedItem item = ShopRandomizer.GetOrCreateShopItem(permanentId, out int price, usedItems);
                    if (item == null)
                    {
                        stats.gameObject.SetActive(false);
                        continue;
                    }

                    var shopItem = CreateShopItem(item, price, permanentId);
                    if (shopItem == null)
                    {
                        stats.gameObject.SetActive(false);
                        continue;
                    }

                    if (_displayNameField != null)
                    {
                        string displayName = GetItemDisplayName(item);
                        string nameKey = $"item_{item.name}";
                        RegisterTranslation(nameKey, displayName);
                        _displayNameField.SetValue(shopItem, new LocalisedString(LocalizationSheet, nameKey));
                    }
                    if (_descriptionField != null)
                    {
                        string description = GetItemDescription(item);
                        if (!string.IsNullOrEmpty(description))
                        {
                            string descKey = $"desc_{item.name}";
                            RegisterTranslation(descKey, description);
                            _descriptionField.SetValue(shopItem, new LocalisedString(LocalizationSheet, descKey));
                        }
                    }

                    stats.SetItem(shopItem);
                    stats.transform.localPosition = new Vector3(0f, yOffset, 0f);
                    stats.ItemNumber = availableStock?.Count ?? 0;
                    availableStock?.Add(stats);
                    yOffset += yDistance;
                    stats.gameObject.SetActive(true);
                    stats.UpdateAppearance();
                }

                var spawnedSubItemsField = AccessTools.Field(typeof(ShopMenuStock), "spawnedSubItems");
                if (spawnedSubItemsField != null)
                {
                    var spawnedSubItems = spawnedSubItemsField.GetValue(__instance) as IEnumerable;
                    if (spawnedSubItems != null)
                    {
                        foreach (Component sub in spawnedSubItems)
                            if (sub != null) sub.gameObject.SetActive(false);
                    }
                }

                var scrollRect = __instance.GetComponentInChildren<ScrollRect>();
                if (scrollRect != null && scrollRect.content != null)
                {
                    int visibleCount = availableStock?.Count ?? 0;
                    __instance.StartCoroutine(AdjustScrollRectDelayed(scrollRect, yDistanceRaw, visibleCount));
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"商店重建补丁异常: {ex}");
            }
        }
    }
}