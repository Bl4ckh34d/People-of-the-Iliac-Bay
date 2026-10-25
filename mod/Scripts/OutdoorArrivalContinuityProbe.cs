using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Opt-in native regression. A temporary local schedule arrives before its
    // existing actor can walk a saved exterior route. No save data changes.
    public static class OutdoorArrivalContinuityProbe
    {
        private const float DoorRadius = 0.55f;
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        public static IEnumerator Run(PeopleOfTheIliacBayMod adapter, StreetPopulation streets)
        {
            var cornerRoute = new List<SavedRoutePoint>
            {
                new SavedRoutePoint { X = 0, Z = 0 },
                new SavedRoutePoint { X = 1, Z = 0 },
                new SavedRoutePoint { X = 1, Z = 1 }
            };
            RoutePoint cornerTarget;
            if (!StreetPopulation.TryNextArrivalRoutePoint(cornerRoute, new Vector3(1, 0, 0), out cornerTarget) ||
                Math.Abs(cornerTarget.X - 1) > 0.0001 || Math.Abs(cornerTarget.Z - 1) > 0.0001)
                throw new InvalidOperationException("Outdoor arrival route follower failed to advance through a shared corner.");

            if (!adapter || !streets) throw new InvalidOperationException("Outdoor arrival probe requires live town components.");
            var game = GameManager.Instance;
            if (game.PlayerEnterExit.IsPlayerInsideBuilding)
            {
                game.PlayerEnterExit.TransitionExterior();
                float exteriorDeadline = Time.realtimeSinceStartup + 60f;
                while ((game.PlayerEnterExit.IsPlayerInsideBuilding || !game.StreamingWorld.IsReady ||
                    game.StreamingWorld.IsRepositioningPlayer) && Time.realtimeSinceStartup < exteriorDeadline)
                    yield return null;
                if (game.PlayerEnterExit.IsPlayerInsideBuilding || !game.StreamingWorld.IsReady ||
                    game.StreamingWorld.IsRepositioningPlayer)
                    throw new InvalidOperationException("Outdoor arrival probe could not load its exterior scene.");
            }
            if (GameManager.IsGamePaused)
                throw new InvalidOperationException("Outdoor arrival probe requires an unpaused exterior scene.");
            float populationDeadline = Time.realtimeSinceStartup + 60f;
            while ((!streets.Ready || !game.StreamingWorld.IsReady || game.StreamingWorld.IsRepositioningPlayer) &&
                Time.realtimeSinceStartup < populationDeadline) yield return null;
            if (!streets.Ready || !game.StreamingWorld.IsReady || game.StreamingWorld.IsRepositioningPlayer)
                throw new InvalidOperationException("Outdoor arrival probe requires a ready exterior population.");

            // Exercise daytime walkers rather than depending on whether an old
            // disposable morning fixture happens to have somebody outside.
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            double timeOfDay=clock.ToSeconds()%86400;
            clock.RaiseTime((float)((43200-timeOfDay+86400)%86400));
            for(int frame=0;frame<5;frame++)yield return null;

            var directory = (BuildingDirectory)GetField(typeof(StreetPopulation), "directory").GetValue(streets);
            var geometry = (TownGeometry)GetField(typeof(StreetPopulation), "geometry").GetValue(streets);
            var plans = (IList)GetField(typeof(StreetPopulation), "plans").GetValue(streets);
            var snapshot = adapter.GetSaveData() as WorldState;
            var town = snapshot == null ? null : snapshot.Routines.Find(item => item.SettlementId == directory.MapID);
            if (town == null) throw new InvalidOperationException("Outdoor arrival probe found no saved town routines.");
            var worldStore = GetField(typeof(PeopleOfTheIliacBayMod), "store").GetValue(adapter) as WorldStateStore;
            if (worldStore == null) throw new InvalidOperationException("Outdoor arrival probe requires the native world store.");
            var tripIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var trip in snapshot.ResidentTrips ?? new List<ResidentTrip>())
                if (trip != null && !string.IsNullOrEmpty(trip.ResidentId)) tripIds.Add(trip.ResidentId);

            object chosenPlan = null;
            ResidentActor chosenActor = null;
            ResidentVisitStop sourceStop = null;
            // Ready marks completed schedule construction. Actor creation happens
            // in the following Update; do not select from that empty first frame.
            float actorDeadline = Time.realtimeSinceStartup + 45f;
            while (!chosenActor && Time.realtimeSinceStartup < actorDeadline)
            {
            foreach (var routine in town.Residents)
            {
                if (routine == null || tripIds.Contains(routine.ResidentId)) continue;
                var candidateActor = streets.FindActor(routine.ResidentId);
                if (!candidateActor || !candidateActor.gameObject.activeInHierarchy || candidateActor.transform.parent != directory.transform) continue;
                if (!CanControlAmbientSchedule(worldStore, candidateActor.Resident, DaggerfallUnity.Instance.WorldTime.Now.ToSeconds())) continue;
                var plan = FindPlan(plans, routine.ResidentId);
                if (plan == null) continue;
                if (routine.Itinerary != null && routine.Itinerary.Stops != null)
                {
                    foreach (var stop in routine.Itinerary.Stops)
                    {
                        if (stop == null || stop.Route == null || stop.Route.Count < 2 || RouteLength(stop.Route) < 3.5) continue;
                        var endpoint = stop.Route[stop.Route.Count - 1];
                        Vector3 door = geometry.ScenePoint(new RoutePoint(endpoint.X, endpoint.Z));
                        if ((door - game.PlayerMotor.transform.position).sqrMagnitude > 165f * 165f) continue;
                        chosenPlan = plan; chosenActor = candidateActor; sourceStop = stop;
                        break;
                    }
                }
                if (!chosenActor && routine.DestinationKey >= 0 && routine.Route != null &&
                    routine.Route.Count >= 2 && RouteLength(routine.Route) >= 3.5)
                {
                    var endpoint = routine.Route[routine.Route.Count - 1];
                    Vector3 door = geometry.ScenePoint(new RoutePoint(endpoint.X, endpoint.Z));
                    if ((door - game.PlayerMotor.transform.position).sqrMagnitude <= 165f * 165f)
                    {
                        chosenPlan = plan; chosenActor = candidateActor;
                        sourceStop = LegacyRoutineStop(routine);
                    }
                }
                if (chosenActor) break;
            }
                if (!chosenActor) yield return null;
            }
            if (!chosenActor)
                throw new InvalidOperationException("No nearby active local resident has a long saved itinerary or legacy route after waiting for population updates.");

            var planType = chosenPlan.GetType();
            var routineField = GetField(planType, "Routine");
            var scheduleField = GetField(planType, "Schedule");
            var hauntedField = GetField(planType, "HauntedSchedule");
            object oldRoutine = routineField.GetValue(chosenPlan);
            object oldSchedule = scheduleField.GetValue(chosenPlan);
            object oldHauntedSchedule = hauntedField.GetValue(chosenPlan);
            var actor = chosenActor;
            TownSmokeHarness.Watch(actor.transform);
            string residentId = actor.Resident.Id;
            Vector3 savedPosition = actor.transform.position;
            Quaternion savedRotation = actor.transform.rotation;
            var motion = actor.GetComponent<CrowdMotion>();
            object[] savedAppointmentCache = SuspendAppointmentCache(streets, residentId);

            try
            {
                double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                double nowOfDay = now % 86400d;
                int wake = ReadHour(streets, "wakeHour", 6), sleep = ReadHour(streets, "sleepHour", 22);
                double departure = nowOfDay - 0.3;
                if (departure < wake * 3600d || nowOfDay + 300.3 >= sleep * 3600d)
                    throw new InvalidOperationException("Outdoor arrival probe requires at least 300.3 waking seconds on this day.");

                var stopCopy = CopyStopRoute(sourceStop);
                stopCopy.OpenMinute = stopCopy.CloseMinute = 0;
                stopCopy.WalkSeconds = 2;
                stopCopy.DwellSeconds = 300;
                var itinerary = new ResidentDestination
                {
                    Stops = new List<ResidentVisitStop> { stopCopy },
                    ExitWalkSeconds = 0.001,
                    ExitRoute = Reverse(stopCopy.Route)
                };
                var temporaryRoutine = new ResidentRoutine
                {
                    ResidentId = actor.Resident.Id,
                    Itinerary = itinerary,
                    ItineraryDepartureSeconds = departure
                };
                var temporarySchedule = DailySchedule.ItineraryDay(actor.Resident.HomeKey, itinerary,
                    departure, wake * 60, sleep * 60);
                var journey = ResidentVisitJourney.Create(departure, itinerary);
                var visitEnumerator = journey.ScheduledVisits().GetEnumerator();
                if (!visitEnumerator.MoveNext()) throw new InvalidOperationException("Probe itinerary has no visit.");
                double enterAt = visitEnumerator.Current.EnterAt;
                if (temporarySchedule.EvaluateSeconds(now).Activity != Activity.Travel)
                    throw new InvalidOperationException("Probe itinerary must start walking before entering its visit.");

                routineField.SetValue(chosenPlan, temporaryRoutine);
                scheduleField.SetValue(chosenPlan, temporarySchedule);
                hauntedField.SetValue(chosenPlan, temporarySchedule);

                var doorPoint = stopCopy.Route[stopCopy.Route.Count - 1];
                Vector3 doorGround = geometry.ScenePoint(new RoutePoint(doorPoint.X, doorPoint.Z));
                double back = Math.Min(3.0, RouteLength(stopCopy.Route) * 0.75);
                var delayedPoint = PointFromEnd(stopCopy.Route, back);
                Vector3 delayedGround = geometry.ScenePoint(new RoutePoint(delayedPoint.X, delayedPoint.Z));
                float initialGap = Vector3.Distance(delayedGround, doorGround);
                if (initialGap <= DoorRadius + 1.5f)
                    throw new InvalidOperationException("Probe route cannot hold delayed walker beyond door threshold.");

                if (motion) motion.ResetPlacement();
                actor.transform.position = delayedGround + Vector3.up;
                actor.Place(delayedGround, doorGround - delayedGround, true);
                Vector3 startPosition = actor.transform.position - Vector3.up;
                int initialActors = CountResidentActors(actor.Resident.Id);
                if (initialActors != 1) throw new InvalidOperationException("Probe resident already has duplicate actor instances.");

                // Real StreetPopulation.Update runs each frame. Wait for the same
                // body to reach the surveyed door, spanning real and game time.
                float startedReal = Time.realtimeSinceStartup;
                float deadline = startedReal + 20f;
                int frames = 0;
                float gap = Vector3.Distance(actor.transform.position - Vector3.up, doorGround);
                Vector3 previousGround = actor.transform.position - Vector3.up;
                Vector3 lastBeforeRemoval = previousGround;
                double startedGame = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                bool reachedDoor = gap <= DoorRadius;
                bool sameActorUntilDoor = true;
                bool nativeWalking = false;
                float largestFrameStep = 0;
                while (Time.realtimeSinceStartup < deadline && frames < 1200 &&
                    !(reachedDoor && Time.realtimeSinceStartup - startedReal >= 0.2f &&
                      DaggerfallUnity.Instance.WorldTime.Now.ToSeconds() - startedGame >= 2.0))
                {
                    yield return null;
                    frames++;
                    if (!actor)
                    {
                        if (gap > DoorRadius) throw new InvalidOperationException("Outdoor actor disappeared before reaching doorway.");
                        reachedDoor = true;
                    }
                    var state = streets.GetState(residentId, DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
                    if (state.Activity != Activity.Travel &&
                        (state.Activity != Activity.Visit || state.BuildingKey != stopCopy.BuildingKey))
                        throw new InvalidOperationException("Probe schedule left visit before multi-frame arrival check completed: resident=" +
                            residentId + "; actual=" + state.Activity + "; building=" + state.BuildingKey +
                            "; expectedBuilding=" + stopCopy.BuildingKey);
                    if (actor)
                    {
                        var native = actor.GetComponent<NativeResidentNavigation>();
                        nativeWalking |= native && native.Active;
                        if (streets.FindActor(residentId) != actor) sameActorUntilDoor = false;
                        int currentCount = CountResidentActors(residentId);
                        if (currentCount > 1 || !reachedDoor && currentCount != 1)
                            throw new InvalidOperationException("Outdoor arrival changed actor identity or created a duplicate before the doorway.");
                        Vector3 currentGround = actor.transform.position - Vector3.up;
                        float frameStep = Vector3.Distance(previousGround, currentGround);
                        largestFrameStep = Mathf.Max(largestFrameStep, frameStep);
                        if (frameStep > 0.55f)
                            throw new InvalidOperationException("Outdoor arrival moved farther than one collision-checked CrowdMotion step.");
                        previousGround = currentGround;
                        lastBeforeRemoval = currentGround;
                        gap = Vector3.Distance(currentGround, doorGround);
                        if (gap <= DoorRadius) reachedDoor = true;
                    }
                }

                double elapsed = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds() - startedGame;
                float elapsedReal = Time.realtimeSinceStartup - startedReal;
                double scheduleElapsed = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds() % 86400d - enterAt;
                float moved = Vector3.Distance(startPosition, lastBeforeRemoval);
                int finalCount = CountResidentActors(residentId);
                if (NativeNavigationWorld.Current && NativeNavigationWorld.Current.Ready && !nativeWalking)
                    throw new InvalidOperationException("Outdoor arrival did not exercise the loaded native NavMesh.");
                if (frames < 4 || elapsedReal < 0.2f || elapsed < 2 || scheduleElapsed <= 0.05 || !reachedDoor ||
                    !sameActorUntilDoor || finalCount > 1 || moved <= 0.5f || largestFrameStep > 0.55f)
                    throw new InvalidOperationException("Outdoor actor did not follow the same body along the route to its actual doorway across repeated updates.");

                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_OUTDOOR_ARRIVAL_CONTINUITY_PASS: resident=" + residentId +
                    "; scheduled=Visit; frames=" + frames + "; elapsedReal=" + elapsedReal.ToString("F3") +
                    "; elapsedGame=" + elapsed.ToString("F3") +
                    "; scheduleElapsed=" + scheduleElapsed.ToString("F3") +
                    "; initialGap=" + initialGap.ToString("F2") + "; doorwayGap=" + gap.ToString("F2") +
                    "; moved=" + moved.ToString("F2") + "; largestFrameStep=" + largestFrameStep.ToString("F2") +
                    "; sameActorUntilDoor=true; nativeWalking=" + nativeWalking + "; duplicateCount=" + finalCount);

                yield return RunSharedDoor(streets,directory,geometry,plans,worldStore,sourceStop,residentId);
                yield return RunSkippedHourCatchup(adapter, streets, directory, geometry, plans, town, tripIds, residentId);
                yield return RunSkippedHourTravelCatchup(adapter, streets, directory, geometry, plans, town, tripIds, residentId);
            }
            finally
            {
                routineField.SetValue(chosenPlan, oldRoutine);
                scheduleField.SetValue(chosenPlan, oldSchedule);
                hauntedField.SetValue(chosenPlan, oldHauntedSchedule);
                if (actor)
                {
                    actor.transform.position = savedPosition;
                    actor.transform.rotation = savedRotation;
                    if (motion) motion.ResetPlacement();
                }
                RestoreAppointmentCache(residentId, savedAppointmentCache);
            }
            yield break;
        }

        private sealed class DoorWalker
        {
            public object Plan,Routine,Schedule,Haunted;
            public ResidentActor Actor;
            public string Id;
            public Vector3 Position,Previous;
            public Quaternion Rotation;
            public object[] Appointment;
            public bool Reached,Removed,Native;
        }
        private static IEnumerator RunSharedDoor(StreetPopulation streets,BuildingDirectory directory,TownGeometry geometry,
            IList plans,WorldStateStore store,ResidentVisitStop source,string excluded)
        {
            if(!NativeNavigationWorld.Current || !NativeNavigationWorld.Current.Ready)
                throw new InvalidOperationException("Shared door test requires shipped native navigation.");
            var walkers=new List<DoorWalker>();
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            var endpoint=source.Route[source.Route.Count-1];
            Vector3 door=geometry.ScenePoint(new RoutePoint(endpoint.X,endpoint.Z));
            foreach(var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
            {
                if(!actor || actor.Resident.Id==excluded || actor.transform.parent!=directory.transform ||
                    !CanControlAmbientSchedule(store,actor.Resident,now) || streets.IsAlarmed(actor.Resident.Id))continue;
                if(store.HasPlannedHouseholdDay(directory.MapID,actor.Resident.Id,(int)Math.Floor(now/86400)) ||
                    store.HasActiveHouseholdJoint(directory.MapID,actor.Resident.Id))continue;
                var plan=FindPlan(plans,actor.Resident.Id);if(plan==null)continue;
                walkers.Add(new DoorWalker {Plan=plan,Actor=actor,Id=actor.Resident.Id,Position=actor.transform.position,
                    Rotation=actor.transform.rotation,Routine=GetField(plan.GetType(),"Routine").GetValue(plan),
                    Schedule=GetField(plan.GetType(),"Schedule").GetValue(plan),Haunted=GetField(plan.GetType(),"HauntedSchedule").GetValue(plan)});
                if(walkers.Count==3)break;
            }
            if(walkers.Count!=3)throw new InvalidOperationException("Shared door test needs three existing ordinary residents.");
            try
            {
                int wake=ReadHour(streets,"wakeHour",6),sleep=ReadHour(streets,"sleepHour",22);
                for(int i=0;i<walkers.Count;i++)
                {
                    var walker=walkers[i];
                    var from=PointFromEnd(source.Route,Math.Min(4+i*2,RouteLength(source.Route)*0.9));
                    var route=geometry.RouteBetweenPoints(from,endpoint);
                    if(route.Count<2)throw new InvalidOperationException("Shared door approach has no native path.");
                    var stop=CopyStopRoute(source);stop.Route=route;stop.WalkSeconds=2;stop.DwellSeconds=300;
                    stop.OpenMinute=stop.CloseMinute=0;
                    var itinerary=new ResidentDestination {Stops=new List<ResidentVisitStop>{stop},
                        ExitWalkSeconds=2,ExitRoute=Reverse(route)};
                    double departure=now%86400-0.1;
                    var routine=new ResidentRoutine {ResidentId=walker.Id,Itinerary=itinerary,ItineraryDepartureSeconds=departure};
                    var schedule=DailySchedule.ItineraryDay(walker.Actor.Resident.HomeKey,itinerary,departure,wake*60,sleep*60);
                    walker.Appointment=SuspendAppointmentCache(streets,walker.Id);
                    GetField(walker.Plan.GetType(),"Routine").SetValue(walker.Plan,routine);
                    GetField(walker.Plan.GetType(),"Schedule").SetValue(walker.Plan,schedule);
                    GetField(walker.Plan.GetType(),"HauntedSchedule").SetValue(walker.Plan,schedule);
                    var native=walker.Actor.GetComponent<NativeResidentNavigation>();if(native)native.ResetPlacement();
                    var ground=geometry.ScenePoint(new RoutePoint(from.X,from.Z));
                    walker.Actor.transform.position=ground+Vector3.up;
                    walker.Actor.Place(ground,door-ground,navigationDestination:door);
                    walker.Previous=walker.Actor.transform.position-Vector3.up;
                }
                float deadline=Time.realtimeSinceStartup+30,maxStep=0;int removed=0;
                while(Time.realtimeSinceStartup<deadline && removed<walkers.Count)
                {
                    yield return null;removed=0;
                    foreach(var visible in walkers)
                        if(visible.Actor && visible.Actor.gameObject.activeInHierarchy)
                        {TownSmokeHarness.Watch(visible.Actor.transform);break;}
                    foreach(var walker in walkers)
                    {
                        if(!walker.Actor || !walker.Actor.gameObject.activeInHierarchy)
                        {
                            if(walker.Actor)
                                walker.Reached|=Vector3.Distance(walker.Actor.transform.position-Vector3.up,door)<=DoorRadius;
                            if(!walker.Reached)throw new InvalidOperationException("Shared door actor disappeared before physical arrival: "+walker.Id);
                            if(CountResidentActors(walker.Id)!=0)
                                throw new InvalidOperationException("Shared door replaced an admitted actor: "+walker.Id);
                            walker.Removed=true;removed++;continue;
                        }
                        var ground=walker.Actor.transform.position-Vector3.up;
                        float step=Vector3.Distance(ground,walker.Previous);maxStep=Mathf.Max(maxStep,step);
                        if(step>0.55f)throw new InvalidOperationException("Shared door actor jumped: "+walker.Id+"; step="+step);
                        walker.Previous=ground;
                        walker.Reached|=Vector3.Distance(ground,door)<=DoorRadius;
                        var native=walker.Actor.GetComponent<NativeResidentNavigation>();walker.Native|=native&&native.Active;
                        int actorCount=CountResidentActors(walker.Id);
                        if(actorCount!=1)throw new InvalidOperationException("Shared door actor count changed: "+walker.Id+"; count="+actorCount);
                    }
                }
                foreach(var walker in walkers)
                    if(!walker.Removed || !walker.Native)
                        throw new InvalidOperationException("Shared door stalled: "+walker.Id+"; gap="+
                            Vector3.Distance(walker.Previous,door)+"; native="+walker.Native+
                            "; failure="+(walker.Actor&&walker.Actor.GetComponent<NativeResidentNavigation>()?
                                walker.Actor.GetComponent<NativeResidentNavigation>().FailureReason:"none"));
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SHARED_DOOR_PASS: ordinaryResidents=3; sameDoor="+source.BuildingKey+
                    "; allPhysicallyArrived=true; allRemoved=true; nativeWalking=true; largestFrameStep="+maxStep);
            }
            finally
            {
                foreach(var walker in walkers)
                {
                    GetField(walker.Plan.GetType(),"Routine").SetValue(walker.Plan,walker.Routine);
                    GetField(walker.Plan.GetType(),"Schedule").SetValue(walker.Plan,walker.Schedule);
                    GetField(walker.Plan.GetType(),"HauntedSchedule").SetValue(walker.Plan,walker.Haunted);
                    if(walker.Appointment!=null)RestoreAppointmentCache(walker.Id,walker.Appointment);
                    if(walker.Actor)
                    {
                        var native=walker.Actor.GetComponent<NativeResidentNavigation>();if(native)native.ResetPlacement();
                        walker.Actor.transform.position=walker.Position;walker.Actor.transform.rotation=walker.Rotation;
                    }
                }
            }
        }

        private static IEnumerator RunSkippedHourCatchup(PeopleOfTheIliacBayMod adapter, StreetPopulation streets,
            BuildingDirectory directory, TownGeometry geometry, IList plans, TownRoutineState town, HashSet<string> tripIds,
            string excludedResidentId)
        {
            var game = GameManager.Instance;
            if (!game.IsPlayerOnHUD)
                throw new InvalidOperationException("Outdoor loiter regression requires player HUD before opening native rest window.");
            var clock = DaggerfallUnity.Instance.WorldTime.Now;
            var snapshot = adapter.GetSaveData() as WorldState;
            var worldStore = GetField(typeof(StreetPopulation), "worldStore").GetValue(streets) as WorldStateStore;
            if (snapshot == null || worldStore == null)
                throw new InvalidOperationException("Outdoor loiter regression requires live routine and world-state stores.");

            object chosenPlan = null;
            ResidentActor actor = null;
            ResidentVisitStop sourceStop = null;
            foreach (var routine in town.Residents)
            {
                if (routine == null || string.IsNullOrEmpty(routine.ResidentId) || routine.ResidentId == excludedResidentId ||
                    tripIds.Contains(routine.ResidentId)) continue;
                var candidate = streets.FindActor(routine.ResidentId);
                if (!candidate || !candidate.gameObject.activeInHierarchy || candidate.transform.parent != directory.transform ||
                    !candidate.Resident.IsAlive || candidate.Resident.IsQuestProtected ||
                    candidate.Resident.AgeGroup != ResidentAgeGroup.Adult || streets.IsAlarmed(routine.ResidentId) ||
                    worldStore.IsResidentAway(routine.ResidentId, clock.ToSeconds()) ||
                    worldStore.ActiveNpcAction(routine.ResidentId) != null || worldStore.ActiveNpcTask(routine.ResidentId) != null)
                    continue;
                if (!CanControlAmbientSchedule(worldStore, candidate.Resident, clock.ToSeconds())) continue;
                var appointments = worldStore.AppointmentsFor(routine.ResidentId);
                double currentDay = Math.Floor(clock.ToSeconds() / 86400d);
                if (appointments.Exists(item => Math.Floor(item.StartsAt / 86400d) == currentDay && item.Status != "cancelled"))
                    continue;

                var questOwned = typeof(StreetPopulation).GetMethod("QuestOwned", BindingFlags.Instance | BindingFlags.NonPublic);
                if (questOwned != null && (bool)questOwned.Invoke(streets, new object[] { candidate.Resident })) continue;
                if (worldStore.GetObservedVisit(routine.ResidentId) != null) continue;
                var contactMotions = GetField(typeof(StreetPopulation), "proactiveContactMotions").GetValue(streets) as IDictionary;
                if (contactMotions != null && contactMotions.Contains(routine.ResidentId)) continue;

                var plan = FindPlan(plans, routine.ResidentId);
                if (plan == null || routine.Itinerary == null || routine.Itinerary.Stops == null) continue;
                foreach (var stop in routine.Itinerary.Stops)
                {
                    if (stop == null || stop.Route == null || stop.Route.Count < 2 || RouteLength(stop.Route) < 3.5) continue;
                    var endpoint = stop.Route[stop.Route.Count - 1];
                    Vector3 door = geometry.ScenePoint(new RoutePoint(endpoint.X, endpoint.Z));
                    if ((door - game.PlayerMotor.transform.position).sqrMagnitude > 165f * 165f) continue;
                    var midpoint = PointFromEnd(stop.Route, RouteLength(stop.Route) * 0.5);
                    Vector3 midpointGround = geometry.ScenePoint(new RoutePoint(midpoint.X, midpoint.Z));
                    if ((midpointGround - game.PlayerMotor.transform.position).sqrMagnitude > 130f * 130f) continue;
                    chosenPlan = plan;
                    actor = candidate;
                    sourceStop = stop;
                    break;
                }
                if (actor) break;
            }
            if (!actor)
                throw new InvalidOperationException("No second nearby ambient resident actor is eligible for the outdoor loiter regression.");

            var planType = chosenPlan.GetType();
            var routineField = GetField(planType, "Routine");
            var scheduleField = GetField(planType, "Schedule");
            var hauntedField = GetField(planType, "HauntedSchedule");
            object oldRoutine = routineField.GetValue(chosenPlan);
            object oldSchedule = scheduleField.GetValue(chosenPlan);
            object oldHauntedSchedule = hauntedField.GetValue(chosenPlan);
            Vector3 savedPosition = actor.transform.position;
            Quaternion savedRotation = actor.transform.rotation;
            var motion = actor.GetComponent<CrowdMotion>();
            string residentId = actor.Resident.Id;
            bool openedRestWindow = false;
            object[] savedAppointmentCache = SuspendAppointmentCache(streets, residentId);

            try
            {
                double initial = clock.ToSeconds();
                double initialDay = Math.Floor(initial / 86400d) * 86400d;
                double initialOfDay = initial - initialDay;
                int wake = ReadHour(streets, "wakeHour", 6), sleep = ReadHour(streets, "sleepHour", 22);
                if (initialOfDay < wake * 3600d + 1800d || initialOfDay + 9000.1 >= sleep * 3600d)
                    throw new InvalidOperationException("Outdoor loiter regression needs 2.5 waking hours remaining today.");

                var stop = CopyStopRoute(sourceStop);
                stop.OpenMinute = stop.CloseMinute = 0;
                stop.WalkSeconds = 3600;
                stop.DwellSeconds = 7200;
                var itinerary = new ResidentDestination
                {
                    Stops = new List<ResidentVisitStop> { stop },
                    ExitWalkSeconds = 0.001,
                    ExitRoute = Reverse(stop.Route)
                };
                double departure = initialOfDay - 1800d;
                var temporaryRoutine = new ResidentRoutine
                {
                    ResidentId = residentId,
                    Itinerary = itinerary,
                    ItineraryDepartureSeconds = departure
                };
                var temporarySchedule = DailySchedule.ItineraryDay(actor.Resident.HomeKey, itinerary, departure,
                    wake * 60, sleep * 60);
                var journey = ResidentVisitJourney.Create(departure, itinerary);
                var pre = journey.Evaluate(initialOfDay);
                if (pre.Phase != ResidentVisitPhase.Walking)
                    throw new InvalidOperationException("Outdoor loiter fixture did not start on a walking route.");

                routineField.SetValue(chosenPlan, temporaryRoutine);
                scheduleField.SetValue(chosenPlan, temporarySchedule);
                hauntedField.SetValue(chosenPlan, temporarySchedule);

                var preGround = geometry.ScenePoint(pre.Position);
                if ((preGround - game.PlayerMotor.transform.position).sqrMagnitude > 140f * 140f)
                    throw new InvalidOperationException("Outdoor loiter fixture route midpoint is outside stable exterior visibility.");
                var doorPoint = stop.Route[stop.Route.Count - 1];
                var doorGround = geometry.ScenePoint(new RoutePoint(doorPoint.X, doorPoint.Z));
                if (motion) motion.ResetPlacement();
                actor.transform.position = preGround + Vector3.up;
                actor.Place(preGround, doorGround - preGround, true);
                yield return null;

                DaggerfallUI.PostMessage(DaggerfallUIMessages.dfuiOpenRestWindow);
                float windowDeadline = Time.realtimeSinceStartup + 10f;
                while (!(DaggerfallUI.UIManager.TopWindow is DaggerfallRestWindow) &&
                    Time.realtimeSinceStartup < windowDeadline) yield return null;
                var rest = DaggerfallUI.UIManager.TopWindow as DaggerfallRestWindow;
                if (rest == null)
                    throw new InvalidOperationException("Outdoor loiter regression could not open native rest window.");
                openedRestWindow = true;
                double from = clock.ToSeconds();
                double fromLocal = from - Math.Floor(from / 86400d) * 86400d;
                var preNow = journey.Evaluate(fromLocal);
                var preState = streets.GetState(residentId, from);
                Vector3 preExpectedGround = geometry.ScenePoint(preNow.Position);
                Vector3 preJumpPosition = actor.transform.position - Vector3.up;
                if (preNow.Phase != ResidentVisitPhase.Walking || preState.Activity != Activity.Travel || streets.FindActor(residentId) != actor ||
                    CountResidentActors(residentId) != 1 || !actor.gameObject.activeInHierarchy ||
                    Vector3.Distance(preJumpPosition, preExpectedGround) > 1f)
                    throw new InvalidOperationException("Outdoor loiter fixture failed pre-wait validation: phase=" + preNow.Phase +
                        "; state=" + preState.Activity + "; actorCount=" + CountResidentActors(residentId) +
                        "; routeDistance=" + Vector3.Distance(preJumpPosition, preExpectedGround).ToString("F2"));

                int oldActorInstance = actor.GetInstanceID();
                string routeHash = RouteHash(stop.Route);
                double lastObservedBeforeLoiter = (double)GetField(typeof(StreetPopulation), "lastPopulationSeconds").GetValue(streets);
                if (lastObservedBeforeLoiter < 0 || from - lastObservedBeforeLoiter > 30)
                    throw new InvalidOperationException("Outdoor loiter regression did not start from a recently observed exterior frame: from=" +
                        from.ToString("F3") + "; lastObserved=" + lastObservedBeforeLoiter.ToString("F3"));
                typeof(DaggerfallRestWindow).GetMethod("LoiterPrompt_OnGotUserInput", Private)
                    .Invoke(rest, new object[] { null, "1" });
                float loiterDeadline = Time.realtimeSinceStartup + 45f;
                while (clock.ToSeconds() - from < 3600d && Time.realtimeSinceStartup < loiterDeadline) yield return null;
                double to = clock.ToSeconds();
                double lastObservedAtClose = (double)GetField(typeof(StreetPopulation), "lastPopulationSeconds").GetValue(streets);
                if (to - from < 3600d)
                    throw new InvalidOperationException("Native one-hour exterior loiter ended before advancing 3600 game seconds.");
                bool skippedHour = ShopVisitJourney.NeedsCatchup(lastObservedAtClose, to, Time.unscaledDeltaTime,
                    DaggerfallUnity.Instance.WorldTime.TimeScale);
                if (!skippedHour)
                    throw new InvalidOperationException("Native one-hour loiter did not leave a population-update gap: from=" +
                        from.ToString("F3") + "; lastObserved=" + lastObservedAtClose.ToString("F3") + "; to=" + to.ToString("F3"));
                DaggerfallUI.Instance.PopToHUD();
                game.PauseGame(false);

                double toLocal = to - Math.Floor(to / 86400d) * 86400d;
                if (toLocal < fromLocal)
                    throw new InvalidOperationException("Outdoor loiter regression crossed a calendar day.");
                var expected = journey.Evaluate(toLocal);
                if (!skippedHour || expected.Phase != ResidentVisitPhase.Inside)
                    throw new InvalidOperationException("Native loiter fixture did not produce a skipped walking-to-inside interval: skip=" +
                        skippedHour + "; phase=" + expected.Phase + "; from=" + from + "; to=" + to);

                var postState = streets.GetState(residentId, to);
                double visitStart, visitEnd;
                if (postState.Activity != Activity.Visit || postState.BuildingKey != stop.BuildingKey ||
                    !temporarySchedule.TryVisitWindowForBuilding(to, stop.BuildingKey, out visitStart, out visitEnd))
                    throw new InvalidOperationException("Outdoor loiter fixture did not project to its expected logical visit: activity=" +
                        postState.Activity + "; building=" + postState.BuildingKey + "; expectedBuilding=" + stop.BuildingKey);

                double dayBase = Math.Floor(to / 86400d) * 86400d;
                double expectedStart = dayBase + expected.EnterAt;
                double expectedEnd = dayBase + expected.LeaveAt;
                double expectedRemaining = expected.LeaveAt - toLocal;
                if (expectedRemaining <= 0 || Math.Abs(visitStart - expectedStart) > 0.01 ||
                    Math.Abs(visitEnd - expectedEnd) > 0.01 || Math.Abs((visitEnd - to) - expectedRemaining) > 0.01)
                    throw new InvalidOperationException("Outdoor loiter logical visit has wrong arrival or remaining dwell.");

                int repeatedFrames = 0;
                int postActorCount = -1;
                while (repeatedFrames < 12)
                {
                    yield return null;
                    repeatedFrames++;
                    postActorCount = CountResidentActors(residentId);
                    var retainedActor = streets.FindActor(residentId);
                    if (retainedActor != null || postActorCount != 0)
                        throw new InvalidOperationException("Outdoor one-hour loiter retained stale exterior actor after skip: resident=" +
                            residentId + "; from=" + from.ToString("F3") + "; to=" + to.ToString("F3") +
                            "; expected=Inside; oldActorInstance=" + oldActorInstance +
                            "; actualActorInstance=" + (retainedActor ? retainedActor.GetInstanceID() : 0) +
                            "; actualPosition=" + (retainedActor ? retainedActor.transform.position - Vector3.up : Vector3.zero) +
                            "; movedAfterSkip=" + (retainedActor ? Vector3.Distance(preJumpPosition, retainedActor.transform.position - Vector3.up) : 0).ToString("F2") +
                            "; doorGap=" + (retainedActor ? Vector3.Distance(retainedActor.transform.position - Vector3.up, doorGround) : 0).ToString("F2") +
                            "; routeHash=" + routeHash + "; actorCount=" + postActorCount +
                            "; dwellRemaining=" + expectedRemaining.ToString("F3"));
                }

                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_OUTDOOR_LOITER_CATCHUP_PASS: resident=" + residentId +
                    "; from=" + from.ToString("F3") + "; to=" + to.ToString("F3") +
                    "; pre=Walking; post=Inside; building=" + stop.BuildingKey +
                    "; enterAt=" + visitStart.ToString("F3") + "; leaveAt=" + visitEnd.ToString("F3") +
                    "; remainingDwell=" + expectedRemaining.ToString("F3") + "; oldActorInstance=" + oldActorInstance +
                    "; prePosition=" + preJumpPosition + "; routeHash=" + routeHash + "; actorCount=" + postActorCount +
                    "; repeatedFrames=" + repeatedFrames);
            }
            finally
            {
                routineField.SetValue(chosenPlan, oldRoutine);
                scheduleField.SetValue(chosenPlan, oldSchedule);
                hauntedField.SetValue(chosenPlan, oldHauntedSchedule);
                if (actor)
                {
                    actor.transform.position = savedPosition;
                    actor.transform.rotation = savedRotation;
                    if (motion) motion.ResetPlacement();
                }
                RestoreAppointmentCache(residentId, savedAppointmentCache);
                if (openedRestWindow)
                {
                    DaggerfallUI.Instance.PopToHUD();
                    game.PauseGame(false);
                }
            }
            yield break;
        }

        private static IEnumerator RunSkippedHourTravelCatchup(PeopleOfTheIliacBayMod adapter, StreetPopulation streets,
            BuildingDirectory directory, TownGeometry geometry, IList plans, TownRoutineState town, HashSet<string> tripIds,
            string excludedResidentId)
        {
            var game = GameManager.Instance;
            if (!game.IsPlayerOnHUD)
                throw new InvalidOperationException("Outdoor loiter regression requires player HUD before opening native rest window.");
            var clock = DaggerfallUnity.Instance.WorldTime.Now;
            var snapshot = adapter.GetSaveData() as WorldState;
            var worldStore = GetField(typeof(StreetPopulation), "worldStore").GetValue(streets) as WorldStateStore;
            if (snapshot == null || worldStore == null)
                throw new InvalidOperationException("Outdoor loiter regression requires live routine and world-state stores.");

            object chosenPlan = null;
            ResidentActor actor = null;
            ResidentVisitStop sourceStop = null;
            foreach (var routine in town.Residents)
            {
                if (routine == null || string.IsNullOrEmpty(routine.ResidentId) || routine.ResidentId == excludedResidentId ||
                    tripIds.Contains(routine.ResidentId)) continue;
                var candidate = streets.FindActor(routine.ResidentId);
                if (!candidate || !candidate.gameObject.activeInHierarchy || candidate.transform.parent != directory.transform ||
                    !candidate.Resident.IsAlive || candidate.Resident.IsQuestProtected ||
                    candidate.Resident.AgeGroup != ResidentAgeGroup.Adult || streets.IsAlarmed(routine.ResidentId) ||
                    worldStore.IsResidentAway(routine.ResidentId, clock.ToSeconds()) ||
                    worldStore.ActiveNpcAction(routine.ResidentId) != null || worldStore.ActiveNpcTask(routine.ResidentId) != null)
                    continue;
                if (!CanControlAmbientSchedule(worldStore, candidate.Resident, clock.ToSeconds())) continue;
                var appointments = worldStore.AppointmentsFor(routine.ResidentId);
                double currentDay = Math.Floor(clock.ToSeconds() / 86400d);
                if (appointments.Exists(item => Math.Floor(item.StartsAt / 86400d) == currentDay && item.Status != "cancelled"))
                    continue;

                var questOwned = typeof(StreetPopulation).GetMethod("QuestOwned", BindingFlags.Instance | BindingFlags.NonPublic);
                if (questOwned != null && (bool)questOwned.Invoke(streets, new object[] { candidate.Resident })) continue;
                if (worldStore.GetObservedVisit(routine.ResidentId) != null) continue;
                var contactMotions = GetField(typeof(StreetPopulation), "proactiveContactMotions").GetValue(streets) as IDictionary;
                if (contactMotions != null && contactMotions.Contains(routine.ResidentId)) continue;

                var plan = FindPlan(plans, routine.ResidentId);
                if (plan == null || routine.Itinerary == null || routine.Itinerary.Stops == null) continue;
                foreach (var stop in routine.Itinerary.Stops)
                {
                    double routeLength = RouteLength(stop == null ? null : stop.Route);
                    if (stop == null || stop.Route == null || stop.Route.Count < 2 || routeLength < 5.5) continue;
                    var endpoint = stop.Route[stop.Route.Count - 1];
                    Vector3 door = geometry.ScenePoint(new RoutePoint(endpoint.X, endpoint.Z));
                    if ((door - game.PlayerMotor.transform.position).sqrMagnitude > 165f * 165f) continue;
                    // A 6,000-second walk starts at 20% and remains at about
                    // 80% after one hour. Require both analytical points visible
                    // and a world-space target change large enough to test catch-up.
                    var beforePoint = PointFromEnd(stop.Route, routeLength * 0.8);
                    var afterPoint = PointFromEnd(stop.Route, routeLength * 0.2);
                    Vector3 beforeGround = geometry.ScenePoint(new RoutePoint(beforePoint.X, beforePoint.Z));
                    Vector3 afterGround = geometry.ScenePoint(new RoutePoint(afterPoint.X, afterPoint.Z));
                    if ((beforeGround - game.PlayerMotor.transform.position).sqrMagnitude > 140f * 140f ||
                        (afterGround - game.PlayerMotor.transform.position).sqrMagnitude > 140f * 140f ||
                        Vector3.Distance(beforeGround, afterGround) <= 3f) continue;
                    chosenPlan = plan;
                    actor = candidate;
                    sourceStop = stop;
                    break;
                }
                if (actor) break;
            }
            if (!actor)
                throw new InvalidOperationException("No second nearby ambient resident actor is eligible for the outdoor loiter regression.");

            var planType = chosenPlan.GetType();
            var routineField = GetField(planType, "Routine");
            var scheduleField = GetField(planType, "Schedule");
            var hauntedField = GetField(planType, "HauntedSchedule");
            object oldRoutine = routineField.GetValue(chosenPlan);
            object oldSchedule = scheduleField.GetValue(chosenPlan);
            object oldHauntedSchedule = hauntedField.GetValue(chosenPlan);
            Vector3 savedPosition = actor.transform.position;
            Quaternion savedRotation = actor.transform.rotation;
            var motion = actor.GetComponent<CrowdMotion>();
            string residentId = actor.Resident.Id;
            bool openedRestWindow = false;
            object[] savedAppointmentCache = SuspendAppointmentCache(streets, residentId);

            try
            {
                double initial = clock.ToSeconds();
                double initialDay = Math.Floor(initial / 86400d) * 86400d;
                double initialOfDay = initial - initialDay;
                int wake = ReadHour(streets, "wakeHour", 6), sleep = ReadHour(streets, "sleepHour", 22);
                if (initialOfDay < wake * 3600d + 1800d || initialOfDay + 9000.1 >= sleep * 3600d)
                    throw new InvalidOperationException("Outdoor loiter regression needs 2.5 waking hours remaining today.");

                var stop = CopyStopRoute(sourceStop);
                stop.OpenMinute = stop.CloseMinute = 0;
                stop.WalkSeconds = 6000;
                stop.DwellSeconds = 7200;
                var itinerary = new ResidentDestination
                {
                    Stops = new List<ResidentVisitStop> { stop },
                    ExitWalkSeconds = 0.001,
                    ExitRoute = Reverse(stop.Route)
                };
                double departure = initialOfDay - 1200d;
                var temporaryRoutine = new ResidentRoutine
                {
                    ResidentId = residentId,
                    Itinerary = itinerary,
                    ItineraryDepartureSeconds = departure
                };
                var temporarySchedule = DailySchedule.ItineraryDay(actor.Resident.HomeKey, itinerary, departure,
                    wake * 60, sleep * 60);
                var journey = ResidentVisitJourney.Create(departure, itinerary);
                var pre = journey.Evaluate(initialOfDay);
                if (pre.Phase != ResidentVisitPhase.Walking)
                    throw new InvalidOperationException("Outdoor loiter fixture did not start on a walking route.");

                routineField.SetValue(chosenPlan, temporaryRoutine);
                scheduleField.SetValue(chosenPlan, temporarySchedule);
                hauntedField.SetValue(chosenPlan, temporarySchedule);

                var preGround = geometry.ScenePoint(pre.Position);
                if ((preGround - game.PlayerMotor.transform.position).sqrMagnitude > 140f * 140f)
                    throw new InvalidOperationException("Outdoor loiter fixture route midpoint is outside stable exterior visibility.");
                var doorPoint = stop.Route[stop.Route.Count - 1];
                var doorGround = geometry.ScenePoint(new RoutePoint(doorPoint.X, doorPoint.Z));
                if (motion) motion.ResetPlacement();
                actor.transform.position = preGround + Vector3.up;
                actor.Place(preGround, doorGround - preGround, true);
                yield return null;

                DaggerfallUI.PostMessage(DaggerfallUIMessages.dfuiOpenRestWindow);
                float windowDeadline = Time.realtimeSinceStartup + 10f;
                while (!(DaggerfallUI.UIManager.TopWindow is DaggerfallRestWindow) &&
                    Time.realtimeSinceStartup < windowDeadline) yield return null;
                var rest = DaggerfallUI.UIManager.TopWindow as DaggerfallRestWindow;
                if (rest == null)
                    throw new InvalidOperationException("Outdoor loiter regression could not open native rest window.");
                openedRestWindow = true;
                double from = clock.ToSeconds();
                double fromLocal = from - Math.Floor(from / 86400d) * 86400d;
                var preNow = journey.Evaluate(fromLocal);
                var preState = streets.GetState(residentId, from);
                Vector3 preExpectedGround = geometry.ScenePoint(preNow.Position);
                Vector3 preJumpPosition = actor.transform.position - Vector3.up;
                if (preNow.Phase != ResidentVisitPhase.Walking || preState.Activity != Activity.Travel || streets.FindActor(residentId) != actor ||
                    CountResidentActors(residentId) != 1 || !actor.gameObject.activeInHierarchy ||
                    Vector3.Distance(preJumpPosition, preExpectedGround) > 1f)
                    throw new InvalidOperationException("Outdoor loiter fixture failed pre-wait validation: phase=" + preNow.Phase +
                        "; state=" + preState.Activity + "; actorCount=" + CountResidentActors(residentId) +
                        "; routeDistance=" + Vector3.Distance(preJumpPosition, preExpectedGround).ToString("F2"));

                int oldActorInstance = actor.GetInstanceID();
                string routeHash = RouteHash(stop.Route);
                double lastObservedBeforeLoiter = (double)GetField(typeof(StreetPopulation), "lastPopulationSeconds").GetValue(streets);
                if (lastObservedBeforeLoiter < 0 || from - lastObservedBeforeLoiter > 30)
                    throw new InvalidOperationException("Outdoor loiter regression did not start from a recently observed exterior frame: from=" +
                        from.ToString("F3") + "; lastObserved=" + lastObservedBeforeLoiter.ToString("F3"));
                typeof(DaggerfallRestWindow).GetMethod("LoiterPrompt_OnGotUserInput", Private)
                    .Invoke(rest, new object[] { null, "1" });
                float loiterDeadline = Time.realtimeSinceStartup + 45f;
                while (clock.ToSeconds() - from < 3600d && Time.realtimeSinceStartup < loiterDeadline) yield return null;
                double to = clock.ToSeconds();
                double lastObservedAtClose = (double)GetField(typeof(StreetPopulation), "lastPopulationSeconds").GetValue(streets);
                if (to - from < 3600d)
                    throw new InvalidOperationException("Native one-hour exterior loiter ended before advancing 3600 game seconds.");
                bool skippedHour = ShopVisitJourney.NeedsCatchup(lastObservedAtClose, to, Time.unscaledDeltaTime,
                    DaggerfallUnity.Instance.WorldTime.TimeScale);
                if (!skippedHour)
                    throw new InvalidOperationException("Native one-hour loiter did not leave a population-update gap: from=" +
                        from.ToString("F3") + "; lastObserved=" + lastObservedAtClose.ToString("F3") + "; to=" + to.ToString("F3"));
                DaggerfallUI.Instance.PopToHUD();
                game.PauseGame(false);

                double toLocal = to - Math.Floor(to / 86400d) * 86400d;
                if (toLocal < fromLocal)
                    throw new InvalidOperationException("Outdoor loiter regression crossed a calendar day.");
                var expected = journey.Evaluate(toLocal);
                if (!skippedHour || expected.Phase != ResidentVisitPhase.Walking)
                    throw new InvalidOperationException("Native loiter fixture did not remain on its walking route after skip: skip=" +
                        skippedHour + "; phase=" + expected.Phase + "; from=" + from + "; to=" + to);

                var postState = streets.GetState(residentId, to);
                Vector3 expectedGround = geometry.ScenePoint(expected.Position);
                Vector3 scheduledGround = geometry.ScenePoint(postState.Position);
                float targetShift = Vector3.Distance(preExpectedGround, expectedGround);
                if (postState.Activity != Activity.Travel || targetShift <= 3f ||
                    Vector3.Distance(expectedGround, game.PlayerMotor.transform.position) > 140f ||
                    Vector3.Distance(scheduledGround, expectedGround) > 0.01f)
                    throw new InvalidOperationException("Native loiter fixture did not retain its analytically advanced visible travel route: activity=" +
                        postState.Activity + "; phase=" + expected.Phase + "; targetShift=" + targetShift.ToString("F2") +
                        "; scheduleDistance=" + Vector3.Distance(scheduledGround, expectedGround).ToString("F2"));

                int repeatedFrames = 0;
                int postActorCount = 0;
                double observedAt = -1;
                double updateDeadline = Time.realtimeSinceStartup + 5f;
                while (repeatedFrames < 120 && Time.realtimeSinceStartup < updateDeadline)
                {
                    yield return null;
                    repeatedFrames++;
                    observedAt = (double)GetField(typeof(StreetPopulation), "lastPopulationSeconds").GetValue(streets);
                    if (observedAt + 0.01 < to) continue;
                    yield return null;
                    repeatedFrames++;
                    postActorCount = CountResidentActors(residentId);
                    var retainedActor = streets.FindActor(residentId);
                    if (retainedActor && postActorCount == 1) break;
                }
                var postActor = streets.FindActor(residentId);
                Vector3 postGround = postActor ? postActor.transform.position - Vector3.up : Vector3.zero;
                float postTargetError = postActor ? Vector3.Distance(postGround, expectedGround) : float.PositiveInfinity;
                float movedAfterSkip = postActor ? Vector3.Distance(preJumpPosition, postGround) : 0;
                if (observedAt + 0.01 < to || !postActor || postActor != actor || postActorCount != 1 ||
                    !postActor.gameObject.activeInHierarchy || postActor.transform.parent != directory.transform ||
                    movedAfterSkip <= 1.5f || postTargetError > 1.25f)
                    throw new InvalidOperationException("Outdoor one-hour loiter did not reconstruct the same actor at its analytical travel point: resident=" +
                        residentId + "; from=" + from.ToString("F3") + "; to=" + to.ToString("F3") +
                        "; observedAt=" + observedAt.ToString("F3") + "; expected=Travel; oldActorInstance=" + oldActorInstance +
                        "; actualActorInstance=" + (postActor ? postActor.GetInstanceID() : 0) +
                        "; prePosition=" + preJumpPosition + "; actualPosition=" + postGround +
                        "; movedAfterSkip=" + movedAfterSkip.ToString("F2") + "; targetShift=" + targetShift.ToString("F2") +
                        "; targetError=" + postTargetError.ToString("F2") + "; routeHash=" + routeHash + "; actorCount=" + postActorCount+
                        "; native="+NativeState(postActor));

                // A reconstructed body and its native proxy must share the new
                // clock position. Later native updates must not pull this same
                // actor back to the pre-wait proxy or recreate another body.
                Vector3 priorGround=postGround;
                for(int frame=0;frame<12;frame++)
                {
                    yield return null;
                    var retained=streets.FindActor(residentId);
                    if(!retained || retained!=actor || !retained.gameObject.activeInHierarchy || CountResidentActors(residentId)!=1)
                        throw new InvalidOperationException("Outdoor loiter travel reconstruction lost its single original actor.");
                    var ground=retained.transform.position-Vector3.up;
                    if(Vector3.Distance(priorGround,ground)>0.551f)
                        throw new InvalidOperationException("Outdoor loiter native proxy displaced its reconstructed actor after ordinary movement resumed: native="+NativeState(retained));
                    priorGround=ground;
                }

                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_OUTDOOR_LOITER_TRAVEL_CATCHUP_PASS: resident=" + residentId +
                    "; from=" + from.ToString("F3") + "; to=" + to.ToString("F3") +
                    "; pre=Travel; post=Travel; walkSeconds=" + stop.WalkSeconds.ToString("F0") +
                    "; targetShift=" + targetShift.ToString("F2") + "; targetError=" + postTargetError.ToString("F2") +
                    "; movedAfterSkip=" + movedAfterSkip.ToString("F2") + "; sameActor=true; actorInstance=" + oldActorInstance +
                    "; prePosition=" + preJumpPosition + "; postPosition=" + postGround +
                    "; routeHash=" + routeHash + "; actorCount=" + postActorCount + "; repeatedFrames=" + repeatedFrames+
                    "; nativeProxyRetained=true; boundedFrames=12");
            }
            finally
            {
                routineField.SetValue(chosenPlan, oldRoutine);
                scheduleField.SetValue(chosenPlan, oldSchedule);
                hauntedField.SetValue(chosenPlan, oldHauntedSchedule);
                if (actor)
                {
                    actor.transform.position = savedPosition;
                    actor.transform.rotation = savedRotation;
                    if (motion) motion.ResetPlacement();
                }
                RestoreAppointmentCache(residentId, savedAppointmentCache);
                if (openedRestWindow)
                {
                    DaggerfallUI.Instance.PopToHUD();
                    game.PauseGame(false);
                }
            }
            yield break;
        }

        private static bool CanControlAmbientSchedule(WorldStateStore store, Resident resident, double now)
        {
            if (resident == null || !resident.IsAlive || resident.IsQuestProtected ||
                resident.AgeGroup != ResidentAgeGroup.Adult || store.IsResidentAway(resident.Id, now) ||
                store.ActiveNpcAction(resident.Id) != null || store.ActiveNpcTask(resident.Id) != null ||
                store.HasRoutineRecovery(resident.Id) || store.GetObservedVisit(resident.Id) != null) return false;
            AlarmPerson departure;
            bool home;
            if (store.TryVisitDeparturePosition(resident.Id, now, out departure, out home)) return false;
            double day = Math.Floor(now / 86400d);
            return !store.AppointmentsFor(resident.Id).Exists(item =>
                Math.Floor(item.StartsAt / 86400d) == day && item.Status != "cancelled");
        }

        private static object[] SuspendAppointmentCache(StreetPopulation streets, string residentId)
        {
            var cache = (IDictionary)GetField(typeof(StreetPopulation), "appointmentSchedules").GetValue(streets);
            bool existed = cache.Contains(residentId);
            object prior = existed ? cache[residentId] : null;
            cache.Remove(residentId);
            return new object[] { cache, existed, prior };
        }

        private static void RestoreAppointmentCache(string residentId, object[] saved)
        {
            var cache = (IDictionary)saved[0];
            cache.Remove(residentId);
            if ((bool)saved[1]) cache[residentId] = saved[2];
        }

        private static object FindPlan(IList plans, string residentId)
        {
            foreach (var plan in plans)
            {
                if (plan == null) continue;
                var resident = GetField(plan.GetType(), "Resident").GetValue(plan) as Resident;
                if (resident != null && resident.Id == residentId) return plan;
            }
            return null;
        }

        private static int CountResidentActors(string id)
        {
            int count = 0;
            foreach (var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                if (actor && actor.Resident != null && actor.Resident.Id == id) count++;
            return count;
        }

        private static string NativeState(ResidentActor actor)
        {
            if(!actor)return "missing-body";
            var native=actor.GetComponent<NativeResidentNavigation>();
            var crowd=actor.GetComponent<CrowdMotion>();
            return "placed="+(crowd&&crowd.HasPlacement)+",active="+(native&&native.Active)+
                ",hasDestination="+(native&&native.HasDestination)+",destination="+(native?native.Destination.ToString("F5"):"none")+
                ",path="+(native?native.PathState:"none")+",failure="+(native?native.FailureReason:"none");
        }

        private static FieldInfo GetField(Type type, string name)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (field == null) throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static int ReadHour(StreetPopulation streets, string name, int fallback)
        {
            var field = GetField(typeof(StreetPopulation), name);
            return field.GetValue(streets) is int ? (int)field.GetValue(streets) : fallback;
        }

        private static ResidentVisitStop CopyStopRoute(ResidentVisitStop source)
        {
            var result = new ResidentVisitStop
            {
                BuildingKey = source.BuildingKey,
                Purpose = source.Purpose,
                OpenMinute = source.OpenMinute,
                CloseMinute = source.CloseMinute,
                WalkSeconds = source.WalkSeconds,
                DwellSeconds = source.DwellSeconds,
                Route = new List<SavedRoutePoint>()
            };
            foreach (var point in source.Route)
                result.Route.Add(new SavedRoutePoint { X = point.X, Z = point.Z });
            return result;
        }

        private static ResidentVisitStop LegacyRoutineStop(ResidentRoutine routine)
        {
            var route = new List<SavedRoutePoint>();
            foreach (var point in routine.Route)
                route.Add(new SavedRoutePoint { X = point.X, Z = point.Z });
            return new ResidentVisitStop
            {
                BuildingKey = routine.DestinationKey,
                Purpose = ResidentVisitPurpose.Social,
                Route = route
            };
        }

        private static List<SavedRoutePoint> Reverse(IList<SavedRoutePoint> route)
        {
            var result = new List<SavedRoutePoint>();
            for (int i = route.Count - 1; i >= 0; i--)
                result.Add(new SavedRoutePoint { X = route[i].X, Z = route[i].Z });
            return result;
        }

        private static double RouteLength(IList<SavedRoutePoint> route)
        {
            double length = 0;
            for (int i = 1; i < route.Count; i++)
            {
                double dx = route[i].X - route[i - 1].X, dz = route[i].Z - route[i - 1].Z;
                length += Math.Sqrt(dx * dx + dz * dz);
            }
            return length;
        }

        private static string RouteHash(IList<SavedRoutePoint> route)
        {
            unchecked
            {
                uint hash = 2166136261;
                foreach (var point in route)
                {
                    hash = (hash ^ (uint)BitConverter.DoubleToInt64Bits(point.X)) * 16777619;
                    hash = (hash ^ (uint)(BitConverter.DoubleToInt64Bits(point.X) >> 32)) * 16777619;
                    hash = (hash ^ (uint)BitConverter.DoubleToInt64Bits(point.Z)) * 16777619;
                    hash = (hash ^ (uint)(BitConverter.DoubleToInt64Bits(point.Z) >> 32)) * 16777619;
                }
                return hash.ToString("X8");
            }
        }

        private static SavedRoutePoint PointFromEnd(IList<SavedRoutePoint> route, double fromEnd)
        {
            double remaining = fromEnd;
            for (int i = route.Count - 1; i > 0; i--)
            {
                var end = route[i]; var start = route[i - 1];
                double dx = end.X - start.X, dz = end.Z - start.Z;
                double segment = Math.Sqrt(dx * dx + dz * dz);
                if (segment <= 0) continue;
                if (remaining <= segment)
                {
                    double fraction = remaining / segment;
                    return new SavedRoutePoint { X = end.X - dx * fraction, Z = end.Z - dz * fraction };
                }
                remaining -= segment;
            }
            return route[0];
        }
    }
}
