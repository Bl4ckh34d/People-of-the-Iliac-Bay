using System;
using System.Collections.Generic;

namespace LivingCities.Core
{
    public enum CombatOrigin { HostileAttacker, Civilian, PassiveEnemy }
    [Serializable]
    public sealed class DefenseWitness
    {
        public string ObserverId;
        public bool Guard;
        public double SeenAt;
    }
    [Serializable]
    public sealed class DefenseDeath
    {
        public double DiedAt, X, Y, Z, WelfareExpiresAt;
        public int BuildingKey, WelfareSeconds, PotionPercent;
        public string CorpseId, WelfareGuardId;
        public bool KilledByPlayer, Lawful, LootEntitled, WelfareEligible, PotionOffered;
        public bool WelfareExpired, WelfareConsumed, PotionGranted;
        public double ExpiredAt = -1, ConsumedAt = -1;
        public List<string> InformedGuards = new List<string>();
    }
    [Serializable]
    public sealed class DefenseCombatant
    {
        public string Id;
        public int SettlementId;
        public CombatOrigin Origin;
        public double ObservedAt;
        public double UnprovokedAttackAt = -1;
        public bool PlayerProvoked;
        public double FirstProvokedAt = -1, FirstPlayerHitAt = -1, LastPlayerHitAt = -1;
        public List<DefenseWitness> Witnesses = new List<DefenseWitness>();
        public DefenseDeath Death;
    }
    public sealed class WelfareResult
    {
        public bool Completed, PotionGranted;
    }
    public sealed partial class WorldStateStore
    {
        public DefenseCombatant GetDefenseCombatant(string id) { return state.DefenseCombatants.Find(c=>c.Id==id); }
        // Called from authoritative combat provenance, not merely a changed hostile flag.
        public bool RecordDefenseUnprovokedAttack(string id,double now)
        {
            var c=DefenseRecord(id);
            Require(c.Death==null && DefenseTime(now) && now>=c.ObservedAt && now>=c.LastPlayerHitAt &&
                now>=c.UnprovokedAttackAt,"Invalid unprovoked attack observation.");
            if(c.Origin==CombatOrigin.Civilian || c.PlayerProvoked) return false;
            if(c.UnprovokedAttackAt<0) c.UnprovokedAttackAt=now;
            return true;
        }
        private DefenseCombatant DefenseRecord(string id)
        { var c=GetDefenseCombatant(id); Require(c!=null,"Unknown defense combatant."); return c; }
        public DefenseCombatant RegisterDefenseCombatant(string id,int settlementId,CombatOrigin origin,double now)
        {
            Require(DefenseId(id) && DefenseTime(now) && Enum.IsDefined(typeof(CombatOrigin),origin) &&
                state.Settlements.Exists(s=>s.SettlementId==settlementId),"Invalid defense combatant.");
            var c=GetDefenseCombatant(id);
            if(c!=null) { Require(c.SettlementId==settlementId && now>=c.ObservedAt,"Defense identity changed."); return c; }
            c=new DefenseCombatant { Id=id,SettlementId=settlementId,Origin=origin,ObservedAt=now };
            state.DefenseCombatants.Add(c); return c;
        }
        public void RecordDefensePlayerHit(string id,double now,bool wasHostile)
        {
            var c=DefenseRecord(id);
            Require(c.Death==null && DefenseTime(now) && now>=c.ObservedAt && now>=c.LastPlayerHitAt && now>=c.UnprovokedAttackAt,"Invalid defense hit time.");
            if(c.FirstPlayerHitAt<0) c.FirstPlayerHitAt=now;
            c.LastPlayerHitAt=now;
            if(!wasHostile && !c.PlayerProvoked) { c.PlayerProvoked=true; c.FirstProvokedAt=now; }
        }
        // The native adapter calls this only for an observer who actually saw the
        // player's participation. Seeing a body or a guard fighting is not evidence.
        public void RecordDefenseWitness(string id,string observerId,bool guard,double now)
        {
            var c=DefenseRecord(id);
            Require(c.Death==null && DefenseTime(now) && now>=c.ObservedAt &&
                DefenseObserver(state,c.SettlementId,observerId,guard,true),"Invalid defense witness.");
            var witness=c.Witnesses.Find(w=>w.ObserverId==observerId);
            Require(witness==null || witness.Guard==guard && now>=witness.SeenAt,"Defense witness changed or time reversed.");
            if(witness==null) c.Witnesses.Add(new DefenseWitness { ObserverId=observerId,Guard=guard,SeenAt=now });
            else witness.SeenAt=now;
        }
        public DefenseCombatant ConfirmDefenseDeath(string id,double now,int buildingKey,double x,double y,double z,
            bool killedByPlayer,int welfareSeconds,int potionPercent)
        {
            var c=DefenseRecord(id);
            Require(DefenseTime(now) && now>=c.ObservedAt && now>=c.LastPlayerHitAt && now>=c.UnprovokedAttackAt,"Invalid defense death time.");
            if(c.Death!=null) { Require(now>=c.Death.DiedAt,"Death time reversed."); return c; }
            Require(buildingKey>=-1 && DefenseCoordinate(x) && DefenseCoordinate(y) && DefenseCoordinate(z) &&
                welfareSeconds>=0 && welfareSeconds<=3600 && DefenseTime(now+welfareSeconds) && potionPercent>=0 && potionPercent<=100 &&
                (!killedByPlayer || c.FirstPlayerHitAt>=0) && !c.Witnesses.Exists(w=>w.SeenAt>now),"Invalid defense death.");
            bool lawful=LawfulDefense(c);
            var d=new DefenseDeath { DiedAt=now,BuildingKey=buildingKey,X=x,Y=y,Z=z,KilledByPlayer=killedByPlayer,
                Lawful=lawful,LootEntitled=lawful && killedByPlayer,WelfareSeconds=welfareSeconds,WelfareExpiresAt=now+welfareSeconds,PotionPercent=potionPercent };
            d.WelfareEligible=DefenseEligible(c,d);
            d.PotionOffered=d.WelfareEligible && DefensePotion(state.WorldSeed,c.Id,potionPercent);
            foreach(var w in c.Witnesses)
                if(w.Guard && RecentDefenseWitness(c,d,w) && DefenseObserver(state,c.SettlementId,w.ObserverId,true,true)) d.InformedGuards.Add(w.ObserverId);
            c.Death=d; return c;
        }
        public void BindDefenseCorpse(string id,string corpseId)
        {
            var c=DefenseRecord(id);
            Require(c.Death!=null && DefenseId(corpseId) && (c.Death.CorpseId==null || c.Death.CorpseId==corpseId) &&
                !state.DefenseCombatants.Exists(other=>other!=c && other.Death!=null && other.Death.CorpseId==corpseId),"Invalid defense corpse binding.");
            c.Death.CorpseId=corpseId;
        }
        public void InformDefenseGuard(string id,string witnessId,string guardId,double now)
        {
            var c=DefenseRecord(id); var d=c.Death; var w=c.Witnesses.Find(item=>item.ObserverId==witnessId);
            Require(d!=null && DefenseTime(now) && now>=d.DiedAt && w!=null && RecentDefenseWitness(c,d,w) &&
                DefenseObserver(state,c.SettlementId,witnessId,w.Guard,true) && DefenseObserver(state,c.SettlementId,guardId,true,true),"Invalid defense witness handoff.");
            if(!d.InformedGuards.Contains(guardId)) d.InformedGuards.Add(guardId);
        }
        public void ObserveDefenseScene(string id,bool onSite,double now)
        {
            var d=DefenseRecord(id).Death;
            Require(d!=null && DefenseTime(now) && now>=d.DiedAt,"Invalid welfare observation.");
            if(!d.WelfareConsumed && !d.WelfareExpired && (!onSite || now>=d.WelfareExpiresAt))
            { d.WelfareExpired=true; d.ExpiredAt=now; }
        }
        public void ObserveDefensePresence(int? settlementId,int buildingKey,double x,double y,double z,double now,double radius)
        {
            Require(DefenseTime(now) && buildingKey>=-1 && DefenseCoordinate(x) && DefenseCoordinate(y) &&
                DefenseCoordinate(z) && radius>0 && radius<=1000,"Invalid welfare presence.");
            foreach(var c in state.DefenseCombatants)
            {
                var d=c.Death;
                if(d==null || d.WelfareConsumed || d.WelfareExpired || now<d.DiedAt) continue;
                double dx=x-d.X,dy=y-d.Y,dz=z-d.Z;
                bool present=settlementId==c.SettlementId && buildingKey==d.BuildingKey &&
                    dx*dx+dy*dy+dz*dz<=radius*radius;
                ObserveDefenseScene(c.Id,present,now);
            }
        }
        public WelfareResult CompleteDefenseWelfare(string id,string guardId,double now,bool playerOnSite,bool acceptPotion)
        {
            var c=DefenseRecord(id); ObserveDefenseScene(id,playerOnSite,now); var d=c.Death;
            if(!d.WelfareEligible || d.WelfareExpired || d.WelfareConsumed || !d.InformedGuards.Contains(guardId) ||
                !DefenseObserver(state,c.SettlementId,guardId,true,true)) return new WelfareResult();
            d.WelfareConsumed=true; d.ConsumedAt=now; d.WelfareGuardId=guardId; d.PotionGranted=d.PotionOffered && acceptPotion;
            return new WelfareResult { Completed=true,PotionGranted=d.PotionGranted };
        }
        private static bool DefenseId(string value) { return !string.IsNullOrWhiteSpace(value) && value.Length<=512; }
        private static bool DefenseTime(double value) { return !double.IsNaN(value) && !double.IsInfinity(value) && value>=0; }
        private static bool DefenseCoordinate(double value) { return !double.IsNaN(value) && Math.Abs(value)<=1000000; }
        private static bool DefenseObserver(WorldState world,int settlement,string id,bool guard,bool living)
        {
            if(!DefenseId(id)) return false;
            if(guard) { var roster=world.Garrisons.Find(g=>g.SettlementId==settlement); return roster!=null && roster.Guards.Exists(g=>g.Id==id && (!living || g.IsAlive)); }
            var town=world.Settlements.Find(t=>t.SettlementId==settlement);
            if(town!=null && town.Households.Exists(h=>h.Residents.Exists(r=>r.Id==id && (!living || r.IsAlive)))) return true;
            return world.NativePeople.Exists(p=>p.SettlementId==settlement && p.Id==id && (!living || p.IsAlive));
        }
        private static bool RecentDefenseWitness(DefenseCombatant c,DefenseDeath d,DefenseWitness w)
        { return c.FirstPlayerHitAt>=0 && w.SeenAt>=c.FirstPlayerHitAt && w.SeenAt<=d.DiedAt && d.DiedAt-w.SeenAt<=300; }
        private static bool DefenseEligible(DefenseCombatant c,DefenseDeath d)
        { return d.Lawful && c.LastPlayerHitAt>=0 && d.DiedAt-c.LastPlayerHitAt<=300 && c.Witnesses.Exists(w=>RecentDefenseWitness(c,d,w)); }
        private static bool LawfulDefense(DefenseCombatant c)
        { return !c.PlayerProvoked && (c.Origin==CombatOrigin.HostileAttacker || c.Origin==CombatOrigin.PassiveEnemy && c.UnprovokedAttackAt>=0); }
        private static bool DefensePotion(uint seed,string id,int percent)
        {
            uint hash=2166136261; unchecked { foreach(char ch in id) hash=(hash^ch)*16777619; }
            return StableSeed.Combine(seed,hash,0,0)%100<percent;
        }
    }
}
