using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Utility;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    // Physical roster controller for the supported town and surveyed buildings.
    public sealed class GuardPopulation : MonoBehaviour
    {
        public GuardRoster Roster { get; private set; }
        public int MaximumResponders = 4;
        public int ActorRadius = 160;
        public int ActiveCount
        { get { int count = 0; foreach (var pair in actors) if (pair.Value && pair.Value.gameObject.activeInHierarchy) count++; return count; } }
        private BuildingDirectory directory;
        private TownGeometry geometry;
        private IDisposable hooks;
        private string reportId;
        private readonly Dictionary<string, GuardActor> actors = new Dictionary<string, GuardActor>();
        private readonly Dictionary<GameObject, bool> nativeGuards = new Dictionary<GameObject, bool>();
        private float nextRoutes;
        private bool awaitingSight;
        private Coroutine approachSurvey;
        private DaggerfallInterior interior;
        private Vector3? entryGround;
        private bool OwnsTown()
        {
            var game = GameManager.Instance;
            return isActiveAndEnabled && directory && Roster != null && game != null && game.IsPlayingGame() &&
                game.PlayerGPS.HasCurrentLocation && game.PlayerGPS.CurrentLocation.MapTableData.MapId == directory.MapID &&
                (!game.PlayerEnterExit.IsPlayerInside || (game.PlayerEnterExit.IsPlayerInsideBuilding &&
                game.PlayerEnterExit.Interior && Roster.BuildingApproaches.Exists(a => a.BuildingKey == game.PlayerEnterExit.Interior.EntryDoor.buildingKey)));
        }
        public void SetTown(BuildingDirectory town, CityNavigation navigation, SettlementPopulation population, WorldStateStore store)
        {
            if (directory == town && Roster != null) return;
            Clear();
            if (town.MapID != 206728581 || (GameManager.Instance.PlayerEnterExit.IsPlayerInside && store.GetGuards(town.MapID) == null)) return;
            var routines = store.GetRoutines(town.MapID);
            if (routines == null) return;
            var posts = new List<SavedRoutePoint>();
            int residents = 0;
            foreach (var home in population.Households) residents += home.Residents.Count;
            foreach (var routine in routines.Residents)
            {
                if (routine.Route.Count < 2) continue;
                var point = routine.Route[0];
                if (!posts.Exists(p => Math.Abs(p.X - point.X) < 10 && Math.Abs(p.Z - point.Z) < 10))
                    posts.Add(point);
            }
            int count = GuardRoster.TargetCount(SettlementScale.Town, residents);
            if (posts.Count < count) return;
            posts.Sort((a, b) => { int x = a.X.CompareTo(b.X); return x == 0 ? a.Z.CompareTo(b.Z) : x; });
            var selected = new List<SavedRoutePoint>();
            for (int i = 0; i < count; i++) selected.Add(posts[i * posts.Count / count]);
            directory = town; geometry = new TownGeometry(town, navigation, false);
            Roster = store.GetOrCreateGuards(town.MapID, count, selected);
            approachSurvey = StartCoroutine(SurveyApproaches(navigation));
            foreach (var guard in Roster.Guards)
                if (guard.IsAlive && guard.IncidentId != null) { reportId = guard.IncidentId; break; }
            var type = Type.GetType("LivingCities.QuestHooks.NativeGuardHooks, LivingCities.QuestHooks", true);
            hooks = (IDisposable)Activator.CreateInstance(type, new object[] { new Func<bool>(OwnsTown),
                new Action<bool>(ReportNative), new Func<Vector3, Vector3, GameObject>(ResolveNative) });
            // Existing transient municipal reinforcements predate roster ownership.
            // Retire them once; quest-owned actors retain their native ownership.
            foreach (var body in UnityEngine.Object.FindObjectsOfType<DaggerfallEnemy>())
            {
                var entity = body.GetComponent<DaggerfallEntityBehaviour>().Entity as EnemyEntity;
                if (!body.QuestSpawn && !body.GetComponent<GuardActor>() && entity != null &&
                    entity.MobileEnemy.ID == (int)MobileTypes.Knight_CityWatch) Destroy(body.gameObject);
            }
            foreach (var spawner in UnityEngine.Object.FindObjectsOfType<FoeSpawner>())
                if (spawner.FoeType == MobileTypes.Knight_CityWatch) Destroy(spawner.gameObject);
            Debug.Log("LIVING_CITIES_GARRISON_READY: " + town.MapID + "; guards=" + Roster.Guards.Count);
        }
        private static double Now()
        {
            var now = DaggerfallUnity.Instance.WorldTime.Now;
            // Second is a float. Promote before addition or the absolute game
            // timestamp (~12 billion seconds) rounds to 1024-second increments.
            return (double)now.ToSeconds() + (double)now.Second - Math.Floor(now.Second);
        }
        private IEnumerator SurveyApproaches(CityNavigation navigation)
        {
            while (GameManager.Instance.PlayerEnterExit.IsPlayerInside || !directory.gameObject.activeInHierarchy)
                yield return null;
            // Door discovery and physics queries must run against the active exterior.
            var survey = new TownGeometry(directory, navigation);
            int completed = 0;
            foreach (var entry in survey.Entrances)
            {
                if (Roster.BuildingApproaches.Exists(a => a.BuildingKey == entry.Key)) continue;
                var routes = new Dictionary<string, List<SavedRoutePoint>>();
                var door = entry.Value.LocalDoor;
                var target = new SavedRoutePoint { X = door.x, Z = door.z };
                // The roster can gain recruits during the coroutine. Use a stable
                // post survey snapshot; replacements can reuse the same post route.
                var guards = new List<GuardRecord>(Roster.Guards);
                foreach (var guard in guards)
                {
                    while (!OwnsTown() || GameManager.Instance.PlayerEnterExit.IsPlayerInside ||
                        !directory.gameObject.activeInHierarchy || GameManager.IsGamePaused)
                        yield return null;
                    var route = survey.RouteBetweenPoints(guard.Post, target);
                    if (route.Count >= 2) routes.Add(guard.Id, route);
                    yield return null;
                }
                if (routes.Count > 0)
                {
                    Roster.CacheBuildingApproach(entry.Key, target, routes);
                    completed++;
                }
            }
            Debug.Log("LIVING_CITIES_GUARD_APPROACHES_READY: buildings=" + Roster.BuildingApproaches.Count + "; added=" + completed);
            approachSurvey = null;
        }
        private SavedRoutePoint Local(Vector3 position)
        { var p = directory.transform.InverseTransformPoint(position); return new SavedRoutePoint { X = p.x, Z = p.z }; }
        private void ReportNative(bool immediate)
        {
            if (!OwnsTown()) return;
            var room = GameManager.Instance.PlayerEnterExit.IsPlayerInsideBuilding ? GameManager.Instance.PlayerEnterExit.Interior : null;
            int? building = room ? (int?)room.EntryDoor.buildingKey : null;
            var currentReport = Roster.Reports.Find(r => r.Id == reportId);
            if (currentReport != null && currentReport.BuildingKey != building) reportId = null;
            if (!immediate)
            {
                bool seen = false;
                foreach (var pair in actors)
                    if (pair.Value && pair.Value.gameObject.activeInHierarchy && !GameManager.Instance.PlayerEntity.IsInvisible &&
                        CivilianResponses.Sees(pair.Value.transform.position, GameManager.Instance.PlayerMotor.transform.position, 20)) { seen = true; break; }
                if (!seen) { awaitingSight = true; return; }
            }
            awaitingSight = false;
            double now = Now();
            if (reportId == null) reportId = "native:" + directory.MapID + ":" + building + ":" + now.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            var previous = Roster.Reports.Find(r => r.Id == reportId);
            if (previous != null && previous.GuardIds.Count > 0)
            {
                if (Roster.Guards.Exists(g => g.IsAlive && g.IncidentId == reportId)) return;
                reportId = "native:" + directory.MapID + ":" + building + ":" + now.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            }
            if (building.HasValue)
            {
                var responders = Roster.DispatchBuilding(reportId, building.Value, now, Mathf.Clamp(MaximumResponders, 1, 8), 2.8, 12);
                Debug.Log("LIVING_CITIES_GUARD_BUILDING_DISPATCH: building=" + building + "; responders=" + responders.Count);
                return;
            }
            var target = Local(GameManager.Instance.PlayerMotor.transform.position);
            var routes = new Dictionary<string, List<SavedRoutePoint>>();
            Roster.Advance(now);
            foreach (var guard in Roster.Guards)
            {
                if (!guard.IsAlive || guard.Duty != GuardDuty.Posted) continue;
                var route = geometry.RouteBetweenPoints(guard.Post, target);
                if (route.Count >= 2) routes.Add(guard.Id, route);
            }
            var sent = Roster.Dispatch(reportId, now, routes, Mathf.Clamp(MaximumResponders, 1, 8), 2.8, 12);
            Debug.Log("LIVING_CITIES_GUARD_DISPATCH: " + reportId + "; responders=" + sent.Count);
        }
        private GameObject ResolveNative(Vector3 position, Vector3 direction)
        {
            GuardRecord nearest = null; double distance = double.MaxValue; double now = Now();
            foreach (var guard in Roster.Guards)
            {
                if (!guard.IsAlive) continue;
                bool indoors = GameManager.Instance.PlayerEnterExit.IsPlayerInsideBuilding;
                if (indoors ? guard.Interior == null || !interior || guard.Interior.BuildingKey != interior.EntryDoor.buildingKey : guard.Interior != null)
                    continue;
                var point = Roster.Position(guard.Id, now);
                var indoor = guard.Interior == null ? null : Roster.InteriorPosition(guard.Id, now);
                var scene = indoor == null ? geometry.ScenePoint(new RoutePoint(point.X, point.Z)) :
                    interior.transform.TransformPoint(new Vector3((float)indoor.X, (float)indoor.Y, (float)indoor.Z));
                double d = (scene - position).sqrMagnitude;
                if (d < distance) { nearest = guard; distance = d; }
            }
            if (nearest == null) throw new InvalidOperationException("No living roster guard exists for a native actor conversion.");
            return View(nearest, now).gameObject;
        }
        private GuardActor View(GuardRecord guard, double now)
        {
            GuardActor actor;
            if (!actors.TryGetValue(guard.Id, out actor) || !actor)
            {
                actor = guard.Interior != null ? GuardActor.CreateInterior(Roster, guard.Id, interior.transform, now) :
                    GuardActor.Create(Roster, guard.Id, directory.transform, geometry, now);
                actors[guard.Id] = actor;
            }
            return actor;
        }
        public void Capture()
        { foreach (var pair in actors) if (pair.Value) pair.Value.Capture(Now()); }
        private void Update()
        {
            if (!OwnsTown()) { HideViews(); return; }
            if (GameManager.IsGamePaused) return;
            var game = GameManager.Instance; double now = Now();
            var loaded = game.PlayerEnterExit.IsPlayerInsideBuilding ? game.PlayerEnterExit.Interior : null;
            if (interior != loaded)
            {
                ClearViews(); interior = loaded;
                entryGround = interior ? new InteriorGeometry(interior).EntryGround : null;
            }
            Roster.Advance(now); Roster.Recruit(now);
            if (game.PlayerEntity.CrimeCommitted == PlayerEntity.Crimes.None) { reportId = null; awaitingSight = false; }
            bool routeTick = Time.unscaledTime >= nextRoutes;
            if (routeTick) nextRoutes = Time.unscaledTime + 1;
            if (awaitingSight && routeTick && game.PlayerEntity.CrimeCommitted != PlayerEntity.Crimes.None) ReportNative(false);
            if (interior) { UpdateInterior(now); return; }
            HideNative();
            foreach (var guard in Roster.Guards)
            {
                if (!guard.IsAlive) continue;
                if (guard.Interior != null) continue;
                var point = Roster.Position(guard.Id, now);
                Vector3 scene = geometry.ScenePoint(new RoutePoint(point.X, point.Z));
                GuardActor actor;
                int radius = Mathf.Clamp(ActorRadius, 64, 256);
                bool close = (scene - game.PlayerMotor.transform.position).sqrMagnitude < radius * radius;
                if (!close && guard.Duty != GuardDuty.Engaged)
                { if (actors.TryGetValue(guard.Id, out actor) && actor) actor.gameObject.SetActive(false); continue; }
                actor = View(guard, now); actor.gameObject.SetActive(true);
                if (guard.Duty != GuardDuty.Engaged && actor.GetComponent<EnemyMotor>().IsHostile &&
                    actor.GetComponent<EnemySenses>().Target == game.PlayerEntityBehaviour)
                {
                    if (guard.IncidentId == null)
                    {
                        string assault = "assault:" + guard.Id + ":" + now.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                        Roster.Dispatch(assault, now, new Dictionary<string, List<SavedRoutePoint>> {
                            { guard.Id, new List<SavedRoutePoint> { guard.Post, guard.Post } } }, 1, 2.8, 12);
                    }
                    if (game.PlayerEntity.CrimeCommitted == PlayerEntity.Crimes.None) game.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.Assault;
                    actor.BeginCombat(now);
                }
                if (guard.Duty == GuardDuty.Engaged)
                {
                    actor.Capture(now);
                    if (now >= guard.SearchUntil || game.PlayerEntity.CrimeCommitted == PlayerEntity.Crimes.None)
                    {
                        actor.EndCombat();
                        if (routeTick)
                        {
                            var route = geometry.RouteBetweenPoints(guard.EngagementPosition, guard.Post);
                            if (route.Count >= 2) Roster.ReturnFromEngagement(guard.Id, now, route, 2.8, 12);
                        }
                    }
                    else actor.BeginCombat(now);
                }
                else
                {
                    actor.Place(now);
                    if ((guard.Duty == GuardDuty.Responding || guard.Duty == GuardDuty.Searching) &&
                        game.PlayerEntity.CrimeCommitted != PlayerEntity.Crimes.None && !game.PlayerEntity.IsInvisible &&
                        CivilianResponses.Sees(actor.transform.position, game.PlayerMotor.transform.position, 20)) actor.BeginCombat(now);
                }
            }
        }
        private void HideNative()
        {
            var pool = directory.GetComponent<PopulationManager>();
            if (!pool) return;
            foreach (var entry in pool.PopulationPool)
                if (entry.npc && entry.npc.IsGuard)
                {
                    var body = entry.npc.gameObject;
                    if (!nativeGuards.ContainsKey(body)) nativeGuards.Add(body, body.activeSelf);
                    body.SetActive(false);
                }
        }
        private void UpdateInterior(double now)
        {
            var game = GameManager.Instance;
            // Expired interior actors must stop combat before a new exterior view can exist.
            var expired = new List<string>();
            foreach (var pair in actors)
            {
                var guard = Roster.Guards.Find(g => g.Id == pair.Key);
                if (guard.Interior == null || guard.Interior.BuildingKey != interior.EntryDoor.buildingKey)
                { if (pair.Value) { pair.Value.EndCombat(); Destroy(pair.Value.gameObject); } expired.Add(pair.Key); }
            }
            foreach (var id in expired) actors.Remove(id);
            foreach (var guard in Roster.Guards)
            {
                if (!guard.IsAlive) continue;
                var report = Roster.Reports.Find(r => r.Id == guard.IncidentId);
                if (guard.Interior == null && guard.Duty == GuardDuty.Searching && entryGround.HasValue &&
                    report != null && report.BuildingKey == interior.EntryDoor.buildingKey)
                {
                    var point = interior.transform.InverseTransformPoint(entryGround.Value + Vector3.up);
                    Roster.EnterBuilding(guard.Id, interior.EntryDoor.buildingKey, now,
                        new GuardPoint3 { X = point.x, Y = point.y, Z = point.z });
                }
                if (guard.Interior == null || guard.Interior.BuildingKey != interior.EntryDoor.buildingKey) continue;
                var actor = View(guard, now); actor.gameObject.SetActive(true);
                if (guard.Interior.Returning) { actor.EndCombat(); actor.Place(now); continue; }
                if (game.PlayerEntity.CrimeCommitted == PlayerEntity.Crimes.None)
                {
                    actor.EndCombat(); actor.Capture(now);
                    Roster.EndInteriorSearch(guard.Id, now); actor.Place(now);
                    continue;
                }
                actor.Capture(now);
                if (game.PlayerEntity.CrimeCommitted != PlayerEntity.Crimes.None && !game.PlayerEntity.IsInvisible &&
                    CivilianResponses.Sees(actor.transform.position, game.PlayerMotor.transform.position, 20))
                    actor.BeginCombat(now);
            }
        }
        private void ClearViews()
        {
            Capture();
            foreach (var pair in actors) if (pair.Value) { pair.Value.EndCombat(); Destroy(pair.Value.gameObject); }
            actors.Clear();
        }
        private void HideViews()
        { foreach (var pair in actors) if (pair.Value && pair.Value.gameObject.activeSelf) { pair.Value.Capture(Now()); pair.Value.EndCombat(); pair.Value.gameObject.SetActive(false); } }
        public void Clear()
        {
            if (approachSurvey != null) { StopCoroutine(approachSurvey); approachSurvey = null; }
            if (hooks != null) { hooks.Dispose(); hooks = null; }
            ClearViews(); interior = null; entryGround = null;
            foreach (var pair in nativeGuards) if (pair.Key) pair.Key.SetActive(pair.Value);
            nativeGuards.Clear(); directory = null; geometry = null; Roster = null; reportId = null; awaitingSight = false;
        }
        private void OnDestroy() { Clear(); }
    }
}
