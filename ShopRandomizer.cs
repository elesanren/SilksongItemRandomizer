// ShopRandomizer.cs - 修复后的完整版本
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using StartingAbilityPicker;
using UnityEngine;
using Random = System.Random;

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
        private static MethodInfo _getPopupIconMethod;  // 缓存反射结果，IsSafeForShop 热路径用

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
            Plugin.Log.LogInfo("商店随机缓存已重置（下次访问时将重新构建物品池）");
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
                var proxy = new ProxySavedItem();
                proxy.Init(reward);
                _allShopItems.Add(proxy);
            }

            Plugin.Log.LogInfo($"商店物品池构建完成：原版 {originalItems?.Count ?? 0} 个，虚拟 {virtualRewards.Count} 个，总计 {_allShopItems.Count} 个");
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
            list.Add(new LimitedVirtualReward("virt:HeartPiece", Locale.Get("面具碎片"), FindSprite("mask_first"), () =>
            {
                var pd = PlayerData.instance;
                if (pd != null) pd.heartPieces++;
                EventRegister.SendEvent(EventRegisterEvents.EquipsChangedEvent, null);
            }, 2));
            list.Add(new LimitedVirtualReward("virt:SpoolPart", Locale.Get("丝轴碎片"), FindSprite("spool_upgrade_pickup"), () =>
            {
                var pd = PlayerData.instance;
                if (pd != null) pd.silkSpoolParts++;
                GameCameras.instance?.silkSpool?.RefreshSilk();
                EventRegister.SendEvent(EventRegisterEvents.EquipsChangedEvent, null);
            }, 2));
            list.Add(new LimitedVirtualReward("virt:MaxSilkRegenUp", Locale.Get("丝线恢复上限 +1"), FindSprite("prompt_silkheart"),
                () => HeroController.instance?.AddToMaxSilkRegen(1), 2));

            // 纹章槽位解锁器（商店版，最多购买 20 次）
            list.Add(new LimitedVirtualReward(
                "virt:UnlockCrestSlot",
                Locale.Get("纹章槽位解锁器"),
                FindSprite("spool_upgrade_pickup"),
                () => ItemRandomizer.TryUnlockCrestSlot(),
                20
            ));

            return list;
        }

        private static Sprite FindSprite(string name) => SpriteCache.Find(name);

        private static SavedItem GetRandomCoinItem(string permanentId, out int price)
        {
            price = 1;
            int amount = UnityEngine.Random.Range(50, 301);
            string displayName = string.Format(Locale.Get("随机念珠"), amount);
            var reward = new VirtualReward($"virt:RandomCoin_{amount}", displayName, FindSprite("coinget_01"),
                () => HeroController.instance?.AddGeo(amount), () => false);
            var proxy = new ProxySavedItem();
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
            var proxy = new ProxySavedItem();
            proxy.Init(reward);
            return proxy;
        }

        private static SavedItem GetRandomCapUpgradeItem(string permanentId, out int price)
        {
            price = UnityEngine.Random.Range(60, 121);
            var capTypes = new List<(string id, string name, Sprite icon, Action giveAction)>
            {
                ("virt:HeartPiece", Locale.Get("面具碎片"), FindSprite("mask_first"), () => {
                    var pd = PlayerData.instance;
                    if (pd != null) pd.heartPieces++;
                    EventRegister.SendEvent(EventRegisterEvents.EquipsChangedEvent, null);
                }),
                ("virt:SpoolPart", Locale.Get("丝轴碎片"), FindSprite("spool_upgrade_pickup"), () => {
                    var pd = PlayerData.instance;
                    if (pd != null) pd.silkSpoolParts++;
                    GameCameras.instance?.silkSpool?.RefreshSilk();
                    EventRegister.SendEvent(EventRegisterEvents.EquipsChangedEvent, null);
                }),
                ("virt:MaxSilkRegenUp", Locale.Get("丝线恢复上限 +1"), FindSprite("prompt_silkheart"), () => {
                    HeroController.instance?.AddToMaxSilkRegen(1);
                })
            };
            var selected = capTypes[UnityEngine.Random.Range(0, capTypes.Count)];
            var reward = new LimitedVirtualReward(selected.id, selected.name, selected.icon, selected.giveAction, 2);
            var proxy = new ProxySavedItem();
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

            int slotIndex = -1;
            string[] parts = permanentId.Split('_');
            for (int i = parts.Length - 1; i >= 0; i--)
            {
                if (int.TryParse(parts[i], out slotIndex))
                    break;
            }

            // 槽位 0,1,2 为特殊奖励（每个商店固定出现，不参与去重）
            if (slotIndex == 0)
            {
                if (_shopItemCache.TryGetValue(permanentId, out var cachedCoin))
                {
                    price = _shopPriceCache[permanentId];
                    return cachedCoin;
                }
                var coinItem = GetRandomCoinItem(permanentId, out price);
                _shopItemCache[permanentId] = coinItem;
                _shopPriceCache[permanentId] = price;
                return coinItem;
            }
            if (slotIndex == 1)
            {
                if (_shopItemCache.TryGetValue(permanentId, out var cachedBlue))
                {
                    price = _shopPriceCache[permanentId];
                    return cachedBlue;
                }
                var blueItem = GetRandomBlueHealthItem(permanentId, out price);
                _shopItemCache[permanentId] = blueItem;
                _shopPriceCache[permanentId] = price;
                return blueItem;
            }
            if (slotIndex == 2)
            {
                if (_shopItemCache.TryGetValue(permanentId, out var cachedCap))
                {
                    price = _shopPriceCache[permanentId];
                    return cachedCap;
                }
                var capItem = GetRandomCapUpgradeItem(permanentId, out price);
                _shopItemCache[permanentId] = capItem;
                _shopPriceCache[permanentId] = price;
                return capItem;
            }

            // 普通槽位（3-11）
            if (_shopItemCache.TryGetValue(permanentId, out var cachedItem) && !IsItemOwned(cachedItem))
            {
                if (!_shopPriceCache.TryGetValue(permanentId, out price))
                {
                    var rng = new Random(Plugin.RandomSeed.Value ^ permanentId.GetHashCode());
                    price = GenerateRandomPrice(rng);
                    _shopPriceCache[permanentId] = price;
                }
                if (usedInThisShop.Contains(cachedItem.name))
                {
                    return GenerateRandomShopItem(permanentId, out price, usedInThisShop);
                }
                usedInThisShop.Add(cachedItem.name);
                return cachedItem;
            }

            var newItem = GenerateRandomShopItem(permanentId, out price, usedInThisShop);
            if (newItem == null)
            {
                price = 10;
                var fallbackReward = new VirtualReward("virt:FallbackGeo", Locale.Get("保底念珠"), FindSprite("coinget_01"),
                    () => HeroController.instance?.AddGeo(10), () => false);
                var proxy = new ProxySavedItem();
                proxy.Init(fallbackReward);
                newItem = proxy;
            }
            _shopItemCache[permanentId] = newItem;
            _shopPriceCache[permanentId] = price;
            usedInThisShop.Add(newItem.name);
            return newItem;
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
                var proxy = new ProxySavedItem();
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

            // 缓存反射结果，避免每个物品每次调用都 GetMethod
            _getPopupIconMethod ??= typeof(SavedItem).GetMethod("GetPopupIcon", BindingFlags.Instance | BindingFlags.Public);
            var actualMethod = item.GetType().GetMethod("GetPopupIcon", BindingFlags.Instance | BindingFlags.Public);
            if (actualMethod == null || actualMethod.DeclaringType == typeof(SavedItem))
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

        // ========== 内部辅助类（只保留 ProxySavedItem，其他已在 RewardTypes 中定义）==========
        private class ProxySavedItem : SavedItem
        {
            private IRandomReward _reward;
            public void Init(IRandomReward reward) { _reward = reward; name = _reward.Id; }
            public override string GetPopupName() => _reward.DisplayName;
            public override Sprite GetPopupIcon() => _reward.Icon;
            public override void Get(bool showPopup = true) => _reward.Give();
            public override bool CanGetMore() => !_reward.IsAtMax();
            public override int GetSavedAmount() => 0;
            public override bool IsUnique => false;
        }
    }
}