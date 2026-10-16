using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;

namespace KitsuneServerPerf
{
    public class AiTier
    {
        public float DistanceSq;
        public float Scale;

        public float Distance => (float)Math.Sqrt(DistanceSq);
    }

    public static class Settings
    {
        private static readonly Dictionary<string, bool> fixes = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private static XmlDocument doc;
        private static string file;

        public static bool AiThrottleEnabled;
        public static bool AiThrottleEnemies = true;
        public static bool AiThrottleAnimals;
        public static bool AiThrottleNpcs;
        public static bool AiSkipWhenAlert = true;
        public static bool AiSkipDuringBloodMoon = true;

        // Sorted farthest first, so the first tier an entity is beyond wins. Replaced as a
        // whole (never edited in place) because the throttle reads it every tick.
        public static List<AiTier> AiTiers = new List<AiTier>();

        public static bool FixEnabled(string name)
        {
            if (string.Equals(name, AiThrottle.FixName, StringComparison.OrdinalIgnoreCase))
            {
                return AiThrottleEnabled;
            }
            return !fixes.TryGetValue(name, out bool on) || on;
        }

        public static void SetFixEnabled(string name, bool on)
        {
            if (string.Equals(name, AiThrottle.FixName, StringComparison.OrdinalIgnoreCase))
            {
                AiThrottleEnabled = on;
            }
            else
            {
                fixes[name] = on;
            }
        }

        public static void Load(string modPath)
        {
            file = Path.Combine(modPath, "Settings.xml");
            if (!File.Exists(file))
            {
                ModLog.Warn("Settings.xml not found, using defaults (all fixes on, AI throttle off)");
                SetTiers(new List<AiTier> { Tier(40f, 0.05f), Tier(80f, 0.025f) });
                return;
            }

            try
            {
                doc = new XmlDocument { PreserveWhitespace = true };
                doc.Load(file);
                XmlElement root = doc.DocumentElement;

                foreach (XmlElement fix in root.SelectNodes("Fix"))
                {
                    fixes[fix.GetAttribute("name")] = ReadBool(fix, "enabled", true);
                }

                var tiers = new List<AiTier>();
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
                        string problem = CheckTier(distance, scale);
                        if (problem != null)
                        {
                            ModLog.Warn($"Ignoring AiThrottle tier distance=\"{tier.GetAttribute("distance")}\" scale=\"{tier.GetAttribute("scale")}\": {problem}");
                            continue;
                        }
                        tiers.Add(Tier(distance, scale));
                    }
                }
                SetTiers(tiers);
            }
            catch (Exception e)
            {
                doc = null;
                ModLog.Error("Could not read Settings.xml, using defaults: " + e.Message);
            }
        }

        public static string CheckTier(float distance, float scale)
        {
            if (distance <= 0f || distance > 1000f)
            {
                return "distance must be between 0 and 1000 m";
            }
            if (scale <= 0f || scale > 1f)
            {
                return "scale must be above 0 and at most 1";
            }
            return null;
        }

        public static AiTier Tier(float distance, float scale) => new AiTier { DistanceSq = distance * distance, Scale = scale };

        public static void SetTiers(List<AiTier> tiers)
        {
            AiTiers = tiers.OrderByDescending(t => t.DistanceSq).ToList();
        }

        // Writes the current values back into Settings.xml, keeping its comments and layout.
        public static void Save()
        {
            if (file == null)
            {
                return;
            }
            try
            {
                if (doc == null)
                {
                    doc = new XmlDocument { PreserveWhitespace = true };
                    doc.LoadXml("<KitsuneServerPerf>\n</KitsuneServerPerf>");
                }
                XmlElement root = doc.DocumentElement;

                foreach (KeyValuePair<string, bool> kv in fixes)
                {
                    XmlElement fix = root.SelectNodes("Fix").Cast<XmlElement>().FirstOrDefault(e => string.Equals(e.GetAttribute("name"), kv.Key, StringComparison.OrdinalIgnoreCase));
                    if (fix == null)
                    {
                        fix = doc.CreateElement("Fix");
                        fix.SetAttribute("name", kv.Key);
                        root.InsertBefore(fix, root.SelectSingleNode("AiThrottle"));
                        root.InsertBefore(doc.CreateWhitespace("\n\t"), fix);
                    }
                    fix.SetAttribute("enabled", kv.Value ? "true" : "false");
                }

                if (!(root.SelectSingleNode("AiThrottle") is XmlElement ai))
                {
                    ai = doc.CreateElement("AiThrottle");
                    root.AppendChild(doc.CreateWhitespace("\n\t"));
                    root.AppendChild(ai);
                    root.AppendChild(doc.CreateWhitespace("\n"));
                }
                ai.SetAttribute("enabled", Bool(AiThrottleEnabled));
                ai.SetAttribute("enemies", Bool(AiThrottleEnemies));
                ai.SetAttribute("animals", Bool(AiThrottleAnimals));
                ai.SetAttribute("npcs", Bool(AiThrottleNpcs));
                ai.SetAttribute("skipWhenAlert", Bool(AiSkipWhenAlert));
                ai.SetAttribute("skipDuringBloodMoon", Bool(AiSkipDuringBloodMoon));

                foreach (XmlNode old in ai.ChildNodes.Cast<XmlNode>().ToList())
                {
                    ai.RemoveChild(old);
                }
                foreach (AiTier t in AiTiers.OrderBy(t => t.DistanceSq))
                {
                    XmlElement tier = doc.CreateElement("Tier");
                    tier.SetAttribute("distance", t.Distance.ToString("0.##", CultureInfo.InvariantCulture));
                    tier.SetAttribute("scale", t.Scale.ToString("0.####", CultureInfo.InvariantCulture));
                    ai.AppendChild(doc.CreateWhitespace("\n\t\t"));
                    ai.AppendChild(tier);
                }
                ai.AppendChild(doc.CreateWhitespace("\n\t"));

                // No byte-order mark, so the file stays as plain as the one we ship.
                using (var writer = new StreamWriter(file, false, new System.Text.UTF8Encoding(false)))
                {
                    doc.Save(writer);
                }
            }
            catch (Exception e)
            {
                ModLog.Error("Could not save Settings.xml: " + e.Message);
            }
        }

        private static string Bool(bool b) => b ? "true" : "false";

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
