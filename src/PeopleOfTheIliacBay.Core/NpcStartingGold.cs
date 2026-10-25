using System;
namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        public int StartingNpcGold(string personId,Func<int,int> locationType,Func<int,bool> nobleFaction)
        {
            if(string.IsNullOrWhiteSpace(personId))throw new ArgumentException("Missing NPC identity.");
            var saved=IndexedNpcInventory(personId);
            if(saved!=null)return saved.WalletGold;
            // Resolve permanent identity, never current visit/routine destination.
            int home=-1;
            var native=state.NativePeople.Find(p=>p.Id==personId);
            var lodger=InnLodgerHome(personId);
            if(lodger!=null)home=lodger.HomeSettlement;
            else {
                foreach(var town in state.Settlements) {
                    if(town.Households.Exists(h=>h.Residents.Exists(r=>r.Id==personId))) {home=town.SettlementId;break;}
                }
                if(home<0) {
                    var traveler=state.Travelers.Find(p=>p.Id==personId);
                    if(traveler!=null)home=traveler.HomeSettlement;
                    // Unresolved rented-bedroom guests are not local workers.
                    else if(native!=null && NativeGuestBedroom(personId)==null)home=native.SettlementId;
                }
            }
            bool noble=native!=null && nobleFaction(native.FactionId);
            int type=home<0?-1:locationType(home);
            int min=10,max=35; // Conservative unknown-origin fallback; no inferred inn wealth.
            if(noble || type==0) {min=250;max=500;}
            else if(type==1) {min=50;max=150;}
            else if(type==3) {min=0;max=15;}
            unchecked {uint hash=WorldSeed^2166136261;foreach(char c in personId)hash=(hash^c)*16777619;return min+(int)(hash%(uint)(max-min+1));}
        }
    }
}
