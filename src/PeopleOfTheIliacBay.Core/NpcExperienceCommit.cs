using System;
using System.Collections.Generic;
using System.Linq;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>Flat save-owned appraisal receipt, retained in the consumed inbox row for exact-once effects.</summary>
    [Serializable]
    public sealed class NpcExperienceAppliedAppraisal
    {
        public int Version = 2;
        public int MoodDelta, AffinityDelta, TrustDelta, ResentmentDelta, FearDelta;
        public int AppliedMoodDelta, AppliedAffinityDelta;
        public NpcExperienceSpeechActKind SpeechActKind;
        public string SpeechTargetPersonId;
        internal static NpcExperienceAppliedAppraisal CopyValidated(NpcExperienceAppliedAppraisal source)
        {
            if (source == null || source.Version < 1 || source.Version > 2 || source.MoodDelta < -10 || source.MoodDelta > 10 ||
                source.AffinityDelta < -50 || source.AffinityDelta > 5 || source.TrustDelta < -25 || source.TrustDelta > 10 ||
                source.ResentmentDelta < -10 || source.ResentmentDelta > 25 || source.FearDelta < -10 || source.FearDelta > 25 ||
                !AppliedWithin(source.MoodDelta, source.AppliedMoodDelta) || !AppliedWithin(source.AffinityDelta, source.AppliedAffinityDelta) ||
                source.Version == 2 && (!Enum.IsDefined(typeof(NpcExperienceSpeechActKind), source.SpeechActKind) ||
                    source.SpeechTargetPersonId != null && source.SpeechTargetPersonId.Length > 0 &&
                        !NpcExperienceAppraisalContract.ValidIdentity(source.SpeechTargetPersonId, 240) ||
                    source.SpeechActKind == NpcExperienceSpeechActKind.NotApplicable &&
                        !string.IsNullOrEmpty(source.SpeechTargetPersonId) ||
                    source.SpeechActKind == NpcExperienceSpeechActKind.DirectInsult && source.AffinityDelta < -25 ||
                    source.SpeechActKind == NpcExperienceSpeechActKind.DirectThreat && source.AffinityDelta < -50))
                throw new ArgumentException("Invalid applied native experience appraisal.", nameof(source));
            return new NpcExperienceAppliedAppraisal { Version = source.Version, MoodDelta = source.MoodDelta,
                AffinityDelta = source.AffinityDelta, TrustDelta = source.TrustDelta, ResentmentDelta = source.ResentmentDelta,
                FearDelta = source.FearDelta, AppliedMoodDelta = source.AppliedMoodDelta, AppliedAffinityDelta = source.AppliedAffinityDelta,
                SpeechActKind = source.SpeechActKind, SpeechTargetPersonId = source.SpeechTargetPersonId };
        }
        private static bool AppliedWithin(int proposed, int applied)
        { return proposed < 0 ? applied >= proposed && applied <= 0 : applied >= 0 && applied <= proposed; }
    }

    public sealed class NpcExperienceAppraisalTicket
    {
        internal WorldStateStore Owner;
        internal object Epoch;
        internal NpcExperienceInboxEntry Entry;
        internal ConversationAttitude Attitude;
        internal long LedgerRevision;
        internal int AppointmentLedgerRevision;
        internal long ServiceContractLedgerRevision;
        internal Dictionary<string, long> ObserverServiceContractRevisions;
        internal string ActiveTaskId, ActiveActionId;
        internal long ActiveTaskRevision, ActiveActionRevision;
        public NpcExperienceAppraisalRequest Request { get; internal set; }
    }

    public sealed partial class WorldStateStore
    {
        public bool TryBindNpcExperienceSpeechClassification(NpcExperienceAppraisalTicket ticket,
            NpcSpeechActClassification classification)
        {
            if (ticket == null || ticket.Owner != this || !ReferenceEquals(ticket.Epoch, bindingEpoch) ||
                ticket.Request.SaveBranchId != experienceBranchId || classification == null ||
                ticket.Request.SpeechClassification != null || !ReferenceEquals(classification.Request.Source, ticket.Request)) return false;
            ticket.Request = NpcExperienceAppraisalContract.WithSpeechClassification(ticket.Request, classification);
            return true;
        }
        private string experienceBranchId = Guid.NewGuid().ToString("N");
        private long experienceCaptureSequence;

        public List<string> ReadPendingNpcExperienceObservers(int limit, string afterObserverId)
        {
            if (limit < 1 || limit > 100) throw new ArgumentOutOfRangeException(nameof(limit));
            if (afterObserverId != null && (string.IsNullOrWhiteSpace(afterObserverId) || afterObserverId.Length > 240))
                throw new ArgumentException("Invalid experience observer cursor.", nameof(afterObserverId));
            // Order by immutable identity for continuation across unchanged/uncapturable events.
            // Each observer's appraisal capture still processes their oldest pending event first.
            return state.NpcExperienceInbox.Entries.Where(x => x.Status == NpcExperienceInboxStatus.Pending)
                .Select(x => x.ObserverId).Distinct(StringComparer.Ordinal)
                .Where(id => afterObserverId == null || StringComparer.Ordinal.Compare(id, afterObserverId) > 0)
                .OrderBy(id => id, StringComparer.Ordinal).Take(limit).ToList();
        }

        public List<string> ReadPendingNpcExperienceObservers(int limit)
        {
            if (limit < 1 || limit > 100) throw new ArgumentOutOfRangeException(nameof(limit));
            return state.NpcExperienceInbox.Entries.Where(x => x.Status == NpcExperienceInboxStatus.Pending)
                .OrderBy(x => x.ObservedGameTime).ThenBy(x => x.DeduplicationKey, StringComparer.Ordinal)
                .Select(x => x.ObserverId).Distinct(StringComparer.Ordinal).Take(limit).ToList();
        }

        /// <summary>Owner-thread capture from the actual saved queue; no model-supplied event is accepted.</summary>
        public bool TryCaptureNpcExperienceAppraisal(string observerId, double now,
            out NpcExperienceAppraisalTicket ticket)
        {
            return TryCaptureNpcExperienceAppraisal(observerId, now, null, out ticket);
        }

        /// <summary>
        /// Captures the oldest eligible native event. A profile is created here only from a saved resident's
        /// stable appearance seed and an authoritative background supplied by the native runtime. Residents
        /// with no native faction have a verified Unknown background; a factioned resident requires the
        /// runtime's resolved classification. This overload never guesses from an unresolved faction ID.
        /// </summary>
        public bool TryCaptureNpcExperienceAppraisal(string observerId, double now,
            NpcSocialBackground? resolvedBackground, out NpcExperienceAppraisalTicket ticket)
        {
            ticket = null;
            if (!Finite(now) || now < 0 || !IsKnownNpcExperienceIdentity(observerId) ||
                experienceCaptureSequence == long.MaxValue) return false;
            var storedProfile = state.NpcPersonalities.Find(item => item.PersonId == observerId);
            if (storedProfile != null && storedProfile.RandomTraitsSuppressed) return false;
            var profile = ReadNpcPersonality(observerId);
            bool createProfile = profile == null;
            if (createProfile)
            {
                uint seed;
                int factionId;
                bool questProtected;
                if (!TryReadNpcPersonalityIdentity(observerId, out seed, out factionId, out questProtected)) return false;

                NpcSocialBackground background;
                if (factionId <= 0)
                {
                    // DFU's zero sentinel and absent native faction both establish Unknown background.
                    background = NpcSocialBackground.Unknown;
                }
                else
                {
                    // A native faction is not enough to infer class: defer until runtime resolves it.
                    if (!resolvedBackground.HasValue) return false;
                    background = resolvedBackground.Value;
                }
                if (!Enum.IsDefined(typeof(NpcSocialBackground), background))
                    return false;
                try { profile = NpcPersonalityGenerator.Generate(observerId, seed, background); }
                catch (ArgumentException) { return false; }
            }
            var entry = OldestExperience(observerId);
            if (entry == null || entry.ObservedGameTime > now) return false;
            var evidence = entry.ToEvent();
            bool waited = false;
            if (evidence.EventKind == NpcExperienceEventKind.AppointmentNoShow ||
                evidence.EventKind == NpcExperienceEventKind.AppointmentAttended)
            {
                bool matchedAppointment = false;
                foreach (var appointment in state.Appointments)
                {
                    NpcExperienceEvent recorded;
                    if (NpcAppointmentExperience.TryCreate(appointment, out recorded) && SameEvidence(entry, recorded))
                    { matchedAppointment = true; break; }
                }
                if (!matchedAppointment) return false;
                waited = evidence.EventKind == NpcExperienceEventKind.AppointmentNoShow;
            }
            var mood = CaptureConversationMood(observerId, now);
            var attitude = state.ConversationAttitudes.Find(x => x.PersonId == observerId);
            int affinity = attitude == null ? 0 : attitude.HasNativeBaseline
                ? ConversationEffects.Reaction(attitude.NativeBaseline, attitude.Offset) : Math.Max(-100, attitude.Offset);
            var projected = ConversationAffinityEventUpdate.Copy(attitude) ?? new ConversationAttitude { PersonId = observerId };
            UpdatePositiveCredit(projected, now);
            NpcRoleState role = ReadNpcRole(observerId);
            string target = evidence.Origin == NpcExperienceEventOrigin.AttributedSpeech
                ? evidence.AttributedSpeakerId : evidence.ResponsibleActorId;
            var relationship = target == null || target == observerId ? null : state.NpcRelationships.Read(observerId, target)
                ?? NpcDirectedRelationship.Create(observerId, target).ToView();
            if (relationship != null && string.Equals(target, NpcDirectedRelationship.PlayerTargetId, StringComparison.Ordinal))
                relationship = relationship.WithPlayerFearPolicy(NpcRolePolicy.SuppressPlayerFear(role),
                    role == null ? -1 : role.Revision);
            if (relationship != null)
                relationship = relationship.ProjectRecovery(now, profile, NpcFearRecoveryPolicy.ProvisionalDefault,
                    NpcResentmentRecoveryPolicy.ProvisionalDefault);
            long capture = ++experienceCaptureSequence;
            NpcExperienceAppraisalRequest request;
            List<string> family = evidence.Origin == NpcExperienceEventOrigin.AttributedSpeech
                ? KnownResidentFamilyIds(observerId) : null;
            NpcObservedConversationTurn observedTurn = evidence.Origin == NpcExperienceEventOrigin.AttributedSpeech
                ? entry.ToObservedTurn() : null;
            List<NpcObservedConversationTurn> conversationContext = observedTurn == null
                ? null : RecentObservedSpeechContext(entry);
            List<NpcExperienceCommitment> commitments;
            try { commitments = CurrentServiceCommitments(observerId, now); }
            catch (InvalidOperationException) { return false; }
            NpcMotivationContextSnapshot motivationContext;
            int appointmentLedgerRevision;
            long serviceContractLedgerRevision, activeTaskRevision, activeActionRevision;
            string activeTaskId, activeActionId;
            if (!TryCaptureExperienceMotivationContext(observerId, now, profile, relationship,
                out motivationContext, out appointmentLedgerRevision, out serviceContractLedgerRevision,
                out activeTaskId, out activeTaskRevision, out activeActionId, out activeActionRevision)) return false;
            NpcKnowledgeSnapshot knowledgeSnapshot;
            if (motivationContext == null || !TryCaptureNpcKnowledgeSnapshot(observerId, out knowledgeSnapshot)) return false;
            try
            {
                request = NpcExperienceAppraisalContract.CreateRequest(experienceBranchId, evidence, profile,
                    mood.Value, mood.Revision, now, affinity, capture,
                    (int)Math.Floor(projected.PositiveCredit), now, relationship, commitments, waited,
                    observedTurn, conversationContext, family, null, null, entry.AlreadyAppliedEffectsMask,
                    motivationContext, knowledgeSnapshot,
                    entry.HasKnownImmediateAffinityDelta ? entry.ImmediateAffinityDelta : (int?)null,
                    entry.AffinityExclusion, role);
            }
            catch (ArgumentException) { return false; }
            ticket = new NpcExperienceAppraisalTicket { Owner = this, Epoch = bindingEpoch,
                Entry = NpcExperienceInboxEntry.CopyValidated(entry), Attitude = ConversationAffinityEventUpdate.Copy(attitude),
                LedgerRevision = state.NpcRelationships.Revision, Request = request,
                AppointmentLedgerRevision = appointmentLedgerRevision,
                ServiceContractLedgerRevision = serviceContractLedgerRevision,
                ObserverServiceContractRevisions = ExperienceServiceRevisions(observerId),
                ActiveTaskId = activeTaskId, ActiveTaskRevision = activeTaskRevision,
                ActiveActionId = activeActionId, ActiveActionRevision = activeActionRevision };
            if (createProfile)
                state.NpcPersonalities.Add(NpcPersonalityProfile.CopyValidated(profile));
            return true;
        }

        private bool TryCaptureExperienceMotivationContext(string observerId, double now,
            NpcPersonalityProfile personality, NpcDirectedRelationshipView relationship,
            out NpcMotivationContextSnapshot context, out int appointmentLedgerRevision,
            out long serviceContractLedgerRevision, out string activeTaskId, out long activeTaskRevision,
            out string activeActionId, out long activeActionRevision)
        {
            context = null;
            appointmentLedgerRevision = AppointmentRevision;
            serviceContractLedgerRevision = -1;
            activeTaskId = null; activeTaskRevision = -1;
            activeActionId = null; activeActionRevision = -1;
            try
            {
                var duties = ExperienceNativeDuties(observerId, now, out serviceContractLedgerRevision,
                    out activeTaskId, out activeTaskRevision, out activeActionId, out activeActionRevision);
                NpcMotivationState agenda = FindNpcMotivation(observerId) ?? NpcMotivationState.Create(observerId);
                long clockRevision = BitConverter.DoubleToInt64Bits(now);
                string clockId = "appraisal-clock-" + clockRevision.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
                var clockEvidence = new NpcMotivationEvidenceRef(experienceBranchId, observerId, clockId,
                    NpcMotivationEvidenceKind.WorldClock, clockRevision, now);
                var clock = new NpcMotivationGameClock(now, clockRevision, clockEvidence);
                var profileEvidence = new NpcMotivationEvidenceRef(experienceBranchId, observerId,
                    "appraisal-profile-v" + personality.Version.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    NpcMotivationEvidenceKind.PersonalityProfile, personality.Version, now);
                var profileReference = new NpcMotivationProfileReference(observerId, personality.Version, profileEvidence);
                NpcMotivationRelationshipReference relationshipReference = null;
                if (relationship != null)
                {
                    var relationshipEvidence = new NpcMotivationEvidenceRef(experienceBranchId, observerId,
                        "appraisal-relationship-r" + relationship.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        NpcMotivationEvidenceKind.RelationshipLedger, relationship.Revision, now);
                    relationshipReference = new NpcMotivationRelationshipReference(observerId, relationship.TargetId,
                        relationship.Revision, relationshipEvidence);
                }
                context = NpcMotivationState.CaptureContext(agenda, experienceBranchId, clock,
                    profileReference, relationshipReference, null, null, null, duties);
                return true;
            }
            catch (ArgumentException) { context = null; return false; }
            catch (OverflowException) { context = null; return false; }
            catch (InvalidOperationException) { context = null; return false; }
        }

        private List<NpcMotivationDuty> ExperienceNativeDuties(string observerId, double now,
            out long serviceContractLedgerRevision, out string activeTaskId, out long activeTaskRevision,
            out string activeActionId, out long activeActionRevision)
        {
            var duties = new List<NpcMotivationDuty>();
            foreach (AppointmentRecord appointment in AppointmentsFor(observerId))
            {
                if (appointment.Status != "accepted" && appointment.Status != "waiting" && appointment.Status != "attended")
                    continue;
                var commitment = new ConversationContextCommitment(appointment.Id, "appointment",
                    appointment.Status, AppointmentRevision, null, appointment.StartsAt, appointment.EndsAt);
                NpcMotivationDuty duty;
                if (NpcMotivationConversationProjection.TryBuildDuty(experienceBranchId, observerId, now,
                    commitment, out duty)) duties.Add(duty);
            }

            NpcTaskPlan task = ActiveNpcTask(observerId);
            activeTaskId = task == null ? null : task.Id;
            activeTaskRevision = task == null ? -1 : task.Revision;
            if (task != null)
            {
                string status = task.Phase == NpcTaskPhase.Ready ? "ready" : "running";
                var commitment = new ConversationContextCommitment(task.Id, "task", status,
                    task.Revision, null, task.AcceptedAt, task.DeadlineAt);
                NpcMotivationDuty duty;
                if (NpcMotivationConversationProjection.TryBuildDuty(experienceBranchId, observerId, now,
                    commitment, out duty)) duties.Add(duty);
            }

            NpcActionJob action = ExperienceDutyAction(observerId);
            activeActionId = action == null ? null : action.Id;
            activeActionRevision = action == null ? -1 : action.Revision;
            if (action != null)
            {
                var commitment = new ConversationContextCommitment(action.Id, "action-task",
                    action.Phase.ToString(), action.Revision, action.Kind, action.AcceptedAt, action.DeadlineAt);
                NpcMotivationDuty duty;
                if (NpcMotivationConversationProjection.TryBuildDuty(experienceBranchId, observerId, now,
                    commitment, out duty)) duties.Add(duty);
            }

            NpcServiceContractLedger serviceContracts = ReadNpcServiceContracts();
            serviceContractLedgerRevision = serviceContracts.Revision;
            NpcMotivationConversationProjection.AddCurrentServiceDuties(serviceContracts,
                experienceBranchId, observerId, now, duties);
            return duties;
        }

        private bool IsExperienceDutySourceCurrent(NpcExperienceAppraisalTicket ticket)
        {
            if (AppointmentRevision != ticket.AppointmentLedgerRevision) return false;
            Dictionary<string, long> currentServices;
            try { currentServices = ExperienceServiceRevisions(ticket.Request.ObserverId); }
            catch (InvalidOperationException) { return false; }
            // Another NPC's contract cannot change this observer's appraisal context.
            // Include every owner contract, even reserved/expired ones absent from the
            // current duty projection, so additions and lifecycle changes still invalidate.
            var capturedServices = ticket.ObserverServiceContractRevisions;
            if (capturedServices == null || currentServices.Count != capturedServices.Count ||
                currentServices.Any(x => !capturedServices.TryGetValue(x.Key, out long revision) || revision != x.Value))
                return false;
            NpcTaskPlan task = ActiveNpcTask(ticket.Request.ObserverId);
            if ((task == null) != (ticket.ActiveTaskId == null) || task != null &&
                (task.Id != ticket.ActiveTaskId || task.Revision != ticket.ActiveTaskRevision)) return false;
            NpcActionJob action = ExperienceDutyAction(ticket.Request.ObserverId);
            if ((action == null) != (ticket.ActiveActionId == null) || action != null &&
                (action.Id != ticket.ActiveActionId || action.Revision != ticket.ActiveActionRevision)) return false;
            return true;
        }

        private NpcActionJob ExperienceDutyAction(string observerId)
        {
            var action = ActiveNpcAction(observerId);
            // Return-position writes do not change the duties supplied to the appraiser.
            return action != null && !NpcActionExecutor.IsTerminal(action) && !action.ReturningToRoutine ? action : null;
        }

        private Dictionary<string, long> ExperienceServiceRevisions(string observerId)
        {
            return ReadNpcServiceContracts().Contracts.Where(x => x != null && x.NpcId == observerId)
                .ToDictionary(x => x.ContractId, x => x.Revision, StringComparer.Ordinal);
        }

        private static bool SameMotivationDuties(NpcMotivationContextSnapshot current,
            NpcMotivationContextSnapshot captured)
        {
            if (current == null || captured == null || current.Duties.Count != captured.Duties.Count) return false;
            for (int i = 0; i < current.Duties.Count; i++)
            {
                NpcMotivationDuty left = current.Duties[i], right = captured.Duties[i];
                if (left.Id != right.Id || left.Kind != right.Kind || left.Scope != right.Scope ||
                    left.Status != right.Status || left.StartsAtGameTime != right.StartsAtGameTime ||
                    left.EndsAtGameTime != right.EndsAtGameTime || left.Revision != right.Revision ||
                    left.Evidence == null || right.Evidence == null ||
                    left.Evidence.SaveBranchId != right.Evidence.SaveBranchId ||
                    left.Evidence.ObserverId != right.Evidence.ObserverId ||
                    left.Evidence.EvidenceId != right.Evidence.EvidenceId ||
                    left.Evidence.Kind != right.Evidence.Kind || left.Evidence.Revision != right.Evidence.Revision ||
                    left.Evidence.ObservedGameTime != right.Evidence.ObservedGameTime) return false;
            }
            return true;
        }

        private List<NpcObservedConversationTurn> RecentObservedSpeechContext(NpcExperienceInboxEntry current)
        {
            var selected = SelectPreviousSpeechEntries(state.NpcExperienceInbox.Entries, current);
            var turns = selected.Select(x => x.ToObservedTurn()).ToList();
            turns.Reverse();
            return turns;
        }

        internal static List<NpcExperienceInboxEntry> SelectPreviousSpeechEntries(
            IEnumerable<NpcExperienceInboxEntry> entries, NpcExperienceInboxEntry current,
            bool alreadySorted = false, Action inspected = null)
        {
            const int maxTurns = 6;
            const int maxTextCharacters = 1800;
            var selected = new List<NpcExperienceInboxEntry>();
            int characters = 0;
            var sorted = alreadySorted ? entries as IList<NpcExperienceInboxEntry> : null;
            var previous = sorted != null ? EarlierSortedSpeechEntries(sorted, current, inspected) :
                alreadySorted ? entries : entries
                .Where(x => x.Origin == NpcExperienceEventOrigin.AttributedSpeech &&
                    x.ObserverId == current.ObserverId)
                .OrderByDescending(x => x.ObservedGameTime)
                .ThenByDescending(x => x.DeduplicationKey, StringComparer.Ordinal);
            foreach (var entry in previous)
            {
                inspected?.Invoke();
                if (selected.Count >= maxTurns) break;
                if (entry.Origin != NpcExperienceEventOrigin.AttributedSpeech ||
                    entry.ObserverId != current.ObserverId ||
                    entry.DeduplicationKey == current.DeduplicationKey ||
                    entry.ObservedGameTime > current.ObservedGameTime ||
                    entry.ObservedGameTime == current.ObservedGameTime &&
                    string.CompareOrdinal(entry.DeduplicationKey, current.DeduplicationKey) >= 0)
                    continue;
                // Inbox validation guarantees these are receipt-bound observed turns, not event claims.
                var evidence = entry.ToEvent();
                if (evidence.ClaimedEventKind.HasValue || evidence.ClaimedResponsibleActorId != null) continue;
                var turn = entry.ToObservedTurn();
                if (turn == null || turn.Text.Length > maxTextCharacters - characters) continue;
                selected.Add(entry);
                characters += turn.Text.Length;
            }
            return selected;
        }

        private static IEnumerable<NpcExperienceInboxEntry> EarlierSortedSpeechEntries(
            IList<NpcExperienceInboxEntry> sorted, NpcExperienceInboxEntry current,
            Action inspected)
        {
            // The retention index is newest first. Seek the first strictly older
            // key once; pending reads must not rewalk every newer row in a full inbox.
            int low = 0, high = sorted.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                var row = sorted[middle];
                inspected?.Invoke();
                bool earlier = row.ObservedGameTime < current.ObservedGameTime ||
                    row.ObservedGameTime == current.ObservedGameTime &&
                    string.CompareOrdinal(row.DeduplicationKey, current.DeduplicationKey) < 0;
                if (earlier) high = middle;
                else low = middle + 1;
            }
            for (int i = low; i < sorted.Count; i++) yield return sorted[i];
        }

        private List<NpcExperienceCommitment> CurrentServiceCommitments(string observerId, double now)
        {
            const int maximum = 8;
            var result = new List<NpcExperienceCommitment>();
            var ledger = ReadNpcServiceContracts();
            foreach (var contract in ledger.Contracts
                .Where(x => x != null && x.NpcId == observerId)
                .OrderBy(x => x.ContractId, StringComparer.Ordinal))
            {
                NpcExperienceCommitmentStatus status;
                if (contract.Status == NpcServiceContractStatus.Reserved)
                    status = NpcExperienceCommitmentStatus.Planned;
                else if (contract.Status == NpcServiceContractStatus.Active &&
                    now >= contract.BlockStartAt && now < contract.BlockEndAt)
                    status = NpcExperienceCommitmentStatus.Active;
                else continue;

                result.Add(NpcExperienceCommitment.Create(contract.ContractId,
                    NpcExperienceCommitmentKind.Task, status, contract.Revision));
                if (result.Count >= maximum) break;
            }
            return result;
        }

        /// <summary>
        /// Synchronous owner-thread transaction. Prepare every replacement before publishing any component.
        /// Like the other WorldStateStore APIs, callers must not access this store concurrently.
        /// </summary>
        public bool TryCommitNpcExperienceAppraisal(NpcExperienceAppraisalTicket ticket, string response)
        {
            if (ticket == null || ticket.Owner != this || !ReferenceEquals(ticket.Epoch, bindingEpoch) ||
                ticket.Request.SaveBranchId != experienceBranchId) return false;
            var request = ticket.Request;
            // The foreground boundary already applied this exact speech receipt as one relationship
            // event. Retire its queued background appraisal without accepting a second model effect.
            // This also lets a previously captured ticket drain after a response-generation failure.
            if (TryConsumeAppliedBoundarySpeech(ticket)) return true;
            if (request.RequiresSpeechAppraisal && request.SpeechClassification == null) return false;
            if (string.Equals(request.RelationshipTargetId, NpcDirectedRelationship.PlayerTargetId, StringComparison.Ordinal))
            {
                NpcRoleState currentRole = ReadNpcRole(request.ObserverId);
                if ((currentRole == null ? -1 : currentRole.Revision) != request.ExpectedRoleRevision) return false;
            }
            var current = OldestExperience(request.ObserverId);
            if (current == null || !SameEvidence(current, ticket.Entry.ToEvent()) || !SameInboxEntry(current, ticket.Entry) ||
                current.Revision != ticket.Entry.Revision || request.AlreadyAppliedEffectsMask != ticket.Entry.AlreadyAppliedEffectsMask ||
                request.ImmediateAffinityDelta != (ticket.Entry.HasKnownImmediateAffinityDelta ? ticket.Entry.ImmediateAffinityDelta : (int?)null) ||
                request.AffinityExclusion != ticket.Entry.AffinityExclusion)
                return false;
            var attitude = state.ConversationAttitudes.Find(x => x.PersonId == request.ObserverId);
            if (!SameAttitude(attitude, ticket.Attitude) || state.NpcRelationships.Revision != ticket.LedgerRevision)
                return false;
            var mood = state.ConversationMoods.Find(x => x.PersonId == request.ObserverId);
            if ((mood == null ? 0 : mood.Revision) != request.ExpectedMoodRevision ||
                mood != null && mood.HasTimeAnchor && mood.UpdatedAt > request.CapturedGameTime ||
                ((request.AlreadyAppliedEffectsMask & NpcExperienceAppliedEffectsMask.Mood) == 0 && request.ExpectedMoodRevision == long.MaxValue)) return false;
            NpcPersonalityProfile currentProfile = ReadNpcPersonality(request.ObserverId);
            NpcMotivationContextSnapshot currentMotivation = null;
            bool hasCurrentMotivation = currentProfile != null && IsExperienceDutySourceCurrent(ticket) &&
                TryCaptureExperienceMotivationContext(request.ObserverId, request.CapturedGameTime,
                    currentProfile, request.Relationship, out currentMotivation, out _, out _, out _, out _, out _, out _);
            if (!hasCurrentMotivation || (currentMotivation == null) != (request.MotivationContext == null) ||
                currentMotivation != null && !SameMotivationDuties(currentMotivation, request.MotivationContext) ||
                currentMotivation != null && currentMotivation.Revision != request.ExpectedMotivationRevision) return false;
            bool hasCurrentKnowledge = TryCaptureNpcKnowledgeSnapshot(request.ObserverId, out NpcKnowledgeSnapshot currentKnowledge);
            if (hasCurrentKnowledge != (request.KnowledgeSnapshot != null) ||
                hasCurrentKnowledge && (request.KnowledgeSnapshot.SaveEpochId != NpcFrameworkScopeId ||
                    currentKnowledge.Revision != request.ExpectedKnowledgeRevision)) return false;
            NpcExperienceAppraisal appraisal;
            if (!NpcExperienceAppraisalContract.TryParseResponse(request, response, out appraisal) ||
                appraisal.ConversationOption != NpcExperienceConversationOption.NoAction) return false;
            appraisal = NpcConversationCalming.ApplyAcceptedSpeech(request, appraisal,
                CanSpeechRecoverCurrentBoundary(request));
            var evidence = current.ToEvent();
            var nextRelationships = state.NpcRelationships;
            if (appraisal.TrustDelta != 0 || appraisal.ResentmentDelta != 0 || appraisal.FearDelta != 0)
            {
                NpcDirectedRelationshipView applied;
                if (!NpcRelationshipLedger.TryApplyAppraisal(state.NpcRelationships, ticket.LedgerRevision,
                    request.SaveBranchId, experienceBranchId, evidence, appraisal, IsKnownNpcExperienceIdentity,
                    request.Personality, NpcFearRecoveryPolicy.ProvisionalDefault,
                    NpcResentmentRecoveryPolicy.ProvisionalDefault,
                    out nextRelationships, out applied)) return false;
            }
            ConversationAffinityEventUpdate affinity = null;
            if (appraisal.AffinityDelta != 0)
            {
                string rejection;
                if (!ConversationAffinityEventPolicy.TryCreate(experienceBranchId, evidence, request, appraisal,
                    attitude, request.ExpectedAffinityRevision, out affinity, out rejection)) return false;
            }
            bool moodAlreadyApplied = (request.AlreadyAppliedEffectsMask & NpcExperienceAppliedEffectsMask.Mood) != 0;
            int atCapture = CaptureConversationMood(request.ObserverId, request.CapturedGameTime).Value;
            // Delayed native evidence contributes only its remaining mood effect; never rewind newer chat state.
            int nextValue = atCapture;
            var nextMoods = new List<ConversationMood>(state.ConversationMoods);
            if (!moodAlreadyApplied)
            {
                int remainingDelta = MoodRecovery.ValueAt(appraisal.MoodDelta, evidence.ObservedGameTime,
                    request.CapturedGameTime, ProfileMoodHalfLife(request.ObserverId));
                nextValue = Math.Max(-100, Math.Min(100, atCapture + remainingDelta));
                var nextMood = new ConversationMood { PersonId = request.ObserverId, Value = nextValue,
                    Revision = request.ExpectedMoodRevision + 1, AcceptedTurnSequence = mood == null ? 0 : mood.AcceptedTurnSequence,
                    HasTimeAnchor = true, UpdatedAt = request.CapturedGameTime,
                    RecoveryHalfLifeSeconds = ProfileMoodHalfLife(request.ObserverId) };
                nextMoods.RemoveAll(x => x.PersonId == request.ObserverId); nextMoods.Add(nextMood);
            }
            var nextAttitudes = new List<ConversationAttitude>(state.ConversationAttitudes);
            if (affinity != null)
            { nextAttitudes.RemoveAll(x => x.PersonId == request.ObserverId); nextAttitudes.Add(affinity.NextAttitude); }
            var nextInbox = NpcExperienceInbox.CopyValidated(state.NpcExperienceInbox, IsKnownNpcExperienceIdentity);
            var consumed = nextInbox.Entries.Find(x => x.ObserverId == request.ObserverId && x.DeduplicationKey == current.DeduplicationKey);
            consumed.Status = NpcExperienceInboxStatus.Consumed; consumed.Revision = 2; consumed.UpdatedAt = request.CapturedGameTime;
            consumed.Appraisal = new NpcExperienceAppliedAppraisal { MoodDelta = appraisal.MoodDelta,
                AffinityDelta = appraisal.AffinityDelta, TrustDelta = appraisal.TrustDelta,
                ResentmentDelta = appraisal.ResentmentDelta, FearDelta = appraisal.FearDelta,
                SpeechActKind = appraisal.SpeechActKind, SpeechTargetPersonId = appraisal.SpeechTargetPersonId,
                AppliedMoodDelta = moodAlreadyApplied ? 0 : nextValue - atCapture,
                AppliedAffinityDelta = affinity == null ? 0 : affinity.AppliedDelta };
            if (nextInbox.DetailRevision == long.MaxValue) return false;
            nextInbox.DetailRevision++;
            nextInbox = NpcExperienceInbox.CopyValidated(nextInbox, IsKnownNpcExperienceIdentity);
            var nextSceneMemories=PrepareAppraisedSceneMemory(evidence,appraisal.MoodDelta);
            // No allocations, callbacks or fallible operations after this point.
            state.ConversationMoods = nextMoods;
            state.ConversationAttitudes = nextAttitudes;
            state.NpcRelationships = nextRelationships;
            state.NpcExperienceInbox = nextInbox;
            if(nextSceneMemories!=null){state.SceneMemories=nextSceneMemories;sceneMemoryOwner=null;SceneMemoryRevision++;}
            return true;
        }

        private bool TryConsumeAppliedBoundarySpeech(NpcExperienceAppraisalTicket ticket)
        {
            var request = ticket.Request;
            var current = OldestExperience(request.ObserverId);
            if (current == null || current.Origin != NpcExperienceEventOrigin.AttributedSpeech ||
                !SameEvidence(current, ticket.Entry.ToEvent()) || !SameInboxEntry(current, ticket.Entry) ||
                current.Revision != ticket.Entry.Revision ||
                state.NpcRelationships.Revision != ticket.LedgerRevision) return false;
            var accepted = state.NpcRelationships.AcceptedOutcomes.Find(x =>
                x.ConversationBoundaryOrdinal > 0 && x.ObserverId == current.ObserverId &&
                x.DeduplicationKey == current.DeduplicationKey && x.ReceiptId == current.ReceiptId &&
                x.OutcomeId == current.UtteranceId && x.ObservedGameTime == current.ObservedGameTime);
            if (accepted == null) return false;
            var nextInbox = NpcExperienceInbox.CopyValidated(state.NpcExperienceInbox,
                IsKnownNpcExperienceIdentity);
            var consumed = nextInbox.Entries.Find(x => x.ObserverId == current.ObserverId &&
                x.DeduplicationKey == current.DeduplicationKey);
            if (consumed == null || consumed.Status != NpcExperienceInboxStatus.Pending ||
                nextInbox.DetailRevision == long.MaxValue) return false;
            consumed.Status = NpcExperienceInboxStatus.Consumed;
            consumed.Revision = 2;
            consumed.UpdatedAt = Math.Max(request.CapturedGameTime, current.ObservedGameTime);
            consumed.Appraisal = new NpcExperienceAppliedAppraisal
            {
                SpeechActKind = accepted.SpeechActKind,
                SpeechTargetPersonId = accepted.SpeechTargetPersonId
            };
            nextInbox.DetailRevision++;
            try { nextInbox = NpcExperienceInbox.CopyValidated(nextInbox, IsKnownNpcExperienceIdentity); }
            catch (ArgumentException) { return false; }
            state.NpcExperienceInbox = nextInbox;
            return true;
        }

        private NpcExperienceInboxEntry OldestExperience(string observer)
        {
            return state.NpcExperienceInbox.Entries.Where(x => x.ObserverId == observer && x.Status == NpcExperienceInboxStatus.Pending)
                .OrderBy(x => x.ObservedGameTime).ThenBy(x => x.DeduplicationKey, StringComparer.Ordinal).FirstOrDefault();
        }
        private List<string> KnownResidentFamilyIds(string observerId)
        {
            var residents = new List<Resident>();
            foreach (var settlement in state.Settlements)
                foreach (var household in settlement.Households)
                    residents.AddRange(household.Residents);
            foreach (var retired in state.HouseholdHistory)
                if (retired != null && retired.Household != null) residents.AddRange(retired.Household.Residents);
            var observer = residents.Find(x => x != null && x.Id == observerId);
            if (observer == null) return new List<string>();
            var family = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrEmpty(observer.PartnerId)) family.Add(observer.PartnerId);
            if (!string.IsNullOrEmpty(observer.MotherId)) family.Add(observer.MotherId);
            if (!string.IsNullOrEmpty(observer.FatherId)) family.Add(observer.FatherId);
            foreach (var resident in residents)
                if (resident != null && (resident.MotherId == observerId || resident.FatherId == observerId)) family.Add(resident.Id);
            family.Remove(observerId);
            var result = new List<string>();
            foreach (var id in family)
                if (IsKnownNpcExperienceIdentity(id)) result.Add(id);
            result.Sort(StringComparer.Ordinal);
            return result;
        }
        private static bool SameEvidence(NpcExperienceInboxEntry saved, NpcExperienceEvent evidence)
        {
            NpcExperienceEventKind? savedEventKind = saved.Origin == NpcExperienceEventOrigin.NativeOutcome
                ? saved.EventKind : (NpcExperienceEventKind?)null;
            return saved.ObserverId == evidence.ObserverId && saved.SubjectPersonId == evidence.SubjectPersonId &&
                saved.ReceiptId == evidence.ReceiptId && saved.EpisodeId == evidence.EpisodeId &&
                saved.OutcomeId == evidence.OutcomeId && savedEventKind == evidence.EventKind &&
                saved.ObservedGameTime == evidence.ObservedGameTime && saved.DeduplicationKey == evidence.DeduplicationKey &&
                saved.Origin == evidence.Origin && saved.UtteranceId == evidence.UtteranceId &&
                saved.AttributedSpeakerId == evidence.AttributedSpeakerId &&
                saved.ResponsibleActorAttribution == evidence.ResponsibleActorAttribution && saved.ResponsibleActorId == evidence.ResponsibleActorId;
        }
        private static bool SameInboxEntry(NpcExperienceInboxEntry first, NpcExperienceInboxEntry second)
        {
            return first.Origin == second.Origin && first.AlreadyAppliedEffectsMask == second.AlreadyAppliedEffectsMask &&
                first.HasKnownImmediateAffinityDelta == second.HasKnownImmediateAffinityDelta &&
                first.ImmediateAffinityDelta == second.ImmediateAffinityDelta && first.AffinityExclusion == second.AffinityExclusion &&
                first.ObservedTurnObserverId == second.ObservedTurnObserverId &&
                first.ObservedTurnEpisodeId == second.ObservedTurnEpisodeId &&
                first.ObservedTurnSpeakerId == second.ObservedTurnSpeakerId &&
                first.ObservedTurnReceiptId == second.ObservedTurnReceiptId &&
                first.ObservedTurnText == second.ObservedTurnText && first.ObservedTurnGameTime == second.ObservedTurnGameTime;
        }
        private static bool SameAttitude(ConversationAttitude first, ConversationAttitude second)
        {
            if (first == null || second == null) return first == second;
            // Include baseline, credit, encounter and familiarity changes, not merely the numerical affinity.
            return first.PersonId == second.PersonId && first.Offset == second.Offset && first.NativeBaseline == second.NativeBaseline &&
                first.HasNativeBaseline == second.HasNativeBaseline && first.NativeFactionId == second.NativeFactionId &&
                first.HasNativeFactionId == second.HasNativeFactionId && first.Day == second.Day &&
                first.PositiveToday == second.PositiveToday && first.NegativeToday == second.NegativeToday &&
                first.PositiveCredit == second.PositiveCredit && first.PositiveCreditUpdatedAt == second.PositiveCreditUpdatedAt &&
                first.PositiveProgressionStage == second.PositiveProgressionStage && first.PositiveStageProgress == second.PositiveStageProgress &&
                first.PositiveEarned == second.PositiveEarned && first.PositiveThisEncounter == second.PositiveThisEncounter &&
                first.Insults == second.Insults && first.EncounterCount == second.EncounterCount && first.LastEncounterAt == second.LastEncounterAt &&
                first.SharedExchangeCount == second.SharedExchangeCount && first.FriendlyInitiatedCount == second.FriendlyInitiatedCount &&
                first.LastFriendlyInitiatedAt == second.LastFriendlyInitiatedAt && first.Utterances.SequenceEqual(second.Utterances);
        }
    }
}
