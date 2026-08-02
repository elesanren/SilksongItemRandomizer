// PreGeneratedMap.cs - 全量预生成映射，数据硬编码，不依赖外部文件
using System;
using System.Collections.Generic;
using System.Linq;
using StartingAbilityPicker;
using UnityEngine;
using UnityEngine.SceneManagement;
using Random = System.Random;

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

        private static bool _initialized = false;

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

        public static void Initialize()
        {
            if (_initialized) return;
            if (!SilksongItemRandomizerAPI.IsEnabled()) return;
            if (!ItemRandomizer.IsInitialized) return;

            BuildAllMappings();
            _initialized = true;
            Plugin.Log.LogInfo($"[PreGeneratedMap] 全量映射构建完成，共 {Dict?.Count ?? 0} 条映射");
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
                    // ★ 前缀统一为 lore:
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

            // 2.3 商店槽位键（已含 "shop:" 前缀）
            foreach (var key in ShopSlotKeys)
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
            var pool = new List<IRandomReward>();
            foreach (var reward in allLimitedRewards)
            {
                int count = 1; // 默认次数
                string id = reward.Id;

                // 珍贵虚拟奖励：读取配置次数
                if (id == "virt:HeartPiece" || id == "virt:SpoolPart" ||
                    id == "virt:MaxSilkRegenUp" || id == "virt:UnlockCrestSlot")
                {
                    count = ItemLimitConfig.GetPreciousLimit(id);
                }
                // 其他所有奖励（SavedItemReward、能力、方向权限、地图、车站、Lore 等）均为 1 次

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
                    // 随机货币（virt:RandomCoin）添加 6 次，其余各 1 次，确保货币占大头
                    int repeat = reward.Id == "virt:RandomCoin" ? 6 : 1;
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

        // 已废弃：全量映射已覆盖所有点，不再需要运行时补缺。
        // 保留代码仅供参考。
        /*
        public static bool EnsureMapped(string key, bool skipLoreReward = false)
        {
            try
            {
                var dict = Dict;
                if (dict == null || string.IsNullOrEmpty(key)) return false;

                // 1. 精确匹配
                if (dict.ContainsKey(key)) return false;

                // 2. ★★★ 容差匹配：检查是否有坐标接近的已有映射 ★★★
                string scene = ExtractScene(key);
                if (!string.IsNullOrEmpty(scene))
                {
                    Vector3 pos = ExtractPosition(key);
                    if (pos != Vector3.zero)
                    {
                        string existingKey = FindKeyByProximity(scene, pos, CoordinateTolerance);
                        if (!string.IsNullOrEmpty(existingKey))
                        {
                            // 已有接近的映射，直接复用，不生成新的
                            return false;
                        }
                    }
                }

                // 3. 没有接近的映射，生成新的
                IRandomReward reward = null;
                for (int i = 0; i < 10; i++)
                {
                    var candidate = ItemRandomizer.GetRandomReward();
                    if (candidate == null) break;
                    if (skipLoreReward && candidate is LoreReward) continue;
                    reward = candidate;
                    break;
                }
                if (reward == null) return false;

                dict[key] = "reward:" + reward.Id;
                Plugin.SaveGlobalData();
                Plugin.Log.LogInfo($"[PreGeneratedMap] 预生成 {key} -> {reward.DisplayName} ({reward.Id})");
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[PreGeneratedMap] 预生成失败 {key}: {ex.Message}");
                return false;
            }
        }
        */

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

                    // 虚拟货币奖励
                    if (id == "virt:FallbackCoin")
                    {
                        return new VirtualReward(
                            "virt:FallbackCoin",
                            Locale.Get("随机货币"),
                            SpriteCache.Find("coinget_01"),
                            () => HeroController.instance?.AddGeo(UnityEngine.Random.Range(1, 6)),
                            () => false
                        );
                    }
                    return ItemRandomizer.FindRewardById(id);
                }

                // 2. 精确失败 → 容差匹配（仅拾取点）
                if (key.StartsWith("lore") || key.StartsWith("check"))
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
            var dict = Dict;
            if (dict == null) return null;

            foreach (var kv in dict)
            {
                if (!kv.Key.StartsWith(scene + "_")) continue;
                Vector3 other = ExtractPosition(kv.Key);
                if (other == Vector3.zero) continue;
                if (Vector3.Distance(pos, other) <= tolerance)
                    return kv.Key;
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
                    Plugin.SaveGlobalData();
                }
                _initialized = false;
                PendingKey = null;
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
    }
}