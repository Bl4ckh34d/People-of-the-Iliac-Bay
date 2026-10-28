using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Standalone native dispatcher. Uses an actual generated caster and its untouched known spell settings.
    internal static class NativeNpcStartingSpellsTrial
    {
        internal static IEnumerator Run()
        {
            var game=GameManager.Instance;
            Require(Environment.GetEnvironmentVariable("POTIB_COMPANION_CAST")=="1" &&
                Environment.GetEnvironmentVariable("POTIB_ROADMAP_PROBE")=="starting-spells" && game &&
                game.PlayerEntity!=null && NativeProbeText.IsTestCharacter(game.PlayerEntity.Name),
                "Starting spell trial needs its explicit isolated disposable fixture.");
            string character=game.PlayerEntity.Name;
            var streets=UnityEngine.Object.FindObjectOfType<StreetPopulation>();
            Require(streets,"Starting spell trial needs the loaded managed population.");
            var world=(WorldStateStore)typeof(StreetPopulation).GetField("worldStore",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(streets);
            Require(world!=null,"Starting spell trial lacks its native world binding.");
            ResidentActor selected=null;int healIndex=-1,healCost=0;
            float deadline=Time.realtimeSinceStartup+60;
            while(!selected && Time.realtimeSinceStartup<deadline)
            {
                foreach(var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                {
                    int index,cost;
                    if(!TryCaster(world,actor,out index,out cost))continue;
                    if(selected!=null && string.CompareOrdinal(actor.Resident.Id,selected.Resident.Id)>=0)continue;
                    selected=actor;healIndex=index;healCost=cost;
                }
                if(!selected)yield return null;
            }
            Require(selected,"No actual visible generated Mage/Healer with an affordable known native heal is available. Load a real traveler location; do not relabel a citizen.");
            var role=world.ResolveNpcArchetype(selected.Resident.Id);
            var body=selected.GetComponent<DaggerfallEntityBehaviour>();
            var binding=selected.GetComponent<NativeNpcVitalsBinding>();
            var profile=world.ReadNpcActor(selected.Resident.Id,role);
            for(int i=0;i<8;i++)Require(body.Entity.Stats.GetPermanentStatValue(i)==profile.Profile.Attribute(i),
                "Generated caster's actual native attribute differs from its seeded profile.");
            for(int i=0;i<35;i++)Require(body.Entity.Skills.GetPermanentSkillValue(i)==profile.Skill(i),
                "Generated caster's actual native skill differs from its persisted profile.");
            NativeNpcStartingSpellsProbe.Verify(world,selected,character);
            int originalHealth=body.Entity.CurrentHealth,originalMagicka=body.Entity.CurrentMagicka;
            try
            {
                // A bounded injury creates a real need. Keep the actor's actual remaining MP and seeded native skills.
                bool quiesce=body.Entity.Quiesce;
                try {body.Entity.Quiesce=true;body.Entity.SetHealth(Math.Max(1,Math.Min(originalHealth,body.Entity.MaxHealth-3)));}
                finally {body.Entity.Quiesce=quiesce;}
                binding.Capture();
                NpcCompanionCastSpec known=null;
                foreach(var spell in NativeNpcCompanionCast.Discover(selected.gameObject,world))
                    if(spell.EffectId==HealHealth.EffectKey && spell.Delivery==NpcCompanionCastDelivery.Self &&
                        spell.SpellId.StartsWith("native:spellbook:"+healIndex+":",StringComparison.Ordinal))known=spell;
                Require(known!=null && known.MagickaCost==healCost,"Actual generated caster's affordable native known heal was not offered.");
                int beforeHealth=body.Entity.CurrentHealth,beforeMagicka=body.Entity.CurrentMagicka;string reason;
                Require(NativeNpcCompanionCast.TryExecute(selected.gameObject,world,known,()=>true,out reason),
                    "Actual generated caster's known native heal failed: "+reason);
                Require(body.Entity.CurrentHealth>beforeHealth && body.Entity.CurrentMagicka==beforeMagicka-healCost,
                    "Generated caster did not deliver native healing with exact own-skill MP cost.");
                var saved=world.ReadNpcVitals(selected.Resident.Id,role);
                Require(saved.IsPersisted && saved.CurrentHealth==body.Entity.CurrentHealth &&
                    saved.CurrentMagicka==body.Entity.CurrentMagicka,"Generated caster's native outcome was not saved.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_STARTING_SPELL_CAST_NATIVE_PASS: actor="+selected.Resident.Id+
                    "; role="+role+"; actualGeneratedTraveler=true; seededStats=true; existingNativeSpell=true; nativeHeal=true; exactMagicka="+
                    healCost+"; savedVitals=true; noRefill=true");
            }
            finally
            {
                if(body && body.Entity!=null && body.Entity.CurrentHealth>0)
                {
                    bool quiesce=body.Entity.Quiesce;
                    try {body.Entity.Quiesce=true;body.Entity.SetHealth(originalHealth);body.Entity.SetMagicka(originalMagicka);}
                    finally {body.Entity.Quiesce=quiesce;}
                    if(binding)binding.Capture();
                }
            }
        }

        private static bool TryCaster(WorldStateStore world,ResidentActor actor,out int index,out int cost)
        {
            index=-1;cost=0;
            if(!actor || !actor.gameObject.activeInHierarchy || actor.Resident==null ||
                !NpcVitalsBindingPolicy.Eligible(actor.Resident.IsAlive,actor.Resident.AgeGroup==ResidentAgeGroup.Adult,
                    actor.Resident.IsQuestProtected))return false;
            var traveler=world.Traveler(actor.Resident.Id);
            if(traveler==null || !traveler.IsAlive)return false;
            var role=world.ResolveNpcArchetype(traveler.Id);
            if(role!=NpcArchetype.Mage && role!=NpcArchetype.Healer ||
                TravelerAdventurerPolicy.Archetype(traveler.AppearanceSeed)!=role)return false;
            var body=actor.GetComponent<DaggerfallEntityBehaviour>();var manager=actor.GetComponent<EntityEffectManager>();
            var binding=actor.GetComponent<NativeNpcVitalsBinding>();
            if(!body || body.Entity==null || body.Entity.CurrentHealth<=0 || body.Entity.MaxHealth<=3 || !manager || !binding ||
                manager.HasReadySpell || manager.UsePlayerCharacterSkillsForEnemyMagicCost || !ReferenceEquals(manager.EntityBehaviour,body))return false;
            int health,maximumHealth,magicka,maximumMagicka;
            if(!binding.TryReadCurrent(out health,out maximumHealth,out magicka,out maximumMagicka) ||
                health!=body.Entity.CurrentHealth || maximumHealth!=body.Entity.MaxHealth ||
                magicka!=body.Entity.CurrentMagicka || maximumMagicka!=body.Entity.MaxMagicka)return false;
            for(int i=0;i<body.Entity.SpellbookCount();i++)
            {
                EffectBundleSettings spell;
                if(!body.Entity.GetSpell(i,out spell) || spell.TargetType!=TargetTypes.CasterOnly || spell.Effects==null ||
                    spell.Effects.Length!=1 || spell.Effects[0].Key!=HealHealth.EffectKey)continue;
                var effect=GameManager.Instance.EntityEffectBroker.InstantiateEffect(spell.Effects[0]);
                if(effect==null || effect.GetType()!=typeof(HealHealth))continue;
                (int _,int nativeCost)=FormulaHelper.CalculateTotalEffectCosts(spell.Effects,spell.TargetType,
                    body.Entity,spell.MinimumCastingCost);
                if(nativeCost<=0 || nativeCost>body.Entity.CurrentMagicka || nativeCost>body.Entity.MaxMagicka)continue;
                index=i;cost=nativeCost;return true;
            }
            return false;
        }

        private static void Require(bool okay,string message)
        {if(!okay)throw new InvalidOperationException(message);}
    }
}
