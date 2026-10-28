using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class FamilySimplificationTests
    {
        internal static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Group reserve covers every counter choice and charges only holiday prices",()=>{
                var f=HouseholdInnServiceTests.Prepared(4,0,"mother");
                Check.Equal(4,HouseholdInnBudget.GroupReserve(f.Store.ReadNpcInnServiceCatalog(2,10)));
                var pending=f.Store.AdvanceHouseholdInnService(f.Plan.Id,f.Departure+301,
                    (region,at)=>new NpcInnServiceTariff{RegionIndex=region,AbsoluteDay=(int)(at/86400),HolidayId=37});
                Check.Equal("pending",pending.Outcome);Check.True(pending.FirstReceipt==null && pending.SecondReceipt==null);
                Check.Equal(4,f.Store.ReadNpcInventory("mother").WalletGold);
                var paid=f.Store.AdvanceHouseholdInnService(f.Plan.Id,f.Departure+700,
                    (region,at)=>new NpcInnServiceTariff{RegionIndex=region,AbsoluteDay=(int)(at/86400),HolidayId=37});
                Check.Equal("purchased",paid.Outcome);Check.Equal(1,paid.FirstReceipt.ChargedGold);Check.Equal(1,paid.SecondReceipt.ChargedGold);
                Check.Equal(2,f.Store.ReadNpcInventory("mother").WalletGold);Check.Equal(0,f.Store.ReadNpcInventory("father").WalletGold);
                new WorldStateStore(0).Restore(f.Store.Snapshot());
            }));
            cases.Add(("Two partial wallets cannot replace one group payer before departure",()=>{
                var f=HouseholdInnServiceTests.Prepared(2,2,arrive:false);
                Check.True(f.Store.NeedsNpcCashErrand("mother",f.Plan.PlannedAt));
                Check.True(!f.Store.NeedsNpcCashErrand("father",f.Plan.PlannedAt));
                Check.Equal(4,f.Store.ReadPlannedHouseholdInnFunding("mother",f.Plan.PlannedAt).RequiredGold);
                Check.Throws<ArgumentException>(()=>f.Store.BeginHouseholdJointSimulation(f.Plan.Id,
                    f.Store.ReadHouseholdJointProgress(2,7).Sequence,f.Departure));
                Check.Equal(2,f.Store.ReadNpcInventory("mother").WalletGold);Check.Equal(2,f.Store.ReadNpcInventory("father").WalletGold);
            }));
            cases.Add(("Occupied observed seats hold materialization without changing offscreen purchases",()=>{
                var f=HouseholdInnServiceTests.Prepared();HouseholdInnServiceTests.Advance(f,f.Departure+700);
                Check.True(!f.Store.PrepareHouseholdInnObservation(f.Plan.Id,f.Departure+800,(catalog,pair)=>false));
                Check.True(f.Store.ProjectHouseholdInnMember(f.Plan.Id,"mother",f.Departure+800)==null);
                var paid=f.Store.ReadHouseholdInnServiceAdmission(f.Plan.Id);
                Check.Equal("purchased",paid.Outcome);Check.True(paid.FirstSeatId==null && paid.SecondSeatId==null);
                Check.Equal(16,f.Store.ReadNpcInventory("mother").WalletGold);
                Check.Equal(0,f.Store.Snapshot().ObservedVisits.Count);
            }));
            cases.Add(("Household days allow both adults out on about half their stable dates",()=>{
                int simultaneous=0,covered=0;
                for(int day=0;day<200;day++)
                {
                    var request=HouseholdDayPlanTests.Request(day);var plan=HouseholdDayPlanTests.Create(request);
                    bool together=plan.First.Outings[0].DepartureSeconds==plan.Second.Outings[0].DepartureSeconds;
                    if(together)simultaneous++;else covered++;
                    var repeated=HouseholdDayPlanTests.Create(request);
                    Check.Near(plan.Second.Outings[0].DepartureSeconds,repeated.Second.Outings[0].DepartureSeconds);
                    var store=HouseholdDayPlanTests.Store(request);
                    Check.True(store.TryPublishHouseholdDay(plan,request.PlannedAt,out var reason),reason);
                    new WorldStateStore(0).Restore(store.Snapshot());
                }
                Check.True(simultaneous>=70 && simultaneous<=130,"Both-out days: "+simultaneous);
                Check.True(covered>=70 && covered<=130,"Covered days: "+covered);
            }));
            cases.Add(("Night locks a family home even while an adult remains awake",()=>{
                var home=HouseholdDayPlanTests.Request().Household;
                var method=typeof(HouseholdAccess).GetMethod("RoutineLocked",new[]{typeof(Household),typeof(Func<Resident,ActivitySnapshot>),typeof(bool)});
                Check.True(method!=null,"Missing explicit day/night access boundary.");
                Func<Resident,ActivitySnapshot> awake=r=>new ActivitySnapshot(Activity.Home,7,new RoutePoint(),0);
                Check.True((bool)method.Invoke(null,new object[]{home,awake,true}));
                Check.True(!(bool)method.Invoke(null,new object[]{home,awake,false}));
            }));
            cases.Add(("Shared service uses one payer even when both adults have gold",()=>{
                var f=HouseholdInnServiceTests.Prepared(20,20);
                var service=(HouseholdInnServiceAdmission)HouseholdInnServiceTests.Advance(f,f.Departure+700);
                Check.Equal("purchased",service.Outcome);
                Check.True(service.FirstReceipt.PayerId!=null && service.FirstReceipt.PayerId==service.SecondReceipt.PayerId);
                string payer=service.FirstReceipt.PayerId,partner=payer=="mother"?"father":"mother";
                Check.Equal(16,f.Store.ReadNpcInventory(payer).WalletGold);
                Check.Equal(20,f.Store.ReadNpcInventory(partner).WalletGold);
                new WorldStateStore(0).Restore(f.Store.Snapshot());
            }));
            cases.Add(("Unobserved Inn service pays without reserving exact seats",()=>{
                var f=HouseholdInnServiceTests.Prepared();int reservations=0;
                var service=f.Store.AdvanceHouseholdInnService(f.Plan.Id,f.Departure+700,
                    (region,at)=>new NpcInnServiceTariff{RegionIndex=region,AbsoluteDay=(int)(at/86400)},
                    (catalog,pair)=>{reservations++;return false;});
                Check.True(service!=null && service.Outcome=="purchased","Offscreen payment depends on observed seat capacity.");
                Check.Equal(0,reservations);Check.True(service.FirstSeatId==null && service.SecondSeatId==null);
                Check.Equal(0,f.Store.Snapshot().ObservedVisits.Count);
                new WorldStateStore(0).Restore(f.Store.Snapshot());
            }));
            cases.Add(("Unobserved service needs menu and keeper but no interior geometry",()=>{
                var f=HouseholdInnServiceTests.Prepared();var catalog=f.Store.ReadNpcInnServiceCatalog(2,10);
                catalog.Pairs.Clear();catalog.EntryToCounter.Clear();catalog.CapturedAt++;
                f.Store.RegisterNpcInnServiceCatalog(NpcInnServiceCatalog.Seal(catalog));
                var service=(HouseholdInnServiceAdmission)HouseholdInnServiceTests.Advance(f,f.Departure+700);
                Check.Equal("purchased",service.Outcome);Check.True(service.FirstSeatId==null);
                new WorldStateStore(0).Restore(f.Store.Snapshot());
            }));
            cases.Add(("Adjacent family seats are selected only on observation",()=>{
                var f=HouseholdInnServiceTests.Prepared();HouseholdInnServiceTests.Advance(f,f.Departure+700);
                var method=typeof(WorldStateStore).GetMethod("PrepareHouseholdInnObservation");
                Check.True(method!=null,"Missing observation-time seat assignment.");
                Func<NpcInnServiceCatalog,NpcInnServiceSeatPair,bool> free=(catalog,pair)=>true;
                Check.True((bool)method.Invoke(f.Store,new object[]{f.Plan.Id,f.Departure+800,free}));
                var service=f.Store.ReadHouseholdInnServiceAdmission(f.Plan.Id);
                Check.Equal("chair-a",service.FirstSeatId);Check.Equal("chair-b",service.SecondSeatId);
                var a=f.Store.ProjectHouseholdInnMember(f.Plan.Id,"mother",f.Departure+800);
                var b=f.Store.ProjectHouseholdInnMember(f.Plan.Id,"father",f.Departure+800);
                Check.Near(3,a.Position.X);Check.Near(4,b.Position.X);
                Check.Equal(0,f.Store.Snapshot().ObservedVisits.Count);
            }));
        }
    }

}
