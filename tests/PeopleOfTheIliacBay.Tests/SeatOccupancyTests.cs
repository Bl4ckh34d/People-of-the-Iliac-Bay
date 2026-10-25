using System;
using System.Collections.Generic;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class SeatOccupancyTests
    {
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Shared table seats require one table and nearby seats facing toward each other",()=>{
                var a=new TableSeatPosition{Id="a",TableId="table-1",X=-1,FacingX=1};
                var b=new TableSeatPosition{Id="b",TableId="table-1",X=1,FacingX=-1};
                Check.True(SharedTableSeats.CanShare(a,b));
                b.TableId="table-2";Check.True(!SharedTableSeats.CanShare(a,b));
                b.TableId="table-1";b.FacingX=1;Check.True(!SharedTableSeats.CanShare(a,b));
                b.FacingX=-1;b.X=10;Check.True(!SharedTableSeats.CanShare(a,b));
                b.X=-0.9;Check.True(!SharedTableSeats.CanShare(a,b));
                b.X=1;b.Height=1;Check.True(!SharedTableSeats.CanShare(a,b));
                b.Height=0;b.Id="a";Check.True(!SharedTableSeats.CanShare(a,b));
                b.Id="b";b.X=double.NaN;Check.True(!SharedTableSeats.CanShare(a,b));
                b.X=1;a.TableId=b.TableId=null;Check.True(!SharedTableSeats.CanShare(a,b));
            }));
            cases.Add(("Adjacent table seats need compatible facing even when direction vectors have different lengths",()=>{
                var a=new TableSeatPosition{Id="a",TableId="t",X=-1,FacingX=2};
                var b=new TableSeatPosition{Id="b",TableId="t",Z=1,FacingZ=-0.5};
                Check.True(SharedTableSeats.CanShare(a,b));
                b.FacingZ=0;Check.True(!SharedTableSeats.CanShare(a,b));
            }));
            cases.Add(("Pair seat claims are all or none and preserve old seats on conflict",()=>{
                var seats=new SeatOccupancy(new[]{"old-a","old-b","new-a","new-b"});
                Check.True(seats.TryClaim("one","old-a",10));Check.True(seats.TryClaim("two","old-b",10));
                Check.True(seats.TryClaim("stranger","new-b",10));
                Check.True(!seats.TryClaimPair("one","new-a","two","new-b",11));
                Check.True(seats.Owns("one","old-a",11));Check.True(seats.Owns("two","old-b",11));
                Check.True(seats.Available("stranger","new-a",11));
                seats.Release("stranger");
                Check.True(seats.TryClaimPair("one","new-a","two","new-b",11));
                Check.True(!seats.Owns("one","old-a",11));Check.True(!seats.Owns("two","old-b",11));
                Check.True(!seats.TryClaim("stranger","new-a",12));Check.True(!seats.TryClaim("stranger","new-b",12));
                Check.True(seats.TryClaimPair("one","new-a","two","new-b",12));
                Check.True(seats.Owns("one","new-a",14));Check.True(seats.Owns("two","new-b",14));
                Check.True(seats.TryClaim("stranger","new-b",14.01));
            }));
            cases.Add(("Pair seat claims require two distinct owners and slots without losing leases",()=>{
                var seats=new SeatOccupancy(new[]{"a","b"});Check.True(seats.TryClaim("one","a",1));
                Check.True(!seats.TryClaimPair("one","a","one","b",2));
                Check.True(!seats.TryClaimPair("one","b","two","b",2));
                Check.True(!seats.TryClaimPair("one","b","two","missing",2));
                Check.True(seats.Owns("one","a",2));Check.True(seats.Available("two","b",2));
                Check.Throws<ArgumentException>(()=>seats.TryClaimPair("one","a","","b",2));
                Check.Throws<ArgumentException>(()=>seats.TryClaimPair("one","a","two","b",double.NaN));
                Check.True(seats.Owns("one","a",2));
            }));
            cases.Add(("Pair seat claims cannot exchange seats still held by the other member",()=>{
                var seats=new SeatOccupancy(new[]{"a","b"});seats.TryClaim("one","a",1);seats.TryClaim("two","b",1);
                Check.True(!seats.TryClaimPair("one","b","two","a",2));
                Check.True(seats.Owns("one","a",2));Check.True(seats.Owns("two","b",2));
            }));
            cases.Add(("Bench slots fit verified length with exclusive personal space",()=>{
                var slots=SeatOccupancy.Offsets(2.6,true);
                Check.Equal(3,slots.Count);Check.Near(-0.8,slots[0]);Check.Near(0,slots[1]);Check.Near(0.8,slots[2]);
                Check.Equal(1,SeatOccupancy.Offsets(2.6,false).Count);
                Check.Equal(0,SeatOccupancy.Offsets(0.3,true).Count);
            }));
            cases.Add(("Seat claim holds exact slot until release or interrupted lease expires",()=>{
                var seats=new SeatOccupancy(new[]{"bench/0","bench/1"});
                Check.True(seats.TryClaim("a","bench/0",10));
                Check.True(!seats.TryClaim("b","bench/0",10));
                Check.True(seats.TryClaim("b","bench/1",10));
                Check.True(!seats.TryClaim("a","missing",11));
                Check.True(seats.Owns("a","bench/0",11));
                seats.Release("a");Check.True(seats.TryClaim("c","bench/0",11));
                Check.True(!seats.Owns("c","bench/0",14));
                Check.True(seats.TryClaim("a","bench/0",14));
            }));
            cases.Add(("Failed seat reassignment never releases current occupied seat",()=>{
                var seats=new SeatOccupancy(new[]{"a","b"});seats.TryClaim("one","a",1);seats.TryClaim("two","b",1);
                Check.True(!seats.TryClaim("one","b",2));Check.True(seats.Owns("one","a",2));
                seats.Release("two");Check.True(seats.TryClaim("one","b",2));Check.True(!seats.Owns("one","a",2));
            }));
            cases.Add(("Offering seat options does not change an existing reservation",()=>{
                var seats=new SeatOccupancy(new[]{"a","b"});seats.TryClaim("one","a",10);
                Check.True(seats.Available("one","b",11));Check.True(!seats.Available("two","a",11));
                Check.True(seats.Owns("one","a",11));Check.True(!seats.Available("one","missing",11));
            }));
            cases.Add(("Observed seat intent survives serialization clipping and journey resume",()=>{
                var saved=new ObservedVisit {ResidentId="a",SettlementId=1,BuildingKey=2,ObservedAt=10,PlannedEndAt=100,ResumeUntil=100,SecondsPerMetre=1,
                    Legs=new List<ObservedVisitLeg>{new ObservedVisitLeg {Route=new List<GuardPoint3>{new GuardPoint3(),new GuardPoint3{X=2}},DwellSeconds=20,AfterPhase=ShopVisitPhase.Browsing,SeatId="model:41105:3/1"},
                        new ObservedVisitLeg {Route=new List<GuardPoint3>{new GuardPoint3{X=2},new GuardPoint3()},AfterPhase=ShopVisitPhase.Complete}}};
                var options=new JsonSerializerOptions {IncludeFields=true};
                var copy=ObservedVisit.Copy(JsonSerializer.Deserialize<ObservedVisit>(JsonSerializer.Serialize(saved,options),options));
                Check.Equal("model:41105:3/1",copy.RemainingLegs(11)[0].SeatId);
                Check.Equal("model:41105:3/1",copy.RemainingLegs(15)[0].SeatId);
                Check.Equal("model:41105:3/1",ShopVisitJourney.Resume(copy,15).SeatId(0));
            }));
        }
    }
}
