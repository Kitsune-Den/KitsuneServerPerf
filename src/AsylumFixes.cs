using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace KitsuneServerPerf
{
    // Filters in front of hot patches that ship in The Asylum overhaul pack.
    // Each filter returns false only when the patch it guards would have done nothing.
    internal static class AsylumFixes
    {
        // ---- CrystalHellHusbandry: EffectManager.GetValue postfix ----------------------
        //
        // GetValue is the game's most-called stat lookup. The postfix runs on every call
        // and, before checking which stat it is, converts the enum to a string several
        // times and walks the entity's CVars with LINQ. It only ever changes:
        //   - stats named TamingValue / TamingSedationValue (tooltip values),
        //   - "animal tracker" stats (name contains "track" and "animal"),
        //   - stat 104 (HealthMax),
        //   - RunSpeed / WalkSpeed / CrouchSpeed.
        // For every other stat it returns without touching the result.

        private static bool[] husbandryStats;

        public static string ApplyHusbandryGetValue()
        {
            MethodBase getValue = AccessTools.Method(typeof(EffectManager), nameof(EffectManager.GetValue));
            MethodInfo patch = Compat.FindInstalledPatch(getValue, "CrystalHellHusbandry.Patches.EffectManager_GetValue_Patches", "Postfix");
            if (patch == null)
            {
                return "CrystalHellHusbandry not loaded";
            }

            int max = 0;
            foreach (PassiveEffects p in Enum.GetValues(typeof(PassiveEffects)))
            {
                max = Math.Max(max, (int)p);
            }
            husbandryStats = new bool[max + 1];
            foreach (PassiveEffects p in Enum.GetValues(typeof(PassiveEffects)))
            {
                husbandryStats[(int)p] = HusbandryTouches(p);
            }

            Compat.AddFilter(patch, typeof(AsylumFixes), nameof(HusbandryFilter));
            return null;
        }

        private static bool HusbandryTouches(PassiveEffects p)
        {
            string name = p.ToString();
            string lower = name.ToLowerInvariant();
            return (int)p == 104
                || string.Equals(name, "TamingValue", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "TamingSedationValue", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "RunSpeed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "WalkSpeed", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "CrouchSpeed", StringComparison.OrdinalIgnoreCase)
                || (lower.Contains("track") && (lower.Contains("animal") || lower.Contains("trackeranimal") || lower.Contains("entityanimal")));
        }

        // Bound by parameter name to the husbandry postfix's own _passiveEffect argument.
        private static bool HusbandryFilter(PassiveEffects _passiveEffect)
        {
            int i = (int)_passiveEffect;
            return i < 0 || i >= husbandryStats.Length || husbandryStats[i];
        }

        // ---- AsylumHealFix: MinEventActionModifyCVar.Execute postfix -------------------
        //
        // Runs after every CVar change in the game (thousands of buff actions per second
        // with Asylum) and reads cvarName through Harmony's Traverse reflection, only to
        // compare it with "medRegHealthIncSpeed".

        private const string HealCVar = "medRegHealthIncSpeed";

        public static string ApplyHealFix()
        {
            MethodBase execute = AccessTools.Method(typeof(MinEventActionModifyCVar), nameof(MinEventActionModifyCVar.Execute));
            MethodInfo patch = Compat.FindInstalledPatch(execute, "AsylumHealFix.Patch_ModifyCVar_Execute", "Postfix");
            if (patch == null)
            {
                return "AsylumHealFix not loaded";
            }
            Compat.AddFilter(patch, typeof(AsylumFixes), nameof(HealFixFilter));
            return null;
        }

        // __0 / __1: the postfix's own arguments (MinEventActionModifyCVar __instance, MinEventParams _params),
        // bound by position because "__instance" means something else to Harmony.
        private static bool HealFixFilter(MinEventActionModifyCVar __0, MinEventParams __1)
        {
            return __1?.Self != null && __0 != null && __0.cvarName == HealCVar;
        }

        // ---- WMMZombieRandomBuffs + AsylumZRBCompat: EntityAlive.Update postfixes ------
        //
        // Both run every frame for every entity. Each lower-cases the entity class name,
        // checks whether it starts with "zombie", and checks for the marker buff that
        // means "already rolled". WMM handles zombie* classes, the Asylum compat patch
        // handles every other EntityEnemy. Neither does anything for a dead entity or one
        // that already has the marker buff.

        private const string MarkerBuff = "zrb-checkstatus01";
        private const byte ClassNoName = 1, ClassZombie = 2, ClassOther = 3;
        // Entity class ids are hashes of the class name, so this is keyed, not indexed.
        private static readonly Dictionary<int, byte> classKinds = new Dictionary<int, byte>();

        public static string ApplyRandomBuffs()
        {
            MethodBase update = AccessTools.Method(typeof(EntityAlive), nameof(EntityAlive.Update));
            MethodInfo wmm = Compat.FindInstalledPatch(update, "WMMZombieRandomBuffs+Patch_EntityAlive_Update", "Postfix");
            MethodInfo compat = Compat.FindInstalledPatch(update, "AsylumZRBCompat_Patch", "Postfix");
            if (wmm == null && compat == null)
            {
                return "WMMZombieRandomBuffs not loaded";
            }
            if (wmm != null)
            {
                Compat.AddFilter(wmm, typeof(AsylumFixes), nameof(WmmFilter));
            }
            if (compat != null)
            {
                Compat.AddFilter(compat, typeof(AsylumFixes), nameof(ZrbCompatFilter));
            }
            return null;
        }

        private static bool WmmFilter(EntityAlive __0)
        {
            return __0 != null
                && ClassKind(__0) == ClassZombie
                && !__0.IsDead()
                && (__0.Buffs == null || !__0.Buffs.HasBuff(MarkerBuff));
        }

        private static bool ZrbCompatFilter(EntityAlive __0)
        {
            return __0 is EntityEnemy
                && ClassKind(__0) == ClassOther
                && !__0.IsDead()
                && (__0.Buffs == null || !__0.Buffs.HasBuff(MarkerBuff));
        }

        // Same test both mods use: EntityClass.entityClassName.ToLower().StartsWith("zombie"),
        // worked out once per entity class instead of every frame.
        private static byte ClassKind(EntityAlive e)
        {
            int id = e.entityClass;
            if (classKinds.TryGetValue(id, out byte kind))
            {
                return kind;
            }

            string name = e.EntityClass?.entityClassName;
            kind = name == null ? ClassNoName
                : name.ToLower().StartsWith("zombie") ? ClassZombie
                : ClassOther;
            classKinds[id] = kind;
            return kind;
        }
    }
}
