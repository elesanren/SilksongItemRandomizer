using HarmonyLib;
using HutongGames.PlayMaker.Actions;
using HutongGames.PlayMaker;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System;
using UnityEngine;
using UnityEngine.SceneManagement;

// SkillRegionRandomizePatch.cs - 技能获得地区演出平替（与教堂演出同模式）
// 背景：技能获得演出（Weaver "Inspection" FSM 模板）中，技能弹窗 = Affix action
//   HutongGames.PlayMaker.Actions.SpawnSkillGetMsg：OnEnter 调
//   SkillGetMsg.Spawn(MsgPrefab.GetComponent<SkillGetMsg>(), Skill.Value as ToolItemSkill, Finish)，
//   弹窗为自实例化自管理（afterMsg=Finish 收尾）。随机模式下在技能弹窗前阻断，平替为随机奖励，
//   等同平替掉该地区"给技能"的触发点。
// 技能弹窗（SpawnSkillGetMsg）所在场景全集（同一 "Inspection" 模板，E:\fsm_all 全量检索）：
//   mosstown_02(织女) / bone_east_05 / crawl_05 / under_18 / greymoor_22 / shellwood_10 /
//   slab_10b / abyss_08 / cradle_03_destroyed / organ_01 / memory_first_sinner
//   （localpoolprefabs_assets_areaweaver 为模板池非场景，排除）
// 启用列表 SkillScenes 决定实际平替范围，可随时增删。

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 技能获得地区平替：阻断技能弹窗（SpawnSkillGetMsg），改为触发随机奖励，
    /// 后续演出状态（Heal 回血 / Fade / End）继续原生执行。
    /// 注册：SilksongItemRandomizerAPI.ToggleablePatchTypes。
    /// </summary>
    [HarmonyPatch(typeof(SpawnSkillGetMsg), "OnEnter")]
    public static class SkillRegionRandomizePatch
    {
        /// <summary>启用平替的技能获得地区场景（"Inspection" 模板全场景：织女/村落/深渊等 11 处技能演出
        /// + 丝忆神社（belltown_shrine Boss战后 Get Needolin）与 weave_10 织女演出——两者能力弹窗同走
        /// SpawnPowerUpGetMsg，一并纳入
        /// + 5 处 UI Msg 全屏弹窗演出（织布机 Brolly / DJ 二段跳 / 蓄力斩 / 蜗牛萨满深邃挽歌 / 幻兽歌）——
        /// 弹窗走 CreateUIMsgGetItem，字段写入走 SetPlayerDataBool/SetPlayerDataVariable）。
        /// internal：PreGeneratedMap.EventKeys 引用收集 event: 预分配键（单一事实来源）。</summary>
        internal static readonly string[] SkillScenes = {
            "mosstown_02", "bone_east_05", "crawl_05", "under_18", "greymoor_22", "shellwood_10",
            "slab_10b", "abyss_08", "cradle_03_destroyed", "organ_01", "memory_first_sinner",
            "belltown_shrine", "weave_10",
            "bone_east_umbrella", "peak_08b", "room_pinstress", "tut_04", "bellway_centipede_arena"
        };

        // UI Msg 全屏弹窗演出场景（没有 SpawnSkillGetMsg/SpawnPowerUpGetMsg，获得演出 =
        //   演出动画 → Msg 状态（CreateUIMsgGetItem 激活官方 UI Msg 弹窗 + 写能力字段）→
        //   "GET ITEM MSG END" 事件 → 收尾状态（Fade Up / Time Passes 等）。
        // 拦截点 = CreateUIMsgGetItem.OnEnter：阻断弹窗并自行补发收尾事件，演出正常走完。
        // internal：PreGeneratedMap.EventKeys 引用收集 event: 预分配键。
        internal static readonly string[] UIMsgScenes = {
            "bone_east_umbrella", "peak_08b", "room_pinstress", "tut_04", "bellway_centipede_arena",
            // 丝之心梦境（钟兽 Boss 战后）：UI Prompt 状态 CreateUIMsgGetItem Item="SilkHeart"
            // 全屏弹窗 → GET ITEM MSG END → End Scene（写 defeatedBellBeast + 回 Bone_05，
            // 均不在拦截范围，原样保留）。lacelower/wardboss 两同款梦境无弹窗，不纳入。
            "memory_silk_heart_bellbeast",
            // 猎人日志（halfway_01 Nuu 剧情赠送）：Dialogue FSM "Journal" 状态 =
            // EndDialogue → SetPlayerDataBool(hasJournal=true) → CallStaticMethod(CheckJournal
            // Achievements，空方法) → Wait → CreateUIMsgGetItem(Item="Journal") → 等 GET ITEM MSG END。
            // 注意：Nuu 对话可重复进入 Journal 状态（玩家抽到日志前每次对话都走），拦截逻辑
            // 必须幂等（已平替只拦不发），hasJournal 写入由随机池 GiveJournal 负责。
            // 字段写入在弹窗 action 之前执行，故另由 Prefix_SetPdBool 的 halfway 分支阻断。
            "halfway_01"
        };

        // 官方 UI Msg 弹窗播放完成时的收尾事件（演出 FSM Msg 状态的 transition 名，5 场景一致）
        private const string GetItemMsgEnd = "GET ITEM MSG END";

        // 技能演出中会写入的 pd 字段全集（仅拦截这些能力字段的写入，不影响演出其他 SetPlayerDataBool）
        private static readonly System.Collections.Generic.HashSet<string> SkillFields = new(System.StringComparer.OrdinalIgnoreCase)
        {
            "hasNeedleThrow", "hasThreadSphere", "hasSilkCharge", "hasSilkBomb", "hasSilkBossNeedle", "hasParry",
            "hasHarpoonDash", "hasNeedolin", "hasDash", "hasBrolly", "hasDoubleJump", "hasChargeSlash",
            "hasSuperJump", "hasWalljump", "HasSeenEvaHeal", "hasNeedolinMemoryPowerup", "hasFastTravelTeleport"
        };

        // 已平替场景持久化于 Plugin.SaveData.SkillRandomizedRegions（跨会话生效），
        // 无独立会话状态，加载存档即恢复。

        public static void ResetHandledScenes()
        {
            if (Plugin.SaveData?.SkillRandomizedRegions != null)
                Plugin.SaveData.SkillRandomizedRegions.Clear();
        }

        /// <summary>该场景是否已被平替（持久化标记：已平替后跨会话不再触发）</summary>
        private static bool IsRegionRandomized(string scene)
        {
            return Plugin.SaveData?.SkillRandomizedRegions?.Contains(scene) ?? false;
        }

        private static bool IsSkillScene(string sceneName)
        {
            foreach (var s in SkillScenes)
                if (string.Equals(sceneName, s, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        private static bool IsUiMsgScene(string sceneName)
        {
            foreach (var s in UIMsgScenes)
                if (string.Equals(sceneName, s, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        [HarmonyPrefix]
        private static bool Prefix(SpawnSkillGetMsg __instance)
        {
            try
            {
                if (!SilksongItemRandomizerAPI.IsEnabled())
                    return true;

                string scene = SceneManager.GetActiveScene().name;
                if (scene == null || !IsSkillScene(scene))
                    return true;

                var skill = __instance.Skill?.Value as ToolItemSkill;
                Plugin.Log.LogInfo($"[技能随机] 技能获得演出拦截: 场景 {scene}, 技能 {skill?.name ?? "空"}");

                // 持久化标记：该场景已平替（已发随机奖励）则跨会话只阻断弹窗不重复发放；
                // 演出自身 Collected Check 兜底，防重复进入演出。
                if (!IsRegionRandomized(scene))
                {
                    GiveRandomReward(scene, skill?.name);
                    if (Plugin.SaveData != null)
                    {
                        Plugin.SaveData.SkillRandomizedRegions.Add(scene);
                        Plugin.SaveGlobalData(); // 去抖落盘：持久化标记必须写盘，跨会话生效
                        Plugin.Log.LogInfo($"[技能随机] 场景 {scene} 已平替，持久化标记并落盘");
                    }
                }
                else
                {
                    Plugin.Log.LogInfo($"[技能随机] 场景 {scene} 已平替过（持久化），仅阻断弹窗");
                }

                // 阻断原生弹窗并正常收尾（与 SpawnSkillGetMsg 的 afterMsg=Finish 一致）
                __instance.Finish();
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[技能随机] 拦截异常，放行原生: {ex}");
                return true;
            }
        }

        /// <summary>
        /// 能力弹窗平替：与技能同逻辑。移动能力（疾跑/爬墙等）的 Inspection 演出
        /// 弹窗走 SpawnPowerUpGetMsg（PowerUpGetMsg.Spawn），与技能 SpawnSkillGetMsg
        /// 是不同 action——只拦技能漏掉能力，本补丁补上同一白名单场景的能力弹窗拦截。
        /// </summary>
        [HarmonyPatch(typeof(SpawnPowerUpGetMsg), "OnEnter")]
        [HarmonyPrefix]
        private static bool Prefix_PowerUp(SpawnPowerUpGetMsg __instance)
        {
            try
            {
                if (!SilksongItemRandomizerAPI.IsEnabled())
                    return true;

                string scene = SceneManager.GetActiveScene().name;
                if (scene == null || !IsSkillScene(scene))
                    return true;

                var powerUp = __instance.PowerUp?.Value;
                Plugin.Log.LogInfo($"[技能随机] 能力弹窗拦截: 场景 {scene}, PowerUp {powerUp}");

                if (!IsRegionRandomized(scene))
                {
                    GiveRandomReward(scene, powerUp?.ToString());
                    if (Plugin.SaveData != null)
                    {
                        Plugin.SaveData.SkillRandomizedRegions.Add(scene);
                        Plugin.SaveGlobalData();
                        Plugin.Log.LogInfo($"[技能随机] 场景 {scene} 已平替（能力弹窗），持久化标记并落盘");
                    }
                }
                else
                {
                    Plugin.Log.LogInfo($"[技能随机] 场景 {scene} 已平替过（持久化），仅阻断能力弹窗");
                }

                __instance.Finish();
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[技能随机] 拦截能力弹窗异常，放行原生: {ex}");
                return true;
            }
        }

        /// <summary>
        /// UI Msg 全屏弹窗平替：织布机（Brolly）/ DJ（二段跳）/ 蓄力斩 / 蜗牛萨满（深邃挽歌）/
        /// 幻兽歌 五个演出的获得弹窗均走 CreateUIMsgGetItem.OnEnter（激活官方 UI Msg prefab）。
        /// 阻断后演出 FSM 停在 Msg 状态等待 "GET ITEM MSG END"（原由弹窗播放完成发送）——
        /// 自行在该 FSM 的 GameObject 上重建 EventRegister 订阅并补发事件，演出正常收尾
        /// （Fade Up / Time Passes 等，演出动画与后续状态原样执行）。
        /// </summary>
        [HarmonyPatch(typeof(CreateUIMsgGetItem), "OnEnter")]
        [HarmonyPrefix]
        private static bool Prefix_CreateUIMsg(CreateUIMsgGetItem __instance)
        {
            try
            {
                if (!SilksongItemRandomizerAPI.IsEnabled())
                    return true;

                string scene = SceneManager.GetActiveScene().name;
                if (scene == null || !IsUiMsgScene(scene))
                    return true;

                Plugin.Log.LogInfo($"[技能随机] UI Msg 全屏弹窗拦截: 场景 {scene}");

                if (!IsRegionRandomized(scene))
                {
                    GiveRandomReward(scene, "UIMsg");
                    if (Plugin.SaveData != null)
                    {
                        Plugin.SaveData.SkillRandomizedRegions.Add(scene);
                        Plugin.SaveGlobalData();
                        Plugin.Log.LogInfo($"[技能随机] 场景 {scene} 已平替（UI Msg 弹窗），持久化标记并落盘");
                    }
                }
                else
                {
                    Plugin.Log.LogInfo($"[技能随机] 场景 {scene} 已平替过（持久化），仅阻断 UI Msg 弹窗");
                }

                // 阻断原生弹窗激活；弹窗 prefab 实例可能在 FSM Awake 时已实例化（保持不激活即可）。
                // 补发收尾事件：弹窗被拦后无人发送 "GET ITEM MSG END"，演出会卡在 Msg 状态。
                try
                {
                    GameObject owner = __instance.Owner;
                    if (owner != null)
                    {
                        EventRegister.GetRegisterGuaranteed(owner, GetItemMsgEnd);
                        EventRegister.SendEvent(GetItemMsgEnd);
                    }
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning($"[技能随机] UI Msg 演出收尾事件发送异常: {ex}");
                }
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[技能随机] 拦截 UI Msg 弹窗异常，放行: {ex}");
                return true;
            }
        }

        // 丝之心梦境无弹窗变体（蕾丝塔/守卫 Boss 战后）：Memory Control FSM 与钟兽变体
        // 同构（Init→Begin→To White→Memory Msg→To Black→End Scene）但没有 UI Prompt 弹窗状态。
        // "原弹窗时刻" = To Black 状态（渐黑收尾）——拦截该状态的 Wait.OnEnter 发随机奖励，
        // 放行 Wait 走完 FINISHED → End Scene（写 defeatedXXX + 回城，原样保留）。
        // internal：PreGeneratedMap.EventKeys 引用收集 event: 预分配键。
        internal static readonly string[] MemoryNoPopupScenes = {
            "memory_silk_heart_lacetower", "memory_silk_heart_wardboss"
        };

        /// <summary>丝之心梦境（无弹窗变体）拦截：To Black 状态 Wait 时发随机并持久化，放行收尾。</summary>
        [HarmonyPatch(typeof(HutongGames.PlayMaker.Actions.Wait), "OnEnter")]
        [HarmonyPrefix]
        private static bool Prefix_MemoryControlWait(HutongGames.PlayMaker.Actions.Wait __instance)
        {
            try
            {
                if (!SilksongItemRandomizerAPI.IsEnabled())
                    return true;

                string scene = SceneManager.GetActiveScene().name;
                if (scene == null || !MemoryNoPopupScenes.Contains(scene))
                    return true;
                if (__instance.Fsm == null
                    || !string.Equals(__instance.Fsm.Name, "Memory Control", StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(__instance.Fsm.ActiveStateName, "To Black", StringComparison.OrdinalIgnoreCase))
                    return true;

                Plugin.Log.LogInfo($"[技能随机] 丝之心梦境拦截(无弹窗变体): 场景 {scene}");

                if (!IsRegionRandomized(scene))
                {
                    GiveRandomReward(scene, "MemoryControl");
                    if (Plugin.SaveData != null)
                    {
                        Plugin.SaveData.SkillRandomizedRegions.Add(scene);
                        Plugin.SaveGlobalData();
                        Plugin.Log.LogInfo($"[技能随机] 场景 {scene} 已平替（丝之心梦境），持久化标记并落盘");
                    }
                }
                else
                {
                    Plugin.Log.LogInfo($"[技能随机] 场景 {scene} 已平替过（持久化），不重复发放");
                }

                return true; // 放行：Wait 走完 FINISHED → End Scene 原样收尾
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[技能随机] 拦截丝之心梦境异常，放行: {ex}");
                return true;
            }
        }

        // 丝之心本体数值阻断：silkRegenMax +1 由 C# 侧物品拾取逻辑调用
        // HeroController.AddToMaxSilkRegen 写入（三场景 FSM dump 均无此字符串），
        // 与演出拦截点无关。三个丝之心梦境场景内一律阻断原生写入，
        // 丝之心数值只由随机池 GiveSilkHeart（AddToMaxSilkRegen）提供。
        [HarmonyPatch(typeof(HeroController), "AddToMaxSilkRegen")]
        [HarmonyPrefix]
        private static bool Prefix_AddToMaxSilkRegen()
        {
            try
            {
                if (!SilksongItemRandomizerAPI.IsEnabled())
                    return true;
                // 随机池自身发放丝之心时放行
                if (ItemRandomizer.GrantingSilkHeartFromPool)
                    return true;

                string scene = SceneManager.GetActiveScene().name;
                if (scene == null)
                    return true;
                foreach (var s in MemoryNoPopupScenes)
                    if (string.Equals(scene, s, StringComparison.OrdinalIgnoreCase))
                    {
                        Plugin.Log.LogInfo($"[技能随机] 阻断原生 silkRegenMax 写入: 场景 {scene}");
                        return false;
                    }
                // 钟兽变体走 UIMsgScenes 名单
                if (string.Equals(scene, "memory_silk_heart_bellbeast", StringComparison.OrdinalIgnoreCase))
                {
                    Plugin.Log.LogInfo($"[技能随机] 阻断原生 silkRegenMax 写入: 场景 {scene}");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[技能随机] silkRegenMax 阻断异常，放行: {ex}");
                return true;
            }
        }

        // weave_10 织女（纹章升级师，Eva 最终任务）额外槽平替：
//   Check Slot1/Slot2 判定 pd 字段 UnlockedExtraBlueSlot/UnlockedExtraYellowSlot，
//   未解锁 → Upgrade Slot1/2 Pre Dlg 对话 → 剧情到 Unlock First/Other Slot 状态
//   （写槽字段 + CreateObject 实例化场景内嵌 UI Msg，Item="Socket N"）→ 弹窗结束发
//   "GET ITEM MSG END" → Check Slot2 / Check Hunter v3（剧情链继续）。
// 平替：Unlock 状态拦截（发随机奖励 + 阻断弹窗与槽字段写入，补发 GET ITEM MSG END
//   放行剧情）。槽字段只由随机池 virt:UnlockCrestSlot（限量 2，对应蓝/黄槽）提供。
// 交互：未平替（字段 false）→ 剧情必然能走到升级流程（一定可触发）；平替后按键
//   "weave_10:Slot1/Slot2" 持久化，跨会话不重复发放。
static string GetWeave10SlotKey(FsmStateAction action)
        {
            if (action == null || action.Fsm == null || action.Fsm.Name != "Dialogue")
                return null;
            string scene = SceneManager.GetActiveScene().name;
            if (!string.Equals(scene, "weave_10", StringComparison.OrdinalIgnoreCase))
                return null;
            string state = action.Fsm.ActiveStateName;
            if (string.Equals(state, "Unlock First Slot", StringComparison.OrdinalIgnoreCase))
                return "weave_10:Slot1";
            if (string.Equals(state, "Unlock Other Slot", StringComparison.OrdinalIgnoreCase))
                return "weave_10:Slot2";
            return null;
        }

        /// <summary>织女额外槽演出拦截（CreateObject 实例化场景内嵌 UI Msg "Socket N"，带 Animator 动画）：
        /// 未平替发随机奖励并持久化；CreateObject 放行（return true），原生动画弹窗照常播放，
        /// 弹窗演完由原生 FSM 自己走 GET ITEM MSG END 转移，无需补发。</summary>
        [HarmonyPatch(typeof(CreateObject), "OnEnter")]
        [HarmonyPrefix]
        private static bool Prefix_WeaveUnlockMsg(CreateObject __instance)
        {
            try
            {
                if (!SilksongItemRandomizerAPI.IsEnabled())
                    return true;
                string key = GetWeave10SlotKey(__instance);
                if (key == null)
                    return true;

                Plugin.Log.LogInfo($"[技能随机] 织女额外槽演出拦截: {key}");

                if (!IsRegionRandomized(key))
                {
                    GiveRandomReward(key, "ExtraSlot");
                    if (Plugin.SaveData != null)
                    {
                        Plugin.SaveData.SkillRandomizedRegions.Add(key);
                        Plugin.SaveGlobalData();
                        Plugin.Log.LogInfo($"[技能随机] {key} 已平替（额外槽），持久化标记并落盘");
                    }
                }
                else
                {
                    Plugin.Log.LogInfo($"[技能随机] {key} 已平替过（持久化），仅拦截");
                }

                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[技能随机] 拦截织女额外槽弹窗异常，放行: {ex}");
                return true;
            }
        }

private static void GiveRandomReward(string scene, string skillName)
        {
            // 种子预生成映射优先：event:{场景} 在开种时已绑定奖励（同一种子结果确定）；
            // 未命中回落现抽（兜底：老存档映射缺失/未重建）。
            var reward = PreGeneratedMap.ResolveEventReward(scene) ?? ItemRandomizer.GetRandomReward();
            if (reward == null)
            {
                Plugin.Log.LogWarning("[技能随机] 随机奖励池为空，未发放");
                return;
            }
            try
            {
                reward.Give();
                ItemRandomizer.AddGivenCount(reward.Id);
                ItemRandomizer.RecordMapping($"skill:{scene}:{skillName ?? "?"}", "reward:" + reward.Id);
                RecentItemsUI.AddItem(reward);
                Plugin.Log.LogInfo($"[技能随机] 已触发随机奖励: {reward.DisplayName} ({reward.Id})");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[技能随机] 发放随机奖励异常: {ex}");
            }
        }

        // ===== 演出后续拦截：自动装配 + 字段写入（与纹章拦截 UnlockCrest/AutoEquipCrestV2 对齐）=====
        // Inspection 演出的解锁链路：Auto Equip 状态（AutoEquipTool 装配技能，弹窗前执行）→
        //   End 状态（SetPlayerDataBool 写能力字段 + ReportToolUnlocked + SetBenchRespawn）。
        // 平替后这两步必须一并阻断，否则技能被装上/字段被写入，与随机奖励重复。

        private static bool ShouldBlockSkillRegion()
        {
            if (!SilksongItemRandomizerAPI.IsEnabled()) return false;
            string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (scene == null || !IsSkillScene(scene)) return false;
            // 技能场景的演出（Auto Equip / 字段写入）只服务于技能获得，一律阻断装配与写入。
            // 注意不能依赖 IsRegionRandomized：Auto Equip 状态在弹窗（SpawnSkillGetMsg）之前执行，
            // 此时平替标记尚未写入；且跨会话重进时 Collected Check 因字段被拦仍为 false 会再走演出。
            return true;
        }

        /// <summary>阻断技能演出的自动装配（AutoEquipTool：ToolItemManager.AutoEquip 换上技能）</summary>
        [HarmonyPatch(typeof(AutoEquipTool), "OnEnter")]
        [HarmonyPrefix]
        private static bool Prefix_AutoEquip(AutoEquipTool __instance)
        {
            try
            {
                if (!ShouldBlockSkillRegion())
                    return true;
                Plugin.Log.LogInfo($"[技能随机] 阻断自动装配: {__instance.Tool?.Value?.name ?? "空"}");
                __instance.Finish();
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[技能随机] 拦截装配异常，放行: {ex}");
                return true;
            }
        }

        /// <summary>阻断技能演出的能力字段写入（SetPlayerDataBool 写 hasXXX；仅拦技能字段）。
        /// 另含 halfway_01 猎人日志分支：Dialogue FSM "Journal" 状态写 hasJournal=true，
        /// 该写入在 CreateUIMsgGetItem（弹窗）之前执行，必须独立于此处技能字段集合拦截。</summary>
        [HarmonyPatch(typeof(HutongGames.PlayMaker.Actions.SetPlayerDataBool), "OnEnter")]
        [HarmonyPrefix]
        private static bool Prefix_SetPdBool(HutongGames.PlayMaker.Actions.SetPlayerDataBool __instance)
        {
            try
            {
                // halfway_01 Nuu 剧情赠送猎人日志：Journal 状态写 hasJournal，一律阻断
                // （数值只由随机池 GiveJournal 提供）。幂等：未抽到时重复对话同样拦截。
                if (__instance.Fsm != null
                    && string.Equals(__instance.Fsm.Name, "Dialogue", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(__instance.Fsm.ActiveStateName, "Journal", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(__instance.boolName?.Value, "hasJournal", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(SceneManager.GetActiveScene().name, "halfway_01", StringComparison.OrdinalIgnoreCase))
                {
                    if (SilksongItemRandomizerAPI.IsEnabled())
                    {
                        Plugin.Log.LogInfo("[技能随机] 阻断猎人日志字段写入: hasJournal (halfway_01 Journal)");
                        __instance.Finish();
                        return false;
                    }
                }

                if (!ShouldBlockSkillRegion())
                    return true;
                string field = __instance.boolName?.Value;
                if (string.IsNullOrEmpty(field) || !SkillFields.Contains(field))
                    return true;
                Plugin.Log.LogInfo($"[技能随机] 阻断字段写入: {field} = {__instance.value?.Value}");
                __instance.Finish();
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[技能随机] 拦截字段写入异常，放行: {ex}");
                return true;
            }
        }

        /// <summary>变量版本的能力字段写入拦截（SetPlayerDataVariable：VariableName.Value 写
        /// PlayerData 字段）。织布机演出（bone_east_umbrella Msg 状态）写 hasBrolly 走的就是
        /// 这个 action，SetPlayerDataBool 前缀拦不到，必须补拦。</summary>
        [HarmonyPatch(typeof(HutongGames.PlayMaker.Actions.SetPlayerDataVariable), "OnEnter")]
        [HarmonyPrefix]
        private static bool Prefix_SetPdVar(HutongGames.PlayMaker.Actions.SetPlayerDataVariable __instance)
        {
            try
            {
                // weave_10 织女额外槽解锁写入拦截：槽只由随机池 virt:UnlockCrestSlot 提供，
                // 原生 Unlock 演出（Unlock First/Other Slot 状态）不得写 UnlockedExtraBlue/YellowSlot
                string slotKey = GetWeave10SlotKey(__instance);
                if (slotKey != null)
                {
                    Plugin.Log.LogInfo($"[技能随机] 阻断额外槽字段写入: {slotKey}");
                    __instance.Finish();
                    return false;
                }
                if (!ShouldBlockSkillRegion())
                    return true;
                string field = __instance.VariableName?.Value;
                if (string.IsNullOrEmpty(field) || !SkillFields.Contains(field))
                    return true;
                Plugin.Log.LogInfo($"[技能随机] 阻断变量字段写入: {field}");
                __instance.Finish();
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[技能随机] 拦截变量字段写入异常，放行: {ex}");
                return true;
            }
        }

        // ===== 技能获取点持久化判定（教堂判定：没触发随机强行有，触发了随机强行关）=====
        // 判定键 = SkillRandomizedRegions 持久化标记（跨会话生效），覆盖游戏原生"是否已拥有技能"判定：
        //   Inspection 演出的 Collected Check（PlayerDataBoolTest 测技能字段）→ 未随机强制走未拥有
        //     （演出继续→弹随机奖励），已随机强制走已拥有（Ability Collected：关闭演出对象与触发
        //     collider，演出短路=不可交互）。
        //   场景外 PlayerDataTestResponse（已拥有技能→激活已收集替换对象/关闭交互点）→ 未随机
        //     强制走未拥有分支（保持交互点），已随机强制走已拥有分支（替换为已收集形态）。

        /// <summary>Inspection 演出 Collected Check 判定强制（Fsm 名 Inspection 且处于技能场景）</summary>
        [HarmonyPatch(typeof(PlayerDataBoolTest), "OnEnter")]
        [HarmonyPrefix]
        private static bool Prefix_CollectedCheck(PlayerDataBoolTest __instance)
        {
            try
            {
                if (!SilksongItemRandomizerAPI.IsEnabled())
                    return true;

                // halfway_01 猎人日志获取判定强制（教堂判定同款："没触发随机强行没有，
                // 触发了随机放行原生"）：Dialogue FSM "Has Journal?" 状态测 hasJournal。
                // 老存档可能已原生持有日志（hasJournal=true）——不强制的话 Has Journal? 直接
                // TRUE → Convo Choice，Journal 状态永不可达，随机永不触发。故：
                //   未随机（标记不存在）→ 强制 isFalse（走赠送流程 → Journal 状态 → 拦截发随机）
                //   已随机 → 放行原生（hasJournal 已由随机池 GiveJournal 写入，true 走已持有分支；
                //     若玩家尚未抽到则 false 自然走赠送流程，与 Journal 状态的幂等拦截衔接）
                if (__instance.Fsm != null
                    && string.Equals(__instance.Fsm.Name, "Dialogue", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(__instance.Fsm.ActiveStateName, "Has Journal?", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(__instance.boolName?.Value, "hasJournal", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(SceneManager.GetActiveScene().name, "halfway_01", StringComparison.OrdinalIgnoreCase))
                {
                    if (IsRegionRandomized("halfway_01"))
                    {
                        Plugin.Log.LogInfo("[技能随机] 日志获取判定: halfway_01 已触发随机 → 放行原生 hasJournal 判定");
                        return true;
                    }
                    Plugin.Log.LogInfo("[技能随机] 日志获取判定: halfway_01 未触发随机 → 强制无日志（可触发赠送演出）");
                    if (__instance.isFalse != null)
                        __instance.Fsm.Event(__instance.isFalse);
                    __instance.Finish();
                    return false;
                }

                if (__instance.Fsm == null || __instance.Fsm.Name != "Inspection")
                    return true;
                string scene = SceneManager.GetActiveScene().name;
                if (scene == null || !IsSkillScene(scene))
                    return true;

                if (IsRegionRandomized(scene))
                {
                    Plugin.Log.LogInfo($"[技能随机] 获取点判定: {scene} 已触发随机 → 强制已收集（关闭交互）");
                    if (__instance.isTrue != null)
                        __instance.Fsm.Event(__instance.isTrue);
                }
                else
                {
                    Plugin.Log.LogInfo($"[技能随机] 获取点判定: {scene} 未触发随机 → 强制可交互（放行演出）");
                    if (__instance.isFalse != null)
                        __instance.Fsm.Event(__instance.isFalse);
                }
                __instance.Finish();
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[技能随机] 拦截获取点判定异常，放行: {ex}");
                return true;
            }
        }

        /// <summary>场景外已拥有判定强制（PlayerDataTestResponse：技能字段测试仅在技能场景平替）</summary>
        [HarmonyPatch(typeof(PlayerDataTestResponse), "Evaluate")]
        [HarmonyPrefix]
        private static bool Prefix_PDTestResponse(PlayerDataTestResponse __instance)
        {
            try
            {
                if (!SilksongItemRandomizerAPI.IsEnabled())
                    return true;
                string scene = SceneManager.GetActiveScene().name;
                if (scene == null || !IsSkillScene(scene))
                    return true;
                if (!TestsSkillField(__instance))
                    return true;

                if (IsRegionRandomized(scene))
                {
                    Plugin.Log.LogInfo($"[技能随机] 获取点外观判定: {scene} 已触发随机 → 已收集形态");
                    __instance.IsFullfilled?.Invoke();
                }
                else
                {
                    Plugin.Log.LogInfo($"[技能随机] 获取点外观判定: {scene} 未触发随机 → 可交互形态");
                    __instance.IsNotFulfilled?.Invoke();
                }
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[技能随机] 拦截获取点外观判定异常，放行: {ex}");
                return true;
            }
        }

        /// <summary>该 PlayerDataTestResponse 是否测试技能字段（反射读 test.TestGroups）</summary>
        private static bool TestsSkillField(PlayerDataTestResponse resp)
        {
            try
            {
                var field = typeof(PlayerDataTestResponse).GetField("test",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var test = field?.GetValue(resp) as PlayerDataTest;
                if (test?.TestGroups == null)
                    return false;
                foreach (var group in test.TestGroups)
                {
                    if (group.Tests == null)
                        continue;
                    foreach (var t in group.Tests)
                    {
                        if (!string.IsNullOrEmpty(t.FieldName) && SkillFields.Contains(t.FieldName))
                            return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        // ===== 已随机场景彻底关闭演出（强行关）：场景加载时直接禁用演出 FSM =====
        // Inspection FSM 场景加载即启动（startState=Pause→Idle，Idle 激活演出点视觉并等待
        // INTERACT）。已触发随机的场景重进后演出点不得再交互：在 PlayMakerFSM.OnEnable 处
        // 直接禁用整个 FSM（演出对象不激活、INTERACT 无响应），比逐层拦判定更彻底。
        // 当场触发随机时演出收尾（Heal/Fade/End）不受影响：OnEnable 只在场景加载时调用。

        /// <summary>禁用已平替技能场景的 Inspection 演出 FSM（场景加载时）</summary>
        [HarmonyPatch(typeof(PlayMakerFSM), "OnEnable")]
        [HarmonyPrefix]
        private static bool Prefix_DisableInspection(PlayMakerFSM __instance)
        {
            try
            {
                if (!SilksongItemRandomizerAPI.IsEnabled())
                    return true;
                if (__instance.Fsm == null || __instance.Fsm.Name != "Inspection")
                    return true;
                string scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
                if (scene == null || !IsSkillScene(scene))
                    return true;
                if (!IsRegionRandomized(scene))
                    return true;

                Plugin.Log.LogInfo($"[技能随机] 场景 {scene} 已触发随机 → 禁用 Inspection 演出 FSM（交互关闭）");
                __instance.enabled = false;
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[技能随机] 禁用演出 FSM 异常，放行: {ex}");
                return true;
            }
        }
    }
}

// ChurchRandomizePatch.cs - 教堂演出平替 + 教堂门开关接管（全部教堂处理集中于此文件）
// 一、演出平替：纹章教堂神社演出（插入缚丝演出）在"原生获得纹章弹窗"触发前的部分结束后：
//   1) 替换为触发随机奖励（ItemRandomizer 标准发放模式）
//   2) 阻止原生弹窗（ShowToolCrestUIMsg）触发，并正常收尾 FSM 流程（发 FinishEvent + Finish）
//   3) 同时拦截 UnlockCrest / AutoEquipCrestV2：演出中不真正解锁、不自动装备该教堂纹章
// 二、教堂门开关接管（ChurchGateCheckPatch）：mod 直接管理所有教堂门的开关，完全阻断原生判断。
//   门 FSM State Check 双条件：GetIsCrestUnlocked（纹章实际 IsUnlocked）+ PlayerDataBoolTest(chapelClosed)。
//   原生逻辑：纹章已解锁且未关闭 -> DO CLOSE（关门演出+置位 chapelClosed），已关 -> CLOSED，未解锁 -> OPEN。
//   原生判断在 mod 场景下错误百出：玩家可能经随机奖励"拿过纹章"（原生会关掉没到过的教堂门）；
//   平替演出后纹章实际未解锁（原生会不关已平替的教堂）。接管规则（仅教堂门场景 + 6 教堂纹章）：
//     已平替（演出完成、奖励已发）-> IsTrue 强制 true：走原生 DO CLOSE（关门动画 + 原生 SetPlayerDataBool 持久化）
//     未平替（玩家还没去该教堂）-> IsTrue 强制 false：发 OPEN 强行开门（无视纹章解锁与 chapelClosed 脏值）
// 依据（反向工程）：
//   - 纹章弹窗唯一 C# 入口 = HutongGames.PlayMaker.Actions.ShowToolCrestUIMsg.OnEnter()
//     （反编译 Assembly-CSharp，全工程仅此一处调用 ToolCrestUIMsg.Spawn）
//   - 神社演出 FSM 状态链：Crest Set -> Crest Change(UnlockCrest) -> Crest Return Anim -> Crest Msg
//     (ShowToolCrestUIMsg) -> FINISHED -> Reload Scene；showToolCrestUIMsg 触发时即"弹窗前部分"已结束
//   - chapelClosed_* 6 字段 = reaper/wanderer/beast/witch/toolmaster/shaman（PlayerData.cs:461-466），
//     官方无写回 false 路径；教堂门场景 (chapel_door_control FSM) 读它关门
//   - 6 座纹章教堂神社场景（含 ShowToolCrestUIMsg）：chapel_wanderer / greymoor_20c / ant_19 / under_20
//     （tut 教学关放行原生）
//   - 含 chapel_door_control 的门场景全集：greymoor_20b(Reaper) / ant_20 / bonegrave（fsmtemplates 为模板）
//     door 对象（door1/Door Open/Door Closed/Door DoClose）由 Init 状态 FindNamedChild 运行时填写
// 注册：SilksongItemRandomizerAPI.ToggleablePatchTypes（随物品随机总开关切换）
// 结构说明：4 个补丁合并为 2 个类。演出侧 3 个补丁（弹窗/解锁/自动装配）合并进主类（方法级
//   [HarmonyPatch] 声明）；门判定单独一类——它拦截 IsTrue 属性 getter，必须以 TargetMethod()
//   显式声明（类级 [HarmonyPatch] 字符串属性声明在 PatchAll 下实测不生效），无法并入方法级声明类。

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 教堂处理主类：神社演出平替（弹窗/解锁/自动装配三个拦截）+ 教堂门开关辅助逻辑。
    /// 3 个补丁合并于此（方法级 [HarmonyPatch] 声明，PatchAll/Unpatch 按类处理均兼容）。
    /// </summary>
    public static class ChurchRandomizePatch
    {
        // ================= 静态数据 =================

        // 纹章神社演出场景（含 ShowToolCrestUIMsg 弹窗的 6 座教堂神社，tut 教学除外）
        // internal：PreGeneratedMap.EventKeys 引用收集 event: 预分配键。
        internal static readonly string[] ChapelShrineScenes = {
            "chapel_wanderer", "greymoor_20c", "ant_19", "under_20"
        };

        // 教堂门所在场景（含 chapel_door_control FSM，启动关门判定的场景；fsmtemplates 为模板非场景）
        private static readonly string[] ChapelDoorScenes = {
            "greymoor_20b", "ant_20", "bonegrave"
        };

        // 纹章名(兼容 _v2 等变体) -> PlayerData.chapelClosed_* 字段名
        private static readonly Dictionary<string, string> CrestToClosedField = new()
        {
            { "reaper",     "chapelClosed_reaper" },
            { "wanderer",   "chapelClosed_wanderer" },
            { "beast",      "chapelClosed_beast" },
            { "witch",      "chapelClosed_witch" },
            { "toolmaster", "chapelClosed_toolmaster" },
            { "shaman",     "chapelClosed_shaman" },
        };

        // 纹章名 -> 映射结果缓存（GetIsCrestUnlocked.IsTrue 是门场景热路径，避免每帧做
        // 字符串匹配/分配；纹章名稳定，缓存安全）
        private static readonly Dictionary<string, string> _fieldCache = new(StringComparer.OrdinalIgnoreCase);

        // 场景名 -> 判定结果缓存（门判定每帧调用，场景切换才失效）
        private static string _lastScene;
        private static bool _lastIsShrine;
        private static bool _lastIsDoor;

        // 每场演出只发一次随机/关一次门（神社 FSM 可能多次回到弹窗状态，如 Rebind 循环）
        private static readonly HashSet<string> _handledScenes = new();

        // ================= 场景/纹章判定 =================

        private static void UpdateSceneCache()
        {
            string scene = SceneManager.GetActiveScene().name;
            if (scene == _lastScene) return;
            _lastScene = scene;
            _lastIsShrine = IsInList(scene, ChapelShrineScenes);
            _lastIsDoor = IsInList(scene, ChapelDoorScenes);
        }

        private static bool IsChapelShrineSceneNow()
        {
            UpdateSceneCache();
            return _lastIsShrine;
        }

        internal static bool IsChapelDoorSceneNow()
        {
            UpdateSceneCache();
            return _lastIsDoor;
        }

        private static bool IsInList(string sceneName, string[] list)
        {
            if (list == null || list.Length == 0) return false;
            foreach (var s in list)
                if (string.Equals(sceneName, s, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        /// <summary>当前（缓存的）场景名，供日志输出</summary>
        internal static string LastScene => _lastScene;

        /// <summary>纹章是否为 6 座教堂对应纹章（命中返回 chapelClosed 字段名，未命中返回 null）；
        /// 带缓存，热路径（门判定）安全。</summary>
        internal static string GetClosedFieldByCrest(ToolCrest crest)
        {
            string crestName = crest?.name;
            if (string.IsNullOrEmpty(crestName)) return null;
            if (_fieldCache.TryGetValue(crestName, out var cached)) return cached;
            string field = MatchClosedField(crestName);
            _fieldCache[crestName] = field;
            return field;
        }

        // 无分配匹配：忽略大小写精确相等，或 "key" + "_" 前缀（_v2 等变体）
        private static string MatchClosedField(string crestName)
        {
            foreach (var kv in CrestToClosedField)
            {
                if (string.Equals(crestName, kv.Key, StringComparison.OrdinalIgnoreCase))
                    return kv.Value;
                if (crestName.Length > kv.Key.Length
                    && string.Compare(crestName, 0, kv.Key, 0, kv.Key.Length, StringComparison.OrdinalIgnoreCase) == 0
                    && crestName[kv.Key.Length] == '_')
                    return kv.Value;
            }
            return null;
        }

        /// <summary>该教堂纹章是否已完成平替（随机奖励已发放，门应永久关闭）</summary>
        internal static bool IsChapelRandomized(string crestName)
        {
            if (string.IsNullOrEmpty(crestName)) return false;
            return Plugin.SaveData?.ChapelRandomizedCrests?.Contains(crestName) ?? false;
        }

        /// <summary>记录教堂平替完成（持久化，门场景据此关门）</summary>
        private static void MarkChapelRandomized(string crestName)
        {
            if (string.IsNullOrEmpty(crestName)) return;
            Plugin.SaveData.ChapelRandomizedCrests.Add(crestName);
        }

        public static void ResetHandledScenes() => _handledScenes.Clear();

        // ================= 补丁 1：教堂演出弹窗拦截 =================

        /// <summary>
        /// 教堂神社演出平替：纹章插入演出结束后拦截原生弹窗，改为触发随机奖励。
        /// </summary>
        [HarmonyPatch(typeof(ShowToolCrestUIMsg), "OnEnter")]
        [HarmonyPrefix]
        private static bool Prefix_ShowMsg(ShowToolCrestUIMsg __instance)
        {
            try
            {
                // 仅受物品随机总开关控制（默认打开，不依赖纹章随机配置开关）
                if (!SilksongItemRandomizerAPI.IsEnabled())
                    return true;

                // 教学关放行原生（tut 场景非教堂，不改教学演出）
                string scene = SceneManager.GetActiveScene().name;
                if (scene != null && scene.StartsWith("tut", StringComparison.OrdinalIgnoreCase))
                {
                    Plugin.Log.LogInfo($"[教堂随机] 教学场景 {scene}，放行原生纹章弹窗");
                    return true;
                }

                // 取神社引用的纹章对象，按名字判定是否教堂演出（命中 6 字段之一才是教堂）
                ToolCrest crest = __instance.Crest?.Value as ToolCrest;
                string closedField = GetClosedFieldByCrest(crest);
                if (closedField == null)
                {
                    Plugin.Log.LogInfo($"[教堂随机] 非教堂纹章演出 ({crest?.name ?? "空"})，放行原生弹窗");
                    return true;
                }

                Plugin.Log.LogInfo($"[教堂随机] 教堂演出拦截: 场景 {scene}, 纹章 {crest?.name}");

                // 1) 本场演出首次：触发随机奖励 + 记录平替标记（不直接写 chapelClosed 字段，
                //    由门场景的原生关门演出负责置位并持久化，保证关门动画正常播放）
                if (!_handledScenes.Contains(scene))
                {
                    _handledScenes.Add(scene);
                    GiveRandomReward(crest?.name);
                    MarkChapelRandomized(crest?.name);
                    Plugin.Log.LogInfo($"[教堂随机] 已标记教堂平替: {crest?.name}（门场景将触发原生关门演出）");
                }
                else
                {
                    Plugin.Log.LogInfo($"[教堂随机] 场景 {scene} 本次演出已处理过，仅跳过弹窗");
                }

                // 2) 阻止原生弹窗并正常结束流程（与原生 OnCrestMsgEnd 收尾一致: 发 FinishEvent + Finish）
                __instance.Fsm.Event(__instance.FinishEvent);
                __instance.Finish();
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[教堂随机] 弹窗拦截异常，放行原生: {ex}");
                return true;
            }
        }

        // ================= 补丁 2：教堂演出纹章解锁拦截 =================

        /// <summary>
        /// 跳过 UnlockCrest（玩家在教堂演出中不真正获得对应纹章，与自动装配拦截联动）。
        /// 仅神社场景 + 教堂纹章判定拦截，其余场景原生解锁不受影响。
        /// </summary>
        [HarmonyPatch(typeof(UnlockCrest), "OnEnter")]
        [HarmonyPrefix]
        private static bool Prefix_UnlockCrest(UnlockCrest __instance)
        {
            try
            {
                if (!SilksongItemRandomizerAPI.IsEnabled())
                    return true;
                if (!IsChapelShrineSceneNow())
                    return true;

                ToolCrest crest = __instance.Crest?.Value as ToolCrest;
                if (GetClosedFieldByCrest(crest) == null)
                    return true;

                Plugin.Log.LogInfo($"[教堂随机] 拦截纹章解锁: 场景 {_lastScene}, 纹章 {crest?.name}");
                __instance.Finish();
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[教堂随机] 纹章解锁拦截异常，放行: {ex}");
                return true;
            }
        }

        // ================= 补丁 3：教堂演出纹章自动装配拦截 =================

        /// <summary>
        /// 跳过 AutoEquipCrestV2（纹章插入演出中的自动装备），避免玩家因教堂演出获得/被装上纹章。
        /// 仅神社场景 + 教堂纹章判定拦截，其余场景原生装配不受影响。
        /// </summary>
        [HarmonyPatch(typeof(AutoEquipCrestV2), "OnEnter")]
        [HarmonyPrefix]
        private static bool Prefix_AutoEquip(AutoEquipCrestV2 __instance)
        {
            try
            {
                if (!SilksongItemRandomizerAPI.IsEnabled())
                    return true;
                if (!IsChapelShrineSceneNow())
                    return true;

                ToolCrest crest = __instance.Crest?.Value as ToolCrest;
                if (GetClosedFieldByCrest(crest) == null)
                    return true;

                Plugin.Log.LogInfo($"[教堂随机] 拦截纹章自动装配: 场景 {_lastScene}, 纹章 {crest?.name}");
                // 防御：原生 AutoEquipCrestV2 会按 SkipToAppear 置位/复位 BindOrbHudFrame.SkipToNextAppear，
                // 拦截后静态标志必须保持干净，避免 HUD 绑定演出停留在跳过状态（声音/动画异常循环）
                if (__instance.SkipToAppear.Value)
                    BindOrbHudFrame.SkipToNextAppear = false;
                __instance.Finish();
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[教堂随机] 自动装配拦截异常，放行: {ex}");
                return true;
            }
        }

        // ================= 随机奖励发放 =================

        private static void GiveRandomReward(string crestName)
        {
            // 种子预生成映射优先（触发点在神社场景内，场景名即 event: 键）；未命中回落现抽。
            string scene = SceneManager.GetActiveScene().name;
            var reward = PreGeneratedMap.ResolveEventReward(scene) ?? ItemRandomizer.GetRandomReward();
            if (reward == null)
            {
                Plugin.Log.LogWarning($"[教堂随机] 随机奖励池为空，未发放");
                return;
            }
            try
            {
                reward.Give();
                ItemRandomizer.AddGivenCount(reward.Id);
                ItemRandomizer.RecordMapping($"church:{crestName}", "reward:" + reward.Id);
                RecentItemsUI.AddItem(reward);
                Plugin.Log.LogInfo($"[教堂随机] 已触发随机奖励: {reward.DisplayName} ({reward.Id})");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[教堂随机] 发放随机奖励异常: {ex}");
            }
        }
    }

    /// <summary>
    /// 教堂门关门判定接管：mod 直接管理教堂门开关，完全阻断原生判断（详见文件头注释）。
    /// 独立类原因：拦截的是 GetIsCrestUnlocked.IsTrue 属性 getter，必须以 TargetMethod()
    /// 显式声明（类级 [HarmonyPatch] 字符串属性声明在 PatchAll 下实测不生效）。
    /// </summary>
    [HarmonyPatch]
    public static class ChurchGateCheckPatch
    {
        private static System.Reflection.MethodBase TargetMethod()
        {
            return typeof(GetIsCrestUnlocked).GetProperty("IsTrue")?.GetGetMethod();
        }

        [HarmonyPrefix]
        private static bool Prefix(GetIsCrestUnlocked __instance, ref bool __result)
        {
            try
            {
                if (!SilksongItemRandomizerAPI.IsEnabled())
                    return true;
                if (!ChurchRandomizePatch.IsChapelDoorSceneNow())
                    return true;

                ToolCrest crest = __instance.Crest?.Value as ToolCrest;
                if (crest == null)
                {
                    // 门场景内该判定每帧调用，不在此打日志（会产生每帧日志 IO）
                    return true;
                }
                if (ChurchRandomizePatch.GetClosedFieldByCrest(crest) == null)
                {
                    return true;
                }

                bool randomized = ChurchRandomizePatch.IsChapelRandomized(crest.name);
                __result = randomized;
                return false;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[教堂随机] 关门判定接管异常，放行: {ex}");
                return true;
            }
        }
    }
}
