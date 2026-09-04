// SpriteTextureUtil.cs - 特殊图片处理工具（通用、集中管理）
// 目的：整合所有"图片旋转 / 翻转 / 缩放 / 去边角 / 裁剪"等特殊处理逻辑，
//       供弹窗图标、横幅图标、物品图标等各处复用。后续遇到倒置、边角异常、
//       比例不合适等问题图统一在此扩展，不在各调用点零散实现。
using System;
using UnityEngine;

namespace SilksongItemRandomizer
{
    /// <summary>可旋转展示的奖励（如方向权限）：暴露获奖展示图标的显示端旋转角度（后处理，不动纹理）。</summary>
    public interface IRotatableIcon
    {
        /// <summary>显示端旋转角度：上劈=180°，左劈=90°，右劈=-90°。非旋转类返回 0。</summary>
        float IconRotationAngle { get; }
    }

    /// <summary>
    /// 特殊图片处理工具：统一处理 UI 图标 / 弹窗图标的显示端旋转、翻转、压缩（后处理，不动纹理）。
    /// 后续遇到倒置、边角异常、比例不合适等问题图，统一在此扩展。
    /// </summary>
    public static class SpriteTextureUtil
    {
        /// <summary>
        /// 对 UI 显示对象（SpriteRenderer 或渲染图标所在根）做"先压扁长轴、再旋转"的后处理。
        /// target 传实际渲染体（SpriteRenderer 或含图标的子物体）的 Transform。
        /// compressLongAxis>0 时先沿原始长轴（本工具假定长轴为竖向 Y）压扁，再旋转，保证
        /// 上下/左右各方向都先缩短长边、旋转后方向一致（否则先旋转会漏压横向旋转后的长边）。
        /// </summary>
        public static void ApplyRotationAndCompress(Transform target, float angle, float compressLongAxis = 0f)
        {
            if (target == null) return;
            try
            {
                Vector3 s = target.localScale;
                if (compressLongAxis > 0f) s.y *= compressLongAxis;   // 先压原始竖向长轴
                target.localScale = s;

                float wrapped = ((angle % 360f) + 360f) % 360f;
                target.localRotation = Quaternion.Euler(0f, 0f, wrapped);   // 再旋转
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[SpriteTextureUtil] ApplyRotationAndCompress 失败: {ex.Message}");
            }
        }

        /// <summary>定位到渲染体（SpriteRenderer / Renderer / uGUI Image）的 Transform。找不到返回 null。</summary>
        public static Transform FindRenderTransform(GameObject root, string hint = null)
        {
            if (root == null) return null;
            if (hint != null)
            {
                var f = FindDescendant(root.transform, hint);
                if (f != null) return f;
            }
            var renderer = root.GetComponentInChildren<Renderer>(true);
            if (renderer != null) return renderer.transform;
            return root.transform;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root == null) return null;
            if (string.Equals(root.name, name, StringComparison.OrdinalIgnoreCase)) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindDescendant(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }
    }
}