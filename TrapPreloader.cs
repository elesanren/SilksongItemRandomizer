// TrapPreloader.cs
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SilksongItemRandomizer;

public static class TrapPreloader
{
    public enum TrapDifficulty { Beginner, Focused, Overflow }

    // 完整陷阱池
    public static readonly List<string> TrapPool = new()
    {
        "fan_hazard", "spike_cog_1", "spike_cog_2", "spike_cog_3", "spike_cog_4", "spike_cog_5",
        "hot_coal", "lava_area", "falling_lava", "bone_boulder",
        "hunter_landmine", "pilgrim_trap_spike", "wisp_flame_lantern",
        "falling_bell", "shellwood_thorns",
        "coral_lightning_rock", "coral_lightning_orb", "voltgrass",
        "coral_crust_s", "coral_crust_m", "coral_crust_l",
        "coral_spike", "coral_spike_fall", "stomp_spire",
        "rubble_field", "steam_vent", "junk_pipe",
        "slab_trap", "slab_spike_ball", "slab_prob_blade", "hunter_sickle_trap",
        "bilewater_trap", "falling_spike_ball", "swing_trap_small", "swing_trap_spike",
        "dust_trap_spike_plate", "dust_trap_spike_dropper", "mite_trap",
        "organ_spikes", "cradle_spikes",
        "brown_vines", "abyss_tendrils", "void_wave",
        "mill_trap", "craw_chain",
        "frost_marker", "white_thorns", "jelly_egg", "wp_trap_spikes"
    };

