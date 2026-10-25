using System;
using System.Collections.Generic;
using System.Globalization;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>Uses one exact native healing potion owned by a live resident.</summary>
    public static class NativeNpcHealingItem
    {
        private static readonly System.Reflection.MethodInfo RestoreItemData=typeof(DaggerfallUnityItem).GetMethod(
            "FromItemData",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);

        public static bool IsSupportedPotion(DaggerfallUnityItem item)
        {
            if(item==null || !item.IsPotion || item.IsEnchanted || item.IsQuestItem || item.IsSummoned || item.PotionRecipeKey==0)
                return false;
            try
            {
                GameManager game=GameManager.Instance;
                return game!=null && game.EntityEffectBroker!=null && IsSupportedRecipe(game,item.PotionRecipeKey);
            }
            catch { return false; }
        }

        // Resolve from DFU's registered recipes. Never bake in the classic
        // recipe hash, which can vary with custom content and registry changes.
        public static bool TryFindHealingPotionRecipeKey(out int recipeKey)
        {
            recipeKey=0;
            GameManager game=GameManager.Instance;
            if(game==null || game.EntityEffectBroker==null)return false;
            List<int> keys=game.EntityEffectBroker.GetPotionRecipeKeys();
            if(keys==null)return false;
            foreach(int key in keys)
            {
                if(IsSupportedRecipe(game,key)) { recipeKey=key;return true; }
            }
            return false;
        }

        private static bool IsSupportedRecipe(GameManager game,int recipeKey)
        {
            PotionRecipe recipe=game.EntityEffectBroker.GetPotionRecipe(recipeKey);
            if(recipe==null || recipe.GetHashCode()!=recipeKey ||
                recipe.SecondaryEffects!=null && recipe.SecondaryEffects.Count!=0)return false;
            IEntityEffect effect=game.EntityEffectBroker.GetPotionRecipeEffect(recipe);
            return effect!=null && effect.GetType()==typeof(HealHealth) &&
                effect.Key==HealHealth.EffectKey && !effect.Properties.SupportDuration;
        }

        // DFU's HealHealth.GetMagnitude rolls inclusive random base and plus
        // values, then adds base + plus * (casterLevel / perLevel). Returning
        // the minimum roll gives need-scoring a guaranteed lower bound for this
        // exact native recipe; it does not assume a full heal.
        public static bool TryEstimateHealthGain(DaggerfallUnityItem item,DaggerfallEntity caster,out int gain)
        {
            gain=0;
            if(!IsSupportedPotion(item) || caster==null)return false;
            try
            {
                GameManager game=GameManager.Instance;
                PotionRecipe recipe=game==null || game.EntityEffectBroker==null?null:
                    game.EntityEffectBroker.GetPotionRecipe(item.PotionRecipeKey);
                IEntityEffect effect=recipe==null?null:game.EntityEffectBroker.GetPotionRecipeEffect(recipe);
                if(effect==null || effect.GetType()!=typeof(HealHealth) || !effect.Properties.SupportMagnitude)return false;
                EffectSettings settings=recipe.Settings;
                if(settings.MagnitudePerLevel<=0 || settings.MagnitudeBaseMin<0 || settings.MagnitudePlusMin<0 ||
                    settings.MagnitudeBaseMax<settings.MagnitudeBaseMin ||
                    settings.MagnitudePlusMax<settings.MagnitudePlusMin)return false;
                int casterLevel=FormulaHelper.CalculateCasterLevel(caster,effect);
                int multiplier=casterLevel/settings.MagnitudePerLevel;
                long magnitude=(long)settings.MagnitudeBaseMin+(long)settings.MagnitudePlusMin*multiplier;
                if(magnitude<=0 || magnitude>int.MaxValue)return false;
                gain=(int)magnitude;
                return true;
            }
            catch{return false;}
        }

        public static bool TryGetConservativeHealingMagnitude(DaggerfallUnityItem item,DaggerfallEntity caster,
            out int minimumMagnitude)
        {return TryEstimateHealthGain(item,caster,out minimumMagnitude);}

        internal static bool IsExactlyOneUnitConsumed(IList<NpcInventoryItem> before,IList<NpcInventoryItem> after,ulong uid)
        {
            if(before==null || after==null || uid==0)return false;
            try
            {
                var original=new Dictionary<ulong,string>();
                foreach(NpcInventoryItem item in before)
                {
                    if(item==null || item.Uid==0 || string.IsNullOrEmpty(item.NativePayload) || original.ContainsKey(item.Uid))return false;
                    original.Add(item.Uid,item.NativePayload);
                }
                string sourcePayload;
                if(!original.TryGetValue(uid,out sourcePayload))return false;
                ItemData_v1 source=(ItemData_v1)SaveLoadManager.Deserialize(typeof(ItemData_v1),sourcePayload);
                if(source==null || source.uid!=uid || source.stackCount<=0)return false;
                bool finalUnit=source.stackCount==1;
                int expectedCount=before.Count-(finalUnit?1:0);
                if(after.Count!=expectedCount)return false;

                string decrementedPayload=null;
                if(!finalUnit)
                {
                    source.stackCount--;
                    decrementedPayload=SaveLoadManager.Serialize(typeof(ItemData_v1),source,false);
                }
                bool foundSource=false;
                var seen=new HashSet<ulong>();
                foreach(NpcInventoryItem item in after)
                {
                    if(item==null || item.Uid==0 || string.IsNullOrEmpty(item.NativePayload) || !seen.Add(item.Uid))return false;
                    if(item.Uid==uid)
                    {
                        if(finalUnit || foundSource || item.NativePayload!=decrementedPayload)return false;
                        foundSource=true;
                    }
                    else
                    {
                        string unchanged;
                        if(!original.TryGetValue(item.Uid,out unchanged) || unchanged!=item.NativePayload)return false;
                    }
                }
                return finalUnit?!foundSource:foundSource;
            }
            catch{return false;}
        }

        public static bool TryFindOwned(NativeNpcInventoryBinding binding,out ulong uid)
        {
            uid=0;
            if(binding==null || binding.Items==null)return false;
            for(int i=0;i<binding.Items.Count;i++)
            {
                DaggerfallUnityItem item=binding.Items.GetItem(i);
                if(item!=null && IsSupportedPotion(item)) { uid=item.UID;return uid!=0; }
            }
            return false;
        }

        public static bool TryExecute(WorldStateStore store,NpcActionJob job,GameObject actor,double now,out string reason)
        {
            EntityEffectManager manager=actor?actor.GetComponent<EntityEffectManager>():null;
            if(manager==null) { reason="The resident has no native potion-effect manager.";return false; }
            return TryExecute(store,job,actor,now,manager.DrinkPotion,out reason);
        }

        // The effect delegate is the narrow CPU-test seam. Runtime callers use
        // the overload above, which always invokes DFU's EntityEffectManager.
        internal static bool TryExecute(WorldStateStore store,NpcActionJob job,GameObject actor,double now,
            Action<DaggerfallUnityItem> drinkPotion,out string reason)
        {
            reason="The resident cannot safely use that healing item.";
            if(store==null || job==null || actor==null || drinkPotion==null ||
                job.Kind!=NpcActionKind.UseHealingItem || job.Target==null ||
                (job.Target.Kind!=NpcActionTargetKind.IndoorPoint && job.Target.Kind!=NpcActionTargetKind.WorldPoint) ||
                job.Target.Quantity!=1 || job.Target.PersonId!=null || job.Target.ContainerId!=null ||
                double.IsNaN(now) || double.IsInfinity(now))return false;

            ulong uid;
            if(!ulong.TryParse(job.Target.ItemId,NumberStyles.None,CultureInfo.InvariantCulture,out uid) || uid==0)return false;
            NativeNpcInventoryOwner owner=actor.GetComponent<NativeNpcInventoryOwner>();
            NativeNpcVitalsBinding vitals=actor.GetComponent<NativeNpcVitalsBinding>();
            DaggerfallEntityBehaviour body=actor.GetComponent<DaggerfallEntityBehaviour>();
            ResidentActor residentActor=actor.GetComponent<ResidentActor>();
            if(owner==null || owner.Binding==null || owner.Binding.Items==null || vitals==null || body==null || body.Entity==null ||
                residentActor==null || residentActor.Resident==null || residentActor.Resident.Id!=job.ResidentId ||
                owner.Binding.PersonId!=residentActor.Resident.Id || !ReferenceEquals(owner.Binding.Items,body.Entity.Items) ||
                RestoreItemData==null)return false;
            ItemCollection nativeItems=owner.Binding.Items;

            Resident resident=store.ResolveResident(job.ResidentId);
            if(resident==null || !NpcVitalsBindingPolicy.Eligible(resident.IsAlive,
                resident.AgeGroup==ResidentAgeGroup.Adult,resident.IsQuestProtected))return false;
            int nativeHealth,nativeMaximumHealth,nativeMagicka,nativeMaximumMagicka;
            if(!vitals.TryReadCurrent(out nativeHealth,out nativeMaximumHealth,out nativeMagicka,out nativeMaximumMagicka))return false;

            NpcArchetype archetype=store.ResolveNpcArchetype(job.ResidentId);
            NpcVitalsView savedVitals=store.ReadNpcVitals(job.ResidentId,archetype);
            NpcInventoryState savedInventory=store.ReadNpcInventory(job.ResidentId);
            if(savedInventory==null || savedVitals==null || !savedVitals.IsAlive || !savedVitals.IsPersisted ||
                savedVitals.CurrentHealth!=nativeHealth || savedVitals.CurrentMagicka!=nativeMagicka ||
                savedVitals.MaximumHealth!=nativeMaximumHealth || savedVitals.MaximumMagicka!=nativeMaximumMagicka ||
                savedVitals.CurrentHealth>=savedVitals.MaximumHealth || savedInventory.Revision!=job.ExpectedInventoryRevision ||
                savedInventory.Revision!=owner.Binding.Revision ||
                store.NpcInventoryItemOwner(uid)!=job.ResidentId)return false;

            IList<NpcInventoryItem> beforeItems;
            DaggerfallUnityItem[] beforeObjects;
            ItemData_v1[] beforeData;
            try
            {
                beforeItems=NativeNpcInventoryBridge.CaptureItems(nativeItems);
                if(!SameItems(savedInventory.Items,beforeItems))return false;
                beforeObjects=new DaggerfallUnityItem[nativeItems.Count];
                beforeData=new ItemData_v1[nativeItems.Count];
                for(int i=0;i<beforeObjects.Length;i++)
                {
                    beforeObjects[i]=nativeItems.GetItem(i);
                    if(beforeObjects[i]==null)return false;
                    NpcInventoryItem savedItem=FindItem(beforeItems,beforeObjects[i].UID);
                    beforeData[i]=savedItem==null?null:(ItemData_v1)SaveLoadManager.Deserialize(typeof(ItemData_v1),savedItem.NativePayload);
                    if(beforeData[i]==null || beforeData[i].uid!=beforeObjects[i].UID)return false;
                }
            }
            catch { return false; }

            DaggerfallUnityItem potion=nativeItems.GetItem(uid);
            if(potion==null || potion.stackCount<=0 || !IsSupportedPotion(potion))return false;
            bool nativeEffectStarted=false;
            bool committed;
            try
            {
                committed=store.TryCompleteNpcHealingItemAction(job.Id,job.Revision,now,archetype,
                    savedVitals.Revision,savedInventory.Revision,() =>
                    {
                        ResidentActor currentActor=actor.GetComponent<ResidentActor>();
                        if(!vitals.TryReadCurrent(out nativeHealth,out nativeMaximumHealth,out nativeMagicka,out nativeMaximumMagicka) ||
                            nativeHealth!=savedVitals.CurrentHealth || nativeMagicka!=savedVitals.CurrentMagicka ||
                            currentActor!=residentActor || currentActor.Resident==null || currentActor.Resident.Id!=job.ResidentId ||
                            owner.Binding.Revision!=savedInventory.Revision || !ReferenceEquals(owner.Binding.Items,nativeItems) ||
                            nativeItems.GetItem(uid)!=potion || !ReferenceEquals(nativeItems,body.Entity.Items) || !IsSupportedPotion(potion))
                            throw new InvalidOperationException("Native healing preflight changed.");

                        nativeEffectStarted=true;
                        drinkPotion(potion);
                        int afterHealth=body.Entity.CurrentHealth;
                        int afterMagicka=body.Entity.CurrentMagicka;
                        if(afterHealth<=nativeHealth || afterHealth>nativeMaximumHealth || afterMagicka!=nativeMagicka)
                            throw new InvalidOperationException("The native potion did not produce one health-only improvement.");

                        nativeItems.RemoveOne(potion);
                        IList<NpcInventoryItem> remainingItems=NativeNpcInventoryBridge.CaptureItems(nativeItems);
                        if(!IsExactlyOneUnitConsumed(beforeItems,remainingItems,uid))
                            throw new InvalidOperationException("The native inventory changed beyond one potion stack unit.");
                        return NpcTreatmentNativeOutcome.Verified(uid,nativeHealth,afterHealth,afterMagicka,remainingItems);
                    });
            }
            catch(Exception error)
            {
                if(nativeEffectStarted)
                {
                    try
                    {
                        owner.Binding.Items=nativeItems;
                        Rollback(body.Entity,nativeItems,beforeObjects,beforeData,beforeItems,nativeHealth,nativeMagicka);
                    }
                    catch(Exception rollbackError){reason=error.Message+" Rollback failed: "+rollbackError.Message;return false;}
                }
                reason=error.Message;return false;
            }

            if(!committed)
            {
                if(nativeEffectStarted)
                {
                    try
                    {
                        owner.Binding.Items=nativeItems;
                        Rollback(body.Entity,nativeItems,beforeObjects,beforeData,beforeItems,nativeHealth,nativeMagicka);
                    }
                    catch(Exception rollbackError){reason="Treatment commit was rejected and rollback failed: "+rollbackError.Message;return false;}
                }
                reason="The healing item or action changed before the treatment could commit.";return false;
            }

            // This only advances the sampler cache after Core has committed the
            // same native values; all eligibility and epoch checks ran above.
            owner.Binding.Revision=savedInventory.Revision+1;
            vitals.RebaselineAfterExternalCommit();
            reason=null;return true;
        }

        private static void Rollback(DaggerfallEntity entity,ItemCollection items,DaggerfallUnityItem[] original,
            ItemData_v1[] originalData,IList<NpcInventoryItem> originalPayloads,int health,int magicka)
        {
            bool quiesce=entity.Quiesce;
            try
            {
                entity.Quiesce=true;
                entity.SetHealth(health);
                entity.SetMagicka(magicka);
            }
            finally { entity.Quiesce=quiesce; }
            items.Clear();
            for(int i=0;i<original.Length;i++)
            {
                if(RestoreItemData!=null)RestoreItemData.Invoke(original[i],new object[]{originalData[i]});
                else original[i].stackCount=originalData[i].stackCount;
                items.AddItem(original[i],noStack:true);
            }
            if(!SameItems(originalPayloads,NativeNpcInventoryBridge.CaptureItems(items)))
                throw new InvalidOperationException("Restored native item payloads differ from the exact pre-treatment snapshot.");
        }

        private static NpcInventoryItem FindItem(IList<NpcInventoryItem> items,ulong uid)
        {if(items==null)return null;foreach(NpcInventoryItem item in items)if(item!=null && item.Uid==uid)return item;return null;}

        private static bool SameItems(IList<NpcInventoryItem> first,IList<NpcInventoryItem> second)
        {
            if(first==null || second==null || first.Count!=second.Count)return false;
            var byUid=new Dictionary<ulong,string>();
            foreach(NpcInventoryItem item in first)
            {
                if(item==null || item.Uid==0 || byUid.ContainsKey(item.Uid))return false;
                byUid.Add(item.Uid,item.NativePayload);
            }
            foreach(NpcInventoryItem item in second)
            {
                string payload;
                if(item==null || !byUid.TryGetValue(item.Uid,out payload) || payload!=item.NativePayload)return false;
            }
            return true;
        }
    }
}
