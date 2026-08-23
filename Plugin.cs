// Plugin.cs - 修复后的完整版本
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Newtonsoft.Json;
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
    [BepInPlugin("HardItemRandomizer.SilksongItemRandomizer", "Silksong Item Randomizer", "1.0.0.0")]
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
            SaveGlobalDataNow(); // 重置即时落盘
        }

        // ========== 生命周期 ==========
        private void Awake()
        {
            Instance = this;
            Log = Logger;

            RandomSeed = Config.Bind("General", "RandomSeed", 0, "随机种子 (0 表示随机)");
            ItemRandomEnabled = Config.Bind("General", "ItemRandomEnabled", true, "Enable/disable item randomization");
            CrestRandomEnabled = Config.Bind("General", "CrestRandomEnabled", false, "启用纹章随机（独立开关，仅在物品随机总开关开启时生效）");

            SilkRandomizerEnabled = Config.Bind("Silk Randomizer", "Enabled", false, "启用灵丝获得/消耗随机化（默认关闭，仅在物品随机总开关开启时生效）");
            SilkRandomMin = Config.Bind("Silk Randomizer", "MinAmount", 1, "随机获得/消耗的最小灵丝数量（1-9）");
            SilkRandomMax = Config.Bind("Silk Randomizer", "MaxAmount", 9, "随机获得/消耗的最大灵丝数量（1-9）");

            LoadGlobalData();

            ItemLimitConfig.Init(Config);
            ItemTypeRandomFilter.Init(Config);
            ItemTypeRandomFilter.SetCrestRandomEnabled(CrestRandomEnabled.Value);

            var apiConfig = new ItemRandomizerConfig
            {
                Seed = RandomSeed.Value,
                Enabled = ItemRandomEnabled.Value,
                CrestEnabled = CrestRandomEnabled.Value,
                TrapEnabled = SaveData.TrapEnabled,
                TrapMovementEnabled = SaveData.TrapMovementEnabled,
                TrapDifficulty = ParseTrapDifficulty(SaveData.TrapDifficulty),
                CrestRandomEnabled = CrestRandomEnabled.Value,
                SkillItemRandomEnabled = ItemTypeRandomFilter.EnableSkillItemRandom,
                RelicRandomEnabled = ItemTypeRandomFilter.EnableRelicRandom,
                CurrencyFirstThreshold = SaveData.CurrencyFirstThreshold,
                CurrencySecondThreshold = SaveData.CurrencySecondThreshold,
                SilkSpearPityCount = SaveData.SilkSpearPityCount,
                Limits = new ItemLimitSettings
                {
                    SkillItem = ItemLimitConfig.LimitSkillItem,
                    Relic = ItemLimitConfig.LimitRelic,
                    OtherItem = ItemLimitConfig.LimitOtherItem,
                    UpSlash = ItemLimitConfig.LimitUpSlash,
                    LeftSlash = ItemLimitConfig.LimitLeftSlash,
                    RightSlash = ItemLimitConfig.LimitRightSlash,
                    DashLeft = ItemLimitConfig.LimitDashLeft,
                    DashRight = ItemLimitConfig.LimitDashRight,
                    HarpoonLeft = ItemLimitConfig.LimitHarpoonLeft,
                    HarpoonRight = ItemLimitConfig.LimitHarpoonRight,
                    FloatLeft = ItemLimitConfig.LimitFloatLeft,
                    FloatRight = ItemLimitConfig.LimitFloatRight,
                    WallJumpLeft = ItemLimitConfig.LimitWallJumpLeft,
                    WallJumpRight = ItemLimitConfig.LimitWallJumpRight,
                    Heal = ItemLimitConfig.LimitHeal,
                    NeedleThrow = ItemLimitConfig.LimitNeedleThrow,
                    ThreadSphere = ItemLimitConfig.LimitThreadSphere,
                    HarpoonDash = ItemLimitConfig.LimitHarpoonDash,
                    SilkCharge = ItemLimitConfig.LimitSilkCharge,
                    SilkBomb = ItemLimitConfig.LimitSilkBomb,
                    SilkBossNeedle = ItemLimitConfig.LimitSilkBossNeedle,
                    Needolin = ItemLimitConfig.LimitNeedolin,
                    Parry = ItemLimitConfig.LimitParry,
                    NeedolinMemory = ItemLimitConfig.LimitNeedolinMemory,
                    FastTravel = ItemLimitConfig.LimitFastTravel,
                    EvaHeal = ItemLimitConfig.LimitEvaHeal,
                    Dash = ItemLimitConfig.LimitDash,
                    Brolly = ItemLimitConfig.LimitBrolly,
                    DoubleJump = ItemLimitConfig.LimitDoubleJump,
                    SuperJump = ItemLimitConfig.LimitSuperJump,
                    WallJump = ItemLimitConfig.LimitWallJump,
                    ChargeSlash = ItemLimitConfig.LimitChargeSlash,
                    HeartPiece = ItemLimitConfig.LimitHeartPiece,
                    SpoolPart = ItemLimitConfig.LimitSpoolPart,
                    MaxSilkRegenUp = ItemLimitConfig.LimitMaxSilkRegenUp,
                    UnlockCrestSlot = ItemLimitConfig.LimitUnlockCrestSlot,
                    SimpleKey = ItemLimitConfig.LimitSimpleKey,
                },
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
        }

        private IEnumerator InitTrapsAfterLoad()
        {
            yield return new WaitForSeconds(5f);
            if (SaveData.TrapEnabled)
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
            PreGeneratedMap.OnSceneLoaded(scene);   // ★ 场景加载时为检查点补齐预生成映射
            StartCoroutine(DestroyMarkedPickups(scene));
            if (SaveData.TrapEnabled && scene.name != "Menu_Title" && scene.name != "Menu" && scene.name != "Loading")
            {
                TrapRandomizer.ClearAndRescan();
                StartCoroutine(SpawnTrapsAfterSceneLoad());
            }
            MapStationUnlockPatch.OnSceneLoaded(scene);
            LoreTriggerPatch.OnSceneLoaded(scene);
            MossberryRandomizer.OnSceneLoaded(scene, mode);
            HeroRespawnReset.CheckAfterSceneLoad(scene.name);   // ★ 出梦境重生修复：检测冻结状态温和恢复
        }

        private IEnumerator SpawnTrapsAfterSceneLoad()
        {
            yield return new WaitForSeconds(0.5f);
            TrapRandomizer.SpawnTraps();
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
            // 碎片世界点（丝轴 Silk Spool / 面具 Heart Piece，PrefabCollectable 场景对象，非 CollectableItemPickup）
            // 无原生持久标记，仅由模组 global data 记录后在此销毁；Repacked 对象延迟实例化，等一小段再扫
            yield return new WaitForSeconds(0.3f);
            bool killAllSpools = SaveData.DestroyedPickupKeys.Contains($"spoolscene:{scene.name}");
            bool killAllHearts = SaveData.DestroyedPickupKeys.Contains($"heartpiecescene:{scene.name}");
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (go == null || go.transform == null || go.scene != scene) continue;
                if (string.Equals(go.name, "Silk Spool", StringComparison.OrdinalIgnoreCase))
                {
                    var pos = go.transform.position;
                    var key = $"spool:{scene.name}_{pos.x:F2}_{pos.y:F2}_{pos.z:F2}";
                    if (killAllSpools || SaveData.DestroyedPickupKeys.Contains(key))
                    {
                        Destroy(go);
                        Log.LogInfo("场景加载时销毁已标记丝轴点: " + key);
                    }
                }
                else if (string.Equals(go.name, "Heart Piece", StringComparison.OrdinalIgnoreCase))
                {
                    var pos = go.transform.position;
                    var key = $"heartpiece:{scene.name}_{pos.x:F2}_{pos.y:F2}_{pos.z:F2}";
                    if (killAllHearts || SaveData.DestroyedPickupKeys.Contains(key))
                    {
                        Destroy(go);
                        Log.LogInfo("场景加载时销毁已标记面具点: " + key);
                    }
                }
            }
        }

        private void Update()
        {
            FsmMasterGuard.Tick();
            CheckAutoSave();
        }

        private void OnGUI()
        {
            try
            {
                RecentItemsUI.Draw();
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

        public void DumpAllMappings()
        {
            var seed = RandomSeed.Value;
            Log.LogInfo($"===== 当前种子: {seed} =====");
            var crestList = CrestRandomizer.CrestList;
            if (crestList != null && crestList.Count > 0)
            {
                Log.LogInfo("--- 纹章映射 (来自外部存储) ---");
                foreach (var crest in crestList)
                    Log.LogInfo($"  {crest.name} -> {CrestRandomizer.GetMappedCrestName(crest.name)}");
            }
            else Log.LogInfo("--- 未找到纹章 ---");

            Log.LogInfo("--- 当前场景拾取点映射 ---");
            var pickups = Resources.FindObjectsOfTypeAll<CollectableItemPickup>().Where(p => p.gameObject.scene.isLoaded).ToList();
            if (pickups.Count == 0) Log.LogInfo("当前场景无拾取点。");
            else
            {
                foreach (var p in pickups)
                {
                    var original = p.Item;
                    if (original == null) continue;
                    var rng = new System.Random(seed + p.GetInstanceID());
                    var random = ItemRandomizer.PeekRandomItem(rng);
                    if (random != null)
                        Log.LogInfo($"  {original.name} (位置 {p.transform.position}) -> {random.name}");
                    else Log.LogInfo($"  {original.name} -> 随机失败");
                }
            }
            Log.LogInfo("===============================");
        }

        public static void ShowNotification(string message, float duration = 3f)
        {
            _notificationMessage = message;
            _notificationEndTime = Time.time + duration;
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

        private int ParseTrapDifficulty(string difficultyStr)
        {
            if (string.IsNullOrEmpty(difficultyStr)) return 0;
            return difficultyStr switch
            {
                "Beginner" => 0,
                "Focused" => 1,
                "Overflow" => 2,
                _ => 0
            };
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