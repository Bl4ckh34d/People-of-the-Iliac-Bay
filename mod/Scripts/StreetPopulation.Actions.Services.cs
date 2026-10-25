using System;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Formulas;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private const string BeerService="tavern-beer";
        private static bool PlayerCanDrink()
        {
            uint minutes=DaggerfallUnity.Instance.WorldTime.DaggerfallDateTime.ToClassicDaggerfallTime();
            uint last=GameManager.Instance.PlayerEntity.LastTimePlayerAteOrDrankAtTavern;
            return minutes>=last && minutes-last>=240 && GameManager.Instance.PlayerEntity.CurrentHealth>0;
        }
        private bool LivingActionKeeper()
        {
            if(!interior || shopGeometry==null || !shopGeometry.IsTavern || !shopGeometry.TavernKeeper || !shopGeometry.CounterPosition.HasValue) return false;
            var person=KeeperPerson(shopGeometry.TavernKeeper);
            return person!=null && person.IsAlive;
        }
        private void OfferBeerAction(ResidentActor actor,string text,List<NpcActionOption> options,bool semantic=true)
        {
            OfferNativeLootSaleAction(actor,text,options,semantic);
            OfferShopPurchaseAction(actor,text,options,semantic);
            if(!LivingActionKeeper() || !PlayerCanDrink()) return;
            var target=ActionPosition(interior.transform.TransformPoint(shopGeometry.CounterPosition.Value));target.ContainerId=BeerService;
            AddActionOption(options,NpcActionKind.Purchase,target,"buy one beer with your own money at this inn's keeper, return, and give the nearby player its native drink effect",text,semantic);
        }
        private bool PrepareBeerAction(ResidentActor actor,NpcActionJob job,out string reason)
        {
            if(IsGuildPotionTarget(job.Target)) return PrepareGuildPotionPurchaseAction(actor,job,out reason);
            if(job.Target!=null && job.Target.ContainerId==NpcShopLootSale.TargetContainerId)
                return PrepareNativeLootSaleAction(actor,job,out reason);
            if(job.Target!=null && job.Target.ContainerId!=BeerService) return PrepareShopPurchaseAction(actor,job,out reason);
            reason="This paid service is unavailable.";
            if(job.Target==null || job.Target.ContainerId!=BeerService || !ActionSpaceMatches(job.Target) || !LivingActionKeeper() || !PlayerCanDrink()) return false;
            Vector3 chosen;
            if(!shopGeometry.TryCounterPosition(actor,new Vector3((float)job.Target.X,(float)job.Target.Y,(float)job.Target.Z),out chosen))
            {reason="The keeper's service positions are occupied.";return false;}
            job.Target=ActionPosition(interior.transform.TransformPoint(chosen));job.Target.ContainerId=BeerService;
            NativeNpcInventoryBinding binding;
            if(!NativeNpcInventoryBridge.TryOpen(worldStore,actor.Resident.Id,null,NativeNpcInventoryBridge.InitialWalletGold(worldStore,actor.Resident.Id),out binding,out reason)) return false;
            var inventory=worldStore.ReadNpcInventory(actor.Resident.Id);
            uint minutes=DaggerfallUnity.Instance.WorldTime.DaggerfallDateTime.ToClassicDaggerfallTime();
            int holiday=FormulaHelper.GetHolidayId(minutes,0);
            job.ServiceCode=BeerService;job.ServicePrice=holiday==(int)DFLocation.Holidays.New_Life?0:1;
            job.ExpectedInventoryRevision=inventory.Revision;
            if(inventory.WalletGold<job.ServicePrice) {reason="The NPC cannot afford this drink.";return false;}
            return true;
        }
        // Called only after the common executor observed physical arrival.
        private bool ExecuteBeerStage(NpcActionJob job,ResidentActor actor,ActionMotion motion,double now,out bool completed,out string reason)
        {
            completed=false;reason=null;
            if(job.ServiceCode!=BeerService || job.Phase!=NpcActionPhase.Executing) {reason="Invalid drink service stage.";return false;}
            if(job.ServiceStep==0)
            {
                if(!LivingActionKeeper() || !PlayerCanDrink() ||
                    Vector3.Distance(actor.transform.position,shopGeometry.TavernKeeper.transform.position)>4)
                {reason="The keeper or drink service is no longer available.";return false;}
                if(!worldStore.TryPayNpcActionService(job.Id,job.Revision,now))
                {reason="The NPC's wallet changed or no longer covers the drink.";return false;}
                shopGeometry.ReleaseCounter(actor.Resident.Id);motion.Indoor=null;motion.Outdoor=null;motion.RepathAt=0;
                return true;
            }
            var player=GameManager.Instance.PlayerEntity;
            if(!PlayerCanDrink() || Vector3.Distance(actor.transform.position,GameManager.Instance.PlayerMotor.transform.position)>1.5f)
            {reason="The player cannot receive this drink here now.";return false;}
            // Reproduce the native beer effect, without invoking the native window
            // (which would debit PLAYER funds). Payment already belongs to this job.
            completed=worldStore.TryCompleteNpcActionEffect(job.Id,job.Revision,now,()=>{
                player.SetHealth(player.CurrentHealth+2);
                player.LastTimePlayerAteOrDrankAtTavern=DaggerfallUnity.Instance.WorldTime.DaggerfallDateTime.ToClassicDaggerfallTime();
                return true;
            });
            if(!completed) reason="The drink delivery revision or deadline changed.";
            return completed;
        }
        private void NotifyActionOutcome(NpcActionJob job,ResidentActor actor,string itemName)
        {
            // Called only on a newly observed terminal transition. Restored jobs
            // and routine-rejoin ticks do not repeat HUD notifications.
            var recipient=job.Target==null || string.IsNullOrEmpty(job.Target.PersonId)?null:FindActor(job.Target.PersonId);
            string message=job.ServiceCode==NpcShopLootSale.ServiceCode && job.Phase==NpcActionPhase.Completed && !string.IsNullOrEmpty(job.CompletedEffectId)?
                ActionRecipientName(actor)+" sells "+(itemName??"owned item")+" to the shop for "+job.ServicePrice+" gold.":
                job.ServiceCode==NpcShopCommerce.ServiceCode && job.Phase==NpcActionPhase.Completed && !string.IsNullOrEmpty(job.CompletedEffectId)?
                ActionRecipientName(actor)+" buys "+job.Target.Quantity+" x "+(itemName??"shop item")+" for "+job.ServicePrice+" gold and keeps it.":
                job.ServiceCode==NpcShopCommerce.RepairServiceCode && job.Phase==NpcActionPhase.Completed?
                    job.ServicePrice==0?ActionRecipientName(actor)+" collects "+(itemName??"the repaired item")+".":
                        ActionRecipientName(actor)+" leaves "+(itemName??"the item")+" for repair and pays "+job.ServicePrice+" gold. Repair is pending.":
                job.ServiceCode==NativeNpcTempleTraining.ServiceCode && job.Phase==NpcActionPhase.Completed?
                    ActionRecipientName(actor)+" completes native temple training for "+job.ServicePrice+" gold from their own wallet.":
                NpcActionFeedback.Describe(job,ActionRecipientName(actor),itemName,ActionRecipientName(recipient));
            if(!string.IsNullOrEmpty(message)) DaggerfallUI.AddHUDText(message,4f);
        }
        private void RememberActionOutcome(NpcActionJob job,double now)
        {
            if(job==null)return;
            if(job.Phase==NpcActionPhase.Failed || job.Phase==NpcActionPhase.Cancelled)
            {
                var outcomeDate=DaggerfallUnity.Instance.WorldTime.Now;
                worldStore.TryRememberNpcActionOutcome(job.Id,job.Revision,outcomeDate.DateString()+", "+outcomeDate.Year,
                    "settlement "+population.SettlementId+(interior?", building "+interior.EntryDoor.buildingKey:" outdoors"),null);
                return;
            }
            if(job.Phase!=NpcActionPhase.Completed)return;
            if(job.Kind==NpcActionKind.ReportMurder)return; // Delivery writes both participants' memories atomically.
            // Treatment memory reads the typed source receipt directly. Do not
            // add a generic arrival narrative for a healing effect.
            if(job.Kind==NpcActionKind.UseHealingItem || job.Kind==NpcActionKind.RestToHeal ||
                job.Kind==NpcActionKind.CastSpell && job.MagicHealing!=null)return;
            if(job.Kind==NpcActionKind.Interact)
            {
                var source=FindActor(job.ResidentId);
                var target=job.Target==null?null:FindActor(job.Target.PersonId);
                if(!source || !target || source==target || !source.gameObject.activeInHierarchy ||
                    !target.gameObject.activeInHierarchy || source.gameObject.scene!=target.gameObject.scene ||
                    !source.Resident.IsAlive || !target.Resident.IsAlive || source.Resident.IsQuestProtected ||
                    target.Resident.IsQuestProtected || QuestOwned(source.Resident) || QuestOwned(target.Resident) ||
                    source.Resident.AgeGroup!=ResidentAgeGroup.Adult || target.Resident.AgeGroup!=ResidentAgeGroup.Adult ||
                    source.IsSleeping || target.IsSleeping || source.IsConversing || target.IsConversing ||
                    IsAlarmed(source.Resident.Id) || IsAlarmed(target.Resident.Id) ||
                    bystanderPostures.ContainsKey(target.Resident.Id) || streetChats.ContainsKey(target.Resident.Id)) return;
                var targetAction=worldStore.ActiveNpcAction(target.Resident.Id);
                if(targetAction!=null && (!NpcActionExecutor.IsTerminal(targetAction) || targetAction.ReturningToRoutine)) return;
                if(worldStore.ActiveNpcTask(target.Resident.Id)!=null) return;
                if(GetState(target.Resident.Id,now).Activity==Activity.Sleep) return;
                Vector3 sourceGround=source.transform.position-Vector3.up,targetGround=target.transform.position-Vector3.up;
                const float interactionRange=1.35f;
                if(Vector3.Distance(sourceGround,targetGround)>interactionRange ||
                    !CivilianResponses.Sees(source.transform.position,target.transform.position,interactionRange+1) ||
                    !CivilianResponses.Sees(target.transform.position,source.transform.position,interactionRange+1)) return;
                var interactionDate=DaggerfallUnity.Instance.WorldTime.Now;
                string observedSpace="settlement:"+population.SettlementId+
                    (interior?":building:"+interior.EntryDoor.buildingKey:":outdoors");
                var physicalEvidence=NpcInteractionPhysicalEvidence.Create(job.Id,job.Revision,
                    job.ResidentId,job.Target.PersonId,now,observedSpace,
                    sourceGround.x,sourceGround.y,sourceGround.z,observedSpace,
                    targetGround.x,targetGround.y,targetGround.z,true);
                worldStore.TryRecordNpcInteractionMemory(job.Id,job.Revision,now,interactionDate.DateString()+", "+interactionDate.Year,
                    "settlement "+population.SettlementId+(interior?", building "+interior.EntryDoor.buildingKey:" outdoors"),
                    physicalEvidence,out _);
                return;
            }
            string observation=job.ServiceCode==NpcShopLootSale.ServiceCode?"The NPC sold exact owned native item UID "+job.Target.ItemId+
                " to the current open native shop for "+job.ServicePrice+" gold. The item left its saved belongings and its own wallet received the exact amount once.":
                job.ServiceCode==NpcShopCommerce.ServiceCode?"The NPC bought "+job.Target.Quantity+" unit(s) from exact native shop stock UID "+job.Target.ItemId+
                " for "+job.ServicePrice+" gold from its own wallet. The actual items remain in its saved belongings; no handover has occurred.":
                job.ServiceCode==NpcShopCommerce.RepairServiceCode?job.ServicePrice==0?
                    "The NPC collected the same completed native repair at its actual repair shop. Its item is now in full condition; no second payment occurred.":
                    "The NPC left exact owned item UID "+job.Target.ItemId+" at the actual native repair shop and paid "+job.ServicePrice+
                    " gold from its own wallet. Repair is pending native completion and later collection; the item is unavailable meanwhile.":
                job.ServiceCode==NativeNpcTempleTraining.ServiceCode?"The NPC reached the current temple trainer, paid the native non-member lesson price from its own wallet once, advanced one eligible saved skill using the native tally, and spent three game hours.":
                job.ServiceCode==BeerService?"The NPC paid for a beer at the keeper, physically returned, and gave the nearby player the native tavern drink effect.":
                job.Kind==NpcActionKind.Transfer?InventoryActionOutcome(job):
                job.Kind==NpcActionKind.ChangeSchedule?"The NPC left an optional visit early through its exit after agreeing to go home. Arrival home was not observed.":
                job.Kind==NpcActionKind.Interact?"The NPC physically approached and faced the requested person. No conversation between those people was observed.":
                job.Kind==NpcActionKind.TakeSeat?"The NPC physically reached the selected seat and stayed there for the accepted activity window.":
                job.Kind==NpcActionKind.Follow?"The NPC followed the nearby player for the accepted activity window.":
                job.Kind==NpcActionKind.Wait?"The NPC remained at the agreed local position for the accepted activity window.":
                "The NPC physically reached the agreed local action destination.";
            var date=DaggerfallUnity.Instance.WorldTime.Now;
            worldStore.TryRememberNpcActionOutcome(job.Id,job.Revision,date.DateString()+", "+date.Year,
                "settlement "+population.SettlementId+(interior?", building "+interior.EntryDoor.buildingKey:" outdoors"),
                observation);
        }
    }
}
