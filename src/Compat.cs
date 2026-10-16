using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace KitsuneServerPerf
{
    // Helpers for fixing other mods' Harmony patches without removing them.
    //
    // Rather than unpatching another mod and re-adding our own copy (which would change
    // the order patches run in), we put a cheap prefix on the other mod's patch method.
    // The prefix returns false only in the cases where that patch method would have
    // returned without doing anything, so behaviour stays the same.
    internal static class Compat
    {
        public static Assembly FindAssembly(string name)
        {
            foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return a;
                }
            }
            return null;
        }

        // The patch method another mod installed on target, found by its declaring type.
        public static MethodInfo FindInstalledPatch(MethodBase target, string declaringTypeFullName, string methodName)
        {
            if (target == null)
            {
                return null;
            }
            Patches info = Harmony.GetPatchInfo(target);
            if (info == null)
            {
                return null;
            }
            foreach (IEnumerable<Patch> list in new[] { info.Prefixes, info.Postfixes })
            {
                foreach (Patch p in list)
                {
                    MethodInfo m = p.PatchMethod;
                    if (m.Name == methodName && m.DeclaringType != null && m.DeclaringType.FullName == declaringTypeFullName)
                    {
                        return m;
                    }
                }
            }
            return null;
        }

        public static void AddFilter(MethodInfo patchMethod, Type filterType, string filterMethod)
        {
            ModInit.Harmony.Patch(patchMethod, prefix: new HarmonyMethod(filterType, filterMethod));
        }
    }
}
