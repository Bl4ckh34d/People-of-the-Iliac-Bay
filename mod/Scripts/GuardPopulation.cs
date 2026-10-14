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
    public sealed partial class GuardPopulation : MonoBehaviour
    {
        public GuardRoster Roster { get; private set; }
        public int MaximumResponders = 4;
        public int ActorRadius = 160;
        public bool NightTorches = true;
        public int TorchLightLimit = 8;
        public int TorchRange = 8;
        private readonly List<GuardActor> torchCandidates = new List<GuardActor>();
        private float nextTorchUpdate;
        private void UpdateTorches()
        {
            var nearby = torchCandidates; nearby.Clear();
            var game = GameManager.Instance;
            bool night = NightTorches && !game.PlayerEnterExit.IsPlayerInside && DaggerfallUnity.Instance.WorldTime.Now.IsNight;
            foreach (var pair in actors)
                if (pair.Value && pair.Value.gameObject.activeInHierarchy && !pair.Value.IsInterior &&
                    Roster.Guards.Exists(g => g.Id == pair.Key && g.IsAlive)) nearby.Add(pair.Value);
            var player = game.PlayerMotor.transform.position;
            nearby.Sort((a, b) => {
                int order = (a.transform.position - player).sqrMagnitude.CompareTo((b.transform.position - player).sqrMagnitude);
                return order == 0 ? string.CompareOrdinal(a.GuardId, b.GuardId) : order;
            });
            foreach (var pair in actors) if (pair.Value)
            {
                int rank = nearby.IndexOf(pair.Value);
                bool visible = night && rank >= 0;
                pair.Value.SetTorch(visible, visible && rank < Mathf.Clamp(TorchLightLimit, 0, 16) &&
                    (pair.Value.transform.position - player).sqrMagnitude <= 48f * 48f, TorchRange);
            }
        }
        public int ActiveCount
        { get { int count = 0; foreach (var pair in actors) if (pair.Value && pair.Value.gameObject.activeInHierarchy) count++; return count; } }
        private WorldStateStore worldStore;
        private BuildingDirectory directory;
        private TownGeometry geometry;
        private IDisposable hooks;
        private string reportId;
        private readonly Dictionary<string, GuardActor> actors = new Dictionary<string, GuardActor>();
        private readonly Dictionary<GameObject, bool> nativeGuards = new Dictionary<GameObject, bool>();
        private float nextRoutes, nextInvestigation;
        private bool awaitingSight;
        private Coroutine approachSurvey;
        private DaggerfallInterior interior;
        private Vector3? entryGround;
        public bool OwnsSettlement { get { return OwnsTown(); } }
        private bool OwnsTown()
        {
            var game = GameManager.Instance;
            return isActiveAndEnabled && directory && Roster != null && game != null && game.IsPlayingGame() &&
                !game.SaveLoadManager.LoadInProgress &&
                game.PlayerGPS.HasCurrentLocation && game.PlayerGPS.CurrentLocation.MapTableData.MapId == directory.MapID &&
                (!game.PlayerEnterExit.IsPlayerInside || (game.PlayerEnterExit.IsPlayerInsideBuilding &&
                game.PlayerEnterExit.Interior));
        }
        public void SetTown(BuildingDirectory town, CityNavigation navigation, SettlementPopulation population, WorldStateStore store)
        {
            if(!SettlementClassification.SupportsPopulation((int)town.LocationData.MapTableData.LocationType) ||
                (town.MapID==1291010263 && DaggerfallPopulationProbe.SuppressCityActors)) { Clear(); return; }
            if (directory == town && Roster != null) return;
            Clear();
            if (GameManager.Instance.PlayerEnterExit.IsPlayerInside && store.GetGuards(town.MapID) == null) return;
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
            int count = GuardRoster.TargetCount(SettlementClassification.FromLocationType((int)town.LocationData.MapTableData.LocationType), residents);
            var savedRoster = store.GetGuards(town.MapID);
            if (posts.Count < count && savedRoster == null) count = posts.Count;
            if (count == 0 && savedRoster == null) return;
            posts.Sort((a, b) => { int x = a.X.CompareTo(b.X); return x == 0 ? a.Z.CompareTo(b.Z) : x; });
            var selected = new List<SavedRoutePoint>();
            for (int i = 0; i < Math.Min(count,posts.Count); i++) selected.Add(posts[i * posts.Count / Math.Min(count,posts.Count)]);
            worldStore = store;
            directory = town; geometry = new TownGeometry(town, navigation, false);
            Roster = store.GetOrCreateGuards(town.MapID, count, selected);
            if (count > Roster.Target)
            {
                try { Roster.IncreaseTarget(count,posts); }
                catch (ArgumentException) { Debug.Log("LIVING_CITIES_GARRISON_EXPANSION_PENDING: insufficient distinct reachable posts."); }
            }
            foreach (var patrol in Roster.Patrols) patrolTemplates.Add(PatrolPairPlan.CopyValidated(patrol.Plan));
            approachSurvey = StartCoroutine(SurveyApproaches(navigation));
            patrolSurvey = StartCoroutine(SurveyPatrols());
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
            while (!OwnsExterior || !directory.gameObject.activeInHierarchy)
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
            {
                var hook = Type.GetType("LivingCities.QuestHooks.NativeCivilianHooks, LivingCities.QuestHooks", true);
                var hitTarget = hook.GetProperty("CurrentTarget").GetValue(null, null) as Transform;
                var resident = hitTarget ? hitTarget.GetComponent<ResidentActor>() : null;
                var streets = GetComponent<StreetPopulation>();
                // Vanilla reports every mobile-citizen weapon hit immediately.
                // Managed citizens report through witnesses/discovery, including when
                // no exit route is available. A navigation failure cannot create knowledge.
                if (resident && streets && streets.ReactionsEnabled && streets.FindActor(resident.Resident.Id) == resident &&
                    resident.Resident.IsAlive) return;
            }
            var room = GameManager.Instance.PlayerEnterExit.IsPlayerInsideBuilding ? GameManager.Instance.PlayerEnterExit.Interior : null;
            int? building = room ? (int?)room.EntryDoor.buildingKey : null;
            var currentReport = Roster.Reports.Find(r => r.Id == reportId);
            if (currentReport != null && (currentReport.BuildingKey != building || currentReport.Investigation)) reportId = null;
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
            if (reportId == null && building.HasValue)
            {
                var saved = Roster.Reports.Find(r => r.BuildingKey == building && !r.Investigation &&
                    r.Id.StartsWith("native:", StringComparison.Ordinal) && (r.GuardIds.Count == 0 ||
                    Roster.Guards.Exists(g => g.IsAlive && g.IncidentId == r.Id)));
                if (saved != null) reportId = saved.Id;
            }
            if (reportId == null) reportId = "native:" + directory.MapID + ":" + building + ":" + now.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            var previous = Roster.Reports.Find(r => r.Id == reportId);
            if (previous != null && previous.GuardIds.Count > 0)
            {
                if (Roster.Guards.Exists(g => g.IsAlive && g.IncidentId == reportId)) return;
                reportId = "native:" + directory.MapID + ":" + building + ":" + now.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            }
            if (building.HasValue)
            {
                if (!Roster.BuildingApproaches.Exists(a => a.BuildingKey == building.Value))
                    Roster.QueueBuildingReport(reportId, building.Value);
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
                var route = geometry.RouteBetweenPoints(Roster.Position(guard.Id, now), target);
                if (route.Count >= 2) routes.Add(guard.Id, route);
            }
            var sent = Roster.Dispatch(reportId, now, routes, Mathf.Clamp(MaximumResponders, 1, 8), 2.8, 12);
            Debug.Log("LIVING_CITIES_GUARD_DISPATCH: " + reportId + "; responders=" + sent.Count);
        }
        public bool CanInvestigate(int buildingKey)
        { return OwnsTown() && Roster.BuildingApproaches.Exists(a => a.BuildingKey == buildingKey && a.Routes.Count > 0); }
        private void RetryNativeReports(double now)
        {
            // Keep the reported building even when the player changes scenes or reloads.
            foreach (var report in new List<GuardReport>(Roster.Reports))
                if (report.Id.StartsWith("native:", StringComparison.Ordinal) && !report.Investigation &&
                    report.BuildingKey.HasValue && report.GuardIds.Count == 0)
                    Roster.DispatchBuilding(report.Id, report.BuildingKey.Value, now,
                        Mathf.Clamp(MaximumResponders, 1, 8), 2.8, 12);
        }
        private void UpdateInvestigations(double now)
        {
            var game = GameManager.Instance;
            if (worldStore == null || Roster == null || !directory || game == null || !game.IsPlayingGame() || game.SaveLoadManager.LoadInProgress) return;
            Roster.Advance(now);
            bool sameTown = game.PlayerGPS.HasCurrentLocation && game.PlayerGPS.CurrentLocation.MapTableData.MapId == directory.MapID;
            int location = sameTown && game.PlayerEnterExit.IsPlayerInsideBuilding && game.PlayerEnterExit.Interior ?
                game.PlayerEnterExit.Interior.EntryDoor.buildingKey : -1;
            foreach (var alarm in worldStore.ObserveCrimeScenes(directory.MapID, location, now))
            {
                string id = "discovery:" + alarm.Id;
                Roster.DispatchBuilding(id, alarm.BuildingKey, now, Mathf.Clamp(MaximumResponders, 1, 8), 2.8, 12, true);
                var report = Roster.Reports.Find(r => r.Id == id);
                if (report != null && report.HasArrival) worldStore.DiscoverCrimeScene(alarm.Id, now);
            }
        }
        public List<SavedRoutePoint> WitnessRoute(int buildingKey, double now)
        {
            if (!OwnsTown()) return null;
            var approach = Roster.BuildingApproaches.Find(a => a.BuildingKey == buildingKey);
            if (approach == null) return null;
            Roster.Advance(now);
            List<SavedRoutePoint> best = null; double shortest = double.MaxValue;
            foreach (var route in approach.Routes)
            {
                if (!Roster.Guards.Exists(g => g.IsAlive && g.Duty == GuardDuty.Posted && !Roster.IsOnPatrol(g.Id) &&
                    Math.Abs(g.Post.X-route.Points[0].X) < 0.05 && Math.Abs(g.Post.Z-route.Points[0].Z) < 0.05)) continue;
                double length = 0;
                for (int i = 1; i < route.Points.Count; i++)
                { double x = route.Points[i].X-route.Points[i-1].X, z = route.Points[i].Z-route.Points[i-1].Z; length += Math.Sqrt(x*x+z*z); }
                if (length >= shortest) continue;
                shortest = length; best = route.Points.ConvertAll(p => new SavedRoutePoint { X = p.X, Z = p.Z });
            }
            if (best != null) best.Reverse();
            return best;
        }
        public void ReportEscapedWitness(CivilianIncident alarm, double now)
        {
            if (!OwnsTown() || alarm.SettlementId != directory.MapID || alarm.BuildingKey < 0 || now >= CivilianAlarms.ActiveUntil(alarm)) return;
            string id = "civilian:" + alarm.Id;
            if (Roster.Reports.Exists(r => r.Id == id && r.GuardIds.Count > 0)) return;
            var streets = GetComponent<StreetPopulation>();
            Roster.Advance(now);
            bool reached = false;
            foreach (var person in alarm.People)
            {
                if (!person.DirectWitness && person.HeardViolence && person.Escape != null && person.Escape.Arrived(now) &&
                    alarm.Scene != null && worldStore.ReportHeardViolence(alarm.Id, person.ResidentId, now))
                {
                    UpdateInvestigations(now);
                    Debug.Log("LIVING_CITIES_GUARD_HEARING_REPORT: " + alarm.Id + "; neutral physical contact");
                }
                if (!person.DirectWitness || person.Reaction != CivilianReaction.Flee || person.Escape == null ||
                    !person.Escape.Arrived(now) || !streets.IsLivingResident(person.ResidentId)) continue;
                if (Roster.Guards.Exists(g => g.IsAlive && g.Duty == GuardDuty.Posted &&
                    Math.Abs(Roster.Position(g.Id,now).X-person.X) < 0.5 && Math.Abs(Roster.Position(g.Id,now).Z-person.Z) < 0.5)) { reached = true; break; }
            }
            if (!reached) return;
            var sent = Roster.DispatchBuilding(id, alarm.BuildingKey, now, Mathf.Clamp(MaximumResponders, 1, 8), 2.8, 12);
            if (sent.Count == 0) return;
            reportId = id; GameManager.Instance.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.Murder;
            Debug.Log("LIVING_CITIES_GUARD_ESCAPE_REPORT: " + id + "; building=" + alarm.BuildingKey + "; responders=" + sent.Count);
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
        public void ReportCivilianCrime(CivilianIncident incident, Vector3 reportFrom)
        {
            if (!OwnsTown() || GameManager.Instance.PlayerEnterExit.IsPlayerInside || incident == null ||
                !incident.HasLocation || incident.BuildingKey != -1 || incident.SettlementId != directory.MapID) return;
            double now = Now();
            if (now < incident.StartedAt || now >= incident.ExpiresAt) return;
            string id = "civilian:" + incident.Id;
            var reported = Roster.Reports.Find(r => r.Id == id);
            if (reported != null && reported.GuardIds.Count > 0) return;
            Roster.Advance(now);
            var target = new SavedRoutePoint { X = incident.X, Z = incident.Z };
            var routes = new Dictionary<string, List<SavedRoutePoint>>();
            foreach (var guard in Roster.Guards)
            {
                if (!guard.IsAlive || guard.Duty != GuardDuty.Posted) continue;
                var current = Roster.Position(guard.Id, now);
                var standing = geometry.ScenePoint(new RoutePoint(current.X, current.Z)) + Vector3.up;
                if (!CivilianResponses.Sees(reportFrom, standing, 20)) continue;
                var route = geometry.RouteBetweenPoints(current, target);
                if (route.Count >= 2) routes.Add(guard.Id, route);
            }
            if (routes.Count == 0) return;
            var sent = Roster.Dispatch(id, now, routes, Mathf.Clamp(MaximumResponders, 1, 8), 2.8, 12);
            if (sent.Count == 0) return;
            reportId = id;
            GameManager.Instance.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.Murder;
            Debug.Log("LIVING_CITIES_GUARD_WITNESS_REPORT: " + id + "; responders=" + sent.Count);
        }
        private GuardActor View(GuardRecord guard, double now)
        {
            GuardActor actor;
            bool indoors=guard.Interior!=null || GuardActor.RefugeIndoors(guard,now);
            if (actors.TryGetValue(guard.Id,out actor) && actor && actor.IsInterior!=indoors)
            {
                actor.EndCombat(); actor.gameObject.SetActive(false); Destroy(actor.gameObject);
                actors.Remove(guard.Id);
            }
            if (!actors.TryGetValue(guard.Id, out actor) || !actor)
            {
                actor = indoors ? GuardActor.CreateInterior(Roster, guard.Id, interior.transform, now) :
                    GuardActor.Create(Roster, guard.Id, directory.transform, geometry, now);
                actors[guard.Id] = actor;
            }
            return actor;
        }
        public void Capture()
        { foreach (var pair in actors) if (pair.Value) pair.Value.Capture(Now()); }
        private void OnEnable()
        {
            PlayerEnterExit.OnTransitionExterior += ObserveTransition;
            PlayerEnterExit.OnTransitionInterior += ObserveTransition;
            PlayerEnterExit.OnTransitionDungeonInterior += ObserveTransition;
        }
        private void OnDisable()
        {
            PlayerEnterExit.OnTransitionExterior -= ObserveTransition;
            PlayerEnterExit.OnTransitionInterior -= ObserveTransition;
            PlayerEnterExit.OnTransitionDungeonInterior -= ObserveTransition;
        }
        private void ObserveTransition(PlayerEnterExit.TransitionEventArgs args)
        { if (worldStore != null && Roster != null && directory) UpdateInvestigations(Now()); }
        private void Update()
        {
            AdvanceSceneWarnings();
            if (worldStore != null && !GameManager.IsGamePaused && Time.unscaledTime >= nextInvestigation)
            { nextInvestigation = Time.unscaledTime + 1; UpdateInvestigations(Now()); }
            if (!OwnsTown()) { HideViews(); return; }
            if (GameManager.IsGamePaused) return;
            var game = GameManager.Instance; double now = Now();
            var loaded = game.PlayerEnterExit.IsPlayerInsideBuilding ? game.PlayerEnterExit.Interior : null;
            if (interior != loaded)
            {
                ClearViews(); interior = loaded;
                entryGround = interior ? new InteriorGeometry(interior).EntryGround : null;
            }
            Roster.Advance(now);
            ReleaseSafeRefuges(now);
            if (Roster.Recruit(now) > 0)
            {
                if (patrolSurvey != null) StopCoroutine(patrolSurvey);
                patrolTemplates.Clear();
                foreach (var pair in Roster.Patrols) patrolTemplates.Add(PatrolPairPlan.CopyValidated(pair.Plan));
                patrolSurvey = StartCoroutine(SurveyPatrols());
            }
            if (Time.unscaledTime >= nextPatrolTick)
            { nextPatrolTick = Time.unscaledTime + 1; UpdateNightPatrols(now); }
            if (game.PlayerEntity.CrimeCommitted == PlayerEntity.Crimes.None)
            {
                reportId = null; awaitingSight = false;
                Roster.Reports.RemoveAll(r => r.Id.StartsWith("native:", StringComparison.Ordinal) &&
                    !r.Investigation && r.GuardIds.Count == 0);
            }
            bool routeTick = Time.unscaledTime >= nextRoutes;
            if (routeTick) nextRoutes = Time.unscaledTime + 1;
            if (routeTick && game.PlayerEntity.CrimeCommitted != PlayerEntity.Crimes.None) RetryNativeReports(now);
            if (awaitingSight && routeTick && game.PlayerEntity.CrimeCommitted != PlayerEntity.Crimes.None) ReportNative(false);
            if (interior) { UpdateInterior(now); return; }
            HideNative();
            if (routeTick) SurveyEnemyThreats(now);
            foreach (var guard in Roster.Guards)
            {
                if (!guard.IsAlive) continue;
                if (guard.Interior != null) continue;
                if (GuardActor.RefugeIndoors(guard,now))
                { GuardActor sheltered; if (actors.TryGetValue(guard.Id,out sheltered) && sheltered) sheltered.gameObject.SetActive(false); continue; }
                var investigation = Roster.Reports.Find(r => r.Id == guard.IncidentId && r.Investigation);
                if (investigation != null && investigation.BuildingKey.HasValue && guard.Duty == GuardDuty.Searching)
                { GuardActor hidden; if (actors.TryGetValue(guard.Id, out hidden) && hidden) hidden.gameObject.SetActive(false); continue; }
                var point = Roster.Position(guard.Id, now);
                Vector3 scene = geometry.ScenePoint(new RoutePoint(point.X, point.Z));
                GuardActor actor;
                int radius = Mathf.Clamp(ActorRadius, 64, 256);
                bool close = (scene - game.PlayerMotor.transform.position).sqrMagnitude < radius * radius;
                if (!close && guard.Duty != GuardDuty.Engaged)
                { if (actors.TryGetValue(guard.Id, out actor) && actor) actor.gameObject.SetActive(false); continue; }
                actor = View(guard, now); actor.gameObject.SetActive(true);
                if (guard.Refuge!=null) { actor.EndCombat(); actor.Place(now); continue; }
                if (actor.GetComponent<EnemyMotor>().IsHostile && (actor.DefenseTarget ||
                    guard.Duty != GuardDuty.Engaged && actor.GetComponent<EnemySenses>().Target == game.PlayerEntityBehaviour))
                {
                    if (guard.IncidentId == null)
                    {
                        string assault = "assault:" + guard.Id + ":" + now.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
                        var actual = Roster.Position(guard.Id, now);
                        Roster.Dispatch(assault, now, new Dictionary<string, List<SavedRoutePoint>> {
                            { guard.Id, new List<SavedRoutePoint> { actual, actual } } }, 1, 2.8, 12);
                    }
                    if (game.PlayerEntity.CrimeCommitted == PlayerEntity.Crimes.None) game.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.Assault;
                    var defense = Roster.Reports.Find(r => r.Id == guard.IncidentId);
                    if (defense != null) defense.ThreatId = null;
                    actor.BeginCombat(now);
                }
                if (UpdateEnemyDefense(guard,actor,now,routeTick)) continue;
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
        private void LateUpdate()
        {
            if (OwnsTown() && !GameManager.IsGamePaused && Time.unscaledTime >= nextTorchUpdate)
            { nextTorchUpdate = Time.unscaledTime + 0.25f; UpdateTorches(); }
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
                bool refugeHere=GuardActor.RefugeIndoors(guard,now) && guard.Refuge.BuildingKey==interior.EntryDoor.buildingKey;
                if (!refugeHere && (guard.Interior == null || guard.Interior.BuildingKey != interior.EntryDoor.buildingKey))
                { if (pair.Value) { pair.Value.EndCombat(); pair.Value.gameObject.SetActive(false); Destroy(pair.Value.gameObject); } expired.Add(pair.Key); }
            }
            foreach (var id in expired) actors.Remove(id);
            foreach (var guard in Roster.Guards)
            {
                if (!guard.IsAlive) continue;
                if (guard.Refuge!=null)
                {
                    if (GuardActor.RefugeIndoors(guard,now) && guard.Refuge.BuildingKey==interior.EntryDoor.buildingKey)
                    { var refugeActor=View(guard,now); refugeActor.gameObject.SetActive(true); refugeActor.EndCombat(); refugeActor.Place(now); }
                    continue;
                }
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
                if (game.PlayerEntity.CrimeCommitted == PlayerEntity.Crimes.None && (report == null || !report.Investigation))
                {
                    actor.EndCombat(); actor.Capture(now);
                    Roster.EndInteriorSearch(guard.Id, now); actor.Place(now);
                    continue;
                }
                actor.Capture(now);
                if (report != null && report.Investigation && game.PlayerEntity.CrimeCommitted == PlayerEntity.Crimes.None &&
                    !game.PlayerEntity.IsInvisible && CivilianResponses.Sees(actor.transform.position, game.PlayerMotor.transform.position, 20))
                {
                    if (report.Warning == null) report.Warning = new CrimeSceneWarning();
                    if (report.Warning.Begin()) DaggerfallUI.AddHUDText("Leave this crime scene. You have ten seconds to exit the house.");
                }
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
            if (patrolSurvey != null) { StopCoroutine(patrolSurvey); patrolSurvey = null; }
            patrolTemplates.Clear();
            defenseEnemies.Clear();
            refugeInteriors.Clear(); refugeGeometry=null;
            if (hooks != null) { hooks.Dispose(); hooks = null; }
            ClearViews(); interior = null; entryGround = null;
            foreach (var pair in nativeGuards) if (pair.Key) pair.Key.SetActive(pair.Value);
            nativeGuards.Clear(); worldStore = null; directory = null; geometry = null; Roster = null; reportId = null; awaitingSight = false;
        }
        private void OnDestroy() { Clear(); }
    }
}
