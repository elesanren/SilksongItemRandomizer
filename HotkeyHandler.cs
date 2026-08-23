using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using SilksongItemRandomizer;
using StartingAbilityPicker;
using GlobalSettings;
using HutongGames.PlayMaker;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;
using Plugin = SilksongItemRandomizer.Plugin;   // 别名，解决命名冲突

/// <summary>
/// 全局快捷键处理器
/// F3: 扫描所有场景的检查点（拾取点/检查交互/收费机/车站长椅）到 check_points_scan.txt，边读边写（建议主菜单使用）
/// F7: 扫描当前场景商店在架商品（含每个商品的实物实体名/显示名/价格），关键词匹配 mask/spool/fragment 等并写入 shop_items_scan.txt
/// F5: 尝试所有方式解锁 EvaHeal（风铃摇）能力（包括 HasBoundCrestUpgrader 等）
/// F6: 传送至上一次坐的椅子
/// F8: 显示/隐藏最近获得物品 UI
/// F9: 转储所有随机映射到控制台
/// ESC: 刷新 Benchwarp 菜单
///
/// F4: 纹章原生获得弹窗测试（临时代码）：轮流显示 ToolItemManager.GetAllCrests() 中每个纹章的
///     "获得纹章"原生弹窗，用于验证弹窗调用方式。
///
/// F4(当前): 依次发放全部能力（调试用）：按列表顺序每次按 F4 发放一个能力，走官方弹窗链路
///     （6 能力弹窗 PowerUpGetMsg / 7 技能弹窗 SkillGetMsg / 4 官方横幅 CollectableUIMsg），
///     发放前重置 pd 字段以便重复测试弹窗效果。
///
/// == 纹章原生弹窗技术方案（已实测验证通过, 2026-08-16）==
/// 落地现状: 机制已实现于 CrestPopupHelper.cs（预制体加载+弹窗调用），随机奖励为纹章时
///   SavedItemReward.Give 自动弹原生纹章弹窗；下方为当时验证的技术要点。
/// 1) 预制体来源: AssetBundle "c3ef29e95eda5580682bb076589a723c.bundle"（游戏启动即预加载, 位于
///    StreamingAssets\aa\StandaloneWindows64\ 下）内的
///    "Assets/Prefabs/UI/Messages/Tool Crest UI Msg.prefab"（另有两个变体:
///    "Tool Crest UI Msg Architect Variant.prefab" 建筑师版 / "Tool Crest Upgrade UI Msg.prefab" 升级版）。
///    注意: 不能直接 AssetBundle.LoadFromFile 加载, Unity 会报 "another AssetBundle with the same
///    files is already loaded" 拒绝; 必须先遍历 AssetBundle.GetAllLoadedAssetBundles() 按
///    bundle.name == "c3ef29e95eda5580682bb076589a723c.bundle" 匹配, 再 targetBundle.LoadAsset<GameObject>(路径)。
///    另外: 该 bundle 与技能弹窗 (Silk_Skill_Get_Prompt.prefab) 是同一个 bundle, 加载方式同
///    SilkSpearPityPatch.GetSkillMsgPrefab。
/// 2) 调用链: ToolCrestUIMsg.Spawn(ToolCrest crest, GameObject prefab, Action afterMsg = null)
///    -> UIMsgBase<ToolCrest>.Spawn 内部: Object.Instantiate(prefab) + StartCoroutine(DoMsg):
///    显示动画(animator) -> 等待玩家按跳过键 -> 隐藏动画 -> afterMsg() 回调 -> SetActive(false)。
///    弹窗为自实例化自管理, 无需手动挂到 Canvas 或销毁; 播放期间 UIMsgProxy 会接管暂停/震动/输入。
/// 3) 纹章数据来源: ToolItemManager.GetAllCrests() 返回全部纹章 List<ToolCrest>（管理器组件序列化
///    的 ToolCrestList ScriptableObject 列表）; ToolCrest 字段均为私有序列化 + 只读属性
///    (CrestSprite/DisplayName/GetPromptDesc/ItemNamePrefix/EquipText), Setup 用它填充弹窗文本。
///    注意: Resources.FindObjectsOfTypeAll<ToolCrest> 只能拿到当前场景已加载的纹章实例(通常只有
///    当前装备的一个, 如 Hunter), 拿不到全部, 必须用 ToolItemManager.GetAllCrests()。
/// 4) 原生游戏的触发入口: PlayMaker action HutongGames.PlayMaker.Actions.ShowToolCrestUIMsg
///    (OnEnter 里调 Spawn), 纹章与 prefab 均来自场景 FSM 变量, 游戏代码侧无其他调用点。
/// 5) 已知纹章名: Hunter, Hunter_v2, Hunter_v3, Reaper, Wanderer, Warrior, Witch, Toolmaster,
///    Spell, Cursed, Cloakless 等。
/// </summary>
public class HotkeyHandler : MonoBehaviour
{
    private GUIStyle _tipStyle;
    private static bool _isChineseCache;
    private static bool _benchwarpChecked;
    private static Type _benchwarpType;
    private static PropertyInfo _benchwarpInstanceProp;
    private static PropertyInfo _benchwarpIsDisplayingProp;
    private static object _benchwarpInstance;

    private void Update()
    {
        // limit 面板改动：防抖持久化 + 重建映射（每帧只做 O(1) 检查）
        SilksongItemRandomizerAPI.FlushLimitsRegenerate();

        if (_pickerActive)
        {
            if (Input.GetKeyDown(KeyCode.Return)) PickCurrent(true);
            else if (Input.GetKeyDown(KeyCode.X)) PickCurrent(false);
        }

        if (_annotateActive && Input.GetKeyDown(KeyCode.Return))
            SaveCurrentNote();

        if (Input.GetKeyDown(KeyCode.F5))
        {
            TestUnlockEvaHeal();
        }

        if (Input.GetKeyDown(KeyCode.F6))
            WarpToLastBench();

        if (Input.GetKeyDown(KeyCode.F8))
            ToggleRecentItemsUI();

        if (Input.GetKeyDown(KeyCode.F9))
            Plugin.Instance?.DumpAllMappings();

        // F4 测试：丝轴 prefab 给予（复现拦截误伤问题，调试用）
        if (Input.GetKeyDown(KeyCode.F4))
        {
            Plugin.Log.LogInfo("[F4丝轴测试] 触发 GiveSpoolPart");
            StartCoroutine(NativePickupGiver.GiveSpoolPart());
        }
        if (Input.GetKeyDown(KeyCode.Escape))
            Plugin.Instance?.RefreshBenchwarpUI();
    }


    private int _f4MsgCycleIndex;

    /// <summary>F4 测试：依次调用 OfficialMsgHelper 三类官方弹窗（额外槽→丝之心→纹章），循环。</summary>
    //private void TestOfficialMsgCycle()
    //{
    //    try
    //    {
    //        switch (_f4MsgCycleIndex++ % 3)
    //        {
    //            case 0:
    //            {
    //                var slot = _f4MsgCycleIndex % 2 == 0 ? ToolItemType.Blue : ToolItemType.Yellow;
    //                Plugin.Log.LogInfo($"[F4弹窗循环] 额外槽 {slot}");
    //                StartingAbilityPicker.OfficialMsgHelper.ShowExtraSlot(slot);
    //                break;
    //            }
    //            case 1:
    //                Plugin.Log.LogInfo("[F4弹窗循环] 丝之心");
    //                // 走 ShowBannerPopup 已有映射 "HasSeenSilkHearts" => "SilkHeart"
    //                // → UiMsgBannerHelper.Show 进 Msg Control FSM StringSwitch，
    //                // 文案由官方语言表自动填入“已汲取，恢复甲壳内灵丝再生的能力”；
    //                // 图标由 SIR 侧传官方丝之心图标（SAP bundle 扫描拿不到该 sprite）
    //                StartingAbilityPicker.SkillRandomizer.ShowBannerPopup("HasSeenSilkHearts",
    //                    SpriteCache.Find("prompt_silkheart"));
    //                break;
    //            case 2:
    //            {
    //                var all = ToolItemManager.GetAllCrests();
    //                if (all == null || all.Count == 0)
    //                {
    //                    Plugin.Log.LogError("[F4弹窗循环] 无纹章清单");
    //                    break;
    //                }
    //                var crest = all[_crestPopupIndex++ % all.Count];
    //                Plugin.Log.LogInfo($"[F4弹窗循环] 纹章 {crest.name}");
    //                StartingAbilityPicker.OfficialMsgHelper.ShowToolCrest(crest,
    //                    () => Plugin.Log.LogInfo("[F4弹窗循环] 纹章弹窗结束"));
    //                break;
    //            }
    //        }
    //    }
    //    catch (Exception ex)
    //    {
    //        Plugin.Log.LogError($"[F4弹窗循环] 异常: {ex}");
    //    }
    //}

    // private void GiveSelfFragmentsTest()
    // {
    //     int n = _f4SelfGiveIndex++ % 2;
    //     if (n == 0)
    //     {
    //         NativePickupGiver.GiveHeartPiece();
    //     }
    //     else
    //     {
    //         Plugin.Instance?.StartCoroutine(NativePickupGiver.GiveSpoolPart());
    //     }
    // }


    /// <summary>
    /// 依次给予 Slab Key A/B/C，每次按 F4 给下一把，循环。
    /// 同时设置对应的 HasSlabKey 布尔值，并弹出原生 CollectableUIMsg。
    /// </summary>

    // ==================== F4 测试：依次给予三把 Slab 钥匙 ====================
    //private static int _slabKeyIndex = 0;

    ///// <summary>
    ///// 依次给予 Slab Key A/B/C，每次按 F4 给下一把，循环。
    ///// 同时设置对应的 HasSlabKey 布尔值，并弹出原生 CollectableUIMsg。
    ///// </summary>
    //// 缓存（与 RewardCore 共享同一 bundle 引用，但为了独立测试，各自维护）
    //private static AssetBundle _testBundle = null;
    //private static bool _testBundleSearched = false;

    //private void TestGiveSlabKeysInOrder()
    //{
    //    try
    //    {
    //        var keys = new (string name, string boolField)[]
    //        {
    //        ("Slab Key A", "HasSlabKeyA"),
    //        ("Slab Key B", "HasSlabKeyB"),
    //        ("Slab Key C", "HasSlabKeyC")
    //        };

    //        var (keyName, boolField) = keys[_slabKeyIndex % keys.Length];
    //        _slabKeyIndex++;

    //        // 1. 设置布尔值
    //        var pd = PlayerData.instance;
    //        if (pd == null)
    //        {
    //            Plugin.Log.LogWarning("[F4测试] PlayerData 不可用");
    //            return;
    //        }
    //        pd.SetBool(boolField, true);
    //        Plugin.Log.LogInfo($"[F4测试] 设置 {boolField} = true");

    //        // 2. 缓存 bundle（只找一次）
    //        if (!_testBundleSearched)
    //        {
    //            var bundles = AssetBundle.GetAllLoadedAssetBundles();
    //            string testPath = "Assets/Data Assets/Collectables/Fake Collectables/Slab Key A.asset";
    //            foreach (var bundle in bundles)
    //            {
    //                if (bundle == null) continue;
    //                var testItem = bundle.LoadAsset<SavedItem>(testPath);
    //                if (testItem != null)
    //                {
    //                    _testBundle = bundle;
    //                    Plugin.Log.LogInfo($"[F4测试] 缓存 bundle: {bundle.name}");
    //                    break;
    //                }
    //            }
    //            _testBundleSearched = true;
    //        }

    //        // 3. 用缓存的 bundle 弹窗
    //        if (_testBundle != null)
    //        {
    //            string path = $"Assets/Data Assets/Collectables/Fake Collectables/{keyName}.asset";
    //            var item = _testBundle.LoadAsset<SavedItem>(path);
    //            if (item != null)
    //            {
    //                item.Get(true);
    //                Plugin.Log.LogInfo($"[F4测试] 弹窗显示: {keyName}");
    //            }
    //            else
    //            {
    //                Plugin.Log.LogWarning($"[F4测试] 从缓存 bundle 加载失败: {keyName}");
    //            }
    //        }
    //        else
    //        {
    //            Plugin.Log.LogWarning("[F4测试] 未找到 FakeCollectables bundle");
    //        }
    //    }
    //    catch (Exception ex)
    //    {
    //        Plugin.Log.LogError($"[F4测试] 异常: {ex}");
    //    }
    //}
    private static int _crestPopupIndex;

    // F4 教堂关门动画触发测试：当前场景找 chapel_door_control FSM 及其门对象，
    // 直接发 DO CLOSE 事件跑原生关门演出（激活 Door DoClose 播关门动画 + SetPlayerDataBool 持久化）。
    // 找不到门对象/FSM 时输出诊断日志，便于确认场景对象名。
    private void TestTriggerChapelDoorClose()
    {
        try
        {
            string scene = SceneManager.GetActiveScene().name;
            Plugin.Log.LogInfo($"[教堂关门测试] 场景: {scene}");

            GameObject doorDoClose = null, doorClosed = null, doorOpen = null, doorArt = null;
            List<string> allDoorLike = new();
            PlayMakerFSM doorFsm = null;

            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                {
                    string n = child.name;
                    if (n.IndexOf("Door", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("door", StringComparison.OrdinalIgnoreCase) >= 0)
                        allDoorLike.Add(n);
                    if (n == "Door DoClose" && doorDoClose == null) doorDoClose = child.gameObject;
                    else if (n == "Door Closed" && doorClosed == null) doorClosed = child.gameObject;
                    else if (n == "Door Art" && doorArt == null) doorArt = child.gameObject;
                }
                foreach (var f in root.GetComponentsInChildren<PlayMakerFSM>(true))
                {
                    if (doorFsm == null && f.FsmName == "chapel_door_control")
                        doorFsm = f;
                }
            }

            Plugin.Log.LogInfo($"[教堂关门测试] chapel_door_control FSM: {(doorFsm != null ? doorFsm.gameObject.name : "未找到")}");
            Plugin.Log.LogInfo($"[教堂关门测试] Door DoClose: {(doorDoClose != null ? doorDoClose.name : "未找到")} | Door Closed: {(doorClosed != null ? doorClosed.name : "未找到")} | Door Art: {(doorArt != null ? doorArt.name : "未找到")}");
            Plugin.Log.LogInfo($"[教堂关门测试] 场景内 door 相关对象: {string.Join(", ", allDoorLike.Distinct(StringComparer.OrdinalIgnoreCase))}");

            if (doorFsm == null)
            {
                Plugin.Log.LogWarning("[教堂关门测试] 无 chapel_door_control FSM，退出");
                return;
            }

            Plugin.Log.LogInfo($"[教堂关门测试] FSM 当前状态: {doorFsm.Fsm.ActiveStateName}");

            // 诊断：State Check 判定相关变量与 mod 平替标记
            try
            {
                var vars = doorFsm.Fsm.Variables;
                var crestType = vars.GetFsmEnum("Crest Type");
                var crestObj = vars.GetFsmObject("Crest Object");
                var closedPdBool = vars.GetFsmString("Closed PD Bool");
                Plugin.Log.LogInfo($"[教堂关门测试] Crest Type: {crestType?.Value} | Crest Object: {crestObj?.Value?.name ?? "空"} | Closed PD Bool: {closedPdBool?.Value ?? "空"}");
                var pd = PlayerData.instance;
                if (pd != null)
                    Plugin.Log.LogInfo($"[教堂关门测试] chapelClosed: reaper={pd.chapelClosed_reaper} wanderer={pd.chapelClosed_wanderer} beast={pd.chapelClosed_beast} witch={pd.chapelClosed_witch} toolmaster={pd.chapelClosed_toolmaster} shaman={pd.chapelClosed_shaman}");
                try { Plugin.Log.LogInfo($"[教堂关门测试] 平替标记: {string.Join(", ", Plugin.SaveData.ChapelRandomizedCrests)}"); } catch { }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[教堂关门测试] 诊断输出异常: {ex.Message}");
            }

            // 直接跳进 Do Close 状态演关门（Init/Crest Check 已跑过，变量已就绪）：
            // SendMessage(door1,SetIsInactive,true) + SetPlayerDataBool + FindNamedChild + SetDeathRespawnV2 -> FINISHED -> Activate Door Close(激活 Door DoClose 播关门动画)
            doorFsm.Fsm.SetState("Do Close");
            Plugin.Log.LogInfo("[教堂关门测试] 已 SetState(\"Do Close\")，观察关门动画是否播放");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[教堂关门测试] 异常: {ex}");
        }
    }

