using System;
using HarmonyLib;

namespace KitsuneServerPerf
{
    public class ModInit : IModApi
    {
        public const string HarmonyId = "kitsune.serverperf";

        internal static Harmony Harmony;
        internal static string ModPath;

        private static bool compatApplied;

        public void InitMod(Mod mod)
        {
            ModPath = mod.Path;
            Settings.Load(mod.Path);
            Harmony = new Harmony(HarmonyId);

            Apply("NetPriorityDistance", NetPriorityFix.Apply);
            Apply("SightCheckLayers", SightCheckFix.Apply);

            if (Settings.AiThrottleEnabled)
            {
                AiThrottle.Apply();
            }
            else
            {
                ModLog.Info("AI throttle is off (Settings.xml)");
            }

            // Fixes for other mods wait until every mod has loaded and patched,
            // because they wrap patches those mods install.
            ModEvents.GameStartDone.RegisterHandler(OnGameStartDone);
            ModEvents.GameShutdown.RegisterHandler(OnGameShutdown);
        }

        private static void OnGameStartDone(ref ModEvents.SGameStartDoneData _)
        {
            SCoreFixes.ClearCaches();
            if (compatApplied)
            {
                return;
            }
            compatApplied = true;

            Apply("SCoreDebugLabel", SCoreFixes.ApplyDebugLabel);
            Apply("SCoreFeatureFlagCache", SCoreFixes.ApplyFeatureFlagCache);
            Apply("HusbandryGetValueFilter", AsylumFixes.ApplyHusbandryGetValue);
            Apply("HealFixCVarFilter", AsylumFixes.ApplyHealFix);
            Apply("RandomBuffsUpdateFilter", AsylumFixes.ApplyRandomBuffs);
            Apply("ShockShaderElectrocuteFilter", AsylumFixes.ApplyShockShader);
        }

        private static void OnGameShutdown(ref ModEvents.SGameShutdownData _)
        {
            Profiler.Shutdown();
            SCoreFixes.ClearCaches();
        }

        // Each fix returns null when applied, or a reason it was skipped.
        internal static void Apply(string name, Func<string> apply)
        {
            if (!Settings.FixEnabled(name))
            {
                ModLog.Info($"{name}: off (Settings.xml)");
                return;
            }
            try
            {
                string skipped = apply();
                ModLog.Info(skipped == null ? $"{name}: on" : $"{name}: skipped, {skipped}");
            }
            catch (Exception e)
            {
                ModLog.Error($"{name}: failed to apply, left off. {e}");
            }
        }
    }
}
