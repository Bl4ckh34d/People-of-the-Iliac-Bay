using System;
using System.Collections.Generic;
namespace PeopleOfTheIliacBay.Core
{
    [Serializable]
    public sealed class InnLodgerHomeReservation
    {
        public string PersonId,RoomId;
        public int InnSettlement,InnKey,HomeSettlement,HomeKey,BedSlot,BedCapacity,Generation;
        public double ReservedAt;
    }
    public sealed partial class WorldStateStore
    {
        private readonly Dictionary<string,InnLodgerHomeReservation> innLodgerHomeIndex=new Dictionary<string,InnLodgerHomeReservation>();
        public InnLodgerHomeReservation InnLodgerHome(string personId)
        {InnLodgerHomeReservation found;return personId!=null && innLodgerHomeIndex.TryGetValue(personId,out found) ? CopyLodgerHome(found) : null;}
        public List<InnLodgerHomeReservation> HouseholdInnLodgers(int town,int key)
        {
            var result=new List<InnLodgerHomeReservation>();var home=CurrentPopulation(town)?.Households.Find(h=>h.BuildingKey==key);
            foreach(var item in state.InnLodgerHomes)
                if(item.HomeSettlement==town && item.HomeKey==key && home!=null && item.Generation==home.Generation) result.Add(CopyLodgerHome(item));
            return result;
        }
        public bool HousingReservedForRecovery(int town,int key)
        {return state.HouseholdRecoveries.Exists(r=>r.SettlementId==town && r.BuildingKey==key &&
            r.Status!=HouseholdRecoveryStatus.Settled);}
        public InnLodgerHomeReservation TryReserveInnLodgerHome(string personId,string roomId,int homeTown,HomeProfile profile,double now)
        {
            Require(DefenseTime(now) && homeTown>=0 && profile!=null,"Invalid lodger household request.");
            InnLodgerHomeReservation existing;
            if(personId!=null && innLodgerHomeIndex.TryGetValue(personId,out existing)) return CopyLodgerHome(existing);
            var person=state.NativePeople.Find(p=>p.Id==personId);
            if(person==null || !person.IsAlive || person.RetiredBedroomFlat || person.HomeKey>=0 || homeTown==person.SettlementId ||
                string.IsNullOrEmpty(roomId) || profile.BedCount<=0 || !profile.AvailableForResidents ||
                HousingReservedForRecovery(homeTown,profile.BuildingKey)) return null;
            var inn=state.Lodgings.Find(t=>t.SettlementId==person.SettlementId && t.BuildingKey==person.WorkKey);
            var room=inn?.Rooms.Find(r=>r.Id==roomId && !r.NonRentable);
            if(room==null) return null;
            var bed=inn.Occupants.Find(o=>o.PersonId==personId);
            if(bed!=null && !room.Beds.Contains(bed.BedId)) return null;
            var town=CurrentPopulation(homeTown);
            var home=town?.Households.Find(h=>h.BuildingKey==profile.BuildingKey);
            bool created=home==null;
            if(created)
            {
                if(town!=null && !town.IsPartial) return null;
                // Reserve the incoming person first; fill only remaining capacity.
                home=Population.CreateFamilies(state.WorldSeed,homeTown,new[]{new HomeProfile(profile.BuildingKey,profile.BedCount-1,profile.FootprintArea)}).Households[0];
                home.BedCapacity=profile.BedCount;
            }
            if(home.BedCapacity!=profile.BedCount || home.Residents.Count>0 && !home.Residents.Exists(r=>r.IsAlive)) return null;
            var occupied=FamilyBedSlots(home);
            foreach(var native in state.NativePeople)
                if(native.SettlementId==homeTown && native.HomeKey==home.BuildingKey && native.BedSlot>=0) occupied.Add(native.BedSlot);
            foreach(var reservation in HouseholdInnLodgers(homeTown,home.BuildingKey)) occupied.Add(reservation.BedSlot);
            int slot=0;while(slot<home.BedCapacity && occupied.Contains(slot)) slot++;
            if(slot>=home.BedCapacity) return null;
            var saved=new InnLodgerHomeReservation {PersonId=person.Id,RoomId=roomId,InnSettlement=person.SettlementId,InnKey=person.WorkKey,
                HomeSettlement=homeTown,HomeKey=home.BuildingKey,BedSlot=slot,BedCapacity=home.BedCapacity,Generation=home.Generation,ReservedAt=now};
            if(created)
            {
                if(town==null) {town=new SettlementPopulation {SettlementId=homeTown,IsPartial=true};state.Settlements.Add(town);}
                town.Households.Add(home);town.Households.Sort((a,b)=>a.BuildingKey.CompareTo(b.BuildingKey));
            }
            state.InnLodgerHomes.Add(saved);innLodgerHomeIndex.Add(person.Id,saved);
            return CopyLodgerHome(saved);
        }
        private static InnLodgerHomeReservation CopyLodgerHome(InnLodgerHomeReservation r)
        {return new InnLodgerHomeReservation {PersonId=r.PersonId,RoomId=r.RoomId,InnSettlement=r.InnSettlement,InnKey=r.InnKey,
            HomeSettlement=r.HomeSettlement,HomeKey=r.HomeKey,BedSlot=r.BedSlot,BedCapacity=r.BedCapacity,Generation=r.Generation,ReservedAt=r.ReservedAt};}
        private static List<InnLodgerHomeReservation> CopyInnLodgerHomes(WorldState source,WorldState world)
        {
            var result=new List<InnLodgerHomeReservation>();if(source.Version<24) return result;
            Require(source.InnLodgerHomes!=null,"Missing inn lodger home reservations.");
            var people=new HashSet<string>();var slots=new HashSet<string>();
            foreach(var item in source.InnLodgerHomes)
            {
                Require(item!=null && !string.IsNullOrEmpty(item.PersonId) && people.Add(item.PersonId) && !string.IsNullOrEmpty(item.RoomId) &&
                    item.HomeSettlement>=0 && item.HomeSettlement!=item.InnSettlement && item.HomeKey>=0 && item.BedCapacity>0 &&
                    item.BedSlot>=0 && item.BedSlot<item.BedCapacity && item.Generation>=0 && DefenseTime(item.ReservedAt),"Invalid inn lodger home reservation.");
                var person=world.NativePeople.Find(p=>p.Id==item.PersonId);
                Require(person!=null && !person.RetiredBedroomFlat && person.SettlementId==item.InnSettlement && person.WorkKey==item.InnKey && person.HomeKey<0,
                    "Lodger home lost its native identity or conflicts with a local home.");
                var inn=world.Lodgings.Find(t=>t.SettlementId==item.InnSettlement && t.BuildingKey==item.InnKey);
                Require(inn!=null && inn.Rooms.Exists(r=>r.Id==item.RoomId && !r.NonRentable),"Lodger home lacks a guest bedroom.");
                var town=world.Settlements.Find(t=>t.SettlementId==item.HomeSettlement);
                var home=town?.Households.Find(h=>h.BuildingKey==item.HomeKey && h.Generation==item.Generation);
                if(home==null && !person.IsAlive) home=world.HouseholdHistory.Find(h=>h.SettlementId==item.HomeSettlement &&
                    h.Household.BuildingKey==item.HomeKey && h.Household.Generation==item.Generation)?.Household;
                Require(home!=null && home.BedCapacity==item.BedCapacity && !FamilyBedSlots(home).Contains(item.BedSlot) &&
                    slots.Add(item.HomeSettlement+":"+item.HomeKey+":"+item.Generation+":"+item.BedSlot),"Lodger home bed is missing or occupied.");
                Require(!world.NativePeople.Exists(p=>p.SettlementId==item.HomeSettlement && p.HomeKey==item.HomeKey && p.BedSlot==item.BedSlot) &&
                    !world.Settlements.Exists(t=>t.Households.Exists(h=>h.Residents.Exists(r=>r.Id==item.PersonId))),"Lodger has a duplicate home representation.");
                result.Add(CopyLodgerHome(item));
            }
            return result;
        }
    }
}
