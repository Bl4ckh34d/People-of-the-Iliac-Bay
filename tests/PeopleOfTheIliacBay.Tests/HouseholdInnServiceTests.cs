using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class HouseholdInnServiceTests
    {
        internal sealed class Fixture
        {
            internal WorldStateStore Store;internal HouseholdDayPlan Plan;internal NativePerson Keeper;internal double Departure;
        }
        internal static Fixture Prepared(int firstGold=20,int secondGold=20)
        {
            var request=HouseholdDayAppointmentTests.SharedRequest();var plan=HouseholdDayPlanTests.Create(request);
            var store=HouseholdDayPlanTests.Store(request);Check.True(store.TryPublishHouseholdDay(plan,request.PlannedAt,out var why),why);
            var motion=HouseholdJointMovement.Create(plan,request.PlannedAt);store.SaveHouseholdJointProgress(motion.Snapshot());
            var keeper=store.RegisterNativePerson(2,10,900,"Inn keeper",ResidentSex.Female,0,0,182,0);
            Check.True(store.InitializeNpcInventory("mother",firstGold,Array.Empty<NpcInventoryItem>()));
            Check.True(store.InitializeNpcInventory("father",secondGold,Array.Empty<NpcInventoryItem>()));
            Check.True(store.InitializeNpcInventory(keeper.Id,40,Array.Empty<NpcInventoryItem>()));
            var catalog=new NpcInnServiceCatalog{SettlementId=2,BuildingKey=10,BlockIndex=1,RecordIndex=2,Climate=224,RegionIndex=0,
                KeeperId=keeper.Id,KeeperHash=900,CapturedAt=plan.PlannedAt,OpenMinute=0,CloseMinute=1440,
                EntryToCounter=new List<GuardPoint3>{P(0),P(2)},Pairs=new List<NpcInnServiceSeatPair>{new NpcInnServiceSeatPair{
                    First=new TableSeatPosition{Id="chair-a",TableId="table",X=3,Z=0,Height=.5,FacingX=1},
                    Second=new TableSeatPosition{Id="chair-b",TableId="table",X=4,Z=0,Height=.5,FacingX=-1},
                    FirstCounterToSeat=new List<GuardPoint3>{P(2),P(3)},SecondCounterToSeat=new List<GuardPoint3>{P(2),P(4)},
                    FirstSeatToEntry=new List<GuardPoint3>{P(3),P(0)},SecondSeatToEntry=new List<GuardPoint3>{P(4),P(0)}}}};
            for(int i=0;i<11;i++)catalog.Menu.Add(new NpcInnMenuItem{MenuIndex=i,Kind=i<4?NpcInnConsumptionKind.Drink:NpcInnConsumptionKind.Food,
                ProductName="native-item-"+i,BasePriceGold=2});
            store.RegisterNpcInnServiceCatalog(NpcInnServiceCatalog.Seal(catalog));
            double start=plan.AbsoluteDay*86400d+plan.JointInn.First.DepartureSeconds;
            store.BeginHouseholdJointSimulation(plan.Id,motion.Snapshot().Sequence,start);
            store.AdvanceHouseholdJointSimulation(plan.Id,start+300);
            Check.Equal(HouseholdJointPhase.AtInn,store.ReadHouseholdJointSimulation(2,7).Phase);
            return new Fixture{Store=store,Plan=plan,Keeper=keeper,Departure=start};
        }
        private static GuardPoint3 P(double x){return new GuardPoint3{X=x};}
        internal static T Field<T>(object record,string name){return (T)record.GetType().GetField(name).GetValue(record);}
        internal static object Advance(Fixture fixture,double at)
        {
            var method=typeof(WorldStateStore).GetMethod("AdvanceHouseholdInnService");
            Check.True(method!=null,"Missing source-bound unloaded household Inn service.");
            Func<int,double,NpcInnServiceTariff> tariff=(region,time)=>new NpcInnServiceTariff{RegionIndex=region,AbsoluteDay=(int)Math.Floor(time/86400d)};
            try{return method.Invoke(fixture.Store,new object[]{fixture.Plan.Id,at,tariff,null});}
            catch(TargetInvocationException error) when(error.InnerException!=null){throw error.InnerException;}
        }
        internal static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Native Inn service needs buyer gold but no keeper inventory",()=>{
                var f=Prepared();var state=f.Store.Snapshot();state.NpcInventories.RemoveAll(i=>i.PersonId==f.Keeper.Id);f.Store.Restore(state);
                var service=(HouseholdInnServiceAdmission)Advance(f,f.Departure+700);
                Check.Equal("purchased",service.Outcome);Check.Equal(18,f.Store.ReadNpcInventory("mother").WalletGold);
                Check.True(f.Store.ReadNpcInventory(f.Keeper.Id)==null);
            }));
            cases.Add(("Native-equivalent reconstructed instant tolerates G15 clock rounding only",()=>{
                var f=Prepared();Advance(f,f.Departure+700);double now=f.Departure+800;
                var state=f.Store.Snapshot();state.HouseholdJointSimulations[0].AdvancedThrough=Math.BitIncrement(now);f.Store.Restore(state);
                var sim=f.Store.ReadHouseholdJointSimulation(2,7);var a=f.Store.ProjectHouseholdInnMember(f.Plan.Id,"mother",now);var b=f.Store.ProjectHouseholdInnMember(f.Plan.Id,"father",now);
                Check.True(a!=null && b!=null);
                f.Store.ReconstructHouseholdInnSimulation(f.Plan.Id,sim.Revision,now,new HouseholdInnPlacement{ResidentId="mother",Position=a.Position},new HouseholdInnPlacement{ResidentId="father",Position=b.Position});
                Check.True(!f.Store.ReadHouseholdJointSimulation(2,7).Active);
            }));
            cases.Add(("Home-room reconstruction samples both parents without treating indoor points as exterior route positions",()=>{
                var f=Prepared();Func<int,double,NpcInnServiceTariff> tariff=(region,time)=>new NpcInnServiceTariff{RegionIndex=region,AbsoluteDay=(int)Math.Floor(time/86400d)};
                var sim=f.Store.AdvanceHouseholdJointLifecycle(f.Plan.Id,f.Departure+6000,tariff);
                var method=typeof(WorldStateStore).GetMethod("ReconstructHouseholdHomeSimulation");Check.True(method!=null,"Missing paired home-room handback.");
                var actual=f.Store.ReadHouseholdJointProgress(2,7);
                var arrival=new HouseholdHomeArrival{SettlementId=2,HomeKey=7,FirstResidentId="mother",SecondResidentId="father",
                    ObservedAt=f.Departure+6000,First=new GuardPoint3{X=100,Y=2},Second=new GuardPoint3{X=102,Y=2}};
                Check.True(f.Store.ReconstructHouseholdHomeSimulation(f.Plan.Id,sim.Revision,arrival));
                var retained=f.Store.ReadHouseholdJointSimulation(2,7);Check.True(!retained.Active && retained.HomeArrival!=null);
                Check.Near(actual.First.Position.X,f.Store.ReadHouseholdJointProgress(2,7).First.Position.X);
                Check.Near(100,retained.HomeArrival.First.X);new WorldStateStore(0).Restore(f.Store.Snapshot());
            }));
            cases.Add(("An ordinary observed visit cannot be preempted by a later shared departure",()=>{
                var request=HouseholdDayAppointmentTests.SharedRequest();var plan=HouseholdDayPlanTests.Create(request);
                var store=HouseholdDayPlanTests.Store(request);Check.True(store.TryPublishJointHouseholdDay(plan,request.PlannedAt,out var reason),reason);
                double departure=plan.AbsoluteDay*86400d+plan.JointInn.First.DepartureSeconds;
                store.StoreObservedVisit(new ObservedVisit{ResidentId="mother",SettlementId=2,BuildingKey=8,ObservedAt=departure-10,
                    PlannedEndAt=departure+300,ResumeUntil=departure+300,SecondsPerMetre=1,Legs=new List<ObservedVisitLeg>{
                        new ObservedVisitLeg{Route=new List<GuardPoint3>{new GuardPoint3()},DwellSeconds=310,AfterPhase=ShopVisitPhase.Browsing},
                        new ObservedVisitLeg{Route=new List<GuardPoint3>{new GuardPoint3()},AfterPhase=ShopVisitPhase.Complete}}});
                var sim=store.AdvanceHouseholdJointLifecycle(plan.Id,departure+20,(region,time)=>new NpcInnServiceTariff{RegionIndex=region,AbsoluteDay=(int)Math.Floor(time/86400d)});
                Check.Equal(HouseholdJointPhase.Waiting,sim.Phase);Check.Equal("partner-unavailable",sim.PauseReason);
            }));
            cases.Add(("Missed joint departure releases optional intent without inventing a home arrival",()=>{
                var request=HouseholdDayAppointmentTests.SharedRequest();var plan=HouseholdDayPlanTests.Create(request);
                var store=HouseholdDayPlanTests.Store(request);Check.True(store.TryPublishJointHouseholdDay(plan,request.PlannedAt,out var reason),reason);
                double late=plan.AbsoluteDay*86400d+23*3600;
                store.AdvanceHouseholdJointSimulation(plan.Id,late,false,false);
                var cancel=typeof(WorldStateStore).GetMethod("TryCancelMissedHouseholdDeparture");Check.True(cancel!=null);
                Check.True((bool)cancel.Invoke(store,new object[]{plan.Id,late}));
                Check.True(!store.HasActiveHouseholdJoint(2,"mother"));Check.True(!store.ReadHouseholdJointSimulation(2,7).Active);
                Check.Equal(HouseholdJointPhase.Waiting,store.ReadHouseholdJointProgress(2,7).Phase);
                Check.Near(request.PlannedAt,store.ReadHouseholdJointProgress(2,7).ObservedAt);
                new WorldStateStore(0).Restore(store.Snapshot());
            }));
            cases.Add(("Unavailable Inn service returns promptly instead of waiting outside for the whole evening",()=>{
                var f=Prepared();var saved=f.Store.Snapshot();saved.NpcInnServiceCatalogs.Clear();f.Store.Restore(saved);
                var result=f.Store.AdvanceHouseholdJointLifecycle(f.Plan.Id,f.Departure+1000,(region,time)=>new NpcInnServiceTariff{RegionIndex=region,AbsoluteDay=(int)Math.Floor(time/86400d)});
                Check.Equal(HouseholdJointPhase.Completed,result.Phase);Check.Equal(20,f.Store.ReadNpcInventory("mother").WalletGold);
            }));
            cases.Add(("Paid simulated Inn handback publishes both sampled bodies and receipts atomically",()=>{
                var f=Prepared();Advance(f,f.Departure+700);
                var project=typeof(WorldStateStore).GetMethod("ProjectHouseholdInnMember");
                Check.True(project!=null,"Missing source-bound Inn member projection.");
                var a=(HouseholdInnProjection)project.Invoke(f.Store,new object[]{f.Plan.Id,"mother",f.Departure+800});
                var b=(HouseholdInnProjection)project.Invoke(f.Store,new object[]{f.Plan.Id,"father",f.Departure+800});
                var method=typeof(WorldStateStore).GetMethod("ReconstructHouseholdInnSimulation");
                Check.True(method!=null,"Missing paired native Inn handback.");
                var sim=f.Store.ReadHouseholdJointSimulation(2,7);
                method.Invoke(f.Store,new object[]{f.Plan.Id,sim.Revision,f.Departure+800,
                    new HouseholdInnPlacement{ResidentId="mother",Position=a.Position},new HouseholdInnPlacement{ResidentId="father",Position=b.Position}});
                Check.True(!f.Store.ReadHouseholdJointSimulation(2,7).Active);
                Check.Equal(HouseholdJointPhase.Visiting,f.Store.ReadHouseholdJointProgress(2,7).Phase);
                Check.Equal(18,f.Store.ReadNpcInventory("mother").WalletGold);
                Check.Equal(f.Store.ReadHouseholdInnServiceAdmission(f.Plan.Id).FirstReceipt.VisitId,f.Store.ReadNpcInnConsumption("mother").VisitId);
                new WorldStateStore(0).Restore(f.Store.Snapshot());
                var actual=f.Store.ReadHouseholdJointProgress(2,7);
                f.Store.BeginHouseholdJointSimulation(f.Plan.Id,actual.Sequence,f.Departure+801);
                var oldA=f.Store.GetObservedVisit("mother");var oldB=f.Store.GetObservedVisit("father");
                var staleA=f.Store.GetObservedVisit("mother");var staleB=f.Store.GetObservedVisit("father");
                staleA.ObservedAt=staleB.ObservedAt=f.Departure+810;
                Check.Throws<ArgumentException>(()=>f.Store.StoreHouseholdInnVisits(staleA,staleB));
                Check.Near(oldA.ObservedAt,f.Store.GetObservedVisit("mother").ObservedAt);
                Check.Near(oldB.ObservedAt,f.Store.GetObservedVisit("father").ObservedAt);
                Check.True(f.Store.ProjectHouseholdInnMember(f.Plan.Id,"mother",f.Departure+820)!=null,
                    "A second Inn scene entry must reconstruct the captured paid visit.");
                sim=f.Store.ReadHouseholdJointSimulation(2,7);a=f.Store.ProjectHouseholdInnMember(f.Plan.Id,"mother",f.Departure+820);
                b=f.Store.ProjectHouseholdInnMember(f.Plan.Id,"father",f.Departure+820);
                f.Store.ReconstructHouseholdInnSimulation(f.Plan.Id,sim.Revision,f.Departure+820,
                    new HouseholdInnPlacement{ResidentId="mother",Position=a.Position},new HouseholdInnPlacement{ResidentId="father",Position=b.Position});
                Check.Equal(18,f.Store.ReadNpcInventory("mother").WalletGold);new WorldStateStore(0).Restore(f.Store.Snapshot());
            }));
            cases.Add(("Joint household publication installs paired waiting ownership with the day",()=>{
                var request=HouseholdDayAppointmentTests.SharedRequest();var plan=HouseholdDayPlanTests.Create(request);
                var store=HouseholdDayPlanTests.Store(request);var publish=typeof(WorldStateStore).GetMethod("TryPublishJointHouseholdDay");
                Check.True(publish!=null,"Missing atomic joint day producer.");
                var args=new object[]{plan,request.PlannedAt,null};Check.True((bool)publish.Invoke(store,args),(string)args[2]);
                Check.Equal(HouseholdJointPhase.Waiting,store.ReadHouseholdJointProgress(2,7).Phase);
                Check.True(store.ReadHouseholdJointSimulation(2,7).Active);new WorldStateStore(0).Restore(store.Snapshot());
            }));
            cases.Add(("Unloaded family completes native service dwell and return without moving actual anchor",()=>{
                var f=Prepared();var at=f.Departure+6000;var lifecycle=typeof(WorldStateStore).GetMethod("AdvanceHouseholdJointLifecycle");
                Check.True(lifecycle!=null,"Missing complete offscreen family lifecycle.");
                Func<int,double,NpcInnServiceTariff> tariff=(region,time)=>new NpcInnServiceTariff{RegionIndex=region,AbsoluteDay=(int)Math.Floor(time/86400d)};
                var sim=(HouseholdJointSimulation)lifecycle.Invoke(f.Store,new object[]{f.Plan.Id,at,tariff,true,true,false,null});
                Check.Equal(HouseholdJointPhase.Completed,sim.Phase);Check.Equal("returned-home",sim.Outcome);
                Check.Equal(HouseholdJointPhase.Waiting,f.Store.ReadHouseholdJointProgress(2,7).Phase);
                Check.Equal(18,f.Store.ReadNpcInventory("mother").WalletGold);
                var restored=new WorldStateStore(0);restored.Restore(f.Store.Snapshot());
                Check.Equal(HouseholdJointPhase.Completed,restored.ReadHouseholdJointSimulation(2,7).Phase);
            }));
            cases.Add(("Large and chunked full family lifecycles preserve identical payments and home arrival",()=>{
                var a=Prepared();var b=Prepared();
                Func<int,double,NpcInnServiceTariff> tariff=(region,time)=>new NpcInnServiceTariff{RegionIndex=region,AbsoluteDay=(int)Math.Floor(time/86400d)};
                var large=a.Store.AdvanceHouseholdJointLifecycle(a.Plan.Id,a.Departure+6000,tariff);
                HouseholdJointSimulation small=null;
                for(double at=b.Departure+301;at<=b.Departure+6000;at+=17)small=b.Store.AdvanceHouseholdJointLifecycle(b.Plan.Id,at,tariff);
                small=b.Store.AdvanceHouseholdJointLifecycle(b.Plan.Id,b.Departure+6000,tariff);
                Check.Equal(large.Phase,small.Phase);Check.Near(large.PhaseSince,small.PhaseSince);
                Check.Near(a.Store.ReadHouseholdInnServiceAdmission(a.Plan.Id).CounterAt,b.Store.ReadHouseholdInnServiceAdmission(b.Plan.Id).CounterAt);
                Check.Equal(18,b.Store.ReadNpcInventory("mother").WalletGold);
            }));
            cases.Add(("Completed offscreen return releases daily ownership without fabricating physical home observation",()=>{
                var f=Prepared();var release=typeof(WorldStateStore).GetMethod("ReleaseCompletedHouseholdSimulation");
                Check.True(release!=null,"Missing virtual home completion release.");
                Func<int,double,NpcInnServiceTariff> tariff=(region,time)=>new NpcInnServiceTariff{RegionIndex=region,AbsoluteDay=(int)Math.Floor(time/86400d)};
                f.Store.AdvanceHouseholdJointLifecycle(f.Plan.Id,f.Departure+6000,tariff);
                var before=f.Store.ReadHouseholdJointProgress(2,7);
                Check.True((bool)release.Invoke(f.Store,new object[]{f.Plan.Id,f.Departure+6000}));
                var actual=f.Store.ReadHouseholdJointProgress(2,7);
                Check.True(!actual.Active);Check.Near(before.ObservedAt,actual.ObservedAt);Check.Near(before.First.Distance,actual.First.Distance);
                Check.True(!f.Store.ReadHouseholdJointSimulation(2,7).Active);
                new WorldStateStore(0).Restore(f.Store.Snapshot());
            }));
            cases.Add(("Shared service restore rejects early capacity release",()=>{
                var f=Prepared();Advance(f,f.Departure+700);var saved=f.Store.Snapshot();
                saved.HouseholdInnServices[0].ReleasedAt=saved.HouseholdInnServices[0].ArrivalAt;
                Check.Throws<ArgumentException>(()=>new WorldStateStore(0).Restore(saved));
            }));
            cases.Add(("Shared service restore rejects a forged native quote even when cursor agrees",()=>{
                var f=Prepared();Advance(f,f.Departure+700);var saved=f.Store.Snapshot();
                saved.HouseholdInnServices[0].FirstReceipt.ProductName="invented product";
                saved.NpcInnConsumptionCursors.Find(c=>c.BuyerId=="mother").Receipt.ProductName="invented product";
                Check.Throws<ArgumentException>(()=>new WorldStateStore(0).Restore(saved));
            }));
            cases.Add(("Shared service restore rejects an orphan projected refusal",()=>{
                var f=Prepared(secondGold:0);Advance(f,f.Departure+700);var saved=f.Store.Snapshot();saved.HouseholdInnServices.Clear();
                Check.Throws<ArgumentException>(()=>new WorldStateStore(0).Restore(saved));
            }));
            cases.Add(("Shared service restore derives counter chronology from retained native routes",()=>{
                var f=Prepared();Advance(f,f.Departure+700);var saved=f.Store.Snapshot();saved.HouseholdInnServices[0].CounterAt++;
                Check.Throws<ArgumentException>(()=>new WorldStateStore(0).Restore(saved));
            }));
            cases.Add(("Unloaded paired Inn service charges both actual wallets without physical observations",()=>{
                var f=Prepared();var anchor=f.Store.ReadHouseholdJointProgress(2,7);
                var service=Advance(f,f.Departure+700);
                Check.Equal("purchased",Field<string>(service,"Outcome"));
                Check.Equal(18,f.Store.ReadNpcInventory("mother").WalletGold);Check.Equal(18,f.Store.ReadNpcInventory("father").WalletGold);
                Check.Equal(40,f.Store.ReadNpcInventory(f.Keeper.Id).WalletGold);
                var saved=f.Store.Snapshot();Check.Equal(0,saved.ObservedVisits.Count);
                Check.Equal(anchor.Sequence,f.Store.ReadHouseholdJointProgress(2,7).Sequence);
                Check.Near(anchor.First.Distance,f.Store.ReadHouseholdJointProgress(2,7).First.Distance);
                Check.True(Field<NpcInnConsumptionReceipt>(service,"FirstReceipt").VisitId!=Field<NpcInnConsumptionReceipt>(service,"SecondReceipt").VisitId);
            }));
            cases.Add(("Unloaded Inn purchase is idempotent through serializer restore and retains explicit cursor origin",()=>{
                var f=Prepared();var first=Advance(f,f.Departure+700);
                var json=JsonSerializer.Serialize(f.Store.Snapshot(),new JsonSerializerOptions{IncludeFields=true});
                var restored=new WorldStateStore(0);restored.Restore(JsonSerializer.Deserialize<WorldState>(json,new JsonSerializerOptions{IncludeFields=true}));
                f.Store=restored;var repeated=Advance(f,f.Departure+701);
                Check.Equal(Field<NpcInnConsumptionReceipt>(first,"FirstReceipt").VisitId,Field<NpcInnConsumptionReceipt>(repeated,"FirstReceipt").VisitId);
                Check.Equal(18,f.Store.ReadNpcInventory("mother").WalletGold);Check.Equal(40,f.Store.ReadNpcInventory(f.Keeper.Id).WalletGold);
                foreach(var cursor in f.Store.Snapshot().NpcInnConsumptionCursors)
                {Check.Near(0,cursor.ObservedAt);Check.Equal("world-time-projection",Field<string>(cursor,"SourceKind"));Check.True(Field<double?>(cursor,"ServiceStartsAt").HasValue);}
            }));
            cases.Add(("Shared service refuses unaffordable pair before any new debit or shared seating",()=>{
                var f=Prepared(secondGold:0);var service=Advance(f,f.Departure+700);
                Check.Equal("refused",Field<string>(service,"Outcome"));Check.Equal(20,f.Store.ReadNpcInventory("mother").WalletGold);
                Check.Equal(40,f.Store.ReadNpcInventory(f.Keeper.Id).WalletGold);Check.True(Field<string>(service,"FirstSeatId")==null);
                Check.True(Field<NpcInnConsumptionReceipt>(service,"SecondReceipt").Outcome==NpcInnConsumptionOutcome.InsufficientFunds);
            }));
            cases.Add(("Unloaded service preserves the exact counter time before a large jump",()=>{
                var f=Prepared();var pending=Advance(f,f.Departure+301);
                Check.Equal("pending",Field<string>(pending,"Outcome"));Check.Equal(20,f.Store.ReadNpcInventory("mother").WalletGold);
                var paid=Advance(f,f.Departure+700);double counter=Field<double>(paid,"CounterAt");
                Check.True(counter>f.Departure+300 && counter<f.Departure+700);
                Check.Near(counter,Field<NpcInnConsumptionReceipt>(paid,"FirstReceipt").CompletedAt);
            }));
            cases.Add(("Missing native service source cannot debit wallets",()=>{
                var f=Prepared();var saved=f.Store.Snapshot();saved.NpcInnServiceCatalogs.Clear();f.Store.Restore(saved);
                Check.True(Advance(f,f.Departure+700)==null);Check.Equal(20,f.Store.ReadNpcInventory("mother").WalletGold);
                Check.Equal(40,f.Store.ReadNpcInventory(f.Keeper.Id).WalletGold);
            }));
            cases.Add(("Schema73 rejects future simulated service records instead of inventing history",()=>{
                var f=Prepared();Advance(f,f.Departure+700);var saved=f.Store.Snapshot();
                saved.Version=73;Check.Throws<ArgumentException>(()=>new WorldStateStore(0).Restore(saved));
            }));
        }
    }
}
