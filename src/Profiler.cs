using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using HarmonyLib;
using UnityEngine;

namespace KitsuneServerPerf
{
    // Times the game's hot methods and every Harmony patch method other mods installed,
    // for a fixed window, then prints where the time went.
    //
    // Each timed method gets a prefix that reads the clock and a postfix that adds the
    // elapsed time. Times are inclusive: TickEntity includes the AI, buffs and any mod
    // patches it calls, so rows overlap and don't add up to a total.
    internal static class Profiler
    {
        private const string HarmonyId = "kitsune.serverperf.profiler";

        // Vanilla methods worth seeing even when no mod patches them.
        private static readonly string[] VanillaTargets =
        {
            "GameManager:Update",
            "GameManager:UpdateTick",
            "World:OnUpdateTick",
            "World:TickEntities",
            "World:TickEntity",
            "World:EntityActivityUpdate",
            "World:TickSleeperVolumes",
            "WorldBlockTicker:Tick",
            "AIDirector:Tick",
            "EntityAlive:Update",
            "EntityAlive:OnUpdateEntity",
            "EntityAlive:OnUpdateLive",
            "EntityAlive:updateTasks",
            "EntityAlive:CanEntityBeSeen",
            "EAIManager:Update",
            "UAI.UAIBase:Update",
            "EntityMoveHelper:UpdateMoveHelper",
            "EntityBuffs:Tick",
            "EntityStats:Tick",
            "EffectManager:GetValue",
            "MinEventActionModifyCVar:Execute",
            "NetEntityDistribution:OnUpdateEntities",
            "GamePath.ASPPathFinder:OnPathFinished",
            "AvatarZombieController:Update",
            "AvatarZombieController:LateUpdate",
        };

        private class Stat
        {
            public string Name;
            public string Mod;      // null for vanilla methods
            public long Ticks;
            public long Calls;
        }

        private enum State { Idle, Installing, Running, Removing }

        // Patching or unpatching a method takes milliseconds (Harmony rebuilds it), and with
        // a big pack there are over a thousand of them. Doing that all in one frame froze a
        // test server for 9 seconds, so it's spread over frames with this budget per frame.
        private const double FrameBudgetMs = 15.0;

        private static Dictionary<MethodBase, Stat> stats = new Dictionary<MethodBase, Stat>();
        private static readonly List<MethodBase> patched = new List<MethodBase>();
        private static Harmony harmony;
        private static State state = State.Idle;
        private static volatile bool collecting;
        private static bool cancelInstall;
        private static int runId;
        private static long startTimestamp;
        private static int startFrame;
        private static string startCensus;

        public static string LastReport { get; private set; }

        public static string Start(float seconds)
        {
            switch (state)
            {
                case State.Installing:
                case State.Running:
                    return "kperf is already running. 'kperf stop' to end it early.";
                case State.Removing:
                    return "kperf is still removing its timing patches from the last run. Try again in a few seconds.";
            }
            if (GameManager.Instance == null || GameManager.Instance.World == null)
            {
                return "No world loaded.";
            }

            harmony = harmony ?? new Harmony(HarmonyId);
            Dictionary<Assembly, string> modByAssembly = MapAssembliesToMods();
            var newStats = new Dictionary<MethodBase, Stat>();
            var targets = new List<MethodBase>();

            foreach (string t in VanillaTargets)
            {
                MethodInfo m = AccessTools.Method(t);
                if (m != null && !newStats.ContainsKey(m))
                {
                    newStats[m] = new Stat { Name = t.Replace(':', '.') };
                    targets.Add(m);
                }
            }

            // Every prefix/postfix/finalizer another mod has installed anywhere.
            foreach (MethodBase original in Harmony.GetAllPatchedMethods().ToList())
            {
                Patches info = Harmony.GetPatchInfo(original);
                if (info == null)
                {
                    continue;
                }
                foreach (Patch p in info.Prefixes.Concat(info.Postfixes).Concat(info.Finalizers))
                {
                    MethodInfo pm = p.PatchMethod;
                    if (p.owner == HarmonyId || newStats.ContainsKey(pm) || pm.IsGenericMethod || pm.DeclaringType == null || pm.DeclaringType.ContainsGenericParameters)
                    {
                        continue;
                    }
                    modByAssembly.TryGetValue(pm.DeclaringType.Assembly, out string mod);
                    newStats[pm] = new Stat
                    {
                        Name = $"{pm.DeclaringType.Name}.{pm.Name} on {original.DeclaringType?.Name}.{original.Name}",
                        Mod = mod ?? pm.DeclaringType.Assembly.GetName().Name,
                    };
                    targets.Add(pm);
                }
            }

            // The table is in place before any timing patch exists, and nothing is
            // counted until every patch is in.
            collecting = false;
            stats = newStats;
            state = State.Installing;
            cancelInstall = false;
            runId++;
            ThreadManager.StartCoroutine(Run(targets, seconds, runId));

            string msg = $"kperf: installing timers on {targets.Count} methods (spread over a few seconds so the server doesn't hitch), then timing for {seconds:0} s. The report prints here when it's done.";
            ModLog.Info(msg);
            return msg;
        }

