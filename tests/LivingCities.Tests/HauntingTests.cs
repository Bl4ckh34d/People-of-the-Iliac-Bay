using System;
using System.Collections.Generic;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class HauntingTests
    {
        internal static void Add(List<(string, Action)> cases)
        {
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
