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
            // This owned routine fixture advances time past native quest-letter
            // deadlines. Isolate unrelated quest popups, which pause all actors.
            var quests=DaggerfallWorkshop.Game.Questing.QuestMachine.Instance;
            bool questsEnabled=quests && quests.enabled;
            int balance=worldStore.ReadNpcInventory(payer).WalletGold,otherBalance=worldStore.ReadNpcInventory(partner).WalletGold;
            int playerGold=game.PlayerEntity.GoldPieces;
            // Inspect only each participant's bounded memory index. A whole-world
            // snapshot on every frame would distort this native movement test.
            var sceneRows=typeof(WorldStateStore).GetMethod("SceneMemoriesFor",
                System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            if(sceneRows==null)throw new InvalidOperationException("Missing bounded scene-memory index.");
            var memoryExpiry=new System.Collections.Generic.Dictionary<string,double>();
            NpcInnConsumptionReceipt[] paidMemoryReceipts=null;
            void AssertInnMemories(NpcInnConsumptionReceipt[] receipts)
            {
                foreach(var receipt in receipts)
                {
                    string actualPayer=receipt.PayerId??receipt.BuyerId,eventId="inn-purchase:"+receipt.VisitId;
                    var observers=new System.Collections.Generic.HashSet<string>{
                        day.First.ResidentId,day.Second.ResidentId,receipt.KeeperId,actualPayer};
                    foreach(string observerId in observers)
                    {
                        var rows=(System.Collections.Generic.List<NpcSceneMemory>)sceneRows.Invoke(worldStore,new object[]{observerId,false});
                        int count=0;NpcSceneMemory match=null;
                        if(rows!=null)foreach(var row in rows)if(row.EventId==eventId){count++;match=row;}
                        bool entitled=observerId==receipt.BuyerId || observerId==receipt.KeeperId || observerId==actualPayer;
                        if(count!=(entitled?1:0))throw new InvalidOperationException(
                            "Inn memory participant/count mismatch: "+observerId+"; event="+eventId+"; count="+count);
                        if(!entitled)continue;
                        string responsible=observerId==receipt.KeeperId?actualPayer:receipt.KeeperId;
                        if(match.Kind!=NpcSceneEventKind.Shopping || match.SubjectId!=receipt.BuyerId ||
                            match.ResponsibleId!=responsible || match.At!=Math.Floor(receipt.CompletedAt) ||
                            match.SettlementId!=day.SettlementId || match.BuildingKey!=day.JointInn.InnKey)
                            throw new InvalidOperationException("Inn memory receipt facts differ.");
                        string key=observerId+"\n"+eventId;double previousExpiry;
                        if(memoryExpiry.TryGetValue(key,out previousExpiry) && previousExpiry!=match.ExpiresAt)
                            throw new InvalidOperationException("Inn memory lifetime renewed.");
                        memoryExpiry[key]=match.ExpiresAt;
                    }
                }
            }
            try
            {
                if(quests)quests.enabled=false;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FUNDED_FAMILY_FIXTURE: questTicksIsolated=true");
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
                var observerRoutes=new[]{worldStore.GetObservedVisit(day.First.ResidentId),worldStore.GetObservedVisit(day.Second.ResidentId)};
                if(observerRoutes[0]==null || observerRoutes[1]==null)
                    throw new InvalidOperationException("Family observer placement has no actual visit routes.");
                Vector3 observer=indoorGeometry.EntryGround.Value;bool found=false;
                foreach(var anchor in indoorGeometry.LocalAnchors)
                {
                    var point=interior.transform.TransformPoint(anchor);
                    if(FurnitureSeatCatalog.Blocks(point,point,CrowdMotion.BodyRadius) || indoorGeometry.RouteToExit(point).Count<2)continue;
                    // A clear floor point can still be the family's chair aisle.
                    // Keep the controlled player clear of every remaining leg.
                    if(!FamilyObserverClearOfRoutes(point,observerRoutes))continue;
                    bool occupied=false;
                    foreach(var nearby in ShopDepartureNeighbours(point,null))if(Vector3.Distance(nearby,point)<1.2f){occupied=true;break;}
                    if(!occupied && (point-indoorGeometry.EntryGround.Value).sqrMagnitude>(observer-indoorGeometry.EntryGround.Value).sqrMagnitude)
                    {observer=point;found=true;}
                }
                if(!found)throw new InvalidOperationException("Funded family has no clear Inn observer floor.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FUNDED_FAMILY_OBSERVER: ground="+observer.ToString("R")+"; routeClearance=1.2");
                game.PlayerMotor.enabled=false;game.PlayerMotor.transform.position=observer+Vector3.up;Physics.SyncTransforms();
                ReportFundedFamilyInnCatalog(day);
                VerifyFundedFamilySeatRoutes(day);
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
                        paidMemoryReceipts=new[]{a,b};AssertInnMemories(paidMemoryReceipts);
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
                    AssertInnMemories(paidMemoryReceipts);
                    yield return null;
                }
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FUNDED_FAMILY_MEMORY_PASS: receipts=2; perspectives="+memoryExpiry.Count+
                    "; exactCounts=true; participantKnowledge=true; lifetimeStable=true");
                double leave=Math.Max(worldStore.GetObservedVisit(day.First.ResidentId).PlannedEndAt,
                    worldStore.GetObservedVisit(day.Second.ResidentId).PlannedEndAt)+1;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FUNDED_FAMILY_LEAVE_REQUEST: now="+ActionNow()+"; leave="+leave);
                if(leave>ActionNow()){game.PlayerEntity.PreventEnemySpawns=true;DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)(leave-ActionNow()));}
                deadline=Time.realtimeSinceStartup+120;reportAt=0;
                while(worldStore.ReadHouseholdJointProgress(day.SettlementId,day.HomeKey).Phase!=HouseholdJointPhase.Returning &&
                    Time.realtimeSinceStartup<deadline)
                {
                    if(Time.realtimeSinceStartup>=reportAt)
                    {ReportFundedFamilyInn(day);reportAt=Time.realtimeSinceStartup+20;}
                    yield return null;
                }
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
            finally
            {
                if(quests)quests.enabled=questsEnabled;
                if(game && game.PlayerMotor)game.PlayerMotor.enabled=motorEnabled;
            }
        }

        private bool FamilyObserverClearOfRoutes(Vector3 candidate,ObservedVisit[] visits)
        {
            foreach(var visit in visits)foreach(var leg in visit.Legs)
                for(int i=0;i<leg.Route.Count;i++)
                {
                    var p=leg.Route[i];var q=leg.Route[Math.Max(0,i-1)];
                    var a=interior.transform.TransformPoint(new Vector3((float)p.X,(float)p.Y,(float)p.Z));
                    var b=interior.transform.TransformPoint(new Vector3((float)q.X,(float)q.Y,(float)q.Z));
                    if(Mathf.Abs(candidate.y-a.y)>2.1f && Mathf.Abs(candidate.y-b.y)>2.1f)continue;
                    a.y=b.y=candidate.y;var delta=b-a;
                    float t=delta.sqrMagnitude<0.000001f?0:Mathf.Clamp01(Vector3.Dot(candidate-a,delta)/delta.sqrMagnitude);
                    if((candidate-(a+delta*t)).sqrMagnitude<1.44f)return false;
                }
            return true;
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

        private void VerifyFundedFamilySeatRoutes(HouseholdDayPlan day)
        {
            var catalog=worldStore.ReadNpcInnServiceCatalog(day.SettlementId,day.JointInn.InnKey);
            if(catalog==null || shopGeometry?.Seating==null)throw new InvalidOperationException("Missing live Inn seat-route fixture.");
            var counter=catalog.EntryToCounter[catalog.EntryToCounter.Count-1];
            var from=new Vector3((float)counter.X,(float)counter.Y,(float)counter.Z);
            var checkedIds=new System.Collections.Generic.HashSet<string>();
            foreach(var pair in catalog.Pairs)foreach(var intent in new[]{pair.First,pair.Second})
            {
                if(!checkedIds.Add(intent.Id))continue;
                var seat=shopGeometry.Seating.Find(intent.Id);
                if(seat==null)throw new InvalidOperationException("Saved family chair has no live identity.");
                var target=interior.transform.InverseTransformPoint(seat.Ground);
                // Reproduce the native restored/world-local rounding mismatch.
                var rounded=target+new Vector3(0.0001f,0.0001f,0);
                var route=shopGeometry.ActivityRouteBetween(from,rounded,null,intent.Id);
                if(route.Count<2)throw new InvalidOperationException("Assigned chair route lost its native approach after coordinate rounding: "+intent.Id);
                var last=route[route.Count-1];
                if(Vector3.Distance(target,new Vector3((float)last.X,(float)last.Y,(float)last.Z))>0.00001f)
                    throw new InvalidOperationException("Assigned chair route did not end at its live seat.");
                if(shopGeometry.ActivityRouteBetween(from,target,null,intent.Id+":missing").Count!=0)
                    throw new InvalidOperationException("Unknown chair identity inherited another chair's route.");
            }
            if(checkedIds.Count<2)throw new InvalidOperationException("No live family seat pair checked.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FUNDED_FAMILY_SEAT_ROUTES_PASS: chairs="+checkedIds.Count+
                "; roundedCoordinates=true; exactIdentity=true; unknownIdentityRejected=true");
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
                var saved=worldStore.GetObservedVisit(id);
                var seat=visit?.SelectedSeatId==null?null:shopGeometry?.Seating?.Find(visit.SelectedSeatId);
                var canSettle=typeof(WorldStateStore).GetMethod("CanSettleHouseholdInn",
                    System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                bool eligible=saved!=null && canSettle!=null && actor &&
                    (bool)canSettle.Invoke(worldStore,new object[]{saved,now,HouseholdInnPointRoutes(actor)});
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FUNDED_FAMILY_MEMBER: id="+id+"; body="+(bool)actor+"; active="+(actor && actor.gameObject.activeInHierarchy)+
                    "; seated="+(actor && actor.IsSeated)+"; available="+HouseholdParentAvailable(id,now)+"; needsCash="+worldStore.NeedsNpcCashErrand(id,now)+
                    "; cashVisit="+(worldStore.ReadNpcCashVisit(id)!=null)+"; projection="+(projection!=null)+"; seat="+projection?.SeatId+
                    "; visit="+visit?.Snapshot.Phase+"; receipt="+worldStore.ReadNpcInnConsumption(id)?.Outcome+
                    "; purchaseFailure="+visit?.PurchaseFailure+"; captureFailure="+visit?.CaptureFailure+
                    "; savedPhase="+(saved==null || now<saved.ObservedAt?"unavailable":saved.Evaluate(now).Phase.ToString())+
                    "; routePoints="+saved?.Legs[0].Route.Count+"; dwell="+saved?.Legs[0].DwellSeconds+
                    "; firstPhase="+saved?.Legs[0].AfterPhase+"; canSettle="+eligible+
                    "; movementFailure="+visit?.LastFailure+"; bodyGround="+(actor?(actor.transform.position-Vector3.up).ToString("R"):"absent")+
                    "; selectedSeat="+visit?.SelectedSeatId+"; seatGround="+seat?.Ground.ToString("R")+"; seatApproach="+seat?.Approach.ToString("R")+
                    "; paused="+GameManager.IsGamePaused+"; window="+DaggerfallUI.UIManager.TopWindow?.GetType().Name);
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
                    "; first="+progress.First.Distance+"; second="+progress.Second.Distance+"; firstVisible="+visible[0]+"; secondVisible="+visible[1]+
                    "; paused="+GameManager.IsGamePaused+"; window="+DaggerfallUI.UIManager.TopWindow?.GetType().Name);}
                yield return null;
            }
            throw new InvalidOperationException("Funded family's exterior leg did not finish: returning="+returning+"; phase="+
                worldStore.ReadHouseholdJointProgress(day.SettlementId,day.HomeKey)?.Phase+"; firstMetres="+travel[0]+"; secondMetres="+travel[1]);
        }
    }
}
