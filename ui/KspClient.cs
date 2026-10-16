using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

public class KitsuneServerPerfUIMod : IModApi
{
    public void InitMod(Mod _modInstance)
    {
        new Harmony("kitsune.serverperf.ui").PatchAll(typeof(KitsuneServerPerfUIMod).Assembly);
        Log.Out("[KitsuneServerPerf_UI] Loaded");
    }
}

/// <summary>
/// Talks to KitsuneServerPerf on the server through the normal console command path:
/// "kperf settings ui" / "kperf set ... ui" go up like any F1 command (so the server's own
/// admin permissions decide who may run them), and the reply comes back as console lines
/// starting with "KSP|", which are caught here instead of being printed.
/// No custom network packages, so nothing changes for players without this add-on.
/// </summary>
public static class KspClient
{
    public const string Prefix = "KSP|";
    const float ReplyTimeout = 6f;

    public class FixInfo
    {
        public string Name;
        public string State;    // On, Off, Skipped, Failed, Waiting
        public bool Enabled;
        public string Group;    // vanilla, mods, gameplay
        public string Label;
        public string Reason;
    }

    public static readonly List<FixInfo> Fixes = new List<FixInfo>();
    public static readonly Dictionary<string, bool> AiOptions = new Dictionary<string, bool>();
    public static readonly List<(float distance, float scale)> Tiers = new List<(float, float)>();

    public static bool HasData;
    public static string Status = "";
    public static bool StatusIsError;
    public static int Version;

    static bool awaiting;
    static float sentAt;

    public static void Refresh() => Send("kperf settings ui");
    public static void SetFix(string name, bool on) => Send($"kperf set {name} {(on ? "on" : "off")} ui");
    public static void SetAiOption(string option, bool on) => Send($"kperf set ai.{option} {(on ? "on" : "off")} ui");
    public static void SetTiers(string spec) => Send($"kperf set ai.tiers {spec} ui");

    static void Send(string command)
    {
        ConnectionManager connection = SingletonMonoBehaviour<ConnectionManager>.Instance;
        if (connection == null)
        {
            return;
        }
        SetStatus(Localization.Get("kspStatusAsking"), false);

        // Single player or hosting: the server is right here.
        if (connection.IsServer)
        {
            List<string> lines = SingletonMonoBehaviour<SdtdConsole>.Instance.ExecuteSync(command, null);
            if (!OnReply(lines))
            {
                OnOtherReply(lines, local: true);
            }
            return;
        }

        awaiting = true;
        sentAt = Time.realtimeSinceStartup;
        connection.SendToServer(NetPackageManager.GetPackage<NetPackageConsoleCmdServer>().Setup(command));
    }

    public static void Tick()
    {
        if (awaiting && Time.realtimeSinceStartup - sentAt > ReplyTimeout)
        {
            awaiting = false;
            SetStatus(Localization.Get("kspStatusNoAnswer"), true);
        }
    }

    public static bool IsOurs(List<string> lines)
    {
        if (lines == null)
        {
            return false;
        }
        foreach (string line in lines)
        {
            if (line != null && line.StartsWith(Prefix))
            {
                return true;
            }
        }
        return false;
    }

    // Returns false if the lines aren't a KSP reply.
    public static bool OnReply(List<string> lines)
    {
        if (!IsOurs(lines))
        {
            return false;
        }
        awaiting = false;

        var fixes = new List<FixInfo>();
        var options = new Dictionary<string, bool>();
        var tiers = new List<(float, float)>();
        string error = null;
        bool complete = false;

        foreach (string line in lines)
        {
            if (line == null || !line.StartsWith(Prefix))
            {
                continue;
            }
            string[] p = line.Substring(Prefix.Length).Split('|');
            switch (p[0])
            {
                case "fix" when p.Length >= 7:
                    fixes.Add(new FixInfo { Name = p[1], State = p[2], Enabled = p[3] == "1", Group = p[4], Label = p[5], Reason = p[6] });
                    break;
                case "ai":
                    for (int i = 1; i < p.Length; i++)
                    {
                        string[] kv = p[i].Split('=');
                        if (kv.Length == 2)
                        {
                            options[kv[0]] = kv[1] == "1";
                        }
                    }
                    break;
                case "tier" when p.Length >= 3:
                    if (float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float d)
                        && float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float s))
                    {
                        tiers.Add((d, s));
                    }
                    break;
                case "error" when p.Length >= 2:
                    error = p[1];
                    break;
                case "end":
                    complete = true;
                    break;
            }
        }

        if (complete)
        {
            Fixes.Clear();
            Fixes.AddRange(fixes);
            AiOptions.Clear();
            foreach (KeyValuePair<string, bool> kv in options)
            {
                AiOptions[kv.Key] = kv.Value;
            }
            Tiers.Clear();
            Tiers.AddRange(tiers);
            HasData = true;
        }

        if (error != null)
        {
            SetStatus(error, true);
        }
        else
        {
            SetStatus(Localization.Get("kspStatusReady"), false);
        }
        return true;
    }

    // A console reply without KSP lines while we're waiting: the server doesn't know the
    // command (no KitsuneServerPerf there) or turned it down (not an admin).
    public static void OnOtherReply(List<string> lines, bool local = false)
    {
        if (!awaiting && !local)
        {
            return;
        }
        awaiting = false;
        string first = lines != null && lines.Count > 0 ? lines[0] : "";
        if (first.IndexOf("Unknown command", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            SetStatus(Localization.Get("kspStatusNotInstalled"), true);
        }
        else
        {
            SetStatus(Localization.Get("kspStatusDenied"), true);
        }
    }

    static void SetStatus(string text, bool isError)
    {
        Status = text;
        StatusIsError = isError;
        Version++;
    }

    public static bool AiOption(string key) => AiOptions.TryGetValue(key, out bool on) && on;

    public static FixInfo Find(string name) => Fixes.Find(f => f.Name == name);
}

/// <summary>
/// Catches replies to our own commands before they reach the F1 console.
/// </summary>
[HarmonyPatch(typeof(NetPackageConsoleCmdClient), nameof(NetPackageConsoleCmdClient.ProcessPackage))]
public static class KspConsoleReplyPatch
{
    static bool Prefix(NetPackageConsoleCmdClient __instance)
    {
        if (__instance.bExecute)
        {
            return true;
        }
        if (KspClient.OnReply(__instance.lines))
        {
            return false;
        }
        KspClient.OnOtherReply(__instance.lines);
        return true;
    }
}