        private static IEnumerator Run(List<MethodBase> targets, float seconds, int id)
        {
            var begin = new HarmonyMethod(typeof(Profiler), nameof(Begin)) { priority = Priority.First };
            var end = new HarmonyMethod(typeof(Profiler), nameof(End)) { priority = Priority.Last };
            int failed = 0;
            var frame = Stopwatch.StartNew();
            foreach (MethodBase m in targets)
            {
                if (cancelInstall)
                {
                    break;
                }
                try
                {
                    harmony.Patch(m, prefix: begin, postfix: end);
                    patched.Add(m);
                }
                catch (Exception e)
                {
                    failed++;
                    stats.Remove(m);
                    ModLog.Warn($"kperf: can't time {m.DeclaringType?.Name}.{m.Name}: {e.Message}");
                }
                if (frame.Elapsed.TotalMilliseconds > FrameBudgetMs)
                {
                    yield return null;
                    frame.Restart();
                }
            }

            if (cancelInstall || id != runId)
            {
                ThreadManager.StartCoroutine(RemovePatches());
                yield break;
            }

            state = State.Running;
            startTimestamp = Stopwatch.GetTimestamp();
            startFrame = Time.frameCount;
            startCensus = Census();
            collecting = true;
            ModLog.Info($"kperf: timing {patched.Count} methods for {seconds:0} s ({failed} could not be timed).");

            yield return new WaitForSecondsRealtime(seconds);
            if (state == State.Running && id == runId)
            {
                Finish(printReport: true);
            }
        }

        public static string Stop(bool printReport)
        {
            switch (state)
            {
                case State.Installing:
                    cancelInstall = true;
                    return "kperf: stopped before timing started; removing its patches.";
                case State.Running:
                    return Finish(printReport);
                case State.Removing:
                    return "kperf is already stopping.";
                default:
                    return "kperf is not running.";
            }
        }

        // Game shutdown: coroutines won't get another frame, and a hitch doesn't matter now.
        public static void Shutdown()
        {
            collecting = false;
            cancelInstall = true;
            if (state == State.Idle || harmony == null)
            {
                return;
            }
            try
            {
                harmony.UnpatchSelf();
            }
            catch (Exception e)
            {
                ModLog.Error("kperf: failed to remove timing patches: " + e);
            }
            patched.Clear();
            state = State.Idle;
        }

        private static string Finish(bool printReport)
        {
            collecting = false;
            double elapsed = (Stopwatch.GetTimestamp() - startTimestamp) / (double)Stopwatch.Frequency;
            int frames = Time.frameCount - startFrame;

            LastReport = BuildReport(elapsed, frames);
            string file = SaveReport(LastReport);
            if (printReport)
            {
                foreach (string line in LastReport.Split('\n'))
                {
                    Log.Out(line);
                }
            }

            ThreadManager.StartCoroutine(RemovePatches());
            return LastReport + (file != null ? $"\nSaved to {file}" : "");
        }

        private static IEnumerator RemovePatches()
        {
            state = State.Removing;
            var frame = Stopwatch.StartNew();
            for (int i = patched.Count - 1; i >= 0; i--)
            {
                try
                {
                    harmony.Unpatch(patched[i], HarmonyPatchType.All, HarmonyId);
                }
                catch (Exception e)
                {
                    ModLog.Error($"kperf: failed to remove timing patch from {patched[i].DeclaringType?.Name}.{patched[i].Name}: {e.Message}");
                }
                patched.RemoveAt(i);
                if (frame.Elapsed.TotalMilliseconds > FrameBudgetMs)
                {
                    yield return null;
                    frame.Restart();
                }
            }
            state = State.Idle;
            ModLog.Info("kperf: timing patches removed.");
        }

        private static void Begin(out long __state)
        {
            __state = Stopwatch.GetTimestamp();
        }

        private static void End(long __state, MethodBase __originalMethod)
        {
            if (collecting && stats.TryGetValue(__originalMethod, out Stat s))
            {
                Interlocked.Add(ref s.Ticks, Stopwatch.GetTimestamp() - __state);
                Interlocked.Increment(ref s.Calls);
            }
        }

        private static Dictionary<Assembly, string> MapAssembliesToMods()
        {
            var map = new Dictionary<Assembly, string>();
            foreach (Mod mod in ModManager.GetLoadedMods())
            {
                foreach (Assembly a in mod.AllAssemblies)
                {
                    map[a] = mod.Name;
                }
            }
            return map;
        }

