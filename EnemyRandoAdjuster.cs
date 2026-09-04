using HarmonyLib;
using UnityEngine;
using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 怪物随机调整器（与 EnemyRando 模组交互）。
    /// 开关唯一权威：GlobalConfig.EnemyRandoAdjustEnabled（HardItemRandomizer.GlobalConfig.cfg）。
    /// 开启 = 把 EnemyRando 的 EnemyRandoType/BossRandoType/MiscRandoType 三字段设为 Any；
    /// 关闭 = 设为 Disabled。字段是 EnemyRando 自己的 ConfigEntry&lt;RandoType&gt;，写 .Value 即生效并持久化。
    /// </summary>
    public static class EnemyRandoAdjuster
    {
        private static bool _patched = false;

        private static Type _randoTypeEnum;
        private static Type _settingsType;
        private static FieldInfo _enemyRandoTypeField;
        private static FieldInfo _bossRandoTypeField;
        private static FieldInfo _miscRandoTypeField;

        private static Type _replacedEnemyType;
        private static Type _replacementEnemyType;
        private static FieldInfo _replacementsField;

        private static bool _enabled = false;
        /// <summary>开关。设置时立即同步到 EnemyRando 三字段，并反写统一 cfg。</summary>
        public static bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                ApplyToEnemyRando(value);
                if (!value)
                    RestoreAllReplacedEnemies();
            }
        }

        private static bool _scaleRandom = true;
        private static float _scaleMin = 1.0f;
        private static float _scaleMax = 1.0f;

        /// <summary>怪物随机：是否启用缩放随机。写值仅更新运行时内存，cfg 由开始游戏时统一固化。</summary>
        public static bool ScaleRandomEnabled
        {
            get => _scaleRandom;
            set
            {
                if (_scaleRandom == value) return;
                _scaleRandom = value;
            }
        }
        /// <summary>怪物随机：缩放最小值。写值仅更新运行时内存，cfg 由开始游戏时统一固化。</summary>
        public static float ScaleMin
        {
            get => _scaleMin;
            set
            {
                if (_scaleMin == value) return;
                _scaleMin = value;
            }
        }
        /// <summary>怪物随机：缩放最大值。写值仅更新运行时内存，cfg 由开始游戏时统一固化。</summary>
        public static float ScaleMax
        {
            get => _scaleMax;
            set
            {
                if (_scaleMax == value) return;
                _scaleMax = value;
            }
        }

        static EnemyRandoAdjuster()
        {
            EnsureReflection();
        }

        /// <summary>
        /// GlobalConfig.Init 完成后调用：从 cfg 恢复怪物缩放设置到运行时内存。
        /// 必须在 GlobalConfig.Bind 全部执行后调用（在 Plugin.Awake 中于 Init 之后调用）。
        /// </summary>
        public static void LoadFromConfig()
        {
            _scaleRandom = SilksongItemRandomizer.GlobalConfig.EnemyScaleRandom.Value;
            _scaleMin = SilksongItemRandomizer.GlobalConfig.EnemyScaleMin.Value;
            _scaleMax = SilksongItemRandomizer.GlobalConfig.EnemyScaleMax.Value;
        }

        /// <summary>
        /// 懒解析 EnemyRando 反射字段：EnemyRando 可能在本 mod 静态初始化之后才加载，
        /// 若一次性缓存会导致 _settingsType 恒为 null、后续写入全部落空（面板打开怪物随机无效）。
        /// 每次写入前若字段未命中则重新解析一次，保证 EnemyRando 后加载也能生效。
        /// </summary>
        private static void EnsureReflection()
        {
            if (_settingsType == null)
            {
                _settingsType = Type.GetType("EnemyRando.Settings, EnemyRando");
                if (_settingsType != null)
                {
                    _enemyRandoTypeField = _settingsType.GetField("EnemyRandoType", BindingFlags.Public | BindingFlags.Static);
                    _bossRandoTypeField = _settingsType.GetField("BossRandoType", BindingFlags.Public | BindingFlags.Static);
                    _miscRandoTypeField = _settingsType.GetField("MiscRandoType", BindingFlags.Public | BindingFlags.Static);
                    _randoTypeEnum = Type.GetType("EnemyRando.Settings+RandoType, EnemyRando");
                }
            }
            if (_replacedEnemyType == null)
            {
                _replacedEnemyType = Type.GetType("EnemyRando.ReplacedEnemy, EnemyRando");
                _replacementEnemyType = Type.GetType("EnemyRando.ReplacementEnemy, EnemyRando");
                if (_replacedEnemyType != null)
                    _replacementsField = _replacedEnemyType.GetField("replacements", BindingFlags.Instance | BindingFlags.Public);
            }
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
        }

        // ========== 开关应用 ==========

        /// <summary>把开关同步到 EnemyRando 的三个 RandoType 字段（Any=开 / Disabled=关）。</summary>
        private static void ApplyToEnemyRando(bool enabled)
        {
            EnsureReflection();
            if (_settingsType == null || _randoTypeEnum == null)
            {
                Plugin.Log.LogWarning($"[EnemyRandoAdjuster] 反射缺失无法写入: settingsType={_settingsType != null}, enum={_randoTypeEnum != null}");
                return;
            }
            try
            {
                object target = enabled
                    ? Enum.Parse(_randoTypeEnum, "Any")
                    : Enum.Parse(_randoTypeEnum, "Disabled");

                bool applied = SetFieldRandoValue(_enemyRandoTypeField, target);
                applied &= SetFieldRandoValue(_bossRandoTypeField, target);
                applied &= SetFieldRandoValue(_miscRandoTypeField, target);

                if (applied)
                    Plugin.Log.LogInfo($"[EnemyRandoAdjuster] 设置随机配置: {(enabled ? "开启" : "关闭")}");
                else
                    Plugin.Log.LogInfo("[EnemyRandoAdjuster] 配置已排队，等待 EnemyRando 就绪后重放");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"设置 EnemyRando 配置失败: {ex}");
            }
        }

        /// <summary>游戏就绪后重放当前开关（EnemyRando 早期未初始化时写入会失败，此调用补上）。</summary>
        public static void ReapplyConfig()
        {
            EnsureReflection();
            if (_settingsType == null || _randoTypeEnum == null) return;
            ApplyToEnemyRando(_enabled);
        }

        /// <summary>每个真实场景进入后强制对齐：EnemyRando 会在敌人重建/场景切换时重置字段，逐场景同步一次。</summary>
        public static void FlushEnabledState()
        {
            EnsureReflection();
            if (_settingsType == null || _randoTypeEnum == null) return;
            ApplyToEnemyRando(_enabled);
        }

        // ========== 存档会话标记 ==========
        private static bool _sessionVerified;
        public static bool SessionActive { get; private set; }
        public static void MarkSessionStart() { SessionActive = true; _sessionVerified = false; }
        public static void MarkSessionEnd() { SessionActive = false; _sessionVerified = false; }

        // ========== 反射写字段 ==========

        // EnemyRando 的 RandoType 字段是 ConfigEntry<T>，通过 .Value 属性读写。
        private static bool SetFieldRandoValue(FieldInfo field, object value)
        {
            if (field == null)
            {
                Plugin.Log.LogWarning("[EnemyRandoAdjuster] 目标字段为 null（反射未命中），写入失败——请检查 EnemyRando 字段名");
                return false;
            }
            Type fieldType = field.FieldType;
            if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition().Name == "ConfigEntry`1")
            {
                object entry = TryGetFieldEntry(field);
                if (entry == null)
                {
                    Plugin.Log.LogWarning("[EnemyRandoAdjuster] EnemyRando 配置尚未初始化，跳过本次写入（进入游戏场景后会重试）");
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

        private static bool IsConfigEntry(object value)
        {
            return value != null && value.GetType().IsGenericType
                && value.GetType().GetGenericTypeDefinition().Name == "ConfigEntry`1";
        }

        // ========== 关闭时恢复已被替换的敌人 ==========
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

        // ========== Harmony 补丁 ==========
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