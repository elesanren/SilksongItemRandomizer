using System;
using System.Collections.Generic;
using System.Linq;
using GlobalEnums;
using StartingAbilityPicker;
using TeamCherry.Localization;
using TMProOld;
using UnityEngine;

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 最近获得物品 UI——像素级仿一代 RecentItemsDisplay mod 的 UI 风格：
    ///   - 右上角常驻堆叠列表：标题 "Recent Items"，每条 = 图标(50x50) + 两行文字(名字 / from 区域)
    ///   - 条目 200x50、图标锚 (-0.1,0.5)、文字 400x100 字号24 锚 (1.1,0.5)、行距 0.06（比例锚点，随分辨率缩放）
    ///   - 新条目在顶部，旧条目依次下移；队列容量 10（超出销毁最旧），同屏最多显示 5 条（超出隐藏不销毁）
    ///   - 纯静态布局：仅 AddItem 时重排一次；CanvasGroup 不拦截输入，组件 raycastTarget 全关
    ///   - 持久化最近 10 条 {图标名, 文本} 到 GlobalSaveData，进游戏后重放
    /// </summary>
    public static class RecentItemsUI
    {
        private const int MaxStored = 10;   // 队列容量/持久化条数（超出移除最旧）
        private const int MaxVisible = 5;   // 同屏最多显示条数（与原 mod 默认 MaxItems 一致）

        // 原 mod GlobalSettings.DefaultAnchor = (0.9, 0.9)
        private static readonly Vector2 AnchorPoint = new(0.9f, 0.9f);

        private static GameObject _canvas;
        private static readonly Queue<GameObject> _items = new();
        private static bool _restorePending = true;
        private static bool _visible = true;
        private static TMP_FontAsset _font;       // 条目正文：当前语言 body 字体
        private static TMP_FontAsset _titleFont;  // 标题：Trajan（官方弹窗标题同款，仅大写字形）
        // 存档历史条目暂存区：与旧版时机一致——进游戏不显示，
        // 首次真实获得物品时才连历史一起铺开
        private static readonly List<KeyValuePair<string, string>> _pendingRestore = new();

        public static bool IsVisible => _visible && _canvas != null && _canvas.activeSelf;

        /// <summary>记录一条奖励（兼容原签名：全部 8 个调用点传 IRandomReward）。
        /// 文本格式仿原 mod DEFAULT_MESSAGE_FORMAT "{0}&lt;br&gt;from {1}"：
        /// 名字取 DisplayName，来源取当前地图区域官方译名（GameMap 同款取法）。</summary>
        public static void AddItem(IRandomReward reward)
        {
            if (reward == null) return;
            string name;
            try { name = reward.DisplayName ?? reward.Id; }
            catch { name = reward.Id; }
            Sprite icon = null;
            try { icon = reward.Icon; }
            catch (Exception ex) { Plugin.Log.LogWarning($"[RecentItems] 取图标失败: {reward.Id} - {ex.Message}"); }
            // 右下角官方横幅：原生 CollectableItem 的自弹已被 NativePopupDetector 发奖窗口内拦截
            // （CollectableUIMsg.Spawn 里非我们 BannerItem 的调用一律跳过），因此这里只需同步弹一条，
            // 既无重复、也不漏虚拟奖励。
            TrySpawnBanner(reward, icon, name);
            // 可旋转展示的奖励（如方向权限）：显示端旋转 + 压扁长轴（后处理，不动纹理）。
            float rotation = reward is IRotatableIcon ri ? ri.IconRotationAngle : 0f;
            AddEntry(icon != null ? icon.name : "", name, GetCurrentAreaName(), rotation);
        }

        /// <summary>
        /// 右下角官方 CollectableUIMsg 横幅：所有类型奖励统一入口（与右上列表同批）。
        /// 可旋转展示的奖励（方向权限）按 IconRotationAngle 旋转 + 压扁长轴（后处理，不动纹理）。
        /// </summary>
        private static void TrySpawnBanner(IRandomReward reward, Sprite icon, string name)
        {
            try
            {
                if (string.IsNullOrEmpty(name)) return;
                // 统一的官方横幅入口：原生 CollectableItem 自弹已由 NativePopupDetector 发奖窗口内拦截，
                // 这里只弹一条原生样式横幅；方向权限等虚奖励也一并正确显示。
                var item = new BannerItem(name, icon ?? reward?.Icon);
                var msg = CollectableUIMsg.Spawn(item);
                if (msg == null || !(reward is IRotatableIcon rotatable)) return;
                GameObject root = msg.gameObject;
                var srField = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
                var sr = typeof(CollectableUIMsg).GetField("icon", srField)?.GetValue(msg) as SpriteRenderer;
                var target = sr != null ? sr.transform : SpriteTextureUtil.FindRenderTransform(root, "Icon");
                if (target == null) return;
                SpriteTextureUtil.ApplyRotationAndCompress(target, rotatable.IconRotationAngle, 0.5f);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[RecentItems] 横幅弹出失败: {ex.Message}");
            }
        }

        /// <summary>进游戏后把存档历史条目暂存（由 Plugin 在随机器就绪后调用一次）。
        /// 不建 UI 不显示；首次 AddItem 时才连同历史一起铺开，时机与旧版一致。</summary>
        public static void RestoreFromSave()
        {
            if (!_restorePending) return;
            _restorePending = false;
            try
            {
                foreach (var e in Plugin.SaveData.RecentItemList)
                    _pendingRestore.Add(new KeyValuePair<string, string>(e?.IconName, e?.Text ?? ""));
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[RecentItems] 重放暂存失败: {ex.Message}"); }
        }

        /// <summary>重置种子世界/重置存档时同步清空运行时缓存：
        /// 清掉 RestoreFromSave 暂存的跨档历史（_pendingRestore）与已铺开的 UI 条目（_items），
        /// 避免局内重置后 UI 仍显示上个存档的最近物品。数据层（Plugin.SaveData）由 ResetSaveData 重建，
        /// 此处仅同步 UI 运行时状态。</summary>
        public static void Reset()
        {
            try
            {
                _pendingRestore.Clear();
                while (_items.Count > 0)
                {
                    var go = _items.Dequeue();
                    if (go != null) UnityEngine.Object.Destroy(go);
                }
                _restorePending = false;
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[RecentItems] 重置失败: {ex.Message}"); }
        }

        public static void Toggle()
        {
            _visible = !_visible;
            ApplyVisibility();
        }

        // ========== 内部 ==========

        private static void AddEntry(string iconName, string displayName, string source, float rotation = 0f)
        {
            // 原 mod GetMessage(): "{名字}<br>from {来源}"，无来源时只显示名字
            string text = string.IsNullOrEmpty(source) ? displayName : $"{displayName}\nfrom {source}";

            try
            {
                var list = Plugin.SaveData.RecentItemList;
                list.Add(new RecentItemsEntry { IconName = iconName ?? "", Text = text ?? "" });
                while (list.Count > MaxStored) list.RemoveAt(0);
                Plugin.SaveGlobalData();
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[RecentItems] 落盘失败: {ex.Message}"); }

            // 首次真实获得物品：先把存档历史条目补建出来（隐藏在队列尾部，超出可见数的自然不显示）
            if (_pendingRestore.Count > 0)
            {
                foreach (var p in _pendingRestore)
                {
                    EnsureCanvas();
                    if (_canvas == null) return;
                    BuildEntry(FindSprite(p.Key), p.Value);
                }
                _pendingRestore.Clear();
            }

            EnsureCanvas();
            if (_canvas == null) return;

            BuildEntry(FindSprite(iconName), text, rotation);
            if (_items.Count > MaxStored)
            {
                UnityEngine.Object.Destroy(_items.Dequeue());
            }
            UpdatePositions();
        }

        /// <summary>当前地图区域的官方译名（GameMap.cs:624 同款取法），失败回退空串。</summary>
        private static string GetCurrentAreaName()
        {
            try
            {
                var gm = GameManager.instance;
                if (gm == null) return "";
                MapZone zone = gm.GetCurrentMapZoneEnum();
                return Language.Get(zone.ToString(), "Map Zones").Replace("<br>", "");
            }
            catch { return ""; }
        }

        private static void EnsureCanvas()
        {
            if (_canvas != null) return;
            try
            {
                _canvas = new GameObject("RecentItemsCanvas");
                UnityEngine.Object.DontDestroyOnLoad(_canvas);

                var canvas = _canvas.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 1000;
                // Silksong 的 uGUI 为 package 版：CanvasScaler 在 UnityEngine.UI 命名空间
                var scaler = _canvas.AddComponent<UnityEngine.UI.CanvasScaler>();
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 1f;
                var group = _canvas.AddComponent<CanvasGroup>();
                group.interactable = false;
                group.blocksRaycasts = false;

                if (_font == null) ResolveFont();

                // 标题：原 mod CreateTextPanel("Recent Items", 24, MiddleCenter, (200,100),
                //          anchor = AnchorPoint + (-0.025, +0.05))；
                //          恒全大写英文 + Trajan（官方弹窗标题同款），缺字形问题不复存在
                CreateLabel("RECENT ITEMS", 24f,
                    AnchorPoint + new Vector2(-0.025f, 0.05f), new Vector2(200f, 100f),
                    TextAnchor.MiddleCenter, title: true);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[RecentItems] Canvas 创建失败: {ex}");
                _canvas = null;
            }
        }

        /// <summary>单条目：原 mod CreateBasePanel((200,50)) + 图标 ImagePanel((50,50), 锚(-0.1,0.5))
        /// + 文字面板((400,100), 字号24, MiddleLeft, 锚(1.1,0.5))。</summary>
        private static void BuildEntry(Sprite sprite, string text, float rotation = 0f)
        {
            var panel = new GameObject("RecentItem");
            panel.transform.SetParent(_canvas.transform, false);

            var rt = panel.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = AnchorPoint;
            rt.sizeDelta = new Vector2(200f, 50f);
            _items.Enqueue(panel);

            if (sprite != null)
            {
                var iconGo = new GameObject("Icon");
                iconGo.transform.SetParent(panel.transform, false);
                var iconRt = iconGo.AddComponent<RectTransform>();
                iconRt.anchorMin = iconRt.anchorMax = new Vector2(-0.1f, 0.5f);
                iconRt.sizeDelta = new Vector2(50f, 50f);
                var image = iconGo.AddComponent<UnityEngine.UI.Image>();
                image.sprite = sprite;
                image.preserveAspect = true;
                image.raycastTarget = false;
                if (sprite.name == "bellbench_toll_machine")
                    iconRt.localRotation = Quaternion.Euler(0f, 0f, 180f);
                // 可旋转展示图标：显示端旋转 + 压扁长轴（后处理，不动纹理），与右下横幅一致
                if (rotation != 0f)
                    SpriteTextureUtil.ApplyRotationAndCompress(iconRt, rotation, 0.5f);
            }

            var labelGo = new GameObject("Text");
            labelGo.transform.SetParent(panel.transform, false);
            var labelRt = labelGo.AddComponent<RectTransform>();
            labelRt.anchorMin = labelRt.anchorMax = new Vector2(1.1f, 0.5f);
            labelRt.sizeDelta = new Vector2(400f, 100f);
            BuildLabel(labelGo, text, 24, TextAnchor.MiddleLeft);
            // 以文字实际渲染高度收紧矩形：MiddleLeft 下矩形中心=文字视觉中心=图标中心，
            // 消除中文字体行高差导致的上下错位
            try
            {
                var tmp = labelGo.GetComponent<TMP_Text>();
                if (tmp != null)
                    labelRt.sizeDelta = new Vector2(400f, Mathf.Max(50f, tmp.preferredHeight));
            }
            catch { }
        }

        /// <summary>画布直属文字（标题）：锚点比例定位，原 mod CreateTextPanel 同构。</summary>
        private static void CreateLabel(string text, float fontSize, Vector2 anchorPos,
            Vector2 size, TextAnchor alignment, bool title = false)
        {
            var go = new GameObject("Title");
            go.transform.SetParent(_canvas.transform, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = anchorPos;
            rt.sizeDelta = size;
            BuildLabel(go, text, Mathf.RoundToInt(fontSize), alignment, title);
        }

        /// <summary>
        /// 从游戏已加载资源中抠当前语言的官方正文字体。
        /// 机制（从 fsm_full fonts_assets_* bundle 确认）：游戏按语言分包加载字体
        /// （中文=chinese_body/NotoSerifCJKsc，英文=Amor Serif Text Pro SDF，俄文=russian_body…），
        /// 运行时枚举内存中的 TMP_FontAsset 天然就是当前语言的字体，自动跟随游戏语言。
        /// 注意避开 Trajan（官方弹窗标题装饰字体，静态图集缺 n 等字形）与 ARIAL SDF（无中文）。
        /// </summary>
        private static void ResolveFont()
        {
            try
            {
                var all = Resources.FindObjectsOfTypeAll<TMP_FontAsset>()
                    .Where(f => f != null && !string.IsNullOrEmpty(f.name)).Distinct().ToList();
                if (all.Count == 0)
                {
                    Plugin.Log.LogWarning("[RecentItems] 内存中无 TMP 字体资产");
                    return;
                }
                Plugin.Log.LogInfo($"[RecentItems] 可用 TMP 字体: {string.Join(", ", all.Select(f => f.name))}");

                string Pick(Func<string, bool> pred) =>
                    all.FirstOrDefault(f => pred(f.name.ToUpperInvariant()))?.name;

                // 按当前游戏语言挑对应语言的 body 正文字体（各语言图集只含本语种字形，
                // 挑错语言会出现方框；名单顺序不定，不能 FirstOrDefault 了事）
                string want;
                try
                {
                    want = Language.CurrentLanguage().ToString().ToUpperInvariant() switch
                    {
                        "ZH" => "CHINESE_BODY",
                        "ZH_TW" => "CHINESE_TRAD_BODY",
                        "JA" => "JAPANESE_BODY",
                        "KO" => "KOREAN_BODY",
                        "RU" => "RUSSIAN_BODY",
                        _ => "AMOR SERIF TEXT PRO REGULAR SDF"
                    };
                }
                catch { want = null; }

                var picked = (want != null ? Pick(n => n == want) : null)
                    ?? Pick(n => n.Contains("BODY") && !n.Contains("DO_NOT_USE"))
                    ?? Pick(n => !n.Contains("TRAJAN") && !n.Contains("ARIAL")
                                && !n.Contains("DO_NOT_USE") && !n.Contains("TITLE"));
                _font = all.First(f => f.name == picked);
                Plugin.Log.LogInfo($"[RecentItems] 使用字体: {_font.name}");

                // 标题用 Trajan（官方弹窗标题同款），文本恒全大写以避开其缺小写字形的图集
                _titleFont = all.FirstOrDefault(f => f.name.ToUpperInvariant().Contains("TRAJAN"));
                if (_titleFont != null)
                    Plugin.Log.LogInfo($"[RecentItems] 标题字体: {_titleFont.name}");
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[RecentItems] 字体解析失败: {ex.Message}"); }
        }

        /// <summary>
        /// 文字标签。title=true 时用 Trajan（官方弹窗标题字体，仅大写字形，配合全大写标题文本），
        /// 否则用当前语言正文字体。防御式创建：禁用状态下 AddComponent 不触发 Awake，
        /// 先显式设好字体再激活，保证运行期不会走到 TMP_Settings.defaultFontAsset
        /// （该 getter 在本游戏中因 Settings 资产未加载而必然 NRE）。
        /// </summary>
        private static void BuildLabel(GameObject go, string text, float fontSize, TextAnchor alignment,
            bool title = false)
        {
            // 防御式创建：禁用状态下 AddComponent 不触发 Awake，先显式设好字体再激活，
            // 保证运行期不会走到 TMP_Settings.defaultFontAsset（该路径在本游戏中必然 NRE）
            bool wasActive = go.activeSelf;
            if (wasActive) go.SetActive(false);
            try
            {
                var tmp = go.GetComponent<TextMeshProUGUI>() ?? go.AddComponent<TextMeshProUGUI>();
                var font = title && _titleFont != null ? _titleFont : _font;
                if (font != null) tmp.font = font;
                tmp.text = text;
                tmp.fontSize = fontSize;
                tmp.alignment = alignment == TextAnchor.MiddleCenter
                    ? TextAlignmentOptions.Center
                    : TextAlignmentOptions.Left;
                tmp.color = Color.white;
                tmp.raycastTarget = false;
                tmp.enableWordWrapping = false;
                tmp.OverflowMode = TextOverflowModes.Overflow;
            }
            finally
            {
                if (wasActive) go.SetActive(true);
            }
        }

        /// <summary>一次性重排（原 mod UpdatePositions 原样移植）：最新条目在锚点处，
        /// 向下按 0.06 间距排开；i 自减后判断 SetActive(i &lt; MaxItems - 1)，即最多可见 MaxVisible 条。</summary>
        private static void UpdatePositions()
        {
            int i = _items.Count - 1;
            foreach (var item in _items)
            {
                if (item == null) continue;
                Vector2 newPos = AnchorPoint + new Vector2(0f, -0.06f * i--);
                var rt = item.GetComponent<RectTransform>();
                rt.anchorMin = newPos;
                rt.anchorMax = newPos;
                item.SetActive(i < MaxVisible - 1);
            }
        }

        private static void ApplyVisibility()
        {
            if (_canvas != null) _canvas.SetActive(_visible);
        }

        private static Sprite FindSprite(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return SpriteCache.Find(name);
        }

        /// <summary>右下角官方 CollectableUIMsg 横幅数据（实现 ICollectableUIMsgItem，照 RewardCore.DirectionBannerItem 同构）。</summary>
        internal sealed class BannerItem : ICollectableUIMsgItem
        {
            private readonly string _name;
            private readonly Sprite _icon;
            public BannerItem(string name, Sprite icon) { _name = name; _icon = icon; }
            public UnityEngine.Object GetRepresentingObject() => null;
            public Sprite GetUIMsgSprite() => _icon;
            public string GetUIMsgName() => _name;
            public float GetUIMsgIconScale() => 1f;
            public bool HasUpgradeIcon() => false;
        }
    }

    /// <summary>
    /// 原生弹窗拦截（仅随机器发奖窗口内生效）：patch CollectableUIMsg.Spawn。
    /// 原生物品（CollectableItem）在 Collect(showPopup=true) 时原生自弹官方横幅，
    /// 其调用在 SavedItemReward.Give 发奖窗口（Arm/Disarm）内被拦截跳过，从而只由
    /// RecentItemsUI.AddItem 统一弹一条，根治重复。
    /// 窗口外（普通游玩/未随机化流程）原生弹窗照常放行，不影响原生既定弹窗。
    /// </summary>
    public static class NativePopupDetector
    {
        private static bool _armed;

        internal static void Arm() { _armed = true; }
        internal static void Disarm() { _armed = false; }

        [HarmonyLib.HarmonyPatch(typeof(CollectableUIMsg), nameof(CollectableUIMsg.Spawn),
            new System.Type[] { typeof(ICollectableUIMsgItem), typeof(Color), typeof(CollectableUIMsg), typeof(bool) })]
        internal static class SpawnPatch
        {
            [HarmonyLib.HarmonyPrefix]
            private static bool Prefix(ICollectableUIMsgItem item)
            {
                if (item is RecentItemsUI.BannerItem) return true; // 我们自己的，放行
                if (!_armed) return true;                          // 非发奖窗口：原生照常弹
                return false;                                      // 发奖窗口内原生的：毙掉，由我们弹
            }
        }
    }
}
