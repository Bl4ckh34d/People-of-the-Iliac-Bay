using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Opt-in native acceptance for a named, surveyed outdoor Building target
    // followed by a Wait bound to that same static destination.
    internal static class NativeMoveToWaitProbe
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private const BindingFlags AnyInstance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        private const int WaitSeconds=10;
        private const float MinimumInitialDistance=5f;
        private const float MaximumInitialDistance=40f;
        private const float MaximumSampleStep=0.5f;

        private sealed class Fixture
        {
            internal ResidentActor Actor;
            internal NpcActionOption MoveOption;
            internal List<NpcActionDecision> Decisions;
            internal string BuildingName;
            internal Vector3 Target,ObserverGround,ActorStart;
            internal float ArrivalRange,InitialDistance,ObserverGap;
            internal List<SavedRoutePoint> PlannedRoute;
            internal double PlannedRouteLength;
        }

        private sealed class Trace
        {
            internal Vector3 Last;
            internal float Length,MaxStep;
            internal Trace(Vector3 start){Last=start;}
            internal void Sample(Vector3 position)
            {float step=Vector3.Distance(Last,position);Length+=step;if(step>MaxStep)MaxStep=step;Last=position;}
        }

        public static IEnumerator Run(PeopleOfTheIliacBayMod host,StreetPopulation streets,string character)
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-move-to-wait-probe")<0 ||
                string.IsNullOrWhiteSpace(character) || !character.StartsWith("PeopleOfTheIliacBayTest-",StringComparison.Ordinal))
                throw new InvalidOperationException("MoveTo/Wait probe requires the explicit flag and owned disposable character.");
            var game=GameManager.Instance;
            if(!host || !streets || !game || game.PlayerEntity.Name!=character || game.PlayerEnterExit.IsPlayerInsideBuilding || !streets.Ready)
                throw new InvalidOperationException("MoveTo/Wait probe requires the owned character in a loaded exterior town.");

            var clock=DaggerfallUnity.Instance.WorldTime.Now;double beforeClock=clock.ToSeconds();
            double morning=Math.Floor(beforeClock/86400d)*86400d+10d*3600d;
            if(beforeClock<morning)clock.RaiseTime((float)(morning-beforeClock));
            yield return null;yield return null;

            var geometry=typeof(StreetPopulation).GetField("geometry",Private).GetValue(streets) as TownGeometry;
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();var store=GetStore(host);
            if(geometry==null || directory==null || store==null)
                throw new InvalidOperationException("MoveTo/Wait probe has no active town geometry, directory or state store.");
            float entranceDeadline=Time.realtimeSinceStartup+60f;
            while(streets.Ready && geometry.Entrances.Count==0 && Time.realtimeSinceStartup<entranceDeadline)yield return null;
            if(!streets.Ready || geometry.Entrances.Count==0)
                throw new InvalidOperationException("No surveyed native building entrance became available for the named MoveTo target.");

            string lastReason="No eligible existing adult had both a clear observer point and a reachable named-building target.";
            var rejectedResidents=new HashSet<string>(StringComparer.Ordinal);Fixture fixture=null;string residentId=null;
            float actorDeadline=Time.realtimeSinceStartup+30f;
            while(residentId==null && Time.realtimeSinceStartup<actorDeadline)
            {
                fixture=FindFixture(streets,store,geometry,directory,game,rejectedResidents,out lastReason);
                if(fixture==null) {yield return new WaitForSecondsRealtime(0.25f);continue;}
                string acceptReason;
                if(NpcProbeCommandSource.TryAcceptTask(streets,fixture.Actor.Resident.Id,
                    "native-moveto-wait:"+Guid.NewGuid().ToString("N"),fixture.Decisions,out acceptReason))
                    residentId=fixture.Actor.Resident.Id;
                else
                {
                    lastReason=acceptReason;
                    rejectedResidents.Add(fixture.Actor.Resident.Id);
                    fixture=null;
                }
            }
            if(fixture==null)throw new InvalidOperationException("Could not find a normal reachable named-building MoveTo task: "+lastReason);
            float stagedGap=fixture.ObserverGap;
            int targetBuildingKey=fixture.MoveOption.Target.BuildingKey;
            var acceptedTask=store.ActiveNpcTask(residentId);
            if(acceptedTask==null)throw new InvalidOperationException("Normal conversation-task acceptance returned true without an active saved task.");
            string taskId=acceptedTask.Id;var task=store.ReadNpcTask(taskId);
            if(task==null || task.Steps.Count!=2 || task.CurrentStepIndex!=0 ||
                task.Steps[0].Kind!=NpcActionKind.MoveTo || task.Steps[0].Target.Kind!=NpcActionTargetKind.Building ||
                task.Steps[0].Target.BuildingKey!=fixture.MoveOption.Target.BuildingKey ||
                !NpcTaskPositionBinding.IsBoundWaitAfterMoveTo(task.Steps[0],task.Steps[1]))
                throw new InvalidOperationException("Accepted task did not retain the exact static Building destination for its later Wait.");
            var move=store.ReadNpcAction(taskId+":step:0");
            if(move==null || move.Kind!=NpcActionKind.MoveTo || move.Target.BuildingKey!=fixture.MoveOption.Target.BuildingKey)
                throw new InvalidOperationException("Accepted task did not dispatch its normal MoveTo action first.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MOVE_TO_WAIT_FIXTURE: task="+taskId+"; resident="+residentId+
                "; action="+move.Id+"; acceptedAt="+move.AcceptedAt.ToString("F3",CultureInfo.InvariantCulture)+
                "; deadlineAt="+move.DeadlineAt.ToString("F3",CultureInfo.InvariantCulture)+
                "; actionBudget="+(move.DeadlineAt-move.AcceptedAt).ToString("F3",CultureInfo.InvariantCulture)+
                "; gameNow="+ActionTime().ToString("F3",CultureInfo.InvariantCulture)+
                "; worldTimeScale="+DaggerfallUnity.Instance.WorldTime.TimeScale.ToString("F3",CultureInfo.InvariantCulture)+
                "; start="+fixture.ActorStart.ToString("F3")+"; straightDistance="+fixture.InitialDistance.ToString("F3",CultureInfo.InvariantCulture)+
                "; observerGap="+fixture.ObserverGap.ToString("F3",CultureInfo.InvariantCulture)+
                "; target="+fixture.Target.ToString("F3")+"; arrivalRange="+fixture.ArrivalRange.ToString("F3",CultureInfo.InvariantCulture)+
                "; buildingKey="+fixture.MoveOption.Target.BuildingKey+"; plannedRouteCount="+fixture.PlannedRoute.Count+
                "; plannedRouteLength="+fixture.PlannedRouteLength.ToString("F3",CultureInfo.InvariantCulture)+
                "; plannedRoutePoints="+DescribeRoute(fixture.PlannedRoute));

            var trace=new Trace(fixture.Actor.transform.position-Vector3.up);
            float movementDeadline=Time.realtimeSinceStartup+240f;NpcActionJob wait=null,completedWait=null;
            bool sawMoveCompletion=false;Vector3 waitStartPosition=Vector3.zero;
            float completionDistance=float.PositiveInfinity,waitDrift=0f;double moveCompletedAt=0;
            bool sawWaitStart=false;float nextProgressLog=Time.realtimeSinceStartup+5f;
            while(Time.realtimeSinceStartup<movementDeadline)
            {
                if(!fixture.Actor || !fixture.Actor.gameObject.activeInHierarchy)
                    throw new InvalidOperationException("Existing managed adult disappeared before MoveTo/Wait task completion.");
                Vector3 actual=fixture.Actor.transform.position-Vector3.up;trace.Sample(actual);
                task=store.ReadNpcTask(taskId);move=store.ReadNpcAction(taskId+":step:0");
                wait=store.ReadNpcAction(taskId+":step:1");
                if(task==null || move==null)throw new InvalidOperationException("Saved MoveTo task receipt disappeared during physical execution.");
                if(task.Phase==NpcTaskPhase.Failed || task.Phase==NpcTaskPhase.Cancelled ||
                    NpcActionExecutor.IsTerminal(move) && move.Phase!=NpcActionPhase.Completed)
                {
                    Vector3 resolvedTarget;float resolvedRange;string resolveReason;
                    bool resolved=TryResolveTarget(streets,move,out resolvedTarget,out resolvedRange,out resolveReason);
                    throw new InvalidOperationException("Native MoveTo step failed before the bound Wait: task="+task.Phase+
                        "; action="+move.Phase+" "+move.FailureReason+"; actualFoot="+actual.ToString("F3")+
                        "; targetResolved="+resolved+"; resolvedTarget="+resolvedTarget.ToString("F3")+
                        "; actualGap="+(resolved?Vector3.Distance(actual,resolvedTarget):float.PositiveInfinity).ToString("F3",CultureInfo.InvariantCulture)+
                        "; arrivalRange="+resolvedRange.ToString("F3",CultureInfo.InvariantCulture)+"; resolveReason="+resolveReason+
                        "; buildingKey="+move.Target.BuildingKey+"; start="+fixture.ActorStart.ToString("F3")+
                        "; traceWalk="+trace.Length.ToString("F3",CultureInfo.InvariantCulture)+
                        "; traceMaxStep="+trace.MaxStep.ToString("F3",CultureInfo.InvariantCulture)+
                        "; gameNow="+ActionTime().ToString("F3",CultureInfo.InvariantCulture)+
                        "; acceptedAt="+move.AcceptedAt.ToString("F3",CultureInfo.InvariantCulture)+
                        "; deadlineAt="+move.DeadlineAt.ToString("F3",CultureInfo.InvariantCulture)+
                        "; plannedRouteCount="+fixture.PlannedRoute.Count+
                        "; plannedRouteLength="+fixture.PlannedRouteLength.ToString("F3",CultureInfo.InvariantCulture)+
                        "; motion="+DescribeActionMotion(streets,residentId,fixture.Actor)+".");
                }
                if(Time.realtimeSinceStartup>=nextProgressLog)
                {
                    bool resolved=TryResolveTarget(streets,move,out var progressTarget,out var progressRange,out var progressReason);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MOVE_TO_WAIT_PROGRESS: task="+taskId+"; action="+move.Id+
                        "; elapsedReal="+(Time.realtimeSinceStartup-(movementDeadline-240f)).ToString("F1",CultureInfo.InvariantCulture)+
                        "; gameNow="+ActionTime().ToString("F3",CultureInfo.InvariantCulture)+
                        "; actionPhase="+move.Phase+"; actual="+actual.ToString("F3")+
                        "; gap="+(resolved?Vector3.Distance(actual,progressTarget):float.PositiveInfinity).ToString("F3",CultureInfo.InvariantCulture)+
                        "; traceWalk="+trace.Length.ToString("F3",CultureInfo.InvariantCulture)+
                        "; traceMaxStep="+trace.MaxStep.ToString("F3",CultureInfo.InvariantCulture)+
                        "; targetResolved="+resolved+"; arrivalRange="+progressRange.ToString("F3",CultureInfo.InvariantCulture)+
                        "; resolveReason="+progressReason+"; motion="+DescribeActionMotion(streets,residentId,fixture.Actor));
                    nextProgressLog+=5f;
                }
                if(move.Phase==NpcActionPhase.Completed && !sawMoveCompletion)
                {
                    sawMoveCompletion=true;moveCompletedAt=move.UpdatedAt;
                    completionDistance=Vector3.Distance(actual,fixture.Target);
                }
                if(wait!=null)
                {
                    if(!sawMoveCompletion)
                    {
                        if(Vector3.Distance(actual,fixture.Target)>fixture.ArrivalRange+0.001f)
                            throw new InvalidOperationException("Bound Wait dispatched before the existing actor physically reached the named entrance.");
                        sawMoveCompletion=true;moveCompletedAt=move.UpdatedAt;
                        completionDistance=Vector3.Distance(actual,fixture.Target);
                    }
                    if(wait.Kind!=NpcActionKind.Wait || !NpcTaskPositionBinding.HasCompletedMoveToReceipt(task.Steps[0],move,task.Steps[1]) ||
                        wait.Target.Kind!=NpcActionTargetKind.Building || wait.Target.BuildingKey!=fixture.MoveOption.Target.BuildingKey ||
                        wait.AcceptedAt+0.000001d<moveCompletedAt)
                        throw new InvalidOperationException("Wait did not retain the exact completed MoveTo destination or started its clock too early.");
                    if(!sawWaitStart)
                    {
                        sawWaitStart=true;waitStartPosition=actual;
                        if(Vector3.Distance(actual,fixture.Target)>fixture.ArrivalRange+0.001f)
                            throw new InvalidOperationException("Wait started before the body was inside the resolved entrance arrival range.");
                    }
                    if(!NpcActionExecutor.IsTerminal(wait) && TimeSeconds()<=wait.FinishAt)
                    {
                        float drift=Vector3.Distance(actual,waitStartPosition);if(drift>waitDrift)waitDrift=drift;
                    }
                    if(NpcActionExecutor.IsTerminal(wait))
                    {
                        if(wait.Phase!=NpcActionPhase.Completed)
                            throw new InvalidOperationException("Bound Wait did not complete normally: "+wait.Phase+" "+wait.FailureReason);
                        completedWait=wait;break;
                    }
                }
                yield return null;
            }
            if(completedWait==null)throw new InvalidOperationException("Named-building MoveTo and 10-second Wait exceeded 240 seconds of observed runtime.");

            if(!sawMoveCompletion || !sawWaitStart || completionDistance>fixture.ArrivalRange+0.001f ||
                trace.Length<Math.Max(2f,fixture.InitialDistance-fixture.ArrivalRange-1f) || trace.MaxStep>MaximumSampleStep ||
                completedWait.FinishAt-completedWait.AcceptedAt<WaitSeconds-0.001d ||
                completedWait.UpdatedAt-completedWait.AcceptedAt<WaitSeconds-0.001d ||
                completedWait.UpdatedAt+0.001d<completedWait.FinishAt || waitDrift>MaximumSampleStep)
                throw new InvalidOperationException("Physical MoveTo/Wait evidence failed: walk="+trace.Length.ToString("0.00",CultureInfo.InvariantCulture)+
                    "m, max-step="+trace.MaxStep.ToString("0.000",CultureInfo.InvariantCulture)+"m, move-distance="+
                    completionDistance.ToString("0.000",CultureInfo.InvariantCulture)+"m, range="+fixture.ArrivalRange.ToString("0.000",CultureInfo.InvariantCulture)+
                    "m, wait="+(completedWait.UpdatedAt-completedWait.AcceptedAt).ToString("0.00",CultureInfo.InvariantCulture)+
                    " game-seconds, drift="+waitDrift.ToString("0.000",CultureInfo.InvariantCulture)+"m.");

            float cleanupDeadline=Time.realtimeSinceStartup+150f;task=store.ReadNpcTask(taskId);
            while(Time.realtimeSinceStartup<cleanupDeadline &&
                (store.ActiveNpcTask(residentId)!=null || store.ActiveNpcAction(residentId)!=null))yield return null;
            task=store.ReadNpcTask(taskId);
            if(task==null || task.Phase!=NpcTaskPhase.Completed || task.CurrentStepIndex!=2 ||
                store.ActiveNpcTask(residentId)!=null || store.ActiveNpcAction(residentId)!=null)
                throw new InvalidOperationException("Completed MoveTo/Wait task did not release action ownership back to the ordinary routine.");

            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MOVE_TO_WAIT_NATIVE_PASS: resident="+residentId+"; building="+
                targetBuildingKey+" ("+fixture.BuildingName+"); target-kind=Building; initial-distance="+
                fixture.InitialDistance.ToString("0.00",CultureInfo.InvariantCulture)+"m; measured-walk="+
                trace.Length.ToString("0.00",CultureInfo.InvariantCulture)+"m; observer-gap="+
                stagedGap.ToString("0.00",CultureInfo.InvariantCulture)+"m; actor-teleport=false; observer-staged=true; max-step="+
                trace.MaxStep.ToString("0.000",CultureInfo.InvariantCulture)+
                "m; move-completion-distance="+completionDistance.ToString("0.000",CultureInfo.InvariantCulture)+"m; arrival-range="+
                fixture.ArrivalRange.ToString("0.000",CultureInfo.InvariantCulture)+"m; wait-start-after-move-receipt=true; wait-elapsed="+
                (completedWait.UpdatedAt-completedWait.AcceptedAt).ToString("0.00",CultureInfo.InvariantCulture)+" game-seconds; wait-drift="+
                waitDrift.ToString("0.000",CultureInfo.InvariantCulture)+"m; routine-owner-released=true.");
        }

        private static Fixture FindFixture(StreetPopulation streets,WorldStateStore store,TownGeometry geometry,
            BuildingDirectory directory,GameManager game,HashSet<string> rejectedResidents,out string reason)
        {
            reason="No eligible existing adult had a clear 2-4m observer point and a reachable surveyed named entrance.";
            var residents=UnityEngine.Object.FindObjectsOfType<ResidentActor>();
            foreach(var actor in residents)
            {
                if(!actor || !actor.Person || actor.Resident==null || !actor.gameObject.activeInHierarchy ||
                    !actor.Resident.IsAlive || actor.Resident.IsQuestProtected || actor.Resident.NativeIdentity!=null ||
                    actor.Resident.AgeGroup!=ResidentAgeGroup.Adult || actor.IsSleeping || actor.IsConversing ||
                    streets.IsAlarmed(actor.Resident.Id) ||
                    rejectedResidents.Contains(actor.Resident.Id) ||
                    store.ActiveNpcTask(actor.Resident.Id)!=null || store.ActiveNpcAction(actor.Resident.Id)!=null)continue;
                var actorStart=actor.transform.position-Vector3.up;
                Vector3 observerGround;
                if(!TryObserverGround(geometry,directory,actorStart,out observerGround))continue;
                // Options are proximity-gated by the production adapter. Move
                // only the player onto this verified route point before asking
                // that adapter for the ordinary Building and Wait capabilities.
                game.PlayerMotor.transform.position=observerGround+Vector3.up;
                Physics.SyncTransforms();
                float observerGap=Vector3.Distance(game.PlayerMotor.transform.position-Vector3.up,actor.transform.position-Vector3.up);
                if(!actor || Vector3.Distance(actor.transform.position-Vector3.up,actorStart)>0.25f ||
                    observerGap<2f || observerGap>4f || observerGap>8f)continue;
                foreach(var pair in geometry.Entrances)
                {
                    var entrance=pair.Value;if(entrance==null)continue;
                    string name=game.TalkManager.GetBuildingNameForBuildingKey(pair.Key);
                    if(string.IsNullOrWhiteSpace(name))continue;
                    Vector3 destination=geometry.ScenePoint(new RoutePoint(entrance.LocalDoor.x,entrance.LocalDoor.z));
                    float initial=Vector3.Distance(actorStart,destination);
                    if(initial<MinimumInitialDistance || initial>MaximumInitialDistance)continue;
                    Vector3 localStart=directory.transform.InverseTransformPoint(actorStart);
                    Vector3 localDestination=directory.transform.InverseTransformPoint(destination);
                    var route=geometry.RouteBetweenPoints(new SavedRoutePoint{X=localStart.x,Z=localStart.z},
                        new SavedRoutePoint{X=localDestination.x,Z=localDestination.z});
                    if(route==null || route.Count<2)continue;
                    double routeLength=RouteLength(route);
                    var moveOptions=streets.ConversationActionCandidates(actor.Resident.Id);
                    NpcActionOption selectedMove=null;int moveMatches=0;
                    foreach(var option in moveOptions)
                        if(option.Kind==NpcActionKind.MoveTo && option.Target!=null && option.Target.Kind==NpcActionTargetKind.Building &&
                            option.Target.SettlementId==geometrySettlement(streets) && option.Target.BuildingKey==pair.Key)
                        {selectedMove=option;moveMatches++;}
                    if(selectedMove==null || moveMatches!=1)continue;
                    Vector3 resolved;float arrival;string resolveReason;
                    if(!TryResolveTarget(streets,new NpcActionJob{Kind=NpcActionKind.MoveTo,Target=selectedMove.Target},
                        out resolved,out arrival,out resolveReason))continue;
                    if(Vector3.Distance(resolved,destination)>0.25f || arrival<=0f)continue;
                    var originalWaitOptions=streets.ConversationActionCandidates(actor.Resident.Id);
                    int maximumOptionId=0;
                    foreach(var option in originalWaitOptions)if(option!=null && option.Id>maximumOptionId)maximumOptionId=option.Id;
                    var waitOptions=NpcActionSequenceChat.BindWaitOptionsAfterMoveTo(
                        new NpcActionDecision {Option=selectedMove,DurationSeconds=300},originalWaitOptions);
                    NpcActionOption boundWait=null;
                    int boundWaitMatches=0;
                    foreach(var option in waitOptions)
                        if(option!=null && option.Kind==NpcActionKind.Wait && option.Id>maximumOptionId &&
                            NpcTaskContract.TargetMatches(selectedMove.Target,option.Target))
                        {boundWait=option;boundWaitMatches++;}
                    if(boundWait==null || boundWaitMatches!=1)continue;
                    var decisions=new List<NpcActionDecision>
                    {
                        new NpcActionDecision {Option=CopyOption(selectedMove),DurationSeconds=300},
                        new NpcActionDecision {Option=CopyOption(boundWait),DurationSeconds=WaitSeconds}
                    };
                    if(decisions[0].Option.Target.BuildingKey!=pair.Key ||
                        !NpcTaskPositionBinding.IsBoundWaitAfterMoveTo(decisions[0],decisions[1]))continue;
                    var validationPlan=new NpcTaskPlan {Id="native-moveto-wait-probe",ProposalId="native-moveto-wait-probe",
                        ResidentId=actor.Resident.Id,AcceptedAt=0,UpdatedAt=0,DeadlineAt=86400};
                    validationPlan.Steps.Add(new NpcTaskStep {Kind=decisions[0].Option.Kind,
                        Target=NpcActionExecutor.CopyTarget(decisions[0].Option.Target)});
                    validationPlan.Steps.Add(new NpcTaskStep {Kind=decisions[1].Option.Kind,
                        Target=NpcActionExecutor.CopyTarget(decisions[1].Option.Target),DurationSeconds=WaitSeconds});
                    try { NpcTaskContract.Validate(validationPlan); }
                    catch(ArgumentException) { continue; }
                    if(!actor || Vector3.Distance(actor.transform.position-Vector3.up,actorStart)>0.25f)continue;
                    return new Fixture{Actor=actor,MoveOption=selectedMove,Decisions=decisions,BuildingName=name,
                        Target=resolved,ObserverGround=observerGround,ActorStart=actorStart,ObserverGap=observerGap,
                        ArrivalRange=arrival,InitialDistance=initial,PlannedRoute=new List<SavedRoutePoint>(route),
                        PlannedRouteLength=routeLength};
                }
            }
            return null;
        }

        private static NpcActionOption CopyOption(NpcActionOption source)
        {
            return new NpcActionOption {Id=source.Id,Kind=source.Kind,Target=NpcActionExecutor.CopyTarget(source.Target),
                Label=source.Label,ItemSourceStep=source.ItemSourceStep,MaximumQuantity=source.MaximumQuantity};
        }

        private static bool TryObserverGround(TownGeometry geometry,BuildingDirectory directory,Vector3 actorGround,out Vector3 observerGround)
        {
            observerGround=Vector3.zero;
            Vector3 local=directory.transform.InverseTransformPoint(actorGround);
            float[] radii={2.5f,3f,3.5f};
            for(int radiusIndex=0;radiusIndex<radii.Length;radiusIndex++)
            for(int bearing=0;bearing<16;bearing++)
            {
                double angle=bearing*Math.PI/8d;
                var candidate=new SavedRoutePoint{X=local.x+Math.Cos(angle)*radii[radiusIndex],Z=local.z+Math.Sin(angle)*radii[radiusIndex]};
                var ground=geometry.ScenePoint(new RoutePoint(candidate.X,candidate.Z));
                float gap=Vector3.Distance(ground,actorGround);
                if(gap<2f || gap>4f)continue;
                var route=geometry.RouteBetweenPoints(new SavedRoutePoint{X=local.x,Z=local.z},candidate);
                if(route==null || route.Count<2)continue;
                observerGround=ground;return true;
            }
            return false;
        }

        private static double RouteLength(IList<SavedRoutePoint> route)
        {
            double length=0;
            if(route==null)return length;
            for(int i=1;i<route.Count;i++)
            {
                double dx=route[i].X-route[i-1].X,dz=route[i].Z-route[i-1].Z;
                length+=Math.Sqrt(dx*dx+dz*dz);
            }
            return length;
        }

        private static string DescribeRoute(IList<SavedRoutePoint> route)
        {
            if(route==null)return "null";
            var text=new StringBuilder();
            int shown=Math.Min(route.Count,1200);
            for(int i=0;i<shown;i++)
            {
                if(i>0)text.Append(';');
                text.Append(route[i].X.ToString("F2",CultureInfo.InvariantCulture)).Append(',')
                    .Append(route[i].Z.ToString("F2",CultureInfo.InvariantCulture));
            }
            if(shown<route.Count)text.Append(";...(truncated)");
            return text.ToString();
        }

        private static double ActionTime()
        {
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            return clock.ToSeconds()+(clock.Second-Math.Floor(clock.Second));
        }

        private static string DescribeActionMotion(StreetPopulation streets,string residentId,ResidentActor actor)
        {
            var motionsField=typeof(StreetPopulation).GetField("actionMotions",Private);
            var motions=motionsField==null?null:motionsField.GetValue(streets) as IDictionary;
            if(motions==null || !motions.Contains(residentId))return "missing";
            object motion=motions[residentId];if(motion==null)return "null";
            var type=motion.GetType();
            var outdoorField=type.GetField("Outdoor",AnyInstance);
            var nextField=type.GetField("Next",AnyInstance);
            var progressField=type.GetField("ProgressAt",AnyInstance);
            var repathField=type.GetField("RepathAt",AnyInstance);
            var outdoor=outdoorField==null?null:outdoorField.GetValue(motion) as IList<SavedRoutePoint>;
            int next=nextField==null?-1:(int)nextField.GetValue(motion);
            string nextPoint=outdoor!=null && next>=0 && next<outdoor.Count?
                outdoor[next].X.ToString("F2",CultureInfo.InvariantCulture)+","+outdoor[next].Z.ToString("F2",CultureInfo.InvariantCulture):"none";
            string nextNextPoint=outdoor!=null && next>=0 && next+1<outdoor.Count?
                outdoor[next+1].X.ToString("F2",CultureInfo.InvariantCulture)+","+outdoor[next+1].Z.ToString("F2",CultureInfo.InvariantCulture):"none";
            string progress=progressField==null?"unknown":((float)progressField.GetValue(motion)).ToString("F3",CultureInfo.InvariantCulture);
            string repath=repathField==null?"unknown":((float)repathField.GetValue(motion)).ToString("F3",CultureInfo.InvariantCulture);
            var crowd=actor?actor.GetComponent<CrowdMotion>():null;
            string steering="missing";
            if(crowd)
            {
                var crowdType=crowd.GetType();
                var sideField=crowdType.GetField("comfortPassingSide",AnyInstance);
                var lateralField=crowdType.GetField("comfortLateralOffset",AnyInstance);
                int side=sideField==null?0:(int)sideField.GetValue(crowd);
                double lateral=lateralField==null?0:(double)lateralField.GetValue(crowd);
                Vector3 ground=actor.transform.position-Vector3.up;
                steering="passingSide="+side+"; lateralOffset="+lateral.ToString("F3",CultureInfo.InvariantCulture)+
                    "; nearestNeighbours="+DescribeNearestNeighbours(crowd.NeighbourPositions(ground),ground)+
                    "; waypointClearance="+DescribeWaypointClearance(streets,crowd,actor,ground,outdoor,next,"next")+
                    (outdoor!=null && next>=0 && next+1<outdoor.Count ?
                        " | "+DescribeWaypointClearance(streets,crowd,actor,ground,outdoor,next+1,"nextNext") : string.Empty);
            }
            return "routeCount="+(outdoor==null?"null":outdoor.Count.ToString(CultureInfo.InvariantCulture))+"; next="+next+
                "; nextPoint="+nextPoint+"; nextNextPoint="+nextNextPoint+"; progressAt="+progress+"; repathAt="+repath+
                "; unscaledNow="+Time.unscaledTime.ToString("F3",CultureInfo.InvariantCulture)+
                "; steering="+steering;
        }

        private static string DescribeNearestNeighbours(IList<Vector3> neighbours,Vector3 center)
        {
            if(neighbours==null || neighbours.Count==0)return "none";
            var points=new Vector3[3];var distances=new float[]{float.PositiveInfinity,float.PositiveInfinity,float.PositiveInfinity};
            int count=0;
            foreach(var point in neighbours)
            {
                float distance=Vector3.Distance(center,point);int insert=count;
                if(insert>2)insert=2;
                while(insert>0 && distance<distances[insert-1])insert--;
                if(insert>=3)continue;
                int last=Math.Min(count,2);
                for(int i=last;i>insert;i--){distances[i]=distances[i-1];points[i]=points[i-1];}
                distances[insert]=distance;points[insert]=point;if(count<3)count++;
            }
            var text=new StringBuilder();
            for(int i=0;i<count;i++)
            {
                if(i>0)text.Append(';');
                text.Append(distances[i].ToString("F3",CultureInfo.InvariantCulture)).Append('@').Append(points[i].ToString("F3"));
            }
            return text.ToString();
        }

        private static string DescribeWaypointClearance(StreetPopulation streets,CrowdMotion crowd,ResidentActor actor,
            Vector3 from,IList<SavedRoutePoint> route,int index,string label)
        {
            if(route==null || index<0 || index>=route.Count)return label+"=none";
            var geometryField=typeof(StreetPopulation).GetField("geometry",Private);
            var geometry=geometryField==null?null:geometryField.GetValue(streets) as TownGeometry;
            if(geometry==null)return label+"=geometryMissing";
            var saved=route[index];
            Vector3 point=geometry.ScenePoint(new RoutePoint(saved.X,saved.Z));
            float radius=CrowdMotion.BodyRadius;
            Vector3 bottom=point+Vector3.up*(0.51f+radius),top=point+Vector3.up*1.65f;
            var overlaps=Physics.OverlapCapsule(bottom,top,radius,~0,QueryTriggerInteraction.Ignore);
            var overlapText=new StringBuilder();int overlapCount=0;
            foreach(var collider in overlaps)
            {
                if(!collider || IsIgnoredByCrowdMotion(crowd,collider))continue;
                overlapCount++;
                if(overlapCount<=8)AppendColliderDescription(overlapText,collider);
            }
            var supportHits=Physics.RaycastAll(point+Vector3.up*0.55f,Vector3.down,1.1f,~0,QueryTriggerInteraction.Ignore);
            bool supported=false;var supportText=new StringBuilder();int supportCount=0;
            foreach(var hit in supportHits)
            {
                var collider=hit.collider;
                if(!collider || IsIgnoredByCrowdMotion(crowd,collider))continue;
                supportCount++;
                bool candidate=hit.normal.y>=0.65f && Mathf.Abs(hit.point.y-point.y)<=0.5f;
                if(candidate)supported=true;
                if(supportCount<=6)
                {
                    if(supportText.Length>0)supportText.Append(" || ");
                    var description=new StringBuilder();AppendColliderDescription(description,collider);
                    supportText.Append(candidate?"candidate:":"hit:").Append(description);
                    supportText.Append("; normal=").Append(hit.normal.ToString("F2"));
                    supportText.Append("; point=").Append(hit.point.ToString("F2"));
                }
            }
            bool clear=false;string clearResult="unavailable";
            var clearMethod=typeof(CrowdMotion).GetMethod("Clear",AnyInstance);
            if(clearMethod!=null)
            {
                try { clear=(bool)clearMethod.Invoke(crowd,new object[]{from,point,null,null});clearResult=clear.ToString(); }
                catch(Exception error) { clearResult="error:"+error.GetBaseException().Message; }
            }
            return label+"="+point.ToString("F2")+"; clear="+clearResult+
                "; support="+supported+" hits="+supportCount+" ["+supportText+"]"+
                "; overlapHits="+overlapCount+" ["+overlapText+"]";
        }

        private static bool IsIgnoredByCrowdMotion(CrowdMotion crowd,Collider collider)
        {
            if(collider.transform==crowd.transform || collider.transform.IsChildOf(crowd.transform))return true;
            var ignore=typeof(CrowdMotion).GetMethod("Ignore",AnyInstance);
            return ignore!=null && (bool)ignore.Invoke(crowd,new object[]{collider});
        }

        private static void AppendColliderDescription(StringBuilder text,Collider collider)
        {
            if(text.Length>0)text.Append(" || ");
            var root=collider.transform.root;
            var bounds=collider.bounds;
            text.Append(collider.name).Append('<').Append(collider.GetType().Name).Append("> root=")
                .Append(root?root.name:"none").Append('<').Append(root?root.GetType().Name:"none").Append('>')
                .Append(" bounds=").Append(bounds.center.ToString("F2")).Append('/').Append(bounds.size.ToString("F2"));
        }

        private static bool TryResolveTarget(StreetPopulation streets,NpcActionJob job,out Vector3 target,out float arrival,out string reason)
        {
            target=Vector3.zero;arrival=0f;reason="Runtime action-target resolver is unavailable.";
            MethodInfo resolver=typeof(StreetPopulation).GetMethod("ResolveActionTarget",Private);if(resolver==null)return false;
            object[] arguments={job,Vector3.zero,0f,null};
            try
            {
                if(!(bool)resolver.Invoke(streets,arguments)){reason=arguments[3] as string ?? "Target is not currently resolvable.";return false;}
                target=(Vector3)arguments[1];arrival=(float)arguments[2];reason=null;return true;
            }
            catch(Exception error){reason=error.GetBaseException().Message;return false;}
        }

        private static int geometrySettlement(StreetPopulation streets)
        {
            var population=typeof(StreetPopulation).GetField("population",Private).GetValue(streets) as SettlementPopulation;
            return population==null?-1:population.SettlementId;
        }

        private static WorldStateStore GetStore(PeopleOfTheIliacBayMod host)
        {var field=typeof(PeopleOfTheIliacBayMod).GetField("store",Private);return field==null?null:field.GetValue(host) as WorldStateStore;}

        private static double TimeSeconds()
        {var clock=DaggerfallUnity.Instance.WorldTime.Now;return clock.ToSeconds()+(clock.Second-Math.Floor(clock.Second));}
    }
}
