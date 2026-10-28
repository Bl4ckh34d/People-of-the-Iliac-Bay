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
        private readonly HashSet<string> simulatedFamilyFrame=new HashSet<string>(StringComparer.Ordinal);
        public bool HouseholdHomeLoaded(int settlement,int home)
        {return population!=null && population.SettlementId==settlement && interior && interior.EntryDoor.buildingKey==home;}
        public bool CanReserveHouseholdSeats(NpcInnServiceCatalog catalog,NpcInnServiceSeatPair pair)
        {
            if(population==null || population.SettlementId!=catalog.SettlementId || !interior ||
                interior.EntryDoor.buildingKey!=catalog.BuildingKey)return true;
            if(shopGeometry?.Seating==null)return false;
            SelectedSeatPairAccess lease;
            if(!shopGeometry.Seating.TryAcquirePair("household-capacity-first",pair.First.Id,"household-capacity-second",pair.Second.Id,out lease))return false;
            lease.Release();return true;
        }
        public bool HouseholdParentAvailable(string id,double now)
        {
            ResidentActor actor;actors.TryGetValue(id,out actor);
            PhysicalShopVisit visit;
            if(physicalShopVisits.TryGetValue(id,out visit) && visit.HouseholdDayId==null && visit.Snapshot.Phase!=ShopVisitPhase.Complete)return false;
            return worldStore!=null && JointBodyFree(id,now) && (!actor || !actor.IsConversing && !actor.IsSleeping);
        }
        private void PrepareHouseholdOwners(double now)
        {
            simulatedFamilyFrame.Clear();if(worldStore==null || population==null || geometry==null)return;
            foreach(var progress in worldStore.ReadActiveHouseholdJoints(population.SettlementId))
            {
                if(Reaction(progress.FirstResidentId,now)?.Reaction==CivilianReaction.Flee ||
                    Reaction(progress.SecondResidentId,now)?.Reaction==CivilianReaction.Flee)
                {HoldFamily(progress);continue;}
                // Cash detours own the collector; the partner keeps the shared
                // checkpoint instead of being hidden with the whole family.
                if(FamilyNeedsCash(progress))continue;
                var sim=worldStore.ReadHouseholdJointSimulation(progress.SettlementId,progress.HomeKey);
                if(sim!=null && sim.Active)
                {
                    if(sim.Phase==HouseholdJointPhase.Waiting)continue; // ordinary earlier-day errands still execute
                    simulatedFamilyFrame.Add(progress.FirstResidentId);simulatedFamilyFrame.Add(progress.SecondResidentId);
                    if(!HouseholdParentAvailable(progress.FirstResidentId,now) || !HouseholdParentAvailable(progress.SecondResidentId,now))
                    {HoldFamily(progress);continue;}
                    // A current-scene visible body is never repositioned by a coarse owner handoff.
                    if(HasVisibleFamilyBodyInRoom(progress)){HoldFamily(progress);continue;}
                    bool restored=sim.Phase==HouseholdJointPhase.Completed?RestoreHouseholdHomeBodies(progress,sim,now):
                        interior?RestoreHouseholdInnBodies(progress,sim,now):RestoreHouseholdExteriorBodies(progress,sim,now);
                    if(restored){simulatedFamilyFrame.Remove(progress.FirstResidentId);simulatedFamilyFrame.Remove(progress.SecondResidentId);}
                    else HideFamily(progress);
                    continue;
                }
                if(progress.Phase==HouseholdJointPhase.Outbound || progress.Phase==HouseholdJointPhase.Returning)
                {
                    if(!FamilyActorCapacity(progress))
                    {simulatedFamilyFrame.Add(progress.FirstResidentId);simulatedFamilyFrame.Add(progress.SecondResidentId);HoldFamily(progress);continue;}
                    var a=geometry.ScenePoint(new RoutePoint(progress.First.Position.X,progress.First.Position.Z));
                    var b=geometry.ScenePoint(new RoutePoint(progress.Second.Position.X,progress.Second.Position.Z));
                    var player=GameManager.Instance.PlayerMotor.transform.position;
                    if(interior || (a-player).sqrMagnitude>135*135 && (b-player).sqrMagnitude>135*135)
                    {
                        if(YieldHousehold(progress,now)){HideFamily(progress);simulatedFamilyFrame.Add(progress.FirstResidentId);simulatedFamilyFrame.Add(progress.SecondResidentId);}
                    }
                }
            }
        }
        private bool HasVisibleFamilyBodyInRoom(HouseholdJointProgress progress)
        {
            if(!interior)return false;
            foreach(string id in new[]{progress.FirstResidentId,progress.SecondResidentId})
            {ResidentActor actor;if(actors.TryGetValue(id,out actor) && actor && actor.gameObject.activeInHierarchy &&
                actor.transform.parent==interior.transform)return true;}
            return false;
        }
        private bool RestoreHouseholdExteriorBodies(HouseholdJointProgress progress,HouseholdJointSimulation sim,double now)
        {
            if(GameManager.Instance.PlayerEnterExit.IsPlayerInside || sim.RequiresRecovery || sim.Phase==HouseholdJointPhase.Visiting ||
                sim.Phase==HouseholdJointPhase.Waiting || sim.Phase==HouseholdJointPhase.Completed)return false;
            var a=geometry.ScenePoint(new RoutePoint(sim.First.Position.X,sim.First.Position.Z));
            var b=geometry.ScenePoint(new RoutePoint(sim.Second.Position.X,sim.Second.Position.Z));
            var player=GameManager.Instance.PlayerMotor.transform.position;
            if((a-player).sqrMagnitude>135*135 && (b-player).sqrMagnitude>135*135)return false;
            var first=plans.Find(p=>p.Resident.Id==progress.FirstResidentId);var second=plans.Find(p=>p.Resident.Id==progress.SecondResidentId);
            if(first==null || second==null || !FamilyActorCapacity(progress))return false;
            ResidentActor firstBody=null,secondBody=null;
            try
            {
                firstBody=PlaceFamilyBody(first,directory.transform,a);secondBody=PlaceFamilyBody(second,directory.transform,b);
                var ap=directory.transform.InverseTransformPoint(firstBody.transform.position-Vector3.up);
                var bp=directory.transform.InverseTransformPoint(secondBody.transform.position-Vector3.up);
                worldStore.ReconstructHouseholdJointSimulation(progress.HouseholdDayId,sim.Revision,now,
                    new HouseholdJointPlacement{ResidentId=first.Resident.Id,Position=new SavedRoutePoint{X=ap.x,Z=ap.z}},
                    new HouseholdJointPlacement{ResidentId=second.Resident.Id,Position=new SavedRoutePoint{X=bp.x,Z=bp.z}});
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_RECONSTRUCTED: scene=exterior; home="+progress.HomeKey+"; phase="+sim.Phase);
                return true;
            }
            catch(ArgumentException error){Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_RECONSTRUCTION_HELD: "+error.Message);HideFamily(progress);return false;}
        }
        private bool RestoreHouseholdHomeBodies(HouseholdJointProgress progress,HouseholdJointSimulation sim,double now)
        {
            if(!HouseholdHomeLoaded(progress.SettlementId,progress.HomeKey) || indoorGeometry==null || !FamilyActorCapacity(progress))return false;
            var first=plans.Find(p=>p.Resident.Id==progress.FirstResidentId);var second=plans.Find(p=>p.Resident.Id==progress.SecondResidentId);
            if(first==null || second==null)return false;
            var used=new HashSet<int>();foreach(var entry in indoorSlots)
                if(entry.Key!=progress.FirstResidentId && entry.Key!=progress.SecondResidentId)used.Add(entry.Value);
            int a=-1,b=-1;
            for(int i=0;i<indoorGeometry.LocalAnchors.Count;i++)
            {
                if(used.Contains(i))continue;
                if(a<0){a=i;continue;}
                if((indoorGeometry.LocalAnchors[i]-indoorGeometry.LocalAnchors[a]).sqrMagnitude>=0.36f){b=i;break;}
            }
            if(a<0 || b<0)return false;
            try
            {
                var firstBody=PlaceFamilyBody(first,interior.transform,interior.transform.TransformPoint(indoorGeometry.LocalAnchors[a]));
                var secondBody=PlaceFamilyBody(second,interior.transform,interior.transform.TransformPoint(indoorGeometry.LocalAnchors[b]));
                var ap=interior.transform.InverseTransformPoint(firstBody.transform.position-Vector3.up);
                var bp=interior.transform.InverseTransformPoint(secondBody.transform.position-Vector3.up);
                if(!worldStore.ReconstructHouseholdHomeSimulation(progress.HouseholdDayId,sim.Revision,new HouseholdHomeArrival{
                    SettlementId=progress.SettlementId,HomeKey=progress.HomeKey,FirstResidentId=progress.FirstResidentId,SecondResidentId=progress.SecondResidentId,
                    ObservedAt=now,First=new GuardPoint3{X=ap.x,Y=ap.y,Z=ap.z},Second=new GuardPoint3{X=bp.x,Y=bp.y,Z=bp.z}}))return false;
                indoorSlots[progress.FirstResidentId]=a;indoorSlots[progress.SecondResidentId]=b;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_RECONSTRUCTED: scene=home; home="+progress.HomeKey+"; atomicPair=true");return true;
            }
            catch(ArgumentException error){Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_RECONSTRUCTION_HELD: "+error.Message);return false;}
        }
        private bool RestoreHouseholdInnBodies(HouseholdJointProgress progress,HouseholdJointSimulation sim,double now)
        {
            if(!interior || indoorGeometry==null || shopGeometry==null || !shopGeometry.IsTavern || shopGeometry.Seating==null || sim.RequiresRecovery ||
                sim.Phase!=HouseholdJointPhase.AtInn && sim.Phase!=HouseholdJointPhase.Visiting)return false;
            var plan=worldStore.ReadHouseholdDay(progress.SettlementId,progress.HomeKey,progress.AbsoluteDay);
            if(plan?.JointInn==null || plan.JointInn.InnKey!=interior.EntryDoor.buildingKey || !FamilyActorCapacity(progress))return false;
            // Existing visible bodies keep their captured movement owner; reconstruction only creates absent bodies.
            ResidentActor visibleFirst,visibleSecond;
            if(actors.TryGetValue(progress.FirstResidentId,out visibleFirst) && visibleFirst && visibleFirst.gameObject.activeInHierarchy ||
                actors.TryGetValue(progress.SecondResidentId,out visibleSecond) && visibleSecond && visibleSecond.gameObject.activeInHierarchy)return false;
            if(!worldStore.PrepareHouseholdInnObservation(plan.Id,now,CanReserveHouseholdSeats))return false;
            var a=worldStore.ProjectHouseholdInnMember(plan.Id,progress.FirstResidentId,now);
            var b=worldStore.ProjectHouseholdInnMember(plan.Id,progress.SecondResidentId,now);
            if(a==null || b==null)return false;
            SelectedSeatPairAccess lease=null;
            if(a.SeatId!=null && !shopGeometry.Seating.TryAcquirePair(a.ResidentId,a.SeatId,b.ResidentId,b.SeatId,out lease))return false;
            try
            {
                var first=plans.Find(p=>p.Resident.Id==a.ResidentId);var second=plans.Find(p=>p.Resident.Id==b.ResidentId);
                if(first==null || second==null)return false;
                var firstBody=PlaceFamilyBody(first,interior.transform,interior.transform.TransformPoint(Vector(a.Position)));
                var secondBody=PlaceFamilyBody(second,interior.transform,interior.transform.TransformPoint(Vector(b.Position)));
                var ap=interior.transform.InverseTransformPoint(firstBody.transform.position-Vector3.up);
                var bp=interior.transform.InverseTransformPoint(secondBody.transform.position-Vector3.up);
                worldStore.ReconstructHouseholdInnSimulation(plan.Id,sim.Revision,now,
                    new HouseholdInnPlacement{ResidentId=a.ResidentId,Position=new GuardPoint3{X=ap.x,Y=ap.y,Z=ap.z}},
                    new HouseholdInnPlacement{ResidentId=b.ResidentId,Position=new GuardPoint3{X=bp.x,Y=bp.y,Z=bp.z}});
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_RECONSTRUCTED: scene=Inn; home="+progress.HomeKey+"; paid="+
                    (worldStore.ReadNpcInnConsumption(a.ResidentId)!=null));return true;
            }
            catch(ArgumentException error){Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_RECONSTRUCTION_HELD: "+error.Message);HideFamily(progress);return false;}
            finally{if(lease!=null)lease.Release();}
        }
        private static Vector3 Vector(GuardPoint3 point){return new Vector3((float)point.X,(float)point.Y,(float)point.Z);}
        private bool FamilyActorCapacity(HouseholdJointProgress progress)
        {
            int needed=actors.ContainsKey(progress.FirstResidentId)?0:1;needed+=actors.ContainsKey(progress.SecondResidentId)?0:1;
            return actors.Count+needed<=80;
        }
        private ResidentActor PlaceFamilyBody(Plan plan,Transform parent,Vector3 ground)
        {
            ResidentActor actor;actors.TryGetValue(plan.Resident.Id,out actor);
            if(!actor)
            {
                var go=Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject,parent);
                go.name="People of the Iliac Bay "+plan.Resident.Id;actor=go.AddComponent<ResidentActor>();
                actor.Initialize(plan.Resident,plan.SurnameSeed,navigation,inventoryStore:worldStore);actor.SetRunningPilotAtlas(runningPilotAtlas);
                actors[plan.Resident.Id]=actor;
            }
            actor.transform.SetParent(parent,false);actor.gameObject.SetActive(true);
            // Reconstruction transfers ownership at the observed simulation
            // position. A nearby old proxy must not pull this body back on its
            // next native step or retain another routine's destination.
            var native=actor.GetComponent<NativeResidentNavigation>();if(native)native.ResetPlacement();
            actor.GetComponent<CrowdMotion>().RestorePlacement(ground);return actor;
        }
        private void HoldFamily(HouseholdJointProgress progress)
        {ResidentActor a,b;actors.TryGetValue(progress.FirstResidentId,out a);actors.TryGetValue(progress.SecondResidentId,out b);HouseholdJointBodyMotion.Hold(a);HouseholdJointBodyMotion.Hold(b);}
        private void HideFamily(HouseholdJointProgress progress)
        {foreach(var id in new[]{progress.FirstResidentId,progress.SecondResidentId}){ResidentActor actor;if(actors.TryGetValue(id,out actor) && actor){actor.CaptureState();actor.gameObject.SetActive(false);Destroy(actor.gameObject);}actors.Remove(id);}}
        private bool YieldHousehold(HouseholdJointProgress progress,double now)
        {
            var sim=worldStore.ReadHouseholdJointSimulation(progress.SettlementId,progress.HomeKey);
            if(sim!=null && sim.Active)return true;
            if(!HouseholdParentAvailable(progress.FirstResidentId,now) || !HouseholdParentAvailable(progress.SecondResidentId,now))return false;
            if(!interior && (progress.Phase==HouseholdJointPhase.Outbound || progress.Phase==HouseholdJointPhase.Returning))
            {
                ResidentActor a,b;actors.TryGetValue(progress.FirstResidentId,out a);actors.TryGetValue(progress.SecondResidentId,out b);
                if(a && !JointDoorBodyMatches(a,progress.First) || b && !JointDoorBodyMatches(b,progress.Second))return false;
            }
            try{worldStore.BeginHouseholdJointSimulation(progress.HouseholdDayId,progress.Sequence,now);return true;}
            catch(ArgumentException){return false;}
        }
        private void YieldHouseholds(double now,bool retainObservedInn=false)
        {
            if(worldStore==null || population==null || GameManager.Instance.SaveLoadManager.LoadInProgress)return;
            CaptureHouseholdInns(now);
            foreach(var progress in worldStore.ReadActiveHouseholdJoints(population.SettlementId))
                if(!retainObservedInn || !HasObservedHouseholdInnOwner(progress.HomeKey))YieldHousehold(progress,now);
        }
    }
}
