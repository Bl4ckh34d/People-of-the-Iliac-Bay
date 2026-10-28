using System;
using System.Collections.Generic;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcObservedSpeechAcceptanceResult { Rejected, Projected, AcceptedPending }

    [Serializable]
    public sealed class NpcObservedSpeechPending
    {
        public long Ordinal;
        public string SessionId;
        public long RequestRevision;
        public NpcExperienceInboxEntry Entry;
    }

    [Serializable]
    public sealed class NpcObservedSpeechSourceState
    {
        public string PersonId;
        public long LastIssuedOrdinal;
        public long LastAcceptedOrdinal;
        public long LastProjectedOrdinal;
        public double LastAcceptedObservedAt;
        public NpcObservedSpeechPending Pending;
    }

    public sealed class NpcObservedSpeechSession
    {
        internal WorldStateStore Owner;
        internal object Epoch;
        internal string BranchId;
        internal string ScopeId;
        internal string PersonId;
        internal bool Closed;
        internal NpcObservedSpeechTicket CurrentTicket;
        public string SessionId { get; internal set; }
    }

    public sealed class NpcObservedSpeechTicket
    {
        internal WorldStateStore Owner;
        internal object Epoch;
        internal string BranchId;
        internal string ScopeId;
        internal NpcObservedSpeechSession Session;
        internal ConversationContextBinding Binding;
        internal bool Accepted;
        internal NpcObservedSpeechReceipt IssuedReceipt;
        public string PersonId { get; internal set; }
        public string SessionId { get; internal set; }
        public long RequestRevision { get; internal set; }
        public long Ordinal { get; internal set; }
        public string ExactText { get; internal set; }
        public double ObservedGameTime { get; internal set; }
        public NpcObservedSpeechReceipt Receipt { get { return NpcObservedSpeechReceipt.CopyValidated(IssuedReceipt); } }
    }

    public sealed partial class WorldStateStore
    {
        private object liveObservedSpeechEpoch;
        private Dictionary<string, NpcObservedSpeechSession> liveObservedSpeechSessions;
        private object pendingObservedSpeechDrainEpoch;
        private int pendingObservedSpeechDrainCursor;

        public bool IsKnownNpcObservedSpeechSpeaker(string personId)
        { return IsKnownNpcExperienceIdentity(personId); }

        private void RefreshObservedSpeechEpoch()
        {
            if (ReferenceEquals(liveObservedSpeechEpoch, bindingEpoch) && liveObservedSpeechSessions != null) return;
            liveObservedSpeechEpoch = bindingEpoch;
            liveObservedSpeechSessions = new Dictionary<string, NpcObservedSpeechSession>(StringComparer.Ordinal);
        }

        public bool TryBeginNpcObservedSpeechSession(string personId, string sessionId,
            out NpcObservedSpeechSession session)
        {
            session = null;
            if (!IsKnownNpcObservedSpeechSpeaker(personId) || string.IsNullOrWhiteSpace(sessionId) ||
                sessionId.Length > 128 || string.IsNullOrWhiteSpace(NpcFrameworkScopeId)) return false;
            RefreshObservedSpeechEpoch();
            NpcObservedSpeechSession previous;
            if (liveObservedSpeechSessions.TryGetValue(personId, out previous))
            {
                if (previous.SessionId == sessionId) return false;
                previous.Closed = true;
            }
            session = new NpcObservedSpeechSession { Owner = this, Epoch = bindingEpoch,
                BranchId = experienceBranchId, ScopeId = NpcFrameworkScopeId,
                PersonId = personId, SessionId = sessionId };
            liveObservedSpeechSessions[personId] = session;
            return true;
        }

        public bool TryCloseNpcObservedSpeechSession(NpcObservedSpeechSession session)
        {
            if (!CurrentObservedSpeechSession(session)) return false;
            session.Closed = true;
            session.CurrentTicket = null;
            liveObservedSpeechSessions.Remove(session.PersonId);
            return true;
        }

        private bool CurrentObservedSpeechSession(NpcObservedSpeechSession session)
        {
            RefreshObservedSpeechEpoch();
            NpcObservedSpeechSession current;
            return session != null && session.Owner == this && !session.Closed &&
                ReferenceEquals(session.Epoch, bindingEpoch) && session.BranchId == experienceBranchId &&
                session.ScopeId == NpcFrameworkScopeId &&
                liveObservedSpeechSessions.TryGetValue(session.PersonId, out current) &&
                ReferenceEquals(current, session);
        }

        private bool CurrentObservedSpeechTicket(NpcObservedSpeechTicket ticket)
        {
            return ticket != null && ticket.Owner == this && !ticket.Accepted &&
                ReferenceEquals(ticket.Epoch, bindingEpoch) && ticket.BranchId == experienceBranchId &&
                ticket.ScopeId == NpcFrameworkScopeId && CurrentObservedSpeechSession(ticket.Session) &&
                ReferenceEquals(ticket.Session.CurrentTicket, ticket) &&
                ticket.SessionId == ticket.Session.SessionId && ticket.PersonId == ticket.Session.PersonId;
        }

        public bool TryIssueNpcObservedSpeech(NpcObservedSpeechSession session, long requestRevision,
            string exactText, double observedGameTime, out NpcObservedSpeechTicket ticket)
        {
            ticket = null;
            if (!CurrentObservedSpeechSession(session) || requestRevision < 0 ||
                string.IsNullOrWhiteSpace(exactText) || exactText.Length > 1024 ||
                !Finite(observedGameTime) || observedGameTime < 0) return false;
            List<NpcObservedSpeechSourceState> next;
            try { next = CopyObservedSpeechSources(state.NpcObservedSpeechSources, state); }
            catch (ArgumentException) { return false; }
            int index = next.FindIndex(x => x.PersonId == session.PersonId);
            var source = index < 0 ? new NpcObservedSpeechSourceState { PersonId = session.PersonId } : next[index];
            if (source.Pending != null || source.LastIssuedOrdinal >= long.MaxValue - 1 ||
                observedGameTime < source.LastAcceptedObservedAt ||
                index < 0 && next.Count >= 100000) return false;
            long ordinal = source.LastIssuedOrdinal + 1;
            NpcObservedSpeechReceipt receipt;
            try { receipt = NpcObservedSpeechReceipt.CreateIssued(NpcFrameworkScopeId, session.PersonId,
                ordinal, exactText, observedGameTime); }
            catch (ArgumentException) { return false; }
            source.LastIssuedOrdinal = ordinal;
            if (index < 0) next.Add(source);
            ticket = new NpcObservedSpeechTicket { Owner = this, Epoch = bindingEpoch,
                BranchId = experienceBranchId, ScopeId = NpcFrameworkScopeId, Session = session,
                PersonId = session.PersonId, SessionId = session.SessionId, RequestRevision = requestRevision,
                Ordinal = ordinal, ExactText = exactText, ObservedGameTime = observedGameTime,
                IssuedReceipt = receipt };
            state.NpcObservedSpeechSources = next;
            session.CurrentTicket = ticket;
            return true;
        }

        public bool TryBindNpcObservedSpeechContext(NpcObservedSpeechTicket ticket,
            ConversationContextBinding binding)
        {
            if (!CurrentObservedSpeechTicket(ticket) || binding == null ||
                binding.IdentityKind != ConversationIdentityKind.StablePerson ||
                binding.PersonId != ticket.PersonId || binding.SessionId != ticket.SessionId ||
                binding.RequestRevision != ticket.RequestRevision ||
                ticket.Binding != null && !ticket.Binding.HasSameRequestBinding(binding)) return false;
            ticket.Binding = binding;
            return true;
        }

        private bool TryPrepareObservedSpeechAcceptance(NpcObservedSpeechTicket ticket,
            NpcObservedSpeechReceipt receipt, NpcExperienceAppliedEffectsMask mask,
            out List<NpcObservedSpeechSourceState> nextSources, out NpcExperienceInboxEntry entry)
        {
            nextSources = null; entry = null;
            if (!CurrentObservedSpeechTicket(ticket) || receipt == null) return false;
            try
            {
                var checkedReceipt = NpcObservedSpeechReceipt.CopyValidated(receipt);
                var original = ticket.IssuedReceipt;
                if (checkedReceipt.Evidence.ObserverId != original.Evidence.ObserverId ||
                    checkedReceipt.Evidence.SubjectPersonId != original.Evidence.SubjectPersonId ||
                    checkedReceipt.Evidence.ReceiptId != original.Evidence.ReceiptId ||
                    checkedReceipt.Evidence.EpisodeId != original.Evidence.EpisodeId ||
                    checkedReceipt.Evidence.UtteranceId != original.Evidence.UtteranceId ||
                    checkedReceipt.Evidence.AttributedSpeakerId != original.Evidence.AttributedSpeakerId ||
                    checkedReceipt.Evidence.DeduplicationKey != original.Evidence.DeduplicationKey ||
                    checkedReceipt.Evidence.ObservedGameTime != original.Evidence.ObservedGameTime ||
                    checkedReceipt.Turn.ObserverId != original.Turn.ObserverId ||
                    checkedReceipt.Turn.EpisodeId != original.Turn.EpisodeId ||
                    checkedReceipt.Turn.SpeakerId != original.Turn.SpeakerId ||
                    checkedReceipt.Turn.ReceiptId != original.Turn.ReceiptId ||
                    checkedReceipt.Turn.Text != ticket.ExactText ||
                    checkedReceipt.Turn.ObservedGameTime != ticket.ObservedGameTime) return false;
                entry = NpcExperienceInboxEntry.FromObservedSpeech(checkedReceipt, mask);
                nextSources = CopyObservedSpeechSources(state.NpcObservedSpeechSources, state);
                var source = nextSources.Find(x => x.PersonId == ticket.PersonId);
                if (source == null || source.Pending != null || source.LastAcceptedOrdinal != source.LastProjectedOrdinal ||
                    source.LastIssuedOrdinal != ticket.Ordinal || source.LastAcceptedOrdinal >= ticket.Ordinal)
                    return false;
                source.LastAcceptedOrdinal = ticket.Ordinal;
                source.LastAcceptedObservedAt = Math.Max(source.LastAcceptedObservedAt, entry.ObservedGameTime);
                return true;
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        private bool TryPrepareObservedSpeechInbox(NpcExperienceInboxEntry entry, out NpcExperienceInbox next)
        {
            next = null;
            try
            {
                var current = NpcExperienceInbox.CopyValidated(state.NpcExperienceInbox,
                    IsKnownNpcExperienceIdentity);
                return NpcExperienceInbox.TryAppendValidated(current, entry,
                    IsKnownNpcExperienceIdentity, out next);
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
        }

        public NpcObservedSpeechAcceptanceResult TryAcceptNpcObservedSpeechAfterAppend(
            NpcObservedSpeechTicket ticket, NpcObservedSpeechReceipt receipt,
            NpcExperienceAppliedEffectsMask mask)
        {
            // Mood must commit with its journal and mood state in ConversationMood.
            if ((mask & NpcExperienceAppliedEffectsMask.Mood) != 0)
                return NpcObservedSpeechAcceptanceResult.Rejected;
            List<NpcObservedSpeechSourceState> sources;
            NpcExperienceInboxEntry entry;
            if (!TryPrepareObservedSpeechAcceptance(ticket, receipt, mask, out sources, out entry))
                return NpcObservedSpeechAcceptanceResult.Rejected;
            var source = sources.Find(x => x.PersonId == ticket.PersonId);
            NpcExperienceInbox inbox;
            if (TryPrepareObservedSpeechInbox(entry, out inbox))
            {
                source.LastProjectedOrdinal = ticket.Ordinal;
                state.NpcExperienceInbox = inbox;
                state.NpcObservedSpeechSources = sources;
                ticket.Accepted = true;
                return NpcObservedSpeechAcceptanceResult.Projected;
            }
            source.Pending = new NpcObservedSpeechPending { Ordinal = ticket.Ordinal,
                SessionId = ticket.SessionId, RequestRevision = ticket.RequestRevision,
                Entry = NpcExperienceInboxEntry.CopyValidated(entry) };
            state.NpcObservedSpeechSources = sources;
            ticket.Accepted = true;
            return NpcObservedSpeechAcceptanceResult.AcceptedPending;
        }

        public bool TryProjectPendingNpcObservedSpeech(string personId)
        {
            if (!IsKnownNpcObservedSpeechSpeaker(personId)) return false;
            List<NpcObservedSpeechSourceState> sources;
            try { sources = CopyObservedSpeechSources(state.NpcObservedSpeechSources, state); }
            catch (ArgumentException) { return false; }
            var source = sources.Find(x => x.PersonId == personId);
            if (source == null || source.Pending == null) return false;
            var entry = source.Pending.Entry;
            NpcExperienceInbox next;
            try
            {
                var current = NpcExperienceInbox.CopyValidated(state.NpcExperienceInbox,
                    IsKnownNpcExperienceIdentity);
                var retained = current.Entries.Find(x => x.ObserverId == personId &&
                    x.DeduplicationKey == entry.DeduplicationKey);
                if (retained != null)
                {
                    if (!SameObservedSpeechSource(retained, entry)) return false;
                    next = current;
                }
                else
                {
                    if (!NpcExperienceInbox.TryAppendValidated(current, entry,
                        IsKnownNpcExperienceIdentity, out next)) return false;
                }
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            source.LastProjectedOrdinal = source.LastAcceptedOrdinal;
            source.Pending = null;
            state.NpcExperienceInbox = next;
            state.NpcObservedSpeechSources = sources;
            return true;
        }

        public int DrainPendingNpcObservedSpeech(int maximumExamined)
        {
            if (maximumExamined < 1 || maximumExamined > 32)
                throw new ArgumentOutOfRangeException(nameof(maximumExamined));
            if (!ReferenceEquals(pendingObservedSpeechDrainEpoch, bindingEpoch))
            {
                pendingObservedSpeechDrainEpoch = bindingEpoch;
                pendingObservedSpeechDrainCursor = 0;
            }
            int count = state.NpcObservedSpeechSources.Count;
            if (count == 0) return 0;
            int projected = 0;
            int examined = Math.Min(maximumExamined, count);
            for (int i = 0; i < examined; i++)
            {
                int index = pendingObservedSpeechDrainCursor % count;
                pendingObservedSpeechDrainCursor = (index + 1) % count;
                var row = state.NpcObservedSpeechSources[index];
                if (row != null && row.Pending != null && TryProjectPendingNpcObservedSpeech(row.PersonId))
                    projected++;
            }
            return projected;
        }

        private static bool SameObservedSpeechSource(NpcExperienceInboxEntry a, NpcExperienceInboxEntry b)
        {
            return a.Origin == b.Origin && a.ObserverId == b.ObserverId &&
                a.SubjectPersonId == b.SubjectPersonId && a.ReceiptId == b.ReceiptId &&
                a.EpisodeId == b.EpisodeId && a.UtteranceId == b.UtteranceId &&
                a.AttributedSpeakerId == b.AttributedSpeakerId &&
                a.ObservedGameTime == b.ObservedGameTime && a.DeduplicationKey == b.DeduplicationKey &&
                a.ObservedTurnObserverId == b.ObservedTurnObserverId &&
                a.ObservedTurnEpisodeId == b.ObservedTurnEpisodeId &&
                a.ObservedTurnSpeakerId == b.ObservedTurnSpeakerId &&
                a.ObservedTurnReceiptId == b.ObservedTurnReceiptId &&
                a.ObservedTurnText == b.ObservedTurnText &&
                a.ObservedTurnGameTime == b.ObservedTurnGameTime &&
                a.AlreadyAppliedEffectsMask == b.AlreadyAppliedEffectsMask &&
                a.HasKnownImmediateAffinityDelta == b.HasKnownImmediateAffinityDelta &&
                a.ImmediateAffinityDelta == b.ImmediateAffinityDelta &&
                a.AffinityExclusion == b.AffinityExclusion;
        }

        private static List<NpcObservedSpeechSourceState> CopyObservedSpeechSources(
            IList<NpcObservedSpeechSourceState> rows, WorldState world)
        {
            if (rows == null || rows.Count > 100000) throw new ArgumentException("Invalid observed speech source list.");
            var result = new List<NpcObservedSpeechSourceState>(rows.Count);
            var owners = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                bool alive;
                if (row == null || string.IsNullOrWhiteSpace(row.PersonId) || row.PersonId.Length > 240 ||
                    !owners.Add(row.PersonId) || !KnownNpcIdentity(world, row.PersonId, out alive) ||
                    row.LastIssuedOrdinal < 1 || row.LastIssuedOrdinal >= long.MaxValue ||
                    row.LastAcceptedOrdinal < 0 || row.LastAcceptedOrdinal > row.LastIssuedOrdinal ||
                    row.LastProjectedOrdinal < 0 || row.LastProjectedOrdinal > row.LastAcceptedOrdinal ||
                    double.IsNaN(row.LastAcceptedObservedAt) || double.IsInfinity(row.LastAcceptedObservedAt) ||
                    row.LastAcceptedObservedAt < 0 ||
                    (row.Pending == null) != (row.LastAcceptedOrdinal == row.LastProjectedOrdinal))
                    throw new ArgumentException("Invalid observed speech source cursor.");
                NpcObservedSpeechPending pending = null;
                if (row.Pending != null)
                {
                    var old = row.Pending;
                    if (old.Ordinal != row.LastAcceptedOrdinal || old.Ordinal < 1 ||
                        string.IsNullOrWhiteSpace(old.SessionId) || old.SessionId.Length > 128 ||
                        old.RequestRevision < 0 || old.Entry == null)
                        throw new ArgumentException("Invalid pending observed speech source.");
                    var entry = NpcExperienceInboxEntry.CopyValidated(old.Entry);
                    var expected = NpcObservedSpeechReceipt.CreateIssued(world.NpcFrameworkScopeId,
                        row.PersonId, old.Ordinal, entry.ObservedTurnText, entry.ObservedGameTime);
                    if (entry.Origin != NpcExperienceEventOrigin.AttributedSpeech ||
                        entry.Status != NpcExperienceInboxStatus.Pending || entry.Revision != 1 ||
                        entry.ObservedGameTime > row.LastAcceptedObservedAt ||
                        entry.Appraisal != null || entry.UpdatedAt != entry.ObservedGameTime ||
                        entry.ObserverId != row.PersonId || entry.ReceiptId != expected.Evidence.ReceiptId ||
                        entry.EpisodeId != expected.Evidence.EpisodeId ||
                        entry.UtteranceId != expected.Evidence.UtteranceId ||
                        entry.DeduplicationKey != expected.Evidence.DeduplicationKey)
                        throw new ArgumentException("Pending speech lacks exact issued provenance.");
                    pending = new NpcObservedSpeechPending { Ordinal = old.Ordinal,
                        SessionId = old.SessionId, RequestRevision = old.RequestRevision, Entry = entry };
                }
                result.Add(new NpcObservedSpeechSourceState { PersonId = row.PersonId,
                    LastIssuedOrdinal = row.LastIssuedOrdinal,
                    LastAcceptedOrdinal = row.LastAcceptedOrdinal,
                    LastProjectedOrdinal = row.LastProjectedOrdinal,
                    LastAcceptedObservedAt = row.LastAcceptedObservedAt, Pending = pending });
            }
            return result;
        }

        private static List<NpcObservedSpeechSourceState> CopyNpcObservedSpeechSources(
            WorldState source, WorldState validated)
        {
            var result = CopyObservedSpeechSources(source.NpcObservedSpeechSources, validated);
            const string v2Prefix = "native-heard-v2-";
            foreach (var entry in validated.NpcExperienceInbox.Entries)
            {
                if (entry.Origin != NpcExperienceEventOrigin.AttributedSpeech ||
                    entry.ReceiptId == null || !entry.ReceiptId.StartsWith(v2Prefix, StringComparison.Ordinal))
                    continue;
                int separator = entry.ReceiptId.LastIndexOf('-');
                long ordinal = 0;
                Require(separator > 0 && long.TryParse(entry.ReceiptId.Substring(separator + 1),
                    NumberStyles.None, CultureInfo.InvariantCulture, out ordinal) && ordinal > 0,
                    "Invalid issued speech receipt ordinal.");
                var owner = result.Find(x => x.PersonId == entry.ObserverId);
                var issued = NpcObservedSpeechReceipt.CreateIssued(validated.NpcFrameworkScopeId,
                    entry.ObserverId, ordinal, entry.ObservedTurnText, entry.ObservedGameTime);
                Require(owner != null && entry.ReceiptId == issued.Evidence.ReceiptId &&
                    owner.LastAcceptedObservedAt >= entry.ObservedGameTime &&
                    entry.SubjectPersonId == issued.Evidence.SubjectPersonId &&
                    entry.EpisodeId == issued.Evidence.EpisodeId &&
                    entry.UtteranceId == issued.Evidence.UtteranceId &&
                    entry.AttributedSpeakerId == issued.Evidence.AttributedSpeakerId &&
                    entry.ObservedTurnObserverId == issued.Turn.ObserverId &&
                    entry.ObservedTurnEpisodeId == issued.Turn.EpisodeId &&
                    entry.ObservedTurnSpeakerId == issued.Turn.SpeakerId &&
                    entry.ObservedTurnReceiptId == issued.Turn.ReceiptId &&
                    (ordinal <= owner.LastProjectedOrdinal ||
                     owner.Pending != null && owner.Pending.Ordinal == ordinal &&
                        SameObservedSpeechSource(owner.Pending.Entry, entry)),
                    "Issued observed speech detail lacks its durable source.");
            }
            return result;
        }
    }
}