    // F4 测试：随机发放一个纹章（走正式奖励路径 SavedItemReward.Give：
    // 解锁 + 原生"获得纹章"弹窗 + 弹窗结束后自动装配动画，与神社演出顺序一致）。
    // 优先选未解锁纹章（贴近真实获得场景），全部解锁后随机任性发。
    private void TestGiveRandomCrest()
    {
        try
        {
            var all = ToolItemManager.GetAllCrests();
            if (all == null || all.Count == 0)
            {
                Plugin.Log.LogError("[F4纹章] ToolItemManager 无纹章清单");
                return;
            }

            ToolCrest pick = null;
            foreach (var c in all)
            {
                if (c != null && !c.IsUnlocked) { pick = c; break; }
            }
            if (pick == null)
                pick = all[UnityEngine.Random.Range(0, all.Count)];
            if (pick == null) return;

            Plugin.Log.LogInfo($"[F4纹章] 随机发放纹章: {pick.name}");
            var reward = new SavedItemReward(pick);
            reward.Give();
            SilksongItemRandomizer.ItemRandomizer.AddGivenCount(reward.Id);
            SilksongItemRandomizer.ItemRandomizer.RecordMapping("test:F4crest", "reward:" + reward.Id);
            RecentItemsUI.AddItem(reward);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[F4纹章] 异常: {ex}");
        }
    }

    // F4 测试：依次播放全部技能弹窗 + 全部纹章弹窗（官方弹窗链路：6 能力弹窗 PowerUpGetMsg /
    // 7 技能弹窗 SkillGetMsg / 4 官方 UI Msg 横幅 + 纹章 ToolCrestUIMsg），每按一次播下一个，
    // 遍历后循环。技能发放前重置对应 pd 字段，保证重复按 F4 每次都能看到弹窗。
    private static readonly string[] _testSkillFields = {
        "hasDash", "hasWalljump", "hasHarpoonDash", "hasNeedolin", "hasSuperJump", "HasSeenEvaHeal",
        "hasNeedleThrow", "hasThreadSphere", "hasSilkCharge", "hasSilkBomb", "hasSilkBossNeedle",
        "hasParry", "hasBrolly", "hasDoubleJump", "hasChargeSlash",
        "hasNeedolinMemoryPowerup", "hasFastTravelTeleport"
    };

    // ==================== F4 测试：额外槽获得（物品给予 + 官方全屏 UI Msg 弹窗） ====================
    // 全屏演出 = ExtraToolSlotUIMsg（UIMsgBase 家族全屏弹窗：槽位图标/名称/染色 + 入场动画），
    // prefab 通过 Addressables 加载（解析全部跨 bundle 依赖：字体/动画/粒子/音效）。
    // 蓝槽/黄槽轮流。
    private static int _extraSlotTestIndex = 0;
    private static bool _slotDepsLoaded;

    /// <summary>全量加载 Addressables bundle（aa/StandaloneWindows64 下所有 .bundle）。
    /// 关键：AssetBundle 的跨 bundle 引用在"依赖 bundle 已加载"时自动解析完整
    /// （动画 clip/字体材质/贴图/音效全在依赖 bundle 里）。只做一次。</summary>
    private static void LoadSlotDependencyBundles()
    {
        if (_slotDepsLoaded) return;
        _slotDepsLoaded = true;
        try
        {
            string dir = System.IO.Path.Combine(Application.streamingAssetsPath, "aa", "StandaloneWindows64");
            if (!System.IO.Directory.Exists(dir))
            {
                Plugin.Log.LogWarning("[F4额外槽] Addressables 目录不存在: " + dir);
                return;
            }
            int ok = 0, fail = 0, skippedScene = 0;
            foreach (var file in System.IO.Directory.GetFiles(dir, "*.bundle"))
            {
                try
                {
                    var b = AssetBundle.LoadFromFile(file);
                    if (b == null) { fail++; continue; }
                    // 跳过场景 bundle（isStreamedSceneAssetBundle），避免污染场景切换
                    if (b.isStreamedSceneAssetBundle)
                    {
                        b.Unload(false);
                        skippedScene++;
                        continue;
                    }
                    ok++;
                }
                catch { fail++; }
            }
            Plugin.Log.LogInfo($"[F4额外槽] 依赖 bundle 全量加载: 成功{ok} 场景跳过{skippedScene} 失败{fail}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[F4额外槽] 依赖 bundle 加载异常: {ex.Message}");
        }
    }

    private void TestGiveExtraSlot()
    {
        try
        {
            var pd = PlayerData.instance;
            if (pd == null)
            {
                Plugin.Log.LogError("[F4额外槽] PlayerData 不可用");
                return;
            }

            int slot = _extraSlotTestIndex % 2;
            _extraSlotTestIndex++;
            string field = slot == 0 ? "UnlockedExtraYellowSlot" : "UnlockedExtraBlueSlot";
            Plugin.Log.LogInfo($"[F4额外槽] 触发: slot#{_extraSlotTestIndex - 1} field={field} scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");

            // 1. 物品给予：解锁额外槽字段
            var fi = typeof(PlayerData).GetField(field, BindingFlags.Instance | BindingFlags.Public);
            if (fi == null || fi.FieldType != typeof(bool))
            {
                Plugin.Log.LogError($"[F4额外槽] 字段不存在: {field}");
                return;
            }
            fi.SetValue(pd, true);
            Plugin.Log.LogInfo($"[F4额外槽] 物品给予: {field} = true");

            // 2. 弹窗：官方全屏演出 ExtraToolSlotUIMsg.Spawn
            //    prefab 走 Addressables 加载（游戏自用加载系统，解析全部跨 bundle 依赖：
            //    字体/动画/材质/音频完整）→ UIMsgBase 全自动演出（Setup 槽位图标/名称/染色
            //    → Animator 播放入场 → 停留 → 淡出 → 回收）
            var slotType = slot == 0 ? ToolItemType.Yellow : ToolItemType.Blue;
            StartCoroutine(SpawnFullScreenSlotMsg(slotType));
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[F4额外槽] 异常: {ex}");
        }
    }

    /// <summary>
    /// 全屏弹窗：走游戏原生完整播放流程
    /// （UI Msg Crest Evolve prefab 的 Msg Control FSM：设置 Item 字符串变量="Socket 0/1"
    ///  → startState=Init → StringSwitch 发 SOCKET 1/2 → Set Socket 写标题/正文/前缀本地化文字
    ///  → Audio Player Actor → Icon Evolve 播放动画+粒子+音效+震动）
    /// </summary>
    private System.Collections.IEnumerator SpawnFullScreenSlotMsg(ToolItemType slotType)
    {
        var evolvePrefab = AssetChainFixer.ChainFixer.LoadPrefabFixed(
            "prompts_assets_all.bundle",
            "Assets/Prefabs/UI/Messages/UI Msg Crest Evolve.prefab",
            "F4额外槽evolve");
        if (evolvePrefab == null)
        {
            string socketName = slotType == ToolItemType.Yellow ? "Socket 0" : "Socket 1";
            Plugin.ShowNotification($"解锁 {socketName}！", 3f);
            yield break;
        }

        var evo = UnityEngine.Object.Instantiate(evolvePrefab);
        evo.SetActive(false);
        evo.transform.position = new UnityEngine.Vector3(0f, 0f, 0f);

        var fsmComp = evo.GetComponentsInChildren<PlayMakerFSM>(true)
            .FirstOrDefault(f => f.FsmName == "Msg Control");
        if (fsmComp != null)
        {
            var itemVar = fsmComp.FsmVariables.GetFsmString("Item");
            if (itemVar != null)
            {
                string branch = slotType == ToolItemType.Yellow ? "Socket 0" : "Socket 1";
                itemVar.Value = branch;
                Plugin.Log.LogInfo($"[F4额外槽] FSM '{fsmComp.FsmName}' 设 Item='{branch}'，激活后 Init→StringSwitch→{(slotType == ToolItemType.Yellow ? "SOCKET 1" : "SOCKET 2")} 分支");
            }
            else Plugin.Log.LogWarning("[F4额外槽] FSM 无 Item 字符串变量");
        }
        else Plugin.Log.LogWarning("[F4额外槽] 未找到 Msg Control FSM");

        Plugin.Log.LogInfo("[F4额外槽] 激活 evolve prefab，Msg Control FSM startState=Init 自动演出");
        evo.SetActive(true);
        StartCoroutine(DiagnoseBg(evo));
        StartCoroutine(HideEvolveVisualLater(evo, fsmComp));
    }

    private System.Collections.IEnumerator DiagnoseBg(GameObject evo)
    {
        yield return new UnityEngine.WaitForSeconds(0.5f);
        if (evo == null) yield break;
        var bg = evo.transform.Find("BG");
        if (bg == null)
        {
            Plugin.Log.LogWarning("[F4额外槽] 未找到 BG 子物体");
            yield break;
        }
        var sr = bg.GetComponent<SpriteRenderer>();
        var anim = bg.GetComponent<Animator>();
        var fsm = bg.GetComponent<PlayMakerFSM>();
        Plugin.Log.LogInfo($"[F4额外槽] BG pos={bg.position} active={bg.gameObject.activeSelf} sr={(sr != null ? $"sprite={sr.sprite?.name} enabled={sr.enabled} color={sr.color} sortingL={sr.sortingLayerName} order={sr.sortingOrder}" : "NULL")} anim={(anim != null ? $"ctrl={anim.runtimeAnimatorController?.name} enabled={anim.enabled}" : "NULL")} fsm={(fsm != null ? $"{fsm.FsmName}/{fsm.ActiveStateName}" : "NULL")}");
        foreach (var c in bg.GetComponents<Component>())
            Plugin.Log.LogInfo($"[F4额外槽] BG 组件: {c.GetType().Name}");
    }

    private System.Collections.IEnumerator HideEvolveVisualLater(GameObject evo, PlayMakerFSM controlFsm)
    {
        yield return new UnityEngine.WaitForSeconds(7.5f);
        if (evo == null) yield break;

        Plugin.Log.LogInfo("[F4额外槽] 演出超时，发 PRESS 让 FSM 走完还原流程 (Detect→Down→Done)");
        if (controlFsm != null)
        {
            try
            {
                controlFsm.SendEvent("PRESS");
                Plugin.Log.LogInfo($"[F4额外槽] 已发 PRESS，当前状态: {controlFsm.Fsm.ActiveStateName}");
            }
            catch (System.Exception ex) { Plugin.Log.LogWarning($"[F4额外槽] 发 PRESS 异常: {ex.Message}"); }
        }

        float t = 0f;
        while (t < 3f && evo != null)
        {
            t += Time.deltaTime;
            if (controlFsm != null && controlFsm.Fsm.ActiveStateName == "Done")
            {
                Plugin.Log.LogInfo("[F4额外槽] FSM 已到 Done（还原完成）");
                break;
            }
            yield return null;
        }

        RestoreMsgInputState();

        if (evo != null)
        {
            evo.SetActive(false);
            UnityEngine.Object.Destroy(evo);
            Plugin.Log.LogInfo("[F4额外槽] evolve 视觉已回收");
        }
    }

    /// <summary>兜底还原弹窗造成的输入/暂停阻塞（原生 Done 状态走 UIMsgProxy.SetIsInMsg(false)）。</summary>
    private static void RestoreMsgInputState()
    {
        try
        {
            var proxies = UnityEngine.Object.FindObjectsOfType<UIMsgProxy>(true);
            foreach (var p in proxies)
            {
                p.SetIsInMsg(false);
                Plugin.Log.LogInfo($"[F4额外槽] 兜底还原 UIMsgProxy '{p.name}' SetIsInMsg(false)");
            }
            if (proxies.Length == 0)
                Plugin.Log.LogInfo("[F4额外槽] 未找到 UIMsgProxy，跳过兜底还原");

            try
            {
                var pd = PlayerData.instance;
                var inp = InventoryPaneInput.IsInputBlocked;
                var pickup = CollectableItemPickup.IsPickupPaused;
                Plugin.Log.LogInfo($"[F4额外槽] 还原后状态: disablePause={pd.disablePause} IsInputBlocked={inp} IsPickupPaused={pickup}");
            }
            catch (System.Exception ex2) { Plugin.Log.LogWarning($"[F4额外槽] 还原后状态读取异常: {ex2.Message}"); }

            try
            {
                if (PlayerData.instance != null && PlayerData.instance.disablePause)
                {
                    PlayerData.instance.disablePause = false;
                    Plugin.Log.LogInfo("[F4额外槽] 强制还原 disablePause=false（FSM 未走完 SetIsInMsg 还原链）");
                }
            }
            catch (System.Exception ex3) { Plugin.Log.LogWarning($"[F4额外槽] 强制还原 disablePause 异常: {ex3.Message}"); }
        }
        catch (System.Exception ex) { Plugin.Log.LogWarning($"[F4额外槽] 兜底还原异常: {ex.Message}"); }
    }

