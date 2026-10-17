using System;
using System.Collections.Generic;
using LivingCities.Core;
namespace LivingCities.Tests
{
    internal static class InnLodgerHomeTests
    {
        private static WorldStateStore Fixture(out NativePerson lodger)
        {
            var store=new WorldStateStore(42);store.GetOrCreateFamilies(1,new[]{new HomeProfile(10,2)});
            lodger=store.RegisterNativePerson(1,5,11,"Visiting Person",ResidentSex.Female,1,0,182,1);
            store.RegisterLodging(1,5,new[]{new LodgingRoom {Id="bedroom",Beds=new List<string>{"bed:1"}},
                new LodgingRoom {Id="staff",NonRentable=true,Beds=new List<string>{"bed:2"}}});
            store.TryClaimLodgingBed(1,5,lodger.Id,"bed:1",100);return store;
        }
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("A pending replacement cannot accept another lodger and poison its saved recovery",()=>{
                NativePerson person;var store=Fixture(out person);
                store.GetOrCreateFamilies(2,new[]{new HomeProfile(20,2,0,false)});
                var profile=new HomeProfile(20,2);Check.True(store.TryReserveInnLodgerHome(person.Id,"bedroom",2,profile,100)!=null);
                var death=store.RecordViolence(1,person.Id,110);store.LocateViolence(death.Id,5,0,0,0);person.IsAlive=false;store.ConfirmCorpse(death.Id,111,5,0,0,0);
                Check.True(store.EnsureHouseholdRecovery(2,20,HouseholdRecoveryTier.DenseCity,new HouseholdRecoveryEligibility {BedCount=2},HouseholdVacancyOutcome.NewFamily)!=null);
                var other=store.RegisterNativePerson(1,5,22,"Later Lodger",ResidentSex.Male,1,0,182,2);
                Check.True(store.TryReserveInnLodgerHome(other.Id,"bedroom",2,profile,200)==null);
                var copy=new WorldStateStore(1);copy.Restore(store.Snapshot());Check.True(copy.InnLodgerHome(other.Id)==null);
            }));
            cases.Add(("A lone absent lodger protects the home until death then participates in ordinary recovery",()=>{
                NativePerson person;var store=Fixture(out person);var profile=new HomeProfile(20,1);
                Check.True(store.TryReserveInnLodgerHome(person.Id,"bedroom",2,profile,100)!=null);
                store.GetOrCreateFamilies(2,new[]{profile});var facts=new HouseholdRecoveryEligibility {BedCount=1};
                Check.True(store.EnsureHouseholdRecovery(2,20,HouseholdRecoveryTier.DenseCity,facts,HouseholdVacancyOutcome.NewFamily)==null);
                var death=store.RecordViolence(1,person.Id,110);store.LocateViolence(death.Id,5,0,0,0);person.IsAlive=false;
                store.ConfirmCorpse(death.Id,111,5,0,0,0);
                var recovery=store.EnsureHouseholdRecovery(2,20,HouseholdRecoveryTier.DenseCity,facts,HouseholdVacancyOutcome.NewFamily);
                Check.True(recovery!=null);store.ObserveCorpses(1,-1,recovery.RecoveryAt);
                Check.True(store.AdvanceHouseholdRecovery(2,20,recovery.RecoveryAt,profile,facts,true));
                var copy=new WorldStateStore(99);copy.Restore(store.Snapshot());
                Check.Equal(1,copy.CurrentPopulation(2).Households[0].Generation);
                Check.Equal(0,copy.HouseholdInnLodgers(2,20).Count);Check.True(copy.InnLodgerHome(person.Id)!=null);
            }));
            cases.Add(("A saved bank-sale property cannot be used as a new household reservation",()=>{
                NativePerson person;var store=Fixture(out person);var profile=new HomeProfile(20,2);
                var town=store.GetOrCreateFamilies(2,new[]{profile});
                foreach(var resident in town.Households[0].Residents)
                {
                    var death=store.RecordViolence(2,resident.Id,100);store.LocateViolence(death.Id,20,0,0,0);resident.IsAlive=false;store.ConfirmCorpse(death.Id,101);
                }
                var sale=store.EnsureHouseholdRecovery(2,20,HouseholdRecoveryTier.DenseCity,new HouseholdRecoveryEligibility {BedCount=2},HouseholdVacancyOutcome.BankSale);
                Check.True(sale!=null);Check.Throws<ArgumentException>(()=>store.ReserveHousehold(2,profile));
                Check.True(store.TryReserveInnLodgerHome(person.Id,"bedroom",2,profile,200)==null);
                Check.True(!town.Households[0].Residents.Exists(r=>r.IsAlive));
            }));
            cases.Add(("Lodger reservation copies are isolated and corrupt or duplicate bed slots reject atomically",()=>{
                NativePerson person;var store=Fixture(out person);
                var saved=store.TryReserveInnLodgerHome(person.Id,"bedroom",2,new HomeProfile(20,2),100);Check.True(saved!=null);
                saved.HomeKey=99;Check.Equal(20,store.InnLodgerHome(person.Id).HomeKey);
                var corrupt=store.Snapshot();corrupt.InnLodgerHomes[0].BedSlot=0;
                Check.Throws<ArgumentException>(()=>store.Restore(corrupt));Check.Equal(20,store.InnLodgerHome(person.Id).HomeKey);
                corrupt=store.Snapshot();corrupt.InnLodgerHomes.Add(corrupt.InnLodgerHomes[0]);
                Check.Throws<ArgumentException>(()=>store.Restore(corrupt));
            }));
            cases.Add(("Legacy saves restore with no fabricated lodger homes",()=>{
                NativePerson person;var store=Fixture(out person);var legacy=store.Snapshot();legacy.Version=23;legacy.InnLodgerHomes=null;
                store.Restore(legacy);Check.True(store.InnLodgerHome(person.Id)==null);Check.Equal(24,store.Snapshot().Version);
            }));
            cases.Add(("A reserved lodger bed cannot be reused by a local native worker",()=>{
                NativePerson person;var store=Fixture(out person);
                Check.True(store.TryReserveInnLodgerHome(person.Id,"bedroom",2,new HomeProfile(20,1),100)!=null);
                var worker=store.RegisterNativePerson(2,30,99,"Local Worker",ResidentSex.Male,1,0,182,1);
                Check.True(!store.AssignNativeBed(worker.Id,new HomeProfile(30,0),new[]{new HomeProfile(20,1)}));
                Check.Equal(-1,worker.HomeKey);
            }));
            cases.Add(("Another bedroom occupant can reserve a home without sharing the first temporary inn bed",()=>{
                NativePerson person;var store=Fixture(out person);
                var second=store.RegisterNativePerson(1,5,12,"Other Lodger",ResidentSex.Male,1,0,182,2);
                Check.True(store.NativeLodgingBed(second.Id)==null);
                Check.True(store.TryReserveInnLodgerHome(second.Id,"bedroom",2,new HomeProfile(20,1),100)!=null);
                Check.True(store.NativeLodgingBed(person.Id)!=null);Check.True(store.NativeLodgingBed(second.Id)==null);
            }));
            cases.Add(("Inn bedroom lodger reserves an unseen home slot without creating a duplicate resident",()=>{
                NativePerson person;var store=Fixture(out person);
                var home=store.TryReserveInnLodgerHome(person.Id,"bedroom",2,new HomeProfile(20,1),100);
                Check.True(home!=null);Check.Equal(2,home.HomeSettlement);Check.Equal(20,home.HomeKey);Check.Equal(0,home.BedSlot);
                var town=store.CurrentPopulation(2);Check.True(town.IsPartial);Check.Equal(0,town.Households[0].Residents.Count);
                Check.Equal(-1,person.HomeKey);Check.True(store.IsResidentAway(person.Id,101));
                Check.Equal(home.HomeKey,store.TryReserveInnLodgerHome(person.Id,"bedroom",3,new HomeProfile(30,2),200).HomeKey);
                Check.True(store.CurrentPopulation(3)==null);
            }));
            cases.Add(("Main hall staff and local origins cannot acquire foreign lodger reservations",()=>{
                NativePerson person;var store=Fixture(out person);
                Check.True(store.TryReserveInnLodgerHome(person.Id,null,2,new HomeProfile(20,2),100)==null);
                Check.True(store.TryReserveInnLodgerHome(person.Id,"staff",2,new HomeProfile(20,2),100)==null);
                Check.True(store.TryReserveInnLodgerHome(person.Id,"missing",2,new HomeProfile(20,2),100)==null);
                Check.True(store.TryReserveInnLodgerHome(person.Id,"bedroom",1,new HomeProfile(20,2),100)==null);
                Check.True(store.CurrentPopulation(2)==null);
            }));
            cases.Add(("Lodger reservation survives completion and restore with original identity and reserved capacity",()=>{
                NativePerson person;var store=Fixture(out person);
                var home=store.TryReserveInnLodgerHome(person.Id,"bedroom",2,new HomeProfile(20,2),100);Check.True(home!=null);
                store.GetOrCreateFamilies(2,new[]{new HomeProfile(20,2),new HomeProfile(21,3)});
                var copy=new WorldStateStore(99);copy.Restore(store.Snapshot());
                var saved=copy.InnLodgerHome(person.Id);Check.True(saved!=null);Check.Equal(home.BedSlot,saved.BedSlot);
                Check.Equal(1,copy.HouseholdInnLodgers(2,20).Count);Check.True(copy.IsResidentAway(person.Id,1000000));
                foreach(var family in copy.CurrentPopulation(2).Households)
                    Check.True(!family.Residents.Exists(r=>r.Id==person.Id));
                Check.Equal(1,copy.CurrentPopulation(2).Households.Find(h=>h.BuildingKey==20).Residents.Count);
            }));
            cases.Add(("Known full homes never evict residents for incoming lodgers",()=>{
                NativePerson person;var store=Fixture(out person);
                var town=store.GetOrCreateFamilies(2,new[]{new HomeProfile(20,1)});var id=town.Households[0].Residents[0].Id;
                Check.True(store.TryReserveInnLodgerHome(person.Id,"bedroom",2,new HomeProfile(20,1),100)==null);
                Check.Equal(id,town.Households[0].Residents[0].Id);
            }));
            cases.Add(("Unavailable property is excluded from families and lodger reservations",()=>{
                NativePerson person;var store=Fixture(out person);var unavailable=new HomeProfile(20,3,0,false);
                Check.Equal(0,Population.CreateFamilies(42,2,new[]{unavailable}).Households[0].Residents.Count);
                Check.True(store.TryReserveInnLodgerHome(person.Id,"bedroom",2,unavailable,100)==null);
                Check.True(store.CurrentPopulation(2)==null);
            }));
            cases.Add(("Dead inn lodger keeps permanent home key without resurrecting or taking an inn key",()=>{
                NativePerson person;var store=Fixture(out person);
                var home=store.TryReserveInnLodgerHome(person.Id,"bedroom",2,new HomeProfile(20,1),100);Check.True(home!=null);
                var alarm=store.RecordViolence(1,person.Id,110);store.LocateViolence(alarm.Id,5,0,0,0);person.IsAlive=false;
                store.ConfirmCorpse(alarm.Id,111,5,0,0,0);store.InitializeCorpseHomeKey(alarm.Id,null,(town,key)=>town==2);
                var keys=store.GetAvailableCorpsePropertyKeyClaims(alarm.Id);Check.Equal(1,keys.Count);Check.Equal(2,keys[0].SettlementId);
                Check.Equal(20,keys[0].BuildingKey);var copy=new WorldStateStore(1);copy.Restore(store.Snapshot());
                Check.True(!copy.NativePeopleFor(1).Find(p=>p.Id==person.Id).IsAlive);
                Check.True(copy.TryReserveInnLodgerHome(person.Id,"bedroom",3,new HomeProfile(30,2),200)!=null);
                Check.True(copy.CurrentPopulation(3)==null);
            }));
        }
    }
}
