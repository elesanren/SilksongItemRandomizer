using BepInEx.Configuration;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System;
using TeamCherry.Localization;
using UnityEngine;

// ItemLimitConfig.cs - 修改为可写属性，保持原有功能

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 物品重复获得次数限制配置
    /// 属性改为可读写，以便外部（如 MenuChanger）实时修改
    /// </summary>
    public static class ItemLimitConfig
    {
        private static BepInEx.Configuration.ConfigFile _configFile;

        // ========== 普通物品（映射模式每物品恰好 1 次全覆盖，此处为动态兜底上限） ==========
        public static int LimitSkillItem { get; set; } = 2;
        public static int LimitRelic { get; set; } = 1;
        public static int LimitOtherItem { get; set; } = 1;

        // ========== 方向权限 ==========
        // 攻击/回血权限与能力一致按"双倍"处理；方向权限同样统一 2 份（2026-08-24 调整）
        public static int LimitUpSlash { get; set; } = 2;
        public static int LimitLeftSlash { get; set; } = 2;
        public static int LimitRightSlash { get; set; } = 2;
        public static int LimitDashLeft { get; set; } = 2;
        public static int LimitDashRight { get; set; } = 2;
        public static int LimitHarpoonLeft { get; set; } = 2;
        public static int LimitHarpoonRight { get; set; } = 2;
        public static int LimitFloatLeft { get; set; } = 2;
        public static int LimitFloatRight { get; set; } = 2;
        public static int LimitWallJumpLeft { get; set; } = 2;
        public static int LimitWallJumpRight { get; set; } = 2;
        public static int LimitHeal { get; set; } = 2;

        // ========== 能力虚拟奖励（默认 2 = 双倍发放，映射与动态共用此值） ==========
        public static int LimitNeedleThrow { get; set; } = 2;
        public static int LimitThreadSphere { get; set; } = 2;
        public static int LimitHarpoonDash { get; set; } = 2;
        public static int LimitSilkCharge { get; set; } = 2;
        public static int LimitSilkBomb { get; set; } = 2;
        public static int LimitSilkBossNeedle { get; set; } = 2;
        public static int LimitNeedolin { get; set; } = 2;
        public static int LimitParry { get; set; } = 2;
        public static int LimitNeedolinMemory { get; set; } = 2;
        public static int LimitFastTravel { get; set; } = 2;
        public static int LimitEvaHeal { get; set; } = 2;
        public static int LimitDash { get; set; } = 2;
        public static int LimitBrolly { get; set; } = 2;
        public static int LimitDoubleJump { get; set; } = 2;
        public static int LimitSuperJump { get; set; } = 2;
        public static int LimitWallJump { get; set; } = 2;
        public static int LimitChargeSlash { get; set; } = 2;

        // ========== 珍贵虚拟奖励 ==========
        public static int LimitHeartPiece { get; set; } = 20;
        public static int LimitSpoolPart { get; set; } = 18;
        public static int LimitMaxSilkRegenUp { get; set; } = 2;
        public static int LimitUnlockCrestSlot { get; set; } = 2;
        /// <summary>简单钥匙全游戏共 4 把，全部入池</summary>
        public static int LimitSimpleKey { get; set; } = 4;
        /// <summary>跳蚤救援可获取次数（对应 flea:01~27 共 27 次）</summary>
        public static int LimitFleaRescue { get; set; } = 27;
        /// <summary>寒冷抗性（雪绫披风拆分功能）：默认全游戏 1 个</summary>
        public static int LimitColdResist { get; set; } = 2;
        /// <summary>游泳权限：默认全游戏 1 个</summary>
        public static int LimitSwim { get; set; } = 2;

        // ========== 收集类顺序键总数（2026-08-29 全量归纳；精确份数 = 物理存在总数，
        // 各分支必须全部齐全，默认值不可低于原生总数，降低会让对应分支缺档） ==========
        /// <summary>苔莓 moss:01~06 全游戏共 6 个</summary>
        public static int LimitMoss { get; set; } = 6;
        /// <summary>花芯 flower:01~06 全游戏共 6 个</summary>
        public static int LimitFlower { get; set; } = 6;
        /// <summary>无限池随机货币（virt:RandomCoin）加权重复份数（权重）</summary>
        public static int RandomCoinWeight { get; set; } = 6;

        // ========== 无限池奖励开关 ==========
        public static bool EnableInfSilk { get; private set; } = true;
        public static bool EnableInfBlueHealth { get; private set; } = true;
        public static bool EnableInfGeo300 { get; private set; } = true;
        public static bool EnableInfShards300 { get; private set; } = true;

        // ========== 日志随机奖励开关 ==========
        public static bool EnableLoreReward { get; private set; } = true;

        // ========== 世界石碑阅读触发随机奖励开关 ==========
        public static bool EnableLoreTrigger { get; private set; } = true;

        // ========== 地图/车站随机开关 ==========
        /// <summary>28 张地图加入随机奖励池</summary>
        public static bool EnableMapRewards { get; set; } = true;
        /// <summary>10 个车站 + 铃兽总开关加入随机奖励池</summary>
        public static bool EnableStationRewards { get; set; } = true;
        /// <summary>拦截原版地图购买（沙克拉商店）转为随机奖励</summary>
        public static bool EnableMapCheckIntercept { get; set; } = true;
        /// <summary>拦截原版车站开通（收费机）转为随机奖励</summary>
        public static bool EnableStationCheckIntercept { get; set; } = true;
        /// <summary>6 个管道站加入随机奖励池</summary>
        public static bool EnableTubeRewards { get; set; } = true;
        /// <summary>拦截原版管道开通（管道收费机）转为随机奖励</summary>
        public static bool EnableTubeCheckIntercept { get; set; } = true;

        // ========== 单一事实来源：全部 limit 归纳在此表 ==========
        // 每个 limit 一条记录，同时驱动：cfg 默认值（SyncCodeDefaultsToConfig）、Init 绑定、
        // Apply 快照反写、SaveToConfigFile 持久化、BuildLimitsStamp 指纹、ItemLimitSettings 捕获。
        // 新增 limit 只需：声明静态属性 + 在 AllLimitInts/AllLimitBools 加一行。菜单/DLL 形态不必再动。
        private sealed class LimitDef
        {
            public string Key;            // cfg 文件里的 key（"SkillItem"）
            public int Default;           // 代码默认值（= 属性声明默认，2026-08-24 方向权限统一 2）
            public Func<int> Get;         // 读静态属性
            public Action<int> Set;       // 写静态属性
            public string DtoField;       // ItemLimitSettings 同名字段（反射快照用）
        }

        private sealed class BoolDef
        {
            public string Section;        // cfg 分区
            public string Key;
            public bool Default;
            public Func<bool> Get;
            public Action<bool> Set;
        }

        private static readonly LimitDef[] AllLimitInts =
        {
            // 普通物品
            new LimitDef { Key = "SkillItem", Default = 2, Get = () => LimitSkillItem, Set = v => LimitSkillItem = v, DtoField = "SkillItem" },
            new LimitDef { Key = "Relic", Default = 1, Get = () => LimitRelic, Set = v => LimitRelic = v, DtoField = "Relic" },
            new LimitDef { Key = "OtherItem", Default = 1, Get = () => LimitOtherItem, Set = v => LimitOtherItem = v, DtoField = "OtherItem" },
            // 方向权限（统一 2 份，2026-08-24 调整）
            new LimitDef { Key = "UpSlash", Default = 2, Get = () => LimitUpSlash, Set = v => LimitUpSlash = v, DtoField = "UpSlash" },
            new LimitDef { Key = "LeftSlash", Default = 2, Get = () => LimitLeftSlash, Set = v => LimitLeftSlash = v, DtoField = "LeftSlash" },
            new LimitDef { Key = "RightSlash", Default = 2, Get = () => LimitRightSlash, Set = v => LimitRightSlash = v, DtoField = "RightSlash" },
            new LimitDef { Key = "DashLeft", Default = 2, Get = () => LimitDashLeft, Set = v => LimitDashLeft = v, DtoField = "DashLeft" },
            new LimitDef { Key = "DashRight", Default = 2, Get = () => LimitDashRight, Set = v => LimitDashRight = v, DtoField = "DashRight" },
            new LimitDef { Key = "HarpoonLeft", Default = 2, Get = () => LimitHarpoonLeft, Set = v => LimitHarpoonLeft = v, DtoField = "HarpoonLeft" },
            new LimitDef { Key = "HarpoonRight", Default = 2, Get = () => LimitHarpoonRight, Set = v => LimitHarpoonRight = v, DtoField = "HarpoonRight" },
            new LimitDef { Key = "FloatLeft", Default = 2, Get = () => LimitFloatLeft, Set = v => LimitFloatLeft = v, DtoField = "FloatLeft" },
            new LimitDef { Key = "FloatRight", Default = 2, Get = () => LimitFloatRight, Set = v => LimitFloatRight = v, DtoField = "FloatRight" },
            new LimitDef { Key = "WallJumpLeft", Default = 2, Get = () => LimitWallJumpLeft, Set = v => LimitWallJumpLeft = v, DtoField = "WallJumpLeft" },
            new LimitDef { Key = "WallJumpRight", Default = 2, Get = () => LimitWallJumpRight, Set = v => LimitWallJumpRight = v, DtoField = "WallJumpRight" },
            new LimitDef { Key = "Heal", Default = 2, Get = () => LimitHeal, Set = v => LimitHeal = v, DtoField = "Heal" },
            // 能力虚拟奖励（默认 2 = 双倍发放，映射与动态共用此值）
            new LimitDef { Key = "NeedleThrow", Default = 2, Get = () => LimitNeedleThrow, Set = v => LimitNeedleThrow = v, DtoField = "NeedleThrow" },
            new LimitDef { Key = "ThreadSphere", Default = 2, Get = () => LimitThreadSphere, Set = v => LimitThreadSphere = v, DtoField = "ThreadSphere" },
            new LimitDef { Key = "HarpoonDash", Default = 2, Get = () => LimitHarpoonDash, Set = v => LimitHarpoonDash = v, DtoField = "HarpoonDash" },
            new LimitDef { Key = "SilkCharge", Default = 2, Get = () => LimitSilkCharge, Set = v => LimitSilkCharge = v, DtoField = "SilkCharge" },
            new LimitDef { Key = "SilkBomb", Default = 2, Get = () => LimitSilkBomb, Set = v => LimitSilkBomb = v, DtoField = "SilkBomb" },
            new LimitDef { Key = "SilkBossNeedle", Default = 2, Get = () => LimitSilkBossNeedle, Set = v => LimitSilkBossNeedle = v, DtoField = "SilkBossNeedle" },
            new LimitDef { Key = "Needolin", Default = 2, Get = () => LimitNeedolin, Set = v => LimitNeedolin = v, DtoField = "Needolin" },
            new LimitDef { Key = "Parry", Default = 2, Get = () => LimitParry, Set = v => LimitParry = v, DtoField = "Parry" },
            new LimitDef { Key = "NeedolinMemory", Default = 2, Get = () => LimitNeedolinMemory, Set = v => LimitNeedolinMemory = v, DtoField = "NeedolinMemory" },
            new LimitDef { Key = "FastTravel", Default = 2, Get = () => LimitFastTravel, Set = v => LimitFastTravel = v, DtoField = "FastTravel" },
            new LimitDef { Key = "EvaHeal", Default = 2, Get = () => LimitEvaHeal, Set = v => LimitEvaHeal = v, DtoField = "EvaHeal" },
            new LimitDef { Key = "Dash", Default = 2, Get = () => LimitDash, Set = v => LimitDash = v, DtoField = "Dash" },
            new LimitDef { Key = "Brolly", Default = 2, Get = () => LimitBrolly, Set = v => LimitBrolly = v, DtoField = "Brolly" },
            new LimitDef { Key = "DoubleJump", Default = 2, Get = () => LimitDoubleJump, Set = v => LimitDoubleJump = v, DtoField = "DoubleJump" },
            new LimitDef { Key = "SuperJump", Default = 2, Get = () => LimitSuperJump, Set = v => LimitSuperJump = v, DtoField = "SuperJump" },
            new LimitDef { Key = "WallJump", Default = 2, Get = () => LimitWallJump, Set = v => LimitWallJump = v, DtoField = "WallJump" },
            new LimitDef { Key = "ChargeSlash", Default = 2, Get = () => LimitChargeSlash, Set = v => LimitChargeSlash = v, DtoField = "ChargeSlash" },
            // 珍贵虚拟奖励
            new LimitDef { Key = "HeartPiece", Default = 20, Get = () => LimitHeartPiece, Set = v => LimitHeartPiece = v, DtoField = "HeartPiece" },
            new LimitDef { Key = "SpoolPart", Default = 18, Get = () => LimitSpoolPart, Set = v => LimitSpoolPart = v, DtoField = "SpoolPart" },
            new LimitDef { Key = "MaxSilkRegenUp", Default = 2, Get = () => LimitMaxSilkRegenUp, Set = v => LimitMaxSilkRegenUp = v, DtoField = "MaxSilkRegenUp" },
            new LimitDef { Key = "UnlockCrestSlot", Default = 2, Get = () => LimitUnlockCrestSlot, Set = v => LimitUnlockCrestSlot = v, DtoField = "UnlockCrestSlot" },
            // 计数类（此前全部缺席于 cfg 绑定——flea 无 case / coldresist.swim 无条目，本次全量归纳）
            new LimitDef { Key = "SimpleKey", Default = 4, Get = () => LimitSimpleKey, Set = v => LimitSimpleKey = v, DtoField = "SimpleKey" },
            new LimitDef { Key = "FleaRescue", Default = 27, Get = () => LimitFleaRescue, Set = v => LimitFleaRescue = v, DtoField = "FleaRescue" },
            new LimitDef { Key = "ColdResist", Default = 2, Get = () => LimitColdResist, Set = v => LimitColdResist = v, DtoField = "ColdResist" },
            new LimitDef { Key = "Swim", Default = 2, Get = () => LimitSwim, Set = v => LimitSwim = v, DtoField = "Swim" },
            // 收集类顺序键总数：精确份数（默认 = 物理存在总数，各分支必须齐全，勿降太低）
            new LimitDef { Key = "Moss", Default = 6, Get = () => LimitMoss, Set = v => LimitMoss = v, DtoField = "Moss" },
            new LimitDef { Key = "Flower", Default = 6, Get = () => LimitFlower, Set = v => LimitFlower = v, DtoField = "Flower" },
            // 无限池随机货币加权权重
            new LimitDef { Key = "RandomCoinWeight", Default = 6, Get = () => RandomCoinWeight, Set = v => RandomCoinWeight = v, DtoField = "RandomCoinWeight" },
        };

        private static readonly BoolDef[] AllLimitBools =
        {
            new BoolDef { Section = "Limits", Key = "EnableInfSilk", Default = true, Get = () => EnableInfSilk, Set = v => EnableInfSilk = v },
            new BoolDef { Section = "Limits", Key = "EnableInfBlueHealth", Default = true, Get = () => EnableInfBlueHealth, Set = v => EnableInfBlueHealth = v },
            new BoolDef { Section = "Limits", Key = "EnableInfGeo300", Default = true, Get = () => EnableInfGeo300, Set = v => EnableInfGeo300 = v },
            new BoolDef { Section = "Limits", Key = "EnableInfShards300", Default = true, Get = () => EnableInfShards300, Set = v => EnableInfShards300 = v },
            new BoolDef { Section = "Limits", Key = "EnableLoreReward", Default = true, Get = () => EnableLoreReward, Set = v => EnableLoreReward = v },
            new BoolDef { Section = "Limits", Key = "EnableLoreTrigger", Default = true, Get = () => EnableLoreTrigger, Set = v => EnableLoreTrigger = v },
            new BoolDef { Section = "MapStation", Key = "EnableMapRewards", Default = true, Get = () => EnableMapRewards, Set = v => EnableMapRewards = v },
            new BoolDef { Section = "MapStation", Key = "EnableStationRewards", Default = true, Get = () => EnableStationRewards, Set = v => EnableStationRewards = v },
            new BoolDef { Section = "MapStation", Key = "EnableMapCheckIntercept", Default = true, Get = () => EnableMapCheckIntercept, Set = v => EnableMapCheckIntercept = v },
            new BoolDef { Section = "MapStation", Key = "EnableStationCheckIntercept", Default = true, Get = () => EnableStationCheckIntercept, Set = v => EnableStationCheckIntercept = v },
            new BoolDef { Section = "MapStation", Key = "EnableTubeRewards", Default = true, Get = () => EnableTubeRewards, Set = v => EnableTubeRewards = v },
            new BoolDef { Section = "MapStation", Key = "EnableTubeCheckIntercept", Default = true, Get = () => EnableTubeCheckIntercept, Set = v => EnableTubeCheckIntercept = v },
        };

        private static string _codeDefaultsStampCache;

        // 反射缓存：ItemLimitSettings 字段名 → FieldInfo（Apply/快照用，低频反射可接受）
        private static readonly Dictionary<string, FieldInfo> ItemLimitSettingsFields =
            typeof(ItemLimitSettings).GetFields(BindingFlags.Public | BindingFlags.Instance)
                .ToDictionary(f => f.Name, f => f);

        private static string BuildCodeDefaultsStamp()
        {
            return _codeDefaultsStampCache ??= BuildCodeDefaultsStampInner();
        }

        private static string BuildCodeDefaultsStampInner()
        {
            var sb = new System.Text.StringBuilder("v1:");
            foreach (var d in AllLimitInts)
            {
                sb.Append(d.Key).Append('=').Append(d.Default).Append(',');
            }
            foreach (var d in AllLimitBools)
            {
                sb.Append(d.Section).Append('.').Append(d.Key).Append('=').Append(d.Default ? 1 : 0).Append(',');
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
            foreach (var d in AllLimitInts)
                config.Bind<int>("Limits", d.Key, d.Default).Value = d.Default;
            foreach (var d in AllLimitBools)
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

            // 全部 limit / 开关由单一事实表驱动绑定，表里每一项自动覆盖（含 FleaRescue/ColdResist/Swim，
            // 此前缺口现在全量归纳，cfg 改动必然生效）
            foreach (var d in AllLimitInts)
                d.Set(config.Bind<int>("Limits", d.Key, d.Default).Value);

            foreach (var d in AllLimitBools)
                d.Set(config.Bind<bool>(d.Section, d.Key, d.Default).Value);
        }

        /// <summary>
        /// 批量应用新的限制值（由 API 调用，覆盖所有可配置项）
        /// </summary>
        public static void Apply(ItemLimitSettings settings)
        {
            if (settings == null) return;

            // 表驱动：与 AllLimitInts 中 DtoField 同名的最新字段存在则写入
            //（反射仅在配置应用时低频调用；保持静态属性名不变以兼容 MenuChanger）
            foreach (var d in AllLimitInts)
            {
                if (ItemLimitSettingsFields.TryGetValue(d.DtoField, out var field))
                    d.Set((int)field.GetValue(settings));
            }
        }

        /// <summary>
        /// 由当前 ItemLimitConfig 静态属性捕获一份 ItemLimitSettings 快照。
        /// 取代 Plugin.cs / API 中逐字段手写映射（新增 limit 后此处自动覆盖）。
        /// </summary>
        public static ItemLimitSettings CaptureSettings()
        {
            var settings = new ItemLimitSettings();
            foreach (var d in AllLimitInts)
            {
                if (ItemLimitSettingsFields.TryGetValue(d.DtoField, out var field))
                    field.SetValue(settings, d.Get());
            }
            return settings;
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

            // 表驱动：所有 limit/开关由单一事实表统一持久化
            foreach (var d in AllLimitInts)
                _configFile.Bind<int>("Limits", d.Key, d.Default).Value = d.Get();
            foreach (var d in AllLimitBools)
                _configFile.Bind<bool>(d.Section, d.Key, d.Default).Value = d.Get();

            _configFile.Save();
            Plugin.Log?.LogInfo("[ItemLimitConfig] limit 配置已写回配置文件");
        }

        /// <summary>
        /// 生成全部 limit 值的紧凑指纹（含面板可改的每一项，用于映射自动失效判断）
        /// </summary>
        public static string BuildLimitsStamp()
        {
            // 表驱动：所有 limit/开关按固定表顺序拼接（顺序稳定 = 指纹稳定）
            var parts = new List<string>(AllLimitInts.Length + AllLimitBools.Length);
            foreach (var d in AllLimitInts)
                parts.Add(d.Get().ToString());
            foreach (var d in AllLimitBools)
                parts.Add(d.Get() ? "1" : "0");
            return string.Join("|", parts);
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
                "ColdResist" => LimitColdResist,
                "Swim" => LimitSwim,
                "SilkHeart" => LimitMaxSilkRegenUp,
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
            // 简单钥匙：全游戏共 4 把，全部入池（配置化）
            if (string.Equals(item.name, "Simple Key", System.StringComparison.Ordinal)) return LimitSimpleKey;
            var t = item.GetType();
            if (t == typeof(ToolItemSkill) || t.IsSubclassOf(typeof(ToolItemSkill)) || item is ToolItemSkill)
                return LimitSkillItem;
            if (typeof(CollectableRelic).IsAssignableFrom(t))
                return LimitRelic;
            return LimitOtherItem;
        }
    }
}