    /// <summary>逐帧转储 FSM 状态（仅打印变化）与 Icon 爆炸对象激活状态，定位演出卡点（调试用）。</summary>
    private System.Collections.IEnumerator TraceSlotFsm(GameObject go, PlayMakerFSM controlFsm)
    {
        if (go == null) yield break;
        string outPath = Path.Combine(BepInEx.Paths.PluginPath, $"crest_evolve_trace_{DateTime.Now:HHmmssfff}.txt");
        DumpGameObjectStructure(go, outPath);
        var icon = go.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Icon");
        var targetAnim = go.transform.Find("Tool_Socket_Evolve_lvl1")?.GetComponent<Animator>();
        string lastState = null;
        for (int frame = 1; frame <= 90; frame++)
        {
            yield return null;
            if (go == null) { Plugin.Log.LogInfo("[F4额外槽] 弹窗已销毁"); yield break; }
            string state = controlFsm != null ? (controlFsm.Fsm.ActiveStateName ?? "none") : "noCtrl";
            string iconActive = icon != null ? icon.gameObject.activeInHierarchy.ToString() : "noIcon";
            string animInfo = "noLvl1Anim";
            if (targetAnim != null)
            {
                var st = targetAnim.GetCurrentAnimatorStateInfo(0);
                string iconSprite = "noIcon";
                if (icon != null)
                {
                    var sr = icon.GetComponent<SpriteRenderer>();
                    iconSprite = sr == null ? "noSR" : (sr.sprite != null ? sr.sprite.name : "nullSprite");
                }
                animInfo = $"animOn={targetAnim.isActiveAndEnabled} normTime={st.normalizedTime:F2} stateHash={st.fullPathHash} clip={targetAnim.GetCurrentAnimatorClipInfo(0).Length} icon={iconSprite}";
            }
            string line = $"state={state} icon={iconActive} | {animInfo}";
            if (state != lastState || frame <= 3 || frame % 10 == 0)
            {
                Plugin.Log.LogInfo($"[F4额外槽] 帧{frame}: {line}");
                lastState = state;
            }
            if (state == "Done" || state == "Finished" || state == "Stop") break;
        }
    }

    /// <summary>导出 GameObject 完整结构（树、组件、FSM 变量、Animator、Sprite 等）到文本文件。</summary>
    private void DumpGameObjectStructure(GameObject root, string outputPath)
    {
        if (root == null) return;
        try
        {
            using (var writer = new StreamWriter(outputPath, false, Encoding.UTF8))
            {
                writer.WriteLine($"=== GameObject 结构导出: {root.name} ===");
                writer.WriteLine($"时间: {DateTime.Now:HH:mm:ss.fff}");
                writer.WriteLine($"Active Self: {root.activeSelf}  Active In Hierarchy: {root.activeInHierarchy}");
                writer.WriteLine($"场景: {root.scene.name}");
                writer.WriteLine($"层: {LayerMask.LayerToName(root.layer)}");
                writer.WriteLine();
                DumpTransformRecursive(root.transform, writer, 0);
            }
            Plugin.Log.LogInfo($"[结构导出] 已保存到: {outputPath}");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[结构导出] 失败: {ex}");
        }
    }

    private void DumpTransformRecursive(Transform t, StreamWriter writer, int depth)
    {
        string indent = new string(' ', depth * 2);
        writer.WriteLine($"{indent}[{depth}] ─ {t.name}");
        writer.WriteLine($"{indent}   Position: {t.localPosition}  Rotation: {t.localEulerAngles}  Scale: {t.localScale}");
        writer.WriteLine($"{indent}   Active Self: {t.gameObject.activeSelf}  Active In Hierarchy: {t.gameObject.activeInHierarchy}");
        writer.WriteLine($"{indent}   Tag: {t.tag}  Layer: {LayerMask.LayerToName(t.gameObject.layer)}");

        var components = t.GetComponents<Component>();
        writer.WriteLine($"{indent}   Components ({components.Length}):");
        foreach (var comp in components)
        {
            if (comp == null)
            {
                writer.WriteLine($"{indent}     - (missing)");
                continue;
            }
            string compType = comp.GetType().FullName;
            writer.WriteLine($"{indent}     - {compType}");
            DumpComponentDetail(comp, writer, indent + "       ");
        }

        foreach (Transform child in t)
        {
            DumpTransformRecursive(child, writer, depth + 1);
        }
    }

    private void DumpComponentDetail(Component comp, StreamWriter writer, string indent)
    {
        try
        {
            if (comp is Animator anim)
            {
                writer.WriteLine($"{indent}Animator:");
                writer.WriteLine($"{indent}  Controller: {(anim.runtimeAnimatorController != null ? anim.runtimeAnimatorController.name : "NULL")}");
                writer.WriteLine($"{indent}  Avatar: {(anim.avatar != null ? anim.avatar.name : "NULL")}");
                writer.WriteLine($"{indent}  Root Motion: {anim.applyRootMotion}");
                writer.WriteLine($"{indent}  Update Mode: {anim.updateMode}");
                writer.WriteLine($"{indent}  Enabled: {anim.enabled}");
                if (anim.runtimeAnimatorController != null)
                {
                    var layers = anim.layerCount;
                    writer.WriteLine($"{indent}  Layers: {layers}");
                    for (int i = 0; i < layers; i++)
                    {
                        writer.WriteLine($"{indent}    Layer[{i}]: {anim.GetLayerName(i)}  weight={anim.GetLayerWeight(i)}");
                    }
                    var parameters = anim.parameters;
                    writer.WriteLine($"{indent}  Parameters: {parameters.Length}");
                    foreach (var p in parameters)
                    {
                        writer.WriteLine($"{indent}    {p.name} ({p.type}) = {GetAnimatorParamValue(anim, p)}");
                    }
                }
                return;
            }

            if (comp is SpriteRenderer sr)
            {
                writer.WriteLine($"{indent}SpriteRenderer:");
                writer.WriteLine($"{indent}  Sprite: {(sr.sprite != null ? sr.sprite.name : "NULL")}");
                writer.WriteLine($"{indent}  Color: {sr.color}");
                writer.WriteLine($"{indent}  Material: {(sr.sharedMaterial != null ? sr.sharedMaterial.name : "NULL")}");
                writer.WriteLine($"{indent}  Sorting Layer: {sr.sortingLayerName}  Order: {sr.sortingOrder}");
                writer.WriteLine($"{indent}  Flip X: {sr.flipX}  Flip Y: {sr.flipY}");
                return;
            }

            if (comp is PlayMakerFSM fsm)
            {
                writer.WriteLine($"{indent}PlayMakerFSM:");
                writer.WriteLine($"{indent}  FSM Name: {fsm.FsmName}");
                writer.WriteLine($"{indent}  Active State: {fsm.ActiveStateName}");
                writer.WriteLine($"{indent}  Start State: {(fsm.Fsm != null ? fsm.Fsm.StartState : "NULL")}");
                writer.WriteLine($"{indent}  Enabled: {fsm.enabled}");
                writer.WriteLine($"{indent}  Variables:");
                if (fsm.Fsm != null && fsm.Fsm.Variables != null)
                {
                    var vars = fsm.Fsm.Variables;
                    foreach (var f in vars.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (!f.FieldType.Name.StartsWith("Fsm")) continue;
                        var v = f.GetValue(vars);
                        if (v == null) continue;
                        string val = GetFsmVariableValue(v);
                        writer.WriteLine($"{indent}    {f.Name} ({f.FieldType.Name}) = {val}");
                    }
                }
                return;
            }

            if (comp is Canvas canvas)
            {
                writer.WriteLine($"{indent}Canvas:");
                writer.WriteLine($"{indent}  Render Mode: {canvas.renderMode}");
                writer.WriteLine($"{indent}  World Camera: {(canvas.worldCamera != null ? canvas.worldCamera.name : "NULL")}");
                writer.WriteLine($"{indent}  Pixel Perfect: {canvas.pixelPerfect}");
                writer.WriteLine($"{indent}  Sort Order: {canvas.sortingOrder}");
                writer.WriteLine($"{indent}  Scale Factor: {canvas.scaleFactor}");
                return;
            }

            if (comp is AudioSource au)
            {
                writer.WriteLine($"{indent}AudioSource:");
                writer.WriteLine($"{indent}  Clip: {(au.clip != null ? au.clip.name : "NULL")}");
                writer.WriteLine($"{indent}  Loop: {au.loop}  Mute: {au.mute}");
                writer.WriteLine($"{indent}  Volume: {au.volume}  Pitch: {au.pitch}");
                return;
            }

            if (comp is ParticleSystem ps)
            {
                writer.WriteLine($"{indent}ParticleSystem:");
                writer.WriteLine($"{indent}  Enabled: {ps.isPlaying}");
                writer.WriteLine($"{indent}  Main: startLifetime={ps.main.startLifetime.constant}  startSpeed={ps.main.startSpeed.constant}");
                writer.WriteLine($"{indent}  Emission: rate={ps.emission.rateOverTime.constant}  enabled={ps.emission.enabled}");
                return;
            }
        }
        catch (Exception ex)
        {
            writer.WriteLine($"{indent}  [组件详情输出异常: {ex.Message}]");
        }
    }

    private string GetAnimatorParamValue(Animator anim, AnimatorControllerParameter p)
    {
        try
        {
            switch (p.type)
            {
                case AnimatorControllerParameterType.Float: return anim.GetFloat(p.nameHash).ToString("F3");
                case AnimatorControllerParameterType.Int: return anim.GetInteger(p.nameHash).ToString();
                case AnimatorControllerParameterType.Bool: return anim.GetBool(p.nameHash).ToString();
                case AnimatorControllerParameterType.Trigger: return "Trigger";
                default: return "?";
            }
        }
        catch { return "获取失败"; }
    }

    private string GetFsmVariableValue(object v)
    {
        if (v == null) return "null";
        try
        {
            var prop = v.GetType().GetProperty("Value");
            if (prop != null)
            {
                var val = prop.GetValue(v);
                return val != null ? val.ToString() : "null";
            }
            var field = v.GetType().GetField("Value");
            if (field != null)
            {
                var val = field.GetValue(v);
                return val != null ? val.ToString() : "null";
            }
            return v.ToString();
        }
        catch { return "??"; }
    }

