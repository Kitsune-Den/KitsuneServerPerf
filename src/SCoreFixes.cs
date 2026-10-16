using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace KitsuneServerPerf
{
    internal static class SCoreFixes
    {
        // ---- Debug label -------------------------------------------------------------
        //
        // UAI.SCoreUtils.DisplayDebugInformation and UAI.CombatUtils.DisplayDebugInformation
        // run on every utility-AI update. Their early-out is
        //   if (!GameManager.IsDedicatedServer && (debug off || dead)) return;
        // so on a dedicated server they always format a multi-line label into
        // EntityAlive.DebugNameInfo. That field is only ever drawn by the client HUD
        // (GUIHUDEntityName) and is never sent over the network, so on a dedicated
        // server the work is thrown away.

        public static string ApplyDebugLabel()
        {
            if (!GameManager.IsDedicatedServer)
            {
                return "not a dedicated server";
            }
            Assembly score = Compat.FindAssembly("SCore");
            if (score == null)
            {
                return "SCore not loaded";
            }

            int patched = 0;
            foreach (string typeName in new[] { "UAI.SCoreUtils", "UAI.CombatUtils" })
            {
                MethodInfo m = score.GetType(typeName)?.GetMethod("DisplayDebugInformation", BindingFlags.Public | BindingFlags.Static);
                if (m != null)
                {
                    ModInit.Harmony.Patch(m, prefix: new HarmonyMethod(typeof(SCoreFixes), nameof(SkipOnDedicated)));
                    patched++;
                }
            }
            return patched > 0 ? null : "DisplayDebugInformation not found in this SCore version";
        }

        private static bool SkipOnDedicated() => false;

        // ---- Feature flag cache --------------------------------------------------------
        //
        // Configuration.CheckFeatureStatus looks up the ConfigFeatureBlock block and scans
        // its properties with a culture-aware compare on every call. SCore calls it from
        // per-frame code (for example once per zombie in AvatarZombieController.LateUpdate).
        // The block's properties don't change while a world is running, so the answer for
        // the same (class, feature) doesn't either. The one exception is checkCVarFirst on
        // a client with a local player, which reads a player CVar; that path isn't cached.

        private static readonly object cacheLock = new object();
        private static readonly Dictionary<string, Dictionary<string, bool>> classFeatureCache = new Dictionary<string, Dictionary<string, bool>>();
        private static readonly Dictionary<string, bool> featureCache = new Dictionary<string, bool>();

        public static void ClearCaches()
        {
            lock (cacheLock)
            {
                classFeatureCache.Clear();
                featureCache.Clear();
            }
        }

        public static string ApplyFeatureFlagCache()
        {
            Assembly score = Compat.FindAssembly("SCore");
            Type config = score?.GetType("Configuration");
            if (config == null)
            {
                return "SCore not loaded";
            }

            MethodInfo twoArg = config.GetMethod("CheckFeatureStatus", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string), typeof(string), typeof(bool) }, null);
            MethodInfo oneArg = config.GetMethod("CheckFeatureStatus", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
            if (twoArg == null && oneArg == null)
            {
                return "CheckFeatureStatus not found in this SCore version";
            }
            if (twoArg != null)
            {
                ModInit.Harmony.Patch(twoArg,
                    prefix: new HarmonyMethod(typeof(SCoreFixes), nameof(ClassFeaturePrefix)),
                    postfix: new HarmonyMethod(typeof(SCoreFixes), nameof(ClassFeaturePostfix)));
            }
            if (oneArg != null)
            {
                ModInit.Harmony.Patch(oneArg,
                    prefix: new HarmonyMethod(typeof(SCoreFixes), nameof(FeaturePrefix)),
                    postfix: new HarmonyMethod(typeof(SCoreFixes), nameof(FeaturePostfix)));
            }
            return null;
        }

        private static bool Cacheable(bool checkCVarFirst)
        {
            return !checkCVarFirst || GameManager.Instance == null || GameManager.Instance.myEntityPlayerLocal == null;
        }

        // __state: true when the original ran and its result should be stored.
        private static bool ClassFeaturePrefix(string strClass, string strFeature, bool checkCVarFirst, ref bool __result, out bool __state)
        {
            __state = false;
            if (strClass == null || strFeature == null || !Cacheable(checkCVarFirst))
            {
                return true;
            }
            lock (cacheLock)
            {
                if (classFeatureCache.TryGetValue(strClass, out Dictionary<string, bool> features) && features.TryGetValue(strFeature, out bool cached))
                {
                    __result = cached;
                    return false;
                }
            }
            __state = true;
            return true;
        }

        private static void ClassFeaturePostfix(string strClass, string strFeature, bool __result, bool __state)
        {
            if (!__state)
            {
                return;
            }
            lock (cacheLock)
            {
                if (!classFeatureCache.TryGetValue(strClass, out Dictionary<string, bool> features))
                {
                    features = new Dictionary<string, bool>();
                    classFeatureCache[strClass] = features;
                }
                features[strFeature] = __result;
            }
        }

        private static bool FeaturePrefix(string strFeature, ref bool __result, out bool __state)
        {
            __state = false;
            if (strFeature == null)
            {
                return true;
            }
            lock (cacheLock)
            {
                if (featureCache.TryGetValue(strFeature, out bool cached))
                {
                    __result = cached;
                    return false;
                }
            }
            __state = true;
            return true;
        }

        private static void FeaturePostfix(string strFeature, bool __result, bool __state)
        {
            if (!__state)
            {
                return;
            }
            lock (cacheLock)
            {
                featureCache[strFeature] = __result;
            }
        }
    }
}