        private static string BuildReport(double seconds, int frames)
        {
            var sb = new StringBuilder();
            double toMs = 1000.0 / Stopwatch.Frequency;
            double perSecond = 1.0 / Math.Max(seconds, 0.001);

            sb.Append("==== kperf report ====\n");
            sb.Append($"Sampled {seconds:0.0} s, {frames} frames ({frames * perSecond:0.0} fps; a dedicated server aims for 20).\n");
            sb.Append("ms/s = milliseconds of CPU per second. 1000 ms/s is one full core; the main thread can't go past that.\n");
            sb.Append("Times are inclusive, so rows overlap. Very fast methods called very often read a little high (the timer has a cost too).\n");
            sb.Append("At start:  ").Append(startCensus).Append('\n');
            sb.Append("At end:    ").Append(Census()).Append('\n');

            List<Stat> rows = stats.Values.Where(s => s.Calls > 0).ToList();

            sb.Append("\n-- Game methods (include any mod patches on them) --\n");
            AppendRows(sb, rows.Where(s => s.Mod == null).OrderByDescending(s => s.Ticks), 30, toMs, perSecond, withMod: false);

            sb.Append("\n-- By mod (time inside its Harmony patches; transpilers can't be timed) --\n");
            sb.Append("     ms/s   calls/s  mod\n");
            foreach (var g in rows.Where(s => s.Mod != null).GroupBy(s => s.Mod).Select(g => new { Mod = g.Key, Ticks = g.Sum(s => s.Ticks), Calls = g.Sum(s => s.Calls) }).OrderByDescending(g => g.Ticks).Take(25))
            {
                sb.Append($"{g.Ticks * toMs * perSecond,9:0.00} {g.Calls * perSecond,9:0}  {g.Mod}\n");
            }

            sb.Append("\n-- Slowest mod patches --\n");
            AppendRows(sb, rows.Where(s => s.Mod != null).OrderByDescending(s => s.Ticks), 30, toMs, perSecond, withMod: true);

            int silent = stats.Values.Count(s => s.Calls == 0 && s.Mod != null);
            if (silent > 0)
            {
                sb.Append($"\n{silent} mod patches never ran in this window (or are too small to time).\n");
            }
            return sb.ToString();
        }

        private static void AppendRows(StringBuilder sb, IEnumerable<Stat> rows, int max, double toMs, double perSecond, bool withMod)
        {
            sb.Append("     ms/s   calls/s   us/call  ").Append(withMod ? "mod: patch\n" : "method\n");
            foreach (Stat s in rows.Take(max))
            {
                double ms = s.Ticks * toMs;
                double usPerCall = ms * 1000.0 / Math.Max(s.Calls, 1);
                sb.Append($"{ms * perSecond,9:0.00} {s.Calls * perSecond,9:0} {usPerCall,9:0.0}  ");
                sb.Append(withMod ? $"{s.Mod}: {s.Name}\n" : $"{s.Name}\n");
            }
        }

        // One line: how many of what are alive, and how the AI speed scales are spread.
        public static string Census()
        {
            World world = GameManager.Instance?.World;
            if (world == null)
            {
                return "no world";
            }
            int enemies = 0, animals = 0, npcs = 0, players = 0, full = 0, mid = 0, low = 0, lower = 0;
            foreach (EntityAlive e in world.EntityAlives)
            {
                if (e == null || e.IsDead())
                {
                    continue;
                }
                if (e is EntityPlayer) { players++; continue; }
                if (e is EntityEnemy) enemies++;
                else if (e is EntityAnimal) animals++;
                else npcs++;

                float s = e.aiActiveScale;
                if (s >= 1f) full++;
                else if (s >= 0.3f) mid++;
                else if (s >= 0.1f) low++;
                else lower++;
            }
            return $"{enemies} enemies, {animals} animals, {npcs} NPCs, {players} players | AI speed: {full} full, {mid} at 0.3, {low} at 0.1, {lower} throttled below 0.1";
        }

        public static string TopClasses(int max)
        {
            World world = GameManager.Instance?.World;
            if (world == null)
            {
                return "no world";
            }
            var counts = new Dictionary<string, int>();
            foreach (EntityAlive e in world.EntityAlives)
            {
                if (e == null || e.IsDead() || e is EntityPlayer)
                {
                    continue;
                }
                string name = e.EntityClass?.entityClassName ?? e.GetType().Name;
                counts.TryGetValue(name, out int n);
                counts[name] = n + 1;
            }
            return string.Join(", ", counts.OrderByDescending(kv => kv.Value).Take(max).Select(kv => $"{kv.Key} {kv.Value}"));
        }

        private static string SaveReport(string report)
        {
            try
            {
                string dir = Path.Combine(GameIO.GetUserGameDataDir(), "KitsuneServerPerf");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, $"kperf-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
                File.WriteAllText(file, report);
                return file;
            }
            catch (Exception e)
            {
                ModLog.Warn("kperf: could not save report: " + e.Message);
                return null;
            }
        }
    }
}