    private void TestGiveSkillsInOrder()
    {
        try
        {
            var pd = PlayerData.instance;
            if (pd == null)
            {
                Plugin.Log.LogError("[F4弹窗测试] PlayerData 为空");
                return;
            }

            // 测试序列：全部技能字段 + 全部纹章名，依次播放
            var crestNames = new List<string>();
            try
            {
                var all = ToolItemManager.GetAllCrests();
                if (all != null)
                    foreach (var c in all)
                        if (c != null && !string.IsNullOrEmpty(c.name))
                            crestNames.Add(c.name);
            }
            catch { }

            int total = _testSkillFields.Length + crestNames.Count;
            if (total == 0)
            {
                Plugin.Log.LogError("[F4弹窗测试] 测试序列为空");
                return;
            }
            int idx = _f4SelfGiveIndex % total;
            _f4SelfGiveIndex++;

            if (idx < _testSkillFields.Length)
            {
                string field = _testSkillFields[idx];
                // 重置 pd 字段（含关联字段），保证每次都能看到弹窗
                ResetPdBoolForTest(pd, field);
                if (field == "HasSeenEvaHeal")
                    ResetPdBoolForTest(pd, "HasBoundCrestUpgrader");
                Plugin.Log.LogInfo($"[F4弹窗测试] 技能 #{_f4SelfGiveIndex}/{total}: {field}");
                StartingAbilityPicker.StartingAbilityPickerAPI.GiveSkill(field);
            }
            else
            {
                string crestName = crestNames[idx - _testSkillFields.Length];
                Plugin.Log.LogInfo($"[F4弹窗测试] 纹章 #{_f4SelfGiveIndex}/{total}: {crestName}");
                StartingAbilityPicker.OfficialMsgHelper.ShowToolCrestByName(crestName);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[F4弹窗测试] 异常: {ex}");
        }
    }

    private static void ResetPdBoolForTest(PlayerData pd, string field)
    {
        try
        {
            var fi = typeof(PlayerData).GetField(field, BindingFlags.Instance | BindingFlags.Public);
            if (fi != null && fi.FieldType == typeof(bool))
                fi.SetValue(pd, false);
        }
        catch { }
    }

    // F4 测试：轮流显示全部纹章的原生获得弹窗（复用正式机制的 CrestPopupHelper）
    private void TestShowCrestPopup()
    {
        try
        {
            var all = ToolItemManager.GetAllCrests();
            if (all == null || all.Count == 0)
            {
                Plugin.Log.LogError("[纹章弹窗] ToolItemManager 无纹章清单");
                return;
            }

            var crest = all[_crestPopupIndex % all.Count];
            _crestPopupIndex++;

            StartingAbilityPicker.OfficialMsgHelper.ShowToolCrest(crest, () => Plugin.Log.LogInfo("[纹章弹窗] 弹窗结束"));
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[纹章弹窗] 异常: {ex}");
        }
    }

    private void TestUnlockEvaHeal()
    {
        var pd = PlayerData.instance;
        if (pd == null)
        {
            Plugin.Log.LogError("PlayerData 不可用");
            return;
        }

        // 1. 基础弹窗标记
        pd.HasSeenEvaHeal = true;

        // 2. 纹章升级相关（伊娃是纹章升级者）
        pd.HasBoundCrestUpgrader = true;

        pd.CrestUpgraderOfferedFinal = true;
        pd.CrestPreUpgradeTalked = true;
        pd.CrestTalkedPurpose = true;
        pd.CrestUpgraderTalkedSnare = true;
        pd.CrestTalkedPurpose = true;
        pd.CrestUpgraderTalkedSnare = true;

        // 4. 教堂关闭标志（可能关联剧情）
        pd.chapelClosed_reaper = true;
        pd.chapelClosed_wanderer = true;
        pd.chapelClosed_beast = true;
        pd.chapelClosed_witch = true;
        pd.chapelClosed_toolmaster = true;
        pd.chapelClosed_shaman = true;

        // 5. 完成记忆标志
        pd.completedMemory_reaper = true;
        pd.completedMemory_wanderer = true;
        pd.completedMemory_beast = true;
        pd.completedMemory_witch = true;
        pd.completedMemory_toolmaster = true;
        pd.completedMemory_shaman = true;

        // 6. 解锁 Tools 中的多个可能名称
        string[] toolNames = { "EvaHeal", "Sylphsong", "Slythsong" };
        foreach (var name in toolNames)
        {
            try
            {
                var tool = pd.Tools.GetData(name);
                tool.IsUnlocked = true;
                tool.AmountLeft = 1;
                pd.Tools.SetData(name, tool);
            }
            catch
            {
            }
        }

        // 7. 设置 Collectables 中的 EvaHeal 物品
        try
        {
            var collectable = pd.Collectables.GetData("EvaHeal");
            collectable.Amount = 1;
            pd.Collectables.SetData("EvaHeal", collectable);
        }
        catch
        {
        }

        // 8. 触发自动存档标记 GAINED_SLYTHSONG
        try
        {
            var autoSaveType = Type.GetType("AutoSaveManager, Assembly-CSharp");
            var saveMethod = autoSaveType?.GetMethod("Save", new[] { typeof(Enum) });
            var autoSaveNameType = Type.GetType("AutoSaveName, Assembly-CSharp");
            var gainedField = autoSaveNameType?.GetField("GAINED_SLYTHSONG");
            if (saveMethod != null && gainedField != null)
            {
                saveMethod.Invoke(null, new object[] { gainedField.GetValue(null) });
            }
            else
            {
            }
        }
        catch
        {
        }

        // 9. 触发物品栏选择
        try
        {
            var helperType = Type.GetType("InventoryCollectableItemSelectionHelper, Assembly-CSharp");
            if (helperType != null)
            {
                var lastSelectionProp = helperType.GetProperty("LastSelectionUpdate", BindingFlags.Public | BindingFlags.Static);
                if (lastSelectionProp != null)
                {
                    var selectionType = helperType.GetNestedType("SelectionType");
                    if (selectionType != null)
                    {
                        var evaHealValue = Enum.Parse(selectionType, "EvaHeal");
                        lastSelectionProp.SetValue(null, evaHealValue);
                    }
                }
            }
        }
        catch
        {
        }

        // 10. 尝试直接增加丝线（测试触发）
        try
        {
            var hc = HeroController.instance;
            if (hc != null)
            {
                hc.AddSilk(1, true);
            }
        }
        catch
        {
        }
    }

    /// <summary>
    /// [调试] F4 依次触发 LoreTable 中每条文本的原生弹窗（每按一次下一条，循环），
    /// 验证跨地图调用原生石碑流程（Language.Get -> DialogueBox）。
    /// </summary>
    private static int _loreTestIndex = -1;

    /// <summary>F4 交替发放计数（0=面具, 1=丝轴）。</summary>
    private static int _f4SelfGiveIndex = 0;

    private void TestLoreDialogue()
    {
        var table = LoreRandomizer.LoreTable;
        if (table.Length == 0)
        {
            return;
        }
        _loreTestIndex = (_loreTestIndex + 1) % table.Length;
        var entry = table[_loreTestIndex];
        LoreRandomizer.ShowLoreDialogue(entry.Key, entry.Sheet);
    }

    // ========== F4：全量精灵集图标搜索 ==========
    /// <summary>
    /// 图标搜索组定义：每组一个概念，主关键词为最代表性单词（优先），
    /// 只有当特征点不明显时才追加备选词。全部忽略大小写匹配 sprite 资源名。
    /// 可随时增删，同名资源同名已由 SpriteCache 去重（同名取第一个）。
    /// </summary>
    private static readonly (string label, string[] keys)[] SpriteSearchGroups =
    {
        // 提示 / 交互
        ("prompt",  new[] { "prompt" }),
        // 攻击方向（上/左/右劈）
        ("slash",   new[] { "slash" }),
        // 针刺类武器
        ("needle",  new[] { "needle" }),
        // 货币 Geo（念珠）
        ("geo",     new[] { "geo" }),
        ("rosary",  new[] { "rosary", "bead" }),
        // 收费机 / 车站收费
        ("toll",    new[] { "toll" }),
        // 地图（地图机 / 地图奖励）
        ("map",     new[] { "map" }),
        // 任务墙 / 任务板
        ("quest",   new[] { "quest" }),
        // 车站（车站 / 铃兽站）
        ("station", new[] { "station" }),
        ("bench",   new[] { "bench" }),
        ("bell",    new[] { "bell" }),
        // 神龛 / 教堂
        ("shrine",  new[] { "shrine", "chapel" }),
        // 门锁 / 门前锁
        ("lock",    new[] { "lock", "door" }),
        // 丝线 / 丝相关奖励
        ("silk",    new[] { "silk" }),
        // 甲壳 / 碎裂碎片
        ("shell",   new[] { "shell", "shard" }),
        // 丝轴碎片 / 上限提升
        ("spool",   new[] { "spool" }),
        // 纹章
        ("crest",   new[] { "crest" }),
        // 面具 / 面具碎片
        ("mask",    new[] { "mask" }),
        // 心 / 生命
        ("heart",   new[] { "heart" }),
        // 钥匙
        ("key",     new[] { "key" }),
        // 记忆 / 记忆珠
        ("memory",  new[] { "memory" , "orb" }),
    };

    // ================= 图标筛选器 =================
    /// <summary>F4 筛选器候选名单：(类别, sprite名)。按此顺序逐个展示，勾选需要者。</summary>
    private static readonly (string cat, string name)[] SpritePickCandidates =
    {
        // 提示类
        ("prompt", "Cursed_item_prompt"),
        ("prompt", "Map_prompt"),
        ("prompt", "Tools_prompt"),
        ("prompt", "Crest_prompt"),
        ("prompt", "Journal_Prompt"),
        ("prompt", "Deep_Memory_Prompt"),
        ("prompt", "kings_brand_prompt"),
        ("prompt", "Wall_Jump_Prompt"),
        ("prompt", "Needolin_Prompt"),
        ("prompt", "dreamer_mask_prompt"),
        ("prompt", "Materium_Prompt"),
        ("prompt", "prompt_silkheart"),
        // 针刺
        ("needle", "T_longneedle"),
        ("needle", "S_needle_throw"),
        ("needle", "cradle_needle"),
        ("needle", "Curse_dark_needle"),
        ("needle", "prompt_hornet_needle_throw"),
        // 念珠
        ("rosary", "I_rosary_icon_clean"),
        ("rosary", "I_rosary_icon_big_clean"),
        ("rosary", "I_rosary_icon_mid_clean"),
        ("rosary", "I_rosary_icon_etched"),
        ("rosary", "tiny_icon_rosary"),
        ("rosary", "rosary_rock_type_base"),
        ("rosary", "T_rosary_magnet"),
        ("rosary", "tiny_tool_icon_rosary_cannon"),
        ("rosary", "rosary_cache_bell"),
        // 收费
        ("toll", "Toll_machine_silk_ration"),
        ("toll", "Toll_cap0003"),
        ("toll", "Toll_cog0006"),
        ("toll", "bellbench_toll_machine"),
        ("toll", "bellbench_toll_machine_map"),
        // 地图
        ("map", "I_map"),
        ("map", "I_map_type_02"),
        ("map", "I_quill_and_map"),
        ("map", "map_arrow"),
        ("map", "No_Map_symbol"),
        ("map", "quest_map_icon_round_style"),
        ("map", "Shop_map_icon__weavehome"),
        ("map", "gramaphone"),
        // 任务
        ("quest", "quest_dot_full"),
        ("quest", "quest_dot_empty"),
        ("quest", "Kill_quest_icon"),
        ("quest", "goop_quest_icon"),
        ("quest", "hunters_nest_quest_icon"),
        ("quest", "Steel_Servant_Quest_marker"),
        ("quest", "Mr_Mushroom_quest_notch"),
        ("quest", "quest_map_icon_round_style"),
        // 车站
        ("station", "pin_stag_station_shop_icon"),
        ("station", "pin_tube_station_shop_icon"),
        ("station", "pin_stag_station"),
        ("station", "pin_tube_station"),
        // 长椅
        ("bench", "Bench_item_prompt"),
        ("bench", "pin_bench_shop_icon"),
        ("bench", "pin_bench"),
        ("bench", "pin_bench_bell"),
        ("bench", "town_bench_lit"),
        // 铃
        ("bell", "Hornet_icon_bell_clapper"),
        ("bell", "Bell_Hub_Icons0001"),
        ("bell", "Bell_Hub_Icons0006"),
        ("bell", "Hornet_bellway_token"),
        ("bell", "Hornet_bellway_token_duo"),
        ("bell", "QI_Main_bell_beast"),
        ("bell", "QI_Main_bellshrines"),
        ("bell", "bell_shrine_single_silk"),
        ("bell", "crawbell"),
        ("bell", "Music_box_bell0000"),
        ("bell", "bell_sub_sign"),
        // 神龛
        ("shrine", "QI_Main_bellshrines"),
        ("shrine", "quest_map_icon_songshrines"),
        ("shrine", "Bellshrine"),
        ("shrine", "Belltown_Shrine"),
        ("shrine", "bell_shrine_single_silk"),
        // 锁
        ("lock", "UI_tool_slot_locked_fill"),
        ("lock", "Tool_slot_lock_ring"),
        ("lock", "trap_door_circle"),
        ("lock", "trapdoor_lever"),
        ("lock", "music_box_bell_lock0000"),
        // 丝
        ("silk", "silk_heart_inv_icon"),
        ("silk", "icon_silk_materium"),
        ("silk", "silk_heart_scene"),
        ("silk", "spell_core_slim_silk_heart_ss"),
        ("silk", "Silk_Grub_large_cocoon0000"),
        ("silk", "Silk_Grub_small_cocoon"),
        ("silk", "Toll_machine_silk_ration"),
        ("silk", "silk_bomb_prompt"),
        ("silk", "UI_halo_silk0000"),
        // 甲壳
        ("shell", "Shell_shard_icon"),
        ("shell", "I_shell_shard_icon_large"),
        ("shell", "Icon_Beast_Shard"),
        ("shell", "T_shell_satchel"),
        ("shell", "T_sting_shard"),
        ("shell", "tiny_tool_icon_sting_shard"),
        ("shell", "HUD_shard_shop"),
        ("shell", "glass_shards"),
        // 丝轴
        ("spool", "Hornet_Spool_Upgrade_Shop_Icon"),
        ("spool", "Hornet_Spool_Upgrade_Shop_Icon_full"),
        ("spool", "Hornet_Spool_Upgrade_Shop_Icon_Heart"),
        ("spool", "Inv_spool_backboard_ss"),
        ("spool", "T_focus_spool"),
        ("spool", "T_spool_bar_extender"),
        ("spool", "spool_upgrade_pickup"),
        ("spool", "Thread_Spool_spinner0000"),
        // 纹章
        ("crest", "C_crest"),
        ("crest", "M_crest"),
        ("crest", "Tools_UI_Crest_Change"),
        ("crest", "crest_blank"),
        ("crest", "cursed_crest_animated0001"),
        ("crest", "Bell_Hub_Crest_Icon_Animated0000"),
        ("crest", "Crest_prompt"),
        // 面具
        ("mask", "Hornet_T_fractured_mask"),
        ("mask", "bestiary_icon_frame_mask"),
        ("mask", "dreamer_mask_prompt"),
        ("mask", "soft_masker"),
        ("mask", "sand_blown_mask"),
        ("mask", "Goomba_mask_chunk_01"),
        // 心
        ("heart", "Heart_Piece_01"),
        ("heart", "Heart_Piece_02"),
        ("heart", "Heart_Piece_03"),
        ("heart", "silk_heart_inv_icon"),
        ("heart", "bone_heart_egg"),
        ("heart", "cog_heart_pieces"),
        ("heart", "heart_prompt_clover"),
        ("heart", "heart_prompt_flower"),
        ("heart", "heart_prompt_coral"),
        ("heart", "coral_king_heart_icon0000"),
        // 钥匙
        ("key", "dock_key"),
        ("key", "I_key_architect"),
        ("key", "I_key_whiteward"),
        ("key", "I_chute_key_whiteward"),
        ("key", "I_slab_key"),
        ("key", "I_slab_key_brass"),
        ("key", "I_slab_key_gold"),
        ("key", "I_slab_key_old"),
        ("key", "belltown_house_key"),
        ("key", "rusted_cage_key"),
        // 记忆
        ("memory", "soul_orb"),
        ("memory", "soul_orb_generic"),
        ("memory", "black_soul_orb"),
        ("memory", "new_item_orb"),
        ("memory", "sc_grey_orb_single"),
        ("memory", "memory_large_flat"),
        ("memory", "memory_large_flat_outer"),
        ("memory", "red_memory_bottle"),
        ("memory", "UI_halo_orb_beams"),
    };

    private bool _pickerActive;
    private int _pickerIndex;
    private readonly List<string> _pickedNames = new();

    // 第二阶段：备注器（对勾选名单逐张输入用途备注）
    private bool _annotateActive;
    private int _annotateIndex;
    private string _noteText = "";
    private readonly List<(string name, string note)> _annotated = new();
    private List<string> _annotateList = new();

    private void PickCurrent(bool pick)
    {
        if (_pickerIndex >= SpritePickCandidates.Length) return;
        if (pick) _pickedNames.Add(SpritePickCandidates[_pickerIndex].name);
        _pickerIndex++;
        if (_pickerIndex >= SpritePickCandidates.Length)
            FinishPicker();
    }

    private void FinishPicker()
    {
        _pickerActive = false;
        string outPath = Path.Combine(BepInEx.Paths.PluginPath, "sprite_picked.txt");
        using (var writer = new StreamWriter(outPath, false))
            foreach (var n in _pickedNames)
                writer.WriteLine(n);

        // 自动进入第二阶段：备注器
        StartAnnotate(_pickedNames);
    }

    /// <summary>启动备注器：对传入的名单逐张展示，输入用途备注。</summary>
    private void StartAnnotate(List<string> names)
    {
        _annotateList = names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (_annotateList.Count == 0)
        {
            return;
        }
        _annotated.Clear();
        _annotateIndex = 0;
        _noteText = "";
        _annotateActive = true;
    }

    /// <summary>也可直接从 sprite_picked.txt 启动备注器。</summary>
    private void StartAnnotateFromFile()
    {
        string pickPath = Path.Combine(BepInEx.Paths.PluginPath, "sprite_picked.txt");
        if (!File.Exists(pickPath))
        {
            return;
        }
        var names = File.ReadAllLines(pickPath)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
        StartAnnotate(names);
    }

    private void SaveCurrentNote()
    {
        if (_annotateIndex >= _annotateList.Count) return;
        _annotated.Add((_annotateList[_annotateIndex], _noteText.Trim()));
        _annotateIndex++;
        _noteText = "";
        if (_annotateIndex >= _annotateList.Count)
            FinishAnnotate();
    }

    private void FinishAnnotate()
    {
        _annotateActive = false;
        string outPath = Path.Combine(BepInEx.Paths.PluginPath, "sprite_annotated.txt");
        using (var writer = new StreamWriter(outPath, false))
        {
            writer.WriteLine("#sprite_name|用途备注");
            foreach (var (name, note) in _annotated)
                writer.WriteLine($"{name}|{note}");
        }
    }

    private bool _isDumpingSprites;

    /// <summary>
    /// [调试] F4 图标筛选器：
    /// 1) 枚举 Addressables 全部 key，找出含关键词的图集/资源路径；
    /// 2) 运行时把匹配到的图集资源加载进内存（Addressables.LoadAssetAsync，不限类型）；
    /// 3) 加载完成后 SpriteCache 重建缓存，弹出筛选窗口逐张展示候选图标，勾选需要者，
    ///    全部完成后勾选名单写入 sprite_picked.txt。
    /// </summary>
    private IEnumerator DumpSpriteIconsCoroutine()
    {
        if (_isDumpingSprites)
        {
            yield break;
        }
        _isDumpingSprites = true;
        yield return StartCoroutine(LoadAtlasesThenStartPicker());
        _isDumpingSprites = false;
    }

    /// <summary>加载候选所需图集资源后激活筛选器（yield 与 try/catch 分离，故独立成协程）。</summary>
    private IEnumerator LoadAtlasesThenStartPicker()
    {
        // 1. 枚举 Addressables 全部 key
        var addrKeys = new List<string>();
        try
        {
            foreach (var locator in Addressables.ResourceLocators)
                foreach (object key in locator.Keys)
                    if (key is string sk && !string.IsNullOrEmpty(sk))
                        addrKeys.Add(sk);
        }
        catch { }

        // 2. 筛选出匹配关键词组的图集类 key（spriteatlas/bundle/图集资源），去重后逐个加载
        var atlasKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in SpriteSearchGroups)
        {
            foreach (var k in addrKeys)
            {
                if (!IsLikelyAtlasKey(k)) continue;
                bool match = false;
                foreach (var kw in group.keys)
                    if (k.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) { match = true; break; }
                if (match) atlasKeys.Add(k);
            }
        }

        int loadedOk = 0, loadedFail = 0;
        foreach (var k in atlasKeys)
        {
            AsyncOperationHandle<UnityEngine.Object> handle = default;
            try
            {
                handle = Addressables.LoadAssetAsync<UnityEngine.Object>(k);
            }
            catch
            {
                loadedFail++;
                continue;
            }

            float deadline = Time.realtimeSinceStartup + 20f;
            while (!handle.IsDone && Time.realtimeSinceStartup <= deadline)
                yield return null;
            if (handle.IsDone && handle.Status == AsyncOperationStatus.Succeeded)
                loadedOk++;
            else
            {
                loadedFail++;
                try { if (handle.IsValid()) Addressables.Release(handle); } catch { }
            }
        }

        // 3. 重置缓存后全量扫描已加载 sprite（图集 sprite 此时已就位）
        SpriteCache.Reset();
        SpriteCache.EnsureBuilt();

        // 4. 若已有勾选名单则直接进备注器，否则先进筛选器
        string pickPath = Path.Combine(BepInEx.Paths.PluginPath, "sprite_picked.txt");
        if (File.Exists(pickPath))
        {
            StartAnnotateFromFile();
        }
        else
        {
            _pickerActive = true;
            _pickerIndex = 0;
            _pickedNames.Clear();
        }
    }

