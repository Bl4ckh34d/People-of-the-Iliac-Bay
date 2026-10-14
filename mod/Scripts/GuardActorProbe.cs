using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Utility;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public static class GuardActorProbe
    {
        public static IEnumerator Run()
        {
            var game = GameManager.Instance;
            var adapter = UnityEngine.Object.FindObjectOfType<LivingCitiesMod>();
            var snapshot = (WorldState)adapter.GetSaveData();
            List<SavedRoutePoint> route = null;
            foreach (var town in snapshot.Routines)
                foreach (var person in town.Residents)
                    if (person.Route.Count > 5) { route = person.Route; break; }
            if (route == null) throw new InvalidOperationException("Guard actor probe needs a surveyed street route.");
            var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            var geometry = new TownGeometry(directory, game.StreamingWorld.GetCurrentCityNavigation(), false);
            var roster = GuardRoster.Create(directory.MapID, 42, 1, new[] { route[0] });
            var type = typeof(LivingCitiesMod).Assembly.GetType("LivingCities.DFU.GuardActor");
            if (type == null) throw new InvalidOperationException("Physical guard actor is missing.");
            var create = type.GetMethod("Create", BindingFlags.Public | BindingFlags.Static);
            var place = type.GetMethod("Place");
            uint originalSeed = DFRandom.Seed;
            var originalRandom = UnityEngine.Random.state;
            var actor = (MonoBehaviour)create.Invoke(null, new object[] { roster, roster.Guards[0].Id, directory.transform, geometry, 100d });
            try
            {
                if (DFRandom.Seed != originalSeed || !UnityEngine.Random.state.Equals(originalRandom))
                    throw new InvalidOperationException("Guard materialization changed shared random state.");
                yield return null;
                var torch = type.GetMethod("SetTorch");
                if (torch == null) throw new InvalidOperationException("Guard torch presentation is missing.");
                torch.Invoke(actor, new object[] { true, true, 8f });
                yield return null;
                var light = actor.GetComponentInChildren<Light>();
                var flame = actor.GetComponentInChildren<DaggerfallBillboard>();
                if (!light || !light.enabled || light.type != LightType.Point || light.shadows != LightShadows.None ||
                    Math.Abs(light.range - 8) > 0.01 || !flame || flame.Summary.Archive != 210 || flame.Summary.Record != 17)
                    throw new InvalidOperationException("Guard torch lacks classic flame or bounded real light.");
                torch.Invoke(actor, new object[] { true, false, 8f });
                if (light.enabled || !flame.gameObject.activeInHierarchy)
                    throw new InvalidOperationException("Light budget must disable lighting without hiding the carried torch.");
                torch.Invoke(actor, new object[] { false, false, 8f });
                if (flame.gameObject.activeInHierarchy) throw new InvalidOperationException("Disabled torch remained visible.");
                torch.Invoke(actor, new object[] { true, true, 8f });
                var entity = actor.GetComponent<DaggerfallEntityBehaviour>();
                var mobile = actor.GetComponentInChildren<MobileUnit>();
                if (entity == null || mobile == null || actor.GetComponent<DaggerfallEnemy>().LoadID != 0 ||
                    actor.GetComponent<EnemyMotor>().enabled || actor.GetComponent<EnemySenses>().enabled)
                    throw new InvalidOperationException("Guard actor must retain native body without competing AI or native persistence.");
                Vector3 start = actor.transform.position;
                roster.Dispatch("actor-test", 100, new Dictionary<string, List<SavedRoutePoint>> { { roster.Guards[0].Id, route } }, 1, 2, 12);
                double middle = (100 + roster.Guards[0].ArrivesAt) / 2;
                place.Invoke(actor, new object[] { middle });
                var expected = roster.Position(roster.Guards[0].Id, middle);
                var actual = directory.transform.InverseTransformPoint(actor.transform.position);
                if (Math.Abs(actual.x - expected.X) > 0.05 || Math.Abs(actual.z - expected.Z) > 0.05 ||
                    Vector3.Distance(start, actor.transform.position) < 1 || mobile.EnemyState != MobileStates.Move)
                    throw new InvalidOperationException("Guard failed to follow the timed route with walking animation.");
                var frames = new HashSet<int>();
                var torchFrames = new HashSet<int>();
                float until = Time.realtimeSinceStartup + 1;
                while (Time.realtimeSinceStartup < until)
                {
                    place.Invoke(actor, new object[] { middle });
                    frames.Add(((DaggerfallMobileUnit)mobile).CurrentFrame);
                    torchFrames.Add(flame.Summary.CurrentFrame);
                    yield return null;
                }
                if (frames.Count < 2) throw new InvalidOperationException("Guard walking sprite did not animate across rendered frames.");
                if (torchFrames.Count < 2) throw new InvalidOperationException("Carried torch flame did not animate across rendered frames.");
                if (game.PlayerEntity.CrimeCommitted != PlayerEntity.Crimes.None)
                    throw new InvalidOperationException("Guard lifecycle test requires a crime-free fixture.");
                var vanilla = GameObjectHelper.CreateFoeGameObjects(start, MobileTypes.Knight_CityWatch)[0];
                var vanillaEntity = vanilla.GetComponent<DaggerfallEntityBehaviour>();
                vanillaEntity.Entity.Update(vanillaEntity);
                yield return null;
                if (vanilla) { UnityEngine.Object.Destroy(vanilla); throw new InvalidOperationException("Unowned vanilla guard cleanup was suppressed."); }
                if (!actor) throw new InvalidOperationException("Owned guard despawned without a crime.");
                entity.Entity.SetHealth(0);
                yield return null;
                if (roster.Guards[0].IsAlive) throw new InvalidOperationException("Native guard death did not reach the roster.");
                if (light.enabled || flame.gameObject.activeInHierarchy) throw new InvalidOperationException("Dead guard retained a burning torch.");
                string json = SaveLoadManager.Serialize(typeof(GuardRoster), roster);
                var restored = GuardRoster.CopyValidated((GuardRoster)SaveLoadManager.Deserialize(typeof(GuardRoster), json));
                if (create.Invoke(null, new object[] { restored, restored.Guards[0].Id, directory.transform, geometry, middle }) != null)
                    throw new InvalidOperationException("Dead guard materialized after reload.");
                Debug.Log("LIVING_CITIES_TOWN_GUARD_ACTOR_PASS: native body, timed route, walking animation, native death and no resurrection.");
                Debug.Log("LIVING_CITIES_GUARD_TORCH_PASS: classic carried flame, point light, no shadows, budget/disable controls and death cleanup.");
            }
            finally { if (actor) UnityEngine.Object.Destroy(actor.gameObject); }
        }
    }
}
