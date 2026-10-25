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
        private void PrepareNpcRoutineRecoveryTransition(PlayerEnterExit.TransitionEventArgs args,double now,bool entering,bool leaving)
        {
            if(worldStore==null)return;
            int routeBudget=4;
            foreach(var candidate in worldStore.PendingNpcActions())
            {
                if(!NpcActionExecutor.IsTerminal(candidate) || !candidate.ReturningToRoutine ||
                    candidate.RoutineRecoveryTransition!=null || candidate.FollowTransition!=null)continue;
                var actor=FindActor(candidate.ResidentId);
                var plan=FindNpcActionPlan(candidate.ResidentId);
                if(!actor || !actor.gameObject.activeInHierarchy || !actor.Resident.IsAlive ||
                    actor.Resident.IsQuestProtected || QuestOwned(actor.Resident) || IsAlarmed(candidate.ResidentId) ||
                    plan==null || worldStore.IsResidentAway(candidate.ResidentId,now) || !ActionSpaceMatches(candidate.LastPosition))continue;
                if(TryReleaseReturningAtOwnHome(plan,actor,candidate,now))continue;
                if(routeBudget--<=0)break;

                var actual=ActionPosition(actor.transform.position-Vector3.up);
                if(!worldStore.SaveNpcActionPosition(candidate.Id,candidate.Revision,actual,true,now))continue;
                var job=worldStore.ReadNpcAction(candidate.Id);
                actor.CaptureState();
                ActionMotion recoveryMotion;actionMotions.TryGetValue(job.ResidentId,out recoveryMotion);
                actionMotions.Remove(job.ResidentId);
                if((!entering&&!leaving) || args.StaticDoor.buildingKey<0 || geometry==null)
                {
                    Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_ROUTINE_RECOVERY_TRANSITION_UNAVAILABLE: resident="+job.ResidentId+
                        "; unsupported player scene transition; source position retained.");
                    continue;
                }
                NpcActionTarget destination;List<NpcActionTarget> route;int originBuildingKey;
                if(!TryBuildRoutineRecoveryDoorRoute(args,actor,plan,job,recoveryMotion,entering,leaving,
                    out destination,out route,out originBuildingKey))
                {
                    Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_ROUTINE_RECOVERY_TRANSITION_UNAVAILABLE: resident="+job.ResidentId+
                        "; building="+args.StaticDoor.buildingKey+"; own routine node unavailable; source position retained.");
                    continue;
                }
                double secondsPerUnit=DaggerfallUnity.Instance.WorldTime.TimeScale/actor.WalkingSpeed;
                if(!worldStore.TryBeginNpcRoutineRecoveryTransition(job.Id,job.Revision,now,destination,route,secondsPerUnit,originBuildingKey))
                    Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_ROUTINE_RECOVERY_TRANSITION_UNAVAILABLE: resident="+job.ResidentId+
                        "; measured doorway route rejected; source position retained.");
            }
        }

        private bool TryBuildRoutineRecoveryDoorRoute(PlayerEnterExit.TransitionEventArgs args,ResidentActor actor,
            Plan plan,NpcActionJob job,ActionMotion recoveryMotion,bool entering,bool leaving,
            out NpcActionTarget destination,out List<NpcActionTarget> route,out int originBuildingKey)
        {
            destination=null;route=new List<NpcActionTarget>();originBuildingKey=-1;
            var actual=actor.transform.position-Vector3.up;
            if(entering && !interior)
            {
                if(IsTravelerActionPlan(plan))return TryBuildTravelerRoutineReturnRoute(plan,actor,job.UpdatedAt,
                    out destination,out route,out originBuildingKey);
                // The player may enter any building. Keep the NPC bound to its own
                // selected recovery origin, never the player's unrelated doorway.
                var motionOrigin=recoveryMotion!=null && recoveryMotion.RecoverySelected?
                    recoveryMotion.RecoveryOrigin:-1;
                var keys=new List<int>{plan.Resident.HomeKey};
                if(plan.Routine.Itinerary!=null)
                {
                    foreach(var itinerary in ResidentRoutineDay.Itineraries(plan.Routine))
                    foreach(var stop in itinerary.Stops) if(!keys.Contains(stop.BuildingKey))keys.Add(stop.BuildingKey);
                }
                else if(plan.Routine.DestinationKey>=0 && !keys.Contains(plan.Routine.DestinationKey))keys.Add(plan.Routine.DestinationKey);
                float nearest=float.PositiveInfinity;SavedRoutePoint chosenDoor=null;
                if(motionOrigin>=0 && keys.Contains(motionOrigin) && TryRoutineRecoveryDoor(plan,motionOrigin,out chosenDoor))
                    originBuildingKey=motionOrigin;
                else foreach(int key in keys)
                {
                    SavedRoutePoint candidateDoor;if(!TryRoutineRecoveryDoor(plan,key,out candidateDoor))continue;
                    var nodePoint=geometry.ScenePoint(new RoutePoint(candidateDoor.X,candidateDoor.Z));float distance=(nodePoint-actual).sqrMagnitude;
                    if(distance>=nearest)continue;nearest=distance;chosenDoor=candidateDoor;originBuildingKey=key;
                }
                if(chosenDoor==null || originBuildingKey<0)return false;
                var from=directory.transform.InverseTransformPoint(actual);
                var to=directory.transform.InverseTransformPoint(geometry.ScenePoint(new RoutePoint(chosenDoor.X,chosenDoor.Z)));
                var points=geometry.RouteBetweenPoints(new SavedRoutePoint {X=from.x,Z=from.z},new SavedRoutePoint {X=to.x,Z=to.z});
                if(points.Count<2 || points.Count>128)return false;
                foreach(var point in points)route.Add(ActionPosition(geometry.ScenePoint(new RoutePoint(point.X,point.Z))));
                route[0]=ActionPosition(actual);
                var node=geometry.ScenePoint(new RoutePoint(chosenDoor.X,chosenDoor.Z));
                if(Vector3.Distance(ActionWorldPoint(route[route.Count-1]),node)>0.4f){route.Clear();return false;}
                destination=ActionPosition(node);
                return true;
            }
            if(leaving && interior && indoorGeometry!=null && interior.EntryDoor.buildingKey==args.StaticDoor.buildingKey &&
                indoorGeometry.EntryGround.HasValue && Vector3.Distance(actual,indoorGeometry.EntryGround.Value)<=32)
            {
                var exits=GameManager.Instance.PlayerEnterExit.ExteriorDoors;
                if(exits==null || exits.Length==0)return false;
                DaggerfallWorkshop.StaticDoor chosen;
                var chosenPosition=DaggerfallStaticDoors.FindClosestDoor(GameManager.Instance.PlayerMotor.transform.position,exits,out chosen);
                if(Vector3.Distance(chosenPosition,DaggerfallStaticDoors.GetDoorPosition(interior.EntryDoor))>1)return false;
                var points=indoorGeometry.RouteToExit(actual);
                if(points==null || points.Count<2 || points.Count>128)return false;
                foreach(var point in points)route.Add(new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,
                    SettlementId=population.SettlementId,BuildingKey=interior.EntryDoor.buildingKey,X=point.X,Y=point.Y,Z=point.Z});
                route[0]=ActionPosition(actual);
                var door=FollowExteriorDoorGround(interior.EntryDoor);
                var local=directory.transform.InverseTransformPoint(door);
                var outside=geometry.ScenePoint(new RoutePoint(local.x,local.z));
                if(Vector3.Distance(door,outside)>0.4f){route.Clear();return false;}
                destination=new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,SettlementId=population.SettlementId,
                    WorldSpaceId=ActionWorldSpace,BuildingKey=-1,X=local.x,Y=local.y,Z=local.z};
                originBuildingKey=interior.EntryDoor.buildingKey;
                return true;
            }
            return false;
        }

        private bool ApplyNpcRoutineRecoveryTransition(ref NpcActionJob job,ref ResidentActor actor,double now)
        {
            var transit=job==null?null:job.RoutineRecoveryTransition;
            if(transit==null)return false;
            if(job.LastPosition.Kind==NpcActionTargetKind.WorldPoint && transit.Destination.Kind==NpcActionTargetKind.WorldPoint &&
                NpcFollowTransition.SameSpace(job.LastPosition,transit.Destination))
                return ApplySameWorldRoutineRecoveryTransition(job,transit,actor,now);
            if(ActionSpaceMatches(job.LastPosition))return true;
            if(!ActionSpaceMatches(transit.Destination) || GameManager.IsGamePaused || now<transit.ArrivesAt)return true;
            Vector3 landing;
            if(interior)
            {
                if(indoorGeometry==null || !indoorGeometry.EntryGround.HasValue)return true;
                landing=indoorGeometry.EntryGround.Value;
            }
            else landing=ActionWorldPoint(transit.Destination);
            if(!FollowLandingClear(landing))return true;
            if(!worldStore.TryArriveNpcRoutineRecoveryTransition(job.Id,job.Revision,now,ActionPosition(landing)))return true;
            job=worldStore.ReadNpcAction(job.Id);actionMotions.Remove(job.ResidentId);return false;
        }

        private bool ApplySameWorldRoutineRecoveryTransition(NpcActionJob job,NpcRoutineRecoveryTransition transit,
            ResidentActor actor,double now)
        {
            if(!ActionSpaceMatches(job.LastPosition))
                return now>=transit.ArrivesAt?CompleteOffscreenRoutineRecovery(job,transit,now):true;
            if(!actor || !actor.gameObject.activeInHierarchy)
                return now>=transit.ArrivesAt?CompleteOffscreenRoutineRecovery(job,transit,now):true;
            var goal=ActionWorldPoint(transit.Destination);
            if(Vector3.Distance(actor.transform.position-Vector3.up,goal)<=0.35f)
            {
                if(now>=transit.ArrivesAt)return CompleteOffscreenRoutineRecovery(job,transit,now);
                actor.Place(actor.transform.position-Vector3.up,goal-(actor.transform.position-Vector3.up),true);return true;
            }
            ActionMotion motion;
            if(!actionMotions.TryGetValue(job.ResidentId,out motion)||motion.JobId!=job.Id||motion.Outdoor==null)
            {
                var actual=actor.transform.position-Vector3.up;
                var from=directory.transform.InverseTransformPoint(actual);
                var to=new Vector3((float)transit.Destination.X,(float)transit.Destination.Y,(float)transit.Destination.Z);
                var points=geometry.RouteBetweenPoints(new SavedRoutePoint {X=from.x,Z=from.z},
                    new SavedRoutePoint {X=to.x,Z=to.z});
                if(points.Count<2||points.Count>128)return true;
                motion=new ActionMotion {JobId=job.Id,Goal=goal,LastProgress=actor.transform.position,
                    ProgressAt=Time.unscaledTime,Outdoor=points,Next=1};
                motion.Outdoor[0]=new SavedRoutePoint {X=from.x,Z=from.z};
                actionMotions[job.ResidentId]=motion;
            }
            if(now>=transit.ArrivesAt && Vector3.Distance(actor.transform.position-Vector3.up,goal)<=0.35f)
                return CompleteOffscreenRoutineRecovery(job,transit,now);
            AdvanceActionMotion(actor,motion);return true;
        }

        private bool CompleteOffscreenRoutineRecovery(NpcActionJob job,NpcRoutineRecoveryTransition transit,double now)
        {
            if(GameManager.IsGamePaused || now<transit.ArrivesAt || transit.OriginBuildingKey<0)return true;
            var plan=FindNpcActionPlan(job.ResidentId);
            if(plan==null || !plan.Resident.IsAlive || plan.Resident.IsQuestProtected)return true;
            if(IsTravelerActionPlan(plan))return CompleteTravelerOffscreenRoutineRecovery(plan,job,transit,now);
            if(IsHomeOnlyWithoutVerifiedRoute(plan) && transit.Destination!=null &&
                transit.Destination.Kind==NpcActionTargetKind.WorldPoint &&
                IsVerifiedOwnHomeDoor(plan,transit.OriginBuildingKey,ActionWorldPoint(transit.Destination)) &&
                CanReleaseHomeOnlyAtDoor(plan,now,out _))
            {
                if(!worldStore.TryArriveNpcRoutineRecoveryTransition(job.Id,job.Revision,now,transit.Destination))return true;
                job=worldStore.ReadNpcAction(job.Id);
                if(!worldStore.SaveNpcActionPosition(job.Id,job.Revision,transit.Destination,false,now))return true;
                actionMotions.Remove(job.ResidentId);
                return true;
            }
            double secondsPerMetre=DaggerfallUnity.Instance.WorldTime.TimeScale/
                ResidentMovement.WalkSpeedFor(plan.Resident.AppearanceSeed);
            bool restricted=population.SettlementId==1291010263 && worldStore.RestrictHauntingRoutines(population.SettlementId,
                GameManager.Instance.PlayerEntity.GlobalVars.GetGlobalVar(0),now);
            int wake=restricted?Math.Max(wakeHour*60,300):wakeHour*60;
            int sleep=restricted?Math.Min(sleepHour*60,1140):sleepHour*60;
            DailySchedule ordinary=restricted?plan.HauntedSchedule:plan.Schedule;
            var previous=CurrentRoutineRecovery(plan,now);
            if(previous!=null)ordinary=DailySchedule.ContinueDay(ordinary,previous);
            RoutineRecoveryPlan recovery;string reason;
            if(!TryCreateRoutineRecoveryWithPoints(plan,ordinary,(int)Math.Floor(now/86400d),now,transit.OriginBuildingKey,now,
                wake,sleep,secondsPerMetre,out recovery,out reason,out _,true))return true;
            worldStore.SaveRoutineRecovery(recovery);
            if(!worldStore.TryArriveNpcRoutineRecoveryTransition(job.Id,job.Revision,now,transit.Destination))return true;
            job=worldStore.ReadNpcAction(job.Id);
            if(!worldStore.SaveNpcActionPosition(job.Id,job.Revision,transit.Destination,false,now))return true;
            InvalidateRoutineRecovery(job.ResidentId);
            RemovePhysicalShopVisit(job.ResidentId);worldStore.RemoveObservedVisit(job.ResidentId);
            shopJourneys.Remove(job.ResidentId);shopDepartures.Remove(job.ResidentId);actionMotions.Remove(job.ResidentId);
            return true;
        }

        private void AbortNpcRoutineRecoveryTransition(PlayerEnterExit.TransitionEventArgs args)
        {
            if(!Ready || worldStore==null || population==null)return;
            double now=ActionNow();
            foreach(var job in worldStore.PendingNpcActions())
            {
                if(job.RoutineRecoveryTransition==null || !ActionSpaceMatches(job.LastPosition))continue;
                if(!worldStore.TryResumeNpcRoutineRecoverySource(job.Id,job.Revision,job.UpdatedAt))continue;
                var actor=FindActor(job.ResidentId);var resumed=worldStore.ReadNpcAction(job.Id);
                if(actor && ActionSpaceMatches(resumed.LastPosition))
                    worldStore.SaveNpcActionPosition(resumed.Id,resumed.Revision,ActionPosition(actor.transform.position-Vector3.up),true,Math.Max(now,resumed.UpdatedAt));
            }
        }
    }
}
