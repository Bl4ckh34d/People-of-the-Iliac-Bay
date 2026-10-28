using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Explicit disposable-player fixture only. The caller supplies actual native
    // door interaction and walks the player clear of the landing; this probe does
    // not change scenes, teleport bodies, create inventory or write player saves.
    public static partial class NpcFollowTransitionProbe
    {
        private const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
        public static IEnumerator RunDoorway(StreetPopulation streets,string residentId,string expectedCharacter,
            Func<IEnumerator> crossDoor,bool stopAfter=true,Func<IEnumerator> beforeCross=null,
            string requiredActionId=null,double requiredFinishAt=0,double requiredDeadlineAt=0)
        {
            var game=GameManager.Instance;
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-follow-transition-probe")<0 ||
                string.IsNullOrEmpty(expectedCharacter) || !NativeProbeText.IsTestCharacter(expectedCharacter) ||
                game.PlayerEntity.Name!=expectedCharacter || !streets || !streets.Ready || crossDoor==null)
                throw new InvalidOperationException("Follow transition probe requires an explicitly owned fixture and native door callback.");
            var store=(WorldStateStore)typeof(StreetPopulation).GetField("worldStore",Private).GetValue(streets);
            var actors=(Dictionary<string,ResidentActor>)typeof(StreetPopulation).GetField("actors",Private).GetValue(streets);
            ResidentActor actor;
            if(!actors.TryGetValue(residentId,out actor) || !actor || !actor.gameObject.activeInHierarchy)
                throw new InvalidOperationException("The specified ordinary resident must already be nearby and visible.");
            var job=store.ActiveNpcAction(residentId);string reason=null;
            if(!string.IsNullOrEmpty(requiredActionId) && !MatchesRequiredFollow(job,requiredActionId,requiredFinishAt,requiredDeadlineAt,Now()))
                throw new InvalidOperationException("The second doorway requires the same live Follow job and original consent window.");
            if(job==null || NpcActionExecutor.IsTerminal(job))
            {
                if(!string.IsNullOrEmpty(requiredActionId))
                    throw new InvalidOperationException("The original Follow job expired or ended; refusing to accept a replacement for the second doorway.");
                bool accepted=false;
                foreach(var option in streets.ConversationActionOptions(residentId,"Follow me."))
                    if(option.Kind==NpcActionKind.Follow && NpcProbeCommandSource.TryAcceptAction(streets,residentId,"follow-probe:"+Guid.NewGuid().ToString("N"),
                        new NpcActionDecision {Option=option,DurationSeconds=300},out reason)){accepted=true;break;}
                if(!accepted)throw new InvalidOperationException("The resident did not accept following: "+reason);
                job=store.ActiveNpcAction(residentId);
            }
            if(job.Kind!=NpcActionKind.Follow || job.FollowTransition!=null)
                throw new InvalidOperationException("The fixture resident is busy with a different action or doorway.");
            string expectedActionId=job.Id;double expectedFinishAt=job.FinishAt,expectedDeadlineAt=job.DeadlineAt;
            if(beforeCross!=null)yield return beforeCross();
            job=store.ActiveNpcAction(residentId);
            if(job==null || job.Kind!=NpcActionKind.Follow || job.FollowTransition!=null || job.Id!=expectedActionId ||
                job.FinishAt!=expectedFinishAt || job.DeadlineAt!=expectedDeadlineAt || NpcActionExecutor.IsTerminal(job) ||
                Now()>=job.DeadlineAt || job.FinishAt>0 && Now()>=job.FinishAt ||
                !string.IsNullOrEmpty(requiredActionId) && !MatchesRequiredFollow(job,requiredActionId,requiredFinishAt,requiredDeadlineAt,Now()))
                throw new InvalidOperationException("Follow ownership changed during the pre-door conversation.");
            actor.CaptureInventory();var inventory=store.ReadNpcInventory(residentId);
            string actionId=job.Id;double finishAt=job.FinishAt;bool sourceInside=game.PlayerEnterExit.IsPlayerInsideBuilding;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FOLLOW_DOOR_BEGIN: resident="+residentId+"; action="+actionId+"; inside="+sourceInside+"; consentLeft="+(finishAt-Now()));
            WorldState transitSave=null;NpcActionJob crossing=null;
            PlayerEnterExit.OnPreTransitionEventHandler capture=args=>
            {
                var pending=store.ReadNpcAction(actionId);
                if(pending!=null && pending.FollowTransition!=null)
                {
                    crossing=pending;transitSave=store.Snapshot();
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FOLLOW_DOOR_SAVED: points="+pending.FollowTransition.SourceRoute.Count+
                        "; walkGameSeconds="+(pending.FollowTransition.ArrivesAt-pending.FollowTransition.StartedAt)+
                        "; destination="+pending.FollowTransition.Destination.Kind+":"+pending.FollowTransition.Destination.BuildingKey);
                }
            };
            PlayerEnterExit.OnPreTransition+=capture;
            var audit=new DoorwayAudit();
            var sourceParent=actor.transform.parent;
            var monitor=streets.StartCoroutine(AuditDoorway(streets,residentId,()=>crossing,sourceInside,sourceParent,audit));
            try {yield return crossDoor();}
            finally {PlayerEnterExit.OnPreTransition-=capture;streets.StopCoroutine(monitor);}
            if(audit.Failure!=null)throw new InvalidOperationException(audit.Failure);
            if(crossing==null || transitSave==null || sourceInside==game.PlayerEnterExit.IsPlayerInsideBuilding)
            {
                var failed=store.ReadNpcAction(actionId);
                throw new InvalidOperationException("The native doorway did not create a saved follow transit: "+
                    (failed==null?"missing action":failed.Phase+"; "+failed.FailureReason));
            }
            string encoded=SaveLoadManager.Serialize(typeof(WorldState),transitSave,false);
            var decoded=(WorldState)SaveLoadManager.Deserialize(typeof(WorldState),encoded);
            var decodedJob=decoded==null || decoded.NpcActions==null?null:decoded.NpcActions.Find(item=>item!=null && item.Id==actionId);
            var branch=new WorldStateStore(1);
            try {branch.Restore(decoded);}
            catch(Exception error)
            {
                throw new InvalidOperationException("Native follow restore validation failed: "+error.Message+
                    "; source="+DescribeSavedFollow(crossing)+"; decoded="+DescribeSavedFollow(decodedJob),error);
            }
            var restored=branch.ReadNpcAction(actionId);
            double arrivalDelta=restored==null || restored.FollowTransition==null?double.NaN:
                restored.FollowTransition.ArrivesAt-crossing.FollowTransition.ArrivesAt;
            string restoreDiagnostic="source="+DescribeSavedFollow(crossing)+"; decoded="+DescribeSavedFollow(decodedJob)+
                "; restored="+DescribeSavedFollow(restored)+"; arrivalDelta="+arrivalDelta.ToString("R",CultureInfo.InvariantCulture);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FOLLOW_NATIVE_RESTORE: "+restoreDiagnostic);
            // Legacy FullSerializer prints doubles with 15 significant digits.
            // At the native ~1.26e10 epoch its verified error is tens of microseconds.
            // Require the entire serialized job to remain identical as well as a
            // strict sub-millisecond arrival bound; do not excuse missing fields.
            if(restored==null || restored.FollowTransition==null || restored.ResidentId!=residentId || restored.Target==null ||
                restored.Target.PersonId!="player" || double.IsNaN(arrivalDelta) || Math.Abs(arrivalDelta)>0.0001 ||
                SaveLoadManager.Serialize(typeof(NpcActionJob),crossing,false)!=SaveLoadManager.Serialize(typeof(NpcActionJob),restored,false))
                throw new InvalidOperationException("Native FullSerializer changed the pending doorway job: "+restoreDiagnostic);
            if(branch.TryArriveNpcFollowTransition(actionId,restored.Revision,restored.FollowTransition.ArrivesAt-0.001,
                restored.FollowTransition.Destination))
                throw new InvalidOperationException("Restored doorway job allowed arrival before its saved deadline: "+restoreDiagnostic);
            // Cancellation in an independent restore must consume the pending route.
            if(!branch.ObserveNpcAction(actionId,restored.Revision,restored.UpdatedAt,NpcActionObservation.Cancelled("Probe stop.")) ||
                branch.ReadNpcAction(actionId).FollowTransition!=null)
                throw new InvalidOperationException("Restored doorway transit did not cancel cleanly.");
            float deadline=Time.realtimeSinceStartup+100;bool arrived=false;
            while(Time.realtimeSinceStartup<deadline)
            {
                job=store.ReadNpcAction(actionId);
                if(job==null || NpcActionExecutor.IsTerminal(job))
                {
                    var failed=streets.FindActor(residentId);
                    var motions=(IDictionary)typeof(StreetPopulation).GetField("actionMotions",Private).GetValue(streets);
                    object motion=motions.Contains(residentId)?motions[residentId]:null;
                    var remaining=motion==null?null:(InteriorRoomFollower)motion.GetType().GetField("Indoor").GetValue(motion);
                    string route="none";
                    if(remaining!=null && failed && game.PlayerEnterExit.IsPlayerInsideBuilding)
                    {
                        var points=remaining.RemainingRoute(game.PlayerEnterExit.Interior.transform.InverseTransformPoint(failed.transform.position-Vector3.up));
                        route=string.Join(" ",points.ConvertAll(p=>new Vector3((float)p.X,(float)p.Y,(float)p.Z).ToString("F3")).ToArray());
                    }
                    throw new InvalidOperationException("Following interrupted: "+(job==null?"missing":job.FailureReason)+
                        "; body="+(failed?(failed.transform.position-Vector3.up).ToString("F3"):"missing")+
                        "; observer="+(game.PlayerMotor.transform.position-Vector3.up).ToString("F3")+
                        "; goal="+(motion==null?"none":motion.GetType().GetField("Goal").GetValue(motion).ToString())+"; localRoute="+route);
                }
                int bodies=0;ResidentActor body=null;
                foreach(var candidate in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                    if(candidate.gameObject.activeInHierarchy && candidate.Resident.Id==residentId){bodies++;body=candidate;}
                if(bodies>1)throw new InvalidOperationException("A doorway created duplicate bodies for the same resident.");
                var clock=DaggerfallUnity.Instance.WorldTime.Now;
                double now=clock.ToSeconds()+(clock.Second-Math.Floor(clock.Second));
                if(now<crossing.FollowTransition.ArrivesAt && bodies!=0)
                    throw new InvalidOperationException("The follower appeared before its source route could finish.");
                if(job.FollowTransition==null && bodies==1 && Vector3.Distance(body.transform.position,game.PlayerMotor.transform.position)<2.1f)
                {
                    var expectedParent=sourceInside?((BuildingDirectory)typeof(StreetPopulation).GetField("directory",Private).GetValue(streets)).transform:
                        game.PlayerEnterExit.Interior.transform;
                    if(body.transform.parent!=expectedParent)
                        throw new InvalidOperationException("Follower body belongs to the wrong scene.");
                    body.CaptureInventory();arrived=true;break;
                }
                yield return null;
            }
            if(!arrived)throw new InvalidOperationException("Follower did not arrive; leave the actual doorway clear and remain nearby.");
            var after=store.ReadNpcInventory(residentId);
            if(inventory==null || after==null || inventory.WalletGold!=after.WalletGold || inventory.Items.Count!=after.Items.Count)
                throw new InvalidOperationException("Follower inventory ownership changed at the doorway.");
            foreach(var item in inventory.Items)
                if(!after.Items.Exists(i=>i.Uid==item.Uid && i.NativePayload==item.NativePayload))
                    throw new InvalidOperationException("Follower item UID or native payload changed at the doorway.");
            if(job.FinishAt!=finishAt || job.Id!=actionId || job.ResidentId!=residentId)
                throw new InvalidOperationException("Doorway arrival replaced identity or extended consent.");
            if(stopAfter)
            {
                if(!streets.TryStopConversationAction(residentId,out reason))throw new InvalidOperationException("Follow stop failed: "+reason);
                job=store.ReadNpcAction(actionId);
                if(job.Phase!=NpcActionPhase.Cancelled || !job.ReturningToRoutine || job.FollowTransition!=null)
                    throw new InvalidOperationException("Stop did not release following into physical routine recovery.");
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FOLLOW_TRANSITION_PASS direction="+(sourceInside?"outdoor":"indoor")+
                " identity inventory native-save delayed-arrival unique-body"+(stopAfter?" stop-recovery":""));
        }

        private static bool MatchesRequiredFollow(NpcActionJob job,string actionId,double finishAt,double deadlineAt,double now)
        {
            return job!=null && !NpcActionExecutor.IsTerminal(job) && job.Kind==NpcActionKind.Follow &&
                job.FollowTransition==null && job.Id==actionId && job.FinishAt==finishAt && job.DeadlineAt==deadlineAt &&
                now<job.DeadlineAt && (job.FinishAt<=0 || now<job.FinishAt);
        }
        private sealed class DoorwayAudit {public string Failure;public bool Seen;public int SourceCleanupFrame=-1;}
        private static string DescribeSavedFollow(NpcActionJob job)
        {
            if(job==null)return "missing-job";
            var transit=job.FollowTransition;
            return "[id="+job.Id+", resident="+job.ResidentId+", phase="+job.Phase+", reason="+(job.FailureReason??"none")+
                ", target="+(job.Target==null?"null":job.Target.Kind+":"+job.Target.PersonId)+", revision="+job.Revision+
                ", updated="+job.UpdatedAt.ToString("R",CultureInfo.InvariantCulture)+", transit="+(transit==null?"null":
                "points:"+(transit.SourceRoute==null?"null":transit.SourceRoute.Count.ToString(CultureInfo.InvariantCulture))+
                "/destination:"+(transit.Destination==null?"null":transit.Destination.Kind+":"+transit.Destination.BuildingKey)+
                "/started:"+transit.StartedAt.ToString("R",CultureInfo.InvariantCulture)+
                "/arrives:"+transit.ArrivesAt.ToString("R",CultureInfo.InvariantCulture))+"]";
        }
        private static string BodyDescription(ResidentActor body)
        {
            if(!body)return "none";
            var parent=body.transform.parent;
            return body.GetInstanceID()+"@"+(parent?parent.name+"#"+parent.GetInstanceID():"no-parent");
        }
        private static IEnumerator AuditDoorway(StreetPopulation streets,string residentId,Func<NpcActionJob> crossing,bool sourceInside,
            Transform sourceParent,DoorwayAudit audit)
        {
            // Monitor every frame while the caller's nested native-transition and
            // observer-walking coroutines run. A late assertion alone can miss an
            // NPC that spawned prematurely and walked away before the callback ended.
            while(audit.Failure==null)
            {
                var job=crossing();var game=GameManager.Instance;
                if(job!=null && game.PlayerEnterExit.IsPlayerInsideBuilding!=sourceInside)
                {
                    int count=0;ResidentActor body=null;
                    foreach(var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                        if(actor.gameObject.activeInHierarchy && actor.Resident.Id==residentId){count++;body=actor;}
                    double now=Now();
                    string diagnostic=" frame="+Time.frameCount+"; now="+now+"; arrives="+job.FollowTransition.ArrivesAt+
                        "; bodies="+count+"; body="+BodyDescription(body);
                    var town=(BuildingDirectory)typeof(StreetPopulation).GetField("directory",Private).GetValue(streets);
                    var room=game.PlayerEnterExit.Interior;
                    Transform destinationParent=sourceInside?(town?town.transform:null):(room?room.transform:null);
                    if(count>1)audit.Failure="Doorway audit found duplicate resident bodies during the native callback."+diagnostic;
                    else if(body && sourceParent && body.transform.parent==sourceParent)
                    {
                        // DFU flips native location state before deferred Destroy
                        // removes the old interior and our next Update clears actors.
                        // Only that one source frame is exempt from destination timing.
                        if(audit.SourceCleanupFrame<0)
                        {audit.SourceCleanupFrame=Time.frameCount;Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FOLLOW_SOURCE_CLEANUP:"+diagnostic);}
                        else if(Time.frameCount>audit.SourceCleanupFrame)
                            audit.Failure="Doorway source body survived its cleanup frame."+diagnostic;
                    }
                    else if(body && (!destinationParent || body.transform.parent!=destinationParent))
                        audit.Failure="Doorway body belongs to neither the source nor destination scene."+diagnostic;
                    else if(body && now<job.FollowTransition.ArrivesAt)
                        audit.Failure="Doorway audit observed a destination body before source walking time elapsed."+diagnostic;
                    else if(body && !audit.Seen)
                    {
                        Vector3 entrance;
                        if(!sourceInside)
                        {
                            var geometry=RoomGeometry(streets);
                            if(geometry==null || !geometry.EntryGround.HasValue)
                            {audit.Failure="Doorway audit found a follower without a surveyed interior entrance.";yield break;}
                            entrance=geometry.EntryGround.Value;
                        }
                        else entrance=OutsideGeometry(streets).ScenePoint(new RoutePoint(job.FollowTransition.Destination.X,job.FollowTransition.Destination.Z));
                        if(Vector3.Distance(body.transform.position-Vector3.up,entrance)>1)
                            audit.Failure="Doorway audit found first appearance away from the actual entrance."+diagnostic;
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FOLLOW_DESTINATION_BODY:"+diagnostic+"; entrance="+entrance+"; ground="+(body.transform.position-Vector3.up));
                        audit.Seen=true;
                    }
                }
                yield return null;
            }
        }
    }
}
