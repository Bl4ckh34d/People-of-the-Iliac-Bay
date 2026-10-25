using System;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // One component per active ordinary resident. It samples only its bound
    // entity; unchanged samples do not call the save store or regenerate profiles.
    public sealed class NativeNpcVitalsBinding : MonoBehaviour
    {
        private const float SampleIntervalSeconds = 1f;
        private WorldStateStore store;
        private DaggerfallEntity entity;
        private Resident resident;
        private NpcArchetype archetype;
        private object writeEpoch;
        private int lastHealth;
        private int lastMagicka;
        private int maximumHealth;
        private int maximumMagicka;
        private long vitalsRevision;
        private float nextSampleAt;
        private bool bound;

        public static void BindTo(GameObject owner,DaggerfallEntity nativeEntity,WorldStateStore world,Resident person)
        {
            if(!owner || nativeEntity==null || world==null || person==null ||
                !NpcVitalsBindingPolicy.Eligible(person.IsAlive,person.AgeGroup==ResidentAgeGroup.Adult,person.IsQuestProtected)) return;
            var binding=owner.GetComponent<NativeNpcVitalsBinding>();
            if(!binding)binding=owner.AddComponent<NativeNpcVitalsBinding>();
            binding.Bind(nativeEntity,world,person);
        }

        private void Bind(DaggerfallEntity nativeEntity,WorldStateStore world,Resident person)
        {
            if(bound)return;
            // A zero-health native body belongs to death handling. Never revive it.
            if(nativeEntity.CurrentHealth<=0)return;
            archetype=world.ResolveNpcArchetype(person.Id);
            var actorProfile=world.ReadNpcActor(person.Id,archetype);
            var vitals=world.ReadNpcVitals(person.Id,archetype);
            if(!vitals.IsAlive)return;

            // Native CivilianEntity leaves Level at zero. Potion and spell
            // magnitudes use that level, so initialize an ordinary level-one
            // body while preserving any positive native/custom level.
            if(nativeEntity.Level<1)nativeEntity.Level=1;
            // Native combat and spell formulas read these actual entity values.
            // Apply the existing seeded profile once when the managed body is
            // materialized, retaining native temporary effect modifiers.
            for(int stat=0;stat<8;stat++)nativeEntity.Stats.SetPermanentStatValue(stat,actorProfile.Profile.Attribute(stat));
            for(int skill=0;skill<35;skill++)nativeEntity.Skills.SetPermanentSkillValue(skill,(short)actorProfile.Skill(skill));
            nativeEntity.MaxHealth=vitals.MaximumHealth;
            nativeEntity.MaxMagicka=vitals.MaximumMagicka;
            if(nativeEntity.MaxHealth<1 || nativeEntity.CurrentHealth<=0)return;

            bool wasQuiesced=nativeEntity.Quiesce;
            nativeEntity.Quiesce=true;
            try
            {
                nativeEntity.SetHealth(Mathf.Clamp(vitals.CurrentHealth,1,nativeEntity.MaxHealth));
                nativeEntity.SetMagicka(Mathf.Clamp(vitals.CurrentMagicka,0,nativeEntity.MaxMagicka));
            }
            finally { nativeEntity.Quiesce=wasQuiesced; }

            store=world;writeEpoch=world.CaptureNpcVitalsWriteEpoch();entity=nativeEntity;resident=person;
            maximumHealth=vitals.MaximumHealth;maximumMagicka=vitals.MaximumMagicka;
            lastHealth=entity.CurrentHealth;lastMagicka=entity.CurrentMagicka;
            vitalsRevision=vitals.Revision;
            nextSampleAt=Time.unscaledTime+SampleIntervalSeconds;
            bound=true;
            var spellbook=NativeNpcKnownSpellBinding.BindTo(gameObject,world,person);
            if(spellbook==null || !spellbook.HasRestoredBook)
                NativeNpcStartingSpells.EnsureKnownSpells(gameObject,world,person);
            if(spellbook!=null)spellbook.Capture();
            NativeNpcConditionBinding.BindTo(gameObject,world,person);
        }

        private void Update()
        {
            if(!bound || !gameObject.activeInHierarchy || GameManager.IsGamePaused || Time.unscaledTime<nextSampleAt)return;
            nextSampleAt=Time.unscaledTime+SampleIntervalSeconds;
            CaptureChangedResources(false);
            var conditions=GetComponent<NativeNpcConditionBinding>();if(conditions)conditions.Capture();
        }

        public bool TryReadCurrent(out int currentHealth,out int maximumHealth,out int currentMagicka,out int maximumMagicka)
        {
            currentHealth=maximumHealth=currentMagicka=maximumMagicka=0;
            if(!bound || entity==null || store==null || !store.IsNpcVitalsWriteEpochCurrent(writeEpoch) || resident==null ||
                !NpcVitalsBindingPolicy.Eligible(resident.IsAlive,resident.AgeGroup==ResidentAgeGroup.Adult,resident.IsQuestProtected))
                return false;
            currentHealth=entity.CurrentHealth;currentMagicka=entity.CurrentMagicka;
            maximumHealth=this.maximumHealth;maximumMagicka=this.maximumMagicka;
            return currentHealth>0 && maximumHealth>0 && currentHealth<=maximumHealth &&
                currentMagicka>=0 && maximumMagicka>0 && currentMagicka<=maximumMagicka;
        }

        // A synchronous native treatment commits its observed resources through
        // the action store. Rebase this sampler so its next tick does not replay
        // the same health change as a second native observation.
        public bool RebaselineAfterExternalCommit()
        {
            if(!bound || entity==null || store==null || resident==null ||
                !store.IsNpcVitalsWriteEpochCurrent(writeEpoch) ||
                !NpcVitalsBindingPolicy.Eligible(resident.IsAlive,resident.AgeGroup==ResidentAgeGroup.Adult,resident.IsQuestProtected))
                return false;
            int health=entity.CurrentHealth,magicka=entity.CurrentMagicka;
            if(health<=0 || health>maximumHealth || magicka<0 || magicka>maximumMagicka)return false;
            NpcVitalsView saved;
            try { saved=store.ReadNpcVitals(resident.Id,archetype); }
            catch { return false; }
            if(saved==null || !saved.IsAlive || !saved.IsPersisted || saved.Revision<=vitalsRevision ||
                saved.MaximumHealth!=maximumHealth || saved.MaximumMagicka!=maximumMagicka ||
                saved.CurrentHealth!=health || saved.CurrentMagicka!=magicka)
                return false;
            lastHealth=health;lastMagicka=magicka;vitalsRevision=saved.Revision;
            return true;
        }

        public Action<Exception> BeginObservedPlayerHealing(WorldStateStore expectedStore,DaggerfallEntityBehaviour source)
        {
            if(!bound || !isActiveAndEnabled || store!=expectedStore || !source ||
                source!=GameManager.Instance.PlayerEntityBehaviour || entity==null || resident==null ||
                !NpcVitalsBindingPolicy.Eligible(resident.IsAlive,resident.AgeGroup==ResidentAgeGroup.Adult,resident.IsQuestProtected) ||
                !store.IsNpcVitalsWriteEpochCurrent(writeEpoch) || entity.CurrentHealth<=0 || entity.CurrentHealth>=maximumHealth)return null;
            CaptureChangedResources(false);
            int before=entity.CurrentHealth,magicka=entity.CurrentMagicka;
            long revision=vitalsRevision;var epoch=writeEpoch;var nativeEntity=entity;
            return error=>
            {
                if(error!=null || !this || !isActiveAndEnabled || store!=expectedStore || entity!=nativeEntity ||
                    !source || source!=GameManager.Instance.PlayerEntityBehaviour || !resident.IsAlive ||
                    !store.IsNpcVitalsWriteEpochCurrent(epoch) || entity.CurrentHealth<=before)return;
                store.TryCaptureObservedPlayerHealing(epoch,resident.Id,archetype,before,magicka,
                    entity.CurrentHealth,entity.CurrentMagicka,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),revision);
                RebaselineAfterExternalCommit();
            };
        }

        public void Capture()
        {
            CaptureChangedResources(true);
            var spellbook=GetComponent<NativeNpcKnownSpellBinding>();if(spellbook)spellbook.Capture();
            var conditions=GetComponent<NativeNpcConditionBinding>();if(conditions)conditions.Capture();
        }

        private void CaptureChangedResources(bool refreshTimestamp)
        {
            if(!bound || store==null || !store.IsNpcVitalsWriteEpochCurrent(writeEpoch) || entity==null ||
                resident==null || !resident.IsAlive || entity.CurrentHealth<=0)return;
            int health=entity.CurrentHealth,magicka=entity.CurrentMagicka;
            bool changed=health!=lastHealth || magicka!=lastMagicka;
            if(!changed && !(refreshTimestamp && vitalsRevision>0))return;
            var game=DaggerfallUnity.Instance;
            if(game==null)return;
            double now=game.WorldTime.Now.ToSeconds();
            bool captured=changed
                ? store.TryCaptureObservedNpcVitalsChange(writeEpoch,resident.Id,archetype,
                    lastHealth,lastMagicka,health,magicka,now,vitalsRevision)
                : store.TryCaptureNpcVitals(writeEpoch,resident.Id,archetype,health,magicka,now,vitalsRevision);
            if(!captured)return;
            vitalsRevision=store.ReadNpcVitals(resident.Id,archetype).Revision;
            lastHealth=health;lastMagicka=magicka;
        }
    }
}
