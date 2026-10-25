using System;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int Main()
    {
        try
        {
            var select=typeof(NpcNeedDestinationPolicy).GetMethod("SelectBanking");
            Need(select!=null,"Missing bank-first cash destination policy.");
            Need((NpcNeedDestinationKind)select.Invoke(null,new object[]{true,true,true})==NpcNeedDestinationKind.Bank,"Home displaced an open bank.");
            Need((NpcNeedDestinationKind)select.Invoke(null,new object[]{true,false,true})==NpcNeedDestinationKind.None,"Closed bank incorrectly fell back to home.");
            Need((NpcNeedDestinationKind)select.Invoke(null,new object[]{false,false,true})==NpcNeedDestinationKind.Home,"Settlement without bank did not use home.");
            Need((NpcNeedDestinationKind)select.Invoke(null,new object[]{false,false,false})==NpcNeedDestinationKind.None,"Missing home invented a destination.");
            CashArrivesOnlyAfterVisit();
            CounterVisit();
            Console.WriteLine("PASS bank priority, closed-bank deferral and no-bank home fallback");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
    private static void CashArrivesOnlyAfterVisit()
    {
        var store=new WorldStateStore(42);var town=store.GetOrCreate(100,new[]{10},1);
        string id=town.Households[0].Residents[0].Id;
        store.ReadNpcActor(id,NpcArchetype.Citizen);
        Need(store.InitializeNpcInventory(id,0,new[]{new NpcInventoryItem{Uid=71,NativePayload="owned-item"}}),"Fixture inventory failed.");
        object epoch=store.CaptureNpcVitalsWriteEpoch();
        var visit=new NpcCashVisit {ProviderKey=20,ReturnKey=10,Bank=true,
            Position=new NpcActionTarget{Kind=NpcActionTargetKind.WorldPoint,SettlementId=100,WorldSpaceId="outdoors:100",BuildingKey=-1}};
        Need(store.SaveNpcCashVisit(epoch,id,visit),"Visit not saved.");
        Need(!store.TryCompleteNpcCashVisit(epoch,id,0,20,50,100),"Walking alone created cash.");
        visit.ServiceAt=100;store.SaveNpcCashVisit(epoch,id,visit);
        Need(!store.TryCompleteNpcCashVisit(epoch,id,0,20,50,100),"Bank entrance alone created cash without a clerk visit.");
        visit.CounterJourney=NpcCashVisit.CreateCounterJourney(id,100,20,140,
            new[]{new GuardPoint3(),new GuardPoint3{X=1}},1,1,8);
        visit.ServiceAt=160;Need(store.SaveNpcCashVisit(epoch,id,visit),"Arrival not saved.");
        visit.WaitingForBank=true;visit.CounterJourney=null;store.SaveNpcCashVisit(epoch,id,visit);
        Need(!store.TryCompleteNpcCashVisit(epoch,id,0,20,50,160),"Waiting for an open bank created cash.");
        visit.WaitingForBank=false;
        visit.CounterJourney=NpcCashVisit.CreateCounterJourney(id,100,20,140,
            new[]{new GuardPoint3(),new GuardPoint3{X=1}},1,1,8);
        store.SaveNpcCashVisit(epoch,id,visit);
        Need(!store.TryCompleteNpcCashVisit(epoch,id,0,20,50,159),"Cash arrived before service ended.");
        Need(!store.TryCompleteNpcCashVisit(epoch,id,0,21,50,160),"Wrong provider supplied cash.");
        Need(!store.TryCompleteNpcCashVisit(epoch,id,1,20,50,160),"Stale inventory revision accepted.");
        var restored=new WorldStateStore(42);restored.Restore(store.Snapshot());
        Need(restored.ReadNpcCashVisit(id).ServiceAt==160,"Save/load lost the pending visit.");
        Need(restored.ReadNpcCashVisit(id).CounterJourney.Legs.Count==2,"Save/load lost the clerk route.");
        Need(!restored.TryCompleteNpcCashVisit(epoch,id,0,20,50,160),"Old store epoch accepted.");
        object restoredEpoch=restored.CaptureNpcVitalsWriteEpoch();
        Need(restored.TryCompleteNpcCashVisit(restoredEpoch,id,0,20,50,160),"Completed visit failed.");
        var cash=restored.ReadNpcInventory(id);
        Need(cash.WalletGold==50 && cash.Items.Count==1 && cash.Items[0].Uid==71,"Cash visit replaced belongings or used another wallet.");
        Need(restored.ReadNpcCashVisit(id).Returning,"Completed visit did not return to routine.");
        Need(!restored.TryCompleteNpcCashVisit(restoredEpoch,id,1,20,50,161),"Visit replay created more cash.");
        Console.WriteLine("PASS arrival, time, source, revision, save/load, replay and shared wallet");
    }
    private static void Need(bool condition,string message)
    {if(!condition)throw new InvalidOperationException(message);}

    private static void CounterVisit()
    {
        var route=new[]{new GuardPoint3{X=0,Y=0,Z=0},new GuardPoint3{X=3,Y=0,Z=4}};
        foreach(int dwell in new[]{8,15})
        {
            var journey=NpcCashVisit.CreateCounterJourney("resident",100,20,100,route,2,12,dwell);
            Need(journey.Evaluate(109).Phase==ShopVisitPhase.Walking,"Skipped the walk to the clerk.");
            Need(journey.Evaluate(110).Phase==ShopVisitPhase.Haggling,"Did not stand at the clerk.");
            Need(!NpcCashVisit.CounterServiceFinished(journey,110+dwell*12-0.01),"Service ended early.");
            Need(NpcCashVisit.CounterServiceFinished(journey,110+dwell*12),"Service did not end after its dwell.");
            Need(journey.Evaluate(110+dwell*12).Phase==ShopVisitPhase.Leaving,"Did not walk back to the entrance.");
            Need(journey.Evaluate(120+dwell*12).Completed,"Did not exit after service.");
        }
        Console.WriteLine("PASS clerk route, 8–15 real seconds, service completion and exit");
    }
}
