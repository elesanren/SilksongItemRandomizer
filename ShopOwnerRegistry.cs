// ShopOwnerRegistry.cs - 店主区分注册表
// 用途：同一场景存在多个商店店主（如 Belltown 有 4 个老式店主）时，
//       通过店主 GameObject 的场景路径区分商店实例，生成带店主标识的永久 ID。
// 原理：预生成映射（PreGeneratedMap）已按本注册表提前生成好所有 key，
//       运行时打开商店只需做一次"店主路径 → 注册表"匹配即可拿到正确 key。
using HarmonyLib;
using System.Collections.Generic;
using System.Text;

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
