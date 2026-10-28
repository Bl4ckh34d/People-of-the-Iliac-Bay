using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public enum LodgingBookingPurpose { Ordinary=0, NpcHealthRest=1 }

    [Serializable]
    public sealed class LodgingBed
    {
        public string Id;
    }
    [Serializable]
    public sealed class LodgingBooking
    {
        public string Id,GuestId;
        public int HomeSettlement,PartySize;
        public bool Player;
        public double StartsAt,EndsAt;
        public List<string> BedIds=new List<string>();
        public LodgingBookingPurpose Purpose;
        public string HealthCareId,HealthCareTaskId,BedId,PaymentTransactionId;
        public int PaidPrice,PaidNights;
    }
    [Serializable]
    public sealed class LodgingOccupant
    {
        public string PersonId,BedId;
        public double StartsAt;
    }
    [Serializable]
    public sealed class TavernLodging
    {
        public int SettlementId,BuildingKey;
        public int GuildFactionId;
        public List<LodgingBed> Beds=new List<LodgingBed>();
        public List<LodgingBooking> Bookings=new List<LodgingBooking>();
        public List<LodgingOccupant> Occupants=new List<LodgingOccupant>();
    }
    public sealed partial class WorldStateStore
    {
        private void ReleaseCleanedLodging(CivilianIncident alarm)
        {
            if(alarm.Corpse==null || !alarm.Corpse.Cleared ||
                !state.NativePeople.Exists(p=>p.Id==alarm.VictimId && !p.IsAlive)) return;
            foreach(var tavern in state.Lodgings)
                tavern.Occupants.RemoveAll(o=>o.PersonId==alarm.VictimId);
        }
        public string NativeLodgingBed(string personId)
        {
            foreach(var tavern in state.Lodgings)
            {
                var claim=tavern.Occupants.Find(o=>o.PersonId==personId);
                if(claim!=null) return claim.BedId;
            }
            return null;
        }
        public string NativeGuestBed(string personId)
        { return NativeLodgingBed(personId); }
        public bool TryClaimLodgingBed(int town,int building,string personId,string bed,double now)
        {
            var tavern=Lodging(town,building);
            Require(DefenseTime(now) && state.NativePeople.Exists(p=>p.Id==personId && p.IsAlive &&
                p.SettlementId==town && p.WorkKey==building),"Invalid native lodging occupant.");
            Require(tavern.Beds.Exists(value=>value.Id==bed),"Unknown occupant bed.");
            if(state.NativePeople.Exists(p=>p.Id==personId && p.RetiredBedroomFlat)) return false;
            foreach(var other in state.Lodgings)
            {
                var prior=other.Occupants.Find(o=>o.PersonId==personId);
                if(prior!=null) return other==tavern && prior.BedId==bed;
            }
            if(tavern.Occupants.Exists(o=>o.BedId==bed) ||
                tavern.Bookings.Exists(b=>b.BedIds.Contains(bed) && b.EndsAt>now)) return false;
            tavern.Occupants.Add(new LodgingOccupant {PersonId=personId,BedId=bed,StartsAt=now});
            return true;
        }
        private TavernLodging Lodging(int town,int building)
        {
            var result=state.Lodgings.Find(t=>t.SettlementId==town && t.BuildingKey==building);
            Require(result!=null,"Unknown tavern lodging."); return result;
        }
        public void RegisterLodgingBeds(int town,int building,IEnumerable<LodgingBed> beds)
        {
            Require(beds!=null,"Missing lodging beds.");
            var incoming=new TavernLodging {SettlementId=town,BuildingKey=building,Beds=new List<LodgingBed>(beds)};
            var copied=CopyLodging(incoming,state,new HashSet<string>(),WorldState.CurrentVersion,state);
            var existing=state.Lodgings.Find(t=>t.SettlementId==town && t.BuildingKey==building);
            if(existing!=null)
            {
                foreach(var previous in existing.Beds)
                    Require(copied.Beds.Exists(value=>value.Id==previous.Id),"Lodging layout cannot erase a known bed.");
                foreach(var addition in copied.Beds)
                    if(!existing.Beds.Exists(value=>value.Id==addition.Id)) existing.Beds.Add(addition);
                existing.Beds.Sort((a,b)=>StringComparer.Ordinal.Compare(a.Id,b.Id));
                return;
            }
            state.Lodgings.Add(copied);
        }
        public bool TryBookLodging(int town,int building,string id,string guest,int home,bool player,int party,double start,double end,string allocatedBed=null)
        {
            var tavern=Lodging(town,building);
            if(tavern.GuildFactionId>0 && (player || party!=1 || !GuildGuest(state,guest,home,tavern.GuildFactionId)))return false;
            Require(allocatedBed==null || tavern.Beds.Exists(value=>value.Id==allocatedBed),"Unknown allocated lodging bed.");
            Require(allocatedBed==null || party==1,"A native bed allocation can book only one guest.");
            Require(DefenseId(id) && DefenseId(guest) && home>=0 && (player || home!=town) && party>0 &&
                DefenseTime(start) && DefenseTime(end) && end>start,"Invalid lodging request.");
            foreach(var other in state.Lodgings)
            {
                var prior=other.Bookings.Find(b=>b.Id==id);
                if(prior==null) continue;
                Require(other==tavern && prior.GuestId==guest && prior.HomeSettlement==home && prior.Player==player &&
                    prior.PartySize==party && prior.StartsAt==start && prior.EndsAt==end &&
                    (allocatedBed==null || prior.BedIds.Count==1 && prior.BedIds[0]==allocatedBed),"Booking identity changed.");
                return true;
            }
            foreach(var other in state.Lodgings)
            {
                Require(!other.Bookings.Exists(b=>b.GuestId==guest && b.Player!=player),"Lodging guest role changed.");
                if(other.Bookings.Exists(b=>b.GuestId==guest && (!player || other==tavern) && Overlap(start,end,b))) return false;
            }
            var available=AvailableBeds(tavern,party,start,end,allocatedBed,null);
            if(available==null) return false;
            tavern.Bookings.Add(new LodgingBooking {Id=id,GuestId=guest,HomeSettlement=home,Player=player,
                PartySize=party,BedIds=available,StartsAt=start,EndsAt=end});
            return true;
        }
        public bool TryExtendLodging(string id,double end)
        {
            foreach(var tavern in state.Lodgings)
            {
                var booking=tavern.Bookings.Find(b=>b.Id==id); if(booking==null) continue;
                Require(DefenseTime(end) && end>=booking.EndsAt,"Invalid lodging extension.");
                if(booking.Purpose==LodgingBookingPurpose.NpcHealthRest && end!=booking.EndsAt)return false;
                if(end!=booking.EndsAt && state.TravelerVisits.Exists(v=>v.BookingId==id)) return false;
                if(end!=booking.EndsAt && state.ResidentTrips.Exists(t=>t.InnStays.Exists(s=>s.BookingId==id))) return false;
                foreach(var bed in booking.BedIds)
                {
                    if(tavern.Occupants.Exists(o=>o.BedId==bed && o.StartsAt<end)) return false;
                    if(tavern.Bookings.Exists(b=>b!=booking && b.BedIds.Contains(bed) && Overlap(booking.StartsAt,end,b))) return false;
                }
                foreach(var other in state.Lodgings)
                    if(other.Bookings.Exists(b=>b!=booking && b.GuestId==booking.GuestId && (!booking.Player || other==tavern) &&
                        Overlap(booking.StartsAt,end,b))) return false;
                booking.EndsAt=end; return true;
            }
            throw new ArgumentException("Unknown lodging booking.");
        }
        public bool ReconcilePlayerLodging(int town,int building,string bed,double now,double expiry)
        {
            Require(DefenseTime(now) && DefenseTime(expiry),"Invalid native rental time.");
            var tavern=Lodging(town,building);
            Require(tavern.Beds.Exists(value=>value.Id==bed),"Unknown native rental bed.");
            if(expiry<=now) return false;
            var booking=tavern.Bookings.Find(b=>b.Player && b.GuestId=="player" && b.StartsAt<=now && now<b.EndsAt);
            if(booking!=null)
            {
                if(booking.BedIds.Count!=1 || booking.BedIds[0]!=bed || expiry<booking.EndsAt) return false;
                return TryExtendLodging(booking.Id,expiry);
            }
            string id="native-player:"+town+":"+building+":"+now.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
            return TryBookLodging(town,building,id,"player",town,true,1,now,expiry,bed);
        }
        public bool IsLodgingBedReserved(int town,int building,string bed,double now,string guest)
        {
            Require(DefenseTime(now),"Invalid lodging time."); var tavern=Lodging(town,building);
            Require(tavern.Beds.Exists(value=>value.Id==bed),"Unknown lodging bed.");
            return tavern.Occupants.Exists(o=>o.BedId==bed && o.PersonId!=guest && o.StartsAt<=now) ||
                tavern.Bookings.Exists(b=>b.BedIds.Contains(bed) && b.GuestId!=guest && b.StartsAt<=now && now<b.EndsAt);
        }
        public double? EarliestLodgingStart(int town,int building,int party,double now,double duration)
        {
            Require(party>0 && DefenseTime(now) && DefenseTime(duration) && duration>0 && DefenseTime(now+duration),"Invalid lodging availability request.");
            var tavern=Lodging(town,building);var candidates=new List<double>{now};
            foreach(var booking in tavern.Bookings) if(booking.EndsAt>=now && !candidates.Contains(booking.EndsAt)) candidates.Add(booking.EndsAt);
            candidates.Sort();
            foreach(double at in candidates)
                if(DefenseTime(at+duration) && AvailableBeds(tavern,party,at,at+duration,null,null)!=null) return at;
            return null;
        }
        public List<string> AvailableLodgingBeds(int town,int building,int party,double start,double end)
        {
            Require(party>0 && DefenseTime(start) && DefenseTime(end) && end>start,"Invalid lodging offer.");
            return AvailableBeds(Lodging(town,building),party,start,end,null,null);
        }
        private static List<string> AvailableBeds(TavernLodging tavern,int count,double start,double end,string exact,LodgingBooking ignored)
        {
            var result=new List<string>();
            foreach(var bed in tavern.Beds)
            {
                if(exact!=null && bed.Id!=exact) continue;
                if(tavern.Occupants.Exists(o=>o.BedId==bed.Id && o.StartsAt<end)) continue;
                if(tavern.Bookings.Exists(b=>b!=ignored && b.BedIds.Contains(bed.Id) && Overlap(start,end,b))) continue;
                result.Add(bed.Id);
            }
            result.Sort(StringComparer.Ordinal);
            if(result.Count<count) return null;
            if(result.Count>count) result.RemoveRange(count,result.Count-count);
            return result;
        }
        private static bool Overlap(double start,double end,LodgingBooking booking)
        { return start<booking.EndsAt && booking.StartsAt<end; }
        private static bool SharesBed(LodgingBooking a,LodgingBooking b)
        {
            foreach(var bed in a.BedIds) if(b.BedIds.Contains(bed)) return true;
            return false;
        }
        private static TavernLodging CopyLodging(TavernLodging source,WorldState world,HashSet<string> ids,int version=84,WorldState authority=null)
        {
            Require(source!=null && source.BuildingKey>=0 && world.Settlements.Exists(s=>s.SettlementId==source.SettlementId) &&
                source.Beds!=null && source.Bookings!=null,"Invalid tavern lodging.");
            Require(source.GuildFactionId>=0,"Invalid guild lodging faction.");
            var copy=new TavernLodging {SettlementId=source.SettlementId,BuildingKey=source.BuildingKey,GuildFactionId=source.GuildFactionId};
            var beds=new HashSet<string>();
            foreach(var bed in source.Beds)
            {
                Require(bed!=null && DefenseId(bed.Id) && beds.Add(bed.Id),"Duplicate or invalid lodging bed.");
                copy.Beds.Add(new LodgingBed {Id=bed.Id});
            }
            copy.Beds.Sort((a,b)=>StringComparer.Ordinal.Compare(a.Id,b.Id));
            Require(source.Occupants!=null,"Missing native lodging occupants.");
            foreach(var occupant in source.Occupants)
            {
                Require(occupant!=null && DefenseTime(occupant.StartsAt) && beds.Contains(occupant.BedId) &&
                    world.NativePeople.Exists(p=>p.Id==occupant.PersonId && !p.RetiredBedroomFlat && p.SettlementId==source.SettlementId && p.WorkKey==source.BuildingKey) &&
                    !copy.Occupants.Exists(o=>o.PersonId==occupant.PersonId || o.BedId==occupant.BedId),"Invalid native lodging claim.");
                copy.Occupants.Add(new LodgingOccupant {PersonId=occupant.PersonId,BedId=occupant.BedId,StartsAt=occupant.StartsAt});
            }
            foreach(var booking in source.Bookings)
            {
                Require(source.GuildFactionId==0 || booking!=null && !booking.Player && booking.PartySize==1 &&
                    GuildGuest(world,booking.GuestId,booking.HomeSettlement,source.GuildFactionId,false),"Guild lodging lacks matching NPC membership.");
                Require(booking!=null && DefenseId(booking.Id) && ids.Add(booking.Id) && DefenseId(booking.GuestId) &&
                    booking.HomeSettlement>=0 && booking.PartySize>0 && booking.BedIds!=null && booking.BedIds.Count==booking.PartySize &&
                    DefenseTime(booking.StartsAt) && DefenseTime(booking.EndsAt) && booking.EndsAt>booking.StartsAt,
                    "Invalid lodging booking.");
                var bookedBeds=new HashSet<string>();
                foreach(var bed in booking.BedIds) Require(beds.Contains(bed) && bookedBeds.Add(bed),"Invalid booked bed.");
                if(booking.Purpose==LodgingBookingPurpose.Ordinary)
                    Require(booking.Player || booking.HomeSettlement!=source.SettlementId,"Invalid local ordinary lodging booking.");
                else if(booking.Purpose==LodgingBookingPurpose.NpcHealthRest)
                    Require(ValidHealthRestLodgingAuthority(authority,source,booking),"Invalid NPC healthcare lodging authority.");
                else Require(false,"Unknown lodging booking purpose.");
                Require(!copy.Occupants.Exists(o=>bookedBeds.Contains(o.BedId) && o.StartsAt<booking.EndsAt),
                    "Booking conflicts with native bed occupant.");
                Require(!copy.Bookings.Exists(b=>b.GuestId==booking.GuestId && b.Player!=booking.Player),"Lodging guest role changed.");
                Require(!copy.Bookings.Exists(b=>(SharesBed(booking,b) || b.GuestId==booking.GuestId) &&
                    Overlap(booking.StartsAt,booking.EndsAt,b)),"Overlapping lodging bookings.");
                copy.Bookings.Add(CopyBooking(booking));
            }
            return copy;
        }
        private static LodgingBooking CopyBooking(LodgingBooking booking)
        {
            return new LodgingBooking {Id=booking.Id,GuestId=booking.GuestId,HomeSettlement=booking.HomeSettlement,
                Player=booking.Player,PartySize=booking.PartySize,BedIds=new List<string>(booking.BedIds),
                StartsAt=booking.StartsAt,EndsAt=booking.EndsAt,Purpose=booking.Purpose,
                HealthCareId=booking.HealthCareId,HealthCareTaskId=booking.HealthCareTaskId,BedId=booking.BedId,
                PaymentTransactionId=booking.PaymentTransactionId,PaidPrice=booking.PaidPrice,PaidNights=booking.PaidNights};
        }
        private static List<TavernLodging> CopyLodgings(WorldState source,WorldState world)
        {
            var result=new List<TavernLodging>();
            if(source.Version<84) return result;
            Require(source.Lodgings!=null,"Missing lodging ledger."); var ids=new HashSet<string>();
            foreach(var tavern in source.Lodgings)
            {
                var copy=CopyLodging(tavern,world,ids,source.Version,source);
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
