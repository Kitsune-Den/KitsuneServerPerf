using System;
using System.Collections.Generic;
using System.Linq;
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
            speedLogOn = null;
        }

        // ---- Strafe LateUpdate prefix --------------------------------------------------
        //
        // Harmony.Animation.Harmony_Animation+AvatarControllerLateUpdate runs on every zombie
        // every frame. Its real work is one UpdateFloat("Strafe", ...). Around it, it builds
        // two log strings (with float-to-string conversions) and hands them to
        // AdvLogging.DisplayLog, which throws them away unless the
        // AdvancedTroubleshootingFeatures/EntitySpeedCheck flag is on. With the flag off,
        // this does the same UpdateFloat without the strings.

        private const string TroubleshootingClass = "AdvancedTroubleshootingFeatures";
        private const string SpeedFeature = "EntitySpeedCheck";
        private static Func<string, string, bool, bool> checkFeature;
        private static int strafeHash;
        private static bool? speedLogOn;

        public static string ApplyStrafeLateUpdate()
        {
            MethodBase lateUpdate = AccessTools.Method(typeof(AvatarZombieController), nameof(AvatarZombieController.LateUpdate));
            MethodInfo patch = Compat.FindInstalledPatch(lateUpdate, "Harmony.Animation.Harmony_Animation+AvatarControllerLateUpdate", "Prefix");
            if (patch == null)
            {
                return "SCore's strafe LateUpdate patch not loaded";
            }
            MethodInfo check = Compat.FindAssembly("SCore")?.GetType("Configuration")?.GetMethod("CheckFeatureStatus", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string), typeof(string), typeof(bool) }, null);
            FieldInfo hash = AccessTools.Field(patch.DeclaringType.DeclaringType, "_strafeHash");
            if (check == null || hash == null)
            {
                return "this SCore version doesn't match what the filter expects";
            }
            checkFeature = (Func<string, string, bool, bool>)Delegate.CreateDelegate(typeof(Func<string, string, bool, bool>), check);
            strafeHash = (int)hash.GetValue(null);
            Compat.AddFilter(patch, typeof(SCoreFixes), nameof(StrafeFilter));
            return null;
        }

        // __0 / __1: the prefix's own (AvatarZombieController __instance, EntityAlive ___entity).
        // __result: that prefix's return value, which is always true (run vanilla LateUpdate).
        private static bool StrafeFilter(AvatarZombieController __0, EntityAlive __1, ref bool __result)
        {
            if (speedLogOn == null)
            {
                speedLogOn = checkFeature(TroubleshootingClass, SpeedFeature, false);
            }
            if (speedLogOn.Value || __0 == null || __1 == null)
            {
                return true;
            }

            float strafe = __1.speedStrafe;
            if (__1.IsFlyMode.Value || strafe >= 1234f)
            {
                strafe = 0f;
            }
            __0.UpdateFloat(strafeHash, strafe, true);
            __result = true;
            return false;
        }

        // ---- Weather CVars on EntityStats.Tick -----------------------------------------
        //
        // SCore.Harmony.PlayerFeatures.SCoreEntityStats+EntityStats_UpdateWeatherStats sets
        // _sc_rain, _sc_snow and _sc_cloud on every entity every tick. CVars starting with
        // "_" are never sent to clients, and EntityBuffs.SetCustomVar does nothing when the
        // value hasn't changed, so when all three already match, the three calls are skipped.
        //
        // That only holds while nothing else watches SetCustomVar calls for those names, so
        // this checks who else patches SetCustomVar and stays off if it's anyone unexpected.

        private static readonly string[] weatherCVars = { "_sc_rain", "_sc_snow", "_sc_cloud" };
        private static readonly HashSet<string> knownSetCVarPatchers = new HashSet<string>
        {
            "EventOnCVarAdded+BuffManagerSetCustomVar",
            "AsylumSanity.Patch_SanityLevel_SetCustomVar",
        };

        public static string ApplyWeatherCVars()
        {
            MethodBase tick = AccessTools.Method(typeof(EntityStats), nameof(EntityStats.Tick));
            MethodInfo patch = Compat.FindInstalledPatch(tick, "SCore.Harmony.PlayerFeatures.SCoreEntityStats+EntityStats_UpdateWeatherStats", "Postfix");
            if (patch == null)
            {
                return "SCore's weather CVar patch not loaded";
            }

            MethodBase setCVar = AccessTools.Method(typeof(EntityBuffs), nameof(EntityBuffs.SetCustomVar));
            Patches info = Harmony.GetPatchInfo(setCVar);
            if (info != null)
            {
                foreach (Patch p in info.Prefixes.Concat(info.Postfixes).Concat(info.Finalizers).Concat(info.Transpilers))
                {
                    string owner = p.PatchMethod.DeclaringType?.FullName;
                    if (!knownSetCVarPatchers.Contains(owner))
                    {
                        return $"{owner} also patches EntityBuffs.SetCustomVar, so skipping its calls might not be safe";
                    }
                }
            }

            Compat.AddFilter(patch, typeof(SCoreFixes), nameof(WeatherFilter));
            return null;
        }

        // __0: the postfix's own EntityStats __instance.
        private static bool WeatherFilter(EntityStats __0)
        {
            WeatherManager weather = WeatherManager.Instance;
            EntityBuffs buffs = __0?.m_entity?.Buffs;
            if (weather == null || buffs?.CVars == null)
            {
                return true;
            }
            return !Matches(buffs, weatherCVars[0], weather.GetCurrentRainfallPercent())
                || !Matches(buffs, weatherCVars[1], weather.GetCurrentSnowfallPercent())
                || !Matches(buffs, weatherCVars[2], weather.GetCurrentCloudThicknessPercent());
        }

        private static bool Matches(EntityBuffs buffs, string name, float value)
        {
            return buffs.CVars.TryGetValue(name, out float current) && current == value;
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
