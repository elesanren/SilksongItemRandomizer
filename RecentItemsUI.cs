using System;
using System.Collections.Generic;
using System.Linq;
using StartingAbilityPicker;
using UnityEngine;

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 最近获得物品 UI 显示
    /// 改造后：不再依赖 Plugin 的静态字段，仅依赖 ItemRandomizer 和本地化
    /// 保留所有原有功能：最多显示5个最近获得的物品，自动隐藏，可手动开关
    /// </summary>
    public static class RecentItemsUI
    {
        private static readonly Queue<IRandomReward> RecentRewards = new();
        private const int MaxItems = 5;
        private static Rect _windowRect = new(20f, 300f, 700f, 600f);
        private static bool _showWindow;
        private static float _hideTime;

        private static readonly Dictionary<string, Sprite> _iconCache = new();
        private static Sprite _defaultFallbackIcon;

        public static bool IsVisible => _showWindow;

        public static void AddItem(SavedItem item) => AddItem(new SavedItemReward(item));
        public static void AddItem(IRandomReward reward)
        {
            if (reward == null) return;
            RecentRewards.Enqueue(reward);
            while (RecentRewards.Count > MaxItems) RecentRewards.Dequeue();
            _showWindow = true;
            _hideTime = Time.time + 10f;
        }

        public static void Toggle()
        {
            _showWindow = !_showWindow;
            if (_showWindow) _hideTime = float.MaxValue;
        }

        public static void UpdateAutoHide()
        {
            if (_showWindow && Time.time >= _hideTime) _showWindow = false;
        }

        public static void Draw()
        {
            if (!_showWindow) return;
            _windowRect = GUILayout.Window(999, _windowRect, DrawWindow, GUIContent.none, GetWindowStyle());
        }

        /// <summary>
        /// 透明、无边框、无标题栏的窗口样式（背景置空 + 去掉上下边框，使标题栏塌陷）
        /// </summary>
        private static GUIStyle _transparentWindowStyle;
        private static GUIStyle GetWindowStyle()
        {
            if (_transparentWindowStyle == null)
            {
                _transparentWindowStyle = new GUIStyle(GUI.skin.window);
                _transparentWindowStyle.normal.background = null;
                _transparentWindowStyle.onNormal.background = null;
                _transparentWindowStyle.border = new RectOffset(0, 0, 0, 0);
                _transparentWindowStyle.padding = new RectOffset(0, 0, 0, 0);
                _transparentWindowStyle.margin = new RectOffset(0, 0, 0, 0);
            }
            return _transparentWindowStyle;
        }

        private static void DrawWindow(int id)
        {
            var originalFontSize = GUI.skin.label.fontSize;
            GUI.skin.label.fontSize = 32;

            GUILayout.BeginVertical();
            // 直接遍历 Queue 避免每帧 ToList() 分配
            foreach (var reward in RecentRewards)
            {
                try
                {
                    DrawRewardItem(reward);
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"绘制奖励失败: {reward?.Id} - {ex.Message}");
                    GUILayout.Label("❌ 显示错误");
                }
            }
            GUILayout.EndVertical();

            GUI.DragWindow();
            GUI.skin.label.fontSize = originalFontSize;
        }

        private static void DrawRewardItem(IRandomReward reward)
        {
            GUILayout.BeginHorizontal();

            Sprite icon = GetIconForReward(reward);
            if (icon == null) icon = GetDefaultFallbackIcon();

            float iconWidth = 96f;
            float iconHeight = 96f;
            Rect texCoords = new Rect(0, 0, 1, 1);

            if (icon != null && icon.texture != null)
            {
                Rect texRect = icon.textureRect;
                float texW = icon.texture.width;
                float texH = icon.texture.height;
                texCoords = new Rect(texRect.x / texW, texRect.y / texH, texRect.width / texW, texRect.height / texH);
                if (texRect.width > 0 && texRect.height > 0)
                {
                    iconHeight = iconWidth * (texRect.height / texRect.width);
                    if (iconHeight > 180f) iconHeight = 180f;
                }
            }

            Rect iconRect = GUILayoutUtility.GetRect(iconWidth, iconHeight, GUILayout.Width(iconWidth), GUILayout.Height(iconHeight));
            if (icon != null && icon.texture != null)
                GUI.DrawTextureWithTexCoords(iconRect, icon.texture, texCoords);
            else
            {
                GUI.Box(iconRect, "");
                GUI.Label(iconRect, "?", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 36 });
            }

            string displayName;
            try { displayName = reward.DisplayName; }
            catch { displayName = reward.Id; }
            // 文字往后挪一点，去掉前面的圆点
            GUILayout.Space(15f);
            GUILayout.Label(displayName, GUILayout.Height(iconHeight));
            GUILayout.EndHorizontal();
        }

        private static Sprite GetIconForReward(IRandomReward reward)
        {
            try
            {
                if (reward is SavedItemReward saved)
                {
                    try { return saved.Icon; }
                    catch { return null; }
                }

                if (_iconCache.TryGetValue(reward.Id, out var cached))
                    return cached;

                Sprite found = null;
                string displayName = reward.DisplayName;

                if (displayName.Contains(Locale.Get("灵丝")))
                    found = FindSprite("silk_heart_inv_icon");
                else if (displayName.Contains(Locale.Get("生命")))
                    found = FindSprite("Inv_health_backboard_SS");
                else if (displayName.Contains(Locale.Get("念珠")))
                    found = FindSprite("coinget_01");
                else if (displayName.Contains(Locale.Get("甲壳")))
                    found = FindSprite("Shell_shard_icon");
                else if (displayName.Contains(Locale.Get("蓝血")))
                    found = FindSprite("Icon_Inv_Blue_Health_Blood");
                else if (displayName.Contains(Locale.Get("灵丝碎片")))
                    found = FindSprite("silk_heart_inv_icon_empty");
                else if (displayName.Contains(Locale.Get("丝轴碎片")))
                    found = FindSprite("spool_upgrade_pickup");
                else if (displayName.Contains(Locale.Get("面具碎片")))
                    found = FindSprite("mask_first");
                else if (displayName.Contains(Locale.Get("丝线恢复上限")))
                    found = FindSprite("prompt_silkheart");
                else if (displayName.Contains(Locale.Get("完全恢复")))
                    found = FindSprite("Inv_health_backboard_SS");
                else if (reward.Id == "virt:UnlockCrestSlot")
                {
                    var slotType = ItemRandomizer.LastUnlockedSlotType;
                    if (slotType.HasValue)
                    {
                        string typeName = slotType.Value.ToString().ToLower();
                        if (typeName.Contains("attack"))
                            found = FindSprite("UI_tool_slot_attack0000");
                        else if (typeName.Contains("defend"))
                            found = FindSprite("UI_tool_slot_defend0000");
                        else if (typeName.Contains("socket") || typeName.Contains("tool") || typeName.Contains("item"))
                            found = FindSprite("UI_tool_slot_socket0000");
                    }
                    if (found == null)
                    {
                        string[] options = { "UI_tool_slot_attack0000", "UI_tool_slot_defend0000", "UI_tool_slot_socket0000" };
                        string randomName = options[UnityEngine.Random.Range(0, options.Length)];
                        found = FindSprite(randomName);
                    }
                    if (found == null)
                        found = FindSprite("spool_upgrade_pickup") ?? FindSprite("simple_key_icon") ?? FindSprite("mask_first");
                }

                _iconCache[reward.Id] = found;
                return found;
            }
            catch (Exception ex)
            {
                // 记录异常但不崩溃，返回默认图标
                Plugin.Log.LogWarning($"GetIconForReward 异常: {ex.Message} for reward {reward?.Id}");
                return GetDefaultFallbackIcon();
            }
        }

        private static Sprite FindSprite(string name) => SpriteCache.Find(name);

        private static Sprite GetDefaultFallbackIcon()
        {
            if (_defaultFallbackIcon == null)
            {
                var tex = new Texture2D(1, 1);
                tex.SetPixel(0, 0, Color.white);
                tex.Apply();
                _defaultFallbackIcon = Sprite.Create(tex, new Rect(0, 0, 1, 1), Vector2.zero);
            }
            return _defaultFallbackIcon;
        }
    }
}