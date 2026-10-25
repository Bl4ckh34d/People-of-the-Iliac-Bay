namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        public bool PreferNativeWorkplaceBed(string id,HomeProfile onSite)
        {
            var person=state.NativePeople.Find(p=>p.Id==id);
            Require(person!=null && onSite!=null && onSite.BuildingKey==person.WorkKey,"Invalid keeper bedroom.");
            ValidateNativeHome(onSite);
            if(!person.IsAlive || person.RetiredBedroomFlat) return false;
            if(person.HomeKey==person.WorkKey) return true;
            if(state.Settlements.Exists(t=>t.Households.Exists(h=>h.Residents.Exists(r=>r.Id==id)))) return false;
            int home=person.HomeKey,slot=person.BedSlot,capacity=person.BedCapacity;
            person.HomeKey=-1;person.BedSlot=-1;person.BedCapacity=0;
            if(ReserveNativeBed(person,onSite,false)) return true;
            person.HomeKey=home;person.BedSlot=slot;person.BedCapacity=capacity;
            return false;
        }
        // Runtime caller must first prove an ordinary bedroom role, not a keeper,
        // staff member, quest actor or named individual.
        public bool RetireOrdinaryBedroomFlat(string id)
        {
            var person=state.NativePeople.Find(p=>p.Id==id);
            Require(person!=null,"Unknown bedroom identity.");
            if(!person.IsAlive) return false; // Preserve corpse, home-key and death evidence.
            if(person.RetiredBedroomFlat) return true;
            // Never dissolve an existing keeper family or an unrelated residence.
            if(state.Settlements.Exists(t=>t.Households.Exists(h=>h.Residents.Exists(r=>r.Id==id))) ||
                person.HomeKey>=0 && person.HomeKey!=person.WorkKey) return false;
            foreach(var inn in state.Lodgings) inn.Occupants.RemoveAll(o=>o.PersonId==id);
            state.InnLodgerHomes.RemoveAll(r=>r.PersonId==id);innLodgerHomeIndex.Remove(id);
            person.HomeKey=-1;person.BedSlot=-1;person.BedCapacity=0;
            person.RetiredBedroomFlat=true;
            return true;
        }
    }
}
