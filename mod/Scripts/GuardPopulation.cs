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
        private bool publicPostSurveyPending;
        private List<SavedRoutePoint> publicPostSites=new List<SavedRoutePoint>();
        private float nextPostMigration;
        private TownGeometry geometry;
        private IDisposable hooks;
        private string reportId;
        private readonly Dictionary<string, GuardActor> actors = new Dictionary<string, GuardActor>();
        private InteriorGeometry interiorNavigation;
        private readonly Dictionary<GameObject, bool> nativeGuards = new Dictionary<GameObject, bool>();
        private float nextRoutes, nextInvestigation;
        private bool awaitingSight;
        private Coroutine approachSurvey;
        private Coroutine setupSurvey;
        private BuildingDirectory setupDirectory;
        private TownGeometry setupGeometry,setupEntrances;
        private List<SavedRoutePoint> setupPosts;
        private DaggerfallInterior interior;
        private Vector3? entryGround;
        public bool OwnsSettlement { get { return OwnsTown(); } }
        private bool OwnsTown()
        {
            var game = GameManager.Instance;
            // A paused dialogue does not relinquish settlement ownership. Hiding
            // guards here ends combat, causing vanilla to reset its surrender flag.
            return isActiveAndEnabled && directory && Roster != null && game != null &&
                !game.SaveLoadManager.LoadInProgress &&
                game.PlayerGPS.HasCurrentLocation && game.PlayerGPS.CurrentLocation.MapTableData.MapId == directory.MapID &&
                (!game.PlayerEnterExit.IsPlayerInside || (game.PlayerEnterExit.IsPlayerInsideBuilding &&
                game.PlayerEnterExit.Interior));
        }
        public void SetTown(BuildingDirectory town, CityNavigation navigation, SettlementPopulation population, WorldStateStore store)
        {
            if(!SettlementClassification.SupportsPopulation((int)town.LocationData.MapTableData.LocationType) ||
                (town.MapID==1291010263 && DaggerfallPopulationProbe.SuppressCityActors)) { Clear(); return; }
            if (directory == town && Roster != null)
            {
                if(publicPostSurveyPending && !GameManager.Instance.PlayerEnterExit.IsPlayerInside)
                {
                    // Finishing a deferred survey must not destroy active pursuers.
                    publicPostSites=new TownGeometry(town,navigation,false).SurveyGuardPosts(350);
                    publicPostSurveyPending=false;
                    int occupants=0; foreach(var home in population.Households) occupants+=home.Residents.Count;
                    int target=GuardRoster.TargetCount(SettlementClassification.FromLocationType((int)town.LocationData.MapTableData.LocationType),occupants);
                    if(target>Roster.Target)
                        try { Roster.IncreaseTarget(target,publicPostSites); }
                        catch(ArgumentException) { Debug.Log("LIVING_CITIES_GARRISON_EXPANSION_PENDING: insufficient public posts."); }
                }
                return;
            }
            if(!GameManager.Instance.PlayerEnterExit.IsPlayerInside)
            {
                if(setupDirectory!=town)
                {
                    Clear(); setupDirectory=town;
                    setupSurvey=StartCoroutine(PrepareGuardSetup(town,navigation));
                    return;
                }
                if(setupPosts==null || setupEntrances==null) return;
            }
            var preparedGeometry=setupDirectory==town ? setupGeometry : null;
            var preparedPosts=setupDirectory==town ? setupPosts : null;
            var preparedEntrances=setupDirectory==town ? setupEntrances : null;
            Clear();
            if (GameManager.Instance.PlayerEnterExit.IsPlayerInside && store.GetGuards(town.MapID) == null) return;
            var routines = store.GetRoutines(town.MapID);
            if (routines == null) return;
            int residents = 0;
            foreach (var home in population.Households) residents += home.Residents.Count;
            int count = GuardRoster.TargetCount(SettlementClassification.FromLocationType((int)town.LocationData.MapTableData.LocationType), residents);
            var savedRoster = store.GetGuards(town.MapID);
            var postSurvey=preparedGeometry ?? new TownGeometry(town,navigation,false);
            publicPostSurveyPending=GameManager.Instance.PlayerEnterExit.IsPlayerInside;
            var posts=new List<SavedRoutePoint>();
            if(publicPostSurveyPending)
            { foreach(var guard in savedRoster.Guards) if(guard.IsAlive) posts.Add(guard.Post); }
            else posts=preparedPosts ?? postSurvey.SurveyGuardPosts(350);
            publicPostSites=publicPostSurveyPending ? new List<SavedRoutePoint>() : posts;
            if (posts.Count < count && savedRoster == null) count = posts.Count;
            if (count == 0 && savedRoster == null) return;
            posts.Sort((a, b) => { int x = a.X.CompareTo(b.X); return x == 0 ? a.Z.CompareTo(b.Z) : x; });
            var selected = new List<SavedRoutePoint>();
            for (int i = 0; i < Math.Min(count,posts.Count); i++) selected.Add(posts[i * posts.Count / Math.Min(count,posts.Count)]);
            worldStore = store;
            directory = town; geometry = postSurvey;
            refugeGeometry=preparedEntrances;
            Roster = store.GetOrCreateGuards(town.MapID, count, selected);
            Roster.Advance(Now());
            long gateSetupStarted=RuntimeTiming.Start();
            try { InitializeGateGuardPosts(); }
            finally {RuntimeTiming.End("guard-initial-gate-posts",gateSetupStarted);}
            // Saved posts may predate a layout mod. Repair them through the sliced
            // migration worker, never by draining thousands of paths during load.
            nextPostMigration=0;
            if (count > Roster.Target)
            {
                try { Roster.IncreaseTarget(count,posts); }
                catch (ArgumentException) { Debug.Log("LIVING_CITIES_GARRISON_EXPANSION_PENDING: insufficient distinct reachable posts."); }
            }
            foreach (var patrol in Roster.Patrols) patrolTemplates.Add(PatrolPairPlan.CopyValidated(patrol.Plan));
            long approachStart=RuntimeTiming.Start();
            try {approachSurvey = StartCoroutine(SurveyApproaches(navigation));}
            finally {RuntimeTiming.End("guard-initial-approach",approachStart);}
            long patrolStart=RuntimeTiming.Start();
            try {patrolSurvey = StartCoroutine(SurveyPatrols());}
            finally {RuntimeTiming.End("guard-initial-patrol",patrolStart);}
            foreach (var guard in Roster.Guards)
                if (guard.IsAlive && guard.IncidentId != null) { reportId = guard.IncidentId; break; }
            var type = Type.GetType("LivingCities.QuestHooks.NativeGuardHooks, LivingCities.QuestHooks", true);
            hooks = (IDisposable)Activator.CreateInstance(type, new object[] { new Func<bool>(OwnsTown),
                new Action<bool>(immediate => ReportNative(immediate)), new Func<Vector3, Vector3, GameObject>(ResolveNative) });
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
        private IEnumerator PrepareGuardSetup(BuildingDirectory town,CityNavigation navigation)
        {
            yield return BackgroundWork.Wait(()=>setupDirectory==town && town);
            if(setupDirectory!=town || !town) yield break;
            var postGeometry=new TownGeometry(town,navigation,false);
            var result=new TownGeometry.GuardPostResult();
            var entrances=new TownGeometry(town,navigation,false);
            foreach(var operation in new[]{postGeometry.SurveyGuardPostSteps(350,result),entrances.ReadEntranceSteps(),postGeometry.SurveyGatePostSteps()})
            {
                try
                {
                    while(setupDirectory==town && town)
                    {
                        var game=GameManager.Instance;
                        if(GameManager.IsGamePaused || game.PlayerEnterExit.IsPlayerInside || !town.gameObject.activeInHierarchy ||
                            game.StreamingWorld.GetCurrentBuildingDirectory()!=town) {yield return null; continue;}
                        bool more; long started=RuntimeTiming.Start();
                        try {more=operation.MoveNext();}
                        finally {RuntimeTiming.End("guard-setup-survey-slice",started);}
                        if(!more) break;
                        yield return null;
                    }
                }
                finally {(operation as IDisposable)?.Dispose();}
                if(setupDirectory!=town || !town) yield break;
            }
            setupGeometry=postGeometry; setupPosts=result.Posts; setupEntrances=entrances;
            setupSurvey=null;
        }
        private sealed class ApproachWork
        {
            public int Building;
            public SavedRoutePoint Target;
            public List<GuardRecord> Guards;
            public Dictionary<string,List<SavedRoutePoint>> Routes;
        }
        private IEnumerator SurveyApproaches(CityNavigation navigation)
        {
            while (!OwnsExterior || !directory.gameObject.activeInHierarchy)
                yield return null;
            // Door discovery and physics queries must run against the active exterior.
            // Approaches, haunting refuges and wall shifts use the same loaded
            // settlement entrance geometry. Clear() invalidates it on scene change.
            if(refugeGeometry==null)
            {
                long entrancesStarted=RuntimeTiming.Start();
                try {refugeGeometry=new TownGeometry(directory,navigation);}
                finally {RuntimeTiming.End("guard-entrance-survey",entrancesStarted);}
            }
            var survey = refugeGeometry;
            int completed = 0;
            var work=new List<ApproachWork>();
            foreach (var entry in survey.Entrances)
            {
                var routes = new Dictionary<string, List<SavedRoutePoint>>();
                var cached=Roster.BuildingApproaches.Find(a=>a.BuildingKey==entry.Key);
                if(cached!=null) foreach(var route in cached.Routes) routes[route.GuardId]=route.Points;
                var door = entry.Value.LocalDoor;
                var target = new SavedRoutePoint { X = door.x, Z = door.z };
                // The roster can gain recruits during the coroutine. Use a stable
                // post survey snapshot; replacements can reuse the same post route.
                var guards = new List<GuardRecord>(Roster.Guards);
                guards.Sort((a,b)=>{
                    double ax=a.Post.X-target.X,az=a.Post.Z-target.Z,bx=b.Post.X-target.X,bz=b.Post.Z-target.Z;
                    int order=(ax*ax+az*az).CompareTo(bx*bx+bz*bz);
                    return order!=0 ? order : string.CompareOrdinal(a.Id,b.Id);
                });
                work.Add(new ApproachWork {Building=entry.Key,Target=target,Guards=guards,Routes=routes});
                if(work.Count%8==0) yield return null;
            }
            // Give each building its nearest available response route before
            // filling every distant guard route for the first building.
            for(int rank=0;rank<Roster.Guards.Count;rank++)
                foreach(var entry in work)
                {
                    if(rank>=entry.Guards.Count) continue;
                    var guard=entry.Guards[rank];var target=entry.Target;var routes=entry.Routes;
                    if(routes.ContainsKey(guard.Id)) continue;
                    while (!OwnsTown() || GameManager.Instance.PlayerEnterExit.IsPlayerInside ||
                        !directory.gameObject.activeInHierarchy || GameManager.IsGamePaused)
                        yield return null;
                    var prepared=new TownGeometry.RouteResult();
                    var routeWork=survey.ReadRouteSteps(guard.Post,target,prepared,()=>OwnsExterior &&
                        directory.gameObject.activeInHierarchy && !GameManager.IsGamePaused);
                    bool finished=false;
                    try
                    {
                        while(!finished)
                        {
                            while(!OwnsExterior || !directory.gameObject.activeInHierarchy || GameManager.IsGamePaused)
                                yield return null;
                            long slice=RuntimeTiming.Start();
                            var budget=System.Diagnostics.Stopwatch.StartNew();
                            try
                            {
                                // Consume cheap internal yields in the same frame. Waiting
                                // one frame per small search/validation step delays readiness.
                                do {finished=!routeWork.MoveNext();}
                                while(!finished && budget.Elapsed.TotalMilliseconds<4);
                            }
                            finally {RuntimeTiming.End("guard-approach-route-slice",slice);}
                            if(!finished) yield return null;
                        }
                    }
                    finally {(routeWork as IDisposable)?.Dispose();}
                    var route = prepared.Points;
                    if (route!=null && route.Count >= 2)
                    {
                        routes.Add(guard.Id, route);
                        Roster.CacheBuildingApproach(entry.Building,target,routes);
                        if(routes.Count==1) completed++;
                    }
                    yield return null;
                }
            Debug.Log("LIVING_CITIES_GUARD_APPROACHES_READY: buildings=" + Roster.BuildingApproaches.Count + "; added=" + completed);
            approachSurvey = null;
        }
        private SavedRoutePoint Local(Vector3 position)
        { var p = directory.transform.InverseTransformPoint(position); return new SavedRoutePoint { X = p.x, Z = p.z }; }
        private void ReportNative(bool immediate, Vector3? rememberedOutdoorPosition = null)
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
            var room = !rememberedOutdoorPosition.HasValue && GameManager.Instance.PlayerEnterExit.IsPlayerInsideBuilding ?
                GameManager.Instance.PlayerEnterExit.Interior : null;
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
                var responders = Roster.DispatchBuilding(reportId, building.Value, now, Mathf.Clamp(MaximumResponders, 1, 8), 2.8, DaggerfallUnity.Instance.WorldTime.TimeScale);
                Debug.Log("LIVING_CITIES_GUARD_BUILDING_DISPATCH: building=" + building + "; responders=" + responders.Count);
                return;
            }
            var target = Local(rememberedOutdoorPosition ?? GameManager.Instance.PlayerMotor.transform.position);
            var routes = new Dictionary<string, List<SavedRoutePoint>>();
            Roster.Advance(now);
            foreach (var guard in Roster.Guards)
            {
                if (!guard.IsAlive || guard.Duty != GuardDuty.Posted) continue;
                var route = geometry.RouteBetweenPoints(Roster.Position(guard.Id, now), target);
                if (route.Count >= 2) routes.Add(guard.Id, route);
            }
            var sent = Roster.Dispatch(reportId, now, routes, Mathf.Clamp(MaximumResponders, 1, 8), 2.8, DaggerfallUnity.Instance.WorldTime.TimeScale);
            Debug.Log("LIVING_CITIES_GUARD_DISPATCH: " + reportId + "; responders=" + sent.Count);
        }
        public void ReportWitnessedInteriorLoot()
        {
            if (!GameManager.Instance.PlayerEnterExit.IsPlayerInsideBuilding ||
                GameManager.Instance.PlayerEntity.CrimeCommitted == PlayerEntity.Crimes.None) return;
            // Reuse finite physical building responses, including pending geometry.
            ReportNative(true);
        }
        public void ReportDialogueOffense()
        {
            if(GameManager.Instance.PlayerEntity.CrimeCommitted != PlayerEntity.Crimes.None)
                ReportNative(true);
        }
        public bool HasAvailableDialogueGuards()
        { return OwnsTown() && Roster != null && Roster.Guards.Exists(guard => guard.IsAlive && guard.Duty == GuardDuty.Posted); }
        public bool CanInvestigate(int buildingKey)
        { return OwnsTown() && Roster.BuildingApproaches.Exists(a => a.BuildingKey == buildingKey && a.Routes.Count > 0); }
        private void RetryNativeReports(double now)
        {
            // Keep the reported building even when the player changes scenes or reloads.
            foreach (var report in new List<GuardReport>(Roster.Reports))
                if (report.Id.StartsWith("native:", StringComparison.Ordinal) && !report.Investigation &&
                    report.BuildingKey.HasValue && report.GuardIds.Count == 0)
                    Roster.DispatchBuilding(report.Id, report.BuildingKey.Value, now,
                        Mathf.Clamp(MaximumResponders, 1, 8), 2.8, DaggerfallUnity.Instance.WorldTime.TimeScale);
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
                Roster.DispatchBuilding(id, alarm.BuildingKey, now, Mathf.Clamp(MaximumResponders, 1, 8), 2.8, DaggerfallUnity.Instance.WorldTime.TimeScale, true);
                var report = Roster.Reports.Find(r => r.Id == id);
                if (report != null && report.HasArrival) worldStore.DiscoverCrimeScene(alarm.Id, now);
            }
        }
        public List<SavedRoutePoint> WitnessRoute(int buildingKey, double now)
        {
            if (!OwnsTown()) return null;
            Roster.Advance(now);
            var plan=Roster.PlanWitnessRoute(buildingKey,now);
            return plan==null ? null : plan.Route;
        }
        private readonly Dictionary<string,double> nextWitnessRoute=new Dictionary<string,double>();
        public void PrepareWitnessAdvance(CivilianIncident alarm,AlarmPerson person,double now)
        {
            double previous=person.EscapeAdvancedAt;
            if(previous==0 || previous>now) previous=now;
            if(!OwnsTown() || alarm.SettlementId!=directory.MapID || now<person.Escape.ExitAt) return;
            var threat=WitnessThreat(alarm);
            if(threat==null) return;
            // Replan from the last simulated position before consuming elapsed time.
            // Crossing the doorway starts this decision at the exact exit time.
            double at=Math.Max(previous,Math.Max(person.Escape.ExitAt,person.Escape.RedirectedAt));
            var before=new AlarmPerson(); var after=new AlarmPerson();
            person.Escape.Advance(before,at); person.Escape.Advance(after,now);
            double next;
            // A newly approaching attacker overrides the normal replan cadence.
            if(nextWitnessRoute.TryGetValue(person.ResidentId,out next) && now<next &&
                FlightSafety.SafeStep(before.X,before.Z,after.X,after.Z,threat.X,threat.Z)) return;
            person.Escape.Advance(person,at);
            var plan=Roster.PlanWitnessRoute(alarm.BuildingKey,now,new SavedRoutePoint{X=person.X,Z=person.Z},threat,true);
            if(plan!=null) person.Escape=person.Escape.RedirectExterior(at,plan.Route);
            nextWitnessRoute[person.ResidentId]=now+6;
        }
        private SavedRoutePoint WitnessThreat(CivilianIncident alarm)
        {
            var game=GameManager.Instance;
            // Exterior routes must never use the player's coordinates inside a building.
            if(game.PlayerEnterExit.IsPlayerInside) return null;
            if(alarm.ThreatId==null) return Local(game.PlayerMotor.transform.position);
            Vector3 target;
            return TryHostileThreatPosition(alarm.ThreatId,out target) ? Local(target) : null;
        }
        private void ReplanEscapedWitnesses(CivilianIncident alarm,double now,StreetPopulation streets)
        {
            if(alarm.Scene!=null && alarm.Scene.Reported || Roster.Reports.Exists(r=>r.GuardIds.Count>0 &&
                (r.Id=="civilian:"+alarm.Id || r.Id=="inn-alarm:"+alarm.Id || r.Id=="discovery:"+alarm.Id))) return;
            var threat=WitnessThreat(alarm);
            foreach(var person in alarm.People)
            {
                if(person.Escape==null || now<person.Escape.ExitAt || !CivilianAlarms.PersonActive(alarm,person,now) ||
                    person.Reaction!=CivilianReaction.Flee || !streets.IsLivingResident(person.ResidentId)) continue;
                double next;
                if(nextWitnessRoute.TryGetValue(person.ResidentId,out next) && now<next) continue;
                nextWitnessRoute[person.ResidentId]=now+(threat==null ? 24 : 6);
                var plan=Roster.PlanWitnessRoute(alarm.BuildingKey,now,new SavedRoutePoint{X=person.X,Z=person.Z},threat,threat!=null);
                if(plan!=null) person.Escape=person.Escape.RedirectExterior(now,plan.Route);
            }
        }
        public void ReportEscapedWitness(CivilianIncident alarm, double now)
        {
            if (alarm==null || !OwnsTown() || alarm.SettlementId != directory.MapID || alarm.BuildingKey < 0 ||
                (now >= CivilianAlarms.ActiveUntil(alarm) && !(alarm.PlayerAttributed && !alarm.CrimeResolved))) return;
            if(alarm.ThreatId!=null && !EnemyDefense) return;
            string id = "civilian:" + alarm.Id;
            if (Roster.Reports.Exists(r => r.Id == id && r.GuardIds.Count > 0)) return;
            var streets = GetComponent<StreetPopulation>();
            Roster.Advance(now);
            bool reached = false, reachedDirectWitness = false;
            var informed=new HashSet<string>();
            foreach (var person in alarm.People)
            {
                if(!CivilianAlarms.PersonActive(alarm,person,now)) continue;
                if (!person.DirectWitness && person.HeardViolence && person.Escape != null && now>=person.Escape.ExitAt &&
                    alarm.Scene != null && worldStore.ReportHeardViolence(alarm.Id, person.ResidentId, now))
                {
                    UpdateInvestigations(now);
                    Debug.Log("LIVING_CITIES_GUARD_HEARING_REPORT: " + alarm.Id + "; neutral physical contact");
                }
                if ((!person.DirectWitness && !person.InnAlarm && !person.CorpseObserver) || person.Reaction != CivilianReaction.Flee || person.Escape == null ||
                    now<person.Escape.ExitAt || !streets.IsLivingResident(person.ResidentId)) continue;
                foreach(var guardId in Roster.GuardsInReportingRange(new SavedRoutePoint{X=person.X,Z=person.Z},now))
                    { reached=true; if(person.DirectWitness) reachedDirectWitness=true; informed.Add(guardId); }
            }
            if (!reached) { ReplanEscapedWitnesses(alarm,now,streets); return; }
            // An inn-wide alarm tells occupants to escape, but does not identify the
            // player as the murderer. A non-witness can send guards to inspect the inn;
            // only an actual direct witness creates the accusation report.
            bool neutralInnReport = alarm.ThreatId == null && !reachedDirectWitness;
            string dispatchId = neutralInnReport ? "inn-alarm:" + alarm.Id : id;
            if(Roster.Reports.Exists(r=>r.Id==dispatchId && r.GuardIds.Count>0)) return;
            var sent = Roster.DispatchBuilding(dispatchId, alarm.BuildingKey, now, Mathf.Clamp(MaximumResponders, 1, 8), 2.8, DaggerfallUnity.Instance.WorldTime.TimeScale,
                neutralInnReport,alarm.ThreatId==null ? null : informed,alarm.ThreatId,
                !neutralInnReport && alarm.ThreatId==null && alarm.Corpse!=null);
            if (sent.Count == 0) return;
            if(alarm.ThreatId==null && !neutralInnReport)
            {
                reportId = id;
                if (alarm.Corpse != null)
                {
                    GameManager.Instance.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.Murder;
                    if (alarm.PlayerAttributed) worldStore.MarkMurderAccusationIssued(alarm.Id);
                }
                else if (GameManager.Instance.PlayerEntity.CrimeCommitted == PlayerEntity.Crimes.None)
                    GameManager.Instance.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.Assault;
                WitnessReportNotice(alarm);
            }
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
            if(incident!=null && incident.ThreatId!=null) { ReportHostileThreat(incident,reportFrom,Now()); return; }
            if (!OwnsTown() || GameManager.Instance.PlayerEnterExit.IsPlayerInside || incident == null ||
                !incident.HasLocation || incident.BuildingKey != -1 || incident.SettlementId != directory.MapID) return;
            double now = Now();
            if (incident.Corpse == null || now < incident.StartedAt || incident.CrimeResolved ||
                (now >= incident.ExpiresAt && !incident.PlayerAttributed)) return;
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
            var sent = Roster.Dispatch(id, now, routes, Mathf.Clamp(MaximumResponders, 1, 8), 2.8, DaggerfallUnity.Instance.WorldTime.TimeScale);
            if (sent.Count == 0) return;
            reportId = id;
            GameManager.Instance.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.Murder;
            if (incident.PlayerAttributed) worldStore.MarkMurderAccusationIssued(incident.Id);
            WitnessReportNotice(incident);
            Debug.Log("LIVING_CITIES_GUARD_WITNESS_REPORT: " + id + "; responders=" + sent.Count);
        }
        private void WitnessReportNotice(CivilianIncident incident)
        {
            var streets=GetComponent<StreetPopulation>();
            if(incident.ThreatId!=null || !streets ||
                !incident.People.Exists(p=>p.DirectWitness && streets.IsLivingResident(p.ResidentId))) return;
            DaggerfallUI.AddHUDText("A witness has reported your crime to the guards.");
            Debug.Log("LIVING_CITIES_WITNESS_REPORT_NOTICE: "+incident.Id);
        }
        private GuardActor View(GuardRecord guard, double now)
        {
            GuardActor actor;
            bool indoors=guard.Interior!=null || GuardActor.RefugeIndoors(guard,now);
            if (actors.TryGetValue(guard.Id,out actor) && actor && actor.IsInterior!=indoors)
            {
                actor.CaptureEffects(); actor.EndCombat(); actor.gameObject.SetActive(false); Destroy(actor.gameObject);
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
        { foreach (var pair in actors) if (pair.Value) { pair.Value.Capture(Now()); pair.Value.CaptureEffects(); } }
        private void OnEnable()
        {
            PlayerEnterExit.OnPreTransition += CaptureBeforeTransition;
            PlayerEnterExit.OnTransitionExterior += ObserveTransition;
            PlayerEnterExit.OnTransitionInterior += ObserveTransition;
            PlayerEnterExit.OnTransitionDungeonInterior += ObserveTransition;
        }
        private void OnDisable()
        {
            PlayerEnterExit.OnPreTransition -= CaptureBeforeTransition;
            PlayerEnterExit.OnTransitionExterior -= ObserveTransition;
            PlayerEnterExit.OnTransitionInterior -= ObserveTransition;
            PlayerEnterExit.OnTransitionDungeonInterior -= ObserveTransition;
        }
        private void CaptureBeforeTransition(PlayerEnterExit.TransitionEventArgs args)
        {
            // Native exterior transition destroys the interior before the next
            // Update. Capture while its actors and effect managers still exist.
            if(Roster!=null) Capture();
        }
        private void ObserveTransition(PlayerEnterExit.TransitionEventArgs args)
        {
            if(worldStore==null || Roster==null || !directory) return;
            double now=Now();
            if(args.TransitionType==PlayerEnterExit.TransitionType.ToBuildingExterior &&
                GameManager.Instance.PlayerEntity.CrimeCommitted!=PlayerEntity.Crimes.None)
                foreach(var pair in actors)
                {
                    var actor=pair.Value;
                    var guard=Roster.Guards.Find(g=>g.Id==pair.Key);
                    if(!actor || !actor.IsInterior || !actor.CombatReady || actor.DefenseTarget ||
                        !actor.GetComponent<EnemyMotor>().IsHostile || guard==null || !guard.IsAlive ||
                        guard.Interior==null || guard.Interior.Returning) continue;
                    actor.Capture(now); Roster.PursueOutside(guard.Id,now); actor.EndCombat();
                    Debug.Log("LIVING_CITIES_GUARD_DOOR_PURSUIT: "+guard.Id+"; exitAt="+
                        (guard.Interior==null ? now : guard.Interior.ExitAt));
                }
            UpdateInvestigations(now);
        }
        private void Update()
        {
            long started=RuntimeTiming.Start();
            try { UpdatePopulation(); }
            finally { RuntimeTiming.End("guard-population",started); }
        }
        private void UpdatePopulation()
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
                interiorNavigation = interior ? new InteriorGeometry(interior) : null;
                entryGround = interiorNavigation == null ? (Vector3?)null : interiorNavigation.EntryGround;
            }
            Roster.Advance(now);
            ReleaseSafeRefuges(now);
            long gateStarted=RuntimeTiming.Start();
            try { UpdateGateGuardDuty(now); }
            finally { RuntimeTiming.End("guard-gate-duty",gateStarted); }
            if (Roster.Recruit(now) > 0)
            {
                if (patrolSurvey != null) StopCoroutine(patrolSurvey);
                patrolTemplates.Clear();
                foreach (var pair in Roster.Patrols) patrolTemplates.Add(PatrolPairPlan.CopyValidated(pair.Plan));
                patrolSurvey = StartCoroutine(SurveyPatrols());
            }
            if (Time.unscaledTime >= nextPatrolTick)
            { nextPatrolTick = Time.unscaledTime + 1; UpdateNightPatrols(now); UpdateDayRest(now); }
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
            if (interior) { if(routeTick) RefreshInteriorThreats(); UpdateInterior(now); return; }
            UpdatePostMigration();
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
                            { guard.Id, new List<SavedRoutePoint> { actual, actual } } }, 1, 2.8, DaggerfallUnity.Instance.WorldTime.TimeScale);
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
                            if (route.Count >= 2) Roster.ReturnFromEngagement(guard.Id, now, route, 2.8, DaggerfallUnity.Instance.WorldTime.TimeScale);
                        }
                    }
                    else actor.BeginCombat(now);
                }
                else
                {
                    actor.Place(now);
                    if(TryResumeKnownPursuit(guard,actor,now)) continue;
                    if ((guard.Duty == GuardDuty.Responding || guard.Duty == GuardDuty.Searching) &&
                        game.PlayerEntity.CrimeCommitted != PlayerEntity.Crimes.None && !game.PlayerEntity.IsInvisible &&
                        CivilianResponses.Sees(actor.transform.position, game.PlayerMotor.transform.position, 20)) actor.BeginCombat(now);
                }
            }
        }
        private float nextPursuitRecruitment;
        private void LateUpdate()
        {
            if(OwnsTown() && !GameManager.IsGamePaused && Time.unscaledTime>=nextPursuitRecruitment)
            { nextPursuitRecruitment=Time.unscaledTime+0.5f; RecruitNearbyPursuitGuards(Now()); }
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
                { if (pair.Value) { pair.Value.CaptureEffects(); pair.Value.EndCombat(); pair.Value.gameObject.SetActive(false); Destroy(pair.Value.gameObject); } expired.Add(pair.Key); }
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
                    if(report.ThreatId!=null && worldStore!=null)
                    {
                        var incident=worldStore.Snapshot().AlarmIncidents.Find(a=>a.ThreatId==report.ThreatId &&
                            a.BuildingKey==report.BuildingKey && a.HasLocation);
                        // Alarms store ground points; observations store actor origins one metre above ground.
                        if(incident!=null) guard.Interior.LastSeenSuspect=new GuardPoint3 { X=incident.X,Y=incident.Y+1,Z=incident.Z };
                    }
                }
                if (guard.Interior == null || guard.Interior.BuildingKey != interior.EntryDoor.buildingKey) continue;
                var actor = View(guard, now); actor.gameObject.SetActive(true);
                if (guard.Interior.Returning) { actor.EndCombat(); actor.Place(now); continue; }
                if(UpdateInteriorEnemyDefense(guard,actor,report,now)) continue;
                if (game.PlayerEntity.CrimeCommitted == PlayerEntity.Crimes.None && (report == null || !report.OccupiesScene))
                {
                    actor.EndCombat(); actor.Capture(now);
                    Roster.EndInteriorSearch(guard.Id, now); actor.Place(now);
                    continue;
                }
                actor.Capture(now);
                if(game.PlayerEntity.CrimeCommitted==PlayerEntity.Crimes.None) actor.EndCombat();
                if(report!=null && report.OccupiesScene && Roster.InteriorPursuitExpired(guard.Id,now))
                {
                    actor.EndCombat(); Roster.FinishInteriorPursuit(guard.Id,now);
                    Debug.Log("LIVING_CITIES_SCENE_PURSUIT_EXPIRED: "+guard.Id+"; accusation retained");
                }
                if (report != null && report.OccupiesScene) UpdateCrimeSceneInterior(guard, report, actor, now);
                if (report != null && report.OccupiesScene && game.PlayerEntity.CrimeCommitted == PlayerEntity.Crimes.None &&
                    !game.PlayerEntity.IsInvisible && CivilianResponses.Sees(actor.transform.position, game.PlayerMotor.transform.position, 20))
                {
                    if (report.Warning == null) report.Warning = new CrimeSceneWarning();
                    if (report.Warning.Begin()) ShowSceneWarning(report, (int)Math.Ceiling(report.Warning.Remaining));
                }
                if (game.PlayerEntity.CrimeCommitted != PlayerEntity.Crimes.None && !game.PlayerEntity.IsInvisible &&
                    CivilianResponses.Sees(actor.transform.position, game.PlayerMotor.transform.position, 20))
                    actor.BeginCombat(now);
            }
        }
        private void ClearViews()
        {
            nextWitnessRoute.Clear();
            Capture();
            foreach (var pair in actors) if (pair.Value) { pair.Value.EndCombat(); Destroy(pair.Value.gameObject); }
            actors.Clear();
            crimeSceneMotions.Clear();
        }
        private void HideViews()
        { foreach (var pair in actors) if (pair.Value && pair.Value.gameObject.activeSelf) { pair.Value.Capture(Now()); pair.Value.EndCombat(); pair.Value.gameObject.SetActive(false); } }
        public void Clear()
        {
            CancelPostMigration();
            if(setupSurvey!=null) {StopCoroutine(setupSurvey); setupSurvey=null;}
            setupDirectory=null; setupGeometry=null; setupEntrances=null; setupPosts=null;
            CancelGateDutyWork();
            gateDutySurveyed=false; gateDutyPosts.Clear(); gateDutyPairs.Clear(); nextGateDutyTick=0; gateApproachesDirty=false;
            if (approachSurvey != null) { StopCoroutine(approachSurvey); approachSurvey = null; }
            if (patrolSurvey != null) { StopCoroutine(patrolSurvey); patrolSurvey = null; }
            patrolTemplates.Clear();
            defenseEnemies.Clear();
            refugeInteriors.Clear(); wallBedRoutes.Clear(); refugeGeometry=null;
            if (hooks != null) { hooks.Dispose(); hooks = null; }
            ClearViews(); interior = null; entryGround = null;
            foreach (var pair in nativeGuards) if (pair.Key) pair.Key.SetActive(pair.Value);
            failedWelfareApproaches.Clear();
            nextWelfareRelay.Clear();
            nativeGuards.Clear(); worldStore = null; directory = null; geometry = null; Roster = null; reportId = null; awaitingSight = false;
        }
        private void OnDestroy() { Clear(); }
    }
}
