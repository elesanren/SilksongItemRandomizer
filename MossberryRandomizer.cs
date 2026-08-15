// MossberryRandomizer.cs - 苔莓随机化拦截
// 机制（来自 areamoss prefab 反编译）：
//   枝头苔莓 moss_berry_fruit（场景实例）
//     - PersistentBoolItem（场景填充，判定是否已拾取）
//     - PlayMakerFSM "Control"（Pause→Init→Idle；Idle 收到 DAMAGED → Break）
//     - DroppableItem items=[Mossberry.asset]
//   打落：moss_berry_fruit Break 状态 FlingObject 抛落，生成可拾取物 Mossberry Pickup
//   拾取：Mossberry Pickup FSM Collect 状态 -> CollectableItemCollect(Item=Mossberry)
//         -> Mossberry.Collect(1) -> CollectableItemManager.AddItem(Mossberry,1)（计数+1、弹UI）
//   重生根源：CollectableItem.CanGetMore() = IsConsumable() || !IsAtMax()（CollectableItem.cs:331）
//             苔莓可售卖（consumable 恒 true）-> 恒可重生 -> 场景重进苔莓重新激活
//
// 本模块两个功能：
//   1. 拾取拦截：CollectableItem.Collect 命中 Mossberry -> 跳过原生 Collect，改发随机奖励，并记录当前房间
//   2. 房间消失：场景加载时，若该场景已记录过苔莓，隐藏场景内全部 moss_berry_fruit / Mossberry Pickup
//      （等同原生"已捡过"效果：枝头不再显示苔莓）
using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SilksongItemRandomizer
{
    /// <summary>苔莓随机化：拦截 Mossberry 拾取为随机奖励，并做房间级"已捡"持久化。</summary>
    public static class MossberryRandomizer
    {
        public const string MossberryName = "Mossberry";

        private static bool _isGiving = false;

        private static readonly HashSet<string> HideNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "moss_berry_fruit", "Mossberry Pickup"
        };

        /// <summary>当前场景名（无 GameManager 时回退到 active scene）。</summary>
        public static string CurrentSceneName()
        {
            try
            {
                var gm = GameManager.instance;
                if (gm != null && !string.IsNullOrEmpty(gm.sceneName))
                    return gm.sceneName;
            }
            catch { }
            var scene = SceneManager.GetActiveScene();
            return scene.IsValid() ? scene.name : "";
        }

        public static bool IsRoomCollected(string sceneName)
        {
            var rooms = Plugin.SaveData?.MossberryCollectedRooms;
            return rooms != null && !string.IsNullOrEmpty(sceneName) && rooms.Contains(sceneName);
        }

        public static void MarkRoomCollected(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return;
            var rooms = Plugin.SaveData?.MossberryCollectedRooms;
            if (rooms == null) return;
            if (rooms.Add(sceneName))
                Plugin.SaveGlobalData(); // 去抖落盘
        }

        /// <summary>按顺序从预生成映射取苔莓奖励（moss:01~06），命中则递增计数器。</summary>
        private static IRandomReward ResolveSequential()
        {
            var save = Plugin.SaveData;
            if (save == null) return null;
            var r = PreGeneratedMap.ResolveSequentialReward("moss", save.MossSeq + 1);
            if (r != null) { save.MossSeq++; Plugin.SaveGlobalData(); }
            return r;
        }

        /// <summary>拾取拦截：CollectableItem.Collect 命中 Mossberry 时改发随机奖励。</summary>
        [HarmonyPatch(typeof(CollectableItem), "Collect", new Type[] { typeof(int), typeof(bool) })]
        public static class CollectableItem_Collect_Patch
        {
            [HarmonyPrefix]
            private static bool Prefix(CollectableItem __instance, ref bool __runOriginal)
            {
                try
                {
                    if (_isGiving) return true;
                    if (TryGetPatch.BypassRandom) return true; // ★ 自发发放（物品奖励池里的苔莓浆果/丝矛保底等）：放行原生 Collect，不被误伤
                    if (__instance == null) return true;
                    if (!SilksongItemRandomizerAPI.IsEnabled()) return true;
                    if (!string.Equals(__instance.name, MossberryName, StringComparison.OrdinalIgnoreCase))
                        return true;

                    // ★ 命中苔莓：跳过原生 Collect（不给苔莓），改为按序映射奖励 + 记录房间
                    string scene = CurrentSceneName();
                    var reward = ResolveSequential();
                    if (reward == null)
                        reward = ItemRandomizer.GetRandomReward();
                    if (reward == null) return true;

                    _isGiving = true;
                    try
                    {
                        reward.Give();
                        ItemRandomizer.AddGivenCount(reward.Id);
                        ItemRandomizer.RecordMapping("item:Mossberry", "reward:" + reward.Id);
                        RecentItemsUI.AddItem(reward);
                    }
                    finally
                    {
                        _isGiving = false;
                    }

                    MarkRoomCollected(scene);
                    __runOriginal = false;
                    return false; // 跳过原生 Collect
                }
                catch (Exception ex)
                {
                    try { Plugin.Log.LogError($"[Mossberry] 拦截异常: {ex}"); } catch { }
                    return true;
                }
            }
        }

        /// <summary>场景加载：已记录的房间隐藏全部苔莓（枝头果实 + 可拾取物）。</summary>
        public static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            try
            {
                if (scene == null || string.IsNullOrEmpty(scene.name)) return;
                if (!IsRoomCollected(scene.name)) return;

                int hidden = 0;
                var roots = scene.GetRootGameObjects();
                foreach (var root in roots)
                {
                    if (root == null) continue;
                    hidden += HideMatchingRecursive(root.transform, HideNames);
                }
                if (hidden > 0)
                    Plugin.Log.LogInfo($"[Mossberry] 场景 {scene.name} 已捡过苔莓，隐藏 {hidden} 个苔莓对象");
            }
            catch (Exception ex)
            {
                try { Plugin.Log.LogError($"[Mossberry] OnSceneLoaded 异常: {ex}"); } catch { }
            }
        }

        private static int HideMatchingRecursive(Transform t, HashSet<string> names)
        {
            int count = 0;
            if (t == null) return 0;
            if (names.Contains(t.name))
            {
                t.gameObject.SetActive(false);
                count++;
            }
            for (int i = 0; i < t.childCount; i++)
                count += HideMatchingRecursive(t.GetChild(i), names);
            return count;
        }
    }

    /// <summary>
    /// 丝轴碎片世界触发点接管：
    /// 游戏原生在世界场景放置 7+ 个 ID=="Silk Spool" 的 PrefabCollectable 触发点，
    /// 玩家到达时场景 FSM 调用该资产的 Get()/TryGet() 发放丝轴碎片。
    /// 本补丁拦截 PrefabCollectable.Get(bool)，当实例名为 "Silk Spool" 且随机器启用时，
    /// 跳过原生发放，改为给予随机奖励（复用随机池），实现"触发点随机化"。
    ///
    /// 隔离说明：
    /// - virt:SpoolPart 虚拟奖励发放（NativePickupGiver.GiveSpoolPart）时设置 Bypass=true，
    ///   放行原生 Get，避免把"奖励池发放的真丝轴"再随机成别的物品造成循环。
    /// - TryGetPatch 已拦截 TryGet 路径；本补丁兜底拦截直接调用 Get() 的世界触发路径。
    /// </summary>
    [HarmonyPatch(typeof(PrefabCollectable), "Get", new Type[] { typeof(bool) })]
    public static class SpoolPartPatch
    {
        /// <summary>旁路标志：虚拟奖励发放时置 true，放行原生丝轴发放。</summary>
        public static bool Bypass = false;

        private static bool _isGiving = false;

        [HarmonyPrefix]
        private static bool Prefix(PrefabCollectable __instance, ref bool __runOriginal)
        {
            try
            {
                if (_isGiving) return true;
                if (Bypass) return true;
                if (SilkSpoolState.IsSelfGiving) return true;
                if (!SilksongItemRandomizerAPI.IsEnabled()) return true;
                if (__instance == null) return true;

                bool isSpool = string.Equals(__instance.name, "Silk Spool", StringComparison.OrdinalIgnoreCase);
                if (!isSpool) return true;

                // ★ 丝轴世界点触发：跳过原生发放，给随机
                // 标记静默窗口：后续被驱动的 Silk Spool UI 动画流程不播动画/不加碎片/不加上限，但正常走完
                SilkSpoolState.MarkNativeIntercept(5f);
                var reward = ItemRandomizer.GetRandomReward();
                if (reward == null) return true;

                _isGiving = true;
                try
                {
                    reward.Give();
                    ItemRandomizer.AddGivenCount(reward.Id);
                    ItemRandomizer.RecordMapping("item:Silk Spool", "reward:" + reward.Id);
                    RecentItemsUI.AddItem(reward);
                }
                finally
                {
                    _isGiving = false;
                }
                __runOriginal = false;
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[SpoolPart] 异常: {ex}");
                return true;
            }
        }
    }
}