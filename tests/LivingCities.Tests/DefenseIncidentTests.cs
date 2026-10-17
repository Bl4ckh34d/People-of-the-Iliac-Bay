using System;
using System.Collections.Generic;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class DefenseIncidentTests
    {
        private static WorldStateStore Store(out string resident, out string guard,bool responding=true)
        {
            var store = new WorldStateStore(42); var town = store.GetOrCreate(1,new[] { 7 },1);
            resident = town.Households[0].Residents[0].Id;
            var roster=store.GetOrCreateGuards(1,1,new[] { new SavedRoutePoint() });
            guard = roster.Guards[0].Id;
            if(responding) roster.DispatchDefense("response:1","enemy:1",100,
                new Dictionary<string,List<SavedRoutePoint>> { [guard]=new List<SavedRoutePoint> {
                    new SavedRoutePoint(),new SavedRoutePoint { X=1 } } },1,2.8,12);
            store.RegisterDefenseCombatant("enemy:1",1,CombatOrigin.HostileAttacker,100);
            return store;
        }
        private static DefenseCombatant Death(WorldStateStore store, bool player = true, int chance = 100)
        {
            store.RecordDefensePlayerHit("enemy:1",110,true);
            return store.ConfirmDefenseDeath("enemy:1",120,-1,1,0,2,player,300,chance);
        }
        internal static void Add(List<(string,Action)> cases)
        {
            cases.Add(("Welfare requires living player health strictly below sixty-six percent",()=>{
                foreach(double health in new[] { 0.0,0.65,0.65999,0.66,0.75,1.0 })
                {
                    string witness,guard; var store=Store(out witness,out guard);
                    store.RecordDefensePlayerHit("enemy:1",110,true); store.RecordDefenseWitness("enemy:1",guard,true,111);
                    var record=Death(store);
                    bool eligible=health>0 && health<0.66;
                    Check.Equal(eligible,store.CanOfferDefenseWelfare("enemy:1",guard,130,true,health));
                    Check.True(!record.Death.WelfareConsumed);
                    Check.Equal(eligible,store.CompleteDefenseWelfare("enemy:1",guard,130,true,true,health).Completed);
                    Check.Equal(eligible,record.Death.PotionGranted);
                    Check.True(record.Death.LootEntitled);
                }
            }));
            cases.Add(("An informed guard cannot offer welfare without responding to this threat",()=>{
                string witness,guard; var store=Store(out witness,out guard,false);
                store.RecordDefensePlayerHit("enemy:1",110,true); store.RecordDefenseWitness("enemy:1",guard,true,111);
                var record=Death(store);
                Check.True(!store.CompleteDefenseWelfare("enemy:1",guard,130,true,true,0.5).Completed);
                Check.True(!record.Death.WelfareConsumed && !record.Death.PotionGranted);
            }));
            cases.Add(("Wrong-threat and returning responders cannot consume welfare",()=>{
                foreach(bool wrongThreat in new[] { true,false })
                {
                    string witness,guard; var store=Store(out witness,out guard);
                    store.RecordDefensePlayerHit("enemy:1",110,true); store.RecordDefenseWitness("enemy:1",guard,true,111);
                    Death(store);
                    var roster=store.GetOrCreateGuards(1,1,new[] { new SavedRoutePoint() });
                    if(wrongThreat) roster.Reports[0].ThreatId="another-enemy";
                    else roster.Advance(410);
                    Check.True(!store.CompleteDefenseWelfare("enemy:1",guard,wrongThreat?130:410,true,true,0.5).Completed);
                }
            }));
            cases.Add(("Welfare presence requires same settlement building and local radius",()=>{
                for(int scenario=0;scenario<5;scenario++)
                {
                    string witness,guard; var store=Store(out witness,out guard);
                    store.RecordDefensePlayerHit("enemy:1",110,true); store.RecordDefenseWitness("enemy:1",guard,true,111);
                    var record=store.ConfirmDefenseDeath("enemy:1",120,7,1,0,2,true,300,100);
                    store.ObserveDefensePresence(scenario==1 ? (int?)null : scenario==2 ? 2 : 1,
                        scenario==3 ? -1 : 7,scenario==4 ? 42 : 1,0,2,130,40);
                    Check.Equal(scenario!=0,record.Death.WelfareExpired);
                    var copy=new WorldStateStore(0); copy.Restore(store.Snapshot());
                    copy.ObserveDefensePresence(1,7,1,0,2,131,40);
                    Check.Equal(scenario!=0,copy.GetDefenseCombatant("enemy:1").Death.WelfareExpired);
                    Check.True(copy.GetDefenseCombatant("enemy:1").Death.LootEntitled);
                }
            }));
            cases.Add(("Welfare presence expires at deadline but preserves nearby opportunity before it",()=>{
                string witness,guard; var store=Store(out witness,out guard);
                store.RecordDefensePlayerHit("enemy:1",110,true); store.RecordDefenseWitness("enemy:1",guard,true,111);
                var record=Death(store);
                store.ObserveDefensePresence(1,-1,41,0,2,419,40);
                Check.True(!record.Death.WelfareExpired);
                store.ObserveDefensePresence(1,-1,1,0,2,420,40);
                Check.True(record.Death.WelfareExpired && record.Death.LootEntitled);
            }));
            cases.Add(("Quest-activated passive attacker qualifies without rewriting origin or pardoning provocation",()=>{
                foreach(var origin in new[] { CombatOrigin.PassiveEnemy,CombatOrigin.Civilian })
                foreach(bool provoked in new[] { false,true })
                {
                    var store=new WorldStateStore(42); store.GetOrCreate(1,new[] { 7 },1);
                    store.RegisterDefenseCombatant("target",1,origin,100);
                    if(provoked) store.RecordDefensePlayerHit("target",105,false);
                    bool allowed=origin==CombatOrigin.PassiveEnemy && !provoked;
                    Check.Equal(allowed,store.RecordDefenseUnprovokedAttack("target",110));
                    store.RecordDefensePlayerHit("target",115,true);
                    var record=store.ConfirmDefenseDeath("target",120,7,0,0,0,true,300,100);
                    Check.Equal(origin,record.Origin); Check.Equal(allowed,record.Death.LootEntitled);
                    var copy=new WorldStateStore(0); copy.Restore(store.Snapshot());
                    Check.Equal(allowed,copy.GetDefenseCombatant("target").Death.Lawful);
                }
            }));
            cases.Add(("Lawful hostile kill owns loot immediately without witnesses or guard arrival",()=>{
                string witness,guard; var store=Store(out witness,out guard); var record=Death(store);
                Check.True(record.Death.Lawful && record.Death.LootEntitled);
                Check.True(!record.Death.WelfareEligible); Check.Near(420,record.Death.WelfareExpiresAt);
                store.BindDefenseCorpse(record.Id,"loot:100");
                var copy=new WorldStateStore(0); copy.Restore(store.Snapshot());
                Check.True(copy.GetDefenseCombatant(record.Id).Death.LootEntitled);
                Check.Equal("loot:100",copy.GetDefenseCombatant(record.Id).Death.CorpseId);
            }));
            cases.Add(("Provoking passive enemies or hostile civilians cannot create lawful loot rights",()=>{
                foreach(var origin in new[] { CombatOrigin.Civilian,CombatOrigin.PassiveEnemy })
                {
                    var store=new WorldStateStore(42); store.GetOrCreate(1,new[] { 7 },1);
                    store.RegisterDefenseCombatant("target",1,origin,100);
                    store.RecordDefensePlayerHit("target",110,false);
                    store.RegisterDefenseCombatant("target",1,CombatOrigin.HostileAttacker,111);
                    store.RecordDefensePlayerHit("target",112,true);
                    var record=store.ConfirmDefenseDeath("target",120,7,1,2,3,true,300,100);
                    Check.Equal(origin,record.Origin); Check.True(record.PlayerProvoked);
                    Check.True(!record.Death.Lawful && !record.Death.LootEntitled && !record.Death.WelfareEligible);
                }
                string witness,guard; var hostile=Store(out witness,out guard);
                hostile.RecordDefensePlayerHit("enemy:1",110,false);
                hostile.RecordDefensePlayerHit("enemy:1",115,true);
                Check.True(!hostile.ConfirmDefenseDeath("enemy:1",120,-1,0,0,0,true,300,100).Death.Lawful);
            }));
            cases.Add(("Body discovery and stale fight witnesses cannot invent a welfare exchange",()=>{
                string witness,guard; var store=Store(out witness,out guard);
                store.RecordDefensePlayerHit("enemy:1",110,true);
                store.RecordDefenseWitness("enemy:1",witness,false,110);
                var record=store.ConfirmDefenseDeath("enemy:1",500,-1,0,0,0,true,300,100);
                Check.True(!record.Death.WelfareEligible);
                Check.Throws<ArgumentException>(()=>store.RecordDefenseWitness("enemy:1",guard,true,501));
                Check.True(!store.CompleteDefenseWelfare("enemy:1",guard,501,true,true,0.5).Completed);
            }));
            cases.Add(("Actual witness handoff permits one bounded welfare gift across reload",()=>{
                string witness,guard; var store=Store(out witness,out guard);
                store.RecordDefensePlayerHit("enemy:1",110,true); store.RecordDefenseWitness("enemy:1",witness,false,111);
                var record=store.ConfirmDefenseDeath("enemy:1",120,7,1,2,3,true,300,100);
                Check.True(!store.CompleteDefenseWelfare(record.Id,guard,130,true,true,0.5).Completed);
                store.InformDefenseGuard(record.Id,witness,guard,131);
                var result=store.CompleteDefenseWelfare(record.Id,guard,132,true,true,0.5);
                Check.True(result.Completed && result.PotionGranted && record.Death.WelfareConsumed);
                var copy=new WorldStateStore(0); copy.Restore(store.Snapshot());
                Check.True(!copy.CompleteDefenseWelfare(record.Id,guard,133,true,true,0.5).Completed);
                Check.True(copy.GetDefenseCombatant(record.Id).Death.PotionGranted);
            }));
            cases.Add(("Declining gift consumes courtesy and does not permit a later potion",()=>{
                string witness,guard; var store=Store(out witness,out guard);
                store.RecordDefensePlayerHit("enemy:1",110,true); store.RecordDefenseWitness("enemy:1",guard,true,111);
                var record=store.ConfirmDefenseDeath("enemy:1",120,-1,1,0,2,true,300,100);
                var result=store.CompleteDefenseWelfare(record.Id,guard,130,true,false,0.5);
                Check.True(result.Completed && !result.PotionGranted);
                Check.True(!store.CompleteDefenseWelfare(record.Id,guard,131,true,true,0.5).Completed);
            }));
            cases.Add(("Courtesy expires at exact deadline or departure without revoking loot",()=>{
                foreach(bool leave in new[] { false,true })
                {
                    string witness,guard; var store=Store(out witness,out guard);
                    store.RecordDefensePlayerHit("enemy:1",110,true); store.RecordDefenseWitness("enemy:1",guard,true,111);
                    var record=store.ConfirmDefenseDeath("enemy:1",120,-1,1,0,2,true,300,100);
                    store.ObserveDefenseScene(record.Id,!leave,leave?130:420);
                    var copy=new WorldStateStore(0); copy.Restore(store.Snapshot());
                    Check.True(!copy.CompleteDefenseWelfare(record.Id,guard,leave?131:420,true,true,0.5).Completed);
                    Check.True(copy.GetDefenseCombatant(record.Id).Death.WelfareExpired);
                    Check.True(copy.GetDefenseCombatant(record.Id).Death.LootEntitled);
                }
            }));
            cases.Add(("Completing welfare outside the scene expires rather than handing out gifts",()=>{
                string witness,guard; var store=Store(out witness,out guard);
                store.RecordDefensePlayerHit("enemy:1",110,true); store.RecordDefenseWitness("enemy:1",guard,true,111);
                Death(store); var result=store.CompleteDefenseWelfare("enemy:1",guard,130,false,true,0.5);
                Check.True(!result.Completed && store.GetDefenseCombatant("enemy:1").Death.WelfareExpired);
            }));
            cases.Add(("Guard finishing a witnessed player fight permits welfare but does not invent a player kill",()=>{
                string witness,guard; var store=Store(out witness,out guard);
                store.RecordDefensePlayerHit("enemy:1",110,true); store.RecordDefenseWitness("enemy:1",guard,true,111);
                var record=store.ConfirmDefenseDeath("enemy:1",120,-1,1,0,2,false,300,100);
                Check.True(record.Death.Lawful && record.Death.WelfareEligible && !record.Death.LootEntitled);
                Check.True(store.CompleteDefenseWelfare(record.Id,guard,130,true,true,0.5).Completed);
            }));
            cases.Add(("Witness death preserves earned rights while preventing a new physical handoff",()=>{
                string witness,guard; var store=Store(out witness,out guard);
                store.RecordDefensePlayerHit("enemy:1",110,true); store.RecordDefenseWitness("enemy:1",witness,false,111);
                Death(store);
                Population.MarkDead(store.GetOrCreate(1,new[] { 7 },1),witness);
                Check.Throws<ArgumentException>(()=>store.InformDefenseGuard("enemy:1",witness,guard,130));
                var copy=new WorldStateStore(0); copy.Restore(store.Snapshot());
                Check.True(copy.GetDefenseCombatant("enemy:1").Death.LootEntitled);
            }));
            cases.Add(("Repeated death and corpse binding preserve original deadline and gift decision",()=>{
                string witness,guard; var store=Store(out witness,out guard);
                store.RecordDefensePlayerHit("enemy:1",110,true); store.RecordDefenseWitness("enemy:1",guard,true,111);
                var original=store.ConfirmDefenseDeath("enemy:1",120,-1,1,0,2,true,300,100);
                var repeated=store.ConfirmDefenseDeath("enemy:1",121,7,9,9,9,false,600,0);
                Check.Near(420,repeated.Death.WelfareExpiresAt); Check.True(repeated.Death.PotionOffered);
                Check.Equal(-1,repeated.Death.BuildingKey); Check.Near(1,repeated.Death.X);
                store.BindDefenseCorpse(original.Id,"loot:100"); store.BindDefenseCorpse(original.Id,"loot:100");
                Check.Throws<ArgumentException>(()=>store.BindDefenseCorpse(original.Id,"loot:101"));
            }));
            cases.Add(("Defense validation rejects unknown witnesses, reversal, and forged gift rights transactionally",()=>{
                string witness,guard; var store=Store(out witness,out guard);
                Check.Throws<ArgumentException>(()=>store.RecordDefenseWitness("enemy:1","unknown",false,110));
                Check.Throws<ArgumentException>(()=>store.RecordDefensePlayerHit("enemy:1",99,true));
                Check.Throws<ArgumentException>(()=>store.RegisterDefenseCombatant("enemy:1",2,CombatOrigin.HostileAttacker,110));
                store.RecordDefensePlayerHit("enemy:1",110,true); store.RecordDefenseWitness("enemy:1",guard,true,111); Death(store);
                var broken=store.Snapshot(); broken.DefenseCombatants[0].Death.PotionGranted=true;
                Check.Throws<ArgumentException>(()=>store.Restore(broken));
                Check.True(!store.GetDefenseCombatant("enemy:1").Death.PotionGranted);
                broken=store.Snapshot(); broken.DefenseCombatants[0].Death.WelfareExpiresAt=119;
                Check.Throws<ArgumentException>(()=>store.Restore(broken));
                broken=store.Snapshot(); broken.DefenseCombatants[0].Origin=CombatOrigin.Civilian;
                Check.Throws<ArgumentException>(()=>store.Restore(broken));
            }));
            cases.Add(("Legacy schema six migrates to current schema without inventing combat or loot entitlements",()=>{
                var store=new WorldStateStore(42); var town=store.GetOrCreate(1,new[] { 7 },1);
                string id=town.Households[0].Residents[0].Id; var legacy=store.Snapshot();
                legacy.Version=6; legacy.DefenseCombatants=null;
                store.Restore(legacy); Check.Equal(22, store.Snapshot().Version);
                Check.Equal(0,store.Snapshot().DefenseCombatants.Count);
                Check.Equal(id,store.Snapshot().Settlements[0].Households[0].Residents[0].Id);
            }));
            cases.Add(("Defense snapshots isolate witness and informed guard collections",()=>{
                string witness,guard; var store=Store(out witness,out guard);
                store.RecordDefensePlayerHit("enemy:1",110,true); store.RecordDefenseWitness("enemy:1",guard,true,111);
                Death(store); var snapshot=store.Snapshot();
                snapshot.DefenseCombatants[0].Witnesses[0].SeenAt=112;
                snapshot.DefenseCombatants[0].Death.InformedGuards.Clear();
                Check.Near(111,store.GetDefenseCombatant("enemy:1").Witnesses[0].SeenAt);
                Check.Equal(1,store.GetDefenseCombatant("enemy:1").Death.InformedGuards.Count);
            }));
            cases.Add(("Malformed defense records reject restore without changing live state",()=>{
                string witness,guard; var store=Store(out witness,out guard);
                store.RecordDefensePlayerHit("enemy:1",110,true); store.RecordDefenseWitness("enemy:1",guard,true,111); Death(store);
                var corrupt=new Action<DefenseCombatant>[] {
                    c=>c.Death.X=double.NaN, c=>c.Death.WelfareExpiresAt=double.PositiveInfinity,
                    c=>c.FirstPlayerHitAt=-2, c=>c.LastPlayerHitAt=99, c=>c.Witnesses[0].ObserverId="missing",
                    c=>c.Witnesses[0].SeenAt=121, c=>c.Death.WelfareSeconds=-1,
                    c=>c.Death.InformedGuards.Add("missing"), c=>c.Death.WelfareConsumed=true,
                    c=>c.Death.PotionOffered=false, c=>c.Death.LootEntitled=false, c=>c.Witnesses=null
                };
                foreach(var change in corrupt)
                {
                    var broken=store.Snapshot(); change(broken.DefenseCombatants[0]);
                    Check.Throws<ArgumentException>(()=>store.Restore(broken));
                    Check.True(store.GetDefenseCombatant("enemy:1").Death.LootEntitled);
                }
            }));
            cases.Add(("Absent player participation and dead guards cannot grant welfare",()=>{
                string witness,guard; var store=Store(out witness,out guard);
                store.RecordDefenseWitness("enemy:1",guard,true,110);
                var record=store.ConfirmDefenseDeath("enemy:1",120,-1,0,0,0,false,300,100);
                Check.True(!record.Death.WelfareEligible && !record.Death.LootEntitled);
                Check.True(!store.CompleteDefenseWelfare(record.Id,guard,121,true,true,0.5).Completed);
                store=Store(out witness,out guard); store.RecordDefensePlayerHit("enemy:1",110,true);
                store.RecordDefenseWitness("enemy:1",guard,true,111); Death(store);
                var state=store.Snapshot(); state.Garrisons[0].Kill(guard,125); store.Restore(state);
                Check.True(!store.CompleteDefenseWelfare("enemy:1",guard,130,true,true,0.5).Completed);
                Check.True(store.GetDefenseCombatant("enemy:1").Death.LootEntitled);
            }));
        }
    }
}
