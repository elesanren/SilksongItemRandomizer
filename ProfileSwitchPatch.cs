using HarmonyLib;
using System;
using System.Reflection;

namespace SilksongItemRandomizer
{
    internal static class ProfileSwitchPatch
    {
        public static void Apply(Harmony harmony)
        {
            // H1: 新档路径
            var startNewGame = AccessTools.DeclaredMethod(typeof(GameManager), "StartNewGame",
                new[] { typeof(bool), typeof(bool) });
            if (startNewGame != null)
                harmony.Patch(startNewGame, postfix: new HarmonyMethod(
                    typeof(ProfileSwitchPatch).GetMethod(nameof(AfterStartNewGame),
                        BindingFlags.Static | BindingFlags.NonPublic)));

            // H2: 读档路径（私有方法，显式注册）
            var setLoaded = AccessTools.DeclaredMethod(typeof(GameManager), "SetLoadedGameData",
                new[] { typeof(global::SaveGameData), typeof(int) });
            if (setLoaded != null)
                harmony.Patch(setLoaded, postfix: new HarmonyMethod(
                    typeof(ProfileSwitchPatch).GetMethod(nameof(AfterSetLoadedGameData),
                        BindingFlags.Static | BindingFlags.NonPublic)));
        }

        internal static void AfterStartNewGame(bool permadeathMode, bool bossRushMode)
        {
            ProfileManager.OnProfileChanged();
        }

        internal static void AfterSetLoadedGameData(global::SaveGameData saveGameData, int saveSlot)
        {
            ProfileManager.OnProfileChanged();
        }
    }
}