namespace SilksongItemRandomizer
{
    /// <summary>
    /// 负责将所有物品的显示名称注册到游戏的本地化系统中，
    /// 以便在商店等 UI 中使用 LocalisedString 正确显示中文名称。
    /// </summary>
    public static class ItemLocalizationRegistrar
    {
        public const string CustomSheetName = "SilksongItemRandomizer";

        private static readonly HashSet<string> _registeredKeys = new HashSet<string>();

        private static Dictionary<string, Dictionary<string, string>> _localizationDict;

        private static bool EnsureLocalizationDict()
        {
            if (_localizationDict != null)
                return true;

            var field = typeof(Language).GetField("_currentEntrySheets", BindingFlags.Static | BindingFlags.NonPublic);
            if (field == null)
            {
                Plugin.Log.LogError("[ItemLocalization] 无法获取 Language._currentEntrySheets 字段");
                return false;
            }

            _localizationDict = field.GetValue(null) as Dictionary<string, Dictionary<string, string>>;
            if (_localizationDict == null)
            {
                Plugin.Log.LogError("[ItemLocalization] Language._currentEntrySheets 字段不是预期类型");
                return false;
            }

            return true;
        }

        public static bool RegisterItemName(string key, string displayName)
        {
            if (!EnsureLocalizationDict())
                return false;

            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(displayName))
                return false;

