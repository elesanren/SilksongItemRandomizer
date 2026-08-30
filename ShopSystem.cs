using HarmonyLib;
using Random = System.Random;
using StartingAbilityPicker;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;
using System;
using TeamCherry.Localization;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine;

// ShopRandomizer.cs - 修复后的完整版本

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 商店随机核心逻辑
    /// 改造后：不再依赖 Plugin.SaveData，改为通过接口访问持久化数据
    /// 保留所有原有功能：物品池、价格生成、已分配物品去重等
    /// </summary>
    public static class ShopRandomizer
    {
        // ========== 持久化数据访问接口 ==========
        public interface IShopSaveDataAccessor
        {
            HashSet<string> GetAssignedItemIds();
            void SaveAssignedItemIds(HashSet<string> ids);
        }

        private static IShopSaveDataAccessor _saveData;
        private static bool _initialized = false;
        private static List<SavedItem> _allShopItems;

        // ========== 缓存（运行时） ==========
        private static readonly Dictionary<string, SavedItem> _shopItemCache = new();
        private static readonly Dictionary<string, int> _shopPriceCache = new();
        private static readonly Dictionary<System.Type, System.Reflection.MethodInfo> _popupIconMethodCache = new();  // 按类型缓存 GetPopupIcon 反射，IsSafeForShop 热路径用

        // ========== 初始化 ==========
        public static void Initialize(IShopSaveDataAccessor saveDataAccessor)
        {
            _saveData = saveDataAccessor;
            _initialized = true;
            ResetCache();
        }

        public static void ResetCache()
        {
            _shopItemCache.Clear();
            _shopPriceCache.Clear();
            _allShopItems = null;
            if (_saveData != null)
                _saveData.GetAssignedItemIds().Clear();
        }

        public static void ResetAssignedItems()
        {
            if (_saveData != null)
                _saveData.GetAssignedItemIds().Clear();
            Plugin.SaveGlobalData();
        }

        private static void MarkItemAsAssigned(string itemId)
        {
            if (_saveData != null && _saveData.GetAssignedItemIds().Add(itemId))
                Plugin.SaveGlobalData();
        }

        private static bool IsItemAssigned(string itemId)
        {
            return _saveData != null && _saveData.GetAssignedItemIds().Contains(itemId);
        }

        private static void EnsureAllShopItems()
        {
            if (_allShopItems != null) return;

            _allShopItems = new List<SavedItem>();

            var originalItems = ItemRandomizer.GetAllItems();
            if (originalItems != null)
            {
                foreach (var item in originalItems)
                    if (IsSafeForShop(item))
                        _allShopItems.Add(item);
            }

            var virtualRewards = GetVirtualRewardsForShop();
            foreach (var reward in virtualRewards)
            {
                var proxy = ScriptableObject.CreateInstance<ProxySavedItem>();
                proxy.Init(reward);
                _allShopItems.Add(proxy);
            }
        }

        private static List<IRandomReward> GetVirtualRewardsForShop()
        {
            var list = new List<IRandomReward>();

            // 普通虚拟奖励
            list.Add(new VirtualReward("virt:Health_2", Locale.Get("小生命"), FindSprite("Inv_health_backboard_SS"),
                () => { for (int i = 0; i < 2; i++) HeroController.instance?.AddHealth(1); }, () => false));
            list.Add(new VirtualReward("virt:Silk_3", Locale.Get("中灵丝"), FindSprite("silk_heart_inv_icon"),
                () => { for (int i = 0; i < 3; i++) HeroController.instance?.AddSilk(1, false); }, () => false));
            list.Add(new VirtualReward("virt:Geo_100", Locale.Get("小念珠"), FindSprite("coinget_01"),
                () => HeroController.instance?.AddGeo(100), () => false));
            list.Add(new VirtualReward("virt:Geo_300", Locale.Get("大念珠"), FindSprite("coinget_01"),
                () => HeroController.instance?.AddGeo(300), () => false));
            list.Add(new VirtualReward("virt:FullRestore", Locale.Get("完全恢复"), FindSprite("Inv_health_backboard_SS"), () =>
            {
                var hero = HeroController.instance;
                var pd = PlayerData.instance;
                if (hero == null || pd == null) return;
                int healthNeeded = pd.CurrentMaxHealth - pd.health;
                for (int i = 0; i < healthNeeded; i++) hero.AddHealth(1);
                int silkNeeded = pd.CurrentSilkMax - pd.silk;
                for (int i = 0; i < silkNeeded; i++) hero.AddSilk(1, false);
            }, () => false));

            // 有限购的虚拟奖励
            // （面具碎片/丝轴碎片不放商店：全局已有专用发放机制——heart:01~20 / spool:01~18
            //   顺序键 + RewardCore 珍贵虚拟奖励（limit 20/18），商店再放会重复发放；
            //   丝线恢复上限同理不放——由随机池 SilkHeart（完整给予含官方弹窗）承担，
            //   曾有的简易版 virt:MaxSilkRegenUp 已删除，避免与丝之心重复发放）

            // 纹章槽位解锁器（商店版；购买/池内发放共用同一 limit，表驱动）
            list.Add(new LimitedVirtualReward(
                "virt:UnlockCrestSlot",
                Locale.Get("纹章槽位解锁器"),
                FindSprite("spool_upgrade_pickup"),
                () => ItemRandomizer.TryUnlockCrestSlot(),
                ItemLimitConfig.LimitUnlockCrestSlot
            ));

            return list;
        }

        private static Sprite FindSprite(string name) => SpriteCache.Find(name);

        private static SavedItem GetRandomCoinItem(string permanentId, out int price)
        {
            price = 1;
            int amount = ItemRandomizer.Rng.Next(0, 91);
            string displayName = string.Format(Locale.Get("随机念珠"), amount);
            var reward = new VirtualReward($"virt:RandomCoin_{amount}", displayName, FindSprite("coinget_01"),
                () => HeroController.instance?.AddGeo(amount), () => false);
            var proxy = ScriptableObject.CreateInstance<ProxySavedItem>();
            proxy.Init(reward);
            return proxy;
        }

        private static SavedItem GetRandomBlueHealthItem(string permanentId, out int price)
        {
            price = 1;
            int total = UnityEngine.Random.Range(1, 7);
            string displayName = string.Format(Locale.Get("随机蓝血"), total);
            var reward = new VirtualReward($"virt:BlueHealth_{total}", displayName, FindSprite("Icon_Inv_Blue_Health_Blood"),
                () => Plugin.Instance?.StartCoroutine(GiveBlueHealthOverTime(total)), () => false);
            var proxy = ScriptableObject.CreateInstance<ProxySavedItem>();
            proxy.Init(reward);
            return proxy;
        }

        private static IEnumerator GiveBlueHealthOverTime(int total)
        {
            for (int i = 0; i < total; i++)
            {
                EventRegister.SendEvent(EventRegisterEvents.AddBlueHealth, null);
                if (i < total - 1)
                    yield return new WaitForSeconds(1f);
            }
        }

        // 核心方法：获取或创建商店物品，支持当前商店去重
        public static SavedItem GetOrCreateShopItem(string permanentId, out int price, HashSet<string> usedInThisShop)
        {
            EnsureAllShopItems();

            // ★ 预生成映射优先：key = "shop:" + permanentId，与 PreGeneratedMap.ShopSlotKeys 完全一致
            //（permanentId = "{场景名}_{槽位}"，ResolveReward 对 shop: 前缀只做精确匹配）
            string shopKey = "shop:" + permanentId;
            IRandomReward preReward = PreGeneratedMap.ResolveReward(shopKey);
            if (preReward != null)
            {
                if (_shopItemCache.TryGetValue(permanentId, out var cachedItem) && !IsItemOwned(cachedItem))
                {
                    price = _shopPriceCache[permanentId];
                    return cachedItem;
                }

                // 价格：虚拟奖励固定为 1，真实物品按种子随机
                if (preReward.Id.StartsWith("virt:"))
                {
                    price = 1;
                }
                else
                {
                    var rng = new Random(Plugin.RandomSeed.Value ^ permanentId.GetHashCode());
                    price = GenerateRandomPrice(rng);
                }

                // ★ 商店购买的随机吉欧/跳蚤：直接给予，而非在玩家位置生成堆/跳蚤
                if (preReward.Id == "virt:RandomCoin" || preReward.Id == "virt:FallbackCoin")
                {
                    int amount = ItemRandomizer.Rng.Next(0, 91);
                    var coinReward = new VirtualReward(
                        $"virt:RandomCoin_{amount}",
                        string.Format(Locale.Get("随机念珠"), amount),
                        FindSprite("I_rosary_icon_clean"),
                        () => HeroController.instance?.AddGeo(amount),
                        () => false);
                    preReward = coinReward;
                }
                else if (preReward.Id == "virt:RandomFlea" || preReward.Id == "virt:FallbackFlea")
                {
                    preReward = new VirtualReward(
                        "virt:RandomFlea",
                        Locale.Get("跳蚤救援"),
                        null,
                        () => FleaRescueBuilder.SpawnAtHero(),
                        () => false);
                }

                var proxy = ScriptableObject.CreateInstance<ProxySavedItem>();
                proxy.Init(preReward);
                _shopItemCache[permanentId] = proxy;
                _shopPriceCache[permanentId] = price;
                usedInThisShop.Add(proxy.name);
                return proxy;
            }

            // 回退（极少发生）：映射缺失时使用虚拟货币奖励
            Plugin.Log.LogWarning($"[ShopRandomizer] 映射缺失，槽位 {permanentId} 使用虚拟奖励");
            var fallbackReward = new VirtualReward(
                "virt:FallbackCoin",
                Locale.Get("随机货币"),
                FindSprite("coinget_01"),
                () => HeroController.instance?.AddGeo(ItemRandomizer.Rng.Next(1, 6)),
                () => false
            );
            price = 1;
            var fallbackProxy = ScriptableObject.CreateInstance<ProxySavedItem>();
            fallbackProxy.Init(fallbackReward);
            _shopItemCache[permanentId] = fallbackProxy;
            _shopPriceCache[permanentId] = price;
            usedInThisShop.Add(fallbackProxy.name);
            return fallbackProxy;
        }

        private static SavedItem GenerateRandomShopItem(string permanentId, out int price, HashSet<string> usedInThisShop)
        {
            var rng = new Random(Plugin.RandomSeed.Value ^ permanentId.GetHashCode());
            EnsureAllShopItems();

            if (_allShopItems.Count == 0)
            {
                price = 0;
                return null;
            }

            var candidates = _allShopItems.Where(item =>
                !IsItemOwned(item) && IsSafeForShop(item) && !IsItemAssigned(item.name) && !usedInThisShop.Contains(item.name)
            ).ToList();

            if (candidates.Count == 0)
            {
                Plugin.Log.LogWarning($"商店随机池已无可用物品，使用保底念珠 (永久ID: {permanentId})");
                price = 10;
                var fallbackReward = new VirtualReward("virt:FallbackGeo", Locale.Get("保底念珠"), FindSprite("coinget_01"),
                    () => HeroController.instance?.AddGeo(10), () => false);
                var proxy = ScriptableObject.CreateInstance<ProxySavedItem>();
                proxy.Init(fallbackReward);
                return proxy;
            }

            var selected = candidates[rng.Next(candidates.Count)];
            price = GenerateRandomPrice(rng);
            MarkItemAsAssigned(selected.name);
            return selected;
        }

        private static bool IsItemOwned(SavedItem item)
        {
            try { return !item.CanGetMore(); }
            catch { return false; }
        }

        private static bool IsSafeForShop(SavedItem item)
        {
            if (item == null) return false;
            if (ItemRandomizer.ExcludedNames.Contains(item.name)) return false;
            if (item is ToolCrest) return false;

            // 按具体类型缓存 GetPopupIcon 反射结果（物品类型有限，缓存后零反射）
            if (!_popupIconMethodCache.TryGetValue(item.GetType(), out var actualMethod))
            {
                actualMethod = item.GetType().GetMethod("GetPopupIcon", BindingFlags.Instance | BindingFlags.Public);
                if (actualMethod == null || actualMethod.DeclaringType == typeof(SavedItem))
                    actualMethod = null;
                _popupIconMethodCache[item.GetType()] = actualMethod;
            }
            if (actualMethod == null)
                return false;

            try
            {
                if (item.GetPopupIcon() == null) return false;
            }
            catch
            {
                return false;
            }
            return true;
        }

        private static int GenerateRandomPrice(Random rng)
        {
            return rng.Next(2) == 0 ? rng.Next(1, 100) : rng.Next(100, 301);
        }
    }
}

