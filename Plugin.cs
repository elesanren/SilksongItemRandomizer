// Plugin.cs - 修复后的完整版本
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Newtonsoft.Json;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SilksongItemRandomizer
{
    [BepInPlugin("HardItemRandomizer.GlobalConfig", "Silksong Item Randomizer", "1.0.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        private static string _notificationMessage;
        private static float _notificationEndTime;
        private static GUIStyle _notificationStyle;
        private static Texture2D _bgTex;

        // ========== 配置项 ==========
        public static ConfigEntry<bool> CrestRandomEnabled { get; private set; }
        public static ConfigEntry<int> RandomSeed { get; private set; }
        public static ConfigEntry<bool> ItemRandomEnabled { get; private set; }
        public static Plugin Instance { get; private set; }

        public static ConfigEntry<bool> SilkRandomizerEnabled { get; private set; }
        public static ConfigEntry<int> SilkRandomMin { get; private set; }
        public static ConfigEntry<int> SilkRandomMax { get; private set; }

        // ========== 全局存储 ==========
        public static GlobalSaveData SaveData { get; private set; }
        private static readonly string GlobalSavePath = Path.Combine(Paths.ConfigPath, "SilksongItemRandomizer", "global_save.json");

        // 延迟落盘（批处理高频存档写入，降低同步序列化/写盘开销）
        private static bool _saveDirty;
        private static float _lastSaveTime = float.MinValue;
        private const float SaveDebounceSeconds = 1.5f;

        public static bool PublicItemRandomEnabled
        {
            get => ItemRandomEnabled.Value;
            set
            {
                if (ItemRandomEnabled.Value == value) return;
                ItemRandomEnabled.Value = value;
                Instance.Config.Save();
                SilksongItemRandomizerAPI.SetEnabled(value);
            }
        }

        // ========== 新增：重置存档数据的方法，供 API 调用 ==========
        public static void ResetSaveData()
        {
            SaveData = new GlobalSaveData();
            RecentItemsUI.Reset(); // 同步清空最近物品 UI 运行时缓存，避免重置种子世界后残留上个存档的条目
            SaveGlobalDataNow(); // 重置即时落盘
        }

        // ========== 生命周期 ==========
        private void Awake()
        {
            Instance = this;
            Log = Logger;

            // ========== 统一配置系统：GlobalConfig 承载三 mod 全部条目 ==========
            GlobalConfig.Init(Config);

            RandomSeed = GlobalConfig.RandomSeed;
            ItemRandomEnabled = GlobalConfig.ItemRandomEnabled;
            CrestRandomEnabled = GlobalConfig.CrestRandomEnabled;
            SilkRandomizerEnabled = GlobalConfig.SilkRandomizerEnabled;
            SilkRandomMin = GlobalConfig.SilkRandomMin;
            SilkRandomMax = GlobalConfig.SilkRandomMax;

            // 怪物随机：以 cfg 字段为准，启动时应用到 EnemyRandoAdjuster（未就绪会自动排队，MarkGameReady 后重放）
            EnemyRandoAdjuster.Enabled = GlobalConfig.EnemyRandoAdjustEnabled.Value;
            // 恢复怪物缩放的 cfg 持久化值到运行时内存（LoadFromConfig 需在 Init 之后调用）
            EnemyRandoAdjuster.LoadFromConfig();

            LoadGlobalData();

            ItemLimitConfig.Init(Config);
            ItemTypeRandomFilter.Init(Config);
            ItemTypeRandomFilter.SetCrestRandomEnabled(CrestRandomEnabled.Value);

            var apiConfig = new ItemRandomizerConfig
            {
                Seed = RandomSeed.Value,
                Enabled = ItemRandomEnabled.Value,
                CrestEnabled = CrestRandomEnabled.Value,
                TrapEnabled = GlobalConfig.TrapEnabled.Value,
                TrapMovementEnabled = GlobalConfig.TrapMovementEnabled.Value,
                TrapDifficulty = GlobalConfig.TrapDifficulty.Value,
                CrestRandomEnabled = CrestRandomEnabled.Value,
                SkillItemRandomEnabled = ItemTypeRandomFilter.EnableSkillItemRandom,
                RelicRandomEnabled = ItemTypeRandomFilter.EnableRelicRandom,
                CurrencyFirstThreshold = SaveData.CurrencyFirstThreshold,
                CurrencySecondThreshold = SaveData.CurrencySecondThreshold,
                SilkSpearPityCount = SaveData.SilkSpearPityCount,
                Limits = ItemLimitConfig.CaptureSettings(),
                InfinitePool = new InfinitePoolSettings
                {
                    Silk = ItemLimitConfig.EnableInfSilk,
                    BlueHealth = ItemLimitConfig.EnableInfBlueHealth,
                    Geo300 = ItemLimitConfig.EnableInfGeo300,
                    Shards300 = ItemLimitConfig.EnableInfShards300,
                }
            };
            SilksongItemRandomizerAPI.Initialize(apiConfig);

            // Harmony 补丁统一由 SilksongItemRandomizerAPI 管理：
            //  - 常驻补丁（MapperPermanentPatch 等）在 Initialize 时就注册，始终生效；
            //  - 收单开关补丁在 MarkGameReady()（InitializeAfterLoad 协程内）随 Enable 状态批量应用。
            // 这里不再维护独立的 _harmonyItem，避免两套补丁注册路径导致的重复/漏挂载。
            ItemRandomEnabled.SettingChanged += (s, e) => SilksongItemRandomizerAPI.SetEnabled(ItemRandomEnabled.Value);
            CrestRandomEnabled.SettingChanged += (s, e) => SilksongItemRandomizerAPI.SetCrestEnabled(CrestRandomEnabled.Value);

            _bgTex = MakeTexture(2, 2, new Color(0, 0, 0, 0.7f));
            SpriteCache.EnsureBuilt();
            OverrideBenchwarpLanguage();
            Extracurrencypickup.RegisterAll();

            GeoRockBuilder.Init();                 // ★ 初始化 Core atlas（自建模式）
            FleaRescueBuilder.Init();               // ★ 跳蚤救援克隆模板初始化
            StartCoroutine(InitializeAfterLoad(RandomSeed.Value));
            SceneManager.sceneLoaded += OnSceneLoaded;
            StartCoroutine(InitTrapsAfterLoad());

            Harmony.CreateAndPatchAll(typeof(HeroRespawnReset));

            var hotkeyGo = new GameObject("SilksongItemRandomizer_HotkeyHandler");
            DontDestroyOnLoad(hotkeyGo);
            hotkeyGo.AddComponent<HotkeyHandler>();

            Log.LogInfo("Plugin SilksongItemRandomizer loaded (global save integrated)");
        }

        private IEnumerator InitializeAfterLoad(int seed)
        {
            GameManager gm = null;
            while (gm == null)
            {
                gm = UnityEngine.Object.FindObjectOfType<GameManager>();
                yield return null;
            }
            while (string.IsNullOrEmpty(gm.sceneName))
                yield return null;
            yield return null;

            SilksongItemRandomizerAPI.MarkGameReady();
            bool fullRandom = SilksongItemRandomizerAPI.GetFullRandomMode();
            ItemRandomizer.Initialize(RandomSeed.Value, null, new SilksongItemRandomizerAPI.PluginSaveDataAccessor(), fullRandom);
            CrestRandomizer.Initialize(RandomSeed.Value, CrestRandomEnabled.Value, new SilksongItemRandomizerAPI.CrestSaveDataAccessor());
            // ★ 预生成映射表：从随机池提前摸出所有检查点奖励并落盘
            PreGeneratedMap.Initialize();

            Log.LogInfo($"Randomizer initialized with seed: {RandomSeed.Value}");
            ItemLocalizationRegistrar.RegisterAllKnownItems();

            // 进游戏后重放最近获得物品列表（图集已加载，图标可正常解析）
            RecentItemsUI.RestoreFromSave();
        }

        private IEnumerator InitTrapsAfterLoad()
        {
            yield return new WaitForSeconds(5f);
            if (GlobalConfig.TrapEnabled.Value)
                Log.LogInfo("陷阱随机系统已由 API 初始化");
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SaveGlobalDataNow(); // 离开时强制立即落盘，避免延迟批处理丢最后几秒数据
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // 检查点扫描导出期间跳过所有 mod 场景响应，避免动态生成物污染扫描结果
            if (HotkeyHandler.IsScanningChecks) return;

            if (ItemRandomEnabled.Value)
                PickupPatch.ApplyStateToSceneWithDelay(scene, 0.2f);

            Extracurrencypickup.SpawnPickupsForScene(scene);
            StartCoroutine(DestroyMarkedPickups(scene));
            StartCoroutine(DestroyMarkedFragmentPoints(scene));   // ★ 碎片点防重生：按场景级 key 全对象扫描销毁
            if (GlobalConfig.TrapEnabled.Value && scene.name != "Menu_Title" && scene.name != "Menu" && scene.name != "Loading")
            {
                TrapRandomizer.ClearAndRescan();
                StartCoroutine(SpawnTrapsAfterSceneLoad());
            }
            MapStationUnlockPatch.OnSceneLoaded(scene);
            MapMachineGetPatch.OnSceneLoaded(scene);
            LoreTriggerPatch.OnSceneLoaded(scene);
            MossberryRandomizer.OnSceneLoaded(scene, mode);
            ShellFlowerRandomizer.OnSceneLoaded(scene);   // ★ 花芯：已捡房间紫花消失
            GeoRockBuilder.OnSceneLoaded(scene);   // ★ 钱堆生成：自建+克隆双模式
            GeoRockReplacer.OnSceneLoaded(scene);   // ★ 钱堆替换：清除钱堆→生成拾取点
            // —— 机制一【FleaRescueReplacer】与机制三【FleaAutoSpawner】是两条完全无关的独立链路，
            //    彼此不共享任何生成/判重真值，不要混为一谈 ——
            FleaRescueReplacer.OnSceneLoaded(scene); // ★ 机制一：27 原生场景销毁原生跳蚤→生成拾取点（坐标记已捡）
            FleaAutoSpawner.OnSceneLoaded(scene);    // ★ 机制三：任意识别点实时生成跳蚤；进场景时先当场克隆模板
            if (scene.name == "Menu_Title" || scene.name == "Menu" || scene.name == "Pre_Menu_Intro")
            {
                // 回到主菜单 = 存档会话结束
                EnemyRandoAdjuster.MarkSessionEnd();
            }
            else if (scene.name != "Loading")
            {
                // 每个真实游戏场景都复核一次开关：EnemyRando 会在敌人重建/切换场景时
                // 把字段重置回 Disabled，必须逐场景对齐（Flush 内部自身做了字段值短路）
                EnemyRandoAdjuster.MarkSessionStart();
                StartCoroutine(FlushEnemyRandoConfigAfterSceneLoad());
            }
            HeroRespawnReset.CheckAfterSceneLoad(scene.name);   // ★ 出梦境重生修复：检测冻结状态温和恢复
        }

        private IEnumerator SpawnTrapsAfterSceneLoad()
        {
            yield return new WaitForSeconds(0.5f);
            TrapRandomizer.SpawnTraps();
        }

        private IEnumerator FlushEnemyRandoConfigAfterSceneLoad()
        {
            // 延迟到 EnemyRando 完成本场景处理之后写入，保证我们的开关状态最终生效
            yield return new WaitForSeconds(0.3f);
            EnemyRandoAdjuster.FlushEnabledState();
        }

        private IEnumerator DestroyMarkedPickups(Scene scene)
        {
            yield return null;
            var pickups = Resources.FindObjectsOfTypeAll<CollectableItemPickup>();
            foreach (var p in pickups)
            {
                if (p.gameObject.scene != scene) continue;
                var key = $"{scene.name}_{p.transform.position.x:F2}_{p.transform.position.y:F2}_{p.transform.position.z:F2}";
                if (SaveData.DestroyedPickupKeys.Contains(key))
                {
                    Destroy(p.gameObject);
                    Log.LogInfo("场景加载时销毁已标记点: " + key);
                }
            }
        }

        /// <summary>
        /// 碎片点（丝轴 Silk Spool / 面具 Heart Piece）防重生：
        /// 触发时由 SpoolPartPatch 记到 DestroyedSpoolPointKeys：场景级 key
        /// （spoolscene:{scene} / heartpiecescene:{scene}）及坐标 key（spool:{scene}_x_y_z）。
        /// 重进该场景时按「场景级 key」或「坐标 key 场景前缀」判断该场景是否被摸过，
        /// 命中则全对象扫描（含未激活子对象）用 IsFragmentObject 判据销毁。
        /// 销毁只能按场景判断（场景内只能枚举对象、无法按坐标定位），与 F4 已验证逻辑一致。
        /// </summary>
        private IEnumerator DestroyMarkedFragmentPoints(Scene scene)
        {
            string sceneName = scene.name;
            if (string.IsNullOrEmpty(sceneName)) yield break;
            string sceneLower = sceneName.ToLowerInvariant();

            // 场景级 key（spool:{scene} / heart:{scene}，GiveRandom 触发时记录；spoolscene:/heartpiecescene: 为旧版格式向后兼容）
            bool markSpool = SaveData.DestroyedSpoolPointKeys.Contains("spool:" + sceneLower)
                || SaveData.DestroyedSpoolPointKeys.Contains("spoolscene:" + sceneLower)
                || SaveData.DestroyedSpoolPointKeys.Any(k => k.StartsWith("spool:" + sceneLower + "_"));
            bool markHeart = SaveData.DestroyedSpoolPointKeys.Contains("heart:" + sceneLower)
                || SaveData.DestroyedSpoolPointKeys.Contains("heartpiecescene:" + sceneLower)
                || SaveData.DestroyedSpoolPointKeys.Any(k => k.StartsWith("heartpiece:" + sceneLower + "_"));
            if (!markSpool && !markHeart) yield break;

            // 轮询：场景对象可能延迟实例化（Addressables/Repacked），每 0.4s 扫一次当前场景，
            // 直到找到碎片点并销毁为止（最多约 8s）。解决固定延时可能过早、对象未生成的问题。
            for (int attempt = 0; attempt < 20; attempt++)
            {
                yield return new WaitForSeconds(0.4f);
                int destroyed = 0;
                foreach (var go in HotkeyHandler.EnumerateSceneGameObjects(scene))
                {
                    if (go == null || go.scene != scene) continue;
                    if (!HotkeyHandler.IsFragmentObject(go)) continue;
                    Destroy(go);
                    destroyed++;
                }
                if (destroyed > 0)
                {
                    Log.LogInfo($"[碎片点] 场景 {sceneName} 命中标记(spool={markSpool},heart={markHeart})，销毁碎片点 {destroyed} 个 (第{attempt + 1}次轮询)");
                    yield break;
                }
            }
            Log.LogWarning($"[碎片点] 场景 {sceneName} 已标记但 8s 内未扫描到可销毁碎片点对象");
        }

        private void Update()
        {
            FsmMasterGuard.Tick();
            ChapelFadeRestore.Tick();
            CheckAutoSave();
        }

        private void OnGUI()
        {
            try
            {
                if (_notificationMessage != null && Time.time <= _notificationEndTime)
                {
                    _notificationStyle ??= new GUIStyle(GUI.skin.box)
                    {
                        fontSize = 40,
                        alignment = TextAnchor.MiddleCenter,
                        normal = { textColor = Color.white, background = _bgTex }
                    };
                    var x = (Screen.width - 600f) / 2f;
                    var y = Screen.height / 2 - 100;
                    GUI.Box(new Rect(x, y, 600f, 120f), _notificationMessage, _notificationStyle);
                }
                else _notificationMessage = null;
            }
            catch { }
        }

        public static void ShowNotification(string message, float duration = 3f)
        {
            _notificationMessage = message;
            _notificationEndTime = Time.time + duration;
        }

        // ========== 黑幕遮挡工具（背景加载时遮挡屏幕）==========
        private static GameObject _blackoutGo;

        public static void BlackoutShow()
        {
            try
            {
                if (_blackoutGo != null) return;
                _blackoutGo = new GameObject("PrimeBlackout");
                UnityEngine.Object.DontDestroyOnLoad(_blackoutGo);
                var canvas = _blackoutGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 9999;
                var img = _blackoutGo.AddComponent<UnityEngine.UI.Image>();
                img.color = Color.black;
                img.raycastTarget = false;
                var rect = img.rectTransform;
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                Log.LogInfo("[PrimeBlackout] 黑幕已显示");
            }
            catch (Exception ex)
            {
                Log.LogWarning($"[PrimeBlackout] 显示黑幕失败: {ex.Message}");
            }
        }

        public static void BlackoutHide()
        {
            try
            {
                if (_blackoutGo == null) return;
                UnityEngine.Object.Destroy(_blackoutGo);
                _blackoutGo = null;
                Log.LogInfo("[PrimeBlackout] 黑幕已移除");
            }
            catch (Exception ex)
            {
                Log.LogWarning($"[PrimeBlackout] 移除黑幕失败: {ex.Message}");
            }
        }

        private Texture2D MakeTexture(int width, int height, Color col)
        {
            var pixels = new Color[width * height];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = col;
            var tex = new Texture2D(width, height);
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        // ========== 全局存档相关 ==========
        private void LoadGlobalData()
        {
            try
            {
                if (File.Exists(GlobalSavePath))
                {
                    string json = File.ReadAllText(GlobalSavePath);
                    SaveData = JsonConvert.DeserializeObject<GlobalSaveData>(json) ?? new GlobalSaveData();
                    Log.LogInfo($"加载全局数据成功，版本 {SaveData.Version}");
                }
                else
                {
                    SaveData = new GlobalSaveData();
                    Log.LogInfo("未找到全局存档，创建新存档");
                }

                if (SaveData.Version < 2)
                {
                    SaveData.VirtualUnlimitedProbability = 0.1f;
                    SaveData.CrestUnlockerProbability = 0.1f;
                    SaveData.NormalLimitedProbability = 0.8f;
                    SaveData.MaxGivenPerItem = 2;
                    SaveData.CurrencyFirstThreshold = 30;
                    SaveData.CurrencySecondThreshold = 1000;
                    SaveData.SilkSpearPityCount = 10;
                    SaveData.Version = 2;
                    SaveGlobalData();
                    Log.LogInfo("已为旧存档添加物品随机配置默认值");
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"加载全局数据失败: {ex}");
                SaveData = new GlobalSaveData();
            }
        }

        /// <summary>
        /// 标记全局存档为「脏」。实际写盘延后到 Update() 中批处理（帧末/间隔），
        /// 避免每次字段变更都同步序列化整个 JSON 造成的 GC 与 IO 抖动。
        /// 需要立即落盘的场景请调用 SaveGlobalDataNow()。
        /// </summary>
        public static void SaveGlobalData()
        {
            _saveDirty = true;
        }

        /// <summary>立即落盘（忽略去抖，供 OnDestroy / 重置等关键节点使用）</summary>
        public static void SaveGlobalDataNow()
        {
            _saveDirty = false;
            WriteGlobalData();
        }

        /// <summary>真正执行序列化并写入磁盘</summary>
        private static void WriteGlobalData()
        {
            try
            {
                string dir = Path.GetDirectoryName(GlobalSavePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                string json = JsonConvert.SerializeObject(SaveData, Formatting.Indented);
                File.WriteAllText(GlobalSavePath, json);
            }
            catch (Exception ex)
            {
                Log.LogError($"保存全局数据失败: {ex}");
            }
        }

        /// <summary>供 Update 调用：到达去抖间隔且有脏标记时写盘</summary>
        private static void CheckAutoSave()
        {
            if (!_saveDirty) return;
            float now = UnityEngine.Time.time;
            if (now - _lastSaveTime >= SaveDebounceSeconds || _lastSaveTime == float.MinValue)
            {
                _lastSaveTime = now;
                _saveDirty = false;
                WriteGlobalData();
            }
        }

        public static void AddDestroyedPickupKey(string key)
        {
            SaveData.DestroyedPickupKeys.Add(key);
            SaveGlobalData(); // 去抖落盘（OnDestroy 兜底强制落盘）
        }

        public static void AddDestroyedSpoolPointKey(string key)
        {
            SaveData.DestroyedSpoolPointKeys.Add(key);
            SaveGlobalData(); // 去抖落盘（OnDestroy 兜底强制落盘）
        }

        public static void ResetDestroyedPickupKeys()
        {
            SaveData.DestroyedPickupKeys.Clear();
            SaveGlobalDataNow(); // 即时落盘
        }

        // ========== 修复后的 ResetAllStaticData ==========
        public static void ResetAllStaticData()
        {
            SilksongItemRandomizerAPI.ResetAllData();
            // 使用类名而非实例
            RandomSeed.Value = 0;
            Instance?.Config.Save();
            Log.LogInfo("物品随机MOD所有静态数据已重置，随机系统已重新初始化，并传送回椅子");
        }

        private void OverrideBenchwarpLanguage()
        {
            // 按系统/当前语言选择注入的语言表：
            //   中文（zh*）-> 用中文 zh.json
            //   其他（英文等）-> 用英文 en.json
            bool isChinese = System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
            string resourceName = isChinese ? "SilksongItemRandomizer.Resources.zh.json" : "SilksongItemRandomizer.Resources.en.json";

            string content;
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using (var stream = asm.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        Log.LogWarning($"[Benchwarp] 嵌入语言资源不存在: {resourceName}");
                        return;
                    }
                    using (var reader = new StreamReader(stream))
                        content = reader.ReadToEnd();
                }
            }
            catch (Exception ex)
            {
                Log.LogError($"[Benchwarp] 读取语言资源失败 {resourceName}: {ex.Message}");
                return;
            }

            string benchwarpLangDir = Path.Combine(Paths.PluginPath, "homothety-Benchwarp", "languages");
            string targetFile = Path.Combine(benchwarpLangDir, "en.json");

            try
            {
                if (!Directory.Exists(benchwarpLangDir))
                    Directory.CreateDirectory(benchwarpLangDir);

                // 首次覆盖前备份 Benchwarp 原始 en.json（仅一次），方便恢复。
                string backupFile = targetFile + ".backup";
                if (File.Exists(targetFile) && !File.Exists(backupFile))
                    File.Copy(targetFile, backupFile, true);

                File.WriteAllText(targetFile, content);
                Log.LogInfo($"[Benchwarp] 已按{(isChinese ? "中文" : "英文")}覆盖 {targetFile}（{resourceName}）");
            }
            catch (Exception ex)
            {
                Log.LogError($"[Benchwarp] 覆盖失败: {ex.Message}");
            }
        }

        public void RefreshBenchwarpUI()
        {
            var menu = GameObject.Find("WarpMenu") ?? GameObject.Find("BenchwarpMenu");
            if (menu == null || !menu.activeInHierarchy)
                return;
            menu.SetActive(false);
            menu.SetActive(true);
            Log.LogInfo("[Benchwarp] UI 已刷新");
        }
    }
}