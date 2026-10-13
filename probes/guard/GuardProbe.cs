using System;
using System.Collections;
using System.Linq;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using HarmonyLib;
using UnityEngine;

namespace LivingCities.Probes
{
    // Temporary feasibility test only. Never included in the Living Cities release.
    public sealed class GuardProbe : MonoBehaviour
    {
        private static int batchCalls, singleCalls;
        private static GameObject sentinel;
        private const string Owner = "local.livingcities.guard-feasibility-probe";
        [Invoke(StateManager.StateTypes.Start, 100)]
        public static void Init(InitParams p)
        {
            p.Mod.IsReady = true;
            if (!Environment.GetCommandLineArgs().Contains("-livingcities-guard-probe")) return;
            DontDestroyOnLoad(new GameObject("Guard interception probe", typeof(GuardProbe)));
        }
        private IEnumerator Start()
        {
            while (GameManager.Instance == null || !GameManager.Instance.IsReady) yield return null;
            var harmony = new Harmony(Owner);
            try
            {
                var batch = typeof(PlayerEntity).GetMethod("SpawnCityGuards", new[] { typeof(bool) });
                var single = typeof(PlayerEntity).GetMethod("SpawnCityGuard", new[] { typeof(Vector3), typeof(Vector3) });
                if (batch == null || single == null) throw new InvalidOperationException("Missing guard methods.");
                harmony.Patch(batch, new HarmonyMethod(typeof(GuardProbe).GetMethod("BatchPrefix")));
                harmony.Patch(single, new HarmonyMethod(typeof(GuardProbe).GetMethod("SinglePrefix")));
                sentinel = new GameObject("Guard probe return sentinel");
                var player = GameManager.Instance.PlayerEntity;
                player.SpawnCityGuards(true);
                player.SpawnCityGuards(false);
                var returned = player.SpawnCityGuard(Vector3.zero, Vector3.forward);
                if (batchCalls != 2 || singleCalls != 1 || returned != sentinel)
                    throw new InvalidOperationException("Guard prefixes failed to intercept calls or return the supplied actor.");
                harmony.UnpatchAll(Owner);
                var batchInfo = Harmony.GetPatchInfo(batch);
                var singleInfo = Harmony.GetPatchInfo(single);
                if ((batchInfo != null && batchInfo.Owners.Contains(Owner)) || (singleInfo != null && singleInfo.Owners.Contains(Owner)))
                    throw new InvalidOperationException("Probe patches were not removed.");
                Debug.Log("LIVING_CITIES_GUARD_PROBE_PASS: batch immediate/deferred and single-return interception; patches removed. No roster or gameplay claim.");
            }
            catch (Exception ex) { Debug.LogError("LIVING_CITIES_GUARD_PROBE_FAIL: " + ex); }
            finally
            {
                harmony.UnpatchAll(Owner);
                if (sentinel != null) Destroy(sentinel);
                Destroy(gameObject);
            }
        }
        public static bool BatchPrefix() { batchCalls++; return false; }
        public static bool SinglePrefix(ref GameObject __result) { singleCalls++; __result = sentinel; return false; }
    }
}
