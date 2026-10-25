using System;
using DaggerfallConnect;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.MagicAndEffects;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>Commits health recovery only after one native NPC actually rests at its saved bed.</summary>
    public static class NativeNpcRestTreatment
    {
        public static int RecoveryRatePerGameHour(DaggerfallEntity entity)
        {
            if(entity==null || entity.MaxHealth<1) return 0;
            int medical=entity.Skills.GetLiveSkillValue(DFCareer.Skills.Medical);
            int endurance=entity.Stats.LiveEndurance;
            return RecoveryRatePerGameHour(endurance,medical,entity.MaxHealth);
        }

        // DFU's CalculateHealthRecoveryRate(PlayerEntity) formula, applied to the
        // resident's native medical skill, endurance and maximum health. NPCs do
        // not inherit player-only rapid-healing flags or the player's stats.
        public static int RecoveryRatePerGameHour(int endurance,int medical,int maximumHealth)
        {
            if(endurance<0 || medical<0 || maximumHealth<1) return 0;
            double rate=FormulaHelper.HealingRateModifier(endurance)+(medical+60)*maximumHealth/1000d;
            if(double.IsNaN(rate) || double.IsInfinity(rate)) return 0;
            return Math.Max((int)Math.Floor(rate),1);
        }

        public static bool TryComplete(WorldStateStore store,NpcActionJob job,GameObject actor,double now,
            Func<bool> bedAndStatusStillValid,out string reason)
        {
            reason="The resident did not complete uninterrupted rest at the exact accessible bed.";
            if(store==null || job==null || !actor || bedAndStatusStillValid==null ||
                job.Kind!=NpcActionKind.RestToHeal || job.Phase!=NpcActionPhase.Executing || job.Target==null ||
                job.Target.Kind!=NpcActionTargetKind.IndoorPoint || string.IsNullOrWhiteSpace(job.Target.ContainerId) ||
                job.Target.ItemId!=null || job.Target.PersonId!=null || job.RestStartedAt<job.AcceptedAt ||
                job.RestDurationSeconds<=0 || job.FinishAt!=job.RestStartedAt+job.RestDurationSeconds ||
                double.IsNaN(now) || double.IsInfinity(now) || now<job.FinishAt || now>=job.DeadlineAt ||
                !bedAndStatusStillValid()) return false;

            ResidentActor residentActor=actor.GetComponent<ResidentActor>();
            DaggerfallEntityBehaviour body=actor.GetComponent<DaggerfallEntityBehaviour>();
            NativeNpcVitalsBinding binding=actor.GetComponent<NativeNpcVitalsBinding>();
            EntityEffectManager effects=actor.GetComponent<EntityEffectManager>();
            if(residentActor==null || residentActor.Resident==null || residentActor.Resident.Id!=job.ResidentId ||
                body==null || body.Entity==null || binding==null || effects==null ||
                effects.PoisonCount>0 || effects.DiseaseCount>0) return false;

            Resident resident=store.ResolveResident(job.ResidentId);
            if(resident==null || !NpcVitalsBindingPolicy.Eligible(resident.IsAlive,
                resident.AgeGroup==ResidentAgeGroup.Adult,resident.IsQuestProtected)) return false;

            // A final native sample detects damage or another HP/MP change before
            // the transactional store checks the revision captured at acceptance.
            binding.Capture();
            int health,maximumHealth,magicka,maximumMagicka;
            if(!binding.TryReadCurrent(out health,out maximumHealth,out magicka,out maximumMagicka)) return false;
            NpcArchetype archetype=store.ResolveNpcArchetype(job.ResidentId);
            NpcVitalsView saved=store.ReadNpcVitals(job.ResidentId,archetype);
            if(saved==null || !saved.IsAlive || !saved.IsPersisted || saved.Revision!=job.ExpectedVitalsRevision ||
                saved.CurrentHealth!=health || saved.MaximumHealth!=maximumHealth || saved.CurrentMagicka!=magicka ||
                saved.MaximumMagicka!=maximumMagicka || health>=maximumHealth || !effects ||
                effects.PoisonCount>0 || effects.DiseaseCount>0 || !bedAndStatusStillValid()) return false;

            long elapsedHours=(long)Math.Floor((now-job.RestStartedAt)/3600d);
            int rate=RecoveryRatePerGameHour(body.Entity);
            if(elapsedHours<1 || rate<1) return false;
            long gain=(long)rate*elapsedHours;
            int after=(int)Math.Min(maximumHealth,(long)health+gain);
            if(after<=health) return false;

            bool nativeWriteStarted=false;
            bool committed;
            bool wasQuiesced=body.Entity.Quiesce;
            try
            {
                committed=store.TryCompleteNpcRestAction(job.Id,job.Revision,now,archetype,
                    saved.Revision,() =>
                    {
                        int currentHealth,currentMaximumHealth,currentMagicka,currentMaximumMagicka;
                        if(!binding.TryReadCurrent(out currentHealth,out currentMaximumHealth,out currentMagicka,out currentMaximumMagicka) ||
                            currentHealth!=health || currentMaximumHealth!=maximumHealth || currentMagicka!=magicka ||
                            currentMaximumMagicka!=maximumMagicka || effects.PoisonCount>0 || effects.DiseaseCount>0 ||
                            !bedAndStatusStillValid())
                            throw new InvalidOperationException("Native health, status, or bed access changed during rest.");

                        nativeWriteStarted=true;
                        body.Entity.Quiesce=true;
                        body.Entity.SetHealth(after);
                        if(body.Entity.CurrentHealth!=after || body.Entity.CurrentMagicka!=magicka ||
                            effects.PoisonCount>0 || effects.DiseaseCount>0 || !bedAndStatusStillValid())
                            throw new InvalidOperationException("DFU did not preserve the verified rest treatment effect.");
                        return NpcTreatmentRestNativeOutcome.Verified(health,after,magicka);
                    });
            }
            catch(Exception error)
            {
                if(nativeWriteStarted) RestoreVitals(body.Entity,health,magicka,wasQuiesced);
                reason=error.Message;
                return false;
            }
            finally { body.Entity.Quiesce=wasQuiesced; }

            if(!committed)
            {
                if(nativeWriteStarted) RestoreVitals(body.Entity,health,magicka,wasQuiesced);
                reason="The rest action or native vitals changed before treatment could commit.";
                return false;
            }

            // Store commit and native health now describe the same observation.
            // The next sampler pass must not record it as a second healing event.
            if(!binding.RebaselineAfterExternalCommit())
            {
                reason="The native health sampler could not rebase after the committed rest.";
                return false;
            }
            reason=null;
            return true;
        }

        private static void RestoreVitals(DaggerfallEntity entity,int health,int magicka,bool previousQuiesce)
        {
            try
            {
                entity.Quiesce=true;
                entity.SetHealth(health);
                entity.SetMagicka(magicka);
            }
            finally { entity.Quiesce=previousQuiesce; }
        }
    }
}
