using System;
using System.Collections.Generic;
using DaggerfallConnect.Save;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public static class NativeNpcStartingSpells
    {
        // Body rebuilds reproduce the same small native book. Existing native/quest books remain authoritative.
        public static void EnsureKnownSpells(GameObject owner,WorldStateStore world,Resident person)
        {
            if(!owner || world==null || person==null || !NpcVitalsBindingPolicy.Eligible(person.IsAlive,
                person.AgeGroup==ResidentAgeGroup.Adult,person.IsQuestProtected) || world.Traveler(person.Id)==null)return;
            var body=owner.GetComponent<DaggerfallEntityBehaviour>();
            var manager=owner.GetComponent<EntityEffectManager>();
            if(!body || body.Entity==null || body.Entity.SpellbookCount()!=0 || !manager || manager.HasReadySpell ||
                !ReferenceEquals(manager.EntityBehaviour,body))return;
            var ids=ClassicSpellIds(world.WorldSeed,person.Id,world.ResolveNpcArchetype(person.Id));
            if(ids.Length==0)return;
            var broker=GameManager.Instance.EntityEffectBroker;
            if(!broker)return;
            var spells=new List<EffectBundleSettings>();
            foreach(int id in ids)
            {
                SpellRecord.SpellRecordData record;EffectBundleSettings spell;
                if(!broker.GetClassicSpellRecord(id,out record) ||
                    !broker.ClassicSpellRecordDataToEffectBundleSettings(record,BundleTypes.Spell,out spell) ||
                    spell.TargetType!=TargetTypes.CasterOnly || spell.Effects==null || spell.Effects.Length!=1)continue;
                string key=id==17?Shield.EffectKey:id==61?CureDisease.EffectKey:
                    id==15 || id==60?CurePoison.EffectKey:HealHealth.EffectKey;
                if(spell.Effects[0].Key!=key)continue;
                var effect=broker.InstantiateEffect(spell.Effects[0]);
                if(effect==null || key==Shield.EffectKey && effect.GetType()!=typeof(Shield) ||
                    key==CureDisease.EffectKey && effect.GetType()!=typeof(CureDisease) ||
                    key==CurePoison.EffectKey && effect.GetType()!=typeof(CurePoison) ||
                    key==HealHealth.EffectKey && effect.GetType()!=typeof(HealHealth))continue;
                spells.Add(spell);
            }
            if(spells.Count==0)return;
            // Managed adventurers pay native costs from their own seeded skills, not the player's skill values.
            manager.UsePlayerCharacterSkillsForEnemyMagicCost=false;
            foreach(var spell in spells)body.Entity.AddSpell(spell);
        }

        internal static int[] ClassicSpellIds(uint worldSeed,string personId,NpcArchetype archetype)
        {
            if(archetype!=NpcArchetype.Mage && archetype!=NpcArchetype.Healer)return new int[0];
            if(string.IsNullOrWhiteSpace(personId))throw new ArgumentException("Invalid NPC identity.",nameof(personId));
            uint hash=2166136261u;
            unchecked {foreach(char value in personId)hash=(hash^value)*16777619u;}
            uint seed=StableSeed.Combine(worldSeed,hash,(uint)archetype,0x53504c31u);
            int antidote=(seed&1)==0?15:60; // Native Cure Poison or Balyna's Antidote.
            return archetype==NpcArchetype.Mage?new[]{97,17,antidote}:
                new[]{(seed&2)==0?97:64,antidote,61}; // Native Balm/Heal and Cure Disease.
        }
    }
}
