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
        public static int LimitHeartPiece { get; set; } = 2;
        public static int LimitSpoolPart { get; set; } = 2;
        public static int LimitMaxSilkRegenUp { get; set; } = 2;
        public static int LimitUnlockCrestSlot { get; set; } = 2;

        // ========== 无限池奖励开关 ==========
        public static bool EnableInfSilk { get; private set; } = true;
        public static bool EnableInfFullRestore { get; private set; } = true;
        public static bool EnableInfBlueHealth { get; private set; } = true;
        public static bool EnableInfGeo300 { get; private set; } = true;
        public static bool EnableInfShards300 { get; private set; } = true;
        public static bool EnableInfSilkParts { get; private set; } = true;

        /// <summary>
        /// 从 BepInEx 配置文件初始化（仅在游戏启动时调用一次）
        /// </summary>
        public static void Init(ConfigFile config)
        {
            var bindInt = new Func<string, string, int, int>((section, key, defaultValue) =>
                config.Bind<int>(section, key, defaultValue).Value);
            var bindBool = new Func<string, string, bool, bool>((section, key, defaultValue) =>
                config.Bind<bool>(section, key, defaultValue).Value);

            LimitSkillItem = bindInt("Limits", "SkillItem", 2);
            LimitRelic = bindInt("Limits", "Relic", 2);
            LimitOtherItem = bindInt("Limits", "OtherItem", 2);

            LimitUpSlash = bindInt("Limits", "UpSlash", 1);
            LimitLeftSlash = bindInt("Limits", "LeftSlash", 1);
            LimitRightSlash = bindInt("Limits", "RightSlash", 1);
            LimitDashLeft = bindInt("Limits", "DashLeft", 1);
            LimitDashRight = bindInt("Limits", "DashRight", 1);
            LimitHarpoonLeft = bindInt("Limits", "HarpoonLeft", 1);
            LimitHarpoonRight = bindInt("Limits", "HarpoonRight", 1);
            LimitFloatLeft = bindInt("Limits", "FloatLeft", 1);
            LimitFloatRight = bindInt("Limits", "FloatRight", 1);
            LimitWallJumpLeft = bindInt("Limits", "WallJumpLeft", 1);
            LimitWallJumpRight = bindInt("Limits", "WallJumpRight", 1);
            LimitHeal = bindInt("Limits", "Heal", 1);

            LimitNeedleThrow = bindInt("Limits", "NeedleThrow", 1);
            LimitThreadSphere = bindInt("Limits", "ThreadSphere", 1);
            LimitHarpoonDash = bindInt("Limits", "HarpoonDash", 1);
            LimitSilkCharge = bindInt("Limits", "SilkCharge", 1);
            LimitSilkBomb = bindInt("Limits", "SilkBomb", 1);
            LimitSilkBossNeedle = bindInt("Limits", "SilkBossNeedle", 1);
            LimitNeedolin = bindInt("Limits", "Needolin", 1);
            LimitParry = bindInt("Limits", "Parry", 1);
            LimitNeedolinMemory = bindInt("Limits", "NeedolinMemory", 1);
            LimitFastTravel = bindInt("Limits", "FastTravel", 1);
            LimitEvaHeal = bindInt("Limits", "EvaHeal", 1);
            LimitDash = bindInt("Limits", "Dash", 1);
            LimitBrolly = bindInt("Limits", "Brolly", 1);
            LimitDoubleJump = bindInt("Limits", "DoubleJump", 1);
            LimitSuperJump = bindInt("Limits", "SuperJump", 1);
            LimitWallJump = bindInt("Limits", "WallJump", 1);
            LimitChargeSlash = bindInt("Limits", "ChargeSlash", 1);

            LimitHeartPiece = bindInt("Limits", "HeartPiece", 2);
            LimitSpoolPart = bindInt("Limits", "SpoolPart", 2);
            LimitMaxSilkRegenUp = bindInt("Limits", "MaxSilkRegenUp", 2);
            LimitUnlockCrestSlot = bindInt("Limits", "UnlockCrestSlot", 2);

            EnableInfSilk = bindBool("Limits", "EnableInfSilk", true);
            EnableInfFullRestore = bindBool("Limits", "EnableInfFullRestore", true);
            EnableInfBlueHealth = bindBool("Limits", "EnableInfBlueHealth", true);
            EnableInfGeo300 = bindBool("Limits", "EnableInfGeo300", true);
            EnableInfShards300 = bindBool("Limits", "EnableInfShards300", true);
            EnableInfSilkParts = bindBool("Limits", "EnableInfSilkParts", true);
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
            EnableInfFullRestore = settings.FullRestore;
            EnableInfBlueHealth = settings.BlueHealth;
            EnableInfGeo300 = settings.Geo300;
            EnableInfShards300 = settings.Shards300;
            EnableInfSilkParts = settings.SilkParts;
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