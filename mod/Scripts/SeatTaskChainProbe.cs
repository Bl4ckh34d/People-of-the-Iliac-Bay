using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Opt-in native fixture. It uses a surveyed live seat and the normal task
    // admission, actor update, timed action, and cancellation paths.
    internal static class SeatTaskChainProbe
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private const int SitSeconds=120;

        internal static IEnumerator Run(StreetPopulation streets,string expectedCharacter)
        {return Run(streets,expectedCharacter,null);}

        internal static IEnumerator Run(StreetPopulation streets,string expectedCharacter,string residentId)
        {
            bool explicitFlag=Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-seat-task-chain-probe")>=0;
            bool roadmapFlag=Environment.GetEnvironmentVariable("POTIB_ROADMAP_PROBE")=="seat-task" &&
                (Environment.GetEnvironmentVariable("POTIB_NAVMESH_TRIAL")=="1" &&
                    Environment.GetEnvironmentVariable("POTIB_NAVMESH_INTERIOR")=="263177" ||
                 residentId!=null && Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-action-probe")>=0);
            if((!explicitFlag && !roadmapFlag) || string.IsNullOrEmpty(expectedCharacter) ||
                !NativeProbeText.IsTestCharacter(expectedCharacter))
                throw new InvalidOperationException("Seat task chain requires its owned native fixture flag.");
            var game=GameManager.Instance;
            if(!streets || game==null || game.PlayerEntity.Name!=expectedCharacter)
                throw new InvalidOperationException("Seat task chain requires a loaded owned character.");
            float readyUntil=Time.realtimeSinceStartup+30;
            while(Time.realtimeSinceStartup<readyUntil && (!streets.Ready || !game.PlayerEnterExit.Interior ||
                typeof(StreetPopulation).GetField("shopGeometry",Private).GetValue(streets)==null))yield return null;
            if(!streets.Ready || !game.PlayerEnterExit.Interior)
                throw new InvalidOperationException("Seat task chain interior population did not become ready.");
            var geometry=(InteriorGeometry)typeof(StreetPopulation).GetField("indoorGeometry",Private).GetValue(streets);
            var activities=(ShopVisitGeometry)typeof(StreetPopulation).GetField("shopGeometry",Private).GetValue(streets);
            var store=(WorldStateStore)typeof(StreetPopulation).GetField("worldStore",Private).GetValue(streets);
            var actors=(Dictionary<string,ResidentActor>)typeof(StreetPopulation).GetField("actors",Private).GetValue(streets);
            var room=game.PlayerEnterExit.Interior;
            var catalog=activities==null?null:activities.Seating;
            if(geometry==null || catalog==null || catalog.Slots.Count==0 || store==null || actors==null)
                throw new InvalidOperationException("Seat task chain requires an actual surveyed seat and live resident catalog.");
            Vector3 playerBefore=game.PlayerMotor.transform.position;
            ResidentActor actor=null;
            NpcActionOption seat=null,move=null;
            FurnitureSeatSlot slot=null;
            string activeTaskId=null;
            try
            {
                foreach(var candidate in new List<ResidentActor>(actors.Values))
                {
                    if(residentId!=null && candidate && candidate.Resident.Id!=residentId)continue;
                    if(!candidate || !candidate.gameObject.activeInHierarchy || candidate.IsSeated || candidate.IsSleeping ||
                        candidate.IsConversing || !candidate.Resident.IsAlive || candidate.Resident.IsQuestProtected ||
                        candidate.Resident.AgeGroup!=ResidentAgeGroup.Adult ||
                        store.ActiveNpcAction(candidate.Resident.Id)!=null || store.ActiveNpcTask(candidate.Resident.Id)!=null)
                        continue;
                    foreach(var local in geometry.LocalAnchors)
                    {
                        Vector3 point=room.transform.TransformPoint(local)+Vector3.up;
                        float gap=Vector3.Distance(point,candidate.transform.position);
                        if(gap<3.5f || gap>6.5f || Math.Abs(point.y-candidate.transform.position.y)>0.3f)continue;
                        game.PlayerMotor.transform.position=point;Physics.SyncTransforms();
                        var seats=streets.ConversationActionOptions(candidate.Resident.Id,"Take a seat for 120 seconds.");
                        var moves=streets.ConversationActionOptions(candidate.Resident.Id,"Come here.");
                        var chosen=seats.Find(option=>option.Kind==NpcActionKind.TakeSeat);
                        var walking=moves.Find(option=>option.Kind==NpcActionKind.MoveTo &&
                            option.Target!=null && option.Target.Kind==NpcActionTargetKind.Person && option.Target.PersonId=="player");
                        var surveyed=chosen==null || chosen.Target==null?null:catalog.Find(chosen.Target.ContainerId);
                        if(chosen==null || walking==null || surveyed==null || !surveyed.HasApproach ||
                            Vector3.Distance(surveyed.Ground,point-Vector3.up)<3.5f)continue;
                        actor=candidate;seat=chosen;move=walking;slot=surveyed;break;
                    }
                    if(actor)break;
                }
                if(!actor)throw new InvalidOperationException("No live resident, surveyed seat and reachable public observer were available for the seat chain.");
                TownSmokeHarness.Watch(actor.transform);

                NpcTaskPlan task;
                NpcActionJob first,second;
                string reason;
                Accept(streets,store,actor,seat,move,out task,out first);
                activeTaskId=task.Id;
                if(first.Kind!=NpcActionKind.TakeSeat || first.Target.ContainerId!=slot.Id ||
                    Math.Abs(first.FinishAt-first.AcceptedAt-SitSeconds)>0.01 ||
                    !catalog.Owns(actor.Resident.Id,slot.Id,Time.unscaledTime))
                    throw new InvalidOperationException("Task did not reserve the exact surveyed seat for its saved duration.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SEAT_TASK_START: resident="+actor.Resident.Id+
                    "; seat="+slot.Id+"; finish="+first.FinishAt+"; observer="+game.PlayerMotor.transform.position.ToString("F3"));

                double seatedAt=-1,lastSeatedAt=-1;
                bool held=false;
                float deadline=Time.realtimeSinceStartup+180;
                Vector3 previous=actor.transform.position;
                second=null;
                while(Time.realtimeSinceStartup<deadline)
                {
                    if(!actor || !actor.gameObject.activeInHierarchy)
                        throw new InvalidOperationException("Seat task lost its live resident before the next step.");
                    if(Vector3.Distance(previous,actor.transform.position)>0.75f)
                        throw new InvalidOperationException("Seat task actor jumped rather than walking through the chair transition.");
                    previous=actor.transform.position;
                    first=store.ReadNpcAction(task.Id+":step:0");
                    second=store.ReadNpcAction(task.Id+":step:1");
                    if(first==null || first.Phase==NpcActionPhase.Failed || first.Phase==NpcActionPhase.Cancelled ||
                        second!=null && first.Phase!=NpcActionPhase.Completed)
                        throw new InvalidOperationException("Seat task advanced without a confirmed timed first step: "+
                            (first==null?"missing":first.Phase+" "+first.FailureReason));
                    if(actor.IsSeated)
                    {
                        double now=GameTime();
                        if(!slot.Furniture.Contains(actor.transform.position-Vector3.up,0.05f) ||
                            !catalog.Owns(actor.Resident.Id,slot.Id,Time.unscaledTime))
                            throw new InvalidOperationException("Resident displayed seated art without occupying its reserved furniture.");
                        if(seatedAt<0)seatedAt=now;
                        lastSeatedAt=now;
                        if(now-seatedAt>=10 && now<first.FinishAt)held=true;
                    }
                    else if(seatedAt>=0 && first.Phase!=NpcActionPhase.Completed && GameTime()<first.FinishAt)
                        throw new InvalidOperationException("Resident left the seat before its agreed finish time.");
                    if(second!=null)break;
                    yield return null;
                }
                if(second==null || first.Phase!=NpcActionPhase.Completed || first.UpdatedAt<first.FinishAt ||
                    seatedAt<0 || lastSeatedAt-seatedAt<10 || !held || second.Kind!=NpcActionKind.MoveTo ||
                    second.AcceptedAt<first.UpdatedAt)
                    throw new InvalidOperationException("Timed seat did not complete before the next saved MoveTo step.");
                if(actor.IsSeated || catalog.Owns(actor.Resident.Id,slot.Id,Time.unscaledTime) ||
                    slot.Furniture.Contains(actor.transform.position-Vector3.up,0.02f))
                    throw new InvalidOperationException("Next task step began before physical standing and seat release.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SEAT_TASK_HANDOFF: seatedGameSeconds="+
                    (lastSeatedAt-seatedAt).ToString("F1")+"; stood=true; claimReleased=true; next="+second.Id);

                Vector3 standing=actor.transform.position;
                deadline=Time.realtimeSinceStartup+90;
                while(Time.realtimeSinceStartup<deadline)
                {
                    if(!actor || !actor.gameObject.activeInHierarchy || actor.IsSeated ||
                        catalog.Owns(actor.Resident.Id,slot.Id,Time.unscaledTime))
                        throw new InvalidOperationException("Seat or claim returned during the walking task step.");
                    task=store.ReadNpcTask(task.Id);
                    second=store.ReadNpcAction(second.Id);
                    if(task==null || second==null || task.Phase==NpcTaskPhase.Failed || task.Phase==NpcTaskPhase.Cancelled ||
                        second.Phase==NpcActionPhase.Failed || second.Phase==NpcActionPhase.Cancelled)
                        throw new InvalidOperationException("MoveTo after sitting failed: "+(task==null?"missing":task.FailureReason));
                    if(task.Phase==NpcTaskPhase.Completed)break;
                    yield return null;
                }
                if(task.Phase!=NpcTaskPhase.Completed || second.Phase!=NpcActionPhase.Completed ||
                    Vector3.Distance(standing,actor.transform.position)<1 ||
                    Vector3.Distance(actor.transform.position,game.PlayerMotor.transform.position)>1.65f ||
                    string.IsNullOrWhiteSpace(task.Steps[0].CompletedEffectId) ||
                    string.IsNullOrWhiteSpace(task.Steps[1].CompletedEffectId))
                    throw new InvalidOperationException("Resident did not physically complete MoveTo after standing.");
                activeTaskId=null;

                var freshSeat=streets.ConversationActionOptions(actor.Resident.Id,"Take a seat for 120 seconds.")
                    .Find(option=>option.Kind==NpcActionKind.TakeSeat);
                var freshMove=streets.ConversationActionOptions(actor.Resident.Id,"Come here.")
                    .Find(option=>option.Kind==NpcActionKind.MoveTo && option.Target!=null &&
                        option.Target.Kind==NpcActionTargetKind.Person && option.Target.PersonId=="player");
                if(freshSeat==null || freshMove==null)
                    throw new InvalidOperationException("Completed seat chain left no usable seat for cancellation check.");
                var cancelledSlot=catalog.Find(freshSeat.Target.ContainerId);
                if(cancelledSlot==null)throw new InvalidOperationException("Cancellation seat is not surveyed.");
                Accept(streets,store,actor,freshSeat,freshMove,out task,out first);
                activeTaskId=task.Id;
                deadline=Time.realtimeSinceStartup+75;
                while(Time.realtimeSinceStartup<deadline && !actor.IsSeated)
                {
                    first=store.ReadNpcAction(task.Id+":step:0");
                    if(first==null || NpcActionExecutor.IsTerminal(first))
                        throw new InvalidOperationException("Cancellation fixture never occupied its second seat.");
                    yield return null;
                }
                if(!actor.IsSeated || !catalog.Owns(actor.Resident.Id,cancelledSlot.Id,Time.unscaledTime))
                    throw new InvalidOperationException("Cancellation fixture did not reserve and occupy a real seat.");
                if(!streets.TryStopConversationAction(actor.Resident.Id,out reason))
                    throw new InvalidOperationException("Stopping seated task was rejected: "+reason);
                task=store.ReadNpcTask(task.Id);first=store.ReadNpcAction(task.Id+":step:0");
                if(task==null || task.Phase!=NpcTaskPhase.Cancelled || first==null ||
                    first.Phase!=NpcActionPhase.Cancelled || store.ReadNpcAction(task.Id+":step:1")!=null)
                    throw new InvalidOperationException("Stopping seated task did not cancel both the action and queued step.");
                deadline=Time.realtimeSinceStartup+30;
                while(Time.realtimeSinceStartup<deadline && (actor.IsSeated ||
                    catalog.Owns(actor.Resident.Id,cancelledSlot.Id,Time.unscaledTime)))yield return null;
                if(actor.IsSeated || catalog.Owns(actor.Resident.Id,cancelledSlot.Id,Time.unscaledTime) ||
                    cancelledSlot.Furniture.Contains(actor.transform.position-Vector3.up,0.02f) ||
                    store.ReadNpcAction(task.Id+":step:1")!=null)
                    throw new InvalidOperationException("Cancelled seat task retained its chair or dispatched a later step.");
                activeTaskId=null;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SEAT_TASK_CHAIN_PASS: realSeat=true; timedOccupancy=true; physicalStandBeforeMove=true; claimReleased=true; moveArrived=true; cancellationLeavesSeat=true; queuedStepBlocked=true.");
            }
            finally
            {
                if(activeTaskId!=null && actor)
                {
                    string ignored;streets.TryStopConversationAction(actor.Resident.Id,out ignored);
                }
                TownSmokeHarness.Watch(null);
                game.PlayerMotor.transform.position=playerBefore;Physics.SyncTransforms();
            }
        }

        private static void Accept(StreetPopulation streets,WorldStateStore store,ResidentActor actor,
            NpcActionOption seat,NpcActionOption move,out NpcTaskPlan task,out NpcActionJob first)
        {
            var decisions=new List<NpcActionDecision> {
                new NpcActionDecision {Option=seat,DurationSeconds=SitSeconds},
                new NpcActionDecision {Option=move,DurationSeconds=300}};
            string reason;
            if(!NpcProbeCommandSource.TryAcceptTask(streets,actor.Resident.Id,
                "native-seat-task:"+Guid.NewGuid().ToString("N"),decisions,
                "Take a seat for 120 seconds, then come here.",out reason))
                throw new InvalidOperationException("Native seat task admission failed: "+reason);
            task=store.ActiveNpcTask(actor.Resident.Id);
            first=task==null?null:store.ReadNpcAction(task.Id+":step:0");
            if(task==null || task.Steps.Count!=2 || task.Steps[0].Kind!=NpcActionKind.TakeSeat ||
                task.Steps[0].DurationSeconds!=SitSeconds || task.Steps[1].Kind!=NpcActionKind.MoveTo ||
                first==null || first.Phase!=NpcActionPhase.Accepted)
                throw new InvalidOperationException("Native seat task did not save and dispatch its first timed step.");
        }

        private static double GameTime()
        {
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            return clock.ToSeconds()+(clock.Second-Math.Floor(clock.Second));
        }
    }
}