            if (_registeredKeys.Contains(key))
                return true;

            if (!_localizationDict.TryGetValue(CustomSheetName, out var sheet))
            {
                sheet = new Dictionary<string, string>();
                _localizationDict[CustomSheetName] = sheet;
            }

            sheet[key] = displayName;
            _registeredKeys.Add(key);

            return true;
        }

        public static string GetLocalizationKey(string itemInternalName)
        {
            return $"item_{itemInternalName}";
        }

        public static LocalisedString GetLocalisedString(string itemInternalName)
        {
            return new LocalisedString(CustomSheetName, GetLocalizationKey(itemInternalName));
        }

        public static void RegisterAllKnownItems()
        {
            if (!EnsureLocalizationDict())
                return;

            var allSavedItems = Resources.FindObjectsOfTypeAll<SavedItem>();
            int registeredCount = 0;

            foreach (var item in allSavedItems)
            {
                if (item == null) continue;

                string internalName = item.name;
                string displayName = GetItemDisplayNameSafe(item);

                if (string.IsNullOrEmpty(displayName))
                    continue;

                string key = GetLocalizationKey(internalName);
                if (RegisterItemName(key, displayName))
                    registeredCount++;
            }

            Plugin.Log.LogInfo($"[ItemLocalization] 已注册 {registeredCount} 个物品的本地化名称");
        }

