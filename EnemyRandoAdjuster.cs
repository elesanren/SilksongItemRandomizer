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
        private static bool _enabled = false;
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
        public static float ScaleMin { get; set; } = 1.0f;
        public static float ScaleMax { get; set; } = 1.0f;

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
                _originalEnemyRandoType = GetFieldRandoValue(_enemyRandoTypeField);
                _originalBossRandoType = GetFieldRandoValue(_bossRandoTypeField);
                _originalMiscRandoType = GetFieldRandoValue(_miscRandoTypeField);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"保存 EnemyRando 原始配置失败: {ex.Message}");
            }
        }

        // EnemyRando 的 RandoType 字段可能是裸枚举，也可能是 BepInEx 的 ConfigEntry<T>。
        // 统一通过 Value 属性读写，避免把枚举直接 SetValue 到 ConfigEntry 字段导致类型转换异常。
        private static object GetFieldRandoValue(FieldInfo field)
        {
            if (field == null) return null;
            object entry = TryGetFieldEntry(field);
            return IsConfigEntry(entry) ? field.FieldType.GetProperty("Value")?.GetValue(entry) : entry;
        }

        private static bool SetFieldRandoValue(FieldInfo field, object value)
        {
            if (field == null) return true;
            Type fieldType = field.FieldType;
            // 按字段【声明类型】判断是否为 ConfigEntry<T>（不依赖实例值，避免启动早期字段未初始化导致误判）
            if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition().Name == "ConfigEntry`1")
            {
                object entry = TryGetFieldEntry(field);
                if (entry == null)
                {
                    Plugin.Log.LogWarning("[EnemyRandoAdjuster] EnemyRando 配置尚未初始化，跳过本次写入（MarkGameReady 后会重放）");
                    return false;
                }
                fieldType.GetProperty("Value")?.SetValue(entry, value);
                return true;
            }
            else
            {
                field.SetValue(null, value);
                return true;
            }
        }

        // 读取 ConfigEntry 实例；为 null 时强制触发 EnemyRando.Settings 静态构造函数后再取一次
        private static object TryGetFieldEntry(FieldInfo field)
        {
            try
            {
                object entry = field.GetValue(null);
                if (entry == null && field.DeclaringType != null)
                {
                    System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(field.DeclaringType.TypeHandle);
                    entry = field.GetValue(null);
                }
                return entry;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[EnemyRandoAdjuster] 读取配置字段失败: {ex.Message}");
                return null;
            }
        }

        private static bool _configApplied;
        private static bool _pendingEnabled;

        // 启动早期 EnemyRando 尚未初始化时写入会被跳过；MarkGameReady 后调用本方法重放一次
        public static void ReapplyConfig()
        {
            if (_configApplied || !_pendingEnabled) return;
            SetEnemyRandoConfig(_pendingEnabled);
        }

        private static bool IsConfigEntry(object value)
        {
            return value != null && value.GetType().IsGenericType
                && value.GetType().GetGenericTypeDefinition().Name == "ConfigEntry`1";
        }

        private static void SetEnemyRandoConfig(bool enabled)
        {
            if (_settingsType == null || _randoTypeEnum == null) return;
            _pendingEnabled = enabled;
            try
            {
                object disabledValue = Enum.Parse(_randoTypeEnum, "Disabled");
                object anyValue = Enum.Parse(_randoTypeEnum, "Any");

                bool applied;
                if (enabled)
                {
                    applied = SetFieldRandoValue(_enemyRandoTypeField, _originalEnemyRandoType ?? anyValue);
                    applied &= SetFieldRandoValue(_bossRandoTypeField, _originalBossRandoType ?? anyValue);
                    applied &= SetFieldRandoValue(_miscRandoTypeField, _originalMiscRandoType ?? anyValue);
                }
                else
                {
                    applied = SetFieldRandoValue(_enemyRandoTypeField, disabledValue);
                    applied &= SetFieldRandoValue(_bossRandoTypeField, disabledValue);
                    applied &= SetFieldRandoValue(_miscRandoTypeField, disabledValue);
                }
                if (applied)
                {
                    _configApplied = true;
                    Plugin.Log.LogInfo($"[EnemyRandoAdjuster] 设置随机配置: {(enabled ? "开启" : "关闭")}");
                }
                else
                {
                    Plugin.Log.LogInfo("[EnemyRandoAdjuster] 配置已排队，等待 EnemyRando 就绪后重放");
                }
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
                // 每个敌人生成都会走到这里，不打日志（避免刷屏与字符串分配）
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