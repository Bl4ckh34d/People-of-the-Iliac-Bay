using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public static partial class NpcFollowTransitionProbe
    {
        private sealed class FixtureVisit
        {public string ResidentId;public int BuildingKey;public double EnterAt,LeaveAt;}
        private static double Now()
        {
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            double whole=clock.ToSeconds();
            double now=whole+(clock.Second-Math.Floor(clock.Second));
            // Epoch seconds exceed float precision. Keep the fractional operation
            // grouped before adding it, including in this native test clock.
            if(now<whole || now-whole>=1)
                throw new InvalidOperationException("Follow probe clock lost epoch precision: whole="+whole+"; now="+now);
            return now;
        }
        private static WorldStateStore Store(StreetPopulation streets)
        {return (WorldStateStore)typeof(StreetPopulation).GetField("worldStore",Private).GetValue(streets);}
        private static InteriorGeometry RoomGeometry(StreetPopulation streets)
        {return (InteriorGeometry)typeof(StreetPopulation).GetField("indoorGeometry",Private).GetValue(streets);}
        private static TownGeometry OutsideGeometry(StreetPopulation streets)
        {return (TownGeometry)typeof(StreetPopulation).GetField("geometry",Private).GetValue(streets);}

        // Called after TownSmokeHarness loads its caller-owned Daggerfall day save.
        // Only setup advances the fixture clock. Once FOLLOW is accepted, all walking
        // and both crossings run at the normal 2x clock with original consent.
        public static IEnumerator RunFixture(PeopleOfTheIliacBayMod host,StreetPopulation streets,string character)
        {
            var game=GameManager.Instance;
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-follow-transition-probe")<0 || !host || !streets ||
                string.IsNullOrEmpty(character) || !character.StartsWith("PeopleOfTheIliacBayTest-",StringComparison.Ordinal) ||
                game.PlayerEntity.Name!=character || game.PlayerEnterExit.IsPlayerInside)
                throw new InvalidOperationException("Follow fixture requires the loaded disposable Daggerfall exterior save and explicit flag.");
            if(Math.Abs(DaggerfallUnity.Instance.WorldTime.TimeScale-2)>0.001)
                throw new InvalidOperationException("Follow fixture requires the normal 2x clock.");
            VerifyDoorGroundProjection();
            float readyDeadline=Time.realtimeSinceStartup+90;
            while((!streets.Ready || !streets.EntranceSurveyReady) && Time.realtimeSinceStartup<readyDeadline)yield return null;
            if(!streets.Ready || !streets.EntranceSurveyReady)throw new InvalidOperationException("Follow fixture entrance survey timed out.");
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            var snapshot=(WorldState)host.GetSaveData();
            var town=snapshot.Routines.Find(t=>t.SettlementId==directory.MapID);
            var population=snapshot.Settlements.Find(t=>t.SettlementId==directory.MapID);
            if(town==null || population==null)throw new InvalidOperationException("Follow fixture lacks saved local routines.");
            var choices=new List<FixtureVisit>();
            foreach(var routine in town.Residents)
            {
                if(routine.Itinerary==null || snapshot.ResidentTrips.Exists(t=>t.ResidentId==routine.ResidentId))continue;
                Resident person=null;
                foreach(var household in population.Households)
                    foreach(var member in household.Residents)
                        if(member.Id==routine.ResidentId && member.IsAlive && !member.IsQuestProtected && member.NativeIdentity==null &&
                            member.AgeGroup==ResidentAgeGroup.Adult)person=member;
                if(person==null)continue;
                foreach(var visit in ResidentVisitJourney.Create(routine.ItineraryDepartureSeconds,routine.Itinerary).ScheduledVisits())
                {
                    BuildingSummary building;
                    if(visit.LeaveAt-visit.EnterAt<900 || visit.BuildingKey==person.HomeKey ||
                        !directory.GetBuildingSummary(visit.BuildingKey,out building) || building.BuildingType!=DFLocation.BuildingTypes.Tavern)continue;
                    choices.Add(new FixtureVisit {ResidentId=person.Id,BuildingKey=visit.BuildingKey,EnterAt=visit.EnterAt,LeaveAt=visit.LeaveAt});
                }
            }
            choices.Sort((a,b)=>{int preferred=(a.BuildingKey==263177?0:1).CompareTo(b.BuildingKey==263177?0:1);
                if(preferred!=0)return preferred;int time=a.EnterAt.CompareTo(b.EnterAt);return time!=0?time:string.CompareOrdinal(a.ResidentId,b.ResidentId);});
            if(choices.Count==0)throw new InvalidOperationException("No genuine saved optional adult inn visit has enough time for both doorways.");
            var selected=choices[0];DaggerfallStaticDoors owner=null;StaticDoor entry=default(StaticDoor);
            foreach(var candidate in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
            {
                foreach(var door in candidate.Doors??new StaticDoor[0])
                    if(door.doorType==DoorTypes.Building && door.buildingKey==selected.BuildingKey){owner=candidate;entry=door;break;}
                if(owner)break;
            }
            if(!owner)throw new InvalidOperationException("The saved inn has no native doorway.");
            var motor=game.PlayerMotor;var controller=motor.GetComponent<CharacterController>();
            bool motorEnabled=motor.enabled,controllerEnabled=controller && controller.enabled,godMode=game.PlayerEntity.GodMode;
            var crime=game.PlayerEntity.CrimeCommitted;
            // Doorway acceptance tests movement and dialogue ownership. Keep
            // unrelated voice preparation out of this existing owned fixture.
            var voice=typeof(PeopleOfTheIliacBayMod).GetField("breezeTtsTrial",Private);
            var oldVoice=voice.GetValue(host);voice.SetValue(host,null);
            motor.enabled=false;if(controller)controller.enabled=false;
            game.PlayerEntity.GodMode=true;game.PlayerEntity.CrimeCommitted=DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.None;
            try
            {
                var clock=DaggerfallUnity.Instance.WorldTime.Now;
                double start=(Math.Floor(Now()/86400)+1)*86400+selected.EnterAt+30;
                clock.RaiseTime((float)(start-Now()));
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FOLLOW_FIXTURE_START: resident="+selected.ResidentId+"; building="+selected.BuildingKey+
                    "; savedVisit="+selected.EnterAt+"-"+selected.LeaveAt+"; setupTime="+Now());
                int correctionBefore=NativeEntryCorrectionCount();
                TownSmokeHarness.EnterInterior(owner.transform,entry);
                yield return WaitForScene(streets,true);
                yield return WaitForNativeEntryCorrection(correctionBefore);
                float actorDeadline=Time.realtimeSinceStartup+25;ResidentActor actor=null;
                while(Time.realtimeSinceStartup<actorDeadline)
                {
                    // Saved overlapping patrons may already be seated. Select another
                    // actual standing adult from the same saved optional visit window;
                    // do not unseat, spawn, or reschedule an NPC for this test.
                    double timeOfDay=Now()%86400;
                    foreach(var candidate in choices)
                    {
                        if(candidate.BuildingKey!=selected.BuildingKey || candidate.EnterAt>timeOfDay || candidate.LeaveAt-timeOfDay<500)continue;
                        var existing=streets.FindActor(candidate.ResidentId);
                        if(existing && existing.gameObject.activeInHierarchy && !existing.IsSleeping && !existing.IsSeated)
                        {selected=candidate;actor=existing;break;}
                    }
                    if(actor && RoomGeometry(streets)!=null)break;
                    yield return null;
                }
                if(!actor || !actor.gameObject.activeInHierarchy || actor.IsSleeping || actor.IsSeated)
                    throw new InvalidOperationException("The saved patron did not naturally materialize standing: "+selected.ResidentId);
                var roomGeometry=RoomGeometry(streets);var sourceRoute=roomGeometry.RouteToExit(actor.transform.position-Vector3.up);
                if(sourceRoute.Count<2)throw new InvalidOperationException("Saved patron has no actual route to the inn entry.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FOLLOW_FIXTURE_PATRON: resident="+selected.ResidentId+"; ground="+(actor.transform.position-Vector3.up)+
                    "; entry="+roomGeometry.EntryGround+"; routePoints="+sourceRoute.Count+"; remainingVisit="+(selected.LeaveAt-Now()%86400));
                // Initial observer placement only; the resident is never repositioned.
                motor.transform.position=actor.transform.position;
                Physics.SyncTransforms();yield return null;
                yield return RunRequestedBuildingActions(host,streets,selected.ResidentId,owner,entry);
                actor=streets.FindActor(selected.ResidentId);
                if(!actor || !actor.gameObject.activeInHierarchy)
                    throw new InvalidOperationException("The requested doorway actions lost the ordinary patron.");
                motor.transform.position=actor.transform.position;Physics.SyncTransforms();yield return null;
                string followedActionId=null;double followFinishAt=0,followDeadlineAt=0;
                yield return RunDoorway(streets,selected.ResidentId,character,()=>ExitFixture(streets),false,
                    ()=>RecordFollowConversationAfterAcceptance(host,streets,selected.ResidentId,(id,finish,deadline)=>
                    {followedActionId=id;followFinishAt=finish;followDeadlineAt=deadline;}));
                if(string.IsNullOrEmpty(followedActionId))throw new InvalidOperationException("Pre-door native Talk did not capture the followed action identity.");
                yield return VerifyOutdoorFollowReentry(host,streets,selected.ResidentId,followedActionId,followFinishAt,followDeadlineAt);
                yield return RunDoorway(streets,selected.ResidentId,character,()=>EnterFixture(streets,owner,entry),true,
                    requiredActionId:followedActionId,requiredFinishAt:followFinishAt,requiredDeadlineAt:followDeadlineAt);
                yield return ObserveRoutineRecovery(streets,selected.ResidentId);
                yield return RunUnplannedBuildingReturn(streets,selected.ResidentId,character);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FOLLOW_FIXTURE_PASS: genuine-saved-patron production-acceptance both-native-doors identity inventory native-restore timed-arrival stop physical-recovery");
            }
            finally
            {
                voice.SetValue(host,oldVoice);
                if(controller)controller.enabled=controllerEnabled;motor.enabled=motorEnabled;
                game.PlayerEntity.GodMode=godMode;game.PlayerEntity.CrimeCommitted=crime;
            }
        }
        private static void VerifyDoorGroundProjection()
        {
            var door=new StaticDoor {ownerPosition=new Vector3(7,10,11),ownerRotation=Quaternion.identity,
                buildingMatrix=Matrix4x4.TRS(new Vector3(2,3,4),Quaternion.identity,Vector3.one),
                centre=new Vector3(1,1.5f,2),size=new Vector3(1,3,0.2f),normal=Vector3.forward};
            var point=StreetPopulation.FollowExteriorDoorGround(door);
            if(Vector3.Distance(point,new Vector3(10,13,17.6f))>0.001f)
                throw new InvalidOperationException("Native doorway route used door centre instead of its transformed lower edge.");
        }
        private static IEnumerator WaitForScene(StreetPopulation streets,bool inside)
        {
            var game=GameManager.Instance;float deadline=Time.realtimeSinceStartup+30;
            do
            {
                DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);yield return null;
                var room=(DaggerfallInterior)typeof(StreetPopulation).GetField("interior",Private).GetValue(streets);
                if(streets.Ready && game.PlayerEnterExit.IsPlayerInsideBuilding==inside &&
                    (inside?room && ReferenceEquals(room,game.PlayerEnterExit.Interior) && RoomGeometry(streets)!=null:!room))yield break;
            }while(Time.realtimeSinceStartup<deadline);
            throw new InvalidOperationException("Follow fixture scene synchronization timed out.");
        }
        private static IEnumerator WalkObserver(IList<Vector3> route)
        {
            if(route==null || route.Count<2)throw new InvalidOperationException("Observer needs a surveyed walking route.");
            var game=GameManager.Instance;float deadline=Time.realtimeSinceStartup+90;
            for(int i=1;i<route.Count;i++)
            {
                while(Vector3.Distance(game.PlayerMotor.transform.position-Vector3.up,route[i])>0.03f)
                {
                    if(Time.realtimeSinceStartup>=deadline)throw new InvalidOperationException("Observer walking route timed out.");
                    DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
                    var ground=game.PlayerMotor.transform.position-Vector3.up;
                    game.PlayerMotor.transform.position=Vector3.MoveTowards(ground,route[i],1.25f*Math.Min(Time.unscaledDeltaTime,0.05f))+Vector3.up;
                    Physics.SyncTransforms();yield return null;
                }
            }
        }
        private static int NativeEntryCorrectionCount()
        {
            var hooks=Type.GetType("PeopleOfTheIliacBay.QuestHooks.RosyInteriorCompatibilityHooks, PeopleOfTheIliacBay.QuestHooks",false);
            return hooks!=null && (bool)hooks.GetProperty("Installed").GetValue(null,null)?
                (int)hooks.GetProperty("Completed").GetValue(null,null):-1;
        }
        private static IEnumerator WaitForNativeEntryCorrection(int before)
        {
            if(before<0)yield break;
            var game=GameManager.Instance;var room=game.PlayerEnterExit.Interior;
            var previous=game.PlayerMotor.transform.position;float stableSince=Time.realtimeSinceStartup;
            float until=stableSince+15;
            while(Time.realtimeSinceStartup<until)
            {
                yield return null;
                if(!game.PlayerEnterExit.IsPlayerInsideBuilding || game.PlayerEnterExit.Interior!=room)
                    throw new InvalidOperationException("Native entry changed rooms before observer walking.");
                var position=game.PlayerMotor.transform.position;
                if(Vector3.Distance(previous,position)>0.01f)stableSince=Time.realtimeSinceStartup;
                previous=position;
                // Cached/unsupported interiors need not invoke the optional correction.
                // Wait for its completion or a stable actual entry, never its installed flag alone.
                if(NativeEntryCorrectionCount()>before || Time.realtimeSinceStartup-stableSince>=2)yield break;
            }
            throw new InvalidOperationException("Native interior entry did not settle before observer walking.");
        }
        private static List<Vector3> IndoorWorldRoute(DaggerfallInterior room,IList<GuardPoint3> route)
        {
            var result=new List<Vector3>();
            foreach(var point in route)result.Add(room.transform.TransformPoint(new Vector3((float)point.X,(float)point.Y,(float)point.Z)));
            return result;
        }
        private static IEnumerator ExitFixture(StreetPopulation streets)
        {
            var game=GameManager.Instance;var room=game.PlayerEnterExit.Interior;var geometry=RoomGeometry(streets);
            yield return WalkObserver(IndoorWorldRoute(room,geometry.RouteToExit(game.PlayerMotor.transform.position-Vector3.up)));
            var entry=room.EntryDoor;
            game.PlayerEnterExit.TransitionExterior();yield return WaitForScene(streets,false);
            var town=(BuildingDirectory)typeof(StreetPopulation).GetField("directory",Private).GetValue(streets);
            var outside=OutsideGeometry(streets);var normal=DaggerfallStaticDoors.GetDoorNormal(entry);normal.y=0;
            var from=town.transform.InverseTransformPoint(game.PlayerMotor.transform.position-Vector3.up);
            List<SavedRoutePoint> route=null;
            foreach(float distance in new[]{3f,4f,2.5f})
            {
                var target=DaggerfallStaticDoors.GetDoorPosition(entry)+normal.normalized*distance;
                var to=town.transform.InverseTransformPoint(target);
                route=outside.RouteBetweenPoints(new SavedRoutePoint{X=from.x,Z=from.z},new SavedRoutePoint{X=to.x,Z=to.z});
                if(route.Count>=2)break;
            }
            var world=new List<Vector3>();foreach(var point in route??new List<SavedRoutePoint>())world.Add(outside.ScenePoint(new RoutePoint(point.X,point.Z)));
            yield return WalkObserver(world);
        }
        private static IEnumerator EnterFixture(StreetPopulation streets,DaggerfallStaticDoors owner,StaticDoor entry)
        {
            var game=GameManager.Instance;var town=(BuildingDirectory)typeof(StreetPopulation).GetField("directory",Private).GetValue(streets);
            // Raw owner.Doors records do not carry a current world transform.
            // Match the native transition's enrichment before using static helpers.
            entry.ownerPosition=owner.transform.position;entry.ownerRotation=owner.transform.rotation;
            var outside=OutsideGeometry(streets);var normal=DaggerfallStaticDoors.GetDoorNormal(entry);normal.y=0;
            var from=town.transform.InverseTransformPoint(game.PlayerMotor.transform.position-Vector3.up);
            var to=town.transform.InverseTransformPoint(DaggerfallStaticDoors.GetDoorPosition(entry)+normal.normalized*0.8f);
            var route=outside.RouteBetweenPoints(new SavedRoutePoint{X=from.x,Z=from.z},new SavedRoutePoint{X=to.x,Z=to.z});
            var world=new List<Vector3>();foreach(var point in route)world.Add(outside.ScenePoint(new RoutePoint(point.X,point.Z)));
            yield return WalkObserver(world);
            int correctionBefore=NativeEntryCorrectionCount();
            TownSmokeHarness.EnterInterior(owner.transform,entry);yield return WaitForScene(streets,true);
            yield return WaitForNativeEntryCorrection(correctionBefore);
            var room=game.PlayerEnterExit.Interior;var geometry=RoomGeometry(streets);
            float surveyDeadline=Time.realtimeSinceStartup+20;
            while(!geometry.IsComplete && Time.realtimeSinceStartup<surveyDeadline)yield return null;
            if(!geometry.EntryGround.HasValue)throw new InvalidOperationException("No surveyed interior landing.");
            // The observer's controller is disabled. Re-anchor its one-metre
            // test offset to the surveyed landing after native entry correction.
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FOLLOW_OBSERVER_LANDING: nativeGround="+
                (game.PlayerMotor.transform.position-Vector3.up)+"; surveyedGround="+geometry.EntryGround.Value);
            game.PlayerMotor.transform.position=geometry.EntryGround.Value+Vector3.up;
            Physics.SyncTransforms();
            var start=room.transform.InverseTransformPoint(game.PlayerMotor.transform.position-Vector3.up);
            List<GuardPoint3> inner=null;
            var observerAnchors=new List<Vector3>(geometry.LocalAnchors);
            observerAnchors.Sort((a,b)=>(a-start).sqrMagnitude.CompareTo((b-start).sqrMagnitude));
            foreach(var candidate in observerAnchors)
            {
                float gap=Vector3.Distance(candidate,start);
                if(gap<2.5f || Math.Abs(candidate.y-start.y)>0.4f)continue;
                inner=geometry.RouteBetween(start,candidate);if(inner.Count>=2)break;
            }
            if(inner==null || inner.Count<2)throw new InvalidOperationException("No reachable nearby inn observer anchor clears the landing; local="+
                start+"; anchors="+geometry.LocalAnchors.Count+"; surveyed="+geometry.IsComplete);
            yield return WalkObserver(IndoorWorldRoute(room,inner));
        }
        private static IEnumerator ObserveRoutineRecovery(StreetPopulation streets,string residentId)
        {
            var store=Store(streets);float deadline=Time.realtimeSinceStartup+120;
            ResidentActor previous=null;Vector3 prior=Vector3.zero;bool released=false;int afterRelease=0;float moved=0;
            while(Time.realtimeSinceStartup<deadline)
            {
                var actor=streets.FindActor(residentId);var job=store.ActiveNpcAction(residentId);
                if(actor && actor.gameObject.activeInHierarchy)
                {
                    if(previous && previous!=actor)throw new InvalidOperationException("Routine recovery replaced the observed follower body.");
                    var ground=actor.transform.position-Vector3.up;
                    if(previous)
                    {float step=Vector3.Distance(prior,ground);if(step>1.5f)throw new InvalidOperationException("Routine recovery teleported "+step+" metres.");moved+=step;}
                    previous=actor;prior=ground;
                }
                if(job==null){released=true;if(++afterRelease>=30)break;}
                else if(job.Phase!=NpcActionPhase.Cancelled || !job.ReturningToRoutine)
                    throw new InvalidOperationException("Stopped follower lost its routine recovery contract.");
                yield return null;
            }
            if(!released)throw new InvalidOperationException("Stopped follower did not physically rejoin its ordinary routine within 120 seconds.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FOLLOW_RECOVERY_PASS: moved="+moved+"; action ownership released; observed body retained");
        }
    }
}