// ShopMenuStock_BuildItemList_Patch.cs - 修复后的完整版本

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
                // 代理包装（预生成/虚拟奖励）：内部若是原生物品则解包转发，恢复描述
                if (item is ProxySavedItem proxy)
                {
                    if (proxy.InnerReward is SavedItemReward sir && sir.Item != null)
                        return GetItemDescription(sir.Item);
                    return "";
                }

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
                // 方向权限图标（技能图标）原图很大，缩小防止遮挡视野
                if (name.StartsWith("perm:"))
                    scale = 0.35f;
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

// ShopOwnerRegistry.cs - 店主区分注册表
// 用途：同一场景存在多个商店店主（如 Belltown 有 4 个老式店主）时，
//       通过店主 GameObject 的场景路径区分商店实例，生成带店主标识的永久 ID。
// 原理：预生成映射（PreGeneratedMap）已按本注册表提前生成好所有 key，
//       运行时打开商店只需做一次"店主路径 → 注册表"匹配即可拿到正确 key。

namespace SilksongItemRandomizer
{
    public static class ShopOwnerRegistry
    {
        public readonly struct Entry
        {
            public readonly string Scene;         // 场景名
            public readonly string Path;          // 店主 GameObject 场景路径（根到店主）
            public readonly bool IsOldStyle;      // true=老式 ShopOwner(ShopMenuStock)；false=新式 SimpleShopMenuOwner（当前补丁不接管）
            public readonly string Discriminator; // NPC 名（去状态）净化后的店主标识（同场景内唯一）

            public Entry(string scene, string path, bool oldStyle)
            {
                Scene = scene;
                Path = path;
                IsOldStyle = oldStyle;
                Discriminator = Sanitize(NormalizeNpcName(LeafOf(path)));
            }
        }

