using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class HouseholdInnVisitTests
    {
        private sealed class Fixture
        {
            internal WorldStateStore Store;
            internal HouseholdDayPlan Plan;
            internal HouseholdDayRequest Request;
            internal HouseholdJointMovement Motion;
            internal NativePerson Keeper;
            internal double Departure,EnteredAt;
        }
        private static readonly JsonSerializerOptions Json=new JsonSerializerOptions{IncludeFields=true};
        private static SavedRoutePoint Point(double x){return new SavedRoutePoint{X=x};}
        private static Fixture Create(bool ordinaryInn=false,Action<Fixture> waiting=null,Action<HouseholdDayRequest> prepare=null)
        {
            var request=HouseholdDayAppointmentTests.SharedRequest();
            if(ordinaryInn)
            {
                request.FirstRoutine.DestinationKey=10;request.FirstRoutine.Route=HouseholdDayPlanTests.Visit(10).Stops[0].Route;
                request.FirstRoutine.Itinerary=HouseholdDayPlanTests.Visit(10);
            }
            prepare?.Invoke(request);
            var plan=HouseholdDayPlanTests.Create(request);
            var store=HouseholdDayPlanTests.Store(request);
            Check.True(store.TryPublishHouseholdDay(plan,request.PlannedAt,out var reason),reason);
            var motion=HouseholdJointMovement.Create(plan,request.PlannedAt);store.SaveHouseholdJointProgress(motion.Snapshot());
            double departure=plan.AbsoluteDay*86400d+plan.JointInn.First.DepartureSeconds;
            var keeper=store.RegisterNativePerson(2,10,900,"Inn keeper",ResidentSex.Female,0,0,182,0);
            foreach(string buyer in new[]{"mother","father"})Check.True(store.InitializeNpcInventory(buyer,20,Array.Empty<NpcInventoryItem>()));
            Check.True(store.InitializeNpcInventory(keeper.Id,40,Array.Empty<NpcInventoryItem>()));
            var fixture=new Fixture{Store=store,Plan=plan,Request=request,Motion=motion,Keeper=keeper,Departure=departure,EnteredAt=departure+30};
            waiting?.Invoke(fixture);
            motion.Depart(departure,Point(0),Point(0));store.SaveHouseholdJointProgress(motion.Snapshot());
            for(int i=1;i<=20;i++){motion.Observe(departure+i,Point(i*0.5),Point(i*0.5));store.SaveHouseholdJointProgress(motion.Snapshot());}
            motion.EnterInn(departure+30);store.SaveHouseholdJointProgress(motion.Snapshot());
            return fixture;
        }
        private static ObservedVisit Visit(Fixture f,string buyer="mother")
        {
            var timed=new List<ResidentVisitSnapshot>(ResidentVisitJourney.Create(f.Plan.JointInn.First.DepartureSeconds,
                f.Plan.JointInn.First.Itinerary).ScheduledVisits())[0];
            double end=f.Plan.AbsoluteDay*86400d+timed.LeaveAt;
            var value=new ObservedVisit{ResidentId=buyer,SettlementId=2,BuildingKey=10,ObservedAt=f.EnteredAt+1,
                PlannedEndAt=end,ResumeUntil=end,SecondsPerMetre=1,Legs=new List<ObservedVisitLeg>{
                    new ObservedVisitLeg{Route=new List<GuardPoint3>{new GuardPoint3()},DwellSeconds=60,AfterPhase=ShopVisitPhase.Haggling},
                    new ObservedVisitLeg{Route=new List<GuardPoint3>{new GuardPoint3()},AfterPhase=ShopVisitPhase.Complete}}};
            Set(value,"HouseholdDayId",f.Plan.Id);Set(value,"HouseholdSourceFingerprint",HouseholdJointMovement.SourceFingerprint(f.Plan));return value;
        }
        private static void Set(object value,string name,object fieldValue)
        {
            var field=value.GetType().GetField(name);
            if(field==null)throw new InvalidOperationException("Missing shared Inn field: "+name);
            field.SetValue(value,fieldValue);
        }
        private static string Text(object value,string name)
        {var field=value.GetType().GetField(name);if(field==null)throw new InvalidOperationException("Missing shared Inn field: "+name);return (string)field.GetValue(value);}
        private static WorldStateStore Reload(WorldStateStore source)
        {
            var state=JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(source.Snapshot(),Json),Json);
            var restored=new WorldStateStore(0);restored.Restore(state);return restored;
        }
        internal static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Atomic family capture cannot publish only one parent's selected chair",()=>{
                var f=Create();var first=Visit(f);var second=Visit(f,"father");Set(first,"HouseholdSeatId","chair-a");
                var method=typeof(WorldStateStore).GetMethod("StoreHouseholdInnVisits");
                bool rejected=false;try{method.Invoke(f.Store,new object[]{first,second});}
                catch(System.Reflection.TargetInvocationException error){if(error.InnerException is ArgumentException)rejected=true;else throw;}
                Check.True(rejected,"An asymmetric chair checkpoint cannot be restored as the intended pair.");
                Check.True(f.Store.GetObservedVisit("mother")==null && f.Store.GetObservedVisit("father")==null);
            }));
            cases.Add(("Paired Inn checkpoint publication rejects the second parent without partially moving the first",()=>{
                var f=Create();var first=Visit(f);var second=Visit(f,"father");f.Store.StoreObservedVisit(first);f.Store.StoreObservedVisit(second);
                first.ObservedAt++;second.ObservedAt++;second.BuildingKey=8;
                var method=typeof(WorldStateStore).GetMethod("StoreHouseholdInnVisits");
                if(method==null)throw new InvalidOperationException("Missing atomic household Inn checkpoints.");
                bool rejected=false;try{method.Invoke(f.Store,new object[]{first,second});}
                catch(System.Reflection.TargetInvocationException error){if(error.InnerException is ArgumentException)rejected=true;else throw;}
                Check.True(rejected);Check.Near(f.EnteredAt+1,f.Store.GetObservedVisit("mother").ObservedAt);
                Check.Near(f.EnteredAt+1,f.Store.GetObservedVisit("father").ObservedAt);
            }));
            cases.Add(("Paired Inn checkpoint publication preserves both settled receipts and shared chair intents",()=>{
                var f=Create();f.Store.StoreObservedVisit(Visit(f));f.Store.StoreObservedVisit(Visit(f,"father"));
                foreach(string buyer in new[]{"mother","father"})Check.True(f.Store.TryBuyNpcInnConsumption(buyer,f.Keeper.Id,
                    f.EnteredAt+2,NpcInnConsumptionKind.Drink,1,"Beer",5,out var receipt));
                var first=Visit(f);var second=Visit(f,"father");first.ObservedAt+=2;second.ObservedAt+=2;
                Set(first,"HouseholdSeatId","chair-a");Set(second,"HouseholdSeatId","chair-b");
                var method=typeof(WorldStateStore).GetMethod("StoreHouseholdInnVisits");
                if(method==null)throw new InvalidOperationException("Missing atomic household Inn checkpoints.");
                method.Invoke(f.Store,new object[]{first,second});var restored=Reload(f.Store);
                Check.True(restored.ReadNpcInnConsumption("mother")!=null && restored.ReadNpcInnConsumption("father")!=null);
                Check.Equal("chair-a",Text(restored.GetObservedVisit("mother"),"HouseholdSeatId"));
                Check.Near(first.ObservedAt,restored.GetObservedVisit("father").ObservedAt);
            }));
            cases.Add(("Shared chair intent survives completed exit checkpoints while the partner is still inside",()=>{
                var f=Create();var first=Visit(f);Set(first,"HouseholdSeatId","family-chair-a");
                first.Legs=new List<ObservedVisitLeg>{new ObservedVisitLeg{Route=new List<GuardPoint3>{new GuardPoint3()},AfterPhase=ShopVisitPhase.Complete}};
                var second=Visit(f,"father");Set(second,"HouseholdSeatId","family-chair-b");second.Legs[0].AfterPhase=ShopVisitPhase.Browsing;second.Legs[0].SeatId="family-chair-b";
                f.Store.StoreObservedVisit(first);f.Store.StoreObservedVisit(second);
                var restored=Reload(f.Store);Check.Equal("family-chair-a",Text(restored.GetObservedVisit("mother"),"HouseholdSeatId"));
                Check.Equal("family-chair-b",Text(restored.GetObservedVisit("father"),"HouseholdSeatId"));
            }));
            cases.Add(("Shared chair metadata requires family provenance and cannot change its remaining chair leg",()=>{
                var f=Create();var visit=Visit(f);Set(visit,"HouseholdSeatId","family-chair-a");
                visit.Legs[0].AfterPhase=ShopVisitPhase.Browsing;visit.Legs[0].SeatId="different-chair";
                Check.Throws<ArgumentException>(()=>ObservedVisit.Copy(visit));
                visit=Visit(f);Set(visit,"HouseholdSeatId","family-chair-a");Set(visit,"HouseholdDayId",null);Set(visit,"HouseholdSourceFingerprint",null);
                Check.Throws<ArgumentException>(()=>ObservedVisit.Copy(visit));
            }));
            cases.Add(("Schema70 rejects future chair intent but migrates visits with no chair metadata",()=>{
                var f=Create();f.Store.StoreObservedVisit(Visit(f));var saved=f.Store.Snapshot();saved.Version=70;
                var restored=new WorldStateStore(0);restored.Restore(saved);
                Set(saved.ObservedVisits[0],"HouseholdSeatId","family-chair-a");Check.Throws<ArgumentException>(()=>restored.Restore(saved));
                Check.True(Text(restored.GetObservedVisit("mother"),"HouseholdSeatId")==null);
            }));
            cases.Add(("Shared seating waits for two successful source-bound purchases across reload",()=>{
                var f=Create();Check.Equal("Pending",Service(f,f.EnteredAt+1));
                f.Store.StoreObservedVisit(Visit(f));f.Store.StoreObservedVisit(Visit(f,"father"));
                Check.Equal("Pending",Service(f,f.EnteredAt+1));
                Check.True(f.Store.TryBuyNpcInnConsumption("mother",f.Keeper.Id,f.EnteredAt+2,NpcInnConsumptionKind.Drink,1,"Beer",5,out var first));
                Check.Equal("Pending",Service(f,f.EnteredAt+2));
                Check.True(f.Store.TryBuyNpcInnConsumption("father",f.Keeper.Id,f.EnteredAt+3,NpcInnConsumptionKind.Drink,1,"Beer",5,out var second));
                Check.Equal("Ready",Service(f,f.EnteredAt+3));f.Store=Reload(f.Store);
                Check.Equal("Ready",Service(f,f.EnteredAt+4));Check.Equal(15,f.Store.ReadNpcInventory("mother").WalletGold);
            }));
            cases.Add(("One failed parent purchase aborts joint seating even before the partner reaches service",()=>{
                var f=Create();f.Store.StoreObservedVisit(Visit(f));
                Check.True(f.Store.TryRecordNpcInnNoPurchase("mother",f.Keeper.Id,f.EnteredAt+2,NpcInnConsumptionOutcome.Refused,"keeper refused service",out var receipt));
                Check.Equal("Unavailable",Service(f,f.EnteredAt+2));Check.Equal(20,f.Store.ReadNpcInventory("mother").WalletGold);
            }));
            cases.Add(("Joint seating cannot use future receipts or outlive current family duties",()=>{
                var f=Create();f.Store.StoreObservedVisit(Visit(f));f.Store.StoreObservedVisit(Visit(f,"father"));
                foreach(string buyer in new[]{"mother","father"})Check.True(f.Store.TryBuyNpcInnConsumption(buyer,f.Keeper.Id,
                    f.EnteredAt+5,NpcInnConsumptionKind.Drink,1,"Beer",5,out var receipt));
                Check.Equal("Pending",Service(f,f.EnteredAt+4));
                Check.Equal("Unavailable",Service(f,Visit(f).PlannedEndAt));
                var booking=new AppointmentRecord{Id="shared-seat-duty",ResidentId="father",Status="accepted",Purpose="meet",Importance=2,
                    StartsAt=f.EnteredAt+120,EndsAt=f.EnteredAt+720,Place=new AppointmentPlace{Kind="building",SettlementId=2,BuildingKey=7}};
                Check.True(f.Store.TryReserveAppointment(booking,f.EnteredAt+6,out var reason),reason);
                // This fixture supplies no native proof for the new meeting.
                // Hold both rather than pretending that proof or seating exists.
                Check.Equal("Blocked",Service(f,f.EnteredAt+6));
            }));
            cases.Add(("Shared fractional campaign-time receipts survive native fifteen-digit wire rounding",()=>{
                var f=Create(prepare:request=>{
                    request.AbsoluteDay=145805;
                    while(!HouseholdDayPlanner.SharedDay(request.WorldSeed,2,request.Household,request.AbsoluteDay))request.AbsoluteDay++;
                    request.PlannedAt=request.AbsoluteDay*86400d+8*3600;
                    request.JointInn.First.Itinerary.Stops[0].WalkSeconds=300.1234567;
                    request.JointInn.Second.Itinerary.Stops[0].WalkSeconds=300.1234567;
                });
                var visit=Visit(f);f.Store.StoreObservedVisit(visit);
                Check.True(f.Store.TryBuyNpcInnConsumption("mother",f.Keeper.Id,f.EnteredAt+2,NpcInnConsumptionKind.Drink,1,"Beer",5,out var receipt));
                var saved=f.Store.Snapshot();
                foreach(var observed in saved.ObservedVisits){observed.PlannedEndAt=WireTime(observed.PlannedEndAt);observed.ResumeUntil=WireTime(observed.ResumeUntil);}
                foreach(var cursor in saved.NpcInnConsumptionCursors)cursor.PlannedEndAt=WireTime(cursor.PlannedEndAt);
                Check.True(saved.ObservedVisits[0].PlannedEndAt!=visit.PlannedEndAt,"Fixture must reproduce actual native precision loss.");
                var restored=new WorldStateStore(0);restored.Restore(saved);
                Check.Equal(receipt.VisitId,restored.ReadNpcInnConsumption("mother").VisitId);
                Check.Equal(15,restored.ReadNpcInventory("mother").WalletGold);
                Check.True(!restored.TryBuyNpcInnConsumption("mother",f.Keeper.Id,f.EnteredAt+3,NpcInnConsumptionKind.Drink,1,"Beer",5,out var replay));
            }));
            cases.Add(("A later entered family event can settle an equal-end ordinary cursor without reopening the old visit",()=>{
                ObservedVisit ordinary=null;NpcInnConsumptionReceipt prior=null;
                var f=Create(true,waiting=>{
                    ordinary=Visit(waiting);Set(ordinary,"HouseholdDayId",null);Set(ordinary,"HouseholdSourceFingerprint",null);
                    ordinary.ObservedAt=waiting.Departure-100;waiting.Store.StoreObservedVisit(ordinary);
                    Check.True(waiting.Store.TryBuyNpcInnConsumption("mother",waiting.Keeper.Id,ordinary.ObservedAt+2,
                        NpcInnConsumptionKind.Drink,1,"Beer",5,out prior));
                    waiting.Store.RemoveObservedVisit("mother");
                });
                var shared=Visit(f);f.Store.StoreObservedVisit(shared);
                Check.True(!f.Store.IsNpcInnConsumptionRetired("mother"),"A genuinely later family entry was mistaken for replay.");
                Check.True(f.Store.TryBuyNpcInnConsumption("mother",f.Keeper.Id,f.EnteredAt+2,NpcInnConsumptionKind.Drink,1,"Beer",5,out var next));
                Check.True(prior.VisitId!=next.VisitId);Check.Equal(10,f.Store.ReadNpcInventory("mother").WalletGold);
                var restored=Reload(f.Store);Check.Equal(40,restored.ReadNpcInventory(f.Keeper.Id).WalletGold);
                restored.RemoveObservedVisit("mother");restored.StoreObservedVisit(ordinary);
                Check.True(restored.IsNpcInnConsumptionRetired("mother"));
                Check.True(!restored.TryBuyNpcInnConsumption("mother",f.Keeper.Id,f.EnteredAt+3,NpcInnConsumptionKind.Drink,1,"Beer",5,out var replay));
                Check.Equal(10,restored.ReadNpcInventory("mother").WalletGold);
            }));
            cases.Add(("A later equal-end shared no-purchase owns its own cursor instead of inheriting an ordinary retirement",()=>{
                var f=Create(true,waiting=>{
                    var ordinary=Visit(waiting);Set(ordinary,"HouseholdDayId",null);Set(ordinary,"HouseholdSourceFingerprint",null);
                    ordinary.ObservedAt=waiting.Departure-100;waiting.Store.StoreObservedVisit(ordinary);
                    Check.True(waiting.Store.TryBuyNpcInnConsumption("mother",waiting.Keeper.Id,ordinary.ObservedAt+2,
                        NpcInnConsumptionKind.Drink,1,"Beer",5,out var prior));waiting.Store.RemoveObservedVisit("mother");
                });
                var shared=Visit(f);f.Store.StoreObservedVisit(shared);
                Check.True(f.Store.TryRecordNpcInnNoPurchase("mother",f.Keeper.Id,f.EnteredAt+2,
                    NpcInnConsumptionOutcome.Unavailable,"No product available",out var next));
                var restored=Reload(f.Store);var cursor=restored.Snapshot().NpcInnConsumptionCursors.Find(c=>c.BuyerId=="mother");
                Check.Equal(next.VisitId,cursor.Receipt.VisitId);Check.Equal(f.Plan.Id,cursor.HouseholdDayId);
                Check.Equal(15,restored.ReadNpcInventory("mother").WalletGold);
            }));
            cases.Add(("Actual family interior checkpoints can survive midnight without extending leisure purchases",()=>{
                var f=Create();var visit=Visit(f);visit.ObservedAt=(f.Plan.AbsoluteDay+1d)*86400+10;
                f.Store.StoreObservedVisit(visit);var restored=Reload(f.Store);
                Check.Near(visit.ObservedAt,restored.GetObservedVisit("mother").ObservedAt);
                Check.True(!restored.TryBuyNpcInnConsumption("mother",f.Keeper.Id,visit.ObservedAt+1,NpcInnConsumptionKind.Drink,1,"Beer",5,out var ignored));
                Check.Equal(20,restored.ReadNpcInventory("mother").WalletGold);
            }));
            cases.Add(("Shared counter cannot spend money after its planned leisure window expires",()=>{
                var f=Create();var visit=Visit(f);visit.ObservedAt=visit.PlannedEndAt-1;
                f.Store.StoreObservedVisit(visit);
                Check.True(!f.Store.TryBuyNpcInnConsumption("mother",f.Keeper.Id,visit.PlannedEndAt+1,NpcInnConsumptionKind.Drink,1,"Beer",5,out var ignored));
                Check.True(!f.Store.TryRecordNpcInnNoPurchase("mother",f.Keeper.Id,visit.PlannedEndAt+1,NpcInnConsumptionOutcome.Unavailable,"late callback",out ignored));
                Check.Equal(20,f.Store.ReadNpcInventory("mother").WalletGold);
            }));
            cases.Add(("New required family duty preempts an already captured shared counter",()=>{
                var f=Create();f.Store.StoreObservedVisit(Visit(f));
                var booking=new AppointmentRecord{Id="new-shared-inn-duty",ResidentId="mother",Status="accepted",Purpose="meet",Importance=2,
                    StartsAt=f.EnteredAt+120,EndsAt=f.EnteredAt+720,Place=new AppointmentPlace{Kind="building",SettlementId=2,BuildingKey=7}};
                Check.True(f.Store.TryReserveAppointment(booking,f.EnteredAt+1,out var reason),reason);
                Check.True(!f.Store.TryBuyNpcInnConsumption("mother",f.Keeper.Id,f.EnteredAt+2,NpcInnConsumptionKind.Drink,1,"Beer",5,out var ignored));
                Check.Equal(20,f.Store.ReadNpcInventory("mother").WalletGold);
            }));
            cases.Add(("Changed parent routine source cannot settle an old shared counter checkpoint",()=>{
                var f=Create();f.Store.StoreObservedVisit(Visit(f));
                var replacement=JsonSerializer.Deserialize<ResidentRoutine>(JsonSerializer.Serialize(f.Request.FirstRoutine,Json),Json);
                replacement.ItineraryDepartureSeconds++;f.Store.SetResidentRoutine(2,replacement);
                Check.True(!f.Store.TryBuyNpcInnConsumption("mother",f.Keeper.Id,f.EnteredAt+2,NpcInnConsumptionKind.Drink,1,"Beer",5,out var ignored));
                Check.Equal(20,f.Store.ReadNpcInventory("mother").WalletGold);
            }));
            cases.Add(("Legacy Inn payment identifier stays byte-identical after shared visit support",()=>{
                var visit=new ObservedVisit{ResidentId="legacy",SettlementId=2,BuildingKey=10,PlannedEndAt=100};
                Check.Equal("inn:066c5200014423c8ac72f3f5359d6a88f3415b137de35a3c95cd4967032a87d5",NpcInnConsumption.VisitId(visit));
            }));
            cases.Add(("Entered shared Inn cannot be recaptured without provenance through an ordinary target",()=>{
                var f=Create(true);var ordinary=Visit(f);Set(ordinary,"HouseholdDayId",null);Set(ordinary,"HouseholdSourceFingerprint",null);
                Check.Throws<ArgumentException>(()=>f.Store.StoreObservedVisit(ordinary));
            }));
            cases.Add(("Next household day removes only obsolete family visits while payment cursor survives",()=>{
                var f=Create();f.Store.StoreObservedVisit(Visit(f));
                Check.True(f.Store.TryBuyNpcInnConsumption("mother",f.Keeper.Id,f.EnteredAt+2,NpcInnConsumptionKind.Drink,1,"Beer",5,out var receipt));
                var other=Visit(f,"father");Set(other,"HouseholdDayId",null);Set(other,"HouseholdSourceFingerprint",null);other.BuildingKey=8;
                f.Store.StoreObservedVisit(other);
                f.Motion.BeginReturn(f.EnteredAt+5,Point(10),Point(10));f.Store.SaveHouseholdJointProgress(f.Motion.Snapshot());
                for(int i=1;i<=20;i++){f.Motion.Observe(f.EnteredAt+5+i,Point(10-i*0.5),Point(10-i*0.5));f.Store.SaveHouseholdJointProgress(f.Motion.Snapshot());}
                f.Request.AbsoluteDay++;f.Request.PlannedAt=f.Request.AbsoluteDay*86400d+8*3600;f.Request.JointInn=null;
                var next=HouseholdDayPlanTests.Create(f.Request);
                Check.True(f.Store.TryPublishHouseholdDay(next,f.Request.PlannedAt,out var reason),reason);
                Check.True(f.Store.GetObservedVisit("mother")==null);Check.True(f.Store.GetObservedVisit("father")!=null);
                var restored=Reload(f.Store);Check.Equal(receipt.VisitId,restored.Snapshot().NpcInnConsumptionCursors.Find(c=>c.BuyerId=="mother").Receipt.VisitId);
                Check.Equal(15,restored.ReadNpcInventory("mother").WalletGold);
            }));
            cases.Add(("Schema69 visiting migration infers entry only from the accepted visiting observation",()=>{
                var f=Create();var saved=f.Store.Snapshot();saved.Version=69;saved.HouseholdJointProgress[0].EnteredAt=0;
                var restored=new WorldStateStore(0);restored.Restore(saved);
                Check.Near(f.EnteredAt,restored.ReadHouseholdJointProgress(2,7).EnteredAt);
                saved=f.Store.Snapshot();saved.Version=69;
                Check.Throws<ArgumentException>(()=>restored.Restore(saved));
            }));
            cases.Add(("A door-only return cannot invent a historical shared Inn checkpoint",()=>{
                var f=Create();var saved=f.Store.Snapshot();var progress=saved.HouseholdJointProgress[0];
                progress.Phase=HouseholdJointPhase.Returning;progress.EnteredAt=0;progress.ReturnStartedAt=f.EnteredAt+5;progress.ObservedAt=progress.ReturnStartedAt;
                progress.First.Segment=0;progress.First.Distance=0;progress.Second.Segment=0;progress.Second.Distance=0;
                saved.ObservedVisits.Add(Visit(f));var restored=new WorldStateStore(0);
                Check.Throws<ArgumentException>(()=>restored.Restore(saved));
            }));
            cases.Add(("Shared Inn admission requires the exact visiting family source outside ordinary shop targets",()=>{
                var f=Create();f.Store.StoreObservedVisit(Visit(f));
                var copy=f.Store.GetObservedVisit("mother");Check.Equal(f.Plan.Id,Text(copy,"HouseholdDayId"));
                Check.Equal(HouseholdJointMovement.SourceFingerprint(f.Plan),Text(copy,"HouseholdSourceFingerprint"));
                Check.Equal(f.Plan.Id,Text(Reload(f.Store).GetObservedVisit("mother"),"HouseholdDayId"));
            }));
            cases.Add(("Shared Inn source fields are paired and cannot impersonate another parent or venue",()=>{
                var f=Create();var visit=Visit(f);Set(visit,"HouseholdSourceFingerprint",null);
                Check.Throws<ArgumentException>(()=>ObservedVisit.Copy(visit));
                visit=Visit(f);Set(visit,"HouseholdSourceFingerprint",new string('a',64));
                Check.Throws<ArgumentException>(()=>f.Store.StoreObservedVisit(visit));
                visit=Visit(f);visit.ResidentId="child";Check.Throws<ArgumentException>(()=>f.Store.StoreObservedVisit(visit));
                visit=Visit(f);visit.BuildingKey=8;Check.Throws<ArgumentException>(()=>f.Store.StoreObservedVisit(visit));
                visit=Visit(f);visit.PlannedEndAt++;visit.ResumeUntil++;Check.Throws<ArgumentException>(()=>f.Store.StoreObservedVisit(visit));
                visit=Visit(f);visit.ObservedAt=f.EnteredAt-1;Check.Throws<ArgumentException>(()=>f.Store.StoreObservedVisit(visit));
            }));
            cases.Add(("Ordinary and shared Inn visits with equal buyer target and end cannot share payment identity",()=>{
                var f=Create();var shared=Visit(f);var ordinary=ObservedVisit.Copy(shared);
                Set(ordinary,"HouseholdDayId",null);Set(ordinary,"HouseholdSourceFingerprint",null);
                Check.True(NpcInnConsumption.VisitId(shared)!=NpcInnConsumption.VisitId(ordinary));
                f.Store.StoreObservedVisit(shared);
                Check.True(f.Store.TryBuyNpcInnConsumption("mother",f.Keeper.Id,f.EnteredAt+2,NpcInnConsumptionKind.Drink,1,"Beer",5,out var receipt));
                ordinary.InnConsumption=receipt;Check.Throws<ArgumentException>(()=>ObservedVisit.Copy(ordinary));
            }));
            cases.Add(("Both parents pay separately and native visit provenance survives consumption cursor reload",()=>{
                var f=Create();f.Store.StoreObservedVisit(Visit(f));f.Store.StoreObservedVisit(Visit(f,"father"));
                Check.True(f.Store.TryBuyNpcInnConsumption("mother",f.Keeper.Id,f.EnteredAt+2,NpcInnConsumptionKind.Drink,1,"Beer",5,out var first));
                Check.True(f.Store.TryBuyNpcInnConsumption("father",f.Keeper.Id,f.EnteredAt+2,NpcInnConsumptionKind.Food,4,"Bread",3,out var second));
                Check.True(first.VisitId!=second.VisitId);Check.Equal(15,f.Store.ReadNpcInventory("mother").WalletGold);
                Check.Equal(17,f.Store.ReadNpcInventory("father").WalletGold);Check.Equal(40,f.Store.ReadNpcInventory(f.Keeper.Id).WalletGold);
                var restored=Reload(f.Store);Check.Equal(first.VisitId,restored.ReadNpcInnConsumption("mother").VisitId);
                var cursor=restored.Snapshot().NpcInnConsumptionCursors.Find(c=>c.BuyerId=="mother");
                Check.Equal(f.Plan.Id,Text(cursor,"HouseholdDayId"));
                Check.True(!restored.TryBuyNpcInnConsumption("mother",f.Keeper.Id,f.EnteredAt+3,NpcInnConsumptionKind.Drink,1,"Beer",5,out var ignored));
                Check.Equal(40,restored.ReadNpcInventory(f.Keeper.Id).WalletGold);
            }));
            cases.Add(("Returning family retains historical visit but cannot issue a new Inn settlement",()=>{
                var f=Create();f.Store.StoreObservedVisit(Visit(f));
                f.Motion.BeginReturn(f.EnteredAt+5,Point(10),Point(10));f.Store.SaveHouseholdJointProgress(f.Motion.Snapshot());
                Check.True(!f.Store.TryBuyNpcInnConsumption("mother",f.Keeper.Id,f.EnteredAt+6,NpcInnConsumptionKind.Drink,1,"Beer",5,out var ignored));
                Check.True(!f.Store.TryRecordNpcInnNoPurchase("mother",f.Keeper.Id,f.EnteredAt+6,NpcInnConsumptionOutcome.Unavailable,"late callback",out ignored));
                Check.Throws<ArgumentException>(()=>f.Store.StoreObservedVisit(Visit(f)));
                Check.True(Reload(f.Store).GetObservedVisit("mother")!=null);Check.Equal(40,f.Store.ReadNpcInventory(f.Keeper.Id).WalletGold);
            }));
            cases.Add(("Shared Inn future fields are rejected by schema69 without changing the current store",()=>{
                var f=Create();f.Store.StoreObservedVisit(Visit(f));var snapshot=f.Store.Snapshot();snapshot.Version=69;
                var restored=new WorldStateStore(0);Check.Throws<ArgumentException>(()=>restored.Restore(snapshot));
                Check.Equal(f.Plan.Id,Text(f.Store.GetObservedVisit("mother"),"HouseholdDayId"));
            }));
            cases.Add(("Inn entry time survives actual return and distinguishes a door-only turnback",()=>{
                var f=Create();var field=typeof(HouseholdJointProgress).GetField("EnteredAt");
                if(field==null)throw new InvalidOperationException("Missing explicit shared Inn entry time.");
                Check.Near(f.EnteredAt,(double)field.GetValue(f.Motion.Snapshot()));
                f.Motion.BeginReturn(f.EnteredAt+5,Point(10),Point(10));f.Store.SaveHouseholdJointProgress(f.Motion.Snapshot());
                Check.Near(f.EnteredAt,(double)field.GetValue(Reload(f.Store).ReadHouseholdJointProgress(2,7)));
            }));
        }
        private static double WireTime(double value){return double.Parse(value.ToString("G15",CultureInfo.InvariantCulture),CultureInfo.InvariantCulture);}
        private static string Service(Fixture f,double now)
        {
            var method=typeof(WorldStateStore).GetMethod("ReadHouseholdInnService");
            if(method==null)throw new InvalidOperationException("Missing shared Inn service gate.");
            return method.Invoke(f.Store,new object[]{2,7,now,null}).ToString();
        }
    }
}
