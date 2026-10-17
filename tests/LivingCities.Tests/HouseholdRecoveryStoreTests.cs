using System;
using System.Collections.Generic;
using System.Text.Json;
using LivingCities.Core;
namespace LivingCities.Tests
{
    internal static class HouseholdRecoveryStoreTests
    {
        public static void Add(List<(string Name,Action Body)> tests)
        {
            tests.Add(("Extinct household receives new identities only after cleanup and deadline", Replacement));
            tests.Add(("Vacant bank listing cannot also generate a family", Bank));
            tests.Add(("Living absent owner and missing death evidence prevent recovery", Evidence));
            tests.Add(("Unassigned native flat occupant blocks while alive and recovers after confirmed cleanup", NativeFlatOccupant));
            tests.Add(("Recovery decision survives reload without rerolling outcome", Saved));
        }
        private static HouseholdRecoveryEligibility Facts()=>new HouseholdRecoveryEligibility {BedCount=3};
        private static WorldStateStore Fixture(bool leaveAlive=false,bool confirmed=true)
        {
            var store=new WorldStateStore(42);
            var town=store.GetOrCreateFamilies(3,new[]{new HomeProfile(17,3,220)});
            for(int i=0;i<town.Households[0].Residents.Count;i++)
            {
                var person=town.Households[0].Residents[i];
                if(leaveAlive && i==0) continue;
                var incident=store.RecordViolence(3,person.Id,100+i);
                store.LocateViolence(incident.Id,17,0,0,0);
                person.IsAlive=false;
                if(confirmed) store.ConfirmCorpse(incident.Id,100+i);
            }
            return store;
        }
        private static HouseholdRecoveryReservation Reserve(WorldStateStore store,HouseholdVacancyOutcome outcome=HouseholdVacancyOutcome.NewFamily)
            =>store.EnsureHouseholdRecovery(3,17,HouseholdRecoveryTier.DenseCity,Facts(),outcome);
        private static bool Advance(WorldStateStore store,double now,bool outside=true)
            =>store.AdvanceHouseholdRecovery(3,17,now,new HomeProfile(17,3,220),Facts(),outside);
        private static void Replacement()
        {
            var store=Fixture();var item=Reserve(store);string old=store.CurrentPopulation(3).Households[0].Residents[0].Id;
            var incident=store.RecordViolence(3,old,100);
            Check.True(!Advance(store,item.RecoveryAt));
            store.ObserveCorpses(3,-1,item.RecoveryAt);
            Check.True(!Advance(store,item.RecoveryAt-1));
            Check.True(!Advance(store,item.RecoveryAt,false));
            Check.True(Advance(store,item.RecoveryAt));
            Check.True(!Advance(store,item.RecoveryAt+1));
            Check.Equal(1,store.CurrentPopulation(3).Households[0].Generation);
            Check.True(store.CurrentPopulation(3).Households[0].Residents[0].Id!=old);
            Check.True(!store.ResolveResident(old).IsAlive);
            Check.True(object.ReferenceEquals(incident,store.RecordViolence(3,old,item.RecoveryAt)),"Replacement invalidated live incident reference.");
            var saved=store.Snapshot();store.Restore(saved);
            Check.Equal(HouseholdRecoveryStatus.Settled,store.HouseholdRecoveryReservations(3)[0].Status);
        }
        private static void Bank()
        {
            var store=Fixture();var item=Reserve(store,HouseholdVacancyOutcome.BankSale);
            store.ObserveCorpses(3,-1,item.RecoveryAt);
            Check.True(!Advance(store,item.RecoveryAt));
            Check.Equal(HouseholdRecoveryStatus.Listed,store.HouseholdRecoveryReservations(3)[0].Status);
            Check.True(store.MarkVacantHouseSold(3,17,item.RecoveryAt));
            Check.True(!Advance(store,item.RecoveryAt+1000000));
            Check.Equal(0,store.CurrentPopulation(3).Households[0].Generation);
            Check.Equal(HouseholdRecoveryStatus.Sold,Reserve(store).Status);
            store.Restore(store.Snapshot());
            Check.Equal(HouseholdRecoveryStatus.Sold,store.HouseholdRecoveryReservations(3)[0].Status);
        }
        private static void Evidence()
        {
            Check.Equal(null,Reserve(Fixture(true)));
            Check.Equal(null,Reserve(Fixture(false,false)));
        }
        private static void NativeFlatOccupant()
        {
            var occupied=Fixture();
            var living=occupied.RegisterNativePerson(3,17,901,"Flat Resident",ResidentSex.Female,1,0,184,1);
            Check.Equal(null,Reserve(occupied));

            var vacant=Fixture();
            var native=vacant.RegisterNativePerson(3,17,902,"Flat Resident",ResidentSex.Female,1,0,184,1);
            var incident=vacant.RecordViolence(3,native.Id,200);
            vacant.LocateViolence(incident.Id,17,0,0,0);
            native.IsAlive=false;
            vacant.ConfirmCorpse(incident.Id,200);
            var reservation=Reserve(vacant);
            Check.True(reservation!=null);
            Check.Equal(200d,reservation.ConfirmedLastDeathAt);
            var invalid=vacant.Snapshot();
            invalid.HouseholdRecoveries[0].ConfirmedLastDeathAt-=1;
            Check.Throws<ArgumentException>(()=>vacant.Restore(invalid));
            vacant.Restore(vacant.Snapshot());
            Check.True(!Advance(vacant,reservation.RecoveryAt));
            vacant.ObserveCorpses(3,-1,reservation.RecoveryAt);
            Check.True(Advance(vacant,reservation.RecoveryAt));
        }
        private static void Saved()
        {
            var store=Fixture();var first=Reserve(store,HouseholdVacancyOutcome.BankSale);
            var json=new JsonSerializerOptions {IncludeFields=true};
            store.Restore(JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(store.Snapshot(),json),json));
            var second=store.EnsureHouseholdRecovery(3,17,HouseholdRecoveryTier.IsolatedHamlet,Facts(),HouseholdVacancyOutcome.NewFamily);
            Check.Equal(first.RecoveryAt,second.RecoveryAt);Check.Equal(first.Outcome,second.Outcome);
            var invalid=store.Snapshot();invalid.HouseholdRecoveries[0].ConfirmedLastDeathAt+=1;
            Check.Throws<ArgumentException>(()=>store.Restore(invalid));
            Check.Equal(first.RecoveryAt,store.HouseholdRecoveryReservations(3)[0].RecoveryAt);
        }
    }
}
