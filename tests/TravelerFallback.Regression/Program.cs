using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.Tests;

internal static class Program
{
    private static void Check(bool value,string message) {if(!value)throw new Exception(message);}
    private static WorldStateStore Fixture(out TravelerVisit visit)
    {
        var store=new WorldStateStore(1);store.GetOrCreate(1,new int[0]);
        store.RegisterLodgingBeds(1,7,new[]{new LodgingBed {Id="bed:0"}});
        store.RegisterLodgingBeds(1,8,new[]{new LodgingBed {Id="bed:0"}});
        var person=new TravelerPerson {Id="traveler:test",Name="Test Guest",HomeSettlement=2,Race=1};
        visit=new TravelerVisit {Id="visit",PersonId=person.Id,SettlementId=1,TavernKey=7,Nights=1,DesiredArrival=100,CommuteSeconds=10,
            Route=new List<SavedRoutePoint>{new SavedRoutePoint(),new SavedRoutePoint {X=10}}};
        store.RegisterTraveler(person);store.RegisterTravelerVisit(visit);
        var journey=store.TravelerJourneyFor(visit.Id);
        store.TryBookLodging(1,7,"full","other",2,false,1,journey.CheckInAt,journey.CheckOutAt);
        Check(!store.TryAdmitTraveler(visit.Id,journey.CheckInAt),"Preferred Inn must be full");return store;
    }
    private static int Main()
    {
        try
        {
            TravelerVisit original;var store=Fixture(out original);
            Check(store.IsResidentAway(original.PersonId,120),"A refused guest seeking shelter must remain absent from origin home");
            // Native NavMesh float sampling may differ from the saved exact
            // doorway by less than the existing 1 mm admission tolerance.
            var path=new List<SavedRoutePoint>{new SavedRoutePoint {X=10.0005},new SavedRoutePoint {X=30}};
            Check(!store.TryRedirectTraveler(original.Id,99,false,path,20,110),"Unknown Inn cannot fabricate lodging inventory");
            Check(!store.TryRedirectTraveler(original.Id,-1,true,new[]{new SavedRoutePoint {X=10},new SavedRoutePoint {X=-20}},30,110),"Camp path must cross its measured exit");
            Check(store.TravelerVisitsFor(1)[0].Diversion==null,"Rejected fallback leaves original visit untouched");
            Check(store.TryRedirectTraveler(original.Id,8,false,path,20,110),"Free alternate Inn should reserve a bed");
            var journey=store.TravelerJourneyFor(original.Id);
            Check(journey.Evaluate(110).Position.X==10,"Diversion starts at actual refused doorway");
            Check(Math.Abs(journey.Evaluate(120).Position.X-20)<0.001,"Diversion walks measured route");
            Check(store.TravelerBed(original.Id,129)==null && store.TravelerBed(original.Id,130)!=null,"No bed before arrival");
            var restored=new WorldStateStore(1);restored.Restore(store.Snapshot());
            Check(restored.TravelerJourneyFor(original.Id).Evaluate(120).Position.X==20,"Diversion survives current save");
            Check(!store.TryRedirectTraveler(original.Id,7,false,path,20,111),"No second redirection");
            var corrupt=store.Snapshot();corrupt.TravelerVisits[0].Diversion.Route[0].X=11;
            bool rejected=false;try{restored.Restore(corrupt);}catch(ArgumentException){rejected=true;}
            Check(rejected,"Current save rejects disconnected diversion provenance");
            store=Fixture(out original);
            Check(store.TryRedirectTraveler(original.Id,-1,true,new[]{new SavedRoutePoint {X=10},new SavedRoutePoint(),new SavedRoutePoint {X=-20}},30,110),"Actual outside camp path accepted");
            journey=store.TravelerJourneyFor(original.Id);
            Check(journey.CheckOutAt>140 && journey.Evaluate(200).Position.X==-20,"Camp stays until planned departure");
            Check(store.TravelerBed(original.Id,200)==null,"Camp must never fabricate a bed");
            Check(TravelerConversationContext.PlannedActivity(store,original.PersonId,1,200,key=>"Preferred Inn").Contains("outside camp"),"Camp activity must not claim an Inn stay");
            Check(Math.Abs(journey.Evaluate(journey.CheckOutAt+10).Position.X+10)<0.001,"Camp departure stays outside rather than revisiting refused Inn");
            restored.Restore(store.Snapshot());
            Check(restored.TravelerJourneyFor(original.Id).Evaluate(200).Position.X==-20,"Camp survives current save");
            Check(store.InitializeNpcInventory(original.PersonId,100,new[]{new NpcInventoryItem {Uid=1,NativePayload="food"},new NpcInventoryItem {Uid=2,NativePayload="gift"},new NpcInventoryItem {Uid=3,NativePayload="potion"}}),"Inventory fixture");
            Check(store.RecordNpcOrdinaryPurchase(original.PersonId,1),"Tag actual owned purchase");
            Check(!store.RecordNpcOrdinaryPurchase(original.PersonId,99),"Reject unknown UID");
            restored.Restore(store.Snapshot());
            Check(restored.ConsumeNpcOrdinaryPurchasesAtHome(original.PersonId)==1,"Consume only tagged ordinary food");
            Check(restored.ReadNpcInventory(original.PersonId).Items.Count==2 && restored.ReadNpcInventory(original.PersonId).WalletGold==100,"Preserve gifts, potions and wallet");
            Check(restored.ConsumeNpcOrdinaryPurchasesAtHome(original.PersonId)==0,"Consumption idempotent");
            VerifyPurchaseProvenance();
            VerifyLocalTravelAbsence();
            ResidentFallbackChecks.Run();
            var linked=new List<(string Name,Action Body)>();TravelerDemandTests.Add(linked);TravelerSaveTests.Add(linked);
            ResidentVisitTests.Add(linked);ResidentRoadStopSaveTests.Add(linked);
            int failed=0;
            foreach(var item in linked){try{item.Body();}catch(Exception error){failed++;Console.Error.WriteLine("FAIL "+item.Name+": "+error.Message);}}
            if(failed>0)throw new Exception(failed+" linked traveler/resident checks failed");
            Console.WriteLine("PASS alternate Inn route/arrival/save, outside camp stay/save/departure, purchase provenance/task retention and "+linked.Count+" linked traveler tests");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }

    private static void VerifyPurchaseProvenance()
    {
        var store=new WorldStateStore(9);var town=store.GetOrCreate(1,new[]{10,20},1);
        string buyer=town.Households[0].Residents[0].Id,friend=town.Households[1].Residents[0].Id;
        store.InitializeNpcInventory(buyer,100,new[]{new NpcInventoryItem {Uid=1,NativePayload="food"},new NpcInventoryItem {Uid=2,NativePayload="food-two"}});
        store.InitializeNpcInventory(friend,0,new NpcInventoryItem[0]);
        Check(!store.HasNpcOrdinaryPurchases(buyer),"Untagged home actor skips native capture");
        store.RecordNpcOrdinaryPurchase(buyer,1);
        var before=store.ReadNpcInventory(buyer);
        Check(store.CommitNpcInventoryTransaction("capture",new[]{new NpcInventoryUpdate {PersonId=buyer,ExpectedRevision=before.Revision,
            WalletGold=100,Items=new List<NpcInventoryItem>{new NpcInventoryItem {Uid=1,NativePayload="food after native capture"},new NpcInventoryItem {Uid=2,NativePayload="food-two"}}}}),"Native-capture-style write");
        Check(store.ReadNpcInventory(buyer).Items[0].OrdinaryPurchase,"Same-owner capture preserves receipt");
        before=store.ReadNpcInventory(buyer);var other=store.ReadNpcInventory(friend);
        Check(store.CommitNpcInventoryTransaction("gift",new[]{new NpcInventoryUpdate {PersonId=buyer,ExpectedRevision=before.Revision,WalletGold=100,
            Items=new List<NpcInventoryItem>{before.Items[1]}},new NpcInventoryUpdate {PersonId=friend,ExpectedRevision=other.Revision,WalletGold=0,
            Items=new List<NpcInventoryItem>{before.Items[0]}}}),"Transfer actual UID to friend");
        Check(!store.HasNpcOrdinaryPurchases(friend) && store.ConsumeNpcOrdinaryPurchasesAtHome(friend)==0,"Gift does not inherit ordinary-consumption marker");
        store.RecordNpcOrdinaryPurchase(buyer,2);
        var action=new NpcActionJob {Id="protect-task-item",ProposalId="protect-task-item",ResidentId=buyer,Kind=NpcActionKind.Transfer,
            Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player",ItemId="2"},Phase=NpcActionPhase.Accepted,
            Revision=1,AcceptedAt=100,UpdatedAt=100,DeadlineAt=400,
            LastPosition=new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,WorldSpaceId="settlement:1",SettlementId=1}};
        string reason;Check(ClassifiedAdmissionFixture.TryAcceptAction(store,action,true,true,true,out reason),reason);
        Check(store.ConsumeNpcOrdinaryPurchasesAtHome(buyer)==0,"Active agreed item action defers consumption");
        Check(store.ObserveNpcAction(action.Id,1,101,NpcActionObservation.Cancelled("Fixture cancellation")),"Cancel without transferring item");
        var ended=store.ReadNpcAction(action.Id);
        Check(store.SaveNpcActionPosition(ended.Id,ended.Revision,ended.LastPosition,false,102),"Actual routine rejoin");
        Check(store.ConsumeNpcOrdinaryPurchasesAtHome(buyer)==0,"Exact retained task item survives home return");
        Check(!store.HasNpcOrdinaryPurchases(buyer),"Retained task item clears ordinary marker to avoid repeated native captures");
        var restored=new WorldStateStore(1);restored.Restore(store.Snapshot());
        Check(restored.ReadNpcInventory(buyer).Items.Count==1 && restored.ReadNpcInventory(buyer).Items[0].Uid==2,
            "Task-item UID survives current save");
    }

