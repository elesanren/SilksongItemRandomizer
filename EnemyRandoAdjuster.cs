using HarmonyLib;
using UnityEngine;
using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 怪物随机调整器（与 EnemyRando 模组交互）
    /// 改造后：通过 SilksongItemRandomizerAPI 的总开关控制是否启用 EnemyRando 的随机替换
    /// 保留所有原有功能：动态启用/禁用 EnemyRando、恢复被替换的敌人、可选随机缩放怪物大小
    /// </summary>
    public static class EnemyRandoAdjuster
    {
        // 反射 EnemyRando 的类型和成员
        private static bool _patched = false;
        private static object _originalEnemyRandoType;
        private static object _originalBossRandoType;
        private static object _originalMiscRandoType;

        private static Type _randoTypeEnum;
        private static Type _settingsType;
        private static FieldInfo _enemyRandoTypeField;
        private static FieldInfo _bossRandoTypeField;
        private static FieldInfo _miscRandoTypeField;

        private static Type _replacedEnemyType;
        private static Type _replacementEnemyType;
        private static FieldInfo _replacementsField;

        // 配置属性（可通过 API 设置）
        private static bool _enabled = true;
        public static bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                SetEnemyRandoConfig(value);
                if (!value)
                {
                    RestoreAllReplacedEnemies();
                }
            }
        }

        public static bool ScaleRandomEnabled { get; set; } = true;
        public static float ScaleMin { get; set; } = 0.5f;
        public static float ScaleMax { get; set; } = 2.0f;

        static EnemyRandoAdjuster()
        {
            // 反射 EnemyRando.Settings
            _settingsType = Type.GetType("EnemyRando.Settings, EnemyRando");
            if (_settingsType != null)
            {
                _enemyRandoTypeField = _settingsType.GetField("EnemyRandoType", BindingFlags.Public | BindingFlags.Static);
                _bossRandoTypeField = _settingsType.GetField("BossRandoType", BindingFlags.Public | BindingFlags.Static);
                _miscRandoTypeField = _settingsType.GetField("MiscRandoType", BindingFlags.Public | BindingFlags.Static);
                _randoTypeEnum = Type.GetType("EnemyRando.Settings+RandoType, EnemyRando");
            }

            // 反射 EnemyRando 的替换组件
            _replacedEnemyType = Type.GetType("EnemyRando.ReplacedEnemy, EnemyRando");
            _replacementEnemyType = Type.GetType("EnemyRando.ReplacementEnemy, EnemyRando");
            if (_replacedEnemyType != null)
                _replacementsField = _replacedEnemyType.GetField("replacements", BindingFlags.Instance | BindingFlags.Public);
        }

        public static void TryPatch(Harmony harmony)
        {
            if (_patched) return;

            Type randomiserType = Type.GetType("EnemyRando.Randomiser, EnemyRando");
            if (randomiserType == null)
            {
                Plugin.Log.LogInfo("[EnemyRandoAdjuster] EnemyRando not found, skip patch.");
                return;
            }

            MethodInfo target = randomiserType.GetMethod("SpawnRandomEnemy",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (target == null)
            {
                Plugin.Log.LogWarning("[EnemyRandoAdjuster] SpawnRandomEnemy method not found.");
                return;
            }

            var prefix = new HarmonyMethod(typeof(EnemyRandoAdjuster).GetMethod(nameof(Prefix),
                BindingFlags.NonPublic | BindingFlags.Static));
            var postfix = new HarmonyMethod(typeof(EnemyRandoAdjuster).GetMethod(nameof(Postfix),
                BindingFlags.NonPublic | BindingFlags.Static));

            harmony.Patch(target, prefix, postfix);
            _patched = true;
            Plugin.Log.LogInfo("[EnemyRandoAdjuster] Patch applied successfully.");

            SaveOriginalConfig();
        }

        private static void SaveOriginalConfig()
        {
            if (_settingsType == null) return;
            try
            {
                if (_enemyRandoTypeField != null)
                    _originalEnemyRandoType = _enemyRandoTypeField.GetValue(null);
                if (_bossRandoTypeField != null)
                    _originalBossRandoType = _bossRandoTypeField.GetValue(null);
                if (_miscRandoTypeField != null)
                    _originalMiscRandoType = _miscRandoTypeField.GetValue(null);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"保存 EnemyRando 原始配置失败: {ex.Message}");
            }
        }

        private static void SetEnemyRandoConfig(bool enabled)
        {
            if (_settingsType == null || _randoTypeEnum == null) return;
            try
            {
                object disabledValue = Enum.Parse(_randoTypeEnum, "Disabled");
                object anyValue = Enum.Parse(_randoTypeEnum, "Any");

                if (enabled)
                {
                    _enemyRandoTypeField?.SetValue(null, _originalEnemyRandoType ?? anyValue);
                    _bossRandoTypeField?.SetValue(null, _originalBossRandoType ?? anyValue);
                    _miscRandoTypeField?.SetValue(null, _originalMiscRandoType ?? anyValue);
                }
                else
                {
                    _enemyRandoTypeField?.SetValue(null, disabledValue);
                    _bossRandoTypeField?.SetValue(null, disabledValue);
                    _miscRandoTypeField?.SetValue(null, disabledValue);
                }
                Plugin.Log.LogInfo($"[EnemyRandoAdjuster] 设置随机配置: {(enabled ? "开启" : "关闭")}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"设置 EnemyRando 配置失败: {ex}");
            }
        }

        private static void RestoreAllReplacedEnemies()
        {
            if (_replacedEnemyType == null) return;

            var allReplaced = Resources.FindObjectsOfTypeAll(_replacedEnemyType);
            foreach (var replaced in allReplaced)
            {
                var gameObj = (replaced as Component)?.gameObject;
                if (gameObj == null) continue;

                var replacements = _replacementsField?.GetValue(replaced) as System.Collections.IList;
                if (replacements == null || replacements.Count == 0) continue;

                var sourceHealthManager = gameObj.GetComponent<HealthManager>();
                if (sourceHealthManager == null) continue;

                gameObj.SetActive(true);
                sourceHealthManager.isDead = false;

                foreach (var rep in replacements)
                {
                    if (rep != null)
                        UnityEngine.Object.Destroy((rep as Component)?.gameObject);
                }
                var listClear = _replacementsField.GetValue(replaced) as System.Collections.IList;
                listClear?.Clear();

                UnityEngine.Object.Destroy(replaced as Component);
            }
            Plugin.Log.LogInfo("[EnemyRandoAdjuster] 已恢复所有被替换的敌人");
        }

        private static bool Prefix(HealthManager source, int hp)
        {
            if (!_enabled)
            {
                if (!source.gameObject.activeSelf)
                    source.gameObject.SetActive(true);
                source.hp = hp;
                if (_replacedEnemyType != null)
                {
                    var replaced = source.GetComponent(_replacedEnemyType);
                    if (replaced != null)
                        UnityEngine.Object.Destroy(replaced);
                }
                Plugin.Log.LogInfo($"[EnemyRandoAdjuster] Disabled, restored {source.name} hp to {hp}");
                return false;
            }
            return true;
        }

        private static void Postfix(bool __result, HealthManager source, int hp)
        {
            if (!_enabled || !__result || source == null) return;
            if (_replacedEnemyType == null) return;

            var replaced = source.GetComponent(_replacedEnemyType);
            if (replaced == null) return;

            if (_replacementsField == null) return;
            var replacements = _replacementsField.GetValue(replaced) as System.Collections.IList;
            if (replacements == null || replacements.Count == 0) return;

            foreach (var rep in replacements)
            {
                var hm = (rep as Component)?.GetComponent<HealthManager>();
                if (hm == null) continue;

                hm.hp = hp;

                if (ScaleRandomEnabled && UnityEngine.Random.value < 0.2f)
                {
                    float t = 1f - Mathf.Sin(UnityEngine.Random.value * Mathf.PI);
                    float scale = ScaleMin + (ScaleMax - ScaleMin) * t;
                    hm.transform.localScale = Vector3.Scale(hm.transform.localScale, new Vector3(scale, scale, scale));
                }
            }
        }
    }
}