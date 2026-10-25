using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Opt-in native acceptance proof. It uses loaded stock and residents only.
    internal static class NativeNamedOtherHandoverProbe
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;

        internal static IEnumerator Run(StreetPopulation streets,ResidentActor source,DaggerfallInterior room)
        {
            Require(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-named-other-handover-probe")>=0,
                "Named-other handover probe requires its explicit flag.");
            var store=typeof(StreetPopulation).GetField("worldStore",Private).GetValue(streets) as WorldStateStore;
            string itemName; DaggerfallUnityItem shopItem;
            var purchase=ActualShopPurchaseOffer(streets,store,source,room,out itemName,out shopItem);
            if(purchase==null)
            { Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAMED_OTHER_HANDOVER_SKIP: no actual affordable exact shop item."); yield break; }

            string first="Buy one "+itemName;
            var prior=new List<NpcActionOption>{purchase};
            if(prior.Count!=1)
            { Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAMED_OTHER_HANDOVER_SKIP: sequence matcher rejected actual purchase."); yield break; }

            foreach(var recipient in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
            {
                if(!IsCurrentRecipient(streets,source,recipient)) continue;
                var handover=streets.TryNamedAcquiredItemHandover(source,recipient.Resident.Id,0,prior);
                var recipientOwner=recipient.GetComponent<NativeNpcInventoryOwner>(); string reason;
                if(handover==null || !recipientOwner || recipientOwner.Binding==null ||
                    !NativeNpcInventoryBridge.TryCapture(store,recipientOwner.Binding,out reason) ||
                    !NativeNpcInventoryBridge.CanAppendExactItem(recipientOwner.Binding.Items,shopItem,out reason) || !CanCarry(recipient,shopItem)) continue;
                Require(handover.ItemSourceStep==1 && handover.Target!=null && handover.Target.PersonId==recipient.Resident.Id &&
                    handover.Target.ItemId==purchase.Target.ItemId && handover.Target.Quantity==purchase.Target.Quantity,
                    "Named-other binding changed verified recipient or exact acquired item.");

                var sourceOwner=source.GetComponent<NativeNpcInventoryOwner>();
                Require(sourceOwner && sourceOwner.Binding!=null,"Purchase source lacks its native inventory binding.");
                var sourceBefore=store.ReadNpcInventory(source.Resident.Id);
                var recipientBefore=store.ReadNpcInventory(recipient.Resident.Id);
                int playerGold=GameManager.Instance.PlayerEntity.GetGoldAmount();
                int recipientItems=MatchingItems(recipientOwner.Binding.Items,shopItem);
                string proposal="native-named-other-handover:"+Guid.NewGuid().ToString("N");
                Require(NpcProbeCommandSource.TryAcceptTask(streets,source.Resident.Id,proposal,
                    new List<NpcActionDecision>{Decision(purchase),Decision(handover)},out reason),reason);

                string taskId="task:"+proposal; float deadline=Time.realtimeSinceStartup+120f;
                while(Time.realtimeSinceStartup<deadline)
                {
                    var task=store.ReadNpcTask(taskId);
                    if(task!=null && (task.Phase==NpcTaskPhase.Completed || task.Phase==NpcTaskPhase.Failed || task.Phase==NpcTaskPhase.Cancelled)) break;
                    yield return null;
                }
                var completed=store.ReadNpcTask(taskId);
                var bought=store.ReadNpcAction(taskId+":step:0");
                var transferred=store.ReadNpcAction(taskId+":step:1");
                Require(completed!=null && completed.Phase==NpcTaskPhase.Completed && completed.Steps.Count==2 &&
                    completed.Steps[1].ItemSourceStep==1,
                    "Named-other runtime task did not complete: "+(completed==null?"missing":completed.FailureReason));
                ulong acquired=0;
                Require(bought!=null && bought.Phase==NpcActionPhase.Completed &&
                    ulong.TryParse(bought.AcquiredItemId,NumberStyles.None,CultureInfo.InvariantCulture,out acquired) && acquired!=0 &&
                    transferred!=null && transferred.Phase==NpcActionPhase.Completed &&
                    transferred.Target.ItemId==bought.AcquiredItemId && transferred.Target.PersonId==recipient.Resident.Id &&
                    transferred.Target.TransferDirection==NpcActionTransferDirection.NpcToCounterparty &&
                    transferred.ItemTransferReceipt==null,
                    "Named-other task lost acquired UID, source step, recipient, or created a player receipt.");
                var sourceAfter=store.ReadNpcInventory(source.Resident.Id);
                var recipientAfter=store.ReadNpcInventory(recipient.Resident.Id);
                Require(sourceOwner.Binding.Items.GetItem(acquired)==null &&
                    MatchingItems(recipientOwner.Binding.Items,shopItem)==recipientItems+purchase.Target.Quantity &&
                    sourceAfter.WalletGold==sourceBefore.WalletGold-purchase.Target.QuotedGold &&
                    recipientAfter.WalletGold==recipientBefore.WalletGold &&
                    sourceAfter.Revision==sourceBefore.Revision+2 && recipientAfter.Revision==recipientBefore.Revision+1 &&
                    GameManager.Instance.PlayerEntity.GetGoldAmount()==playerGold,
                    "Named-other handover changed wrong inventory, revision, or wallet.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAMED_OTHER_HANDOVER_PASS source="+source.Resident.Id+"; recipient="+
                    recipient.Resident.Id+"; acquiredUid="+bought.AcquiredItemId+"; price="+purchase.Target.QuotedGold+"; task="+taskId);
                yield break;
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAMED_OTHER_HANDOVER_SKIP: no unique visible eligible recipient with native capacity.");
        }

        private static bool IsCurrentRecipient(StreetPopulation streets,ResidentActor source,ResidentActor recipient)
        {
            return recipient && recipient!=source && recipient.gameObject.activeInHierarchy && recipient.Resident!=null &&
                recipient.Resident.IsAlive && recipient.Resident.AgeGroup==ResidentAgeGroup.Adult && !recipient.Resident.IsQuestProtected &&
                recipient.Person && !string.IsNullOrWhiteSpace(recipient.Person.NameNPC) &&
                streets.FindActor(recipient.Resident.Id)==recipient;
        }

        private static NpcActionDecision Decision(NpcActionOption source)
        {
            return new NpcActionDecision {DurationSeconds=300,Option=new NpcActionOption {Id=source.Id,Kind=source.Kind,
                Label=source.Label,ItemSourceStep=source.ItemSourceStep,Target=NpcActionExecutor.CopyTarget(source.Target)}};
        }

        private static bool CanCarry(ResidentActor actor,DaggerfallUnityItem item)
        {
            var behaviour=actor?actor.GetComponent<DaggerfallEntityBehaviour>():null;
            var entity=behaviour?behaviour.Entity:null;
            return entity!=null && item!=null && entity.Items.GetWeight()+item.EffectiveUnitWeightInKg()<=entity.MaxEncumbrance;
        }

        private static int MatchingItems(ItemCollection items,DaggerfallUnityItem item)
        {
            int total=0;
            if(items==null || item==null) return total;
            for(int i=0;i<items.Count;i++)
            {
                var candidate=items.GetItem(i);
                if(candidate!=null && candidate.ItemGroup==item.ItemGroup && candidate.TemplateIndex==item.TemplateIndex) total+=candidate.stackCount;
            }
            return total;
        }

        private static NpcActionOption ActualShopPurchaseOffer(StreetPopulation streets,WorldStateStore store,ResidentActor actor,DaggerfallInterior room,
            out string itemName,out DaggerfallUnityItem shopItem)
        {
            itemName=null; shopItem=null;
            var inventory=store==null?null:store.ReadNpcInventory(actor.Resident.Id);
            if(inventory==null) return null;
            foreach(var shelf in room.GetComponentsInChildren<DaggerfallLoot>())
            for(int index=0;index<shelf.Items.Count;index++)
            {
                var item=shelf.Items.GetItem(index);
                if(item==null || string.IsNullOrWhiteSpace(item.LongName)) continue;
                foreach(var option in streets.ConversationActionOptions(actor.Resident.Id,"Buy one "+item.LongName+"."))
                {
                    ulong optionUid;
                    if(option.Kind!=NpcActionKind.Purchase || option.Target==null || option.Target.ContainerId==NpcShopLootSale.TargetContainerId ||
                        option.Target.Quantity!=1 || option.Target.QuotedGold<=0 || option.Target.QuotedGold>inventory.WalletGold ||
                        !ulong.TryParse(option.Target.ItemId,NumberStyles.None,CultureInfo.InvariantCulture,out optionUid) || optionUid!=item.UID) continue;
                    itemName=item.LongName; shopItem=item; return option;
                }
            }
            return null;
        }

        private static void Require(bool value,string message)
        { if(!value) throw new InvalidOperationException(message); }
    }
}
