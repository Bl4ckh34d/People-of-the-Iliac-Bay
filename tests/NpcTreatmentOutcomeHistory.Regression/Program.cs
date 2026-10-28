using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.Tests;

internal static class Program
{
    private static int Main()
    {
        var cases=new List<(string,Action)> {
            ("verified rest projects once and survives detail prune",VerifiedRestProjectsOnce),
            ("summary overflow keeps bounded recent facts and unknown older detail",OverflowKeepsCoverage),
            ("future outcome stays hidden and stales read at native time",FutureOutcomeVisibility),
            ("out-of-order overflow exposes unknown past without future facts",OutOfOrderOverflow),
            ("linked purchase provenance survives detail prune",LinkedPurchaseSurvivesPrune),
            ("bounded runtime batch counts examined and future rows",BoundedBatchProjection),
            ("empty runtime batch performs no work",EmptyBatch)
        };
        int failed=0;
        foreach(var test in cases)
            try {test.Item2();Console.WriteLine("PASS "+test.Item1);}
            catch(Exception error) {failed++;Console.WriteLine("FAIL "+test.Item1+": "+error);}
        Console.WriteLine((cases.Count-failed)+"/"+cases.Count+" passed");
        return failed==0?0:1;
    }

    private static void VerifiedRestProjectsOnce()
    {
        Fixture f=Create();CompleteRest(f,"rest:first",10,5);
        NpcMemoryReadSnapshot before=Read(f,11);
        NpcActionJob action=f.Store.ReadNpcAction(ActionId(f,"rest:first"));
        Check(f.Store.TryProjectNpcTreatmentOutcome(action.Id,action.Revision),"project native receipt");
        Equal(action.Revision,f.Store.ReadNpcAction(action.Id).Revision);
        Check(!f.Store.IsNpcMemoryReadCurrent(before,f.ResidentId,11),"projection stales read");
        NpcTreatmentOutcomeHistory history=History(f);
        Equal(1L,history.Revision);Equal(0L,history.OutcomeBaseRevision);
        Equal(1,history.Outcomes.Count);Equal(action.Id,history.Outcomes[0].ActionId);
        Check(f.Store.TryProjectNpcTreatmentOutcome(action.Id,f.Store.ReadNpcAction(action.Id).Revision),"idempotent replay");
        Equal(1L,History(f).Revision);
        WorldState lostMarker=f.Store.Snapshot();
        lostMarker.NpcActions.Find(value=>value.Id==action.Id).TreatmentOutcomeRevision=0;
        bool rejected=false;
        try {new WorldStateStore(0).Restore(lostMarker);} catch(ArgumentException) {rejected=true;}
        Check(rejected,"retained outcome cannot load beside an unmarked duplicate source action");
        WorldState pruned=f.Store.Snapshot();
        pruned.NpcActions.RemoveAll(value=>value.Id==action.Id);
        var restored=new WorldStateStore(0);restored.Restore(pruned);
        NpcMemoryReadSnapshot read=Read(restored,f.ResidentId,11);
        Check(read.Text.Contains("rested") && read.Text.Contains("health improved"),"summary backs recall after detail prune");
        Check(!read.Text.Contains("bought") && !read.Text.Contains("gold"),"rest cannot fabricate cost");
    }

    private static void OverflowKeepsCoverage()
    {
        Fixture f=Create();
        for(int i=0;i<33;i++)
        {
            string id="rest:overflow:"+i;
            CompleteRest(f,id,10+i*100,1);
            NpcActionJob action=f.Store.ReadNpcAction(ActionId(f,id));
            Check(f.Store.TryProjectNpcTreatmentOutcome(action.Id,action.Revision),"project "+id);
        }
        NpcTreatmentOutcomeHistory history=History(f);
        Equal(33L,history.Revision);Equal(1L,history.OutcomeBaseRevision);
        Equal(32,history.Outcomes.Count);
        WorldState pruned=f.Store.Snapshot();pruned.NpcActions.Clear();
        var restored=new WorldStateStore(0);restored.Restore(pruned);
        NpcMemoryReadSnapshot read=Read(restored,f.ResidentId,10+32*100+2);
        Check(read.Text.Contains("game time "+(10+32*100-2)),"latest summary retains exact observed rest start");
        NpcMemoryReadSnapshot old=Read(restored,f.ResidentId,11);
        Check(old.Text.Contains("unavailable"),"older evicted detail is explicitly unavailable");
        Check(!old.Text.Contains("gold") && !old.Text.Contains("bought"),"old detail cannot invent cost");
    }

