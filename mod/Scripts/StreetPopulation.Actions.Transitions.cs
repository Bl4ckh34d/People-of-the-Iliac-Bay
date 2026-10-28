using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private void RegisterNpcFollowTransitions()
        {
            PlayerEnterExit.OnPreTransition-=PrepareNpcFollowTransition;
            PlayerEnterExit.OnPreTransition+=PrepareNpcFollowTransition;
            PlayerEnterExit.OnFailedTransition-=AbortNpcRoutineRecoveryTransition;
            PlayerEnterExit.OnFailedTransition+=AbortNpcRoutineRecoveryTransition;
            PlayerEnterExit.OnFailedTransition-=AbortNpcFollowTransition;
            PlayerEnterExit.OnFailedTransition+=AbortNpcFollowTransition;
        }
        private void UnregisterNpcFollowTransitions()
        {
            PlayerEnterExit.OnPreTransition-=PrepareNpcFollowTransition;
            PlayerEnterExit.OnFailedTransition-=AbortNpcRoutineRecoveryTransition;
            PlayerEnterExit.OnFailedTransition-=AbortNpcFollowTransition;
        }
        internal static Vector3 FollowExteriorDoorGround(StaticDoor door)
        {
            // DFU GetDoorPosition returns the model's door centre, not its floor.
            // Project the actual lower edge through the same owner/building matrix
            // before comparing it with the terrain-based pedestrian route endpoint.
            var bottom=door;bottom.centre-=Vector3.up*(Mathf.Abs(door.size.y)*0.5f);
            var normal=DaggerfallStaticDoors.GetDoorNormal(door);normal.y=0;
            return DaggerfallStaticDoors.GetDoorPosition(bottom)+normal.normalized*0.6f;
        }
        private void PrepareNpcFollowTransition(PlayerEnterExit.TransitionEventArgs args)
        {
            var dungeonOwner=DungeonFollowOwner;
            if(dungeonOwner && dungeonOwner.PrepareDungeonFollow(args))return;
            if(!Ready || worldStore==null || population==null || !directory)return;
            bool entering=args.TransitionType==PlayerEnterExit.TransitionType.ToBuildingInterior && !interior;
            bool leaving=args.TransitionType==PlayerEnterExit.TransitionType.ToBuildingExterior && interior;
            double now=ActionNow();int routeBudget=4;
            PrepareNpcRoutineRecoveryTransition(args,now,entering,leaving);
            foreach(var candidate in worldStore.PendingNpcActions())
            {
                if(candidate.Kind!=NpcActionKind.Follow || NpcActionExecutor.IsTerminal(candidate))continue;
                if(candidate.FollowTransition!=null)
                {
                    if(!entering && !leaving)EndAction(candidate,now,"Following does not extend into dungeons or remote travel.",false);
                    continue;
                }
                var job=candidate;var actor=FindActor(job.ResidentId);
                if(!actor && entering && roadTravelActors &&
                    roadTravelActors.TryGetPaidCompanionForDoor(job.ResidentId,worldStore,now,out actor))
                {
                    // Exact active paid Follow action owns temporary plan. It neither
                    // needs nor creates local visit, Inn, home, or route.
                    if(!TryBindRoadPaidDoorPlan(actor,job,now))continue;
                    var actualRoad=actor.transform.position-Vector3.up;
                    if(!worldStore.SaveNpcActionPosition(job.Id,job.Revision,ActionPosition(actualRoad),false,now))continue;
                    job=worldStore.ReadNpcAction(job.Id);
                    if(job==null || !roadTravelActors.TryReleasePaidCompanionToStreet(job.ResidentId,worldStore,now,out actor))continue;
                    actor.TransferOwner(directory.transform,navigation);actor.SetRunningPilotAtlas(runningPilotAtlas);actors[job.ResidentId]=actor;
                }
                if(!actor || !actor.gameObject.activeInHierarchy || !ActionSpaceMatches(job.LastPosition))continue;
                NpcServiceContract paidContract;
                bool paidRoad=job.PaidTravelHandoff!=null && NpcPaidCompanionTravel.TryReadActiveTravel(worldStore,job,now,out paidContract);
                var actionPlan=FindNpcActionPlan(job.ResidentId);
                if(!entering && !leaving || !actor.Resident.IsAlive || QuestOwned(actor.Resident) || IsAlarmed(job.ResidentId) ||
                    actor.Resident.IsQuestProtected || worldStore.IsResidentAway(job.ResidentId,now,
                        ObservedTravelerActionVisit(actionPlan,actor,job)) && !paidRoad && !PaidDoorOwnsTravel(actionPlan,job,now))
                {EndAction(job,now,"Following ended at an unsupported transition or interruption.",true);continue;}
                if(routeBudget--<=0)
                {EndAction(job,now,"Too many followers need this doorway at once.",true);continue;}
                var actual=actor.transform.position-Vector3.up;
                if(!worldStore.SaveNpcActionPosition(job.Id,job.Revision,ActionPosition(actual),false,now))continue;
                job=worldStore.ReadNpcAction(job.Id);
                var route=new List<NpcActionTarget>();NpcActionTarget destination=null;
                if(entering && geometry!=null)
                {
                    // Use the door actually activated, including secondary entrances.
                    var normal=DaggerfallStaticDoors.GetDoorNormal(args.StaticDoor);normal.y=0;
                    var door=FollowExteriorDoorGround(args.StaticDoor);
                    BuildingSummary building;
                    if(normal.sqrMagnitude>0.01f && directory.GetBuildingSummary(args.StaticDoor.buildingKey,out building) && Vector3.Distance(actual,door)<=32)
                    {
                        var from=directory.transform.InverseTransformPoint(actual);var to=directory.transform.InverseTransformPoint(door);
                        var points=geometry.RouteBetweenPoints(new SavedRoutePoint {X=from.x,Z=from.z},new SavedRoutePoint {X=to.x,Z=to.z});
                        if(points.Count>=2 && points.Count<=128)
                        {
                            foreach(var point in points)route.Add(ActionPosition(geometry.ScenePoint(new RoutePoint(point.X,point.Z))));
                            // The geometry verifies the first and last connecting segments.
                            route[0]=ActionPosition(actual);
                            if(Vector3.Distance(ActionWorldPoint(route[route.Count-1]),door)>0.4f)route.Clear();
                        }
                        destination=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=population.SettlementId,BuildingKey=args.StaticDoor.buildingKey};
                    }
                }
                else if(leaving && indoorGeometry!=null && indoorGeometry.EntryGround.HasValue && Vector3.Distance(actual,indoorGeometry.EntryGround.Value)<=32)
                {
                    // The native exit chooses the closest exterior door. This bounded
                    // adapter uses the surveyed entry route, so a different exit must
                    // be declined instead of transporting the NPC across the building.
                    var exits=GameManager.Instance.PlayerEnterExit.ExteriorDoors;
                    if(exits==null || exits.Length==0)
                    {EndAction(job,now,"No native exterior doorway is available.",true);continue;}
                    DaggerfallWorkshop.StaticDoor chosen;
                    var chosenPosition=DaggerfallStaticDoors.FindClosestDoor(GameManager.Instance.PlayerMotor.transform.position,exits,out chosen);
                    if(Vector3.Distance(chosenPosition,DaggerfallStaticDoors.GetDoorPosition(interior.EntryDoor))>1)
                    {EndAction(job,now,"That exit has no surveyed follower route.",true);continue;}
                    var points=indoorGeometry.RouteToExit(actual);
                    if(points.Count>=2 && points.Count<=128)
                        foreach(var point in points)route.Add(new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,
                            SettlementId=population.SettlementId,BuildingKey=interior.EntryDoor.buildingKey,X=point.X,Y=point.Y,Z=point.Z});
                    var normal=DaggerfallStaticDoors.GetDoorNormal(interior.EntryDoor);normal.y=0;
                    var door=FollowExteriorDoorGround(interior.EntryDoor);
                    var local=directory.transform.InverseTransformPoint(door);
                    var outdoorGround=geometry==null?door:geometry.ScenePoint(new RoutePoint(local.x,local.z));
                    if(normal.sqrMagnitude>0.01f && geometry!=null && Vector3.Distance(door,outdoorGround)<=0.4f)destination=new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,
                        SettlementId=population.SettlementId,WorldSpaceId=ActionWorldSpace,BuildingKey=-1,X=local.x,Y=local.y,Z=local.z};
                }
                if(destination==null || !worldStore.TryBeginNpcFollowTransition(job.Id,job.Revision,now,destination,route,
                    DaggerfallUnity.Instance.WorldTime.TimeScale/actor.WalkingSpeed))
                {EndAction(job,now,"No safe, timely walking route reaches that doorway.",true);continue;}
                actor.CaptureState();actionMotions.Remove(job.ResidentId);
            }
        }

        private bool TryBindRoadTravelerDoorPlan(ResidentActor actor,double now)
        {
            if(actor==null || actor.Resident==null || population==null || worldStore==null)return false;
            TravelerPlan current;
            if(travelerPlans.TryGetValue(actor.Resident.Id,out current))
                return current.Person!=null && current.ActionPlan!=null && ReferenceEquals(current.Resident,actor.Resident) &&
                    ReferenceEquals(current.ActionPlan.Resident,actor.Resident);
            var person=worldStore.Traveler(actor.Resident.Id);
            var visit=worldStore.TravelerVisitsFor(population.SettlementId).Find(value=>value.PersonId==actor.Resident.Id);
            if(person==null || !person.IsAlive || visit==null)return false;
            var journey=worldStore.TravelerJourneyFor(visit.Id);
            if(journey==null)return false;
            var routine=new ResidentRoutine {ResidentId=actor.Resident.Id,DestinationKey=visit.TavernKey};
            foreach(var point in visit.Route)routine.Route.Add(new SavedRoutePoint {X=point.X,Z=point.Z});
            travelerPlans.Add(actor.Resident.Id,new TravelerPlan {Person=person,Visit=visit,Journey=journey,
                JourneyAdmission=visit.Admission,Routine=routine,Resident=actor.Resident,
                ActionPlan=new Plan {Resident=actor.Resident,Routine=routine}});
            return true;
        }
        private void AbortNpcFollowTransition(PlayerEnterExit.TransitionEventArgs args)
        {
            if(!Ready || worldStore==null || population==null)return;
            double now=ActionNow();
            foreach(var job in worldStore.PendingNpcActions())
                if(job.FollowTransition!=null && ActionSpaceMatches(job.LastPosition) &&
                    !(roadTravelActors && roadTravelActors.OwnsDungeonFollow(job.ResidentId)))
                {
                    // A failed synchronous native transition did not move this actor.
                    var actor=FindActor(job.ResidentId);
                    if(worldStore.TryResumeNpcFollowSource(job.Id,job.Revision,now) && actor)
                    {var resumed=worldStore.ReadNpcAction(job.Id);worldStore.SaveNpcActionPosition(job.Id,resumed.Revision,ActionPosition(actor.transform.position-Vector3.up),false,now);}
                }
        }
        // True holds scene ownership while the source route is unobserved. No body
        // is created until the full walking time and a clear destination door agree.
        private bool ApplyNpcFollowTransition(ref NpcActionJob job,double now)
        {
            var transit=job.FollowTransition;if(transit==null)return false;
            if(now>=job.DeadlineAt || job.FinishAt>0 && now>=job.FinishAt)
            {EndAction(job,now,"The agreed following time ended before doorway arrival.",false);job=worldStore.ReadNpcAction(job.Id);return false;}
            if(ActionSpaceMatches(job.LastPosition))
            {
                worldStore.TryResumeNpcFollowSource(job.Id,job.Revision,now);
                job=worldStore.ReadNpcAction(job.Id);actionMotions.Remove(job.ResidentId);return false;
            }
            if(!ActionSpaceMatches(transit.Destination))
            {EndAction(job,now,"The player left the agreed building and settlement route.",false);job=worldStore.ReadNpcAction(job.Id);return false;}
            if(GameManager.IsGamePaused || now<transit.ArrivesAt)return true;
            Vector3 landing;
            if(interior)
            {
                if(indoorGeometry==null || !indoorGeometry.EntryGround.HasValue)
                {EndAction(job,now,"The destination has no accessible entrance.",false);job=worldStore.ReadNpcAction(job.Id);return false;}
                landing=indoorGeometry.EntryGround.Value;
            }
            else landing=ActionWorldPoint(transit.Destination);
            // Occupied entrances wait in transit; they never spawn overlapping bodies
            // or search arbitrary room positions. The persisted consent deadline bounds the wait.
            if(!FollowLandingClear(landing))return true;
            if(!worldStore.TryArriveNpcFollowTransition(job.Id,job.Revision,now,ActionPosition(landing)))return true;
            job=worldStore.ReadNpcAction(job.Id);actionMotions.Remove(job.ResidentId);return false;
        }
        private static bool FollowLandingClear(Vector3 ground)
        {
            return Physics.OverlapCapsule(ground+Vector3.up*0.78f,ground+Vector3.up*1.6f,
                CrowdMotion.BodyRadius,~0,QueryTriggerInteraction.Ignore).Length==0;
        }
    }
}