        private static Entry E(string scene, string path) => new Entry(scene, path, true);   // 老式店主
        private static Entry N(string scene, string path) => new Entry(scene, path, false);  // 新式店主（仅登记，不参与随机）

        // 数据来源：全量商店原始数据。同一 NPC 的状态变体（Shakra Resting/Shakra Away/
        // Black Thread World 下的 Sit/Rest/StandGuard 形态）已去重，只保留一个代表条目：
        // 已移除 Belltown "Mapper Control/Shakra Resting/Mapper Sit NPC"、
        //       Belltown "Mapper Control/Black Thread World/Mapper Rest NPC"、
        //       Bonetown "Mapper Control/Shakra Resting/Mapper Sit NPC"（均并入 MapperNPC）。
        private static readonly Entry[] Entries =
        {
            E("Ant_04_mid", "Black Thread States Thread Only Variant/Normal World/Battle Scene/Mapper NPC"),
            E("Ant_20", "Mapper States/Here/Mapper Sit NPC"),
            E("Ant_Merchant", "_NPCs/Ant Merchant States/Ant Merchant"),
            E("Belltown", "Mapper Control/Shakra Away/Mapper NPC"),
            N("Belltown", "Couriers States/Here/Couriers Quest Giver"),
            E("Belltown", "Town States/Spinner Defeated/Bagpipers Not Here/Belltown Shop NPC"),
            E("Bone_04", "Mapper NPC"),
            N("Bone_10", "Black Thread States Thread Only Variant/Normal World/Caravan/Caravan State Regular/Caravan Troupe Hunter"),
            E("Bone_East_01", "Mapper NPC"),
            E("Bone_East_10_Room", "Black Thread States/Normal World/Pilgrims Rest Shop"),
            E("Bone_East_21", "Mapper Sit NPC"),
            E("Bonetown", "Mapper Control/Shakra Away/Mapper NPC"),
            E("Bonetown", "Black Thread States/Normal World/Bonechurch_Shop"),
            E("Coral_12", "Mapper NPC (1)"),
            E("Coral_40", "Mapper Sit NPC"),
            E("Coral_42", "Thief NPC Shop"),
            N("Coral_Judge_Arena", "Caravan_Set/Caravan/Active/Caravan Troupe Hunter"),
            E("Crawl_01", "Mapper NPC"),
            E("Dust_10", "Mapper Sit NPC"),
            E("Greymoor_02", "Mapper Sit NPC"),
            E("Greymoor_08", "Black Thread States Thread Only Variant/Black Thread World/Shakra Guard Scene/Scene Folder/Mapper StandGuard NPC"),
            E("Hang_04", "Black Thread States/Normal World/Aftermath Control/Battle Aftermath/City Merchant Scavenge Generic"),
            E("Library_03", "City Merchant Scavenge Generic"),
            E("Peak_02", "Mapper NPC"),
            E("Room_Forge", "_NPCs/Forge Daughter"),
            E("Shadow_23", "Mapper Sit NPC"),
            E("Shellwood_01", "Black Thread States/Black Thread World/Shakra Guard Scene/Scene Folder/Mapper StandGuard NPC"),
            E("Shellwood_16", "Scene Control/Mapper NPC"),
            E("Song_Enclave", "Black Thread States/Normal World/Enclave States/States/Level 1/City Merchant Enclave"),
            E("Under_17", "Architect Scene/Chair/pillar E/pillar D/pillar C/pillar B/pillar A/seat/Architect NPC"),
        };

