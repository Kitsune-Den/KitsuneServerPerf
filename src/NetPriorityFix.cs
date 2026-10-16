using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace KitsuneServerPerf
{
    // NetEntityDistribution.OnUpdateEntities decides how often each zombie's position is
    // sent to clients from its squared distance to the nearest player. The enemy loop
    // computes it as  v.x * v.x + v.z + v.z  instead of  v.x * v.x + v.z * v.z,
    // so zombies north or south of a player land in priority 0 and are sent every tick.
    // The same method's player loop does it correctly. This rewrites the enemy one.
    internal static class NetPriorityFix
    {
        private static int rewrites;

        public static string Apply()
        {
            MethodInfo target = AccessTools.Method(typeof(NetEntityDistribution), nameof(NetEntityDistribution.OnUpdateEntities));
            if (target == null)
            {
                return "NetEntityDistribution.OnUpdateEntities not found";
            }

            rewrites = 0;
            ModInit.Harmony.Patch(target, transpiler: new HarmonyMethod(typeof(NetPriorityFix), nameof(Transpiler)));
            if (rewrites == 1)
            {
                return null;
            }

            // Not exactly one match means the game changed; don't leave a guess in place.
            ModInit.Harmony.Unpatch(target, HarmonyPatchType.Transpiler, ModInit.HarmonyId);
            return $"expected the bug once, found it {rewrites} times (game version changed?)";
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            FieldInfo z = AccessTools.Field(typeof(Vector3), nameof(Vector3.z));
            var codes = new List<CodeInstruction>(instructions);

            // Looking for:  ldfld z; add; ldloc v; ldfld z; add
            // Rewriting to: ldfld z; nop; ldloc v; ldfld z; mul; add
            for (int i = 0; i + 4 < codes.Count; i++)
            {
                if (codes[i].LoadsField(z)
                    && codes[i + 1].opcode == OpCodes.Add
                    && codes[i + 2].IsLdloc()
                    && codes[i + 3].LoadsField(z)
                    && codes[i + 4].opcode == OpCodes.Add)
                {
                    codes[i + 1].opcode = OpCodes.Nop;
                    codes[i + 4].opcode = OpCodes.Mul;
                    codes.Insert(i + 5, new CodeInstruction(OpCodes.Add));
                    rewrites++;
                }
            }
            return codes;
        }
    }
}
