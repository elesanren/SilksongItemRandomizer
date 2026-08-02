using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SilksongItemRandomizer;
using StartingAbilityPicker;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;
using TeamCherry.Localization;
using Plugin = SilksongItemRandomizer.Plugin;   // 别名，解决命名冲突

/// <summary>
/// 全局快捷键处理器
/// F3: 扫描所有场景的检查点（拾取点/检查交互/收费机/车站长椅）到 check_points_scan.txt，边读边写（建议主菜单使用）
/// F4: 导出所有场景原版门连接关系到 vanilla_transitions.txt（每对只输出一次，建议主菜单使用）
/// F5: 尝试所有方式解锁 EvaHeal（风铃摇）能力（包括 HasBoundCrestUpgrader 等）
/// F6: 传送至上一次坐的椅子
/// F8: 显示/隐藏最近获得物品 UI
/// F9: 转储所有随机映射到控制台
/// ESC: 刷新 Benchwarp 菜单
/// </summary>
public class HotkeyHandler : MonoBehaviour
{
    private GUIStyle _tipStyle;
    private static bool _isChineseCache;

    private void Update()
    {
        // [调试] F3 扫描检查点 / F4 导出门连接 —— 已注释停用，避免误触触发大量文件读写
        // if (Input.GetKeyDown(KeyCode.F3))
        //     DumpCheckPoints();
        //
        // if (Input.GetKeyDown(KeyCode.F4))
        //     DumpVanillaTransitions();

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

        if (Input.GetKeyDown(KeyCode.Escape))
            Plugin.Instance?.RefreshBenchwarpUI();
    }

    /// <summary>
    /// 综合尝试所有可能的 EvaHeal 解锁方式
    /// </summary>
    private void TestUnlockEvaHeal()
    {
        var pd = PlayerData.instance;
        if (pd == null)
        {
            Plugin.Log.LogError("PlayerData 不可用");
            return;
        }

        Plugin.Log.LogInfo("=== 开始尝试所有方式解锁 EvaHeal（风铃摇）===");

        // 1. 基础弹窗标记
        pd.HasSeenEvaHeal = true;
        Plugin.Log.LogInfo("1. HasSeenEvaHeal = true");

        // 2. 纹章升级相关（伊娃是纹章升级者）
        pd.HasBoundCrestUpgrader = true;
        Plugin.Log.LogInfo("2. HasBoundCrestUpgrader = true");

        pd.CrestUpgraderOfferedFinal = true;
        pd.CrestPreUpgradeTalked = true;
        pd.CrestTalkedPurpose = true;
        pd.CrestUpgraderTalkedSnare = true;
        Plugin.Log.LogInfo("3. 设置纹章升级相关标志: CrestUpgraderOfferedFinal, CrestPreUpgradeTalked, CrestTalkedPurpose, CrestUpgraderTalkedSnare = true");

        // 4. 教堂关闭标志（可能关联剧情）
        pd.chapelClosed_reaper = true;
        pd.chapelClosed_wanderer = true;
        pd.chapelClosed_beast = true;
        pd.chapelClosed_witch = true;
        pd.chapelClosed_toolmaster = true;
        pd.chapelClosed_shaman = true;
        Plugin.Log.LogInfo("4. 设置所有 chapelClosed 标志 = true");

        // 5. 完成记忆标志
        pd.completedMemory_reaper = true;
        pd.completedMemory_wanderer = true;
        pd.completedMemory_beast = true;
        pd.completedMemory_witch = true;
        pd.completedMemory_toolmaster = true;
        pd.completedMemory_shaman = true;
        Plugin.Log.LogInfo("5. 设置所有 completedMemory 标志 = true");

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
                Plugin.Log.LogInfo($"6. 已解锁工具 {name}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"6. 工具 {name} 不存在或无法修改: {ex.Message}");
            }
        }

