using HarmonyLib;

namespace KitsuneServerPerf
{
    public class ModInit : IModApi
    {
        public const string HarmonyId = "kitsune.serverperf";

        // The Harmony instance of the fix currently being applied (see Fixes.Enable).
        internal static Harmony Harmony;
        internal static string ModPath;

        public void InitMod(Mod mod)
        {
            ModPath = mod.Path;
            Settings.Load(mod.Path);

            Fixes.ApplyStartupFixes();

            // Fixes for other mods wait until every mod has loaded and patched,
            // because they wrap patches those mods install.
            ModEvents.GameStartDone.RegisterHandler(OnGameStartDone);
            ModEvents.GameShutdown.RegisterHandler(OnGameShutdown);
        }

        private static void OnGameStartDone(ref ModEvents.SGameStartDoneData _)
        {
            SCoreFixes.ClearCaches();
            Fixes.ApplyAfterGameStart();
        }

        private static void OnGameShutdown(ref ModEvents.SGameShutdownData _)
        {
            Profiler.Shutdown();
            SCoreFixes.ClearCaches();
        }
    }
}
