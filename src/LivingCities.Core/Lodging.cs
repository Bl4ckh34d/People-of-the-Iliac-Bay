using System;
using System.Collections.Generic;

namespace LivingCities.Core
{
    [Serializable]
    public sealed class LodgingRoom
    {
        public string Id;
        public List<string> Beds=new List<string>();
    }
    [Serializable]
    public sealed class LodgingBooking
    {
        public string Id,GuestId,RoomId;
        public int HomeSettlement,PartySize;
        public bool Player;
        public double StartsAt,EndsAt;
    }
    [Serializable]
    public sealed class TavernLodging
    {
        public int SettlementId,BuildingKey;
        public List<LodgingRoom> Rooms=new List<LodgingRoom>();
        public List<LodgingBooking> Bookings=new List<LodgingBooking>();
    }
    public sealed partial class WorldStateStore
    {
        private TavernLodging Lodging(int town,int building)
        {
            var result=state.Lodgings.Find(t=>t.SettlementId==town && t.BuildingKey==building);
            Require(result!=null,"Unknown tavern lodging."); return result;
        }
        public void RegisterLodging(int town,int building,IEnumerable<LodgingRoom> rooms)
        {
            Require(rooms!=null,"Missing lodging rooms.");
            var incoming=new TavernLodging { SettlementId=town,BuildingKey=building,Rooms=new List<LodgingRoom>(rooms) };
            var copied=CopyLodging(incoming,state,new HashSet<string>());
            var existing=state.Lodgings.Find(t=>t.SettlementId==town && t.BuildingKey==building);
            if(existing!=null)
            {
                Require(existing.Rooms.Count==copied.Rooms.Count,"Lodging layout change requires reconciliation.");
                foreach(var room in copied.Rooms)
                {
                    var previous=existing.Rooms.Find(r=>r.Id==room.Id);
                    Require(previous!=null && previous.Beds.Count==room.Beds.Count &&
                        previous.Beds.TrueForAll(b=>room.Beds.Contains(b)),"Lodging layout change requires reconciliation.");
                }
                return;
            }
            state.Lodgings.Add(copied);
        }
        // A successful commit represents a paid reservation; callers must reconcile native payment.
        public bool TryBookLodging(int town,int building,string id,string guest,int home,bool player,int party,double start,double end,string allocatedRoom=null)
        {
            var tavern=Lodging(town,building);
            Require(allocatedRoom==null || tavern.Rooms.Exists(r=>r.Id==allocatedRoom),"Unknown allocated lodging room.");
            Require(DefenseId(id) && DefenseId(guest) && home>=0 && (player || home!=town) && party>0 &&
                DefenseTime(start) && DefenseTime(end) && end>start,"Invalid lodging request.");
            foreach(var other in state.Lodgings)
            {
                var prior=other.Bookings.Find(b=>b.Id==id);
                if(prior==null) continue;
                Require(other==tavern && prior.GuestId==guest && prior.HomeSettlement==home && prior.Player==player &&
                    prior.PartySize==party && prior.StartsAt==start && prior.EndsAt==end &&
                    (allocatedRoom==null || prior.RoomId==allocatedRoom),"Booking identity changed.");
                return true;
            }
            // Vanilla permits the player to retain paid rooms in different inns.
            foreach(var other in state.Lodgings)
            {
                Require(!other.Bookings.Exists(b=>b.GuestId==guest && b.Player!=player),"Lodging guest role changed.");
                if(other.Bookings.Exists(b=>b.GuestId==guest && (!player || other==tavern) && Overlap(start,end,b))) return false;
            }
            foreach(var room in tavern.Rooms)
            {
                if((allocatedRoom!=null && room.Id!=allocatedRoom) || room.Beds.Count<party ||
                    tavern.Bookings.Exists(b=>b.RoomId==room.Id && Overlap(start,end,b))) continue;
                tavern.Bookings.Add(new LodgingBooking { Id=id,GuestId=guest,HomeSettlement=home,Player=player,
                    PartySize=party,RoomId=room.Id,StartsAt=start,EndsAt=end });
                return true;
            }
            return false;
        }
        public bool TryExtendLodging(string id,double end)
        {
            foreach(var tavern in state.Lodgings)
            {
                var booking=tavern.Bookings.Find(b=>b.Id==id); if(booking==null) continue;
                Require(DefenseTime(end) && end>=booking.EndsAt,"Invalid lodging extension.");
                foreach(var other in state.Lodgings)
                    if(other.Bookings.Exists(b=>b!=booking && (b.GuestId==booking.GuestId && (!booking.Player || other==tavern) ||
                        other==tavern && b.RoomId==booking.RoomId) && Overlap(booking.StartsAt,end,b))) return false;
                booking.EndsAt=end; return true;
            }
            throw new ArgumentException("Unknown lodging booking.");
        }
        public bool IsLodgingBedReserved(int town,int building,string bed,double now,string guest)
        {
            Require(DefenseTime(now),"Invalid lodging time."); var tavern=Lodging(town,building);
            var room=tavern.Rooms.Find(r=>r.Beds.Contains(bed));
            Require(room!=null,"Unknown lodging bed.");
            return tavern.Bookings.Exists(b=>b.RoomId==room.Id && b.GuestId!=guest && b.StartsAt<=now && now<b.EndsAt);
        }
        public double? EarliestLodgingStart(int town,int building,int party,double now,double duration)
        {
            Require(party>0 && DefenseTime(now) && DefenseTime(duration) && duration>0 && DefenseTime(now+duration),"Invalid lodging availability request.");
            var tavern=Lodging(town,building); double? earliest=null;
            foreach(var room in tavern.Rooms)
            {
                if(room.Beds.Count<party) continue;
                double at=now; var bookings=tavern.Bookings.FindAll(b=>b.RoomId==room.Id);
                bookings.Sort((a,b)=>a.StartsAt.CompareTo(b.StartsAt));
                foreach(var booking in bookings) if(Overlap(at,at+duration,booking)) at=booking.EndsAt;
                if(DefenseTime(at+duration) && (!earliest.HasValue || at<earliest.Value)) earliest=at;
            }
            return earliest;
        }
        private static bool Overlap(double start,double end,LodgingBooking booking)
        { return start<booking.EndsAt && booking.StartsAt<end; }
        private static TavernLodging CopyLodging(TavernLodging source,WorldState world,HashSet<string> ids)
        {
            Require(source!=null && source.BuildingKey>=0 && world.Settlements.Exists(s=>s.SettlementId==source.SettlementId) &&
                source.Rooms!=null && source.Bookings!=null,"Invalid tavern lodging.");
            var copy=new TavernLodging { SettlementId=source.SettlementId,BuildingKey=source.BuildingKey };
            var beds=new HashSet<string>(); var rooms=new HashSet<string>();
            foreach(var room in source.Rooms)
            {
                Require(room!=null && DefenseId(room.Id) && rooms.Add(room.Id) && room.Beds!=null && room.Beds.Count>0,"Invalid lodging room.");
                var next=new LodgingRoom { Id=room.Id };
                foreach(var bed in room.Beds) { Require(DefenseId(bed) && beds.Add(bed),"Duplicate or invalid lodging bed."); next.Beds.Add(bed); }
                copy.Rooms.Add(next);
            }
            copy.Rooms.Sort((a,b)=>StringComparer.Ordinal.Compare(a.Id,b.Id));
            foreach(var booking in source.Bookings)
            {
                Require(booking!=null && DefenseId(booking.Id) && ids.Add(booking.Id) && DefenseId(booking.GuestId) &&
                    booking.HomeSettlement>=0 && (booking.Player || booking.HomeSettlement!=source.SettlementId) &&
                    booking.PartySize>0 && copy.Rooms.Exists(r=>r.Id==booking.RoomId && r.Beds.Count>=booking.PartySize) &&
                    DefenseTime(booking.StartsAt) && DefenseTime(booking.EndsAt) && booking.EndsAt>booking.StartsAt,
                    "Invalid lodging booking.");
                Require(!copy.Bookings.Exists(b=>b.GuestId==booking.GuestId && b.Player!=booking.Player),"Lodging guest role changed.");
                Require(!copy.Bookings.Exists(b=>(b.RoomId==booking.RoomId || b.GuestId==booking.GuestId) &&
                    Overlap(booking.StartsAt,booking.EndsAt,b)),"Overlapping lodging bookings.");
                copy.Bookings.Add(new LodgingBooking { Id=booking.Id,GuestId=booking.GuestId,RoomId=booking.RoomId,
                    HomeSettlement=booking.HomeSettlement,Player=booking.Player,PartySize=booking.PartySize,
                    StartsAt=booking.StartsAt,EndsAt=booking.EndsAt });
            }
            return copy;
        }
        private static List<TavernLodging> CopyLodgings(WorldState source,WorldState world)
        {
            var result=new List<TavernLodging>(); if(source.Version<8) return result;
            Require(source.Lodgings!=null,"Missing lodging ledger."); var ids=new HashSet<string>();
            foreach(var tavern in source.Lodgings)
            {
                var copy=CopyLodging(tavern,world,ids);
                Require(!result.Exists(t=>t.SettlementId==copy.SettlementId && t.BuildingKey==copy.BuildingKey),"Duplicate tavern lodging.");
                foreach(var booking in copy.Bookings)
                {
                    Require(!result.Exists(t=>t.Bookings.Exists(b=>b.GuestId==booking.GuestId && b.Player!=booking.Player)),
                        "Lodging guest role changed.");
                    Require(!result.Exists(t=>t.Bookings.Exists(b=>b.GuestId==booking.GuestId && !booking.Player &&
                        Overlap(booking.StartsAt,booking.EndsAt,b))),"Guest has simultaneous lodging in different taverns.");
                }
                result.Add(copy);
            }
            return result;
        }
    }
}
