using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class LodgingTests
    {
        private static WorldStateStore Store()
        {
            var store=new WorldStateStore(42);store.GetOrCreate(1,new int[0]);
            store.RegisterLodgingBeds(1,7,new[]{
                new LodgingBed {Id="bed:0"},new LodgingBed {Id="bed:1"},new LodgingBed {Id="bed:2"}});
            return store;
        }
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Each physical bed remains independently bookable",()=>{
                var store=Store();
                Check.True(store.TryBookLodging(1,7,"first","first",2,false,1,100,200,"bed:0"));
                Check.True(store.TryBookLodging(1,7,"second","second",3,false,1,100,200,"bed:1"));
                var beds=store.AvailableLodgingBeds(1,7,1,100,200);
                Check.Equal(1,beds.Count);Check.Equal("bed:2",beds[0]);
                Check.Equal("bed:0",store.Snapshot().Lodgings[0].Bookings[0].BedIds[0]);
                Check.Equal("bed:1",store.Snapshot().Lodgings[0].Bookings[1].BedIds[0]);
            }));
            cases.Add(("Party receives any requested number of free beds",()=>{
                var store=Store();
                Check.True(store.TryBookLodging(1,7,"family","family",2,false,2,100,200));
                var booking=store.Snapshot().Lodgings[0].Bookings[0];
                Check.Equal(2,booking.BedIds.Count);Check.Equal("bed:0",booking.BedIds[0]);Check.Equal("bed:1",booking.BedIds[1]);
                Check.True(store.TryBookLodging(1,7,"other","other",3,false,1,100,200,"bed:2"));
                Check.True(!store.TryBookLodging(1,7,"full","full",4,false,1,100,200));
            }));
            cases.Add(("Native occupant blocks only exact bed",()=>{
                var store=Store();
                var person=store.RegisterNativePerson(1,7,42,"Occupant",ResidentSex.Male,1,0,182,0);
                Check.True(store.TryClaimLodgingBed(1,7,person.Id,"bed:0",100));
                Check.Equal(2,store.AvailableLodgingBeds(1,7,2,100,200).Count);
                Check.True(store.TryBookLodging(1,7,"family","family",2,false,2,100,200));
                Check.True(store.IsLodgingBedReserved(1,7,"bed:0",150,"family"));
                Check.True(!store.IsLodgingBedReserved(1,7,"bed:1",150,"family"));
            }));
            cases.Add(("Cleaned corpse releases only deceased occupant bed",()=>{
                var store=Store();
                var first=store.RegisterNativePerson(1,7,51,"First",ResidentSex.Male,1,0,182,0);
                var second=store.RegisterNativePerson(1,7,52,"Second",ResidentSex.Female,1,0,182,1);
                store.TryClaimLodgingBed(1,7,first.Id,"bed:0",100);store.TryClaimLodgingBed(1,7,second.Id,"bed:1",100);
                var alarm=store.RecordViolence(1,first.Id,110);store.LocateViolence(alarm.Id,7,0,0,0);
                first.IsAlive=false;store.ConfirmCorpse(alarm.Id,111);store.ObserveCorpses(1,-1,86513);
                Check.True(store.NativeLodgingBed(first.Id)==null);Check.Equal("bed:1",store.NativeLodgingBed(second.Id));
                Check.True(store.TryBookLodging(1,7,"released","player",1,true,1,86513,87000,"bed:0"));
                Check.True(!store.TryBookLodging(1,7,"occupied","other",2,false,1,86513,87000,"bed:1"));
            }));
            cases.Add(("Registration can add beds but cannot erase known beds",()=>{
                var store=Store();var expanded=store.Snapshot().Lodgings[0].Beds;
                expanded.Add(new LodgingBed {Id="bed:3"});store.RegisterLodgingBeds(1,7,expanded);
                Check.Equal(4,store.Snapshot().Lodgings[0].Beds.Count);
                Check.Throws<ArgumentException>(()=>store.RegisterLodgingBeds(1,7,new[]{new LodgingBed {Id="bed:0"}}));
                Check.Equal(4,store.Snapshot().Lodgings[0].Beds.Count);
            }));
            cases.Add(("Native paid receipt remains tied to exact bed",()=>{
                var store=Store();
                Check.True(store.ReconcilePlayerLodging(1,7,"bed:2",100,200));
                var original=store.Snapshot().Lodgings[0].Bookings[0];
                Check.True(store.ReconcilePlayerLodging(1,7,"bed:2",125,200));
                Check.True(store.ReconcilePlayerLodging(1,7,"bed:2",150,300));
                Check.True(!store.ReconcilePlayerLodging(1,7,"bed:1",175,300));
                var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
                var booking=restored.Snapshot().Lodgings[0].Bookings[0];
                Check.Equal(original.Id,booking.Id);Check.Equal("bed:2",booking.BedIds[0]);
                Check.Equal(100d,booking.StartsAt);Check.Equal(300d,booking.EndsAt);
            }));
            cases.Add(("Player may retain paid beds in different Inns but not overlap in one Inn",()=>{
                var store=Store();store.RegisterLodgingBeds(1,8,new[]{new LodgingBed {Id="other:0"}});
                Check.True(store.TryBookLodging(1,7,"first","player",1,true,1,100,200,"bed:0"));
                Check.True(store.TryBookLodging(1,8,"second","player",1,true,1,150,300,"other:0"));
                Check.True(!store.TryBookLodging(1,7,"duplicate","player",1,true,1,150,250,"bed:1"));
                Check.True(store.TryExtendLodging("first",250));
                var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
                Check.Equal(250d,restored.Snapshot().Lodgings[0].Bookings[0].EndsAt);
            }));
            cases.Add(("Travelers cannot overlap Inns or change guest role",()=>{
                var store=Store();store.RegisterLodgingBeds(1,8,new[]{new LodgingBed {Id="other:0"}});
                Check.True(store.TryBookLodging(1,7,"first","guest",2,false,1,100,200));
                Check.True(!store.TryBookLodging(1,8,"overlap","guest",2,false,1,150,250));
                Check.Throws<ArgumentException>(()=>store.TryBookLodging(1,8,"changed","guest",2,true,1,200,300));
                Check.True(store.TryBookLodging(1,8,"next","guest",2,false,1,200,300));
            }));
            cases.Add(("Earliest vacancy checks simultaneous free beds",()=>{
                var store=Store();
                store.TryBookLodging(1,7,"a","a",2,false,2,100,200);
                store.TryBookLodging(1,7,"b","b",2,false,2,220,300);
                Check.Equal(200d,store.EarliestLodgingStart(1,7,2,100,20).Value);
                Check.Equal(300d,store.EarliestLodgingStart(1,7,2,100,21).Value);
                Check.Equal(100d,store.EarliestLodgingStart(1,7,1,100,20).Value);
            }));
            cases.Add(("Extension checks only booked beds",()=>{
                var store=Store();
                store.TryBookLodging(1,7,"player","player",1,true,1,100,200,"bed:0");
                store.TryBookLodging(1,7,"next","next",2,false,1,210,300,"bed:0");
                store.TryBookLodging(1,7,"other","other",3,false,1,150,400,"bed:1");
                Check.True(!store.TryExtendLodging("player",220));
                Check.True(store.TryExtendLodging("player",210));
                Check.Equal(210d,store.Snapshot().Lodgings[0].Bookings[0].EndsAt);
            }));
            cases.Add(("Restore rejects duplicate bed assignment atomically",()=>{
                var store=Store();store.TryBookLodging(1,7,"a","a",2,false,1,100,200,"bed:0");
                var malformed=store.Snapshot();malformed.Lodgings[0].Bookings.Add(new LodgingBooking {
                    Id="b",GuestId="b",HomeSettlement=3,PartySize=1,BedIds=new List<string>{"bed:0"},StartsAt=100,EndsAt=200});
                Check.Throws<ArgumentException>(()=>store.Restore(malformed));
                Check.Equal(1,store.Snapshot().Lodgings[0].Bookings.Count);
            }));
        }
    }
}
