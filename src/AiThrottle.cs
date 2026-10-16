using System.Collections.Generic;
using HarmonyLib;

namespace KitsuneServerPerf
{
    // World.EntityActivityUpdate runs every tick and sets each entity's aiActiveScale:
    // the AI (EAIManager or SCore's utility AI) only updates when the running total of
    // that scale passes 1. Vanilla gives full speed (1.0) to the closest 4-20 entities per
    // player and anything within 8 m, then 0.3 within 15 m and 0.1 for everything else.
    //
    // This postfix adds slower tiers for entities farther away still. It only lowers a
    // scale vanilla already lowered, so the closest entities are never touched.
    //
    // Gameplay cost: AI task timers count AI updates, not seconds, so a far zombie at
    // 0.05 waits twice as long as vanilla between decisions. Its movement and animation
    // still run every tick.
    internal static class AiThrottle
    {
        public static void Apply()
        {
            if (Settings.AiTiers.Count == 0)
            {
                ModLog.Warn("AiThrottle is on but has no valid tiers, nothing to do");
                return;
            }
            ModInit.Harmony.Patch(
                AccessTools.Method(typeof(World), nameof(World.EntityActivityUpdate)),
                postfix: new HarmonyMethod(typeof(AiThrottle), nameof(Postfix)));

            var tiers = new List<string>();
            foreach (AiTier t in Settings.AiTiers)
            {
                tiers.Add($"{System.Math.Sqrt(t.DistanceSq):0} m -> {t.Scale}");
            }
            ModLog.Info("AiThrottle: on, tiers " + string.Join(", ", tiers));
        }

        private static void Postfix(World __instance)
        {
            if (__instance.Players.list.Count == 0)
            {
                return;
            }
            if (Settings.AiSkipDuringBloodMoon && __instance.aiDirector != null && __instance.aiDirector.BloodMoonComponent.BloodMoonActive)
            {
                return;
            }

            List<AiTier> tiers = Settings.AiTiers;
            List<EntityAlive> alives = __instance.EntityAlives;
            for (int i = alives.Count - 1; i >= 0; i--)
            {
                EntityAlive e = alives[i];
                // Vanilla left this one at full speed: one of the closest, or very near.
                if (e.aiActiveScale >= 1f)
                {
                    continue;
                }

                float distSq = e.aiClosestPlayerDistSq;
                float scale = 1f;
                for (int t = 0; t < tiers.Count; t++)
                {
                    if (distSq >= tiers[t].DistanceSq)
                    {
                        scale = tiers[t].Scale;
                        break;
                    }
                }
                if (scale >= e.aiActiveScale || !ShouldThrottle(e))
                {
                    continue;
                }
                e.aiActiveScale = scale;
            }
        }

        private static bool ShouldThrottle(EntityAlive e)
        {
            if (e is EntityPlayer || e.IsDead())
            {
                return false;
            }

            bool kindAllowed = e is EntityEnemy ? Settings.AiThrottleEnemies
                : e is EntityAnimal ? Settings.AiThrottleAnimals
                : Settings.AiThrottleNpcs;
            if (!kindAllowed)
            {
                return false;
            }

            if (Settings.AiSkipWhenAlert && (e.GetAttackTarget() != null || e.HasInvestigatePosition))
            {
                return false;
            }
            return true;
        }
    }
}
