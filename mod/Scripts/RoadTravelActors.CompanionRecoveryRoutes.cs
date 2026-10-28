using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class RoadTravelActors
    {
        private void AdvanceUnobservedDungeonRecoveries(WorldStateStore store,double now)
        {
            foreach(var candidate in store.PendingNpcActions())
            {
                if(!IsCompanionRecovering(candidate))continue;
                DungeonFollower binding;dungeonFollowers.TryGetValue(candidate.ResidentId,out binding);
                if(candidate.CompanionRecovery.RecruitmentArrival==null && candidate.CompanionRecovery.NativeReturnEstimateSeconds>0 &&
                    (binding==null || !binding.OriginTravelPreparationStarted))
                {
                    if(binding==null){binding=new DungeonFollower {ActionId=candidate.Id,MapId=candidate.LastPosition.SettlementId};dungeonFollowers[candidate.ResidentId]=binding;}
                    binding.OriginTravelPreparationStarted=true;StartCoroutine(PrepareCompanionOriginTravel(store,binding));
                }
                if(binding!=null && binding.Actor && binding.Actor.gameObject.activeInHierarchy)continue;
                var job=candidate;
                if(!NpcActionExecutor.IsTerminal(job) && (now>=job.DeadlineAt || job.FinishAt>0 && now>=job.FinishAt))
                {EndDungeonFollow(store,job,now,NpcActionObservation.Interrupted("The agreed service ended; measured recovery continues."));job=store.ReadNpcAction(job.Id);}
                var value=job.CompanionRecovery;
                if(value.RecruitmentArrival!=null && value.OverlandStartedAt==0 && now>=value.WaitUntil &&
                    (value.Phase==NpcCompanionRecoveryPhase.WaitingOutside || value.Phase==NpcCompanionRecoveryPhase.ReturningToOrigin))
                {store.TryBeginCompanionOverlandReturn(job.Id,job.Revision,now,value.WaitUntil);job=store.ReadNpcAction(job.Id);value=job.CompanionRecovery;}
                if(value.OverlandStartedAt>0 && now>=value.OverlandArrivesAt && !NpcFollowTransition.SameSpace(job.LastPosition,value.RecruitmentArrival))
                {store.TryArriveCompanionOverlandReturn(job.Id,job.Revision,now);job=store.ReadNpcAction(job.Id);value=job.CompanionRecovery;}
                var game=GameManager.Instance;var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
                bool destinationObserved=value.RecruitmentArrival!=null && !game.IsPlayerInside && directory &&
                    directory.MapID==value.RecruitmentArrival.SettlementId && NpcFollowTransition.SameSpace(job.LastPosition,value.RecruitmentArrival);
                bool originInteriorObserved=game.PlayerEnterExit.IsPlayerInsideBuilding && directory &&
                    value.RecruitmentOrigin.Kind==NpcActionTargetKind.IndoorPoint && directory.MapID==value.RecruitmentOrigin.SettlementId &&
                    game.PlayerEnterExit.Interior.EntryDoor.buildingKey==value.RecruitmentOrigin.BuildingKey;
                // Keep the saved journey pending until the watched destination
                // can receive it. A busy entry or loading plan must not lose the
                // only handoff by completing the action before a body can exist.
                if(originInteriorObserved && value.Phase==NpcCompanionRecoveryPhase.ReturningToOrigin &&
                    value.MeasuredRoute!=null && now>=value.MeasuredRoute.ArrivesAt &&
                    (!streets || !streets.CanReceiveRecoveredCompanionInterior(job,store)))continue;
                bool advanced=!destinationObserved && store.TryAdvanceUnobservedCompanionRecovery(job.Id,job.Revision,now);
                if(!advanced && job.CompanionRecovery.Phase!=NpcCompanionRecoveryPhase.ResumedRoutine)
                {if(binding!=null)binding.MapId=job.LastPosition.SettlementId;continue;}
                job=store.ReadNpcAction(job.Id);
                if(binding!=null)binding.MapId=job.LastPosition.SettlementId;
                if(job.CompanionRecovery.Phase==NpcCompanionRecoveryPhase.ResumedRoutine)
                {
                    if(streets && streets.TryReceiveRecoveredCompanionInterior(job,store))
                        RemoveDungeonFollower(job.ResidentId);
                    if(!NpcActionExecutor.IsTerminal(job))EndDungeonFollow(store,store.ReadNpcAction(job.Id),now,
                        NpcActionObservation.Interrupted("The companion completed its native timed return to the actual recruitment origin."));
                }
            }
        }

        // Capture before native source teardown. This is the same route proof
        // used for visible movement; game time advances its unobserved remainder.
        private void CaptureRecoveringDungeonDeparture(WorldStateStore store,double now,int map,Transform root)
        {
            foreach(var binding in dungeonFollowers.Values)
            {
                var job=store.ReadNpcAction(binding.ActionId);
                if(!IsCompanionRecovering(job) || !binding.Actor || !binding.Actor.gameObject.activeInHierarchy ||
                    !NpcFollowTransition.IsDungeonSpace(job.LastPosition) || job.LastPosition.SettlementId!=map)continue;
                SaveCompanionRecoveryPosition(store,binding,now,root,true,true);
                job=store.ReadNpcAction(job.Id);CaptureCompanionRecoveryRoute(store,binding,job,now,root,true);
            }
        }

        private static List<NpcActionTarget> MeasuredCompanionPath(ResidentActor actor,Vector3 from,Vector3 goal,Transform root,int map,bool inside)
        {
            var world=NativeNavigationWorld.Current;
            if(!world || !world.Ready || world.Scene!=(inside?"dungeon-":"city-")+map)return null;
            var path=new NavMeshPath();
            if(!NavMesh.CalculatePath(from,goal,NavMesh.AllAreas,path) || path.status!=NavMeshPathStatus.PathComplete ||
                path.corners.Length<1 || path.corners.Length>127 || !world.ClearCityGatePath(path.corners))return null;
            var route=new List<NpcActionTarget>{DungeonPoint(map,root,from,inside)};
            var previous=from;float length=0;
            for(int index=1;index<path.corners.Length;index++)
            {
                var next=path.corners[index];length+=Vector3.Distance(previous,next);
                if(length>4096 || !ClearNativeDoorApproach(actor,previous,next))return null;
                route.Add(DungeonPoint(map,root,next,inside));previous=next;
            }
            if(Vector3.Distance(previous,goal)>0.55f)return null;
            if(route.Count==1)route.Add(DungeonPoint(map,root,goal,inside));
            return route;
        }
        private static void CaptureCompanionRecoveryRoute(WorldStateStore store,DungeonFollower binding,NpcActionJob job,double now,Transform root,bool inside)
        {
            var value=job.CompanionRecovery;bool exiting=value.Phase==NpcCompanionRecoveryPhase.WaitingInside || value.Phase==NpcCompanionRecoveryPhase.ReturningToExit;
            var endpoint=exiting?value.InteriorEntrance:value.RecruitmentOrigin;
            if(!binding.Actor || !NpcFollowTransition.SameSpace(job.LastPosition,endpoint))return;
            var from=binding.Actor.transform.position-Vector3.up;
            var goal=root.TransformPoint(new Vector3((float)endpoint.X,(float)endpoint.Y,(float)endpoint.Z));
            var route=MeasuredCompanionPath(binding.Actor,from,goal,root,binding.MapId,inside);
            if(route==null)return;
            store.TryCaptureCompanionRecoveryRoute(job.Id,job.Revision,now,route,exiting?value.ExteriorEntrance:value.RecruitmentOrigin,
                DaggerfallUnity.Instance.WorldTime.TimeScale/binding.Actor.WalkingSpeed);
        }

        private static bool TryActualInteriorEntrance(DaggerfallDungeon dungeon,out Vector3 point)
        {
            point=Vector3.zero;var doors=DaggerfallStaticDoors.FindDoorsInCollections(dungeon.StaticDoorCollections,DoorTypes.DungeonExit);
            if(doors==null || doors.Length==0 || !dungeon.StartMarker)return false;
            float nearest=float.MaxValue;
            foreach(var door in doors)
            {
                var candidate=StreetPopulation.FollowExteriorDoorGround(door);Vector3 floor;
                if(!VerifiedDungeonFloor(candidate,dungeon.transform,out floor))continue;
                float distance=Vector3.Distance(floor,dungeon.StartMarker.transform.position);
                if(distance>=nearest)continue;point=floor;nearest=distance;
            }
            return nearest<float.MaxValue;
        }
    }
}