        /// <summary>当前正在交互的老式店主（由 SpawnUpdateShop 补丁记录）</summary>
        public static ShopOwnerBase CurrentOwner;

        private static string LeafOf(string path)
        {
            int idx = path.LastIndexOf('/');
            return idx >= 0 ? path.Substring(idx + 1) : path;
        }

        private static string Sanitize(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
                if (char.IsLetterOrDigit(c) || c == '_')
                    sb.Append(c);
            return sb.ToString();
        }

        /// <summary>
        /// 去掉 NPC 名的状态后缀：同一 NPC 的 Sit/Rest/StandGuard 形态视为同一个映射。
        /// 只处理 "... NPC" 结尾的名字，不影响 "Pilgrims Rest Shop" 这类商店名。
        /// </summary>
        private static string NormalizeNpcName(string name)
        {
            return name
                .Replace(" StandGuard NPC", " NPC")
                .Replace(" Sit NPC", " NPC")
                .Replace(" Rest NPC", " NPC");
        }

        private static List<Entry> OldStyleEntriesForScene(string sceneName)
        {
            var list = new List<Entry>();
            foreach (var e in Entries)
                if (e.IsOldStyle && e.Scene == sceneName)
                    list.Add(e);
            return list;
        }

        /// <summary>
        /// 解析当前场景商店的店主标识（只看 房间 + NPC 名，状态变体归并）。
        /// 返回 null 表示该场景只有一个老式店主（沿用旧 key 格式，兼容旧存档）；
        /// 返回非 null 表示多店主场景，永久 ID 需带上该标识。
        /// </summary>
        public static string ResolveDiscriminator(string sceneName)
        {
            var sceneEntries = OldStyleEntriesForScene(sceneName);
            if (sceneEntries.Count <= 1)
                return null;

            var owner = CurrentOwner;
            if (owner == null || owner.gameObject.scene.name != sceneName)
            {
                Plugin.Log.LogWarning($"[店主区分] {sceneName} 有 {sceneEntries.Count} 个店主，但未能捕获当前店主，回退旧格式");
                return null;
            }

            // 按当前店主的 NPC 名（去状态）匹配注册表标识
            string disc = Sanitize(NormalizeNpcName(owner.gameObject.name));
            foreach (var e in sceneEntries)
                if (e.Discriminator == disc)
                    return disc;

            Plugin.Log.LogWarning($"[店主区分] {sceneName} 店主匹配失败（NPC 名: {owner.gameObject.name}），回退旧格式");
            return null;
        }

        // 注意：商店槽位 key 已逐条硬编码在 PreGeneratedMap.ShopSlotKeys（去重后 27 个老式商店 × 12 = 324 条），
        // 本注册表只负责运行时"当前店主 → 店主标识"的一次匹配。
        // 若改动 Entries 的 Discriminator，必须同步修改 PreGeneratedMap.ShopSlotKeys。
    }

    /// <summary>
    /// 记录当前交互的老式店主：官方 ShopCheck 每次开店都会走 ShopObject getter → SpawnUpdateShop，
    /// 因此该补丁必然先于 ShopMenuStock.BuildItemList 触发。
    /// </summary>
    [HarmonyPatch(typeof(ShopOwnerBase), "SpawnUpdateShop")]
    public static class ShopOwnerBase_SpawnUpdateShop_Patch
    {
        [HarmonyPostfix]
        private static void Postfix(ShopOwnerBase __instance)
        {
            ShopOwnerRegistry.CurrentOwner = __instance;
        }
    }
}

// PreGeneratedMap.cs - 全量预生成映射，数据硬编码，不依赖外部文件

namespace SilksongItemRandomizer
{
    public static class PreGeneratedMap
    {
        public static string PendingKey = null;

        // ========== 检查点 / 商店槽位数据（外置：嵌入资源 + 可选外部覆盖） ==========
        // 数据源：
        //   check_points.txt      -> 检查点（原 EmbeddedPointLines）
        //   shop_slot_keys.txt    -> 商店槽位键（原 ShopSlotKeys）
        // 加载优先级：插件目录内同名覆盖文件 > 编译进 DLL 的嵌入资源。
        // 嵌入资源随 DLL 发布不会丢失；把同名 txt 放到 DLL 同目录可无编译更新数据。
        // 数据读取为逐行 string[]，喂给下方 BuildAllMappings 的解析逻辑，
        // 与旧内联数组逐字一致，匹配/解析逻辑完全不变，只是数据源头外置。
        private const string CheckPointsResource = "SilksongItemRandomizer.Resources.check_points.txt";
        private const string ShopSlotKeysResource = "SilksongItemRandomizer.Resources.shop_slot_keys.txt";

        private static string[] _checkPoints = null;
        private static string[] _shopSlotKeys = null;

        private static string PluginDir
        {
            get
            {
                try { return System.IO.Path.GetDirectoryName(typeof(PreGeneratedMap).Assembly.Location); }
                catch { return null; }
            }
        }

        private static string[] ReadEmbeddedLines(string resourceName)
        {
            try
            {
                var asm = typeof(PreGeneratedMap).Assembly;
                using (var stream = asm.GetManifestResourceStream(resourceName))
                {
                    if (stream == null) return null;
                    using (var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8))
                        return reader.ReadToEnd().Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                }
            }
            catch { return null; }
        }

        private static string[] ReadExternalOverride(string fileName)
        {
            try
            {
                string dir = PluginDir;
                if (string.IsNullOrEmpty(dir)) return null;
                string path = System.IO.Path.Combine(dir, fileName);
                if (!System.IO.File.Exists(path)) return null;
                return System.IO.File.ReadAllLines(path);
            }
            catch { return null; }
        }

        private static string[] LoadCheckPoints()
            => _checkPoints ??= ReadExternalOverride("check_points.txt") ?? ReadEmbeddedLines(CheckPointsResource) ?? Array.Empty<string>();

