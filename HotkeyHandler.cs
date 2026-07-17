using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SilksongItemRandomizer;
using StartingAbilityPicker;
using UnityEngine;
using Plugin = SilksongItemRandomizer.Plugin;   // 别名，解决命名冲突

/// <summary>
/// 全局快捷键处理器
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