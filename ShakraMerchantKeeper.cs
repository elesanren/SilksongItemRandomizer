using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 沙克拉商人（Mapper/ShopOwner）保活助手。
    /// 背景：ShopOwnerBase._spawnedShop 是全局静态单例，一次只能存在一个商店 UI。
    /// 因此切换场景后，若旧商店单例残留，当前场景商人会误以为已有商店而不重新生成，
    /// 表现为"有的地区有商人可买、有的地区商人不在/买不了"。
    /// 解法：进入含沙克拉商人的场景后，主动重置该静态单例（卸载旧商店 + 置 null），
    /// 强制当前场景商人重新生成自己的商店，保证商人始终可购买。
    /// </summary>
    public static class ShakraMerchantKeeper
    {
        private static bool _reflectionReady;
        private static FieldInfo _spawnedShopField;

        /// <summary>确保反射字段就绪</summary>
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

        /// <summary>
        /// 重置 ShopOwnerBase._spawnedShop 静态单例：销毁残留商店 UI 并置 null。
        /// 强制当前场景的商人（后续交互/OnEnable）认为"没有商店"而重新生成自己的商店。
        /// </summary>
        public static void ResetSpawnedShopSingleton()
        {
            InitReflection();
            if (_spawnedShopField == null) return;
            try
            {
                var existing = _spawnedShopField.GetValue(null) as UnityEngine.Object;
                if (existing != null)
                    UnityEngine.Object.DestroyImmediate(existing);   // 销毁残留商店 UI
                _spawnedShopField.SetValue(null, null);  // 置 null，强制重生
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Shakra] 重置 _spawnedShop 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 判断当前场景是否含沙克拉商人（ShopOwnerBase 子类，含残留/未激活）。
        /// </summary>
        public static bool HasMerchantInScene(Scene scene)
        {
            if (scene == null || string.IsNullOrEmpty(scene.name)) return false;
            var name = scene.name;
            // 菜单/加载等非游戏场景跳过
            if (name == "Menu_Title" || name == "Menu" || name == "Loading" || name == "Cinematic") return false;

            try
            {
                // FindObjectsOfType(bool) 找所有（含未激活、含隐藏的）商人组件
                var owners = UnityEngine.Object.FindObjectsOfType<ShopOwnerBase>(false);
                foreach (var o in owners)
                {
                    if (o == null) continue;
                    if (o.gameObject.scene == scene) return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>场景加载完成后延迟调用：重置商店单例，保证商人可购买。</summary>
        public static IEnumerator RefreshAfterSceneLoad(Scene scene)
        {
            // 等商人 prefab OnEnable（生成/或跳过）完成之后，再重置，确保是"卸载旧 + 重新生成当前"
            yield return new WaitForSeconds(0.5f);

            if (!HasMerchantInScene(scene)) yield break;
            ResetSpawnedShopSingleton();
            Plugin.Log.LogInfo($"[Shakra] 场景 {scene.name} 检测到商人，已重置商店单例");
        }
    }
}
