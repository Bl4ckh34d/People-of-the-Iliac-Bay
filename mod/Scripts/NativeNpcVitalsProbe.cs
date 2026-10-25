using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Native-only acceptance for eligible ordinary residents. The probe changes
    // resources through DFU entity damage APIs and uses normal save/load teardown
    // to exercise actor capture and reconstruction; it never writes Core values.
    public static class NativeNpcVitalsProbe
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private const string Flag="-peopleoftheiliacbay-npc-vitals-probe";
        private const string SavePrefix="People of the Iliac Bay - NPC vitals ";

        private sealed class Fixture
        {
            public string ResidentId;
            public NpcArchetype Archetype;
            public int MaximumHealth,MaximumMagicka,Health,Magicka;
            public bool Alive,Persisted;
        }

        private sealed class SavedCheckpoint
        {
            public string Title;
        }

        public static IEnumerator RunFixture(StreetPopulation streets,string character)
        {
            string originalFixtureSave=CommandLineValue("-peopleoftheiliacbay-fixture-save");
            Require(Array.IndexOf(Environment.GetCommandLineArgs(),Flag)>=0 &&
                NativeProbeText.IsTestCharacter(character??""),
                "Native NPC vitals probe requires its explicit flag and owned disposable character.");
            Require(!string.IsNullOrWhiteSpace(originalFixtureSave),
                "Native NPC vitals probe requires the exact original -peopleoftheiliacbay-fixture-save title.");
            Require(streets!=null && streets.Ready,"Native NPC vitals probe requires a ready town population.");
            var game=GameManager.Instance;
            Require(game!=null && game.PlayerEntity!=null && game.PlayerEnterExit,"Native NPC vitals probe has no live game session.");
            game.SaveLoadManager.EnumerateSaves();
            Require(game.SaveLoadManager.FindSaveFolderByNames(character,originalFixtureSave)>=0,
                "Exact original fixture save is missing: "+originalFixtureSave);
            if(game.PlayerEnterExit.IsPlayerInside)
            {
                game.PlayerEnterExit.TransitionExterior(true);
                float exteriorUntil=Time.realtimeSinceStartup+90f;
                while((game.PlayerEnterExit.IsPlayerInside || !streets.Ready) && Time.realtimeSinceStartup<exteriorUntil)yield return null;
            }
            Require(!game.PlayerEnterExit.IsPlayerInside && streets.Ready,
                "Could not return the owned fixture to its exterior before selecting an existing resident.");
            if(Environment.GetEnvironmentVariable("POTIB_PLAYER_HEALING_PROBE")=="1")
            {yield return ObservePlayerHealing(streets);yield break;}

            float until=Time.realtimeSinceStartup+120f;
            Fixture injured=null;ResidentActor actor=null;
            while(Time.realtimeSinceStartup<until)
            {
                var store=Store(streets);
                foreach(var candidate in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                {
                    Fixture found=TryEligible(candidate,store);
                    if(found!=null && !found.Persisted && found.Health==found.MaximumHealth &&
                        found.Magicka==found.MaximumMagicka && found.Health>1 && found.Magicka>0)
                    {injured=found;actor=candidate;break;}
                }
                if(injured!=null)break;
                yield return null;
            }
            Require(injured!=null && actor,"No existing eligible adult with positive HP and MP was naturally materialized; no actor was created.");
            var behavior=actor.GetComponent<DaggerfallEntityBehaviour>();
            Require(behavior && behavior.Entity!=null,"Selected resident has no native entity.");
            var entity=behavior.Entity;
            Require(entity.MaxHealth==injured.MaximumHealth && entity.MaxMagicka==injured.MaximumMagicka,
                "Native maximum HP/MP do not match the resident's seeded profile before damage: native="+
                entity.MaxHealth+"/"+entity.MaxMagicka+" seeded="+injured.MaximumHealth+"/"+injured.MaximumMagicka);

            var baseline=new Fixture {ResidentId=injured.ResidentId,Archetype=injured.Archetype,
                MaximumHealth=injured.MaximumHealth,MaximumMagicka=injured.MaximumMagicka,
                Health=injured.Health,Magicka=injured.Magicka,Alive=true,Persisted=injured.Persisted};
            var checkpointA=new SavedCheckpoint();
            yield return SaveCheckpoint(character,checkpointA);
            int healthBefore=entity.CurrentHealth;
            int healthDamage=Math.Max(1,Math.Min(entity.CurrentHealth-1,Math.Max(1,entity.CurrentHealth/4)));
            int magickaDamage=entity.CurrentMagicka;
            behavior.DamageHealthFromSource((DaggerfallEntityBehaviour)null,healthDamage,false,Vector3.zero);
            behavior.DamageMagickaFromSource((DaggerfallEntityBehaviour)null,magickaDamage);
            Require(entity.CurrentHealth>0 && entity.CurrentHealth<healthBefore && entity.CurrentMagicka==0,
                "DFU native damage APIs did not produce the requested nonlethal HP loss and zero-MP state.");
            Require(actor.Resident.IsAlive,"Zero magicka incorrectly marked the resident dead.");
            injured.Health=entity.CurrentHealth;injured.Magicka=entity.CurrentMagicka;
            injured.Persisted=true;
            int beforeStreamInstance=actor.GetInstanceID();
            MethodInfo clearActors=typeof(StreetPopulation).GetMethod("ClearActors",Private);
            Require(clearActors!=null,"Production resident stream teardown method changed.");
            // This is the same population retirement boundary used for scene
            // changes: it calls CaptureState before destroying active actors.
            clearActors.Invoke(streets,null);
            var afterFirstCapture=Store(streets);
            var capturedProgress=afterFirstCapture.ReadNpcProgress(injured.ResidentId);
            int observedDamage=healthBefore-injured.Health;
            Require(capturedProgress!=null && capturedProgress.HasPendingHealthEpisode &&
                capturedProgress.HealthEpisodeInitialHealth==healthBefore &&
                capturedProgress.HealthEpisodeFinalHealth==injured.Health &&
                capturedProgress.HealthEpisodeDamageTotal==observedDamage && observedDamage>0 &&
                capturedProgress.HealthEpisodeHealingTotal==0 && capturedProgress.HealthEpisodeChangeCount==1,
                "Production first-health-change capture did not create one exact unknown-cause pending episode: progress="+
                (capturedProgress==null?"missing":capturedProgress.HasPendingHealthEpisode+"; initial="+
                    capturedProgress.HealthEpisodeInitialHealth+"; final="+capturedProgress.HealthEpisodeFinalHealth+
                    "; damage="+capturedProgress.HealthEpisodeDamageTotal+"; healing="+
                    capturedProgress.HealthEpisodeHealingTotal+"; changes="+capturedProgress.HealthEpisodeChangeCount));
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FIRST_HEALTH_CHANGE_CAPTURED: resident="+injured.ResidentId+
                "; initial="+capturedProgress.HealthEpisodeInitialHealth+"; final="+
                capturedProgress.HealthEpisodeFinalHealth+"; damage="+capturedProgress.HealthEpisodeDamageTotal+
                "; healing="+capturedProgress.HealthEpisodeHealingTotal+"; changes="+
                capturedProgress.HealthEpisodeChangeCount+"; pending=true; cause=unknown");
            var retiredVitals=afterFirstCapture.ReadNpcVitals(injured.ResidentId,injured.Archetype);
            Require(retiredVitals.IsAlive && retiredVitals.IsPersisted && retiredVitals.CurrentHealth==injured.Health &&
                retiredVitals.CurrentMagicka==0 && retiredVitals.MaximumHealth==injured.MaximumHealth &&
                retiredVitals.MaximumMagicka==injured.MaximumMagicka,
                "Production actor retirement did not capture native HP/MP before destroying the body.");
            float rebindUntil=Time.realtimeSinceStartup+150f;
            ResidentActor rebound=null;
            while(Time.realtimeSinceStartup<rebindUntil)
            {
                rebound=streets.FindActor(injured.ResidentId);
                if(streets.Ready && rebound && rebound.gameObject.activeInHierarchy && rebound.GetInstanceID()!=beforeStreamInstance)break;
                yield return null;
            }
            Require(rebound && rebound.gameObject.activeInHierarchy && rebound.GetInstanceID()!=beforeStreamInstance,
                "Production actor retirement did not naturally rebuild the same resident identity.");
            var storeAfterCapture=Store(streets);
            var captured=storeAfterCapture.ReadNpcVitals(injured.ResidentId,injured.Archetype);
            var reboundBehavior=rebound.GetComponent<DaggerfallEntityBehaviour>();
            Require(reboundBehavior && reboundBehavior.Entity!=null && captured.IsAlive && captured.IsPersisted &&
                captured.CurrentHealth==injured.Health && captured.CurrentMagicka==0 &&
                captured.MaximumHealth==injured.MaximumHealth && captured.MaximumMagicka==injured.MaximumMagicka &&
                reboundBehavior.Entity.CurrentHealth==captured.CurrentHealth && reboundBehavior.Entity.CurrentMagicka==0 &&
                reboundBehavior.Entity.MaxHealth==captured.MaximumHealth && reboundBehavior.Entity.MaxMagicka==captured.MaximumMagicka,
                "Production actor stream/rebind lost changed HP/MP or seeded maxima.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_VITALS_CAPTURE_PASS: resident="+injured.ResidentId+"; maxHP="+injured.MaximumHealth+
                "; maxMP="+injured.MaximumMagicka+"; currentHP="+injured.Health+"; currentMP="+injured.Magicka+
                "; nativeDamageAPIs=true; zeroMPAlive=true; productionClearActors=true; sameIdentityRebuilt=true; sparseCapture=true");

            int woundedInstance=rebound.GetInstanceID();
            var staleWoundedActor=rebound;
            var staleWoundedBinding=rebound.GetComponent<NativeNpcVitalsBinding>();
            yield return LoadCheckpointAndVerify(streets,character,baseline,"full baseline A after wounded branch",woundedInstance,checkpointA.Title);
            // Simulate a delayed retirement callback from the wounded actor
            // after the baseline branch has replaced the store state and actor.
            // ResidentActor and the component both carry the pre-load write epoch.
            if(!object.ReferenceEquals(staleWoundedActor,null))staleWoundedActor.CaptureState();
            if(!object.ReferenceEquals(staleWoundedBinding,null))staleWoundedBinding.Capture();
            var afterLateCapture=Store(streets).ReadNpcVitals(injured.ResidentId,injured.Archetype);
            Require(afterLateCapture.IsAlive && !afterLateCapture.IsPersisted &&
                afterLateCapture.CurrentHealth==baseline.MaximumHealth && afterLateCapture.CurrentMagicka==baseline.MaximumMagicka,
                "A late capture from the wounded actor overwrote full-resource baseline branch A.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_VITALS_BRANCH_RESTORE_PASS: resident="+injured.ResidentId+"; saved=A-full; currentBranch=B-wounded-zeroMP; " +
                "loaded=A; staleWoundedCaptureDidNotOverwrite=true; explicitOldActorCaptureChecked=true; explicitOldBindingCaptureChecked=true");

            actor=streets.FindActor(injured.ResidentId);
            Require(actor && actor.gameObject.activeInHierarchy,"Baseline-branch resident is unavailable after rollback.");
            behavior=actor.GetComponent<DaggerfallEntityBehaviour>();
            Require(behavior && behavior.Entity!=null && behavior.Entity.CurrentHealth>0,"Restored resident has no live native entity.");
            entity=behavior.Entity;
            int restoredHealthBefore=entity.CurrentHealth;
            int restoredMagickaBefore=entity.CurrentMagicka;
            Require(restoredHealthBefore==baseline.MaximumHealth && restoredMagickaBefore==baseline.MaximumMagicka,
                "Baseline A did not restore full native HP/MP before creating a new wounded branch.");
            int secondHealthDamage=Math.Max(1,Math.Min(restoredHealthBefore-1,Math.Max(1,restoredHealthBefore/4)));
            behavior.DamageHealthFromSource((DaggerfallEntityBehaviour)null,secondHealthDamage,false,Vector3.zero);
            behavior.DamageMagickaFromSource((DaggerfallEntityBehaviour)null,restoredMagickaBefore);
            Require(actor.Resident.IsAlive && entity.CurrentHealth>0 && entity.CurrentHealth<restoredHealthBefore && entity.CurrentMagicka==0,
                "Native damage APIs failed on the restored baseline actor.");
            actor.CaptureState();
            injured.Health=entity.CurrentHealth;injured.Magicka=entity.CurrentMagicka;
            int beforeWoundedDiskInstance=actor.GetInstanceID();
            yield return SaveLoadAndVerify(streets,character,injured,"wounded resources after branch restore",beforeWoundedDiskInstance);

            actor=streets.FindActor(injured.ResidentId);
            Require(actor && actor.gameObject.activeInHierarchy,"Wounded branch resident is unavailable for death test.");
            behavior=actor.GetComponent<DaggerfallEntityBehaviour>();
            Require(behavior && behavior.Entity!=null && behavior.Entity.CurrentHealth>0,"Reloaded wounded resident has no live native entity.");
            entity=behavior.Entity;
            int lethalDamage=entity.CurrentHealth;
            behavior.DamageHealthFromSource((DaggerfallEntityBehaviour)null,lethalDamage,false,Vector3.zero);
            float deathUntil=Time.realtimeSinceStartup+20f;
            while(actor && actor.Resident!=null && actor.Resident.IsAlive && Time.realtimeSinceStartup<deathUntil)yield return null;
            var deadStore=Store(streets);var deadVitals=deadStore.ReadNpcVitals(injured.ResidentId,injured.Archetype);
            Require(!deadVitals.IsAlive && deadVitals.CurrentHealth==0,"Native zero-HP event did not persist terminal death.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_VITALS_DEATH_PASS: resident="+injured.ResidentId+"; nativeZeroHP=true; ledgerDead=true");
            yield return SaveLoadAndVerify(streets,character,new Fixture {ResidentId=injured.ResidentId,
                Archetype=injured.Archetype,MaximumHealth=injured.MaximumHealth,MaximumMagicka=injured.MaximumMagicka,
                Health=0,Magicka=deadVitals.CurrentMagicka,Alive=false},"dead identity after native zero HP",actor?actor.GetInstanceID():0);

            var finalStore=Store(streets);var finalVitals=finalStore.ReadNpcVitals(injured.ResidentId,injured.Archetype);
            Require(!finalVitals.IsAlive && finalVitals.CurrentHealth==0 && !streets.FindActor(injured.ResidentId),
                "Disk load revived or rematerialized the resident after native death.");
            var host=UnityEngine.Object.FindObjectOfType<PeopleOfTheIliacBayMod>();
            Require(host,"Native NPC vitals probe cannot find the live mod host for original-save restore.");
            yield return ConversationMoodProbe.LoadSave(host,streets,character,originalFixtureSave,false);
            Require(streets.Ready,"Original fixture save loaded but town population is not ready.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_VITALS_ORIGINAL_BASELINE_RESTORED: character="+character+
                "; save="+originalFixtureSave+"; exactSaveLoaded=true; townReady=true");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_VITALS_PASS: resident="+injured.ResidentId+
                "; nativeDamage=true; productionStreamRebind=pass; branchRollbackA=pass; woundedDiskReload=pass; zeroMP=alive; zeroHP=terminal; deadReload=pass");
        }

        private static IEnumerator ObservePlayerHealing(StreetPopulation streets)
        {
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            clock.RaiseTime((float)((86400+10*3600-clock.ToSeconds()%86400)%86400));
            yield return null;
            var store=Store(streets);ResidentActor actor=null;Fixture fixture=null;
            float deadline=Time.realtimeSinceStartup+120;
            while(Time.realtimeSinceStartup<deadline && !actor)
            {
                foreach(var candidate in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                {
                    var eligible=TryEligible(candidate,store);
                    if(eligible!=null && eligible.Health==eligible.MaximumHealth && eligible.Health>30 &&
                        candidate.GetComponent<EntityEffectManager>())
                    {actor=candidate;fixture=eligible;break;}
                }
                if(!actor)yield return null;
            }
            Require(actor && fixture!=null,"Player-healing fixture has no eligible living native resident.");
            var body=actor.GetComponent<DaggerfallEntityBehaviour>();
            var binding=actor.GetComponent<NativeNpcVitalsBinding>();
            var manager=actor.GetComponent<EntityEffectManager>();
            var player=GameManager.Instance.PlayerEntityBehaviour;
            int start=store.ReadPendingNpcExperiences(fixture.ResidentId,100).FindAll(e=>
                e.EventKind==NpcExperienceEventKind.Healing && e.ResponsibleActorId==NpcDirectedRelationship.PlayerTargetId).Count;
            body.Entity.DecreaseHealth(20);binding.Capture();
            ApplyNativeHeal(manager,body,5);binding.Capture();
            Require(store.ReadPendingNpcExperiences(fixture.ResidentId,100).FindAll(e=>
                e.EventKind==NpcExperienceEventKind.Healing && e.ResponsibleActorId==NpcDirectedRelationship.PlayerTargetId).Count==start,
                "NPC self-healing was credited to the player.");
            int before=body.Entity.CurrentHealth;
            ApplyNativeHeal(manager,player,5);binding.Capture();
            Require(body.Entity.CurrentHealth==before+5,"Actual player-sourced native HealHealth did not restore five HP.");
            var events=store.ReadPendingNpcExperiences(fixture.ResidentId,100);
            var received=events.FindLast(e=>e.EventKind==NpcExperienceEventKind.Healing &&
                e.ResponsibleActorAttribution==NpcResponsibleActorAttribution.Known &&
                e.ResponsibleActorId==NpcDirectedRelationship.PlayerTargetId);
            Require(received!=null && received.NativeImpactDetails!=null && received.NativeImpactDetails.TotalHealing==5 &&
                received.NativeImpactDetails.TotalDamage==0 && events.FindAll(e=>e.EventKind==NpcExperienceEventKind.Healing &&
                e.ResponsibleActorId==NpcDirectedRelationship.PlayerTargetId).Count==start+1,
                "Native healing did not retain the exact player source and HP change once.");
            Require(store.ReadNpcVitals(fixture.ResidentId,fixture.Archetype).CurrentHealth==body.Entity.CurrentHealth,
                "Player healing did not rebaseline the native sampler.");
            store.FlushNpcHealthExperiences(DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),0);
            Require(store.ReadPendingNpcExperiences(fixture.ResidentId,100).Count==events.Count,
                "The health sampler duplicated the attributed player heal.");
            body.Entity.SetHealth(body.Entity.MaxHealth);binding.Capture();
            ApplyNativeHeal(manager,player,5);binding.Capture();
            Require(store.ReadPendingNpcExperiences(fixture.ResidentId,100).FindAll(e=>
                e.EventKind==NpcExperienceEventKind.Healing && e.ResponsibleActorId==NpcDirectedRelationship.PlayerTargetId).Count==start+1,
                "A full-health target produced false player-healing credit.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PLAYER_HEALING_PASS: resident="+fixture.ResidentId+
                "; actualNativeHealHealth=true; nativeEffectCaster=player; hpRestored=5; selfHealExcluded=true; fullHealthExcluded=true; samplerReplay=false; modelAppraisal=false; spellTargetingNotTested=true");
        }

        private static void ApplyNativeHeal(EntityEffectManager target,DaggerfallEntityBehaviour caster,int amount)
        {
            var magnitude=BaseEntityEffect.DefaultEffectSettings();
            magnitude.MagnitudeBaseMin=amount;magnitude.MagnitudeBaseMax=amount;
            magnitude.MagnitudePlusMin=0;magnitude.MagnitudePlusMax=0;magnitude.MagnitudePerLevel=1;
            var created=target.CreateSpellBundle(HealHealth.EffectKey,ElementTypes.Magic,magnitude);
            Require(created!=null,"Native effect broker cannot create HealHealth.");
            var settings=created.Settings;settings.TargetType=TargetTypes.ByTouch;
            target.AssignBundle(new EntityEffectBundle(settings,caster),AssignBundleFlags.BypassSavingThrows|AssignBundleFlags.BypassChance);
        }

        private static string CommandLineValue(string key)
        {
            string[] arguments=Environment.GetCommandLineArgs();
            int index=Array.IndexOf(arguments,key);
            return index>=0 && index+1<arguments.Length ? arguments[index+1] : null;
        }

        private static Fixture TryEligible(ResidentActor actor,WorldStateStore store)
        {
            if(!actor || !actor.gameObject.activeInHierarchy || actor.Resident==null || !actor.Resident.IsAlive ||
                actor.Resident.IsQuestProtected || actor.Resident.AgeGroup!=ResidentAgeGroup.Adult ||
                !actor.GetComponent<NativeNpcVitalsBinding>())return null;
            var native=actor.GetComponent<DaggerfallEntityBehaviour>();
            if(!native || native.Entity==null || native.Entity.CurrentHealth<=1 || native.Entity.CurrentMagicka<=0)return null;
            var progress=store.ReadNpcProgress(actor.Resident.Id);
            NpcArchetype archetype=progress==null?NpcArchetype.Citizen:progress.Archetype;
            NpcVitalsView vitals;
            try {vitals=store.ReadNpcVitals(actor.Resident.Id,archetype);}catch(ArgumentException){return null;}
            if(!vitals.IsAlive || native.Entity.MaxHealth!=vitals.MaximumHealth || native.Entity.MaxMagicka!=vitals.MaximumMagicka)return null;
            return new Fixture {ResidentId=actor.Resident.Id,Archetype=archetype,MaximumHealth=vitals.MaximumHealth,
                MaximumMagicka=vitals.MaximumMagicka,Health=native.Entity.CurrentHealth,Magicka=native.Entity.CurrentMagicka,
                Alive=true,Persisted=vitals.IsPersisted};
        }

        private static IEnumerator SaveLoadAndVerify(StreetPopulation streets,string character,Fixture expected,string purpose,int priorInstance)
        {
            var checkpoint=new SavedCheckpoint();
            yield return SaveCheckpoint(character,checkpoint);
            yield return LoadCheckpointAndVerify(streets,character,expected,purpose,priorInstance,checkpoint.Title);
        }

        private static IEnumerator SaveCheckpoint(string character,SavedCheckpoint checkpoint)
        {
            var game=GameManager.Instance;bool saved=false;
            SaveLoadManager.OnSaveEventHandler onSave=ignored=>saved=true;
            string title=SavePrefix+Guid.NewGuid().ToString("N");
            SaveLoadManager.OnSave+=onSave;
            try
            {
                game.SaveLoadManager.Save(character,title);
                float until=Time.realtimeSinceStartup+45f;
                while(!saved && Time.realtimeSinceStartup<until)yield return null;
                Require(saved,"Native vitals disk save timed out.");
                game.SaveLoadManager.EnumerateSaves();
                int slot=game.SaveLoadManager.FindSaveFolderByNames(character,title);
                Require(slot>=0,"Native vitals save slot was not enumerated.");
                checkpoint.Title=title;
            }
            finally {SaveLoadManager.OnSave-=onSave;}
        }

        private static IEnumerator LoadCheckpointAndVerify(StreetPopulation streets,string character,Fixture expected,
            string purpose,int priorInstance,string checkpointTitle)
        {
            var game=GameManager.Instance;bool loaded=false;
            SaveLoadManager.OnLoadEventHandler onLoad=ignored=>loaded=true;
            SaveLoadManager.OnLoad+=onLoad;
            try
            {
                game.SaveLoadManager.EnumerateSaves();
                int slot=game.SaveLoadManager.FindSaveFolderByNames(character,checkpointTitle);
                Require(slot>=0,"Native vitals disk slot was not found for "+purpose+".");
                game.SaveLoadManager.Load(slot);DaggerfallUI.Instance.PopToHUD();
                float until=Time.realtimeSinceStartup+150f;
                while((!loaded || !streets.Ready) && Time.realtimeSinceStartup<until)yield return null;
                Require(loaded && streets.Ready,"Native vitals disk reload timed out for "+purpose+".");
                if(expected.Alive)
                {
                    ResidentActor restored=null;until=Time.realtimeSinceStartup+90f;
                    while(Time.realtimeSinceStartup<until)
                    {restored=streets.FindActor(expected.ResidentId);if(restored && restored.gameObject.activeInHierarchy)break;yield return null;}
                    Require(restored && restored.gameObject.activeInHierarchy,"Eligible resident did not rematerialize after native disk load.");
                    Require(priorInstance==0 || restored.GetInstanceID()!=priorInstance,
                        "Disk reload retained the original ResidentActor; actor stream/rebind was not exercised.");
                    var behavior=restored.GetComponent<DaggerfallEntityBehaviour>();
                    var binding=restored.GetComponent<NativeNpcVitalsBinding>();
                    Require(behavior && behavior.Entity!=null && binding,"Reloaded resident did not receive native vitals binding.");
                    var entity=behavior.Entity;var store=Store(streets);
                    var vitals=store.ReadNpcVitals(expected.ResidentId,expected.Archetype);
                    Require(vitals.IsAlive && vitals.IsPersisted==expected.Persisted && vitals.CurrentHealth==expected.Health &&
                        vitals.CurrentMagicka==expected.Magicka && entity.CurrentHealth==expected.Health &&
                        entity.CurrentMagicka==expected.Magicka && entity.MaxHealth==expected.MaximumHealth &&
                        entity.MaxMagicka==expected.MaximumMagicka,
                        "Native HP/MP changed across disk save/load: "+purpose+"; ledger="+vitals.CurrentHealth+"/"+
                        vitals.CurrentMagicka+" entity="+entity.CurrentHealth+"/"+entity.CurrentMagicka);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_VITALS_DISK_PASS: resident="+expected.ResidentId+"; purpose="+purpose+
                        "; oldActor="+priorInstance+"; newActor="+restored.GetInstanceID()+"; maxHP="+entity.MaxHealth+
                        "; maxMP="+entity.MaxMagicka+"; currentHP="+entity.CurrentHealth+"; currentMP="+entity.CurrentMagicka+
                        "; actorRebuilt=true; nativeDiskSaveLoad=true");
                }
                else
                {
                    var store=Store(streets);var resident=store.ResolveResident(expected.ResidentId);
                    var dead=store.ReadNpcVitals(expected.ResidentId,expected.Archetype);
                    Require(resident!=null && !resident.IsAlive && !dead.IsAlive && dead.CurrentHealth==0,
                        "Disk reload revived dead resident identity after native zero HP.");
                    foreach(var body in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                        Require(!body || !body.gameObject.activeInHierarchy || body.Resident==null || body.Resident.Id!=expected.ResidentId,
                            "Disk reload materialized a native body for a dead resident.");
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_VITALS_DEAD_RELOAD_PASS: resident="+expected.ResidentId+
                        "; actorNotRecreated=true; health=0; alive=false");
                }
            }
            finally {SaveLoadManager.OnLoad-=onLoad;}
        }

        private static WorldStateStore Store(StreetPopulation streets)
        {return (WorldStateStore)typeof(StreetPopulation).GetField("worldStore",Private).GetValue(streets);}
        private static void Require(bool condition,string message)
        {if(!condition)throw new InvalidOperationException(message);}
    }
}
