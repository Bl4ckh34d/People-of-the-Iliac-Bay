using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Opt-in acceptance: follow one existing composed day through every outing.
    // Only stationary dwell/opening waits are skipped; walking uses the 2x clock.
    public static class PhysicalDailyRoutineProbe
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private const string OwnedActionCharacter="PeopleOfTheIliacBayTest-9a5f197b";
        private const string OwnedActionSave="People - Gothway Garden";
        private static double Now()
        {var clock=DaggerfallUnity.Instance.WorldTime.Now;return (double)clock.ToSeconds()+clock.Second-Math.Floor(clock.Second);}
        public static IEnumerator Run(PeopleOfTheIliacBayMod host,StreetPopulation streets)
        {
            bool actionProbe=Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-action-probe")>=0;
            string fixtureCharacter=null,fixtureSave=null;
            if(actionProbe)
            {
                string[] arguments=Environment.GetCommandLineArgs();
                fixtureCharacter=ArgumentValue(arguments,"-peopleoftheiliacbay-fixture-character");
                fixtureSave=ArgumentValue(arguments,"-peopleoftheiliacbay-fixture-save");
                if(!string.Equals(GameManager.Instance.PlayerEntity.Name,OwnedActionCharacter,StringComparison.Ordinal) ||
                    !string.Equals(fixtureCharacter,OwnedActionCharacter,StringComparison.Ordinal) ||
                    !string.Equals(fixtureSave,OwnedActionSave,StringComparison.Ordinal))
                    throw new InvalidOperationException("Physical action probe requires the exact owned character and explicit baseline save: "+
                        OwnedActionCharacter+" / "+OwnedActionSave+".");
                GameManager.Instance.SaveLoadManager.EnumerateSaves();
                if(GameManager.Instance.SaveLoadManager.FindSaveFolderByNames(fixtureCharacter,fixtureSave)<0)
                    throw new InvalidOperationException("Physical action probe baseline save is missing before mutation: "+fixtureSave+".");
            }
            // This process is a camera observer, not a person in the crowd.
            // Otherwise the scripted camera can create the very doorway stalls
            // it is trying to measure. Restore native components on completion.
            var motor=GameManager.Instance.PlayerMotor;
            var controller=motor.GetComponent<CharacterController>();
            bool motorEnabled=motor.enabled,controllerEnabled=controller && controller.enabled;
            motor.enabled=false;if(controller) controller.enabled=false;
            if(!actionProbe)
            {
                try { yield return ObserveDay(host,streets); }
                finally { if(controller) controller.enabled=controllerEnabled;motor.enabled=motorEnabled; }
                yield break;
            }

            Exception probeFailure=null,cleanupFailure=null;
            var observation=new NestedEnumeratorDriver(ObserveDay(host,streets));
            try
            {
                object current;
                while(observation.TryMoveNext(out current)) yield return current;
                probeFailure=observation.Failure;
            }
            finally
            {
                observation.Dispose();
                if(controller) controller.enabled=controllerEnabled;
                motor.enabled=motorEnabled;
                try { ConversationMoodProbe.CancelTransport(host); }
                catch(Exception error) { AppendFailure(ref cleanupFailure,error); }
            }

            var closeTalk=new NestedEnumeratorDriver(ConversationMoodProbe.CloseTalk(host));
            try
            {
                object current;
                while(closeTalk.TryMoveNext(out current)) yield return current;
            }
            finally { closeTalk.Dispose(); }
            AppendFailure(ref cleanupFailure,closeTalk.Failure);

            // Reload the exact, pre-existing fixture only after the observed action
            // has completed or failed and all native dialogue work has been closed.
            var restore=new NestedEnumeratorDriver(ConversationMoodProbe.LoadSave(host,streets,
                fixtureCharacter,fixtureSave,true));
            try
            {
                object current;
                while(restore.TryMoveNext(out current)) yield return current;
            }
            finally { restore.Dispose(); }
            AppendFailure(ref cleanupFailure,restore.Failure);
            if(restore.Failure==null)
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PHYSICAL_ACTION_BASELINE_RESTORED: character="+fixtureCharacter+"; save="+fixtureSave+".");
            else
                Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_PHYSICAL_ACTION_BASELINE_RESTORE_FAILED: character="+fixtureCharacter+
                    "; save="+fixtureSave+"; error="+restore.Failure);
            if(cleanupFailure!=null)
                Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_PHYSICAL_ACTION_CLEANUP_FAILED: "+cleanupFailure);

            if(probeFailure!=null && cleanupFailure!=null) throw new AggregateException("Physical action probe failed and baseline cleanup also failed.",probeFailure,cleanupFailure);
            if(probeFailure!=null) throw probeFailure;
            if(cleanupFailure!=null) throw cleanupFailure;
        }

        private static string ArgumentValue(string[] arguments,string name)
        {
            int index=Array.IndexOf(arguments,name);
            return index>=0 && index+1<arguments.Length ? arguments[index+1] : null;
        }

        private static void AppendFailure(ref Exception current,Exception next)
        {
            if(next==null)return;
            current=current==null?next:new AggregateException(current,next);
        }

        // Unity drives yielded child iterators outside the parent's MoveNext call,
        // so a parent try/catch cannot catch their failures. Keep an explicit stack
        // to catch errors at every depth and dispose each iterator's finally block.
        private sealed class NestedEnumeratorDriver : IDisposable
        {
            private readonly Stack<IEnumerator> stack=new Stack<IEnumerator>();
            private bool complete;
            public Exception Failure { get; private set; }

            public NestedEnumeratorDriver(IEnumerator root)
            {
                if(root==null)throw new ArgumentNullException("root");
                stack.Push(root);
            }

            public bool TryMoveNext(out object yielded)
            {
                yielded=null;
                if(complete || Failure!=null)return false;
                while(stack.Count>0)
                {
                    IEnumerator top=stack.Peek();bool more;object current=null;
                    try
                    {
                        more=top.MoveNext();
                        if(more)current=top.Current;
                    }
                    catch(Exception error)
                    {
                        SetFailure(error);return false;
                    }
                    if(!more)
                    {
                        stack.Pop();
                        try { (top as IDisposable)?.Dispose(); }
                        catch(Exception error) { SetFailure(error);return false; }
                        continue;
                    }
                    IEnumerator nested=current as IEnumerator;
                    if(nested!=null){stack.Push(nested);continue;}
                    yielded=current;return true;
                }
                complete=true;return false;
            }

            private void SetFailure(Exception error)
            {
                Failure=CombineFailures(Failure,error);
                DisposeStack();
            }

            private void DisposeStack()
            {
                while(stack.Count>0)
                {
                    IEnumerator pending=stack.Pop();
                    try { (pending as IDisposable)?.Dispose(); }
                    catch(Exception error) { Failure=CombineFailures(Failure,error); }
                }
            }

            public void Dispose()
            {
                DisposeStack();complete=true;
            }
        }

        private static Exception CombineFailures(Exception current,Exception next)
        {
            if(next==null)return current;
            return current==null?next:new AggregateException(current,next);
        }
        private static IEnumerator ObserveDay(PeopleOfTheIliacBayMod host,StreetPopulation streets)
        {
            bool seatTaskProbe=Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-action-probe")>=0 &&
                Environment.GetEnvironmentVariable("POTIB_ROADMAP_PROBE")=="seat-task";
            var game=GameManager.Instance;
            game.PlayerEntity.GodMode=true;
            game.PlayerEntity.CrimeCommitted=DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.None;
            float entranceDeadline=Time.realtimeSinceStartup+90;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PHYSICAL_DAY_PREPARE: waiting for entrance survey only.");
            while(!streets.EntranceSurveyReady&&Time.realtimeSinceStartup<entranceDeadline)yield return null;
            if(!streets.EntranceSurveyReady)throw new InvalidOperationException("Physical daily probe entrance survey timed out.");
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            var geometry=(TownGeometry)typeof(StreetPopulation).GetField("geometry",Private).GetValue(streets);
            SleepCoverageProbe.Verify(streets);
            var saved=(WorldState)host.GetSaveData();
            var town=saved.Routines.Find(t=>t.SettlementId==directory.MapID);
            var population=saved.Settlements.Find(t=>t.SettlementId==directory.MapID);
            Resident person=null; ResidentRoutine routine=null; double best=double.MaxValue;
            int seatBuilding=0;
            bool firstStopOnly=Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-daily-interior-only")>=0;
            int focusedBuilding;
            int.TryParse(Environment.GetEnvironmentVariable("POTIB_DAILY_BUILDING"),out focusedBuilding);
            // This probe selects an actual scheduled tavern in the loaded town.
            // A key from another saved town must not filter out every resident.
            if(seatTaskProbe)focusedBuilding=0;
            string focusedResident=Environment.GetEnvironmentVariable("POTIB_DAILY_RESIDENT");
            if(focusedBuilding>0 && !firstStopOnly)throw new InvalidOperationException("A focused building requires the interior-only probe.");
            foreach(var candidate in town.Residents)
            {
                if(candidate.Itinerary==null || !seatTaskProbe && firstStopOnly && focusedBuilding<=0 && candidate.Itinerary.Stops.Count<2 ||
                    focusedBuilding>0 && !string.IsNullOrEmpty(focusedResident) && candidate.ResidentId!=focusedResident) continue;
                var candidateOutings=!seatTaskProbe && firstStopOnly && focusedBuilding<=0 ? new List<ResidentDailyOuting>{new ResidentDailyOuting {
                    DepartureSeconds=candidate.ItineraryDepartureSeconds,Itinerary=candidate.Itinerary}} : ResidentRoutineDay.CopyOutings(candidate);
                if(focusedBuilding>0)candidateOutings.RemoveAll(o=>o.Itinerary.Stops.Count==0 || o.Itinerary.Stops[0].BuildingKey!=focusedBuilding);
                if(candidateOutings.Count==0)continue;
                ResidentDailyOuting seatOuting=null;int candidateSeatBuilding=0;
                if(seatTaskProbe)
                {
                    foreach(var outing in candidateOutings)
                    {
                        foreach(var stop in outing.Itinerary.Stops)
                        {
                            BuildingSummary tavern;
                            if(stop.Purpose!=ResidentVisitPurpose.Social ||
                                !directory.GetBuildingSummary(stop.BuildingKey,out tavern) ||
                                tavern.BuildingType!=DaggerfallConnect.DFLocation.BuildingTypes.Tavern)continue;
                            seatOuting=outing;candidateSeatBuilding=stop.BuildingKey;break;
                        }
                        if(seatOuting!=null)break;
                    }
                    if(seatOuting==null)continue;
                }
                bool shop=false,inn=false;
                int stopCount=0;double metres=0;
                foreach(var outing in candidateOutings)
                {
                    metres+=Length(outing.Itinerary.ExitRoute);
                    foreach(var stop in outing.Itinerary.Stops)
                    {
                        stopCount++;metres+=Length(stop.Route);
                        shop|=stop.Purpose==ResidentVisitPurpose.Shop;inn|=stop.Purpose==ResidentVisitPurpose.Social || stop.Purpose==ResidentVisitPurpose.Inn;
                    }
                }
                if(!seatTaskProbe && focusedBuilding<=0 && (stopCount<2 || !shop || !inn) ||
                    saved.ResidentTrips.Exists(t=>t.ResidentId==candidate.ResidentId)) continue;
                Resident adult=null;
                foreach(var home in population.Households)
                {
                    // This observer follows an ordinary solo day. Dated parent
                    // plans and shared outings have their separate native probes.
                    if(!firstStopOnly && home.Residents.Exists(p=>p.IsAlive && p.AgeGroup==ResidentAgeGroup.Child))continue;
                    foreach(var member in home.Residents)
                        if(member.Id==candidate.ResidentId && member.IsAlive && member.AgeGroup==ResidentAgeGroup.Adult &&
                            member.NativeIdentity==null && !member.IsQuestProtected) adult=member;
                }
                if(adult==null || !geometry.Entrances.ContainsKey(adult.HomeKey)) continue;
                if(metres>=best) continue;
                best=metres;person=adult;routine=candidate;
                if(seatTaskProbe)
                {
                    routine=new ResidentRoutine {ResidentId=candidate.ResidentId,
                        Itinerary=seatOuting.Itinerary,ItineraryDepartureSeconds=seatOuting.DepartureSeconds};
                    seatBuilding=candidateSeatBuilding;
                }
                // Select an existing scheduled outing for a focused replay;
                // the resident's saved/native schedule is never rewritten.
                if(!seatTaskProbe && focusedBuilding>0)routine=new ResidentRoutine {ResidentId=candidate.ResidentId,
                    Itinerary=candidateOutings[0].Itinerary,ItineraryDepartureSeconds=candidateOutings[0].DepartureSeconds};
            }
            if(person==null) throw new InvalidOperationException(seatTaskProbe?
                "No saved adult has a scheduled visit to a native tavern in this town.":
                "No saved local routine covers both shop and inn.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PHYSICAL_DAY_START: resident="+person.Id+"; name="+person.FullName+
                "; home="+person.HomeKey+"; stops="+routine.Itinerary.Stops.Count+"; surveyedMetres="+best+
                (seatTaskProbe?"; scheduledTavern="+seatBuilding:""));
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            if(Math.Abs(DaggerfallUnity.Instance.WorldTime.TimeScale-2)>0.001)
                throw new InvalidOperationException("Physical daily acceptance requires the normal 2x world clock.");
            double day=Math.Floor(clock.ToSeconds()/86400d)*86400d+86400;
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-daily-interior-only")>=0)
            {
                var visits=ResidentVisitJourney.Create(routine.ItineraryDepartureSeconds,routine.Itinerary);
                foreach(var visit in visits.ScheduledVisits())
                    if(visit.BuildingKey==(seatTaskProbe?seatBuilding:routine.Itinerary.Stops[0].BuildingKey))
                    {
                        double sample=visit.EnterAt+1;
                        if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-action-probe")>=0 && !seatTaskProbe &&
                            Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-beer-probe")<0)
                        {
                            // Delivery requires two real scheduled patrons. Choose a saved
                            // overlap before entering, rather than manufacturing a recipient.
                            double overlap=double.MaxValue;
                            foreach(var other in town.Residents)
                            {
                                if(other.ResidentId==person.Id || other.Itinerary==null || saved.ResidentTrips.Exists(t=>t.ResidentId==other.ResidentId)) continue;
                                foreach(var otherVisit in ResidentVisitJourney.Create(other.ItineraryDepartureSeconds,other.Itinerary).ScheduledVisits())
                                {
                                    double start=Math.Max(visit.EnterAt,otherVisit.EnterAt)+30;
                                    if(otherVisit.BuildingKey==visit.BuildingKey && start+600<Math.Min(visit.LeaveAt,otherVisit.LeaveAt)) overlap=Math.Min(overlap,start);
                                }
                            }
                            if(overlap==double.MaxValue) throw new InvalidOperationException("Action fixture needs two overlapping saved visits at this inn.");
                            sample=overlap;
                            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_OVERLAP: building="+visit.BuildingKey+"; seconds="+sample);
                        }
                        clock.RaiseTime((float)(day+sample-clock.ToSeconds()));break;
                    }
                bool focusedAction=Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-action-probe")>=0;
                Type rosyHooks=focusedAction?Type.GetType(
                    "PeopleOfTheIliacBay.QuestHooks.RosyInteriorCompatibilityHooks, PeopleOfTheIliacBay.QuestHooks",false):null;
                bool rosyInstalled=rosyHooks!=null && (bool)rosyHooks.GetProperty("Installed").GetValue(null,null);
                int rosyCorrectionsBefore=rosyInstalled?(int)rosyHooks.GetProperty("Completed").GetValue(null,null):0;
                yield return Enter(directory,seatTaskProbe?seatBuilding:routine.Itinerary.Stops[0].BuildingKey);
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-action-probe")>=0)
                {
                    yield return WaitForActor(streets,person.Id,20);
                    if(rosyInstalled)
                    {
                        float correctionDeadline=Time.realtimeSinceStartup+15;
                        while((int)rosyHooks.GetProperty("Completed").GetValue(null,null)<=rosyCorrectionsBefore &&
                            Time.realtimeSinceStartup<correctionDeadline)
                        {
                            if(!streets.Ready || !game.PlayerEnterExit.Interior ||
                                game.PlayerEnterExit.Interior.EntryDoor.buildingKey!=(seatTaskProbe?seatBuilding:routine.Itinerary.Stops[0].BuildingKey) ||
                                !streets.FindActor(person.Id))
                                throw new InvalidOperationException("Action fixture changed scene or lost its resident before the native entry correction settled.");
                            yield return null;
                        }
                        if((int)rosyHooks.GetProperty("Completed").GetValue(null,null)<=rosyCorrectionsBefore)
                            throw new InvalidOperationException("Rosy's native interior correction did not settle before action observer placement.");
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ACTION_ENTRY_SETTLED: native correction completed before observer placement.");
                    }
                    if(seatTaskProbe) yield return SeatTaskChainProbe.Run(streets,game.PlayerEntity.Name,person.Id);
                    else yield return NpcActionRuntimeProbe.RunFixture(streets,game.PlayerEntity.Name);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PHYSICAL_DAY_INTERIOR_PASS: focused action diagnostic only.");
                    yield break;
                }
                yield return VisitInterior(host,streets,person,routine.Itinerary.Stops[0].BuildingKey,routine.Itinerary.Stops[0].Purpose);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PHYSICAL_DAY_INTERIOR_PASS: focused first-stop diagnostic only.");
                yield break;
            }
            // Let the restored routine prepare outside while this resident is
            // still at home. Read back the actual published saved geometry.
            clock.RaiseTime((float)(day+5*3600-clock.ToSeconds()));
            var routineStore=(WorldStateStore)typeof(StreetPopulation).GetField("worldStore",Private).GetValue(streets);
            float migrationDeadline=Time.realtimeSinceStartup+360;
            while(Time.realtimeSinceStartup<migrationDeadline)
            {
                var published=routineStore.GetResidentDialogueRoutine(directory.MapID,person.Id);
                if(published!=null && published.RouteGeometryVersion==OutdoorRoutineMigration.CurrentVersion &&
                    published.PersonalLaneVersion==OutdoorRoutineMigration.CurrentPersonalLaneVersion)
                {routine=published;break;}
                yield return null;
            }
            if(routine.RouteGeometryVersion!=OutdoorRoutineMigration.CurrentVersion ||
                routine.PersonalLaneVersion!=OutdoorRoutineMigration.CurrentPersonalLaneVersion)
                throw new InvalidOperationException("Saved daily route migration did not publish at stationary home boundary.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PHYSICAL_DAY_SAVED_GEOMETRY: resident="+person.Id+"; geometryVersion="+routine.RouteGeometryVersion+
                "; personalLaneVersion="+routine.PersonalLaneVersion+"; firstLegPoints="+routine.Itinerary.Stops[0].Route.Count);
            int completedStops=0,completedOutings=0;
            foreach(var outing in ResidentRoutineDay.CopyOutings(routine))
            {
                double beforeDeparture=day+outing.DepartureSeconds-60;
                if(beforeDeparture>clock.ToSeconds())clock.RaiseTime((float)(beforeDeparture-clock.ToSeconds()));
                if(!game.PlayerEnterExit.IsPlayerInsideBuilding || game.PlayerEnterExit.Interior.EntryDoor.buildingKey!=person.HomeKey)
                    yield return Enter(directory,person.HomeKey);
                yield return WaitForActor(streets,person.Id,15);
                var atHome=streets.GetState(person.Id,Now());
                if(atHome.Activity!=Activity.Home || atHome.BuildingKey!=person.HomeKey)
                    throw new InvalidOperationException("Daily outing does not start at the resident's home.");
                yield return Exit(streets);
                var homePoint=geometry.SceneDoor(geometry.Entrances[person.HomeKey]);
                game.PlayerMotor.transform.position=homePoint+Vector3.up+Vector3.right*3;
                double departure=day+outing.DepartureSeconds+0.1;
                if(departure>clock.ToSeconds())clock.RaiseTime((float)(departure-clock.ToSeconds()));
                foreach(var stop in outing.Itinerary.Stops)
                {
                    yield return WalkOutside(streets,geometry,person,stop.BuildingKey,stop.Route);
                    if(stop.Purpose==ResidentVisitPurpose.Social && Now()-day<16*3600)
                        throw new InvalidOperationException("Physical local Inn visit began before the evening.");
                    yield return Enter(directory,stop.BuildingKey);
                    yield return VisitInterior(host,streets,person,stop.BuildingKey,stop.Purpose);
                    yield return Exit(streets);completedStops++;
                }
                yield return WalkOutside(streets,geometry,person,person.HomeKey,outing.Itinerary.ExitRoute);
                yield return Enter(directory,person.HomeKey);
                yield return WaitForActor(streets,person.Id,15);
                var returned=streets.GetState(person.Id,Now());
                if(returned.BuildingKey!=person.HomeKey || returned.Activity!=Activity.Home && returned.Activity!=Activity.Sleep)
                    throw new InvalidOperationException("Daily routine failed to return home after an outing.");
                completedOutings++;
            }
            var beforeRestore=(WorldState)host.GetSaveData();
            var roundTrip=(WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),beforeRestore));
            OutdoorNavigationProbe.VerifyPersonalGeometryRestore(beforeRestore,roundTrip,directory.MapID,person.Id);
            host.RestoreSaveData(roundTrip);
            yield return WaitForActor(streets,person.Id,20);
            var restored=streets.FindActor(person.Id);
            if(restored.Resident.FullName!=person.FullName || restored.Resident.HomeKey!=person.HomeKey || Count(person.Id)!=1)
                throw new InvalidOperationException("Daily routine restore changed identity or duplicated the resident.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PHYSICAL_DAY_PASS: resident="+person.Id+"; stops="+completedStops+"; outings="+completedOutings+
                "; home/shop/inn/home actual doorway arrivals; real interior activities/exits; normal walking; 2x clock; identity/native restore=pass; player saves untouched.");
        }
        private static IEnumerator WalkOutside(StreetPopulation streets,TownGeometry geometry,Resident person,int building,List<SavedRoutePoint> route)
        {
            if(route==null || route.Count<2) throw new InvalidOperationException("Daily leg lacks a saved route.");
            var verify=geometry.VerifySavedOutdoorRouteSteps(route);
            while(verify.MoveNext()) yield return null;
            var navigationWitness=new OutdoorNavigationProbe.SavedLegWitness(person.Id,geometry,route);
            var game=GameManager.Instance;var clock=DaggerfallUnity.Instance.WorldTime.Now;
            var entrance=geometry.Entrances[building];
            var door=geometry.ScenePoint(new RoutePoint(entrance.LocalDoor.x,entrance.LocalDoor.z));
            ResidentActor tracked=null;Vector3 previous=Vector3.zero,last=Vector3.zero;
            float travelled=0,maxStep=0;int frames=0;bool vanished=false,seen=false;
            float started=Time.realtimeSinceStartup,deadline=started+(float)(Length(route)/0.7+90),nextTrace=started+20;
            double gameStart=Now();
            while(Time.realtimeSinceStartup<deadline)
            {
                DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
                var actor=streets.FindActor(person.Id);
                if(actor && actor.gameObject.activeInHierarchy)
                {
                    if(seen && tracked!=actor) throw new InvalidOperationException("Outdoor daily leg replaced an observed body.");
                    var ground=actor.transform.position-Vector3.up;
                    navigationWitness.Record(ground);
                    if(seen)
                    {
                        float step=Vector3.Distance(previous,ground);maxStep=Math.Max(maxStep,step);travelled+=step;
                        if(step>1.5f) throw new InvalidOperationException("Outdoor daily leg teleported "+step+"m.");
                    }
                    tracked=actor;seen=true;previous=last=ground;frames++;
                    FollowCamera(actor,ground);
                    if(Vector3.Distance(ground,door)<0.55f)
                    {
                        var waiting=streets.GetState(person.Id,Now());
                        if(waiting.Activity==Activity.Travel)
                        {
                            var store=(WorldStateStore)typeof(StreetPopulation).GetField("worldStore",Private).GetValue(streets);
                            var routine=store.GetResidentDialogueRoutine(game.PlayerGPS.CurrentLocation.MapTableData.MapId,person.Id);
                            foreach(var visit in ResidentRoutineDay.ScheduledVisits(routine))
                                if(visit.BuildingKey==building)
                                {
                                    double enter=Math.Floor(Now()/86400d)*86400+visit.EnterAt+ObservedShift(store,person.Id,Now());
                                    if(enter>Now()+2)
                                    {
                                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PHYSICAL_DAY_OPENING_WAIT: building="+building+"; skippedGameSeconds="+(enter-Now()));
                                        double skipped=enter-Now()+0.1;
                                        clock.RaiseTime((float)skipped);gameStart+=skipped;
                                    }
                                    break;
                                }
                        }
                    }
                    if(Time.realtimeSinceStartup>=nextTrace)
                    {Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PHYSICAL_DAY_TRACE: "+OutdoorDiagnostics(streets,geometry,person,route));nextTrace=Time.realtimeSinceStartup+20;}
                }
                else if(seen)
                {
                    if(Vector3.Distance(last,door)>0.8f)
                        throw new InvalidOperationException("Daily walker disappeared away from destination door: "+building+"; gap="+Vector3.Distance(last,door));
                    vanished=true;break;
                }
                else
                {
                    var start=route[0];game.PlayerMotor.transform.position=geometry.ScenePoint(new RoutePoint(start.X,start.Z))+Vector3.up+Vector3.right*3;
                }
                yield return null;
            }
            var state=streets.GetState(person.Id,Now());
            double elapsed=Now()-gameStart;float real=Time.realtimeSinceStartup-started;
            if(!vanished || frames<3 || travelled<0.5 || state.BuildingKey!=building || Math.Abs(elapsed-real*2)>3)
                throw new InvalidOperationException("Daily outdoor arrival failed: building="+building+"; vanished="+vanished+
                    "; frames="+frames+"; moved="+travelled+"; actual="+state.Activity+":"+state.BuildingKey+"; gameSeconds="+elapsed+"; realSeconds="+real+
                    "; "+OutdoorDiagnostics(streets,geometry,person,route));
            navigationWitness.Complete();
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PHYSICAL_DAY_WALK: building="+building+"; moved="+travelled+"; maxFrame="+maxStep+"; frames="+frames+"; doorGap="+Vector3.Distance(last,door));
        }
        private static IEnumerator VisitInterior(PeopleOfTheIliacBayMod host,StreetPopulation streets,Resident person,int building,ResidentVisitPurpose purpose)
        {
            var game=GameManager.Instance;var clock=DaggerfallUnity.Instance.WorldTime.Now;
            yield return WaitForActor(streets,person.Id,20);
            var room=game.PlayerEnterExit.Interior;
            var geometry=(InteriorGeometry)typeof(StreetPopulation).GetField("indoorGeometry",Private).GetValue(streets);
            if(geometry==null || !geometry.EntryGround.HasValue) throw new InvalidOperationException("Daily service has no entry ground.");
            var entry=geometry.EntryGround.Value;
            var activityGeometry=(ShopVisitGeometry)typeof(StreetPopulation).GetField("shopGeometry",Private).GetValue(streets);
            if(activityGeometry!=null && activityGeometry.IsTavern)
            {
                var layout=(InnActivityLayout)typeof(ShopVisitGeometry).GetField("innLayout",Private).GetValue(activityGeometry);
                var journeys=(Dictionary<string,ShopVisitJourney>)typeof(StreetPopulation).GetField("shopJourneys",Private).GetValue(streets);
                ShopVisitJourney visit;
                if(layout==null || layout.KeeperApproaches.Count==0 || !journeys.TryGetValue(person.Id,out visit) || visit==null)
                    throw new InvalidOperationException("Inn has no public customer approach/activity plan.");
                int seats=0;foreach(var spot in layout.Spots) if(spot.Seat) seats++;
                for(int leg=0;leg<visit.LegCount-1;leg++)
                {
                    var path=visit.LegRoute(leg);var end=path[path.Count-1];var local=new Vector3((float)end.X,(float)end.Y,(float)end.Z);
                    if(visit.StopPhase(leg)==ShopVisitPhase.Browsing)
                    {
                        var seatId=visit.SeatId(leg);
                        var seat=seatId==null?null:layout.Seating.Find(seatId);
                        // Occupied furniture is deliberately not normal walkable
                        // floor. Validate its public approach and catalogued slot.
                        bool publicStop=seatId==null?layout.IsPublic(local):seat!=null &&
                            layout.Spots.Exists(s=>s.Slot==seat&&(s.Position-local).sqrMagnitude<0.0001f) &&
                            layout.IsPublic(room.transform.InverseTransformPoint(seat.Approach));
                        if(!publicStop)throw new InvalidOperationException("Ordinary inn activity planned in a private/staff area: seat="+seatId+"; position="+local);
                    }
                }
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INN_PUBLIC_ACTIVITIES_PASS: publicStops="+layout.Spots.Count+"; seatPlaceholders="+seats+
                    "; counterBarrier="+layout.HasCounterBarrier+"; keeperApproaches="+layout.KeeperApproaches.Count+"; no private-room stops.");
                bool seatsOnly=Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-inn-seat-only")>=0;
                if(!seatsOnly)yield return InnCounterProbe.Run(host,streets,room,geometry,activityGeometry,layout);
                yield return InnSeatProbe.Run(host,streets,room,geometry,activityGeometry,layout);
                if(seatsOnly)yield break;
            }
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-inn-seat-only")>=0)
                throw new InvalidOperationException("Focused seating acceptance requires an inn fixture.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PHYSICAL_DAY_OBSERVER_BEFORE: "+InteriorDiagnostics(streets,person));
            Vector3 observation=entry;float observationDistance=0;
            foreach(var anchor in geometry.LocalAnchors)
            {
                var point=room.transform.TransformPoint(anchor);float distance=(point-entry).sqrMagnitude;
                if(Math.Abs(point.y-entry.y)>0.3f || distance<16 || distance<=observationDistance) continue;
                observation=point;observationDistance=distance;
            }
            if(observationDistance<16) throw new InvalidOperationException("No observer position clear of inn/shop entry.");
            game.PlayerMotor.transform.position=observation+Vector3.up*1.5f;
            Physics.SyncTransforms();
            var actor=streets.FindActor(person.Id);Vector3 previous=actor.transform.position-Vector3.up,last=previous;
            float travelled=0,maxStep=0,deadline=Time.realtimeSinceStartup+210;int idleFrames=0;bool counter=false,browsed=false,exited=false,socialTested=false;
            var skipped=new HashSet<int>();float nextTrace=Time.realtimeSinceStartup+10,counterStarted=-1;
            var state=(WorldState)host.GetSaveData();
            var routine=state.Routines.Find(t=>t.SettlementId==game.PlayerGPS.CurrentLocation.MapTableData.MapId)
                .Residents.Find(r=>r.ResidentId==person.Id);
            bool retail=purpose==ResidentVisitPurpose.Shop || purpose==ResidentVisitPurpose.Social || purpose==ResidentVisitPurpose.Inn;
            while(Time.realtimeSinceStartup<deadline)
            {
                yield return null;DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
                actor=streets.FindActor(person.Id);
                if(!actor || !actor.gameObject.activeInHierarchy)
                {
                    if(Vector3.Distance(last,entry)>0.8f) throw new InvalidOperationException("Daily patron vanished away from interior exit.");
                    exited=true;break;
                }
                var current=actor.transform.position-Vector3.up;float step=Vector3.Distance(previous,current);
                FollowCamera(actor,current,geometry);
                maxStep=Math.Max(maxStep,step);travelled+=step;previous=last=current;
                if(step>1.5f) throw new InvalidOperationException("Daily patron teleported inside "+building+" by "+step+"m.");
                if(Time.realtimeSinceStartup>=nextTrace)
                {Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PHYSICAL_DAY_INDOOR_TRACE: "+InteriorDiagnostics(streets,person));nextTrace=Time.realtimeSinceStartup+10;}
                var visits=(Dictionary<string,PhysicalShopVisit>)typeof(StreetPopulation).GetField("physicalShopVisits",Private).GetValue(streets);
                PhysicalShopVisit physical;
                if(!visits.TryGetValue(person.Id,out physical))
                {
                    if(retail || skipped.Contains(-1) || !actor.IsStandingStill || ++idleFrames<4) continue;
                    var institutional=streets.GetState(person.Id,Now());
                    if(institutional.Activity!=Activity.Visit || institutional.BuildingKey!=building)
                        throw new InvalidOperationException("Institutional visit disagrees with its actual building.");
                    var store=(WorldStateStore)typeof(PeopleOfTheIliacBayMod).GetField("store",Private).GetValue(host);
                    int observedTown,observedBuilding;double observed,end,planned,resume,shift=0;
                    if(store.TryObservedVisitMetadata(person.Id,out observedTown,out observedBuilding,out observed,out end,out planned,out resume) &&
                        observedTown==game.PlayerGPS.CurrentLocation.MapTableData.MapId && Now()>=observed && Now()<resume+end-planned)
                        shift=end-planned;
                    foreach(var visit in ResidentRoutineDay.ScheduledVisits(routine))
                        if(visit.BuildingKey==building)
                        {
                            double leave=Math.Floor(Now()/86400d)*86400+visit.LeaveAt+shift;
                            // Institutional visits have no shop checkpoint. Keep
                            // stationary skips below native catch-up's30s floor,
                            // so it retains the observed actor for the exit walk.
                            double dwellRemaining=leave-Now()+0.1;
                            if(dwellRemaining>0)clock.RaiseTime((float)Math.Min(20,dwellRemaining));
                            if(dwellRemaining<=20)skipped.Add(-1);
                            break;
                        }
                    continue;
                }
                var sample=physical.Snapshot;
                if(sample.Phase==ShopVisitPhase.Haggling)
                {counter=true;if(counterStarted<0) counterStarted=Time.realtimeSinceStartup;}
                else if(counterStarted>=0)
                {
                    float duration=Time.realtimeSinceStartup-counterStarted;
                    if(duration<4.8f || duration>15.5f) throw new InvalidOperationException("Keeper conversation outside 5–15 real seconds: "+duration);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_KEEPER_CONVERSATION_PASS: realSeconds="+duration+"; normal2xClock=true");counterStarted=-1;
                }
                if(sample.Phase==ShopVisitPhase.Browsing) browsed=true;
                if(counter && !socialTested && sample.Phase==ShopVisitPhase.Browsing && actor.IsStandingStill)
                {
                    socialTested=true;
                    var activities=(ShopVisitGeometry)typeof(StreetPopulation).GetField("shopGeometry",Private).GetValue(streets);
                    if(activities.IsTavern) yield return InnChatProbe.Verify(streets,actor,geometry,activities);
                }
                if(sample.Phase!=ShopVisitPhase.Haggling && sample.Phase!=ShopVisitPhase.Browsing) {idleFrames=0;continue;}
                if(!actor.IsStandingStill) throw new InvalidOperationException("Daily patron kept a walking pose at a completed activity. seated="+
                    actor.IsSeated+"; nativeIdle="+actor.Person.Asset.IsIdle+"; phase="+sample.Phase+"; seat="+physical.SelectedSeatId);
                var actual=streets.GetState(person.Id,Now());
                if(actual.BuildingKey!=building || actual.Activity!=Activity.Visit)
                    throw new InvalidOperationException("Daily activity claims a different place while patron remains inside.");
                var context=ConversationLifeContext.RoutineStatus(routine,Now(),actual,0,
                    key=>"building "+key);
                if(context.IndexOf("currently at building "+building,StringComparison.Ordinal)<0)
                    throw new InvalidOperationException("Daily dialogue context disagrees with physical service location.");
                var progress=(ShopPhysicalProgress)typeof(PhysicalShopVisit).GetField("progress",Private).GetValue(physical);
                // Catch-up trims completed legs and resets the local leg index.
                // The remaining suffix identifies this dwell across reconstruction.
                int dwellKey=physical.Journey.LegCount-1-progress.Leg;
                if(sample.Phase==ShopVisitPhase.Haggling || ++idleFrames<4 || skipped.Contains(dwellKey)) continue;
                // Preserve real arrival and a few rendered idle frames; skip only
                // the current stationary dwell, never travel or doorway approach.
                var remaining=physical.Capture(actor,room,geometry,Now(),game.PlayerGPS.CurrentLocation.MapTableData.MapId);
                if(remaining==null || remaining.Legs.Count==0) throw new InvalidOperationException("Daily stationary visit could not be captured.");
                skipped.Add(dwellKey);clock.RaiseTime((float)(remaining.Legs[0].DwellSeconds+0.1));idleFrames=0;
            }
            if(!exited || retail && (travelled<0.5 || !browsed) || purpose==ResidentVisitPurpose.Social && !counter)
                throw new InvalidOperationException("Daily service failed: building="+building+"; exited="+exited+"; browse="+browsed+
                    "; counter="+counter+"; moved="+travelled+"; last="+last+"; "+InteriorDiagnostics(streets,person));
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PHYSICAL_DAY_VISIT: building="+building+"; browsing="+browsed+"; counter="+counter+
                "; moved="+travelled+"; maxFrame="+maxStep+"; stationaryDwellsSkipped="+skipped.Count);
        }
        private static IEnumerator Enter(BuildingDirectory directory,int key)
        {
            foreach(var owner in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                foreach(var door in owner.Doors ?? new StaticDoor[0])
                    if(door.doorType==DoorTypes.Building && door.buildingKey==key)
                    {
                        TownSmokeHarness.EnterInterior(owner.transform,door);
                        yield return new WaitForSecondsRealtime(2);DaggerfallUI.Instance.PopToHUD();GameManager.Instance.PauseGame(false);yield break;
                    }
            throw new InvalidOperationException("Daily destination has no actual entrance: "+key);
        }
        private static IEnumerator Exit(StreetPopulation streets)
        {
            GameManager.Instance.PlayerEnterExit.TransitionExterior();
            float deadline=Time.realtimeSinceStartup+25;
            do {yield return null;} while((GameManager.Instance.IsPlayerInside || !streets.Ready) && Time.realtimeSinceStartup<deadline);
            if(GameManager.Instance.IsPlayerInside || !streets.Ready) throw new InvalidOperationException("Daily exterior transition timed out.");
            DaggerfallUI.Instance.PopToHUD();GameManager.Instance.PauseGame(false);
        }
        private static IEnumerator WaitForActor(StreetPopulation streets,string id,float seconds)
        {
            float deadline=Time.realtimeSinceStartup+seconds;
            while(Time.realtimeSinceStartup<deadline)
            {
                var actor=streets.FindActor(id);
                if(streets.Ready && actor && actor.gameObject.activeInHierarchy) yield break;
                yield return null;
            }
            throw new InvalidOperationException("Daily resident missing in current building: "+id);
        }
        private static int Count(string id)
        {int count=0;foreach(var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>()) if(actor.Resident.Id==id) count++;return count;}
        private static Vector3 cameraForward=Vector3.forward;
        private static void FollowCamera(ResidentActor actor,Vector3 ground,InteriorGeometry roomGeometry=null)
        {
            TownSmokeHarness.Watch(actor.transform);
            Vector3 forward=actor.transform.forward;forward.y=0;
            if(forward.sqrMagnitude>0.01f)
                cameraForward=Vector3.RotateTowards(cameraForward,forward.normalized,120*Mathf.Deg2Rad*Time.unscaledDeltaTime,0).normalized;
            float distance=3;
            if(roomGeometry!=null)
                while(distance>0.75f && !roomGeometry.HasClearLine(ground+Vector3.up*1.5f,ground-cameraForward*distance+Vector3.up*1.5f)) distance-=0.25f;
            GameManager.Instance.PlayerMotor.transform.position=ground-cameraForward*distance+Vector3.up;
            SleepProbe.AimCamera(actor.transform.position);
        }
        private static double Length(List<SavedRoutePoint> route)
        {double result=0;for(int i=1;i<route.Count;i++){double x=route[i].X-route[i-1].X,z=route[i].Z-route[i-1].Z;result+=Math.Sqrt(x*x+z*z);}return result;}
        private static string OutdoorDiagnostics(StreetPopulation streets,TownGeometry geometry,Resident person,List<SavedRoutePoint> route)
        {
            var actor=streets.FindActor(person.Id);if(!actor) return "body=missing";
            var motion=actor.GetComponent<CrowdMotion>();
            var target=(Vector3)typeof(CrowdMotion).GetField("lastTarget",Private).GetValue(motion);
            var ground=actor.transform.position-Vector3.up;var delta=target-ground;
            var blockers=new List<string>();
            if(delta.sqrMagnitude>0.001f)
                foreach(var hit in Physics.CapsuleCastAll(ground+Vector3.up*0.76f,ground+Vector3.up*1.65f,0.25f,
                    delta.normalized,Math.Min(delta.magnitude,3),~0,QueryTriggerInteraction.Ignore))
                    if(!hit.collider.GetComponentInParent<CrowdMotion>()) blockers.Add(hit.collider.name+"@"+hit.distance);
            var directory=GameManager.Instance.StreamingWorld.GetCurrentBuildingDirectory();
            var local=directory.transform.InverseTransformPoint(ground);RoutePoint corner;
            string next=StreetPopulation.TryNextArrivalRoutePoint(route,local,out corner) ? geometry.ScenePoint(corner).ToString("F3") : "none";
            var store=(WorldStateStore)typeof(StreetPopulation).GetField("worldStore",Private).GetValue(streets);
            var routine=store.GetResidentDialogueRoutine(directory.MapID,person.Id);
            var trip=store.GetResidentDialogueTrip(person.Id);
            return "body="+ground.ToString("F3")+"; target="+target.ToString("F3")+"; gap="+delta.magnitude+"; nextCorner="+next+
                "; blockers="+string.Join(",",blockers.ToArray())+"; gameTime="+DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()%86400+
                "; firstWalkSeconds="+routine.Itinerary.Stops[0].WalkSeconds+"; trip="+(trip==null ? "none" : trip.DepartsAt.ToString());
        }
        private static double ObservedShift(WorldStateStore store,string id,double now)
        {
            int town,building;double observed,end,planned,resume;
            return store.TryObservedVisitMetadata(id,out town,out building,out observed,out end,out planned,out resume) &&
                now>=observed && now<resume+end-planned ? end-planned : 0;
        }
        private static string InteriorDiagnostics(StreetPopulation streets,Resident person)
        {
            var actor=streets.FindActor(person.Id);
            var visits=(Dictionary<string,PhysicalShopVisit>)typeof(StreetPopulation).GetField("physicalShopVisits",Private).GetValue(streets);
            var journeys=(Dictionary<string,ShopVisitJourney>)typeof(StreetPopulation).GetField("shopJourneys",Private).GetValue(streets);
            var activities=(ShopVisitGeometry)typeof(StreetPopulation).GetField("shopGeometry",Private).GetValue(streets);
            PhysicalShopVisit physical;visits.TryGetValue(person.Id,out physical);
            ShopVisitJourney journey;journeys.TryGetValue(person.Id,out journey);
            var state=streets.GetState(person.Id,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
            var blockers=new List<string>();
            if(actor)
            {
                var ground=actor.transform.position-Vector3.up;
                foreach(var collider in Physics.OverlapCapsule(ground+Vector3.up*0.76f,ground+Vector3.up*1.65f,0.25f,~0,QueryTriggerInteraction.Ignore))
                    if(!collider.GetComponentInParent<CrowdMotion>()) blockers.Add(collider.name);
            }
            return "body="+(actor ? (actor.transform.position-Vector3.up).ToString("F3") : "missing")+"; activity="+state.Activity+":"+state.BuildingKey+
                "; journey="+(journey==null ? "missing" : journey.LegCount.ToString())+"; rejection="+(activities==null ? "no activities" : activities.RejectionFor(person.Id))+
                "; physical="+(physical==null ? "missing" : physical.Snapshot.Phase+":"+physical.LastFailure)+
                "; player="+GameManager.Instance.PlayerMotor.transform.position+"; overlaps="+string.Join(",",blockers.ToArray());
        }
    }
}