        private static string[] LoadShopSlotKeys()
            => _shopSlotKeys ??= ReadExternalOverride("shop_slot_keys.txt") ?? ReadEmbeddedLines(ShopSlotKeysResource) ?? Array.Empty<string>();

        /// <summary>检查点数据（外置加载，懒缓存）</summary>
        private static string[] EmbeddedPointLines => LoadCheckPoints();

        /// <summary>商店槽位键（外置加载，懒缓存）</summary>
        private static string[] ShopSlotKeys => LoadShopSlotKeys();

        /// <summary>数据缓存重置（Reset 时调用，供外部覆盖文件变更后强制重读）</summary>
        private static void ResetDataCache() { _checkPoints = null; _shopSlotKeys = null; }

        private const float CoordinateTolerance = 2.0f;

        // ========== 剧情演出拦截点（事件类随机点）==========
        // 场景全集引用 RegionReplacePatch / ChurchRandomizePatch 的拦截清单（单一事实来源）：
        //   技能演出（SpawnSkillGetMsg/PowerUpGetMsg）+ UI Msg 全屏弹窗 + 丝之心无弹窗梦境 + 教堂神社。
        // 一个场景最多一个剧情拦截点，故键 = "event:" + 场景名（与拾取点坐标键/lore:/check:/shop: 天然不撞）。
        internal static IEnumerable<string> EventKeys()
        {
            foreach (var s in SkillRegionRandomizePatch.SkillScenes) yield return "event:" + s;
            foreach (var s in SkillRegionRandomizePatch.UIMsgScenes) yield return "event:" + s;
            foreach (var s in SkillRegionRandomizePatch.MemoryNoPopupScenes) yield return "event:" + s;
            foreach (var s in ChurchRandomizePatch.ChapelShrineScenes) yield return "event:" + s;
            // weave_10 织女五个独立随机点位（两纹章升级/两额外槽/风铃谣）：预注册保证同种子结果
            // 确定。场景键 event:weave_10 因 SkillScenes 仍会预分配但永不 Resolve（无害预留）。
            foreach (var s in new[] { "weave_10:CrestUpg1", "weave_10:CrestUpg2", "weave_10:Slot1", "weave_10:Slot2", "weave_10:EvaHeal" })
                yield return "event:" + s;
        }

        /// <summary>触发侧查询：按当前拦截点场景取预生成奖励；未命中返回 null 由调用方回落现抽。</summary>
        public static IRandomReward ResolveEventReward(string scene)
        {
            if (string.IsNullOrEmpty(scene)) return null;
            return ResolveReward("event:" + scene);
        }

        private static bool _initialized = false;

        // 场景索引缓存（FindKeyByProximity 容差匹配用）：按场景分组 key，避免每次全表线性扫描
        private static Dictionary<string, string> _sceneIndexDictRef = null;
        private static readonly Dictionary<string, List<string>> SceneKeysCache =
            new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        private static void EnsureSceneIndex()
        {
            var dict = Dict;
            if (dict == null) return;
            if (ReferenceEquals(_sceneIndexDictRef, dict)) return;
            _sceneIndexDictRef = dict;
            SceneKeysCache.Clear();
            foreach (var key in dict.Keys)
            {
                string scene = ExtractScene(key);
                if (string.IsNullOrEmpty(scene)) continue;
                if (!SceneKeysCache.TryGetValue(scene, out var list))
                {
                    list = new List<string>();
                    SceneKeysCache[scene] = list;
                }
                list.Add(key);
            }
        }

        private static Dictionary<string, string> Dict
        {
            get
            {
                if (Plugin.SaveData == null) return null;
                if (Plugin.SaveData.PreGeneratedMappings == null)
                    Plugin.SaveData.PreGeneratedMappings = new Dictionary<string, string>();
                return Plugin.SaveData.PreGeneratedMappings;
            }
        }

        /// <summary>
        /// 初始化预生成映射。
        /// 判断依据是「持久化的配置指纹」（落盘，重启后依旧在）：
        ///   - 指纹一致 且 映射表非空 → 认为已被本次配置生成过，直接跳过，绝不重复重建；
        ///   - 指纹不一致（配置/种子变化）或映射表为空 → 才重新生成。
        /// 静态 _initialized 仅作进程内缓存标记，不代表跨启动的已生成状态。
        /// </summary>
        public static void Initialize()
        {
            if (!SilksongItemRandomizerAPI.IsEnabled()) return;
            if (!ItemRandomizer.IsInitialized) return;

            string stamp = BuildConfigStamp();
            var dict = Dict;
            bool stampMatches = Plugin.SaveData.MappingsConfigStamp == stamp;
            bool hasMappings = dict != null && dict.Count > 0;

            // 已持久化的指纹与本轮一致，且映射表已有内容：本次配置下已生成过，无需重建
            if (stampMatches && hasMappings)
            {
                _initialized = true;
                return;
            }

            // 指纹变化（种子/limit 有差异）或映射为空 → 自动失效旧映射重建
            Plugin.Log.LogInfo($"[PreGeneratedMap] 映射配置指纹: {stamp}，保存值: {Plugin.SaveData.MappingsConfigStamp ?? "(空)"}，{(stampMatches ? "映射为空，重建" : "配置有变化，重新生成")}");
            BuildAllMappings();
            Plugin.SaveData.MappingsConfigStamp = stamp;
            Plugin.SaveGlobalData();
            _initialized = true;
            Plugin.Log.LogInfo($"[PreGeneratedMap] 全量映射构建完成，共 {Dict?.Count ?? 0} 条映射");
        }

