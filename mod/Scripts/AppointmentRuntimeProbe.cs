using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Runs only in the isolated, disposable native test process.
    // It never writes a player save; persistence check uses FullSerializer in memory.
    public static class AppointmentRuntimeProbe
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const int NoShowVenuePreflightLimit = 6;
        // A diameter greater than twice the staging margin guarantees a far
        // surveyed endpoint even when the NPC stands at the room's center.
        private const float NoShowRequiredApproachDiameter = 2 * NoShowObserverDestinationGap + 0.25f;
        private const int NoShowApproachPointLimit = 256;
        private const float NoShowObserverTriggerGap = 11.5f;
        private const float NoShowObserverDestinationGap = 12.5f;
        private const float NoShowObserverRouteGap = 9.5f;
        private const int MaxNoShowObserverRelocations = 8;
        private const double NoShowObservationPollMarginSeconds = 15;

        private sealed class NoShowVenueCandidate
        {
            public Resident Person;
            public ResidentRoutine Routine;
            public int VenueKey;
            public double RouteMetres;
        }

        private sealed class NoShowVenuePreflightResult
        {
            public bool Passed;
            public int ApproachPointCount;
            public float ApproachDiameter;
        }

        private sealed class IndoorExitEvidence
        {
            public double StartedAt, ReachedAt;
            public float TotalMoved, MaximumStep, EndpointError;
            public int Samples;
            public bool BodyRemovedAtExit, OutdoorMotionAvailable;
        }

        private static double Now()
        {
            var clock = DaggerfallUnity.Instance.WorldTime.Now;
            // Combine the fractional expression first. ulong + float would round
            // the entire DFU epoch to 1024-second intervals before conversion.
            return clock.ToSeconds() + (clock.Second - Math.Floor(clock.Second));
        }

        public static IEnumerator Run(PeopleOfTheIliacBayMod host, StreetPopulation streets, string character,bool playerNoShow=false)
        {
            var motor = GameManager.Instance.PlayerMotor;
            var controller = motor.GetComponent<CharacterController>();
            bool motorEnabled = motor.enabled;
            bool controllerEnabled = controller && controller.enabled;
            motor.enabled = false;
            if (controller) controller.enabled = false;
            try { yield return RunCore(host, streets, character,playerNoShow); }
            finally
            {
                if (controller) controller.enabled = controllerEnabled;
                motor.enabled = motorEnabled;
            }
        }

        private static IEnumerator RunCore(PeopleOfTheIliacBayMod host, StreetPopulation streets, string character,bool playerNoShow)
        {
            VerifyNativeCalendar();
            var game = GameManager.Instance;
            game.SaveLoadManager.EnumerateSaves();
            int slot = game.SaveLoadManager.FindSaveFolderByNames(character, "People - Daggerfall day");
            if (slot < 0) throw new InvalidOperationException("Owned People - Daggerfall day fixture save missing.");
            bool loaded = false;
            SaveLoadManager.OnLoadEventHandler handler = delegate(SaveData_v1 ignored) { loaded = true; };
            SaveLoadManager.OnLoad += handler;
            try
            {
                game.SaveLoadManager.Load(slot);
                DaggerfallUI.Instance.PopToHUD();
                float deadline = Time.realtimeSinceStartup + 120;
                while ((!loaded || !streets.Ready || !ExteriorReady(streets)) && Time.realtimeSinceStartup < deadline)
                    yield return null;
                if (!loaded || !streets.Ready || !ExteriorReady(streets))
                    throw new InvalidOperationException("Owned appointment fixture did not finish exterior setup.");
            }
            finally { SaveLoadManager.OnLoad -= handler; }

            if (game.PlayerEntity.Name != character)
                throw new InvalidOperationException("Appointment probe did not load its owned fixture character.");

            if (game.IsPlayerInside)
                throw new InvalidOperationException("Appointment fixture did not load outdoors.");
            var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            var geometry = (TownGeometry)typeof(StreetPopulation).GetField("geometry", Private).GetValue(streets);
            var worldBefore = (WorldState)host.GetSaveData();
            var population = worldBefore.Settlements.Find(item => item.SettlementId == directory.MapID);
            var town = worldBefore.Routines.Find(item => item.SettlementId == directory.MapID);
            if (population == null || town == null || town.Residents == null)
                throw new InvalidOperationException("Fixture has no local resident routine ledger.");

            Resident selected = null;
            int venueKey = -1;
            double bestMetres = double.MaxValue;
            bool selectedSocialVenue=false;
            var noShowVenueCandidates = playerNoShow ? new List<NoShowVenueCandidate>() : null;
            foreach (var routine in town.Residents)
            {
                if (routine.Itinerary == null || routine.Itinerary.Stops == null || routine.Itinerary.Stops.Count == 0 ||
                    worldBefore.ResidentTrips.Exists(trip => trip.ResidentId == routine.ResidentId)) continue;
                ResidentVisitStop stop = null, firstSocialTavern=null;
                double metres = 0, socialTavernMetres=0;
                foreach (var candidateStop in routine.Itinerary.Stops)
                {
                    if (candidateStop.Route == null || candidateStop.Route.Count < 2) { metres = double.MaxValue; break; }
                    metres += Length(candidateStop.Route);
                    BuildingSummary socialSummary;
                    if(candidateStop.Purpose==ResidentVisitPurpose.Social && firstSocialTavern==null &&
                        directory.GetBuildingSummary(candidateStop.BuildingKey,out socialSummary) &&
                        socialSummary.BuildingType==DaggerfallConnect.DFLocation.BuildingTypes.Tavern)
                    {firstSocialTavern=candidateStop;socialTavernMetres=metres;}
                    if(!playerNoShow && (candidateStop.Purpose==ResidentVisitPurpose.Social || candidateStop.Purpose==ResidentVisitPurpose.Shop))
                    {stop=candidateStop;break;}
                }
                if(playerNoShow)
                {
                    // Shop interiors can be too small to preserve separation while
                    // the resident naturally walks between browsed items. Use only
                    // an existing saved Social stop that is a native tavern.
                    stop=firstSocialTavern;
                    metres=socialTavernMetres;
                }
                if (stop == null || !geometry.Entrances.ContainsKey(stop.BuildingKey))
                    continue;
                Resident person = null;
                foreach (var household in population.Households)
                    foreach (var member in household.Residents)
                        if (member.Id == routine.ResidentId && member.IsAlive && member.AgeGroup == ResidentAgeGroup.Adult &&
                            member.NativeIdentity == null && !member.IsQuestProtected) person = member;
                if (person == null || !geometry.Entrances.ContainsKey(person.HomeKey)) continue;
                double physicalLimit = ResidentMovement.WalkSpeedFor(person.AppearanceSeed) * 150;
                if (metres <= 0 || metres > 100 || metres > physicalLimit ||
                    !playerNoShow && metres>=bestMetres) continue;
                if (playerNoShow)
                {
                    int existingIndex=noShowVenueCandidates.FindIndex(item=>item.VenueKey==stop.BuildingKey);
                    if(existingIndex<0) noShowVenueCandidates.Add(new NoShowVenueCandidate { Person=person, Routine=routine,
                        VenueKey=stop.BuildingKey, RouteMetres=metres });
                    else if(metres<noShowVenueCandidates[existingIndex].RouteMetres)
                        noShowVenueCandidates[existingIndex]=new NoShowVenueCandidate { Person=person, Routine=routine,
                            VenueKey=stop.BuildingKey, RouteMetres=metres };
                    continue;
                }
                selected = person;
                venueKey = stop.BuildingKey;
                bestMetres = metres;
                selectedSocialVenue=stop.Purpose==ResidentVisitPurpose.Social;
            }
            if(playerNoShow)
            {
                selected=null;
                noShowVenueCandidates.Sort((a,b)=>a.RouteMetres.CompareTo(b.RouteMetres));
                var attempted=new List<string>();
                int geometryAttempts=0;
                double noShowStartsAt=(Math.Floor(Now()/86400d)+1)*86400d+9*3600d+15*60d;
                foreach(var candidate in noShowVenueCandidates)
                {
                    string forecastReason;
                    if(!ForecastNoShowRecovery(worldBefore,streets,candidate.Routine,candidate.Person,
                        directory.MapID,candidate.VenueKey,noShowStartsAt,out forecastReason))
                    {
                        attempted.Add(candidate.VenueKey+"/recovery=unavailable/"+forecastReason);
                        continue;
                    }
                    if(geometryAttempts>=NoShowVenuePreflightLimit)break;
                    var result=new NoShowVenuePreflightResult();
                    yield return PreflightNoShowVenue(game,streets,candidate.VenueKey,result);
                    attempted.Add(candidate.VenueKey+"/"+result.ApproachPointCount+"/"+result.ApproachDiameter.ToString("F2")+"m");
                    geometryAttempts++;
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_NO_SHOW_VENUE_PREFLIGHT: venue="+candidate.VenueKey+
                        "; sampledApproachPoints="+result.ApproachPointCount+"; diameter="+result.ApproachDiameter.ToString("F2")+
                        "; passed="+result.Passed+"; source=surveyed-floors; sampleLimit="+NoShowApproachPointLimit+"; attempt="+geometryAttempts);
                    if(!result.Passed) continue;
                    selected=candidate.Person;
                    venueKey=candidate.VenueKey;
                    bestMetres=candidate.RouteMetres;
                    selectedSocialVenue=true;
                    // Scene transitions replace geometry instances; refresh before
                    // the real appointment and outdoor route checks.
                    directory=game.StreamingWorld.GetCurrentBuildingDirectory();
                    geometry=(TownGeometry)typeof(StreetPopulation).GetField("geometry",Private).GetValue(streets);
                    if(geometry==null || !geometry.Entrances.ContainsKey(venueKey))
                        throw new InvalidOperationException("Selected preflight tavern lost its surveyed native entrance after return to exterior.");
                    break;
                }
                if(selected==null)
                    throw new InvalidOperationException("Fixture has no preflighted existing Social tavern with a connected surveyed-floor diameter of at least "+
                        NoShowRequiredApproachDiameter.ToString("F2")+"m; attempts="+string.Join(",",attempted.ToArray())+
                        "; eligibleSavedTaverns="+noShowVenueCandidates.Count+"; preflightLimit="+NoShowVenuePreflightLimit+".");
            }
            if (selected == null)
                throw new InvalidOperationException(playerNoShow?
                    "Fixture has no non-travelling local adult with an existing surveyed Social tavern stop within 100m.":
                    "Fixture has no non-travelling local adult with a surveyed Social/Shop stop within 100m.");
            if(playerNoShow)
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_NO_SHOW_VENUE: resident="+selected.Id+"; venue="+venueKey+
                    "; savedPurpose="+(selectedSocialVenue?"Social":"Shop")+"; routeMetres="+bestMetres);

            var store = (WorldStateStore)typeof(StreetPopulation).GetField("worldStore", Private).GetValue(streets);
            if (store == null) throw new InvalidOperationException("Appointment world-state store is unavailable.");
            if (worldBefore.Appointments == null || worldBefore.Appointments.Exists(item => item.ResidentId == selected.Id))
                throw new InvalidOperationException("Fixture resident already has appointment records; isolated positive case is not clean.");
            if (Math.Abs(DaggerfallUnity.Instance.WorldTime.TimeScale - 2f) > 0.001f)
                throw new InvalidOperationException("Appointment runtime check requires normal 2x game clock.");
            int baselineDisposition;
            bool hadBaselineDisposition = store.TryGetActualConversationDisposition(selected.Id, out baselineDisposition);

            var beforeBytes = SaveLoadManager.Serialize(typeof(WorldState), worldBefore);
            var beforeRoundTrip = (WorldState)SaveLoadManager.Deserialize(typeof(WorldState), beforeBytes);
            if (beforeRoundTrip.Appointments == null || beforeRoundTrip.Appointments.Exists(item => item.ResidentId == selected.Id))
                throw new InvalidOperationException("Pre-booking fixture snapshot contains appointment for selected resident.");

            double now = Now();
            double absoluteDay = Math.Floor(now / 86400d) + 1;
            double startsAt = absoluteDay * 86400d + (playerNoShow ? 9*3600d+15*60d : 14*3600d);
            var appointment = new AppointmentRecord
            {
                Id = "native-runtime-probe-" + selected.Id + "-" + absoluteDay.ToString("0"),
                ResidentId = selected.Id,
                Purpose = "native runtime appointment verification",
                Place = new AppointmentPlace { Kind = "building", SettlementId = directory.MapID, BuildingKey = venueKey },
                StartsAt = startsAt,
                EndsAt = startsAt + 15*60,
                GraceSeconds = 15*60,
                PatienceSeconds = playerNoShow ? AppointmentTiming.MinimumPatienceSeconds : 0,
                Importance = playerNoShow?3:1
            };
            string reason;
            if(directory.MapID==1291010263 && !game.PlayerEntity.GlobalVars.GetGlobalVar(0))
            {
                var late=new AppointmentRecord { Id="probe-late-appointment",ResidentId=selected.Id,Purpose="meet",
                    Place=appointment.Place,StartsAt=Math.Floor(startsAt/86400)*86400+20*3600,
                    EndsAt=Math.Floor(startsAt/86400)*86400+20*3600+900,GraceSeconds=60,Importance=1 };
                if(streets.TryBookAppointment(late,out reason) || store.FindAppointment(late.Id)!=null)
                    throw new InvalidOperationException("Haunted-city booking escaped the safe daytime window.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_CURFEW_PASS: late booking refused without ledger mutation.");
            }
            // The production dialogue result binding must preserve the request's
            // calendar day even when the reply is applied on a later game day.
            VerifySameDayRefusal(host, store, selected.Id, venueKey, now);
            if(playerNoShow)
            {
                if(!streets.TryBookAppointment(appointment,out reason))
                    throw new InvalidOperationException("Native no-show booking did not pass the production route planner: "+reason);
                appointment=store.FindAppointment(appointment.Id);
                if(appointment==null || appointment.EndsAt!=startsAt+15*60 || appointment.GraceSeconds!=15*60 ||
                    appointment.PatienceSeconds<AppointmentTiming.MinimumPatienceSeconds || appointment.Importance!=3 ||
                    Math.Abs(AppointmentTiming.ArrivalDeadline(appointment)-(startsAt+appointment.PatienceSeconds))>0.01)
                    throw new InvalidOperationException("Native no-show booking did not persist a valid 10–150 minute arrival-patience window.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_NO_SHOW_PATIENCE_PASS: resident="+selected.Id+"; patienceSeconds="+
                    appointment.PatienceSeconds+"; meetingDurationSeconds="+(appointment.EndsAt-appointment.StartsAt)+
                    "; arrivalDeadline="+AppointmentTiming.ArrivalDeadline(appointment)+"; separateFields=true");
            }
            else appointment = BookThroughDialogue(host, store, selected.Id, venueKey, now - 86400, startsAt);

            var worldAfter = (WorldState)host.GetSaveData();
            if (worldAfter.Appointments == null || !worldAfter.Appointments.Exists(item => item.Id == appointment.Id) ||
                worldBefore.Appointments.Exists(item => item.Id == appointment.Id) ||
                worldAfter.Appointments.FindAll(item => item.ResidentId == selected.Id).Count != 1)
                throw new InvalidOperationException("Booking did not persist in new snapshot or changed old snapshot copy.");
            var afterBytes = SaveLoadManager.Serialize(typeof(WorldState), worldAfter);
            var afterRoundTrip = (WorldState)SaveLoadManager.Deserialize(typeof(WorldState), afterBytes);
            var independent = new WorldStateStore(0, PeopleOfTheIliacBayMod.ConversationArchiveRoot());
            independent.Restore(afterRoundTrip, now);
            var restoredBooking = independent.FindAppointment(appointment.Id);
            if (restoredBooking == null || restoredBooking.ResidentId != selected.Id ||
                restoredBooking.Place == null || restoredBooking.Place.BuildingKey != venueKey ||
                Math.Abs(restoredBooking.StartsAt - startsAt) > 0.01 || restoredBooking.Status != "accepted" ||
                restoredBooking.PatienceSeconds != appointment.PatienceSeconds ||
                Math.Abs(AppointmentTiming.ArrivalDeadline(restoredBooking)-AppointmentTiming.ArrivalDeadline(appointment))>0.01)
                throw new InvalidOperationException("Independent FullSerializer restore lost appointment fields or status.");
            if(!playerNoShow)
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_BOOKING_PATIENCE_RESTORE_PASS: appointment="+appointment.Id+
                    "; patienceSeconds="+appointment.PatienceSeconds+"; restoredPatienceSeconds="+
                    restoredBooking.PatienceSeconds+"; restoredDeadline="+AppointmentTiming.ArrivalDeadline(restoredBooking)+
                    "; exactPersistence=true; playerSaveWrite=none");

            // Activate the real runtime's day-keyed schedule while resident remains home.
            // All clock advancement ends before calculated departure; route walking is not accelerated.
            var clock = DaggerfallUnity.Instance.WorldTime.Now;
            double speed = ResidentMovement.WalkSpeedFor(selected.AppearanceSeed);
            double estimatedWalkSeconds = bestMetres / speed * 2;
            double preDeparture = startsAt - estimatedWalkSeconds - 5;
            if (preDeparture <= now) throw new InvalidOperationException("Fixture appointment departure is not in the future.");
            clock.RaiseTime((float)(preDeparture - Now()));
            if(Math.Abs(Now()-preDeparture)>1)
                throw new InvalidOperationException("Appointment probe lost fractional clock precision before departure.");
            if (Math.Abs(DaggerfallUnity.Instance.WorldTime.TimeScale - 2f) > 0.001f)
                throw new InvalidOperationException("Native appointment time advance changed normal 2x clock.");

            double departure = startsAt - 1;
            while (departure > startsAt - 1800 && streets.GetState(selected.Id, departure - 1).Activity == Activity.Travel)
                departure -= 1;
            var travelStart = streets.GetState(selected.Id, departure);
            var arriving = streets.GetState(selected.Id, startsAt);
            var afterArrival = streets.GetState(selected.Id, startsAt + 1);
            if (travelStart.Activity != Activity.Travel || arriving.Activity != Activity.Visit ||
                arriving.BuildingKey != venueKey || afterArrival.Activity != Activity.Visit || afterArrival.BuildingKey != venueKey)
                throw new InvalidOperationException("Native ActiveSchedule did not project route travel and exact appointment arrival: " +
                    "departure=" + travelStart.Activity + "; arrival=" + arriving.Activity + ":" + arriving.BuildingKey +
                    "; after=" + afterArrival.Activity + ":" + afterArrival.BuildingKey + "; routeMetres=" + bestMetres);

            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_SCHEDULE_PASS: resident=" + selected.Id + "; venue=" + venueKey +
                "; routeMetres=" + bestMetres + "; scheduledDeparture=" + departure + "; arrival=" + startsAt +
                "; nativeStates=travel/visit/visit.");
            yield return ObservePhysicalAppointment(host, streets, geometry, selected, venueKey,
                appointment, store, hadBaselineDisposition, baselineDisposition,playerNoShow);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_SERIALIZER_PASS: independentRestore=pass; oldSnapshotAppointments=" +
                beforeRoundTrip.Appointments.Count + "; restoredStatus=" + restoredBooking.Status + "; playerSaveWrite=none.");
            Debug.Log(playerNoShow?"PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_NO_SHOW_RUNTIME_PASS: native booking, 2x physical walking, same-body doorway arrival, " +
                "verified NPC waiting, player absence penalty and durable memory passed; player save write=none.":
                "PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_RUNTIME_PASS: native booking, 2x physical walking, same-body doorway arrival, " +
                "interior attendance and neutral memory outcome passed; player save write=none.");
        }

        private static IEnumerator PreflightNoShowVenue(GameManager game,StreetPopulation streets,int venueKey,
            NoShowVenuePreflightResult result)
        {
            var geometry=(TownGeometry)typeof(StreetPopulation).GetField("geometry",Private).GetValue(streets);
            DaggerfallStaticDoors owner; StaticDoor door;
            if(geometry==null || !FindEntrance(geometry,venueKey,out owner,out door)) yield break;

            DaggerfallUI.Instance.PopToHUD();
            game.PauseGame(false);
            TownSmokeHarness.EnterInterior(owner.transform,door);
            float enterDeadline=Time.realtimeSinceStartup+45;
            DaggerfallInterior room=null;
            InteriorGeometry indoor=null;
            while(Time.realtimeSinceStartup<enterDeadline)
            {
                if(game.PlayerEnterExit.IsPlayerInside && game.PlayerEnterExit.BuildingDiscoveryData.buildingKey==venueKey &&
                    streets.Ready && InteriorBuildingCleared(streets))
                {
                    room=game.PlayerEnterExit.Interior;
                    indoor=typeof(StreetPopulation).GetField("indoorGeometry",Private).GetValue(streets) as InteriorGeometry;
                    if(room && indoor!=null && indoor.IsComplete) break;
                }
                yield return null;
            }
            if(room && indoor!=null && indoor.IsComplete && indoor.EntryGround.HasValue)
            {
                Vector3 entryLocal=room.transform.InverseTransformPoint(indoor.EntryGround.Value);
                var surveyed=BoundedApproachPoints(indoor,entryLocal,200f,100f,NoShowApproachPointLimit);
                result.ApproachPointCount=surveyed.Count;
                float diameter=0;
                Vector3 first=Vector3.zero,second=Vector3.zero;
                for(int a=0;a<surveyed.Count;a++)
                for(int b=a+1;b<surveyed.Count;b++)
                {
                    float span=Vector3.Distance(surveyed[a],surveyed[b]);
                    if(span>diameter){diameter=span;first=surveyed[a];second=surveyed[b];}
                }
                var routeA=indoor.RouteBetween(entryLocal,first);
                var routeB=indoor.RouteBetween(entryLocal,second);
                result.ApproachDiameter=diameter;
                result.Passed=surveyed.Count>=2 && diameter>=NoShowRequiredApproachDiameter &&
                    routeA!=null && routeA.Count>=2 && routeB!=null && routeB.Count>=2;
            }
            if(game.PlayerEnterExit.IsPlayerInside) game.PlayerEnterExit.TransitionExterior(false);
            DaggerfallUI.Instance.PopToHUD();
            float exitDeadline=Time.realtimeSinceStartup+45;
            while((game.PlayerEnterExit.IsPlayerInside || !streets.Ready || !ExteriorReady(streets)) && Time.realtimeSinceStartup<exitDeadline)
                yield return null;
            if(game.PlayerEnterExit.IsPlayerInside || !streets.Ready || !ExteriorReady(streets))
                throw new InvalidOperationException("No-show venue preflight failed to return through the native exterior transition: venue="+venueKey);
        }

        private static bool ForecastNoShowRecovery(WorldState world,StreetPopulation streets,ResidentRoutine routine,
            Resident person,int settlementId,int venueKey,double startsAt,out string reason)
        {
            reason="Missing routine or resident.";
            if(world==null || streets==null || routine==null || person==null || routine.Itinerary==null ||
                world.Appointments!=null && world.Appointments.Exists(item=>item.ResidentId==person.Id)) return false;
            var store=(WorldStateStore)typeof(StreetPopulation).GetField("worldStore",Private).GetValue(streets);
            if(store==null){reason="world store unavailable";return false;}
            // Use the minimum booked patience plus three minutes for measured indoor
            // exit. The source social stay is still active; production planning must
            // retain a later original stop and a home leg after that departure.
            double forecastNow=startsAt+AppointmentTiming.MinimumPatienceSeconds+180d;
            int day=(int)Math.Floor(startsAt/86400d);
            int wake=(int)typeof(StreetPopulation).GetField("wakeHour",Private).GetValue(streets)*60;
            int sleep=(int)typeof(StreetPopulation).GetField("sleepHour",Private).GetValue(streets)*60;
            bool restricted=settlementId==1291010263 && store.RestrictHauntingRoutines(settlementId,
                GameManager.Instance.PlayerEntity.GlobalVars.GetGlobalVar(0),forecastNow);
            if(restricted){wake=Math.Max(wake,300);sleep=Math.Min(sleep,1140);}
            try
            {
                var itinerary=(ResidentDestination)typeof(StreetPopulation).GetMethod("OffsetStreetItinerary",Private)
                    .Invoke(streets,new object[]{routine,person.AppearanceSeed});
                var ordinary=DailySchedule.ItineraryDay(person.HomeKey,itinerary,routine.ItineraryDepartureSeconds,wake,sleep);
                var bookings=world.Appointments==null?new List<AppointmentRecord>():
                    world.Appointments.FindAll(item=>item.ResidentId==person.Id);
                RoutineRecoveryPlan forecast;RoutineRecoveryFailure failure;
                bool feasible=RoutineRecoveryPlanner.TryCreate(settlementId,person,routine,ordinary,bookings,day,
                    forecastNow,venueKey,forecastNow,wake,sleep,
                    DaggerfallUnity.Instance.WorldTime.TimeScale/ResidentMovement.WalkSpeedFor(person.AppearanceSeed),
                    out forecast,out reason,out failure,true);
                if(!feasible)return false;
                var later=forecast.Legs.Find(leg=>!leg.ReturnsHome && leg.OriginalStopIndex>=0 && leg.EnterAt>=forecastNow);
                var home=forecast.Legs.Count==0?null:forecast.Legs[forecast.Legs.Count-1];
                if(later==null || home==null || !home.ReturnsHome || home.BuildingKey!=person.HomeKey)
                {reason="planner retained no later optional stop and home";return false;}
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_NO_SHOW_RECOVERY_PREFLIGHT_PASS: resident="+person.Id+
                    "; venue="+venueKey+"; startsAt="+startsAt.ToString("G15")+"; forecastDepart="+
                    forecastNow.ToString("G15")+"; laterStop="+later.BuildingKey+"; laterEnter="+
                    later.EnterAt.ToString("G15")+"; home="+person.HomeKey+"; planner=true");
                return true;
            }
            catch(Exception error){reason="planner forecast threw "+error.GetType().Name+": "+error.Message;return false;}
        }

        private static IEnumerator ObservePhysicalAppointment(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            TownGeometry geometry, Resident person, int venueKey,
            AppointmentRecord appointment, WorldStateStore store, bool hadBaselineDisposition, int baselineDisposition,bool playerNoShow)
        {
            var game = GameManager.Instance;
            var homeEntrance=geometry.Entrances[person.HomeKey];
            Vector3 homeDoor = geometry.ScenePoint(new RoutePoint(homeEntrance.LocalDoor.x,homeEntrance.LocalDoor.z));
            game.PlayerMotor.transform.position = homeDoor + Vector3.up + Vector3.right * 3;
            Physics.SyncTransforms();

            // SceneDoor is the native door's centre, about 1.2m above the floor.
            // Compare body feet with the ground projection, as daily-walk probes do.
            var venueEntrance=geometry.Entrances[venueKey];
            Vector3 venueDoor = geometry.ScenePoint(new RoutePoint(venueEntrance.LocalDoor.x,venueEntrance.LocalDoor.z));
            ResidentActor tracked = null;
            Vector3 previous = Vector3.zero, last = Vector3.zero;
            bool seen = false, vanishedAtDoor = false;
            float maxStep = 0, travelled = 0;
            float deadline = Time.realtimeSinceStartup + 180;
            float firstReal = 0;
            double firstGame = 0;
            while (Time.realtimeSinceStartup < deadline)
            {
                DaggerfallUI.Instance.PopToHUD();
                game.PauseGame(false);
                var actor = streets.FindActor(person.Id);
                if (actor && actor.gameObject.activeInHierarchy && streets.GetState(person.Id, Now()).Activity == Activity.Travel)
                {
                    if (seen && tracked != actor)
                        throw new InvalidOperationException("Appointment walk replaced observed outdoor body before venue door.");
                    Vector3 ground = actor.transform.position - Vector3.up;
                    if (seen)
                    {
                        float step = Vector3.Distance(previous, ground);
                        maxStep = Mathf.Max(maxStep, step);
                        travelled += step;
                        if (step > 1.5f)
                            throw new InvalidOperationException("Appointment walker teleported " + step + "m.");
                    }
                    else
                    {
                        seen = true;
                        tracked = actor;
                        firstReal = Time.realtimeSinceStartup;
                        firstGame = Now();
                    }
                    previous = last = ground;
                    FollowOutdoorCamera(actor, ground);
                    yield return null;
                    continue;
                }
                if (seen)
                {
                    var state = streets.GetState(person.Id, Now());
                    float doorGap = Vector3.Distance(last, venueDoor);
                    if (state.Activity == Activity.Visit && state.BuildingKey == venueKey && doorGap <= 0.8f)
                    {
                        vanishedAtDoor = true;
                        break;
                    }
                    if (!actor || !actor.gameObject.activeInHierarchy)
                        throw new InvalidOperationException("Appointment body vanished away from target doorway: gap=" + doorGap +
                            "; state=" + state.Activity + ":" + state.BuildingKey);
                }
                if (!seen && Now() > appointment.StartsAt + 2)
                {
                    var missingState=streets.GetState(person.Id,Now());
                    var actors=typeof(StreetPopulation).GetField("actors",Private).GetValue(streets) as System.Collections.IDictionary;
                    Vector3 projected=geometry.ScenePoint(missingState.Position);
                    throw new InvalidOperationException("Appointment resident never materialized for scheduled outdoor walk: ready="+streets.Ready+
                        "; enabled="+streets.enabled+"; playing="+game.IsPlayingGame()+"; loading="+game.SaveLoadManager.LoadInProgress+
                        "; state="+missingState.Activity+":"+missingState.BuildingKey+"; alive="+person.IsAlive+
                        "; away="+store.IsResidentAway(person.Id,Now())+"; actors="+(actors==null ? -1 : actors.Count)+
                        "; player="+game.PlayerMotor.transform.position+"; homeDoor="+homeDoor+"; projected="+projected+
                        "; distance="+Vector3.Distance(projected,game.PlayerMotor.transform.position)+"; now="+Now());
                }
                yield return null;
            }

            double walkGameSeconds = Now() - firstGame;
            float walkRealSeconds = Time.realtimeSinceStartup - firstReal;
            var arrived = streets.GetState(person.Id, Now());
            if (!vanishedAtDoor || travelled < 0.5f || maxStep > 1.5f || arrived.Activity != Activity.Visit ||
                arrived.BuildingKey != venueKey || Math.Abs(walkGameSeconds - walkRealSeconds * 2) > 3)
                throw new InvalidOperationException("Appointment physical arrival failed: body=" + vanishedAtDoor +
                    "; moved=" + travelled + "; maxStep=" + maxStep + "; gameSeconds=" + walkGameSeconds +
                    "; realSeconds=" + walkRealSeconds + "; state=" + arrived.Activity + ":" + arrived.BuildingKey +
                    "; doorGap=" + Vector3.Distance(last, venueDoor));
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_WALK_PASS: resident=" + person.Id + "; venue=" + venueKey +
                "; moved=" + travelled + "; maxStep=" + maxStep + "; gameSeconds=" + walkGameSeconds +
                "; realtimeSeconds=" + walkRealSeconds + "; bodyStableUntilDoor=true; doorGap=" + Vector3.Distance(last, venueDoor));

            DaggerfallStaticDoors owner;
            StaticDoor door;
            if (!FindEntrance(geometry, venueKey, out owner, out door))
                throw new InvalidOperationException("Appointment venue has no surveyed native entrance: " + venueKey);
            Type rosyHooks=Type.GetType("PeopleOfTheIliacBay.QuestHooks.RosyInteriorCompatibilityHooks, PeopleOfTheIliacBay.QuestHooks",false);
            bool rosyInstalled=rosyHooks!=null && (bool)rosyHooks.GetProperty("Installed").GetValue(null,null);
            int rosyCorrectionsBefore=rosyInstalled?(int)rosyHooks.GetProperty("Completed").GetValue(null,null):0;
            TownSmokeHarness.EnterInterior(owner.transform, door);
            float interiorDeadline = Time.realtimeSinceStartup + 45;
            while ((!game.PlayerEnterExit.IsPlayerInside || game.PlayerEnterExit.BuildingDiscoveryData.buildingKey != venueKey ||
                    !streets.Ready || !InteriorBuildingCleared(streets) || !streets.FindActor(person.Id)) &&
                    Time.realtimeSinceStartup < interiorDeadline)
                yield return null;
            if (!game.PlayerEnterExit.IsPlayerInside || game.PlayerEnterExit.BuildingDiscoveryData.buildingKey != venueKey ||
                !streets.Ready || !InteriorBuildingCleared(streets) || !streets.FindActor(person.Id))
                throw new InvalidOperationException("Appointment actor did not render after native venue entry.");

            if(playerNoShow)
            {
                yield return ObservePhysicalPlayerNoShow(host,streets,person,venueKey,appointment,store,rosyHooks,rosyInstalled,rosyCorrectionsBefore);
                yield break;
            }

            float attendDeadline = Time.realtimeSinceStartup + 20;
            while (Time.realtimeSinceStartup < attendDeadline)
            {
                DaggerfallUI.Instance.PopToHUD();
                game.PauseGame(false);
                var indoor = streets.FindActor(person.Id);
                if (indoor && indoor.gameObject.activeInHierarchy)
                {
                    var state = streets.GetState(person.Id, Now());
                    if (state.Activity != Activity.Visit || state.BuildingKey != venueKey)
                        throw new InvalidOperationException("Rendered appointment actor disagrees with scheduled venue: " +
                            state.Activity + ":" + state.BuildingKey);
                    Vector3 forward = indoor.transform.forward;
                    forward.y = 0;
                    if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
                    game.PlayerMotor.transform.position = indoor.transform.position - forward.normalized * 2.5f;
                    SleepProbe.AimCamera(indoor.transform.position);
                    Physics.SyncTransforms();
                    if (Vector3.Distance(game.PlayerMotor.transform.position, indoor.transform.position) >= 4)
                        throw new InvalidOperationException("Appointment observer did not reach actor within 4m.");
                }
                var outcome = store.FindAppointment(appointment.Id);
                if (outcome != null && outcome.Status == "attended" && outcome.OutcomeRecorded) break;
                yield return null;
            }
            var attended = store.FindAppointment(appointment.Id);
            if (attended == null || attended.Status != "attended" || !attended.OutcomeRecorded || attended.NoShowPenalty != 0)
                throw new InvalidOperationException("Production appointment observer did not record neutral attendance: " +
                    (attended == null ? "missing" : attended.Status));
            int currentDisposition;
            bool hasDisposition = store.TryGetActualConversationDisposition(person.Id, out currentDisposition);
            if (hasDisposition != hadBaselineDisposition || (hasDisposition && currentDisposition != baselineDisposition))
                throw new InvalidOperationException("Attended appointment changed resident disposition.");
            var snapshot = (WorldState)host.GetSaveData();
            // Save checkpoints intentionally empty the inline journal. Verify the
            // committed shared archive, not the old duplicated-save representation.
            var archive=new SharedConversationArchive(PeopleOfTheIliacBayMod.ConversationArchiveRoot());
            var memory = archive.Load(snapshot.ConversationArchive).Find(item => item.PersonId == person.Id);
            bool neutralObservation = memory != null && memory.Entries.Exists(item => item.Source == "observed" &&
                string.IsNullOrEmpty(item.PlayerText) && string.IsNullOrEmpty(item.NpcText) &&
                item.Observation != null && item.Observation.Contains(appointment.Purpose + " appointment together"));
            if (!neutralObservation)
                throw new InvalidOperationException("Attended appointment did not create expected neutral NPC memory observation.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_ATTENDANCE_PASS: resident=" + person.Id + "; venue=" + venueKey +
                "; status=" + attended.Status + "; noShowPenalty=" + attended.NoShowPenalty +
                "; neutralObservation=true; dispositionUnchanged=true; observerDistance<4m.");
        }

        private static IEnumerator ObservePhysicalPlayerNoShow(PeopleOfTheIliacBayMod host,StreetPopulation streets,Resident person,
            int venueKey,AppointmentRecord appointment,WorldStateStore store,Type rosyHooks,bool rosyInstalled,int rosyCorrectionsBefore)
        {
            // Runtime only simulates the current interior. Therefore this real NPC-wait
            // case keeps the player in that venue but outside the executor's 8m meeting radius.
            var game=GameManager.Instance;var room=game.PlayerEnterExit.Interior;
            var indoor=(InteriorGeometry)typeof(StreetPopulation).GetField("indoorGeometry",Private).GetValue(streets);
            var actor=streets.FindActor(person.Id);
            if(!room || indoor==null || !actor || !actor.gameObject.activeInHierarchy || actor.IsSleeping ||
                streets.GetState(person.Id,Now()).Activity!=Activity.Visit || streets.GetState(person.Id,Now()).BuildingKey!=venueKey)
                throw new InvalidOperationException("No-show fixture lacks a real awake resident at the planned venue.");
            var originalBody=actor;

            // Provisional placement prevents the native doorway spawn from being
            // mistaken for player attendance while Rosy's real correction settles.
            float provisionalGap=StageNoShowObserver(game,room,indoor,actor,venueKey,person.Id);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_NO_SHOW_PLAYER_STAGE_PROVISIONAL: resident="+person.Id+
                "; gap="+provisionalGap.ToString("F2")+"; rosyInstalled="+rosyInstalled+
                "; correctionsBefore="+rosyCorrectionsBefore+"; player="+game.PlayerMotor.transform.position);
            if(rosyInstalled)
            {
                float correctionDeadline=Time.realtimeSinceStartup+15f;
                int completed=rosyCorrectionsBefore;
                while(completed<=rosyCorrectionsBefore && Time.realtimeSinceStartup<correctionDeadline)
                {
                    DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
                    var booking=store.FindAppointment(appointment.Id);
                    if(booking==null || booking.PlayerArrivedAt>=0 || booking.OutcomeRecorded)
                        throw new InvalidOperationException("Player attendance/outcome was recorded before the native interior correction settled: "+
                            (booking==null?"missing":booking.Status+"; playerArrivedAt="+booking.PlayerArrivedAt+"; outcome="+booking.OutcomeRecorded));
                    actor=streets.FindActor(person.Id);
                    if(!actor || !actor.gameObject.activeInHierarchy || actor.IsSleeping ||
                        streets.GetState(person.Id,Now()).Activity!=Activity.Visit || streets.GetState(person.Id,Now()).BuildingKey!=venueKey)
                        throw new InvalidOperationException("No-show resident left the venue while awaiting the native correction settle marker.");
                    completed=(int)rosyHooks.GetProperty("Completed").GetValue(null,null);
                    if(completed>rosyCorrectionsBefore)break;
                    yield return null;
                }
                if(completed<=rosyCorrectionsBefore)
                    throw new InvalidOperationException("Rosy's native interior correction did not complete before no-show observer placement.");
                var settledBooking=store.FindAppointment(appointment.Id);
                if(settledBooking==null || settledBooking.PlayerArrivedAt>=0 || settledBooking.OutcomeRecorded)
                    throw new InvalidOperationException("Player attendance/outcome was recorded before post-correction observer placement: "+
                        (settledBooking==null?"missing":settledBooking.Status+"; playerArrivedAt="+settledBooking.PlayerArrivedAt+"; outcome="+settledBooking.OutcomeRecorded));
                actor=streets.FindActor(person.Id);
                if(!actor || !actor.gameObject.activeInHierarchy || actor.IsSleeping)
                    throw new InvalidOperationException("No-show resident disappeared after native interior correction.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_NO_SHOW_INTERIOR_SETTLED: resident="+person.Id+
                    "; correctionBefore="+rosyCorrectionsBefore+"; correctionAfter="+completed+
                    "; player="+game.PlayerMotor.transform.position+"; gapBeforeFinalStage="+
                    Vector3.Distance(game.PlayerMotor.transform.position,actor.transform.position).ToString("F2")+"; playerArrivedAt=-1.");
            }
            float finalGap=rosyInstalled?StageNoShowObserver(game,room,indoor,actor,venueKey,person.Id):provisionalGap;
            if(rosyInstalled)Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_NO_SHOW_PLAYER_STAGE_FINAL: resident="+person.Id+
                "; gap="+finalGap.ToString("F2")+"; correctionsSettled=true; playerArrivedAt=-1");
            float initialGap=Vector3.Distance(game.PlayerMotor.transform.position,actor.transform.position);
            if(initialGap<NoShowObserverDestinationGap)
                throw new InvalidOperationException("No-show observer staging lost its 14m safety margin: gap="+initialGap.ToString("F2"));

            int dispositionBefore=store.ConversationDisposition(person.Id);
            var before= (WorldState)host.GetSaveData();
            var archive=new SharedConversationArchive(PeopleOfTheIliacBayMod.ConversationArchiveRoot());
            var prior=archive.Load(before.ConversationArchive).Find(value=>value.PersonId==person.Id);
            int priorCount=prior==null?0:prior.Entries.Count(value=>value.Source=="observed" && value.Observation!=null &&
                value.Observation.Contains("missed our "+appointment.Purpose));
            double arrival=AppointmentTiming.ArrivalDeadline(appointment);
            float deadline=Time.realtimeSinceStartup+(float)Math.Max(120,(arrival-Now())/
                DaggerfallUnity.Instance.WorldTime.TimeScale+90);
            float waitStarted=Time.realtimeSinceStartup;
            double lastProgressGame=Now();
            Vector3 previousActorPosition=actor.transform.position;
            float actorTravelled=0,minGap=initialGap;
            int relocations=0;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_NO_SHOW_WAIT_START: resident="+person.Id+"; venue="+venueKey+
                "; initialGap="+initialGap.ToString("F2")+"; requiredOutside=8; trigger="+
                NoShowObserverTriggerGap.ToString("F1")+"; deadline="+arrival.ToString("F0")+"; patienceSeconds="+
                appointment.PatienceSeconds+"; meetingDurationSeconds="+(appointment.EndsAt-appointment.StartsAt));
            while(Time.realtimeSinceStartup<deadline)
            {
                DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
                actor=streets.FindActor(person.Id);
                if(actor && actor.gameObject.activeInHierarchy)
                {
                    Vector3 actorPosition=actor.transform.position;
                    actorTravelled+=Vector3.Distance(previousActorPosition,actorPosition);
                    previousActorPosition=actorPosition;
                    float gap=Vector3.Distance(game.PlayerMotor.transform.position,actorPosition);
                    minGap=Mathf.Min(minGap,gap);
                    if(gap<=8f)
                        throw new InvalidOperationException("Observer crossed the appointment's 8m presence radius before no-show resolution; gap="+
                            gap.ToString("F2")+"; minGap="+minGap.ToString("F2")+"; actorTravelled="+actorTravelled.ToString("F2")+
                            "; relocations="+relocations+". This run cannot count as a player no-show.");
                    if(gap<=NoShowObserverTriggerGap)
                    {
                        if(relocations>=MaxNoShowObserverRelocations)
                            throw new InvalidOperationException("No-show observer exhausted bounded safe relocations; minGap="+minGap.ToString("F2")+
                                "; actorTravelled="+actorTravelled.ToString("F2")+"; relocations="+relocations);
                        List<GuardPoint3> safeRoute;float routeGap,endpointGap;
                        if(!TryBuildSafeObserverRoute(indoor,room,game.PlayerMotor.transform.position,actorPosition,
                            out safeRoute,out routeGap,out endpointGap))
                            throw new InvalidOperationException("No collision-checked indoor route preserves the 8m absence radius; currentGap="+
                                gap.ToString("F2")+"; minGap="+minGap.ToString("F2")+"; maxSafePathGap="+routeGap.ToString("F2")+
                                "; maxEndpointGap="+endpointGap.ToString("F2")+"; actorTravelled="+actorTravelled.ToString("F2")+
                                "; venue="+venueKey+"; resident="+person.Id);
                        relocations++;
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_NO_SHOW_RELOCATE: resident="+person.Id+"; venue="+venueKey+
                            "; currentGap="+gap.ToString("F2")+"; routeGap="+routeGap.ToString("F2")+
                            "; endpointGap="+endpointGap.ToString("F2")+"; relocations="+relocations);
                        var controller=game.PlayerController;
                        if(!controller)
                            throw new InvalidOperationException("No-show observer cannot move through the native collision-checked player controller.");
                        bool controllerWasEnabled=controller.enabled;
                        controller.enabled=true;
                        bool replan=false;
                        float relocationDeadline=Time.realtimeSinceStartup+20f;
                        try
                        {
                            for(int routeIndex=1;routeIndex<safeRoute.Count && !replan;routeIndex++)
                            {
                                GuardPoint3 routePoint=safeRoute[routeIndex];
                                Vector3 target=room.transform.TransformPoint(new Vector3((float)routePoint.X,(float)routePoint.Y,(float)routePoint.Z))+Vector3.up;
                                float lastMovedAt=Time.realtimeSinceStartup;
                                while(Vector3.Distance(game.PlayerMotor.transform.position,target)>0.15f)
                                {
                                    if(Time.realtimeSinceStartup>=relocationDeadline)
                                        throw new InvalidOperationException("Native player collision-checked observer relocation exceeded its 20s bound.");
                                    actor=streets.FindActor(person.Id);
                                    if(!actor || !actor.gameObject.activeInHierarchy)
                                        throw new InvalidOperationException("NPC left the venue during observer relocation; no no-show is inferred.");
                                    actorPosition=actor.transform.position;
                                    actorTravelled+=Vector3.Distance(previousActorPosition,actorPosition);
                                    previousActorPosition=actorPosition;
                                    gap=Vector3.Distance(game.PlayerMotor.transform.position,actorPosition);
                                    minGap=Mathf.Min(minGap,gap);
                                    if(gap<=8f)
                                        throw new InvalidOperationException("Observer crossed the appointment's 8m presence radius while repositioning; gap="+
                                            gap.ToString("F2")+"; minGap="+minGap.ToString("F2")+"; relocations="+relocations);
                                    Vector3 step=Vector3.MoveTowards(game.PlayerMotor.transform.position,target,3f*Time.unscaledDeltaTime);
                                    if(step.sqrMagnitude<0.000001f){yield return null;continue;}
                                    if(DistanceToSegment(actorPosition,game.PlayerMotor.transform.position,step)<NoShowObserverRouteGap)
                                    {replan=true;break;}
                                    Vector3 beforeMove=game.PlayerMotor.transform.position;
                                    controller.Move(step-beforeMove);
                                    if(Vector3.Distance(beforeMove,game.PlayerMotor.transform.position)>0.001f)
                                        lastMovedAt=Time.realtimeSinceStartup;
                                    else if(Time.realtimeSinceStartup-lastMovedAt>1.5f)
                                        throw new InvalidOperationException("Native player collision controller blocked the safe observer route.");
                                    if(Time.realtimeSinceStartup>=relocationDeadline)
                                        throw new InvalidOperationException("Native player collision-checked observer relocation exceeded its 20s bound.");
                                    yield return null;
                                }
                            }
                        }
                        finally {controller.enabled=controllerWasEnabled;}
                        if(!replan)
                        {
                            float relocatedGap=Vector3.Distance(game.PlayerMotor.transform.position,actor.transform.position);
                            if(relocatedGap<NoShowObserverDestinationGap)
                                replan=true;
                            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_NO_SHOW_RELOCATED: resident="+person.Id+"; venue="+venueKey+
                                "; gap="+relocatedGap.ToString("F2")+"; minGap="+minGap.ToString("F2")+
                                "; actorTravelled="+actorTravelled.ToString("F2")+"; relocations="+relocations);
                        }
                        continue;
                    }
                }
                var state=streets.GetState(person.Id,Now());
                var booking=store.FindAppointment(appointment.Id);
                if(booking!=null && !booking.OutcomeRecorded && Now()<=arrival &&
                    (!actor || !actor.gameObject.activeInHierarchy || actor.IsSleeping || state.Activity!=Activity.Visit || state.BuildingKey!=venueKey))
                    throw new InvalidOperationException("NPC stopped physically attending before the appointment grace ended.");
                if(booking!=null && booking.OutcomeRecorded)break;
                double currentGame=Now();
                if(currentGame-lastProgressGame>=120)
                {
                    float observedGap=actor && actor.gameObject.activeInHierarchy?
                        Vector3.Distance(game.PlayerMotor.transform.position,actor.transform.position):-1f;
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_NO_SHOW_WAIT_PROGRESS: resident="+person.Id+"; gameElapsed="+
                        (currentGame-appointment.StartsAt).ToString("F0")+"; gameRemaining="+Math.Max(0,arrival-currentGame).ToString("F0")+
                        "; realtimeElapsed="+(Time.realtimeSinceStartup-waitStarted).ToString("F1")+
                        "; npcWaited="+(booking==null?0:booking.NpcWaitedSeconds).ToString("F0")+"; playerGap="+observedGap.ToString("F2"));
                    lastProgressGame=currentGame;
                }
                yield return null;
            }

            var outcome=store.FindAppointment(appointment.Id);
            if(outcome==null || outcome.Status!="player-absent" || !outcome.OutcomeRecorded || outcome.NoShowPenalty<=0 ||
                outcome.PlayerArrivedAt>=0 || outcome.NpcArrivedAt<appointment.StartsAt ||
                outcome.NpcLastSeenAt<arrival-NoShowObservationPollMarginSeconds || outcome.NpcLastSeenAt>arrival ||
                outcome.ResolvedAt<=arrival || outcome.ResolvedAt-outcome.NpcLastSeenAt>NoShowObservationPollMarginSeconds)
                throw new InvalidOperationException("Native no-show was not backed by an actually present NPC's required wait: "+
                    (outcome==null?"missing":outcome.Status+"; waited="+outcome.NpcWaitedSeconds+"; last="+outcome.NpcLastSeenAt+"; resolved="+outcome.ResolvedAt));
            double expectedWaitFromArrival=arrival-outcome.NpcArrivedAt;
            double waitShortfall=expectedWaitFromArrival-outcome.NpcWaitedSeconds;
            double actualNpcWaitWindow=arrival-outcome.NpcArrivedAt;
            if(outcome.NpcWaitedSeconds<30 || actualNpcWaitWindow<AppointmentTiming.MinimumPatienceSeconds-NoShowObservationPollMarginSeconds ||
                waitShortfall>NoShowObservationPollMarginSeconds || waitShortfall < -NoShowObservationPollMarginSeconds)
                throw new InvalidOperationException("No-show did not preserve nearly the full captured arrival-patience window as continuous NPC wait evidence: "+
                    "patienceSeconds="+appointment.PatienceSeconds+"; arrivalDelay="+(outcome.NpcArrivedAt-appointment.StartsAt).ToString("F2")+
                    "; actualNpcWaitWindow="+actualNpcWaitWindow.ToString("F2")+"; expectedWaitFromNpcArrival="+
                    expectedWaitFromArrival.ToString("F2")+"; measuredWait="+outcome.NpcWaitedSeconds.ToString("F2")+
                    "; shortfall="+waitShortfall.ToString("F2")+"; pollMargin="+NoShowObservationPollMarginSeconds);
            int expected=Math.Max(-200,dispositionBefore-outcome.NoShowPenalty);
            if(store.ConversationDisposition(person.Id)!=expected)
                throw new InvalidOperationException("Player no-show did not apply the exact disposition penalty once.");

            var indoorExit=new IndoorExitEvidence();
            yield return ObserveMeasuredAppointmentIndoorExit(streets,store,originalBody,room,person.Id,appointment.Id,indoorExit);

            var saved=(WorldState)host.GetSaveData();
            var departure=saved.VisitDepartures==null?null:saved.VisitDepartures.Find(item=>item.ResidentId==person.Id &&
                item.AppointmentId==appointment.Id);
            var recovery=saved.RoutineRecoveries==null?null:saved.RoutineRecoveries.Find(item=>item.ResidentId==person.Id &&
                item.AbsoluteDay==(int)Math.Floor(appointment.StartsAt/86400d));
            if(departure==null || departure.Motion==null || recovery==null || departure.HomeKey!=person.HomeKey ||
                departure.Motion.BuildingKey!=venueKey || Math.Abs(departure.Motion.StartedAt-outcome.ResolvedAt)>0.01 ||
                departure.ResumeRoutineAt!=departure.Motion.ExitAt || recovery.ResidentId!=person.Id ||
                recovery.OriginBuildingKey!=venueKey || !recovery.OriginOutside ||
                Math.Abs(recovery.CreatedAt-departure.Motion.StartedAt)>0.01 ||
                Math.Abs(recovery.DepartAt-departure.ResumeRoutineAt)>0.01 || recovery.Legs==null || recovery.Legs.Count<2)
                throw new InvalidOperationException("Observed no-show did not create a matching measured venue departure and source-door recovery plan: "+
                    "departure="+(departure!=null)+"; appointment="+(departure==null?"missing":departure.AppointmentId)+
                    "; source="+(departure==null||departure.Motion==null?-1:departure.Motion.BuildingKey)+
                    "; resumedAt="+(departure==null?0:departure.ResumeRoutineAt)+"; recovery="+(recovery!=null)+
                    "; origin="+(recovery==null?-1:recovery.OriginBuildingKey)+"; outside="+(recovery!=null&&recovery.OriginOutside));
            var laterOptional=recovery.Legs.Find(leg=>!leg.ReturnsHome && leg.OriginalStopIndex>=0 &&
                leg.EnterAt>=recovery.DepartAt);
            var homeLeg=recovery.Legs[recovery.Legs.Count-1];
            if(laterOptional==null || !homeLeg.ReturnsHome || homeLeg.BuildingKey!=person.HomeKey ||
                homeLeg.Route==null || homeLeg.Route.Count<2 || Math.Abs(homeLeg.LeaveAt-recovery.EndsAt)>0.01)
                throw new InvalidOperationException("No-show continuation did not retain a later optional stop and verified home-return leg.");
            var memory=archive.Load(saved.ConversationArchive).Find(value=>value.PersonId==person.Id);
            int afterCount=memory==null?0:memory.Entries.Count(value=>value.Source=="observed" && value.Observation!=null &&
                value.Observation.Contains("missed our "+appointment.Purpose));
            if(afterCount!=priorCount+1)
                throw new InvalidOperationException("Player no-show did not write exactly one durable observation.");
            string encoded=SaveLoadManager.Serialize(typeof(WorldState),saved,false);
            var decoded=(WorldState)SaveLoadManager.Deserialize(typeof(WorldState),encoded);
            var restored=new WorldStateStore(0,PeopleOfTheIliacBayMod.ConversationArchiveRoot());restored.Restore(decoded,Now());
            var disk=restored.FindAppointment(appointment.Id);
            if(disk==null || disk.Status!="player-absent" || disk.NoShowPenalty!=outcome.NoShowPenalty ||
                restored.ConversationDisposition(person.Id)!=expected)
                throw new InvalidOperationException("Native save/restore changed the player no-show consequence.");
            var restoredDeparture=decoded.VisitDepartures.Find(item=>item.ResidentId==person.Id && item.AppointmentId==appointment.Id);
            var savedTown=decoded.Routines.Find(item=>item.SettlementId==appointment.Place.SettlementId);
            var savedRoutine=savedTown==null?null:savedTown.Residents.Find(item=>item.ResidentId==person.Id);
            var restoredRecovery=savedRoutine==null?null:RestoredRecoveryLookup(restored,savedRoutine,recovery);
            if(restoredDeparture==null || Math.Abs(restoredDeparture.ResumeRoutineAt-restoredDeparture.Motion.ExitAt)>0.001 ||
                restoredRecovery==null || restoredRecovery.OriginBuildingKey!=venueKey || !restoredRecovery.OriginOutside ||
                restoredRecovery.Legs.Find(leg=>!leg.ReturnsHome && leg.OriginalStopIndex>=0 &&
                    leg.EnterAt>=restoredRecovery.DepartAt)==null ||
                !restoredRecovery.Legs[restoredRecovery.Legs.Count-1].ReturnsHome ||
                restoredRecovery.Legs[restoredRecovery.Legs.Count-1].BuildingKey!=person.HomeKey)
                throw new InvalidOperationException("Native save/restore lost the appointment departure marker or matching resumed recovery plan.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_RECOVERY_HANDOFF_PASS: resident="+person.Id+"; appointment="+appointment.Id+
                "; sourceVenue="+venueKey+"; departureStarted="+departure.Motion.StartedAt.ToString("G15")+
                "; indoorExit="+departure.Motion.ExitAt.ToString("G15")+"; resumeRoutineAt="+departure.ResumeRoutineAt.ToString("G15")+
                "; originOutside=true; laterOptional="+laterOptional.BuildingKey+"; home="+homeLeg.BuildingKey+
                "; saveRestore=true; sourceDoorPlanValidated=true");
            var restoredMemory=archive.Load(decoded.ConversationArchive).Find(value=>value.PersonId==person.Id);
            int restoredCount=restoredMemory==null?0:restoredMemory.Entries.Count(value=>value.Source=="observed" && value.Observation!=null &&
                value.Observation.Contains("missed our "+appointment.Purpose));
            if(restoredCount!=priorCount+1)throw new InvalidOperationException("Native save/restore duplicated or lost no-show memory.");
            store.ObserveAppointment(appointment.Id,Now()+60,true,true,false);
            if(store.FindAppointment(appointment.Id).NoShowPenalty!=outcome.NoShowPenalty ||
                store.ConversationDisposition(person.Id)!=expected)
                throw new InvalidOperationException("Repeated native observation applied the no-show penalty twice.");
            var repeatedSave=(WorldState)host.GetSaveData();
            var repeatedMemory=archive.Load(repeatedSave.ConversationArchive).Find(value=>value.PersonId==person.Id);
            int repeatedCount=repeatedMemory==null?0:repeatedMemory.Entries.Count(value=>value.Source=="observed" && value.Observation!=null &&
                value.Observation.Contains("missed our "+appointment.Purpose));
            if(repeatedCount!=priorCount+1)throw new InvalidOperationException("Repeated no-show observation duplicated durable memory.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_PLAYER_NO_SHOW_PASS: resident="+person.Id+"; venue="+venueKey+
                "; waited="+outcome.NpcWaitedSeconds.ToString("F0")+"; requiredWait="+
                AppointmentTiming.MinimumPatienceSeconds.ToString("F0")+"; patienceSeconds="+appointment.PatienceSeconds+
                "; meetingDurationSeconds="+(appointment.EndsAt-appointment.StartsAt)+"; penalty="+outcome.NoShowPenalty+
                "; memoryOnce=true; nativeRestore=true; repeatedObservationIdempotent=true; playerOutside8m=true; sameVenue=true"+
                "; initialGap="+initialGap.ToString("F2")+"; minGap="+minGap.ToString("F2")+"; actorTravelled="+actorTravelled.ToString("F2")+
                "; relocations="+relocations+"; waitRealtime="+(Time.realtimeSinceStartup-waitStarted).ToString("F1")+
                "; indoorExitSamples="+indoorExit.Samples+"; indoorExitMaxStep="+indoorExit.MaximumStep.ToString("F3")+
                "; indoorExitEndpointError="+indoorExit.EndpointError.ToString("F3")+"; actorSameUntilExit="+
                indoorExit.BodyRemovedAtExit+"; outdoorMotionAvailable="+indoorExit.OutdoorMotionAvailable+"; outdoorMovementObserved=false");
        }

        private static IEnumerator ObserveMeasuredAppointmentIndoorExit(StreetPopulation streets,WorldStateStore store,
            ResidentActor body,DaggerfallInterior room,string residentId,string appointmentId,IndoorExitEvidence evidence)
        {
            var game=GameManager.Instance;
            float departDeadline=Time.realtimeSinceStartup+15f;
            VisitDeparture departure=null;
            while(Time.realtimeSinceStartup<departDeadline)
            {
                DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
                if(!body || streets.FindActor(residentId)!=body)
                    throw new InvalidOperationException("Appointment return replaced or removed the waiting resident body before measured exit.");
                var current=store.VisitDepartureFor(residentId,Now());
                if(current!=null && current.AppointmentId==appointmentId){departure=current;break;}
                yield return null;
            }
            if(departure==null || departure.Motion==null || departure.Motion.InteriorRoute==null ||
                departure.Motion.InteriorRoute.Count<2 || departure.Motion.BuildingKey<0 || departure.Motion.Speed<=0 ||
                departure.Motion.TimeScale<=0)
                throw new InvalidOperationException("No valid production appointment departure motion appeared for the physical handoff probe.");
            var motion=departure.Motion;
            evidence.StartedAt=motion.StartedAt;
            if(motion.BuildingKey<0 || Now()>=motion.ExitAt)
                throw new InvalidOperationException("Appointment departure reached its source transition before body sampling began.");
            if(!body || streets.FindActor(residentId)!=body || body.transform.parent!=room.transform)
                throw new InvalidOperationException("The appointment departure did not retain the same resident actor in its source interior.");

            Vector3 endpoint=InteriorMotionWorld(room,motion.InteriorRoute[motion.InteriorRoute.Count-1]);
            double previousTime=Now();
            Vector3 previousPosition=body.transform.position;
            var predicted=new AlarmPerson();motion.Advance(predicted,previousTime);
            float initialPathError=Vector3.Distance(previousPosition,InteriorMotionWorld(room,predicted));
            if(initialPathError>0.75f)
                throw new InvalidOperationException("Resident body did not begin on its saved measured indoor appointment-exit path: error="+
                    initialPathError.ToString("F3")+"; resident="+residentId);
            evidence.Samples=1;
            float realtimeDeadline=Time.realtimeSinceStartup+(float)Math.Max(15,Math.Min(90,
                Math.Max(0,motion.ExitAt-previousTime)/motion.TimeScale+15));
            bool reached=false;
            while(Time.realtimeSinceStartup<realtimeDeadline && previousTime<motion.ExitAt)
            {
                DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
                yield return null;
                double currentTime=Now();
                if(currentTime<previousTime)
                    throw new InvalidOperationException("World clock moved backward during measured resident doorway exit.");
                var currentBody=streets.FindActor(residentId);
                if(!currentBody || currentBody!=body)
                {
                    if(currentTime<motion.ExitAt-0.75)
                        throw new InvalidOperationException("The actual resident body disappeared before its measured doorway endpoint.");
                    evidence.BodyRemovedAtExit=true;
                    evidence.ReachedAt=currentTime;
                    reached=true;
                    break;
                }
                float step=Vector3.Distance(previousPosition,currentBody.transform.position);
                float elapsedGame=(float)(currentTime-previousTime);
                float allowedStep=(float)(motion.Speed*Math.Max(0,currentTime-previousTime)/motion.TimeScale)+0.40f;
                evidence.MaximumStep=Mathf.Max(evidence.MaximumStep,step);
                if(step>allowedStep)
                    throw new InvalidOperationException("Resident body exceeded its measured appointment-exit walking step: step="+
                        step.ToString("F3")+"; allowed="+allowedStep.ToString("F3")+"; elapsedGame="+elapsedGame.ToString("F3")+
                        "; maxStep="+evidence.MaximumStep.ToString("F3"));
                evidence.TotalMoved+=step;
                previousPosition=currentBody.transform.position;
                previousTime=currentTime;
                evidence.Samples++;
                if(currentTime<motion.ExitAt)
                {
                    predicted=new AlarmPerson();motion.Advance(predicted,currentTime);
                    float pathError=Vector3.Distance(previousPosition,InteriorMotionWorld(room,predicted));
                    if(pathError>0.75f)
                        throw new InvalidOperationException("Resident body diverged from its saved indoor appointment-exit path: error="+
                            pathError.ToString("F3")+"; resident="+residentId);
                }
                else
                {
                    evidence.EndpointError=Vector3.Distance(previousPosition,endpoint);
                    if(evidence.EndpointError>allowedStep+0.75f)
                        throw new InvalidOperationException("Resident body did not reach the surveyed source doorway within its final walking step: error="+
                            evidence.EndpointError.ToString("F3")+"; allowed="+allowedStep.ToString("F3"));
                    evidence.ReachedAt=currentTime;
                    reached=true;
                    break;
                }
            }
            if(!reached)
                throw new InvalidOperationException("Measured appointment indoor exit exceeded its bounded realtime observation window.");
            if(evidence.BodyRemovedAtExit)
            {
                double remaining=Math.Max(0,motion.ExitAt-previousTime);
                evidence.EndpointError=Vector3.Distance(previousPosition,endpoint);
                double remainingDistance=motion.Speed*remaining/motion.TimeScale;
                if(evidence.EndpointError>remainingDistance+1.15)
                    throw new InvalidOperationException("Resident body was removed too far from the measured source doorway: endpointError="+
                        evidence.EndpointError.ToString("F3")+"; remainingDistance="+remainingDistance.ToString("F3"));
            }
            float exteriorDeadline=Time.realtimeSinceStartup+3f;
            while(Now()<motion.ExitAt && Time.realtimeSinceStartup<exteriorDeadline)
            { DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);yield return null; }
            if(evidence.Samples<2 || evidence.TotalMoved<0.25f)
                throw new InvalidOperationException("Appointment handoff showed no sampled physical body movement: samples="+
                    evidence.Samples+"; moved="+evidence.TotalMoved.ToString("F3"));
            AlarmPerson outside;bool home;
            evidence.OutdoorMotionAvailable=Now()>=motion.ExitAt && store.TryVisitDeparturePosition(residentId,Now(),out outside,out home) &&
                outside!=null && outside.BuildingKey<0;
            if(!evidence.OutdoorMotionAvailable)
                throw new InvalidOperationException("Measured indoor exit did not reach the saved departure's exterior phase.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_INDOOR_EXIT_PHYSICAL_PASS: resident="+residentId+"; appointment="+appointmentId+
                "; bodySamples="+evidence.Samples+"; bodyMoved="+evidence.TotalMoved.ToString("F3")+
                "; maxStep="+evidence.MaximumStep.ToString("F3")+"; endpointError="+evidence.EndpointError.ToString("F3")+
                "; startedAt="+evidence.StartedAt.ToString("G15")+"; reachedAt="+evidence.ReachedAt.ToString("G15")+
                "; sameActorUntilExit=true; measuredSavedIndoorPath=true; exteriorPhaseAvailable=true; outdoorMovementObserved=false");
        }

        private static Vector3 InteriorMotionWorld(DaggerfallInterior room,GuardPoint3 point)
        { return room.transform.TransformPoint(new Vector3((float)point.X,(float)point.Y,(float)point.Z))+Vector3.up; }

        private static Vector3 InteriorMotionWorld(DaggerfallInterior room,AlarmPerson point)
        { return room.transform.TransformPoint(new Vector3((float)point.X,(float)point.Y,(float)point.Z))+Vector3.up; }

        private static float StageNoShowObserver(GameManager game,DaggerfallInterior room,InteriorGeometry indoor,
            ResidentActor actor,int venueKey,string residentId)
        {
            if(!indoor.EntryGround.HasValue)
                throw new InvalidOperationException("Saved appointment venue has no surveyed indoor entry ground for observer routing; venue="+
                    venueKey+"; resident="+residentId+"; anchors="+indoor.LocalAnchors.Count);
            // Rosy's native compatibility correction can leave the player at a
            // valid-looking but unsurveyed pose. RouteBetween then cannot join
            // that pose to the floor graph. Anchor this reachability preflight to
            // the graph's actual surveyed seed instead. Staging is an observer
            // placement and is not counted as player travel.
            Vector3 entryLocal=room.transform.InverseTransformPoint(indoor.EntryGround.Value);
            Vector3 selected=Vector3.zero;float farthest=0,maxFarGap=0;bool found=false;
            int farAnchors=0,reachableFarAnchors=0;
            Vector3 actorLocal=room.transform.InverseTransformPoint(actor.transform.position-Vector3.up);
            var surveyed=BoundedApproachPoints(indoor,actorLocal,200f,100f,NoShowApproachPointLimit);
            foreach(var local in surveyed)
            {
                Vector3 ground=room.transform.TransformPoint(local);
                float gap=Vector3.Distance(ground+Vector3.up,actor.transform.position);
                if(gap<NoShowObserverDestinationGap)continue;
                farAnchors++;maxFarGap=Mathf.Max(maxFarGap,gap);
                var route=indoor.RouteBetween(entryLocal,local);
                if(route==null || route.Count<2)continue;
                reachableFarAnchors++;
                if(gap<farthest)continue;
                selected=local;farthest=gap;found=true;
            }
            if(!found || farthest<NoShowObserverDestinationGap)
                throw new InvalidOperationException("Saved appointment venue has no surveyed reachable observer point with a 14m separation margin; choose a larger saved Social venue. venue="+
                    venueKey+"; resident="+residentId+"; surveyedCandidates="+surveyed.Count+"; farAnchors="+farAnchors+
                    "; reachableFarAnchors="+reachableFarAnchors+"; maxCandidateGap="+maxFarGap.ToString("F2")+
                    "; maxReachableGap="+farthest.ToString("F2")+"; source=surveyed-entry-ground");
            game.PlayerMotor.transform.position=room.transform.TransformPoint(selected)+Vector3.up;
            Physics.SyncTransforms();
            float stagedGap=Vector3.Distance(game.PlayerMotor.transform.position,actor.transform.position);
            if(stagedGap<NoShowObserverDestinationGap)
                throw new InvalidOperationException("No-show observer staging lost its 14m safety margin: gap="+stagedGap.ToString("F2"));
            return stagedGap;
        }

        private static bool TryBuildSafeObserverRoute(InteriorGeometry indoor,DaggerfallInterior room,Vector3 playerPosition,
            Vector3 actorPosition,out List<GuardPoint3> selectedRoute,out float greatestPathGap,out float greatestEndpointGap)
        {
            selectedRoute=null;greatestPathGap=0;greatestEndpointGap=0;
            Vector3 localFrom=room.transform.InverseTransformPoint(playerPosition-Vector3.up);
            Vector3 actorLocal=room.transform.InverseTransformPoint(actorPosition-Vector3.up);
            var surveyed=BoundedApproachPoints(indoor,actorLocal,200f,100f,NoShowApproachPointLimit);
            float bestLength=float.PositiveInfinity;
            foreach(Vector3 candidate in surveyed)
            {
                Vector3 target=room.transform.TransformPoint(candidate)+Vector3.up;
                float endpointGap=Vector3.Distance(target,actorPosition);
                greatestEndpointGap=Mathf.Max(greatestEndpointGap,endpointGap);
                if(endpointGap<NoShowObserverDestinationGap)continue;
                var route=indoor.RouteBetween(localFrom,candidate);
                if(route==null || route.Count<2)continue;
                float pathGap=float.PositiveInfinity,length=0;
                Vector3 previous=playerPosition;
                for(int i=1;i<route.Count;i++)
                {
                    GuardPoint3 point=route[i];
                    Vector3 current=room.transform.TransformPoint(new Vector3((float)point.X,(float)point.Y,(float)point.Z))+Vector3.up;
                    pathGap=Mathf.Min(pathGap,DistanceToSegment(actorPosition,previous,current));
                    length+=Vector3.Distance(previous,current);previous=current;
                }
                greatestPathGap=Mathf.Max(greatestPathGap,pathGap);
                if(pathGap<NoShowObserverRouteGap || length>=bestLength)continue;
                selectedRoute=route;bestLength=length;
            }
            return selectedRoute!=null;
        }

        private static List<Vector3> BoundedApproachPoints(InteriorGeometry indoor,Vector3 localTarget,
            float radius,float verticalTolerance,int maximum)
        {
            var all=indoor.ApproachPoints(localTarget,radius,verticalTolerance);
            if(all.Count<=maximum)return all;
            var bounded=new List<Vector3>(maximum);
            for(int i=0;i<maximum;i++)
            {
                int index=(int)Math.Round(i*(all.Count-1d)/(maximum-1d));
                if(bounded.Count==0 || (bounded[bounded.Count-1]-all[index]).sqrMagnitude>0.000001f)
                    bounded.Add(all[index]);
            }
            return bounded;
        }

        private static RoutineRecoveryPlan RestoredRecoveryLookup(WorldStateStore store,ResidentRoutine routine,
            RoutineRecoveryPlan expected)
        {
            if(store==null || routine==null || expected==null)return null;
            string routineFingerprint=RoutineRecoveryIdentity.Fingerprint(routine,expected.HomeBuildingKey,
                expected.WakeMinute,expected.SleepMinute);
            string appointmentFingerprint=RoutineRecoveryIdentity.Appointments(store.AppointmentsFor(routine.ResidentId),
                routine.ResidentId,expected.AbsoluteDay);
            return store.GetRoutineRecovery(routine.ResidentId,expected.AbsoluteDay,routineFingerprint,appointmentFingerprint);
        }

        private static float DistanceToSegment(Vector3 point,Vector3 start,Vector3 end)
        {
            Vector3 segment=end-start;
            float lengthSquared=segment.sqrMagnitude;
            if(lengthSquared<=0.000001f)return Vector3.Distance(point,start);
            float t=Mathf.Clamp01(Vector3.Dot(point-start,segment)/lengthSquared);
            return Vector3.Distance(point,start+segment*t);
        }

        private static bool FindEntrance(TownGeometry geometry, int buildingKey,
            out DaggerfallStaticDoors selectedOwner, out StaticDoor selectedDoor)
        {
            selectedOwner = null;
            selectedDoor = default(StaticDoor);
            TownGeometry.Entrance entrance;
            if (!geometry.Entrances.TryGetValue(buildingKey, out entrance)) return false;
            Vector3 target = geometry.SceneDoor(entrance);
            float best = 0.8f;
            foreach (var candidate in GameManager.Instance.StreamingWorld.GetCurrentBuildingDirectory()
                .GetComponentsInChildren<DaggerfallStaticDoors>())
            {
                if (candidate.Doors == null) continue;
                for (int i = 0; i < candidate.Doors.Length; i++)
                {
                    var door = candidate.Doors[i];
                    if (door.doorType != DoorTypes.Building || door.buildingKey != buildingKey) continue;
                    Vector3 normal = candidate.GetDoorNormal(i);
                    normal.y = 0;
                    if (normal.sqrMagnitude < 0.01f) continue;
                    Vector3 point = candidate.GetDoorPosition(i) + normal.normalized * 0.6f;
                    float gap = Vector3.Distance(point, target);
                    if (gap >= best) continue;
                    best = gap;
                    selectedOwner = candidate;
                    selectedDoor = door;
                }
            }
            return selectedOwner != null;
        }

        private static bool InteriorBuildingCleared(StreetPopulation streets)
        {
            var field = typeof(StreetPopulation).GetField("building", Private);
            return field != null && field.GetValue(streets) == null;
        }

        private static void FollowOutdoorCamera(ResidentActor actor, Vector3 ground)
        {
            Vector3 forward = actor.transform.forward;
            forward.y = 0;
            if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
            GameManager.Instance.PlayerMotor.transform.position = ground - forward.normalized * 3 + Vector3.up;
            SleepProbe.AimCamera(actor.transform.position);
            Physics.SyncTransforms();
        }

        private static bool ExteriorReady(StreetPopulation streets)
        {
            var field = typeof(StreetPopulation).GetField("building", Private);
            var geometryField = typeof(StreetPopulation).GetField("geometry", Private);
            var geometry = geometryField == null ? null : geometryField.GetValue(streets) as TownGeometry;
            return field != null && field.GetValue(streets) == null && geometry != null && geometry.Entrances.Count > 0;
        }

        private static void VerifyNativeCalendar()
        {
            foreach (int month in new[] { 0, 9, 11 })
            foreach (int day in new[] { 0, 27, 28, 29 })
            {
                var native = new DaggerfallDateTime { Year = 405, Month = month, Day = day, Hour = 12, Minute = 0, Second = 0 };
                var calendar = new AppointmentCalendar(native.Year, native.MonthOfYear, native.DayOfMonth);
                var checkedDays = new HashSet<string>();
                for (int offset = 1; offset <= 30 && checkedDays.Count < 7; offset++)
                {
                    var future = new DaggerfallDateTime(); future.FromSeconds(native.ToSeconds() + (ulong)(offset * 86400));
                    if (!checkedDays.Add(future.DayName)) continue;
                    int delta=(future.Year-calendar.Year)*360+(future.MonthOfYear-calendar.Month)*30+future.DayOfMonth-calendar.Day;
                    if(delta!=offset)throw new InvalidOperationException("Native appointment calendar mismatch.");
                    string date=future.Year+"-"+future.MonthOfYear+"-"+future.DayOfMonth;
                    var places = new List<ConversationTopic> { new ConversationTopic(42, "The Copper Kettle", "Known native fixture venue", false) };
                    string request = "Could we meet on " + future.DayName + ", " + date + " at 14:00 at The Copper Kettle?";
                    VerifyChoice(request, places, native, offset, 840);
                }
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_NATIVE_CALENDAR: PASS; native dates, weekday month reset and year rollover");
        }

        private static AppointmentChatDecision VerifyChoice(string request, List<ConversationTopic> places,
            DaggerfallDateTime requestDate, int days, int minute)
        {
            var calendar = new AppointmentCalendar(requestDate.Year, requestDate.MonthOfYear, requestDate.DayOfMonth);
            string output = "DIRECT|BOOK|" + days + "|" + minute + "|15|" + places[0].Id + "|1";
            var choice=SemanticAppointmentChat.Parse(output,places,requestDate.MinuteOfDay,0);
            if(choice==null || choice.Kind!="BOOK" || choice.DayOffset!=days || choice.MinuteOfDay!=minute)
                throw new InvalidOperationException("Native appointment parser changed the supplied semantic choice: "+request);
            return choice;
        }

        private static List<ConversationTopic> VerifiedPlace(PeopleOfTheIliacBayMod host, string residentId, int venueKey)
        {
            var places = (List<ConversationTopic>)typeof(PeopleOfTheIliacBayMod).GetMethod("ConversationAppointmentPlaces", Private)
                .Invoke(host, new object[] { residentId });
            var selected = places.Find(item => item.Id == venueKey);
            if (selected == null) throw new InvalidOperationException("Dialogue does not expose the fixture's verified venue.");
            return new List<ConversationTopic> { selected };
        }

        private static string ApplyChoice(PeopleOfTheIliacBayMod host, WorldStateStore store, string residentId,
            AppointmentChatDecision choice, double requestedAt)
        {
            return (string)typeof(PeopleOfTheIliacBayMod).GetMethod("ApplyAppointmentDecision", Private)
                .Invoke(host, new object[] { choice, store.AppointmentsFor(residentId), residentId, requestedAt });
        }

        private static AppointmentRecord BookThroughDialogue(PeopleOfTheIliacBayMod host, WorldStateStore store,
            string residentId, int venueKey, double requestedAt, double expectedStart)
        {
            var places = VerifiedPlace(host, residentId, venueKey);
            var requestDate = new DaggerfallDateTime(); requestDate.FromSeconds((ulong)requestedAt);
            var targetDate = new DaggerfallDateTime(); targetDate.FromSeconds((ulong)expectedStart);
            string request = "Could we meet on " + targetDate.DayName + ", " + targetDate.MonthName + " " +
                targetDate.DayOfMonth + ", " + targetDate.Year + " at 14:00 at " + places[0].Caption + "?";
            int days = (int)(Math.Floor(expectedStart / 86400) - Math.Floor(requestedAt / 86400));
            var choice = VerifyChoice(request, places, requestDate, days, 840);
            int capturedDisposition=(int)typeof(PeopleOfTheIliacBayMod).GetMethod("CaptureAppointmentDisposition",Private)
                .Invoke(host,new object[] {residentId});
            string result = ApplyChoice(host, store, residentId, choice, requestedAt);
            var bookings = store.AppointmentsFor(residentId);
            if (!result.StartsWith("APPOINTMENT CONFIRMED AND SAVED:", StringComparison.Ordinal) || bookings.Count != 1 ||
                bookings[0].Place.BuildingKey != venueKey || Math.Abs(bookings[0].StartsAt - expectedStart) > 0.01 ||
                bookings[0].EndsAt != expectedStart + 900 || bookings[0].GraceSeconds != 900)
                throw new InvalidOperationException("Dialogue binding did not reserve the exact request-time date: " + result);
            int expectedPatience=AppointmentTiming.CreatePatienceSeconds(store.WorldSeed,residentId,bookings[0].Id,capturedDisposition);
            if(bookings[0].PatienceSeconds<AppointmentTiming.MinimumPatienceSeconds ||
                bookings[0].PatienceSeconds>AppointmentTiming.MaximumPatienceSeconds || bookings[0].PatienceSeconds!=expectedPatience)
                throw new InvalidOperationException("Normal BOOK did not capture the production disposition-derived patience exactly once: resident="+
                    residentId+"; appointment="+bookings[0].Id+"; capturedDisposition="+capturedDisposition+
                    "; expected="+expectedPatience+"; actual="+bookings[0].PatienceSeconds);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_BOOK_PATIENCE_CAPTURE_PASS: resident="+residentId+"; appointment="+bookings[0].Id+
                "; capturedDisposition="+capturedDisposition+"; patienceSeconds="+bookings[0].PatienceSeconds+
                "; expectedFromWorldSeedAndIdentity="+expectedPatience+"; range=600..9000; capturedAtBooking=true");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_DIALOGUE_BINDING_PASS: namedCalendar=true; grammarAndParser=true; " +
                "requestDay=" + Math.Floor(requestedAt / 86400) + "; relativeDays=" + days + "; startsAt=" + bookings[0].StartsAt +
                "; delayedReplyDatePreserved=true; liveModel=not-invoked.");
            return bookings[0];
        }

        private static void VerifySameDayRefusal(PeopleOfTheIliacBayMod host, WorldStateStore store, string residentId, int venueKey, double now)
        {
            var places = VerifiedPlace(host, residentId, venueKey);
            int before = store.AppointmentsFor(residentId).Count;
            var current = new DaggerfallDateTime(); current.FromSeconds((ulong)now);
            // A captured request from yesterday that says tomorrow is still today
            // when its callback arrives. Do not silently move it to tomorrow again.
            var previous = new DaggerfallDateTime(); previous.FromSeconds((ulong)(now - 86400));
            var delayed = VerifyChoice("Could we meet tomorrow at 23:59 at " + places[0].Caption + "?", places, previous, 1, 1439);
            string result = ApplyChoice(host, store, residentId, delayed, now - 86400);
            if (!result.StartsWith("No new appointment was booked.", StringComparison.Ordinal) ||
                !result.Contains("Today's physical routine is already under way") || store.AppointmentsFor(residentId).Count != before)
                throw new InvalidOperationException("Delayed same-day request made an untrue confirmation or changed the ledger: " + result);
            if (current.MinuteOfDay < 1439)
            {
                var today = VerifyChoice("Could we meet today at 23:59 at " + places[0].Caption + "?", places, current, 0, 1439);
                result = ApplyChoice(host, store, residentId, today, now);
                if (!result.StartsWith("No new appointment was booked.", StringComparison.Ordinal) || store.AppointmentsFor(residentId).Count != before)
                    throw new InvalidOperationException("Today request changed the ledger or promised attendance: " + result);
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_SAME_DAY_REFUSAL_PASS: todayAndDelayedReply=true; truthfulResponse=true; ledgerUnchanged=true.");
        }

        private static double Length(IList<SavedRoutePoint> route)
        {
            double result = 0;
            for (int i = 1; i < route.Count; i++)
            {
                double dx = route[i].X - route[i - 1].X;
                double dz = route[i].Z - route[i - 1].Z;
                result += Math.Sqrt(dx * dx + dz * dz);
            }
            return result;
        }
    }
}
