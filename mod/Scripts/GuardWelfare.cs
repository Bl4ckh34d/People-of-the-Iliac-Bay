using System;
using System.Collections.Generic;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public sealed partial class GuardPopulation
    {
        private WelfareWindow welfareWindow;
        private readonly Dictionary<string,float> nextWelfareRelay=new Dictionary<string,float>();
        private readonly List<FailedWelfareApproach> failedWelfareApproaches=new List<FailedWelfareApproach>();
        private sealed class FailedWelfareApproach
        {
            public WorldStateStore Store;
            public string Threat,Guard;
            public Vector3 Destination;
        }
        private bool MayAttemptWelfare(string threat,string guard)
        {
            var destination=GameManager.Instance.PlayerMotor.transform.position;
            return !failedWelfareApproaches.Exists(f=>f.Store==worldStore && f.Threat==threat && f.Guard==guard &&
                (f.Destination-destination).sqrMagnitude<=1);
        }
        private bool WaitForWelfareResponder(DefenseCombatant record,GuardRecord guard,GuardActor actor,GuardReport report,double now)
        {
            foreach(var other in report.GuardIds)
                if(other!=guard.Id && MayAttemptWelfare(record.Id,other) &&
                    worldStore.CanOfferDefenseWelfare(record.Id,other,now,WelfareOnSite(record),WelfareHealth()))
                { actor.EndCombat(); return true; }
            return false;
        }
        private static double WelfareHealth()
        {
            var player=GameManager.Instance.PlayerEntity;
            return player.MaxHealth<=0 ? 0 : Math.Max(0,Math.Min(1,(double)player.CurrentHealth/player.MaxHealth));
        }
        private bool WelfareOnSite(DefenseCombatant record)
        {
            var scene=DefenseScene.For(GameManager.Instance.PlayerEnterExit.IsPlayerInsideBuilding ?
                (Component)GameManager.Instance.PlayerEnterExit.Interior : directory);
            if(scene==null || !scene.PlayerPresent || scene.SettlementId!=record.SettlementId || scene.BuildingKey!=record.Death.BuildingKey) return false;
            var p=scene.Root.InverseTransformPoint(GameManager.Instance.PlayerMotor.transform.position-Vector3.up);
            return (p-new Vector3((float)record.Death.X,(float)record.Death.Y,(float)record.Death.Z)).sqrMagnitude<=1600;
        }
        private bool TryDefenseWelfare(GuardRecord guard,GuardActor actor,GuardReport report,double now)
        {
            var game=GameManager.Instance;
            if(actor.GetComponent<EnemyMotor>().IsHostile && actor.GetComponent<EnemySenses>().Target==game.PlayerEntityBehaviour)
            {
                if(game.PlayerEntity.CrimeCommitted==PlayerEntity.Crimes.None) game.PlayerEntity.CrimeCommitted=PlayerEntity.Crimes.Assault;
                report.ThreatId=null; actor.BeginCombat(now); return true;
            }
            var record=worldStore.GetDefenseCombatant(report.ThreatId);
            if(record==null || record.Death==null) return false;
            float next;
            if(!record.Death.InformedGuards.Contains(guard.Id) &&
                (!nextWelfareRelay.TryGetValue(guard.Id,out next) || Time.unscaledTime>=next))
            {
                nextWelfareRelay[guard.Id]=Time.unscaledTime+0.25f;
                DefenseCombatAdapter.RelayDefenseWitnesses(worldStore,record,actor,now);
            }
            failedWelfareApproaches.RemoveAll(f=> {
                var old=f.Store==worldStore ? worldStore.GetDefenseCombatant(f.Threat) : null;
                return old==null || old.Death==null || old.Death.WelfareConsumed || old.Death.WelfareExpired || now>=old.Death.WelfareExpiresAt;
            });
            if(welfareWindow!=null) { actor.EndCombat(); return true; }
            if(game.PlayerEntity.IsInvisible || GameManager.IsGamePaused) return false;
            if(!MayAttemptWelfare(record.Id,guard.Id) ||
                !worldStore.CanOfferDefenseWelfare(record.Id,guard.Id,now,WelfareOnSite(record),WelfareHealth()))
                return WaitForWelfareResponder(record,guard,actor,report,now);
            var approached=actor.ApproachWelfare(record.Id,game.PlayerMotor.transform.position,actor.IsInterior ? interior : null,now);
            if(!approached.HasValue)
            {
                failedWelfareApproaches.RemoveAll(f=>f.Store==worldStore && f.Threat==record.Id && f.Guard==guard.Id);
                failedWelfareApproaches.Add(new FailedWelfareApproach { Store=worldStore,Threat=record.Id,Guard=guard.Id,
                    Destination=game.PlayerMotor.transform.position });
                return WaitForWelfareResponder(record,guard,actor,report,now);
            }
            if(!approached.Value) return true;
            var expectedStore=worldStore;
            welfareWindow=new WelfareWindow(record.Death.PotionOffered,record.Death.LootEntitled,accept=> {
                welfareWindow=null;
                if(worldStore!=expectedStore || !actor) return;
                var result=worldStore.CompleteDefenseWelfare(record.Id,guard.Id,Now(),WelfareOnSite(record),accept,WelfareHealth());
                if(!result.Completed) return;
                if(result.PotionGranted) game.PlayerEntity.Items.AddItem(ItemBuilder.CreatePotion(4975678));
                // Resume existing native surrender handling after courtesy, never erase crime.
                if(game.PlayerEntity.CrimeCommitted!=PlayerEntity.Crimes.None) report.ThreatId=null;
                Debug.Log("LIVING_CITIES_GUARD_WELFARE: "+guard.Id+"; potion="+result.PotionGranted);
            });
            welfareWindow.Show();
            return true;
        }
        private sealed class WelfareWindow : DaggerfallMessageBox
        {
            private Action<bool> finished;
            private bool accepted;
            public WelfareWindow(bool potion,bool loot,Action<bool> completion):base(DaggerfallUI.UIManager)
            {
                finished=completion;
                SetText(new[] { "Are you all right? That attacker has been dealt with.",
                    loot ? "Their belongings are yours to take, if you wish." : "You helped us stop that attacker.",
                    potion ? "You look hurt. Would you like a healing potion?" : "Take care of those wounds." });
                if(potion)
                {
                    AddButton(MessageBoxButtons.Yes); AddButton(MessageBoxButtons.No,true);
                    OnButtonClick+=(sender,button)=> { accepted=button==MessageBoxButtons.Yes; CloseWindow(); };
                }
                else ClickAnywhereToClose=true;
            }
            public override void OnPop()
            {
                base.OnPop(); var callback=finished; finished=null; if(callback!=null) callback(accepted);
            }
        }
    }
}
