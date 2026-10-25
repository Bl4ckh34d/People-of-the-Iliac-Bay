using System;
using System.Collections.Generic;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    [Serializable]
    public sealed class NpcActionJob
    {
        public string Id, ProposalId, ResidentId;
        public NpcActionKind Kind;
        public NpcActionTarget Target;
        public NpcActionPhase Phase;
        public long Revision;
        public double AcceptedAt, DeadlineAt, UpdatedAt,FinishAt;
        public string FailureReason, CompletedEffectId;
        public string AcquiredItemId;
        public bool InteractionMemoryRecorded;
        public NpcInteractionProjectionDisposition InteractionProjectionDisposition;
        public string InteractionProjectionReason;
        public NpcInteractionPhysicalEvidence InteractionPhysicalEvidence;
        public long TreatmentOutcomeRevision;
        public NpcOperationAdmission Admission;
        // Local coordinates are qualified by a stable loaded space. No Unity references.
        public NpcActionTarget LastPosition;
        public NpcFollowTransition FollowTransition;
        public NpcPaidTravelHandoff PaidTravelHandoff;
        public NpcRoutineRecoveryTransition RoutineRecoveryTransition;
        public bool ReturningToRoutine;
        public long ExpectedInventoryRevision;
        // Rest pins its pre-rest vitals snapshot so a change during rest is stale.
        public long ExpectedVitalsRevision;
        public long ExpectedTargetInventoryRevision;
        public string ServiceCode;
        public int ServicePrice,ServiceStep;
        public NpcActionTransferReceipt ItemTransferReceipt;
        // Detached copy marks the exact native receipt after canonical inbox projection.
        // Legacy completed actions have a null marker and may be reconciled on drain.
        public NpcActionTransferReceipt ProjectedItemTransferReceipt;
        // Native treatment duration begins only after observed arrival at the bed.
        public double RestDurationSeconds,RestStartedAt;
        public NpcTreatmentReceipt TreatmentReceipt;
        public NpcMagicHealingSpec MagicHealing;
        public NpcCompanionCastSpec CompanionCast;
        public NpcBuildingTransitionReceipt BuildingTransitionReceipt;
    }

    /// <summary>Validated native ownership evidence for one completed player-to-NPC item transfer.</summary>
    [Serializable]
    public sealed class NpcActionTransferReceipt
    {
        public string TransactionId, RecipientId, CounterpartyId;
        public ulong SourceItemUid, MovedItemUid, RetainedDestinationItemUid;
        public int Quantity;
        public double ObservedGameTime;
        public NpcExperienceNativeValuationStatus NativeValuationStatus;
        public long NativeGoldValue;

        private NpcActionTransferReceipt() { }

        public static NpcActionTransferReceipt CreatePlayerToNpc(NpcActionJob job, string transactionId,
            ulong sourceItemUid, ulong movedItemUid, ulong retainedDestinationItemUid, int quantity,
            string recipientId, string counterpartyId, double observedGameTime,
            NpcExperienceNativeValuationStatus nativeValuationStatus, long nativeGoldValue)
        {
            Validate(job, transactionId, sourceItemUid, movedItemUid, retainedDestinationItemUid,
                quantity, recipientId, counterpartyId, observedGameTime, nativeValuationStatus, nativeGoldValue,
                beforeCommit: true);
            return new NpcActionTransferReceipt
            {
                TransactionId = transactionId,
                SourceItemUid = sourceItemUid,
                MovedItemUid = movedItemUid,
                RetainedDestinationItemUid = retainedDestinationItemUid,
                Quantity = quantity,
                RecipientId = recipientId,
                CounterpartyId = counterpartyId,
                ObservedGameTime = observedGameTime,
                NativeValuationStatus = nativeValuationStatus,
                NativeGoldValue = nativeGoldValue
            };
        }

        internal static NpcActionTransferReceipt CopyValidated(NpcActionTransferReceipt source, NpcActionJob job)
        {
            if (source == null) return null;
            Validate(job, source.TransactionId, source.SourceItemUid, source.MovedItemUid,
                source.RetainedDestinationItemUid, source.Quantity, source.RecipientId,
                source.CounterpartyId, source.ObservedGameTime, source.NativeValuationStatus,
                source.NativeGoldValue, beforeCommit: false);
            return CopyFields(source);
        }

        internal static NpcActionTransferReceipt CopyFields(NpcActionTransferReceipt source)
        {
            if (source == null) return null;
            return new NpcActionTransferReceipt
            {
                TransactionId = source.TransactionId,
                SourceItemUid = source.SourceItemUid,
                MovedItemUid = source.MovedItemUid,
                RetainedDestinationItemUid = source.RetainedDestinationItemUid,
                Quantity = source.Quantity,
                RecipientId = source.RecipientId,
                CounterpartyId = source.CounterpartyId,
                ObservedGameTime = source.ObservedGameTime,
                NativeValuationStatus = source.NativeValuationStatus,
                NativeGoldValue = source.NativeGoldValue
            };
        }

        internal static bool SameFields(NpcActionTransferReceipt first, NpcActionTransferReceipt second)
        {
            return first != null && second != null &&
                string.Equals(first.TransactionId, second.TransactionId, StringComparison.Ordinal) &&
                string.Equals(first.RecipientId, second.RecipientId, StringComparison.Ordinal) &&
                string.Equals(first.CounterpartyId, second.CounterpartyId, StringComparison.Ordinal) &&
                first.SourceItemUid == second.SourceItemUid && first.MovedItemUid == second.MovedItemUid &&
                first.RetainedDestinationItemUid == second.RetainedDestinationItemUid && first.Quantity == second.Quantity &&
                BitConverter.DoubleToInt64Bits(first.ObservedGameTime) == BitConverter.DoubleToInt64Bits(second.ObservedGameTime) &&
                first.NativeValuationStatus == second.NativeValuationStatus && first.NativeGoldValue == second.NativeGoldValue;
        }

        public NpcExperienceEvent ToNativeEvent(string actionId)
        {
            var impact = NpcExperienceImpactDetails.ForReceivedItemTransfer(RetainedDestinationItemUid,
                Quantity, NativeValuationStatus, NativeGoldValue);
            return NpcExperienceEvent.FromNativeOutcome(RecipientId, RecipientId, TransactionId,
                actionId + ":item-transfer", actionId + ":item-received", NpcExperienceEventKind.ItemReceived,
                ObservedGameTime, NpcResponsibleActorAttribution.Known, CounterpartyId, impact);
        }

        private static void Validate(NpcActionJob job, string transactionId, ulong sourceItemUid,
            ulong movedItemUid, ulong retainedDestinationItemUid, int quantity, string recipientId,
            string counterpartyId, double observedGameTime,
            NpcExperienceNativeValuationStatus nativeValuationStatus, long nativeGoldValue, bool beforeCommit)
        {
            ulong requestedUid;
            if (job == null || job.Kind != NpcActionKind.Transfer || job.Target == null ||
                beforeCommit && job.Phase != NpcActionPhase.Executing ||
                !beforeCommit && job.Phase != NpcActionPhase.Completed ||
                job.Target.TransferDirection != NpcActionTransferDirection.PlayerToNpc ||
                job.Target.Kind != NpcActionTargetKind.Person || job.Target.PersonId != "player" ||
                !ulong.TryParse(job.Target.ItemId, NumberStyles.None, CultureInfo.InvariantCulture, out requestedUid) ||
                requestedUid != sourceItemUid || sourceItemUid == 0 || movedItemUid == 0 || retainedDestinationItemUid == 0 ||
                string.IsNullOrWhiteSpace(job.Id) || transactionId != job.Id + ":transfer" ||
                string.IsNullOrWhiteSpace(recipientId) || recipientId != job.ResidentId || counterpartyId != "player" ||
                quantity < 1 || quantity > 1000000 || quantity != job.Target.Quantity ||
                double.IsNaN(observedGameTime) || double.IsInfinity(observedGameTime) ||
                observedGameTime < job.AcceptedAt || observedGameTime >= job.DeadlineAt ||
                beforeCommit && observedGameTime < job.UpdatedAt ||
                !beforeCommit && observedGameTime > job.UpdatedAt ||
                (nativeValuationStatus != NpcExperienceNativeValuationStatus.Unknown &&
                 nativeValuationStatus != NpcExperienceNativeValuationStatus.Known) || nativeGoldValue < 0 ||
                nativeGoldValue > 1000000000000L ||
                nativeValuationStatus == NpcExperienceNativeValuationStatus.Unknown && nativeGoldValue != 0)
                throw new ArgumentException("Invalid player-to-NPC item transfer receipt.");
        }
    }

    internal sealed class NpcActionEffectResult
    {
        public bool Succeeded { get; private set; }
        public NpcActionTransferReceipt TransferReceipt { get; private set; }
        private NpcActionEffectResult() { }
        internal static NpcActionEffectResult Failure() { return new NpcActionEffectResult(); }
        internal static NpcActionEffectResult Success(NpcActionTransferReceipt receipt)
        { return new NpcActionEffectResult { Succeeded = true, TransferReceipt = NpcActionTransferReceipt.CopyFields(receipt) }; }
    }

    public sealed class NpcActionObservation
    {
        internal NpcActionPhase Phase;
        internal string Reason, EffectId;
        internal bool Confirmed;
        internal NpcTreatmentReceipt TreatmentReceipt;
        internal NpcBuildingTransitionReceipt BuildingTransitionReceipt;
        private NpcActionObservation() { }
        public static NpcActionObservation Approach() { return new NpcActionObservation {Phase=NpcActionPhase.Approaching}; }
        public static NpcActionObservation Arrival() { return new NpcActionObservation {Phase=NpcActionPhase.Executing}; }
        public static NpcActionObservation Success(string effectId) { return new NpcActionObservation {Phase=NpcActionPhase.Completed,Confirmed=true,EffectId=effectId}; }
        internal static NpcActionObservation TreatmentSuccess(string effectId,NpcTreatmentReceipt receipt)
        { return new NpcActionObservation {Phase=NpcActionPhase.Completed,Confirmed=true,EffectId=effectId,TreatmentReceipt=NpcTreatmentReceipt.CopyFields(receipt)}; }
        public static NpcActionObservation BuildingTransitionSuccess(NpcBuildingTransitionReceipt receipt)
        { return new NpcActionObservation {Phase=NpcActionPhase.Completed,Confirmed=true,
            EffectId=receipt==null?null:receipt.ActionId+":observed",
            BuildingTransitionReceipt=NpcBuildingTransitionReceipt.CopyFields(receipt)}; }
        public static NpcActionObservation Interrupted(string reason) { return new NpcActionObservation {Phase=NpcActionPhase.Failed,Reason=reason}; }
        public static NpcActionObservation Cancelled(string reason) { return new NpcActionObservation {Phase=NpcActionPhase.Cancelled,Reason=reason}; }
    }

    public static class NpcActionExecutor
    {
        public static bool IsTerminal(NpcActionJob job) {return job!=null && job.Phase>=NpcActionPhase.Completed;}
        public static bool Advance(NpcActionJob current,long expectedRevision,double now,NpcActionObservation observation,out NpcActionJob updated)
        {
            updated=null;
            if(current==null || observation==null || !Number(now) || now<current.UpdatedAt || current.Revision!=expectedRevision ||
                current.Revision==long.MaxValue || IsTerminal(current)) return false;
            var phase=observation.Phase;var reason=observation.Reason;
            if(now>=current.DeadlineAt) {phase=NpcActionPhase.Failed;reason="The action deadline expired.";}
            if(current.FollowTransition!=null && phase!=NpcActionPhase.Failed && phase!=NpcActionPhase.Cancelled)return false;
            if(current.Kind==NpcActionKind.RestToHeal && current.FinishAt>0 && phase==NpcActionPhase.Approaching)return false;
            if(!NpcActionContract.CanTransition(current.Phase,phase,observation.Confirmed)) return false;
            if(phase==NpcActionPhase.Completed &&
                ((current.Kind==NpcActionKind.UseHealingItem || current.Kind==NpcActionKind.RestToHeal || current.Kind==NpcActionKind.CastSpell && current.MagicHealing!=null) && observation.TreatmentReceipt==null ||
                 current.Kind!=NpcActionKind.UseHealingItem && current.Kind!=NpcActionKind.RestToHeal && current.Kind!=NpcActionKind.CastSpell && observation.TreatmentReceipt!=null))return false;
            bool buildingTransition=current.Kind==NpcActionKind.Enter || current.Kind==NpcActionKind.Exit;
            if(phase==NpcActionPhase.Completed &&
                (buildingTransition != (observation.BuildingTransitionReceipt!=null) ||
                 buildingTransition && (observation.EffectId!=current.Id+":observed" ||
                     observation.BuildingTransitionReceipt.ObservedGameTime!=now)))return false;
            if(phase==NpcActionPhase.Completed && current.FinishAt>0 && now<current.FinishAt) return false;
            if(phase==NpcActionPhase.Completed && !Identifier(observation.EffectId)) return false;
            if((phase==NpcActionPhase.Failed || phase==NpcActionPhase.Cancelled) && !Identifier(reason,1000)) return false;
            updated=Copy(current);updated.Phase=phase;updated.Revision++;updated.UpdatedAt=now;
            updated.FailureReason=reason;updated.CompletedEffectId=phase==NpcActionPhase.Completed?observation.EffectId:null;
            if(phase==NpcActionPhase.Executing && current.Kind==NpcActionKind.RestToHeal && current.Phase!=NpcActionPhase.Executing)
            {
                if(current.RestDurationSeconds<=0 || !Number(current.RestDurationSeconds) || now+current.RestDurationSeconds>=current.DeadlineAt)return false;
                updated.RestStartedAt=now;updated.FinishAt=now+current.RestDurationSeconds;
            }
            if(phase==NpcActionPhase.Completed)updated.TreatmentReceipt=NpcTreatmentReceipt.CopyFields(observation.TreatmentReceipt);
            if(phase==NpcActionPhase.Completed)updated.BuildingTransitionReceipt=NpcBuildingTransitionReceipt.CopyFields(observation.BuildingTransitionReceipt);
            if(updated.FollowTransition!=null && IsTerminal(updated))
            {updated.LastPosition=updated.FollowTransition.SourcePosition(now);updated.FollowTransition=null;}
            Validate(updated);return true;
        }
        public static void Validate(NpcActionJob value)
        {
            ulong itemUid=0;
            if(value==null || !Identifier(value.Id,220) || !Identifier(value.ResidentId) || !Identifier(value.ProposalId) ||
                (int)value.Kind<0 || (int)value.Kind>(int)NpcActionKind.Defend || (int)value.Phase<1 || (int)value.Phase>6 || value.Revision<1 ||
                !Number(value.AcceptedAt) || !Number(value.DeadlineAt) || !Number(value.UpdatedAt) || value.AcceptedAt<0 ||
                value.DeadlineAt<=value.AcceptedAt ||
                (value.DeadlineAt-value.AcceptedAt>86400 && !NpcPaidCompanionAction.IsPaidFollowJob(value)) ||
                value.UpdatedAt<value.AcceptedAt ||
                value.ExpectedInventoryRevision<0 || value.ExpectedTargetInventoryRevision<0 || !Number(value.FinishAt) || value.FinishAt!=0 && (value.FinishAt<value.AcceptedAt || value.FinishAt>=value.DeadlineAt)) throw new ArgumentException("Invalid NPC action job.");
            if(value.ExpectedVitalsRevision<0 || value.ExpectedVitalsRevision>NpcSkillProgression.MaximumRawExperience ||
                value.Kind!=NpcActionKind.RestToHeal && value.ExpectedVitalsRevision!=0)
                throw new ArgumentException("Vitals revision binding is only valid for rest treatment actions.");
            NpcActionContract.ValidateTarget(value.Target);
            if(value.CompanionCast!=null)
            {
                NpcCompanionCastSpec.CopyValidated(value.CompanionCast);
                if(value.Kind!=NpcActionKind.CastSpell || value.MagicHealing!=null ||
                    value.Target.Kind!=NpcActionTargetKind.Person || value.Target.PersonId!=value.CompanionCast.TargetId ||
                    value.CompanionCast.Delivery==NpcCompanionCastDelivery.Self && value.CompanionCast.TargetId!=value.ResidentId ||
                    value.FinishAt!=0 || value.ServiceCode!=null || value.RestDurationSeconds!=0 ||
                    value.TreatmentReceipt!=null)
                    throw new ArgumentException("Invalid companion spell action.");
            }
            if(value.Kind==NpcActionKind.CastSpell)
            {
                if(value.MagicHealing!=null &&
                    (value.Target.Kind!=NpcActionTargetKind.IndoorPoint && value.Target.Kind!=NpcActionTargetKind.WorldPoint ||
                    value.Target.PersonId!=null || value.Target.ContainerId!=null || value.Target.ItemId!=null ||
                    value.Target.Quantity!=1 || value.Target.QuotedGold!=0 || value.FinishAt!=0 ||
                    value.RestDurationSeconds!=0 || value.RestStartedAt!=0 ||
                    value.ExpectedInventoryRevision!=0 || value.ExpectedTargetInventoryRevision!=0 ||
                    value.ServiceCode!=null || value.ServicePrice!=0 || value.ServiceStep!=0))
                    throw new ArgumentException("Invalid self-healing spell action.");
                if(value.MagicHealing!=null)NpcMagicHealingSpec.CopyValidated(value.MagicHealing);
            }
            else if(value.MagicHealing!=null)
                throw new ArgumentException("Magic healing metadata requires a cast action.");
            if(value.Kind==NpcActionKind.Enter || value.Kind==NpcActionKind.Exit)
            {
                if(value.Target.Kind!=NpcActionTargetKind.Building || string.IsNullOrWhiteSpace(value.Target.DoorId) ||
                    value.Target.DoorId.Length>240 || string.IsNullOrWhiteSpace(value.Target.WorldSpaceId) ||
                    value.Target.WorldSpaceId.Length>240 || string.IsNullOrWhiteSpace(value.Target.DestinationSpaceId) ||
                    value.Target.DestinationSpaceId.Length>240 || value.Target.WorldSpaceId==value.Target.DestinationSpaceId ||
                    value.Target.PersonId!=null || value.Target.ContainerId!=null || value.Target.ItemId!=null ||
                    value.Target.Quantity!=1 || value.Target.QuotedGold!=0 || value.FinishAt!=0 ||
                    value.ServiceCode!=null || value.ServicePrice!=0 || value.ServiceStep!=0)
                    throw new ArgumentException("Invalid exact building transition target.");
            }
            else if(value.Target.DoorId!=null || value.Target.DestinationSpaceId!=null)
            {
                // An offscreen Wait may retain an Enter's exact native door as
                // logical building identity. It never supplies an invented room
                // point; the transition receipt remains attached to Enter only.
                if(value.Kind!=NpcActionKind.Wait || value.Target.Kind!=NpcActionTargetKind.Building ||
                    string.IsNullOrWhiteSpace(value.Target.DoorId) || string.IsNullOrWhiteSpace(value.Target.WorldSpaceId) ||
                    string.IsNullOrWhiteSpace(value.Target.DestinationSpaceId) || value.Target.WorldSpaceId==value.Target.DestinationSpaceId ||
                    value.Target.PersonId!=null || value.Target.ContainerId!=null || value.Target.ItemId!=null ||
                    value.Target.Quantity!=1 || value.Target.QuotedGold!=0)
                    throw new ArgumentException("Door transition fields require Enter, Exit, or exact building Wait.");
            }
            if(value.Kind==NpcActionKind.UseHealingItem)
            {
                ulong healingUid;
                if((value.Target.Kind!=NpcActionTargetKind.IndoorPoint && value.Target.Kind!=NpcActionTargetKind.WorldPoint) ||
                    !ulong.TryParse(value.Target.ItemId,NumberStyles.None,CultureInfo.InvariantCulture,out healingUid) || healingUid==0 ||
                    value.Target.Quantity!=1 || value.Target.PersonId!=null || value.Target.ContainerId!=null || value.Target.QuotedGold!=0 ||
                    value.ExpectedInventoryRevision<0 || value.RestDurationSeconds!=0 || value.RestStartedAt!=0 || value.FinishAt!=0)
                    throw new ArgumentException("Invalid healing item action target.");
            }
            if(value.Kind==NpcActionKind.RestToHeal)
            {
                bool point=value.Target.Kind==NpcActionTargetKind.IndoorPoint || value.Target.Kind==NpcActionTargetKind.WorldPoint;
                if(!point || string.IsNullOrWhiteSpace(value.Target.ContainerId) || value.Target.ContainerId.Length>240 ||
                    value.Target.ItemId!=null || value.Target.PersonId!=null || value.Target.Quantity!=1 || value.Target.QuotedGold!=0 ||
                    !Number(value.RestDurationSeconds) || value.RestDurationSeconds<=0 || value.RestDurationSeconds>86400 ||
                    !Number(value.RestStartedAt) || value.RestStartedAt<0 ||
                    value.FinishAt!=0 && (value.Phase<NpcActionPhase.Executing ||
                        value.FinishAt!=value.RestStartedAt+value.RestDurationSeconds ||
                        value.RestStartedAt<value.AcceptedAt || value.FinishAt>=value.DeadlineAt) ||
                    value.FinishAt==0 && value.RestStartedAt!=0)
                    throw new ArgumentException("Invalid rest treatment target or duration.");
            }
            else if(value.RestDurationSeconds!=0 || value.RestStartedAt!=0)
                throw new ArgumentException("Rest duration is only valid for rest treatment actions.");
            if(value.Kind==NpcActionKind.Transfer)
            {
                bool containerDeposit=value.Target.TransferDirection==NpcActionTransferDirection.NpcToContainer;
                bool homeContainerRetrieval=value.Target.TransferDirection==NpcActionTransferDirection.HomeContainerToNpc;
                if(!ulong.TryParse(value.Target.ItemId,NumberStyles.None,CultureInfo.InvariantCulture,out itemUid) || itemUid==0 ||
                    value.Target.Quantity<1 || value.Target.Quantity>1000000 ||
                    (int)value.Target.TransferDirection<0 || (int)value.Target.TransferDirection>3 ||
                    containerDeposit && (value.Target.Kind!=NpcActionTargetKind.IndoorPoint || value.Target.PersonId!=null || value.ExpectedTargetInventoryRevision!=0) ||
                    homeContainerRetrieval && (value.Target.Kind!=NpcActionTargetKind.IndoorPoint || value.Target.PersonId!=null ||
                        value.ExpectedInventoryRevision!=0 || value.ExpectedTargetInventoryRevision<0) ||
                    !containerDeposit && !homeContainerRetrieval && (value.Target.Kind!=NpcActionTargetKind.Person || value.Target.PersonId==value.ResidentId ||
                        value.Target.TransferDirection==NpcActionTransferDirection.PlayerToNpc && value.Target.PersonId!="player" ||
                        value.Target.TransferDirection==NpcActionTransferDirection.PlayerToNpc && value.ExpectedTargetInventoryRevision!=0 ||
                        value.Target.TransferDirection==NpcActionTransferDirection.NpcToCounterparty && value.Target.PersonId=="player" && value.ExpectedTargetInventoryRevision!=0))
                    throw new ArgumentException("Invalid NPC item transfer target or quantity.");
            }
            if(value.Kind!=NpcActionKind.Transfer && value.Target.TransferDirection!=NpcActionTransferDirection.NpcToCounterparty)
                throw new ArgumentException("Transfer direction is only valid for item transfer actions.");
            if(value.LastPosition!=null)
            {
                NpcActionContract.ValidateTarget(value.LastPosition);
                if(value.LastPosition.Kind!=NpcActionTargetKind.IndoorPoint && value.LastPosition.Kind!=NpcActionTargetKind.WorldPoint)
                    throw new ArgumentException("Action position must identify a stable spatial point.");
            }
            if(value.Phase==NpcActionPhase.Completed && !Identifier(value.CompletedEffectId) ||
                value.Phase!=NpcActionPhase.Completed && value.CompletedEffectId!=null ||
                (value.Phase==NpcActionPhase.Failed || value.Phase==NpcActionPhase.Cancelled) && !Identifier(value.FailureReason,1000) ||
                value.ReturningToRoutine && (!IsTerminal(value) || value.LastPosition==null))
                throw new ArgumentException("Action outcome is inconsistent with observed progress.");
            if(value.InteractionMemoryRecorded && (value.Kind!=NpcActionKind.Interact || value.Phase!=NpcActionPhase.Completed ||
                value.Target.Kind!=NpcActionTargetKind.Person || value.Target.PersonId==value.ResidentId || value.Target.PersonId=="player" ||
                value.CompletedEffectId!=value.Id+":observed"))
                throw new ArgumentException("NPC interaction memory receipt is inconsistent with its confirmed action.");
            if((int)value.InteractionProjectionDisposition<0 || (int)value.InteractionProjectionDisposition>2 ||
                (value.Kind!=NpcActionKind.Interact && (value.InteractionProjectionDisposition!=NpcInteractionProjectionDisposition.Pending ||
                    value.InteractionProjectionReason!=null || value.InteractionPhysicalEvidence!=null)) ||
                (value.InteractionProjectionDisposition==NpcInteractionProjectionDisposition.Pending &&
                    (value.InteractionProjectionReason!=null || value.InteractionPhysicalEvidence!=null)) ||
                (value.InteractionProjectionDisposition==NpcInteractionProjectionDisposition.Recorded &&
                    (!value.InteractionMemoryRecorded || value.Phase!=NpcActionPhase.Completed ||
                     value.CompletedEffectId!=value.Id+":observed" || value.InteractionProjectionReason!=null)) ||
                (value.InteractionProjectionDisposition==NpcInteractionProjectionDisposition.Unavailable &&
                    (value.InteractionMemoryRecorded || !IsTerminal(value) || value.InteractionPhysicalEvidence!=null ||
                     string.IsNullOrWhiteSpace(value.InteractionProjectionReason) || value.InteractionProjectionReason.Length>240)))
                throw new ArgumentException("Invalid NPC interaction projection disposition.");
            if(value.InteractionPhysicalEvidence!=null)
            {
                if(value.InteractionProjectionDisposition!=NpcInteractionProjectionDisposition.Recorded ||
                    !NpcInteractionPhysicalEvidence.ValidForCompletedAction(value.InteractionPhysicalEvidence,value))
                    throw new ArgumentException("Interaction physical evidence differs from its completed action.");
            }
            if(value.Phase==NpcActionPhase.Completed && (value.Kind==NpcActionKind.UseHealingItem || value.Kind==NpcActionKind.RestToHeal || value.Kind==NpcActionKind.CastSpell && value.MagicHealing!=null) && value.TreatmentReceipt==null ||
                value.Phase!=NpcActionPhase.Completed && value.TreatmentReceipt!=null)
                throw new ArgumentException("Treatment action outcome requires its typed receipt.");
            if(value.TreatmentReceipt!=null)
            {
                if(value.CompletedEffectId!=value.Id+":observed")throw new ArgumentException("Treatment receipt requires a completed native action.");
                NpcTreatmentReceipt.CopyValidated(value.TreatmentReceipt,value);
            }
            if(value.Kind==NpcActionKind.Enter || value.Kind==NpcActionKind.Exit)
            {
                if(value.Phase==NpcActionPhase.Completed && value.BuildingTransitionReceipt==null ||
                    value.Phase!=NpcActionPhase.Completed && value.BuildingTransitionReceipt!=null)
                    throw new ArgumentException("Building transition outcome requires its native receipt.");
                if(value.BuildingTransitionReceipt!=null)
                    NpcBuildingTransitionReceipt.CopyValidated(value.BuildingTransitionReceipt,value);
            }
            else if(value.BuildingTransitionReceipt!=null)
                throw new ArgumentException("Building transition receipt requires Enter or Exit.");
            if(value.TreatmentOutcomeRevision<0 || value.TreatmentOutcomeRevision>0 &&
                (value.Phase!=NpcActionPhase.Completed ||
                 (value.Kind!=NpcActionKind.UseHealingItem && value.Kind!=NpcActionKind.RestToHeal && value.Kind!=NpcActionKind.CastSpell) ||
                 value.TreatmentReceipt==null || value.CompletedEffectId!=value.Id+":observed"))
                throw new ArgumentException("Treatment projection marker requires its completed native receipt.");
            if(value.Admission!=null)NpcOperationAdmission.CopyValidated(value.Admission);
            if (value.ItemTransferReceipt != null)
            {
                if (value.Phase != NpcActionPhase.Completed || value.CompletedEffectId != value.Id + ":observed")
                    throw new ArgumentException("Item transfer receipt requires a completed action.");
                NpcActionTransferReceipt.CopyValidated(value.ItemTransferReceipt, value);
            }
            if (value.ProjectedItemTransferReceipt != null)
            {
                if (value.ItemTransferReceipt == null ||
                    !NpcActionTransferReceipt.SameFields(value.ProjectedItemTransferReceipt, value.ItemTransferReceipt))
                    throw new ArgumentException("Projected item transfer marker differs from its completed receipt.");
                NpcActionTransferReceipt.CopyValidated(value.ProjectedItemTransferReceipt, value);
            }
            NpcShopCommerce.ValidateService(value);
            ulong acquired;
            if(value.AcquiredItemId!=null)
            {
                bool purchaseResult=value.Kind==NpcActionKind.Purchase && value.ServiceCode==NpcShopCommerce.ServiceCode;
                bool homeRetrievalResult=value.Kind==NpcActionKind.Transfer && value.ServiceCode==null &&
                    value.Target.TransferDirection==NpcActionTransferDirection.HomeContainerToNpc && value.Target.ItemId==value.AcquiredItemId;
                if(value.Phase!=NpcActionPhase.Completed || !purchaseResult && !homeRetrievalResult ||
                    !ulong.TryParse(value.AcquiredItemId,NumberStyles.None,CultureInfo.InvariantCulture,out acquired) || acquired==0)
                    throw new ArgumentException("Invalid confirmed acquired item.");
            }
            NpcFollowTransition.Validate(value);
            NpcPaidTravelHandoff.Validate(value);
            NpcRoutineRecoveryTransition.Validate(value);
        }
        public static NpcActionJob Copy(NpcActionJob value)
        {
            return new NpcActionJob {Id=value.Id,ProposalId=value.ProposalId,ResidentId=value.ResidentId,Kind=value.Kind,
                Target=CopyTarget(value.Target),Phase=value.Phase,Revision=value.Revision,AcceptedAt=value.AcceptedAt,
                DeadlineAt=value.DeadlineAt,UpdatedAt=value.UpdatedAt,FinishAt=value.FinishAt,FailureReason=value.FailureReason,CompletedEffectId=value.CompletedEffectId,
                LastPosition=CopyTarget(value.LastPosition),FollowTransition=NpcFollowTransition.Copy(value.FollowTransition),
                PaidTravelHandoff=NpcPaidTravelHandoff.Copy(value.PaidTravelHandoff),
                RoutineRecoveryTransition=NpcRoutineRecoveryTransition.Copy(value.RoutineRecoveryTransition),ReturningToRoutine=value.ReturningToRoutine,InteractionMemoryRecorded=value.InteractionMemoryRecorded,ExpectedInventoryRevision=value.ExpectedInventoryRevision,
                InteractionProjectionDisposition=value.InteractionProjectionDisposition,
                InteractionProjectionReason=value.InteractionProjectionReason,
                InteractionPhysicalEvidence=NpcInteractionPhysicalEvidence.CopyFields(value.InteractionPhysicalEvidence),
                TreatmentOutcomeRevision=value.TreatmentOutcomeRevision,
                Admission=value.Admission==null?null:NpcOperationAdmission.CopyValidated(value.Admission),
                ExpectedVitalsRevision=value.ExpectedVitalsRevision,
                ExpectedTargetInventoryRevision=value.ExpectedTargetInventoryRevision,
                ServiceCode=value.ServiceCode,ServicePrice=value.ServicePrice,ServiceStep=value.ServiceStep,AcquiredItemId=value.AcquiredItemId,
                ItemTransferReceipt=NpcActionTransferReceipt.CopyValidated(value.ItemTransferReceipt,value),
                ProjectedItemTransferReceipt=NpcActionTransferReceipt.CopyValidated(value.ProjectedItemTransferReceipt,value),
                RestDurationSeconds=value.RestDurationSeconds,RestStartedAt=value.RestStartedAt,
                TreatmentReceipt=NpcTreatmentReceipt.CopyValidated(value.TreatmentReceipt,value),
                MagicHealing=value.MagicHealing==null?null:NpcMagicHealingSpec.CopyValidated(value.MagicHealing),
                CompanionCast=value.CompanionCast==null?null:NpcCompanionCastSpec.CopyValidated(value.CompanionCast),
                BuildingTransitionReceipt=NpcBuildingTransitionReceipt.CopyValidated(value.BuildingTransitionReceipt,value)};
        }
        public static NpcActionTarget CopyTarget(NpcActionTarget value)
        {
            return value==null?null:new NpcActionTarget {Kind=value.Kind,PersonId=value.PersonId,ContainerId=value.ContainerId,ItemId=value.ItemId,
                WorldSpaceId=value.WorldSpaceId,DoorId=value.DoorId,DestinationSpaceId=value.DestinationSpaceId,
                Quantity=value.Quantity,QuotedGold=value.QuotedGold,TransferDirection=value.TransferDirection,
                SettlementId=value.SettlementId,BuildingKey=value.BuildingKey,X=value.X,Y=value.Y,Z=value.Z};
        }
        private static bool Number(double value) {return !double.IsNaN(value) && !double.IsInfinity(value);}
        private static bool Identifier(string value,int maximum=240) {return !string.IsNullOrWhiteSpace(value) && value.Length<=maximum;}
    }

    public sealed partial class WorldStateStore
    {
        private WorldState npcActionIndexOwner;
        private Dictionary<string,NpcActionJob> npcActionsById,npcActionsByResident;
        private void EnsureNpcActionIndex()
        {
            if(npcActionIndexOwner==state) return;
            npcActionsById=new Dictionary<string,NpcActionJob>(StringComparer.Ordinal);
            npcActionsByResident=new Dictionary<string,NpcActionJob>(StringComparer.Ordinal);
            foreach(var job in state.NpcActions)
            {npcActionsById.Add(job.Id,job);if(!NpcActionExecutor.IsTerminal(job) || job.ReturningToRoutine) npcActionsByResident.Add(job.ResidentId,job);}
            npcActionIndexOwner=state;
        }
        private void IndexNpcAction(NpcActionJob job)
        {
            EnsureNpcActionIndex();npcActionsById[job.Id]=job;
            if(!NpcActionExecutor.IsTerminal(job) || job.ReturningToRoutine) npcActionsByResident[job.ResidentId]=job;
            else
            {
                NpcActionJob active;
                if(npcActionsByResident.TryGetValue(job.ResidentId,out active) && active.Id==job.Id) npcActionsByResident.Remove(job.ResidentId);
            }
        }
        public NpcActionJob ReadNpcAction(string id)
        {EnsureNpcActionIndex();NpcActionJob job;return id!=null && npcActionsById.TryGetValue(id,out job)?NpcActionExecutor.Copy(job):null;}
        public NpcActionJob ActiveNpcAction(string residentId)
        {
            EnsureNpcActionIndex();NpcActionJob job;
            return residentId!=null && npcActionsByResident.TryGetValue(residentId,out job)?NpcActionExecutor.Copy(job):null;
        }
        public NpcActionJob LatestNpcAction(string residentId)
        {
            for(int i=state.NpcActions.Count-1;i>=0;i--) if(state.NpcActions[i].ResidentId==residentId) return NpcActionExecutor.Copy(state.NpcActions[i]);
            return null;
        }
        public List<NpcActionJob> PendingNpcActions()
        {
            var result=new List<NpcActionJob>();
            foreach(var job in state.NpcActions) if(!NpcActionExecutor.IsTerminal(job) || job.ReturningToRoutine) result.Add(NpcActionExecutor.Copy(job));
            return result;
        }
        public bool TryAcceptNpcAction(NpcActionJob proposed,bool adapterAvailable,bool consent,bool scheduleCompatible,out string reason)
        {return TryAcceptNpcActionCore(proposed,adapterAvailable,consent,scheduleCompatible,null,out reason);}

        internal bool TryAcceptNpcActionClassified(NpcActionJob proposed,bool adapterAvailable,bool consent,
            bool scheduleCompatible,NpcOperationAdmission expectedSource,out string reason)
        {
            reason="The action source is no longer current.";
            if(expectedSource==null)return false;
            return TryAcceptNpcActionCore(proposed,adapterAvailable,consent,scheduleCompatible,expectedSource,out reason);
        }

        private bool TryAcceptNpcActionCore(NpcActionJob proposed,bool adapterAvailable,bool consent,
            bool scheduleCompatible,NpcOperationAdmission expectedSource,out string reason)
        {
            reason="Invalid action proposal.";
            try {NpcActionExecutor.Validate(proposed);} catch(ArgumentException) {return false;}
            if(expectedSource==null || !ActionAdmissionMatchesCurrentSource(proposed,expectedSource))
            {reason="The action source is no longer current.";return false;}
            if(proposed.Kind==NpcActionKind.CastSpell &&
                !(proposed.MagicHealing!=null && expectedSource.Kind==NpcOperationAdmissionKind.TaskStep) &&
                !(proposed.CompanionCast!=null && expectedSource.Kind==NpcOperationAdmissionKind.Conversation))
            {reason="Spells require a bound health-care task or an agreed known spell.";return false;}
            if(proposed.Phase!=NpcActionPhase.Accepted || proposed.Revision!=1 || proposed.ReturningToRoutine || proposed.UpdatedAt!=proposed.AcceptedAt || proposed.ServiceStep!=0 || proposed.FollowTransition!=null) return false;
            var resident=ResolveResident(proposed.ResidentId);
            if(resident==null) {reason="The person has no managed local routine.";return false;}
            if(!NpcActionContract.CanAccept(resident.IsAlive,resident.IsQuestProtected,adapterAvailable,consent,scheduleCompatible,out reason)) return false;
            if(ActiveNpcTask(proposed.ResidentId)!=null && dispatchingNpcTaskActionId!=proposed.Id)
            {reason="The NPC is already carrying out an agreed task.";return false;}
            var existing=state.NpcActions.Find(j=>j.Id==proposed.Id || j.ProposalId==proposed.ProposalId && j.ResidentId==proposed.ResidentId);
            if(existing!=null) {reason="This dialogue proposal was already handled.";return false;}
            var previous=ActiveNpcAction(resident.Id);
            if(previous!=null && !NpcActionExecutor.IsTerminal(previous)) {reason="The NPC is already carrying out another action.";return false;}
            // A care plan already admitted and bound to the resident owns its steps even
            // when an older appointment overlaps. Ordinary action proposals still yield.
            bool boundCareStep=IsBoundNpcHealthCareActionStep(proposed,expectedSource);
            if(!boundCareStep)
                foreach(var booking in state.Appointments)
                    if(booking.ResidentId==resident.Id && !booking.OutcomeRecorded && booking.Status!="cancelled" &&
                        proposed.AcceptedAt<AppointmentTiming.ReservedUntil(booking) && booking.StartsAt<proposed.DeadlineAt)
                    {reason="An accepted appointment takes priority.";return false;}
            foreach(var trip in state.ResidentTrips)
                if(trip.ResidentId==resident.Id && proposed.AcceptedAt<trip.HomeAtFor(proposed.AcceptedAt,IndexedObservedVisit(resident.Id)) && trip.DepartsAt<proposed.DeadlineAt)
                {reason="An existing journey takes priority.";return false;}
            if(previous!=null && previous.Revision==long.MaxValue)
            {reason="The previous action revision cannot advance.";return false;}
            if(!TryMakeNpcOperationCapacity(NpcOperationalHistoryPressureTarget.Actions,proposed.AcceptedAt,out reason))return false;
            if(previous!=null)
            {
                // A new verified route can take over a stranded terminal rejoin.
                int index=state.NpcActions.FindIndex(j=>j.Id==previous.Id);
                var released=NpcActionExecutor.Copy(state.NpcActions[index]);released.ReturningToRoutine=false;
                released.Revision++;state.NpcActions[index]=released;IndexNpcAction(released);
            }
            var accepted=NpcActionExecutor.Copy(proposed);state.NpcActions.Add(accepted);IndexNpcAction(accepted);reason="accepted";return true;
        }
        public bool ObserveNpcAction(string id,long expectedRevision,double now,NpcActionObservation observation)
        {
            int index=state.NpcActions.FindIndex(j=>j.Id==id);NpcActionJob updated;
            if(index>=0 && (state.NpcActions[index].Kind==NpcActionKind.UseHealingItem || state.NpcActions[index].Kind==NpcActionKind.RestToHeal || state.NpcActions[index].Kind==NpcActionKind.CastSpell && state.NpcActions[index].MagicHealing!=null) &&
                observation!=null && observation.Phase==NpcActionPhase.Completed)return false;
            if(index>=0 && (state.NpcActions[index].Kind==NpcActionKind.Enter || state.NpcActions[index].Kind==NpcActionKind.Exit) &&
                observation!=null && observation.Phase==NpcActionPhase.Completed)return false;
            if(index<0 || !NpcActionExecutor.Advance(state.NpcActions[index],expectedRevision,now,observation,out updated)) return false;
            state.NpcActions[index]=updated;IndexNpcAction(updated);return true;
        }
        public bool TryCompleteNpcBuildingTransition(string id,long expectedRevision,double now,NpcBuildingTransitionReceipt receipt)
        {
            int index=state.NpcActions.FindIndex(j=>j.Id==id);
            if(index<0 || receipt==null)return false;
            var current=state.NpcActions[index];var resident=ResolveResident(current.ResidentId);
            if(resident==null || !resident.IsAlive || resident.IsQuestProtected ||
                current.Kind!=NpcActionKind.Enter && current.Kind!=NpcActionKind.Exit ||
                current.Phase!=NpcActionPhase.Executing || current.Revision!=expectedRevision ||
                receipt.ObservedGameTime!=now)return false;
            try {NpcBuildingTransitionReceipt.CopyValidated(receipt,current);}
            catch(ArgumentException) {return false;}
            NpcActionJob completed;
            if(!NpcActionExecutor.Advance(current,expectedRevision,now,
                NpcActionObservation.BuildingTransitionSuccess(receipt),out completed) || completed.Phase!=NpcActionPhase.Completed)return false;
            state.NpcActions[index]=completed;IndexNpcAction(completed);return true;
        }
        public bool TryCompleteNpcActionEffect(string id,long expectedRevision,double now,Func<bool> applyEffect)
        {
            if (applyEffect == null) return false;
            return TryCompleteNpcActionEffectWithReceipt(id, expectedRevision, now, () =>
                applyEffect() ? NpcActionEffectResult.Success(null) : NpcActionEffectResult.Failure());
        }
        internal bool TryCompleteNpcActionEffectWithReceipt(string id,long expectedRevision,double now,Func<NpcActionEffectResult> applyEffect)
        {
            int index=state.NpcActions.FindIndex(j=>j.Id==id);NpcActionJob completed;
            if(index<0 || applyEffect==null) return false;
            var current=state.NpcActions[index];var resident=ResolveResident(current.ResidentId);
            if(resident==null || !resident.IsAlive || resident.IsQuestProtected || current.ServiceCode=="tavern-beer" && current.ServiceStep!=1 ||
                current.ServiceCode==NpcShopCommerce.TrainingServiceCode && current.ServiceStep!=1 ||
                !NpcActionExecutor.Advance(current,expectedRevision,now,
                NpcActionObservation.Success(id+":observed"),out completed) || completed.Phase!=NpcActionPhase.Completed) return false;
            // Synchronous adapter only: no yield, save/load, or action-ledger mutation
            // between validation, the native effect, and this completion record.
            bool purchase=current.ServiceCode==NpcShopCommerce.ServiceCode;
            bool homeRetrieval=current.Kind==NpcActionKind.Transfer && current.Target.TransferDirection==NpcActionTransferDirection.HomeContainerToNpc;
            var before=purchase || homeRetrieval?ReadNpcInventory(current.ResidentId):null;
            var effectResult=applyEffect();
            if(effectResult==null || !effectResult.Succeeded) return false;
            // Receipt creation is restricted to validated factory output supplied by
            // the synchronous native adapter before it commits inventory state.
            completed.ItemTransferReceipt=NpcActionTransferReceipt.CopyFields(effectResult.TransferReceipt);
            if(before!=null)
            {
                var after=ReadNpcInventory(current.ResidentId);
                string transaction=id+(purchase?":purchase":":transfer");
                if(after!=null && after.LastTransactionId==transaction && after.Revision==before.Revision+1)
                {
                    var oldIds=new HashSet<ulong>();foreach(var item in before.Items)oldIds.Add(item.Uid);
                    string acquired=null;int count=0;
                    foreach(var item in after.Items)if(!oldIds.Contains(item.Uid)){acquired=item.Uid.ToString(CultureInfo.InvariantCulture);count++;}
                    // Ambiguous/missing result never grants a subsequent handover.
                    if(count==1 && (purchase || acquired==current.Target.ItemId) &&
                        (purchase || after.Items.Exists(item=>item.Uid.ToString(CultureInfo.InvariantCulture)==current.Target.ItemId)))
                        completed.AcquiredItemId=acquired;
                }
            }
            state.NpcActions[index]=completed;IndexNpcAction(completed);return true;
        }

        private int itemTransferReceiptDrainCursor;

        /// <summary>Projects at most 32 durable player-to-NPC transfer receipts into the optional appraisal inbox.</summary>
        public int DrainNpcItemTransferReceipts(int maxAttempts)
        {
            if (maxAttempts < 1 || maxAttempts > 32) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
            int count=state.NpcActions.Count;
            if(count==0){itemTransferReceiptDrainCursor=0;return 0;}
            if(itemTransferReceiptDrainCursor<0 || itemTransferReceiptDrainCursor>=count)itemTransferReceiptDrainCursor=0;
            int scanned=0,attempted=0,enqueued=0;
            while(scanned<maxAttempts && scanned<count && attempted<maxAttempts)
            {
                if(itemTransferReceiptDrainCursor>=count)itemTransferReceiptDrainCursor=0;
                int index=itemTransferReceiptDrainCursor++;
                var job=state.NpcActions[index];scanned++;
                if(job==null || job.Phase!=NpcActionPhase.Completed || job.ItemTransferReceipt==null ||
                    job.ProjectedItemTransferReceipt!=null)continue;
                attempted++;
                try
                {
                    var evidence=job.ItemTransferReceipt.ToNativeEvent(job.Id);
                    var marked=PrepareProjectedItemTransferReceipt(job);
                    if(state.NpcExperienceInbox.HasOutcome(evidence.ObserverId,evidence.DeduplicationKey))
                    {
                        if(HasMatchingItemTransferOutcome(job,evidence))PublishProjectedItemTransferReceipt(index,marked);
                        continue;
                    }
                    if(TryEnqueueVerifiedNativeExperience(evidence))
                    {
                        // Prepared action is already copied and validated. No fallible
                        // receipt work remains after the inbox accepts the outcome.
                        PublishProjectedItemTransferReceipt(index,marked);
                        enqueued++;
                    }
                }
                catch(ArgumentException) { /* Leave durable receipt for a later valid projection attempt. */ }
                catch(InvalidOperationException) { /* Inbox failure must not affect native item ownership. */ }
            }
            return enqueued;
        }
        private bool HasMatchingItemTransferOutcome(NpcActionJob job,NpcExperienceEvent evidence)
        {
            if(state.NpcExperienceInbox==null || state.NpcExperienceInbox.Entries==null)return false;
            var receipt=job.ItemTransferReceipt;
            foreach(var entry in state.NpcExperienceInbox.Entries)
            {
                if(entry==null || entry.ObserverId!=evidence.ObserverId || entry.DeduplicationKey!=evidence.DeduplicationKey)
                    continue;
                return entry.Origin==NpcExperienceEventOrigin.NativeOutcome &&
                    entry.SubjectPersonId==receipt.RecipientId && entry.ReceiptId==receipt.TransactionId &&
                    entry.EpisodeId==job.Id+":item-transfer" && entry.OutcomeId==job.Id+":item-received" &&
                    entry.EventKind==NpcExperienceEventKind.ItemReceived &&
                    BitConverter.DoubleToInt64Bits(entry.ObservedGameTime)==BitConverter.DoubleToInt64Bits(receipt.ObservedGameTime) &&
                    entry.ResponsibleActorAttribution==NpcResponsibleActorAttribution.Known &&
                    entry.ResponsibleActorId==receipt.CounterpartyId &&
                    entry.ImpactDetailsKind==(int)NpcExperienceImpactKind.ItemTransfer &&
                    entry.ReceivedItemUid==receipt.RetainedDestinationItemUid && entry.TransferQuantity==receipt.Quantity &&
                    entry.TransferCurrency==0 && entry.NativeValuationStatus==(int)receipt.NativeValuationStatus &&
                    entry.NativeGoldValue==receipt.NativeGoldValue;
            }
            return false;
        }
        private static NpcActionJob PrepareProjectedItemTransferReceipt(NpcActionJob job)
        {
            var marked=NpcActionExecutor.Copy(job);
            marked.ProjectedItemTransferReceipt=NpcActionTransferReceipt.CopyFields(job.ItemTransferReceipt);
            NpcActionExecutor.Validate(marked);
            return marked;
        }
        private void PublishProjectedItemTransferReceipt(int index,NpcActionJob marked)
        {
            // Inbox and action metadata change on the same main-thread call. Projection
            // does not advance the completed action or stale routine-recovery revisions.
            state.NpcActions[index]=marked;
            IndexNpcAction(marked);
        }
        public bool TryAcceptNpcDeparture(NpcActionJob proposed,VisitDeparture departure,bool consent,out string reason)
        {return TryAcceptNpcDepartureCore(proposed,departure,consent,null,out reason);}

        internal bool TryAcceptNpcDepartureClassified(NpcActionJob proposed,VisitDeparture departure,bool consent,
            NpcOperationAdmission expectedSource,out string reason)
        {
            reason="The departure source is no longer current.";
            if(expectedSource==null)return false;
            return TryAcceptNpcDepartureCore(proposed,departure,consent,expectedSource,out reason);
        }

        private bool TryAcceptNpcDepartureCore(NpcActionJob proposed,VisitDeparture departure,bool consent,
            NpcOperationAdmission expectedSource,out string reason)
        {
            reason="Invalid optional-visit departure.";
            VisitDeparture copy;
            try {copy=VisitDeparture.Copy(departure);ValidateDepartureResident(state,copy);ValidateDepartureAppointment(state,copy);}
            catch(ArgumentException) {return false;}
            if(proposed==null || proposed.Kind!=NpcActionKind.ChangeSchedule || proposed.ResidentId!=copy.ResidentId ||
                proposed.AcceptedAt!=copy.Motion.StartedAt) return false;
            foreach(var booking in state.Appointments)
                if(booking.ResidentId==copy.ResidentId && !booking.OutcomeRecorded && booking.Status!="cancelled" &&
                    proposed.AcceptedAt<AppointmentTiming.ReservedUntil(booking) && booking.StartsAt<copy.Until)
                {reason="An accepted appointment takes priority over leaving early.";return false;}
            if(!TryAcceptNpcActionCore(proposed,true,consent,true,expectedSource,out reason)) return false;
            // Both payloads are fully validated before either ledger changes.
            state.VisitDepartures.RemoveAll(d=>d.ResidentId==copy.ResidentId);state.VisitDepartures.Add(copy);return true;
        }

        private bool ActionAdmissionMatchesCurrentSource(NpcActionJob proposed,NpcOperationAdmission expected)
        {
            if(!NpcOperationAdmission.Same(proposed.Admission,expected))return false;
            try {NpcOperationAdmission.CopyValidated(expected);}
            catch(ArgumentException){return false;}
            if(expected.Kind==NpcOperationAdmissionKind.Conversation)
            {
                if(expected.SourceId!=proposed.ResidentId)return false;
                var cursor=state.NpcConversationCommandCursors.Find(x=>x.PersonId==proposed.ResidentId);
                if(cursor==null || cursor.LastIssuedOrdinal!=expected.SourceRevision ||
                    cursor.LastAcceptedOrdinal>=expected.SourceRevision)return false;
                string id=proposed.Kind==NpcActionKind.ChangeSchedule?
                    NpcOperationAdmission.ConversationDepartureId(NpcFrameworkScopeId,proposed.ResidentId,expected.SourceRevision):
                    NpcOperationAdmission.ConversationActionId(NpcFrameworkScopeId,proposed.ResidentId,expected.SourceRevision);
                return proposed.Id==id;
            }
            if(expected.Kind!=NpcOperationAdmissionKind.TaskStep)return false;
            var task=ReadNpcTask(expected.SourceId);
            return task!=null && task.ResidentId==proposed.ResidentId && task.Phase==NpcTaskPhase.Ready &&
                task.Revision==expected.SourceRevision && task.CurrentStepIndex==expected.StepIndex &&
                proposed.Id==NpcTaskContract.ActionId(task) && dispatchingNpcTaskActionId==proposed.Id;
        }
        private bool IsBoundNpcHealthCareActionStep(NpcActionJob proposed,NpcOperationAdmission source)
        {
            if(source.Kind!=NpcOperationAdmissionKind.TaskStep)return false;
            NpcTaskPlan task=ReadNpcTask(source.SourceId);
            NpcHealthCareRecord care=state.NpcHealthCares.Find(row=>row.PersonId==proposed.ResidentId);
            return task!=null && task.Phase==NpcTaskPhase.Ready &&
                task.Revision==source.SourceRevision && task.CurrentStepIndex==source.StepIndex &&
                task.Admission!=null && task.Admission.Kind==NpcOperationAdmissionKind.HealthCare &&
                care!=null && care.Phase==NpcHealthCarePhase.Acting && care.TaskId==task.Id &&
                NpcHealthCare.IsBoundCareTask(care,task);
        }
        public bool TryPayNpcActionService(string id,long expectedRevision,double now)
        {
            var job=ReadNpcAction(id);
            bool training=job!=null && job.ServiceCode==NpcShopCommerce.TrainingServiceCode;
            if(job==null || job.Revision!=expectedRevision || expectedRevision==long.MaxValue || job.Phase!=NpcActionPhase.Executing ||
                job.ServiceCode!="tavern-beer" && !training || job.ServiceStep!=0 || !Finite(now) || now<job.UpdatedAt || now>=job.DeadlineAt ||
                training && now+10800d>=job.DeadlineAt) return false;
            var inventory=ReadNpcInventory(job.ResidentId);
            if(inventory==null || inventory.Revision!=job.ExpectedInventoryRevision || inventory.WalletGold<job.ServicePrice) return false;
            var updated=NpcActionExecutor.Copy(job);updated.ServiceStep=1;updated.Phase=training?NpcActionPhase.Executing:NpcActionPhase.Approaching;
            if(training) {updated.FinishAt=now+10800d;updated.ExpectedInventoryRevision++;}
            updated.UpdatedAt=now;updated.Revision++;NpcActionExecutor.Validate(updated);
            bool paid=job.ServicePrice>0 ? TryPayNpcService(job.ResidentId,job.ServicePrice,job.Id+":service",job.ExpectedInventoryRevision):
                CommitNpcInventoryTransaction(job.Id+":service",new[]{new NpcInventoryUpdate {PersonId=job.ResidentId,
                    ExpectedRevision=job.ExpectedInventoryRevision,WalletGold=inventory.WalletGold,Items=inventory.Items}});
            if(!paid) return false;
            // Main-thread atomic operation: no yield or native side effect between
            // authoritative payment and the persisted return-to-recipient stage.
            state.NpcActions[state.NpcActions.FindIndex(j=>j.Id==id)]=updated;IndexNpcAction(updated);return true;
        }
        public bool SaveNpcActionPosition(string id,long expectedRevision,NpcActionTarget position,bool returning,double now)
        {
            int index=state.NpcActions.FindIndex(j=>j.Id==id);
            if(index<0 || state.NpcActions[index].FollowTransition!=null || state.NpcActions[index].RoutineRecoveryTransition!=null ||
                state.NpcActions[index].Revision!=expectedRevision || expectedRevision==long.MaxValue || !Finite(now) || now<state.NpcActions[index].UpdatedAt) return false;
            var active=ActiveNpcAction(state.NpcActions[index].ResidentId);
            if(returning && active!=null && active.Id!=id) return false;
            var copy=NpcActionExecutor.Copy(state.NpcActions[index]);copy.LastPosition=NpcActionExecutor.CopyTarget(position);
            if(copy.Phase==NpcActionPhase.Completed && copy.BuildingTransitionReceipt!=null &&
                copy.BuildingTransitionReceipt.MatchesDestination(copy.LastPosition))
                copy.BuildingTransitionReceipt.DestinationObserved=true;
            copy.ReturningToRoutine=returning;copy.UpdatedAt=now;copy.Revision++;
            try {NpcActionExecutor.Validate(copy);} catch(ArgumentException) {return false;}
            state.NpcActions[index]=copy;IndexNpcAction(copy);return true;
        }
        private static List<NpcActionJob> CopyNpcActions(WorldState source,WorldState validated)
        {
            Require(source.NpcActions!=null && source.NpcActions.Count<=10000,"Invalid NPC action ledger.");
            var result=new List<NpcActionJob>();var ids=new HashSet<string>(StringComparer.Ordinal);
            var proposals=new HashSet<string>(StringComparer.Ordinal);var active=new HashSet<string>(StringComparer.Ordinal);
            var knownOwners=new HashSet<string>(StringComparer.Ordinal);
            foreach(var job in source.NpcActions)
            {
                Require(source.Version>=59 || job.MagicHealing==null,
                    "Legacy action has future magic-healing metadata.");
                NpcActionExecutor.Validate(job);
                var copy=NpcActionExecutor.Copy(job);
                if(source.Version<48 && copy.Kind==NpcActionKind.Interact)
                {
                    if(copy.InteractionMemoryRecorded)copy.InteractionProjectionDisposition=NpcInteractionProjectionDisposition.Recorded;
                    else if(NpcActionExecutor.IsTerminal(copy))
                    {
                        copy.InteractionProjectionDisposition=NpcInteractionProjectionDisposition.Unavailable;
                        copy.InteractionProjectionReason="Historical physical encounter evidence was not saved.";
                    }
                }
                if(source.Version>=48)
                    Require(copy.InteractionMemoryRecorded==
                        (copy.InteractionProjectionDisposition==NpcInteractionProjectionDisposition.Recorded),
                        "Interaction projection flag disagrees with disposition.");
                NpcActionExecutor.Validate(copy);
                Require(CachedKnownNpcIdentity(validated,copy.ResidentId,knownOwners) && ids.Add(copy.Id) && proposals.Add(copy.ResidentId+"\n"+copy.ProposalId),"Unknown or duplicate NPC action owner/proposal.");
                if(!NpcActionExecutor.IsTerminal(copy) || copy.ReturningToRoutine) Require(active.Add(copy.ResidentId),"Multiple actions own one NPC.");
                result.Add(copy);
            }
            return result;
        }
    }
}