        /// <summary>
        /// 安全获取显示名称，避免调用未实现的 GetPopupName 抛出异常。
        /// </summary>
        private static string GetItemDisplayNameSafe(SavedItem item)
        {
            // CollectableItemStates 在无状态满足时调用 GetPopupName 会触发游戏本体
            // "Item state was less than 0" 错误日志；改为直接读取首个状态的显示名
            if (item is CollectableItemStates statesItem)
            {
                try
                {
                    var statesField = typeof(CollectableItemStates).GetField("states", BindingFlags.Instance | BindingFlags.NonPublic);
                    var states = statesField?.GetValue(statesItem) as Array;
                    if (states != null && states.Length > 0)
                    {
                        for (int i = 0; i < states.Length; i++)
                        {
                            var state = states.GetValue(i);
                            if (state == null) continue;
                            var dnField = state.GetType().GetField("DisplayName",
                                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            var dn = dnField?.GetValue(state);
                            if (dn is LocalisedString ls)
                            {
                                string text = ToLocalisedString(ls);
                                if (!string.IsNullOrEmpty(text))
                                    return text;
                            }
                        }
                    }
                }
                catch
                {
                    // 反射失败则回退到 item.name
                }
                return item.name;
            }

            // 通过反射检查 GetPopupName 方法是否被重写（即 DeclaringType 不是 SavedItem）
            var method = typeof(SavedItem).GetMethod("GetPopupName", BindingFlags.Instance | BindingFlags.Public);
            if (method != null)
            {
                // 获取 item 实际类型的方法
                var actualMethod = item.GetType().GetMethod("GetPopupName", BindingFlags.Instance | BindingFlags.Public);
                if (actualMethod != null && actualMethod.DeclaringType == typeof(SavedItem))
                {
                    // 未重写，直接返回 item.name
                    return item.name;
                }
            }

            // 尝试调用，但捕获所有异常（包括 NotImplementedException 和其他）
            try
            {
                return item.GetPopupName();
            }
            catch
            {
                return item.name;
            }
        }

        // 通过反射调用 LocalisedString 的隐式转换（op_Implicit），避免直接强转
        private static string ToLocalisedString(LocalisedString ls)
        {
            try
            {
                var op = typeof(LocalisedString).GetMethod("op_Implicit", new[] { typeof(LocalisedString) });
                if (op != null && op.IsStatic)
                    return (string)op.Invoke(null, new object[] { ls });
                return ls.ToString();
            }
            catch
            {
                return null;
            }
        }

        public static void Reset()
        {
            if (!EnsureLocalizationDict())
                return;

            if (_localizationDict.ContainsKey(CustomSheetName))
                _localizationDict[CustomSheetName].Clear();

            _registeredKeys.Clear();
            Plugin.Log.LogInfo("[ItemLocalization] 已清空所有自定义本地化条目");
        }
    }
}


