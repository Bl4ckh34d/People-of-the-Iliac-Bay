using System;
using System.Collections.Generic;
namespace LivingCities.Core
{
    public sealed class ResidentTravelProposal
    {
        public string ResidentId;
        public int HomeSettlement,DestinationSettlement,VisitDays;
        public double DesiredDeparture;
        public ResidentVisitPurpose Purpose;
    }
    public static class ResidentTravelDemand
    {
        // Proposals reference existing residents. Route/service feasibility is a
        // separate prerequisite before ScheduleResidentTrip commits any absence.
        public static List<ResidentTravelProposal> Select(uint worldSeed,int town,int day,IEnumerable<Resident> residents,
            IEnumerable<int> destinations,int chancePerThousand=10,int maximumDepartures=8)
        {
            if(town<0 || day<0 || residents==null || destinations==null || chancePerThousand<0 || chancePerThousand>1000 ||
                maximumDepartures<1 || maximumDepartures>1000) throw new ArgumentException("Invalid resident travel demand.");
            var targets=new SortedSet<int>();
            foreach(int target in destinations)
            {if(target<0) throw new ArgumentException("Invalid travel destination."); if(target!=town) targets.Add(target);}
            var pool=new List<int>(targets); var result=new List<ResidentTravelProposal>();
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var resident in residents)
            {
                if(resident==null || string.IsNullOrWhiteSpace(resident.Id) || !ids.Add(resident.Id)) throw new ArgumentException("Invalid resident demand identity.");
                if(pool.Count==0 || !resident.IsAlive || resident.AgeGroup!=ResidentAgeGroup.Adult || resident.IsQuestProtected || resident.NativeIdentity!=null) continue;
                uint seed=StableSeed.Combine(worldSeed,(uint)town,(uint)day,IdentitySeed(resident.Id));
                if(seed%1000>=chancePerThousand) continue;
                uint timing=StableSeed.Combine(seed,0x74696d65,0,0);
                uint purpose=StableSeed.Combine(seed,0x76697369,0,0)%100;
                result.Add(new ResidentTravelProposal {ResidentId=resident.Id,HomeSettlement=town,
                    DestinationSettlement=pool[(int)(StableSeed.Combine(seed,0x64657374,0,0)%(uint)pool.Count)],
                    DesiredDeparture=(double)day*86400+6*3600+timing%(8*3600),
                    VisitDays=1+(int)(StableSeed.Combine(seed,0x64617973,0,0)%4),
                    Purpose=purpose<35 ? ResidentVisitPurpose.Shop : purpose<60 ? ResidentVisitPurpose.Temple :
                        purpose<80 ? ResidentVisitPurpose.Guild : purpose<95 ? ResidentVisitPurpose.Social : ResidentVisitPurpose.Castle});
            }
            result.Sort((a,b)=>{int order=a.DesiredDeparture.CompareTo(b.DesiredDeparture); return order!=0 ? order : string.CompareOrdinal(a.ResidentId,b.ResidentId);});
            if(result.Count>maximumDepartures) result.RemoveRange(maximumDepartures,result.Count-maximumDepartures);
            return result;
        }
        private static uint IdentitySeed(string id)
        {unchecked {uint hash=2166136261; foreach(char c in id) hash=(hash^c)*16777619; return hash;}}
    }
    public sealed partial class WorldStateStore
    {
        public List<ResidentTravelProposal> ReserveResidentTravelProposals(int town,int day,IEnumerable<HomeProfile> homes,
            IEnumerable<int> destinations,int chancePerThousand=10,int maximumDepartures=8)
        {
            var saved=state.Settlements.Find(s=>s.SettlementId==town);
            if(saved!=null && !saved.IsPartial)
                return ResidentTravelProposals(town,day,destinations,chancePerThousand,maximumDepartures);
            if(homes==null) throw new ArgumentNullException(nameof(homes));
            var profiles=new List<HomeProfile>(homes);
            var preview=Population.CreateFamilies(state.WorldSeed,town,profiles);
            if(saved!=null) foreach(var household in saved.Households)
            {
                int index=preview.Households.FindIndex(h=>h.BuildingKey==household.BuildingKey);
                if(index<0) throw new ArgumentException("Reserved hometown layout no longer contains its household.");
                preview.Households[index]=household;
            }
            var residents=new List<Resident>();
            foreach(var household in preview.Households) residents.AddRange(household.Residents);
            var proposals=ResidentTravelDemand.Select(state.WorldSeed,town,day,residents,destinations,chancePerThousand,maximumDepartures);
            proposals.RemoveAll(p=>{
                ResidentTrip trip;return residentTripIndex.TryGetValue(p.ResidentId,out trip) && p.DesiredDeparture<trip.HomeAt;
            });
            foreach(var proposal in proposals)
            {
                var resident=residents.Find(p=>p.Id==proposal.ResidentId);
                ReserveHousehold(town,profiles.Find(h=>h.BuildingKey==resident.HomeKey));
            }
            return proposals;
        }
        public List<ResidentTravelProposal> ResidentTravelProposals(int town,int day,IEnumerable<int> destinations,
            int chancePerThousand=10,int maximumDepartures=8)
        {
            var residents=new List<Resident>();
            var settlement=state.Settlements.Find(s=>s.SettlementId==town);
            if(settlement!=null) foreach(var household in settlement.Households) residents.AddRange(household.Residents);
            var proposals=ResidentTravelDemand.Select(state.WorldSeed,town,day,residents,destinations,chancePerThousand,maximumDepartures);
            proposals.RemoveAll(p=>{
                ResidentTrip existing;
                return residentTripIndex.TryGetValue(p.ResidentId,out existing) && p.DesiredDeparture<existing.HomeAt;
            });
            return proposals;
        }
    }
}
