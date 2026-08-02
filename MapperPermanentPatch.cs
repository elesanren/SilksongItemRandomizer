using HarmonyLib;
using UnityEngine;

namespace SilksongItemRandomizer
{
    /// <summary>
    /// 强制所有区域的沙克拉（Mapper）商店永久显示，不会因换区、剧情或随机而消失。
    /// 采用"独立补丁类"拆分注册：每个 [HarmonyPatch] 类单独 PatchAll，
    /// 单个目标方法解析失败不影响其余补丁（避免 PatchClassProcessor 一票否决）。
    /// </summary>
    public static class MapperPermanentPatch
    {
        // ========== 供外部（Plugin 等）调用的强制重置 ==========
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

            // 阻止"沙克拉最终任务出现"标志被置真：
            // GameManager 会因它调 MapperLeaveAll() 让所有商人离场。
            try { pd.ShakraFinalQuestAppear = false; } catch { }
        }
    }

    /// <summary>拦截 PlayerData.MapperLeaveAll：阻止一次性清空所有商人</summary>
    [HarmonyPatch(typeof(PlayerData), "MapperLeaveAll")]
    public static class MapperLeaveAllPatch
    {
        [HarmonyPrefix]
        private static bool Prefix() => false;
    }

    /// <summary>拦截 GameManager.MapperLeavePreviousLocations：阻止经过地区的商人离场</summary>
    [HarmonyPatch(typeof(GameManager), "MapperLeavePreviousLocations")]
    public static class MapperLeavePrevPatch
    {
        [HarmonyPrefix]
        private static bool Prefix() => false;
    }

    /// <summary>
    /// 拦截 PlayMaker 版离开逻辑 SceneTravelerTempEval.OnEnter（HutongGames.PlayMaker.Actions）。
    /// 用字符串类型名避免编译期依赖 PlayMaker。独立类，若目标缺失不影响其他补丁。
    /// </summary>
    [HarmonyPatch("HutongGames.PlayMaker.Actions.SceneTravelerTempEval", "OnEnter")]
    public static class SceneTravelerEvalPatch
    {
        [HarmonyPrefix]
        private static bool Prefix() => false;
    }

    /// <summary>
    /// 场景加载/进入后强制重置所有 Mapper 字段，兜底覆盖剧情/PlayMaker 在场景中后期再次置真。
    /// LoadScene 与 FinishedEnteringScene 各自独立，某个失败不影响另一个。
    /// </summary>
    [HarmonyPatch(typeof(GameManager), "LoadScene")]
    public static class MapperResetOnLoadPatch
    {
        [HarmonyPostfix]
        private static void Postfix() => MapperPermanentPatch.ForceResetMapperFields();
    }

    [HarmonyPatch(typeof(GameManager), "FinishedEnteringScene")]
    public static class MapperResetOnEnterPatch
    {
        [HarmonyPostfix]
        private static void Postfix() => MapperPermanentPatch.ForceResetMapperFields();
    }
}
