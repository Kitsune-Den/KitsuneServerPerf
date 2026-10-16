using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;

namespace KitsuneServerPerf
{
    public class AiTier
    {
        public float DistanceSq;
        public float Scale;
    }

    public static class Settings
    {
        private static readonly Dictionary<string, bool> fixes = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        public static bool AiThrottleEnabled;
        public static bool AiThrottleEnemies = true;
        public static bool AiThrottleAnimals;
        public static bool AiThrottleNpcs;
        public static bool AiSkipWhenAlert = true;
        public static bool AiSkipDuringBloodMoon = true;

        // Sorted farthest first, so the first tier an entity is beyond wins.
        public static readonly List<AiTier> AiTiers = new List<AiTier>();

        public static bool FixEnabled(string name)
        {
            return !fixes.TryGetValue(name, out bool on) || on;
        }

        public static void Load(string modPath)
        {
            string file = Path.Combine(modPath, "Settings.xml");
            if (!File.Exists(file))
            {
                ModLog.Warn("Settings.xml not found, using defaults (all fixes on, AI throttle off)");
                return;
            }

            try
            {
                var doc = new XmlDocument();
                doc.Load(file);
                XmlElement root = doc.DocumentElement;

                foreach (XmlElement fix in root.SelectNodes("Fix"))
                {
                    fixes[fix.GetAttribute("name")] = ReadBool(fix, "enabled", true);
                }

                if (root.SelectSingleNode("AiThrottle") is XmlElement ai)
                {
                    AiThrottleEnabled = ReadBool(ai, "enabled", false);
                    AiThrottleEnemies = ReadBool(ai, "enemies", true);
                    AiThrottleAnimals = ReadBool(ai, "animals", false);
                    AiThrottleNpcs = ReadBool(ai, "npcs", false);
                    AiSkipWhenAlert = ReadBool(ai, "skipWhenAlert", true);
                    AiSkipDuringBloodMoon = ReadBool(ai, "skipDuringBloodMoon", true);

                    foreach (XmlElement tier in ai.SelectNodes("Tier"))
                    {
                        float distance = ReadFloat(tier, "distance", -1f);
                        float scale = ReadFloat(tier, "scale", -1f);
                        if (distance <= 0f || scale <= 0f || scale > 1f)
                        {
                            ModLog.Warn($"Ignoring AiThrottle tier distance=\"{tier.GetAttribute("distance")}\" scale=\"{tier.GetAttribute("scale")}\": distance must be > 0 and scale between 0 and 1");
                            continue;
                        }
                        AiTiers.Add(new AiTier { DistanceSq = distance * distance, Scale = scale });
                    }
                    AiTiers.Sort((a, b) => b.DistanceSq.CompareTo(a.DistanceSq));
                }
            }
            catch (Exception e)
            {
                ModLog.Error("Could not read Settings.xml, using defaults: " + e.Message);
            }
        }

        private static bool ReadBool(XmlElement e, string attr, bool fallback)
        {
            string v = e.GetAttribute(attr);
            return bool.TryParse(v, out bool b) ? b : fallback;
        }

        private static float ReadFloat(XmlElement e, string attr, float fallback)
        {
            string v = e.GetAttribute(attr);
            return float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : fallback;
        }
    }

    internal static class ModLog
    {
        private const string Prefix = "[KitsuneServerPerf] ";
        public static void Info(string msg) => Log.Out(Prefix + msg);
        public static void Warn(string msg) => Log.Warning(Prefix + msg);
        public static void Error(string msg) => Log.Error(Prefix + msg);
    }
}
