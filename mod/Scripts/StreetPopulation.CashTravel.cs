using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private void CaptureCashExits(double now)
        {
            foreach(var pair in actors)
            {
                var actor=pair.Value;var visit=worldStore.ReadNpcCashVisit(pair.Key);
                if(!actor || visit==null || visit.CounterJourney!=null || !ActionSpaceMatches(visit.Position))continue;
                if(visit.WaitingForBank && interior.EntryDoor.buildingKey==actor.Resident.HomeKey)
                    continue;
                var route=indoorGeometry.RouteToExit(actor.transform.position-UnityEngine.Vector3.up);
                if(route.Count<1)continue;
                visit.OutsideJourney=new ObservedVisit {ResidentId=pair.Key,SettlementId=population.SettlementId,
                    BuildingKey=interior.EntryDoor.buildingKey,ObservedAt=now,SecondsPerMetre=DaggerfallUnity.Instance.WorldTime.TimeScale/actor.WalkingSpeed,
                    Legs=new List<ObservedVisitLeg>{new ObservedVisitLeg{Route=route,AfterPhase=ShopVisitPhase.Complete}}};
                visit.OutsideJourney.ResumeUntil=visit.OutsideJourney.PlannedEndAt=visit.OutsideJourney.EndsAt;
                visit.Position=ActionPosition(actor.transform.position-UnityEngine.Vector3.up);SaveCashVisit(pair.Key,visit);
            }
        }

        private bool AdvanceUnobservedCashExit(Plan plan,NpcCashVisit visit,double now)
        {
            if(visit.Position.Kind!=NpcActionTargetKind.IndoorPoint)return true;
            if(visit.Returning && visit.Rendezvous!=null && visit.Position.BuildingKey==visit.Rendezvous.BuildingKey)
            {SaveCashVisit(plan.Resident.Id,null);return true;}
            bool waitingHome=visit.WaitingForBank && visit.Position.BuildingKey==plan.Resident.HomeKey;
            if(!waitingHome && (visit.OutsideJourney==null || !visit.OutsideJourney.Evaluate(Math.Max(now,visit.OutsideJourney.ObservedAt)).Completed))return true;
            TownGeometry.Entrance door;
            if(!geometry.Entrances.TryGetValue(visit.Position.BuildingKey,out door))return true;
            visit.Position=MedicalDoorPosition(door);visit.OutsideJourney=null;SaveCashVisit(plan.Resident.Id,visit);return true;
        }

        private NpcActionTarget CashScheduledPosition(Plan plan)
        {
            var scheduled=ActiveSchedule(plan).EvaluateSeconds(ActionNow());
            ActivitySnapshot shared;if(TryHouseholdJointTravelState(plan,out shared))scheduled=shared;
            if(scheduled.Activity==Activity.Travel || scheduled.Activity==Activity.Meeting)
                return new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,SettlementId=population.SettlementId,
                    WorldSpaceId=ActionWorldSpace,BuildingKey=-1,X=scheduled.Position.X,Z=scheduled.Position.Z};
            TownGeometry.Entrance door;
            if(!geometry.Entrances.TryGetValue(scheduled.BuildingKey,out door))return null;
            return MedicalDoorPosition(door);
        }

        // The same surveyed outdoor path continues while the player is indoors
        // or far away. Observed bodies still use the physical movement owner.
        private bool AdvanceUnobservedCashVisit(Plan plan,NpcCashVisit visit,double now)
        {
            string id=plan.Resident.Id;
            TownGeometry.Entrance door;
            int destination=visit.Returning?visit.ReturnKey:visit.ProviderKey;
            if(!geometry.Entrances.TryGetValue(destination,out door))return true;
            if(visit.OutsideJourney==null)
            {
                double targetX=door.LocalDoor.x,targetZ=door.LocalDoor.z;
                if(visit.Returning && visit.Rendezvous!=null && visit.Rendezvous.Kind==NpcActionTargetKind.WorldPoint)
                {targetX=visit.Rendezvous.X;targetZ=visit.Rendezvous.Z;}
                var path=geometry.RouteBetweenPoints(new SavedRoutePoint{X=visit.Position.X,Z=visit.Position.Z},
                    new SavedRoutePoint{X=targetX,Z=targetZ});
                double dx=visit.Position.X-targetX,dz=visit.Position.Z-targetZ;
                if(path.Count<2 && dx*dx+dz*dz>0.36)return true;
                var route=new List<GuardPoint3>();
                foreach(var point in path)route.Add(new GuardPoint3 {X=point.X,Z=point.Z});
                if(route.Count==0)route.Add(new GuardPoint3{X=targetX,Z=targetZ});
                visit.OutsideJourney=new ObservedVisit {ResidentId=id,SettlementId=population.SettlementId,
                    BuildingKey=destination,ObservedAt=now,SecondsPerMetre=DaggerfallUnity.Instance.WorldTime.TimeScale/
                        ResidentMovement.WalkSpeedFor(plan.Resident.AppearanceSeed),
                    Legs=new List<ObservedVisitLeg>{new ObservedVisitLeg{Route=route,AfterPhase=ShopVisitPhase.Complete}}};
                visit.OutsideJourney.ResumeUntil=visit.OutsideJourney.PlannedEndAt=visit.OutsideJourney.EndsAt;
            }
            var position=visit.OutsideJourney.Evaluate(Math.Max(now,visit.OutsideJourney.ObservedAt));
            visit.Position.X=position.Position.X;visit.Position.Z=position.Position.Z;
            if(!position.Completed){SaveCashVisit(id,visit);return true;}
            if(visit.Returning)
            {
                if(visit.Rendezvous!=null && visit.Rendezvous.Kind==NpcActionTargetKind.IndoorPoint)
                {visit.Position=NpcActionExecutor.CopyTarget(visit.Rendezvous);visit.OutsideJourney=null;SaveCashVisit(id,visit);}
                else SaveCashVisit(id,null);
                return true;
            }
            if(visit.WaitingForBank){SaveCashVisit(id,visit);return true;}
            if(!CashProviderAvailable(plan,null,visit))
            {visit.WaitingForBank=true;visit.ProviderKey=plan.Resident.HomeKey;visit.Bank=false;visit.OutsideJourney=null;SaveCashVisit(id,visit);return true;}
            if(visit.Bank){BeginBankCounterVisit(plan,null,visit,now);return true;}
            if(visit.ServiceAt==0){visit.ServiceAt=now+60;SaveCashVisit(id,visit);return true;}
            var inventory=worldStore.ReadNpcInventory(id);
            if(now>=visit.ServiceAt && inventory!=null && worldStore.TryCompleteNpcCashVisit(worldStore.CaptureNpcVitalsWriteEpoch(),id,
                inventory.Revision,visit.ProviderKey,NativeNpcInventoryBridge.InitialWalletGold(worldStore,id),now))
            {
                if(visit.Rendezvous==null)SaveCashVisit(id,null);
                else {visit=worldStore.ReadNpcCashVisit(id);visit.OutsideJourney=null;SaveCashVisit(id,visit);}
            }
            return true;
        }
    }
}
