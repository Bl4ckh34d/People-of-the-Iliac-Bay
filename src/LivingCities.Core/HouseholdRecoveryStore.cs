using System;
using System.Collections.Generic;

namespace LivingCities.Core
{
    public sealed partial class WorldStateStore
    {
        public SettlementPopulation CurrentPopulation(int settlementId)
        { return state.Settlements.Find(s=>s.SettlementId==settlementId); }

        public List<HouseholdRecoveryReservation> HouseholdRecoveryReservations(int settlementId)
        {
            var result=new List<HouseholdRecoveryReservation>();
            foreach(var item in state.HouseholdRecoveries)
                if(item.SettlementId==settlementId) result.Add(HouseholdRecoveryReservation.Copy(item));
            return result;
        }

        // Runtime supplies property evidence. Death, absent-owner and corpse evidence come from the ledger.
        private HouseholdRecoveryEligibility RecoveryFacts(int settlementId,Household home,
            HouseholdRecoveryEligibility property,out double lastDeath,out bool cleared)
        {
            if(property==null) throw new ArgumentNullException(nameof(property));
            lastDeath=0;cleared=true;
            bool confirmed=home.Residents.Count>0,living=false;
            var permanentIds=new HashSet<string>();
            foreach(var person in home.Residents)
            {
                permanentIds.Add(person.Id);
                if(person.IsAlive) {living=true;confirmed=false;continue;}
                var incident=state.AlarmIncidents.Find(a=>a.VictimId==person.Id && a.Corpse!=null);
                if(incident==null) {confirmed=false;cleared=false;continue;}
                lastDeath=Math.Max(lastDeath,incident.Corpse.DiedAt);
                cleared &= incident.Corpse.Cleared;
            }
            bool nativeAlive=false,worksite=false;
            int permanentCount=home.Residents.Count;
            foreach(var native in state.NativePeople)
            {
                if(native.SettlementId!=settlementId) continue;
                bool assignedHome=native.HomeKey==home.BuildingKey && native.BedSlot>=0;
                bool unassignedFlatOccupant=native.HomeKey<0 && native.WorkKey==home.BuildingKey;
                bool assignedWorker=native.WorkKey==home.BuildingKey && native.HomeKey>=0;
                // A reserved native bed or a native assigned to work here remains a worksite
                // exclusion until its bed reservation can be released safely.
                worksite |= assignedHome || assignedWorker;
                if(assignedHome && native.IsAlive) nativeAlive=true;
                if(!unassignedFlatOccupant || !permanentIds.Add(native.Id)) continue;
                permanentCount++;
                if(native.IsAlive) {nativeAlive=true;living=true;confirmed=false;continue;}
                var incident=state.AlarmIncidents.Find(a=>a.VictimId==native.Id && a.Corpse!=null);
                if(incident==null) {confirmed=false;cleared=false;continue;}
                lastDeath=Math.Max(lastDeath,incident.Corpse.DiedAt);
                cleared &= incident.Corpse.Cleared;
            }
            return new HouseholdRecoveryEligibility {AllPermanentResidentsConfirmedDead=confirmed,
                PriorPermanentResidentCount=permanentCount,HasLivingPermanentResident=living,
                HasLivingNativeOwner=nativeAlive,BedCount=property.BedCount,PlayerOwned=property.PlayerOwned,
                Factioned=property.Factioned,ActiveQuest=property.ActiveQuest,Worksite=property.Worksite || worksite,
                PartialSettlement=CurrentPopulation(settlementId).IsPartial || property.PartialSettlement};
        }

        public HouseholdRecoveryReservation EnsureHouseholdRecovery(int settlementId,int buildingKey,
            HouseholdRecoveryTier tier,HouseholdRecoveryEligibility property,HouseholdVacancyOutcome outcome)
        {
            var town=CurrentPopulation(settlementId);
            var home=town==null ? null : town.Households.Find(h=>h.BuildingKey==buildingKey);
            if(home==null) return null;
            var existing=state.HouseholdRecoveries.Find(r=>r.SettlementId==settlementId && r.BuildingKey==buildingKey && r.Generation==home.Generation);
            if(existing!=null) return HouseholdRecoveryReservation.Copy(existing);
            double diedAt;bool cleared;
            var facts=RecoveryFacts(settlementId,home,property,out diedAt,out cleared);
            var decision=HouseholdRecoveryPolicy.Evaluate(state.WorldSeed,settlementId,buildingKey,home.Generation,diedAt,tier,facts);
            if(!decision.IsEligible) return null;
            var reservation=HouseholdRecoveryReservation.Create(decision,outcome);
            state.HouseholdRecoveries.Add(reservation);
            return HouseholdRecoveryReservation.Copy(reservation);
        }

