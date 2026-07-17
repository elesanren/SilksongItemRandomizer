using BepInEx.Configuration;
using System;

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 物品随机类型过滤器（纹章、技能类物品、遗物等是否参与随机）
    /// 改为普通静态布尔属性，由 API 通过 Apply 方法设置，不再依赖 ConfigEntry
    /// </summary>
    public static class ItemTypeRandomFilter
    {
        // ========== 开关属性 ==========
        public static bool EnableCrestRandom { get; private set; } = true;
        public static bool EnableSkillItemRandom { get; private set; } = true;
        public static bool EnableRelicRandom { get; private set; } = true;

        /// <summary>
        /// 从 BepInEx 配置文件初始化（仅在游戏启动时调用一次）
        /// </summary>
        public static void Init(ConfigFile config)
        {
            EnableSkillItemRandom = config.Bind<bool>("ItemRandom", "EnableSkillItemRandom", true,
                "是否随机技能类物品（ToolItemSkill 等）").Value;
            EnableRelicRandom = config.Bind<bool>("ItemRandom", "EnableRelicRandom", true,
                "是否随机遗物（CollectableRelic）").Value;
            // 注意：EnableCrestRandom 没有在配置文件中直接绑定，而是通过 SetCrestRandomEntry 从外部传入
            // 初始值设为 true，后续由 API 或 Plugin 设置
        }

        /// <summary>
        /// 设置纹章随机开关（外部传入，因为纹章开关可能来自总控配置）
        /// </summary>
        public static void SetCrestRandomEnabled(bool enabled)
        {
            EnableCrestRandom = enabled;
        }

        /// <summary>
        /// 批量应用所有开关（由 API 调用）
        /// </summary>
        public static void Apply(bool crestEnabled, bool skillItemEnabled, bool relicEnabled)
        {
            EnableCrestRandom = crestEnabled;
            EnableSkillItemRandom = skillItemEnabled;
            EnableRelicRandom = relicEnabled;
        }

        // ========== 判断方法（供 TryGetPatch 等使用） ==========
        public static bool ShouldRandomize(SavedItem item)
        {
            if (item == null) return false;
            Type t = item.GetType();

            if (t == typeof(ToolCrest) || t.IsSubclassOf(typeof(ToolCrest)))
                return EnableCrestRandom;

            if (t == typeof(ToolItemSkill) || t == typeof(ToolItem) || item is ToolItemSkill)
                return EnableSkillItemRandom;

            if (typeof(CollectableRelic).IsAssignableFrom(t))
                return EnableRelicRandom;

            return true; // 其他类型默认参与随机
        }

        public static bool ShouldRandomizeCrest()
        {
            return EnableCrestRandom;
        }

        public static bool ShouldRandomizeRelic()
        {
            return EnableRelicRandom;
        }

        public static bool ShouldRandomizeSkillItem(SavedItem item)
        {
            if (item == null) return false;
            Type t = item.GetType();
            return (t == typeof(ToolItemSkill) || t == typeof(ToolItem) || item is ToolItemSkill) && EnableSkillItemRandom;
        }
    }
}