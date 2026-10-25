using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcMagicCareTaskPhase { Reserved=1, Blocked=2, Completed=3 }

    /// <summary>Exact self-cast reservation. Only a trusted native adapter may attach a receipt.</summary>
    [Serializable]
    public sealed class NpcMagicCareTask
    {
        public string Id, PersonId, SpellId, EffectId;
        public NpcActionKind Kind;
        public NpcMagicCareNeed Need;
        public NpcMagicEffectKind EffectKind;
        public int MagickaCost, MagickaBefore, ConditionBefore;
        public long Revision;
        public double ReservedAt, UpdatedAt, RetryAt, AttemptDeadlineAt;
        public NpcMagicCareTaskPhase Phase;
        public string LastFailure;
        public NpcTreatmentReceipt Receipt;
        public bool HasObservedClear;
        public double ClearedAt;

        public static NpcMagicCareTask Reserve(string id,NpcMagicCareOffer offer,int conditionBefore,double now)
        {
            if(!IdValid(id) || offer==null || offer.Authorization==null || !Finite(now) || now<0 ||
                now>double.MaxValue-60)
                throw new ArgumentException("Invalid magic-care reservation.");
            NpcMagicAuthorization auth=offer.Authorization;
            if(auth.ActorId!=auth.TargetId || auth.Hostile || auth.MagickaCost<=0 ||
                conditionBefore!=offer.ObservedConditionBefore ||
                auth.MagickaBefore<auth.MagickaCost ||
                (offer.Need==NpcMagicCareNeed.Heal &&
                    (auth.Intent!=NpcMagicIntent.Heal || auth.EffectKind!=NpcMagicEffectKind.Health ||
                     auth.EffectId!="Heal-Health" || conditionBefore<1)) ||
                (offer.Need==NpcMagicCareNeed.CurePoison &&
                    (auth.Intent!=NpcMagicIntent.Cure || auth.EffectKind!=NpcMagicEffectKind.Condition ||
                     auth.EffectId!="Cure-Poison" || conditionBefore<1)) ||
                (offer.Need==NpcMagicCareNeed.CureDisease &&
                    (auth.Intent!=NpcMagicIntent.Cure || auth.EffectKind!=NpcMagicEffectKind.Condition ||
                     auth.EffectId!="Cure-Disease" || conditionBefore<1)))
                throw new ArgumentException("Magic-care reservation lacks exact self-treatment evidence.");
            var task=new NpcMagicCareTask {Id=id,PersonId=auth.ActorId,SpellId=auth.SpellId,
                EffectId=auth.EffectId,Kind=NpcActionKind.CastSpell,Need=offer.Need,
                EffectKind=auth.EffectKind,MagickaCost=auth.MagickaCost,
                MagickaBefore=auth.MagickaBefore,ConditionBefore=conditionBefore,
                Revision=1,ReservedAt=now,UpdatedAt=now,AttemptDeadlineAt=now+60,
                Phase=NpcMagicCareTaskPhase.Reserved};
            Validate(task);return task;
        }

        public static bool MatchesObservation(NpcMagicCareTask task,NpcMagicAuthorization authorization,
            NpcMagicObservedOutcome outcome)
        {
            return task!=null && task.Phase==NpcMagicCareTaskPhase.Reserved &&
                authorization!=null && authorization.ActorId==task.PersonId &&
                authorization.TargetId==task.PersonId && authorization.SpellId==task.SpellId &&
                authorization.EffectId==task.EffectId && authorization.EffectKind==task.EffectKind &&
                authorization.MagickaBefore==task.MagickaBefore &&
                authorization.MagickaCost==task.MagickaCost &&
                outcome!=null && outcome.EffectValueBefore==task.ConditionBefore &&
                NpcMagicActionContract.MatchesObservedOutcome(authorization,outcome);
        }

        public static bool CanRetry(NpcMagicCareTask task,double now)
        {return task!=null && task.Phase==NpcMagicCareTaskPhase.Blocked && Finite(now) && now>=task.RetryAt;}

        public static NpcMagicCareTask CopyValidated(NpcMagicCareTask value)
        {
            Validate(value);
            return new NpcMagicCareTask {Id=value.Id,PersonId=value.PersonId,SpellId=value.SpellId,
                EffectId=value.EffectId,Kind=value.Kind,Need=value.Need,EffectKind=value.EffectKind,
                MagickaCost=value.MagickaCost,MagickaBefore=value.MagickaBefore,
                ConditionBefore=value.ConditionBefore,Revision=value.Revision,
                ReservedAt=value.ReservedAt,UpdatedAt=value.UpdatedAt,RetryAt=value.RetryAt,
                AttemptDeadlineAt=value.AttemptDeadlineAt,
                Phase=value.Phase,LastFailure=value.LastFailure,
                Receipt=NpcTreatmentReceipt.CopyFields(value.Receipt),
                HasObservedClear=value.HasObservedClear,ClearedAt=value.ClearedAt};
        }

        internal static void Validate(NpcMagicCareTask value)
        {
            if(value==null || !IdValid(value.Id) || !IdValid(value.PersonId) ||
                !IdValid(value.SpellId) || !IdValid(value.EffectId) ||
                value.Kind!=NpcActionKind.CastSpell || value.Revision<1 ||
                value.MagickaCost<=0 || value.MagickaBefore<value.MagickaCost ||
                value.ConditionBefore<1 || !Finite(value.ReservedAt) || value.ReservedAt<0 ||
                !Finite(value.UpdatedAt) || value.UpdatedAt<value.ReservedAt ||
                !Finite(value.RetryAt) || value.RetryAt<0 ||
                !Finite(value.AttemptDeadlineAt) || value.AttemptDeadlineAt<=value.ReservedAt ||
                value.AttemptDeadlineAt-value.ReservedAt>86400 ||
                (int)value.Phase<1 || (int)value.Phase>3 ||
                (value.Need==NpcMagicCareNeed.Heal &&
                    (value.EffectId!="Heal-Health" || value.EffectKind!=NpcMagicEffectKind.Health)) ||
                (value.Need==NpcMagicCareNeed.CurePoison &&
                    (value.EffectId!="Cure-Poison" || value.EffectKind!=NpcMagicEffectKind.Condition)) ||
                (value.Need==NpcMagicCareNeed.CureDisease &&
                    (value.EffectId!="Cure-Disease" || value.EffectKind!=NpcMagicEffectKind.Condition)) ||
                (int)value.Need<0 || (int)value.Need>2 ||
                (value.Phase==NpcMagicCareTaskPhase.Reserved &&
                    (value.RetryAt!=0 || value.LastFailure!=null || value.Receipt!=null ||
                     value.UpdatedAt>=value.AttemptDeadlineAt)) ||
                (value.Phase==NpcMagicCareTaskPhase.Blocked &&
                    (value.RetryAt!=value.UpdatedAt+300 || !IdValid(value.LastFailure) || value.Receipt!=null)) ||
                (value.Phase==NpcMagicCareTaskPhase.Completed &&
                    (value.RetryAt!=0 || value.LastFailure!=null || value.Receipt==null ||
                     value.UpdatedAt>=value.AttemptDeadlineAt)) ||
                (value.HasObservedClear ?
                    value.Phase!=NpcMagicCareTaskPhase.Completed || !Finite(value.ClearedAt) ||
                    value.ClearedAt<value.UpdatedAt : value.ClearedAt!=0))
                throw new ArgumentException("Invalid exact magic-care task.");
            if(value.Receipt!=null)NpcTreatmentReceipt.ValidateMagicCare(value.Receipt,value);
        }

        private static bool IdValid(string value)
        {return !string.IsNullOrWhiteSpace(value) && value.Length<=240;}
        private static bool Finite(double value)
        {return !double.IsNaN(value) && !double.IsInfinity(value);}
    }

    public sealed partial class WorldStateStore
    {
        public NpcMagicCareTask ReadNpcMagicCareTask(string id)
        {
            var row=state.NpcMagicCareTasks.Find(value=>value.Id==id);
            return row==null?null:NpcMagicCareTask.CopyValidated(row);
        }

        /// <summary>Reservation only. The runtime must still recheck duty, spellbook, and native state.</summary>
        public bool TryReserveNpcMagicCareTask(string id,NpcMagicCareOffer offer,int conditionBefore,double now)
        {
            NpcMagicCareTask candidate;
            try {candidate=NpcMagicCareTask.Reserve(id,offer,conditionBefore,now);}
            catch(ArgumentException) {return false;}
            var resident=ResolveResident(candidate.PersonId);
            if(resident==null || !resident.IsAlive || resident.IsQuestProtected ||
                resident.AgeGroup!=ResidentAgeGroup.Adult ||
                ActiveNpcTask(candidate.PersonId)!=null || ActiveNpcAction(candidate.PersonId)!=null ||
                state.NpcMagicCareTasks.Count>=100000 ||
                state.NpcMagicCareTasks.Exists(value=>value.Id==id ||
                    value.PersonId==candidate.PersonId && value.Phase==NpcMagicCareTaskPhase.Reserved ||
                    value.PersonId==candidate.PersonId && value.Need==candidate.Need && !value.HasObservedClear))
                return false;
            state.NpcMagicCareTasks.Add(candidate);return true;
        }

        public bool TryBlockNpcMagicCareTask(string id,long expectedRevision,double now,string reason)
        {
            int index=state.NpcMagicCareTasks.FindIndex(value=>value.Id==id);
            if(index<0 || string.IsNullOrWhiteSpace(reason) || reason.Length>240 ||
                double.IsNaN(now) || double.IsInfinity(now))return false;
            var current=state.NpcMagicCareTasks[index];
            if(current.Phase!=NpcMagicCareTaskPhase.Reserved || current.Revision!=expectedRevision ||
                current.Revision==long.MaxValue || now<current.UpdatedAt || now>double.MaxValue-300)return false;
            var next=NpcMagicCareTask.CopyValidated(current);
            next.Phase=NpcMagicCareTaskPhase.Blocked;next.Revision++;next.UpdatedAt=now;
            next.RetryAt=now+300;next.LastFailure=reason.Trim();
            NpcMagicCareTask.Validate(next);state.NpcMagicCareTasks[index]=next;return true;
        }

        public bool TryRetryNpcMagicCareTask(string id,long expectedRevision,NpcMagicCareOffer offer,
            int conditionBefore,double now)
        {
            int index=state.NpcMagicCareTasks.FindIndex(value=>value.Id==id);
            if(index<0)return false;
            var current=state.NpcMagicCareTasks[index];
            if(current.Revision!=expectedRevision || current.Revision==long.MaxValue ||
                !NpcMagicCareTask.CanRetry(current,now) ||
                ActiveNpcTask(current.PersonId)!=null || ActiveNpcAction(current.PersonId)!=null)return false;
            NpcMagicCareTask candidate;
            try {candidate=NpcMagicCareTask.Reserve(id,offer,conditionBefore,now);}
            catch(ArgumentException) {return false;}
            var resident=ResolveResident(current.PersonId);
            if(candidate.PersonId!=current.PersonId || candidate.Need!=current.Need ||
                resident==null || !resident.IsAlive || resident.IsQuestProtected ||
                resident.AgeGroup!=ResidentAgeGroup.Adult)return false;
            candidate.Revision=current.Revision+1;
            NpcMagicCareTask.Validate(candidate);state.NpcMagicCareTasks[index]=candidate;return true;
        }

        /// <summary>
        /// Trusted native boundary. The caller must roll back its cast if this returns false after
        /// invoking applyNativeEffect. Public observation data alone cannot complete treatment.
        /// </summary>
        internal bool TryCompleteNpcMagicCareTask(string id,long expectedRevision,double now,
            NpcArchetype archetype,long expectedVitalsRevision,
            Func<NpcMagicCareOffer> refreshNativeOffer,Func<NpcMagicObservedOutcome> applyNativeEffect)
        {
            int index=state.NpcMagicCareTasks.FindIndex(value=>value.Id==id);
            if(index<0 || refreshNativeOffer==null || applyNativeEffect==null ||
                double.IsNaN(now) || double.IsInfinity(now))return false;
            var current=state.NpcMagicCareTasks[index];
            if(current.Revision!=expectedRevision || expectedRevision==long.MaxValue ||
                current.Phase!=NpcMagicCareTaskPhase.Reserved || now<current.UpdatedAt ||
                now>=current.AttemptDeadlineAt ||
                !Enum.IsDefined(typeof(NpcArchetype),archetype) ||
                ActiveNpcTask(current.PersonId)!=null || ActiveNpcAction(current.PersonId)!=null)return false;
            var resident=ResolveResident(current.PersonId);
            if(resident==null || !resident.IsAlive || resident.IsQuestProtected ||
                resident.AgeGroup!=ResidentAgeGroup.Adult)return false;
            var vitals=ReadNpcVitals(current.PersonId,archetype);
            if(!vitals.IsPersisted || !vitals.IsAlive || vitals.Revision!=expectedVitalsRevision ||
                expectedVitalsRevision>=NpcSkillProgression.MaximumRawExperience ||
                vitals.CurrentMagicka!=current.MagickaBefore ||
                current.Need==NpcMagicCareNeed.Heal && vitals.CurrentHealth!=current.ConditionBefore)
                return false;
            NpcMagicCareOffer refreshed;
            try {refreshed=refreshNativeOffer();} catch(Exception) {return false;}
            if(refreshed==null || refreshed.Need!=current.Need || refreshed.Authorization==null ||
                refreshed.Authorization.ActorId!=current.PersonId ||
                refreshed.Authorization.TargetId!=current.PersonId ||
                refreshed.Authorization.SpellId!=current.SpellId ||
                refreshed.Authorization.EffectId!=current.EffectId ||
                refreshed.Authorization.EffectKind!=current.EffectKind ||
                refreshed.Authorization.MagickaCost!=current.MagickaCost ||
                refreshed.Authorization.MagickaBefore!=current.MagickaBefore ||
                refreshed.ObservedConditionBefore!=current.ConditionBefore)return false;
            NpcMagicObservedOutcome outcome;
            try {outcome=applyNativeEffect();} catch(Exception) {return false;}
            if(!NpcMagicCareTask.MatchesObservation(current,refreshed.Authorization,outcome))return false;
            var next=NpcMagicCareTask.CopyValidated(current);
            next.Phase=NpcMagicCareTaskPhase.Completed;next.Revision++;next.UpdatedAt=now;
            if(current.Need==NpcMagicCareNeed.Heal ? outcome.EffectValueAfter==vitals.MaximumHealth :
                outcome.EffectValueAfter==0)
            {next.HasObservedClear=true;next.ClearedAt=now;}
            try {next.Receipt=NpcTreatmentReceipt.MagicSelfCast(next,outcome,now);NpcMagicCareTask.Validate(next);}
            catch(ArgumentException) {return false;}

            NpcTreatmentVitalsStage stage;
            if(current.Need==NpcMagicCareNeed.Heal)
            {
                if(outcome.EffectValueAfter>vitals.MaximumHealth ||
                    !TryStageTreatmentVitals(current.PersonId,archetype,expectedVitalsRevision,
                        outcome.EffectValueBefore,outcome.EffectValueAfter,outcome.MagickaAfter,now,out stage))return false;
            }
            else
            {
                var actor=IndexedNpcProgress(current.PersonId);
                if(actor==null || actor.Archetype!=archetype || actor.VitalsRevision!=expectedVitalsRevision ||
                    now<actor.VitalsCapturedAt)return false;
                var proposed=CopyNpcProgress(actor);
                proposed.CurrentMagicka=outcome.MagickaAfter;proposed.VitalsCapturedAt=now;
                proposed.VitalsRevision++;
                int actorIndex=state.NpcActors.IndexOf(actor);
                if(actorIndex<0)return false;
                stage=new NpcTreatmentVitalsStage {Current=actor,Proposed=proposed,Index=actorIndex};
            }
            if(!ReferenceEquals(state.NpcMagicCareTasks[index],current) ||
                !ReferenceEquals(IndexedNpcProgress(current.PersonId),stage.Current))return false;
            CommitTreatmentVitals(current.PersonId,stage);
            state.NpcMagicCareTasks[index]=next;
            return true;
        }

        private static List<NpcMagicCareTask> CopyNpcMagicCareTasks(WorldState source,WorldState validated)
        {
            var result=new List<NpcMagicCareTask>();
            if(source.Version<57)return result;
            if(source.NpcMagicCareTasks==null || source.NpcMagicCareTasks.Count>100000)
                throw new ArgumentException("Invalid NPC magic-care ledger.");
            var ids=new HashSet<string>(StringComparer.Ordinal);
            var owners=new HashSet<string>(StringComparer.Ordinal);
            var activeOwners=new HashSet<string>(StringComparer.Ordinal);
            foreach(var row in source.NpcMagicCareTasks)
            {
                var copy=NpcMagicCareTask.CopyValidated(row);
                bool alive;
                if(!ids.Add(copy.Id) || !KnownNpcIdentity(validated,copy.PersonId,out alive) ||
                    copy.Phase==NpcMagicCareTaskPhase.Reserved && !activeOwners.Add(copy.PersonId) ||
                    !copy.HasObservedClear && !owners.Add(copy.PersonId+"\n"+copy.Need))
                    throw new ArgumentException("Duplicate or unknown NPC magic-care owner.");
                result.Add(copy);
            }
            return result;
        }
    }
}
