using System;
using System.Collections;
using System.Reflection;
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
    internal static class NpcConditionsNativeProbe
    {
        internal static IEnumerator Run(PeopleOfTheIliacBayMod host,StreetPopulation streets,string character)
        {
            var game=GameManager.Instance;
            if(game.PlayerEnterExit.IsPlayerInside)game.PlayerEnterExit.TransitionExterior(true);
            float readyUntil=Time.realtimeSinceStartup+60;
            while((game.PlayerEnterExit.IsPlayerInside || !streets.Ready) && Time.realtimeSinceStartup<readyUntil)yield return null;
            var store=(WorldStateStore)typeof(StreetPopulation).GetField("worldStore",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(streets);
            ResidentActor actor=null;
            while(!actor && Time.realtimeSinceStartup<readyUntil)
            {
            foreach(var candidate in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                if(candidate.Resident!=null && candidate.Resident.IsAlive && !candidate.Resident.IsQuestProtected &&
                    candidate.Resident.AgeGroup==ResidentAgeGroup.Adult && candidate.GetComponent<NativeNpcInventoryOwner>() &&
                    candidate.GetComponent<EntityEffectManager>() && ReferenceEquals(store.ResolveResident(candidate.Resident.Id),candidate.Resident))
                {
                    var sample=candidate.GetComponent<NativeNpcVitalsBinding>();int h,mh,mp,mmp;
                    if(sample && sample.TryReadCurrent(out h,out mh,out mp,out mmp)) {actor=candidate;break;}
                }
                if(!actor)yield return null;
            }
            Require(actor,"No ordinary resident available for native condition care.");
            var manager=actor.GetComponent<EntityEffectManager>();
            var body=actor.GetComponent<DaggerfallEntityBehaviour>();
            var owner=actor.GetComponent<NativeNpcInventoryOwner>();
            var vitals=actor.GetComponent<NativeNpcVitalsBinding>();
            // Controlled level guarantees the native classic antidote recipe's chance roll.
            body.Entity.Level=20;
            vitals.Capture();
            int health=body.Entity.CurrentHealth;
            manager.AssignBundle(manager.CreateDisease(Diseases.Plague),AssignBundleFlags.BypassSavingThrows);
            Require(manager.DiseaseCount==1,"Native disease did not remain attached to the ordinary NPC.");
            vitals.Capture();
            var captured=store.ReadNpcProgress(actor.Resident.Id);
            Require(captured!=null && captured.VitalsInitialized,"Native condition sample did not initialize observed vitals.");
            string packet=captured.NativeConditions;
            Require(!string.IsNullOrEmpty(packet),"Native disease save packet was not captured.");
            var wire=(WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot(),false));
            var detached=new WorldStateStore(0);detached.Restore(wire);
            Require(detached.ReadNpcProgress(actor.Resident.Id).NativeConditions==packet,"Native serializer lost condition packet.");
            manager.CureAllDiseases();
            NativeNpcConditionBinding.BindTo(actor.gameObject,store,actor.Resident);
            Require(manager.DiseaseCount==1,"Native restore lost the actual disease effect.");

            foreach(string key in new[] {CureDisease.EffectKey,CurePoison.EffectKey})
            {
                if(key==CurePoison.EffectKey)
                    manager.AssignBundle(manager.CreatePoison(Poisons.Nux_Vomica),AssignBundleFlags.BypassSavingThrows);
                int recipe=FindRecipe(key);
                var potion=ItemBuilder.CreatePotion(recipe);potion.stackCount=2;
                owner.Binding.Items.AddItem(potion,noStack:true);owner.Capture();
                float until=Time.realtimeSinceStartup+15;
                while((key==CurePoison.EffectKey?manager.PoisonCount:manager.DiseaseCount)>0 && Time.realtimeSinceStartup<until)
                    yield return null;
                Require((key==CurePoison.EffectKey?manager.PoisonCount:manager.DiseaseCount)==0,"Autonomous native cure failed: "+key);
                Require(owner.Binding.Items.GetItem(potion.UID)==potion && potion.stackCount==1,"Native cure did not consume exactly one potion.");
                Require(body.Entity.CurrentHealth==health,"Condition treatment invented health recovery.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CONDITION_POTION_PASS: effect="+key+"; nativeCount=0; consumed=1; hpUnchanged=true");
            }
            // Both conditions coexist; the remaining actual potions must treat poison first.
            manager.AssignBundle(manager.CreateDisease(Diseases.Plague),AssignBundleFlags.BypassSavingThrows);
            manager.AssignBundle(manager.CreatePoison(Poisons.Nux_Vomica),AssignBundleFlags.BypassSavingThrows);
            float priorityUntil=Time.realtimeSinceStartup+15;
            while(manager.PoisonCount>0 && Time.realtimeSinceStartup<priorityUntil)yield return null;
            Require(manager.PoisonCount==0 && manager.DiseaseCount==1,"Poison did not take priority over simultaneous disease.");
            while(manager.DiseaseCount>0 && Time.realtimeSinceStartup<priorityUntil)yield return null;
            Require(manager.DiseaseCount==0,"Disease was not treated after poison.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CONDITION_PRIORITY_PASS: poisonFirst=true; diseaseAfter=true");

            // The fixture supplies a capable caster; ordinary residents receive no free skills or spells.
            body.Entity.Skills.SetPermanentSkillValue(DaggerfallConnect.DFCareer.Skills.Restoration,100);
            manager.UsePlayerCharacterSkillsForEnemyMagicCost=false;
            foreach(string key in new[] {CurePoison.EffectKey,CureDisease.EffectKey})
            {
                var chance=BaseEntityEffect.DefaultEffectSettings();chance.ChanceBase=0;chance.ChancePlus=5;chance.ChancePerLevel=1;
                var spell=manager.CreateSpellBundle(key,ElementTypes.Magic,chance);
                var settings=spell.Settings;settings.TargetType=TargetTypes.CasterOnly;settings.Name="Condition care fixture "+key;
                DaggerfallEntity caster=manager.UsePlayerCharacterSkillsForEnemyMagicCost?null:body.Entity;
                (int _,int cost)=FormulaHelper.CalculateTotalEffectCosts(settings.Effects,settings.TargetType,caster,settings.MinimumCastingCost);
                Require(cost>0 && cost<=body.Entity.MaxMagicka,"Fixture cannot afford its actual native cure spell: "+cost+"/"+body.Entity.MaxMagicka);
                int index=body.Entity.SpellbookCount();body.Entity.AddSpell(settings);
                body.Entity.SetMagicka(body.Entity.MaxMagicka);vitals.Capture();
                int beforeMana=body.Entity.CurrentMagicka;
                long inventoryRevision=store.ReadNpcInventory(actor.Resident.Id).Revision;
                if(key==CurePoison.EffectKey)
                    manager.AssignBundle(manager.CreatePoison(Poisons.Nux_Vomica),AssignBundleFlags.BypassSavingThrows);
                else manager.AssignBundle(manager.CreateDisease(Diseases.Plague),AssignBundleFlags.BypassSavingThrows);
                float until=Time.realtimeSinceStartup+15;
                while((key==CurePoison.EffectKey?manager.PoisonCount:manager.DiseaseCount)>0 && Time.realtimeSinceStartup<until)yield return null;
                Require((key==CurePoison.EffectKey?manager.PoisonCount:manager.DiseaseCount)==0,"Native self-cure spell failed: "+key);
                Require(body.Entity.CurrentMagicka==beforeMana-cost,"Native self-cure did not spend exact magicka.");
                Require(store.ReadNpcInventory(actor.Resident.Id).Revision==inventoryRevision,"Spell treatment spent an item.");
                body.Entity.DeleteSpell(index);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CONDITION_SPELL_PASS: effect="+key+"; magickaSpent="+cost+"; itemConsumed=false");
            }
            var failureChance=BaseEntityEffect.DefaultEffectSettings();failureChance.ChanceBase=0;failureChance.ChancePlus=0;failureChance.ChancePerLevel=1;
            var failedSpell=manager.CreateSpellBundle(CurePoison.EffectKey,ElementTypes.Magic,failureChance).Settings;
            failedSpell.TargetType=TargetTypes.CasterOnly;failedSpell.Name="Condition fixture failed cure";
            int failedIndex=body.Entity.SpellbookCount();body.Entity.AddSpell(failedSpell);
            body.Entity.SetMagicka(body.Entity.MaxMagicka);vitals.Capture();int failedMana=body.Entity.CurrentMagicka;
            var fallbackPotion=ItemBuilder.CreatePotion(FindRecipe(CurePoison.EffectKey));
            owner.Binding.Items.AddItem(fallbackPotion,noStack:true);owner.Capture();
            manager.AssignBundle(manager.CreatePoison(Poisons.Nux_Vomica),AssignBundleFlags.BypassSavingThrows);
            float fallbackUntil=Time.realtimeSinceStartup+6;
            while(manager.PoisonCount>0 && Time.realtimeSinceStartup<fallbackUntil)yield return null;
            Require(manager.PoisonCount==0 && body.Entity.CurrentMagicka<failedMana && owner.Binding.Items.GetItem(fallbackPotion.UID)==null,
                "Failed native cure spell did not promptly fall back to the owned antidote.");
            body.Entity.DeleteSpell(failedIndex);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CONDITION_FALLBACK_PASS: failedSpellDebited=true; actualAntidoteConsumed=true; poisonRemoved=true");
            vitals.Capture();
            Require(store.ReadNpcProgress(actor.Resident.Id).NativeConditions==null,"Cured condition remained in saved state.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CONDITIONS_NATIVE_PASS: diseaseRetained=true; nativeSerializer=true; nativeEffectRestored=true; diseasePotion=true; poisonPotion=true; exactConsumption=true; savedClear=true; poisonPriority=true; diseaseSpell=true; poisonSpell=true; exactMagicka=true");
            string save=null;var args=Environment.GetCommandLineArgs();
            for(int i=0;i+1<args.Length;i++)if(args[i]=="-peopleoftheiliacbay-fixture-save")save=args[i+1];
            yield return ConversationMoodProbe.LoadSave(host,streets,character,save,false);
        }

        private static int FindRecipe(string key)
        {
            var broker=GameManager.Instance.EntityEffectBroker;
            foreach(int id in broker.GetPotionRecipeKeys())
            {
                var recipe=broker.GetPotionRecipe(id);var effect=broker.GetPotionRecipeEffect(recipe);
                if(effect!=null && effect.Key==key && (recipe.SecondaryEffects==null || recipe.SecondaryEffects.Count==0))return id;
            }
            throw new InvalidOperationException("No native potion recipe for "+key);
        }
        private static void Require(bool value,string message) {if(!value)throw new InvalidOperationException(message);}
    }
}
