using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KitsuneServerPerf
{
    public class PerfCommand : ConsoleCmdAbstract
    {
        // Lines starting with this are for the in-game settings page (KitsuneServerPerf_UI),
        // which sends "kperf settings ui" / "kperf set ... ui" through the normal console
        // command path and reads the reply. Keep the format stable; the page parses it.
        public const string UiPrefix = "KSP|";

        public override string[] getCommands() => new[] { "kperf" };

        public override string getDescription() => "Kitsune Server Perf: time the server's hot code and every mod's patches, and change its settings.";

        public override string getHelp() =>
            "Usage:\n" +
            "  kperf start [seconds]        time for N seconds (default 30), then print the report\n" +
            "  kperf stop                   stop early and print the report\n" +
            "  kperf report                 print the last report again\n" +
            "  kperf census                 what's alive right now and how fast its AI is running\n" +
            "  kperf settings               every fix, whether it's on, and the AI throttle settings\n" +
            "  kperf set <fix> on|off       switch a fix right now (saved to Settings.xml)\n" +
            "  kperf set group:mods on|off  every fix for other mods at once (group:vanilla works too)\n" +
            "  kperf set ai.<option> on|off enemies, animals, npcs, skipWhenAlert, skipDuringBloodMoon\n" +
            "  kperf set ai.tiers 40:0.05,80:0.025   AI throttle tiers as distance:scale (or 'none')\n" +
            "Profile with players online and zombies around: an empty server pauses most of its work.\n" +
            "Timing adds some overhead while it runs; nothing is changed once it stops.";

        public override void Execute(List<string> _params, CommandSenderInfo _senderInfo)
        {
            string sub = _params.Count > 0 ? _params[0].ToLowerInvariant() : "help";
            bool ui = _params.Count > 1 && _params[_params.Count - 1].Equals("ui", StringComparison.OrdinalIgnoreCase);
            switch (sub)
            {
                case "start":
                    float seconds = 30f;
                    if (_params.Count > 1 && (!float.TryParse(_params[1], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) || seconds < 1f || seconds > 600f))
                    {
                        Print("Seconds must be a number from 1 to 600.");
                        return;
                    }
                    Print(Profiler.Start(seconds));
                    break;
                case "stop":
                    Print(Profiler.Stop(printReport: false));
                    break;
                case "report":
                    Print(Profiler.LastReport ?? "No report yet. Run 'kperf start' first.");
                    break;
                case "census":
                    Print(Profiler.Census());
                    Print("Most common: " + Profiler.TopClasses(15));
                    break;
                case "settings":
                    PrintSettings(ui);
                    break;
                case "set":
                    string error = _params.Count >= 3 ? Set(_params[1], _params[2]) : "Usage: kperf set <fix> on|off";
                    if (error != null)
                    {
                        Print(ui ? UiPrefix + "error|" + Clean(error) : error);
                    }
                    PrintSettings(ui);
                    break;
                default:
                    Print(getHelp());
                    break;
            }
        }

        private static string Set(string what, string value)
        {
            if (what.StartsWith("ai.", StringComparison.OrdinalIgnoreCase))
            {
                string option = what.Substring(3).ToLowerInvariant();
                if (option == "tiers")
                {
                    return SetTiers(value);
                }
                if (!TryParseOnOff(value, out bool aiOn))
                {
                    return $"'{value}' isn't on or off.";
                }
                switch (option)
                {
                    case "enemies": Settings.AiThrottleEnemies = aiOn; break;
                    case "animals": Settings.AiThrottleAnimals = aiOn; break;
                    case "npcs": Settings.AiThrottleNpcs = aiOn; break;
                    case "skipwhenalert": Settings.AiSkipWhenAlert = aiOn; break;
                    case "skipduringbloodmoon": Settings.AiSkipDuringBloodMoon = aiOn; break;
                    default: return $"No AI throttle option called '{option}'.";
                }
                Settings.Save();
                return null;
            }

            if (!TryParseOnOff(value, out bool on))
            {
                return $"'{value}' isn't on or off.";
            }

            // group:<name> switches every fix in that group (vanilla, mods), e.g. the
            // settings page's "Asylum Optimized" header toggle.
            if (what.StartsWith("group:", StringComparison.OrdinalIgnoreCase))
            {
                string group = what.Substring(6);
                List<Fix> inGroup = Fixes.All.Where(f => string.Equals(f.Group, group, StringComparison.OrdinalIgnoreCase)).ToList();
                if (inGroup.Count == 0 || group.Equals("gameplay", StringComparison.OrdinalIgnoreCase))
                {
                    return $"No fix group called '{group}'.";
                }
                foreach (Fix f in inGroup)
                {
                    Fixes.Set(f.Name, on);
                }
                return null;
            }
            return Fixes.Set(what, on);
        }

        private static string SetTiers(string value)
        {
            var tiers = new List<AiTier>();
            if (!value.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string part in value.Split(','))
                {
                    string[] ds = part.Split(':');
                    if (ds.Length != 2
                        || !float.TryParse(ds[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float distance)
                        || !float.TryParse(ds[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float scale))
                    {
                        return $"'{part}' isn't distance:scale, like 40:0.05.";
                    }
                    string problem = Settings.CheckTier(distance, scale);
                    if (problem != null)
                    {
                        return $"{part}: {problem}.";
                    }
                    tiers.Add(Settings.Tier(distance, scale));
                }
            }
            Settings.SetTiers(tiers);
            Settings.Save();
            ModLog.Info("AiThrottle: tiers changed in game to " + AiThrottle.TierText());
            return null;
        }

        private static void PrintSettings(bool ui)
        {
            if (ui)
            {
                Print(UiPrefix + "begin");
                foreach (Fix f in Fixes.All)
                {
                    Print($"{UiPrefix}fix|{f.Name}|{f.State}|{(Settings.FixEnabled(f.Name) ? 1 : 0)}|{f.Group}|{Clean(f.Label)}|{Clean(f.Reason)}");
                }
                Print($"{UiPrefix}ai|enemies={B(Settings.AiThrottleEnemies)}|animals={B(Settings.AiThrottleAnimals)}|npcs={B(Settings.AiThrottleNpcs)}|skipWhenAlert={B(Settings.AiSkipWhenAlert)}|skipDuringBloodMoon={B(Settings.AiSkipDuringBloodMoon)}");
                foreach (AiTier t in Settings.AiTiers.OrderBy(t => t.DistanceSq))
                {
                    Print(UiPrefix + "tier|" + t.Distance.ToString("0.##", CultureInfo.InvariantCulture) + "|" + t.Scale.ToString("0.####", CultureInfo.InvariantCulture));
                }
                Print(UiPrefix + "end");
                return;
            }

            foreach (Fix f in Fixes.All)
            {
                string state = f.State == FixState.Skipped || f.State == FixState.Failed ? $"{f.State.ToString().ToLowerInvariant()}: {f.Reason}" : f.State.ToString().ToLowerInvariant();
                Print($"  {f.Name,-30} {state}");
            }
            Print($"  AI throttle options: enemies={Settings.AiThrottleEnemies}, animals={Settings.AiThrottleAnimals}, npcs={Settings.AiThrottleNpcs}, skipWhenAlert={Settings.AiSkipWhenAlert}, skipDuringBloodMoon={Settings.AiSkipDuringBloodMoon}");
            Print("  AI throttle tiers: " + AiThrottle.TierText());
        }

        private static bool TryParseOnOff(string v, out bool on)
        {
            switch (v.ToLowerInvariant())
            {
                case "on": case "true": case "1": on = true; return true;
                case "off": case "false": case "0": on = false; return true;
                default: on = false; return false;
            }
        }

        private static string B(bool b) => b ? "1" : "0";

        private static string Clean(string s) => (s ?? "").Replace('|', '/').Replace('\n', ' ');

        private static void Print(string text)
        {
            foreach (string line in text.Split('\n'))
            {
                SingletonMonoBehaviour<SdtdConsole>.Instance.Output(line);
            }
        }
    }
}
