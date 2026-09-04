// GlobalConfig.cs - 三合一统一配置系统（SilksongItemRandomizer / StartingAbilityPicker / HKSilksong_SceneRandomizer）
using BepInEx;
using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using System.IO;

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 统一配置宿主：三个 mod 的全部配置条目由本类一次绑定到 SIR 的 ConfigFile，
    /// 生成的配置文件为 HardItemRandomizer.GlobalConfig.cfg（GUID 由 SIR Plugin 负责）。
    /// SAP / SceneRandomizer 不再各自 Bind，仅消费本类导出的 ConfigEntry。
    /// </summary>
    public static class GlobalConfig
    {
        public static ConfigFile File { get; private set; }

        internal static readonly List<ConfigEntryBase> Entries = new List<ConfigEntryBase>();

        // ========== SIR：General ==========
        public static ConfigEntry<int> RandomSeed;
        public static ConfigEntry<bool> ItemRandomEnabled;
        public static ConfigEntry<bool> CrestRandomEnabled;

        // ========== SIR：Silk Randomizer ==========
        public static ConfigEntry<bool> SilkRandomizerEnabled;
        public static ConfigEntry<int> SilkRandomMin;
        public static ConfigEntry<int> SilkRandomMax;

        // ========== SIR：Traps（统一以 cfg 为准） ==========
        public static ConfigEntry<bool> TrapEnabled;
        public static ConfigEntry<bool> TrapMovementEnabled;
        public static ConfigEntry<int> TrapDifficulty;

        // ========== SAP：General ==========
        public static ConfigEntry<string> ChosenProfiles;
        public static ConfigEntry<int> StartingSkillCount;
        public static ConfigEntry<int> SkillVertical;
        public static ConfigEntry<int> SkillHorizontal;
        public static ConfigEntry<int> SkillSpecial;
        public static ConfigEntry<int> SkillAttack;
        public static ConfigEntry<int> StartingItemCount;
        public static ConfigEntry<bool> CrestCurseEnabled;
        public static ConfigEntry<bool> EnemyRandoAdjustEnabled;
        public static ConfigEntry<bool> EnemyScaleRandom;
        public static ConfigEntry<float> EnemyScaleMin;
        public static ConfigEntry<float> EnemyScaleMax;
        public static ConfigEntry<bool> UseNativeSkillGiving;
        public static ConfigEntry<bool> FullRandomMode;
        public static ConfigEntry<bool> UseTypeMode;
        public static ConfigEntry<bool> ForceCompletionDisplay;
        public static ConfigEntry<bool> ResetSeedWorld;

        // ========== SAP：UI ==========
        public static ConfigEntry<bool> PanelBackgroundEnabled;

        // ========== SAP：Direction ==========
        public static ConfigEntry<bool> DirectionEnabled;
        public static ConfigEntry<bool> DashLeft;
        public static ConfigEntry<bool> DashRight;
        public static ConfigEntry<bool> HarpoonLeft;
        public static ConfigEntry<bool> HarpoonRight;
        public static ConfigEntry<bool> FloatLeft;
        public static ConfigEntry<bool> FloatRight;
        public static ConfigEntry<bool> WallJumpLeft;
        public static ConfigEntry<bool> WallJumpRight;
        public static ConfigEntry<bool> Heal;
        public static ConfigEntry<bool> AttackUp;
        public static ConfigEntry<bool> AttackLeft;
        public static ConfigEntry<bool> AttackRight;

        // ========== SAP：Ability（能力权限，统一以 cfg 为准） ==========
        public static ConfigEntry<bool> Swim;
        public static ConfigEntry<bool> ColdResist;

        // ========== SceneRandomizer ==========
        public static ConfigEntry<bool> EnableRandomization;
        public static ConfigEntry<int> RandomMode;
        public static ConfigEntry<bool> InstantMusicTransition;
        public static ConfigEntry<bool> ShowSceneLabel;
        public static ConfigEntry<string> TeleportScene;
        public static ConfigEntry<bool> TeleportConfirm;
        public static ConfigEntry<bool> ShowSeedOnScreen;
        public static ConfigEntry<string> CurrentSeed;
        public static ConfigEntry<int> NewSeed;
        public static ConfigEntry<bool> RegenerateNow;

        private static readonly string[] LegacyFiles =
        {
            Path.Combine(Paths.ConfigPath, "HardItemRandomizer.SilksongItemRandomizer.cfg"),
            Path.Combine(Paths.ConfigPath, "HardItemRandomizer.StartingAbilityPicker.cfg"),
            Path.Combine(Paths.ConfigPath, "HardItemRandomizer.HKSilksong_SceneRandomizer.cfg"),
        };

        public static void Init(ConfigFile config)
        {
            File = config;

            // ========== SIR ==========
            RandomSeed = Bind(config, "General", "RandomSeed", 0, "随机种子 (0 表示随机)");
            ItemRandomEnabled = Bind(config, "General", "ItemRandomEnabled", true, "Enable/disable item randomization");
            CrestRandomEnabled = Bind(config, "General", "CrestRandomEnabled", false, "启用纹章随机（独立开关，仅在物品随机总开关开启时生效）");
            SilkRandomizerEnabled = Bind(config, "Silk Randomizer", "Enabled", false, "启用灵丝获得/消耗随机化（默认关闭，仅在物品随机总开关开启时生效）");
            SilkRandomMin = Bind(config, "Silk Randomizer", "MinAmount", 1, "随机获得/消耗的最小灵丝数量（1-9）");
            SilkRandomMax = Bind(config, "Silk Randomizer", "MaxAmount", 9, "随机获得/消耗的最大灵丝数量（1-9）");

            // ========== Traps（统一以 cfg 为准，面板/API 改动会反写此处） ==========
            TrapEnabled = Bind(config, "Traps", "Enabled", false, "启用陷阱随机");
            TrapMovementEnabled = Bind(config, "Traps", "MovementEnabled", false, "陷阱随机：生成移动类陷阱");
            TrapDifficulty = Bind(config, "Traps", "Difficulty", 0, "陷阱难度 0=Beginner, 1=Focused, 2=Overflow");

            // ========== SAP ==========
            ChosenProfiles = Bind(config, "General", "ChosenProfiles", "", "已选择过开局选项的存档ID列表");
            StartingSkillCount = Bind(config, "General", "StartingSkillCount", 0, "开局随机技能数量 (0-5)");
            StartingItemCount = Bind(config, "General", "StartingItemCount", 0, "开局随机物品数量 (0-5)");
            CrestCurseEnabled = Bind(config, "General", "CrestCurseEnabled", false, "启用纹章诅咒");
            EnemyRandoAdjustEnabled = Bind(config, "General", "EnemyRandoAdjustEnabled", false, "启用怪物随机调整");
            EnemyScaleRandom = Bind(config, "General", "EnemyScaleRandom", true, "怪物随机：启用怪物缩放随机");
            EnemyScaleMin = Bind(config, "General", "EnemyScaleMin", 1.0f, "怪物随机：缩放最小值");
            EnemyScaleMax = Bind(config, "General", "EnemyScaleMax", 1.0f, "怪物随机：缩放最大值");
            UseNativeSkillGiving = Bind(config, "General", "UseNativeSkillGiving", true, "技能给予使用原生物品获得动画");
            FullRandomMode = Bind(config, "General", "FullRandomMode", false, "全随机模式：开启后只允许下劈，所有其他方向动作全部禁用");
            UseTypeMode = Bind(config, "General", "UseTypeMode", false, "技能分配模式：false=自由分配, true=分类分配");
            SkillVertical = Bind(config, "General", "SkillVertical", 0, "技能垂直分配数量");
            SkillHorizontal = Bind(config, "General", "SkillHorizontal", 0, "技能水平分配数量");
            SkillSpecial = Bind(config, "General", "SkillSpecial", 0, "技能特殊分配数量");
            SkillAttack = Bind(config, "General", "SkillAttack", 0, "技能攻击分配数量");
            ForceCompletionDisplay = Bind(config, "General", "ForceCompletionDisplay", true, "强制显示完成度");
            ResetSeedWorld = Bind(config, "General", "ResetSeedWorld", false, "重置所有物品（每次开局前使用）");
            PanelBackgroundEnabled = Bind(config, "UI", "PanelBackgroundEnabled", true, "显示开局选项面板背景图片");
            DirectionEnabled = Bind(config, "Direction", "Enabled", true, "启用方向分裂系统");
            DashLeft = Bind(config, "Direction", "DashLeft", false, "允许向左冲刺");
            DashRight = Bind(config, "Direction", "DashRight", false, "允许向右冲刺");
            HarpoonLeft = Bind(config, "Direction", "HarpoonLeft", false, "允许向左飞针");
            HarpoonRight = Bind(config, "Direction", "HarpoonRight", false, "允许向右飞针");
            FloatLeft = Bind(config, "Direction", "FloatLeft", false, "允许向左漂浮");
            FloatRight = Bind(config, "Direction", "FloatRight", false, "允许向右漂浮");
            WallJumpLeft = Bind(config, "Direction", "WallJumpLeft", false, "允许向左壁跳");
            WallJumpRight = Bind(config, "Direction", "WallJumpRight", false, "允许向右壁跳");
            Heal = Bind(config, "Direction", "Heal", false, "允许回血（config panel 权限分裂开关的持久化，默认未获得）");
            AttackUp = Bind(config, "Direction", "AttackUp", true, "允许上劈（攻击方向权限，默认开）");
            AttackLeft = Bind(config, "Direction", "AttackLeft", true, "允许左劈（攻击方向权限，默认开）");
            AttackRight = Bind(config, "Direction", "AttackRight", true, "允许右劈（攻击方向权限，默认开）");

            // ========== Ability（能力权限，统一以 cfg 为准） ==========
            Swim = Bind(config, "Ability", "Swim", false, "游泳权限（未持有则碰水受陷阱伤）");
            ColdResist = Bind(config, "Ability", "ColdResist", false, "抗寒权限（雪绫披风分裂功能）");

            // ========== SceneRandomizer ==========
            EnableRandomization = Bind(config, "General", "EnableRandomization", false, "Enable/disable scene transition randomization");
            RandomMode = Bind(config, "General", "RandomMode", 0, "房间随机模式：0=全房间随机, 1=区域随机（只随机区域边界门）");
            InstantMusicTransition = Bind(config, "Audio", "InstantMusicTransition", true, "Instant music transition when changing scenes (disable fade in/out)");
            ShowSceneLabel = Bind(config, "UI", "ShowSceneLabel", true, "Show small scene label in upper-left (toggle)");
            TeleportScene = Bind(config, "Teleport", "TeleportScene", "", "Scene name to teleport to (type exact scene name)");
            TeleportConfirm = Bind(config, "Teleport", "TeleportConfirm", false, "Set to true to teleport to scene in TeleportScene (resets automatically)");
            ShowSeedOnScreen = Bind(config, "Seed Manager", "ShowSeedOnScreen", true, "Show current seed text on screen (toggle to hide)");
            CurrentSeed = Bind(config, "Seed Manager", "CurrentSeed", "0", "Current generation seed (read-only display)");
            NewSeed = Bind(config, "Seed Manager", "NewSeed", 0, "Enter a seed value to regenerate the map");
            RegenerateNow = Bind(config, "Seed Manager", "RegenerateNow", false, "Set to true to regenerate map with NewSeed (resets automatically)");

            MigrateLegacyValues();
            Save();
        }

        private static ConfigEntry<T> Bind<T>(ConfigFile config, string section, string key, T defaultValue, string description)
        {
            var entry = config.Bind(section, key, defaultValue, description);
            Entries.Add(entry);
            return entry;
        }

        public static void Save()
        {
            try { File?.Save(); }
            catch (Exception ex) { Plugin.Log?.LogError($"[GlobalConfig] 保存配置文件失败: {ex}"); }
        }

        /// <summary>
        /// 原子化"设值即落盘"：把写入本地 cfg 与同步内存绑定在一次调用里，避免分离调用产生时序问题。
        /// 局内权限给付统一走这里（等价于 `entry.Value = value` + `Save()`，但保证同一时刻完成）。
        /// </summary>
        public static void SetPersisted<T>(ConfigEntry<T> entry, T value)
        {
            entry.Value = value;
            Save();
        }

        /// <summary>
        /// 旧配置迁移：旧三份 cfg 文件仍存在时，将其已使用的 section/key/value
        /// 解析并覆盖到 GlobalConfig 同名条目，随后删除旧文件。旧文件被删后不再触发。
        /// </summary>
        private static void MigrateLegacyValues()
        {
            if (!System.IO.File.Exists(LegacyFiles[0]) && !System.IO.File.Exists(LegacyFiles[1]) && !System.IO.File.Exists(LegacyFiles[2]))
                return;

            try
            {
                var legacy = new Dictionary<(string, string), string>();
                foreach (string path in LegacyFiles)
                {
                    if (!System.IO.File.Exists(path)) continue;
                    string section = null;
                    foreach (string raw in System.IO.File.ReadAllLines(path))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0 || line.StartsWith("#")) continue;
                        if (line.StartsWith("[") && line.EndsWith("]")) { section = line.Substring(1, line.Length - 2).Trim(); continue; }
                        if (section == null) continue;
                        int eq = line.IndexOf('=');
                        if (eq < 0) continue;
                        string key = line.Substring(0, eq).Trim();
                        string value = line.Substring(eq + 1).Trim();
                        legacy[(section, key)] = value;
                    }
                }

                int applied = 0;
                foreach (var entry in Entries)
                {
                    if (legacy.TryGetValue((entry.Definition.Section, entry.Definition.Key), out string raw))
                    {
                        if (TryApply(entry, raw)) applied++;
                    }
                }
                Plugin.Log?.LogInfo($"[GlobalConfig] 从旧配置文件迁移了 {applied} 个配置值");

                foreach (string path in LegacyFiles)
                {
                    try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); } catch { }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[GlobalConfig] 迁移旧配置失败: {ex}");
            }
        }

        /// <summary>
        /// 导出当前全部配置值（section, key -> 序列化字符串）。ProfileManager 用它写入 profile_N_config.json。
        /// </summary>
        internal static Dictionary<(string, string), string> ExportValues()
        {
            var dict = new Dictionary<(string, string), string>();
            foreach (var entry in Entries)
                dict[(entry.Definition.Section, entry.Definition.Key)] = entry.GetSerializedValue();
            return dict;
        }

        /// <summary>
        /// 用从 profile_N_config.json 读出的值覆盖内存 ConfigEntry。返回成功应用的条数。
        /// </summary>
        internal static int ApplyValues(Dictionary<(string, string), string> values)
        {
            int applied = 0;
            foreach (var kv in values)
            {
                foreach (var entry in Entries)
                {
                    if (entry.Definition.Section == kv.Key.Item1 && entry.Definition.Key == kv.Key.Item2)
                    {
                        if (TryApply(entry, kv.Value)) applied++;
                        break;
                    }
                }
            }
            return applied;
        }

        private static bool TryApply(ConfigEntryBase entry, string raw)
        {
            try
            {
                if (entry is ConfigEntry<int> intEntry && int.TryParse(raw, out int iv)) { intEntry.Value = iv; return true; }
                if (entry is ConfigEntry<bool> boolEntry && bool.TryParse(raw, out bool bv)) { boolEntry.Value = bv; return true; }
                if (entry is ConfigEntry<float> floatEntry && float.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float fv)) { floatEntry.Value = fv; return true; }
                if (entry is ConfigEntry<string> strEntry) { strEntry.Value = raw; return true; }
                return false;
            }
            catch { return false; }
        }
    }
}