        /// <summary>生成当前配置指纹：全部 limit 额度 + 随机种子 + 映射数据版本（凡会影响映射分配的内容）
        /// 数据版本随 check_points.txt 等映射数据变更时手动 +1，强制旧存档重建映射。</summary>
        private static string BuildConfigStamp()
        {
            // data3：方向权限 Id 规则变更（攻击/回血去掉方向后缀），旧映射表残留的
            // reward:perm:upward_R 等旧键在新 Id 下找不到，需强制重建一次
            // data4：剧情演出拦截点纳入预分配（event:场景 键入池），旧映射无这些键，强制重建
            // data5：删除简易版 virt:MaxSilkRegenUp（与丝之心重复），旧映射残留该奖励键，强制重建
            // data6：跳蚤救援 flea:01~27 顺序键纳入预分配，旧映射无这些键，强制重建
            // data7：删除收费机权限物（virt:Permit:*）与收费机 lore 键，收费机奖励改走 check 键，旧映射残留，强制重建
            return $"L{ItemLimitConfig.BuildLimitsStamp()}|seed{Plugin.RandomSeed?.Value ?? 0}|data7";
        }

        /// <summary>
        /// 全覆盖模式：一次性构建全量映射。
        /// 所有有限奖励（真实物品、能力、方向权限、地图、车站、珍贵虚拟、Lore 等）
        /// 按可分配次数构成奖励池，与全部映射键一起随机打乱后分配。
        /// 每个真实物品恰好一次，每种有限虚拟奖励至少一次（珍贵虚拟按配置可多次），
        /// 剩余映射点使用加权无限虚拟奖励池填充（随机货币权重 6，其余各 1）。
        /// </summary>
        private static void BuildAllMappings()
        {
            var dict = Dict;
            if (dict == null) return;

            // 1. 清空现有映射
            dict.Clear();

            // 2. 收集所有映射键
            var allKeys = new HashSet<string>();

            // 2.1 从 EmbeddedPointLines 提取 Pickup 和 Lore 键
            foreach (var rawLine in EmbeddedPointLines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;
                var parts = line.Split('|');
                if (parts.Length < 9) continue;
                string type = parts[0].Trim();
                string scene = parts[1].Trim();
                string name = parts[3].Trim();
                string xStr = parts[6].Trim();
                string yStr = parts[7].Trim();
                string key = null;
                if (type == "Pickup")
                {
                    if (float.TryParse(xStr, out float px) && float.TryParse(yStr, out float py))
                        key = $"{scene}_{px:F2}_{py:F2}_0.00";
                    else
                        key = parts[5].Trim(); // fallback
                }
                else if (type == "Lore")
                {
                    // ★ 仅生成 LoreTable 白名单内的 lore 键（石碑/铭文日志）；
                    // 收费机（Bellway Toll Machine / tube_toll_machine 等）不在白名单，
                    // 其奖励已改由车站/管道/地图购买走 check 键发放，不再生成 lore 键。
                    if (LoreRandomizer.IsLoreObject(scene, name))
                        key = $"lore:{scene}:{name}";
                }
                // 忽略 Toll 和 BellBench（它们由 check 键处理）
                if (!string.IsNullOrEmpty(key))
                    allKeys.Add(key);
            }

            // 2.2 地图和车站 check 键
            foreach (var (boolName, _) in MapStationRewards.Maps)
                allKeys.Add("check:" + boolName);
            foreach (var (boolName, _) in MapStationRewards.Stations)
                allKeys.Add("check:" + boolName);
            foreach (var (boolName, _) in MapStationRewards.Tubes)
                allKeys.Add("check:" + boolName);

            // 2.3 商店槽位键（已含 "shop:" 前缀）
            foreach (var key in ShopSlotKeys)
                allKeys.Add(key);

            // 2.4 原生碎片/苔莓顺序发放键（按获取顺序依次给；地点未知，先预编号）
            //     袋面碎片 heart、丝轴 spool、苔莓 moss、花芯 flower、跳蚤救援 flea
            //     份数统一由 ItemLimitConfig 表驱动（精确份数 = 收集类物理总数，各分支必须齐全）
            for (int i = 1; i <= ItemLimitConfig.LimitHeartPiece; i++) allKeys.Add($"heart:{i:D2}");
            for (int i = 1; i <= ItemLimitConfig.LimitSpoolPart; i++) allKeys.Add($"spool:{i:D2}");
            for (int i = 1; i <= ItemLimitConfig.LimitMoss; i++) allKeys.Add($"moss:{i:D2}");
            for (int i = 1; i <= ItemLimitConfig.LimitFlower; i++) allKeys.Add($"flower:{i:D2}");
            for (int i = 1; i <= FleaSceneMap.Count; i++) allKeys.Add($"flea:{i:D2}");

            // 2.5 剧情演出拦截点键（技能/丝之心/猎人日志/教堂神社；一场景至多一点，HashSet 去重重叠场景）
            foreach (var key in EventKeys())
                allKeys.Add(key);

            // 3. 获取所有有限奖励
            var allLimitedRewards = ItemRandomizer.LimitedRewards;
            if (allLimitedRewards == null || allLimitedRewards.Count == 0)
            {
                // 无奖励，全填充虚拟货币
                foreach (var key in allKeys)
                    dict[key] = "virt:FallbackCoin";
                Plugin.SaveGlobalData();
                Plugin.Log.LogInfo("[PreGeneratedMap] 无有限奖励，全部填充虚拟货币");
                return;
            }

            // 4. 构建奖励池（按可分配次数添加副本）
            //    次数来源统一为 ItemLimitConfig（单一事实来源，与动态模式 IsAtMax 共用同一配置）：
            //      - 珍贵虚拟：GetPreciousLimit（默认 20/18/2/2）
            //      - 能力：GetAbilityLimit（默认 2 = 双倍，"能力双倍"体现在配置里而非硬编码）
            //      - 方向权限：GetDirectionLimit（攻击/回血默认 2，移动方向默认 1）
            //      - 其余（真实物品/地图/车站/Lore/权限物）：恒 1 次（全覆盖模式每物品恰好一次）
            var pool = new List<IRandomReward>();
            foreach (var reward in allLimitedRewards)
            {
                int count = 1; // 默认次数
                string id = reward.Id;

                if (id == "Simple Key")
                {
                    count = ItemLimitConfig.LimitSimpleKey; // 简单钥匙全游戏共 4 把，全部入池（配置化）
                }
                else if (id == "virt:HeartPiece" || id == "virt:SpoolPart" ||
                    id == "virt:UnlockCrestSlot")
                {
                    count = ItemLimitConfig.GetPreciousLimit(id);
                }
                else if (ItemRandomizer.AbilityRewardIds.Contains(id))
                {
                    count = ItemLimitConfig.GetAbilityLimit(id);
                }
                else if (id.StartsWith("perm:", StringComparison.Ordinal))
                {
                    count = ItemLimitConfig.GetDirectionLimit(id);
                }
                else if (id == "flea:Rescue")
                {
                    count = ItemLimitConfig.LimitFleaRescue; // 这个值应为 27
                }
                for (int i = 0; i < count; i++)
                    pool.Add(reward);
            }

            // 5. 打乱池和键列表（使用种子保证一致性）
            var seed = Plugin.RandomSeed.Value ^ 0x7F3E2D1C;
            var rng = new Random(seed);
            var shuffledPool = pool.OrderBy(_ => rng.Next()).ToList();
            var shuffledKeys = allKeys.OrderBy(_ => rng.Next()).ToList();

            // 6. 获取无限奖励并构建加权池
            var unlimitedSource = ItemRandomizer.UnlimitedRewards;
            var weightedUnlimited = new List<IRandomReward>();
            if (unlimitedSource != null && unlimitedSource.Count > 0)
            {
                foreach (var reward in unlimitedSource)
                {
                    // 随机货币（virt:RandomCoin）按表权重重复（默认 6），确保货币占大头
                    int repeat = reward.Id == "virt:RandomCoin" ? ItemLimitConfig.RandomCoinWeight : 1;
                    for (int i = 0; i < repeat; i++)
                        weightedUnlimited.Add(reward);
                }
                // 打乱加权池，避免顺序固定
                var weightedRng = new Random(Plugin.RandomSeed.Value ^ 0x7F3E2D1C);
                weightedUnlimited = weightedUnlimited.OrderBy(_ => weightedRng.Next()).ToList();
            }

            // 7. 分配映射
            int itemIndex = 0;
            int totalPool = shuffledPool.Count;
            int unlimitedIndex = 0;
            int unlimitedCount = weightedUnlimited.Count;

            foreach (var key in shuffledKeys)
            {
                string rewardId;
                if (itemIndex < totalPool)
                {
                    rewardId = "reward:" + shuffledPool[itemIndex++].Id;
                }
                else if (unlimitedCount > 0)
                {
                    var reward = weightedUnlimited[unlimitedIndex % unlimitedCount];
                    rewardId = "reward:" + reward.Id;
                    unlimitedIndex++;
                }
                else
                {
                    // 极少数情况无限池也为空，则使用 fallback（实际上不会发生）
                    rewardId = "virt:FallbackCoin";
                }
                dict[key] = rewardId;
            }

            // 8. 保存并输出日志
            Plugin.SaveGlobalData();
            int fallbackCount = Math.Max(0, shuffledKeys.Count - totalPool);
            int unlimitedUsed = Math.Min(fallbackCount, unlimitedCount > 0 ? shuffledKeys.Count - totalPool : 0);
            Plugin.Log.LogInfo($"[PreGeneratedMap] 全量映射完成: 总点 {shuffledKeys.Count}，有限池容量 {totalPool}，使用无限池填充 {unlimitedUsed}，虚拟货币 {fallbackCount - unlimitedUsed}");
        }

