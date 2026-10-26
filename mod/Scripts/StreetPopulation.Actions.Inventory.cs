using System;
using System.Collections.Generic;
using System.Globalization;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private static bool TransferableActionItem(DaggerfallUnityItem item)
        {
            return item!=null && !item.IsQuestItem && !item.IsEquipped && !item.IsSummoned && !item.RepairData.IsBeingRepaired() && item.stackCount>0 &&
                !(item is HouseholdPropertyKeyItem) &&
                (item.stackCount==1 || item.IsAStack() && string.IsNullOrEmpty(item.GetSaveData().className));
        }
        private void OfferInventoryActions(ResidentActor actor,string text,List<NpcActionOption> options,bool proposals=false,bool semantic=true)
        {
            int semanticStart=options.Count;
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();
            if(!owner || owner.Binding==null) return;
            string reason;
            if(!NativeNpcInventoryBridge.TryCapture(worldStore,owner.Binding,out reason)) return;
            for(int i=0;i<owner.Binding.Items.Count && options.Count<(semantic?NpcActionCandidateCatalog.MaximumCandidates:24) && (!semantic || options.Count-semanticStart<NpcActionCandidateCatalog.MaximumPerGroup);i++)
            {
                var item=owner.Binding.Items.GetItem(i);int quantity=1;
                if(!TransferableActionItem(item)) continue;
                int maximum=semantic?MaximumSemanticPlayerTransferQuantity(item):0;
                if(semantic && maximum<1) continue;
                options.Add(new NpcActionOption {Id=options.Count+1,Kind=NpcActionKind.Transfer,
                    MaximumQuantity=semantic && maximum>1?maximum:0,
                    Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player",ItemId=item.UID.ToString(CultureInfo.InvariantCulture),Quantity=quantity},
                    Label=semantic?"give you up to "+maximum.ToString(CultureInfo.InvariantCulture)+" x "+item.LongName+" from my belongings":
                        "give you "+quantity.ToString(CultureInfo.InvariantCulture)+" x "+item.LongName+" from my belongings"});
            }
        }
        private void OfferPlayerInventoryActions(ResidentActor actor,string text,List<NpcActionOption> options,bool semantic=true)
        {
            int semanticStart=options.Count;
            bool ordinaryReceiver=EligibleNpcTransferReceiver(actor,true);
            string companionCondition=null;
            bool companionReceiver=!ordinaryReceiver && TryCurrentCompanionHealthRequest(actor,ActionNow(),
                out companionCondition,out _,out _);
            if(options.Count>=(semantic?NpcActionCandidateCatalog.MaximumCandidates:24) ||
                !ordinaryReceiver && !companionReceiver ||
                Vector3.Distance(actor.transform.position,GameManager.Instance.PlayerMotor.transform.position)>20) return;
            var player=GameManager.Instance.PlayerEntity;
            if(player==null || player.Items==null) return;
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();
            string reason;
            if(!owner || owner.Binding==null) return;
            for(int i=0;i<player.Items.Count && options.Count<(semantic?NpcActionCandidateCatalog.MaximumCandidates:24) && (!semantic || options.Count-semanticStart<NpcActionCandidateCatalog.MaximumPerGroup);i++)
            {
                var item=player.Items.GetItem(i);int quantity=1;
                if(!TransferableActionItem(item)) continue;
                if(!ordinaryReceiver && (!NativeNpcConditionTreatment.MatchesPotion(item,
                    CompanionCureEffect(companionCondition)) || quantity!=1))continue;
                if(semantic && !NativeNpcInventoryBridge.CanAppendExactItem(owner.Binding.Items,item,out reason)) continue;
                int maximum=semantic?(ordinaryReceiver?MaximumSemanticTransferQuantity(actor,item):1):0;
                if(semantic && maximum<1) continue;
                if(semantic) quantity=1;
                options.Add(new NpcActionOption {Id=options.Count+1,Kind=NpcActionKind.Transfer,
                    MaximumQuantity=semantic && maximum>1?maximum:0,
                    Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player",ItemId=item.UID.ToString(CultureInfo.InvariantCulture),
                        Quantity=quantity,TransferDirection=NpcActionTransferDirection.PlayerToNpc},
                    Label=semantic?"accept up to "+maximum.ToString(CultureInfo.InvariantCulture)+" x "+item.LongName+" from you into my belongings":
                        "accept "+quantity.ToString(CultureInfo.InvariantCulture)+" x "+item.LongName+" from you into my belongings"});
            }
        }
        private static string ActionRecipientName(ResidentActor actor)
        {return actor && actor.Person && !string.IsNullOrWhiteSpace(actor.Person.NameNPC)?actor.Person.NameNPC:null;}
        private static int MaximumSemanticTransferQuantity(ResidentActor recipient,DaggerfallUnityItem item)
        {
            var behaviour=recipient?recipient.GetComponent<DaggerfallEntityBehaviour>():null;
            var entity=behaviour?behaviour.Entity:null;
            if(entity==null || item==null || item.stackCount<1) return 0;
            float unitWeight=item.EffectiveUnitWeightInKg();
            if(unitWeight<=0) return item.stackCount;
            float remaining=entity.MaxEncumbrance-entity.Items.GetWeight();
            if(remaining<unitWeight) return 0;
            return Math.Min(item.stackCount,(int)Math.Floor(remaining/unitWeight));
        }
        private static int MaximumSemanticPlayerTransferQuantity(DaggerfallUnityItem item)
        {
            var player=GameManager.Instance.PlayerEntity;
            if(player==null || item==null || item.stackCount<1) return 0;
            float unitWeight=item.EffectiveUnitWeightInKg();
            if(unitWeight<=0) return item.stackCount;
            float remaining=player.MaxEncumbrance-player.CarriedWeight;
            if(remaining<unitWeight) return 0;
            return Math.Min(item.stackCount,(int)Math.Floor(remaining/unitWeight));
        }
        private bool EligibleNpcItemRecipient(ResidentActor source,ResidentActor recipient)
        {
            return source && recipient && source!=recipient && source.gameObject.scene==recipient.gameObject.scene &&
                EligibleNpcTransferReceiver(recipient);
        }
        // Bind only a unique actor already active in this scene. Display text never
        // becomes an identity: PersonId comes from that verified actor.
        internal NpcActionOption TryNamedAcquiredItemHandover(ResidentActor source,string recipientId,int sourceIndex,IList<NpcActionOption> sources)
        {
            if(!source || sources==null || sources.Count!=1)return null;
            ResidentActor recipient=null;string recipientName=null;int matchingVisibleNames=0;
            foreach(var pair in actors)
            {
                var candidate=pair.Value;string name=ActionRecipientName(candidate);
                if(!candidate || !candidate.gameObject.activeInHierarchy || !candidate.Resident.IsAlive ||
                    string.IsNullOrWhiteSpace(name) || candidate.Resident.Id!=recipientId)continue;
                matchingVisibleNames++;
                if(EligibleNpcItemRecipient(source,candidate) &&
                    Vector3.Distance(source.transform.position,candidate.transform.position)<=20f)
                {recipient=candidate;recipientName=name;}
            }
            if(matchingVisibleNames!=1 || !recipient)return null;
            return NpcNamedAcquiredHandover.TryBind(sourceIndex,sources[0],
                new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId=recipient.Resident.Id},recipientName);
        }
        private bool EligibleNpcTransferReceiver(ResidentActor recipient,bool allowCurrentConversation=false,string expectedTransferId=null)
        {
            if(!recipient || !recipient.gameObject.activeInHierarchy || !recipient.Resident.IsAlive ||
                recipient.Resident.IsQuestProtected || recipient.Resident.AgeGroup!=ResidentAgeGroup.Adult || recipient.IsSleeping ||
                recipient.IsConversing && !allowCurrentConversation || QuestOwned(recipient.Resident) || IsAlarmed(recipient.Resident.Id) ||
                !NpcTransferReceiverPolicy.Allows(worldStore.ActiveNpcAction(recipient.Resident.Id),worldStore.ActiveNpcTask(recipient.Resident.Id),expectedTransferId)) return false;
            var owner=recipient.GetComponent<NativeNpcInventoryOwner>();
            return owner && owner.Binding!=null && owner.Binding.PersonId==recipient.Resident.Id &&
                recipient.GetComponent<DaggerfallEntityBehaviour>()!=null;
        }
        private void OfferNpcInventoryActions(ResidentActor actor,string text,List<NpcActionOption> options,bool semantic=true)
        {
            int semanticStart=options.Count;
            if(!semantic && string.IsNullOrWhiteSpace(text) || options.Count>=(semantic?NpcActionCandidateCatalog.MaximumCandidates:24)) return;
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();
            if(!owner || owner.Binding==null) return;
            string reason;if(!NativeNpcInventoryBridge.TryCapture(worldStore,owner.Binding,out reason)) return;
            var nearby=new List<ResidentActor>();
            foreach(var pair in actors)
            {
                var other=pair.Value;string name=ActionRecipientName(other);
                if(string.IsNullOrWhiteSpace(name) || !EligibleNpcItemRecipient(actor,other) ||
                    Vector3.Distance(actor.transform.position,other.transform.position)>20) continue;
                nearby.Add(other);
            }
            for(int candidate=0;candidate<nearby.Count && options.Count<(semantic?NpcActionCandidateCatalog.MaximumCandidates:24) && (!semantic || options.Count-semanticStart<NpcActionCandidateCatalog.MaximumPerGroup);candidate++)
            {
                var recipient=nearby[candidate];string name=ActionRecipientName(recipient);int duplicateCount=0;
                foreach(var pair in actors)
                {
                    var known=pair.Value;
                    if(known && known.gameObject.activeInHierarchy && known.Resident.IsAlive &&
                        string.Equals(ActionRecipientName(known),name,StringComparison.OrdinalIgnoreCase)) duplicateCount++;
                }
                if(duplicateCount!=1) continue;
                var recipientOwner=recipient.GetComponent<NativeNpcInventoryOwner>();
                if(!NativeNpcInventoryBridge.TryCapture(worldStore,recipientOwner.Binding,out reason)) continue;
                for(int i=0;i<owner.Binding.Items.Count && options.Count<(semantic?NpcActionCandidateCatalog.MaximumCandidates:24) && (!semantic || options.Count-semanticStart<NpcActionCandidateCatalog.MaximumPerGroup);i++)
                {
                    var item=owner.Binding.Items.GetItem(i);int quantity=1;
                    if(!TransferableActionItem(item)) continue;
                    if(semantic && !NativeNpcInventoryBridge.CanAppendExactItem(recipientOwner.Binding.Items,item,out reason)) continue;
                    int maximum=semantic?MaximumSemanticTransferQuantity(recipient,item):0;
                    if(semantic && maximum<1) continue;
                    if(semantic) quantity=1;
                    options.Add(new NpcActionOption {Id=options.Count+1,Kind=NpcActionKind.Transfer,
                        MaximumQuantity=semantic && maximum>1?maximum:0,
                        Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId=recipient.Resident.Id,
                            ItemId=item.UID.ToString(CultureInfo.InvariantCulture),Quantity=quantity},
                        Label=semantic?"give up to "+maximum.ToString(CultureInfo.InvariantCulture)+" "+item.LongName+" to "+name+" from my belongings":
                            "give "+quantity.ToString(CultureInfo.InvariantCulture)+" "+item.LongName+" to "+name+" from my belongings"});
                }
            }
        }
        // Available decisions for ordinary conversation. These are proposals only;
        // the player must assent before the normal acceptance checks run again.
        public List<NpcActionOption> ConversationActionProposals(string residentId)
        {
            var options=new List<NpcActionOption>();
            foreach(var candidate in ConversationActionCandidates(residentId))
            {
                if(candidate.Kind!=NpcActionKind.Follow && candidate.Kind!=NpcActionKind.Wait)continue;
                candidate.Id=options.Count+1;options.Add(candidate);
            }
            var actor=FindActor(residentId);
            if(actor)OfferInventoryActions(actor,null,options,true,true);
            return options;
        }
        private bool PrepareInventoryAction(ResidentActor actor,NpcActionJob job,out string reason)
        {
            reason="The requested item is no longer available.";
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();ulong uid;
            if(!owner || owner.Binding==null || job.Target==null || job.Target.Kind!=NpcActionTargetKind.Person ||
                !ulong.TryParse(job.Target.ItemId,NumberStyles.None,CultureInfo.InvariantCulture,out uid) || job.Target.Quantity<1) return false;
            if(!NativeNpcInventoryBridge.TryCapture(worldStore,owner.Binding,out reason)) return false;
            if(job.Target.TransferDirection==NpcActionTransferDirection.PlayerToNpc)
            {
                var player=GameManager.Instance.PlayerEntity;
                bool ordinaryReceiver=EligibleNpcTransferReceiver(actor,true,job.Id);
                bool companionReceiver=!ordinaryReceiver && IsCompanionHealthHandoff(actor,job,ActionNow());
                if(job.Target.PersonId!="player" || !ordinaryReceiver && !companionReceiver || player==null || player.Items==null ||
                    Vector3.Distance(actor.transform.position,GameManager.Instance.PlayerMotor.transform.position)>20)
                {reason="The NPC cannot accept items from the player now.";return false;}
                var offered=player.Items.GetItem(uid);
                if(!TransferableActionItem(offered) || job.Target.Quantity>offered.stackCount ||
                    job.Target.Quantity<offered.stackCount && (!offered.IsAStack() || !string.IsNullOrEmpty(offered.GetSaveData().className)))
                {reason="The exact player item is missing, protected, equipped or cannot safely split.";return false;}
                var entity=actor.GetComponent<DaggerfallEntityBehaviour>().Entity;
                if(entity==null || entity.Items.GetWeight()+offered.EffectiveUnitWeightInKg()*job.Target.Quantity>entity.MaxEncumbrance)
                {reason="The NPC cannot carry that quantity.";return false;}
                job.ExpectedInventoryRevision=owner.Binding.Revision;job.ExpectedTargetInventoryRevision=0;return true;
            }
            if(job.Target.TransferDirection!=NpcActionTransferDirection.NpcToCounterparty)
            {reason="The item transfer direction is invalid.";return false;}
            var item=owner.Binding.Items.GetItem(uid);
            if(!TransferableActionItem(item) || job.Target.Quantity>item.stackCount ||
                job.Target.Quantity<item.stackCount && (!item.IsAStack() || !string.IsNullOrEmpty(item.GetSaveData().className)))
            {reason="The item is missing, protected, equipped or cannot safely split at requested quantity.";return false;}
            if(job.Target.PersonId=="player")
            {
                var player=GameManager.Instance.PlayerEntity;
                if(player.CarriedWeight+item.EffectiveUnitWeightInKg()*job.Target.Quantity>player.MaxEncumbrance)
                {reason="The player cannot carry that quantity.";return false;}
                job.ExpectedTargetInventoryRevision=0;
            }
            else
            {
                var recipient=FindActor(job.Target.PersonId);var recipientOwner=recipient?recipient.GetComponent<NativeNpcInventoryOwner>():null;
                if(!EligibleNpcItemRecipient(actor,recipient) || !recipientOwner || recipientOwner.Binding==null ||
                    Vector3.Distance(actor.transform.position,recipient.transform.position)>20)
                {reason="The named recipient is no longer eligible nearby.";return false;}
                if(!NativeNpcInventoryBridge.TryCapture(worldStore,recipientOwner.Binding,out reason)) return false;
                var entity=recipient.GetComponent<DaggerfallEntityBehaviour>().Entity;
                if(entity==null || entity.Items.GetWeight()+item.EffectiveUnitWeightInKg()*job.Target.Quantity>entity.MaxEncumbrance)
                {reason="The named recipient cannot carry that quantity.";return false;}
                job.ExpectedTargetInventoryRevision=recipientOwner.Binding.Revision;
            }
            job.ExpectedInventoryRevision=owner.Binding.Revision;return true;
        }
        private bool ExecuteInventoryAction(NpcActionJob job,ResidentActor actor,double now,out string reason)
        {
            if(job!=null && job.Target!=null && job.Target.TransferDirection==NpcActionTransferDirection.NpcToContainer)
                return ExecuteHomeContainerDepositAction(job,actor,now,out reason);
            if(job!=null && job.Target!=null && job.Target.TransferDirection==NpcActionTransferDirection.HomeContainerToNpc)
                return ExecuteHomeContainerRetrievalAction(job,actor,now,out reason);
            if(job!=null && job.Target!=null && job.Target.TransferDirection==NpcActionTransferDirection.ObservedLootToNpc)
                return ExecuteObservedLawfulLootAction(job,actor,now,out reason);
            reason="The item handover is no longer valid.";
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();
            if(!owner || owner.Binding==null) return false;
            var player=GameManager.Instance.PlayerEntity;
            if(player==null || player.Items==null) return false;
            if(job.Target.TransferDirection==NpcActionTransferDirection.PlayerToNpc)
                return NativeNpcInventoryBridge.TryCompleteActionTransfer(worldStore,job,now,null,player.Items,owner.Binding,null,()=>{
                    ulong uid;
                    if(job.Target.PersonId!="player" || !EligibleNpcTransferReceiver(actor,true,job.Id) || !actor.Resident.IsAlive ||
                        QuestOwned(actor.Resident) || IsAlarmed(job.ResidentId) || player==null || player.Items==null ||
                        Vector3.Distance(actor.transform.position,GameManager.Instance.PlayerMotor.transform.position)>1.5f ||
                        !ulong.TryParse(job.Target.ItemId,NumberStyles.None,CultureInfo.InvariantCulture,out uid)) return false;
                    var item=player.Items.GetItem(uid);
                    if(!TransferableActionItem(item) || item.stackCount<job.Target.Quantity) return false;
                    var entity=actor.GetComponent<DaggerfallEntityBehaviour>().Entity;
                    return entity!=null && entity.Items.GetWeight()+item.EffectiveUnitWeightInKg()*job.Target.Quantity<=entity.MaxEncumbrance;
                },out reason);
            var recipient=job.Target.PersonId=="player"?null:FindActor(job.Target.PersonId);
            var recipientOwner=recipient?recipient.GetComponent<NativeNpcInventoryOwner>():null;
            return NativeNpcInventoryBridge.TryCompleteActionTransfer(worldStore,job,now,owner.Binding,
                recipientOwner?recipientOwner.Binding:null,recipient==null?player.Items:null,()=>{
                ulong uid;
                if(!ulong.TryParse(job.Target.ItemId,out uid)) return false;
                var item=owner.Binding.Items.GetItem(uid);
                if(!TransferableActionItem(item) || item.stackCount<job.Target.Quantity || !actor.Resident.IsAlive ||
                    QuestOwned(actor.Resident) || IsAlarmed(job.ResidentId)) return false;
                if(recipient==null)
                    return job.Target.PersonId=="player" &&
                        Vector3.Distance(actor.transform.position,GameManager.Instance.PlayerMotor.transform.position)<=1.5f &&
                        player.CarriedWeight+item.EffectiveUnitWeightInKg()*job.Target.Quantity<=player.MaxEncumbrance;
                if(!recipientOwner || recipientOwner.Binding==null || !EligibleNpcItemRecipient(actor,recipient) ||
                    Vector3.Distance(actor.transform.position,recipient.transform.position)>1.5f ||
                    !CivilianResponses.Sees(actor.transform.position,recipient.transform.position,1.5f)) return false;
                var entity=recipient.GetComponent<DaggerfallEntityBehaviour>().Entity;
                return entity!=null && entity.Items.GetWeight()+item.EffectiveUnitWeightInKg()*job.Target.Quantity<=entity.MaxEncumbrance;
            },out reason);
        }
        private string ActionItemName(NpcActionJob job,ResidentActor actor)
        {
            if(job.ServiceCode==NpcShopCommerce.ServiceCode && job.Target!=null && actor) return ShopPurchaseItemName(job,actor);
            if(job.ServiceCode==NpcShopLootSale.ServiceCode && job.Target!=null && actor) return NativeLootSaleItemName(job,actor);
            if(job.Kind!=NpcActionKind.Transfer || job.Target==null || !actor) return null;
            ulong uid;if(!ulong.TryParse(job.Target.ItemId,NumberStyles.None,CultureInfo.InvariantCulture,out uid)) return null;
            if(job.Target.TransferDirection==NpcActionTransferDirection.PlayerToNpc)
            {var player=GameManager.Instance.PlayerEntity;var offered=player==null?null:player.Items.GetItem(uid);return offered==null?null:offered.LongName;}
            if(job.Target.TransferDirection==NpcActionTransferDirection.HomeContainerToNpc)
            {
                var loot=ResolveOwnHomeRetrievalContainer(actor,job.Target);var retrieved=loot?loot.Items.GetItem(uid):null;
                return retrieved==null?null:retrieved.LongName;
            }
            if(job.Target.TransferDirection==NpcActionTransferDirection.ObservedLootToNpc)
            {
                DaggerfallLoot loot;
                if(!TryResolveObservedLawfulLoot(job.Target,out loot))return null;
                var taken=loot.Items.GetItem(uid);return taken==null?null:taken.LongName;
            }
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();
            if(!owner || owner.Binding==null) return null;
            var item=owner.Binding.Items.GetItem(uid);return item==null?null:item.LongName;
        }
        private string InventoryActionOutcome(NpcActionJob job)
        {
            if(job.Target.TransferDirection==NpcActionTransferDirection.NpcToContainer)
                return "The NPC physically deposited "+job.Target.Quantity+" unit(s) from exact native item UID "+job.Target.ItemId+
                    " into its own verified permanent home container "+job.Target.ContainerId+".";
            if(job.Target.TransferDirection==NpcActionTransferDirection.PlayerToNpc)
                return "The nearby player transferred "+job.Target.Quantity+" unit(s) from exact native item UID "+job.Target.ItemId+
                    " into the NPC's actual belongings.";
            if(job.Target.TransferDirection==NpcActionTransferDirection.HomeContainerToNpc)
                return "The NPC physically retrieved "+job.Target.Quantity+" unit(s) from exact native household-container item UID "+job.Target.ItemId+
                    " into its actual belongings. This records household access, not personal ownership of all stored goods.";
            if(job.Target.TransferDirection==NpcActionTransferDirection.ObservedLootToNpc)
                return "The NPC physically took "+job.Target.Quantity+" unit(s) from observed lawful-defense loot "+job.Target.ContainerId+
                    " at exact native item UID "+job.Target.ItemId+" into its actual belongings.";
            if(job.Target.PersonId=="player")
                return "The NPC physically transferred "+job.Target.Quantity+" unit(s) from native item UID "+job.Target.ItemId+
                    " in its actual belongings to the nearby player.";
            var recipient=FindActor(job.Target.PersonId);string name=ActionRecipientName(recipient);
            return "The NPC physically transferred "+job.Target.Quantity+" unit(s) from native item UID "+job.Target.ItemId+
                " in its actual belongings to "+(string.IsNullOrWhiteSpace(name)?"managed person "+job.Target.PersonId:name)+".";
        }
    }
}

