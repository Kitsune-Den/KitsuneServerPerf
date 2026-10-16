using System;
using System.Collections.Generic;
using HarmonyLib;

namespace KitsuneServerPerf
{
    internal enum FixState { Off, On, Skipped, Failed, Waiting }

    internal class Fix
    {
        public string Name;
        public string Group;            // "vanilla", "mods" or "gameplay"
        public string Label;            // one line for the settings page
        public bool AfterGameStart;     // wraps other mods' patches, so waits for them
        public Func<string> Apply;      // null when applied, else why it was skipped
        public Action OnDisable;

        public FixState State = FixState.Off;
        public string Reason = "";
        public Harmony Harmony;
    }

    // Every fix gets its own Harmony id, so turning one off is UnpatchSelf() and
    // turning it back on is running its Apply again.
    internal static class Fixes
    {
        public static readonly List<Fix> All = new List<Fix>
        {
            new Fix { Name = "NetPriorityDistance", Group = "vanilla", Label = "Zombie network priority distance bug", Apply = NetPriorityFix.Apply },
            new Fix { Name = "SightCheckLayers", Group = "vanilla", Label = "Cheaper line-of-sight checks (dedicated only)", Apply = SightCheckFix.Apply },
            new Fix { Name = "SCoreDebugLabel", Group = "mods", Label = "SCore: skip NPC debug labels on dedicated", AfterGameStart = true, Apply = SCoreFixes.ApplyDebugLabel },
            new Fix { Name = "SCoreFeatureFlagCache", Group = "mods", Label = "SCore: cache feature flags", AfterGameStart = true, Apply = SCoreFixes.ApplyFeatureFlagCache, OnDisable = SCoreFixes.ClearCaches },
            new Fix { Name = "SCoreStrafeLateUpdate", Group = "mods", Label = "SCore: strafe update without log strings", AfterGameStart = true, Apply = SCoreFixes.ApplyStrafeLateUpdate },
            new Fix { Name = "SCoreWeatherCVars", Group = "mods", Label = "SCore: skip unchanged weather CVars", AfterGameStart = true, Apply = SCoreFixes.ApplyWeatherCVars },
            new Fix { Name = "HusbandryGetValueFilter", Group = "mods", Label = "CrystalHellHusbandry: only filter the stats it changes", AfterGameStart = true, Apply = AsylumFixes.ApplyHusbandryGetValue },
            new Fix { Name = "HealFixCVarFilter", Group = "mods", Label = "AsylumHealFix: only check its own CVar", AfterGameStart = true, Apply = AsylumFixes.ApplyHealFix },
            new Fix { Name = "RandomBuffsUpdateFilter", Group = "mods", Label = "Zombie Random Buffs: skip entities it ignores", AfterGameStart = true, Apply = AsylumFixes.ApplyRandomBuffs },
            new Fix { Name = "ShockShaderElectrocuteFilter", Group = "mods", Label = "ShockShaderPatch: skip the per-frame material walk", AfterGameStart = true, Apply = AsylumFixes.ApplyShockShader },
            new Fix { Name = AiThrottle.FixName, Group = "gameplay", Label = "AI distance throttling (far idle zombies think less often)", Apply = AiThrottle.Apply },
        };

        private static bool gameStarted;

        public static Fix Find(string name)
        {
            return All.Find(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
        }

        public static void ApplyStartupFixes()
        {
            foreach (Fix f in All)
            {
                if (!f.AfterGameStart)
                {
                    Enable(f, log: true);
                }
                else
                {
                    f.State = FixState.Waiting;
                }
            }
        }

        public static void ApplyAfterGameStart()
        {
            if (gameStarted)
            {
                return;
            }
            gameStarted = true;
            foreach (Fix f in All)
            {
                if (f.AfterGameStart)
                {
                    Enable(f, log: true);
                }
            }
        }

        // Turns a fix on or off right now and saves the choice to Settings.xml.
        public static string Set(string name, bool on)
        {
            Fix f = Find(name);
            if (f == null)
            {
                return $"No fix called '{name}'.";
            }
            Settings.SetFixEnabled(f.Name, on);
            Settings.Save();

            if (on)
            {
                if (f.State == FixState.On)
                {
                    return null;
                }
                if (f.AfterGameStart && !gameStarted)
                {
                    f.State = FixState.Waiting;
                    return null;
                }
                Enable(f, log: true);
            }
            else
            {
                Disable(f);
                f.State = FixState.Off;
                f.Reason = "";
                ModLog.Info($"{f.Name}: off (changed in game)");
            }
            return null;
        }

        private static void Enable(Fix f, bool log)
        {
            if (!Settings.FixEnabled(f.Name))
            {
                f.State = FixState.Off;
                f.Reason = "";
                if (log) ModLog.Info($"{f.Name}: off (Settings.xml)");
                return;
            }

            Disable(f);
            f.Harmony = new Harmony(ModInit.HarmonyId + "." + f.Name);
            ModInit.Harmony = f.Harmony;
            try
            {
                string skipped = f.Apply();
                if (skipped == null)
                {
                    f.State = FixState.On;
                    f.Reason = "";
                    if (log) ModLog.Info($"{f.Name}: on");
                }
                else
                {
                    f.Harmony.UnpatchSelf();
                    f.State = FixState.Skipped;
                    f.Reason = skipped;
                    if (log) ModLog.Info($"{f.Name}: skipped, {skipped}");
                }
            }
            catch (Exception e)
            {
                try { f.Harmony.UnpatchSelf(); } catch { }
                f.State = FixState.Failed;
                f.Reason = e.Message;
                ModLog.Error($"{f.Name}: failed to apply, left off. {e}");
            }
            finally
            {
                ModInit.Harmony = null;
            }
        }

        private static void Disable(Fix f)
        {
            if (f.Harmony == null)
            {
                return;
            }
            try
            {
                f.Harmony.UnpatchSelf();
            }
            catch (Exception e)
            {
                ModLog.Error($"{f.Name}: failed to remove its patches: {e.Message}");
            }
            f.Harmony = null;
            f.OnDisable?.Invoke();
        }
    }
}
