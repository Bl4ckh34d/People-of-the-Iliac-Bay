using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public static class WelfareEncounterProbe
    {
        public static IEnumerator Finish(WorldStateStore store,GuardPopulation guards,GuardActor actor,GuardRecord guard,
            GuardReport report,DaggerfallEntityBehaviour target)
        {
            var game=GameManager.Instance; var room=game.PlayerEnterExit.Interior;
            bool god=game.PlayerEntity.GodMode; int health=game.PlayerEntity.CurrentHealth;
            bool surrendered=game.PlayerEntity.HaveShownSurrenderToGuardsDialogue;
            var original=game.PlayerMotor.transform.position; var priorCrime=game.PlayerEntity.CrimeCommitted;
            string threat=report.ThreatId;
            try
            {
                var capsule=actor.GetComponent<CharacterController>();
                var geometry=new InteriorGeometry(room,capsule.radius+capsule.skinWidth,capsule.height-capsule.radius+capsule.skinWidth);
                var from=room.transform.InverseTransformPoint(actor.transform.position-Vector3.up);
                bool placed=false;
                foreach(var anchor in geometry.LocalAnchors)
                {
                    var candidate=room.transform.TransformPoint(anchor)+Vector3.up;
                    float distance=Vector3.Distance(actor.transform.position,candidate);
                    if(distance<3 || distance>7 || !CivilianResponses.Sees(actor.transform.position,candidate,20) ||
                        !CivilianResponses.Sees(actor.transform.position,target.transform.position,20) || geometry.RouteBetween(from,anchor).Count<2) continue;
                    game.PlayerMotor.transform.position=candidate; placed=true; break;
                }
                if(!placed) throw new InvalidOperationException("No reachable witnessed welfare encounter position.");
                Physics.SyncTransforms();
                game.PlayerEntity.GodMode=false; game.PlayerEntity.SetHealth(Math.Max(1,game.PlayerEntity.MaxHealth/2),true);
                game.PlayerEntity.HaveShownSurrenderToGuardsDialogue=false;
                game.PlayerEntity.CrimeCommitted=PlayerEntity.Crimes.Theft;
                target.DamageHealthFromSource(game.PlayerEntityBehaviour,target.Entity.CurrentHealth,false,Vector3.zero);
                var record=store.GetDefenseCombatant(threat);
                if(record==null || record.Death==null || !record.Death.LootEntitled || !record.Death.InformedGuards.Contains(guard.Id))
                    throw new InvalidOperationException("Real defending guard did not witness legitimate player kill.");
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                var field=typeof(GuardPopulation).GetField("welfareWindow",flags);
                var start=actor.transform.position;
                float approachDeadline=Time.realtimeSinceStartup+10;
                while(Vector3.Distance(start,actor.transform.position)<0.2f && field.GetValue(guards)==null &&
                    Time.realtimeSinceStartup<approachDeadline) yield return null;
                if(field.GetValue(guards)!=null || Vector3.Distance(start,actor.transform.position)<0.2f)
                    throw new InvalidOperationException("Welfare save fixture did not reach a moving approach before dialogue.");
                string guardId=guard.Id; int settlement=record.SettlementId;
                yield return ReloadApproach(guards);
                var mod=UnityEngine.Object.FindObjectOfType<LivingCitiesMod>();
                store=(WorldStateStore)typeof(LivingCitiesMod).GetField("store",flags).GetValue(mod);
                record=store.GetDefenseCombatant(threat);
                guard=store.GetGuards(settlement).Guards.Find(g=>g.Id==guardId);
                actor=Array.Find(UnityEngine.Object.FindObjectsOfType<GuardActor>(),g=>g.GuardId==guardId);
                if(!actor || record==null || record.Death.WelfareConsumed || record.Death.WelfareExpired ||
                    game.PlayerEntity.CrimeCommitted!=PlayerEntity.Crimes.Theft)
                    throw new InvalidOperationException("Actual welfare slot reload lost responder, opportunity or prior crime.");
                Debug.Log("LIVING_CITIES_WELFARE_SLOT_PASS: real save/load preserves approaching responder, welfare opportunity and prior Theft.");
                DaggerfallMessageBox window=null; float deadline=Time.realtimeSinceStartup+22;
                while(Time.realtimeSinceStartup<deadline && window==null)
                { window=field.GetValue(guards) as DaggerfallMessageBox; yield return null; }
                if(window==null) throw new InvalidOperationException("Reported-threat responder never offered welfare; expired="+record.Death.WelfareExpired+
                    "; duty="+guard.Duty+"; distance="+Vector3.Distance(actor.transform.position,game.PlayerMotor.transform.position));
                if(game.PlayerEntity.HaveShownSurrenderToGuardsDialogue) throw new InvalidOperationException("Guard attempted arrest before welfare.");
                window.CloseWindow(); yield return null; yield return null;
                if(!record.Death.WelfareConsumed || record.Death.PotionGranted || game.PlayerEntity.CrimeCommitted!=PlayerEntity.Crimes.Theft)
                    throw new InvalidOperationException("Declined welfare changed prior crime or granted unwanted potion.");
                deadline=Time.realtimeSinceStartup+12;
                while(Time.realtimeSinceStartup<deadline && !game.PlayerEntity.HaveShownSurrenderToGuardsDialogue) yield return null;
                if(!game.PlayerEntity.HaveShownSurrenderToGuardsDialogue || !(DaggerfallUI.UIManager.TopWindow is DaggerfallMessageBox))
                    throw new InvalidOperationException("Native surrender did not follow completed welfare for prior crime.");
                Debug.Log("LIVING_CITIES_WELFARE_CHAIN_PASS: keeper report, finite guard travel/combat, witnessed native player kill, welfare before native surrender and prior Theft retained.");
            }
            finally
            {
                DaggerfallUI.Instance.PopToHUD(); if(actor) actor.EndCombat();
                game.PlayerEntity.GodMode=god; game.PlayerEntity.SetHealth(health,true);
                game.PlayerEntity.HaveShownSurrenderToGuardsDialogue=surrendered;
                game.PlayerEntity.CrimeCommitted=priorCrime; game.PlayerMotor.transform.position=original;
            }
        }
        private static IEnumerator ReloadApproach(GuardPopulation guards)
        {
            var game=GameManager.Instance;
            if(!game.PlayerEntity.Name.StartsWith("LivingCitiesTest-",StringComparison.Ordinal))
                throw new InvalidOperationException("Welfare reload requires the dedicated test character.");
            bool saved=false,loaded=false;
            SaveLoadManager.OnSaveEventHandler onSave=ignored=>saved=true;
            SaveLoadManager.OnLoadEventHandler onLoad=ignored=>loaded=true;
            SaveLoadManager.OnSave+=onSave; SaveLoadManager.OnLoad+=onLoad;
            try
            {
                const string name="Living Cities - Welfare approach";
                game.SaveLoadManager.Save(game.PlayerEntity.Name,name);
                float deadline=Time.realtimeSinceStartup+30;
                while(!saved && Time.realtimeSinceStartup<deadline) yield return null;
                if(!saved) throw new InvalidOperationException("Welfare save timed out.");
                int key=game.SaveLoadManager.FindSaveFolderByNames(game.PlayerEntity.Name,name);
                game.SaveLoadManager.Load(key); DaggerfallUI.Instance.PopToHUD();
                deadline=Time.realtimeSinceStartup+60;
                while((!loaded || guards.Roster==null || guards.ActiveCount==0) && Time.realtimeSinceStartup<deadline) yield return null;
                if(!loaded || guards.Roster==null || guards.ActiveCount==0)
                    throw new InvalidOperationException("Welfare reload timed out.");
            }
            finally { SaveLoadManager.OnSave-=onSave; SaveLoadManager.OnLoad-=onLoad; }
        }
    }
}
