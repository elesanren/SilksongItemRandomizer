using System;
using System.Reflection;
using UnityEngine;

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 沙克拉商人（Mapper）商店单例辅助。
    /// 商人常驻由 MapperPermanentPatch 负责（Postfix 拉回 mapperAway/MapperLeft* 并拦截场景隐藏组件），
    /// 本类只保留商店单例重建能力（供 HotkeyHandler 调试使用）。
    /// </summary>
    public static class ShakraMerchantKeeper
    {
        private static bool _reflectionReady;
        private static FieldInfo _spawnedShopField;

        private static void InitReflection()
        {
            if (_reflectionReady) return;
            _reflectionReady = true;
            try
            {
                var t = typeof(ShopOwnerBase);
                _spawnedShopField = t.GetField("_spawnedShop", BindingFlags.Static | BindingFlags.NonPublic);
            }
            catch { }
        }

        /// <summary>重置 ShopOwnerBase._spawnedShop 静态单例：销毁残留商店 UI 并置 null，强制当前商人生成自己的商店。</summary>
        public static void ResetSpawnedShopSingleton()
        {
            InitReflection();
            if (_spawnedShopField == null) return;
            try
            {
                var existing = _spawnedShopField.GetValue(null) as UnityEngine.Object;
                if (existing != null)
                    UnityEngine.Object.DestroyImmediate(existing);
                _spawnedShopField.SetValue(null, null);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Shakra] 重置 _spawnedShop 失败: {ex.Message}");
            }
        }
    }
}
