using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public static class GuardWitnessProbe
    {
        public static IEnumerator Run()
        {
            var game = GameManager.Instance;
            var adapter = UnityEngine.Object.FindObjectOfType<LivingCitiesMod>();
            var streets = adapter.GetComponent<StreetPopulation>();
            var guards = adapter.GetComponent<GuardPopulation>();
            var flag = typeof(LivingCitiesMod).GetField("exteriorGuardsEnabled", BindingFlags.Instance | BindingFlags.NonPublic);
            bool previous = (bool)flag.GetValue(adapter), moving = streets.enabled;
            flag.SetValue(adapter, true); streets.ReactionsEnabled = true;
            try
            {
                var clock = DaggerfallUnity.Instance.WorldTime.Now;
                clock.RaiseTime((86400 + 12 * 3600 - (int)(clock.ToSeconds() % 86400)) % 86400);
                float deadline = Time.realtimeSinceStartup + 30;
                while ((guards.Roster == null || guards.Roster.BuildingApproaches.Count == 0) && Time.realtimeSinceStartup < deadline) yield return null;
                if (guards.Roster == null || guards.Roster.BuildingApproaches.Count == 0) throw new InvalidOperationException("Witness probe needs surveyed guard routes.");
                streets.enabled = false;
                var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
                var geometry = new TownGeometry(directory, game.StreamingWorld.GetCurrentCityNavigation(), false);
                SavedRoutePoint crime = null;
                foreach (var approach in guards.Roster.BuildingApproaches)
                    foreach (var route in approach.Routes)
                        foreach (var point in route.Points)
                        {
                            bool distant = true;
                            foreach (var guard in guards.Roster.Guards)
                                if (Vector2.Distance(new Vector2((float)point.X, (float)point.Z), new Vector2((float)guard.Post.X, (float)guard.Post.Z)) < 35) { distant = false; break; }
                            if (distant && geometry.RouteBetweenPoints(guards.Roster.Guards[0].Post, point).Count >= 2) { crime = point; break; }
                        }
                if (crime == null) throw new InvalidOperationException("No reachable crime fixture away from guard posts.");
                var population = (SettlementPopulation)typeof(LivingCitiesMod).GetField("current", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(adapter);
                var actors = (Dictionary<string, ResidentActor>)typeof(StreetPopulation).GetField("actors", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(streets);
                foreach (var actor in actors.Values) if (actor) UnityEngine.Object.Destroy(actor.gameObject);
                actors.Clear();
                var fixtures = new List<ResidentActor>();
                foreach (var home in population.Households)
                    foreach (var resident in home.Residents)
                    {
                        if (fixtures.Count >= 2 || !resident.IsAlive || resident.AgeGroup != ResidentAgeGroup.Adult) continue;
                        var body = UnityEngine.Object.Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject, directory.transform);
                        var actor = body.AddComponent<ResidentActor>();
                        actor.Initialize(resident, home.SurnameSeed, game.StreamingWorld.GetCurrentCityNavigation());
                        actor.Place(geometry.ScenePoint(new RoutePoint(crime.X, crime.Z)), Vector3.forward, true);
                        body.SetActive(true); actors.Add(resident.Id, actor); fixtures.Add(actor);
                    }
                if (fixtures.Count != 2) throw new InvalidOperationException("Need two adult residents.");
                var victim = fixtures[0]; var witness = fixtures[1];
                game.PlayerMotor.transform.position = victim.transform.position + Vector3.right * 2;
                game.WeaponManager.WeaponDamage(null, false, false, victim.transform, victim.transform.position, Vector3.forward);
                if (victim.Resident.IsAlive || guards.Roster.Reports.Count != 0)
                    throw new InvalidOperationException("Unreported exterior killing triggered immediate guards.");
                var hook = Type.GetType("LivingCities.QuestHooks.NativeCivilianHooks, LivingCities.QuestHooks", true);
                if (hook.GetProperty("CurrentTarget").GetValue(null, null) != null) throw new InvalidOperationException("Weapon context leaked after native hit.");
                bool failed = false;
                try { game.WeaponManager.WeaponDamage(null, false, false, null, Vector3.zero, Vector3.forward); }
                catch (NullReferenceException) { failed = true; }
                if (!failed || hook.GetProperty("CurrentTarget").GetValue(null, null) != null) throw new InvalidOperationException("Weapon exception did not restore context.");
                var store = (WorldStateStore)typeof(LivingCitiesMod).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(adapter);
                double now = clock.ToSeconds();
                var alarm = store.ActiveAlarm(witness.Resident.Id, now);
                if (alarm == null) throw new InvalidOperationException("Visible citizen did not witness killing.");
                alarm.People.Find(p => p.ResidentId == witness.Resident.Id).Reaction = CivilianReaction.Flee;
                var post = guards.Roster.Guards[0].Post;
                witness.Place(geometry.ScenePoint(new RoutePoint(post.X, post.Z)), Vector3.forward, true);
                var responses = (CivilianResponses)typeof(StreetPopulation).GetField("responses", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(streets);
                // The live controller may have polled earlier in this frame. Respect its warning cadence.
                yield return new WaitForSecondsRealtime(0.3f);
                responses.WarnNearby(actors.Values, now, -1, ignored => false);
                var report = guards.Roster.Reports.Find(r => r.Id == "civilian:" + alarm.Id);
                if (report == null || report.GuardIds.Count < 1 || report.GuardIds.Count > 4) throw new InvalidOperationException("Nearby guard did not receive witness report.");
                foreach (var id in report.GuardIds)
                {
                    var route = guards.Roster.Guards.Find(g => g.Id == id).Route;
                    var end = route[route.Count - 1];
                    if (Math.Abs(end.X - crime.X) > 0.01 || Math.Abs(end.Z - crime.Z) > 0.01) throw new InvalidOperationException("Guard learned current player position instead of crime location.");
                }
                guards.ReportCivilianCrime(alarm, witness.transform.position);
                if (guards.Roster.Reports.Count != 1 || alarm.People[0].Warned.Count != 0) throw new InvalidOperationException("Report duplicated or spent citizen warning budget.");
                string json = SaveLoadManager.Serialize(typeof(WorldState), adapter.GetSaveData());
                var restored = new WorldStateStore(0); restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState), json));
                if (!restored.ActiveAlarm(witness.Resident.Id, now).HasLocation || restored.GetGuards(directory.MapID).Reports.Count != 1)
                    throw new InvalidOperationException("Native serialization lost crime origin or spent report.");
                Debug.Log("LIVING_CITIES_TOWN_GUARD_WITNESS_PASS: no automatic exterior alarm, direct witness, nearby guard report, fixed crime destination, bounded dispatch and native persistence.");
            }
            finally { streets.enabled = moving; guards.Clear(); flag.SetValue(adapter, previous); game.PlayerEntity.CrimeCommitted = DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.None; }
        }
    }
}
