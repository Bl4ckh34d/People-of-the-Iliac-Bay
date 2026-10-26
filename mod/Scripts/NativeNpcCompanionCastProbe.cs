using System;
using DaggerfallConnect;
using DaggerfallWorkshop;
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
    // Opt-in only. Caller supplies a disposable, loaded action actor and nearby player.
    public static class NativeNpcCompanionCastProbe
    {
        public static void Verify(WorldStateStore store,ResidentActor actor,string expectedCharacter)
        {
            bool ownedRoadmapProbe=Environment.GetEnvironmentVariable("POTIB_COMPANION_CAST")=="1" &&
                Environment.GetEnvironmentVariable("POTIB_ROADMAP_PROBE")=="companion-health";
            Require((Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-companion-cast-probe")>=0 ||
                ownedRoadmapProbe) &&
                !string.IsNullOrEmpty(expectedCharacter) && expectedCharacter.StartsWith("PeopleOfTheIliacBayTest-",StringComparison.Ordinal) &&
                GameManager.Instance.PlayerEntity.Name==expectedCharacter,
                "Companion cast probe requires its isolated disposable character flag.");
            Require(store!=null && actor && actor.Resident!=null && actor.gameObject.activeInHierarchy,
                "Companion cast probe needs one active ordinary resident.");
            var body=actor.GetComponent<DaggerfallEntityBehaviour>();
            var manager=actor.GetComponent<EntityEffectManager>();
            var binding=actor.GetComponent<NativeNpcVitalsBinding>();
            var spellbook=actor.GetComponent<NativeNpcKnownSpellBinding>();
            var player=GameManager.Instance.PlayerEntityBehaviour;
            Require(body && body.Entity!=null && manager && binding && spellbook && player && player.Entity!=null &&
                ReferenceEquals(manager.EntityBehaviour,body) && !manager.HasReadySpell,
                "Companion cast probe lacks bound native bodies, spellbook persistence, or has a ready spell.");
            var activeFollow=store.ActiveNpcAction(actor.Resident.Id);
            Require(activeFollow!=null && ActivePlayerFollow(store,actor.Resident.Id,activeFollow.Id),
                "Companion cast skill proof requires an active player Follow action.");
            string followId=activeFollow.Id;
            int originalSpellCount=body.Entity.SpellbookCount();
            var originalBook=body.Entity.SerializeSpellbook();
            string originalBookPacket=SaveLoadManager.Serialize(typeof(EffectBundleSettings[]),originalBook,false);
            object originalEpoch=store.CaptureNpcVitalsWriteEpoch();
            int originalNpcHealth=body.Entity.CurrentHealth,originalNpcMagicka=body.Entity.CurrentMagicka;
            int originalPlayerHealth=player.Entity.CurrentHealth;
            bool originalGodMode=GameManager.Instance.PlayerEntity.GodMode;
            int originalLevel=body.Entity.Level;
            short originalRestoration=body.Entity.Skills.GetPermanentSkillValue(DFCareer.Skills.Restoration);
            short originalAlteration=body.Entity.Skills.GetPermanentSkillValue(DFCareer.Skills.Alteration);
            bool originalCostMode=manager.UsePlayerCharacterSkillsForEnemyMagicCost;
            var originalBundles=manager.GetInstancedBundlesSaveData();
            var originalPlayerBundles=GameManager.Instance.PlayerEffectManager.GetInstancedBundlesSaveData();
            var playerEntity=GameManager.Instance.PlayerEntity;
            var originalRace=playerEntity.BirthRaceTemplate;
            var originalMagicTolerance=playerEntity.Career.Magic;
            int originalBiographyMagic=playerEntity.BiographyResistMagicMod;
            bool originalMagicFlag=playerEntity.HasResistanceFlag(DFCareer.Elements.Magic);
            Require(originalSpellCount>=0 && originalSpellCount<=126 && body.Entity.MaxHealth>2 &&
                player.Entity.MaxHealth>2 && body.Entity.MaxMagicka>2,
                "Companion cast fixture lacks spellbook slots or safe health/magicka capacity.");
            try
            {
                // Controlled positive-effect fixture. Native resistance remains
                // active, using a real vulnerable target; production casts do
                // not alter these values or bypass saving throws.
                GameManager.Instance.PlayerEffectManager.RestoreInstancedBundleSaveData(null);
                var race=originalRace.Clone();
                race.ResistanceFlags&=~DFCareer.EffectFlags.Magic;
                race.ImmunityFlags&=~DFCareer.EffectFlags.Magic;
                race.LowToleranceFlags&=~DFCareer.EffectFlags.Magic;
                race.CriticalWeaknessFlags&=~DFCareer.EffectFlags.Magic;
                playerEntity.BirthRaceTemplate=race;
                playerEntity.Career.Magic=DFCareer.Tolerance.CriticalWeakness;
                playerEntity.BiographyResistMagicMod=0;
                playerEntity.SetResistanceFlag(DFCareer.Elements.Magic,false);
                Require(playerEntity.Resistances.LiveMagic==0 && playerEntity.MagicResist<=10,
                    "Controlled native magic vulnerability is unavailable.");
                body.Entity.Level=20;
                body.Entity.Skills.SetPermanentSkillValue(DFCareer.Skills.Restoration,100);
                body.Entity.Skills.SetPermanentSkillValue(DFCareer.Skills.Alteration,100);
                manager.UsePlayerCharacterSkillsForEnemyMagicCost=false;
                var effect=BaseEntityEffect.DefaultEffectSettings();
                effect.MagnitudeBaseMin=10;effect.MagnitudeBaseMax=10;
                effect.MagnitudePlusMin=0;effect.MagnitudePlusMax=0;effect.MagnitudePerLevel=1;
                var native=manager.CreateSpellBundle(HealHealth.EffectKey,ElementTypes.Magic,effect);
                Require(native!=null,"DFU could not construct a native HealHealth bundle.");
                EffectBundleSettings self=native.Settings;
                self.TargetType=TargetTypes.CasterOnly;self.Name="Companion probe self heal";
                EffectBundleSettings touch=native.Settings;
                touch.TargetType=TargetTypes.ByTouch;touch.Name="Companion probe player touch heal";
                DaggerfallEntity costCaster=manager.UsePlayerCharacterSkillsForEnemyMagicCost?null:body.Entity;
                (int _,int selfCost)=FormulaHelper.CalculateTotalEffectCosts(self.Effects,self.TargetType,costCaster,self.MinimumCastingCost);
                (int __,int touchCost)=FormulaHelper.CalculateTotalEffectCosts(touch.Effects,touch.TargetType,costCaster,touch.MinimumCastingCost);
                Require(selfCost>0 && touchCost>0 && selfCost<=body.Entity.MaxMagicka &&
                    touchCost<=body.Entity.MaxMagicka,
                    "Controlled known spells exceed this actor's native magicka capacity.");
                body.Entity.AddSpell(self);body.Entity.AddSpell(touch);
                Require(body.Entity.SpellbookCount()==originalSpellCount+2,"Controlled spellbook entries were not added.");
                VerifyControlledSpellbookRestore(store,actor,body,spellbook,originalSpellCount);

                bool oldQuiesce=body.Entity.Quiesce;
                try
                {
                    body.Entity.Quiesce=true;
                    body.Entity.SetHealth(Math.Max(1,body.Entity.MaxHealth-3));
                    body.Entity.SetMagicka(body.Entity.MaxMagicka);
                }
                finally{body.Entity.Quiesce=oldQuiesce;}
                binding.Capture();
                GameManager.Instance.PlayerEntity.GodMode=false;
                GameManager.Instance.PlayerEntity.SetHealth(Math.Max(1,player.Entity.MaxHealth-12),true);
                Physics.SyncTransforms();

                NpcCompanionCastSpec selfSpec=null,touchSpec=null;
                foreach(var available in NativeNpcCompanionCast.Discover(actor.gameObject,store))
                {
                    if(available.SpellId.StartsWith("native:spellbook:"+originalSpellCount+":",StringComparison.Ordinal) &&
                        available.Delivery==NpcCompanionCastDelivery.Self)selfSpec=available;
                    if(available.SpellId.StartsWith("native:spellbook:"+(originalSpellCount+1)+":",StringComparison.Ordinal) &&
                        available.Delivery==NpcCompanionCastDelivery.PlayerTouch)touchSpec=available;
                }
                Require(selfSpec!=null && touchSpec!=null && selfSpec.MagickaCost==selfCost &&
                    touchSpec.MagickaCost==touchCost,"Native spell discovery lost exact self/player spell identity or cost.");
                int beforeHealth=body.Entity.CurrentHealth,beforeMagicka=body.Entity.CurrentMagicka;
                var healEffect=GameManager.Instance.EntityEffectBroker.GetEffectTemplate(HealHealth.EffectKey);
                Require(healEffect!=null,"Native HealHealth template is unavailable for companion skill evidence.");
                int healSkill=(int)healEffect.Properties.MagicSkill;
                long experienceBefore=RawExperience(store,actor.Resident.Id,healSkill);
                int coreSkillBefore=store.ReadNpcActor(actor.Resident.Id,Archetype(store,actor.Resident.Id)).Skill(healSkill);
                short nativeSkillBefore=body.Entity.Skills.GetPermanentSkillValue(healSkill);
                string reason;
                Require(NativeNpcCompanionCast.TryExecute(actor.gameObject,store,selfSpec,
                    ()=>ActivePlayerFollow(store,actor.Resident.Id,followId),out reason),
                    "Known self heal did not execute: "+reason);
                Require(body.Entity.CurrentHealth>beforeHealth && body.Entity.CurrentMagicka==beforeMagicka-selfCost,
                    "Self spell did not deliver a native heal and exact magicka debit.");
                Require(ActivePlayerFollow(store,actor.Resident.Id,followId),
                    "Follow ended before verified companion cast progression could be read.");
                long experienceAfter=RawExperience(store,actor.Resident.Id,healSkill);
                int coreSkillAfter=store.ReadNpcActor(actor.Resident.Id,Archetype(store,actor.Resident.Id)).Skill(healSkill);
                short nativeSkillAfter=body.Entity.Skills.GetPermanentSkillValue(healSkill);
                Require(experienceAfter==experienceBefore+NativeNpcCompanionExperience.ExperiencePerSuccessfulUse,
                    "One verified Follow-bound native HealHealth cast did not add exactly one companion skill-use record.");
                Require(coreSkillAfter>=coreSkillBefore &&
                    nativeSkillAfter==Math.Min(100,nativeSkillBefore+coreSkillAfter-coreSkillBefore),
                    "Verified companion casting did not apply the exact persisted skill gain to its native body.");
                VerifySkillPersistence(store,actor.Resident.Id,healSkill,experienceAfter,coreSkillAfter);
                int replayMagicka=body.Entity.CurrentMagicka;
                Require(!NativeNpcCompanionCast.TryExecute(actor.gameObject,store,selfSpec,
                    ()=>ActivePlayerFollow(store,actor.Resident.Id,followId),out reason),
                    "A fully healed companion spell replay incorrectly executed.");
                Require(RawExperience(store,actor.Resident.Id,healSkill)==experienceAfter &&
                    body.Entity.CurrentMagicka==replayMagicka,
                    "A no-effect companion spell replay added skill experience or spent magicka.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMPANION_CAST_SKILL_PASS: follow="+followId+"; effect="+
                    HealHealth.EffectKey+"; skill="+healSkill+"; rawExperience="+experienceBefore+"->"+experienceAfter+
                    "; coreSkill="+coreSkillBefore+"->"+coreSkillAfter+"; nativeSkill="+nativeSkillBefore+"->"+nativeSkillAfter+
                    "; replayNoop=true; persisted=true; rebuiltNativeSkills=true");
                var saved=store.ReadNpcVitals(actor.Resident.Id,Archetype(store,actor.Resident.Id));
                Require(saved.CurrentHealth==body.Entity.CurrentHealth && saved.CurrentMagicka==body.Entity.CurrentMagicka,
                    "Self spell changed native vitals without saved vitals.");

                // Separate fixture casts each start from the actor's real native maximum.
                oldQuiesce=body.Entity.Quiesce;
                try{body.Entity.Quiesce=true;body.Entity.SetMagicka(body.Entity.MaxMagicka);}
                finally{body.Entity.Quiesce=oldQuiesce;}
                binding.Capture();
                beforeHealth=player.Entity.CurrentHealth;beforeMagicka=body.Entity.CurrentMagicka;
                bool playerHeal=NativeNpcCompanionCast.TryExecute(actor.gameObject,store,touchSpec,()=>true,out reason);
                if(playerHeal)
                {
                    Require(player.Entity.CurrentHealth>beforeHealth && body.Entity.CurrentMagicka==beforeMagicka-touchCost,
                        "Player touch spell did not deliver a native heal and exact magicka debit.");
                    saved=store.ReadNpcVitals(actor.Resident.Id,Archetype(store,actor.Resident.Id));
                    Require(saved.CurrentMagicka==body.Entity.CurrentMagicka,
                        "Player touch spell did not save the actual NPC magicka debit.");
                }
                else
                {
                    // DFU clamps any non-immune incoming Magic save to at least
                    // 5%, even after this probe removes all known resistance.
                    // Do not infer a saving-throw cause from this observation;
                    // continue with independent deterministic Cure/Shield evidence.
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMPANION_CAST_PLAYER_HEAL_NO_EFFECT_OBSERVED: nativeResistOutcomePermitted=true; reason="+
                        (reason??"unspecified"));
                }
                VerifyShield(store,actor,body,manager,binding);
                VerifyCures(store,actor,body,manager,binding,player);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMPANION_CAST_NATIVE_PASS: exactSelf=true; "+
                    "playerHeal="+(playerHeal?"native":"no-effect-observed")+"; exactPlayerTouchCures=true; exactMagicka=true; savedNpcVitals=true; shieldRestore=true");
            }
            finally
            {
                if(body && body.Entity!=null)body.Entity.DeserializeSpellbook(originalBook);
                if(manager)
                {
                    manager.UsePlayerCharacterSkillsForEnemyMagicCost=originalCostMode;
                    manager.RestoreInstancedBundleSaveData(originalBundles);
                }
                if(body && body.Entity!=null && body.Entity.CurrentHealth>0)
                {
                    body.Entity.Level=originalLevel;
                    body.Entity.Skills.SetPermanentSkillValue(DFCareer.Skills.Restoration,originalRestoration);
                    body.Entity.Skills.SetPermanentSkillValue(DFCareer.Skills.Alteration,originalAlteration);
                    bool oldQuiesce=body.Entity.Quiesce;
                    try{body.Entity.Quiesce=true;body.Entity.SetHealth(originalNpcHealth);body.Entity.SetMagicka(originalNpcMagicka);}
                    finally{body.Entity.Quiesce=oldQuiesce;}
                    if(binding)binding.Capture();
                }
                Require(body && body.Entity!=null && SaveLoadManager.Serialize(typeof(EffectBundleSettings[]),
                    body.Entity.SerializeSpellbook(),false)==originalBookPacket && store.IsNpcVitalsWriteEpochCurrent(originalEpoch) &&
                    spellbook.Capture() && store.ReadNpcProgress(actor.Resident.Id).NativeSpellbook==originalBookPacket,
                    "Controlled spellbook probe did not restore original native book or write epoch.");
                if(player && player.Entity!=null)
                {
                    playerEntity.BirthRaceTemplate=originalRace;
                    playerEntity.Career.Magic=originalMagicTolerance;
                    playerEntity.BiographyResistMagicMod=originalBiographyMagic;
                    playerEntity.SetResistanceFlag(DFCareer.Elements.Magic,originalMagicFlag);
                    GameManager.Instance.PlayerEffectManager.RestoreInstancedBundleSaveData(originalPlayerBundles);
                    GameManager.Instance.PlayerEntity.GodMode=false;
                    GameManager.Instance.PlayerEntity.SetHealth(originalPlayerHealth,true);
                    GameManager.Instance.PlayerEntity.GodMode=originalGodMode;
                }
            }
        }

        // Fixture spells prove only binding round-trip. They are not generated
        // Mage/Healer starting books and must not alter native HP or MP.
        private static void VerifyControlledSpellbookRestore(WorldStateStore store,ResidentActor actor,
            DaggerfallEntityBehaviour body,NativeNpcKnownSpellBinding spellbook,int originalSpellCount)
        {
            int health=body.Entity.CurrentHealth,magicka=body.Entity.CurrentMagicka;
            Require(body.Entity.SpellbookCount()==originalSpellCount+2 && spellbook.Capture(),
                "Controlled fixture spellbook was not captured.");
            var controlledBook=body.Entity.SerializeSpellbook();
            string controlledPacket=SaveLoadManager.Serialize(typeof(EffectBundleSettings[]),controlledBook,false);
            Require(controlledBook!=null && controlledBook.Length==originalSpellCount+2,
                "Controlled fixture spellbook changed before exact serialization.");
            var progress=store.ReadNpcProgress(actor.Resident.Id);
            Require(progress!=null && progress.NativeSpellbook==controlledPacket,
                "Controlled fixture spellbook was not saved exactly.");
            body.Entity.DeserializeSpellbook(null);
            Require(body.Entity.SpellbookCount()==0 && spellbook.Restore() &&
                SaveLoadManager.Serialize(typeof(EffectBundleSettings[]),body.Entity.SerializeSpellbook(),false)==controlledPacket &&
                body.Entity.CurrentHealth==health && body.Entity.CurrentMagicka==magicka,
                "Controlled fixture spellbook restore changed settings, health, or magicka.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMPANION_CONTROLLED_BOOK_RESTORE_PASS: controlledBook=true; "+
                "exactSettings=true; resourcesUnchanged=true");
        }

        private static void VerifyCures(WorldStateStore store,ResidentActor actor,DaggerfallEntityBehaviour body,
            EntityEffectManager manager,NativeNpcVitalsBinding binding,DaggerfallEntityBehaviour player)
        {
            var playerEffects=GameManager.Instance.PlayerEffectManager;
            Require(player && player.Entity!=null && playerEffects && ReferenceEquals(playerEffects.EntityBehaviour,player),
                "Companion cure probe lacks the exact player target.");
            foreach(string key in new[]{CurePoison.EffectKey,CureDisease.EffectKey})
            {
                var settings=BaseEntityEffect.DefaultEffectSettings();settings.ChanceBase=0;
                settings.ChancePlus=5;settings.ChancePerLevel=1;
                var spell=manager.CreateSpellBundle(key,ElementTypes.Magic,settings).Settings;
                spell.TargetType=TargetTypes.ByTouch;spell.Name="Companion probe player touch "+key;
                var template=GameManager.Instance.EntityEffectBroker.InstantiateEffect(spell.Effects[0]);
                Require(template!=null && template.Key==key,"Controlled native cure template missing: "+key);
                int casterLevel=FormulaHelper.CalculateCasterLevel(body.Entity,template);
                int chanceValue=settings.ChanceBase+settings.ChancePlus*(int)Math.Floor((double)casterLevel/Math.Max(1,settings.ChancePerLevel));
                int index=body.Entity.SpellbookCount();body.Entity.AddSpell(spell);
                try
                {
                    playerEffects.AssignBundle(key==CurePoison.EffectKey?playerEffects.CreatePoison(Poisons.Nux_Vomica):
                        playerEffects.CreateDisease(Diseases.Plague),AssignBundleFlags.BypassSavingThrows);
                    int countBefore=key==CurePoison.EffectKey?playerEffects.PoisonCount:playerEffects.DiseaseCount;
                    Require(countBefore>0,
                        "Controlled native condition was not attached: "+key);
                    body.Entity.SetMagicka(body.Entity.MaxMagicka);binding.Capture();
                    var spec=Find(store,actor,index,NpcCompanionCastDelivery.PlayerTouch);
                    int before=body.Entity.CurrentMagicka;string reason;
                    int effectsBefore=NativeEffectCount(playerEffects,key);
                    bool applied=NativeNpcCompanionCast.TryExecute(actor.gameObject,store,spec,()=>true,out reason);
                    int countAfter=key==CurePoison.EffectKey?playerEffects.PoisonCount:playerEffects.DiseaseCount;
                    int effectsAfter=NativeEffectCount(playerEffects,key);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMPANION_CURE_PLAYER_TOUCH_DIAGNOSTIC: effect="+key+
                        "; casterLevel="+casterLevel+"; chanceValue="+chanceValue+"; target=player; conditionBefore="+
                        countBefore+"; conditionAfter="+countAfter+"; nativeEffectsBefore="+effectsBefore+
                        "; nativeEffectsAfter="+effectsAfter+"; nativeBypass=false; result="+(applied?"applied":"no-effect")+
                        "; magickaBefore="+before+"; magickaAfter="+body.Entity.CurrentMagicka+"; cost="+spec.MagickaCost+
                        "; reason="+(reason??"none"));
                    Require(body.Entity.CurrentMagicka==before-spec.MagickaCost,
                        "Player touch cure did not debit exact native magicka: "+key);
                    if(applied)
                    {
                        Require(countAfter==0,"Player touch cure reported native effect without clearing condition: "+key);
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMPANION_CURE_NATIVE_PASS: effect="+key+"; target=player; exactMagicka=true; savedClear=true");
                    }
                    else Require(countAfter==countBefore && effectsAfter==effectsBefore,
                        "Player touch cure failed without the native no-effect outcome: "+key+"; "+reason);
                }
                finally{body.Entity.DeleteSpell(index);}
            }
        }

        private static int NativeEffectCount(EntityEffectManager manager,string key)
        {
            int count=0;
            foreach(var bundle in manager.EffectBundles)
                if(bundle!=null && bundle.liveEffects!=null)foreach(var effect in bundle.liveEffects)
                    if(effect!=null && effect.Key==key && !effect.HasEnded)count++;
            return count;
        }

        private static void VerifyShield(WorldStateStore store,ResidentActor actor,DaggerfallEntityBehaviour body,
            EntityEffectManager manager,NativeNpcVitalsBinding binding)
        {
            var settings=BaseEntityEffect.DefaultEffectSettings();
            settings.DurationBase=1;settings.DurationPlus=1;settings.DurationPerLevel=4;
            settings.MagnitudeBaseMin=1;settings.MagnitudeBaseMax=1;
            settings.MagnitudePlusMin=1;settings.MagnitudePlusMax=1;settings.MagnitudePerLevel=2;
            var spell=manager.CreateSpellBundle(Shield.EffectKey,ElementTypes.Magic,settings).Settings;
            spell.TargetType=TargetTypes.CasterOnly;spell.Name="Companion probe Shield";
            int index=body.Entity.SpellbookCount();body.Entity.AddSpell(spell);
            try
            {
                manager.RestoreInstancedBundleSaveData(null);body.Entity.SetMagicka(body.Entity.MaxMagicka);binding.Capture();
                var spec=Find(store,actor,index,NpcCompanionCastDelivery.Self);
                int before=body.Entity.CurrentMagicka;string reason;
                Require(NativeNpcCompanionCast.TryExecute(actor.gameObject,store,spec,()=>true,out reason),
                    "Known native Shield did not execute: "+reason);
                Shield shield=FindShield(manager);Require(shield!=null,"Native cast did not attach Shield.");
                Require(body.Entity.CurrentMagicka==before-spec.MagickaCost,"Shield did not debit exact native magicka.");
                Require(shield.DamageShield(3)==0,"Controlled Shield did not absorb native damage.");
                int rounds=shield.RoundsRemaining;var strength=(Shield.SaveData_v1)shield.GetSaveData();
                binding.Capture();
                Require(!string.IsNullOrEmpty(store.ReadNpcProgress(actor.Resident.Id).NativeConditions),
                    "Shield was not captured in the native condition packet.");
                manager.RestoreInstancedBundleSaveData(null);NativeNpcConditionBinding.BindTo(actor.gameObject,store,actor.Resident);
                shield=FindShield(manager);
                Require(shield!=null && shield.RoundsRemaining==rounds &&
                    ((Shield.SaveData_v1)shield.GetSaveData()).startingShield==strength.startingShield &&
                    ((Shield.SaveData_v1)shield.GetSaveData()).shieldRemaining==strength.shieldRemaining,
                    "Shield restore lost rounds or native partially depleted strength.");

                // Date only this snapshot, simulating an unmaterialized actor without changing global game time.
                string captured=store.ReadNpcProgress(actor.Resident.Id).NativeConditions;
                var packet=(NativeNpcConditionBinding.ConditionPacket)SaveLoadManager.Deserialize(
                    typeof(NativeNpcConditionBinding.ConditionPacket),captured);
                double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                Require(rounds>2,"Controlled Shield lacks rounds for bounded aging checks.");
                packet.CapturedAt=now-120;
                var epoch=store.CaptureNpcVitalsWriteEpoch();
                Require(store.TryCaptureNpcConditions(epoch,actor.Resident.Id,SaveLoadManager.Serialize(
                    typeof(NativeNpcConditionBinding.ConditionPacket),packet,false)),"Could not stage offscreen Shield snapshot.");
                // Vitals remain captured at the present time; they must not redate this older Shield packet.
                manager.RestoreInstancedBundleSaveData(null);NativeNpcConditionBinding.BindTo(actor.gameObject,store,actor.Resident);
                shield=FindShield(manager);
                Require(shield!=null && shield.RoundsRemaining==rounds-2 &&
                    ((Shield.SaveData_v1)shield.GetSaveData()).shieldRemaining==strength.shieldRemaining,
                    "Shield did not age by crossed game minutes independently of vitals capture.");
                packet.CapturedAt=now-rounds*60;
                Require(store.TryCaptureNpcConditions(epoch,actor.Resident.Id,SaveLoadManager.Serialize(
                    typeof(NativeNpcConditionBinding.ConditionPacket),packet,false)),"Could not stage expired Shield snapshot.");
                manager.RestoreInstancedBundleSaveData(null);NativeNpcConditionBinding.BindTo(actor.gameObject,store,actor.Resident);
                Require(FindShield(manager)==null,"Expired offscreen Shield was restored.");
                binding.Capture();
                Require(store.ReadNpcProgress(actor.Resident.Id).NativeConditions==null,"Expired Shield packet was not cleared.");

                // Player timed effects use DFU's own player save system, but delivery still needs a real native touch cast.
                var playerEffects=GameManager.Instance.PlayerEffectManager;
                playerEffects.RestoreInstancedBundleSaveData(null);
                spell.TargetType=TargetTypes.ByTouch;spell.Name="Companion probe player Shield";
                body.Entity.DeleteSpell(index);body.Entity.AddSpell(spell);
                body.Entity.SetMagicka(body.Entity.MaxMagicka);binding.Capture();
                spec=Find(store,actor,index,NpcCompanionCastDelivery.PlayerTouch);before=body.Entity.CurrentMagicka;
                Require(NativeNpcCompanionCast.TryExecute(actor.gameObject,store,spec,()=>true,out reason),
                    "Known player touch Shield did not execute: "+reason);
                Require(FindShield(playerEffects)!=null && body.Entity.CurrentMagicka==before-spec.MagickaCost,
                    "Player touch Shield did not attach with exact native magicka.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMPANION_SHIELD_NATIVE_PASS: nativeSelf=true; nativePlayerTouch=true; exactMagicka=true; nativeDamage=true; roundsRestored=true; strengthRestored=true; offscreenAged=true; independentTimestamp=true; expiredClear=true");
            }
            finally{body.Entity.DeleteSpell(index);}
        }

        private static NpcCompanionCastSpec Find(WorldStateStore store,ResidentActor actor,int index,NpcCompanionCastDelivery delivery)
        {
            foreach(var spec in NativeNpcCompanionCast.Discover(actor.gameObject,store))
                if(spec.Delivery==delivery && spec.SpellId.StartsWith("native:spellbook:"+index+":",StringComparison.Ordinal))return spec;
            throw new InvalidOperationException("Controlled known native spell was not discovered: "+index);
        }
        private static Shield FindShield(EntityEffectManager manager)
        {
            foreach(var bundle in manager.EffectBundles)
                foreach(var effect in bundle.liveEffects)
                    if(effect.GetType()==typeof(Shield) && !effect.HasEnded && effect.RoundsRemaining>0)return (Shield)effect;
            return null;
        }

        private static bool ActivePlayerFollow(WorldStateStore store,string residentId,string expectedActionId)
        {
            var action=store==null?null:store.ActiveNpcAction(residentId);
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            double now=clock.ToSeconds()+(clock.Second-Math.Floor(clock.Second));
            return action!=null && action.Id==expectedActionId && !NpcActionExecutor.IsTerminal(action) &&
                !action.ReturningToRoutine && now>=action.AcceptedAt && now>=action.UpdatedAt && now<action.DeadlineAt &&
                (action.FinishAt<=0 || now<action.FinishAt) &&
                action.Kind==NpcActionKind.Follow && action.Target!=null &&
                action.Target.Kind==NpcActionTargetKind.Person &&
                action.Target.PersonId==NpcDirectedRelationship.PlayerTargetId;
        }

        private static long RawExperience(WorldStateStore store,string residentId,int skill)
        {
            var progress=store==null?null:store.ReadNpcProgress(residentId);
            var recorded=progress==null || progress.Skills==null?null:progress.Skills.Find(entry=>entry.Skill==skill);
            return recorded==null?0:recorded.Experience;
        }

        internal static void VerifySkillPersistence(WorldStateStore store,string residentId,int skill,
            long expectedExperience,int expectedSkill)
        {
            string saved=SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot(),false);
            var restored=new WorldStateStore(store.WorldSeed,PeopleOfTheIliacBayMod.ConversationArchiveRoot());
            restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),saved));
            Require(RawExperience(restored,residentId,skill)==expectedExperience &&
                restored.ReadNpcActor(residentId,restored.ResolveNpcArchetype(residentId)).Skill(skill)==expectedSkill,
                "Native FullSerializer round trip lost verified companion skill progression.");
            // Exercise the same materialization binding as a recreated resident.
            // No scene actor or observer is needed for the native entity skill projection.
            var owner=new GameObject("Companion skill persistence probe");
            try
            {
                var native=new CivilianEntity(null);
                var resident=restored.ResolveResident(residentId);
                Require(resident!=null,"Restored companion identity was not found.");
                NativeNpcVitalsBinding.BindTo(owner,native,restored,resident);
                Require(owner.GetComponent<NativeNpcVitalsBinding>() &&
                    native.Skills.GetPermanentSkillValue(skill)==expectedSkill,
                    "Restored companion skill did not reach the rebuilt native entity.");
            }
            finally {UnityEngine.Object.DestroyImmediate(owner);}
        }

        private static NpcArchetype Archetype(WorldStateStore store,string id)
        {
            var progress=store.ReadNpcProgress(id);
            return NpcVitalsBindingPolicy.ResolveArchetype(progress!=null,
                progress==null?NpcArchetype.Citizen:progress.Archetype);
        }
        private static void Require(bool okay,string message)
        {if(!okay)throw new InvalidOperationException(message);}
    }
}