    private static void VerifyLocalTravelAbsence()
    {
        var store=new WorldStateStore(17);var family=store.ReserveHousehold(9,new HomeProfile(91,4,180));
        RegionalIdentity.Initialize(family,1,(seed,sex)=>"Person"+seed,seed=>"Origin");
        var resident=family.Residents[0];store.GetOrCreate(1,new int[0]);
        store.RegisterLodgingBeds(1,7,new[]{new LodgingBed {Id="bed"}});
        var person=new TravelerPerson {Id=resident.Id,Name=resident.FullName,HomeSettlement=9,HomeKey=resident.HomeKey,
            Race=resident.Race,Sex=resident.Sex,AppearanceSeed=resident.AppearanceSeed};
        store.RegisterTraveler(person);
        var visit=new TravelerVisit {Id="origin-absence",PersonId=resident.Id,SettlementId=1,TavernKey=7,Nights=1,DesiredArrival=100,
            CommuteSeconds=10,Route=new List<SavedRoutePoint>{new SavedRoutePoint(),new SavedRoutePoint {X=10}}};
        store.RegisterTravelerVisit(visit);var journey=store.TravelerJourneyFor(visit.Id);
        var home=new ActivitySnapshot(Activity.Home,resident.HomeKey,new RoutePoint(4,5),0);
        Check(store.LocalResidentState(resident.Id,99,home).BuildingKey==resident.HomeKey,"Predeparture keeps actual origin routine");
        var away=store.LocalResidentState(resident.Id,105,home);
        Check(away.Activity==Activity.Visit && away.BuildingKey==-1,"Traveler-backed absence hides origin without assuming ResidentTrip");
        store.TryBookLodging(1,7,"full","player",1,true,1,journey.CheckInAt,journey.CheckOutAt);
        Check(!store.TryAdmitTraveler(visit.Id,journey.CheckInAt),"Actual full-Inn refusal");
        var restored=new WorldStateStore(1);restored.Restore(store.Snapshot());
        away=restored.LocalResidentState(resident.Id,130,home);
        Check(away.Activity==Activity.Visit && away.BuildingKey==-1,"Saved unresolved refusal retains hidden origin without a ResidentTrip");
        Check(restored.LocalResidentState(resident.Id,journey.ExitAt+1,home).BuildingKey==resident.HomeKey,"Ended traveler absence returns actual origin routine");
    }
}