        /// <summary>
        /// 判断物品是否适合放入商店/映射（需要有有效的图标）
        /// </summary>
        private static bool IsSafeForShop(SavedItem item)
        {
            if (item == null) return false;
            var method = typeof(SavedItem).GetMethod("GetPopupIcon", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            var actualMethod = item.GetType().GetMethod("GetPopupIcon", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (actualMethod == null || actualMethod.DeclaringType == typeof(SavedItem))
                return false;
            try { return item.GetPopupIcon() != null; }
            catch { return false; }
        }

        // 已废弃：全量映射在 BuildAllMappings 中一次性完成，不再调用此方法。
        // 保留代码仅供参考。
        /*
        private static int LoadAndGenerateFromEmbedded()
        {
            int added = 0;
            int skipped = 0;
            int errors = 0;

            foreach (var rawLine in EmbeddedPointLines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                var parts = line.Split('|');
                if (parts.Length < 9) { errors++; continue; }

                string type = parts[0].Trim();
                string scene = parts[1].Trim();
                string name = parts[3].Trim();
                string xStr = parts[6].Trim();
                string yStr = parts[7].Trim();
                string generatedKey = null;
                bool skipLore = false;

                switch (type)
                {
                    case "Pickup":
                        if (float.TryParse(xStr, out float px) && float.TryParse(yStr, out float py))
                            generatedKey = $"{scene}_{px:F2}_{py:F2}_0.00";
                        else
                            generatedKey = parts[5].Trim(); // fallback
                        break;

                    case "Lore":
                        generatedKey = $"loretrig:{scene}:{name}";
                        skipLore = true;
                        break;

                    case "Toll":
                    case "BellBench":
                        // 通过场景名查找对应的车站 bool
                        if (MapStationUnlockPatch.TryGetStationBoolForScene(scene, out string boolName))
                            generatedKey = "check:" + boolName;
                        else
                            skipped++;
                        break;

                    default:
                        skipped++;
                        continue;
                }

                if (!string.IsNullOrEmpty(generatedKey))
                {
                    if (EnsureMapped(generatedKey, skipLore)) added++;
                }
            }

            Plugin.Log.LogInfo($"[PreGeneratedMap] 嵌入解析完成: 新增 {added}，跳过 {skipped}，错误 {errors}");
            return added;
        }
        */

        public static void OnSceneLoaded(Scene scene)
        {
            if (!_initialized) return;
            // 全量映射已覆盖所有点，不再补缺
            return;
        }

        public static IRandomReward ResolveReward(string key)
        {
            try
            {
                var dict = Dict;
                if (dict == null || string.IsNullOrEmpty(key)) return null;

                // 1. 精确匹配
                if (dict.TryGetValue(key, out string stored) && !string.IsNullOrEmpty(stored))
                {
                    string id = stored.StartsWith("reward:", StringComparison.Ordinal)
                        ? stored.Substring("reward:".Length)
                        : stored;

                    // 虚拟货币奖励（地图点：生成钱堆）
                    if (id == "virt:FallbackCoin")
                    {
                        return new VirtualReward(
                            "virt:FallbackCoin",
                            Locale.Get("随机货币"),
                            SpriteCache.Find("coinget_01"),
                            () => GeoRockBuilder.SpawnAtHero(),
                            () => false
                        );
                    }
                    // 虚拟跳蚤奖励（地图点：生成跳蚤救援）
                    if (id == "virt:FallbackFlea")
                    {
                        return new VirtualReward(
                            "virt:FallbackFlea",
                            Locale.Get("跳蚤救援"),
                            null,
                            () => FleaRescueBuilder.SpawnAtHero(),
                            () => false
                        );
                    }
                    return ItemRandomizer.FindRewardById(id);
                }

                // 2. 精确失败 → 容差匹配（仅拾取点）
                if (key.StartsWith("lore") || key.StartsWith("check") || key.StartsWith("event:"))
                    return null;

                string scene = ExtractScene(key);
                if (string.IsNullOrEmpty(scene)) return null;

                Vector3 pos = ExtractPosition(key);
                if (pos == Vector3.zero) return null;

                string matchedKey = FindKeyByProximity(scene, pos, CoordinateTolerance);
                if (!string.IsNullOrEmpty(matchedKey) && dict.TryGetValue(matchedKey, out stored))
                {
                    string id = stored.StartsWith("reward:", StringComparison.Ordinal)
                        ? stored.Substring("reward:".Length)
                        : stored;
                    return ItemRandomizer.FindRewardById(id);
                }

                return null;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[PreGeneratedMap] 解析失败 {key}: {ex.Message}");
                return null;
            }
        }

        private static string ExtractScene(string key)
        {
            // key 格式：场景名_x_y_z（如 Tut_01_26.24_76.28_0.00）
            // 从末尾取最后三段坐标（_x _y _z），剩下的就是完整场景名
            int lastUnderscore = key.LastIndexOf('_');
            if (lastUnderscore <= 0) return null;
            int secondLast = key.LastIndexOf('_', lastUnderscore - 1);
            if (secondLast <= 0) return null;
            int thirdLast = key.LastIndexOf('_', secondLast - 1);
            if (thirdLast <= 0) return null;
            return key.Substring(0, thirdLast);
        }

        private static Vector3 ExtractPosition(string key)
        {
            var parts = key.Split('_');
            if (parts.Length >= 4)
            {
                float x, y, z;
                if (float.TryParse(parts[parts.Length - 3], out x) &&
                    float.TryParse(parts[parts.Length - 2], out y) &&
                    float.TryParse(parts[parts.Length - 1], out z))
                    return new Vector3(x, y, z);
            }
            return Vector3.zero;
        }

        private static string FindKeyByProximity(string scene, Vector3 pos, float tolerance)
        {
            EnsureSceneIndex();
            if (!SceneKeysCache.TryGetValue(scene, out var keys)) return null;
            for (int i = 0; i < keys.Count; i++)
            {
                Vector3 other = ExtractPosition(keys[i]);
                if (other == Vector3.zero) continue;
                if (Vector3.Distance(pos, other) <= tolerance)
                    return keys[i];
            }
            return null;
        }

        public static void Reset()
        {
            try
            {
                if (Plugin.SaveData != null && Plugin.SaveData.PreGeneratedMappings != null)
                {
                    Plugin.SaveData.PreGeneratedMappings.Clear();
                    Plugin.SaveData.MappingsConfigStamp = null;
                    Plugin.SaveGlobalData();
                }
                _initialized = false;
                PendingKey = null;
                _sceneIndexDictRef = null;
                SceneKeysCache.Clear();
                ItemLimitConfig.ResetRegenerateState(); // 防抖状态复位，避免残留脏标
                ResetDataCache(); // 允许外部覆盖文件变更后重新加载
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[PreGeneratedMap] 重置失败: {ex.Message}");
            }
        }

        public static string PickupKeyOf(CollectableItemPickup pickup)
        {
            var pos = pickup.transform.position;
            return $"{pickup.gameObject.scene.name}_{pos.x:F2}_{pos.y:F2}_{pos.z:F2}";
        }

        /// <summary>
        /// 按顺序发放的映射奖励：按前缀+序号（如 heart:01）从预生成映射取奖励。
        /// 命中则递增对应序号并返回奖励；未命中（映射未初始化/序号越界）返回 null，由调用方回退动态随机。
        /// </summary>
        public static IRandomReward ResolveSequentialReward(string prefix, int seq)
        {
            try
            {
                if (seq <= 0) return null;
                string key = $"{prefix}:{seq:D2}";
                var dict = Dict;
                if (dict == null || !dict.TryGetValue(key, out string stored) || string.IsNullOrEmpty(stored))
                    return null;
                string id = stored.StartsWith("reward:", StringComparison.Ordinal)
                    ? stored.Substring("reward:".Length)
                    : stored;
                if (id == "virt:FallbackCoin") return null;
                // 跳蚤顺序键：映射为跳蚤奖励时直接给跳蚤（生成救援跳蚤）
                if (prefix == "flea" && (id == "virt:FallbackFlea" || id == "virt:RandomFlea"))
                {
                    return new VirtualReward(
                        id,
                        Locale.Get("跳蚤救援"),
                        null,
                        () => FleaRescueBuilder.SpawnAtHero(),
                        () => false);
                }
                return ItemRandomizer.FindRewardById(id);
            }
            catch { return null; }
        }
    }
}
