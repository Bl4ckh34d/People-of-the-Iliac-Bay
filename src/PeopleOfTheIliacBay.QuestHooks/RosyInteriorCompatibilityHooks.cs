using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using HarmonyLib;
using UnityEngine;

namespace PeopleOfTheIliacBay.QuestHooks
{
    // Keep the dependency's valid entry correction, but never resume it in another scene.
    public sealed class RosyInteriorCompatibilityHooks : IDisposable
    {
        private const string Owner = "local.peopleoftheiliacbay.rosy-interior-lifetime";
        private readonly Harmony harmony = new Harmony(Owner);
        private static int generation;
        public static bool Installed { get; private set; }
        public static int Cancelled { get; private set; }
        public static int Completed { get; private set; }

        public RosyInteriorCompatibilityHooks()
        {
            Type target = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                target = assembly.GetType("FlowerDeActivatorMod.FlowerDeActivator", false);
                if (target != null) break;
            }
            if (target == null) return; // Optional, separately installed dependency.
            var routine = AccessTools.DeclaredMethod(target, "TeleportPlayerToEnterDoorOrMarkerAfterDelay",
                new[] { typeof(DaggerfallInterior), typeof(StaticDoor), typeof(float) });
            var enter = AccessTools.DeclaredMethod(target, "OnTransitionToInterior");
            var exit = AccessTools.DeclaredMethod(target, "OnTransitionToExterior");
            if (routine == null || routine.ReturnType != typeof(IEnumerator) || enter == null || exit == null)
                throw new InvalidOperationException("Unsupported Rosy's Resources interior callback signature.");
            try
            {
                harmony.Patch(enter, prefix: new HarmonyMethod(typeof(RosyInteriorCompatibilityHooks), nameof(NewTransition)));
                harmony.Patch(exit, prefix: new HarmonyMethod(typeof(RosyInteriorCompatibilityHooks), nameof(NewTransition)));
                harmony.Patch(routine, postfix: new HarmonyMethod(typeof(RosyInteriorCompatibilityHooks), nameof(Wrap)));
                Installed = true;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ROSY_INTERIOR_GUARD_INSTALLED");
            }
            catch { Dispose(); throw; }
        }

        private static void NewTransition() { generation++; }

        private static void Wrap(DaggerfallInterior __0, ref IEnumerator __result)
        {
            if (__result != null) __result = Guard(__result, __0, generation);
        }

        private static IEnumerator Guard(IEnumerator original, DaggerfallInterior interior, int visit)
        {
            try
            {
                while (true)
                {
                    var game = GameManager.Instance;
                    bool valid = visit == generation && interior && game && game.PlayerEnterExit &&
                        game.PlayerEnterExit.IsPlayerInsideBuilding && game.PlayerEnterExit.Interior == interior &&
                        interior.gameObject.activeInHierarchy && game.PlayerObject;
                    if (valid && interior.Markers != null)
                        foreach (var marker in interior.Markers)
                            if ((int)marker.type == 8 && !marker.gameObject) { valid = false; break; }
                    if (!valid)
                    {
                        Cancelled++;
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ROSY_INTERIOR_CORRECTION_CANCELLED: stale visit or missing entry marker.");
                        yield break;
                    }
                    if (!original.MoveNext())
                    {
                        Completed++;
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ROSY_INTERIOR_CORRECTION_COMPLETED");
                        yield break;
                    }
                    yield return original.Current;
                }
            }
            finally { (original as IDisposable)?.Dispose(); }
        }

        public void Dispose()
        {
            harmony.UnpatchAll(Owner);
            generation++;
            Installed = false;
        }
    }
}
