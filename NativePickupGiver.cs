using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 面具碎片/丝轴碎片的原生发放：走 SavedItem.Get()/PrefabCollectable.Get() 完整原生流程
    /// （获取弹窗 + 实例化真实 prefab + PlayMakerFSM 驱动 HUD 碎片/合成动画），
    /// 替代原先手工 ++heartPieces/silkSpoolParts 的复刻方案。
    /// </summary>
    public static class NativePickupGiver
    {
        // 导出数据得到的 Silk Spool prefab 所在数据集（无全局 prefab，必须先加载该数据集）
        private const string SpoolDatasetKey = "AssetHelper-RepackedScenes/Assets/bone_11b/Silk Spool";

        private static SavedItem _heartItem;
        private static SavedItem _spoolItem;

        /// <summary>面具碎片：原生 SavedItem.Get()。</summary>
        public static void GiveHeartPiece()
        {
            var item = GetHeartPiece();
            if (item != null)
            {
                try
                {
                    SilkSpoolState.MarkSelfGiving(30f);
                    item.Get();
                    return;
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogWarning("[NativeGive] HeartPiece Get 异常，回退 ++: " + ex.Message);
                }
            }
            var pd = PlayerData.instance;
            if (pd != null)
            {
                pd.heartPieces++;
                EventRegister.SendEvent(EventRegisterEvents.EquipsChangedEvent, null);
            }
        }

        /// <summary>丝轴碎片：异步加载数据集后原生 PrefabCollectable.Get()。</summary>
        public static IEnumerator GiveSpoolPart()
        {
            if (_spoolItem != null)
            {
                DoSpoolGet(_spoolItem);
                yield break;
            }

            _spoolItem = Resources.FindObjectsOfTypeAll<SavedItem>()
                .FirstOrDefault(s => s != null &&
                    string.Equals(s.name, "Silk Spool", StringComparison.OrdinalIgnoreCase));
            if (_spoolItem != null)
            {
                DoSpoolGet(_spoolItem);
                yield break;
            }

            AsyncOperationHandle<GameObject> handle = default;
            try
            {
                handle = Addressables.LoadAssetAsync<GameObject>(SpoolDatasetKey);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[NativeGive] 数据集加载调用失败: " + ex.Message);
                FallbackSpool();
                yield break;
            }

            float deadline = Time.realtimeSinceStartup + 20f;
            while (!handle.IsDone && Time.realtimeSinceStartup <= deadline)
                yield return null;

            if (!handle.IsDone || handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
            {
                Plugin.Log.LogWarning("[NativeGive] 数据集加载失败/超时，回退 ++");
                try { if (handle.IsValid()) Addressables.Release(handle); } catch { }
                FallbackSpool();
                yield break;
            }

            var prefab = handle.Result;
            _spoolItem = Resources.FindObjectsOfTypeAll<SavedItem>()
                .FirstOrDefault(s => s != null &&
                    string.Equals(s.name, "Silk Spool", StringComparison.OrdinalIgnoreCase));
            if (_spoolItem == null)
                _spoolItem = prefab.GetComponent<PrefabCollectable>();
            try { if (handle.IsValid()) Addressables.Release(handle); } catch { }

            if (_spoolItem != null)
                DoSpoolGet(_spoolItem);
            else
                FallbackSpool();
        }

        private static void DoSpoolGet(SavedItem item)
        {
            // 自发发放放行窗口：Get 内部与 Get 之后的延迟回调都放行，两个拦截器均识别
            SilkSpoolState.MarkSelfGiving(30f);
            SpoolPartPatch.Bypass = true;
            SilkSpoolState.Bypass = true;
            try
            {
                Plugin.Log.LogInfo("[NativeGive] Silk Spool 原生 PrefabCollectable.Get()");
                item.Get();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("[NativeGive] Spool Get 异常，回退 ++: " + ex.Message);
                FallbackSpool();
            }
            finally
            {
                SpoolPartPatch.Bypass = false;
                SilkSpoolState.Bypass = false;
            }
        }

        private static void FallbackSpool()
        {
            var pd = PlayerData.instance;
            if (pd != null)
            {
                pd.silkSpoolParts++;
                GameCameras.instance?.silkSpool?.RefreshSilk();
                EventRegister.SendEvent(EventRegisterEvents.EquipsChangedEvent, null);
            }
        }

        private static SavedItem GetHeartPiece()
        {
            if (_heartItem != null) return _heartItem;
            _heartItem = Resources.FindObjectsOfTypeAll<SavedItem>()
                .FirstOrDefault(s => s != null &&
                    string.Equals(s.name, "Heart Piece", StringComparison.OrdinalIgnoreCase));
            return _heartItem;
        }
    }
}
