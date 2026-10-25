using System;
using System.Collections.Generic;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcTreatmentKind { HealingItem=0, Rest=1, MagicSelfCast=2 }

    /// <summary>Durable native-confirmed health treatment attached to its action.</summary>
    [Serializable]
    public sealed class NpcTreatmentReceipt
    {
        public NpcTreatmentKind Kind;
        public ulong SourceItemUid;
        public string TransactionId;
        public int HealthBefore,HealthAfter;
        public double ObservedGameTime,RestStartedAt,RestDurationSeconds;
        // Only MagicSelfCast uses these exact native spell and effect observations.
        public string SpellId,EffectId;
        public int MagickaCost,MagickaBefore,MagickaAfter,ConditionBefore,ConditionAfter;

        private NpcTreatmentReceipt() { }

        internal static NpcTreatmentReceipt HealingItem(string transactionId,ulong sourceItemUid,
            int healthBefore,int healthAfter,double observedGameTime)
        {
            return new NpcTreatmentReceipt {Kind=NpcTreatmentKind.HealingItem,SourceItemUid=sourceItemUid,
                TransactionId=transactionId,HealthBefore=healthBefore,HealthAfter=healthAfter,
                ObservedGameTime=observedGameTime};
        }

        internal static NpcTreatmentReceipt Rest(string transactionId,int healthBefore,int healthAfter,
            double observedGameTime,double restStartedAt,double restDurationSeconds)
        {
            return new NpcTreatmentReceipt {Kind=NpcTreatmentKind.Rest,SourceItemUid=0,
                TransactionId=transactionId,HealthBefore=healthBefore,HealthAfter=healthAfter,
                ObservedGameTime=observedGameTime,RestStartedAt=restStartedAt,
                RestDurationSeconds=restDurationSeconds};
        }

        internal static NpcTreatmentReceipt MagicSelfCast(NpcMagicCareTask task,
            NpcMagicObservedOutcome outcome,double observedGameTime)
        {
            var receipt=new NpcTreatmentReceipt {Kind=NpcTreatmentKind.MagicSelfCast,
                TransactionId=task.Id+":treatment",ObservedGameTime=observedGameTime,
                SpellId=task.SpellId,EffectId=task.EffectId,MagickaCost=task.MagickaCost,
                MagickaBefore=outcome.MagickaBefore,MagickaAfter=outcome.MagickaAfter,
                ConditionBefore=outcome.EffectValueBefore,ConditionAfter=outcome.EffectValueAfter,
                HealthBefore=task.Need==NpcMagicCareNeed.Heal?outcome.EffectValueBefore:0,
                HealthAfter=task.Need==NpcMagicCareNeed.Heal?outcome.EffectValueAfter:0};
            ValidateMagicCare(receipt,task);return receipt;
        }

        internal static NpcTreatmentReceipt MagicSelfCast(NpcActionJob action,
            NpcMagicObservedOutcome outcome,double observedGameTime)
        {
            var receipt=new NpcTreatmentReceipt {Kind=NpcTreatmentKind.MagicSelfCast,
                TransactionId=action.Id+":treatment",ObservedGameTime=observedGameTime,
                SpellId=outcome.SpellId,EffectId=outcome.EffectId,
                MagickaCost=action.MagicHealing.MagickaCost,MagickaBefore=outcome.MagickaBefore,
                MagickaAfter=outcome.MagickaAfter,ConditionBefore=outcome.EffectValueBefore,
                ConditionAfter=outcome.EffectValueAfter,HealthBefore=outcome.EffectValueBefore,
                HealthAfter=outcome.EffectValueAfter};
            return receipt;
        }

        internal static NpcTreatmentReceipt CopyFields(NpcTreatmentReceipt value)
        {
            return value==null?null:new NpcTreatmentReceipt {Kind=value.Kind,SourceItemUid=value.SourceItemUid,
                TransactionId=value.TransactionId,HealthBefore=value.HealthBefore,HealthAfter=value.HealthAfter,
                ObservedGameTime=value.ObservedGameTime,RestStartedAt=value.RestStartedAt,
                RestDurationSeconds=value.RestDurationSeconds,SpellId=value.SpellId,EffectId=value.EffectId,
                MagickaCost=value.MagickaCost,MagickaBefore=value.MagickaBefore,MagickaAfter=value.MagickaAfter,
                ConditionBefore=value.ConditionBefore,ConditionAfter=value.ConditionAfter};
        }

        internal static NpcTreatmentReceipt CopyValidated(NpcTreatmentReceipt value,NpcActionJob action)
        {
            if(value==null)return null;
            Validate(value,action);
            return CopyFields(value);
        }

        private static void Validate(NpcTreatmentReceipt value,NpcActionJob action)
        {
            if(value==null || action==null || action.Phase!=NpcActionPhase.Completed ||
                action.CompletedEffectId!=action.Id+":observed" || value.TransactionId!=action.Id+":treatment" ||
                !Number(value.ObservedGameTime) || value.ObservedGameTime<action.AcceptedAt ||
                value.ObservedGameTime>=action.DeadlineAt || value.ObservedGameTime>action.UpdatedAt ||
                value.HealthBefore<1 || value.HealthAfter<=value.HealthBefore || value.HealthAfter>10000)
                throw new ArgumentException("Invalid NPC treatment receipt.");
            if(value.Kind==NpcTreatmentKind.HealingItem)
            {
                ulong uid;
                if(action.Kind!=NpcActionKind.UseHealingItem || value.SourceItemUid==0 ||
                    !ulong.TryParse(action.Target.ItemId,NumberStyles.None,CultureInfo.InvariantCulture,out uid) ||
                    uid!=value.SourceItemUid || action.Target.Quantity!=1 || value.RestStartedAt!=0 ||
                    value.RestDurationSeconds!=0)
                    throw new ArgumentException("Healing item receipt does not match its action.");
            }
            else if(value.Kind==NpcTreatmentKind.Rest)
            {
                if(action.Kind!=NpcActionKind.RestToHeal || value.SourceItemUid!=0 ||
                    !Number(value.RestStartedAt) || !Number(value.RestDurationSeconds) ||
                    value.RestStartedAt<0 || value.RestStartedAt<action.AcceptedAt ||
                    value.RestDurationSeconds<=0 || value.RestDurationSeconds!=action.RestDurationSeconds ||
                    value.RestStartedAt!=action.RestStartedAt ||
                    value.ObservedGameTime<value.RestStartedAt+value.RestDurationSeconds ||
                    action.FinishAt!=value.RestStartedAt+value.RestDurationSeconds)
                    throw new ArgumentException("Rest receipt does not match its action.");
            }
            else if(value.Kind==NpcTreatmentKind.MagicSelfCast)
            {
                NpcMagicHealingSpec spec=action.MagicHealing;
                if(action.Kind!=NpcActionKind.CastSpell || spec==null || value.SourceItemUid!=0 ||
                    value.RestStartedAt!=0 || value.RestDurationSeconds!=0 ||
                    value.SpellId!=spec.SpellId || value.EffectId!=spec.EffectId ||
                    value.MagickaCost!=spec.MagickaCost || value.MagickaBefore!=spec.MagickaBefore ||
                    value.MagickaAfter!=spec.MagickaBefore-spec.MagickaCost ||
                    value.ConditionBefore!=spec.HealthBefore || value.ConditionAfter!=value.HealthAfter ||
                    value.HealthBefore!=spec.HealthBefore)
                    throw new ArgumentException("Magic healing receipt does not match its action.");
            }
            else throw new ArgumentException("Unknown NPC treatment kind.");
        }

        internal static void ValidateMagicCare(NpcTreatmentReceipt value,NpcMagicCareTask task)
        {
            if(value==null || task==null || value.Kind!=NpcTreatmentKind.MagicSelfCast ||
                value.TransactionId!=task.Id+":treatment" ||
                !Number(value.ObservedGameTime) || value.ObservedGameTime<task.ReservedAt ||
                value.ObservedGameTime>task.UpdatedAt || value.SourceItemUid!=0 ||
                value.RestStartedAt!=0 || value.RestDurationSeconds!=0 ||
                value.SpellId!=task.SpellId || value.EffectId!=task.EffectId ||
                value.MagickaCost!=task.MagickaCost || value.MagickaBefore!=task.MagickaBefore ||
                value.MagickaAfter!=task.MagickaBefore-task.MagickaCost ||
                value.ConditionBefore!=task.ConditionBefore || value.ConditionAfter<0 ||
                (task.Need==NpcMagicCareNeed.Heal ?
                    value.ConditionAfter<=value.ConditionBefore ||
                    value.HealthBefore!=value.ConditionBefore || value.HealthAfter!=value.ConditionAfter :
                    value.ConditionAfter>=value.ConditionBefore ||
                    value.HealthBefore!=0 || value.HealthAfter!=0))
                throw new ArgumentException("Invalid observed magic-care receipt.");
        }

        private static bool Number(double value)
        {return !double.IsNaN(value) && !double.IsInfinity(value);}
    }

    /// <summary>Runtime-only result verified against one native health and inventory observation.</summary>
    internal sealed class NpcTreatmentNativeOutcome
    {
        internal ulong SourceItemUid;
        internal int HealthBefore,HealthAfter,CurrentMagicka;
        internal List<NpcInventoryItem> RemainingItems;

        private NpcTreatmentNativeOutcome() { }

        internal static NpcTreatmentNativeOutcome Verified(ulong sourceItemUid,int healthBefore,int healthAfter,
            int currentMagicka,IList<NpcInventoryItem> remainingItems)
        {
            if(remainingItems==null)return null;
            var copy=new List<NpcInventoryItem>();
            foreach(var item in remainingItems)
                copy.Add(item==null?null:new NpcInventoryItem {Uid=item.Uid,NativePayload=item.NativePayload});
            return new NpcTreatmentNativeOutcome {SourceItemUid=sourceItemUid,HealthBefore=healthBefore,
                HealthAfter=healthAfter,CurrentMagicka=currentMagicka,RemainingItems=copy};
        }
    }

    internal sealed class NpcTreatmentRestNativeOutcome
    {
        internal int HealthBefore,HealthAfter,CurrentMagicka;
        private NpcTreatmentRestNativeOutcome() { }
        internal static NpcTreatmentRestNativeOutcome Verified(int healthBefore,int healthAfter,int currentMagicka)
        {return new NpcTreatmentRestNativeOutcome {HealthBefore=healthBefore,HealthAfter=healthAfter,CurrentMagicka=currentMagicka};}
    }

    internal sealed class NpcTreatmentVitalsStage
    {
        internal NpcActorProgress Current,Proposed;
        internal int Index;
    }

    public sealed partial class WorldStateStore
    {
        /// <summary>
        /// Commits one native-verified owned healing item. The callback must stage native health/item
        /// mutation and roll it back if this method returns false; no native callback runs after commit.
        /// </summary>
        internal bool TryCompleteNpcHealingItemAction(string id,long expectedRevision,double now,
            NpcArchetype archetype,long expectedVitalsRevision,long expectedInventoryRevision,
            Func<NpcTreatmentNativeOutcome> applyNativeEffect)
        {
            int actionIndex=state.NpcActions.FindIndex(job=>job.Id==id);
            if(actionIndex<0 || applyNativeEffect==null)return false;
            var current=state.NpcActions[actionIndex];var resident=ResolveResident(current.ResidentId);
            ulong targetUid;
            if(current.Kind!=NpcActionKind.UseHealingItem || current.Phase!=NpcActionPhase.Executing ||
                current.Revision!=expectedRevision || expectedRevision==long.MaxValue ||
                current.TreatmentReceipt!=null || current.FollowTransition!=null || current.RoutineRecoveryTransition!=null ||
                !Finite(now) || now<current.UpdatedAt || now<current.AcceptedAt || now>=current.DeadlineAt ||
                resident==null || !resident.IsAlive || resident.AgeGroup!=ResidentAgeGroup.Adult || resident.IsQuestProtected ||
                !ulong.TryParse(current.Target.ItemId,NumberStyles.None,CultureInfo.InvariantCulture,out targetUid) ||
                targetUid==0 || current.Target.Quantity!=1 ||
                !NpcActionContract.CanTransition(current.Phase,NpcActionPhase.Completed,true))return false;

            var inventory=IndexedNpcInventory(current.ResidentId);
            if(inventory==null || inventory.Revision!=expectedInventoryRevision ||
                current.ExpectedInventoryRevision!=expectedInventoryRevision ||
                expectedInventoryRevision>=NpcInventoryLimits.MaximumRevision ||
                inventory.LastTransactionId==id+":treatment")return false;
            var inventoryIndex=state.NpcInventories.IndexOf(inventory);
            if(inventoryIndex<0 || !inventory.Items.Exists(item=>item.Uid==targetUid))return false;
            var vitals=ReadNpcVitals(current.ResidentId,archetype);
            if(!vitals.IsAlive || vitals.Revision!=expectedVitalsRevision ||
                expectedVitalsRevision>=NpcSkillProgression.MaximumRawExperience)return false;

            NpcTreatmentNativeOutcome outcome;
            try {outcome=applyNativeEffect();}
            catch(Exception) {return false;}
            if(outcome==null || outcome.SourceItemUid!=targetUid || outcome.HealthBefore!=vitals.CurrentHealth ||
                outcome.HealthAfter<=outcome.HealthBefore || outcome.HealthAfter>vitals.MaximumHealth ||
                outcome.CurrentMagicka!=vitals.CurrentMagicka)return false;
            NpcInventoryState nextInventory;
            if(!TryBuildTreatmentInventory(inventory,id+":treatment",targetUid,outcome.RemainingItems,out nextInventory))return false;
            NpcTreatmentVitalsStage vitalsStage;
            if(!TryStageTreatmentVitals(current.ResidentId,archetype,expectedVitalsRevision,
                outcome.HealthBefore,outcome.HealthAfter,outcome.CurrentMagicka,now,out vitalsStage))return false;

            if(actionIndex>=state.NpcActions.Count || !ReferenceEquals(state.NpcActions[actionIndex],current) ||
                !ReferenceEquals(IndexedNpcInventory(current.ResidentId),inventory) ||
                !ReferenceEquals(IndexedNpcProgress(current.ResidentId),vitalsStage.Current))return false;
            var receipt=NpcTreatmentReceipt.HealingItem(id+":treatment",targetUid,
                outcome.HealthBefore,outcome.HealthAfter,now);
            NpcActionJob completed;
            if(!NpcActionExecutor.Advance(current,expectedRevision,now,
                NpcActionObservation.TreatmentSuccess(id+":observed",receipt),out completed) ||
                completed.Phase!=NpcActionPhase.Completed)return false;

            CommitTreatmentInventory(inventoryIndex,inventory,nextInventory);
            CommitTreatmentVitals(current.ResidentId,vitalsStage);
            state.NpcActions[actionIndex]=completed;IndexNpcAction(completed);
            return true;
        }

        internal bool TryCompleteNpcRestAction(string id,long expectedRevision,double now,NpcArchetype archetype,
            long expectedVitalsRevision,Func<NpcTreatmentRestNativeOutcome> applyNativeEffect)
        {
            int actionIndex=state.NpcActions.FindIndex(job=>job.Id==id);
            if(actionIndex<0 || applyNativeEffect==null)return false;
            var current=state.NpcActions[actionIndex];var resident=ResolveResident(current.ResidentId);
            if(current.Kind!=NpcActionKind.RestToHeal || current.Phase!=NpcActionPhase.Executing ||
                current.Revision!=expectedRevision || expectedRevision==long.MaxValue || current.TreatmentReceipt!=null ||
                current.FollowTransition!=null || current.RoutineRecoveryTransition!=null || !Finite(now) ||
                now<current.UpdatedAt || now<current.FinishAt || now>=current.DeadlineAt ||
                expectedVitalsRevision!=current.ExpectedVitalsRevision ||
                current.RestStartedAt<current.AcceptedAt || current.FinishAt<=current.RestStartedAt ||
                current.FinishAt!=current.RestStartedAt+current.RestDurationSeconds ||
                resident==null || !resident.IsAlive || resident.AgeGroup!=ResidentAgeGroup.Adult || resident.IsQuestProtected ||
                !NpcActionContract.CanTransition(current.Phase,NpcActionPhase.Completed,true))return false;
            var vitals=ReadNpcVitals(current.ResidentId,archetype);
            if(!vitals.IsAlive || vitals.Revision!=expectedVitalsRevision ||
                expectedVitalsRevision>=NpcSkillProgression.MaximumRawExperience)return false;
            NpcTreatmentRestNativeOutcome outcome;
            try {outcome=applyNativeEffect();}
            catch(Exception) {return false;}
            if(outcome==null || outcome.HealthBefore!=vitals.CurrentHealth ||
                outcome.HealthAfter<=outcome.HealthBefore || outcome.HealthAfter>vitals.MaximumHealth ||
                outcome.CurrentMagicka<0 || outcome.CurrentMagicka>vitals.MaximumMagicka)return false;
            NpcTreatmentVitalsStage vitalsStage;
            if(!TryStageTreatmentVitals(current.ResidentId,archetype,expectedVitalsRevision,
                outcome.HealthBefore,outcome.HealthAfter,outcome.CurrentMagicka,now,out vitalsStage))return false;
            if(actionIndex>=state.NpcActions.Count || !ReferenceEquals(state.NpcActions[actionIndex],current) ||
                !ReferenceEquals(IndexedNpcProgress(current.ResidentId),vitalsStage.Current))return false;
            var receipt=NpcTreatmentReceipt.Rest(id+":treatment",outcome.HealthBefore,outcome.HealthAfter,
                now,current.RestStartedAt,current.RestDurationSeconds);
            NpcActionJob completed;
            if(!NpcActionExecutor.Advance(current,expectedRevision,now,
                NpcActionObservation.TreatmentSuccess(id+":observed",receipt),out completed) ||
                completed.Phase!=NpcActionPhase.Completed)return false;
            CommitTreatmentVitals(current.ResidentId,vitalsStage);
            state.NpcActions[actionIndex]=completed;IndexNpcAction(completed);
            return true;
        }

        private bool TryBuildTreatmentInventory(NpcInventoryState current,string transactionId,ulong targetUid,
            IList<NpcInventoryItem> remainingItems,out NpcInventoryState proposed)
        {
            proposed=null;
            if(current==null || remainingItems==null || remainingItems.Count>NpcInventoryLimits.MaximumItems ||
                current.Revision>=NpcInventoryLimits.MaximumRevision || string.IsNullOrWhiteSpace(transactionId) ||
                transactionId.Length>240)return false;
            var before=new Dictionary<ulong,string>();
            foreach(var item in current.Items)before[item.Uid]=item.NativePayload;
            if(!before.ContainsKey(targetUid))return false;
            var retained=new HashSet<ulong>();bool targetChangedOrConsumed=false;
            var copies=new List<NpcInventoryItem>();
            foreach(var item in remainingItems)
            {
                if(item==null || item.Uid==0 || !retained.Add(item.Uid) || string.IsNullOrWhiteSpace(item.NativePayload) ||
                    item.NativePayload.Length>NpcInventoryLimits.MaximumItemPayloadLength)return false;
                string previous;
                if(!before.TryGetValue(item.Uid,out previous))return false;
                if(item.Uid==targetUid)
                {
                    if(item.NativePayload==previous)return false;
                    targetChangedOrConsumed=true;
                }
                else if(item.NativePayload!=previous)return false;
                copies.Add(new NpcInventoryItem {Uid=item.Uid,NativePayload=item.NativePayload});
            }
            if(!retained.Contains(targetUid))targetChangedOrConsumed=true;
            if(!targetChangedOrConsumed)return false;
            foreach(var item in current.Items)if(item.Uid!=targetUid && !retained.Contains(item.Uid))return false;
            proposed=new NpcInventoryState {PersonId=current.PersonId,WalletGold=current.WalletGold,
                Revision=current.Revision+1,LastTransactionId=transactionId,Items=copies};
            try {ValidateNpcInventory(proposed,new HashSet<ulong>());}
            catch(ArgumentException) {proposed=null;return false;}
            return true;
        }

        private bool TryStageTreatmentVitals(string id,NpcArchetype archetype,long expectedRevision,int beforeHealth,
            int afterHealth,int afterMagicka,double now,out NpcTreatmentVitalsStage stage)
        {
            stage=null;
            if(!Finite(now) || now<0 || !Enum.IsDefined(typeof(NpcArchetype),archetype))return false;
            var current=IndexedNpcProgress(id);var view=ReadNpcVitals(id,archetype);
            if(!view.IsAlive || view.Revision!=expectedRevision || view.CurrentHealth!=beforeHealth ||
                afterHealth<=beforeHealth || afterHealth>view.MaximumHealth || afterMagicka<0 ||
                afterMagicka>view.MaximumMagicka || now<view.CapturedAt ||
                view.Revision>=NpcSkillProgression.MaximumRawExperience)return false;
            if(current!=null && current.Archetype!=archetype)return false;
            if(current!=null && current.HasPendingHealthEpisode &&
                (current.HealthEpisodeChangeCount==int.MaxValue || current.HealthEpisodeHealingTotal>long.MaxValue-(afterHealth-beforeHealth)))return false;
            long nextRevision=view.Revision+1;
            var proposed=current==null?new NpcActorProgress {PersonId=id,Archetype=archetype}:CopyNpcProgress(current);
            if(!proposed.HasPendingHealthEpisode)
            {
                proposed.HasPendingHealthEpisode=true;
                proposed.HealthEpisodeStartedAt=now;
                proposed.HealthEpisodeInitialHealth=beforeHealth;
                proposed.HealthEpisodeMaximumHealth=view.MaximumHealth;
                proposed.HealthEpisodeChangeCount=1;
                proposed.HealthEpisodeFirstVitalsRevision=nextRevision;
            }
            else proposed.HealthEpisodeChangeCount++;
            proposed.HealthEpisodeHealingTotal+=afterHealth-beforeHealth;
            proposed.HealthEpisodeFinalHealth=afterHealth;
            proposed.HealthEpisodeLastChangedAt=now;
            proposed.HealthEpisodeLastVitalsRevision=nextRevision;
            proposed.VitalsInitialized=true;proposed.CurrentHealth=afterHealth;proposed.CurrentMagicka=afterMagicka;
            proposed.VitalsCapturedAt=now;proposed.VitalsRevision=nextRevision;
            int index=current==null?-1:state.NpcActors.IndexOf(current);
            if(current!=null && index<0)return false;
            stage=new NpcTreatmentVitalsStage {Current=current,Proposed=proposed,Index=index};
            return true;
        }

        private void CommitTreatmentInventory(int index,NpcInventoryState current,NpcInventoryState proposed)
        {
            foreach(var item in current.Items)npcItemOwners.Remove(item.Uid);
            state.NpcInventories[index]=proposed;npcInventoryIndex[proposed.PersonId]=proposed;
            foreach(var item in proposed.Items)npcItemOwners.Add(item.Uid,proposed.PersonId);
        }

        private void CommitTreatmentVitals(string id,NpcTreatmentVitalsStage stage)
        {
            if(stage.Index<0)state.NpcActors.Add(stage.Proposed);
            else state.NpcActors[stage.Index]=stage.Proposed;
            npcActorIndex[id]=stage.Proposed;
        }
    }
}
