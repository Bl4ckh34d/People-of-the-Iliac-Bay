using System;
using System.Collections.Generic;
using LivingCities.Core;
namespace LivingCities.Tests
{
    internal static class GuardRefugeTests
    {
        private static GuardRefugeJourney Plan() { return GuardRefugeJourney.Create(7,
            new[] { new GuardPoint3(),new GuardPoint3 { X=10 } },
            new[] { new GuardPoint3(),new GuardPoint3 { Z=4 } },100,2,12); }
        internal static void Add(List<(string,Action)> cases)
        {
            cases.Add(("Refuge destination reservations reject overlapping guards after copying",()=>{
                var roster=GuardRoster.Create(1,42,3,new[] { new SavedRoutePoint() });
                roster.TakeRefuge(roster.Guards[0].Id,Plan(),100);
                roster=GuardRoster.CopyValidated(roster);
                var overlap=Plan(); overlap.Interior[1].X=0.5;
                Check.Throws<ArgumentException>(()=>roster.TakeRefuge(roster.Guards[1].Id,overlap,100));
                Check.Equal(GuardDuty.Posted,roster.Guards[1].Duty);
                var separate=Plan(); separate.Interior[1].X=1;
                roster.TakeRefuge(roster.Guards[1].Id,separate,100);
                var otherBuilding=Plan(); otherBuilding.BuildingKey=8;
                roster.TakeRefuge(roster.Guards[2].Id,otherBuilding,100);
            }));
            cases.Add(("Refuge reservation remains during exit but frees after the guard reaches the exterior",()=>{
                var roster=GuardRoster.Create(1,42,2,new[] { new SavedRoutePoint() });
                roster.TakeRefuge(roster.Guards[0].Id,Plan(),100); roster.Guards[0].Refuge.Release(200);
                var later=Plan(); later.StartsAt=212;
                Check.Throws<ArgumentException>(()=>roster.TakeRefuge(roster.Guards[1].Id,later,212));
                later.StartsAt=224; roster.TakeRefuge(roster.Guards[1].Id,later,224);
            }));
            cases.Add(("Refuge indoor corpse blocks its actual position while exterior death frees reservation",()=>{
                var roster=GuardRoster.Create(1,42,2,new[] { new SavedRoutePoint() });
                roster.TakeRefuge(roster.Guards[0].Id,Plan(),100); roster.Kill(roster.Guards[0].Id,200);
                var later=Plan(); later.StartsAt=201;
                Check.Throws<ArgumentException>(()=>roster.TakeRefuge(roster.Guards[1].Id,later,201));
                roster=GuardRoster.Create(1,42,2,new[] { new SavedRoutePoint() });
                roster.TakeRefuge(roster.Guards[0].Id,Plan(),100); roster.Kill(roster.Guards[0].Id,130);
                roster.TakeRefuge(roster.Guards[1].Id,later,201);
            }));
            cases.Add(("Refuge roster state survives copying and blocks dispatch until physical return",()=>{
                var roster=GuardRoster.Create(1,42,1,new[] { new SavedRoutePoint() }); var id=roster.Guards[0].Id;
                roster.TakeRefuge(id,Plan(),100);
                Check.Near(5,roster.Position(id,130).X);
                var copy=GuardRoster.CopyValidated(roster);
                Check.Equal(0,copy.Dispatch("crime",130,new Dictionary<string,List<SavedRoutePoint>> {
                    { id,new List<SavedRoutePoint> { new SavedRoutePoint { X=5 },new SavedRoutePoint { X=20 } } }
                },1,2,12).Count);
                Check.Equal(RefugePhase.Sheltered,copy.Guards[0].Refuge.Evaluate(200).Phase);
                copy.Guards[0].Refuge.Release(200); copy.Advance(283);
                Check.Equal(GuardDuty.Refuge,copy.Guards[0].Duty);
                copy.Advance(284); Check.Equal(GuardDuty.Posted,copy.Guards[0].Duty);
                Check.True(copy.Guards[0].Refuge==null); Check.Near(0,copy.Position(id,284).X);
            }));
            cases.Add(("Refuge deaths retain journey and never revive or resume travel",()=>{
                var roster=GuardRoster.Create(1,42,1,new[] { new SavedRoutePoint() }); var id=roster.Guards[0].Id;
                roster.TakeRefuge(id,Plan(),100); roster.Kill(id,130);
                var copy=GuardRoster.CopyValidated(roster); copy.Advance(99999);
                Check.True(!copy.Guards[0].IsAlive); Check.Near(5,copy.Position(id,99999).X);
                Check.True(copy.Guards[0].Refuge!=null);
            }));
            cases.Add(("Refuge admission rejects wrong route origin without mutating guard",()=>{
                var roster=GuardRoster.Create(1,42,1,new[] { new SavedRoutePoint { X=50 } });
                Check.Throws<ArgumentException>(()=>roster.TakeRefuge(roster.Guards[0].Id,Plan(),100));
                Check.Equal(GuardDuty.Posted,roster.Guards[0].Duty); Check.True(roster.Guards[0].Refuge==null);
            }));
        }
    }
}
