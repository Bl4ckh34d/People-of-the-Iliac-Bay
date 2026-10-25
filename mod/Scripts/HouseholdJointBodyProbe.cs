using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Two real bodies on surveyed native routes, using a detached event/store.
    // No world-clock changes, campaign publication, purchases or save writes.
    internal static class HouseholdJointBodyProbe
    {
        private sealed class PreparedRequest
        {
            internal HouseholdDayRequest Request;
            internal HouseholdDayPlan Plan;
        }
        internal static IEnumerator Run(PeopleOfTheIliacBayMod host,StreetPopulation streets)
        {
            var game=GameManager.Instance;var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            var geometry=(TownGeometry)typeof(StreetPopulation).GetField("geometry",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(streets);
            // Town readiness precedes the incremental doorway survey. The old
            // full-day preflight used to wait for it before calling this probe.
            int initialEntrances=geometry==null?0:geometry.Entrances.Count;
            DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
            float doorsUntil=Time.realtimeSinceStartup+60;
            while(geometry!=null && geometry.Entrances.Count==0 && streets.Ready &&
                game.StreamingWorld.GetCurrentBuildingDirectory()==directory && Time.realtimeSinceStartup<doorsUntil)
                yield return null;
            if(geometry==null || geometry.Entrances.Count==0)
                throw new InvalidOperationException("Family body probe needs the completed native doorway survey.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_BODY_DOORS_READY: initial="+initialEntrances+
                "; complete="+geometry.Entrances.Count);
            var snapshot=(WorldState)host.GetSaveData();var prepared=new PreparedRequest();
            var preparation=PrepareRequest(snapshot,directory,geometry,6*60,22*60,
                DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),prepared,
                ()=>host && streets && streets.Ready && game.StreamingWorld.GetCurrentBuildingDirectory()==directory);
            try{while(preparation.MoveNext())yield return null;}finally{(preparation as IDisposable)?.Dispose();}
            var request=prepared.Request;var plan=prepared.Plan;
            if(request==null)throw new InvalidOperationException("No actual two-parent household has a fresh native home-to-Inn route suitable for body acceptance.");
            var store=new WorldStateStore(0,PeopleOfTheIliacBayMod.ConversationArchiveRoot());store.Restore(snapshot);
            string reason;if(!store.TryPublishHouseholdDay(plan,request.PlannedAt,out reason))throw new InvalidOperationException(reason);
            var motion=HouseholdJointMovement.Create(plan,plan.PlannedAt);store.SaveHouseholdJointProgress(motion.Snapshot());
            bool hostEnabled=host.enabled,streetEnabled=streets.enabled;
            var player=game.PlayerMotor;var originalPlayerPosition=player.transform.position;
            var originalLookDirection=Camera.main.transform.forward;
            bool playerEnabled=player.enabled,playerCancelled=player.CancelMovement;
            var playerColliders=player.GetComponentsInChildren<Collider>(true);var colliderEnabled=new bool[playerColliders.Length];
            for(int i=0;i<playerColliders.Length;i++)colliderEnabled[i]=playerColliders[i].enabled;
            var hidden=new List<GameObject>();ResidentActor first=null,second=null;
            foreach(var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                if(actor.gameObject.activeSelf)
                {
                    if(actor.IsSeated || actor.IsSleeping || actor.IsConversing)
                        throw new InvalidOperationException("Controlled exterior fixture cannot interrupt a live seat, sleep or conversation owner.");
                    hidden.Add(actor.gameObject);
                }
            try
            {
                player.CancelMovement=true;player.enabled=false;
                foreach(var collider in playerColliders)collider.enabled=false;
                host.enabled=false;streets.enabled=false;
                foreach(var body in hidden)body.SetActive(false);
                first=Create(store.ResolveResident(plan.First.ResidentId),directory.transform,game);
                second=Create(store.ResolveResident(plan.Second.ResidentId),directory.transform,game);
                first.Place(Scene(geometry,motion.Snapshot().First.Position),Vector3.forward,true);
                second.Place(Scene(geometry,motion.Snapshot().Second.Position),Vector3.forward,true);
                WatchPair(first,second,Scene(geometry,motion.Targets(true,true).First),true);
                if(!first.GetComponent<CrowdMotion>().HasPlacement || !second.GetComponent<CrowdMotion>().HasPlacement)
                    throw new InvalidOperationException("Actual home doorway cannot place both family members.");
                double departure=plan.AbsoluteDay*86400d+Math.Max(plan.JointInn.First.DepartureSeconds,plan.JointInn.Second.DepartureSeconds);
                motion.Depart(departure,Point(first,directory.transform),Point(second,directory.transform));
                store.SaveHouseholdJointProgress(motion.Snapshot());
                var executor=new HouseholdJointBodyMotion(motion,first,second,geometry,directory.transform,
                    p=>store.SaveHouseholdJointProgress(p));
                if(geometry.UsesNativeNavigation)
                {
                    // Force the first native command to create its proxy from the
                    // real body position. A future route corner is not a valid
                    // starting position for an observed paired movement step.
                    var firstNative=first.GetComponent<NativeResidentNavigation>();
                    var secondNative=second.GetComponent<NativeResidentNavigation>();
                    if(!firstNative || !secondNative)
                        throw new InvalidOperationException("Both family bodies need native navigation for the initial-ground regression.");
                    var firstGround=first.transform.position-Vector3.up;
                    var secondGround=second.transform.position-Vector3.up;
                    firstNative.ResetPlacement();secondNative.ResetPlacement();
                    first.GetComponent<CrowdMotion>().RestorePlacement(firstGround);
                    second.GetComponent<CrowdMotion>().RestorePlacement(secondGround);
                    var firstTargets=motion.RejoinTargets(true,true);
                    executor.Tick(departure,true,true);
                    if(Vector3.Distance(firstGround,first.transform.position-Vector3.up)>0.551f ||
                        Vector3.Distance(secondGround,second.transform.position-Vector3.up)>0.551f ||
                        !firstNative.HasDestination || !secondNative.HasDestination ||
                        Vector3.Distance(firstNative.Destination,Scene(geometry,firstTargets.First))>0.01f ||
                        Vector3.Distance(secondNative.Destination,Scene(geometry,firstTargets.Second))>0.01f)
                        throw new InvalidOperationException("Initial native family command jumped to a future waypoint or lost its route target.");
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_NATIVE_INITIAL_GROUND_PASS: bodies=start-at-observed-position; destinations=next-saved-corners; no-teleport=true");
                }
                var initialA=first.transform.position;var initialB=second.transform.position;int initialSequence=motion.Snapshot().Sequence;
                first.GetComponent<CrowdMotion>().ResetPlacement();executor.Tick(departure,true,true);
                if(first.transform.position!=initialA || second.transform.position!=initialB || motion.Snapshot().Sequence!=initialSequence)
                    throw new InvalidOperationException("An unplaced family body teleported or let its partner move.");
                first.GetComponent<CrowdMotion>().RestorePlacement(initialA-Vector3.up);
                bool invalidTime=false;try{executor.Tick(departure-1,true,true);}catch(ArgumentOutOfRangeException){invalidTime=true;}
                if(!invalidTime || first.transform.position!=initialA || second.transform.position!=initialB)
                    throw new InvalidOperationException("Invalid observation time moved a family body.");
                float started=Time.realtimeSinceStartup,deadline=started+480,lastProgressAt=started;bool paused=false,resumed=false,missing=false,commitFailure=false;
                double metres=0;int observations=0;bool outboundArrived=false,firstPrecise=false,secondPrecise=false;
                while(Time.realtimeSinceStartup<deadline &&
                    (motion.Snapshot().Phase==HouseholdJointPhase.Outbound || motion.Snapshot().Phase==HouseholdJointPhase.Returning))
                {
                    double at=departure+Time.realtimeSinceStartup-started;
                    var a=first.transform.position;var b=second.transform.position;
                    var commandedTargets=motion.RejoinTargets(true,true);
                    bool firstNear=commandedTargets.MoveFirst && Vector3.Distance(a-Vector3.up,Scene(geometry,commandedTargets.First))<=0.55f;
                    bool secondNear=commandedTargets.MoveSecond && Vector3.Distance(b-Vector3.up,Scene(geometry,commandedTargets.Second))<=0.55f;
                    executor.Tick(at,true,true);var progress=motion.Snapshot();observations++;
                    if(geometry.UsesNativeNavigation)
                    {
                        firstPrecise|=firstNear;secondPrecise|=secondNear;
                    }
                    WatchPair(first,second,Scene(geometry,motion.Targets(true,true).First));
                    if(Vector3.Distance(a,first.transform.position)>0.551f || Vector3.Distance(b,second.transform.position)>0.551f)
                        throw new InvalidOperationException("Pair body movement reconstructed or teleported a target.");
                    if(Math.Abs(Point(first,directory.transform).X-progress.First.Position.X)>0.0001 ||
                        Math.Abs(Point(first,directory.transform).Z-progress.First.Position.Z)>0.0001 ||
                        Math.Abs(Point(second,directory.transform).X-progress.Second.Position.X)>0.0001 ||
                        Math.Abs(Point(second,directory.transform).Z-progress.Second.Position.Z)>0.0001)
                        throw new InvalidOperationException("Pair checkpoint is not the actual observed body position.");
                    double advanced=progress.First.Distance+progress.Second.Distance;
                    if(advanced>metres+0.01)lastProgressAt=Time.realtimeSinceStartup;
                    metres=advanced;
                    if(Time.realtimeSinceStartup-lastProgressAt>40)
                        throw new InvalidOperationException("Actual family route stalled: metres="+metres+"; first="+progress.First.Segment+"; second="+progress.Second.Segment+
                            "; a="+first.transform.position.ToString("F5")+"; b="+second.transform.position.ToString("F5")+
                            "; targetA="+Scene(geometry,motion.Targets(true,true).First).ToString("F5")+"; targetB="+Scene(geometry,motion.Targets(true,true).Second).ToString("F5")+
                            "; nativeA="+NativeState(first,Scene(geometry,motion.Targets(true,true).First))+
                            "; nativeB="+NativeState(second,Scene(geometry,motion.Targets(true,true).Second)));
                    if(!commitFailure && metres>1)
                    {
                        commitFailure=true;var original=motion.Snapshot();int calls=0;
                        var failedMotion=store.GetHouseholdJointMovement(plan.SettlementId,plan.HomeKey);
                        var failing=new HouseholdJointBodyMotion(failedMotion,first,second,geometry,directory.transform,p=>{calls++;throw new InvalidOperationException("controlled-commit-failure");});
                        bool caught=false;
                        try{failing.Tick(departure+Time.realtimeSinceStartup-started,true,true);}
                        catch(InvalidOperationException error){if(error.Message!="controlled-commit-failure")throw;caught=true;}
                        if(!caught || calls!=1 || failedMotion.Snapshot().Sequence!=original.Sequence ||
                            store.ReadHouseholdJointProgress(plan.SettlementId,plan.HomeKey).Sequence!=original.Sequence)
                            throw new InvalidOperationException("Failed pair commit changed controller or durable progress.");
                        var actualA=first.transform.position;var actualB=second.transform.position;
                        failing.Tick(departure+Time.realtimeSinceStartup-started,true,true);
                        if(calls!=1 || first.transform.position!=actualA || second.transform.position!=actualB)
                            throw new InvalidOperationException("A rejected pair commit allowed further movement.");
                        // Recover only the two real bounded steps just observed;
                        // no additional motion or schedule-position catch-up.
                        double recoverAt=departure+Time.realtimeSinceStartup-started;
                        store.SaveHouseholdJointProgress(failedMotion.PreviewObservation(recoverAt,Point(first,directory.transform),Point(second,directory.transform)));
                        failedMotion.Observe(recoverAt,Point(first,directory.transform),Point(second,directory.transform));motion=failedMotion;
                        executor=new HouseholdJointBodyMotion(motion,first,second,geometry,directory.transform,p=>store.SaveHouseholdJointProgress(p));
                    }
                    if(!paused && metres>2)
                    {
                        paused=true;first.SetConversationFacing(true);var checkpoint=motion.Snapshot();
                        for(int i=0;i<20;i++)
                        {
                            var beforeA=first.transform.position;var beforeB=second.transform.position;
                            executor.Tick(departure+Time.realtimeSinceStartup-started,true,true);
                            if(first.transform.position!=beforeA || second.transform.position!=beforeB ||
                                motion.Snapshot().Sequence!=checkpoint.Sequence)
                                throw new InvalidOperationException("Talking to one parent moves the pair or records unwalked progress.");
                            yield return null;
                        }
                        first.SetConversationFacing(false);resumed=true;
                    }
                    if(!missing && metres>4)
                    {
                        missing=true;second.gameObject.SetActive(false);var checkpoint=motion.Snapshot();
                        for(int i=0;i<20;i++)
                        {
                            var before=first.transform.position;
                            executor.Tick(departure+Time.realtimeSinceStartup-started,true,true);
                            if(first.transform.position!=before || motion.Snapshot().Sequence!=checkpoint.Sequence)
                                throw new InvalidOperationException("A missing partner leaves the other parent walking alone.");
                            yield return null;
                        }
                        second.gameObject.SetActive(true);
                        string json=SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot());
                        var restored=new WorldStateStore(0,PeopleOfTheIliacBayMod.ConversationArchiveRoot());
                        restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),json));store=restored;
                        motion=store.GetHouseholdJointMovement(plan.SettlementId,plan.HomeKey);
                        if(motion==null)throw new InvalidOperationException("Actual pair progress cannot resume after native serialization.");
                        executor=new HouseholdJointBodyMotion(motion,first,second,geometry,directory.transform,p=>store.SaveHouseholdJointProgress(p));
                    }
                    if(observations%300==0)Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_BODY_PROGRESS: metres="+metres+"; first="+progress.First.Segment+"; second="+progress.Second.Segment);
                    if(motion.Snapshot().Phase==HouseholdJointPhase.AtInn)
                    {
                        outboundArrived=true;
                        // This fixture turns back from the real exterior door;
                        // it does not claim Inn entry, purchases or shared seats.
                        motion.BeginReturn(departure+Time.realtimeSinceStartup-started,Point(first,directory.transform),Point(second,directory.transform));
                        store.SaveHouseholdJointProgress(motion.Snapshot());metres=0;lastProgressAt=Time.realtimeSinceStartup;
                    }
                    yield return null;
                }
                if(motion.Snapshot().Phase!=HouseholdJointPhase.Completed || !outboundArrived || !paused || !resumed || !missing || !commitFailure || metres<5)
                    throw new InvalidOperationException("Actual family bodies failed their route, pause, missing-partner or resume acceptance: metres="+metres+"; first="+motion.Snapshot().First.Segment+"; second="+motion.Snapshot().Second.Segment);
                if(geometry.UsesNativeNavigation && (!firstPrecise || !secondPrecise))
                    throw new InvalidOperationException("Actual family route did not observe both waypoint approaches.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_WAYPOINT_TOLERANCE_PASS: first="+firstPrecise+"; second="+secondPrecise+"; radius="+HouseholdJointMovement.ArrivalTolerance+"; physicalSteps=true");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_BODY_PASS: actualBodies=true; actualNativeRoute=true; talkingPausesBoth=true; missingPausesBoth=true; nativeSerializer=true; bothArrive=true; controlledEvent=true; liveFamily=false; purchases=false; unplacedHolds=true; failedCommitHolds=true; homeReturn=true; innEntry=false");
            }
            finally
            {
                TownSmokeHarness.Watch(null);
                if(first){first.gameObject.SetActive(false);UnityEngine.Object.Destroy(first.gameObject);}
                if(second){second.gameObject.SetActive(false);UnityEngine.Object.Destroy(second.gameObject);}
                foreach(var body in hidden)if(body)body.SetActive(true);
                host.enabled=hostEnabled;streets.enabled=streetEnabled;
                player.transform.position=originalPlayerPosition;
                SleepProbe.AimCamera(Camera.main.transform.position+originalLookDirection);
                for(int i=0;i<playerColliders.Length;i++)if(playerColliders[i])playerColliders[i].enabled=colliderEnabled[i];
                player.CancelMovement=playerCancelled;player.enabled=playerEnabled;
            }
        }
        private static IEnumerator PrepareRequest(WorldState snapshot,BuildingDirectory directory,TownGeometry geometry,
            int wake,int sleep,double now,PreparedRequest result,Func<bool> valid)
        {
            var population=snapshot.Settlements.Find(t=>t.SettlementId==directory.MapID);
            var routines=snapshot.Routines.Find(t=>t.SettlementId==directory.MapID);
            if(population==null || routines==null)throw new InvalidOperationException("No native household source.");
            int pairs=0,surveyed=0;string lastReason="no eligible parent pair";
            foreach(var household in population.Households)
            {
                TownGeometry.Entrance homeDoor;
                if(!geometry.Entrances.TryGetValue(household.BuildingKey,out homeDoor))continue;
                foreach(var child in household.Residents)
                {
                    if(!child.IsAlive || child.AgeGroup!=ResidentAgeGroup.Child)continue;
                    var first=household.Residents.Find(p=>p.Id==child.MotherId);
                    var second=household.Residents.Find(p=>p.Id==child.FatherId);
                    if(first==null || second==null || snapshot.ResidentTrips.Exists(t=>t.ResidentId==first.Id || t.ResidentId==second.Id))continue;
                    var firstRoutine=routines.Residents.Find(r=>r.ResidentId==first.Id);
                    var secondRoutine=routines.Residents.Find(r=>r.ResidentId==second.Id);
                    if(firstRoutine?.Itinerary==null || secondRoutine?.Itinerary==null)continue;
                    pairs++;
                    int day=(int)Math.Floor(now/86400d)+1,limit=day+1000;
                    while(day<limit && !HouseholdDayPlanner.SharedDay(snapshot.WorldSeed,directory.MapID,household,day))day++;
                    foreach(var entrance in geometry.Entrances.Values)
                    {
                        if(!valid())yield break;
                        BuildingSummary summary;DailyServiceWindow hours;
                        if(!directory.GetBuildingSummary(entrance.BuildingKey,out summary) || summary.BuildingType!=DFLocation.BuildingTypes.Tavern ||
                            !DailyServiceHours.TryFromNativeHours(PlayerActivate.openHours[(int)summary.BuildingType],
                                PlayerActivate.closeHours[(int)summary.BuildingType],out hours))continue;
                        var route=new TownGeometry.RouteResult();var survey=geometry.ReadRouteSteps(homeDoor,entrance,route,valid);
                        try{while(valid() && survey.MoveNext())yield return null;}finally{(survey as IDisposable)?.Dispose();}
                        if(!valid())yield break;
                        if(route.Points==null || route.Points.Count<2)continue;
                        surveyed++;
                        var lanes=new TownGeometry.HouseholdLaneResult();var laneSurvey=geometry.PrepareHouseholdLanes(route.Points,lanes,valid);
                        try{while(valid() && laneSurvey.MoveNext())yield return null;}finally{(laneSurvey as IDisposable)?.Dispose();}
                        if(!valid())yield break;
                        if(lanes.First==null || lanes.Second==null)continue;
                        var proposed=new HouseholdDayRequest{Household=household,SettlementId=directory.MapID,WorldSeed=snapshot.WorldSeed,
                            AbsoluteDay=day,WakeMinute=wake,SleepMinute=sleep,PlannedAt=day*86400d+wake*60d+60,
                            FirstRoutine=firstRoutine,SecondRoutine=secondRoutine,JointInn=new HouseholdJointInn{InnKey=entrance.BuildingKey,
                                First=Outing(first,entrance.BuildingKey,lanes.First,hours),Second=Outing(second,entrance.BuildingKey,lanes.Second,hours)}};
                        HouseholdDayPlan plan;
                        if(!HouseholdDayPlanner.TryCreate(proposed,out plan,out lastReason))continue;
                        result.Request=proposed;result.Plan=plan;
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_BODY_ROUTE_PREPARED: home="+household.BuildingKey+"; inn="+entrance.BuildingKey+
                            "; native="+geometry.UsesNativeNavigation+"; firstPoints="+lanes.First.Count+"; secondPoints="+lanes.Second.Count+"; savedRoutinesUnchanged=true");
                        yield break;
                    }
                }
            }
            Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_BODY_ROUTE_UNAVAILABLE: parentPairs="+pairs+"; surveyed="+surveyed+"; planner="+lastReason);
        }
        private static ResidentDailyOuting Outing(Resident parent,int inn,List<SavedRoutePoint> route,DailyServiceWindow hours)
        {
            double length=0;
            for(int i=1;i<route.Count;i++){double x=route[i].X-route[i-1].X,z=route[i].Z-route[i-1].Z;length+=Math.Sqrt(x*x+z*z);}
            double walking=length*DaggerfallUnity.Instance.WorldTime.TimeScale/ResidentMovement.WalkSpeedFor(parent.AppearanceSeed);
            var reverse=new List<SavedRoutePoint>(route);reverse.Reverse();
            return new ResidentDailyOuting{DepartureSeconds=16*3600,Itinerary=new ResidentDestination{
                Stops=new List<ResidentVisitStop>{new ResidentVisitStop{BuildingKey=inn,Purpose=ResidentVisitPurpose.Social,
                    OpenMinute=hours.OpenMinute,CloseMinute=hours.CloseMinute,WalkSeconds=walking,DwellSeconds=1800,Route=route}},
                ExitRoute=reverse,ExitWalkSeconds=walking}};
        }
        private static string NativeState(ResidentActor actor,Vector3 corner)
        {
            var native=actor.GetComponent<NativeResidentNavigation>();
            return "cornerGap="+Vector3.Distance(actor.transform.position-Vector3.up,corner).ToString("F5")+
                ",active="+(native && native.Active)+",hasDestination="+(native && native.HasDestination)+
                ",destination="+(native?native.Destination.ToString("F5"):"none")+",path="+(native?native.PathState:"none")+
                ",failure="+(native?native.FailureReason:"none");
        }
        private static Vector3 pairCameraDirection;
        private static void WatchPair(ResidentActor first,ResidentActor second,Vector3 destination,bool initialize=false)
        {
            var middle=(first.transform.position+second.transform.position)*0.5f;
            var forward=destination-(first.transform.position-Vector3.up);forward.y=0;
            if(forward.sqrMagnitude<0.25f)forward=pairCameraDirection.sqrMagnitude>0.5f?pairCameraDirection:Vector3.forward;
            if(initialize || pairCameraDirection.sqrMagnitude<0.5f)pairCameraDirection=forward.normalized;
            else pairCameraDirection=Vector3.RotateTowards(pairCameraDirection,forward.normalized,Mathf.PI*0.5f*Time.unscaledDeltaTime,0);
            var observer=GameManager.Instance.PlayerMotor.transform;
            var desired=middle-pairCameraDirection*4;
            observer.position=initialize?desired:Vector3.MoveTowards(observer.position,desired,6*Time.unscaledDeltaTime);
            TownSmokeHarness.Watch(first.transform,(second.transform.position-first.transform.position)*0.5f+Vector3.up*0.3f);
            SleepProbe.AimCamera(middle+Vector3.up*0.3f);
        }
        private static ResidentActor Create(Resident resident,Transform origin,GameManager game)
        {
            var body=UnityEngine.Object.Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject,origin);
            var actor=body.AddComponent<ResidentActor>();actor.Initialize(resident,1,game.StreamingWorld.GetCurrentCityNavigation());
            body.name="Controlled household body "+resident.Id;body.SetActive(true);return actor;
        }
        private static Vector3 Scene(TownGeometry geometry,SavedRoutePoint p){return geometry.ScenePoint(new RoutePoint(p.X,p.Z));}
        private static SavedRoutePoint Point(ResidentActor actor,Transform origin)
        {var p=origin.InverseTransformPoint(actor.transform.position-Vector3.up);return new SavedRoutePoint{X=p.x,Z=p.z};}
    }
}
