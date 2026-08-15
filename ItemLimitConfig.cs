// ItemLimitConfig.cs - 修改为可写属性，保持原有功能
using BepInEx.Configuration;
using System;

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 物品重复获得次数限制配置
    /// 属性改为可读写，以便外部（如 MenuChanger）实时修改
    /// </summary>
    public static class ItemLimitConfig
    {
        private static BepInEx.Configuration.ConfigFile _configFile;

        // ========== 普通物品 ==========
        public static int LimitSkillItem { get; set; } = 2;
        public static int LimitRelic { get; set; } = 2;
        public static int LimitOtherItem { get; set; } = 2;

        // ========== 方向权限 ==========
        public static int LimitUpSlash { get; set; } = 1;
        public static int LimitLeftSlash { get; set; } = 1;
        public static int LimitRightSlash { get; set; } = 1;
        public static int LimitDashLeft { get; set; } = 1;
        public static int LimitDashRight { get; set; } = 1;
        public static int LimitHarpoonLeft { get; set; } = 1;
        public static int LimitHarpoonRight { get; set; } = 1;
        public static int LimitFloatLeft { get; set; } = 1;
        public static int LimitFloatRight { get; set; } = 1;
        public static int LimitWallJumpLeft { get; set; } = 1;
        public static int LimitWallJumpRight { get; set; } = 1;
        public static int LimitHeal { get; set; } = 1;

        // ========== 能力虚拟奖励 ==========
        public static int LimitNeedleThrow { get; set; } = 1;
        public static int LimitThreadSphere { get; set; } = 1;
        public static int LimitHarpoonDash { get; set; } = 1;
        public static int LimitSilkCharge { get; set; } = 1;
        public static int LimitSilkBomb { get; set; } = 1;
        public static int LimitSilkBossNeedle { get; set; } = 1;
        public static int LimitNeedolin { get; set; } = 1;
        public static int LimitParry { get; set; } = 1;
        public static int LimitNeedolinMemory { get; set; } = 1;
        public static int LimitFastTravel { get; set; } = 1;
        public static int LimitEvaHeal { get; set; } = 1;
        public static int LimitDash { get; set; } = 1;
        public static int LimitBrolly { get; set; } = 1;
        public static int LimitDoubleJump { get; set; } = 1;
        public static int LimitSuperJump { get; set; } = 1;
        public static int LimitWallJump { get; set; } = 1;
        public static int LimitChargeSlash { get; set; } = 1;

        // ========== 珍贵虚拟奖励 ==========
        public static int LimitHeartPiece { get; set; } = 20;
        public static int LimitSpoolPart { get; set; } = 18;
        public static int LimitMaxSilkRegenUp { get; set; } = 2;
        public static int LimitUnlockCrestSlot { get; set; } = 2;

        // ========== 无限池奖励开关 ==========
        public static bool EnableInfSilk { get; private set; } = true;
        public static bool EnableInfBlueHealth { get; private set; } = true;
        public static bool EnableInfGeo300 { get; private set; } = true;
        public static bool EnableInfShards300 { get; private set; } = true;

        // ========== 日志随机奖励开关 ==========
        public static bool EnableLoreReward { get; private set; } = true;

        // ========== 世界石碑阅读触发随机奖励开关 ==========
        public static bool EnableLoreTrigger { get; private set; } = true;

        // ========== 权限类 inspect 地点权限物开关（进随机池） ==========
        public static bool EnableInspectPermit { get; private set; } = true;

        // ========== 地图/车站随机开关 ==========
        /// <summary>28 张地图加入随机奖励池</summary>
        public static bool EnableMapRewards { get; set; } = true;
        /// <summary>10 个车站 + 铃兽总开关加入随机奖励池</summary>
        public static bool EnableStationRewards { get; set; } = true;
        /// <summary>拦截原版地图购买（沙克拉商店）转为随机奖励</summary>
        public static bool EnableMapCheckIntercept { get; set; } = true;
        /// <summary>拦截原版车站开通（收费机）转为随机奖励</summary>
        public static bool EnableStationCheckIntercept { get; set; } = true;

        // ========== 代码默认值（单一事实来源） ==========
        // 升级改动这些数字会触发「代码更新标记」，首次同步后即被标记，不再重复覆盖用户实际设置。
        private static readonly (string Section, string Key, int Default)[] CodeDefaultInts =
        {
            ("Limits", "SkillItem", 2), ("Limits", "Relic", 2), ("Limits", "OtherItem", 2),
            ("Limits", "UpSlash", 1), ("Limits", "LeftSlash", 1), ("Limits", "RightSlash", 1),
            ("Limits", "DashLeft", 1), ("Limits", "DashRight", 1),
            ("Limits", "HarpoonLeft", 1), ("Limits", "HarpoonRight", 1),
            ("Limits", "FloatLeft", 1), ("Limits", "FloatRight", 1),
            ("Limits", "WallJumpLeft", 1), ("Limits", "WallJumpRight", 1), ("Limits", "Heal", 1),
            ("Limits", "NeedleThrow", 1), ("Limits", "ThreadSphere", 1), ("Limits", "HarpoonDash", 1),
            ("Limits", "SilkCharge", 1), ("Limits", "SilkBomb", 1), ("Limits", "SilkBossNeedle", 1),
            ("Limits", "Needolin", 1), ("Limits", "Parry", 1), ("Limits", "NeedolinMemory", 1),
            ("Limits", "FastTravel", 1), ("Limits", "EvaHeal", 1), ("Limits", "Dash", 1),
            ("Limits", "Brolly", 1), ("Limits", "DoubleJump", 1), ("Limits", "SuperJump", 1),
            ("Limits", "WallJump", 1), ("Limits", "ChargeSlash", 1),
            ("Limits", "HeartPiece", 20), ("Limits", "SpoolPart", 18),
            ("Limits", "MaxSilkRegenUp", 2), ("Limits", "UnlockCrestSlot", 2),
        };

        private static readonly (string Section, string Key, bool Default)[] CodeDefaultBools =
        {
            ("Limits", "EnableInfSilk", true), ("Limits", "EnableInfBlueHealth", true),
            ("Limits", "EnableInfGeo300", true), ("Limits", "EnableInfShards300", true),
            ("Limits", "EnableLoreReward", true), ("Limits", "EnableLoreTrigger", true),
            ("Limits", "EnableInspectPermit", true),
            ("MapStation", "EnableMapRewards", true), ("MapStation", "EnableStationRewards", true),
            ("MapStation", "EnableMapCheckIntercept", true), ("MapStation", "EnableStationCheckIntercept", true),
        };

        private static string _codeDefaultsStampCache;

        private static string BuildCodeDefaultsStamp()
        {
            return _codeDefaultsStampCache ??= BuildCodeDefaultsStampInner();
        }

        private static string BuildCodeDefaultsStampInner()
        {
            var sb = new System.Text.StringBuilder("v1:");
            foreach (var d in CodeDefaultInts)
            {
                sb.Append(d.Key).Append('=').Append(d.Default).Append(',');
            }
            foreach (var d in CodeDefaultBools)
            {
                sb.Append(d.Key).Append('=').Append(d.Default ? 1 : 0).Append(',');
            }
            return sb.ToString();
        }

        /// <summary>
        /// 代码更新标记：检测到代码默认值与上次保存的不一致（即 mod 更新过）时，
        /// 把代码默认值同步进 cfg 文件一次，然后立即把标记保存为当前值。
        /// 之后启动时标记一致就不再调用代码默认去覆盖用户的本地设置。
        /// </summary>
        private static void SyncCodeDefaultsToConfig(ConfigFile config)
        {
            var stampEntry = config.Bind<string>("Limits", "CodeDefaultsStamp", "");
            string codeStamp = BuildCodeDefaultsStamp();

            if (stampEntry.Value == codeStamp)
                return; // 已经同步过本次代码的默认值，绝不覆盖用户已改的本地值

            Plugin.Log?.LogInfo($"[ItemLimitConfig] 检测到代码默认值更新，同步新默认值到配置（旧标记: {stampEntry.Value ?? "(空)"}）");
            foreach (var d in CodeDefaultInts)
                config.Bind<int>(d.Section, d.Key, d.Default).Value = d.Default;
            foreach (var d in CodeDefaultBools)
                config.Bind<bool>(d.Section, d.Key, d.Default).Value = d.Default;

            stampEntry.Value = codeStamp;
            config.Save();
            Plugin.Log?.LogInfo("[ItemLimitConfig] 代码默认值已同步，更新标记已固化");
        }

        /// <summary>
        /// 从 BepInEx 配置文件初始化（仅在游戏启动时调用一次）
        /// </summary>
        public static void Init(ConfigFile config)
        {
            _configFile = config;

            // 代码更新标记：仅在检测到代码默认值变化时同步一次并固化标记，之后不再覆盖本地设置
            SyncCodeDefaultsToConfig(config);

            // 由代码默认值数组驱动绑定与读取，单一事实来源（CodeDefaultInts/Bools），避免与上文重复维护
            foreach (var d in CodeDefaultInts)
            {
                var entry = config.Bind<int>(d.Section, d.Key, d.Default);
                switch (d.Key)
                {
                    case "SkillItem": LimitSkillItem = entry.Value; break;
                    case "Relic": LimitRelic = entry.Value; break;
                    case "OtherItem": LimitOtherItem = entry.Value; break;
                    case "UpSlash": LimitUpSlash = entry.Value; break;
                    case "LeftSlash": LimitLeftSlash = entry.Value; break;
                    case "RightSlash": LimitRightSlash = entry.Value; break;
                    case "DashLeft": LimitDashLeft = entry.Value; break;
                    case "DashRight": LimitDashRight = entry.Value; break;
                    case "HarpoonLeft": LimitHarpoonLeft = entry.Value; break;
                    case "HarpoonRight": LimitHarpoonRight = entry.Value; break;
                    case "FloatLeft": LimitFloatLeft = entry.Value; break;
                    case "FloatRight": LimitFloatRight = entry.Value; break;
                    case "WallJumpLeft": LimitWallJumpLeft = entry.Value; break;
                    case "WallJumpRight": LimitWallJumpRight = entry.Value; break;
                    case "Heal": LimitHeal = entry.Value; break;
                    case "NeedleThrow": LimitNeedleThrow = entry.Value; break;
                    case "ThreadSphere": LimitThreadSphere = entry.Value; break;
                    case "HarpoonDash": LimitHarpoonDash = entry.Value; break;
                    case "SilkCharge": LimitSilkCharge = entry.Value; break;
                    case "SilkBomb": LimitSilkBomb = entry.Value; break;
                    case "SilkBossNeedle": LimitSilkBossNeedle = entry.Value; break;
                    case "Needolin": LimitNeedolin = entry.Value; break;
                    case "Parry": LimitParry = entry.Value; break;
                    case "NeedolinMemory": LimitNeedolinMemory = entry.Value; break;
                    case "FastTravel": LimitFastTravel = entry.Value; break;
                    case "EvaHeal": LimitEvaHeal = entry.Value; break;
                    case "Dash": LimitDash = entry.Value; break;
                    case "Brolly": LimitBrolly = entry.Value; break;
                    case "DoubleJump": LimitDoubleJump = entry.Value; break;
                    case "SuperJump": LimitSuperJump = entry.Value; break;
                    case "WallJump": LimitWallJump = entry.Value; break;
                    case "ChargeSlash": LimitChargeSlash = entry.Value; break;
                    case "HeartPiece": LimitHeartPiece = entry.Value; break;
                    case "SpoolPart": LimitSpoolPart = entry.Value; break;
                    case "MaxSilkRegenUp": LimitMaxSilkRegenUp = entry.Value; break;
                    case "UnlockCrestSlot": LimitUnlockCrestSlot = entry.Value; break;
                }
            }

            foreach (var d in CodeDefaultBools)
            {
                var entry = config.Bind<bool>(d.Section, d.Key, d.Default);
                switch (d.Key)
                {
                    case "EnableInfSilk": EnableInfSilk = entry.Value; break;
                    case "EnableInfBlueHealth": EnableInfBlueHealth = entry.Value; break;
                    case "EnableInfGeo300": EnableInfGeo300 = entry.Value; break;
                    case "EnableInfShards300": EnableInfShards300 = entry.Value; break;
                    case "EnableLoreReward": EnableLoreReward = entry.Value; break;
                    case "EnableLoreTrigger": EnableLoreTrigger = entry.Value; break;
                    case "EnableInspectPermit": EnableInspectPermit = entry.Value; break;
                    case "EnableMapRewards": EnableMapRewards = entry.Value; break;
                    case "EnableStationRewards": EnableStationRewards = entry.Value; break;
                    case "EnableMapCheckIntercept": EnableMapCheckIntercept = entry.Value; break;
                    case "EnableStationCheckIntercept": EnableStationCheckIntercept = entry.Value; break;
                }
            }
        }

        /// <summary>
        /// 批量应用新的限制值（由 API 调用，覆盖所有可配置项）
        /// </summary>
        public static void Apply(ItemLimitSettings settings)
        {
            if (settings == null) return;

            LimitSkillItem = settings.SkillItem;
            LimitRelic = settings.Relic;
            LimitOtherItem = settings.OtherItem;

            LimitUpSlash = settings.UpSlash;
            LimitLeftSlash = settings.LeftSlash;
            LimitRightSlash = settings.RightSlash;
            LimitDashLeft = settings.DashLeft;
            LimitDashRight = settings.DashRight;
            LimitHarpoonLeft = settings.HarpoonLeft;
            LimitHarpoonRight = settings.HarpoonRight;
            LimitFloatLeft = settings.FloatLeft;
            LimitFloatRight = settings.FloatRight;
            LimitWallJumpLeft = settings.WallJumpLeft;
            LimitWallJumpRight = settings.WallJumpRight;
            LimitHeal = settings.Heal;

            LimitNeedleThrow = settings.NeedleThrow;
            LimitThreadSphere = settings.ThreadSphere;
            LimitHarpoonDash = settings.HarpoonDash;
            LimitSilkCharge = settings.SilkCharge;
            LimitSilkBomb = settings.SilkBomb;
            LimitSilkBossNeedle = settings.SilkBossNeedle;
            LimitNeedolin = settings.Needolin;
            LimitParry = settings.Parry;
            LimitNeedolinMemory = settings.NeedolinMemory;
            LimitFastTravel = settings.FastTravel;
            LimitEvaHeal = settings.EvaHeal;
            LimitDash = settings.Dash;
            LimitBrolly = settings.Brolly;
            LimitDoubleJump = settings.DoubleJump;
            LimitSuperJump = settings.SuperJump;
            LimitWallJump = settings.WallJump;
            LimitChargeSlash = settings.ChargeSlash;

            LimitHeartPiece = settings.HeartPiece;
            LimitSpoolPart = settings.SpoolPart;
            LimitMaxSilkRegenUp = settings.MaxSilkRegenUp;
            LimitUnlockCrestSlot = settings.UnlockCrestSlot;
        }

        /// <summary>
        /// 单独应用无限池开关设置
        /// </summary>
        public static void ApplyInfinitePoolSettings(InfinitePoolSettings settings)
        {
            if (settings == null) return;

            EnableInfSilk = settings.Silk;
            EnableInfBlueHealth = settings.BlueHealth;
            EnableInfGeo300 = settings.Geo300;
            EnableInfShards300 = settings.Shards300;
        }

        /// <summary>
        /// 把当前内存中所有 limit 值写回 BepInEx 配置文件并保存（面板/外部修改后持久化）
        /// </summary>
        public static void SaveToConfigFile()
        {
            if (_configFile == null) return;

            void SetInt(string key, int value) => _configFile.Bind<int>("Limits", key, value).Value = value;
            void SetBool(string key, bool value) => _configFile.Bind<bool>("Limits", key, value).Value = value;

            // 普通物品
            SetInt("SkillItem", LimitSkillItem);
            SetInt("Relic", LimitRelic);
            SetInt("OtherItem", LimitOtherItem);

            // 方向权限
            SetInt("UpSlash", LimitUpSlash);
            SetInt("LeftSlash", LimitLeftSlash);
            SetInt("RightSlash", LimitRightSlash);
            SetInt("DashLeft", LimitDashLeft);
            SetInt("DashRight", LimitDashRight);
            SetInt("HarpoonLeft", LimitHarpoonLeft);
            SetInt("HarpoonRight", LimitHarpoonRight);
            SetInt("FloatLeft", LimitFloatLeft);
            SetInt("FloatRight", LimitFloatRight);
            SetInt("WallJumpLeft", LimitWallJumpLeft);
            SetInt("WallJumpRight", LimitWallJumpRight);
            SetInt("Heal", LimitHeal);

            // 能力虚拟奖励
            SetInt("NeedleThrow", LimitNeedleThrow);
            SetInt("ThreadSphere", LimitThreadSphere);
            SetInt("HarpoonDash", LimitHarpoonDash);
            SetInt("SilkCharge", LimitSilkCharge);
            SetInt("SilkBomb", LimitSilkBomb);
            SetInt("SilkBossNeedle", LimitSilkBossNeedle);
            SetInt("Needolin", LimitNeedolin);
            SetInt("Parry", LimitParry);
            SetInt("NeedolinMemory", LimitNeedolinMemory);
            SetInt("FastTravel", LimitFastTravel);
            SetInt("EvaHeal", LimitEvaHeal);
            SetInt("Dash", LimitDash);
            SetInt("Brolly", LimitBrolly);
            SetInt("DoubleJump", LimitDoubleJump);
            SetInt("SuperJump", LimitSuperJump);
            SetInt("WallJump", LimitWallJump);
            SetInt("ChargeSlash", LimitChargeSlash);

            // 珍贵虚拟奖励
            SetInt("HeartPiece", LimitHeartPiece);
            SetInt("SpoolPart", LimitSpoolPart);
            SetInt("MaxSilkRegenUp", LimitMaxSilkRegenUp);
            SetInt("UnlockCrestSlot", LimitUnlockCrestSlot);

            // 无限池 / 开关（只读入口，面板不直接改，但一并持久化）：
            SetBool("EnableInfSilk", EnableInfSilk);
            SetBool("EnableInfBlueHealth", EnableInfBlueHealth);
            SetBool("EnableInfGeo300", EnableInfGeo300);
            SetBool("EnableInfShards300", EnableInfShards300);
            SetBool("EnableLoreReward", EnableLoreReward);
            SetBool("EnableLoreTrigger", EnableLoreTrigger);
            SetBool("EnableInspectPermit", EnableInspectPermit);

            _configFile.Save();
            Plugin.Log?.LogInfo("[ItemLimitConfig] limit 配置已写回配置文件");
        }

        /// <summary>
        /// 生成全部 limit 值的紧凑指纹（含面板可改的每一项，用于映射自动失效判断）
        /// </summary>
        public static string BuildLimitsStamp()
        {
            return string.Join("|",
                LimitSkillItem, LimitRelic, LimitOtherItem,
                LimitUpSlash, LimitLeftSlash, LimitRightSlash,
                LimitDashLeft, LimitDashRight,
                LimitHarpoonLeft, LimitHarpoonRight,
                LimitFloatLeft, LimitFloatRight,
                LimitWallJumpLeft, LimitWallJumpRight, LimitHeal,
                LimitNeedleThrow, LimitThreadSphere, LimitHarpoonDash,
                LimitSilkCharge, LimitSilkBomb, LimitSilkBossNeedle,
                LimitNeedolin, LimitParry, LimitNeedolinMemory,
                LimitFastTravel, LimitEvaHeal, LimitDash, LimitBrolly,
                LimitDoubleJump, LimitSuperJump, LimitWallJump, LimitChargeSlash,
                LimitHeartPiece, LimitSpoolPart, LimitMaxSilkRegenUp, LimitUnlockCrestSlot,
                EnableInfSilk, EnableInfBlueHealth,
                EnableInfGeo300, EnableInfShards300,
                EnableLoreReward, EnableLoreTrigger, EnableInspectPermit,
                EnableMapRewards, EnableStationRewards,
                EnableMapCheckIntercept, EnableStationCheckIntercept);
        }

        // ========== 更新标记 / 防抖 ==========
        // 面板每次输入只需置脏标（O(1)），真正重建由 HotkeyHandler.Update 每帧防抖执行，
        // 避免连续键入时每个字符都触发全量映射重建。
        private static bool _limitsDirty = false;
        private static float _lastRegenerateTime = float.MinValue;
        private const float RegenerateDebounceMs = 350f;

        public static bool LimitsDirty => _limitsDirty;

        /// <summary>面板/代码改动后调用：仅置脏标，不立即重建</summary>
        public static void MarkLimitsDirty() => _limitsDirty = true;

        /// <summary>
        /// 由主线程每帧调用。若已超过防抖间隔且存在脏标，则执行一次「同步 cfg + 强制重建映射」。
        /// 返回是否真正执行了重建。
        /// </summary>
        public static bool TryFlushLimitsRegenerate()
        {
            if (!_limitsDirty) return false;
            if (UnityEngine.Time.time < _lastRegenerateTime + RegenerateDebounceMs / 1000f) return false;

            _limitsDirty = false;
            _lastRegenerateTime = UnityEngine.Time.time;
            Plugin.Log?.LogInfo("[ItemLimitConfig] 防抖窗口到，执行 limit 持久化与映射重建");
            return true;
        }

        /// <summary>由启动/删档显式清空脏标与时间戳，防抖机制复位</summary>
        public static void ResetRegenerateState()
        {
            _limitsDirty = false;
            _lastRegenerateTime = float.MinValue;
        }

        // ========== 查询方法（供 ItemRandomizer 使用） ==========
        public static int GetAbilityLimit(string abilityId)
        {
            return abilityId switch
            {
                "hasNeedleThrow" => LimitNeedleThrow,
                "hasThreadSphere" => LimitThreadSphere,
                "hasHarpoonDash" => LimitHarpoonDash,
                "hasSilkCharge" => LimitSilkCharge,
                "hasSilkBomb" => LimitSilkBomb,
                "hasSilkBossNeedle" => LimitSilkBossNeedle,
                "hasNeedolin" => LimitNeedolin,
                "hasParry" => LimitParry,
                "hasNeedolinMemoryPowerup" => LimitNeedolinMemory,
                "hasFastTravelTeleport" => LimitFastTravel,
                "HasSeenEvaHeal" => LimitEvaHeal,
                "hasDash" => LimitDash,
                "hasBrolly" => LimitBrolly,
                "hasDoubleJump" => LimitDoubleJump,
                "hasSuperJump" => LimitSuperJump,
                "hasWalljump" => LimitWallJump,
                "hasChargeSlash" => LimitChargeSlash,
                _ => 1
            };
        }

        public static int GetDirectionLimit(string id)
        {
            return id switch
            {
                "perm:upward" => LimitUpSlash,
                "perm:left" => LimitLeftSlash,
                "perm:right" => LimitRightSlash,
                "perm:hasDash_L" => LimitDashLeft,
                "perm:hasDash_R" => LimitDashRight,
                "perm:hasHarpoonDash_L" => LimitHarpoonLeft,
                "perm:hasHarpoonDash_R" => LimitHarpoonRight,
                "perm:hasBrolly_L" => LimitFloatLeft,
                "perm:hasBrolly_R" => LimitFloatRight,
                "perm:hasWalljump_L" => LimitWallJumpLeft,
                "perm:hasWalljump_R" => LimitWallJumpRight,
                "perm:heal" => LimitHeal,
                _ => 1
            };
        }

        public static int GetPreciousLimit(string id)
        {
            return id switch
            {
                "virt:HeartPiece" => LimitHeartPiece,
                "virt:SpoolPart" => LimitSpoolPart,
                "virt:MaxSilkRegenUp" => LimitMaxSilkRegenUp,
                "virt:UnlockCrestSlot" => LimitUnlockCrestSlot,
                _ => 2
            };
        }

        public static int GetItemTypeLimit(SavedItem item)
        {
            if (item == null) return 2;
            var t = item.GetType();
            if (t == typeof(ToolItemSkill) || t.IsSubclassOf(typeof(ToolItemSkill)) || item is ToolItemSkill)
                return LimitSkillItem;
            if (typeof(CollectableRelic).IsAssignableFrom(t))
                return LimitRelic;
            return LimitOtherItem;
        }
    }
}