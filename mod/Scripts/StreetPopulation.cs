using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public sealed class StreetPopulation : MonoBehaviour
    {
        private sealed class Plan
        {
            public Resident Resident;
            public uint SurnameSeed;
            public DailySchedule Schedule;
            public ResidentRoutine Routine;
        }
        private BuildingDirectory directory;
        private CityNavigation navigation;
        private TownGeometry geometry;
        private PopulationManager vanilla;
        private bool vanillaWasEnabled, ownsVanilla;
        private readonly Dictionary<GameObject, bool> hiddenVanilla = new Dictionary<GameObject, bool>();
        private readonly List<Plan> plans = new List<Plan>();
        private readonly Dictionary<string, ResidentActor> actors = new Dictionary<string, ResidentActor>();
        private Coroutine building;
        private bool needsExterior;
        private DaggerfallInterior interior;
        private InteriorGeometry indoorGeometry;
        private readonly Dictionary<string, int> indoorSlots = new Dictionary<string, int>();
        private SettlementPopulation population;
        private SleepingArt sleepingArt;
        private CivilianResponses responses;
        private IDisposable reactionHook;
        public bool ReactionsEnabled { get; set; }
        public void InstallReactionHook()
        {
            var type = Type.GetType("LivingCities.QuestHooks.NativeCivilianHooks, LivingCities.QuestHooks", true);
            reactionHook = (IDisposable)Activator.CreateInstance(type, new object[] { new Action<Transform>(BeforeWeaponHit) });
        }
        private void BeforeWeaponHit(Transform target)
        {
            if (!ReactionsEnabled || !Ready || responses == null || !target) return;
            var actor = target.GetComponent<ResidentActor>();
            if (!actor || !actor.Resident.IsAlive || FindActor(actor.Resident.Id) != actor) return;
            double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            responses.Murder(actor, actors.Values, id => GetState(id, now).Activity == Activity.Sleep, now,
                interior ? interior.EntryDoor.buildingKey : -1);
        }
        private AlarmPerson Reaction(string id, double now)
        { return ReactionsEnabled && responses != null ? responses.Active(id, now) : null; }
        public bool IsAlarmed(string id)
        { return Reaction(id, DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()) != null; }
        private readonly Dictionary<int, BedAnchor> indoorBeds = new Dictionary<int, BedAnchor>();
        private readonly Dictionary<string, BedAssignment> bedClaims = new Dictionary<string, BedAssignment>();
        public void SetSleepingArt(Mod mod) { if (sleepingArt != null) sleepingArt.Dispose(); sleepingArt = new SleepingArt(mod); }
        public int ActiveCount { get { return actors.Count; } }
        public int PlannedCount { get { return plans.Count; } }
        public bool Ready { get; private set; }
        public bool SleepEnabled { get; set; }
        public Func<string, bool> QuestOwnsResident { get; set; }
        private bool QuestOwned(Resident resident) { return QuestOwnsResident != null && QuestOwnsResident(resident.Id); }
        public bool TrySample(double seconds, out string id, out Vector3 ground)
        {
            foreach (var plan in plans)
            {
                var state = plan.Schedule.EvaluateSeconds(seconds);
                if (!plan.Resident.IsAlive || state.Activity != Activity.Travel || state.RouteProgress < 0.1 || state.RouteProgress > 0.9 ||
                    plan.Schedule.EvaluateSeconds(seconds + 60).Activity != Activity.Travel) continue;
                id = plan.Resident.Id; ground = geometry.ScenePoint(state.Position); return true;
            }
            id = null; ground = Vector3.zero; return false;
        }
        public ResidentActor FindActor(string id) { ResidentActor actor; return actors.TryGetValue(id, out actor) ? actor : null; }
        public static bool IsVisitDestination(DFLocation.BuildingTypes buildingType)
        {
            int type = (int)buildingType;
            return type >= 0 && type < PlayerActivate.openHours.Length &&
                Math.Max(9, (int)PlayerActivate.openHours[type]) < Math.Min(18, (int)PlayerActivate.closeHours[type]) &&
                buildingType != DFLocation.BuildingTypes.HouseForSale &&
                buildingType != DFLocation.BuildingTypes.Palace &&
                buildingType != DFLocation.BuildingTypes.GuildHall &&
                buildingType != DFLocation.BuildingTypes.Town4;
        }
        public ActivitySnapshot GetState(string id, double seconds)
        {
            var plan = plans.Find(item => item.Resident.Id == id);
            if (plan == null) throw new ArgumentException("Unknown resident schedule.");
            return plan.Schedule.EvaluateSeconds(seconds);
        }
        public void SetTown(BuildingDirectory town, CityNavigation nav, SettlementPopulation population, WorldStateStore store, int wake, int sleep)
        {
            // M2 preview is deliberately limited to the verified Breton test town.
            if (town.MapID != 206728581) { Clear(); return; }
            if (directory == town && !(needsExterior && !GameManager.Instance.PlayerEnterExit.IsPlayerInside)) return;
            Clear(); directory = town; navigation = nav; this.population = population;
            responses = new CivilianResponses(store, population.SettlementId, (alarm, position) => {
                var guards = GetComponent<GuardPopulation>();
                if (guards) guards.ReportCivilianCrime(alarm, position);
            });
            building = StartCoroutine(Build(population, store, wake, sleep));
        }
        private IEnumerator Build(SettlementPopulation population, WorldStateStore store, int wake, int sleep)
        {
            var saved = store.GetRoutines(population.SettlementId);
            if (saved != null)
            {
                geometry = new TownGeometry(directory, navigation, false);
                foreach (var home in population.Households)
                    foreach (var resident in home.Residents)
                    {
                        if (resident.IsQuestProtected) continue;
                        var routine = saved.Residents.Find(item => item.ResidentId == resident.Id) ?? new ResidentRoutine { ResidentId = resident.Id };
                        AddPlan(resident, routine, home.SurnameSeed, wake, sleep);
                    }
                FinishBuild(population.SettlementId);
                yield break;
            }
            // Old interior saves have no routes yet. Display household residents, then
            // perform the first exterior survey after the player steps outside.
            if (GameManager.Instance.PlayerEnterExit.IsPlayerInside)
            {
                needsExterior = true;
                foreach (var home in population.Households)
                    foreach (var resident in home.Residents)
                        if (!resident.IsQuestProtected) AddPlan(resident, new ResidentRoutine { ResidentId = resident.Id }, home.SurnameSeed, wake, sleep);
                Ready = true; building = null;
                yield break;
            }
            // CityNavigation resolves its location reference in Start, after activation.
            yield return null;
            geometry = new TownGeometry(directory, navigation);
            saved = new TownRoutineState { SettlementId = population.SettlementId };
            var destinations = new List<BuildingSummary>();
            foreach (var pair in geometry.Entrances)
            {
                BuildingSummary summary;
                if (!directory.GetBuildingSummary(pair.Key, out summary)) continue;
                if (IsVisitDestination(summary.BuildingType))
                    destinations.Add(summary);
            }
            var cache = new Dictionary<string, List<RoutePoint>>();
            foreach (var home in population.Households)
            {
                TownGeometry.Entrance entrance;
                geometry.Entrances.TryGetValue(home.BuildingKey, out entrance);
                foreach (var resident in home.Residents)
                {
                    if (resident.IsQuestProtected) continue;
                    var routine = new ResidentRoutine { ResidentId = resident.Id };
                    int first = destinations.Count == 0 ? 0 : (int)(resident.AppearanceSeed % (uint)destinations.Count);
                    for (int attempt = 0; resident.AgeGroup == ResidentAgeGroup.Adult && entrance != null && attempt < Math.Min(4, destinations.Count); attempt++)
                    {
                        // Physics queries are valid only while exterior geometry is active.
                        while (GameManager.Instance.PlayerEnterExit.IsPlayerInside) yield return null;
                        var destination = destinations[(first + attempt) % destinations.Count];
                        if (destination.buildingKey == home.BuildingKey) continue;
                        string key = home.BuildingKey + ":" + destination.buildingKey;
                        List<RoutePoint> route;
                        if (!cache.TryGetValue(key, out route))
                        {
                            route = geometry.RouteBetween(entrance, geometry.Entrances[destination.buildingKey]);
                            cache.Add(key, route);
                        }
                        if (route.Count < 2) continue;
                        int type = (int)destination.BuildingType;
                        routine.DestinationKey = destination.buildingKey;
                        routine.OpenMinute = Math.Max(9 * 60, PlayerActivate.openHours[type] * 60);
                        routine.CloseMinute = Math.Min(18 * 60, PlayerActivate.closeHours[type] * 60);
                        foreach (var point in route) routine.Route.Add(new SavedRoutePoint { X = point.X, Z = point.Z });
                        break;
                    }
                    saved.Residents.Add(routine);
                    AddPlan(resident, routine, home.SurnameSeed, wake, sleep);
                }
                // Bounded incremental layout work avoids a single long main-thread stall.
                yield return null;
            }
            store.SetRoutines(saved);
            FinishBuild(population.SettlementId);
        }
        private void AddPlan(Resident resident, ResidentRoutine routine, uint surnameSeed, int wake, int sleep)
        {
            var points = new List<RoutePoint>();
            foreach (var point in routine.Route) points.Add(new RoutePoint(point.X, point.Z));
            var schedule = routine.DestinationKey < 0 ? DailySchedule.HomeDay(resident.HomeKey, wake * 60, sleep * 60) :
                DailySchedule.DayVisit(resident.HomeKey, routine.DestinationKey, points, routine.OpenMinute, routine.CloseMinute,
                    20 + (int)(resident.AppearanceSeed % 41), 1.3, 12, resident.AppearanceSeed, wake * 60, sleep * 60);
            plans.Add(new Plan { Resident = resident, Schedule = schedule, Routine = routine,
                SurnameSeed = surnameSeed });
        }
        private void FinishBuild(int settlementId)
        {
            vanilla = directory.GetComponent<PopulationManager>();
            if (plans.Count > 0 && vanilla)
            {
                vanillaWasEnabled = vanilla.enabled;
                if (vanillaWasEnabled)
                {
                    vanilla.enabled = false; ownsVanilla = true;
                    foreach (var item in vanilla.PopulationPool)
                        if (item.npc && !item.npc.IsGuard)
                        {
                            hiddenVanilla[item.npc.gameObject] = item.npc.gameObject.activeSelf;
                            item.npc.gameObject.SetActive(false);
                        }
                }
            }
            Ready = true; building = null;
            Debug.Log("LIVING_CITIES_STREETS_READY: residentsWithSchedules=" + plans.Count + "; map=" + settlementId);
        }
        private void Update()
        {
            if (!directory) { if (plans.Count > 0) Clear(); return; }
            var game = GameManager.Instance;
            if (!Ready || game == null || !game.IsReady || !game.IsPlayingGame()) return;
            var now = DaggerfallUnity.Instance.WorldTime.Now;
            double seconds = now.ToSeconds() + (now.Second - Math.Floor(now.Second));
            var loadedInterior = game.PlayerEnterExit.IsPlayerInsideBuilding ? game.PlayerEnterExit.Interior : null;
            if (interior != loadedInterior)
            {
                ClearActors(); interior = loadedInterior; indoorSlots.Clear();
                indoorGeometry = interior ? new InteriorGeometry(interior) : null;
                indoorBeds.Clear(); bedClaims.Clear();
                if (interior && population != null)
                {
                    var home = population.Households.Find(h => h.BuildingKey == interior.EntryDoor.buildingKey);
                    if (home != null)
                    {
                        foreach (var bed in BedGeometry.Read(interior)) indoorBeds.Add(bed.RecordIndex, bed);
                        foreach (var claim in HomeBeds.Assign(home, indoorBeds.Keys)) bedClaims.Add(claim.ResidentId, claim);
                    }
                }
                if (interior) Debug.Log("LIVING_CITIES_INTERIOR_ANCHORS: building=" + interior.EntryDoor.buildingKey +
                    "; anchors=" + indoorGeometry.LocalAnchors.Count);
            }
            if (ReactionsEnabled && responses != null)
                responses.WarnNearby(actors.Values, seconds, interior ? interior.EntryDoor.buildingKey : -1,
                    id => GetState(id, seconds).Activity == Activity.Sleep);
            if (game.PlayerEnterExit.IsPlayerInside)
            {
                if (interior) UpdateInterior(seconds); else ClearActors();
                return;
            }
            Vector3 player = game.PlayerMotor.transform.position;
            foreach (var plan in plans)
            {
                var state = plan.Schedule.EvaluateSeconds(seconds);
                ResidentActor actor;
                actors.TryGetValue(plan.Resident.Id, out actor);
                var reaction = Reaction(plan.Resident.Id, seconds);
                Vector3 position = state.Activity == Activity.Travel ? geometry.ScenePoint(state.Position) : Vector3.zero;
                if (reaction != null && reaction.HasPosition)
                    position = directory.transform.TransformPoint(new Vector3((float)reaction.X, (float)reaction.Y, (float)reaction.Z));
                float limit = actor ? 165 : 140;
                bool outside = reaction != null && reaction.HasPosition ? reaction.BuildingKey == -1 && !reaction.ReachedRefuge : state.Activity == Activity.Travel;
                bool visible = plan.Resident.IsAlive && !QuestOwned(plan.Resident) && outside &&
                    (position - player).sqrMagnitude <= limit * limit;
                if (!visible)
                {
                    if (actor) { actor.gameObject.SetActive(false); Destroy(actor.gameObject); }
                    actors.Remove(plan.Resident.Id); continue;
                }
                if (!actor)
                {
                    if (actors.Count >= 80) continue;
                    var go = Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject, directory.transform);
                    go.name = "Living Cities " + plan.Resident.Id;
                    actor = go.AddComponent<ResidentActor>();
                    actor.Initialize(plan.Resident, plan.SurnameSeed, navigation);
                    actors.Add(plan.Resident.Id, actor);
                    go.SetActive(true);
                }
                if (reaction != null && reaction.HasPosition)
                {
                    CivilianResponses.Move(actor, reaction, plan.Routine, directory.transform, geometry, null);
                    continue;
                }
                var next = plan.Schedule.EvaluateSeconds(seconds + 0.1);
                Vector3 direction = next.Activity == Activity.Travel ? geometry.ScenePoint(next.Position) - position :
                    position - geometry.ScenePoint(plan.Schedule.EvaluateSeconds(Math.Max(0, seconds - 0.1)).Position);
                actor.Place(position, direction);
            }
        }
        private void UpdateInterior(double seconds)
        {
            foreach (var plan in plans)
            {
                var state = plan.Schedule.EvaluateSeconds(seconds);
                ResidentActor actor; actors.TryGetValue(plan.Resident.Id, out actor);
                var reaction = Reaction(plan.Resident.Id, seconds);
                bool atHome = reaction != null && reaction.HasPosition ? reaction.BuildingKey == interior.EntryDoor.buildingKey :
                    state.Activity != Activity.Travel && state.BuildingKey == interior.EntryDoor.buildingKey;
                bool present = plan.Resident.IsAlive && !QuestOwned(plan.Resident) && atHome;
                if (!present)
                {
                    if (actor) { actor.gameObject.SetActive(false); Destroy(actor.gameObject); }
                    actors.Remove(plan.Resident.Id); indoorSlots.Remove(plan.Resident.Id); continue;
                }
                int slot;
                if (!indoorSlots.TryGetValue(plan.Resident.Id, out slot))
                {
                    slot = 0;
                    while (slot < indoorGeometry.LocalAnchors.Count && indoorSlots.ContainsValue(slot)) slot++;
                    if (slot == indoorGeometry.LocalAnchors.Count) continue;
                    indoorSlots.Add(plan.Resident.Id, slot);
                }
                if (!actor)
                {
                    var go = Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject, interior.transform);
                    go.name = "Living Cities " + plan.Resident.Id;
                    actor = go.AddComponent<ResidentActor>();
                    actor.Initialize(plan.Resident, plan.SurnameSeed, navigation);
                    actors.Add(plan.Resident.Id, actor); go.SetActive(true);
                }
                if (reaction != null && reaction.HasPosition && !reaction.ReachedRefuge)
                {
                    CivilianResponses.Move(actor, reaction, plan.Routine, interior.transform, geometry, indoorGeometry);
                    continue;
                }
                BedAssignment bedClaim;
                var art = reaction == null && SleepEnabled && state.Activity == Activity.Sleep && sleepingArt != null &&
                    bedClaims.TryGetValue(plan.Resident.Id, out bedClaim) && bedClaim.SharingCount == 1 ?
                    sleepingArt.ForResident(plan.Resident) : null;
                if (art != null)
                {
                    bedClaim = bedClaims[plan.Resident.Id];
                    actor.Sleep(interior.transform, indoorBeds[bedClaim.BedRecordIndex], art);
                }
                else actor.Place(interior.transform.TransformPoint(indoorGeometry.LocalAnchors[slot]), Vector3.forward, true);
            }
        }
        private void ClearActors()
        {
            foreach (var actor in actors.Values) if (actor) { actor.gameObject.SetActive(false); Destroy(actor.gameObject); }
            actors.Clear();
        }
        public void Clear()
        {
            if (building != null) { StopCoroutine(building); building = null; }
            ClearActors(); plans.Clear(); Ready = false;
            needsExterior = false;
            interior = null; indoorGeometry = null; indoorSlots.Clear();
            population = null; indoorBeds.Clear(); bedClaims.Clear();
            responses = null;
            if (ownsVanilla && vanilla)
            {
                foreach (var pair in hiddenVanilla) if (pair.Key) pair.Key.SetActive(pair.Value);
                vanilla.enabled = vanillaWasEnabled;
            }
            hiddenVanilla.Clear(); ownsVanilla = false; vanilla = null; directory = null; geometry = null;
        }
        private void OnDestroy() { Clear(); if (sleepingArt != null) sleepingArt.Dispose(); if (reactionHook != null) reactionHook.Dispose(); }
    }
}