    /// <summary>判断 Addressables key 是否可能是图集/资源包类资源（值得加载后枚举内部 sprite）。</summary>
    private static bool IsLikelyAtlasKey(string key)
    {
        string k = key;
        if (k.IndexOf(".spriteatlas", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        if (k.IndexOf(".bundle", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        // 资源文件夹 / Assets/ 前缀的资源也可能是 sprite 集合
        if (k.IndexOf("sprites/", StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    /// <summary>
    /// [调试] F4：找到 mode_select_Steel_Soul_HUD 子 Sprite，按 textureRect 从图集纹理中裁剪，
    /// 导出为独立 PNG 到插件目录，供外部配置引用。
    /// </summary>
    private IEnumerator ExportSteelModeIconCoroutine()
    {
        _isDumpingSprites = true;
        try
        {
            // 1. 直接尝试从缓存拿 sprite
            SpriteCache.EnsureBuilt();
            Sprite sprite = SpriteCache.Find("mode_select_Steel_Soul_HUD");

            // 2. 拿不到则枚举 Addressables 加载可能含它的图集（Area_Art），重建缓存再找
            if (sprite == null)
            {
                var atlasKeys = new List<string>();
                try
                {
                    foreach (var locator in Addressables.ResourceLocators)
                        foreach (object key in locator.Keys)
                        {
                            if (key is not string sk || string.IsNullOrEmpty(sk)) continue;
                            if (!IsLikelyAtlasKey(sk)) continue;
                            string low = sk.ToLowerInvariant();
                            if (low.Contains("area_art") || low.Contains("mode_select") || low.Contains("steel"))
                                atlasKeys.Add(sk);
                        }
                }
                catch { }

                foreach (var k in atlasKeys)
                {
                    AsyncOperationHandle<UnityEngine.Object> handle = default;
                    try { handle = Addressables.LoadAssetAsync<UnityEngine.Object>(k); }
                    catch { continue; }

                    float deadline = Time.realtimeSinceStartup + 20f;
                    while (!handle.IsDone && Time.realtimeSinceStartup <= deadline)
                        yield return null;
                    if (!(handle.IsDone && handle.Status == AsyncOperationStatus.Succeeded))
                    {
                        try { if (handle.IsValid()) Addressables.Release(handle); } catch { }
                        continue;
                    }
                }
                SpriteCache.Reset();
                SpriteCache.EnsureBuilt();
                sprite = SpriteCache.Find("mode_select_Steel_Soul_HUD");
            }

            if (sprite == null)
            {
                yield break;
            }

            // 3. 按 textureRect 裁剪并导出 PNG
            string outPath = Path.Combine(BepInEx.Paths.PluginPath, "steel_mode_icon.png");
            ExportSpriteToPng(sprite, outPath);
        }
        finally
        {
            _isDumpingSprites = false;
        }
    }

    /// <summary>把 Sprite 按 textureRect 区域从图集纹理裁剪为独立 PNG 写入文件。
    /// 图集纹理通常不可直接读取（not readable），故通过 RenderTexture 中转读回像素。</summary>
    private static void ExportSpriteToPng(Sprite s, string outPath)
    {
        if (s == null) return;
        Texture2D src = s.texture as Texture2D;
        if (src == null) { return; }

        Rect r = s.textureRect;
        int x = Mathf.FloorToInt(r.x);
        int y = Mathf.FloorToInt(r.y);
        int w = Mathf.CeilToInt(r.width);
        int h = Mathf.CeilToInt(r.height);

        Texture2D outTex = null;
        RenderTexture renderTex = null;
        RenderTexture prevActive = null;
        try
        {
            // 1. 把源纹理绘制进 RenderTexture（兼容不可读的压缩图集纹理）
            renderTex = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(src, renderTex);

            // 2. 从 RenderTexture 按裁剪区域 ReadPixels 读回
            prevActive = RenderTexture.active;
            RenderTexture.active = renderTex;
            outTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            outTex.ReadPixels(new Rect(x, y, w, h), 0, 0);
            outTex.Apply();
            RenderTexture.active = prevActive;
            prevActive = null;
            RenderTexture.ReleaseTemporary(renderTex);
            renderTex = null;

            byte[] png = outTex.EncodeToPNG();
            File.WriteAllBytes(outPath, png);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[sprite搜索] 裁剪失败: {ex.Message}");
        }
        finally
        {
            if (RenderTexture.active != prevActive && prevActive != null) RenderTexture.active = prevActive;
            if (renderTex != null) RenderTexture.ReleaseTemporary(renderTex);
            if (outTex != null) UnityEngine.Object.Destroy(outTex);
        }
    }
    private bool _isDumpingTransitions;

    /// <summary>导出时排除的场景名片段：旅行/载具/菜单/过场类场景加载后会自己驱动游戏状态，会卡死加载队列。
    /// 注意这些场景的"门"本来就是脚本化旅行过渡，不属于可行走的门连接。需要调整直接改这个列表。</summary>
    private static readonly string[] DumpSkipSubstrings =
        { "Travel", "Caravan", "Diving_Bell", "Menu", "Boot", "Preloader", "Intro", "Cutscene", "Credits" };

    private static bool IsDumpSkipped(string sceneName)
    {
        foreach (string s in DumpSkipSubstrings)
            if (sceneName.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    // ========== F3：扫描所有场景的检查点（拾取点/检查交互/收费机/车站长椅） ==========
    /// <summary>检查点扫描进行中标志：Plugin.OnSceneLoaded 据此跳过 mod 场景响应，避免动态生成物污染结果</summary>
    public static bool IsScanningChecks { get; private set; }

    private void DumpCheckPoints()
    {
        if (IsScanningChecks || _isDumpingTransitions)
        {
            return;
        }
        StartCoroutine(DumpCheckPointsCoroutine());
    }

    /// <summary>
    /// 串行逐个加载所有场景（先试 SceneManager，失败换 Addressables 交叉尝试），扫描
    /// CollectableItemPickup / NPCControlBase(检查) / 收费机 / BellBench 四类检查点，
    /// 每场景扫完立即写入并 Flush（边读边写），输出到 check_points_scan.txt。
    /// 建议在主菜单按 F3 触发。
    /// </summary>
    private IEnumerator DumpCheckPointsCoroutine()
    {
        IsScanningChecks = true;
        // 提高后台加载线程优先级，加快逐场景加载（结束后 finally 恢复）
        var oldLoadPriority = Application.backgroundLoadingPriority;
        Application.backgroundLoadingPriority = UnityEngine.ThreadPriority.High;
        // 抑制场景随机模组事件（反射，尽力而为）
        FieldInfo suppressField = Type.GetType("HKSilksong_Randomizer.RandomSceneLoader, HKSilksong_SceneRandomizer")
            ?.GetField("SuppressSceneEvents", BindingFlags.Public | BindingFlags.Static);
        suppressField?.SetValue(null, true);
        try
        {
            // 场景清单：优先 Build Settings（零依赖），数量异常时退回 Addressables 目录
            var sceneNames = new List<string>();
            try
            {
                int bsCount = SceneManager.sceneCountInBuildSettings;
                for (int i = 0; i < bsCount; i++)
                {
                    string p = SceneUtility.GetScenePathByBuildIndex(i);
                    if (!string.IsNullOrEmpty(p))
                        sceneNames.Add(Path.GetFileNameWithoutExtension(p));
                }
            }
            catch { }

            if (sceneNames.Count < 10)
            {
                sceneNames.Clear();
                try
                {
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var locator in Addressables.ResourceLocators)
                        foreach (object key in locator.Keys)
                            if (key is string s && s.StartsWith("Scenes/", StringComparison.OrdinalIgnoreCase) && seen.Add(s))
                                sceneNames.Add(s.Substring("Scenes/".Length));
                }
                catch { }
            }

            if (sceneNames.Count == 0)
            {
                Plugin.Log.LogError("[扫描] 无法枚举场景清单（Build Settings 与 Addressables 均为空）");
                yield break;
            }

            string outPath = Path.Combine(BepInEx.Paths.PluginPath, "check_points_scan.txt");
            int sceneCount = 0, pointCount = 0, failed = 0, excluded = 0;

            using (var writer = new StreamWriter(outPath, false))
            {
                writer.WriteLine("#type|scene|path|name|detail|key|x|y");
                writer.Flush();

                foreach (string sceneName in sceneNames)
                {
                    if (IsDumpSkipped(sceneName)) { excluded++; continue; }
                    Scene scene = SceneManager.GetSceneByName(sceneName);
                    bool weLoaded = false;
                    bool viaAddr = false;
                    AsyncOperationHandle<SceneInstance> addr = default;

                    if (!scene.IsValid() || !scene.isLoaded)
                    {
                        // 先试 SceneManager 激活加载
                        AsyncOperation op = null;
                        try { op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive); }
                        catch { }
                        if (op != null)
                        {
                            float deadline = Time.realtimeSinceStartup + 30f;
                            while (!op.isDone && Time.realtimeSinceStartup <= deadline)
                                yield return null;
                            if (op.isDone)
                            {
                                scene = SceneManager.GetSceneByName(sceneName);
                                if (scene.IsValid() && scene.isLoaded) weLoaded = true;
                            }
                        }

                        // 不行换 Addressables 激活加载
                        if (!weLoaded)
                        {
                            bool started = false;
                            try
                            {
                                addr = Addressables.LoadSceneAsync("Scenes/" + sceneName, LoadSceneMode.Additive, true, 100);
                                started = true;
                            }
                            catch { }
                            if (started)
                            {
                                float deadline = Time.realtimeSinceStartup + 30f;
                                while (!addr.IsDone && Time.realtimeSinceStartup <= deadline)
                                    yield return null;
                                if (addr.IsDone && addr.Status == AsyncOperationStatus.Succeeded)
                                {
                                    scene = addr.Result.Scene;
                                    weLoaded = true;
                                    viaAddr = true;
                                }
                                else
                                {
                                    try { if (addr.IsValid()) Addressables.Release(addr); } catch { }
                                }
                            }
                        }

                        if (!weLoaded)
                        {
                            failed++;
                            yield return null;
                            continue;
                        }
                    }

                    // 扫描前重置商店静态单例：ShopOwnerBase._spawnedShop 是全局唯一实例，
                    // 若不重置，后加载的场景会复用上一场景生成的商店 UI 而不再生成新实例。
                    ResetSpawnedShopSingleton();

                    // 扫描（边读边写：本场景扫完立即落盘）
                    try
                    {
                        int foundHere = ScanSceneCheckPoints(scene, sceneName, writer);
                        pointCount += foundHere;
                        sceneCount++;
                    }
                    catch
                    {
                        failed++;
                    }

                    if (weLoaded)
                    {
                        try
                        {
                            if (viaAddr) Addressables.UnloadSceneAsync(addr);
                            else if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
                        }
                        catch { }
                    }
                    writer.Flush();
                    yield return null;
                }
            }
        }
        finally
        {
            Application.backgroundLoadingPriority = oldLoadPriority;
            suppressField?.SetValue(null, false);
            IsScanningChecks = false;
        }
    }

    /// <summary>扫描单个场景，写出 Pickup / Lore / Toll / BellBench 四类检查点，返回条数。
    /// Pickup 的 key 与 PickupPatch.GetPickupKey 一致；Lore 的 key 与 LoreTriggerPatch 的 triggerId 一致。</summary>
    private static int ScanSceneCheckPoints(Scene scene, string sceneName, StreamWriter writer)
    {
        int count = 0;
        string stationInfo = MapStationUnlockPatch.TryGetStationBoolForScene(sceneName, out string stationBool)
            ? "station:" + stationBool : "";

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            // 1. 世界拾取点
            foreach (var pickup in root.GetComponentsInChildren<CollectableItemPickup>(true))
            {
                if (pickup == null) continue;
                string key;
                try { key = PickupPatch.GetPickupKey(pickup); }
                catch { key = $"{sceneName}_{pickup.gameObject.name}"; }
                string itemName = "";
                try { if (pickup.Item != null) itemName = pickup.Item.name; } catch { }
                Vector3 pos = pickup.transform.position;
                writer.WriteLine($"Pickup|{sceneName}|{GetObjectPath(pickup.transform)}|{pickup.gameObject.name}|{itemName}|{key}|{pos.x:F1}|{pos.y:F1}");
                count++;
            }

            // 2. 交互点：收费机（Toll）与石碑类（Lore）
            foreach (var npc in root.GetComponentsInChildren<NPCControlBase>(true))
            {
                if (npc == null) continue;
                string name = npc.gameObject.name;
                Vector3 pos = npc.transform.position;
                if (LoreTriggerPatch.IsExcludedTollName(name))
                {
                    writer.WriteLine($"Toll|{sceneName}|{GetObjectPath(npc.transform)}|{name}|{stationInfo}||{pos.x:F1}|{pos.y:F1}");
                    count++;
                    continue;
                }
                bool isInspect = false;
                try { isInspect = npc.InteractLabel == InteractableBase.PromptLabels.Inspect; } catch { }
                if (isInspect)
                {
                    string triggerId = $"loretrig:{sceneName}:{name}";
                    writer.WriteLine($"Lore|{sceneName}|{GetObjectPath(npc.transform)}|{name}||{triggerId}|{pos.x:F1}|{pos.y:F1}");
                    count++;
                }
            }

            // 3. 车站长椅（参考信息）
            foreach (var bench in root.GetComponentsInChildren<BellBench>(true))
            {
                if (bench == null) continue;
                Vector3 pos = bench.transform.position;
                writer.WriteLine($"BellBench|{sceneName}|{GetObjectPath(bench.transform)}|{bench.gameObject.name}|{stationInfo}||{pos.x:F1}|{pos.y:F1}");
                count++;
            }

            // ★ 4. 商店：搜索场景中静态存在的店主组件（ShopOwner / SimpleShopMenuOwner 等）
            // 每个店主对应一个商店，固定 12 个槽位
            foreach (var shopOwner in root.GetComponentsInChildren<ShopOwner>(true))
            {
                if (shopOwner == null) continue;
                string shopId = sceneName;
                for (int i = 0; i < 12; i++)
                {
                    string key = $"shop:{shopId}_{i}";
                    Vector3 pos = shopOwner.transform.position;
                    writer.WriteLine($"ShopSlot|{sceneName}|{GetObjectPath(shopOwner.transform)}|ShopSlot_{i}|Slot {i}|{key}|{pos.x:F1}|{pos.y:F1}");
                    count++;
                }
            }

            // ★ 5. Caravan / SimpleShopMenuOwner 体系（沙克拉、流动商人等）
            foreach (var simpleOwner in root.GetComponentsInChildren<SimpleShopMenuOwner>(true))
            {
                if (simpleOwner == null) continue;
                string shopId = sceneName;
                for (int i = 0; i < 12; i++)
                {
                    string key = $"shop:{shopId}_{i}";
                    Vector3 pos = simpleOwner.transform.position;
                    writer.WriteLine($"ShopSlot|{sceneName}|{GetObjectPath(simpleOwner.transform)}|ShopSlot_{i}|Slot {i}|{key}|{pos.x:F1}|{pos.y:F1}");
                    count++;
                }
            }

            // ★ 6. 特定 caravan 类型（它们可能不继承 SimpleShopMenuOwner，单独处理）
            foreach (var spider in root.GetComponentsInChildren<CaravanSpider>(true))
            {
                if (spider == null) continue;
                string shopId = sceneName;
                for (int i = 0; i < 12; i++)
                {
                    string key = $"shop:{shopId}_{i}";
                    Vector3 pos = spider.transform.position;
                    writer.WriteLine($"ShopSlot|{sceneName}|{GetObjectPath(spider.transform)}|ShopSlot_{i}|Slot {i}|{key}|{pos.x:F1}|{pos.y:F1}");
                    count++;
                }
            }
            foreach (var troupe in root.GetComponentsInChildren<CaravanTroupeHunter>(true))
            {
                if (troupe == null) continue;
                string shopId = sceneName;
                for (int i = 0; i < 12; i++)
                {
                    string key = $"shop:{shopId}_{i}";
                    Vector3 pos = troupe.transform.position;
                    writer.WriteLine($"ShopSlot|{sceneName}|{GetObjectPath(troupe.transform)}|ShopSlot_{i}|Slot {i}|{key}|{pos.x:F1}|{pos.y:F1}");
                    count++;
                }
            }
            foreach (var questShop in root.GetComponentsInChildren<SimpleQuestsShopOwner>(true))
            {
                if (questShop == null) continue;
                string shopId = sceneName;
                for (int i = 0; i < 12; i++)
                {
                    string key = $"shop:{shopId}_{i}";
                    Vector3 pos = questShop.transform.position;
                    writer.WriteLine($"ShopSlot|{sceneName}|{GetObjectPath(questShop.transform)}|ShopSlot_{i}|Slot {i}|{key}|{pos.x:F1}|{pos.y:F1}");
                    count++;
                }
            }
        }
        return count;
    }

    /// <summary>重置 ShopOwnerBase._spawnedShop 静态单例：销毁残留实例并置 null，
    /// 强制当前场景的 ShopOwner 认为"没有商店"而重新生成商店 UI，保证逐场景扫描不互相干扰。
    /// 逻辑收敛到 ShakraMerchantKeeper.ResetSpawnedShopSingleton，供扫描与运行时共用。</summary>
    private static void ResetSpawnedShopSingleton() => ShakraMerchantKeeper.ResetSpawnedShopSingleton();

    private static string GetObjectPath(Transform t)
    {
        var sb = new System.Text.StringBuilder(t.name);
        while (t.parent != null)
        {
            t = t.parent;
            sb.Insert(0, t.name + "/");
        }
        return sb.ToString();
    }

    private void DumpVanillaTransitions()
    {
        if (_isDumpingTransitions)
        {
            return;
        }
        StartCoroutine(DumpVanillaTransitionsCoroutine());
    }

    /// <summary>
    /// 场景清单优先取 Build Settings（为空则退回 Addressables 目录），逐个附加加载（激活），
    /// 读取所有 TransitionPoint 的原版目标（targetScene/entryPoint），超时/失败会等待恢复后继续，
    /// 去重后边扫边写到插件目录 vanilla_transitions.txt，格式：场景A|门A|场景B|门B（每对只输出一次）。
    /// 建议在主菜单按 F4 触发。
    /// </summary>
    private IEnumerator DumpVanillaTransitionsCoroutine()
    {
        _isDumpingTransitions = true;
        // 提高后台加载线程优先级，显著加快逐场景加载速度（导出结束后在 finally 恢复）
        var oldLoadPriority = Application.backgroundLoadingPriority;
        Application.backgroundLoadingPriority = UnityEngine.ThreadPriority.High;
        // 抑制场景随机模组的场景事件副作用（反射，尽力而为；模组不在也不影响导出）
        FieldInfo suppressField = Type.GetType("HKSilksong_Randomizer.RandomSceneLoader, HKSilksong_SceneRandomizer")
            ?.GetField("SuppressSceneEvents", BindingFlags.Public | BindingFlags.Static);
        suppressField?.SetValue(null, true);
        try
        {
            // 1. 场景清单：优先 Build Settings（零依赖），数量异常时退回 Addressables 目录
            var sceneNames = new List<string>();
            string source = "Build Settings";
            try
            {
                int bsCount = SceneManager.sceneCountInBuildSettings;
                for (int i = 0; i < bsCount; i++)
                {
                    string p = SceneUtility.GetScenePathByBuildIndex(i);
                    if (!string.IsNullOrEmpty(p))
                        sceneNames.Add(Path.GetFileNameWithoutExtension(p));
                }
            }
            catch { }

            if (sceneNames.Count < 10)
            {
                sceneNames.Clear();
                source = "Addressables";
                try
                {
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var locator in Addressables.ResourceLocators)
                        foreach (object key in locator.Keys)
                            if (key is string s && s.StartsWith("Scenes/", StringComparison.OrdinalIgnoreCase) && seen.Add(s))
                                sceneNames.Add(s.Substring("Scenes/".Length));
                }
                catch { }
            }

            if (sceneNames.Count == 0)
            {
                Plugin.Log.LogError("[Dump] 无法枚举场景清单（Build Settings 与 Addressables 均为空）");
                yield break;
            }
            bool viaAddrList = source == "Addressables";

            string outPath = Path.Combine(BepInEx.Paths.PluginPath, "vanilla_transitions.txt");
            var seenPairs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int sceneCount = 0, pairCount = 0, skipped = 0, excluded = 0;

            using (var writer = new StreamWriter(outPath, false))
            {
                writer.WriteLine("#sceneA|gateA|sceneB|gateB");

                // 分批并发：每批 10 个场景同时加载（激活），整批扫描、整批卸载
                const int BATCH_SIZE = 10;

                for (int batchStart = 0; batchStart < sceneNames.Count; batchStart += BATCH_SIZE)
                {
                    int batchLen = Math.Min(BATCH_SIZE, sceneNames.Count - batchStart);
                    var items = new List<(string name, Scene scene, AsyncOperation op, AsyncOperationHandle<SceneInstance> addr, bool weLoaded, bool viaAddr)>(batchLen);

                    // 1. 同批场景全部同时发起加载（不逐个等待）
                    for (int i = batchStart; i < batchStart + batchLen; i++)
                    {
                        string sceneName = sceneNames[i];

                        // 排除自驱动特殊场景，不参与加载
                        if (IsDumpSkipped(sceneName))
                        {
                            excluded++;
                            continue;
                        }

                        Scene scene = SceneManager.GetSceneByName(sceneName);
                        if (scene.IsValid() && scene.isLoaded)
                        {
                            items.Add((sceneName, scene, null, default, false, false));
                            continue;
                        }

                        AsyncOperation op = null;
                        AsyncOperationHandle<SceneInstance> addr = default;
                        bool started = false;
                        try
                        {
                            if (viaAddrList)
                                addr = Addressables.LoadSceneAsync("Scenes/" + sceneName, LoadSceneMode.Additive, true, 100);
                            else
                                op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
                            started = true;
                        }
                        catch
                        {
                            skipped++;
                        }
                        if (started && (op != null || viaAddrList))
                            items.Add((sceneName, default, op, addr, true, viaAddrList));
                        else if (started)
                            skipped++; // SceneManager 返回 null（场景不在 Build Settings）
                    }

                    // 2. 等本批全部完成（总超时 60 秒）
                    float deadline = Time.realtimeSinceStartup + 60f;
                    bool pending = true;
                    while (pending && Time.realtimeSinceStartup <= deadline)
                    {
                        pending = false;
                        foreach (var e in items)
                        {
                            if (!e.weLoaded) continue;
                            bool done = e.viaAddr ? e.addr.IsDone : e.op.isDone;
                            if (!done) { pending = true; break; }
                        }
                        if (pending) yield return null;
                    }

                    // 3. 逐个扫描（场景对象树完全隔离，并发不影响计数）
                    foreach (var e in items)
                    {
                        Scene scene;
                        if (!e.weLoaded)
                        {
                            scene = e.scene;
                        }
                        else
                        {
                            bool ok = e.viaAddr
                                ? (e.addr.IsDone && e.addr.Status == AsyncOperationStatus.Succeeded)
                                : e.op.isDone;
                            if (!ok)
                            {
                                skipped++;
                                if (e.viaAddr) { try { if (e.addr.IsValid()) Addressables.Release(e.addr); } catch { } }
                                continue;
                            }
                            scene = e.viaAddr ? e.addr.Result.Scene : SceneManager.GetSceneByName(e.name);
                        }

                        try
                        {
                            if (scene.IsValid() && scene.isLoaded)
                            {
                                int foundHere = 0;   // 本场景新增写入的连接对数
                                int totalHere = 0;   // 本场景实际扫到的门总数（含反向已记录的）
                                foreach (GameObject root in scene.GetRootGameObjects())
                                    foreach (TransitionPoint tp in root.GetComponentsInChildren<TransitionPoint>(true))
                                    {
                                        if (tp == null || string.IsNullOrEmpty(tp.targetScene)) continue;
                                        totalHere++;
                                        string keyA = e.name + "|" + tp.gameObject.name;
                                        string keyB = tp.targetScene + "|" + (tp.entryPoint ?? "");
                                        // 无方向键去重：同一对连接只输出一次
                                        string pairKey = string.CompareOrdinal(keyA, keyB) <= 0 ? keyA + "<->" + keyB : keyB + "<->" + keyA;
                                        if (!seenPairs.Add(pairKey)) continue;
                                        writer.WriteLine($"{keyA}|{tp.targetScene}|{tp.entryPoint}");
                                        pairCount++;
                                        foundHere++;
                                    }
                                sceneCount++;
                            }
                            else
                            {
                                skipped++;
                            }
                        }
                        catch
                        {
                            skipped++;
                        }
                    }

                    // 4. 整批卸载
                    foreach (var e in items)
                    {
                        if (!e.weLoaded) continue;
                        try
                        {
                            if (e.viaAddr)
                            {
                                if (e.addr.IsValid() && e.addr.IsDone && e.addr.Status == AsyncOperationStatus.Succeeded)
                                    Addressables.UnloadSceneAsync(e.addr);
                            }
                            else
                            {
                                Scene s = SceneManager.GetSceneByName(e.name);
                                if (s.IsValid() && s.isLoaded) SceneManager.UnloadSceneAsync(s);
                            }
                        }
                        catch { }
                    }

                    writer.Flush();
                    yield return null;
                }
            }
        }
        finally
        {
            Application.backgroundLoadingPriority = oldLoadPriority;
            suppressField?.SetValue(null, false);
            _isDumpingTransitions = false;
        }
    }

    // ========== 以下为原有方法（未改动） ==========

    private void WarpToLastBench()
    {
        try
        {
            var pd = PlayerData.instance;
            if (pd == null)
            {
                Plugin.Log.LogError("[HotkeyHandler] PlayerData 不可用");
                return;
            }

            var sceneName = pd.respawnScene;
            if (string.IsNullOrEmpty(sceneName))
            {
                Plugin.Log.LogWarning("[HotkeyHandler] 重生点场景为空，请先坐一次椅子");
                return;
            }

            Plugin.Log.LogInfo($"[HotkeyHandler] 传送至重生点: {sceneName} / {pd.respawnMarkerName}");

            var gm = GameManager.instance;
            gm.SaveGame(success =>
            {
                if (!success)
                {
                    Plugin.Log.LogError("[HotkeyHandler] 保存游戏失败");
                    return;
                }
                gm.LoadGameFromUI(gm.profileID);
            });
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"[HotkeyHandler] 传送异常: {ex.Message}");
        }
    }

    private static void ToggleRecentItemsUI()
    {
        RecentItemsUI.Toggle();
        Plugin.Log.LogInfo($"最近获得物品UI {(RecentItemsUI.IsVisible ? "显示" : "隐藏")}");
    }

    /// <summary>第二阶段备注器窗口：图片预览 + 足够长的输入框填用途备注。</summary>
    private void DrawAnnotateWindow()
    {
        if (_annotateIndex >= _annotateList.Count)
        {
            FinishAnnotate();
            return;
        }

        string name = _annotateList[_annotateIndex];
        const float winW = 900f, winH = 300f;
        float wx = (Screen.width - winW) / 2f;
        float wy = (Screen.height - winH) / 2f;

        GUI.Box(new Rect(wx, wy, winW, winH), "");
        GUI.Label(new Rect(wx + 20, wy + 12, winW - 40, 30),
            $"图标备注器  {_annotateIndex + 1}/{_annotateList.Count}  填写该图标用途",
            new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold });

        // 左侧：图片预览
        Rect imgRect = new Rect(wx + 30, wy + 60, 200f, 200f);
        var spr = SpriteCache.Find(name);
        if (spr != null && spr.texture != null)
            GUI.DrawTextureWithTexCoords(imgRect, spr.texture, RectToUv(spr));
        else
            GUI.Box(imgRect, "未加载");

        // 右侧：名字 + 长输入框 + 按钮
        GUI.Label(new Rect(wx + 260, wy + 55, winW - 300, 30), name,
            new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold });

        GUI.Label(new Rect(wx + 260, wy + 95, winW - 300, 25), "用途备注（输入后 Enter 保存并下一张）",
            new GUIStyle(GUI.skin.label) { fontSize = 15 });

        // 足够长的输入框
        _noteText = GUI.TextField(new Rect(wx + 260, wy + 125, winW - 310, 40), _noteText,
            new GUIStyle(GUI.skin.textField) { fontSize = 20 });

        bool save = false;
        if (GUI.Button(new Rect(wx + 260, wy + 185, 200f, 60f), "保存并下一张 (Enter)",
            new GUIStyle(GUI.skin.button) { fontSize = 18 }))
            save = true;
        if (GUI.Button(new Rect(wx + 480, wy + 185, 160f, 60f), "跳过 (留空)",
            new GUIStyle(GUI.skin.button) { fontSize = 18 }))
            save = true;

        if (save)
            SaveCurrentNote();
    }

    /// <summary>F4 筛选器窗口：左图右文，下方勾/叉/跳过按钮。</summary>
    private void DrawSpritePicker()
    {
        if (_pickerIndex >= SpritePickCandidates.Length)
        {
            FinishPicker();
            return;
        }

        var (cat, name) = SpritePickCandidates[_pickerIndex];
        const float winW = 720f, winH = 420f;
        float wx = (Screen.width - winW) / 2f;
        float wy = (Screen.height - winH) / 2f;

        GUI.Box(new Rect(wx, wy, winW, winH), "");
        GUI.Label(new Rect(wx + 20, wy + 12, winW - 40, 30),
            $"图标筛选器  {_pickerIndex + 1}/{SpritePickCandidates.Length}  类别[{cat}]",
            new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold });

        // 左侧：图片预览
        Rect imgRect = new Rect(wx + 30, wy + 60, 240f, 240f);
        var spr = SpriteCache.Find(name);
        if (spr != null && spr.texture != null)
        {
            GUI.DrawTextureWithTexCoords(imgRect, spr.texture, RectToUv(spr));
        }
        else
        {
            GUI.Box(imgRect, "未加载");
        }

        // 右侧：名字 + 操作按钮
        GUI.Label(new Rect(wx + 300, wy + 70, winW - 340, 40), name,
            new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold });
        GUI.Label(new Rect(wx + 300, wy + 120, winW - 340, 30), $"[Enter] ✓ 使用    [X] ✗ 跳过",
            new GUIStyle(GUI.skin.label) { fontSize = 16 });

        bool wantPick = false;
        bool wantSkip = false;
        if (GUI.Button(new Rect(wx + 310, wy + 200, 170f, 70f), "✓ 使用 (Enter)", new GUIStyle(GUI.skin.button) { fontSize = 22, normal = { textColor = Color.green } }))
            wantPick = true;
        if (GUI.Button(new Rect(wx + 500, wy + 200, 170f, 70f), "✗ 跳过 (X)", new GUIStyle(GUI.skin.button) { fontSize = 22, normal = { textColor = Color.red } }))
            wantSkip = true;

        if (wantPick)
        {
            _pickedNames.Add(name);
            _pickerIndex++;
        }
        else if (wantSkip)
        {
            _pickerIndex++;
        }
    }

