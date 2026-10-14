using System;
using System.Collections.Generic;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class HauntingTests
    {
        internal static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Single-name cultures get a stable shared family name",()=>{
                var home=new Household { SurnameSeed=42 };
                home.Residents.Add(new Resident { AppearanceSeed=1 });
                home.Residents.Add(new Resident { AppearanceSeed=2 });
                RegionalIdentity.Initialize(home,2,(seed,sex)=>"Name"+seed,seed=>string.Empty);
                Check.Equal("Name42",home.Surname);
                Check.Equal("Name1 Name42",home.Residents[0].FullName);
                Check.Equal("Name2 Name42",home.Residents[1].FullName);
                RegionalIdentity.Initialize(home,2,(seed,sex)=>"Changed",seed=>string.Empty);
                Check.Equal("Name1 Name42",home.Residents[0].FullName);
            }));
            cases.Add(("Regional identity persists shared surname and preserves existing names and race",()=>{
                var home=new Household { SurnameSeed=42 };
                home.Residents.Add(new Resident { Id="a",AppearanceSeed=1 });
                home.Residents.Add(new Resident { Id="b",AppearanceSeed=2,FullName="Existing Name",Race=4 });
                RegionalIdentity.Initialize(home,2,(seed,sex)=>"Given"+seed,seed=>"Family"+seed);
                Check.Equal("Given1 Family42",home.Residents[0].FullName);
                Check.Equal(2,home.Residents[0].Race); Check.Equal(4,home.Residents[1].Race);
                Check.Equal("Existing Name",home.Residents[1].FullName);
                RegionalIdentity.Initialize(home,3,(seed,sex)=>"Changed",seed=>"Other");
                Check.Equal("Given1 Family42",home.Residents[0].FullName); Check.Equal(2,home.Residents[0].Race);
            }));
            cases.Add(("Settlement scale follows location category rather than named city IDs",()=>{
                Check.Equal(SettlementScale.City,SettlementClassification.FromLocationType(0));
                Check.Equal(SettlementScale.Town,SettlementClassification.FromLocationType(1));
                Check.Equal(SettlementScale.Hamlet,SettlementClassification.FromLocationType(2));
                Check.Equal(16,GuardRoster.TargetCount(SettlementClassification.FromLocationType(0),10));
                Check.Equal(8,GuardRoster.TargetCount(SettlementClassification.FromLocationType(1),10));
                Check.Equal(1,GuardRoster.TargetCount(SettlementClassification.FromLocationType(2),10));
                foreach(int other in new[] { -1,3,4,5,6,7,8,9,10,11,12,13,14,65535 })
                    Check.Throws<ArgumentOutOfRangeException>(()=>SettlementClassification.FromLocationType(other));
            }));
            cases.Add(("Legacy saves default daily haunting policy and invalid saved days reject transactionally",()=>{
                var store=new WorldStateStore(42); store.Restore(new WorldState { WorldSeed=42,HauntingDay=null });
                Check.True(store.RestrictHauntingRoutines(1291010263,false,600));
                var bad=store.Snapshot(); bad.HauntingDay.Day=0.5;
                Check.Throws<ArgumentException>(()=>store.Restore(bad));
                Check.True(store.RestrictHauntingRoutines(1291010263,true,1200));
            }));
            cases.Add(("Civilian haunting policy survives saves and changes only with the next day",()=>{
                var store=new WorldStateStore(42);
                Check.True(store.RestrictHauntingRoutines(1291010263,false,600));
                Check.True(store.RestrictHauntingRoutines(1291010263,true,1200));
                var restored=new WorldStateStore(0); restored.Restore(store.Snapshot());
                Check.True(restored.RestrictHauntingRoutines(1291010263,true,1800));
                Check.True(!restored.RestrictHauntingRoutines(1291010263,true,86400));
                Check.True(!restored.RestrictHauntingRoutines(206728581,false,86400));
                Check.Throws<ArgumentOutOfRangeException>(()=>restored.RestrictHauntingRoutines(1291010263,false,double.NaN));
            }));
            cases.Add(("Unloaded refuge release uses first dawn even when revisiting on a later haunted night",()=>{
                Check.True(!SettlementHaunting.FirstSafeTime(1291010263,false,20*3600,86400+5*3600-0.01).HasValue);
                Check.Near(86400+5*3600,SettlementHaunting.FirstSafeTime(1291010263,false,20*3600,86400+5*3600).Value);
                Check.Near(86400+5*3600,SettlementHaunting.FirstSafeTime(1291010263,false,20*3600,3*86400+22*3600).Value);
                Check.Near(5*3600,SettlementHaunting.FirstSafeTime(1291010263,false,2*3600,22*3600).Value);
            }));
            cases.Add(("Curse resolution release uses observation time unless an earlier dawn is known",()=>{
                Check.Near(21*3600,SettlementHaunting.FirstSafeTime(1291010263,true,20*3600,21*3600).Value);
                Check.Near(86400+5*3600,SettlementHaunting.FirstSafeTime(1291010263,true,20*3600,3*86400).Value);
                Check.Throws<ArgumentOutOfRangeException>(()=>SettlementHaunting.FirstSafeTime(1291010263,false,200,100));
            }));
            cases.Add(("Daggerfall haunting changes at exact 19:00 and 05:00 boundaries", () => {
                const int city = 1291010263;
                Check.True(!SettlementHaunting.StreetsDangerous(city, false, 19*3600-0.01));
                Check.True(SettlementHaunting.StreetsDangerous(city, false, 19*3600));
                Check.True(SettlementHaunting.StreetsDangerous(city, false, 86400+5*3600-0.01));
                Check.True(!SettlementHaunting.StreetsDangerous(city, false, 86400+5*3600));
                Check.True(SettlementHaunting.StreetsDangerous(city, false, 123456*86400d));
            }));
            cases.Add(("Curse resolution immediately restores Daggerfall streets without affecting other settlements", () => {
                Check.True(!SettlementHaunting.StreetsDangerous(1291010263, true, 23*3600));
                Check.True(!SettlementHaunting.StreetsDangerous(206728581, false, 23*3600));
                Check.True(SettlementHaunting.StreetsDangerous(1291010263, false, 23*3600));
            }));
            cases.Add(("Haunting rejects invalid game timestamps", () => {
                foreach (double value in new[] { -1, double.NaN, double.PositiveInfinity })
                    Check.Throws<ArgumentOutOfRangeException>(() => SettlementHaunting.StreetsDangerous(1291010263, false, value));
            }));
        }
    }
}
