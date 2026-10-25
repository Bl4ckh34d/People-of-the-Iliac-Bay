using System;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static class NativeNpcConditionTreatment
    {
        internal static bool HasOwnTreatment(ResidentActor actor,WorldStateStore store,Func<bool> available)
        {
            var effects=actor.GetComponent<EntityEffectManager>();
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();
            if(!effects || !owner || owner.Binding==null)return false;
            string key=effects.PoisonCount>0?CurePoison.EffectKey:effects.DiseaseCount>0?CureDisease.EffectKey:null;
            if(key==null)return false;
            for(int i=0;i<owner.Binding.Items.Count;i++)
                if(MatchesPotion(owner.Binding.Items.GetItem(i),key))return true;
            foreach(var offer in NativeNpcMagicAction.DiscoverCareOptions(actor.gameObject,store,available))
                if(offer.Authorization.EffectId==key)return true;
            return false;
        }

        // Instant self-treatment yields one movement update, then the existing task/routine continues.
        internal static bool TryTreatOwned(ResidentActor actor,WorldStateStore store,Func<bool> available)
        {
            if(!actor || store==null || available==null || !available())return false;
            var body=actor.GetComponent<DaggerfallEntityBehaviour>();
            var effects=actor.GetComponent<EntityEffectManager>();
            var vitals=actor.GetComponent<NativeNpcVitalsBinding>();
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();
            if(!body || !NativeNpcDiseases.Eligible(body) || !effects || !vitals || !owner || owner.Binding==null ||
                effects.HasReadySpell || effects.PoisonCount==0 && effects.DiseaseCount==0)return false;
            vitals.Capture();owner.Capture();
            foreach(string key in new[] {CurePoison.EffectKey,CureDisease.EffectKey})
            {
                if((key==CurePoison.EffectKey?effects.PoisonCount:effects.DiseaseCount)==0)continue;
                bool spellAttempted=false;
                foreach(var offer in NativeNpcMagicAction.DiscoverCareOptions(actor.gameObject,store,available))
                {
                    if(offer.Authorization.EffectId!=key)continue;
                    int before=body.Entity.CurrentMagicka;
                    NpcMagicObservedOutcome result;
                    bool cured=NativeNpcMagicAction.TryExecuteSelf(actor.gameObject,store,offer.Authorization,available,out result);
                    vitals.Capture();
                    if(cured || body.Entity.CurrentMagicka<before)
                    {
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CONDITION_SELF_CAST: resident="+actor.Resident.Id+"; effect="+key+"; observedCure="+cured);
                        if(cured)return true;
                        spellAttempted=true;break;
                    }
                }
                var items=owner.Binding.Items;
                for(int i=0;i<items.Count;i++)
                {
                    var potion=items.GetItem(i);
                    if(!MatchesPotion(potion,key))continue;
                    var before=effects.GetInstancedBundlesSaveData();int stack=potion.stackCount;
                    if(!available() || !ReferenceEquals(items,body.Entity.Items))return false;
                    try
                    {
                        effects.DrinkPotion(potion);
                        items.RemoveOne(potion);
                        string reason;
                        if(!NativeNpcInventoryBridge.TryCapture(store,owner.Binding,out reason))
                            throw new InvalidOperationException(reason);
                    }
                    catch
                    {
                        effects.RestoreInstancedBundleSaveData(before);
                        potion.stackCount=stack;if(items.GetItem(potion.UID)==null)items.AddItem(potion,noStack:true);
                        throw;
                    }
                    vitals.Capture();
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CONDITION_POTION: resident="+actor.Resident.Id+"; effect="+key+
                        "; remaining="+(key==CurePoison.EffectKey?effects.PoisonCount:effects.DiseaseCount));
                    return true;
                }
                if(spellAttempted)return true;
            }
            return false;
        }

        internal static bool MatchesPotion(DaggerfallUnityItem item,string key)
        {
            if(item==null || !item.IsPotion || item.IsEnchanted || item.IsQuestItem || item.IsSummoned || item.stackCount<1)return false;
            var broker=GameManager.Instance.EntityEffectBroker;var recipe=broker.GetPotionRecipe(item.PotionRecipeKey);
            if(recipe==null || recipe.SecondaryEffects!=null && recipe.SecondaryEffects.Count>0)return false;
            var effect=broker.GetPotionRecipeEffect(recipe);
            return effect!=null && effect.Key==key && (effect.GetType()==typeof(CurePoison) || effect.GetType()==typeof(CureDisease));
        }
    }
}
