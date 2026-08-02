// SilkSpearPityPatch.cs - 修复后的完整版本
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 丝矛保底补丁：每获得 N 个物品（排除丝矛自身）后强制给予丝矛
    /// 改造后：通过接口访问持久化数据，不再直接依赖 Plugin.SaveData
    /// 保留所有原有功能：计数、保底触发、原生弹窗、回满血丝等
    /// </summary>
    [HarmonyPatch(typeof(SavedItem), "TryGet")]
    public static class SilkSpearPityPatch
    {
        // ========== 持久化数据访问接口 ==========
        public interface ISilkSpearSaveDataAccessor
        {
            int GetTryGetCount();
            void SetTryGetCount(int count);
            bool GetSilkSpearGiven();
            void SetSilkSpearGiven(bool given);
            int GetPityCount();
            void SetPityCount(int count);   // 保底触发次数阈值
        }

        private static ISilkSpearSaveDataAccessor _saveData;

        private static SkillGetMsg _cachedSkillMsgPrefab;
        private static readonly string SKILL_MSG_BUNDLE_NAME = "c3ef29e95eda5580682bb076589a723c.bundle";
        private static readonly string SKILL_MSG_ASSET_PATH = "Assets/Prefabs/UI/Hornet UI/Silk_Skill_Get_Prompt.prefab";

        // ========== 初始化 ==========
        public static void Initialize(ISilkSpearSaveDataAccessor saveDataAccessor)
        {
            _saveData = saveDataAccessor;
            if (_saveData != null)
            {
                Plugin.Log.LogInfo($"[丝矛保底] 加载状态: 已给予={_saveData.GetSilkSpearGiven()}, 当前计数={_saveData.GetTryGetCount()}");
            }
        }

        public static void ResetSilkSpearState()
        {
            if (_saveData != null)
            {
                _saveData.SetTryGetCount(0);
                _saveData.SetSilkSpearGiven(false);
                Plugin.Log.LogInfo("[丝矛保底] 保底状态已重置");
            }
        }

        // ========== Harmony 补丁 ==========
        [HarmonyPostfix]
        private static void Postfix(SavedItem __instance, bool __result)
        {
            if (!__result) return;
            if (_saveData == null) return;
            if (_saveData.GetSilkSpearGiven()) return;

            // 排除丝矛自身
            if (__instance.name == "Silk Spear") return;

            int newCount = _saveData.GetTryGetCount() + 1;
            _saveData.SetTryGetCount(newCount);
            int requiredCount = _saveData.GetPityCount();

            Plugin.Log.LogInfo($"[丝矛保底] 物品获得计数: {newCount}/{requiredCount} (物品: {__instance.name})");

            if (newCount < requiredCount) return;

            Plugin.Log.LogInfo($"[丝矛保底] 保底触发（第{newCount}次物品获得）");

            GiveSilkSpearWithNativePopup();

            _saveData.SetSilkSpearGiven(true);
        }

        private static void GiveSilkSpearWithNativePopup()
        {
            try
            {
                var silkSpearItem = Resources.FindObjectsOfTypeAll<SavedItem>()
                    .FirstOrDefault(i => i.name == "Silk Spear");
                if (silkSpearItem == null)
                {
                    Plugin.Log.LogError("[丝矛保底] 找不到 Silk Spear 物品");
                    return;
                }

                if (silkSpearItem is ToolItemSkill skillItem)
                {
                    EnsureCrestSlots();
                    var prefab = GetSkillMsgPrefab();
                    if (prefab != null)
                    {
                        Plugin.Instance.StartCoroutine(ShowNativePopupAndRefreshHealth(prefab, skillItem, silkSpearItem));
                        return;
                    }
                }

                TryGetPatch.BypassRandom = true;
                try
                {
                    silkSpearItem.TryGet(false, true);
                }
                finally
                {
                    TryGetPatch.BypassRandom = false;
                }
                Plugin.Instance.StartCoroutine(DelayedHealAndSilk());
                Plugin.Log.LogInfo("[丝矛保底] 降级给予丝矛，5秒后回满血丝");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[丝矛保底] 给予丝矛失败: {ex}");
            }
        }

        private static IEnumerator ShowNativePopupAndRefreshHealth(SkillGetMsg prefab, ToolItemSkill skillItem, SavedItem silkSpearItem)
        {
            bool finished = false;
            SkillGetMsg.Spawn(prefab, skillItem, () => finished = true);

            TryGetPatch.BypassRandom = true;
            try
            {
                silkSpearItem.TryGet(false, true);
            }
            finally
            {
                TryGetPatch.BypassRandom = false;
            }
            Plugin.Log.LogInfo("[丝矛保底] 原生弹窗已触发，丝矛已给予");

            float timeout = 15f;
            while (!finished && timeout > 0)
            {
                yield return null;
                timeout -= Time.deltaTime;
            }

            yield return new WaitForSeconds(5f);
            ForceFullHealAndSilk();
        }

        private static IEnumerator DelayedHealAndSilk()
        {
            yield return new WaitForSeconds(5f);
            ForceFullHealAndSilk();
        }

        private static void ForceFullHealAndSilk()
        {
            try
            {
                var hero = HeroController.instance;
                var pd = PlayerData.instance;
                if (hero == null || pd == null)
                {
                    Plugin.Log.LogWarning("[丝矛保底] HeroController 或 PlayerData 为空，无法回满");
                    return;
                }

                int healthNeeded = pd.CurrentMaxHealth - pd.health;
                if (healthNeeded > 0)
                {
                    for (int i = 0; i < healthNeeded; i++)
                        hero.AddHealth(1);
                    Plugin.Log.LogInfo($"[丝矛保底] 已回满血量 (+{healthNeeded})");
                }

                int silkNeeded = pd.CurrentSilkMax - pd.silk;
                if (silkNeeded > 0)
                {
                    for (int i = 0; i < silkNeeded; i++)
                        hero.AddSilk(1, false);
                    Plugin.Log.LogInfo($"[丝矛保底] 已回满丝线 (+{silkNeeded})");
                }

                if (healthNeeded <= 0 && silkNeeded <= 0)
                {
                    EventRegister.SendEvent(EventRegisterEvents.HealthUpdate, null);
                    GameCameras.instance?.silkSpool?.RefreshSilk();
                    Plugin.Log.LogInfo("[丝矛保底] 血量/丝线已满，仅刷新UI");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[丝矛保底] ForceFullHealAndSilk 异常: {ex}");
            }
        }

        private static void EnsureCrestSlots()
        {
            try
            {
                var pd = PlayerData.instance;
                if (pd == null) return;
                string crestId = pd.CurrentCrestID;
                if (string.IsNullOrEmpty(crestId)) crestId = "Hunter";
                var crest = Resources.FindObjectsOfTypeAll<ToolCrest>().FirstOrDefault(c => c.name == crestId);
                if (crest == null) return;
                var data = crest.SaveData;
                if (data.Slots == null || data.Slots.Count == 0)
                {
                    data.Slots = new List<ToolCrestsData.SlotData>();
                    for (int i = 0; i < crest.Slots.Length; i++)
                        data.Slots.Add(new ToolCrestsData.SlotData { IsUnlocked = true });
                    crest.SaveData = data;
                }
            }
            catch { }
        }

        private static SkillGetMsg GetSkillMsgPrefab()
        {
            if (_cachedSkillMsgPrefab != null) return _cachedSkillMsgPrefab;
            try
            {
                AssetBundle targetBundle = null;
                foreach (var bundle in AssetBundle.GetAllLoadedAssetBundles())
                {
                    if (bundle != null && bundle.name == SKILL_MSG_BUNDLE_NAME)
                    {
                        targetBundle = bundle;
                        break;
                    }
                }
                if (targetBundle == null)
                {
                    string bundlePath = Path.Combine(Application.streamingAssetsPath, "aa", "StandaloneWindows64", SKILL_MSG_BUNDLE_NAME);
                    if (File.Exists(bundlePath))
                        targetBundle = AssetBundle.LoadFromFile(bundlePath);
                }
                if (targetBundle != null)
                {
                    var go = targetBundle.LoadAsset<GameObject>(SKILL_MSG_ASSET_PATH);
                    if (go != null)
                        _cachedSkillMsgPrefab = go.GetComponent<SkillGetMsg>();
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[丝矛保底] 加载预制体失败: {ex}");
            }
            if (_cachedSkillMsgPrefab == null)
                Plugin.Log.LogWarning("[丝矛保底] 未找到 SkillGetMsg 预制体，原生弹窗不可用");
            return _cachedSkillMsgPrefab;
        }
    }
}