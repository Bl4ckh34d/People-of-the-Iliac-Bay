using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public static class GuardWitnessProbe
    {
        public static IEnumerator Run()
        {
            var game = GameManager.Instance;
            var adapter = UnityEngine.Object.FindObjectOfType<PeopleOfTheIliacBayMod>();
            var streets = adapter.GetComponent<StreetPopulation>();
            var guards = adapter.GetComponent<GuardPopulation>();
            var flag = typeof(PeopleOfTheIliacBayMod).GetField("exteriorGuardsEnabled", BindingFlags.Instance | BindingFlags.NonPublic);
            bool previous = (bool)flag.GetValue(adapter), moving = streets.enabled;
            flag.SetValue(adapter, true); streets.ReactionsEnabled = true;
            try
            {
                var clock = DaggerfallUnity.Instance.WorldTime.Now;
                clock.RaiseTime((86400 + 12 * 3600 - (int)(clock.ToSeconds() % 86400)) % 86400);
                var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
                var geometry = new TownGeometry(directory, game.StreamingWorld.GetCurrentCityNavigation(), false);
                var approachSurveyField = typeof(GuardPopulation).GetField("approachSurvey", BindingFlags.Instance | BindingFlags.NonPublic);
                float deadline = Time.realtimeSinceStartup + 120;
                while ((guards.Roster == null || (Coroutine)approachSurveyField.GetValue(guards) != null) && Time.realtimeSinceStartup < deadline)
                    yield return null;
                if (guards.Roster == null || (Coroutine)approachSurveyField.GetValue(guards) != null || guards.Roster.BuildingApproaches.Count == 0)
                    throw new InvalidOperationException("Witness probe needs completed guard approach survey.");
                streets.enabled = false;
                double now = clock.ToSeconds();
                SavedRoutePoint crime = null, nearPost = null;
                var nearPostPosition = Vector3.zero;
                foreach (var approach in guards.Roster.BuildingApproaches)
                    foreach (var route in approach.Routes)
                    {
                        var routeGuard = guards.Roster.Guards.Find(g => g.Id == route.GuardId && g.IsAlive && g.Duty == GuardDuty.Posted);
                        if (routeGuard == null || route.Points == null || route.Points.Count < 2) continue;
                        var current = guards.Roster.Position(routeGuard.Id, now);
                        if (Vector2.Distance(new Vector2((float)current.X, (float)current.Z),
                            new Vector2((float)routeGuard.Post.X, (float)routeGuard.Post.Z)) > 0.05f) continue;
                        foreach (var point in route.Points)
                        {
                            var scenePoint = geometry.ScenePoint(new RoutePoint(point.X, point.Z));
                            var standing = geometry.ScenePoint(new RoutePoint(current.X, current.Z)) + Vector3.up;
                            float distance = Vector2.Distance(new Vector2((float)point.X, (float)point.Z),
                                new Vector2((float)current.X, (float)current.Z));
                            if (nearPost == null && distance > 0.5f && distance < 20 &&
                                CivilianResponses.Sees(scenePoint, standing, 20) &&
                                geometry.RouteBetweenPoints(current, point).Count >= 2)
                            { nearPost = point; nearPostPosition = scenePoint; }
                            if (crime == null)
                            {
                                bool outsideReportRange = true;
                                foreach (var guard in guards.Roster.Guards)
                                {
                                    if (!guard.IsAlive || guard.Duty != GuardDuty.Posted) continue;
                                    var posted = guards.Roster.Position(guard.Id, now);
                                    if (Vector2.Distance(new Vector2((float)point.X, (float)point.Z),
                                        new Vector2((float)posted.X, (float)posted.Z)) <= 20)
                                    { outsideReportRange = false; break; }
                                }
                                if (outsideReportRange) crime = point;
                            }
                            if (crime != null && nearPost != null) break;
                        }
                        if (crime != null && nearPost != null) break;
                    }
                if (crime == null || nearPost == null)
                    throw new InvalidOperationException("Completed guard routes contain no reachable remote and near-post witness fixtures.");
                var population = (SettlementPopulation)typeof(PeopleOfTheIliacBayMod).GetField("current", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(adapter);
                var actors = (Dictionary<string, ResidentActor>)typeof(StreetPopulation).GetField("actors", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(streets);
                var store = (WorldStateStore)typeof(PeopleOfTheIliacBayMod).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(adapter);
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
                Resident preHitResident = null;
                foreach (var home in population.Households)
                    foreach (var resident in home.Residents)
                        if (resident.IsAlive && !fixtures.Exists(actor => actor.Resident.Id == resident.Id)) { preHitResident = resident; break; }
                if (preHitResident == null) throw new InvalidOperationException("Need a separate living resident for the pre-death report assertion.");
                var preHit = store.RecordViolence(population.SettlementId, preHitResident.Id, now);
                var nearLocal = directory.transform.InverseTransformPoint(nearPostPosition);
                store.LocateViolence(preHit.Id, -1, nearLocal.x, nearLocal.y, nearLocal.z);
                var priorCrime = game.PlayerEntity.CrimeCommitted;
                guards.ReportCivilianCrime(preHit, nearPostPosition);
                if (guards.Roster.Reports.Exists(r => r.Id == "civilian:" + preHit.Id) ||
                    game.PlayerEntity.CrimeCommitted != priorCrime)
                    throw new InvalidOperationException("Unconfirmed civilian hit was dispatched as murder despite a visible posted guard.");
                var victim = fixtures[0]; var witness = fixtures[1];
                game.PlayerMotor.transform.position = victim.transform.position + Vector3.right * 2;
                game.WeaponManager.WeaponDamage(null, false, false, victim.transform, victim.transform.position, Vector3.forward);
                if (victim.Resident.IsAlive || guards.Roster.Reports.Count != 0)
                    throw new InvalidOperationException("Unreported exterior killing triggered immediate guards.");
                var hook = Type.GetType("PeopleOfTheIliacBay.QuestHooks.NativeCivilianHooks, PeopleOfTheIliacBay.QuestHooks", true);
                if (hook.GetProperty("CurrentTarget").GetValue(null, null) != null) throw new InvalidOperationException("Weapon context leaked after native hit.");
                bool failed = false;
                try { game.WeaponManager.WeaponDamage(null, false, false, null, Vector3.zero, Vector3.forward); }
                catch (NullReferenceException) { failed = true; }
                if (!failed || hook.GetProperty("CurrentTarget").GetValue(null, null) != null) throw new InvalidOperationException("Weapon exception did not restore context.");
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
                var restored = new WorldStateStore(0,PeopleOfTheIliacBayMod.ConversationArchiveRoot()); restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState), json));
                if (!restored.ActiveAlarm(witness.Resident.Id, now).HasLocation || restored.GetGuards(directory.MapID).Reports.Count != 1)
                    throw new InvalidOperationException("Native serialization lost crime origin or spent report.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_GUARD_WITNESS_PASS: no automatic exterior alarm, direct witness, nearby guard report, fixed crime destination, bounded dispatch and native persistence.");
            }
            finally { streets.enabled = moving; guards.Clear(); flag.SetValue(adapter, previous); game.PlayerEntity.CrimeCommitted = DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.None; }
        }
    }
}
