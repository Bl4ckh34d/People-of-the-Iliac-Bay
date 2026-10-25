using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private bool TravelerActionOwnsVisit(string id)
        {
            var person=worldStore.Traveler(id);
            return person!=null && TravelerActionRoutine.KeepsVisitAlive(person.IsAlive,
                worldStore.ActiveNpcAction(id),worldStore.ActiveNpcTask(id)!=null);
        }

        private Plan FindNpcActionPlan(string id)
        {
            var resident=plans.Find(p=>p.Resident.Id==id);if(resident!=null)return resident;
            PaidDoorPlanBinding paidDoor;
            if(paidDoorPlans.TryGetValue(id,out paidDoor) && ReferenceEquals(paidDoor.Store,worldStore))return paidDoor.Plan;
            TravelerPlan traveler;
            return travelerPlans.TryGetValue(id,out traveler)?traveler.ActionPlan:null;
        }

        internal bool HasBoundTravelerActionPlan(string id)
        {
            TravelerPlan plan;ResidentActor actor;
            return travelerPlans.TryGetValue(id,out plan) && plan.Person.IsAlive && plan.ActionPlan!=null &&
                ReferenceEquals(plan.Resident,plan.ActionPlan.Resident) && ReferenceEquals(plan.Routine,plan.ActionPlan.Routine) &&
                ReferenceEquals(FindNpcActionPlan(id),plan.ActionPlan) && actors.TryGetValue(id,out actor) && actor &&
                actor.gameObject.activeInHierarchy && ReferenceEquals(actor.Resident,plan.Resident);
        }

        private bool IsTravelerActionPlan(Plan plan)
        {return plan!=null && plan.Resident!=null && travelerPlans.ContainsKey(plan.Resident.Id);}

        private bool TryTravelerScheduled(Plan plan,double now,out ActivitySnapshot state)
        {
            TravelerPlan traveler;
            if(plan!=null && plan.Resident!=null && travelerPlans.TryGetValue(plan.Resident.Id,out traveler))
            {state=TravelerState(traveler,now);return true;}
            state=default(ActivitySnapshot);return false;
        }

        private Action TravelerActionDeathHandler(Plan plan)
        {
            if(!IsTravelerActionPlan(plan))return null;
            var owner=worldStore;var epoch=owner.CaptureNpcVitalsWriteEpoch();string id=plan.Resident.Id;
            return ()=>{if(owner.IsNpcVitalsWriteEpochCurrent(epoch))owner.KillTraveler(id,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());};
        }

        private void PrepareTravelerRoutineReturn(TravelerPlan plan,double now)
        {
            var job=worldStore.ActiveNpcAction(plan.Person.Id);
            var transit=job==null?null:job.RoutineRecoveryTransition;
            // A room booking can expire while its returning guest is unseen.
            // Retain the measured source doorway and resume the actual departure.
            if(transit!=null && transit.Destination.Kind==NpcActionTargetKind.IndoorPoint &&
                transit.Destination.BuildingKey==plan.Visit.TavernKey && now>=plan.Journey.CheckOutAt)
                worldStore.TryResumeNpcRoutineRecoverySource(job.Id,job.Revision,Math.Max(now,job.UpdatedAt));
        }

        private bool TryBuildTravelerRoutineReturnRoute(Plan actionPlan,ResidentActor actor,double now,
            out NpcActionTarget destination,out List<NpcActionTarget> route,out int originBuildingKey)
        {
            destination=null;route=new List<NpcActionTarget>();originBuildingKey=-1;TravelerPlan plan;
            if(interior || geometry==null || !actor || !travelerPlans.TryGetValue(actionPlan.Resident.Id,out plan))return false;
            bool toInn=TravelerActionRoutine.ReturnsToInn(plan.Visit.Admission,plan.Journey,now);
            var endpoint=toInn?plan.Visit.Route[plan.Visit.Route.Count-1]:plan.Visit.Route[0];
            Vector3 actual=actor.transform.position-Vector3.up;
            var local=directory.transform.InverseTransformPoint(actual);
            var points=geometry.RouteBetweenPoints(new SavedRoutePoint{X=local.x,Z=local.z},
                new SavedRoutePoint{X=endpoint.X,Z=endpoint.Z});
            if(points.Count<2 || points.Count>128)return false;
            foreach(var point in points)route.Add(ActionPosition(geometry.ScenePoint(new RoutePoint(point.X,point.Z))));
            route[0]=ActionPosition(actual);
            destination=ActionPosition(geometry.ScenePoint(new RoutePoint(endpoint.X,endpoint.Z)));
            if(Vector3.Distance(ActionWorldPoint(route[route.Count-1]),ActionWorldPoint(destination))>0.4f)
            {route.Clear();destination=null;return false;}
            // The existing visit owns both its Inn and departure route. This is
            // provenance for the bounded handoff, never a permanent home claim.
            originBuildingKey=plan.Visit.TavernKey;return true;
        }

        private bool BeginTravelerInnEntry(TravelerPlan plan,NpcActionJob job,NpcActionTarget source,double now)
        {
            if(source==null || source.Kind!=NpcActionTargetKind.WorldPoint || source.SettlementId!=population.SettlementId ||
                plan.Visit.Admission!=TravelerAdmission.Admitted || now<plan.Journey.CheckInAt || now>=plan.Journey.CheckOutAt ||
                worldStore.TravelerBed(plan.Visit.Id,now)==null)return false;
            var door=plan.Visit.Route[plan.Visit.Route.Count-1];
            double dx=source.X-door.X,dz=source.Z-door.Z;
            if(dx*dx+dz*dz>0.25)return false;
            var destination=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,
                SettlementId=population.SettlementId,BuildingKey=plan.Visit.TavernKey};
            if(!worldStore.TryBeginNpcRoutineRecoveryTransition(job.Id,job.Revision,Math.Max(now,job.UpdatedAt),destination,
                new List<NpcActionTarget>{source,NpcActionExecutor.CopyTarget(source)},
                DaggerfallUnity.Instance.WorldTime.TimeScale/ResidentMovement.WalkSpeedFor(plan.Resident.AppearanceSeed),plan.Visit.TavernKey))return false;
            var actor=FindActor(job.ResidentId);
            if(actor){actor.CaptureState();actor.gameObject.SetActive(false);Destroy(actor.gameObject);actors.Remove(job.ResidentId);}
            actionMotions.Remove(job.ResidentId);return true;
        }

        private bool CompleteTravelerOffscreenRoutineRecovery(Plan actionPlan,NpcActionJob job,NpcRoutineRecoveryTransition transit,double now)
        {
            TravelerPlan plan;
            if(!travelerPlans.TryGetValue(actionPlan.Resident.Id,out plan) || transit.Destination==null ||
                transit.Destination.Kind!=NpcActionTargetKind.WorldPoint)return true;
            if(!worldStore.TryArriveNpcRoutineRecoveryTransition(job.Id,job.Revision,now,transit.Destination))return true;
            job=worldStore.ReadNpcAction(job.Id);actionMotions.Remove(job.ResidentId);
            bool toInn=TravelerActionRoutine.ReturnsToInn(plan.Visit.Admission,plan.Journey,now);
            if(toInn)
            {
                BeginTravelerInnEntry(plan,job,job.LastPosition,now);
                return true; // Room return or admission/gate waiting still owns the actor.
            }
            var border=plan.Visit.Route[0];double dx=job.LastPosition.X-border.X,dz=job.LastPosition.Z-border.Z;
            // If checkout occurred during an Inn approach, retain return ownership
            // at that measured doorway until the departure route can run.
            if(dx*dx+dz*dz<=0.25)
                worldStore.SaveNpcActionPosition(job.Id,job.Revision,job.LastPosition,false,Math.Max(now,job.UpdatedAt));
            return true;
        }

        private bool ReturnTravelerActionToRoutine(Plan actionPlan,ResidentActor actor,NpcActionJob job,ActionMotion motion,double now)
        {
            TravelerPlan plan;
            if(!travelerPlans.TryGetValue(actionPlan.Resident.Id,out plan) || !job.ReturningToRoutine)return false;
            bool toInn=TravelerActionRoutine.ReturnsToInn(plan.Visit.Admission,plan.Journey,now);
            bool booked=plan.Visit.Admission==TravelerAdmission.Admitted && now>=plan.Journey.CheckInAt && now<plan.Journey.CheckOutAt;
            bool room=interior && interior.EntryDoor.buildingKey==plan.Visit.TavernKey && booked;
            Vector3 goal;
            if(room)
            {
                string bed=worldStore.TravelerBed(plan.Visit.Id,now);
                var anchor=bed==null?null:BedGeometry.Read(interior).Find(b=>"bed:"+b.RecordIndex==bed);
                if(anchor==null || indoorGeometry==null || !indoorGeometry.StandBesideBed(anchor,out goal))
                {WaitForRoutineRecovery(actor,motion);return true;}
            }
            else if(interior)
            {
                if(indoorGeometry==null || !indoorGeometry.EntryGround.HasValue)
                {WaitForRoutineRecovery(actor,motion);return true;}
                goal=indoorGeometry.EntryGround.Value;
            }
            else
            {
                var point=toInn?plan.Visit.Route[plan.Visit.Route.Count-1]:plan.Visit.Route[0];
                goal=geometry.ScenePoint(new RoutePoint(point.X,point.Z));
            }
            Vector3 actual=actor.transform.position-Vector3.up;
            if(Vector3.Distance(actual,goal)<=0.35f)
            {
                if(room)
                {
                    if(worldStore.SaveNpcActionPosition(job.Id,job.Revision,ActionPosition(actual),false,Math.Max(now,job.UpdatedAt)))
                    {plan.RoomInterior=null;plan.RoomFollower=null;actionMotions.Remove(job.ResidentId);}
                    return true;
                }
                if(interior)
                {
                    string reason;if(!BeginUnplannedBuildingReturn(actor,job,now,out reason))WaitForRoutineRecovery(actor,motion);
                    return true;
                }
                if(toInn)
                {
                    // Waiting for admission or an open gate never grants a bed.
                    if(!booked){actor.Place(actual,actor.transform.forward,true);return true;}
                    var source=ActionPosition(actual);
                    if(!worldStore.SaveNpcActionPosition(job.Id,job.Revision,source,true,Math.Max(now,job.UpdatedAt)))return true;
                    job=worldStore.ReadNpcAction(job.Id);
                    BeginTravelerInnEntry(plan,job,source,now);return true;
                }
                // Only arrival at the actual border releases an expired guest.
                if(worldStore.SaveNpcActionPosition(job.Id,job.Revision,ActionPosition(actual),false,Math.Max(now,job.UpdatedAt)))
                    actionMotions.Remove(job.ResidentId);
                return true;
            }
            if(motion.RecoveryWaiting && Time.unscaledTime<motion.RepathAt)
            {actor.Place(actual,actor.transform.forward,true);return true;}
            bool exhausted=interior?motion.Indoor==null || motion.Indoor.Arrived:
                motion.Outdoor==null || motion.Next>=motion.Outdoor.Count;
            if(!motion.RecoverySelected || Vector3.Distance(motion.Goal,goal)>0.4f || exhausted && Time.unscaledTime>=motion.RepathAt)
            {
                string reason;
                if(!BuildActionRoute(actor,motion,goal,0.25f,out reason)){WaitForRoutineRecovery(actor,motion);return true;}
                motion.RecoverySelected=true;motion.RecoveryWaiting=false;
            }
            if(Time.unscaledTime>=motion.SaveAt)
            {
                motion.SaveAt=Time.unscaledTime+0.5f;
                worldStore.SaveNpcActionPosition(job.Id,job.Revision,ActionPosition(actual),true,Math.Max(now,job.UpdatedAt));
            }
            AdvanceActionMotion(actor,motion);return true;
        }
    }
}
