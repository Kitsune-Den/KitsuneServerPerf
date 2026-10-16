using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace KitsuneServerPerf
{
    // EntityAlive.CanEntityBeSeen moves the looker's whole model to layer 2 (Ignore Raycast)
    // so its own ray doesn't hit itself, raycasts, then moves it back. Each move walks
    // every transform under the model (GetComponentsInChildren), which on overhaul
    // zombies is a few hundred bones, twice per sight check.
    //
    // A raycast can only hit colliders, so moving just the colliders gives the same
    // ray result. On a dedicated server nothing renders, so the layer of the other
    // transforms doesn't matter. This swaps the two SetModelLayer calls inside
    // CanEntityBeSeen for a version that only touches the cached collider list.
    internal static class SightCheckFix
    {
        private const int IgnoreRaycastLayer = 2;
        private const int LargeEntityBlockerLayer = 19;
        private const int RebuildEveryFrames = 600;

        private class LayerCache
        {
            public Transform Model;
            public GameObject[] Objects;
            public bool[] IsBlocker;
            public int BuiltFrame;
        }

        private static readonly ConditionalWeakTable<EntityAlive, LayerCache> caches = new ConditionalWeakTable<EntityAlive, LayerCache>();
        private static readonly Dictionary<Type, bool> overridesSetModelLayer = new Dictionary<Type, bool>();
        private static readonly List<Collider> colliderBuffer = new List<Collider>();
        private static readonly Type[] setModelLayerArgs = { typeof(int), typeof(bool), typeof(string[]) };

        public static string Apply()
        {
            if (!GameManager.IsDedicatedServer)
            {
                return "not a dedicated server";
            }

            MethodInfo target = AccessTools.Method(typeof(EntityAlive), nameof(EntityAlive.CanEntityBeSeen));
            if (target == null)
            {
                return "EntityAlive.CanEntityBeSeen not found";
            }
            ModInit.Harmony.Patch(target, transpiler: new HarmonyMethod(typeof(SightCheckFix), nameof(Transpiler)));
            return null;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo original = AccessTools.Method(typeof(EntityAlive), nameof(EntityAlive.SetModelLayer), setModelLayerArgs);
            MethodInfo replacement = AccessTools.Method(typeof(SightCheckFix), nameof(SetModelLayer));
            int replaced = 0;
            foreach (CodeInstruction code in instructions)
            {
                if ((code.opcode == OpCodes.Callvirt || code.opcode == OpCodes.Call) && code.operand is MethodInfo m && m == original)
                {
                    code.opcode = OpCodes.Call;
                    code.operand = replacement;
                    replaced++;
                }
                yield return code;
            }
            if (replaced != 2)
            {
                ModLog.Warn($"SightCheckLayers: expected 2 SetModelLayer calls in CanEntityBeSeen, replaced {replaced}");
            }
        }

        // Same arguments as EntityAlive.SetModelLayer, with the instance first, so the
        // call site's stack lines up.
        public static void SetModelLayer(EntityAlive entity, int layer, bool force, string[] excludeTags)
        {
            if (excludeTags != null || OverridesSetModelLayer(entity.GetType()))
            {
                entity.SetModelLayer(layer, force, excludeTags);
                return;
            }

            LayerCache cache = GetCache(entity);
            if (cache == null)
            {
                entity.SetModelLayer(layer, force, excludeTags);
                return;
            }

            GameObject[] objects = cache.Objects;
            if (layer == IgnoreRaycastLayer)
            {
                for (int i = 0; i < objects.Length; i++)
                {
                    objects[i].layer = IgnoreRaycastLayer;
                }
            }
            else
            {
                // Matches Utils.SetLayerRecursively: LargeEntityBlocker parts keep layer 19.
                bool[] isBlocker = cache.IsBlocker;
                for (int i = 0; i < objects.Length; i++)
                {
                    objects[i].layer = isBlocker[i] ? LargeEntityBlockerLayer : layer;
                }
            }
        }

        private static LayerCache GetCache(EntityAlive entity)
        {
            Transform model = entity.emodel != null ? entity.emodel.GetModelTransform() : null;
            if (model == null)
            {
                return null;
            }

            if (caches.TryGetValue(entity, out LayerCache cache)
                && cache.Model == model
                && Time.frameCount - cache.BuiltFrame < RebuildEveryFrames
                && AllAlive(cache.Objects))
            {
                return cache;
            }

            model.GetComponentsInChildren(true, colliderBuffer);
            var objects = new GameObject[colliderBuffer.Count];
            var isBlocker = new bool[colliderBuffer.Count];
            for (int i = 0; i < colliderBuffer.Count; i++)
            {
                objects[i] = colliderBuffer[i].gameObject;
                isBlocker[i] = objects[i].CompareTag("LargeEntityBlocker");
            }
            colliderBuffer.Clear();

            cache = new LayerCache { Model = model, Objects = objects, IsBlocker = isBlocker, BuiltFrame = Time.frameCount };
            caches.Remove(entity);
            caches.Add(entity, cache);
            return cache;
        }

        private static bool AllAlive(GameObject[] objects)
        {
            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] == null)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool OverridesSetModelLayer(Type type)
        {
            if (!overridesSetModelLayer.TryGetValue(type, out bool overrides))
            {
                MethodInfo m = type.GetMethod(nameof(EntityAlive.SetModelLayer), BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, setModelLayerArgs, null);
                overrides = m != null && m.DeclaringType != typeof(EntityAlive);
                overridesSetModelLayer[type] = overrides;
            }
            return overrides;
        }
    }
}