namespace SilksongItemRandomizer
{
    /// <summary>
    /// 全局 Sprite 按需缓存：不再启动时全量扫描并强引用数万个 Sprite（阻止图集纹理卸载），
    /// 只缓存实际被请求过的名字（命中后 O(1)，未命中的名字记入负缓存避免重复扫描）。
    /// 全量枚举（图标筛选调试窗）改为现场扫描+去重，不在内存中驻留。
    /// </summary>
    public static class SpriteCache
    {
        private static readonly Dictionary<string, Sprite> _cache = new Dictionary<string, Sprite>();
        private static readonly HashSet<string> _misses = new HashSet<string>(System.StringComparer.Ordinal);

        /// <summary>兼容旧调用点：按需缓存模式下无需预构建，保留空实现。</summary>
        public static void EnsureBuilt()
        {
        }

        public static Sprite Find(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_cache.TryGetValue(name, out var sprite)) return sprite;
            if (_misses.Contains(name)) return null;
            var all = Resources.FindObjectsOfTypeAll<Sprite>();
            foreach (var s in all)
            {
                if (s == null || string.IsNullOrEmpty(s.name)) continue;
                if (string.Equals(s.name, name, System.StringComparison.Ordinal))
                {
                    _cache[name] = s;
                    return s;
                }
            }
            _misses.Add(name);
            return null;
        }

        /// <summary>返回当前已加载的全部 Sprite 名单（同名去重，现场扫描不驻留），供全量图标搜索使用。</summary>
        public static IEnumerable<Sprite> GetAll()
        {
            var all = Resources.FindObjectsOfTypeAll<Sprite>();
            var seen = new HashSet<string>(System.StringComparer.Ordinal);
            var result = new List<Sprite>();
            foreach (var s in all)
            {
                if (s == null || string.IsNullOrEmpty(s.name)) continue;
                if (seen.Add(s.name)) result.Add(s);
            }
            return result;
        }

        public static void Reset()
        {
            _cache.Clear();
            _misses.Clear();
        }
    }
}
