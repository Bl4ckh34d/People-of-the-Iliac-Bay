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
        private const float ObservedLootMaximumDistance=6f;

        // Direct player request only. This does not scan scenes or invent a target.
        private void OfferObservedLawfulLootAction(ResidentActor actor,string text,List<NpcActionOption> options)
        {
            int initialCount=options.Count;
            if(actor==null || interior || options.Count>=NpcActionCandidateCatalog.MaximumCandidates)return;
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();var guards=GetComponent<GuardPopulation>();string reason;
            if(owner==null || owner.Binding==null || guards==null || !NativeNpcInventoryBridge.TryCapture(worldStore,owner.Binding,out reason))return;
            foreach(var record in worldStore.DefenseCorpsesFor(population.SettlementId))
            {
                if(record==null || record.Death==null || string.IsNullOrWhiteSpace(record.Death.CorpseId))continue;
                DaggerfallLoot loot;
                if(!guards.TryGetLawfulLootClaim(population.SettlementId,record.Death.CorpseId,out loot) || !ObservedBy(actor,loot))continue;
                for(int i=0;i<loot.Items.Count;i++)
                {
                    var item=loot.Items.GetItem(i);int quantity=1;
                    if(!TransferableActionItem(item) ||
                        owner.Binding.Items.GetItem(item.UID)!=null || !NativeNpcInventoryBridge.CanAppendExactItem(owner.Binding.Items,item,out reason))continue;
                    var entity=actor.GetComponent<DaggerfallEntityBehaviour>();
                    if(entity==null || entity.Entity==null || entity.Entity.Items.GetWeight()+item.EffectiveUnitWeightInKg()*quantity>entity.Entity.MaxEncumbrance)continue;
                    var source=new NpcObservedLootTake {ContainerId=record.Death.CorpseId,ItemId=item.UID.ToString(CultureInfo.InvariantCulture),Quantity=quantity,
                        Active=loot.gameObject.activeInHierarchy,UniqueContainer=true,ExactItemAvailable=true,LawfulDefenseClaim=true,ObservedLocally=true};
                    if(!NpcObservedLootTakePolicy.Allows(source))continue;
                    var target=ActionPosition(loot.transform.position);target.ContainerId=source.ContainerId;target.ItemId=source.ItemId;target.Quantity=quantity;
                    target.TransferDirection=NpcActionTransferDirection.ObservedLootToNpc;
                    int maximum=MaximumSemanticTransferQuantity(actor,item);
                    if(maximum<1)continue;
                    options.Add(new NpcActionOption {Id=options.Count+1,Kind=NpcActionKind.Transfer,Target=target,MaximumQuantity=maximum>1?maximum:0,
                        Label="take up to "+maximum.ToString(CultureInfo.InvariantCulture)+" x "+item.LongName+" from the observed lawful-defense corpse"});
                    if(options.Count>=NpcActionCandidateCatalog.MaximumCandidates || options.Count-initialCount>=NpcActionCandidateCatalog.MaximumPerGroup)return;
                }
            }
        }

        private bool PrepareObservedLawfulLootAction(ResidentActor actor,NpcActionJob job,out string reason)
        {
            reason="The observed lawful loot or exact item is no longer available.";
            DaggerfallLoot loot;var owner=actor?actor.GetComponent<NativeNpcInventoryOwner>():null;ulong uid;
            if(job==null || job.Target==null || job.Target.TransferDirection!=NpcActionTransferDirection.ObservedLootToNpc ||
                !TryResolveObservedLawfulLoot(job.Target,out loot) || owner==null || owner.Binding==null ||
                !ulong.TryParse(job.Target.ItemId,NumberStyles.None,CultureInfo.InvariantCulture,out uid) ||
                job.Target.PersonId!=null || job.Target.Quantity<1 || !NativeNpcInventoryBridge.TryCapture(worldStore,owner.Binding,out reason))return false;
            var item=loot.Items.GetItem(uid);var entity=actor.GetComponent<DaggerfallEntityBehaviour>();
            if(!ObservedBy(actor,loot) || !TransferableActionItem(item) || item.stackCount<job.Target.Quantity || owner.Binding.Items.GetItem(uid)!=null ||
                entity==null || entity.Entity==null || entity.Entity.Items.GetWeight()+item.EffectiveUnitWeightInKg()*job.Target.Quantity>entity.Entity.MaxEncumbrance ||
                !NativeNpcInventoryBridge.CanAppendExactItem(owner.Binding.Items,item,out reason))return false;
            job.ExpectedInventoryRevision=0;job.ExpectedTargetInventoryRevision=owner.Binding.Revision;return true;
        }

        private bool ExecuteObservedLawfulLootAction(NpcActionJob job,ResidentActor actor,double now,out string reason)
        {
            reason="The observed lawful loot is no longer valid.";
            DaggerfallLoot loot;var owner=actor?actor.GetComponent<NativeNpcInventoryOwner>():null;
            if(owner==null || owner.Binding==null || !TryResolveObservedLawfulLoot(job==null?null:job.Target,out loot) ||
                !ActionSpaceMatches(job.Target) || Vector3.Distance(actor.transform.position-Vector3.up,ActionWorldPoint(job.Target))>0.75f)return false;
            return NativeNpcInventoryBridge.TryCompleteActionTransfer(worldStore,job,now,null,loot.Items,owner.Binding,null,()=>
            {
                ulong uid;var entity=actor.GetComponent<DaggerfallEntityBehaviour>();
                if(!actor.Resident.IsAlive || actor.Resident.IsQuestProtected || QuestOwned(actor.Resident) || IsAlarmed(job.ResidentId) ||
                    !TryResolveObservedLawfulLoot(job.Target,out loot) || !ObservedBy(actor,loot) ||
                    !ulong.TryParse(job.Target.ItemId,NumberStyles.None,CultureInfo.InvariantCulture,out uid))return false;
                var item=loot.Items.GetItem(uid);string ignored;
                return TransferableActionItem(item) && item.stackCount>=job.Target.Quantity && owner.Binding.Items.GetItem(uid)==null &&
                    entity!=null && entity.Entity!=null && entity.Entity.Items.GetWeight()+item.EffectiveUnitWeightInKg()*job.Target.Quantity<=entity.Entity.MaxEncumbrance &&
                    NativeNpcInventoryBridge.CanAppendExactItem(owner.Binding.Items,item,out ignored);
            },out reason);
        }

        private bool TryResolveObservedLawfulLoot(NpcActionTarget target,out DaggerfallLoot loot)
        {
            loot=null;
            if(target==null || target.TransferDirection!=NpcActionTransferDirection.ObservedLootToNpc ||
                !ActionSpaceMatches(target) || interior || string.IsNullOrWhiteSpace(target.ContainerId))return false;
            var guards=GetComponent<GuardPopulation>();
            return guards!=null && guards.TryGetLawfulLootClaim(population.SettlementId,target.ContainerId,out loot);
        }

        private static bool ObservedBy(ResidentActor actor,DaggerfallLoot loot)
        {
            return actor && loot && (actor.transform.position-loot.transform.position).sqrMagnitude<=ObservedLootMaximumDistance*ObservedLootMaximumDistance &&
                CivilianResponses.Sees(actor.transform.position,loot.transform.position+Vector3.up*0.3f,ObservedLootMaximumDistance);
        }
    }
}
