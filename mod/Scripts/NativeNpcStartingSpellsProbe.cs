using System;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Explicit disposable probe only: caller supplies a real generated magical adventurer.
    internal static class NativeNpcStartingSpellsProbe
    {
        internal static void Verify(WorldStateStore world,ResidentActor actor,string character)
        {
            string selected=Environment.GetEnvironmentVariable("POTIB_ROADMAP_PROBE");
            Require(Environment.GetEnvironmentVariable("POTIB_COMPANION_CAST")=="1" &&
                (selected=="companion-health" || selected=="starting-spells") &&
                !string.IsNullOrEmpty(character) && NativeProbeText.IsTestCharacter(character) &&
                GameManager.Instance.PlayerEntity.Name==character,"Starting spell probe needs the isolated disposable cast fixture.");
            Require(world!=null && actor && actor.Resident!=null && world.Traveler(actor.Resident.Id)!=null,
                "Starting spell probe needs a real generated traveler.");
            var role=world.ResolveNpcArchetype(actor.Resident.Id);
            Require(role==NpcArchetype.Mage || role==NpcArchetype.Healer,"Starting spell probe needs actual Mage/Healer role.");
            var body=actor.GetComponent<DaggerfallEntityBehaviour>();var manager=actor.GetComponent<EntityEffectManager>();
            var spellbook=actor.GetComponent<NativeNpcKnownSpellBinding>();
            Require(body && body.Entity!=null && manager && !manager.HasReadySpell &&
                !manager.UsePlayerCharacterSkillsForEnemyMagicCost && spellbook,
                "Starting spell probe needs bound native own-skill caster and spellbook persistence.");
            var original=body.Entity.SerializeSpellbook();
            int health=body.Entity.CurrentHealth,magicka=body.Entity.CurrentMagicka;
            string book=SaveLoadManager.Serialize(typeof(EffectBundleSettings[]),original,false);
            var expected=NativeNpcStartingSpells.ClassicSpellIds(world.WorldSeed,actor.Resident.Id,role);
            Require(original.Length==expected.Length,"Generated magical adventurer has no native starting spellbook.");
            int affordable=0;
            for(int i=0;i<original.Length;i++)
            {
                Require(original[i].StandardSpellIndex==expected[i],"Generated book lost stable native template order.");
                (int _,int cost)=FormulaHelper.CalculateTotalEffectCosts(original[i].Effects,original[i].TargetType,
                    body.Entity,original[i].MinimumCastingCost);
                if(cost>0 && cost<=body.Entity.MaxMagicka)affordable++;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_STARTING_SPELL_COST: role="+role+"; native="+expected[i]+
                    "; magicka="+cost+"; maximum="+body.Entity.MaxMagicka);
            }
            Require(affordable>0,"Generated caster cannot afford any actual native known spell.");
            try
            {
                Require(spellbook.Capture(),"Known native spellbook was not captured.");
                var progress=world.ReadNpcProgress(actor.Resident.Id);
                Require(progress!=null && progress.NativeSpellbook==book,
                    "Exact native known spellbook was not stored on the actor progress record.");
                body.Entity.DeserializeSpellbook(null);
                // Simulate the fresh manager's native default, which the former
                // clear-book-only fixture never reset during reconstruction.
                manager.UsePlayerCharacterSkillsForEnemyMagicCost=true;
                Require(NativeNpcKnownSpellBinding.BindTo(actor.gameObject,world,actor.Resident)==spellbook &&
                    !manager.UsePlayerCharacterSkillsForEnemyMagicCost,
                    "Known spell binding did not restore the resident's own native skill cost source.");
                Require(spellbook.Restore() && SaveLoadManager.Serialize(typeof(EffectBundleSettings[]),
                    body.Entity.SerializeSpellbook(),false)==book,
                    "Exact native known spellbook did not survive a cleared-body reconstruction.");
                NativeNpcStartingSpells.EnsureKnownSpells(actor.gameObject,world,actor.Resident);
                Require(SaveLoadManager.Serialize(typeof(EffectBundleSettings[]),body.Entity.SerializeSpellbook(),false)==book,
                    "Starting spell initialization overwrote an existing native book.");

                var authored=(EffectBundleSettings[])SaveLoadManager.Deserialize(typeof(EffectBundleSettings[]),book);
                authored[0].Name="PeopleOfTheIliacBay probe authored spell";
                body.Entity.DeserializeSpellbook(authored);
                Require(spellbook.Capture(),"Authored native spellbook was not captured.");
                string authoredBook=SaveLoadManager.Serialize(typeof(EffectBundleSettings[]),authored,false);
                body.Entity.DeserializeSpellbook(null);
                Require(spellbook.Restore() && SaveLoadManager.Serialize(typeof(EffectBundleSettings[]),
                    body.Entity.SerializeSpellbook(),false)==authoredBook,
                    "Authored native spellbook did not survive a cleared-body reconstruction.");
                NativeNpcStartingSpells.EnsureKnownSpells(actor.gameObject,world,actor.Resident);
                Require(SaveLoadManager.Serialize(typeof(EffectBundleSettings[]),body.Entity.SerializeSpellbook(),false)==authoredBook,
                    "Seeded fallback overwrote an authored native spellbook.");
                VerifyEmptyNonCaster(world,actor);
                Require(NativeNpcKnownSpellBinding.BindTo(actor.gameObject,world,new Resident {
                    Id=actor.Resident.Id,IsAlive=true,AgeGroup=ResidentAgeGroup.Adult,IsQuestProtected=true})==null,
                    "Quest-protected actor was admitted to ordinary spellbook persistence.");
                body.Entity.DeserializeSpellbook(null);
                NativeNpcStartingSpells.EnsureKnownSpells(actor.gameObject,world,actor.Resident);
                Require(SaveLoadManager.Serialize(typeof(EffectBundleSettings[]),body.Entity.SerializeSpellbook(),false)==book,
                    "Body rebuild changed exact native known spell identities.");
                Require(body.Entity.CurrentHealth==health && body.Entity.CurrentMagicka==magicka,
                    "Known spell materialization changed remaining native health or magicka.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_STARTING_SPELLS_NATIVE_PASS: actualTraveler=true; role="+role+
                    "; nativeTemplates=true; ownSkillCosts=true; ownSkillCostRestored=true; savedBookRestore=true; authoredBookPreserved=true; "+
                    "emptyNoncasterSparse=true; questExcluded=true; stableRebuild=true; resourcesUnchanged=true");
            }
            finally{body.Entity.DeserializeSpellbook(original);spellbook.Capture();}
        }

        private static void VerifyEmptyNonCaster(WorldStateStore world,ResidentActor caster)
        {
            foreach(var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
            {
                if(!actor || actor==caster || actor.Resident==null || !NpcVitalsBindingPolicy.Eligible(actor.Resident.IsAlive,
                    actor.Resident.AgeGroup==ResidentAgeGroup.Adult,actor.Resident.IsQuestProtected))continue;
                var role=world.ResolveNpcArchetype(actor.Resident.Id);
                if(role==NpcArchetype.Mage || role==NpcArchetype.Healer)continue;
                var body=actor.GetComponent<DaggerfallEntityBehaviour>();
                var binding=actor.GetComponent<NativeNpcKnownSpellBinding>();
                var before=world.ReadNpcProgress(actor.Resident.Id);
                if(!body || body.Entity==null || body.Entity.SpellbookCount()!=0 || !binding ||
                    before!=null && before.NativeSpellbook!=null)continue;
                Require(binding.Capture(),"Empty noncaster spellbook capture rejected.");
                var after=world.ReadNpcProgress(actor.Resident.Id);
                Require(after==null || after.NativeSpellbook==null,
                    "Empty noncaster allocated a native spellbook payload.");
                return;
            }
            throw new InvalidOperationException("Starting spell probe needs one visible empty ordinary noncaster.");
        }

        private static void Require(bool okay,string message)
        {if(!okay)throw new InvalidOperationException(message);}
    }
}
