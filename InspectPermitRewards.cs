// InspectPermitRewards.cs
using System.Collections.Generic;
using UnityEngine;

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 权限类 inspect 地点（总表 Lore 行 - lore 白名单）的权限物：
    /// 每个地点一个"允许走原生流程"的许可放进随机池（记录二），
    /// 玩家随机获得后该地点 inspect 放行原生流程；inspect 每地只触发一次随机（记录一）。
    /// 车站权限（Station_Unlocked*）已有独立机制，不在本表。
    /// </summary>
    public static class InspectPermitRewards
    {
        public readonly struct InspectPermitEntry
        {
            public readonly string Scene;
            public readonly string Name;
            public InspectPermitEntry(string scene, string name)
            {
                Scene = scene;
                Name = name;
            }
        }

        /// <summary>权限类地点清单（scene|name，与 lore 白名单互补，仅保留车站收费机 26 处）
        /// 其余权限类 inspect 地点已移除随机化，回归原生交互流程。</summary>
        public static readonly InspectPermitEntry[] Permits =
        {
            new InspectPermitEntry("Arborium_Tube", "tube_toll_machine"),
            new InspectPermitEntry("Belltown_basement", "Bellway Toll Machine"),
            new InspectPermitEntry("Bellway_02", "Bellway Toll Machine"),
            new InspectPermitEntry("Bellway_03", "Bellway Toll Machine"),
            new InspectPermitEntry("Bellway_04", "Bellway Toll Machine"),
            new InspectPermitEntry("Bellway_08", "Bellway Toll Machine"),
            new InspectPermitEntry("Bellway_Aqueduct", "Bellway Toll Machine"),
            new InspectPermitEntry("Bellway_City", "Bellway Toll Machine"),
            new InspectPermitEntry("Bellway_City", "Bellway Toll Machine (1)"),
            new InspectPermitEntry("Bellway_City", "tube_toll_machine"),
            new InspectPermitEntry("Bellway_Shadow", "Bellway Toll Machine"),
            new InspectPermitEntry("Bone_East_10", "toll door interactible"),
            new InspectPermitEntry("Hang_06b", "tube_toll_machine"),
            new InspectPermitEntry("Shellwood_19", "Bellway Toll Machine"),
            new InspectPermitEntry("Slab_06", "Bellway Toll Machine"),
            new InspectPermitEntry("Song_01b", "tube_toll_machine"),
            new InspectPermitEntry("Song_28", "Toll_machine_silk_ration"),
            new InspectPermitEntry("Song_29", "Toll_machine_silk_ration"),
            new InspectPermitEntry("Song_Enclave_Tube", "tube_toll_machine"),
            new InspectPermitEntry("Tube_Hub", "tube_toll_machine"),
            new InspectPermitEntry("Under_01b", "Understore Toll Bench"),
            new InspectPermitEntry("Under_01b", "Understore Toll Bench (1)"),
            new InspectPermitEntry("Under_08", "Understore Toll Bench"),
            new InspectPermitEntry("Under_08", "Understore Toll Bench (1)"),
            new InspectPermitEntry("Under_08", "Understore Toll Bench (2)"),
            new InspectPermitEntry("Under_22", "tube_toll_machine"),
        };

        /// <summary>运行时应答：该地点是否为权限类（有权限物）</summary>
        public static bool IsPermitObject(string scene, string name)
            => !string.IsNullOrEmpty(scene) && !string.IsNullOrEmpty(name) && PermitKeys.Contains(scene + "|" + name);

        private static readonly HashSet<string> PermitKeys = BuildPermitKeys();

        private static HashSet<string> BuildPermitKeys()
        {
            var set = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var e in Permits)
                set.Add(e.Scene + "|" + e.Name);
            return set;
        }
    }
}
