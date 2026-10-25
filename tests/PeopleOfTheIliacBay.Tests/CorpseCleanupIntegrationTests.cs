using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class CorpseCleanupIntegrationTests
    {
        private static WorldStateStore Create(out string observer,out string guard)
        {
            var store=new WorldStateStore(51);var town=store.GetOrCreate(1,new[]{7,8,9},1);
            observer=town.Households[0].Residents[0].Id;
            guard=store.GetOrCreateGuards(1,2,new[]{new SavedRoutePoint()}).Guards[0].Id;
            return store;
        }
        private static DefenseCombatant Dead(WorldStateStore store,string id,CombatOrigin origin,bool provoke=false)
        {
            store.RegisterDefenseCombatant(id,1,origin,100);
            if(provoke)store.RecordDefensePlayerHit(id,110,false);
            var record=store.ConfirmDefenseDeath(id,120,-1,10,0,0,false,300,0);
            store.BindDefenseCorpse(id,"loot:"+id);return record;
        }
        private static void Dispatch(WorldStateStore store,string guard,string corpse)
        {
            store.GetGuards(1).DispatchCorpseCleanup("cleanup:test",corpse,130,
                new Dictionary<string,List<SavedRoutePoint>>{{guard,new List<SavedRoutePoint>{new SavedRoutePoint(),new SavedRoutePoint{X=10}}}},1,3,12);
        }
        internal static void Add(List<(string,Action)> cases)
        {
            cases.Add(("Aggressor corpse reports persist without attributing a new murder",()=>{
                string observer,guard;var store=Create(out observer,out guard);var record=Dead(store,"native:1",CombatOrigin.HostileAttacker);
                var report=store.NoticeDefenseCorpse(record.Id,observer,false,125);
                report.Deferred=true;report.HasPosition=true;report.X=2;report.Z=3;
                var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
                var saved=restored.DefenseCorpsesFor(1)[0];
                Check.True(saved.Death.Cleanup.Reports[0].Report.Deferred);
                Check.Near(2,saved.Death.Cleanup.Reports[0].Report.X);
                Check.Equal(0,restored.Snapshot().AlarmIncidents.Count);
                Check.Equal(1,saved.Death.Cleanup.Reports.Count);
                store.NoticeDefenseCorpse(record.Id,observer,false,126);
                Check.Equal(1,record.Death.Cleanup.Reports.Count);
            }));
            cases.Add(("Cleanup dispatch has no suspect or investigation and requires physical guard arrival",()=>{
                string observer,guard;var store=Create(out observer,out guard);var record=Dead(store,"native:1",CombatOrigin.HostileAttacker);
                store.NoticeDefenseCorpse(record.Id,observer,false,125);Dispatch(store,guard,record.Death.CorpseId);
                var response=store.GetGuards(1).Reports[0];
                Check.True(response.ThreatId==null&&!response.Investigation&&!response.CrimeScene);
                Check.Equal(record.Death.CorpseId,response.CleanupCorpseId);
                Check.True(!store.TryClearDefenseCorpse(record.Id,guard,0,0,0,140));
                Check.True(!store.TryClearDefenseCorpse(record.Id,guard,10,10,0,140));
                Check.True(!record.Death.Cleanup.Cleared);
                Check.True(store.TryClearDefenseCorpse(record.Id,guard,10,0,0,175));
                Check.True(!store.TryClearDefenseCorpse(record.Id,guard,10,0,0,176));
                var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
                Check.True(restored.DefenseCorpsesFor(1)[0].Death.Cleanup.Cleared);
                Check.Equal(guard,restored.DefenseCorpsesFor(1)[0].Death.Cleanup.ClearedBy);
                Check.True(restored.GetGuards(1).Reports[0].ThreatId==null);
                var serializer=new FullSerializer.fsSerializer();FullSerializer.fsData data;
                Check.True(serializer.TrySerialize(store.Snapshot(),out data).Succeeded);
                WorldState disk=null;Check.True(serializer.TryDeserialize(data,ref disk).Succeeded);
                var nativeReload=new WorldStateStore(0);nativeReload.Restore(disk);
                Check.True(nativeReload.DefenseCorpsesFor(1)[0].Death.Cleanup.Cleared);
                Check.Equal(record.Death.CorpseId,nativeReload.GetGuards(1).Reports[0].CleanupCorpseId);
                var forged=store.Snapshot();forged.Garrisons[0].Reports[0].ThreatId="invented suspect";
                Check.Throws<ArgumentException>(()=>nativeReload.Restore(forged));
            }));
            cases.Add(("Passive and player-provoked deaths do not become cleanup-only aggressors",()=>{
                string observer,guard;var store=Create(out observer,out guard);
                Dead(store,"passive",CombatOrigin.PassiveEnemy);
                Dead(store,"provoked",CombatOrigin.HostileAttacker,true);
                Check.Equal(0,store.DefenseCorpsesFor(1).Count);
                Check.Throws<ArgumentException>(()=>store.NoticeDefenseCorpse("passive",observer,false,130));
                Check.Throws<ArgumentException>(()=>store.NoticeDefenseCorpse("provoked",observer,false,130));
                var third=store.RegisterDefenseCombatant("attacker",1,CombatOrigin.PassiveEnemy,130);
                Check.True(store.RecordDefenseUnprovokedAttack(third.Id,131));
                store.ConfirmDefenseDeath(third.Id,140,-1,0,0,0,false,300,0);store.BindDefenseCorpse(third.Id,"loot:third");
                Check.Equal(1,store.DefenseCorpsesFor(1).Count);
            }));
            cases.Add(("Failed native corpse removal never records cleanup or invokes the wrong UID",()=>{
                string observer,guard;var store=Create(out observer,out guard);var record=Dead(store,"native:1",CombatOrigin.HostileAttacker);
                store.NoticeDefenseCorpse(record.Id,observer,false,125);Dispatch(store,guard,record.Death.CorpseId);
                int calls=0;
                Check.True(!store.TryExecuteDefenseCorpseCleanup(record.Id,guard,0,0,0,175,id=>{calls++;return true;}));
                Check.Equal(0,calls);
                Check.True(!store.TryExecuteDefenseCorpseCleanup(record.Id,guard,10,0,0,175,id=>{
                    Check.Equal(record.Death.CorpseId,id);calls++;return false;}));
                Check.True(!record.Death.Cleanup.Cleared);Check.Equal(1,calls);
                Check.True(store.TryExecuteDefenseCorpseCleanup(record.Id,guard,10,0,0,175,id=>{
                    Check.Equal(record.Death.CorpseId,id);calls++;return true;}));
                Check.Equal(2,calls);Check.True(record.Death.Cleanup.Cleared);
            }));
            cases.Add(("Cleanup rejects unknown observers and forged removal on restore",()=>{
                string observer,guard;var store=Create(out observer,out guard);var record=Dead(store,"native:1",CombatOrigin.HostileAttacker);
                Check.Throws<ArgumentException>(()=>store.NoticeDefenseCorpse(record.Id,"unknown",false,125));
                store.NoticeDefenseCorpse(record.Id,observer,false,125);
                var bad=store.Snapshot();bad.DefenseCombatants[0].Death.Cleanup.Cleared=true;
                bad.DefenseCombatants[0].Death.Cleanup.ClearedAt=126;bad.DefenseCombatants[0].Death.Cleanup.ClearedBy=guard;
                Check.Throws<ArgumentException>(()=>store.Restore(bad));
                Check.True(!record.Death.Cleanup.Cleared);
            }));
            cases.Add(("A single static witness reserves and completes the entire murder case",()=>{
                string observer,guard;var store=Create(out observer,out guard);var town=store.GetOrCreate(1,new[]{7,8,9},1);
                var victim=town.Households[1].Residents[0];var alarm=store.RecordViolence(1,victim.Id,100);
                store.LocateViolence(alarm.Id,-1,10,0,0);Population.MarkDead(town,victim.Id);store.ConfirmCorpse(alarm.Id,120,-1,10,0,0);store.MarkPlayerMurder(alarm.Id);
                var witnesses=new List<AlarmPerson>();var roster=store.GetGuards(1);
                for(int i=0;i<2;i++)
                {
                    var native=store.RegisterNativePerson(1,-1,100+i,"Witness "+i,ResidentSex.Male,0,0,182,i);
                    var w=store.Witness(alarm.Id,native.Id,121);w.StaticWitness=true;w.HasPosition=true;w.X=10;w.Reaction=CivilianReaction.Flee;witnesses.Add(w);
                    roster.Dispatch("witness:"+alarm.Id+":"+w.ResidentId+":test",122,
                        new Dictionary<string,List<SavedRoutePoint>>{{roster.Guards[i].Id,new List<SavedRoutePoint>{new SavedRoutePoint(),new SavedRoutePoint{X=10}}}},1,3,12);
                }
                Check.True(store.TryAssignStaticInterview(alarm.Id,witnesses[0].ResidentId,roster.Guards[0].Id,123));
                Check.True(!store.TryAssignStaticInterview(alarm.Id,witnesses[1].ResidentId,roster.Guards[1].Id,123));
                var pending=new WorldStateStore(0);pending.Restore(store.Snapshot());
                Check.True(!pending.TryAssignStaticInterview(alarm.Id,witnesses[1].ResidentId,roster.Guards[1].Id,124));
                pending.GetGuards(1).Kill(guard,125);
                Check.True(pending.TryAssignStaticInterview(alarm.Id,witnesses[1].ResidentId,roster.Guards[1].Id,126));
                Check.True(!store.TryDeliverStaticInterview(alarm.Id,witnesses[0].ResidentId,guard,0,0,0,130));
                Check.True(store.TryDeliverStaticInterview(alarm.Id,witnesses[0].ResidentId,guard,10,0,0,175));
                Check.True(!store.TryDeliverStaticInterview(alarm.Id,witnesses[0].ResidentId,guard,10,0,0,176));
                var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
                Check.True(!restored.TryAssignStaticInterview(alarm.Id,witnesses[1].ResidentId,roster.Guards[1].Id,176));
                Check.Equal(1,restored.Snapshot().AlarmIncidents[0].People.FindAll(p=>p.StatementDelivered).Count);
            }));
        }
    }
}
