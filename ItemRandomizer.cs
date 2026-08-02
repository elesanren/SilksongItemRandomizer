// ItemRandomizer.cs - 完整修复版（消除 Random 歧义，使用 System.Random 明确命名）
using GlobalEnums;
using StartingAbilityPicker;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Random = System.Random;   // 明确使用 System.Random

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 存档数据访问接口（由 Plugin 实现，用于读写持久化数据）
    /// </summary>
    public interface ISaveDataAccessor
    {
        Dictionary<string, int> GetItemGivenCounts();
        Dictionary<string, string> GetTotalMappings();
        void SaveItemGivenCounts(Dictionary<string, int> counts);
        void SaveTotalMappings(Dictionary<string, string> mappings);
    }

    /// <summary>
    /// 物品随机核心逻辑
    /// 改造后：所有配置通过 Initialize 传入，不再依赖 Plugin 静态字段或 ConfigEntry
    /// 保留所有原有功能：物品池、虚拟奖励、纹章槽位解锁、技能给予等
    /// </summary>
    public static class ItemRandomizer
    {
        // ========== 状态（全部由 Initialize 设置） ==========
        private static List<IRandomReward> _unlimitedRewards = new();
        private static List<IRandomReward> _limitedRewards = new();
        private static IRandomReward _cachedCrestUnlocker;
        private static Dictionary<string, ToolCrest> _crestCache;  // 按 name 索引的纹章缓存，避免重复 FindObjectsOfTypeAll

        private static Random _globalRng;
        private static int _seed;

        // 通过接口访问持久化数据
        private static ISaveDataAccessor _saveData;

        // ========== 配置缓存 ==========
        private static float _virtualUnlimitedProbability = 0.1f;
        private static float _crestUnlockerProbability = 0.1f;
        private static float _normalLimitedProbability = 0.8f;
        private static bool _fullRandomModeActive = false;

        /// <summary>
        /// LoreReward 在有限池中的抽取权重：等于可用 lore 条目（地点）数，
        /// 使"随机日志"出现概率按 lore 地点数分配（其余奖励权重恒为 1）。
        /// </summary>
        private static int _loreRewardWeight = 1;

        // ========== 对外只读属性 ==========
        public static Random Rng => _globalRng;
        public static bool IsInitialized { get; private set; } = false;
        public static IReadOnlyList<IRandomReward> LimitedRewards => _limitedRewards;
        public static IReadOnlyList<IRandomReward> UnlimitedRewards => _unlimitedRewards;

        // ========== 初始化 ==========
        public static void Initialize(int seed, ItemRandomizerConfig config, ISaveDataAccessor saveDataAccessor, bool fullRandomMode)
        {
            _seed = seed;
            _globalRng = seed == 0 ? new Random() : new Random(seed);
            _saveData = saveDataAccessor;
            _fullRandomModeActive = fullRandomMode;

            if (config != null)
            {
                _virtualUnlimitedProbability = config.VirtualUnlimitedProbability;
                _crestUnlockerProbability = config.CrestUnlockerProbability;
                _normalLimitedProbability = config.NormalLimitedProbability;
            }

            BuildRewardPools(config, fullRandomMode);
            BuildCrestCache();
            IsInitialized = true;
        }

        public static void RefreshPool()
        {
            if (!IsInitialized) return;
            BuildRewardPools(null, _fullRandomModeActive);
            BuildCrestCache();
        }

        // ========== 奖励池构建 ==========
        private static void BuildRewardPools(ItemRandomizerConfig config, bool fullRandomMode)
        {
            _limitedRewards.Clear();
            _unlimitedRewards.Clear(); 

            var allSaved = Resources.FindObjectsOfTypeAll<SavedItem>();
            var validSaved = new List<SavedItem>();
            foreach (var item in allSaved)
            {
                if (item == null) continue;
                
                Type t = item.GetType();
                if (item is EnemyJournalRecord) continue;
                if (t.Name.StartsWith("QuestTarget")) continue;
                if (t == typeof(Quest) || t == typeof(MainQuest) || t == typeof(SubQuest) || t == typeof(QuestRumour) ||
                    t == typeof(FullQuestBase) || t == typeof(BasicQuestBase) ||
                    t == typeof(QuestGroup) || t == typeof(QuestGroupBase))
                    continue;
                if (!IsValidItem(item)) continue;
                validSaved.Add(item);
            }

            var directionSkillItemNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "Swift Step", "Harpoon Dash", "Drifter's Cloak", "Cling Grip", "EvaHeal"
            };

            foreach (var item in validSaved)
            {
                if (fullRandomMode && directionSkillItemNames.Contains(item.name))
                    continue;
                _limitedRewards.Add(new SavedItemReward(item));
            }

            BuildAbilityVirtualRewards(fullRandomMode);
            if (fullRandomMode) AddDirectionPermissionRewards();
            BuildUnlimitedVirtualRewards();
            BuildPreciousVirtualRewards();
            BuildLoreReward();
            if (ItemLimitConfig.EnableMapRewards)
                _limitedRewards.AddRange(MapStationRewards.BuildMapRewards());
            if (ItemLimitConfig.EnableStationRewards)
                _limitedRewards.AddRange(MapStationRewards.BuildStationRewards());

            _cachedCrestUnlocker = _limitedRewards.FirstOrDefault(r => r.Id == "virt:UnlockCrestSlot");

            Plugin.Log.LogInfo($"随机奖励池构建完成：无限类 {_unlimitedRewards.Count} 个，有限类 {_limitedRewards.Count} 个");
        }

        private static void BuildAbilityVirtualRewards(bool fullRandomMode)
        {
            var allSkillFields = new[]
            {
                "hasNeedleThrow", "hasThreadSphere", "hasSilkCharge", "hasSilkBomb", "hasSilkBossNeedle", "hasParry",
                "hasHarpoonDash", "hasNeedolin", "hasDash", "hasBrolly", "hasDoubleJump", "hasChargeSlash",
                "hasSuperJump", "hasWalljump", "HasSeenEvaHeal", "hasNeedolinMemoryPowerup", "hasFastTravelTeleport"
            };

            foreach (var field in allSkillFields)
            {
                if (fullRandomMode && IsDirectionSkillField(field))
                    continue;

                var (displayName, icon) = GetAbilityInfo(field);
                if (icon == null) icon = GetFallbackIcon();
                if (string.IsNullOrEmpty(displayName)) displayName = field;

                var reward = new VirtualReward(field, displayName, icon,
                    () => GiveSkillWithMenuCheck(field),
                    () => GetGivenCount(field) >= ItemLimitConfig.GetAbilityLimit(field)
                );
                _limitedRewards.Add(reward);
            }
        }

        private static void AddDirectionPermissionRewards()
        {
            _limitedRewards.Add(new DirectionPermissionReward(Locale.Get("冲刺左"), "hasDash", false, true));
            _limitedRewards.Add(new DirectionPermissionReward(Locale.Get("冲刺右"), "hasDash", true, false));
            _limitedRewards.Add(new DirectionPermissionReward(Locale.Get("飞针左"), "hasHarpoonDash", false, true));
            _limitedRewards.Add(new DirectionPermissionReward(Locale.Get("飞针右"), "hasHarpoonDash", true, false));
            _limitedRewards.Add(new DirectionPermissionReward(Locale.Get("漂浮左"), "hasBrolly", false, true));
            _limitedRewards.Add(new DirectionPermissionReward(Locale.Get("漂浮右"), "hasBrolly", true, false));
            _limitedRewards.Add(new DirectionPermissionReward(Locale.Get("壁跳左"), "hasWalljump", false, true));
            _limitedRewards.Add(new DirectionPermissionReward(Locale.Get("壁跳右"), "hasWalljump", true, false));
            _limitedRewards.Add(new DirectionPermissionReward(Locale.Get("上劈权限"), "upward", true, false, true));
            _limitedRewards.Add(new DirectionPermissionReward(Locale.Get("左劈权限"), "left", true, false, true));
            _limitedRewards.Add(new DirectionPermissionReward(Locale.Get("右劈权限"), "right", true, false, true));
            _limitedRewards.Add(new DirectionPermissionReward(Locale.Get("回血权限"), "heal", true, false, false, true));
        }

        private static void BuildUnlimitedVirtualRewards()
        {
            // 1. 灵丝3格（固定）
            if (ItemLimitConfig.EnableInfSilk)
                _unlimitedRewards.Add(new VirtualReward("virt:Silk_3", Locale.Get("灵丝(3)"), null, () =>
                {
                    var hero = HeroController.instance;
                    if (hero != null) for (int i = 0; i < 3; i++) hero.AddSilk(1, false);
                }, () => false));

            // 2. 甲壳300（固定）
            if (ItemLimitConfig.EnableInfShards300)
                _unlimitedRewards.Add(new VirtualReward("virt:Shards_300", Locale.Get("甲壳300"), null, () => HeroController.instance?.AddShards(300), () => false));

            // 3. 随机蓝血（1~6格，逐格给予，使用协程）
            if (ItemLimitConfig.EnableInfBlueHealth)
                _unlimitedRewards.Add(new VirtualReward("virt:BlueHealth_Random", Locale.Get("随机蓝血"), null, () =>
                {
                    int total = UnityEngine.Random.Range(1, 7); // 1~6
                    Plugin.Instance?.StartCoroutine(AddBlueHealthOverTime(total, 0.5f));
                }, () => false));

            // 4. 随机货币（金额50~300，商店风格）
            if (ItemLimitConfig.EnableInfGeo300)  // 复用 EnableInfGeo300 作为总开关，但金额随机
                _unlimitedRewards.Add(new VirtualReward("virt:RandomCoin", Locale.Get("随机念珠"), null, () =>
                {
                    int amount = UnityEngine.Random.Range(50, 301);
                    HeroController.instance?.AddGeo(amount);
                }, () => false));
        }

        private static IEnumerator AddBlueHealthOverTime(int total, float interval)
        {
            for (int i = 0; i < total; i++)
            {
                EventRegister.SendEvent(EventRegisterEvents.AddBlueHealth, null);
                if (i < total - 1)
                    yield return new WaitForSeconds(interval);
            }
        }

        private static void BuildPreciousVirtualRewards()
        {
            _limitedRewards.Add(new VirtualReward("virt:HeartPiece", Locale.Get("面具碎片"), null, () =>
            {
                var pd = PlayerData.instance;
                if (pd != null) pd.heartPieces++;
                EventRegister.SendEvent(EventRegisterEvents.EquipsChangedEvent, null);
            }, () => GetGivenCount("virt:HeartPiece") >= ItemLimitConfig.GetPreciousLimit("virt:HeartPiece")));

            _limitedRewards.Add(new VirtualReward("virt:SpoolPart", Locale.Get("丝轴碎片"), null, () =>
            {
                var pd = PlayerData.instance;
                if (pd != null) pd.silkSpoolParts++;
                GameCameras.instance?.silkSpool?.RefreshSilk();
                EventRegister.SendEvent(EventRegisterEvents.EquipsChangedEvent, null);
            }, () => GetGivenCount("virt:SpoolPart") >= ItemLimitConfig.GetPreciousLimit("virt:SpoolPart")));

            _limitedRewards.Add(new VirtualReward("virt:MaxSilkRegenUp", Locale.Get("丝线恢复上限 +1"), null, () => HeroController.instance?.AddToMaxSilkRegen(1),
                () => GetGivenCount("virt:MaxSilkRegenUp") >= ItemLimitConfig.GetPreciousLimit("virt:MaxSilkRegenUp")));

            _limitedRewards.Add(new VirtualReward(
                "virt:UnlockCrestSlot",
                Locale.Get("纹章槽位解锁器"),
                null,
                () => TryUnlockCrestSlot(),
                () => GetGivenCount("virt:UnlockCrestSlot") >= ItemLimitConfig.GetPreciousLimit("virt:UnlockCrestSlot")
            ));
        }

        private static void BuildLoreReward()
        {
            if (!ItemLimitConfig.EnableLoreReward)
                return;
            // 权重 = 可用 lore 条目（地点）数；取不到时退化为 1（保持原行为）
            int keyCount = LoreRandomizer.GetLoreKeys().Count;
            _loreRewardWeight = keyCount > 0 ? keyCount : 1;
            _limitedRewards.Add(new LoreReward());
            Plugin.Log.LogInfo($"[ItemRandomizer] 已加入日志随机奖励 (LoreReward)，按 lore 地点数加权权重 = {_loreRewardWeight}");
        }

        // ========== 辅助方法 ==========
        private static void BuildCrestCache()
        {
            var allCrests = Resources.FindObjectsOfTypeAll<ToolCrest>();
            _crestCache = new Dictionary<string, ToolCrest>(allCrests.Length, StringComparer.OrdinalIgnoreCase);
            foreach (var c in allCrests)
                if (c != null && !string.IsNullOrEmpty(c.name))
                    _crestCache[c.name] = c;
        }

        private static ToolCrest FindCrestCached(string crestName)
        {
            if (_crestCache == null) BuildCrestCache();
            _crestCache.TryGetValue(crestName, out var crest);
            return crest;
        }

        private static IEnumerator AddBlueHealthCoroutine(int times, float interval)
        {
            for (int i = 0; i < times; i++)
            {
                EventRegister.SendEvent(EventRegisterEvents.AddBlueHealth, null);
                if (i < times - 1)
                    yield return new WaitForSeconds(interval);
            }
        }

        private static bool IsDirectionSkillField(string field)
        {
            return field == "hasDash" || field == "hasHarpoonDash" || field == "hasBrolly" ||
                   field == "hasWalljump" || field == "HasSeenEvaHeal";
        }

        private static bool IsSystemMenuOpen()
        {
            try
            {
                if (UIManager.instance != null && UIManager.instance.uiState == UIState.PAUSED)
                    return true;
                if (GameObject.Find("PauseMenu")?.activeInHierarchy == true)
                    return true;
            }
            catch { }
            return false;
        }

        private static IEnumerator WaitForSystemMenuThenGive(string skillField)
        {
            while (IsSystemMenuOpen())
                yield return null;
            yield return null;
            // 直接调用 StartingAbilityPickerAPI.GiveSkill（已存在）
            StartingAbilityPicker.StartingAbilityPickerAPI.GiveSkill(skillField);
        }

        private static void GiveSkillWithMenuCheck(string skillField)
        {
            EnsureSpellSlotsUnlocked();
            if (IsSystemMenuOpen())
            {
                Plugin.Instance.StartCoroutine(WaitForSystemMenuThenGive(skillField));
            }
            else
            {
                StartingAbilityPicker.StartingAbilityPickerAPI.GiveSkill(skillField);
            }
        }

        private static void EnsureSpellSlotsUnlocked()
        {
            try
            {
                PlayerData pd = PlayerData.instance;
                if (pd == null) return;
                string crestId = pd.CurrentCrestID;
                if (string.IsNullOrEmpty(crestId)) crestId = "Hunter";
                ToolCrest crest = FindCrestCached(crestId);
                if (crest == null) return;

                var data = crest.SaveData;
                if (data.Slots == null || data.Slots.Count == 0)
                {
                    data.Slots = new List<ToolCrestsData.SlotData>();
                    for (int i = 0; i < crest.Slots.Length; i++)
                        data.Slots.Add(new ToolCrestsData.SlotData { IsUnlocked = !crest.Slots[i].IsLocked });
                    crest.SaveData = data;
                }

                bool changed = false;
                for (int i = 0; i < crest.Slots.Length && i < data.Slots.Count; i++)
                {
                    if (crest.Slots[i].Type.ToString().Contains("Spell") && !data.Slots[i].IsUnlocked)
                    {
                        var slotData = data.Slots[i];
                        slotData.IsUnlocked = true;
                        data.Slots[i] = slotData;
                        changed = true;
                    }
                }
                if (changed)
                {
                    crest.SaveData = data;
                    Plugin.Log.LogInfo($"[ItemRandomizer] 已为纹章 {crestId} 解锁法术槽");
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[ItemRandomizer] 解锁法术槽失败: {ex.Message}"); }
        }

        private static (string displayName, Sprite icon) GetAbilityInfo(string abilityField)
        {
            // 通过 StartingAbilityPickerAPI 获取（已添加 GetDisplayName/GetIcon 方法）
            try
            {
                string name = StartingAbilityPicker.StartingAbilityPickerAPI.GetDisplayName(abilityField);
                Sprite icon = StartingAbilityPicker.StartingAbilityPickerAPI.GetIcon(abilityField);
                if (!string.IsNullOrEmpty(name) && icon != null)
                    return (name, icon);
            }
            catch { }

            string fallbackName = abilityField switch
            {
                "hasDash" => Locale.Get("疾风步"),
                "hasBrolly" => Locale.Get("流浪者披风"),
                "hasDoubleJump" => Locale.Get("雪绒披风"),
                "hasSuperJump" => Locale.Get("灵丝升腾"),
                "hasWalljump" => Locale.Get("蛛攀术"),
                "HasSeenEvaHeal" => Locale.Get("风铃摇"),
                _ => abilityField
            };
            Sprite fallbackIcon = GetFallbackIcon();
            return (fallbackName, fallbackIcon);
        }

        private static Sprite GetFallbackIcon()
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.zero);
        }

        // ========== 持久化数据访问 ==========
        private static Dictionary<string, int> GetGivenCountsDict() => _saveData?.GetItemGivenCounts() ?? new Dictionary<string, int>();
        private static Dictionary<string, string> GetTotalMappingsDict() => _saveData?.GetTotalMappings() ?? new Dictionary<string, string>();
        private static void SaveGivenCounts() => _saveData?.SaveItemGivenCounts(GetGivenCountsDict());
        private static void SaveTotalMappings() => _saveData?.SaveTotalMappings(GetTotalMappingsDict());

        public static int GetGivenCount(string id)
        {
            var dict = GetGivenCountsDict();
            return dict.TryGetValue(id, out int count) ? count : 0;
        }

        public static void AddGivenCount(string id)
        {
            var dict = GetGivenCountsDict();
            if (dict.ContainsKey(id)) dict[id]++;
            else dict[id] = 1;
            SaveGivenCounts();
        }

        public static void RecordMapping(string sourceKey, string targetKey)
        {
            var dict = GetTotalMappingsDict();
            if (dict.TryGetValue(sourceKey, out var existing) && existing == targetKey) return;
            dict[sourceKey] = targetKey;
            SaveTotalMappings();
        }

        public static string GetMapping(string sourceKey)
        {
            var dict = GetTotalMappingsDict();
            dict.TryGetValue(sourceKey, out var target);
            return target;
        }

        // ========== 公共接口 ==========
        public static IRandomReward GetRandomReward()
        {
            double rand = _globalRng.NextDouble();

            if (rand < _virtualUnlimitedProbability)
            {
                if (_unlimitedRewards.Count > 0)
                    return _unlimitedRewards[_globalRng.Next(_unlimitedRewards.Count)];
                else
                    rand = _virtualUnlimitedProbability + 0.01;
            }

            if (rand < _virtualUnlimitedProbability + _crestUnlockerProbability)
            {
                if (_cachedCrestUnlocker != null && !_cachedCrestUnlocker.IsAtMax())
                    return _cachedCrestUnlocker;
                else
                    rand = _virtualUnlimitedProbability + _crestUnlockerProbability + 0.01;
            }

            // 有限池按权重抽取：LoreReward 权重 = lore 地点数，其余奖励权重恒为 1。
            // 这样"随机日志"的出现概率随 lore 地点数线性放大，而不是只占 1 个名额。
            int totalWeight = 0;
            foreach (var r in _limitedRewards)
                if (r != _cachedCrestUnlocker && !r.IsAtMax())
                    totalWeight += (r is LoreReward ? _loreRewardWeight : 1);

            if (totalWeight > 0)
            {
                int target = _globalRng.Next(totalWeight);
                foreach (var r in _limitedRewards)
                {
                    if (r != _cachedCrestUnlocker && !r.IsAtMax())
                    {
                        int w = (r is LoreReward ? _loreRewardWeight : 1);
                        if (target < w)
                            return r;
                        target -= w;
                    }
                }
            }

            if (_unlimitedRewards.Count > 0)
                return _unlimitedRewards[_globalRng.Next(_unlimitedRewards.Count)];

            return new VirtualReward("virt:FallbackGeo", Locale.Get("保底念珠"), null, () => HeroController.instance?.AddGeo(10), () => false);
        }

        public static SavedItem GetRandomItem()
        {
            // 仅统计真实物品奖励（SavedItemReward）；跳过 LoreReward 等纯效果奖励，
            // 避免包成 ProxySavedItem 返回破损物品
            int availableCount = 0;
            foreach (var r in _limitedRewards)
                if (r is SavedItemReward && !r.IsAtMax())
                    availableCount++;

            if (availableCount == 0) return null;
            int targetIndex = _globalRng.Next(availableCount);
            foreach (var r in _limitedRewards)
            {
                if (r is SavedItemReward saved && !saved.IsAtMax())
                {
                    if (targetIndex == 0)
                        return saved.Item;
                    targetIndex--;
                }
            }
            return null;
        }

        public static SavedItem PeekRandomItem(Random externalRng)
        {
            var candidates = _limitedRewards.OfType<SavedItemReward>().ToList();
            if (candidates.Count == 0) return null;
            return candidates[externalRng.Next(candidates.Count)].Item;
        }

        public static List<SavedItem> GetAllItems() => _limitedRewards.OfType<SavedItemReward>().Select(r => r.Item).Where(i => i != null).ToList();

        public static bool GiveRandomReward()
        {
            var reward = GetRandomReward();
            if (reward == null) return false;
            reward.Give();
            AddGivenCount(reward.Id);
            return true;
        }

        public static void ResetAllData()
        {
            GetGivenCountsDict().Clear();
            GetTotalMappingsDict().Clear();
            SaveGivenCounts();
            SaveTotalMappings();
            PreGeneratedMap.Reset();
            Plugin.Log.LogInfo("所有随机奖励次数和映射表已重置");
        }

        /// <summary>
        /// 按奖励 Id 反查奖励实例（预生成映射表使用）。
        /// Id 体系：SavedItemReward=物品名，VirtualReward/LoreReward="virt:xxx"，DirectionPermissionReward="perm:xxx"。
        /// </summary>
        public static IRandomReward FindRewardById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var r in _limitedRewards)
                if (r != null && r.Id == id) return r;
            foreach (var r in _unlimitedRewards)
                if (r != null && r.Id == id) return r;
            return null;
        }

        // ========== 纹章槽位解锁器辅助 ==========
        public static ToolItemType? LastUnlockedSlotType { get; private set; }

        public static string GetSlotTypeName(ToolItemType type)
        {
            if (type.ToString().Contains("Attack")) return Locale.Get("攻击槽");
            if (type.ToString().Contains("Tool") || type.ToString().Contains("Item")) return Locale.Get("工具槽");
            if (type.ToString().Contains("Spell")) return Locale.Get("法术槽");
            return type.ToString();
        }

        public static void TryUnlockCrestSlot()
        {
            LastUnlockedSlotType = null;
            var pd = PlayerData.instance;
            if (pd == null) return;

            string crestId = pd.CurrentCrestID;
            if (string.IsNullOrEmpty(crestId))
            {
                var unlockedCrests = new List<string>();
                foreach (var kv in pd.ToolEquips.Enumerate())
                    if (kv.Value.IsUnlocked) unlockedCrests.Add(kv.Key);
                if (unlockedCrests.Count == 0) return;
                crestId = unlockedCrests[UnityEngine.Random.Range(0, unlockedCrests.Count)];
            }

            var crest = FindCrestCached(crestId);
            if (crest == null) return;

            var data = crest.SaveData;
            if (data.Slots == null || data.Slots.Count == 0)
            {
                data.Slots = new List<ToolCrestsData.SlotData>();
                for (int i = 0; i < crest.Slots.Length; i++)
                    data.Slots.Add(new ToolCrestsData.SlotData { IsUnlocked = !crest.Slots[i].IsLocked });
                crest.SaveData = data;
            }

            int targetIndex = -1;
            ToolItemType? slotType = null;
            for (int i = 0; i < data.Slots.Count && i < crest.Slots.Length; i++)
            {
                if (!data.Slots[i].IsUnlocked)
                {
                    string typeStr = crest.Slots[i].Type.ToString();
                    if (!typeStr.Contains("Spell"))
                    {
                        targetIndex = i;
                        slotType = crest.Slots[i].Type;
                        break;
                    }
                }
            }

            if (targetIndex == -1)
            {
                HeroController.instance?.AddGeo(100);
                Plugin.ShowNotification(Locale.Get("纹章槽位已满，获得 100 念珠"), 2f);
                return;
            }

            var slotData = data.Slots[targetIndex];
            slotData.IsUnlocked = true;
            data.Slots[targetIndex] = slotData;
            crest.SaveData = data;

            LastUnlockedSlotType = slotType;
            EventRegister.SendEvent(EventRegisterEvents.EquipsChangedEvent, null);
            string typeName = slotType.HasValue ? GetSlotTypeName(slotType.Value) : Locale.Get("未知");
            Plugin.ShowNotification(string.Format(Locale.Get("纹章槽位 {0} ({1}) 已解锁！"), targetIndex + 1, typeName), 3f);
        }

        public static bool IsAllCrestSlotsUnlocked()
        {
            var pd = PlayerData.instance;
            if (pd == null) return true;
            string crestId = pd.CurrentCrestID;
            if (string.IsNullOrEmpty(crestId)) return false;
            var crest = FindCrestCached(crestId);
            if (crest == null) return false;
            var data = crest.SaveData;
            if (data.Slots == null || data.Slots.Count == 0) return false;
            for (int i = 0; i < data.Slots.Count && i < crest.Slots.Length; i++)
            {
                if (!crest.Slots[i].Type.ToString().Contains("Spell") && !data.Slots[i].IsUnlocked)
                    return false;
            }
            return true;
        }

        private static bool IsValidItem(SavedItem item) => !(item == null || string.IsNullOrEmpty(item.name) || ExcludedNames.Contains(item.name));

        public static readonly HashSet<string> ExcludedNames = new()
        {
            "Steel Spines", "Common Spine", "Plasmium", "Sliver Bell",
            "Seared Organ", "Shredded Organ", "Skewered Organ", "Ragpelt",
            "Invalid Item Template",
        };
    }
}