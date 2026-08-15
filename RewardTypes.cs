// RewardTypes.cs - 补充缺失的 Item 属性和 LimitedVirtualReward
using StartingAbilityPicker;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SilksongItemRandomizer
{
    // ========== 奖励接口 ==========
    public interface IRandomReward
    {
        string Id { get; }
        string DisplayName { get; }
        Sprite Icon { get; }
        void Give();
        bool IsAtMax();
    }

    // ========== 配置类 ==========
    public class ItemLimitSettings
    {
        public int SkillItem = 2, Relic = 2, OtherItem = 2;
        public int UpSlash = 2, LeftSlash = 2, RightSlash = 2;
        public int DashLeft = 1, DashRight = 1;
        public int HarpoonLeft = 1, HarpoonRight = 1;
        public int FloatLeft = 1, FloatRight = 1;
        public int WallJumpLeft = 1, WallJumpRight = 1;
        public int Heal = 2;
        public int NeedleThrow = 2, ThreadSphere = 2, HarpoonDash = 2;
        public int SilkCharge = 2, SilkBomb = 2, SilkBossNeedle = 2;
        public int Needolin = 2, Parry = 2, NeedolinMemory = 2, FastTravel = 2, EvaHeal = 2;
        public int Dash = 2, Brolly = 2, DoubleJump = 2, SuperJump = 2, WallJump = 2, ChargeSlash = 2;
        public int HeartPiece = 20, SpoolPart = 18, MaxSilkRegenUp = 2;
        public int UnlockCrestSlot = 2;
    }

    public class InfinitePoolSettings
    {
        public bool Silk = true;
        public bool BlueHealth = true;
        public bool Geo300 = true;
        public bool Shards300 = true;
    }

    // ========== 物品/技能奖励的具体实现 ==========
    public class SavedItemReward : IRandomReward
    {
        private SavedItem _item;
        private static bool _displayNameErrorLogged = false;
        private static bool _iconErrorLogged = false;

        public string Id => _item?.name ?? "null";
        public string DisplayName
        {
            get
            {
                if (_item == null) return Locale.Get("空物品");
                if (_item is ToolCrest) return _item.name;
                try { return _item.GetPopupName(); }
                catch { return _item.name; }
            }
        }
        public Sprite Icon
        {
            get
            {
                if (_item == null) return null;
                try { return _item.GetPopupIcon(); }
                catch { return null; }
            }
        }
        public SavedItem Item => _item;  // 添加公开属性
        public SavedItemReward(SavedItem item) => _item = item;

        /// <summary>
        /// 定向给予 _item 本体（拾取点按表 / 商店货架 / 保底等「已确定给哪个物品」的场景）。
        /// 必须走 TryGetPatch.BypassRandom 保护：否则 _item.TryGet 会被 TryGetPatch 拦截并
        /// 重新随机成一个新物品（表现为「买到的和货架不是同一个」）。
        /// 保存/恢复 BypassRandom 与 PendingKey，对调用方完全透明，不影响外部 PendingKey 分发。
        /// </summary>
        public void Give()
        {
            if (_item == null) return;
            bool prevBypass = TryGetPatch.BypassRandom;
            string prevPending = PreGeneratedMap.PendingKey;
            TryGetPatch.BypassRandom = true;
            try
            {
                _item.TryGet(false, true);
            }
            finally
            {
                TryGetPatch.BypassRandom = prevBypass;
                PreGeneratedMap.PendingKey = prevPending;
            }
        }

        public bool IsAtMax() => ItemRandomizer.GetGivenCount(Id) >= ItemLimitConfig.GetItemTypeLimit(_item);
    }

    public class VirtualReward : IRandomReward
    {
        private readonly string _id, _displayName;
        private readonly Sprite _icon;
        private readonly Action _giveAction;
        private readonly Func<bool> _isAtMax;
        public VirtualReward(string id, string displayName, Sprite icon, Action giveAction, Func<bool> isAtMax)
        { _id = id; _displayName = displayName; _icon = icon; _giveAction = giveAction; _isAtMax = isAtMax; }
        public string Id => _id;
        public string DisplayName => _displayName;
        public Sprite Icon => _icon;
        public void Give() => _giveAction?.Invoke();
        public bool IsAtMax() => _isAtMax?.Invoke() ?? false;
    }

    /// <summary>
    /// 权限类 inspect 地点的权限物：放进随机池，玩家获得后该地点"允许"走原生流程
    /// （把存储的不允许变成允许）。纯记录型奖励，发放记录由触发方统一 AddGivenCount。
    /// </summary>
    public class InspectPermissionReward : IRandomReward
    {
        private readonly string _scene, _name, _displayName;
        public InspectPermissionReward(string scene, string name)
        {
            _scene = scene;
            _name = name;
            _displayName = $"地点权限:{scene}/{name}";
        }
        public string Id => "virt:Permit:" + _scene + ":" + _name;
        public string DisplayName => _displayName;
        public Sprite Icon => SpriteCache.Find("Map_prompt");
        public void Give() { }
        public bool IsAtMax() => ItemRandomizer.GetGivenCount(Id) >= 1;
    }

    public class DirectionPermissionReward : IRandomReward
    {
        private readonly string _id;
        private readonly string _displayName;
        private readonly Sprite _icon;
        private readonly string _skillField;
        private readonly bool _allowRight, _allowLeft;
        private readonly bool _isAttack, _isHeal;
        public DirectionPermissionReward(string displayName, string skillField, bool allowRight, bool allowLeft, bool isAttack = false, bool isHeal = false)
        {
            _id = $"perm:{skillField}_{(allowRight ? "R" : "")}{(allowLeft ? "L" : "")}";
            _displayName = displayName;
            _skillField = skillField;
            _allowRight = allowRight;
            _allowLeft = allowLeft;
            _isAttack = isAttack;
            _isHeal = isHeal;
            _icon = null;
        }
        public string Id => _id;
        public string DisplayName => _displayName;
        public Sprite Icon => GetIcon();
        public void Give()
        {
            if (_isAttack)
            {
                if (_skillField == "upward") StartingAbilityPicker.StartingAbilityPickerAPI.SetAttackPermissions(true, false, false);
                else if (_skillField == "left") StartingAbilityPicker.StartingAbilityPickerAPI.SetAttackPermissions(false, true, false);
                else if (_skillField == "right") StartingAbilityPicker.StartingAbilityPickerAPI.SetAttackPermissions(false, false, true);
            }
            else if (_isHeal)
            {
                StartingAbilityPicker.StartingAbilityPickerAPI.SetMovementPermissions(new DirectionPermissions { AllowHeal = true });
            }
            else
            {
                var perms = StartingAbilityPicker.StartingAbilityPickerAPI.GetMovementPermissions();
                if (_skillField == "hasDash") { perms.DashLeft = _allowLeft; perms.DashRight = _allowRight; }
                else if (_skillField == "hasHarpoonDash") { perms.HarpoonLeft = _allowLeft; perms.HarpoonRight = _allowRight; }
                else if (_skillField == "hasBrolly") { perms.FloatLeft = _allowLeft; perms.FloatRight = _allowRight; }
                else if (_skillField == "hasWalljump") { perms.WallJumpLeft = _allowLeft; perms.WallJumpRight = _allowRight; }
                StartingAbilityPicker.StartingAbilityPickerAPI.SetMovementPermissions(perms);
            }
        }
        public bool IsAtMax() => ItemRandomizer.GetGivenCount(Id) >= ItemLimitConfig.GetDirectionLimit(Id);

        /// <summary>方向权限图标：优先取对应技能图标（冲刺/飞针/漂浮/壁跳），攻击/回血权限用通用图标。</summary>
        private Sprite GetIcon()
        {
            try
            {
                if (_isAttack || _isHeal)
                {
                    string iconName = _isHeal ? "prompt_silkheart" : "cross_slash_attack_circle";
                    return SpriteCache.Find(iconName);
                }
                return StartingAbilityPicker.StartingAbilityPickerAPI.GetIcon(_skillField);
            }
            catch { return null; }
        }
    }

    public class ProxySavedItem : SavedItem
    {
        private IRandomReward _reward;
        public void Init(IRandomReward reward) { _reward = reward; name = _reward.Id; }
        public IRandomReward InnerReward => _reward;
        public override string GetPopupName() => _reward.DisplayName;
        public override Sprite GetPopupIcon() => _reward.Icon;
        public override void Get(bool showPopup = true) => _reward.Give();
        public override bool CanGetMore() => !_reward.IsAtMax();
        public override int GetSavedAmount() => 0;
        public override bool IsUnique => false;
    }

    /// <summary>
    /// 图标覆盖包装：显示用指定图标，其余行为完全委托给被包装奖励（给予/上限判断/名称）。
    /// 用于 lore 触发时按交互物类型（收费机/地图/音乐/蘑菇/门锁）展示对应图标。
    /// </summary>
    public class IconOverrideReward : IRandomReward
    {
        private readonly IRandomReward _inner;
        private readonly Sprite _icon;
        public IconOverrideReward(IRandomReward inner, Sprite icon) { _inner = inner; _icon = icon; }
        public string Id => _inner.Id;
        public string DisplayName => _inner.DisplayName;
        public Sprite Icon => _icon;
        public void Give() => _inner.Give();
        public bool IsAtMax() => _inner.IsAtMax();
    }

    // 将 LimitedVirtualReward 移到公共位置
    public class LimitedVirtualReward : IRandomReward
    {
        private readonly string _id, _displayName;
        private readonly Sprite _icon;
        private readonly Action _giveAction;
        private readonly int _maxCount;
        public LimitedVirtualReward(string id, string displayName, Sprite icon, Action giveAction, int maxCount)
        { _id = id; _displayName = displayName; _icon = icon; _giveAction = giveAction; _maxCount = maxCount; }
        public string Id => _id;
        public string DisplayName => _displayName;
        public Sprite Icon => _icon;
        public void Give() { _giveAction?.Invoke(); ItemRandomizer.AddGivenCount(Id); }
        public bool IsAtMax() => ItemRandomizer.GetGivenCount(Id) >= _maxCount;
    }
}