        // 7. 设置 Collectables 中的 EvaHeal 物品
        try
        {
            var collectable = pd.Collectables.GetData("EvaHeal");
            collectable.Amount = 1;
            pd.Collectables.SetData("EvaHeal", collectable);
            Plugin.Log.LogInfo("7. 已设置 Collectables[EvaHeal].Amount = 1");
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"7. 设置 Collectables 失败: {ex.Message}");
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
                Plugin.Log.LogInfo("8. 已触发 GAINED_SLYTHSONG 自动存档");
            }
            else
            {
                Plugin.Log.LogWarning("8. 未找到 AutoSaveManager.Save 方法或 GAINED_SLYTHSONG 枚举");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"8. 触发自动存档失败: {ex.Message}");
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
                        Plugin.Log.LogInfo("9. 已触发 InventoryCollectableItemSelectionHelper.SelectionType.EvaHeal");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"9. 触发物品栏选择失败: {ex.Message}");
        }

        // 10. 尝试直接增加丝线（测试触发）
        try
        {
            var hc = HeroController.instance;
            if (hc != null)
            {
                hc.AddSilk(1, true);
                Plugin.Log.LogInfo("10. 已添加 1 丝线（测试用）");
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"10. 添加丝线失败: {ex.Message}");
        }

        Plugin.Log.LogInfo("=== 解锁尝试完成，请坐椅子并观察丝线是否自动恢复 ===");
        Plugin.Log.LogInfo("如果仍未恢复，请将日志提交给开发者分析。");
    }

    // ========== F4：导出原版门连接关系 ==========
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
            Plugin.Log.LogWarning("[扫描] 已有导出任务进行中，忽略");
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
            catch (Exception ex) { Plugin.Log.LogWarning($"[扫描] Build Settings 枚举异常: {ex.Message}"); }

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
                catch (Exception ex) { Plugin.Log.LogWarning($"[扫描] Addressables 枚举异常: {ex.Message}"); }
            }

            if (sceneNames.Count == 0)
            {
                Plugin.Log.LogError("[扫描] 无法枚举场景清单（Build Settings 与 Addressables 均为空）");
                yield break;
            }

            string outPath = Path.Combine(BepInEx.Paths.PluginPath, "check_points_scan.txt");
            Plugin.Log.LogInfo($"=== 开始扫描检查点，共 {sceneNames.Count} 个场景 -> {outPath} ===");
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
                        catch (Exception ex) { Plugin.Log.LogWarning($"[Retry] {sceneName} SceneManager 调用失败: {ex.Message}"); }
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
                            catch (Exception ex) { Plugin.Log.LogWarning($"[Retry] {sceneName} Addressables 调用失败: {ex.Message}"); }
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
                            Plugin.Log.LogWarning($"[Retry] {sceneName} 两种方式均加载失败");
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
                        Plugin.Log.LogInfo($"[扫描] {sceneName}: {foundHere} 个检查点");
                        sceneCount++;
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.LogWarning($"[扫描] 扫描 {sceneName} 异常: {ex.Message}");
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

            Plugin.Log.LogInfo($"=== 扫描完成：{sceneCount} 场景，{pointCount} 个检查点，失败 {failed}，排除 {excluded}，文件 -> {outPath} ===");
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
    /// 强制当前场景的 ShopOwner 认为"没有商店"而重新生成商店 UI，保证逐场景扫描不互相干扰。</summary>
    private static void ResetSpawnedShopSingleton()
    {
        try
        {
            var shopOwnerBaseType = Type.GetType("ShopOwnerBase, Assembly-CSharp");
            if (shopOwnerBaseType == null) return;
            var field = shopOwnerBaseType.GetField("_spawnedShop", BindingFlags.Static | BindingFlags.NonPublic);
            if (field == null) return;
            var existing = field.GetValue(null) as UnityEngine.Object;
            if (existing != null)
                UnityEngine.Object.DestroyImmediate(existing);
            field.SetValue(null, null);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[扫描] 重置 ShopOwnerBase._spawnedShop 失败: {ex.Message}");
        }
    }

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
            Plugin.Log.LogWarning("[Dump] 正在导出中，忽略重复触发");
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
            catch (Exception ex) { Plugin.Log.LogWarning($"[Dump] Build Settings 枚举异常: {ex.Message}"); }

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
                catch (Exception ex) { Plugin.Log.LogWarning($"[Dump] Addressables 枚举异常: {ex.Message}"); }
            }

            if (sceneNames.Count == 0)
            {
                Plugin.Log.LogError("[Dump] 无法枚举场景清单（Build Settings 与 Addressables 均为空）");
                yield break;
            }
            bool viaAddrList = source == "Addressables";
            Plugin.Log.LogInfo($"=== 开始导出原版门连接，共 {sceneNames.Count} 个场景（来源: {source}）===");

            string outPath = Path.Combine(BepInEx.Paths.PluginPath, "vanilla_transitions.txt");
            Plugin.Log.LogInfo($"[Dump] 输出文件: {outPath}");
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
                            Plugin.Log.LogInfo($"[Dump] 排除特殊场景: {sceneName}");
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
                        catch (Exception ex)
                        {
                            Plugin.Log.LogWarning($"[Dump] 加载调用失败 {sceneName}: {ex.Message}");
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
                                Plugin.Log.LogWarning($"[Dump] 加载超时/失败 {e.name}，跳过");
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
                                Plugin.Log.LogInfo($"[Dump] {e.name}: 共 {totalHere} 个门，新增 {foundHere} 对");
                                sceneCount++;
                            }
                            else
                            {
                                Plugin.Log.LogWarning($"[Dump] 场景未保持加载状态 {e.name}，跳过");
                                skipped++;
                            }
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.LogWarning($"[Dump] 扫描 {e.name} 异常: {ex.Message}");
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
                    Plugin.Log.LogInfo($"[Dump] 进度 {Math.Min(batchStart + batchLen, sceneNames.Count)}/{sceneNames.Count}，已导出 {pairCount} 对");
                }
            }

            Plugin.Log.LogInfo($"=== 导出完成：{sceneCount} 场景，{pairCount} 对原版连接，跳过 {skipped}，排除 {excluded}，文件 -> {outPath} ===");
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

    private void OnGUI()
    {
        try
        {
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
        try
        {
            Type guiControllerType = Type.GetType("Benchwarp.Components.GUIController, Benchwarp");
            if (guiControllerType == null)
            {
                var menu = GameObject.Find("WarpMenu") ?? GameObject.Find("BenchwarpMenu");
                return menu != null && menu.activeInHierarchy;
            }
            var instanceProperty = guiControllerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            if (instanceProperty == null) return false;
            var guiController = instanceProperty.GetValue(null);
            if (guiController != null)
            {
                var isDisplayingProperty = guiControllerType.GetProperty("IsDisplaying");
                if (isDisplayingProperty != null && (bool)isDisplayingProperty.GetValue(guiController))
                    return true;
            }
            var menuFallback = GameObject.Find("WarpMenu") ?? GameObject.Find("BenchwarpMenu");
            return menuFallback != null && menuFallback.activeInHierarchy;
        }
        catch
        {
            return false;
        }
    }
}