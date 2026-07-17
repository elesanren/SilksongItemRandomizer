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
        public int UpSlash = 1, LeftSlash = 1, RightSlash = 1;
        public int DashLeft = 1, DashRight = 1;
        public int HarpoonLeft = 1, HarpoonRight = 1;
        public int FloatLeft = 1, FloatRight = 1;
        public int WallJumpLeft = 1, WallJumpRight = 1;
        public int Heal = 1;
        public int NeedleThrow = 1, ThreadSphere = 1, HarpoonDash = 1;
        public int SilkCharge = 1, SilkBomb = 1, SilkBossNeedle = 1;
        public int Needolin = 1, Parry = 1, NeedolinMemory = 1, FastTravel = 1, EvaHeal = 1;
        public int Dash = 1, Brolly = 1, DoubleJump = 1, SuperJump = 1, WallJump = 1, ChargeSlash = 1;
        public int HeartPiece = 2, SpoolPart = 2, MaxSilkRegenUp = 2;
        public int UnlockCrestSlot = 2;
    }

    public class InfinitePoolSettings
    {
        public bool Silk = true;
        public bool FullRestore = true;
        public bool BlueHealth = true;
        public bool Geo300 = true;
        public bool Shards300 = true;
        public bool SilkParts = true;
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
        public void Give() => _item?.TryGet(false, true);
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
        public Sprite Icon => _icon;
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
    }

    public class ProxySavedItem : SavedItem
    {
        private IRandomReward _reward;
        public void Init(IRandomReward reward) { _reward = reward; name = _reward.Id; }
        public override string GetPopupName() => _reward.DisplayName;
        public override Sprite GetPopupIcon() => _reward.Icon;
        public override void Get(bool showPopup = true) => _reward.Give();
        public override bool CanGetMore() => !_reward.IsAtMax();
        public override int GetSavedAmount() => 0;
        public override bool IsUnique => false;
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