    private static void FutureOutcomeVisibility()
    {
        Fixture f=Create();CompleteRest(f,"rest:future",104,3);
        NpcActionJob action=f.Store.ReadNpcAction(ActionId(f,"rest:future"));
        Check(f.Store.TryProjectNpcTreatmentOutcome(action.Id,action.Revision),"project future-dated receipt");
        WorldState pruned=f.Store.Snapshot();pruned.NpcActions.Clear();
        var restored=new WorldStateStore(0);restored.Restore(pruned);
        NpcMemoryReadSnapshot early=Read(restored,f.ResidentId,103);
        Check(!early.Text.Contains("rested"),"future treatment hidden");
        Check(restored.IsNpcMemoryReadCurrent(early,f.ResidentId,103),"early read current");
        Check(!restored.IsNpcMemoryReadCurrent(early,f.ResidentId,104),"time threshold stales read");
        Check(Read(restored,f.ResidentId,104).Text.Contains("rested"),"receipt visible at observed time");
    }

    private static void BoundedBatchProjection()
    {
        Fixture f=Create();CompleteRest(f,"rest:batch:first",10,2);
        CompleteRest(f,"rest:batch:future",110,2);
        var first=f.Store.ProjectPendingNpcTreatmentOutcomes(11,1);
        Equal(1,first.Examined);Equal(1,first.Projected);
        var future=f.Store.ProjectPendingNpcTreatmentOutcomes(11,1);
        Equal(1,future.Examined);Equal(0,future.Projected);Equal(1,future.Future);
        var prior=f.Store.ProjectPendingNpcTreatmentOutcomes(111,1);
        Equal(1,prior.Examined);Equal(1,prior.AlreadyProjected);
        var second=f.Store.ProjectPendingNpcTreatmentOutcomes(111,1);
        Equal(1,second.Examined);Equal(1,second.Projected);
        Equal(2L,History(f).Revision);
    }

    private static void OutOfOrderOverflow()
    {
        Fixture f=Create();
        for(int i=0;i<34;i++)CompleteRest(f,"rest:order:"+i,10+i*100,1);
        Project(f,"rest:order:33");
        for(int i=0;i<33;i++)Project(f,"rest:order:"+i);
        WorldState pruned=f.Store.Snapshot();pruned.NpcActions.Clear();
        var restored=new WorldStateStore(0);restored.Restore(pruned);
        NpcMemoryReadSnapshot early=Read(restored,f.ResidentId,9);
        Check(!early.Text.Contains("unavailable") && !early.Text.Contains("rested"),"future and past facts hidden early");
        Check(!restored.IsNpcMemoryReadCurrent(early,f.ResidentId,10),"first missing past event stales read");
        NpcMemoryReadSnapshot past=Read(restored,f.ResidentId,11);
        Check(past.Text.Contains("unavailable"),"past detail explicitly unavailable despite future dropped event");
        Check(!past.Text.Contains("game time 3308"),"future rest detail stays hidden");
    }

    private static void Project(Fixture f,string id)
    {
        NpcActionJob action=f.Store.ReadNpcAction(ActionId(f,id));
        Check(f.Store.TryProjectNpcTreatmentOutcome(action.Id,action.Revision),"project "+id);
    }

    private static void EmptyBatch()
    {
        Fixture f=Create();
        var result=f.Store.ProjectPendingNpcTreatmentOutcomes(10,4);
        Equal(0,result.Examined);Equal(0,result.Projected);
    }

