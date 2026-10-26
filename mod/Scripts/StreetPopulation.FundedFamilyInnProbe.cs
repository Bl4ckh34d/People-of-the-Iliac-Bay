using System;
using System.Collections;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        // Continue the actual morning-funded plan. No injected pair, route,
        // receipt, seat or manual movement controller substitutes for gameplay.
        private IEnumerator VerifyFundedFamilyInn(HouseholdDayPlan day,string payer,string partner)
        {
            var game=GameManager.Instance;bool motorEnabled=game.PlayerMotor.enabled;
            int balance=worldStore.ReadNpcInventory(payer).WalletGold,otherBalance=worldStore.ReadNpcInventory(partner).WalletGold;
            int playerGold=game.PlayerEntity.GoldPieces;
            try
            {
                yield return ObserveFundedFamilyExterior(day,false);
                DaggerfallStaticDoors owner=null;StaticDoor entrance=default(StaticDoor);
                foreach(var doors in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                    foreach(var door in doors.Doors)
                        if(door.doorType==DoorTypes.Building && door.buildingKey==day.JointInn.InnKey){owner=doors;entrance=door;break;}
                if(!owner)throw new InvalidOperationException("Funded family's Inn entrance is missing.");
                TownSmokeHarness.EnterInterior(owner.transform,entrance);
                float deadline=Time.realtimeSinceStartup+30;
                while((!interior || interior.EntryDoor.buildingKey!=day.JointInn.InnKey || indoorGeometry==null || shopGeometry==null) &&
                    Time.realtimeSinceStartup<deadline)yield return null;
                if(!interior || interior.EntryDoor.buildingKey!=day.JointInn.InnKey || indoorGeometry==null || shopGeometry==null)
                    throw new InvalidOperationException("Funded family's live Inn did not bind.");
                yield return new WaitForSecondsRealtime(2);DaggerfallUI.Instance.PopToHUD();
                Vector3 observer=indoorGeometry.EntryGround.Value;bool found=false;
                foreach(var anchor in indoorGeometry.LocalAnchors)
                {
                    var point=interior.transform.TransformPoint(anchor);
                    if(FurnitureSeatCatalog.Blocks(point,point,CrowdMotion.BodyRadius) || indoorGeometry.RouteToExit(point).Count<2)continue;
                    bool occupied=false;
                    foreach(var nearby in ShopDepartureNeighbours(point,null))if(Vector3.Distance(nearby,point)<1.2f){occupied=true;break;}
                    if(!occupied && (point-indoorGeometry.EntryGround.Value).sqrMagnitude>(observer-indoorGeometry.EntryGround.Value).sqrMagnitude)
                    {observer=point;found=true;}
                }
                if(!found)throw new InvalidOperationException("Funded family has no clear Inn observer floor.");
                game.PlayerMotor.enabled=false;game.PlayerMotor.transform.position=observer+Vector3.up;Physics.SyncTransforms();
                ReportFundedFamilyInnCatalog(day);
                bool seated=false,physicalReceipts=false;int cost=0;deadline=Time.realtimeSinceStartup+180;
                float reportAt=0;
                while(Time.realtimeSinceStartup<deadline)
                {
                    if(Time.realtimeSinceStartup>=reportAt)
                    {ReportFundedFamilyInn(day);reportAt=Time.realtimeSinceStartup+20;}
                    var first=FindActor(day.First.ResidentId);var second=FindActor(day.Second.ResidentId);
                    var a=worldStore.ReadNpcInnConsumption(day.First.ResidentId);var b=worldStore.ReadNpcInnConsumption(day.Second.ResidentId);
                    var progress=worldStore.ReadHouseholdJointProgress(day.SettlementId,day.HomeKey);
                    bool paid=a!=null && b!=null && a.CompletedAt>=progress.EnteredAt && b.CompletedAt>=progress.EnteredAt &&
                        a.Outcome==NpcInnConsumptionOutcome.Purchased && b.Outcome==NpcInnConsumptionOutcome.Purchased;
                    if(paid)
                    {
                        if(!physicalReceipts)
                        {
                            var saved=worldStore.Snapshot();
                            foreach(string id in new[]{day.First.ResidentId,day.Second.ResidentId})
                            {
                                var cursor=saved.NpcInnConsumptionCursors.Find(c=>c.BuyerId==id);
                                if(cursor==null || cursor.HouseholdDayId!=day.Id || cursor.SourceKind!=null)
                                    throw new InvalidOperationException("Family receipt was projected before the observed counter service: "+id+"; source="+cursor?.SourceKind);
                            }
                            physicalReceipts=true;
                        }
                        if(a.PayerId!=payer || b.PayerId!=payer || a.MenuIndex!=day.JointInn.FirstMenuIndex || b.MenuIndex!=day.JointInn.SecondMenuIndex)
                            throw new InvalidOperationException("Funded family used a different payer or planned menu.");
                        cost=a.ChargedGold+b.ChargedGold;
                        if(cost!=day.JointInn.FirstPriceGold+day.JointInn.SecondPriceGold || worldStore.ReadNpcInventory(payer).WalletGold!=balance-cost ||
                            worldStore.ReadNpcInventory(partner).WalletGold!=otherBalance || game.PlayerEntity.GoldPieces!=playerGold)
                            throw new InvalidOperationException("Funded family's native payment differs from its two receipts.");
                        if(first && second && first.IsSeated && second.IsSeated){seated=true;break;}
                    }
                    yield return null;
                }
                if(!seated)throw new InvalidOperationException("Funded family did not both pay and sit: phase="+
                    worldStore.ReadHouseholdJointProgress(day.SettlementId,day.HomeKey)?.Phase);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FUNDED_FAMILY_SEATED: payer="+payer+"; chargedGold="+cost+"; bothSeated=true; observedInn=true");
                // A time skip is tested only after both actual purchases/seats.
                // Exit and return still have to be walked by the real bodies.
                var seatedFirst=FindActor(day.First.ResidentId);var seatedSecond=FindActor(day.Second.ResidentId);
                Vector3 firstSeat=seatedFirst.transform.position,secondSeat=seatedSecond.transform.position;
                float holdUntil=Time.realtimeSinceStartup+5;
                while(Time.realtimeSinceStartup<holdUntil)
                {
                    if(!seatedFirst || !seatedSecond || !seatedFirst.IsSeated || !seatedSecond.IsSeated ||
                        Vector3.Distance(firstSeat,seatedFirst.transform.position)>0.1f || Vector3.Distance(secondSeat,seatedSecond.transform.position)>0.1f)
                        throw new InvalidOperationException("Family stopped sitting or moved in its chairs before the time skip.");
                    yield return null;
                }
                double leave=Math.Max(worldStore.GetObservedVisit(day.First.ResidentId).PlannedEndAt,
                    worldStore.GetObservedVisit(day.Second.ResidentId).PlannedEndAt)+1;
                if(leave>ActionNow()){game.PlayerEntity.PreventEnemySpawns=true;DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)(leave-ActionNow()));}
                deadline=Time.realtimeSinceStartup+120;
                while(worldStore.ReadHouseholdJointProgress(day.SettlementId,day.HomeKey).Phase!=HouseholdJointPhase.Returning &&
                    Time.realtimeSinceStartup<deadline)yield return null;
                if(worldStore.ReadHouseholdJointProgress(day.SettlementId,day.HomeKey).Phase!=HouseholdJointPhase.Returning)
                    throw new InvalidOperationException("Funded family did not physically leave the Inn.");
                game.PlayerMotor.enabled=motorEnabled;game.PlayerEnterExit.TransitionExterior();
                deadline=Time.realtimeSinceStartup+30;
                while((game.IsPlayerInside || interior) && Time.realtimeSinceStartup<deadline)yield return null;
                DaggerfallUI.Instance.PopToHUD();
                if(game.IsPlayerInside || interior)throw new InvalidOperationException("Family observer did not exit the actual Inn.");
                yield return ObserveFundedFamilyExterior(day,true);
                if(worldStore.ReadNpcInventory(payer).WalletGold!=balance-cost || worldStore.ReadNpcInventory(partner).WalletGold!=otherBalance ||
                    game.PlayerEntity.GoldPieces!=playerGold)throw new InvalidOperationException("Family payment repeated after service.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FUNDED_FAMILY_INN_PASS: naturalPlan=true; morningCash=true; observedOutward=true; observedInn=true; "+
                    "bothPaid=true; bothSeated=true; observedReturn=true; chargedGold="+cost+"; seatedDwellSkipped=true");
            }
            finally{if(game && game.PlayerMotor)game.PlayerMotor.enabled=motorEnabled;}
        }

        private void ReportFundedFamilyInnCatalog(HouseholdDayPlan day)
        {
            var catalog=worldStore.ReadNpcInnServiceCatalog(day.SettlementId,day.JointInn.InnKey);
            int matched=0,available=0;
            if(catalog!=null && shopGeometry?.Seating!=null)
                foreach(var pair in catalog.Pairs)
                {
                    if(shopGeometry.Seating.Find(pair.First.Id)!=null && shopGeometry.Seating.Find(pair.Second.Id)!=null)matched++;
                    if(CanReserveHouseholdSeats(catalog,pair))available++;
                }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FUNDED_FAMILY_CATALOG: present="+(catalog!=null)+"; pairs="+catalog?.Pairs.Count+
                "; matchingLivePairs="+matched+"; availableLivePairs="+available+"; keeperAlive="+
                (catalog==null?null:(bool?)worldStore.NativePersonInSettlement(day.SettlementId,catalog.KeeperId)?.IsAlive));
            foreach(var outing in new[]{day.JointInn.First,day.JointInn.Second})
                foreach(var visit in ResidentVisitJourney.Create(outing.DepartureSeconds,outing.Itinerary).ScheduledVisits())
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FUNDED_FAMILY_WINDOW: now="+ActionNow()+"; plannedLeave="+(day.AbsoluteDay*86400d+visit.LeaveAt));
        }

        private void ReportFundedFamilyInn(HouseholdDayPlan day)
        {
            double now=ActionNow();var progress=worldStore.ReadHouseholdJointProgress(day.SettlementId,day.HomeKey);
            var sim=worldStore.ReadHouseholdJointSimulation(day.SettlementId,day.HomeKey);
            var service=worldStore.ReadHouseholdInnServiceAdmission(day.Id);
            JointInnBinding binding;jointInnBindings.TryGetValue(day.HomeKey,out binding);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FUNDED_FAMILY_INDOOR: phase="+progress?.Phase+"; cash="+FamilyNeedsCash(progress)+
                "; simulation="+sim?.Active+"/"+sim?.Phase+"/"+sim?.PauseReason+"; service="+service?.Outcome+"/"+service?.Materialized+
                "; tavern="+shopGeometry?.IsTavern+"; seating="+(shopGeometry?.Seating!=null)+"; motion="+(binding?.Motion!=null)+
                "; now="+now+"; serviceArrival="+service?.ArrivalAt+"; return="+service?.ReturnAt+"; offset="+sim?.InnClockOffset);
            foreach(string id in new[]{day.First.ResidentId,day.Second.ResidentId})
            {
                var actor=FindActor(id);PhysicalShopVisit visit;physicalShopVisits.TryGetValue(id,out visit);
                var projection=worldStore.ProjectHouseholdInnMember(day.Id,id,now);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FUNDED_FAMILY_MEMBER: id="+id+"; body="+(bool)actor+"; active="+(actor && actor.gameObject.activeInHierarchy)+
                    "; seated="+(actor && actor.IsSeated)+"; available="+HouseholdParentAvailable(id,now)+"; needsCash="+worldStore.NeedsNpcCashErrand(id,now)+
                    "; cashVisit="+(worldStore.ReadNpcCashVisit(id)!=null)+"; projection="+(projection!=null)+"; seat="+projection?.SeatId+
                    "; visit="+visit?.Snapshot.Phase+"; receipt="+worldStore.ReadNpcInnConsumption(id)?.Outcome);
            }
        }

        private IEnumerator ObserveFundedFamilyExterior(HouseholdDayPlan day,bool returning)
        {
            var game=GameManager.Instance;string[] ids={day.First.ResidentId,day.Second.ResidentId};
            var previous=new Vector3[2];var visible=new bool[2];var seen=new bool[2];var travel=new double[2];float maxStep=0;
            float deadline=Time.realtimeSinceStartup+300,nextLog=0;
            var end=returning?HouseholdJointPhase.Completed:HouseholdJointPhase.Visiting;
            while(Time.realtimeSinceStartup<deadline)
            {
                var progress=worldStore.ReadHouseholdJointProgress(day.SettlementId,day.HomeKey);
                if(progress==null || progress.Phase==HouseholdJointPhase.Cancelled)throw new InvalidOperationException("Funded family lost its shared plan.");
                if(progress.Phase==end)
                {
                    if(travel[0]<5 || travel[1]<5)throw new InvalidOperationException("Family leg completed without observed movement by both bodies.");
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FUNDED_FAMILY_LEG: returning="+returning+"; firstMetres="+travel[0]+"; secondMetres="+travel[1]+"; maxStep="+maxStep);
                    yield break;
                }
                for(int i=0;i<2;i++)
                {
                    var actor=FindActor(ids[i]);bool active=actor && actor.gameObject.activeInHierarchy;
                    if(seen[i] && !active)throw new InvalidOperationException("Observed family body disappeared before the exterior leg finished: "+ids[i]);
                    if(active)
                    {
                        seen[i]=true;
                        var point=actor.transform.position;
                        if(visible[i]){float step=Vector3.Distance(point,previous[i]);travel[i]+=step;maxStep=Mathf.Max(maxStep,step);
                            if(step>2)throw new InvalidOperationException("Funded family body jumped during exterior leg: "+step);}
                        previous[i]=point;
                        if(i==0)game.PlayerMotor.transform.position=point+new Vector3(7,1,7);
                    }
                    visible[i]=active;
                }
                Physics.SyncTransforms();
                if(Time.realtimeSinceStartup>=nextLog)
                {nextLog=Time.realtimeSinceStartup+20;Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FUNDED_FAMILY_PROGRESS: returning="+returning+"; phase="+progress.Phase+
                    "; first="+progress.First.Distance+"; second="+progress.Second.Distance+"; firstVisible="+visible[0]+"; secondVisible="+visible[1]);}
                yield return null;
            }
            throw new InvalidOperationException("Funded family's exterior leg did not finish: returning="+returning+"; phase="+
                worldStore.ReadHouseholdJointProgress(day.SettlementId,day.HomeKey)?.Phase+"; firstMetres="+travel[0]+"; secondMetres="+travel[1]);
        }
    }
}
