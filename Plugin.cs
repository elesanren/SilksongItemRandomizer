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

        private Harmony _harmonyItem;

        // ========== 全局存储 ==========
        public static GlobalSaveData SaveData { get; private set; }
        private static readonly string GlobalSavePath = Path.Combine(Paths.ConfigPath, "SilksongItemRandomizer", "global_save.json");

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
            SaveGlobalData();
        }

        // ========== 生命周期 ==========
        private void Awake()
        {
            Instance = this;
            Log = Logger;

            RandomSeed = Config.Bind("General", "RandomSeed", 0, "随机种子 (0 表示随机)");
            ItemRandomEnabled = Config.Bind("General", "ItemRandomEnabled", true, "Enable/disable item randomization");
            CrestRandomEnabled = Config.Bind("General", "CrestRandomEnabled", true, "启用纹章随机（独立开关，仅在物品随机总开关开启时生效）");
            SilkRandomizerEnabled = Config.Bind("Silk Randomizer", "Enabled", true, "启用灵丝获得/消耗随机化");
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
                },
                InfinitePool = new InfinitePoolSettings
                {
                    Silk = ItemLimitConfig.EnableInfSilk,
                    FullRestore = ItemLimitConfig.EnableInfFullRestore,
                    BlueHealth = ItemLimitConfig.EnableInfBlueHealth,
                    Geo300 = ItemLimitConfig.EnableInfGeo300,
                    Shards300 = ItemLimitConfig.EnableInfShards300,
                    SilkParts = ItemLimitConfig.EnableInfSilkParts,
                }
            };
            SilksongItemRandomizerAPI.Initialize(apiConfig);

            _harmonyItem = new Harmony("SilksongItemRandomizer.ItemPatches");
            if (ItemRandomEnabled.Value)
            {
                ApplyItemPatches();
                PickupPatch.EnableRandomizer();
            }
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
            ItemRandomizer.Initialize(RandomSeed.Value, null, new SilksongItemRandomizerAPI.PluginSaveDataAccessor(), false);
            CrestRandomizer.Initialize(RandomSeed.Value, CrestRandomEnabled.Value, new SilksongItemRandomizerAPI.CrestSaveDataAccessor());
            
            Log.LogInfo($"Randomizer initialized with seed: {RandomSeed.Value}");
            ItemLocalizationRegistrar.RegisterAllKnownItems();
        }

        public void ApplyItemPatches()
        {
            if (!ItemRandomEnabled.Value) return;
            _harmonyItem.PatchAll(typeof(PickupPatch));
            PickupPatch.Initialize(new SilksongItemRandomizerAPI.PluginSaveDataAccessor());
            _harmonyItem.PatchAll(typeof(CurrencyCollectPatch));
            _harmonyItem.PatchAll(typeof(TryGetPatch));
            _harmonyItem.PatchAll(typeof(CrestRandomizePatch));
            CrestRandomizePatch.Initialize(new SilksongItemRandomizerAPI.PluginSaveDataAccessor());
            _harmonyItem.PatchAll(typeof(ShopMenuStock_BuildItemList_Patch));
            ShopMenuStock_BuildItemList_Patch.Initialize(new SilksongItemRandomizerAPI.PluginSaveDataAccessor());
            _harmonyItem.PatchAll(typeof(ShopItemStats_Purchase_Patch));
            _harmonyItem.PatchAll(typeof(SilkSpearPityPatch));
            SilkSpearPityPatch.Initialize(new SilksongItemRandomizerAPI.PluginSaveDataAccessor());
            _harmonyItem.PatchAll(typeof(BenchRespawnPatch));
            BenchRespawnPatch.Initialize(new SilksongItemRandomizerAPI.PluginSaveDataAccessor());
            _harmonyItem.PatchAll(typeof(SilkRandomizerPatch));
            _harmonyItem.PatchAll(typeof(Extracurrencypickup));
            Extracurrencypickup.Initialize(new SilksongItemRandomizerAPI.PluginSaveDataAccessor());
            try
            {
                EnemyRandoAdjuster.TryPatch(_harmonyItem);
            }
            catch (Exception ex)
            {
                Log.LogWarning($"EnemyRandoAdjuster 补丁跳过（可能缺少 EnemyRando）: {ex.Message}");
            }
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
            SaveGlobalData();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (ItemRandomEnabled.Value)
                PickupPatch.ApplyStateToSceneWithDelay(scene, 0.2f);

            Extracurrencypickup.SpawnPickupsForScene(scene);
            StartCoroutine(DestroyMarkedPickups(scene));
            if (SaveData.TrapEnabled && scene.name != "Menu_Title" && scene.name != "Menu" && scene.name != "Loading")
            {
                TrapRandomizer.ClearAndRescan();
                StartCoroutine(SpawnTrapsAfterSceneLoad());
            }
            CrestRandomizePatch.OnSceneLoaded(scene, mode);
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
                var key = $"{scene.name}_{p.transform.position.x:F1}_{p.transform.position.y:F1}_{p.transform.position.z:F1}";
                if (SaveData.DestroyedPickupKeys.Contains(key))
                {
                    Destroy(p.gameObject);
                    Log.LogInfo("场景加载时销毁已标记点: " + key);
                }
            }
        }

        private void Update() => RecentItemsUI.UpdateAutoHide();

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

        public static void SaveGlobalData()
        {
            try
            {
                string dir = Path.GetDirectoryName(GlobalSavePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                string json = JsonConvert.SerializeObject(SaveData, Formatting.Indented);
                File.WriteAllText(GlobalSavePath, json);
            }
            catch (Exception ex)
            {
                Log.LogError($"保存全局数据失败: {ex}");
            }
        }

        public static void AddDestroyedPickupKey(string key)
        {
            SaveData.DestroyedPickupKeys.Add(key);
            SaveGlobalData();
        }

        public static void ResetDestroyedPickupKeys()
        {
            SaveData.DestroyedPickupKeys.Clear();
            SaveGlobalData();
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
            if (!System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                return;

            string sourceFile = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "en.json");
            string benchwarpLangDir = Path.Combine(Paths.PluginPath, "homothety-Benchwarp", "languages");
            string targetFile = Path.Combine(benchwarpLangDir, "en.json");

            if (!File.Exists(sourceFile))
            {
                Log.LogWarning($"[Benchwarp] 源文件不存在: {sourceFile}");
                return;
            }

            try
            {
                if (!Directory.Exists(benchwarpLangDir))
                    Directory.CreateDirectory(benchwarpLangDir);

                string backupFile = targetFile + ".backup";
                if (File.Exists(targetFile) && !File.Exists(backupFile))
                    File.Copy(targetFile, backupFile, true);

                File.Copy(sourceFile, targetFile, true);
                Log.LogInfo($"[Benchwarp] 已覆盖语言文件: {sourceFile} -> {targetFile}");
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