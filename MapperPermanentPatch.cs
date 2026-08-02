using HarmonyLib;
using UnityEngine;
using System.Reflection;
using UnityEngine.SceneManagement;

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 强制所有区域的沙克拉（Mapper）商店永久显示，不会因换区、剧情或随机而消失。
    /// </summary>
    [HarmonyPatch]
    public static class MapperPermanentPatch
    {
        // 1. 拦截 GameManager.MapperLeavePreviousLocations
        [HarmonyPatch(typeof(GameManager), "MapperLeavePreviousLocations")]
        [HarmonyPrefix]
        private static bool Prefix_MapperLeavePreviousLocations()
        {
            // 直接返回 false，阻止任何旧区域离开标记被置 true
            return false;
        }

        // 2. 拦截 SceneTravelerTempEval.OnEnter（PlayMaker 版本）
        [HarmonyPatch("HutongGames.PlayMaker.Actions.SceneTravelerTempEval", "OnEnter")]
        [HarmonyPrefix]
        private static bool Prefix_SceneTravelerTempEval()
        {
            // 直接返回 false，阻止 PlayMaker 版本的旧区域离开逻辑
            return false;
        }

        // 3. 拦截 PlayerData.MapperLeaveAll（剧情最终离开）
        [HarmonyPatch(typeof(PlayerData), "MapperLeaveAll")]
        [HarmonyPrefix]
        private static bool Prefix_MapperLeaveAll()
        {
            return false;
        }

        // 4. 拦截场景加载时随机设置 mapperAway 的逻辑
        // 由于内联代码无法直接 Patch，我们用后置补丁在每次场景加载后强制重置
        [HarmonyPatch(typeof(GameManager), "LoadScene")]
        [HarmonyPostfix] // 或使用其他合适的方法如 OnSceneLoaded
        private static void Postfix_ResetMapperState()
        {
            ForceResetMapperFields();
        }

        // 5. 额外兜底：在场景加载时强制重置所有 Mapper 相关字段（双重保险）
        public static void ForceResetMapperFields()
        {
            var pd = PlayerData.instance;
            if (pd == null) return;

            // 所有 14 个离开标记置 false（逐字段保护，防止个别字段缺失影响整体）
            try { pd.MapperLeftBonetown = false; } catch { }
            try { pd.MapperLeftBoneForest = false; } catch { }
            try { pd.MapperLeftDocks = false; } catch { }
            try { pd.MapperLeftWilds = false; } catch { }
            try { pd.MapperLeftCrawl = false; } catch { }
            try { pd.MapperLeftGreymoor = false; } catch { }
            try { pd.MapperLeftBellhart = false; } catch { }
            try { pd.MapperLeftShellwood = false; } catch { }
            try { pd.MapperLeftHuntersNest = false; } catch { }
            try { pd.MapperLeftJudgeSteps = false; } catch { }
            try { pd.MapperLeftDustpens = false; } catch { }
            try { pd.MapperLeftPeak = false; } catch { }
            try { pd.MapperLeftShadow = false; } catch { }
            try { pd.MapperLeftCoralCaverns = false; } catch { }

            // 临时离开标记置 false
            try { pd.mapperAway = false; } catch { }

            // 如果需要进行更多重置，可在此处添加
        }
    }
}
