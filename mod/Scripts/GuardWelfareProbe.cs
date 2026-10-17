using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public static class GuardWelfareProbe
    {
        public static IEnumerator Run()
        {
            var game=GameManager.Instance; var room=game.PlayerEnterExit.Interior;
            var scene=DefenseScene.For(room); var navigation=new InteriorGeometry(room);
            var original=game.PlayerMotor.transform.position; int health=game.PlayerEntity.CurrentHealth;
            bool godMode=game.PlayerEntity.GodMode;
            var crime=game.PlayerEntity.CrimeCommitted;
            var destination=room.transform.InverseTransformPoint(original-Vector3.up);
            Vector3? origin=null;
            foreach(var anchor in navigation.LocalAnchors)
                if(Vector3.Distance(anchor,destination)>4 && Vector3.Distance(anchor,destination)<9 && navigation.RouteBetween(anchor,destination).Count>=2)
                { origin=anchor; break; }
            if(!origin.HasValue) throw new InvalidOperationException("No surveyed welfare approach fixture.");
            var store=new WorldStateStore(42,LivingCitiesMod.ConversationArchiveRoot()); var population=store.GetOrCreate(scene.SettlementId,new[] { scene.BuildingKey },1);
            var roster=store.GetOrCreateGuards(scene.SettlementId,2,new[] { new SavedRoutePoint(),new SavedRoutePoint { X=2 } });
            var guard=roster.Guards[0]; double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            var companion=roster.Guards[1];
            roster.CacheBuildingApproach(scene.BuildingKey,new SavedRoutePoint { X=1 },
                new Dictionary<string,List<SavedRoutePoint>> {
                    [guard.Id]=new List<SavedRoutePoint> { new SavedRoutePoint(),new SavedRoutePoint { X=1 } },
                    [companion.Id]=new List<SavedRoutePoint> { new SavedRoutePoint { X=2 },new SavedRoutePoint { X=1 } } });
            roster.DispatchBuilding("welfare-probe",scene.BuildingKey,now-20,2,2,12,threatId:"probe:welfare");
            var point=origin.Value+Vector3.up;
            roster.EnterBuilding(guard.Id,scene.BuildingKey,now,new GuardPoint3 { X=point.x,Y=point.y,Z=point.z });
            roster.EnterBuilding(companion.Id,scene.BuildingKey,now,new GuardPoint3 { X=point.x+1,Y=point.y,Z=point.z });
            store.RegisterDefenseCombatant("probe:welfare",scene.SettlementId,CombatOrigin.HostileAttacker,now);
            store.RecordDefensePlayerHit("probe:welfare",now,true); store.RecordDefenseWitness("probe:welfare",guard.Id,true,now);
            var witness=population.Households[0].Residents[0];
            store.RecordDefenseWitness("probe:welfare",witness.Id,false,now);
            var record=store.ConfirmDefenseDeath("probe:welfare",now,scene.BuildingKey,destination.x,destination.y,destination.z,true,300,100);
            var host=new GameObject("Welfare fixture controller"); var controller=host.AddComponent<GuardPopulation>(); controller.enabled=false;
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            typeof(GuardPopulation).GetField("worldStore",flags).SetValue(controller,store);
            typeof(GuardPopulation).GetField("interior",flags).SetValue(controller,room);
            typeof(GuardPopulation).GetField("<Roster>k__BackingField",flags).SetValue(controller,roster);
            var method=typeof(GuardPopulation).GetMethod("TryDefenseWelfare",flags);
            var windowField=typeof(GuardPopulation).GetField("welfareWindow",flags);
            GuardActor actor=null,otherActor=null; DaggerfallMessageBox window=null;
            var seededPotion=ItemBuilder.CreatePotion(4975678,2);
            game.PlayerEntity.Items.AddItem(seededPotion,noStack:true);
            var inventoryBefore=new HashSet<ulong>();
            var potionStacks=new Dictionary<ulong,int>(); int potionCount=0;
            for(int i=0;i<game.PlayerEntity.Items.Count;i++)
            {
                var item=game.PlayerEntity.Items.GetItem(i); inventoryBefore.Add(item.UID);
                if(item.PotionRecipeKey==4975678) { potionStacks[item.UID]=item.stackCount; potionCount+=item.stackCount; }
            }
            try
            {
                actor=GuardActor.CreateInterior(roster,guard.Id,room.transform,now);
                yield return null;
                var capsule=actor.GetComponent<CharacterController>();
                var nativeNavigation=new InteriorGeometry(room,capsule.radius+capsule.skinWidth,capsule.height-capsule.radius+capsule.skinWidth);
                float offset=(float)typeof(GuardActor).GetField("groundOffset",flags).GetValue(actor);
                bool placed=false;
                foreach(var anchor in nativeNavigation.LocalAnchors)
                {
                    if(placed) break;
                    if(Vector3.Distance(anchor,destination)>4 && Vector3.Distance(anchor,destination)<9)
                        foreach(var near in nativeNavigation.LocalAnchors)
                            if(Vector3.Distance(near,destination)<2 && nativeNavigation.RouteBetween(anchor,near).Count>=2)
                            {
                                actor.transform.position=room.transform.TransformPoint(anchor+Vector3.up*offset);
                                placed=true; break;
                            }
                }
                if(!placed) throw new InvalidOperationException("No native-sized welfare approach fixture.");
                var start=actor.transform.position;
                VerifyWitnessRelay(store,record,actor,witness,room,now);
                game.PlayerEntity.GodMode=false;
                game.PlayerEntity.SetHealth(Math.Max(1,game.PlayerEntity.MaxHealth/2),true);
                game.PlayerEntity.CrimeCommitted=PlayerEntity.Crimes.None;
                actor.GetComponent<EnemyMotor>().MakeEnemyHostileToAttacker(game.PlayerEntityBehaviour);
                method.Invoke(controller,new object[] { guard,actor,roster.Reports[0],now });
                if(game.PlayerEntity.CrimeCommitted!=PlayerEntity.Crimes.Assault || roster.Reports[0].ThreatId!=null || record.Death.WelfareConsumed)
                    throw new InvalidOperationException("Assaulted welfare guard did not cancel courtesy for crime handling.");
                actor.EndCombat(); roster.Reports[0].ThreatId=record.Id;
                Debug.Log("LIVING_CITIES_WELFARE_ASSAULT_PASS: native assault notification cancels courtesy for crime handling.");
                game.PlayerEntity.CrimeCommitted=PlayerEntity.Crimes.Theft;
                otherActor=GuardActor.CreateInterior(roster,companion.Id,room.transform,now);
                yield return null;
                var response=typeof(GuardPopulation).GetMethod("UpdateInteriorEnemyDefense",flags);
                bool held=(bool)response.Invoke(controller,new object[] { companion,otherActor,roster.Reports[0],now });
                if(!held || roster.Reports[0].ThreatId!=record.Id || otherActor.GetComponent<EnemyMotor>().IsHostile)
                    throw new InvalidOperationException("Uninformed responder preempted another guard's eligible welfare.");
                game.PlayerEntity.SetHealth(game.PlayerEntity.MaxHealth,true);
                held=(bool)response.Invoke(controller,new object[] { companion,otherActor,roster.Reports[0],now });
                if(held || roster.Reports[0].ThreatId!=null)
                    throw new InvalidOperationException("Responder deferred existing crime after health made welfare ineligible.");
                roster.Reports[0].ThreatId=record.Id;
                game.PlayerEntity.SetHealth(Math.Max(1,game.PlayerEntity.MaxHealth/2),true);
                UnityEngine.Object.Destroy(otherActor.gameObject); otherActor=null;
                Debug.Log("LIVING_CITIES_WELFARE_COMPANION_PASS: uninformed responder preserves courtesy before prior-crime handling.");
                bool recreated=false;
                float deadline=Time.realtimeSinceStartup+23;
                while(Time.realtimeSinceStartup<deadline && window==null)
                {
                    method.Invoke(controller,new object[] { guard,actor,roster.Reports[0],DaggerfallUnity.Instance.WorldTime.Now.ToSeconds() });
                    window=windowField.GetValue(controller) as DaggerfallMessageBox;
                    if(!recreated && window==null && Vector3.Distance(start,actor.transform.position)>0.5f)
                    {
                        double savedAt=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                        actor.Capture(savedAt);
                        var savedPosition=actor.transform.position; string guardId=guard.Id;
                        string json=SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot());
                        var restored=new WorldStateStore(0,LivingCitiesMod.ConversationArchiveRoot());
                        restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),json));
                        UnityEngine.Object.Destroy(actor.gameObject); actor=null;
                        yield return null;
                        store=restored; roster=store.GetGuards(scene.SettlementId);
                        guard=roster.Guards.Find(g=>g.Id==guardId); record=store.GetDefenseCombatant("probe:welfare");
                        typeof(GuardPopulation).GetField("worldStore",flags).SetValue(controller,store);
                        typeof(GuardPopulation).GetField("<Roster>k__BackingField",flags).SetValue(controller,roster);
                        actor=GuardActor.CreateInterior(roster,guard.Id,room.transform,savedAt);
                        yield return null;
                        if(Vector3.Distance(savedPosition,actor.transform.position)>0.1f || record.Death.WelfareConsumed ||
                            !store.CanOfferDefenseWelfare(record.Id,guard.Id,savedAt,true,0.5))
                            throw new InvalidOperationException("Mid-approach native serialization lost guard position or welfare eligibility.");
                        recreated=true;
                    }
                    yield return null;
                }
                if(!recreated || window==null || Vector3.Distance(start,actor.transform.position)<1 ||
                    Vector3.Distance(actor.transform.position,game.PlayerMotor.transform.position)>=2.5f || actor.GetComponent<EnemyMotor>().IsHostile)
                    throw new InvalidOperationException("Welfare guard failed peaceful physical approach; window="+(window!=null)+
                        "; moved="+Vector3.Distance(start,actor.transform.position)+"; distance="+Vector3.Distance(actor.transform.position,game.PlayerMotor.transform.position)+
                        "; health="+game.PlayerEntity.CurrentHealth+"/"+game.PlayerEntity.MaxHealth+"; duty="+guard.Duty+
                        "; paused="+GameManager.IsGamePaused+"; elapsed="+(DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()-now)+
                        "; route="+((List<Vector3>)typeof(GuardActor).GetField("welfareRoute",flags).GetValue(actor))?.Count+
                        "; step="+typeof(GuardActor).GetField("welfareStep",flags).GetValue(actor)+"; position="+actor.transform.position);
                if(!window.ClickAnywhereToClose) throw new InvalidOperationException("Recognized offender was offered a potion instead of recognition.");
                window.CloseWindow();
                yield return null; yield return null;
                int after=0;
                for(int i=0;i<game.PlayerEntity.Items.Count;i++)
                { var item=game.PlayerEntity.Items.GetItem(i); if(item.PotionRecipeKey==4975678) after+=item.stackCount; }
                if(!record.Death.WelfareConsumed || record.Death.PotionGranted || after!=potionCount ||
                    game.PlayerEntity.CrimeCommitted!=PlayerEntity.Crimes.Theft || roster.Reports[0].ThreatId!=null)
                    throw new InvalidOperationException("Recognition granted a potion or lost prior-bounty handoff.");
                Debug.Log("LIVING_CITIES_GUARD_WELFARE_PASS: surveyed physical approach, recognition interrupts welfare, no potion and prior-crime handoff.");
                var completedStore=new WorldStateStore(0,LivingCitiesMod.ConversationArchiveRoot());
                completedStore.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),
                    SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot())));
                var completed=completedStore.GetDefenseCombatant(record.Id);
                var duplicate=completedStore.CompleteDefenseWelfare(record.Id,guard.Id,
                    DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),true,true,0.5);
                if(!completed.Death.WelfareConsumed || completed.Death.PotionGranted || duplicate.Completed || duplicate.PotionGranted)
                    throw new InvalidOperationException("Completed welfare repeated or lost reward state after serialization.");
                Debug.Log("LIVING_CITIES_WELFARE_RECREATE_PASS: native JSON and actor recreation resume approach; completed exchange cannot repeat.");
            }
            finally
            {
                if(window!=null && DaggerfallUI.UIManager.TopWindow==window) window.CloseWindow();
                if(actor) UnityEngine.Object.Destroy(actor.gameObject);
                if(otherActor) UnityEngine.Object.Destroy(otherActor.gameObject);
                UnityEngine.Object.Destroy(host);
                for(int i=game.PlayerEntity.Items.Count-1;i>=0;i--)
                {
                    var item=game.PlayerEntity.Items.GetItem(i);
                    if(!inventoryBefore.Contains(item.UID) && item.PotionRecipeKey==4975678) game.PlayerEntity.Items.RemoveItem(item);
                    else if(potionStacks.ContainsKey(item.UID)) item.stackCount=potionStacks[item.UID];
                }
                game.PlayerEntity.Items.RemoveItem(seededPotion);
                game.PlayerEntity.GodMode=godMode; game.PlayerEntity.SetHealth(health,true); game.PlayerEntity.CrimeCommitted=crime; game.PlayerMotor.transform.position=original;
            }
        }
        private static void VerifyWitnessRelay(WorldStateStore store,DefenseCombatant record,GuardActor guard,
            Resident witness,DaggerfallInterior room,double now)
        {
            var relay=typeof(DefenseCombatAdapter).GetMethod("RelayDefenseWitnesses",BindingFlags.Static|BindingFlags.Public);
            if(relay==null) throw new InvalidOperationException("Defense witness handoff has no runtime contact adapter.");
            var body=new GameObject("Defense witness contact fixture");
            var otherRoom=new GameObject("Separate defense witness room"); otherRoom.AddComponent<DaggerfallInterior>();
            var resident=body.AddComponent<ResidentActor>();
            typeof(ResidentActor).GetField("<Resident>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(resident,witness);
            Action invoke=()=>relay.Invoke(null,new object[] { store,record,guard,now });
            try
            {
                record.Death.InformedGuards.Clear(); body.transform.SetParent(room.transform);
                body.transform.position=guard.transform.position+Vector3.right*10; invoke();
                if(record.Death.InformedGuards.Count!=0) throw new InvalidOperationException("Distant witness informed guard without contact.");
                body.transform.position=guard.transform.position; body.transform.SetParent(otherRoom.transform,true); invoke();
                if(record.Death.InformedGuards.Count!=0) throw new InvalidOperationException("Witness in another room informed guard.");
                body.transform.SetParent(room.transform,true); witness.IsAlive=false; invoke(); witness.IsAlive=true;
                if(record.Death.InformedGuards.Count!=0) throw new InvalidOperationException("Dead witness informed guard.");
                invoke();
                if(!record.Death.InformedGuards.Contains(guard.GuardId)) throw new InvalidOperationException("Nearby living fight witness failed to inform responder.");
                Debug.Log("LIVING_CITIES_DEFENSE_RELAY_PASS: nearby living witness informs matching responder; distant, dead and other-room witnesses do not.");
            }
            finally { witness.IsAlive=true; UnityEngine.Object.Destroy(body); UnityEngine.Object.Destroy(otherRoom); }
        }
    }
}