    // 场景黑名单
    public static readonly HashSet<string> ExcludedScenes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Belltown_04",
        "Bellshrine", "Bellshrine_02", "Bellshrine_03", "Bellshrine_05",
        "Bone_East_Umbrella",
        "Room_Pinstress",
    };

    public static readonly HashSet<string> NoLavaScenes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Hang_01", "Hang_02", "Hang_10"
    };

    public static readonly HashSet<string> LargeRooms = new(StringComparer.OrdinalIgnoreCase)
    {
        "Song_20", "Arborium_01", "Cog_04", "Song_11", "Song_05", "Song_01", "Coral_35b"
    };

    // 特殊陷阱集
    public static readonly HashSet<string> LargeTraps = new(StringComparer.OrdinalIgnoreCase)
    {
        "fan_hazard", "steam_vent", "mill_trap",
        "spike_cog_2", "spike_cog_3", "spike_cog_1", "spike_cog_4", "spike_cog_5", "voltgrass",
        "junk_pipe"
    };

    public static List<string> TrapPoolNoLava => TrapPool.Where(t => t != LavaTrapId).ToList();

    public static readonly HashSet<string> ThornTraps = new(StringComparer.OrdinalIgnoreCase)
    {
        "brown_vines", "shellwood_thorns", "white_thorns"
    };

    // 小平台（不缩放）
    public static readonly HashSet<string> SmallPlatforms = new(StringComparer.OrdinalIgnoreCase)
    {
        "small_grey_coral_plat",
        "small_red_coral_plat",
        "shell_small"
    };

    public static readonly HashSet<string> LoweredSpikeTraps = new(StringComparer.OrdinalIgnoreCase)
    {
        "pilgrim_trap_spike", "organ_spikes", "cradle_spikes"
    };

    public static readonly HashSet<string> HammerTraps = new(StringComparer.OrdinalIgnoreCase)
    {
        "slab_spike_ball"
    };

    // 常量
    public const string LavaTrapId = "lava_area";
    public const string FallingLavaId = "falling_lava";
    public const float MinDistance = 8f;
    public const float PickupSafeRadius = 5f;
    public const float DoorSafeRadius = 7f;
    public const int LargeTrapRadius = 5;
    public const float LargeTrapYOffset = 1.5f;
    public const int ThornTrapMinWidth = 4;
    public const float SpikeYOffset = 1.8f;
    public const int HammerTrapMinHeight = 7;
    public const float WallPointMatchDistance = 1f;
    public const float DarkThunderChainDistance = 4f;
    public const int MaxDarkThunderCount = 4;

    // 陷阱元数据
    public static readonly Dictionary<string, TrapMeta> TrapMetaDict = new()
    {
        // 无特殊配置的陷阱
        ["fan_hazard"] = new(),
        ["spike_cog_1"] = new(),
        ["spike_cog_2"] = new(),
        ["spike_cog_3"] = new(),
        ["spike_cog_4"] = new(),
        ["spike_cog_5"] = new(),
        ["hot_coal"] = new(),
        ["lava_area"] = new(),
        ["falling_lava"] = new(),
        ["voltgrass"] = new(),
        ["steam_vent"] = new(),
        ["slab_trap"] = new(),
        ["slab_prob_blade"] = new(),
        ["slab_spike_ball"] = new(),
        ["dust_trap_spike_plate"] = new(),
        ["dust_trap_spike_dropper"] = new(),
        ["mite_trap"] = new(),
        ["organ_spikes"] = new(),
        ["cradle_spikes"] = new(),
        ["mill_trap"] = new(),
        ["coral_lightning_rock"] = new(),
        ["coral_crust_s"] = new(),
        ["coral_crust_m"] = new(),
        ["coral_crust_l"] = new(),
        ["abyss_tendrils"] = new(),
        ["bone_boulder"] = new(),
        ["void_wave"] = new(),
        ["coral_lightning_orb"] = new(),

        // 有配置的陷阱
        ["wisp_flame_lantern"] = new(new() { ["breakable_on"] = "True" }),
        ["falling_bell"] = new(new() { ["bell_reset"] = "1" }),
        ["shellwood_thorns"] = new(new() { ["vines_hurt_player"] = "True" }),
        ["brown_vines"] = new(new() { ["vines_hurt_player"] = "True" }),
        ["white_thorns"] = new(new() { ["vines_hurt_player"] = "True" }),
        ["junk_pipe"] = new(new() { ["junk_pipe_terrain"] = "True" }),
        ["frost_marker"] = new(new() { ["frost_speed"] = "10" }),
        ["jelly_egg"] = new(new() { ["egg_regen"] = "-1" }),
        ["wp_trap_spikes"] = new(
            new() { ["wp_spikes_up"] = "True", ["wp_spikes_delay"] = "0", ["wp_spikes_speed"] = "1" },
            positionOffset: new Vector3(0f, 5f, 0f)
        ),

        // 需要触发器的陷阱
        ["pilgrim_trap_spike"] = new(needsActivator: true, activatorId: "pilgrim_trap_wire"),
        ["rubble_field"] = new(needsActivator: true, activatorId: "slab_pressure_plate", positionOffset: new Vector3(0f, 0.5f, 0f)),
        ["bilewater_trap"] = new(needsActivator: true, activatorId: "slab_pressure_plate", positionOffset: new Vector3(10f, 0f, 0f)),
        ["falling_spike_ball"] = new(needsActivator: true, activatorId: "slab_pressure_plate"),
        ["swing_trap_small"] = new(needsActivator: true, activatorId: "slab_pressure_plate", positionOffset: new Vector3(0f, 8f, 0f)),
        ["swing_trap_spike"] = new(needsActivator: true, activatorId: "slab_pressure_plate", positionOffset: new Vector3(0f, 15f, 0f)),
        ["hunter_landmine"] = new(needsActivator: true, activatorId: "hunter_trap_plate", positionOffset: new Vector3(0f, -1f, 0f), positionRotate: new Vector3(0f, 0f, 180f)),
        ["hunter_sickle_trap"] = new(needsActivator: true, activatorId: "hunter_trap_plate", positionOffset: new Vector3(0f, 6f, 0f)),
        ["craw_chain"] = new(needsActivator: true, activatorId: "trigger_zone"),
        ["coral_spike"] = new(needsActivator: true, activatorId: "trigger_zone"),
        ["coral_spike_fall"] = new(needsActivator: true, activatorId: "trigger_zone"),
        ["stomp_spire"] = new(needsActivator: true, activatorId: "trigger_zone"),
    };

    // 功能分类（墙壁已禁用）
    public static readonly Dictionary<string, List<string>> TrapCategories = new()
    {
        ["暗雷"] = new() { "hunter_landmine", "dust_trap_spike_plate", "slab_trap", "slab_prob_blade", "hunter_sickle_trap" },
        ["跳跳乐"] = new() { "spike_cog_1", "spike_cog_2", "spike_cog_3", "spike_cog_4", "spike_cog_5" },
        ["平台类"] = new() { "spike_cog_1", "spike_cog_2", "spike_cog_3", "spike_cog_4", "spike_cog_5", "fan_hazard", "mill_trap" },
        ["尖刺类"] = new() { "wp_trap_spikes", "organ_spikes", "coral_spike", "pilgrim_trap_spike", "cradle_spikes", "slab_spike_ball" },
        ["墙壁"] = new(),
        ["天花板"] = new() { "falling_bell", "bone_boulder", "dust_trap_spike_dropper", "falling_lava", "coral_lightning_orb", "coral_lightning_rock", "steam_vent", "junk_pipe" },
        ["障碍物"] = new()
        {
            "coral_crust_s", "coral_crust_m", "coral_crust_l",
            "hot_coal",
            "march_pogo", "bounce_bloom", "wisp_bounce_pod",
            "sprintmaster_pod", "swap_bounce_pod", "celeste_bumper"
        },
        ["装饰物"] = new()
        {
            "clover_pod", "abyss_pod",
            "lilypad", "cradle_nut",
            "karaka_statue", "judge_statue", "clover_statue",
            "shard_statue_1", "shard_statue_2", "shard_statue_3",
            "shard_statue_4", "shard_statue_5", "flick_statue",
            "fayforn_npc", "snow_chunk", "float_crystal",
            "white_palace_fly", "pond_skipper_body", "winged_lifeseed",
            "life_pustule", "bounce_flea", "dodge_flea",
            "hornet_cocoon", "bellbeast_child",
            "bell_s", "bell_l", "bell_lock",
            "greymoor_balloon_small", "greymoor_balloon_mid", "greymoor_balloon_large",
            "swamp_mosquito", "swamp_mosquito_skinny", "mothleaf",
            "imoba", "garpid",
            "crystal_drifter", "crystal_drifter_giant",
            "stilkin", "stilkin_trapper", "dock_bomber"
        },
        ["触发型"] = new() { "swing_trap_small", "coral_spike_fall", "stomp_spire", "falling_spike_ball", "rubble_field", "mite_trap", "bilewater_trap", "craw_chain", "swing_trap_spike" },
        ["追逐型"] = new() { "wisp_flame_lantern" },
        ["场景伤害"] = new() { "abyss_tendrils", "voltgrass", "void_wave", "frost_marker" },

        ["跳跳乐1"] = new()
        {
            "march_pogo", "bounce_bloom", "wisp_bounce_pod", "sprintmaster_pod",
            "swap_bounce_pod", "clover_pod", "abyss_pod", "celeste_bumper",
            "lilypad", "cradle_nut",
            "karaka_statue", "judge_statue", "clover_statue",
            "shard_statue_1", "shard_statue_2", "shard_statue_3",
            "shard_statue_4", "shard_statue_5", "flick_statue",
            "fayforn_npc", "snow_chunk", "float_crystal",
            "white_palace_fly", "pond_skipper_body", "winged_lifeseed",
            "life_pustule", "bounce_flea", "dodge_flea",
            "hornet_cocoon", "bellbeast_child",
            "bell_s", "bell_l", "bell_lock",
            "greymoor_balloon_small", "greymoor_balloon_mid", "greymoor_balloon_large",
            "swamp_mosquito", "swamp_mosquito_skinny", "mothleaf",
            "imoba", "garpid",
            "crystal_drifter", "crystal_drifter_giant","stilkin", "stilkin_trapper", "dock_bomber"
        },

        ["平台类1"] = new()
        {
            "coral_plat_float", "small_grey_coral_plat", "small_red_coral_plat",
            "mid_red_coral_plat", "large_red_coral_plat",
            "abyss_plat_mid", "abyss_plat_wide",
            "deepnest_platform_01", "deepnest_platform_02",
            "deepnest_platform_03", "deepnest_platform_04", "deepnest_platform_05",
            "hive_platform_01", "hive_platform_02", "hive_platform_03",
            "shell_small", "shell_mid", "shell_large",
        },
    };

    // 难度配额
    private static readonly Dictionary<TrapDifficulty, Dictionary<string, int>> Quotas = new()
    {
        [TrapDifficulty.Beginner] = new()
        {
            ["暗雷"] = 2,
            ["跳跳乐"] = 1,
            ["平台类"] = 2,
            ["尖刺类"] = 2,
            ["墙壁"] = 0,
            ["天花板"] = 5,
            ["障碍物"] = 4,
            ["装饰物"] = 3,
            ["触发型"] = 5,
            ["追逐型"] = 0,
            ["场景伤害"] = 0
        },
        [TrapDifficulty.Focused] = new()
        {
            ["暗雷"] = 4,
            ["跳跳乐"] = 3,
            ["平台类"] = 4,
            ["尖刺类"] = 4,
            ["墙壁"] = 0,
            ["天花板"] = 6,
            ["障碍物"] = 5,
            ["装饰物"] = 4,
            ["触发型"] = 6,
            ["追逐型"] = 1,
            ["场景伤害"] = 1
        },
        [TrapDifficulty.Overflow] = new()
        {
            ["暗雷"] = 6,
            ["跳跳乐"] = 5,
            ["平台类"] = 6,
            ["尖刺类"] = 6,
            ["墙壁"] = 0,
            ["天花板"] = 8,
            ["障碍物"] = 6,
            ["装饰物"] = 5,
            ["触发型"] = 8,
            ["追逐型"] = 2,
            ["场景伤害"] = 2
        }
    };

    public static Dictionary<string, int> GetCategoryQuotas(TrapDifficulty difficulty)
    {
        return Quotas.TryGetValue(difficulty, out var quota) ? new Dictionary<string, int>(quota) : new Dictionary<string, int>(Quotas[TrapDifficulty.Beginner]);
    }

    // 分类顺序
    public static readonly string[] CategoryOrder =
    {
        "暗雷", "跳跳乐", "平台类", "尖刺类", "墙壁", "天花板", "障碍物", "装饰物", "触发型", "追逐型", "场景伤害"
    };

    public static double GetFrostProbability(TrapDifficulty difficulty) =>
        difficulty switch
        {
            TrapDifficulty.Focused => 0.10,
            TrapDifficulty.Overflow => 0.20,
            _ => 0.0
        };
}

public class TrapMeta
{
    public Dictionary<string, string> Config { get; set; }
    public bool NeedsActivator { get; set; }
    public string ActivatorId { get; set; }
    public Vector3 PositionOffset { get; set; }
    public Vector3 PositionRotate { get; set; }

    public TrapMeta(
        Dictionary<string, string> config = null,
        bool needsActivator = false,
        string activatorId = null,
        Vector3? positionOffset = null,
        Vector3? positionRotate = null)
    {
        Config = config ?? new Dictionary<string, string>();
        NeedsActivator = needsActivator;
        ActivatorId = activatorId;
        PositionOffset = positionOffset ?? Vector3.zero;
        PositionRotate = positionRotate ?? Vector3.zero;
    }
}