        public bool AdvanceHouseholdRecovery(int settlementId,int buildingKey,double now,HomeProfile profile,
            HouseholdRecoveryEligibility property,bool playerOutside)
        {
            Require(ValidTime(now),"Invalid recovery time.");
            var town=CurrentPopulation(settlementId);
            var home=town==null ? null : town.Households.Find(h=>h.BuildingKey==buildingKey);
            if(home==null) return false;
            var reservation=state.HouseholdRecoveries.Find(r=>r.SettlementId==settlementId && r.BuildingKey==buildingKey && r.Generation==home.Generation);
            if(reservation==null || reservation.Status!=HouseholdRecoveryStatus.Pending) return false;
            double diedAt;bool cleared;
            var facts=RecoveryFacts(settlementId,home,property,out diedAt,out cleared);
            if(diedAt!=reservation.ConfirmedLastDeathAt || !reservation.CanActivate(now,facts,playerOutside,cleared)) return false;
            if(reservation.Outcome==HouseholdVacancyOutcome.BankSale)
            {reservation.Status=HouseholdRecoveryStatus.Listed;return false;}
            Require(profile!=null && profile.BuildingKey==buildingKey && profile.BedCount==facts.BedCount,"Recovery profile differs from current property.");
            var family=HouseholdGeneration.Create(state.WorldSeed,settlementId,profile,home.Generation+1);
            ArchiveHouseholdAndInstall(settlementId,buildingKey,home.Generation,now,family);
            return true;
        }

        public bool MarkVacantHouseSold(int settlementId,int buildingKey,double now)
        {
            Require(ValidTime(now),"Invalid house sale time.");
            var reservation=state.HouseholdRecoveries.Find(r=>r.SettlementId==settlementId && r.BuildingKey==buildingKey &&
                r.Outcome==HouseholdVacancyOutcome.BankSale && r.Status==HouseholdRecoveryStatus.Listed);
            if(reservation==null || now<reservation.RecoveryAt) return false;
            reservation.Status=HouseholdRecoveryStatus.Sold;return true;
        }

        private static List<HouseholdRecoveryReservation> CopyHouseholdRecoveries(WorldState source,WorldState target)
        {
            var result=new List<HouseholdRecoveryReservation>();
            if(source.Version<20 || source.HouseholdRecoveries==null) return result;
            var keys=new HashSet<string>();
            foreach(var raw in source.HouseholdRecoveries)
            {
                var item=HouseholdRecoveryReservation.Copy(raw);
                Require(item.WorldSeed==target.WorldSeed && keys.Add(HistoryKey(item.SettlementId,item.BuildingKey,item.Generation)),"Duplicate or foreign recovery reservation.");
                var town=target.Settlements.Find(t=>t.SettlementId==item.SettlementId);
                var current=town==null ? null : town.Households.Find(h=>h.BuildingKey==item.BuildingKey);
                Require(current!=null,"Recovery household is missing.");
                var home=current;
                if(item.Status==HouseholdRecoveryStatus.Settled)
                {
                    var history=target.HouseholdHistory.Find(h=>h.SettlementId==item.SettlementId && h.Household.BuildingKey==item.BuildingKey && h.Household.Generation==item.Generation);
                    Require(history!=null && history.RetiredAt>=item.RecoveryAt,"Settled recovery lacks retired household.");
                    home=history.Household;
                }
                Require(home.Generation==item.Generation && home.Residents.Count>0 && home.Residents.TrueForAll(r=>!r.IsAlive),"Recovery references a living or different household.");
                double last=0;
                var permanentIds=new HashSet<string>();
                foreach(var member in home.Residents)
                {
                    permanentIds.Add(member.Id);
                    var incident=target.AlarmIncidents.Find(a=>a.VictimId==member.Id && a.Corpse!=null);
                    Require(incident!=null,"Recovery lacks confirmed death evidence.");
                    last=Math.Max(last,incident.Corpse.DiedAt);
                    Require(item.Status==HouseholdRecoveryStatus.Pending || incident.Corpse.Cleared,"Activated recovery has an uncleared corpse.");
                }
                foreach(var native in target.NativePeople)
                {
                    if(native.SettlementId!=item.SettlementId) continue;
                    bool assignedHome=native.HomeKey==item.BuildingKey && native.BedSlot>=0;
                    bool assignedWorker=native.WorkKey==item.BuildingKey && native.HomeKey>=0;
                    Require(!assignedHome && !assignedWorker,"Recovery references a reserved native worksite.");
                    if(native.HomeKey>=0 || native.WorkKey!=item.BuildingKey || !permanentIds.Add(native.Id)) continue;
                    Require(!native.IsAlive,"Recovery references a living native flat occupant.");
                    var incident=target.AlarmIncidents.Find(a=>a.VictimId==native.Id && a.Corpse!=null);
                    Require(incident!=null,"Recovery lacks confirmed native death evidence.");
                    last=Math.Max(last,incident.Corpse.DiedAt);
                    Require(item.Status==HouseholdRecoveryStatus.Pending || incident.Corpse.Cleared,"Activated recovery has an uncleared native corpse.");
                }
                Require(last==item.ConfirmedLastDeathAt,"Recovery extinction timestamp differs from death evidence.");
                result.Add(item);
            }
            return result;
        }
    }
}
