using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Utility;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation : MonoBehaviour
    {
        internal bool TryCurrentNavigationBuilding(ResidentActor actor,out int buildingKey)
        {
            buildingKey=-1;
            if(!actor || interior || geometry==null || actor.IsSeated || actor.Resident==null ||
                FindActor(actor.Resident.Id)!=actor)return false;
            var native=actor.GetComponent<NativeResidentNavigation>();
            if(!native || !native.HasDestination)return false;
            float nearest=0.6f;
            foreach(var entrance in geometry.Entrances)
            {
                float distance=Vector3.Distance(native.Destination,geometry.SceneDoor(entrance.Value));
                if(distance>=nearest)continue;
                nearest=distance;buildingKey=entrance.Key;
            }
            return buildingKey>=0;
        }

        private sealed class Plan
        {
            public Resident Resident;
            public uint SurnameSeed;
            public DailySchedule Schedule;
            public DailySchedule HauntedSchedule;
            public ResidentRoutine Routine;
        }
        private BuildingDirectory directory;
        private CityNavigation navigation;
        private RoadTravelActors roadTravelActors;
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
        private ShopVisitGeometry shopGeometry;
        private readonly Dictionary<string,ShopVisitJourney> shopJourneys=new Dictionary<string,ShopVisitJourney>();
        private readonly Dictionary<string,PhysicalShopVisit> physicalShopVisits=new Dictionary<string,PhysicalShopVisit>();
        private sealed class ShopDeparture { public List<GuardPoint3> Route; public int Next,TrackedWaypoint=-1; public double StartedAt,RetryAt; public float LastTargetDistance,StalledAt,NextDetourAt; }
        private readonly Dictionary<string,ShopDeparture> shopDepartures=new Dictionary<string,ShopDeparture>();
        private readonly Dictionary<string, BystanderPosture> bystanderPostures = new Dictionary<string, BystanderPosture>();
        private sealed class BystanderRouteCache { public Vector3 Goal; public List<SavedRoutePoint> Route; public float RepathAt; }
        private readonly Dictionary<string, BystanderRouteCache> bystanderRoutes = new Dictionary<string, BystanderRouteCache>();
        private readonly HashSet<string> bystanderReturning = new HashSet<string>();
        private float bystanderUntil;
        private double shopVisitDay=-1;
        private double lastPopulationSeconds=-1;
        private bool reconstructInteriorVisits;
        private readonly Dictionary<string, int> indoorSlots = new Dictionary<string, int>();
        private SettlementPopulation population;
        private SleepingArt sleepingArt;
        private Texture2D runningPilotAtlas;
        private CivilianResponses responses;
        private IDisposable reactionHook;
        private WorldStateStore worldStore;
        private int wakeHour, sleepHour;
        private float nextThreatSurvey;
        private float nextSafetyReconciliation;
        private const int SafetyReconciliationBatchSize = 16;
        private int nextSafetyReconciliationPlan;
        public bool ReactionsEnabled { get; set; }
        private void OnEnable()
        {
            DaggerfallWorkshop.Game.UserInterfaceWindows.DaggerfallCourtWindow.OnEndPrisonTime += OnMurderSentenceServed;
            RegisterCustodyHooks();
            RegisterNpcFollowTransitions();
            GuardActor.PlayerHalted -= OnPlayerHalted;
            GuardActor.PlayerHalted += OnPlayerHalted;
        }
        private void OnDisable()
        {
            ResetInnServiceCatalogCapture();
            DaggerfallWorkshop.Game.UserInterfaceWindows.DaggerfallCourtWindow.OnEndPrisonTime -= OnMurderSentenceServed;
            if(custodyHooks!=null){custodyHooks.Dispose();custodyHooks=null;}
            UnregisterNpcFollowTransitions();
            GuardActor.PlayerHalted -= OnPlayerHalted;
            DisposeHouseholdLootConsentHook();
        }
        private void OnMurderSentenceServed()
        {
            if(worldStore!=null && GameManager.Instance.PlayerEntity.CrimeCommitted==DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.Murder)
                worldStore.ResolveAccusedPlayerMurders();
        }
        private void OnPlayerHalted(Vector3 guardPosition, Vector3 playerPosition, float audibleRange)
        {
            if (!Ready || GameManager.IsGamePaused) return;
            bystanderUntil = Time.unscaledTime + 45f;
            lastHaltGuardPosition = guardPosition;
            lastHaltPlayerPosition = playerPosition;
            lastHaltAudibleRange = audibleRange;
        }
        private Vector3 lastHaltGuardPosition, lastHaltPlayerPosition;
        private float lastHaltAudibleRange;
        private bool ApplyHaltBystander(ResidentActor actor, Vector3 player, Vector3 scheduledPosition, double seconds)
        {
            if (!actor) return false;
            string id = actor.Resident.Id;
            bool alarmActive = Time.unscaledTime <= bystanderUntil &&
                GameManager.Instance.PlayerEntity.CrimeCommitted != DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.None;
            if (!alarmActive)
            {
                bystanderPostures.Remove(id);
                if (!bystanderReturning.Contains(id)) return false;
                return ReturnBystander(actor, scheduledPosition);
            }
            float playerDistance = Vector3.Distance(actor.transform.position, player);
            bool previouslyWatching = bystanderPostures.ContainsKey(id);
            bool newlyHeard = Vector3.Distance(actor.transform.position, lastHaltGuardPosition) <= lastHaltAudibleRange &&
                CivilianResponses.Sees(actor.transform.position + Vector3.up, lastHaltGuardPosition + Vector3.up, lastHaltAudibleRange);
            if (!previouslyWatching && !newlyHeard || playerDistance > 22 ||
                !CivilianResponses.Sees(actor.transform.position + Vector3.up, player + Vector3.up, 22))
            {
                if (previouslyWatching) { bystanderPostures.Remove(id); bystanderReturning.Add(id); }
                if (!bystanderReturning.Contains(id)) return false;
                return ReturnBystander(actor, scheduledPosition);
            }
            BystanderPosture previous;
            if (!bystanderPostures.TryGetValue(id, out previous)) previous = BystanderPosture.Resume;
            var posture = CivilianResponses.Bystander(actor, player, previous);
            if (posture == BystanderPosture.Resume)
            {
                bystanderPostures.Remove(id);
                bystanderReturning.Add(id);
                return ReturnBystander(actor, scheduledPosition);
            }
            bystanderReturning.Remove(id);
            bystanderPostures[id] = posture;
            if (previous != posture) bystanderRoutes.Remove(id);
            Vector3 position = actor.transform.position - Vector3.up;
            if (posture == BystanderPosture.Watch)
            {
                actor.Place(position, player - actor.transform.position, true);
                return true;
            }
            Vector3 away = position - player; away.y = 0;
            if (away.sqrMagnitude < 0.0001f) away = actor.transform.forward;
            away.Normalize();
            float desiredDistance = BystanderResponse.RetreatDistance(actor.Resident.AppearanceSeed);
            float currentDistance = Vector3.Distance(position, player);
            if (currentDistance < desiredDistance && geometry != null)
            {
                Vector3 destination = position + away * Mathf.Min(3f, desiredDistance - currentDistance + 0.25f);
                var route = GetBystanderRoute(id, position, destination);
                if (route.Count > 1)
                {
                    Vector3 next = geometry.ScenePoint(new RoutePoint((float)route[1].X, (float)route[1].Z));
                    Vector3 step = Vector3.MoveTowards(position, next, 2.2f * Time.deltaTime) - position;
                    actor.Place(position + step, step, false);
                    return true;
                }
            }
            actor.Place(position, away, true);
            return true;
        }
        private List<SavedRoutePoint> GetBystanderRoute(string id, Vector3 fromPosition, Vector3 destination)
        {
            BystanderRouteCache cache;
            bool stale = !bystanderRoutes.TryGetValue(id, out cache) || cache.Route == null || cache.Route.Count < 2 ||
                Vector3.Distance(cache.Goal, destination) > 1.25f || Time.unscaledTime >= cache.RepathAt;
            if (stale)
            {
                Vector3 from = directory.transform.InverseTransformPoint(fromPosition);
                Vector3 to = directory.transform.InverseTransformPoint(destination);
                var route = geometry.RouteBetweenPoints(new SavedRoutePoint { X = from.x, Z = from.z }, new SavedRoutePoint { X = to.x, Z = to.z });
                cache = new BystanderRouteCache { Goal = destination, Route = route, RepathAt = Time.unscaledTime + 0.75f };
                bystanderRoutes[id] = cache;
            }
            return cache.Route;
        }
        private bool ReturnBystander(ResidentActor actor, Vector3 scheduledPosition)
        {
            string id = actor.Resident.Id;
            Vector3 position = actor.transform.position - Vector3.up;
            if (Vector3.Distance(position, scheduledPosition) <= 0.35f)
            {
                bystanderReturning.Remove(id); bystanderRoutes.Remove(id); return false;
            }
            if (geometry != null)
            {
                var route = GetBystanderRoute(id, position, scheduledPosition);
                if (route.Count > 1)
                {
                    Vector3 next = geometry.ScenePoint(new RoutePoint((float)route[1].X, (float)route[1].Z));
                    Vector3 step = Vector3.MoveTowards(position, next, 1.5f * Time.deltaTime) - position;
                    actor.Place(position + step, step);
                    return true;
                }
            }
            actor.Place(position, scheduledPosition - position, true);
            return true;
        }
        public void InstallReactionHook()
        {
            var type = Type.GetType("PeopleOfTheIliacBay.QuestHooks.NativeCivilianHooks, PeopleOfTheIliacBay.QuestHooks", true);
            reactionHook = (IDisposable)Activator.CreateInstance(type, new object[] { new Func<Transform, Action<Exception>>(BeginWeaponHit) });
            InstallHouseholdDoorHook();
            InstallTheftHook();
            InstallPlayerShopProvenanceHook();
            InstallCorpseTransferHook();
            InstallHouseholdLootConsentHook();
            InstallEntryAwareness();
            InstallKeeperServiceHook();
        }
        private readonly HashSet<string> concealedHits = new HashSet<string>();
        public bool IsConcealedHit(string id) { return concealedHits.Contains(id); }
        private Action<Exception> BeginWeaponHit(Transform target)
        {
            var actor = target ? target.GetComponent<ResidentActor>() : null;
            bool eligible = ReactionsEnabled && Ready && responses != null && actor && actor.Resident.IsAlive &&
                FindActor(actor.Resident.Id) == actor;
            var previousCrime = GameManager.Instance.PlayerEntity.CrimeCommitted;
            BeforeWeaponHit(target);
            if (!eligible) return BeginKeeperHit(target);
            int deathBuilding = interior ? interior.EntryDoor.buildingKey : -1;
            var deathPosition = actor.transform.parent.InverseTransformPoint(actor.transform.position - Vector3.up);
            var guards = GetComponent<GuardPopulation>();
            var alarm = worldStore.RecordViolence(population.SettlementId, actor.Resident.Id, DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
            bool concealed = interior && (actor.Resident.HomeKey == deathBuilding || alarm.People.Exists(p => p.HeardViolence)) && alarm.BuildingKey == deathBuilding &&
                guards && guards.OwnsSettlement &&
                !alarm.People.Exists(p => p.DirectWitness) && concealedHits.Add(actor.Resident.Id);
            return error => {
                if (concealed) concealedHits.Remove(actor.Resident.Id);
                if (!actor.Resident.IsAlive)
                {
                    worldStore.ConfirmCorpse(alarm.Id, DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),
                        deathBuilding, deathPosition.x, deathPosition.y, deathPosition.z);
                    worldStore.MarkPlayerMurder(alarm.Id);
                    SpreadInnAlarm(alarm, DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
                    // A civilian seeing the murder has not yet told a guard.
                    // Native WeaponDamage sets Murder immediately; retain the prior
                    // crime until physical reporting or a guard's own observation.
                    if (deathBuilding == -1 && guards && guards.OwnsExterior &&
                        !guards.Roster.Reports.Exists(r => r.Id == "civilian:" + alarm.Id && r.GuardIds.Count > 0) &&
                        GameManager.Instance.PlayerEntity.CrimeCommitted == DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.Murder)
                        GameManager.Instance.PlayerEntity.CrimeCommitted = previousCrime;
                    if (!concealed)
                    {
                        // The pre-damage witness pass records reactions, but cannot
                        // dispatch a murder report until this callback confirms death.
                        if (guards) guards.ReportCivilianCrime(alarm, actor.transform.position);
                        // Exterior reporting marks its own exact incident. A prior
                        // unrelated Murder flag is not evidence for this death.
                        if (deathBuilding >= 0 && GameManager.Instance.PlayerEntity.CrimeCommitted == DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.Murder)
                            worldStore.MarkMurderAccusationIssued(alarm.Id);
                        return;
                    }
                    worldStore.RegisterUnwitnessedScene(alarm.Id);
                    if (GameManager.Instance.PlayerEntity.CrimeCommitted == DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.Murder)
                        GameManager.Instance.PlayerEntity.CrimeCommitted = previousCrime;
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CONCEALED_DEATH: " + alarm.Id);
                }
                else if (concealed && GameManager.Instance.PlayerEntity.CrimeCommitted == DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.Murder)
                    GameManager.Instance.PlayerEntity.SpawnCityGuards(true);
            };
        }
        private void BeforeWeaponHit(Transform target)
        {
            if (!ReactionsEnabled || !Ready || responses == null || !target) return;
            var actor = target.GetComponent<ResidentActor>();
            if (!actor || !actor.Resident.IsAlive || FindActor(actor.Resident.Id) != actor) return;
            double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            responses.Murder(actor, actors.Values, id => GetState(id, now).Activity == Activity.Sleep, now,
                interior ? interior.EntryDoor.buildingKey : -1);
            ObserveKeeperViolence(worldStore.RecordViolence(population.SettlementId, actor.Resident.Id, now), actor.transform.position, now);
            PrepareIndoorEscapes(now);
        }
        private float nextEscapePreparation;
        private void PrepareIndoorEscapes(double now)
        {
            if (!interior || indoorGeometry == null) return;
            var guards = GetComponent<GuardPopulation>();
            if(!guards) return;
            List<SavedRoutePoint> outside=null;
            foreach (var witness in actors.Values)
            {
                if (!witness || !witness.Resident.IsAlive) continue;
                var alarm = worldStore.ActiveAlarm(witness.Resident.Id, now);
                if (alarm == null) continue;
                var person = alarm.People.Find(p => p.ResidentId == witness.Resident.Id);
                if (person == null || (!person.DirectWitness && !person.HeardViolence && !person.InnAlarm && !person.CorpseObserver) || person.Reaction != CivilianReaction.Flee || person.Escape != null ||
                    person.BuildingKey != interior.EntryDoor.buildingKey) continue;
                if (!WakeResidentForAlarm(witness,person)) continue;
                // Rendering can lag the authoritative alarm position while actors move.
                // Start the route at the same saved position used by CivilianEscape.
                var ground = interior.transform.TransformPoint(new Vector3((float)person.X, (float)person.Y, (float)person.Z));
                var route = indoorGeometry.RouteToExit(ground);
                if (route.Count < 2) continue;
                if(outside==null) outside=guards.WitnessRoute(interior.EntryDoor.buildingKey,now);
                if(outside==null) return;
                var escape = CivilianEscape.Create(person, now, route, outside, ResidentMovement.FleeSpeed(witness.Resident.AppearanceSeed));
                person.Escape = escape;
            }
        }
        public bool HasEscapingWitness(string victimId, double now)
        {
            if (!ReactionsEnabled || worldStore == null) return false;
            foreach (var plan in plans)
            {
                if (!plan.Resident.IsAlive) continue;
                var alarm = worldStore.ActiveAlarm(plan.Resident.Id, now);
                if (alarm == null || alarm.VictimId != victimId) continue;
                var p = alarm.People.Find(person => person.ResidentId == plan.Resident.Id);
                if (p.Escape != null && (p.DirectWitness || p.HeardViolence || p.InnAlarm) && p.Reaction == CivilianReaction.Flee) return true;
            }
            return false;
        }
        private bool WakeResidentForAlarm(ResidentActor actor, AlarmPerson person)
        {
            if (!actor.IsSleeping) return true;
            var pose=actor.GetComponent<SleepingPose>();
            if (person.Escape!=null) { pose.Wake(); return true; }
            Vector3 ground;
            if (!pose.WakeOnFloor(indoorGeometry,out ground)) return false;
            var local=interior.transform.InverseTransformPoint(ground);
            person.HasPosition=true; person.BuildingKey=interior.EntryDoor.buildingKey;
            person.X=local.x; person.Y=local.y; person.Z=local.z;
            return true;
        }
        private AlarmPerson Reaction(string id, double now)
        {
            if(!ReactionsEnabled) return null;
            var guardFlight=ActiveLawfulGuardFlight(id);
            if(guardFlight!=null)return guardFlight;
            AlarmPerson grudge;if(partnerRetaliation.TryGetValue(id,out grudge)) return grudge;
            return responses != null ? responses.Active(id, now) : null;
        }
        private readonly Dictionary<string,AlarmPerson> partnerRetaliation=new Dictionary<string,AlarmPerson>();
        private readonly Dictionary<string,float> nextGrudgeCheck=new Dictionary<string,float>();
        private void RecognizePartnerKiller(ResidentActor actor,double now,Transform origin,int building)
        {
            if(!ReactionsEnabled || actor.IsSleeping || GameManager.IsGamePaused || GameManager.Instance.PlayerEntity.IsInvisible) return;
            float next;if(nextGrudgeCheck.TryGetValue(actor.Resident.Id,out next) && Time.time<next) return;
            nextGrudgeCheck[actor.Resident.Id]=Time.time+.5f;
            if(!worldStore.HoldsPartnerGrudge(actor.Resident,now) ||
                !CivilianResponses.Sees(actor.transform.position,GameManager.Instance.PlayerMotor.transform.position,20)) return;
            var point=origin.InverseTransformPoint(actor.transform.position-Vector3.up);
            partnerRetaliation[actor.Resident.Id]=new AlarmPerson {ResidentId=actor.Resident.Id,DirectWitness=true,
                Reaction=CivilianReaction.Fight,HasPosition=true,BuildingKey=building,X=point.x,Y=point.y,Z=point.z};
        }
        public bool IsAlarmed(string id)
        { return Reaction(id, DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()) != null; }
        private readonly Dictionary<int, BedAnchor> indoorBeds = new Dictionary<int, BedAnchor>();
        private readonly Dictionary<string, BedAssignment> bedClaims = new Dictionary<string, BedAssignment>();
        public void SetSleepingArt(Mod mod)
        {
            if (sleepingArt != null) sleepingArt.Dispose();
            sleepingArt = new SleepingArt(mod);
            if(keeperStandingArt!=null) keeperStandingArt.Dispose();
            keeperStandingArt=new KeeperStandingArt(mod);
            if (!runningPilotAtlas) runningPilotAtlas = RunningPilotPose.Load(mod);
        }
        public int ActiveCount { get { return actors.Count; } }
        private readonly Dictionary<string, GameObject> corpses = new Dictionary<string, GameObject>();
        public int CorpseCount { get { return corpses.Count; } }
        public int PlannedCount { get { return plans.Count; } }
        public bool Ready { get; private set; }
        public bool EntranceSurveyReady { get; private set; }
        public bool SleepEnabled { get; set; }
        public Func<string, bool> QuestOwnsResident { get; set; }
        private bool QuestOwned(Resident resident) { return QuestOwnsResident != null && QuestOwnsResident(resident.Id); }
        public bool TrySample(double seconds, out string id, out Vector3 ground)
        {
            foreach (var plan in plans)
            {
                var state = Scheduled(plan,seconds);
                if (!plan.Resident.IsAlive || state.Activity != Activity.Travel || state.RouteProgress < 0.1 || state.RouteProgress > 0.9 ||
                    Scheduled(plan,seconds + 60).Activity != Activity.Travel) continue;
                id = plan.Resident.Id; ground = geometry.ScenePoint(state.Position); return true;
            }
            id = null; ground = Vector3.zero; return false;
        }
        public ResidentActor FindActor(string id) { ResidentActor actor; return actors.TryGetValue(id, out actor) ? actor : null; }
        public void ReleaseTransferredActor(ResidentActor actor)
        {
            if(actor && FindActor(actor.Resident.Id)==actor)
            { actors.Remove(actor.Resident.Id); bystanderPostures.Remove(actor.Resident.Id); }
        }
        public bool TryReceiveRoadActor(ResidentActor actor,WorldStateStore store,double seconds)
        {
            ActivitySnapshot destination;
            bool visiting=actor && population!=null && store.TryDestinationResidentState(actor.Resident.Id,population.SettlementId,seconds,out destination);
            if(!Ready || !directory || !navigation || interior || !actor || !actor.Resident.IsAlive ||
                !ReferenceEquals(worldStore,store) || !visiting && !store.IsResidentOnHomeRoute(actor.Resident.Id,seconds) ||
                store.ResidentTripAt(actor.Resident.Id,seconds).SettlementId!=population.SettlementId ||
                !visiting && !plans.Exists(p=>ReferenceEquals(p.Resident,actor.Resident))) return false;
            var existing=FindActor(actor.Resident.Id);
            if(existing && existing!=actor) return false;
            actor.TransferOwner(directory.transform,navigation);
            actors[actor.Resident.Id]=actor;
            actor.SetRunningPilotAtlas(runningPilotAtlas);
            return true;
        }
        public bool IsLivingResident(string id) { return plans.Exists(p => p.Resident.Id == id && p.Resident.IsAlive) ||
            destinationResidents.ContainsKey(id) && destinationResidents[id].IsAlive ||
            travelerPlans.ContainsKey(id) && travelerPlans[id].Person.IsAlive ||
            (worldStore != null && population != null && NativePersonIsLivingHere(id)); }
        private bool NativePersonIsLivingHere(string id)
        {
            var person=worldStore.NativePersonInSettlement(population.SettlementId,id);
            return person!=null && person.IsAlive;
        }
        public static bool IsVisitDestination(DFLocation.BuildingTypes buildingType)
        {
            int type = (int)buildingType;
            ResidentVisitPurpose purpose;
            DailyServiceWindow window;
            return type >= 0 && type < PlayerActivate.openHours.Length &&
                TryGetDailyPurpose(buildingType, out purpose) &&
                DailyServiceHours.TryFromNativeHours(PlayerActivate.openHours[type],PlayerActivate.closeHours[type],out window);
        }
        public ActivitySnapshot GetState(string id, double seconds)
        {
            var medical=worldStore==null?null:worldStore.ReadNpcTreatmentVisit(id);
            if(medical!=null)return new ActivitySnapshot(Activity.Recovery,medical.ServiceAt>0?medical.ProviderKey:medical.Position.BuildingKey,
                new RoutePoint(medical.Position.X,medical.Position.Z),0);
            var care=worldStore==null?null:worldStore.ReadNpcHealthCare(id);
            if(care!=null && care.Phase==NpcHealthCarePhase.Acting && care.TaskId!=null && care.PausedPosition!=null &&
                !IsAlarmed(id) && !worldStore.IsResidentAway(id,seconds))
                return new ActivitySnapshot(Activity.Recovery,care.PausedPosition.BuildingKey,
                    new RoutePoint(care.PausedPosition.X,care.PausedPosition.Z),0);
            ActivitySnapshot destination;
            if(worldStore!=null && population!=null &&
                worldStore.TryDestinationResidentState(id,population.SettlementId,seconds,out destination))
                return destination.BuildingKey>=0 && ResidentInnSleeping(id,seconds) ?
                    new ActivitySnapshot(Activity.Sleep,destination.BuildingKey,destination.Position,0) : destination;
            if(worldStore!=null && worldStore.IsResidentAway(id,seconds))
                return worldStore.LocalResidentState(id,seconds,default(ActivitySnapshot));
            var plan = plans.Find(item => item.Resident.Id == id);
            if (plan == null)
            {
                TravelerPlan traveler;
                if(travelerPlans.TryGetValue(id,out traveler)) return TravelerState(traveler,seconds);
                throw new ArgumentException("Unknown resident schedule.");
            }
            ActivitySnapshot joint;
            return TryHouseholdJointTravelState(plan,out joint)?joint:EntryState(id, seconds, Scheduled(plan,seconds));
        }
        public void SetTown(BuildingDirectory town, CityNavigation nav, SettlementPopulation population, WorldStateStore store, int wake, int sleep)
        {
            wakeHour = wake; sleepHour = sleep;
            if (!SettlementClassification.SupportsPopulation((int)town.LocationData.MapTableData.LocationType) ||
                (town.MapID==1291010263 && DaggerfallPopulationProbe.SuppressCityActors)) { Clear(); return; }
            if (directory == town && this.population == population &&
                !(needsExterior && !GameManager.Instance.PlayerEnterExit.IsPlayerInside)) return;
            Clear(); directory = town; navigation = nav; this.population = population; worldStore = store;
            roadTravelActors=GetComponent<RoadTravelActors>();
            responses = new CivilianResponses(store, population.SettlementId, (alarm, position) => {
                var guards = GetComponent<GuardPopulation>();
                if (guards) guards.ReportCivilianCrime(alarm, position);
            },CombatActionOwnsThreatResponse);
            building = StartCoroutine(Build(population, store, wake, sleep));
        }
        private IEnumerator Build(SettlementPopulation population, WorldStateStore store, int wake, int sleep)
        {
            OutdoorRoutesPrepared=false;
            EntranceSurveyReady=false;
            // Ensure StartCoroutine returns its handle before any fast-path clears it.
            yield return null;
            var saved = store.GetRoutines(population.SettlementId);
            bool upgrade = saved != null && saved.ItineraryVersion < 1;
            if (saved != null && !upgrade)
            {
                geometry = new TownGeometry(directory, navigation, false);
                bool addedMissingRoutine=false;
                foreach (var home in population.Households)
                    foreach (var resident in home.Residents)
                    {
                        if (resident.IsQuestProtected) continue;
                        var routine = saved.Residents.Find(item => item.ResidentId == resident.Id);
                        if(routine==null)
                        {
                            routine=new ResidentRoutine {ResidentId=resident.Id};
                            saved.Residents.Add(routine);addedMissingRoutine=true;
                        }
                        AddPlan(resident, routine, home.SurnameSeed, wake, sleep);
                    }
                if(addedMissingRoutine) store.SetRoutines(saved);
                Coroutine activeBuild = building;
                FinishBuild(population.SettlementId);
                // Saved routes restore immediately, but doors are scene geometry.
                // Survey them in slices so shelter and destination lookups work
                // after loading a save without adding a synchronous town stall.
                building = activeBuild;
                var savedEntranceSteps = geometry.ReadEntranceSteps();
                while (true)
                {
                    while (GameManager.Instance.PlayerEnterExit.IsPlayerInside || GameManager.IsGamePaused)
                        yield return null;
                    if (!directory || !ReferenceEquals(this.population, population) ||
                        !ReferenceEquals(worldStore, store)) { building = null; yield break; }
                    if (!savedEntranceSteps.MoveNext()) break;
                    yield return savedEntranceSteps.Current;
                }
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TOWN_ENTRANCES_READY: map=" + population.SettlementId +
                    "; entrances=" + geometry.Entrances.Count);
                EntranceSurveyReady=true;
                var outdoorPreparation=PrepareSavedOutdoorRoutines(population,store,saved,wake,sleep);
                var outdoorFrames=OutdoorPreparationFrames(outdoorPreparation,()=>directory && ReferenceEquals(this.population,population) && ReferenceEquals(worldStore,store));
                try {while(outdoorFrames.MoveNext()) yield return null;}
                finally {var disposable=outdoorFrames as IDisposable;if(disposable!=null)disposable.Dispose();}
                building = null;
                yield break;
            }
            // Old interior saves have no routes yet. Display household residents, then
            // perform the first exterior survey after the player steps outside.
            if (GameManager.Instance.PlayerEnterExit.IsPlayerInside)
            {
                needsExterior = true;
                if (saved != null) geometry = new TownGeometry(directory, navigation, false);
                foreach (var home in population.Households)
                    foreach (var resident in home.Residents)
                        if (!resident.IsQuestProtected)
                        {
                            var routine = saved == null ? null : saved.Residents.Find(item => item.ResidentId == resident.Id);
                            if (routine == null) routine = new ResidentRoutine { ResidentId = resident.Id };
                            AddPlan(resident, routine, home.SurnameSeed, wake, sleep);
                        }
                Ready = true; building = null;
                yield break;
            }
            // CityNavigation resolves its location reference in Start, after activation.
            yield return null;
            geometry = new TownGeometry(directory, navigation, false);
            // Exterior route preparation can pause when the player enters a building.
            // Keep household/interior simulation ready independently of that work,
            // including the first visit to a settlement with no saved routes.
            if (saved == null) saved = new TownRoutineState { SettlementId = population.SettlementId };
            {
                foreach (var home in population.Households)
                    foreach (var resident in home.Residents)
                    {
                        if (resident.IsQuestProtected) continue;
                        var routine = saved.Residents.Find(item => item.ResidentId == resident.Id);
                        if (routine == null)
                        {
                            routine = new ResidentRoutine { ResidentId = resident.Id };
                            saved.Residents.Add(routine);
                        }
                        AddPlan(resident, routine, home.SurnameSeed, wake, sleep);
                    }
                // Keep old routines and actors available while new route pairs are surveyed.
                Coroutine activeBuild = building;
                FinishBuild(population.SettlementId);
                building = activeBuild;
            }

            // Entrance surveying performs exterior physics queries. Pause every step
            // while an interior is active, including if the player enters mid-survey.
            var entranceSteps = geometry.ReadEntranceSteps();
            while (true)
            {
                while (GameManager.Instance.PlayerEnterExit.IsPlayerInside) yield return null;
                if (!entranceSteps.MoveNext()) break;
                yield return entranceSteps.Current;
            }
            EntranceSurveyReady=true;
            var destinations = new List<BuildingSummary>();
            foreach (var pair in geometry.Entrances)
            {
                BuildingSummary summary;
                if (!directory.GetBuildingSummary(pair.Key, out summary)) continue;
                if (IsVisitDestination(summary.BuildingType))
                    destinations.Add(summary);
            }
            dailyRouteCache.Clear();
            dailyRouteCacheOrder.Clear();
            dailyRouteCacheNodes.Clear();
            foreach (var home in population.Households)
            {
                foreach (var resident in home.Residents)
                {
                    if (resident.IsQuestProtected) continue;
                    var routine = saved.Residents.Find(item => item.ResidentId == resident.Id);
                    if (routine == null)
                    {
                        routine = new ResidentRoutine { ResidentId = resident.Id };
                        saved.Residents.Add(routine);
                    }

                    if (resident.IsAlive && resident.AgeGroup == ResidentAgeGroup.Adult && resident.NativeIdentity == null &&
                        geometry.Entrances.ContainsKey(home.BuildingKey))
                    {
                        var options = SelectDailyItineraryOptions(destinations, resident, home.BuildingKey);
                        var buildingKeys = new List<int> { home.BuildingKey };
                        foreach (var option in options) buildingKeys.Add(option.BuildingKey);
                        var residentRoutes = new Dictionary<DailyRouteKey, DailyRoute>();

                        // Survey only each resident's bounded candidate graph. The shared
                        // cache reuses paths across residents; reverse paths reuse the same
                        // validated static route in the opposite direction.
                        for (int fromIndex = 0; fromIndex < buildingKeys.Count; fromIndex++)
                        for (int toIndex = 0; toIndex < buildingKeys.Count; toIndex++)
                        {
                            int from = buildingKeys[fromIndex], to = buildingKeys[toIndex];
                            if (from == to || GetDailyRoute(from, to, residentRoutes) != null) continue;
                            var routeResult = new TownGeometry.RouteResult();
                            Func<bool> stillCurrent = () => directory && directory.gameObject.activeInHierarchy &&
                                ReferenceEquals(this.population, population) && ReferenceEquals(worldStore, store);
                            var routeWork = geometry.ReadRouteSteps(geometry.Entrances[from], geometry.Entrances[to],
                                routeResult, stillCurrent);
                            bool routeFinished = false;
                            try
                            {
                                while (!routeFinished)
                                {
                                    while (GameManager.Instance.PlayerEnterExit.IsPlayerInside || GameManager.IsGamePaused)
                                        yield return null;
                                    if (!stillCurrent()) yield break;
                                    long sliceStarted = RuntimeTiming.Start("daily-route-slice");
                                    var budget = System.Diagnostics.Stopwatch.StartNew();
                                    try
                                    {
                                        do
                                        {
                                            if (GameManager.Instance.PlayerEnterExit.IsPlayerInside || GameManager.IsGamePaused ||
                                                !stillCurrent()) break;
                                            routeFinished = !routeWork.MoveNext();
                                        }
                                        while (!routeFinished && budget.Elapsed.TotalMilliseconds < 4);
                                    }
                                    finally { RuntimeTiming.End("daily-route-slice", sliceStarted); }
                                    if (!routeFinished) yield return null;
                                }
                            }
                            finally { (routeWork as IDisposable)?.Dispose(); }
                            if (routeResult.Points == null) yield break;
                            residentRoutes[new DailyRouteKey { From = from, To = to }] =
                                SurveyDailyRoute(from, to, routeResult.Points);
                            yield return null;
                        }

                        var routes = new List<AdultDailyItineraryLeg>();
                        for (int fromIndex = 0; fromIndex < buildingKeys.Count; fromIndex++)
                        for (int toIndex = 0; toIndex < buildingKeys.Count; toIndex++)
                        {
                            int from = buildingKeys[fromIndex], to = buildingKeys[toIndex];
                            if (from == to) continue;
                            var route = GetDailyRoute(from, to, residentRoutes);
                            if (route == null || route.Points.Count < 2 || route.WalkSeconds <= 0) continue;
                            var leg = new AdultDailyItineraryLeg { FromBuildingKey = from, ToBuildingKey = to,
                                WalkSeconds = route.WalkSeconds*ResidentMovement.WalkSpeed/ResidentMovement.WalkSpeedFor(resident.AppearanceSeed) };
                            foreach (var point in route.Points)
                                leg.Route.Add(new SavedRoutePoint { X = point.X, Z = point.Z });
                            routes.Add(leg);
                        }

                        if (options.Count > 0 && routes.Count > 0)
                        {
                            double departure = DailyDepartureSeconds(resident, wake, sleep);
                            double latestHome = DailyReturnDeadline(sleep);
                            int minimumStops = Math.Min(3, options.Count);
                            int maximumStops = Math.Min(5, options.Count);
                            int[] activeMinimums = options.Count >= 3 ? new[] { 3 * 3600, 2 * 3600, 90 * 60 } :
                                options.Count == 2 ? new[] { 90 * 60, 45 * 60 } : new[] { 20 * 60 };
                            for (int stopFloor = minimumStops; stopFloor >= 1 && routine.Itinerary == null; stopFloor--)
                            for (int activeIndex = 0; activeIndex < activeMinimums.Length && routine.Itinerary == null; activeIndex++)
                            {
                                var request = new AdultDailyItineraryRequest { HomeBuildingKey = home.BuildingKey,
                                    DepartureAt = departure, LatestHomeAt = latestHome,
                                    MinimumAwaySeconds = activeMinimums[activeIndex], MinimumStops = stopFloor,
                                    MaximumStops = maximumStops, Seed = resident.AppearanceSeed,
                                    Options = options, Routes = routes };
                                ResidentDestination itinerary;
                                bool planned;
                                long planStarted = RuntimeTiming.Start("daily-itinerary-plan");
                                try
                                {
                                    planned = AdultDailyItineraryPlanner.TryCreate(request, out itinerary);
                                }
                                catch (ArgumentException) { continue; }
                                finally { RuntimeTiming.End("daily-itinerary-plan", planStarted); }
                                if (!planned) continue;
                                routine.Itinerary = itinerary;
                                routine.ItineraryDepartureSeconds = departure;
                                KeepFirstStopAsLegacyRoute(routine, itinerary);
                            }
                        }

                        // Preserve original single-destination route as a fallback when
                        // an itinerary cannot be planned or later gets interrupted.
                        if (routine.DestinationKey < 0)
                            foreach (var option in options)
                            {
                                // The legacy day visit has no social daypart. Let
                                // the evening outing planner handle Inn-only residents.
                                if (option.Purpose == ResidentVisitPurpose.Social) continue;
                                var route = GetDailyRoute(home.BuildingKey, option.BuildingKey, residentRoutes);
                                if (route == null || route.Points.Count < 2) continue;
                                routine.DestinationKey = option.BuildingKey;
                                routine.OpenMinute = option.OpenMinute;
                                routine.CloseMinute = option.CloseMinute;
                                routine.Route = new List<SavedRoutePoint>();
                                foreach (var point in route.Points)
                                    routine.Route.Add(new SavedRoutePoint { X = point.X, Z = point.Z });
                                routine.RouteGeometryVersion=OutdoorRoutineMigration.CenterlineVersion;
                                break;
                            }
                    }

                    // Do not publish a visit before its routine exists in the save
                    // ledger. Interior entry can save observed visits mid-survey.
                }
                // Bounded incremental layout work avoids a single long main-thread stall.
                yield return null;
            }
            saved.ItineraryVersion = 1;
            long commitStarted = RuntimeTiming.Start("daily-routine-commit");
            try { store.SetRoutines(saved); }
            finally { RuntimeTiming.End("daily-routine-commit", commitStarted); }
            var personalPreparation=PrepareSavedOutdoorRoutines(population,store,saved,wake,sleep);
            var personalFrames=OutdoorPreparationFrames(personalPreparation,()=>directory && ReferenceEquals(this.population,population) && ReferenceEquals(worldStore,store));
            try {while(personalFrames.MoveNext()) yield return null;}
            finally {var disposable=personalFrames as IDisposable;if(disposable!=null)disposable.Dispose();}
            needsExterior = false;
            OutdoorRoutesPrepared=true;
            building = null;
        }
        private void AddPlan(Resident resident, ResidentRoutine routine, uint surnameSeed, int wake, int sleep)
        {
            // The native actor already represents this exact household member.
            if (worldStore.NativePeopleFor(population.SettlementId).Exists(p => p.Id == resident.Id)) return;
            DailySchedule schedule,haunted;
            BuildOutdoorSchedules(resident,routine,wake,sleep,out schedule,out haunted);
            plans.Add(new Plan { Resident = resident, Schedule = schedule, HauntedSchedule=haunted, Routine = routine,
                SurnameSeed = surnameSeed });
        }
        private void BuildOutdoorSchedules(Resident resident,ResidentRoutine routine,int wake,int sleep,
            out DailySchedule schedule,out DailySchedule haunted)
        {
            int hauntedWake=population.SettlementId==1291010263?Math.Max(wake*60,300):wake*60;
            int hauntedSleep=population.SettlementId==1291010263?Math.Min(sleep*60,1140):sleep*60;
            var points = new List<RoutePoint>();
            foreach (var point in OutdoorRoutineMigration.RuntimeRoute(routine,route=>OffsetSavedRoute(route,resident.AppearanceSeed)))
                points.Add(new RoutePoint(point.X,point.Z));
            schedule = routine.DestinationKey < 0 ? DailySchedule.HomeDay(resident.HomeKey, wake * 60, sleep * 60) :
                DailySchedule.DayVisit(resident.HomeKey, routine.DestinationKey, points, routine.OpenMinute, routine.CloseMinute,
                    20 + (int)(resident.AppearanceSeed % 41), ResidentMovement.WalkSpeedFor(resident.AppearanceSeed), DaggerfallUnity.Instance.WorldTime.TimeScale, resident.AppearanceSeed, wake * 60, sleep * 60);
            haunted=population.SettlementId!=1291010263 ? schedule : routine.DestinationKey<0 ?
                DailySchedule.HomeDay(resident.HomeKey,hauntedWake,hauntedSleep) :
                DailySchedule.DayVisit(resident.HomeKey,routine.DestinationKey,points,routine.OpenMinute,routine.CloseMinute,
                    20+(int)(resident.AppearanceSeed%41),ResidentMovement.WalkSpeedFor(resident.AppearanceSeed),DaggerfallUnity.Instance.WorldTime.TimeScale,resident.AppearanceSeed,hauntedWake,hauntedSleep,300,1140);
            if(routine.Itinerary!=null)
            {
                try
                {
                    var children=new List<DailySchedule>();var safeChildren=new List<DailySchedule>();int offset=0;
                    foreach(var outing in ResidentRoutineDay.CopyOutings(routine))
                    {
                        var laneSource=new ResidentRoutine{Itinerary=outing.Itinerary,PersonalLaneVersion=routine.PersonalLaneVersion};
                        var itinerary=OffsetStreetItinerary(laneSource,resident.AppearanceSeed);
                        var child=DailySchedule.ItineraryDay(resident.HomeKey,itinerary,outing.DepartureSeconds,wake*60,sleep*60,offset);
                        children.Add(child);
                        var journey=ResidentVisitJourney.Create(outing.DepartureSeconds,itinerary);
                        if(population.SettlementId!=1291010263 || outing.DepartureSeconds>=300*60 && journey.EndsAt<=1140*60)
                            safeChildren.Add(population.SettlementId!=1291010263?child:
                                DailySchedule.ItineraryDay(resident.HomeKey,itinerary,outing.DepartureSeconds,hauntedWake,hauntedSleep,offset));
                        offset+=itinerary.Stops.Count;
                    }
                    schedule=DailySchedule.OutingsDay(resident.HomeKey,children,wake*60,sleep*60);
                    // Preserve the old single-outing fallback. A composed day may
                    // retain safe outings without admitting an unsafe later one.
                    if(children.Count>1 || safeChildren.Count==children.Count)
                        haunted=DailySchedule.OutingsDay(resident.HomeKey,safeChildren,hauntedWake,hauntedSleep);
                }
                catch(ArgumentException error)
                {
                    Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_DAILY_PLAN_FALLBACK: "+resident.Id+"; "+error.Message);
                }
            }
        }
        private ActivitySnapshot Scheduled(Plan plan,double seconds)
        {
            ActivitySnapshot paidDoorState;
            if(TryScheduledPaidDoorPlan(plan,seconds,out paidDoorState))return paidDoorState;
            ActivitySnapshot travelerState;
            if(TryTravelerScheduled(plan,seconds,out travelerState))return travelerState;
            return worldStore.LocalResidentState(plan.Resident.Id,seconds,ClosedShopState(plan,seconds,ObservedSchedule(plan,seconds,ActiveSchedule(plan))));
        }
        private DailySchedule ActiveSchedule(Plan plan)
        {
            // Use the actual world day, not a look-ahead animation sample, to commit policy.
            bool restricted=population.SettlementId==1291010263 && worldStore.RestrictHauntingRoutines(population.SettlementId,
                GameManager.Instance.PlayerEntity.GlobalVars.GetGlobalVar(0),DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
            DailySchedule scheduled;
            if(!TryRuntimeHouseholdSchedule(plan,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),out scheduled))
                scheduled=AppointmentSchedule(plan,restricted ? plan.HauntedSchedule : plan.Schedule,restricted);
            return ClosedShopSchedule(plan,RecoveredSchedule(plan,scheduled,restricted),DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
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
            UpdateHouseholdDays(DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),true);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_STREETS_READY: residentsWithSchedules=" + plans.Count + "; map=" + settlementId);
        }
        private void Update()
        {
            UpdateCorpseInventory();
            long started=RuntimeTiming.Start("street-population");
            try { UpdatePopulation(); }
            finally { RuntimeTiming.End("street-population",started); }
        }
        private void UpdatePopulation()
        {
            if (!directory) { if (plans.Count > 0) Clear(); return; }
            var game = GameManager.Instance;
            if (!Ready || game == null || !game.IsReady || !game.IsPlayingGame()) return;
            var now = DaggerfallUnity.Instance.WorldTime.Now;
            double seconds = now.ToSeconds() + (now.Second - Math.Floor(now.Second));
            reconstructInteriorVisits=false;
            bool skipped=ShopVisitJourney.NeedsCatchup(lastPopulationSeconds,seconds,
                Time.unscaledDeltaTime,DaggerfallUnity.Instance.WorldTime.TimeScale);
            var currentRoom=game.PlayerEnterExit.IsPlayerInsideBuilding?game.PlayerEnterExit.Interior:null;
            if(skipped && lastPopulationSeconds>=0)YieldHouseholds(lastPopulationSeconds);
            if(skipped && interior && ReferenceEquals(interior,currentRoom))
            {
                // Capture the last observed moment, before the skipped time. The
                // saved remaining route then advances analytically to the new time.
                CaptureObservedVisits(lastPopulationSeconds);
                foreach(var id in physicalShopVisits.Keys) if(shopGeometry!=null)shopGeometry.ReleaseCounter(id);
                ClearActors();indoorSlots.Clear();shopDepartures.Clear();
                reconstructInteriorVisits=true;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INDOOR_TIME_CATCHUP: from="+lastPopulationSeconds+"; to="+seconds);
            }
            lastPopulationSeconds=seconds;
            long phase=RuntimeTiming.Start("household-days");
            try { UpdateHouseholdDays(seconds); }
            finally { RuntimeTiming.End("household-days",phase); }
            if (ReactionsEnabled && worldStore != null && !GameManager.IsGamePaused)
            {
                if (Time.unscaledTime >= nextSafetyReconciliation)
                {
                    nextSafetyReconciliation = Time.unscaledTime + 2f;
                    int reconcileCount = Math.Min(SafetyReconciliationBatchSize, plans.Count);
                    for (int i = 0; i < reconcileCount; i++)
                    {
                        if (nextSafetyReconciliationPlan >= plans.Count) nextSafetyReconciliationPlan = 0;
                        var plan = plans[nextSafetyReconciliationPlan++];
                        NpcNativeSafetyGoalLifecycle.TryReconcile(worldStore, plan.Resident.Id, seconds,
                            plan.Resident.IsAlive, plan.Resident.IsQuestProtected);
                    }
                    if (nextSafetyReconciliationPlan >= plans.Count) nextSafetyReconciliationPlan = 0;
                }
                if (Time.unscaledTime >= nextEscapePreparation)
                {
                    nextEscapePreparation = Time.unscaledTime + 0.5f;
                    PrepareIndoorEscapes(seconds);
                }
                var guards = GetComponent<GuardPopulation>();
                foreach (var alarm in worldStore.AdvanceEscapes(population.SettlementId, seconds,true,
                    guards ? (Action<CivilianIncident,AlarmPerson,double>)guards.PrepareWitnessAdvance : null))
                    if (guards) guards.ReportEscapedWitness(alarm, seconds);
            }
            var loadedInterior = game.PlayerEnterExit.IsPlayerInsideBuilding ? game.PlayerEnterExit.Interior : null;
            // Unity treats a destroyed interior as equal to null. Its cached actor
            // keys still need clearing when that scene is unloaded on departure.
            if (!ReferenceEquals(interior, loadedInterior))
            {
                ResetInnServiceCatalogCapture();
                reconstructInteriorVisits=true;
                RestoreKeeperViews(); RestoreHouseFlats();
                ClearActors(); ClearCorpses(); interior = loadedInterior; indoorSlots.Clear();
                if (interior) ReconcileHouseFlats(interior);
                indoorGeometry = interior ? new InteriorGeometry(interior,CrowdMotion.BodyRadius) : null;
                shopGeometry=null;
                shopJourneys.Clear();
                ClearPhysicalShopVisits();
                shopDepartures.Clear();
                shopVisitDay=-1;
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
                if(interior)
                {
                    SurveyKeeperIdentities();
                    shopGeometry=new ShopVisitGeometry(interior,indoorGeometry,npc=>
                    {
                        var keeper=KeeperPerson(npc);
                        return keeper!=null && keeper.IsAlive;
                    });
                }
                if (interior) Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INTERIOR_ANCHORS: building=" + interior.EntryDoor.buildingKey +
                    "; anchors=" + indoorGeometry.LocalAnchors.Count);
            }
            SurveyKeeperIdentities();
            SurveyStationaryObservers();
            phase=RuntimeTiming.Start("inn-service-catalog");
            try { UpdateInnServiceCatalog(seconds); }
            finally { RuntimeTiming.End("inn-service-catalog",phase); }
            phase=RuntimeTiming.Start("bank-counter-survey");
            try { UpdateBankCounterSurvey(); }
            finally { RuntimeTiming.End("bank-counter-survey",phase); }
            phase=RuntimeTiming.Start("household-owners");
            try { PrepareHouseholdOwners(seconds); }
            finally { RuntimeTiming.End("household-owners",phase); }
            phase=RuntimeTiming.Start("traveler-actors");
            try { UpdateTravelerActors(seconds); }
            finally { RuntimeTiming.End("traveler-actors",phase); }
            phase=RuntimeTiming.Start("destination-actors");
            try { UpdateDestinationActors(seconds); }
            finally { RuntimeTiming.End("destination-actors",phase); }
            ObserveAppointments(seconds);
            UpdateKeeperResponses(seconds);
            UpdateEntryReports(seconds);
            UpdateOutdoorScenes(seconds);
            UpdateCorpses(seconds);
            if(ReactionsEnabled && responses!=null && Time.unscaledTime>=nextThreatSurvey)
            {
                nextThreatSurvey=Time.unscaledTime+0.5f;
                long threatStarted = RuntimeTiming.Start();
                try
                {
                    var guards=GetComponent<GuardPopulation>();
                    if(guards) foreach(var target in UnityEngine.Object.FindObjectsOfType<DaggerfallWorkshop.Game.Entity.DaggerfallEntityBehaviour>())
                    {
                        string key=guards.RegisterHostileThreat(target);
                        if(key!=null)
                        {
                            responses.Threat(target,key,actors.Values,seconds,interior ? interior.EntryDoor.buildingKey : -1,
                                id=>GetState(id,seconds).Activity==Activity.Sleep);
                            ObserveKeeperThreat(target,key,seconds);
                        }
                    }
                }
                finally { RuntimeTiming.End("citizen-threat-scan", threatStarted); }
            }
            if (ReactionsEnabled && responses != null)
                responses.WarnNearby(actors.Values, seconds, interior ? interior.EntryDoor.buildingKey : -1,
                    id => GetState(id, seconds).Activity == Activity.Sleep);
            AdvanceOffscreenDialogueComplaints(seconds);
            ObserveSceneMemories(seconds);
            ObserveStationaryMemories(seconds);
            PrepareStreetChats(seconds);
            if (game.PlayerEnterExit.IsPlayerInside)
            {
                if (interior) UpdateInterior(seconds); else ClearActors();
                return;
            }
            Vector3 player = game.PlayerMotor.transform.position;
            PrepareClosedShopLeisure(seconds);
            BeginHouseholdJointFrame();
            var actorSpawnBudget = new PopulationSpawnBudget(4,2);
            long actorSpawnStarted=System.Diagnostics.Stopwatch.GetTimestamp();
            foreach (var plan in RuntimePlansWithPaidDoor(seconds))
            {
                if(simulatedFamilyFrame.Contains(plan.Resident.Id))continue;
                if(pendingHouseholdInitialization.Contains(plan.Resident.Id))continue;
                if(roadTravelActors && roadTravelActors.OwnsPaidCompanion(plan.Resident.Id))continue;
                var state = worldStore.IsResidentAway(plan.Resident.Id,seconds) ? Scheduled(plan,seconds) :
                    EntryState(plan.Resident.Id, seconds, Scheduled(plan,seconds));
                ActivitySnapshot jointState;
                if(TryHouseholdJointTravelState(plan,out jointState))state=jointState;
                ResidentActor actor;
                actors.TryGetValue(plan.Resident.Id, out actor);
                if(actor && actor.gameObject.activeInHierarchy) RecognizePartnerKiller(actor,seconds,directory.transform,-1);
                var reaction = Reaction(plan.Resident.Id, seconds);
                if(reaction!=null || !plan.Resident.IsAlive || QuestOwned(plan.Resident))
                { worldStore.RemoveObservedVisit(plan.Resident.Id);RemovePhysicalShopVisit(plan.Resident.Id); }
                if (!interior && ApplyOutdoorBodyReport(plan,ref actor,seconds,reaction)) continue;
                if(ApplyNpcActionOverlay(plan,ref actor,seconds,reaction)) continue;
                if(reaction!=null) ClearProactiveContactMotion(plan.Resident.Id);
                else if(ApplyProactiveContactApproach(plan,ref actor,seconds)) continue;
                bool outsideSchedule=state.Activity==Activity.Travel || state.Activity==Activity.Meeting;
                Vector3 position = outsideSchedule ? geometry.ScenePoint(state.Position) : Vector3.zero;
                if (reaction != null && reaction.HasPosition)
                    position = directory.transform.TransformPoint(new Vector3((float)reaction.X, (float)reaction.Y, (float)reaction.Z));
                float limit = actor ? 165 : 140;
                bool outside = reaction != null && reaction.HasPosition ? reaction.BuildingKey == -1 && !reaction.ReachedRefuge : outsideSchedule;
                bool visible = plan.Resident.IsAlive && (!worldStore.IsResidentAway(plan.Resident.Id,seconds) ||
                    worldStore.IsResidentOnHomeRoute(plan.Resident.Id,seconds)) && !QuestOwned(plan.Resident) && outside &&
                    (position - player).sqrMagnitude <= limit * limit;
                if (HasDialogueComplaint(plan.Resident.Id))
                    visible = true;
                if (!visible)
                {
                    if (!skipped && reaction == null && actor && !worldStore.IsResidentAway(plan.Resident.Id, seconds) &&
                        !QuestOwned(plan.Resident) && ContinueLocalOutdoorArrival(plan, actor, state, seconds)) continue;
                    bystanderPostures.Remove(plan.Resident.Id);
                    if(actor && roadTravelActors && roadTravelActors.TryAdopt(actor,worldStore,seconds))
                    { actors.Remove(plan.Resident.Id); continue; }
                    if (actor) { actor.CaptureState(); actor.gameObject.SetActive(false); Destroy(actor.gameObject); }
                    actors.Remove(plan.Resident.Id); continue;
                }
                if (!actor)
                {
                    if(roadTravelActors && worldStore.IsResidentOnHomeRoute(plan.Resident.Id,seconds) &&
                        roadTravelActors.TryReturn(plan.Resident.Id,worldStore,seconds)) actor=FindActor(plan.Resident.Id);
                }
                if (!actor)
                {
                    if (actors.Count >= 80 || !actorSpawnBudget.TryConsume(
                        (System.Diagnostics.Stopwatch.GetTimestamp()-actorSpawnStarted)*1000d/System.Diagnostics.Stopwatch.Frequency)) continue;
                    phase=RuntimeTiming.Start("resident-spawn");
                    try
                    {
                        var go = Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject, directory.transform);
                        go.name = "People of the Iliac Bay " + plan.Resident.Id;
                        actor = go.AddComponent<ResidentActor>();
                        actor.Initialize(plan.Resident, plan.SurnameSeed, navigation,inventoryStore:worldStore);
                        actor.SetRunningPilotAtlas(runningPilotAtlas);
                        RestoreHouseholdJointBody(plan,actor);
                        actors.Add(plan.Resident.Id, actor);
                        go.SetActive(true);
                        ObserveHomeSleep(plan,actor,seconds);
                    }
                    finally { RuntimeTiming.End("resident-spawn",phase); }
                }
                if (reaction != null && reaction.HasPosition)
                {
                    bystanderPostures.Remove(plan.Resident.Id);
                    if (reaction.Escape != null)
                    {
                        TryRerouteEscapingWitnessToShelter(actor,reaction,seconds);
                        PlaceEscape(actor, reaction, seconds, directory.transform, true);
                        RememberNpcCareAlarmPosition(plan,actor,seconds);
                        continue;
                    }
                    Vector3? danger=null;
                    Vector3 lawfulGuard;
                    var alarm=worldStore.ActiveAlarm(plan.Resident.Id,seconds);
                    if(TryLawfulGuardFlightDanger(actor,out lawfulGuard)) danger=lawfulGuard;
                    else if(alarm!=null && (alarm.ThreatId!=null || alarm.KillerThreatId!=null))
                    {
                        Vector3 current; var guards=GetComponent<GuardPopulation>();
                        danger=guards && guards.TryHostileThreatPosition(alarm.KillerThreatId??alarm.ThreatId,out current) ? current :
                            alarm.HasLocation && alarm.BuildingKey==-1 ? directory.transform.TransformPoint(new Vector3((float)alarm.X,(float)alarm.Y,(float)alarm.Z)) :
                            actor.transform.position-Vector3.up;
                    }
                    Vector3 shelter=Vector3.zero;int shelterBuilding=-1;
                    bool hasShelter=reaction.Reaction==CivilianReaction.Flee && !reaction.ReachedRefuge &&
                        TryShelterTarget(actor,reaction,alarm,danger ?? player,seconds,out shelter,out shelterBuilding);
                    if(!hasShelter) {shelter=Vector3.zero;shelterBuilding=-1;}
                    CivilianResponses.Move(actor, reaction, plan.Routine, directory.transform, geometry, null,danger,
                        hasShelter ? (Vector3?)shelter : null,shelterBuilding,
                        ShelterAccessible(actor.Resident.HomeKey,actor.Resident.HomeKey,alarm,seconds));
                    RememberNpcCareAlarmPosition(plan,actor,seconds);
                    if(reaction.ReachedRefuge && !reaction.RefugeSealed && alarm!=null)
                    {
                        worldStore.RecordShelterArrival(alarm.Id,plan.Resident.Id,seconds);
                        NpcNativeSafetyGoalLifecycle.TryFulfillAtRefuge(worldStore, plan.Resident.Id,
                            alarm, reaction, seconds, plan.Resident.IsAlive, plan.Resident.IsQuestProtected);
                    }
                    continue;
                }
                if (PlaceDialogueComplaint(actor,seconds)) continue;
                if (ApplyHaltBystander(actor, player, position, seconds)) continue;
                if (PlaceStreetChat(actor,seconds)) continue;
                if (DeferHouseholdJointBody(plan,actor)) continue;
                if (PlaceClosedShopWait(plan,actor,state,seconds)) continue;
                var next = worldStore.IsResidentAway(plan.Resident.Id,seconds+0.1) ? Scheduled(plan,seconds+0.1) :
                    EntryState(plan.Resident.Id, seconds + 0.1, Scheduled(plan,seconds + 0.1));
                Vector3 direction = next.Activity == Activity.Travel || next.Activity==Activity.Meeting ? geometry.ScenePoint(next.Position) - position :
                    position - geometry.ScenePoint(Scheduled(plan,Math.Max(0, seconds - 0.1)).Position);
                if(skipped)
                {var native=actor.GetComponent<NativeResidentNavigation>();if(native)native.ResetPlacement();}
                actor.Place(position, direction,freeStanding:state.Activity==Activity.Home||state.Activity==Activity.Visit||state.Activity==Activity.Meeting,
                    navigationDestination:state.TravelDestination.HasValue?(Vector3?)geometry.ScenePoint(state.TravelDestination.Value):null);
                RecognizePartnerKiller(actor,seconds,directory.transform,-1);
            }
            TickHouseholdJointBodies(seconds);
        }
        private void UpdateInterior(double seconds)
        {
            BeginHouseholdInnFrame();
            PrepareShopVisits(seconds);
            var bodies=worldStore.ObserveCorpses(population.SettlementId,interior.EntryDoor.buildingKey,seconds);
            foreach (var plan in RuntimePlansWithPaidDoor(seconds))
            {
                if(simulatedFamilyFrame.Contains(plan.Resident.Id))continue;
                if(pendingHouseholdInitialization.Contains(plan.Resident.Id))continue;
                var state = EntryState(plan.Resident.Id, seconds, Scheduled(plan,seconds));
                ActivitySnapshot jointState;bool sharedInn=TryHouseholdJointTravelState(plan,out jointState) && jointState.Activity==Activity.Visit;
                if(sharedInn)state=jointState;
                ResidentActor actor; actors.TryGetValue(plan.Resident.Id, out actor);
                if(actor && actor.gameObject.activeInHierarchy) RecognizePartnerKiller(actor,seconds,interior.transform,interior.EntryDoor.buildingKey);
                var reaction = Reaction(plan.Resident.Id, seconds);
                if(reaction!=null || !plan.Resident.IsAlive || QuestOwned(plan.Resident))
                { worldStore.RemoveObservedVisit(plan.Resident.Id);RemovePhysicalShopVisit(plan.Resident.Id); }
                if(ApplyNpcActionOverlay(plan,ref actor,seconds,reaction)) continue;
                if(reaction!=null) ClearProactiveContactMotion(plan.Resident.Id);
                else if(ApplyProactiveContactApproach(plan,ref actor,seconds)) continue;
                bool atHome = reaction != null && reaction.HasPosition ? reaction.BuildingKey == interior.EntryDoor.buildingKey :
                    state.Activity != Activity.Travel && state.BuildingKey == interior.EntryDoor.buildingKey;
                bool present = plan.Resident.IsAlive && !worldStore.IsResidentAway(plan.Resident.Id,seconds) && !QuestOwned(plan.Resident) && atHome;
                if (!present)
                {
                    PhysicalShopVisit unfinished;
                    if(actor && plan.Resident.IsAlive && reaction==null && !QuestOwned(plan.Resident) &&
                        physicalShopVisits.TryGetValue(plan.Resident.Id,out unfinished) &&
                        unfinished.Snapshot.Phase!=ShopVisitPhase.Complete && PlaceShopVisit(actor,seconds)) continue;
                    if(actor && plan.Resident.IsAlive && reaction==null && !worldStore.IsResidentAway(plan.Resident.Id,seconds) &&
                        !QuestOwned(plan.Resident) && state.BuildingKey!=interior.EntryDoor.buildingKey &&
                        ActiveSchedule(plan).ContainsVisitBuilding(interior.EntryDoor.buildingKey,seconds) && shopJourneys.ContainsKey(plan.Resident.Id) &&
                        ContinueShopDeparture(actor,seconds)) continue;
                    if (actor) { actor.CaptureState(); actor.gameObject.SetActive(false); Destroy(actor.gameObject); }
                    actors.Remove(plan.Resident.Id); indoorSlots.Remove(plan.Resident.Id); shopDepartures.Remove(plan.Resident.Id);
                    continue;
                }
                int slot;
                if (!indoorSlots.TryGetValue(plan.Resident.Id, out slot))
                {
                    slot = 0;
                    while (slot < indoorGeometry.LocalAnchors.Count && indoorSlots.ContainsValue(slot)) slot++;
                    if (slot == indoorGeometry.LocalAnchors.Count)
                    { continue; }
                    indoorSlots.Add(plan.Resident.Id, slot);
                }
                if (!actor)
                {
                    var go = Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject, interior.transform);
                    go.name = "People of the Iliac Bay " + plan.Resident.Id;
                    actor = go.AddComponent<ResidentActor>();
                    actor.Initialize(plan.Resident, plan.SurnameSeed, navigation,inventoryStore:worldStore);
                    actor.SetRunningPilotAtlas(runningPilotAtlas);
                    actors.Add(plan.Resident.Id, actor); go.SetActive(true);
                    ObserveHomeSleep(plan,actor,seconds);
                    if(state.Activity==Activity.Visit && indoorGeometry.EntryGround.HasValue)
                        actor.transform.position=indoorGeometry.EntryGround.Value+Vector3.up;
                }
                if(reaction==null && ApplyNpcCashVisit(plan,ref actor,seconds,false))continue;
                if(reaction==null && state.Activity==Activity.Visit && !sharedInn)
                {
                    AlarmPerson departure; bool home;
                    bool hasDeparture=worldStore.TryVisitDeparturePosition(plan.Resident.Id,seconds,out departure,out home);
                    if(hasDeparture) { RemovePhysicalShopVisit(plan.Resident.Id);worldStore.RemoveObservedVisit(plan.Resident.Id); }
                    ShopVisitJourney localJourney;
                    bool failedLocalTavernVisit=shopGeometry!=null && shopGeometry.IsTavern &&
                        plan.Resident.HomeKey!=interior.EntryDoor.buildingKey &&
                        ActiveSchedule(plan).ContainsVisitBuilding(interior.EntryDoor.buildingKey,seconds) &&
                        shopJourneys.TryGetValue(plan.Resident.Id,out localJourney) && localJourney==null;
                    if(!hasDeparture && failedLocalTavernVisit)
                    {
                        var route=indoorGeometry.RouteToExit(actor.transform.position-Vector3.up);
                        var outside=DailyReturnRoute(plan.Routine,interior.EntryDoor.buildingKey);
                        if(route.Count<2 || outside.Count<2)
                        {
                            Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_TAVERN_VISIT_CANCEL_ROUTE_MISSING: "+plan.Resident.Id+
                                "; building="+interior.EntryDoor.buildingKey);
                            continue;
                        }
                        double until=(Math.Floor(seconds/86400)+1)*86400;
                        try
                        {
                            var cancelled=VisitDeparture.Create(plan.Resident.Id,population.SettlementId,plan.Resident.HomeKey,
                                interior.EntryDoor.buildingKey,seconds,until,route,outside,actor.WalkingSpeed,DaggerfallUnity.Instance.WorldTime.TimeScale);
                            worldStore.SaveVisitDeparture(cancelled);
                            hasDeparture=worldStore.TryVisitDeparturePosition(plan.Resident.Id,seconds,out departure,out home);
                        }
                        catch(System.ArgumentException)
                        {
                            Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_TAVERN_VISIT_CANCEL_REJECTED: "+plan.Resident.Id+
                                "; building="+interior.EntryDoor.buildingKey);
                            continue;
                        }
                    }
                    if(failedLocalTavernVisit && hasDeparture && !home)
                    {
                        AlarmPerson next; bool nextHome;
                        worldStore.TryVisitDeparturePosition(plan.Resident.Id,seconds+0.1,out next,out nextHome);
                        var ground=interior.transform.TransformPoint(new Vector3((float)departure.X,(float)departure.Y,(float)departure.Z));
                        Vector3 direction=next!=null && next.BuildingKey==departure.BuildingKey ?
                            interior.transform.TransformVector(new Vector3((float)(next.X-departure.X),(float)(next.Y-departure.Y),(float)(next.Z-departure.Z))) : actor.transform.forward;
                        InteriorGeometry.OpenOrdinaryDoors(actor.transform.position-Vector3.up,ground);
                        actor.Place(ground,direction);
                        continue;
                    }
                }
                if(ReactionsEnabled && reaction==null && state.Activity==Activity.Visit)
                {
                    var guards=GetComponent<GuardPopulation>();
                    AlarmPerson departure; bool home;
                    if(!sharedInn && !worldStore.TryVisitDeparturePosition(plan.Resident.Id,seconds,out departure,out home) &&
                        guards && guards.OccupiesCrimeScene(interior.EntryDoor.buildingKey,seconds))
                    {
                        var route=indoorGeometry.RouteToExit(actor.transform.position-Vector3.up);
                        var outside=DailyReturnRoute(plan.Routine,interior.EntryDoor.buildingKey);
                        if(route.Count>=2 && outside.Count>=2 && plan.Resident.HomeKey!=interior.EntryDoor.buildingKey)
                        {
                            double until=(Math.Floor(seconds/86400)+1)*86400;
                            var cancelled=VisitDeparture.Create(plan.Resident.Id,population.SettlementId,plan.Resident.HomeKey,
                                interior.EntryDoor.buildingKey,seconds,until,route,outside,actor.WalkingSpeed,DaggerfallUnity.Instance.WorldTime.TimeScale);
                            worldStore.SaveVisitDeparture(cancelled);
                        }
                    }
                    if(!sharedInn && worldStore.TryVisitDeparturePosition(plan.Resident.Id,seconds,out departure,out home) && !home)
                    {
                        AlarmPerson next; bool nextHome;
                        worldStore.TryVisitDeparturePosition(plan.Resident.Id,seconds+0.1,out next,out nextHome);
                        var ground=interior.transform.TransformPoint(new Vector3((float)departure.X,(float)departure.Y,(float)departure.Z));
                        Vector3 direction=next!=null && next.BuildingKey==departure.BuildingKey ?
                            interior.transform.TransformVector(new Vector3((float)(next.X-departure.X),(float)(next.Y-departure.Y),(float)(next.Z-departure.Z))) : actor.transform.forward;
                        InteriorGeometry.OpenOrdinaryDoors(actor.transform.position-Vector3.up,ground);
                        actor.Place(ground,direction);
                        continue;
                    }
                    var body=bodies.Find(a=>a.BuildingKey==interior.EntryDoor.buildingKey && a.ThreatId==null);
                    if(body!=null)
                    {
                        var observer=worldStore.NoticeInteriorCorpse(body.Id,plan.Resident.Id,interior.EntryDoor.buildingKey,seconds);
                        if(observer!=null && CivilianAlarms.PersonActive(body,observer,seconds))
                        {
                            if(!observer.HasPosition)
                            {
                                var local=interior.transform.InverseTransformPoint(actor.transform.position-Vector3.up);
                                observer.HasPosition=true; observer.BuildingKey=interior.EntryDoor.buildingKey;
                                observer.X=local.x; observer.Y=local.y; observer.Z=local.z;
                            }
                            reaction=observer;
                            PrepareIndoorEscapes(seconds);
                        }
                    }
                }
                if (reaction != null && reaction.HasPosition && !reaction.ReachedRefuge)
                {
                    if (!WakeResidentForAlarm(actor,reaction)) continue;
                    if (reaction.Escape != null)
                    {
                        PlaceEscape(actor, reaction, seconds, interior.transform, false);
                        RememberNpcCareAlarmPosition(plan,actor,seconds);
                        continue;
                    }
                    var alarm=worldStore.ActiveAlarm(plan.Resident.Id,seconds);
                    Vector3? danger=null;
                    Vector3 lawfulGuard;
                    if(TryLawfulGuardFlightDanger(actor,out lawfulGuard)) danger=lawfulGuard;
                    else if(alarm!=null && (alarm.ThreatId!=null || alarm.KillerThreatId!=null))
                    {
                        Vector3 current;
                        var guards=GetComponent<GuardPopulation>();
                        // A saved alarm can originate in another scene. Prefer the
                        // live foe; never reinterpret exterior coordinates indoors.
                        danger=guards && guards.TryHostileThreatPosition(alarm.KillerThreatId??alarm.ThreatId,out current) ? current :
                            alarm.HasLocation && alarm.BuildingKey==interior.EntryDoor.buildingKey ?
                            interior.transform.TransformPoint(new Vector3((float)alarm.X,(float)alarm.Y,(float)alarm.Z)) : actor.transform.position;
                    }
                    CivilianResponses.Move(actor, reaction, plan.Routine, interior.transform, geometry, indoorGeometry,danger);
                    RememberNpcCareAlarmPosition(plan,actor,seconds);
                    continue;
                }
                BedAssignment bedClaim;
                if(reaction==null && DeferHouseholdInn(plan,actor,seconds))continue;
                var art = reaction == null && SleepEnabled && state.Activity == Activity.Sleep && sleepingArt != null &&
                    bedClaims.TryGetValue(plan.Resident.Id, out bedClaim) && bedClaim.SharingCount == 1 ?
                    sleepingArt.ForResident(plan.Resident) : null;
                if (art != null)
                {
                    bedClaim = bedClaims[plan.Resident.Id];
                    actor.Sleep(interior.transform, indoorBeds[bedClaim.BedRecordIndex], art);
                }
                else if(reaction==null && PlaceStreetChat(actor,seconds)) { }
                else if(reaction==null && state.Activity==Activity.Visit && PlaceShopVisit(actor,seconds)) { }
                else actor.Place(interior.transform.TransformPoint(indoorGeometry.LocalAnchors[slot]), Vector3.forward, true,freeStanding:state.Activity==Activity.Home||state.Activity==Activity.Visit);
                RecognizePartnerKiller(actor,seconds,interior.transform,interior.EntryDoor.buildingKey);
            }
            TickHouseholdInns(seconds);
            if(shopGeometry!=null) UpdateInnChats(seconds);
        }
        private bool PlaceShopVisit(ResidentActor actor,double seconds)
        {
            if(shopGeometry==null) return false;
            PhysicalShopVisit physical;
            if(!physicalShopVisits.TryGetValue(actor.Resident.Id,out physical))
            {
                if(!RestoreObservedShopVisit(actor,seconds,out physical))
                {
                    ShopVisitJourney journey;
                    if(!shopJourneys.TryGetValue(actor.Resident.Id,out journey) || journey==null) return false;
                    double resume=ShopResumeUntil(actor,seconds,journey.PlannedEndAt);
                    if(reconstructInteriorVisits && journey.Evaluate(seconds).Phase!=ShopVisitPhase.Complete)
                    {
                        var saved=journey.Checkpoint(actor.Resident.Id,population.SettlementId,interior.EntryDoor.buildingKey,resume);
                        if(seconds>=saved.ObservedAt) RestoreShopCheckpoint(actor,saved,seconds,out physical);
                        else physical=new PhysicalShopVisit(journey) {ResumeUntil=resume};
                    }
                    else physical=new PhysicalShopVisit(journey) {ResumeUntil=resume};
                }
                physicalShopVisits[actor.Resident.Id]=physical;
            }
            if(shopGeometry.IsShop && physical.Journey.Purchases && !physical.CounterPurchaseResolved &&
                physical.PurchaseSelection==null && physical.Snapshot.Phase==ShopVisitPhase.Browsing)
                PrepareAmbientShopPurchase(actor,physical);
            physical.Advance(actor,interior,indoorGeometry,shopGeometry,seconds,
                () => shopGeometry.IsTavern ? TryCompleteInnPurchaseAtCounter(actor,physical,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()) :
                    TryCompleteAmbientShopPurchase(actor,physical,seconds));
            if(physical.Snapshot.Phase==ShopVisitPhase.Complete)
            {
                var saved=physical.Capture(actor,interior,indoorGeometry,seconds,population.SettlementId);
                if(saved!=null) worldStore.StoreObservedVisit(saved);
                RemovePhysicalShopVisit(actor.Resident.Id);
                RemoveDestinationActor(actor.Resident.Id);
            }
            return true;
        }
        public bool TryShopActivity(string id,out ShopVisitSnapshot snapshot)
        {
            PhysicalShopVisit physical;
            if(physicalShopVisits.TryGetValue(id,out physical)) {snapshot=physical.Snapshot;return true;}
            snapshot=new ShopVisitSnapshot();return false;
        }
        private bool ContinueShopDeparture(ResidentActor actor,double seconds)
        {
            var id=actor.Resident.Id;
            ShopDeparture departure;
            if(!shopDepartures.TryGetValue(id,out departure))
            {
                departure=new ShopDeparture { StartedAt=seconds };
                shopDepartures[id]=departure;
            }
            if(seconds-departure.StartedAt>=600)
            {
                departure.Route=null;
                departure.Next=0;
                departure.StartedAt=seconds;
                departure.RetryAt=seconds;
            }
            if(departure.Route==null)
            {
                if(seconds<departure.RetryAt) return true;
                var current=actor.transform.position-Vector3.up;
                var route=indoorGeometry.RouteToExit(current);
                if(route.Count<2)
                {
                    // Retry briefly if crowd displacement leaves actor outside surveyed mesh.
                    departure.RetryAt=seconds+0.5;
                    return true;
                }
                departure.Route=route; departure.Next=1; departure.TrackedWaypoint=-1;
            }
            var actorGround=actor.transform.position-Vector3.up;
            int trackedIndex=Mathf.Clamp(departure.Next,0,departure.Route.Count-1);
            var tracked=departure.Route[trackedIndex];
            var trackedGround=interior.transform.TransformPoint(new Vector3((float)tracked.X,(float)tracked.Y,(float)tracked.Z));
            float targetDistance=Vector3.Distance(actorGround,trackedGround);
            if(departure.TrackedWaypoint!=trackedIndex)
            {
                departure.TrackedWaypoint=trackedIndex;
                departure.LastTargetDistance=targetDistance;
                departure.StalledAt=Time.unscaledTime;
            }
            else if(departure.LastTargetDistance-targetDistance>=0.12f)
            {
                departure.LastTargetDistance=targetDistance;
                departure.StalledAt=0;
            }
            else if(departure.StalledAt<=0) departure.StalledAt=Time.unscaledTime;
            if(targetDistance>=0.35f && departure.StalledAt>0 && Time.unscaledTime-departure.StalledAt>=0.8f &&
                Time.unscaledTime>=departure.NextDetourAt)
            {
                var neighbours=ShopDepartureNeighbours(actorGround,actor);
                var rejoinPoints=new List<GuardPoint3>();
                for(int i=departure.Next+1;i<departure.Route.Count;i++) rejoinPoints.Add(departure.Route[i]);
                int rejoinIndex;
                var detour=indoorGeometry.FindLocalDetour(actorGround,neighbours,CrowdMotion.Separation,rejoinPoints,out rejoinIndex);
                departure.NextDetourAt=Time.unscaledTime+1.5f;
                if(detour.Count>=2)
                {
                    for(int i=rejoinIndex+1;i<rejoinPoints.Count;i++) detour.Add(rejoinPoints[i]);
                    departure.Route=detour;departure.Next=1;departure.TrackedWaypoint=-1;departure.StalledAt=Time.unscaledTime;
                }
            }
            var final=departure.Route[departure.Route.Count-1];
            var finalGround=interior.transform.TransformPoint(new Vector3((float)final.X,(float)final.Y,(float)final.Z));
            if(Vector3.Distance(actor.transform.position,finalGround+Vector3.up)<0.65f)
            {
                shopDepartures.Remove(id);
                RemoveDestinationActor(id);
                return true;
            }
            while(departure.Next<departure.Route.Count-1)
            {
                var point=departure.Route[departure.Next];
                var waypoint=interior.transform.TransformPoint(new Vector3((float)point.X,(float)point.Y,(float)point.Z));
                if(Vector3.Distance(actor.transform.position,waypoint+Vector3.up)<0.35f)
                { departure.Next++; continue; }
                var afterPoint=departure.Route[departure.Next+1];
                var afterGround=interior.transform.TransformPoint(new Vector3((float)afterPoint.X,(float)afterPoint.Y,(float)afterPoint.Z));
                // Survey paths may pass within the crowd radius of an unmodeled
                // native NPC. Skip that waypoint only when the body sweep and floor
                // remain clear to the next surveyed point.
                if(Vector3.Distance(actor.transform.position,waypoint+Vector3.up)<=CrowdMotion.Separation+0.1f &&
                    ShopDepartureSegmentClear(actor.transform.position-Vector3.up,afterGround,actor))
                { departure.Next++; continue; }
                break;
            }
            var next=departure.Route[Math.Min(departure.Next,departure.Route.Count-1)];
            var ground=interior.transform.TransformPoint(new Vector3((float)next.X,(float)next.Y,(float)next.Z));
            var after=departure.Route[Math.Min(departure.Next+1,departure.Route.Count-1)];
            var following=interior.transform.TransformPoint(new Vector3((float)after.X,(float)after.Y,(float)after.Z));
            InteriorGeometry.OpenOrdinaryDoors(actor.transform.position-Vector3.up,ground);
            var direction=following-ground;
            if(direction.sqrMagnitude<0.0001f) direction=ground-(actor.transform.position-Vector3.up);
            var motion=actor.GetComponent<CrowdMotion>();
            if(motion) motion.ResumeFromPause();
            actor.Place(ground,direction);
            return true;
        }
        internal static List<Vector3> ShopDepartureNeighbours(Vector3 center,ResidentActor actor)
        {
            // Managed bodies are represented by CrowdMotion, not physical blocking
            // colliders. Include them before collecting native people below.
            var motion=actor ? actor.GetComponent<CrowdMotion>() : null;
            var result=motion ? motion.NeighbourPositions(center) : new List<Vector3>();
            foreach(var collider in Physics.OverlapSphere(center,4,~0,QueryTriggerInteraction.Ignore))
            {
                if(!collider || actor && collider.transform.IsChildOf(actor.transform) || collider.GetComponentInParent<CrowdMotion>()) continue;
                if(!collider.GetComponentInParent<StaticNPC>() && !collider.GetComponentInParent<MobilePersonNPC>() &&
                    !collider.GetComponentInParent<DaggerfallWorkshop.Game.Entity.DaggerfallEntityBehaviour>() &&
                    !collider.GetComponentInParent<PlayerMotor>()) continue;
                var bounds=collider.bounds;var point=new Vector3(bounds.center.x,bounds.min.y,bounds.center.z);
                bool duplicate=false;foreach(var existing in result){var delta=existing-point;delta.y=0;if(delta.sqrMagnitude<0.04f){duplicate=true;break;}}
                if(!duplicate) result.Add(point);
            }
            return result;
        }
        private bool ShopDepartureSegmentClear(Vector3 from,Vector3 to,ResidentActor actor)
        {
            var delta=to-from;
            if(delta.sqrMagnitude>1.25f*1.25f || Mathf.Abs(delta.y)>0.5f) return false;
            if(delta.sqrMagnitude>0.000001f)
                foreach(var hit in Physics.CapsuleCastAll(from+Vector3.up*(0.51f+CrowdMotion.BodyRadius),from+Vector3.up*1.65f,
                    CrowdMotion.BodyRadius,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore))
                    if(ShopDepartureBlocker(hit.collider,actor)) return false;
            for(float t=0;t<=1.001f;t+=0.2f)
            {
                var sample=Vector3.Lerp(from,to,t); bool supported=false;
                foreach(var hit in Physics.RaycastAll(sample+Vector3.up*0.55f,Vector3.down,1.1f,~0,QueryTriggerInteraction.Ignore))
                {
                    if(hit.normal.y<0.65f || Mathf.Abs(hit.point.y-sample.y)>0.5f || !hit.collider.transform.IsChildOf(interior.transform)) continue;
                    supported=true; break;
                }
                if(!supported) return false;
            }
            return true;
        }
        private static bool ShopDepartureBlocker(Collider collider,ResidentActor actor)
        {
            if(actor && collider.transform.IsChildOf(actor.transform) || collider.GetComponentInParent<CrowdMotion>()) return false;
            if(collider.GetComponentInParent<DaggerfallActionDoor>() || collider.GetComponentInParent<DaggerfallAction>()) return false;
            return true;
        }
        private void PrepareShopVisits(double seconds)
        {
            double day=Math.Floor(seconds/86400);
            if(shopGeometry==null || day==shopVisitDay) return;
            shopVisitDay=day; shopJourneys.Clear(); shopGeometry.ResetReservations();
            // Assign all planned visitors in stable population order, including those
            // currently offscreen. Re-entry therefore reconstructs the same stops.
            foreach(var plan in plans)
            {
                var schedule=ActiveSchedule(plan);
                if(worldStore.HasActiveHouseholdJoint(population.SettlementId,plan.Resident.Id))continue;
                if(!plan.Resident.IsAlive || !schedule.ContainsVisitBuilding(interior.EntryDoor.buildingKey,seconds)) continue;
                double start,end;
                if(!schedule.TryVisitWindowForBuilding(seconds,interior.EntryDoor.buildingKey,out start,out end)) continue;
                shopJourneys[plan.Resident.Id]=shopGeometry.Create(plan.Resident.Id,plan.Resident.AppearanceSeed,start,end);
            }
        }
        private void PlaceEscape(ResidentActor actor, AlarmPerson person, double now, Transform origin, bool outside)
        {
            var next = new AlarmPerson(); person.Escape.Advance(next, now + 0.1);
            Vector3 point = outside ? geometry.ScenePoint(new RoutePoint(person.X, person.Z)) :
                origin.TransformPoint(new Vector3((float)person.X, (float)person.Y, (float)person.Z));
            Vector3 direction = next.BuildingKey == person.BuildingKey ?
                origin.TransformVector(new Vector3((float)(next.X-person.X), (float)(next.Y-person.Y), (float)(next.Z-person.Z))) : actor.transform.forward;
            if (!outside) InteriorGeometry.OpenOrdinaryDoors(actor.transform.position - Vector3.up, point);
            bool stopped = person.Escape.Arrived(now) || GameManager.IsGamePaused;
            actor.Place(point, direction, stopped, !stopped);
        }
        private void RemovePhysicalShopVisit(string residentId)
        {
            PhysicalShopVisit visit;
            if(physicalShopVisits.TryGetValue(residentId,out visit)) visit.ReleaseReservations();
            physicalShopVisits.Remove(residentId);
        }
        private void ClearPhysicalShopVisits()
        {
            foreach(var visit in physicalShopVisits.Values) visit.ReleaseReservations();
            physicalShopVisits.Clear();
        }
        private void ClearActors()
        {
            FlushHomeSleep();
            YieldHouseholds(DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
            ClearHouseholdJointBodies();
            ClearHouseholdInns();
            ClearAppointmentApproaches();
            ClearPhysicalShopVisits();
            bankVisitors.Clear();cashMotions.Clear();
            ClearInnChats();
            ClearStreetChats();nextStreetChat.Clear();nextChatScan=0;
            partnerRetaliation.Clear();nextGrudgeCheck.Clear();
            foreach (var actor in actors.Values) if (actor) { actor.CaptureState(); actor.gameObject.SetActive(false); Destroy(actor.gameObject); }
            actors.Clear();
        }
        private void UpdateCorpses(double seconds)
        {
            if (worldStore == null || population == null) return;
            var game = GameManager.Instance;
            int? current = interior ? (int?)interior.EntryDoor.buildingKey : game.PlayerEnterExit.IsPlayerInside ? null : (int?)-1;
            var wanted = new HashSet<string>();
            foreach (var alarm in worldStore.ObserveCorpses(population.SettlementId, current, seconds))
            {
                var body = alarm.Corpse;
                int buildingKey = body.HasPosition ? body.BuildingKey : alarm.BuildingKey;
                if (!current.HasValue || buildingKey != current.Value) continue;
                var origin = interior ? interior.transform : directory.transform;
                var local = body.HasPosition ? new Vector3((float)body.X, (float)body.Y, (float)body.Z) :
                    new Vector3((float)alarm.X, (float)alarm.Y, (float)alarm.Z);
                var point = origin.TransformPoint(local);
                if (!interior && (point - game.PlayerMotor.transform.position).sqrMagnitude > 165 * 165) continue;
                wanted.Add(alarm.Id);
                GameObject view;
                if (corpses.TryGetValue(alarm.Id, out view) && view) continue;
                InitializeCorpseKeys(alarm);
                // Native human corpse placeholder; resident-specific corpse art is a separate milestone.
                view = GameObjectHelper.CreateDaggerfallBillboardGameObject(380, 1, origin);
                view.name = "People of the Iliac Bay corpse " + alarm.VictimId;
                view.transform.position = point;
                view.GetComponent<DaggerfallBillboard>().AlignToBase();
                {
                    var collider = view.GetComponent<BoxCollider>() ?? view.AddComponent<BoxCollider>();
                    collider.isTrigger = true;
                    var action = view.AddComponent<CorpseInteraction>();
                    action.Streets = this; action.IncidentId = alarm.Id;
                }
                corpses[alarm.Id] = view;
            }
            foreach (var id in new List<string>(corpses.Keys))
                if (!wanted.Contains(id))
                {
                    if (corpses[id]) { corpses[id].SetActive(false); Destroy(corpses[id]); }
                    corpses.Remove(id);
                }
        }
        private void ClearCorpses()
        {
            CloseCorpseInventory();
            foreach (var view in corpses.Values) if (view) { view.SetActive(false); Destroy(view); }
            corpses.Clear();
        }
        private bool destroyingPopulation;
        public void Clear()
        {
            // Town/fast-travel changes can advance the clock before teardown.
            // Yield at the last actual frame so unloaded time remains consumable.
            if(!destroyingPopulation && lastPopulationSeconds>=0)YieldHouseholds(lastPopulationSeconds);
            if(!destroyingPopulation && !GameManager.Instance.IsPlayerInside && roadTravelActors && worldStore!=null)
            {
                double now=lastPopulationSeconds>=0?lastPopulationSeconds:ActionNow();
                foreach(var actor in new List<ResidentActor>(actors.Values))
                    if(actor && roadTravelActors.TryDeferPaidCompanion(actor,worldStore,now))
                        actors.Remove(actor.Resident.Id);
            }
            ResetInnServiceCatalogCapture();
            healthCareChecks.Clear();healthCareEpoch=null;
            medicalMotions.Clear();medicalChecks.Clear();medicalServices=null;
            cashMotions.Clear();cashChecks.Clear();
            (bankCounterSurvey as IDisposable)?.Dispose();bankCounterSurvey=null;bankCounters.Clear();bankVisitors.Clear();
            lastPopulationSeconds=-1;reconstructInteriorVisits=false;
            nextSafetyReconciliation=0;
            nextSafetyReconciliationPlan=0;
            OutdoorRoutesPrepared=false;
            EntranceSurveyReady=false;
            ResetNpcActionMotion();
            appointmentSchedules.Clear(); nextAppointmentObservation=0;
            householdSchedules.Clear();nextHouseholdDay=0;nextHouseholdDayUpdate=0;
            householdJointCandidates.Clear();simulatedFamilyFrame.Clear();
            preparedHouseholdRoutineSources.Clear();
            pendingHouseholdInitialization.Clear();
            appointmentPointRouteCache.Clear();
            bystanderPostures.Clear(); bystanderUntil=0;
            travelerPlans.Clear(); nextTravelerRefresh=0;
            destinationResidents.Clear(); nextDestinationRefresh=0;
            destinationShopWindows.Clear();
            residentRooms.Clear();
            residentReceptions.Clear(); receptionKeeper=null;
            travelerWindow.Clear(); travelerWindowDay=-1; travelerWindowRevision=-1;
            entryJourneys.Clear();
            keeperRoom = null;
            RestoreKeeperViews();
            RestoreHouseFlats();
            FlushOutdoorDeparture();
            if (building != null) { StopCoroutine(building); building = null; }
            ClearActors();closedShopWaits.Clear();nextClosedShopPlan=0; homeSleepClocks.Clear();homeSleepEpoch=null;
            ClearCorpses(); ClearPaidDoorPlans(); plans.Clear(); Ready = false;
            needsExterior = false;
            interior = null; indoorGeometry = null; indoorSlots.Clear();
            shopGeometry=null; shopJourneys.Clear(); ClearPhysicalShopVisits(); shopDepartures.Clear(); shopVisitDay=-1;
            population = null; indoorBeds.Clear(); bedClaims.Clear();
            stationaryParticipants.Clear();stationaryScene=null;stationaryMemoryScratch.Clear();
            responses = null; worldStore = null;
            if (ownsVanilla && vanilla)
            {
                foreach (var pair in hiddenVanilla) if (pair.Key) pair.Key.SetActive(pair.Value);
                vanilla.enabled = vanillaWasEnabled;
            }
            hiddenVanilla.Clear(); ownsVanilla = false; vanilla = null; directory = null; geometry = null;
        }
        private void OnDestroy() { destroyingPopulation=true; DisposePlayerShopProvenanceHook(); DisposeCorpseInventory(); RemoveEntryAwareness(); Clear(); if (runningPilotAtlas) Destroy(runningPilotAtlas); if (sleepingArt != null) sleepingArt.Dispose(); if (keeperStandingArt!=null) keeperStandingArt.Dispose(); if (reactionHook != null) reactionHook.Dispose(); if (householdDoorHook != null) householdDoorHook.Dispose(); if (theftHook != null) theftHook.Dispose(); if (keeperServiceHook != null) keeperServiceHook.Dispose(); }
    }
}
