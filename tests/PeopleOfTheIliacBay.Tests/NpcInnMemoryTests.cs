using System;
using System.Collections.Generic;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcInnMemoryTests
    {
        private sealed class Fixture
        {
            internal WorldStateStore Store;
            internal Resident Buyer, Other;
            internal NativePerson Keeper;
            internal ObservedVisit Visit;
        }

        private static Fixture Create(int gold=20)
        {
            var store=new WorldStateStore(17);
            var town=store.GetOrCreate(1,new[]{10,20},1);
            var buyer=town.Households[0].Residents[0];
            buyer.FullName="Inn Buyer";
            var other=town.Households[1].Residents[0];
            int destination=town.Households[1].BuildingKey;
            store.SetRoutines(new TownRoutineState {SettlementId=1,Residents=new List<ResidentRoutine>{
                new ResidentRoutine {ResidentId=buyer.Id,DestinationKey=destination,OpenMinute=500,CloseMinute=600,
                    Route=new List<SavedRoutePoint>{new SavedRoutePoint(),new SavedRoutePoint{X=1}}}}});
            var keeper=store.RegisterNativePerson(1,destination,900,"Inn keeper",ResidentSex.Female,0,0,182,0);
            Check.True(store.InitializeNpcInventory(buyer.Id,gold,Array.Empty<NpcInventoryItem>()));
            var visit=new ObservedVisit {ResidentId=buyer.Id,SettlementId=1,BuildingKey=destination,
                ObservedAt=100,PlannedEndAt=200,ResumeUntil=200,SecondsPerMetre=1,
                Legs=new List<ObservedVisitLeg>{
                    new ObservedVisitLeg {Route=new List<GuardPoint3>{new GuardPoint3(),new GuardPoint3{X=1}},
                        DwellSeconds=60,AfterPhase=ShopVisitPhase.Haggling},
                    new ObservedVisitLeg {Route=new List<GuardPoint3>{new GuardPoint3{X=1}},AfterPhase=ShopVisitPhase.Complete}}};
            store.StoreObservedVisit(visit);
            return new Fixture {Store=store,Buyer=buyer,Other=other,Keeper=keeper,Visit=visit};
        }

        private static bool Buy(Fixture f,double at=102,string product="Beer")
        {return f.Store.TryBuyNpcInnConsumption(f.Buyer.Id,f.Keeper.Id,at,NpcInnConsumptionKind.Drink,1,product,5,out _);}

        private static List<NpcSceneMemory> Purchases(WorldStateStore store)
        {return store.Snapshot().SceneMemories.FindAll(m=>m.Kind==NpcSceneEventKind.Shopping);}

        private static WorldStateStore Reload(WorldStateStore store)
        {
            var json=new JsonSerializerOptions {IncludeFields=true};
            var restored=new WorldStateStore(0);
            restored.Restore(JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(store.Snapshot(),json),json));
            return restored;
        }

        internal static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Paid Inn purchase supplies truthful buyer and stationary keeper recall only",()=>{
                var f=Create();Check.True(Buy(f));var memories=Purchases(f.Store);
                Check.Equal(2,memories.Count);
                var buyer=memories.Find(m=>m.ObserverId==f.Buyer.Id);var keeper=memories.Find(m=>m.ObserverId==f.Keeper.Id);
                Check.True(buyer!=null && keeper!=null,"Both actual service participants need a memory.");
                Check.Equal(buyer.EventId,keeper.EventId);Check.Equal(f.Buyer.Id,buyer.SubjectId);
                Check.Equal(f.Keeper.Id,buyer.ResponsibleId);Check.Equal(f.Buyer.Id,keeper.ResponsibleId);
                Check.Equal(0,buyer.Valence);Check.Equal(0,keeper.Valence);
                Check.True(buyer.Observation.Contains("Beer") && buyer.Observation.Contains("5 gold") && buyer.Observation.Contains("Inn keeper"));
                Check.True(keeper.Observation.Contains(f.Buyer.FullName) && keeper.Observation.Contains("served"));
                Check.True(!buyer.Observation.Contains("drank") && !buyer.Observation.Contains("ate"));
                Check.Equal(1,buyer.SettlementId);Check.Equal(f.Visit.BuildingKey,buyer.BuildingKey);Check.Near(102,buyer.At);
                Check.Equal("",f.Store.RecallSceneEvents(f.Other.Id,103));
                Check.True(f.Store.TryCaptureNpcMemoryRead(f.Keeper.Id,"",103,2000,out var read));
                Check.True(read.SceneRecall.Contains("Beer"),"Stationary dialogue must receive the service recollection.");
                Check.Equal(15,f.Store.ReadNpcInventory(f.Buyer.Id).WalletGold);
                Check.True(f.Store.ReadNpcInventory(f.Keeper.Id)==null,"Memory must not require or create keeper stock.");
            }));
            cases.Add(("Inn receipt replay and save restore cannot duplicate or refresh recollections",()=>{
                var f=Create();Check.True(Buy(f));var before=Purchases(f.Store);
                f.Store=Reload(f.Store);Check.True(!Buy(f,103));
                f.Store.RemoveObservedVisit(f.Buyer.Id);f.Store.StoreObservedVisit(f.Visit);
                Check.True(Buy(f,104),"Cursor replay returns the original receipt.");
                var after=Purchases(f.Store);Check.Equal(2,after.Count);
                for(int i=0;i<before.Count;i++){Check.Equal(before[i].EventId,after[i].EventId);Check.Near(before[i].At,after[i].At);Check.Near(before[i].ExpiresAt,after[i].ExpiresAt);}
                var expired=f.Store.Snapshot();expired.SceneMemories.Clear();f.Store.Restore(expired);
                Check.True(Buy(f,105));Check.Equal(0,Purchases(f.Store).Count);
            }));
            cases.Add(("Failed or rejected Inn service creates no successful purchase recollection",()=>{
                var f=Create();Check.True(!Buy(f,100));Check.Equal(0,Purchases(f.Store).Count);
                Check.True(!f.Store.TryBuyNpcInnConsumption(f.Buyer.Id,f.Keeper.Id,102,NpcInnConsumptionKind.Food,1,"Beer",5,out _));
                Check.Equal(0,Purchases(f.Store).Count);
                var poor=Create(0);Check.True(Buy(poor));Check.Equal(0,Purchases(poor.Store).Count);
                Check.True(f.Store.TryRecordNpcInnNoPurchase(f.Buyer.Id,f.Keeper.Id,102,NpcInnConsumptionOutcome.Refused,"keeper refused",out _));
                Check.Equal(0,Purchases(f.Store).Count);Check.Equal(20,f.Store.ReadNpcInventory(f.Buyer.Id).WalletGold);
            }));
            cases.Add(("Inn memory uses existing variable minor-event retention and cannot displace trauma",()=>{
                var f=Create();Check.True(Buy(f));var memory=Purchases(f.Store)[0];
                Check.Equal(1,memory.Salience);Check.True(memory.ExpiresAt>=memory.At+1800 && memory.ExpiresAt<=memory.At+86400);
                Check.True(f.Store.RecallSceneEvents(memory.ObserverId,memory.ExpiresAt)=="");
                var crowded=Create();
                for(int i=0;i<32;i++)crowded.Store.RememberSceneEvent(crowded.Buyer.Id,"trauma:"+i,NpcSceneEventKind.Murder,100,1,-1,"I witnessed a murder.");
                Check.True(Buy(crowded));Check.Equal(15,crowded.Store.ReadNpcInventory(crowded.Buyer.Id).WalletGold);
                Check.Equal(32,crowded.Store.Snapshot().SceneMemories.FindAll(m=>m.ObserverId==crowded.Buyer.Id).Count);
                Check.Equal(1,Purchases(crowded.Store).Count);
            }));
            cases.Add(("Native Inn product text cannot invalidate a committed purchase memory",()=>{
                var f=Create();Check.True(Buy(f,102,"Beer\nfrom native menu"));
                Check.Equal(15,f.Store.ReadNpcInventory(f.Buyer.Id).WalletGold);Check.Equal(2,Purchases(f.Store).Count);
                foreach(var memory in Purchases(f.Store))foreach(char c in memory.Observation)Check.True(!char.IsControl(c));
                Reload(f.Store);
            }));
            cases.Add(("Projected paid household service creates participant facts once through materialization",()=>{
                var f=HouseholdInnServiceTests.Prepared(20,0,"mother");
                var service=f.Store.AdvanceHouseholdInnService(f.Plan.Id,f.Departure+700,
                    (region,time)=>new NpcInnServiceTariff{RegionIndex=region,AbsoluteDay=(int)Math.Floor(time/86400d)});
                Check.Equal("purchased",service.Outcome);var memories=Purchases(f.Store);Check.Equal(5,memories.Count);
                Check.Equal(2,memories.FindAll(m=>m.ObserverId=="mother").Count);
                Check.Equal(1,memories.FindAll(m=>m.ObserverId=="father").Count);
                Check.Equal(2,memories.FindAll(m=>m.ObserverId==f.Keeper.Id).Count);
                var father=memories.Find(m=>m.ObserverId=="father");Check.True(father.Observation.Contains("paid") && !father.Observation.Contains("I paid"));
                Check.True(memories.Exists(m=>m.ObserverId=="mother" && m.SubjectId=="father" && m.Observation.Contains("I paid")));
                Check.True(memories.TrueForAll(m=>m.At==Math.Floor(service.CounterAt)));
                f.Store=Reload(f.Store);
                f.Store.AdvanceHouseholdInnService(f.Plan.Id,f.Departure+800,
                    (region,time)=>new NpcInnServiceTariff{RegionIndex=region,AbsoluteDay=(int)Math.Floor(time/86400d)});
                Check.True(f.Store.PrepareHouseholdInnObservation(f.Plan.Id,f.Departure+800,(catalog,pair)=>true));
                var a=f.Store.ProjectHouseholdInnMember(f.Plan.Id,"mother",f.Departure+800);
                var b=f.Store.ProjectHouseholdInnMember(f.Plan.Id,"father",f.Departure+800);
                var sim=f.Store.ReadHouseholdJointSimulation(2,7);
                f.Store.ReconstructHouseholdInnSimulation(f.Plan.Id,sim.Revision,f.Departure+800,
                    new HouseholdInnPlacement{ResidentId="mother",Position=a.Position},new HouseholdInnPlacement{ResidentId="father",Position=b.Position});
                Check.Equal(5,Purchases(f.Store).Count);Check.Equal(16,f.Store.ReadNpcInventory("mother").WalletGold);
                Check.Equal(0,f.Store.ReadNpcInventory("father").WalletGold);Reload(f.Store);
            }));
            cases.Add(("Pending and unavailable projected Inn admissions do not invent purchases",()=>{
                var f=HouseholdInnServiceTests.Prepared();
                var admission=f.Store.AdvanceHouseholdInnService(f.Plan.Id,f.Departure+300,
                    (region,time)=>new NpcInnServiceTariff{RegionIndex=region,AbsoluteDay=(int)Math.Floor(time/86400d)});
                Check.Equal("pending",admission.Outcome);
                Check.Equal(0,Purchases(f.Store).Count);
                var saved=f.Store.Snapshot();saved.NativePeople.Find(p=>p.Id==f.Keeper.Id).IsAlive=false;f.Store.Restore(saved);
                var refused=f.Store.AdvanceHouseholdInnService(f.Plan.Id,f.Departure+700,
                    (region,time)=>new NpcInnServiceTariff{RegionIndex=region,AbsoluteDay=(int)Math.Floor(time/86400d)});
                Check.True(refused==null);Check.Equal(0,Purchases(f.Store).Count);
            }));
        }
    }
}