    /// <summary>根据 Sprite 的 textureRect 计算 GUI.DrawTextureWithTexCoords 需要的 UV 矩形（与 RecentItemsUI 一致，不做 y 翻转）。</summary>
    private static Rect RectToUv(Sprite spr)
    {
        var t = spr.texture;
        Rect r = spr.textureRect;
        return new Rect(
            r.x / t.width,
            r.y / t.height,
            r.width / t.width,
            r.height / t.height);
    }

    private void OnGUI()
    {
        try
        {
            if (_pickerActive)
            {
                DrawSpritePicker();
                return;
            }
            if (_annotateActive)
            {
                DrawAnnotateWindow();
                return;
            }

            if (!IsBenchwarpMenuVisible()) return;

            _tipStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 48,
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = Color.red }
            };

            // 本地化提示文字
            var tipText = Locale.Get("按 F6 可直接传送");
            const float textWidth = 600f;
            const float textHeight = 80f;
            var x = (Screen.width - textWidth) / 2f;
            var y = Screen.height * 0.66f;

            GUI.Label(new Rect(x, y, textWidth, textHeight), tipText, _tipStyle);
        }
        catch { }
    }

    private static bool IsBenchwarpMenuVisible()
    {
        EnsureBenchwarpCache();
        try
        {
            if (_benchwarpType != null)
            {
                if (_benchwarpInstanceProp != null && _benchwarpInstance == null)
                    _benchwarpInstance = _benchwarpInstanceProp.GetValue(null);
                if (_benchwarpInstance != null && _benchwarpIsDisplayingProp != null)
                    return (bool)_benchwarpIsDisplayingProp.GetValue(_benchwarpInstance);
                var menu = GameObject.Find("WarpMenu") ?? GameObject.Find("BenchwarpMenu");
                return menu != null && menu.activeInHierarchy;
            }
            var menuFallback = GameObject.Find("WarpMenu") ?? GameObject.Find("BenchwarpMenu");
            return menuFallback != null && menuFallback.activeInHierarchy;
        }
        catch
        {
            return false;
        }
    }

    private static void EnsureBenchwarpCache()
    {
        if (_benchwarpChecked)
            return;
        _benchwarpChecked = true;
        _benchwarpType = Type.GetType("Benchwarp.Components.GUIController, Benchwarp");
        if (_benchwarpType == null)
            return;
        _benchwarpInstanceProp = _benchwarpType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
        _benchwarpIsDisplayingProp = _benchwarpType.GetProperty("IsDisplaying");
    }

    // ========== F4：扫描特殊收集品触发点（苔莓 Mossberry / 面具碎片 Heart Piece / 丝轴碎片 Silk Spool） ==========
    /// <summary>
    /// 串行逐个加载所有场景（先试 SceneManager，失败换 Addressables 交叉尝试），扫描
    /// CollectableItemPickup / PersistentBoolItem / HeartPieceOrb / PrefabCollectable 以及
    /// 名称含关键词的对象，每场景扫完立即写入并 Flush（边读边写），输出到 special_objects_scan.txt。
    /// 建议在主菜单按 F11 触发。
    /// </summary>
    private static readonly string[] SpecialNameKeywords =
        { "silk spool", "heart piece", "mossberry", "aspid berry", "berry", "spool", "moss", "mask piece" };

    private bool _isScanningSpecial;

    /// <summary>
    /// 手动触发（F4）：枚举当前场景所有与丝轴/面具相关的 PlayMakerFSM（含未激活对象）、
    /// 所有 PersistentBoolItem，并对名字含 "Silk Spool" 的对象输出全部组件列表 + Control FSM
    /// 完整状态机（状态/动作/转换），用于定位丝轴世界触发点的发放动作。
    /// </summary>
    private void ManualDumpCurrentSceneSpoolFsms()
    {
        try
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            string outPath = Path.Combine(BepInEx.Paths.PluginPath, "spool_fsm_scan.txt");
            var lines = new List<string>();
            lines.Add($"# 当前场景: {scene.name}  触发时间: {DateTime.Now:HH:mm:ss}");
            lines.Add("#FSM|objectPath|fsmName|enabled|activeInHierarchy|activeState");

            int fsmTotal = 0, pbiTotal = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                // 1. 所有含 spool/silk/heart 关键词的 PlayMakerFSM（含未激活）
                foreach (var fsm in root.GetComponentsInChildren<PlayMakerFSM>(true))
                {
                    if (fsm == null) continue;
                    string objName = fsm.gameObject.name ?? "";
                    string fsmName = !string.IsNullOrEmpty(fsm.FsmName) ? fsm.FsmName : (fsm.Fsm != null ? fsm.Fsm.Name : "");
                    string lower = (objName + " " + fsmName).ToLowerInvariant();
                    if (!lower.Contains("spool") && !lower.Contains("silk") && !lower.Contains("heart"))
                        continue;
                    string state = "";
                    try { state = fsm.ActiveStateName ?? ""; } catch { }
                    lines.Add($"FSM|{GetObjectPath(fsm.transform)}|{fsmName}|{fsm.enabled}|{fsm.gameObject.activeInHierarchy}|{state}");
                    fsmTotal++;
                }

                // 2. 所有 PersistentBoolItem（含 ID，丝轴点 ID=="Silk Spool"）
                foreach (var pbi in root.GetComponentsInChildren<PersistentBoolItem>(true))
                {
                    if (pbi == null) continue;
                    string id = GetPersistentBoolItemId(pbi);
                    if (string.IsNullOrEmpty(id)) continue;
                    var pos = pbi.transform.position;
                    lines.Add($"PBI|{GetObjectPath(pbi.transform)}|{pbi.gameObject.name}|id={id}|({pos.x:F1},{pos.y:F1})");
                    pbiTotal++;
                }

                // 3. 对 Silk Spool 对象输出全部组件 + Control FSM 完整细节
                foreach (var go in root.GetComponentsInChildren<Transform>(true))
                {
                    if (go == null || !string.Equals(go.name, "Silk Spool", StringComparison.OrdinalIgnoreCase))
                        continue;
                    lines.Add($"===== Silk Spool 对象: {GetObjectPath(go)} 组件列表 =====");
                    foreach (var comp in go.GetComponents<Component>())
                    {
                        if (comp == null) continue;
                        lines.Add($"组件: {comp.GetType().FullName}");
                        if (comp is PlayMakerFSM fsm)
                        {
                            lines.Add($"  FSM: {fsm.FsmName} 当前状态: {fsm.ActiveStateName}");
                            DumpFsmDetail(fsm, lines);
                        }
                    }
                }
            }

            System.IO.File.WriteAllLines(outPath, lines, System.Text.Encoding.UTF8);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[SpoolFSM] 手动枚举异常: {ex}");
        }
    }

    /// <summary>将动作字段值格式化为可读文本，PlayMaker 变量取 .Value 实值。</summary>
    private static string FormatFsmValue(object v)
    {
        if (v == null) return "null";
        try
        {
            if (v is HutongGames.PlayMaker.FsmString fs) return "FsmString=" + (fs.Value ?? "null");
            if (v is HutongGames.PlayMaker.FsmInt fi) return "FsmInt=" + fi.Value;
            if (v is HutongGames.PlayMaker.FsmBool fb) return "FsmBool=" + fb.Value;
            if (v is HutongGames.PlayMaker.FsmFloat ff) return "FsmFloat=" + ff.Value;
            if (v is HutongGames.PlayMaker.FsmEvent fe) return "FsmEvent=" + (fe.Name ?? "null");
            if (v is HutongGames.PlayMaker.FsmGameObject fg) return "FsmGameObject=" + (fg.Value != null ? fg.Value.name : "null");
            if (v is HutongGames.PlayMaker.FsmOwnerDefault fod) return "FsmOwnerDefault=" + (fod.GameObject != null && fod.GameObject.Value != null ? fod.GameObject.Value.name : "null");
            if (v is Array arr) return "Array[" + arr.Length + "]";
        }
        catch { }
        return v.ToString();
    }

    /// <summary>递归转储 PlayMakerFSM 完整状态机：每个状态的动作类名、字段实值、转换事件与目标状态。</summary>
    private static void DumpFsmDetail(PlayMakerFSM fsm, List<string> lines)
    {
        try
        {
            var fsmData = fsm.Fsm;
            if (fsmData == null) { lines.Add("  FSM 数据为空"); return; }
            foreach (var st in fsmData.States)
            {
                if (st == null) continue;
                lines.Add($"  状态: {st.Name}");
                foreach (var action in st.Actions)
                {
                    if (action == null) continue;
                    lines.Add($"    动作: {action.GetType().Name}");
                    // 输出关键字段值（方法名/物品名/字段名等），FsmString/FsmInt 等变量取 .Value 实值
                    var fields = action.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    foreach (var f in fields)
                    {
                        object v;
                        try { v = f.GetValue(action); } catch { continue; }
                        if (v == null) continue;
                        string valStr = FormatFsmValue(v);
                        lines.Add($"      字段 {f.Name} = {valStr}");
                    }
                }
                foreach (var tr in st.Transitions)
                {
                    if (tr == null) continue;
                    string evName = "";
                    try { evName = tr.EventName; } catch { }
                    string toName = "";
                    try { toName = tr.ToState; } catch { }
                    lines.Add($"    转换: 事件[{evName}] -> {toName}");
                }
            }
        }
        catch (Exception ex)
        {
            lines.Add($"  FSM 转储异常: {ex.Message}");
        }
    }

    private void ScanSpecialObjects()
    {
        if (_isScanningSpecial || IsScanningChecks || _isDumpingTransitions)
        {
            Plugin.Log.LogWarning("[特殊扫描] 已有导出任务进行中，忽略");
            return;
        }
        StartCoroutine(ScanSpecialObjectsCoroutine());
    }

    private IEnumerator ScanSpecialObjectsCoroutine()
    {
        _isScanningSpecial = true;
        // 提高后台加载线程优先级，加快逐场景加载（结束后 finally 恢复）
        var oldLoadPriority = Application.backgroundLoadingPriority;
        Application.backgroundLoadingPriority = UnityEngine.ThreadPriority.High;
        // 抑制场景随机模组事件（反射，尽力而为）
        FieldInfo suppressField = Type.GetType("HKSilksong_Randomizer.RandomSceneLoader, HKSilksong_SceneRandomizer")
            ?.GetField("SuppressSceneEvents", BindingFlags.Public | BindingFlags.Static);
        suppressField?.SetValue(null, true);
        try
        {
            // 场景清单：优先 Build Settings（零依赖），数量异常时退回 Addressables 目录
            var sceneNames = new List<string>();
            try
            {
                int bsCount = SceneManager.sceneCountInBuildSettings;
                for (int i = 0; i < bsCount; i++)
                {
                    string p = SceneUtility.GetScenePathByBuildIndex(i);
                    if (!string.IsNullOrEmpty(p))
                        sceneNames.Add(Path.GetFileNameWithoutExtension(p));
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[特殊扫描] Build Settings 枚举异常: {ex.Message}"); }

            if (sceneNames.Count < 10)
            {
                sceneNames.Clear();
                try
                {
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var locator in Addressables.ResourceLocators)
                        foreach (object key in locator.Keys)
                            if (key is string s && s.StartsWith("Scenes/", StringComparison.OrdinalIgnoreCase) && seen.Add(s))
                                sceneNames.Add(s.Substring("Scenes/".Length));
                }
                catch (Exception ex) { Plugin.Log.LogWarning($"[特殊扫描] Addressables 枚举异常: {ex.Message}"); }
            }

            if (sceneNames.Count == 0)
            {
                Plugin.Log.LogError("[特殊扫描] 无法枚举场景清单（Build Settings 与 Addressables 均为空）");
                yield break;
            }

            string outPath = Path.Combine(BepInEx.Paths.PluginPath, "special_objects_scan.txt");
            Plugin.Log.LogInfo($"=== 开始扫描特殊收集品触发点（苔莓/面具碎片/丝轴碎片），共 {sceneNames.Count} 个场景 -> {outPath} ===");
            int sceneCount = 0, found = 0, failed = 0, excluded = 0;

            using (var writer = new StreamWriter(outPath, false))
            {
                writer.WriteLine("#type|scene|path|name|detail|key|x|y|trigger");
                writer.Flush();

                // 分批并发：每批 20 个场景同时加载（激活），整批扫描、整批卸载
                const int BATCH_SIZE = 20;

                for (int batchStart = 0; batchStart < sceneNames.Count; batchStart += BATCH_SIZE)
                {
                    int batchLen = Math.Min(BATCH_SIZE, sceneNames.Count - batchStart);
                    var items = new List<(string name, Scene scene, AsyncOperation op, AsyncOperationHandle<SceneInstance> addr, bool weLoaded, bool viaAddr)>(batchLen);

                    // 1. 同批场景全部同时发起加载（不逐个等待）
                    for (int i = batchStart; i < batchStart + batchLen; i++)
                    {
                        string sceneName = sceneNames[i];
                        if (IsDumpSkipped(sceneName)) { excluded++; continue; }

                        Scene scene = SceneManager.GetSceneByName(sceneName);
                        if (scene.IsValid() && scene.isLoaded)
                        {
                            items.Add((sceneName, scene, null, default, false, false));
                            continue;
                        }

                        AsyncOperation op = null;
                        AsyncOperationHandle<SceneInstance> addr = default;
                        bool started = false;
                        try
                        {
                            addr = Addressables.LoadSceneAsync("Scenes/" + sceneName, LoadSceneMode.Additive, true, 100);
                            started = true;
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.LogWarning($"[特殊扫描] Addressables 调用失败 {sceneName}: {ex.Message}");
                            // 换 SceneManager 再试
                            try
                            {
                                op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
                                started = op != null;
                            }
                            catch (Exception ex2)
                            {
                                Plugin.Log.LogWarning($"[特殊扫描] SceneManager 调用失败 {sceneName}: {ex2.Message}");
                                started = false;
                            }
                        }
                        if (started && (op != null || addr.IsValid()))
                            items.Add((sceneName, default, op, addr, true, addr.IsValid()));
                        else
                            failed++; // 两种方式均启动失败
                    }

                    // 2. 等本批全部完成（总超时 60 秒）
                    float deadline = Time.realtimeSinceStartup + 60f;
                    bool pending = true;
                    while (pending && Time.realtimeSinceStartup <= deadline)
                    {
                        pending = false;
                        foreach (var e in items)
                        {
                            if (!e.weLoaded) continue;
                            bool done = e.viaAddr ? e.addr.IsDone : e.op.isDone;
                            if (!done) { pending = true; break; }
                        }
                        if (pending) yield return null;
                    }

                    // 3. 逐个扫描（场景对象树完全隔离，并发不影响计数）
                    foreach (var e in items)
                    {
                        Scene scene;
                        if (!e.weLoaded)
                        {
                            scene = e.scene;
                        }
                        else
                        {
                            bool ok = e.viaAddr
                                ? (e.addr.IsDone && e.addr.Status == AsyncOperationStatus.Succeeded)
                                : e.op.isDone;
                            if (!ok)
                            {
                                Plugin.Log.LogWarning($"[特殊扫描] 加载超时/失败 {e.name}，跳过");
                                failed++;
                                if (e.viaAddr) { try { if (e.addr.IsValid()) Addressables.Release(e.addr); } catch { } }
                                continue;
                            }
                            scene = e.viaAddr ? e.addr.Result.Scene : SceneManager.GetSceneByName(e.name);
                        }

                        // 扫描前重置商店静态单例（复用 F3 经验，避免跨场景残留）
                        ResetSpawnedShopSingleton();

                        // 扫描（边读边写：本场景扫完立即落盘）
                        try
                        {
                            if (scene.IsValid() && scene.isLoaded)
                            {
                                int here = ScanSceneSpecialObjects(scene, e.name, writer);
                                found += here;
                                sceneCount++;
                            }
                            else
                            {
                                Plugin.Log.LogWarning($"[特殊扫描] 场景未保持加载状态 {e.name}，跳过");
                                failed++;
                            }
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.LogWarning($"[特殊扫描] 扫描 {e.name} 异常: {ex.Message}");
                            failed++;
                        }
                    }

                    // 4. 整批卸载
                    foreach (var e in items)
                    {
                        if (!e.weLoaded) continue;
                        try
                        {
                            if (e.viaAddr)
                            {
                                if (e.addr.IsValid() && e.addr.IsDone && e.addr.Status == AsyncOperationStatus.Succeeded)
                                    Addressables.UnloadSceneAsync(e.addr);
                            }
                            else
                            {
                                Scene s = SceneManager.GetSceneByName(e.name);
                                if (s.IsValid() && s.isLoaded) SceneManager.UnloadSceneAsync(s);
                            }
                        }
                        catch { }
                    }

                    writer.Flush();
                    yield return null;
                }
            }

            Plugin.Log.LogInfo($"=== 特殊扫描完成：{sceneCount} 场景，{found} 个对象，失败 {failed}，排除 {excluded}，文件 -> {outPath} ===");
        }
        finally
        {
            Application.backgroundLoadingPriority = oldLoadPriority;
            suppressField?.SetValue(null, false);
            _isScanningSpecial = false;
        }
    }

    /// <summary>
    /// 扫描单个场景的特殊触发区域，只输出三类特殊收集品（其余普通拾取点/持久项已被 F3 统计，一律跳过）：
    /// - Mossberry（苔莓）：CollectableItemPickup 的 playerDataBool ∈ 三个 AspidBerry bool，
    ///   或 item 资产名含 moss/berry；
    /// - MaskPiece（面具碎片）：HeartPieceOrb 组件，或 item 名含 heart/mask；
    /// - SpoolPart（丝轴碎片）：PersistentBoolItem ID=="Silk Spool"，或 item 名含 spool。
    /// 附带名称关键词兜底（spool/heart piece/moss/berry/mask piece），防止漏掉非标准命名的点。
    /// </summary>
    private static readonly string[] AspidBerryBoolNames =
        { "mosstownAspidBerryCollected", "bonegraveAspidBerryCollected", "bonetownAspidBerryCollected" };

    private static int ScanSceneSpecialObjects(Scene scene, string sceneName, StreamWriter writer)
    {
        int count = 0;
        var seenObjects = new HashSet<GameObject>();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            // 1. 苔莓 / 面具点（普通拾取点直接跳过：已由 F3 的 check_points 统计）
            foreach (var pickup in root.GetComponentsInChildren<CollectableItemPickup>(true))
            {
                if (pickup == null || !seenObjects.Add(pickup.gameObject)) continue;
                string itemName = "";
                try { if (pickup.Item != null) itemName = pickup.Item.name; } catch { }
                string pdBool = GetPickupPlayerDataBool(pickup);
                string persistentId = GetPersistentBoolItemIdOn(pickup.gameObject);
                string trigger = GetPickupTriggerMode(pickup);
                string lower = (itemName + "|" + pickup.gameObject.name).ToLowerInvariant();

                string specialType = null;
                foreach (string b in AspidBerryBoolNames)
                    if (pdBool == b) { specialType = "Mossberry"; break; }
                if (specialType == null && (lower.Contains("moss") || lower.Contains("berry")))
                    specialType = "Mossberry";
                if (specialType == null && (lower.Contains("heart") || lower.Contains("mask")))
                    specialType = "MaskPiece";
                if (specialType == null && lower.Contains("spool"))
                    specialType = "SpoolPart";

                if (specialType == null) continue; // 普通拾取点，跳过

                Vector3 pos = pickup.transform.position;
                string key = $"{sceneName}_{pos.x:F2}_{pos.y:F2}_{pos.z:F2}";
                string detail = $"item={itemName}|pdbool={pdBool}|persist={persistentId}";
                writer.WriteLine($"{specialType}|{sceneName}|{GetObjectPath(pickup.transform)}|{pickup.gameObject.name}|{detail}|{key}|{pos.x:F1}|{pos.y:F1}|{trigger}");
                count++;
            }

            // 2. 丝轴点：PersistentBoolItem ID=="Silk Spool"（世界丝轴触发点的唯一可靠判据）
            foreach (var pbi in root.GetComponentsInChildren<PersistentBoolItem>(true))
            {
                if (pbi == null || !seenObjects.Add(pbi.gameObject)) continue;
                string id = GetPersistentBoolItemId(pbi);
                if (string.IsNullOrEmpty(id) || id.IndexOf("spool", StringComparison.OrdinalIgnoreCase) < 0) continue;
                Vector3 pos = pbi.transform.position;
                string key = $"{sceneName}_{pos.x:F2}_{pos.y:F2}_{pos.z:F2}";
                writer.WriteLine($"SpoolPart|{sceneName}|{GetObjectPath(pbi.transform)}|{pbi.gameObject.name}|id={id}|{key}|{pos.x:F1}|{pos.y:F1}|");
                count++;
            }

            // 3. 面具碎片 orb（视觉件，兜底判断）
            foreach (var orb in root.GetComponentsInChildren<HeartPieceOrb>(true))
            {
                if (orb == null || !seenObjects.Add(orb.gameObject)) continue;
                Vector3 pos = orb.transform.position;
                string key = $"{sceneName}_{pos.x:F2}_{pos.y:F2}_{pos.z:F2}";
                writer.WriteLine($"MaskPiece|{sceneName}|{GetObjectPath(orb.transform)}|{orb.gameObject.name}||{key}|{pos.x:F1}|{pos.y:F1}|");
                count++;
            }

            // 4. 名称关键词兜底（防漏；已在上面输出的对象不重复）
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || string.IsNullOrEmpty(t.name) || !seenObjects.Add(t.gameObject)) continue;
                string lower = t.name.ToLowerInvariant();
                bool hit = false;
                foreach (string k in SpecialNameKeywords)
                    if (lower.Contains(k)) { hit = true; break; }
                if (!hit) continue;
                Vector3 pos = t.position;
                string key = $"{sceneName}_{pos.x:F2}_{pos.y:F2}_{pos.z:F2}";
                writer.WriteLine($"NameKeyword|{sceneName}|{GetObjectPath(t)}|{t.name}||{key}|{pos.x:F1}|{pos.y:F1}|");
                count++;
            }
        }
        return count;
    }

    private static string GetPickupPlayerDataBool(CollectableItemPickup pickup)
    {
        try
        {
            var f = typeof(CollectableItemPickup).GetField("playerDataBool", BindingFlags.Instance | BindingFlags.NonPublic);
            return f?.GetValue(pickup) as string ?? "";
        }
        catch { return ""; }
    }

    private static string GetPickupTriggerMode(CollectableItemPickup pickup)
    {
        try
        {
            string result = "";
            var f = typeof(CollectableItemPickup).GetField("interactEvents", BindingFlags.Instance | BindingFlags.NonPublic);
            if (f?.GetValue(pickup) != null) result += "interact";
            var t = typeof(CollectableItemPickup).GetField("pickupTrigger", BindingFlags.Instance | BindingFlags.NonPublic);
            if (t?.GetValue(pickup) != null) result += (result.Length > 0 ? "+" : "") + "trigger";
            var fl = typeof(CollectableItemPickup).GetField("fling", BindingFlags.Instance | BindingFlags.NonPublic);
            if (fl != null && (bool)fl.GetValue(pickup)) result += (result.Length > 0 ? "+" : "") + "fling";
            return result;
        }
        catch { return ""; }
    }

    private static string GetPersistentBoolItemId(PersistentBoolItem pbi)
    {
        try
        {
            var itemDataField = typeof(PersistentBoolItem).GetField("itemData", BindingFlags.Instance | BindingFlags.NonPublic);
            var itemData = itemDataField?.GetValue(pbi);
            if (itemData == null) return "";
            var idField = itemData.GetType().GetField("ID", BindingFlags.Instance | BindingFlags.Public);
            return idField?.GetValue(itemData) as string ?? "";
        }
        catch { return ""; }
    }

    private static string GetPersistentBoolItemIdOn(GameObject go)
    {
        try
        {
            var pbi = go.GetComponent<PersistentBoolItem>();
            return pbi == null ? "" : GetPersistentBoolItemId(pbi);
        }
        catch { return ""; }
    }
}