    private static void LinkedPurchaseSurvivesPrune()
    {
        var store=new WorldStateStore(42);
        var town=store.GetOrCreate(1,new[]{10,20},1);
        string patient=town.Households[0].Residents[0].Id;
        string seller=town.Households[1].Residents[0].Id;
        Check(store.InitializeNpcInventory(patient,30,new List<NpcInventoryItem>()),"patient wallet");
        Check(store.InitializeNpcInventory(seller,0,new[]{new NpcInventoryItem {Uid=44,NativePayload="shop potion stock"}}),"seller stock");
        NpcVitalsView vitals=store.ReadNpcVitals(patient,NpcArchetype.Citizen);
        int before=Math.Max(1,vitals.MaximumHealth-8);
        Check(store.TryCaptureNpcVitals(patient,NpcArchetype.Citizen,before,vitals.CurrentMagicka,99,vitals.Revision),"injury");
        var purchaseTarget=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=1,BuildingKey=10,
            PersonId=seller,ContainerId="700",ItemId="44",Quantity=1,QuotedGold=9};
        var healTarget=new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,WorldSpaceId="settlement:1",
            X=1,Y=0,Z=2,ItemId="44",Quantity=1};
        var task=new NpcTaskPlan {Id="purchased:history",ProposalId="purchased:history:proposal",ResidentId=patient,
            Phase=NpcTaskPhase.Ready,Revision=1,AcceptedAt=100,UpdatedAt=100,DeadlineAt=500,
            Steps=new List<NpcTaskStep>{
                new NpcTaskStep {Kind=NpcActionKind.Purchase,Target=purchaseTarget},
                new NpcTaskStep {Kind=NpcActionKind.UseHealingItem,ItemSourceStep=1,Target=healTarget}}};
        string reason;
        Check(ClassifiedAdmissionFixture.TryAcceptTask(store,task,true,out reason),"accept purchase task: "+reason);
        var purchase=new NpcActionJob {Id=task.Id+":step:0",ProposalId=task.Id+":step:0",ResidentId=patient,
            Kind=NpcActionKind.Purchase,Target=NpcActionExecutor.CopyTarget(purchaseTarget),
            Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=100,UpdatedAt=100,DeadlineAt=500,
            ServiceCode=NpcShopCommerce.ServiceCode,ServicePrice=9};
        Check(store.TryDispatchNpcTaskStep(task.Id,1,100,purchase,true,true,true,out reason),"dispatch purchase: "+reason);
        Check(store.ObserveNpcAction(purchase.Id,1,101,NpcActionObservation.Arrival()),"purchase arrival");
        purchase=store.ReadNpcAction(purchase.Id);
        Check(store.TryCompleteNpcActionEffect(purchase.Id,purchase.Revision,102,()=>
            store.CommitNpcInventoryTransaction(purchase.Id+":purchase",new[]{
                new NpcInventoryUpdate {PersonId=patient,ExpectedRevision=0,WalletGold=21,
                    Items=new List<NpcInventoryItem>{new NpcInventoryItem {Uid=501,NativePayload="potion-stack:2"}}},
                new NpcInventoryUpdate {PersonId=seller,ExpectedRevision=0,WalletGold=9,Items=new List<NpcInventoryItem>()}
            })),"native purchase receipt");
        Check(store.ReconcileNpcTask(task.Id,2,103),"purchase step reconciles");
        task=store.ReadNpcTask(task.Id);
        NpcActionTarget resolved=store.ResolveNpcTaskTarget(task,1);
        Equal("501",resolved.ItemId);
        var treatment=new NpcActionJob {Id=task.Id+":step:1",ProposalId=task.Id+":step:1",ResidentId=patient,
            Kind=NpcActionKind.UseHealingItem,Target=resolved,Phase=NpcActionPhase.Accepted,Revision=1,
            AcceptedAt=104,UpdatedAt=104,DeadlineAt=500,
            ExpectedInventoryRevision=store.ReadNpcInventory(patient).Revision};
        Check(store.TryDispatchNpcTaskStep(task.Id,task.Revision,104,treatment,true,true,true,out reason),
            "dispatch treatment: "+reason);
        Check(store.ObserveNpcAction(treatment.Id,1,105,NpcActionObservation.Arrival()),"treatment arrival");
        treatment=store.ReadNpcAction(treatment.Id);
        vitals=store.ReadNpcVitals(patient,NpcArchetype.Citizen);
        Type type=typeof(WorldStateStore).Assembly.GetType("PeopleOfTheIliacBay.Core.NpcTreatmentNativeOutcome");
        object native=type.GetMethod("Verified",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,
            new object[]{501UL,before,before+5,vitals.CurrentMagicka,
                new NpcInventoryItem[]{new NpcInventoryItem {Uid=501,NativePayload="potion-stack:1"}}});
        Delegate callback=Expression.Lambda(typeof(Func<>).MakeGenericType(type),Expression.Constant(native,type)).Compile();
        MethodInfo complete=typeof(WorldStateStore).GetMethod("TryCompleteNpcHealingItemAction",
            BindingFlags.Instance|BindingFlags.NonPublic);
        Check((bool)complete.Invoke(store,new object[]{treatment.Id,treatment.Revision,106d,NpcArchetype.Citizen,
            vitals.Revision,store.ReadNpcInventory(patient).Revision,callback}),"native healing receipt");
        treatment=store.ReadNpcAction(treatment.Id);
        Check(store.TryProjectNpcTreatmentOutcome(treatment.Id,treatment.Revision),"project purchased treatment");
        NpcTreatmentOutcomeHistory history=store.Snapshot().NpcTreatmentOutcomeHistories.Find(value=>value.PersonId==patient);
        Equal(seller,history.Outcomes[0].SellerPersonId);Equal(9,history.Outcomes[0].PaidGold);
        task=store.ReadNpcTask(task.Id);
        Check(store.ReconcileNpcTask(task.Id,task.Revision,107),"treatment task completes before prune");
        WorldState pruned=store.Snapshot();pruned.NpcTasks.Clear();pruned.NpcActions.Clear();
        var restored=new WorldStateStore(0);restored.Restore(pruned);
        NpcMemoryReadSnapshot recall;
        Check(restored.TryCaptureNpcMemoryRead(patient,"How much did you pay for the healing potion?",108,1000,out recall),
            "purchased care recall");
        Check(recall.Text.Contains("9 gold") && recall.Text.Contains("bought"),"exact paid price survives detail prune");
        string provider=store.ResolveResident(seller).FullName;
        Check(recall.Text.Contains(string.IsNullOrWhiteSpace(provider)?"the shop":provider),
            "verified seller survives detail prune");
    }

    private static Fixture Create()
    {
        var store=new WorldStateStore(42);
        var town=store.GetOrCreate(1,new[]{10},1);
        string person=town.Households[0].Residents[0].Id;
        return new Fixture {Store=store,ResidentId=person};
    }

    private static void CompleteRest(Fixture f,string id,double completedAt,int gain)
    {
        NpcVitalsView vitals=f.Store.ReadNpcVitals(f.ResidentId,NpcArchetype.Citizen);
        int before=Math.Max(1,vitals.MaximumHealth-gain-1);
        Check(f.Store.TryCaptureNpcVitals(f.ResidentId,NpcArchetype.Citizen,before,vitals.CurrentMagicka,
            completedAt-4,vitals.Revision),"fixture injury");
        vitals=f.Store.ReadNpcVitals(f.ResidentId,NpcArchetype.Citizen);
        var action=new NpcActionJob {Id=id,ProposalId=id,ResidentId=f.ResidentId,Kind=NpcActionKind.RestToHeal,
            Target=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=1,BuildingKey=10,
                ContainerId="bed:resident-stay:1",X=2,Y=0,Z=4},
            Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=completedAt-3,UpdatedAt=completedAt-3,
            DeadlineAt=completedAt+100,RestDurationSeconds=1,ExpectedVitalsRevision=vitals.Revision};
        string reason;
        Check(ClassifiedAdmissionFixture.TryAcceptAction(f.Store,action,true,true,true,out reason),"accept rest: "+reason);
        f.ActionIds[id]=action.Id;
        Check(f.Store.ObserveNpcAction(action.Id,1,completedAt-2,NpcActionObservation.Arrival()),"rest arrival");
        action=f.Store.ReadNpcAction(action.Id);
        Type type=typeof(WorldStateStore).Assembly.GetType("PeopleOfTheIliacBay.Core.NpcTreatmentRestNativeOutcome");
        MethodInfo factory=type.GetMethod("Verified",BindingFlags.Static|BindingFlags.NonPublic);
        object outcome=factory.Invoke(null,new object[]{before,before+gain,vitals.CurrentMagicka});
        var method=typeof(WorldStateStore).GetMethod("TryCompleteNpcRestAction",BindingFlags.Instance|BindingFlags.NonPublic);
        Type callbackType=typeof(Func<>).MakeGenericType(type);
        Delegate callback=Expression.Lambda(callbackType,Expression.Constant(outcome,type)).Compile();
        Check((bool)method.Invoke(f.Store,new object[]{action.Id,action.Revision,completedAt,NpcArchetype.Citizen,
            vitals.Revision,callback}),"native rest completion");
    }

    private static NpcTreatmentOutcomeHistory History(Fixture f)
    {return f.Store.Snapshot().NpcTreatmentOutcomeHistories.Find(value=>value.PersonId==f.ResidentId);}
    private static NpcMemoryReadSnapshot Read(Fixture f,double now) {return Read(f.Store,f.ResidentId,now);}
    private static NpcMemoryReadSnapshot Read(WorldStateStore store,string person,double now)
    {
        NpcMemoryReadSnapshot read;
        Check(store.TryCaptureNpcMemoryRead(person,"Did you rest and how was your health?",now,1000,out read),"memory read");
        return read;
    }
    private static void Check(bool value,string message) {if(!value)throw new InvalidOperationException(message);}
    private static void Equal<T>(T expected,T actual)
    {if(!Equals(expected,actual))throw new InvalidOperationException("Expected "+expected+", got "+actual+".");}
    private static string ActionId(Fixture fixture,string alias) {return fixture.ActionIds[alias];}
    private sealed class Fixture
    {
        public WorldStateStore Store;
        public string ResidentId;
        public Dictionary<string,string> ActionIds=new Dictionary<string,string>(StringComparer.Ordinal);
    }
}
