using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.UserInterface;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Opt-in physical acceptance for the exact outdoor point captured from the
    // player's real position. The player stays there; only native schedules and
    // production movement carry the resident to it.
    public static class AppointmentPointProbe
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string SaveName = "People - Gothway Garden exact point";

        private static double Now()
        {
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            return clock.ToSeconds()+(clock.Second-Math.Floor(clock.Second));
        }

        public static IEnumerator Run(PeopleOfTheIliacBayMod host,StreetPopulation streets,string character)
        {
            var game=GameManager.Instance;
            var motor=game.PlayerMotor;
            var controller=motor.GetComponent<CharacterController>();
            bool motorEnabled=motor.enabled,controllerEnabled=controller&&controller.enabled;
            bool originalPreventEnemySpawns=game.PlayerEntity.PreventEnemySpawns;
            motor.enabled=false;
            if(controller)controller.enabled=false;
            try
            {
                if(originalPreventEnemySpawns)
                    throw new InvalidOperationException("Point probe requires normal native encounter-spawn state before it performs its scoped clock catch-up.");
                yield return RunCore(host,streets,character);
            }
            finally
            {
                game.PlayerEntity.PreventEnemySpawns=originalPreventEnemySpawns;
                if(controller)controller.enabled=controllerEnabled;
                motor.enabled=motorEnabled;
            }
        }

        private static IEnumerator RunCore(PeopleOfTheIliacBayMod host,StreetPopulation streets,string character)
        {
            var game=GameManager.Instance;
            if(game.PlayerEntity.Name!=character || game.IsPlayerInside || !streets.Ready)
                throw new InvalidOperationException("Exact point probe requires its loaded outdoor fixture character.");
            if(Math.Abs(DaggerfallUnity.Instance.WorldTime.TimeScale-2f)>0.001f)
                throw new InvalidOperationException("Exact point probe requires the normal 2x world clock.");
            DaggerfallUI.Instance.PopToHUD();

            // StreetPopulation.Ready means residents are prepared; exact point
            // snapshots additionally require the asynchronous entrance survey.
            // Wait for that real production prerequisite instead of treating its
            // normal startup gap as an invalid player location.
            float entranceDeadline=Time.realtimeSinceStartup+120f;
            float surveyWaitStarted=Time.realtimeSinceStartup;
            if(!streets.EntranceSurveyReady)
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_POINT_WAIT_SURVEY: map="+
                    (game.StreamingWorld.GetCurrentBuildingDirectory()==null?-1:game.StreamingWorld.GetCurrentBuildingDirectory().MapID)+
                    "; streetsReady="+streets.Ready+"; entranceSurveyReady=false");
            while(!streets.EntranceSurveyReady && Time.realtimeSinceStartup<entranceDeadline)
            {
                var current=game.StreamingWorld.GetCurrentBuildingDirectory();
                if(game.IsPlayerInside || current==null || current.MapID<0)
                    throw new InvalidOperationException("Exterior route survey wait lost its loaded outdoor map.");
                yield return null;
            }
            if(!streets.EntranceSurveyReady)
                throw new InvalidOperationException("Exterior entrance survey did not become ready within 120 real seconds; streetsReady="+
                    streets.Ready+"; entranceSurveyReady="+streets.EntranceSurveyReady+"; inside="+game.IsPlayerInside);
            if(Time.realtimeSinceStartup>surveyWaitStarted)
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_POINT_SURVEY_READY: waited="+
                    (Time.realtimeSinceStartup-surveyWaitStarted).ToString("F1",CultureInfo.InvariantCulture)+"; entrances="+
                    streets.EntranceSurveyReady);

            // AreEnemiesNearby() is a native global gate for the player and
            // mobile conversations. Capture its concrete inputs before any
            // appointment travel/wait so a false gate can be diagnosed without
            // changing or suppressing native danger behavior.
            if(LogDangerCensus(game))
                throw new InvalidOperationException("Appointment point fixture is blocked by DFU's native AreEnemiesNearby gate; see PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_POINT_DANGER_CENSUS_* for concrete enemies/spawners. No danger state was changed.");

            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            var geometry=(TownGeometry)typeof(StreetPopulation).GetField("geometry",Private).GetValue(streets);
            AppointmentPlace captured;
            if(!streets.TryCaptureConversationLocation(out captured) || captured==null ||
                captured.Kind!="world-point" || captured.BuildingKey>=0)
            {
                float verticalGap=float.NaN;
                if(directory!=null && geometry!=null && !game.IsPlayerInside)
                {
                    Vector3 feet=game.PlayerMotor.transform.position-Vector3.up;
                    Vector3 local=directory.transform.InverseTransformPoint(feet);
                    Vector3 ground=geometry.ScenePoint(new RoutePoint(local.x,local.z));
                    verticalGap=Math.Abs(feet.y-ground.y);
                }
                throw new InvalidOperationException("Production snapshot did not capture the player's current exterior point; streetsReady="+
                    streets.Ready+"; entranceSurveyReady="+streets.EntranceSurveyReady+"; inside="+game.IsPlayerInside+
                    "; map="+(directory==null?-1:directory.MapID)+"; playerGroundGap="+
                    verticalGap.ToString("F3",CultureInfo.InvariantCulture)+"; requiredMaxGap=0.500");
            }
            var state=(WorldState)host.GetSaveData();
            var population=state.Settlements.Find(item=>item.SettlementId==captured.SettlementId);
            var town=state.Routines.Find(item=>item.SettlementId==captured.SettlementId);
            var store=(WorldStateStore)typeof(StreetPopulation).GetField("worldStore",Private).GetValue(streets);
            if(directory==null || geometry==null || population==null || town==null || store==null)
                throw new InvalidOperationException("Exact point fixture is missing its native settlement, route survey, or state store.");
            if(directory.MapID!=captured.SettlementId)
                throw new InvalidOperationException("Captured point does not belong to the active map directory.");

            Vector3 target=geometry.ScenePoint(new RoutePoint(captured.X,captured.Z));
            Vector3 playerFeet=game.PlayerMotor.transform.position-Vector3.up;
            Vector3 capturedLocal=directory.transform.InverseTransformPoint(playerFeet);
            if(Vector3.Distance(new Vector3(capturedLocal.x,capturedLocal.y,capturedLocal.z),
                new Vector3((float)captured.X,(float)captured.Y,(float)captured.Z))>0.02f ||
                Vector3.Distance(playerFeet,target)>0.35f)
                throw new InvalidOperationException("Captured outdoor coordinates do not map back to the player's actual grounded position.");

            var existingAppointments=state.Appointments??new List<AppointmentRecord>();
            var trips=state.ResidentTrips??new List<ResidentTrip>();
            var routines=town.Residents??new List<ResidentRoutine>();
            Resident selected=null;
            ResidentRoutine selectedRoutine=null;
            AppointmentRecord appointment=null;
            string lastRejection="no eligible saved route";
            double now=Now();
            int day=(int)Math.Floor(now/86400d)+1;
            double startsAt=day*86400d+14*3600d;
            int tried=0;
            foreach(var routine in routines)
            {
                if(tried>=80)break;
                if(routine==null || string.IsNullOrEmpty(routine.ResidentId) || routine.Itinerary==null &&
                    (routine.Route==null || routine.Route.Count<2))continue;
                if(trips.Exists(item=>item.ResidentId==routine.ResidentId) ||
                    existingAppointments.Exists(item=>item.ResidentId==routine.ResidentId && item.Status!="cancelled"))continue;
                Resident person=null;
                foreach(var household in population.Households)
                {
                    if(household==null || household.Residents==null)continue;
                    person=household.Residents.Find(item=>item.Id==routine.ResidentId);
                    if(person!=null)break;
                }
                if(person==null || !person.IsAlive || person.AgeGroup!=ResidentAgeGroup.Adult ||
                    person.IsQuestProtected || person.NativeIdentity!=null || person.HomeKey<0)continue;
                tried++;
                var candidate=new AppointmentRecord
                {
                    Id="native-point-"+person.Id+"-"+day.ToString(CultureInfo.InvariantCulture),
                    ResidentId=person.Id,
                    Purpose="meet at the saved outdoor point",
                    Place=new AppointmentPlace {Kind="world-point",SettlementId=captured.SettlementId,
                        BuildingKey=-1,X=captured.X,Y=captured.Y,Z=captured.Z,Detail="our agreed meeting spot"},
                    StartsAt=startsAt,EndsAt=startsAt+15*60,GraceSeconds=15*60,
                    PatienceSeconds=AppointmentTiming.MinimumPatienceSeconds,Importance=2,Status="accepted"
                };
                if(!streets.TryBookAppointment(candidate,out lastRejection))continue;
                appointment=store.FindAppointment(candidate.Id);
                if(appointment==null || appointment.Place==null || appointment.Place.Kind!="world-point" ||
                    appointment.Place.SettlementId!=captured.SettlementId || appointment.Place.X!=captured.X ||
                    appointment.Place.Y!=captured.Y || appointment.Place.Z!=captured.Z)
                    throw new InvalidOperationException("Production booking did not retain the exact captured coordinates.");
                selected=person;selectedRoutine=routine;
                break;
            }
            if(appointment==null)
                throw new InvalidOperationException("No existing resident routine can reach the exact captured point at 14:00 tomorrow; tried="+
                    tried+"; lastRefusal="+lastRejection);

            var serialized=(WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),host.GetSaveData()));
            var savedPoint=serialized.Appointments==null?null:serialized.Appointments.Find(item=>item.Id==appointment.Id);
            if(savedPoint==null || savedPoint.Place==null || savedPoint.Place.Kind!="world-point" ||
                savedPoint.Place.SettlementId!=captured.SettlementId || savedPoint.Place.X!=captured.X ||
                savedPoint.Place.Y!=captured.Y || savedPoint.Place.Z!=captured.Z)
                throw new InvalidOperationException("Native FullSerializer round-trip changed the exact appointment point; idFound="+
                    (savedPoint!=null)+"; captured="+DescribePlace(captured)+"; serialized="+
                    DescribePlace(savedPoint==null?null:savedPoint.Place));

            // GetState samples a timestamp within the schedule selected for the
            // world's current day; it is not a future-day planner query. Move
            // the unobserved clock into the booked day before asking it about
            // tomorrow's appointment or scanning that day's outbound leg.
            // Keep this well before the 14:00 meeting so physical observation
            // still starts at the measured route departure below.
            double scheduleDayStart=day*86400d+5*3600d;
            if(scheduleDayStart<=Now() || scheduleDayStart>=startsAt)
                throw new InvalidOperationException("Appointment schedule activation time is outside the booked day.");
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            // The native PlayerEntity catches up every elapsed minute on its
            // next Update() and rolls normal random encounters. Since this
            // fixture deliberately jumps over unobserved time, use DFU's own
            // fast-travel suppression only until that catch-up has completed;
            // normal encounter behavior remains enabled during the real walk.
            game.PlayerEntity.PreventEnemySpawns=true;
            clock.RaiseTime((float)(scheduleDayStart-Now()));
            if(Math.Abs(Now()-scheduleDayStart)>1 || (int)Math.Floor(Now()/86400d)!=day ||
                Math.Abs(DaggerfallUnity.Instance.WorldTime.TimeScale-2f)>0.001f)
                throw new InvalidOperationException("Unobserved appointment-day activation changed clock precision, day, or time scale.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_POINT_DAY_ACTIVE: day="+day+
                "; now="+Now().ToString("G15",CultureInfo.InvariantCulture)+"; playerUnmoved=true");

            var atStart=streets.GetState(selected.Id,startsAt);
            Vector3 projected=geometry.ScenePoint(atStart.Position);
            if(atStart.Activity!=Activity.Meeting || Vector3.Distance(projected,target)>0.02f)
                throw new InvalidOperationException("The exact point was not the resident's scheduled meeting target: state="+
                    atStart.Activity+"; projectedError="+Vector3.Distance(projected,target).ToString("F3",CultureInfo.InvariantCulture));
            double departure=startsAt-1;
            if(streets.GetState(selected.Id,departure).Activity!=Activity.Travel)
                throw new InvalidOperationException("The resident does not have a physical outbound leg immediately before the point meeting.");
            double travelStart=departure;
            for(int guard=0;guard<8*60;guard++)
            {
                double candidateTime=travelStart-5;
                if(candidateTime<day*86400d+5*3600d || streets.GetState(selected.Id,candidateTime).Activity!=Activity.Travel)break;
                travelStart=candidateTime;
            }
            double outboundGameSeconds=startsAt-travelStart;
            if(outboundGameSeconds<=0 || outboundGameSeconds>600)
            {
                streets.TryCancelAppointment(appointment.Id,now,out _);
                throw new InvalidOperationException("Exact point resident route cannot be physically observed within five real minutes: gameSeconds="+
                    outboundGameSeconds.ToString("F1",CultureInfo.InvariantCulture));
            }
            var predeparture=travelStart-30;
            if(predeparture<=now)throw new InvalidOperationException("Exact point departure is no longer safely in the future.");
            clock.RaiseTime((float)(predeparture-Now()));
            if(Math.Abs(Now()-predeparture)>1 || Math.Abs(DaggerfallUnity.Instance.WorldTime.TimeScale-2f)>0.001f)
                throw new InvalidOperationException("Unobserved pre-departure time advance changed clock precision or time scale.");
            uint catchupTarget=clock.ToClassicDaggerfallTime();
            float catchupStarted=Time.realtimeSinceStartup;
            while((game.PlayerEntity.LastGameMinutes<catchupTarget || game.PlayerEntity.PreventEnemySpawns) &&
                Time.realtimeSinceStartup-catchupStarted<5f)
                yield return null;
            if(game.PlayerEntity.LastGameMinutes<catchupTarget || game.PlayerEntity.PreventEnemySpawns)
                throw new InvalidOperationException("Native PlayerEntity did not consume the unobserved clock catch-up and clear PreventEnemySpawns within 5 real seconds.");
            if(LogDangerCensus(game))
                throw new InvalidOperationException("Native danger gate became active during the probe's unobserved clock catch-up despite scoped DFU encounter suppression; see danger census. No enemy/spawner was changed.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_POINT_CLOCK_CATCHUP_PASS: targetMinute="+catchupTarget+
                "; playerLastGameMinute="+game.PlayerEntity.LastGameMinutes+"; nativeSuppressionConsumed=true"+
                "; suppressionReset="+(!game.PlayerEntity.PreventEnemySpawns)+"; enemiesNearby=false"+
                "; now="+Now().ToString("G15",CultureInfo.InvariantCulture)+
                "; waitRealSeconds="+(Time.realtimeSinceStartup-catchupStarted).ToString("F2",CultureInfo.InvariantCulture));
            Vector3 playerBefore=game.PlayerMotor.transform.position;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_POINT_FIXTURE_PASS: resident="+selected.Id+"; appointment="+appointment.Id+
                "; settlement="+captured.SettlementId+"; point="+captured.X.ToString("G15",CultureInfo.InvariantCulture)+","+
                captured.Y.ToString("G15",CultureInfo.InvariantCulture)+","+captured.Z.ToString("G15",CultureInfo.InvariantCulture)+
                "; scheduleDeparture="+travelStart.ToString("G15",CultureInfo.InvariantCulture)+"; arrival="+
                startsAt.ToString("G15",CultureInfo.InvariantCulture)+"; outboundGameSeconds="+
                outboundGameSeconds.ToString("F1",CultureInfo.InvariantCulture)+"; playerUnmoved=true");

            ResidentActor tracked=null;
            Vector3 previous=Vector3.zero,last=Vector3.zero;
            float moved=0,maxStep=0,minError=float.PositiveInfinity;
            bool outdoorBodySeen=false,arrived=false;
            double physicalArrivalAt=-1;
            float startedReal=Time.realtimeSinceStartup;
            double startedGame=Now();
            float realtimeDeadline=startedReal+Mathf.Max(180f,(float)(outboundGameSeconds/2+45));
            while(Time.realtimeSinceStartup<realtimeDeadline)
            {
                if(!game.IsPlayerOnHUD && !(DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow))
                    DaggerfallUI.Instance.PopToHUD();
                var currentDirectory=game.StreamingWorld.GetCurrentBuildingDirectory();
                if(currentDirectory==null || currentDirectory.MapID!=captured.SettlementId || game.IsPlayerInside)
                    throw new InvalidOperationException("Player left the captured exterior scene during the point walk.");
                Vector3 playerNow=directory.transform.InverseTransformPoint(game.PlayerMotor.transform.position-Vector3.up);
                float playerDrift=Vector3.Distance(playerNow,new Vector3((float)captured.X,(float)captured.Y,(float)captured.Z));
                if(playerDrift>0.75f)
                    throw new InvalidOperationException("Player moved away from the captured HERE point during native approach: drift="+
                        playerDrift.ToString("F3",CultureInfo.InvariantCulture));

                var actor=streets.FindActor(selected.Id);
                var liveState=streets.GetState(selected.Id,Now());
                if(actor && actor.gameObject.activeInHierarchy && actor.transform.parent==directory.transform &&
                    (liveState.Activity==Activity.Travel || liveState.Activity==Activity.Meeting))
                {
                    Vector3 ground=actor.transform.position-Vector3.up;
                    if(!outdoorBodySeen)
                    {outdoorBodySeen=true;tracked=actor;previous=last=ground;}
                    else
                    {
                        if(actor!=tracked)
                            throw new InvalidOperationException("Resident body identity changed during the point-bound outdoor leg.");
                        float step=Vector3.Distance(previous,ground);
                        maxStep=Mathf.Max(maxStep,step);moved+=step;
                        if(step>1.5f)
                            throw new InvalidOperationException("Point-bound resident moved discontinuously: step="+step.ToString("F3",CultureInfo.InvariantCulture));
                        previous=last=ground;
                    }
                    float error=Vector3.Distance(ground,target);
                    minError=Mathf.Min(minError,error);
                    if(liveState.Activity==Activity.Meeting && error<=1.35f)
                    {arrived=true;physicalArrivalAt=Now();break;}
                }
                if(Now()>startsAt+AppointmentTiming.MinimumPatienceSeconds+30)
                    throw new InvalidOperationException("Resident did not physically arrive at exact point before patience ended; activity="+
                        liveState.Activity+"; error="+(actor?Vector3.Distance(actor.transform.position-Vector3.up,target):-1));
                yield return null;
            }
            float realWalk=Time.realtimeSinceStartup-startedReal;
            double gameWalk=Now()-startedGame;
            if(!arrived || !outdoorBodySeen || moved<0.5f || maxStep>1.5f ||
                Math.Abs(gameWalk-realWalk*2)>3 || Vector3.Distance(last,target)>1.35f)
                throw new InvalidOperationException("Exact point physical arrival failed: arrived="+arrived+"; body="+outdoorBodySeen+
                    "; moved="+moved.ToString("F2",CultureInfo.InvariantCulture)+"; maxStep="+maxStep.ToString("F3",CultureInfo.InvariantCulture)+
                    "; finalError="+Vector3.Distance(last,target).ToString("F3",CultureInfo.InvariantCulture)+"; minError="+
                    minError.ToString("F3",CultureInfo.InvariantCulture)+"; realSeconds="+realWalk.ToString("F1",CultureInfo.InvariantCulture)+
                    "; gameSeconds="+gameWalk.ToString("F1",CultureInfo.InvariantCulture));

            // The normal half-second observer records attendance at actual
            // co-presence; the proactive opener is a separate confirmation.
            float observedDeadline=Time.realtimeSinceStartup+15f;
            AppointmentRecord outcome=null;
            bool opened=false;
            while(Time.realtimeSinceStartup<observedDeadline)
            {
                outcome=store.FindAppointment(appointment.Id);
                opened=outcome!=null && outcome.ConversationOpenedAt>=0;
                if(outcome!=null && outcome.Status=="attended" && outcome.OutcomeRecorded && opened)break;
                yield return null;
            }
            outcome=store.FindAppointment(appointment.Id);
            bool attendanceCommitted=outcome!=null && outcome.Status=="attended" && outcome.OutcomeRecorded &&
                outcome.NoShowPenalty==0;
            bool conversationOpened=outcome!=null && outcome.ConversationOpenedAt>=0;
            if(!attendanceCommitted || !conversationOpened)
            {
                LogDangerCensus(game);
                double failureNow=Now();
                var liveActor=streets.FindActor(selected.Id);
                var liveState=streets.GetState(selected.Id,failureNow);
                var world=store.Snapshot();
                string contactId="meeting:"+appointment.Id;
                var contact=world.RecentProactiveContacts==null?null:
                    world.RecentProactiveContacts.Find(item=>item.ContactId==contactId);
                bool offerAvailable=store.TryGetProactiveContactOffer(selected.Id,failureNow,out var offer);
                bool playerOnHud=game.IsPlayerOnHUD;
                bool enemiesNearby=game.AreEnemiesNearby(false,false);
                bool suppressed=store.IsNpcProactiveContactSuppressed(failureNow);
                bool activeNpcAction=store.ActiveNpcAction(selected.Id)!=null;
                bool alarmed=store.ActiveAlarm(selected.Id,failureNow)!=null;
                bool actorActive=liveActor&&liveActor.gameObject.activeInHierarchy;
                float pointDistance=liveActor?Vector3.Distance(liveActor.transform.position-Vector3.up,target):float.NaN;
                float playerDistance=liveActor?Vector3.Distance(liveActor.transform.position,game.PlayerMotor.transform.position):float.NaN;
                bool npcSeesPlayer=liveActor&&CivilianResponses.Sees(liveActor.transform.position,
                    game.PlayerMotor.transform.position,20);
                bool playerSeesNpc=liveActor&&CivilianResponses.Sees(game.PlayerMotor.transform.position,
                    liveActor.transform.position,20);
                var controller=game.PlayerMotor.GetComponent<CharacterController>();
                bool dispositionAvailable=store.TryGetActualConversationDisposition(selected.Id,out var actualDisposition);
                if(!dispositionAvailable)actualDisposition=store.ConversationDisposition(selected.Id);
                Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_POINT_OPENER_DIAGNOSTIC: resident="+selected.Id+
                    "; appointment="+appointment.Id+"; now="+failureNow.ToString("G15",CultureInfo.InvariantCulture)+
                    "; physicalArrivalAt="+physicalArrivalAt.ToString("G15",CultureInfo.InvariantCulture)+
                    "; contactDeadline="+(outcome==null?"-1":AppointmentTiming.ContactDeadline(outcome).ToString("G15",CultureInfo.InvariantCulture))+
                    "; attendanceCommitted="+attendanceCommitted+"; conversationOpened="+conversationOpened+
                    "; status="+(outcome==null?"missing":outcome.Status)+"; outcome="+
                    (outcome!=null&&outcome.OutcomeRecorded)+"; noShowPenalty="+(outcome==null?-1:outcome.NoShowPenalty)+
                    "; npcArrivedAt="+(outcome==null?"-1":outcome.NpcArrivedAt.ToString("G15",CultureInfo.InvariantCulture))+
                    "; npcLastSeenAt="+(outcome==null?"-1":outcome.NpcLastSeenAt.ToString("G15",CultureInfo.InvariantCulture))+
                    "; npcWaitedSeconds="+(outcome==null?"-1":outcome.NpcWaitedSeconds.ToString("F1",CultureInfo.InvariantCulture))+
                    "; playerArrivedAt="+(outcome==null?"-1":outcome.PlayerArrivedAt.ToString("G15",CultureInfo.InvariantCulture))+
                    "; playerAtVenueAt="+(outcome==null?"-1":outcome.PlayerAtVenueAt.ToString("G15",CultureInfo.InvariantCulture))+
                    "; playerNoticedAt="+(outcome==null?"-1":outcome.PlayerNoticedAt.ToString("G15",CultureInfo.InvariantCulture))+
                    "; approachUntil="+(outcome==null?"-1":outcome.ApproachUntil.ToString("G15",CultureInfo.InvariantCulture))+
                    "; offerAvailable="+offerAvailable+"; offerKind="+(offerAvailable?offer.Kind.ToString():"none")+
                    "; offerNoticedAt="+(offerAvailable?offer.NoticedAt.ToString("G15",CultureInfo.InvariantCulture):"-1")+
                    "; recentContact="+(contact!=null)+"; contactNoticedAt="+(contact==null?"-1":
                        contact.NoticedAt.ToString("G15",CultureInfo.InvariantCulture))+"; contactApproachUntil="+
                    (contact==null?"-1":contact.ApproachUntil.ToString("G15",CultureInfo.InvariantCulture))+
                    "; activeContact="+(world.ActiveProactiveContactId??"none")+
                    "; blockedUntil="+world.PlayerProactiveContactBlockedUntil.ToString("G15",CultureInfo.InvariantCulture)+
                    "; suppressed="+suppressed+"; gamePaused="+GameManager.IsGamePaused+"; playerOnHud="+playerOnHud+
                    "; playerInvisible="+game.PlayerEntity.IsInvisible+"; playerConcealed="+
                    game.PlayerEntity.IsMagicallyConcealed+"; enemiesNearby="+enemiesNearby+"; playerMotorEnabled="+
                    game.PlayerMotor.enabled+"; controllerEnabled="+(controller&&controller.enabled)+
                    "; disposition="+actualDisposition+"; questProtected="+selected.IsQuestProtected+
                    "; actorActive="+actorActive+"; actorSleeping="+(liveActor&&liveActor.IsSleeping)+
                    "; actorConversing="+(liveActor&&liveActor.IsConversing)+"; parentIsDirectory="+
                    (liveActor&&liveActor.transform.parent==directory.transform)+"; schedule="+liveState.Activity+
                    "; pointDistance="+pointDistance.ToString("F3",CultureInfo.InvariantCulture)+
                    "; playerDistance="+playerDistance.ToString("F3",CultureInfo.InvariantCulture)+
                    "; npcSeesPlayer="+npcSeesPlayer+"; playerSeesNpc="+playerSeesNpc+
                    "; activeAction="+activeNpcAction+"; alarmed="+alarmed+"; playerGroundAtPoint="+
                    (Vector3.Distance(game.PlayerMotor.transform.position-Vector3.up,target)<=1.35f));
                throw new InvalidOperationException("Resident arrived at the exact point but attendance/opening did not both commit; attendance="+
                    attendanceCommitted+"; conversationOpened="+conversationOpened+".");
            }

            var actualTalk=DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
            var talkManager=game.TalkManager;
            var proactiveActor=streets.FindActor(selected.Id);
            if(actualTalk==null||!actualTalk.IsSetup||!proactiveActor||talkManager.CurrentNPCType!=TalkManager.NPCType.Mobile||
                talkManager.MobileNPC!=proactiveActor.Person)
                throw new InvalidOperationException("Appointment receipt committed without the exact native Talk window and expected mobile speaker still active.");
            FieldInfo busyField=typeof(PeopleOfTheIliacBayMod).GetField("conversationBusy",Private);
            FieldInfo listFieldField=typeof(PeopleOfTheIliacBayMod).GetField("conversationListField",BindingFlags.Static|BindingFlags.NonPublic);
            FieldInfo nativeListField=listFieldField==null?null:listFieldField.GetValue(null) as FieldInfo;
            if(busyField==null||nativeListField==null)
                throw new InvalidOperationException("Could not inspect the proactive opener's pending reply and native visible transcript.");
            float openerDeadline=Time.realtimeSinceStartup+30f;
            bool openerWasBusy=(bool)busyField.GetValue(host);
            while((bool)busyField.GetValue(host)&&Time.realtimeSinceStartup<openerDeadline&&
                DaggerfallUI.UIManager.TopWindow==actualTalk&&talkManager.CurrentNPCType==TalkManager.NPCType.Mobile&&
                talkManager.MobileNPC==proactiveActor.Person)
                yield return null;
            bool openerStillBusy=(bool)busyField.GetValue(host);
            var visibleTranscript=nativeListField.GetValue(actualTalk) as ListBox;
            string visibleOpener=null;
            int visibleOpenerCount=0;
            bool syntheticPlayerPrompt=false;
            if(visibleTranscript!=null)
                for(int index=0;index<visibleTranscript.Count;index++)
                {
                    var item=visibleTranscript.GetItem(index);
                    if(item==null||item.textLabel==null)continue;
                    string line=item.textLabel.Text??string.Empty;
                    if(item.textLabel.Name=="PeopleOfTheIliacBayReturnGreeting")
                    {visibleOpener=line.Trim();visibleOpenerCount++;}
                    if(item.textColor==DaggerfallUI.DaggerfallQuestionTextColor&&
                        (line=="A proactive contact has opened this conversation."||line=="The player returns silently."))
                        syntheticPlayerPrompt=true;
                }
            bool openerOnMeeting=ProactiveContactConversationContext.ReplyIsOnTopic(
                ProactiveContactKind.AppointmentMeeting,visibleOpener,
                appointment.Place==null?null:appointment.Place.Detail,appointment.Purpose,null);
            if(openerStillBusy||DaggerfallUI.UIManager.TopWindow!=actualTalk||visibleOpenerCount!=1||!openerOnMeeting||syntheticPlayerPrompt)
            {
                Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_POINT_VISIBLE_OPENER_DIAGNOSTIC: appointment="+appointment.Id+
                    "; actualTalkStillTop="+(DaggerfallUI.UIManager.TopWindow==actualTalk)+
                    "; exactMobileSpeaker="+(talkManager.CurrentNPCType==TalkManager.NPCType.Mobile&&
                        talkManager.MobileNPC==proactiveActor.Person)+
                    "; openerWasBusy="+openerWasBusy+"; openerStillBusy="+openerStillBusy+
                    "; replyWaitRealSeconds="+(30f-Mathf.Max(0,openerDeadline-Time.realtimeSinceStartup)).ToString("F2",CultureInfo.InvariantCulture)+
                    "; transcriptPresent="+(visibleTranscript!=null)+"; openerCount="+visibleOpenerCount+
                    "; openerOnMeeting="+openerOnMeeting+"; syntheticPlayerPrompt="+syntheticPlayerPrompt+
                    "; visibleOpener="+(visibleOpener??"<missing>")+
                    "; meetingPurpose="+appointment.Purpose+"; meetingPlace="+
                    (appointment.Place==null?"<missing>":appointment.Place.Detail));
                throw new InvalidOperationException("Exact appointment Talk opened but did not finish with one visible on-topic NPC opener and no synthetic player turn.");
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_POINT_VISIBLE_OPENER_PASS: appointment="+appointment.Id+
                "; exactTalk=true; exactSpeaker=true; openerWasBusy="+openerWasBusy+
                "; openerCount="+visibleOpenerCount+"; onTopic=true; syntheticPlayerPrompt=false; reply="+visibleOpener);

            DaggerfallUI.Instance.PopToHUD();
            bool saved=false,loaded=false;
            SaveLoadManager.OnSaveEventHandler actualSave=delegate(SaveData_v1 ignored){saved=true;};
            SaveLoadManager.OnLoadEventHandler actualLoad=delegate(SaveData_v1 ignored){loaded=true;};
            SaveLoadManager.OnSave+=actualSave;SaveLoadManager.OnLoad+=actualLoad;
            try
            {
                game.SaveLoadManager.Save(character,SaveName);
                float saveDeadline=Time.realtimeSinceStartup+45;
                while(!saved && Time.realtimeSinceStartup<saveDeadline)yield return null;
                if(!saved)throw new InvalidOperationException("Native point appointment disk save timed out.");
                game.SaveLoadManager.EnumerateSaves();
                int slot=game.SaveLoadManager.FindSaveFolderByNames(character,SaveName);
                if(slot<0)throw new InvalidOperationException("Native exact-point acceptance save was not created.");
                game.SaveLoadManager.Load(slot);DaggerfallUI.Instance.PopToHUD();
                float loadDeadline=Time.realtimeSinceStartup+90;
                while((!loaded || !streets.Ready || !ExteriorReady(streets,captured.SettlementId)) &&
                    Time.realtimeSinceStartup<loadDeadline)yield return null;
                if(!loaded || !streets.Ready || !ExteriorReady(streets,captured.SettlementId))
                    throw new InvalidOperationException("Native point appointment save did not reload into its exterior settlement.");
            }
            finally
            { SaveLoadManager.OnSave-=actualSave;SaveLoadManager.OnLoad-=actualLoad; }

            var restored=(WorldState)host.GetSaveData();
            var restoredRecord=restored.Appointments==null?null:restored.Appointments.Find(item=>item.Id==appointment.Id);
            if(restoredRecord==null || restoredRecord.Place==null || restoredRecord.Place.Kind!="world-point" ||
                restoredRecord.Place.SettlementId!=captured.SettlementId || restoredRecord.Place.X!=captured.X ||
                restoredRecord.Place.Y!=captured.Y || restoredRecord.Place.Z!=captured.Z ||
                restoredRecord.Status!="attended" || !restoredRecord.OutcomeRecorded || restoredRecord.ConversationOpenedAt<0)
                throw new InvalidOperationException("Native disk save/load changed exact point or meeting/open receipt.");
            var restoredDirectory=game.StreamingWorld.GetCurrentBuildingDirectory();
            var restoredGeometry=(TownGeometry)typeof(StreetPopulation).GetField("geometry",Private).GetValue(streets);
            if(restoredDirectory==null || restoredGeometry==null || restoredDirectory.MapID!=captured.SettlementId ||
                Vector3.Distance(restoredGeometry.ScenePoint(new RoutePoint(restoredRecord.Place.X,restoredRecord.Place.Z)),target)>0.02f)
                throw new InvalidOperationException("Restored point no longer resolves to the same settlement-local exterior coordinates.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_POINT_PHYSICAL_PASS: resident="+selected.Id+"; appointment="+appointment.Id+
                "; point="+captured.X.ToString("G15",CultureInfo.InvariantCulture)+","+captured.Y.ToString("G15",CultureInfo.InvariantCulture)+","+
                captured.Z.ToString("G15",CultureInfo.InvariantCulture)+"; walked="+moved.ToString("F2",CultureInfo.InvariantCulture)+
                "; maxStep="+maxStep.ToString("F3",CultureInfo.InvariantCulture)+"; finalError="+
                Vector3.Distance(last,target).ToString("F3",CultureInfo.InvariantCulture)+"; minError="+
                minError.ToString("F3",CultureInfo.InvariantCulture)+"; realSeconds="+realWalk.ToString("F1",CultureInfo.InvariantCulture)+
                "; gameSeconds="+gameWalk.ToString("F1",CultureInfo.InvariantCulture)+"; attendance=true; autoTalkOpened=true; " +
                "nativeDiskSaveLoad=true; exactSettlementLocalPoint=true; playerSaveWrite=ownedProbeSave:"+SaveName);
        }

        private static bool LogDangerCensus(GameManager game)
        {
            Vector3 playerPosition=game.PlayerMotor.transform.position;
            float spawnDistance=1024f*MeshReader.GlobalScale;
            bool nativeGate=game.AreEnemiesNearby(false,false);
            int enemyCount=0;
            int enemyGateCount=0;
            var player=game.PlayerEntityBehaviour;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_POINT_DANGER_CENSUS_BEGIN: player="+
                playerPosition.ToString("F2")+"; enemiesNearby="+nativeGate+
                "; nativeSpawnerRadius="+spawnDistance.ToString("F2",CultureInfo.InvariantCulture));

            foreach(DaggerfallEntityBehaviour behaviour in ActiveGameObjectDatabase.GetActiveEnemyBehaviours())
            {
                if(!behaviour)continue;
                EnemySenses senses=behaviour.GetComponent<EnemySenses>();
                EnemyMotor motor=behaviour.GetComponent<EnemyMotor>();
                EnemyEntity enemy=behaviour.Entity as EnemyEntity;
                DaggerfallEnemy daggerfallEnemy=behaviour.GetComponent<DaggerfallEnemy>();
                QuestResourceBehaviour quest=behaviour.GetComponent<QuestResourceBehaviour>();
                bool targetsPlayer=senses&&senses.Target==player;
                bool seesPlayer=targetsPlayer&&senses.TargetInSight;
                bool classic=senses&&senses.WouldBeSpawnedInClassic;
                bool hostile=motor&&motor.IsHostile;
                bool hasEnemy=enemy!=null;
                bool playerAlly=hasEnemy&&enemy.MobileEnemy.Team==MobileTeams.PlayerAlly;
                bool eligibleType=behaviour.EntityType==EntityTypes.EnemyMonster||behaviour.EntityType==EntityTypes.EnemyClass;
                bool wouldGate=eligibleType&&senses&&(seesPlayer||classic)&&hostile&&!playerAlly;
                if(wouldGate)enemyGateCount++;
                enemyCount++;
                string target= senses&&senses.Target ? senses.Target.gameObject.name : "none";
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_POINT_DANGER_ENEMY: name="+behaviour.gameObject.name+
                    "; loadID="+(daggerfallEnemy?daggerfallEnemy.LoadID:0)+
                    "; active="+behaviour.gameObject.activeInHierarchy+"; behaviourEnabled="+behaviour.enabled+
                    "; entityType="+behaviour.EntityType+"; health="+(behaviour.Entity==null?-1:behaviour.Entity.CurrentHealth)+
                    "/"+(behaviour.Entity==null?-1:behaviour.Entity.MaxHealth)+"; senses="+(bool)senses+
                    "; motor="+(bool)motor+"; hostile="+hostile+"; target="+target+
                    "; targetIsPlayer="+targetsPlayer+"; targetInSight="+(senses&&senses.TargetInSight)+
                    "; wouldBeSpawnedInClassic="+classic+"; team="+(hasEnemy?enemy.MobileEnemy.Team.ToString():"none")+
                    "; entityTeam="+(behaviour.Entity==null?"none":behaviour.Entity.Team.ToString())+
                    "; questResource="+(bool)quest+"; questSpawn="+(daggerfallEnemy&&daggerfallEnemy.QuestSpawn)+
                    "; gateEligible="+wouldGate+"; distance="+Vector3.Distance(playerPosition,behaviour.transform.position).ToString("F2",CultureInfo.InvariantCulture));
            }

            int spawnerCount=0;
            int nearbySpawnerCount=0;
            foreach(FoeSpawner spawner in ActiveGameObjectDatabase.GetActiveFoeSpawners())
            {
                if(!spawner)continue;
                float distance=Vector3.Distance(playerPosition,spawner.transform.position);
                bool gateEligible=distance<spawnDistance;
                if(gateEligible)nearbySpawnerCount++;
                spawnerCount++;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_POINT_DANGER_SPAWNER: name="+spawner.gameObject.name+
                    "; active="+spawner.gameObject.activeInHierarchy+"; enabled="+spawner.enabled+
                    "; foeType="+spawner.FoeType+"; lastFoeType="+spawner.lastFoeType+
                    "; spawnCount="+spawner.SpawnCount+
                    "; position="+spawner.transform.position.ToString("F2")+
                    "; distance="+distance.ToString("F2",CultureInfo.InvariantCulture)+
                    "; gateEligible="+gateEligible);
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_APPOINTMENT_POINT_DANGER_CENSUS_END: enemyBehaviours="+enemyCount+
                "; hostileEnemyGateCandidates="+enemyGateCount+"; activeFoeSpawners="+spawnerCount+
                "; nearbySpawnerGateCandidates="+nearbySpawnerCount);
            return nativeGate;
        }

        private static bool ExteriorReady(StreetPopulation streets,int settlementId)
        {
            var game=GameManager.Instance;
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            return !game.IsPlayerInside && directory!=null && directory.MapID==settlementId && streets.Ready;
        }

        private static string DescribePlace(AppointmentPlace place)
        {
            if(place==null)return "null";
            return place.Kind+"/"+place.SettlementId+"/"+place.BuildingKey+"/"+
                place.X.ToString("R",CultureInfo.InvariantCulture)+","+
                place.Y.ToString("R",CultureInfo.InvariantCulture)+","+
                place.Z.ToString("R",CultureInfo.InvariantCulture);
        }
    }
}
