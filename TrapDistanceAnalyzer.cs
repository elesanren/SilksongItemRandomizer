// TrapDistanceAnalyzer.cs
// 测试用：场景加载后自动输出一次所有活跃陷阱间的最近距离与相对水平面的角度
// 用完从项目中排除编译即可

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SilksongItemRandomizer
{
    public static class TrapDistanceAnalyzer
    {
        private static bool _hasRun = false;

        public static void TryRun()
        {
            if (_hasRun) return;
            _hasRun = true;
            AnalyzeAndLog();
        }

        private static void AnalyzeAndLog()
        {
            // 反射获取 ActiveTraps
            var field = typeof(TrapRandomizer).GetField(
                "ActiveTraps", BindingFlags.Static | BindingFlags.NonPublic);
            if (field == null) return;

            var traps = field.GetValue(null) as List<GameObject>;
            if (traps == null || traps.Count < 2)
            {
                Debug.Log("[TrapAnalyzer] 陷阱不足，跳过分析");
                return;
            }

            Debug.Log($"===== 陷阱距离分析 (共 {traps.Count} 个) =====");

            for (int i = 0; i < traps.Count; i++)
            {
                if (traps[i] == null) continue;
                Vector3 posA = traps[i].transform.position;
                float minDist = float.MaxValue;
                int nearestIdx = -1;

                for (int j = 0; j < traps.Count; j++)
                {
                    if (i == j || traps[j] == null) continue;
                    float dist = Vector3.Distance(posA, traps[j].transform.position);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        nearestIdx = j;
                    }
                }

                if (nearestIdx >= 0)
                {
                    Vector3 dir = traps[nearestIdx].transform.position - posA;
                    float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
                    Debug.Log($"[{i}] {traps[i].name} -> [{nearestIdx}] {traps[nearestIdx].name} | 距离:{minDist:F1} | 角度:{angle:F1}°");
                }
            }

            Debug.Log("===== 分析结束 =====");
        }
    }
}