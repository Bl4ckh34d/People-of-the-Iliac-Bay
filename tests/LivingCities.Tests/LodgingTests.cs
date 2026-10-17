using System;
using System.Collections.Generic;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class LodgingTests
    {
        private static WorldStateStore Store()
        {
            var store=new WorldStateStore(42); store.GetOrCreate(1,new int[0]);
            store.RegisterLodging(1,7,new[] {
                new LodgingRoom { Id="double",Beds=new List<string> { "bed:0","bed:1" } },
                new LodgingRoom { Id="single",Beds=new List<string> { "bed:2" } } });
            return store;
        }
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Cleaning one inn corpse preserves surviving roommate room ownership",()=>{
                var store=Store();
                var a=store.RegisterNativePerson(1,7,51,"First",ResidentSex.Male,1,0,182,0);
                var b=store.RegisterNativePerson(1,7,52,"Second",ResidentSex.Female,1,0,182,1);
                store.TryClaimLodgingBed(1,7,a.Id,"bed:0",100);
                store.TryClaimLodgingBed(1,7,b.Id,"bed:1",100);
                var alarm=store.RecordViolence(1,a.Id,110); store.LocateViolence(alarm.Id,7,0,0,0);
                a.IsAlive=false; store.ConfirmCorpse(alarm.Id,111); store.ObserveCorpses(1,-1,86513);
                Check.True(store.NativeLodgingBed(a.Id)==null);
                Check.Equal("bed:1",store.NativeLodgingBed(b.Id));
                Check.True(!store.TryBookLodging(1,7,"blocked","player",1,true,1,86513,87000,"double"));
            }));
            cases.Add(("Inn occupant death keeps room claimed until corpse cleanup then releases it",()=>{
                var store=Store();
                var person=store.RegisterNativePerson(1,7,46,"Guest",ResidentSex.Male,1,0,182,0);
                store.TryClaimLodgingBed(1,7,person.Id,"bed:0",100);
                var alarm=store.RecordViolence(1,person.Id,110);
                store.LocateViolence(alarm.Id,7,0,0,0); person.IsAlive=false;
                store.ConfirmCorpse(alarm.Id,111);
                store.ObserveCorpses(1,7,86512);
                Check.Equal("bed:0",store.NativeLodgingBed(person.Id));
                Check.True(!store.TryBookLodging(1,7,"early","player",1,true,1,86512,87000,"double"));
                store.ObserveCorpses(1,-1,86513);
                Check.True(store.NativeLodgingBed(person.Id)==null);
                Check.True(store.TryBookLodging(1,7,"after-cleanup","player",1,true,1,86513,87000,"double"));
                var restored=new WorldStateStore(0); restored.Restore(store.Snapshot());
                Check.True(restored.NativeLodgingBed(person.Id)==null);
                Check.True(!restored.NativePeopleFor(1).Find(p=>p.Id==person.Id).IsAlive);
            }));
            cases.Add(("Non-rental inn bedrooms accept occupants but never rental offers",()=>{
                var store=Store();
                var all=store.Snapshot().Lodgings[0].Rooms;
                all.Add(new LodgingRoom {Id="staff",Beds=new List<string>{"bed:staff"},NonRentable=true});
                store.RegisterLodging(1,7,all);
                var person=store.RegisterNativePerson(1,7,45,"Resident",ResidentSex.Male,1,0,182,0);
                Check.True(!store.TryBookLodging(1,7,"bad","player",1,true,1,100,200,"staff"));
                Check.True(store.TryClaimLodgingBed(1,7,person.Id,"bed:staff",100));
                var restored=new WorldStateStore(0); restored.Restore(store.Snapshot());
                Check.True(restored.Snapshot().Lodgings[0].Rooms.Find(r=>r.Id=="staff").NonRentable);
                Check.Equal("bed:staff",restored.NativeLodgingBed(person.Id));
                Check.Equal("double",restored.AvailableLodgingRoom(1,7,1,100,200));
                var unsafeLayout=restored.Snapshot().Lodgings[0].Rooms;
                unsafeLayout.Find(r=>r.Id=="staff").NonRentable=false;
                Check.Throws<ArgumentException>(()=>restored.RegisterLodging(1,7,unsafeLayout));
            }));
            cases.Add(("Schema nine lodging gains empty occupant claims without losing paid rooms",()=>{
                var store=Store(); store.TryBookLodging(1,7,"paid","player",1,true,1,100,200,"single");
                var old=store.Snapshot(); old.Version=9; old.Lodgings[0].Occupants=null;
                var restored=new WorldStateStore(0); restored.Restore(old);
                Check.Equal(new WorldState().Version,restored.Snapshot().Version);
                Check.Equal(0,restored.Snapshot().Lodgings[0].Occupants.Count);
                Check.Equal("paid",restored.Snapshot().Lodgings[0].Bookings[0].Id);
            }));
            cases.Add(("Native inn occupants exclude private rooms from offers rental and rest after reload",()=>{
                var store=Store();
                var person=store.RegisterNativePerson(1,7,42,"Room Occupant",ResidentSex.Male,1,0,182,0);
                Check.True(store.TryClaimLodgingBed(1,7,person.Id,"bed:0",100));
                Check.True(store.TryClaimLodgingBed(1,7,person.Id,"bed:0",120));
                Check.Equal("single",store.AvailableLodgingRoom(1,7,1,100,200));
                Check.True(!store.TryBookLodging(1,7,"blocked","player",1,true,1,100,200,"double"));
                Check.True(store.IsLodgingBedReserved(1,7,"bed:1",100,"player"));
                Check.True(store.EarliestLodgingStart(1,7,2,100,100)==null);
                var restored=new WorldStateStore(0); restored.Restore(store.Snapshot());
                Check.True(restored.IsLodgingBedReserved(1,7,"bed:0",200,"player"));
                Check.Equal("bed:0",restored.NativeLodgingBed(person.Id));
            }));
            cases.Add(("Native room claims preserve paid bookings and reject double bed ownership",()=>{
                var store=Store();
                var a=store.RegisterNativePerson(1,7,41,"First",ResidentSex.Male,1,0,182,0);
                var b=store.RegisterNativePerson(1,7,42,"Second",ResidentSex.Female,1,0,182,1);
                store.TryBookLodging(1,7,"paid","player",1,true,1,100,200,"single");
                Check.True(!store.TryClaimLodgingBed(1,7,a.Id,"bed:2",150));
                Check.True(store.TryClaimLodgingBed(1,7,a.Id,"bed:2",200));
                Check.True(!store.TryClaimLodgingBed(1,7,b.Id,"bed:2",200));
                Check.True(!store.TryExtendLodging("paid",300));
                Check.Equal(200d,store.Snapshot().Lodgings[0].Bookings[0].EndsAt);
                var corrupt=store.Snapshot(); corrupt.Lodgings[0].Occupants[0].BedId="missing";
                Check.Throws<ArgumentException>(()=>store.Restore(corrupt));
                Check.Equal("bed:2",store.NativeLodgingBed(a.Id));
            }));
            cases.Add(("Room offer checks whole stay without reserving or promising a future gap",()=>{
                var store=Store();
                store.TryBookLodging(1,7,"future","guest",2,false,2,150,300,"double");
                Check.Equal("single",store.AvailableLodgingRoom(1,7,1,100,200));
                Check.Equal("double",store.AvailableLodgingRoom(1,7,2,100,150));
                Check.True(store.AvailableLodgingRoom(1,7,2,100,151)==null);
                Check.Equal(1,store.Snapshot().Lodgings[0].Bookings.Count);
            }));
            cases.Add(("Native paid rental reconciliation preserves identity room and original interval on repeat and extension",()=>{
                var store=Store();
                Check.True(store.ReconcilePlayerLodging(1,7,"single",100,200));
                var original=store.Snapshot().Lodgings[0].Bookings[0];
                Check.True(store.ReconcilePlayerLodging(1,7,"single",125,200));
                Check.True(store.ReconcilePlayerLodging(1,7,"single",150,300));
                var restored=new WorldStateStore(0); restored.Restore(store.Snapshot());
                Check.True(restored.ReconcilePlayerLodging(1,7,"single",175,300));
                var bookings=restored.Snapshot().Lodgings[0].Bookings;
                Check.Equal(1,bookings.Count); Check.Equal(original.Id,bookings[0].Id);
                Check.Equal(100d,bookings[0].StartsAt); Check.Equal(300d,bookings[0].EndsAt);
                Check.Equal("single",bookings[0].RoomId);
                Check.True(!restored.ReconcilePlayerLodging(1,7,"single",300,300));
                Check.True(restored.ReconcilePlayerLodging(1,7,"single",400,500));
                Check.Equal(2,restored.Snapshot().Lodgings[0].Bookings.Count);
            }));
            cases.Add(("Native rental conflicts cannot silently move rooms evict guests or shorten paid stays",()=>{
                var store=Store(); store.ReconcilePlayerLodging(1,7,"single",100,200);
                Check.True(!store.ReconcilePlayerLodging(1,7,"double",120,200));
                Check.True(!store.ReconcilePlayerLodging(1,7,"single",120,190));
                store.TryBookLodging(1,7,"next","traveler",2,false,1,200,300,"single");
                Check.True(!store.ReconcilePlayerLodging(1,7,"single",120,250));
                Check.Equal(200d,store.Snapshot().Lodgings[0].Bookings[0].EndsAt);
                Check.True(!store.ReconcilePlayerLodging(1,7,"single",210,400));
                Check.Equal(2,store.Snapshot().Lodgings[0].Bookings.Count);
            }));
            cases.Add(("Player retains concurrent paid rooms in different inns through extension and reload",()=>{
                var store=Store();
                store.RegisterLodging(1,8,new[] { new LodgingRoom { Id="room",Beds=new List<string> { "bed" } } });
                Check.True(store.TryBookLodging(1,7,"first","player",1,true,1,100,200));
                Check.True(store.TryBookLodging(1,8,"second","player",1,true,1,150,300));
                Check.True(!store.TryBookLodging(1,7,"duplicate","player",1,true,1,150,250));
                Check.True(store.TryExtendLodging("first",250));
                var copy=new WorldStateStore(0); copy.Restore(store.Snapshot());
                Check.Equal(250d,copy.Snapshot().Lodgings[0].Bookings[0].EndsAt);
                Check.True(copy.IsLodgingBedReserved(1,8,"bed",200,"traveler"));
            }));
            cases.Add(("Travelers cannot overlap inns or change guest role to bypass reservations",()=>{
                var store=Store();
                store.RegisterLodging(1,8,new[] { new LodgingRoom { Id="room",Beds=new List<string> { "bed" } } });
                Check.True(store.TryBookLodging(1,7,"first","guest",2,false,1,100,200));
                Check.True(!store.TryBookLodging(1,8,"overlap","guest",2,false,1,150,250));
                Check.Throws<ArgumentException>(()=>store.TryBookLodging(1,8,"changed","guest",2,true,1,200,300));
                Check.True(store.TryBookLodging(1,8,"next","guest",2,false,1,200,300));
                Check.True(!store.TryExtendLodging("first",201));
                var malformed=store.Snapshot(); malformed.Lodgings[1].Bookings[0].Player=true;
                Check.Throws<ArgumentException>(()=>store.Restore(malformed));
                Check.True(!store.Snapshot().Lodgings[1].Bookings[0].Player);
            }));
            cases.Add(("Native allocated room is preserved without fallback to another vacancy",()=>{
                var store=Store();
                Check.True(store.TryBookLodging(1,7,"player","player",1,true,1,100,200,"single"));
                Check.Equal("single",store.Snapshot().Lodgings[0].Bookings[0].RoomId);
                Check.True(!store.TryBookLodging(1,7,"next","next",2,false,1,100,200,"single"));
                Check.True(store.TryBookLodging(1,7,"next","next",2,false,1,100,200,"double"));
                Check.Throws<ArgumentException>(()=>store.TryBookLodging(1,7,"player","player",1,true,1,100,200,"double"));
                Check.Throws<ArgumentException>(()=>store.TryBookLodging(1,7,"unknown","other",2,false,1,300,400,"missing"));
            }));
            cases.Add(("Paid lodging reserves entire room while guest is away",()=>{
                var store=Store();
                Check.True(store.TryBookLodging(1,7,"a","traveler:a",2,false,2,100,200));
                Check.True(store.TryBookLodging(1,7,"b","traveler:b",3,false,1,100,150));
                Check.True(!store.TryBookLodging(1,7,"player","player",1,true,1,110,130));
                Check.True(store.IsLodgingBedReserved(1,7,"bed:0",120,"player"));
                Check.True(store.IsLodgingBedReserved(1,7,"bed:1",120,"player"));
                Check.True(!store.IsLodgingBedReserved(1,7,"bed:0",120,"traveler:a"));
                Check.True(!store.IsLodgingBedReserved(1,7,"bed:0",200,"player"));
            }));
            cases.Add(("Earliest vacancy checks requested duration and exact checkout",()=>{
                var store=Store();
                store.TryBookLodging(1,7,"a","a",2,false,2,100,200);
                store.TryBookLodging(1,7,"b","b",2,false,2,220,300);
                Check.Equal(200d,store.EarliestLodgingStart(1,7,2,100,20).Value);
                Check.Equal(300d,store.EarliestLodgingStart(1,7,2,100,21).Value);
                Check.True(!store.EarliestLodgingStart(1,7,3,100,20).HasValue);
            }));
            cases.Add(("Failed extensions preserve player payment interval and competing booking",()=>{
                var store=Store(); store.TryBookLodging(1,7,"player","player",1,true,2,100,200);
                store.TryBookLodging(1,7,"next","next",2,false,2,210,300);
                Check.True(!store.TryExtendLodging("player",220));
                Check.Equal(200d,store.Snapshot().Lodgings[0].Bookings[0].EndsAt);
                Check.True(store.TryExtendLodging("player",210));
                Check.True(store.IsLodgingBedReserved(1,7,"bed:0",205,"next"));
            }));
            cases.Add(("Lodging is idempotent, rejects local traveler and preserves save ownership",()=>{
                var store=Store(); Check.True(store.TryBookLodging(1,7,"a","a",2,false,1,100,200));
                Check.True(store.TryBookLodging(1,7,"a","a",2,false,1,100,200));
                Check.Equal(1,store.Snapshot().Lodgings[0].Bookings.Count);
                Check.Throws<ArgumentException>(()=>store.TryBookLodging(1,7,"a","other",2,false,1,100,200));
                Check.Throws<ArgumentException>(()=>store.TryBookLodging(1,7,"local","local",1,false,1,100,200));
                var saved=store.Snapshot(); var copy=new WorldStateStore(0); copy.Restore(saved);
                saved.Lodgings[0].Bookings[0].EndsAt=101;
                Check.True(copy.IsLodgingBedReserved(1,7,"bed:0",150,"player"));
            }));
            cases.Add(("Lodging migration and malformed overlapping saves are atomic",()=>{
                var store=Store(); store.TryBookLodging(1,7,"a","a",2,false,1,100,200);
                var malformed=store.Snapshot();
                malformed.Lodgings[0].Bookings.Add(new LodgingBooking { Id="b",GuestId="b",HomeSettlement=2,
                    RoomId="double",PartySize=1,StartsAt=100,EndsAt=200 });
                Check.Throws<ArgumentException>(()=>store.Restore(malformed));
                Check.Equal(1,store.Snapshot().Lodgings[0].Bookings.Count);
                for(int version=1;version<=7;version++)
                {
                    var legacy=store.Snapshot(); legacy.Version=version; legacy.Lodgings=null;
                    var migrated=new WorldStateStore(0); migrated.Restore(legacy);
                    Check.Equal(0,migrated.Snapshot().Lodgings.Count);
                }
            }));
            cases.Add(("Tavern room survey cannot silently erase occupied beds",()=>{
                var store=Store(); store.TryBookLodging(1,7,"a","a",2,false,1,100,200);
                Check.Throws<ArgumentException>(()=>store.RegisterLodging(1,7,new[] {
                    new LodgingRoom { Id="double",Beds=new List<string> { "different" } } }));
                Check.True(store.IsLodgingBedReserved(1,7,"bed:0",150,"player"));
            }));
        }
    }
}
