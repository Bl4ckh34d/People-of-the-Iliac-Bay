using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        private static List<DefenseCombatant> CopyDefense(WorldState source,WorldState validated)
        {
            var result=new List<DefenseCombatant>();
            Require(source.DefenseCombatants!=null,"Missing defense ledger.");
            var ids=new HashSet<string>(StringComparer.Ordinal); var corpses=new HashSet<string>(StringComparer.Ordinal);
            foreach(var c in source.DefenseCombatants)
            {
                Require(c!=null && DefenseId(c.Id) && ids.Add(c.Id) && validated.Settlements.Exists(s=>s.SettlementId==c.SettlementId) &&
                    Enum.IsDefined(typeof(CombatOrigin),c.Origin) && DefenseTime(c.ObservedAt) && c.Witnesses!=null,"Invalid saved defense combatant.");
                Require(c.FirstPlayerHitAt==-1 && c.LastPlayerHitAt==-1 || DefenseTime(c.FirstPlayerHitAt) &&
                    c.FirstPlayerHitAt>=c.ObservedAt && DefenseTime(c.LastPlayerHitAt) && c.LastPlayerHitAt>=c.FirstPlayerHitAt,"Invalid saved player participation.");
                Require(c.PlayerProvoked ? DefenseTime(c.FirstProvokedAt) && c.FirstProvokedAt>=c.FirstPlayerHitAt &&
                    c.FirstPlayerHitAt>=0 && c.FirstProvokedAt<=c.LastPlayerHitAt : c.FirstProvokedAt==-1,"Invalid saved provocation.");
                Require(c.UnprovokedAttackAt==-1 || c.Origin!=CombatOrigin.Civilian && DefenseTime(c.UnprovokedAttackAt) &&
                    c.UnprovokedAttackAt>=c.ObservedAt,"Invalid saved unprovoked attack.");
                var shelteredResident=c.Shelter==null ? null : ResolveResident(validated,c.SettlementId,c.Id);
                Require(c.Shelter==null || shelteredResident!=null && shelteredResident.IsAlive && !shelteredResident.IsQuestProtected,
                    "Defense shelter lacks its living settlement resident.");
                var clone=new DefenseCombatant { Id=c.Id,SettlementId=c.SettlementId,Origin=c.Origin,ObservedAt=c.ObservedAt,
                    Shelter=CopyDefenseShelter(c.Shelter,c),
                    UnprovokedAttackAt=c.UnprovokedAttackAt,PlayerProvoked=c.PlayerProvoked,FirstProvokedAt=c.FirstProvokedAt,FirstPlayerHitAt=c.FirstPlayerHitAt,LastPlayerHitAt=c.LastPlayerHitAt };
                var witnesses=new HashSet<string>(StringComparer.Ordinal);
                foreach(var w in c.Witnesses)
                {
                    Require(w!=null && witnesses.Add(w.ObserverId) && DefenseTime(w.SeenAt) && w.SeenAt>=c.ObservedAt &&
                        DefenseObserver(validated,c.SettlementId,w.ObserverId,w.Guard,false),"Invalid saved defense witness.");
                    clone.Witnesses.Add(new DefenseWitness { ObserverId=w.ObserverId,Guard=w.Guard,SeenAt=w.SeenAt });
                }
                var d=c.Death;
                if(d!=null)
                {
                    Require(DefenseTime(d.DiedAt) && d.DiedAt>=c.ObservedAt && d.DiedAt>=c.LastPlayerHitAt && d.DiedAt>=c.UnprovokedAttackAt &&
                        !c.Witnesses.Exists(w=>w.SeenAt>d.DiedAt) && d.BuildingKey>=-1 && DefenseCoordinate(d.X) &&
                        DefenseCoordinate(d.Y) && DefenseCoordinate(d.Z) && d.WelfareSeconds>=0 && d.WelfareSeconds<=3600 &&
                        DefenseTime(d.WelfareExpiresAt) && d.WelfareExpiresAt==d.DiedAt+d.WelfareSeconds &&
                        d.PotionPercent>=0 && d.PotionPercent<=100 && d.InformedGuards!=null,"Invalid saved defense death.");
                    Require(!d.KilledByPlayer || c.FirstPlayerHitAt>=0,"Player kill lacks participation.");
                    Require(d.Lawful==LawfulDefense(c) &&
                        d.LootEntitled==(d.Lawful && d.KilledByPlayer) && d.WelfareEligible==DefenseEligible(c,d) &&
                        d.PotionOffered==(d.WelfareEligible && DefensePotion(source.WorldSeed,c.Id,d.PotionPercent)),"Forged defense rights.");
                    Require(d.CorpseId==null || DefenseId(d.CorpseId) && corpses.Add(d.CorpseId),"Invalid or duplicate corpse binding.");
                    Require(!(d.WelfareExpired && d.WelfareConsumed) &&
                        (d.WelfareExpired ? DefenseTime(d.ExpiredAt) && d.ExpiredAt>=d.DiedAt : d.ExpiredAt==-1),"Invalid welfare expiry.");
                    var informed=new HashSet<string>(StringComparer.Ordinal);
                    foreach(var guard in d.InformedGuards)
                        Require(informed.Add(guard) && DefenseObserver(validated,c.SettlementId,guard,true,false) &&
                            c.Witnesses.Exists(w=>RecentDefenseWitness(c,d,w)),"Invalid informed defense guard.");
                    Require(d.WelfareConsumed ? d.WelfareEligible && DefenseTime(d.ConsumedAt) && d.ConsumedAt>=d.DiedAt &&
                        d.ConsumedAt<d.WelfareExpiresAt && d.WelfareGuardId!=null && informed.Contains(d.WelfareGuardId) :
                        d.ConsumedAt==-1 && d.WelfareGuardId==null && !d.PotionGranted,"Invalid welfare consumption.");
                    Require(!d.PotionGranted || d.PotionOffered && d.WelfareConsumed,"Invalid welfare gift.");
                    clone.Death=new DefenseDeath { DiedAt=d.DiedAt,X=d.X,Y=d.Y,Z=d.Z,WelfareExpiresAt=d.WelfareExpiresAt,
                        BuildingKey=d.BuildingKey,WelfareSeconds=d.WelfareSeconds,PotionPercent=d.PotionPercent,CorpseId=d.CorpseId,
                        WelfareGuardId=d.WelfareGuardId,KilledByPlayer=d.KilledByPlayer,Lawful=d.Lawful,LootEntitled=d.LootEntitled,
                        WelfareEligible=d.WelfareEligible,PotionOffered=d.PotionOffered,WelfareExpired=d.WelfareExpired,WelfareConsumed=d.WelfareConsumed,
                        PotionGranted=d.PotionGranted,ExpiredAt=d.ExpiredAt,ConsumedAt=d.ConsumedAt,InformedGuards=new List<string>(d.InformedGuards) };
                    clone.Death.Cleanup=CopyDefenseCleanup(c,validated);
                }
                result.Add(clone);
            }
            return result;
        }
    }
}
