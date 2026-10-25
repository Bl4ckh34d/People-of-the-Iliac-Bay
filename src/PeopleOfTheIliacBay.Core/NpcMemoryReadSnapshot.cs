using System;

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcMemoryReadKind { Memory, MoodCauses }

    /// <summary>A detached read result with process-local source authority. Never serialize or accept this from a model.</summary>
    public sealed class NpcMemoryReadSnapshot
    {
        internal WorldStateStore Owner;
        internal WorldState State;
        internal NpcExperienceInbox Inbox;
        internal long InboxDetailRevision;
        internal string CueArchiveHead;
        internal int JournalCount, MaximumCharacters;
        internal long MoodRevision;
        internal double NextFutureRecordAt;
        internal double NextFutureTreatmentAt;
        internal string TreatmentAuthority;
        public string PersonId { get; internal set; }
        public NpcMemoryReadKind Kind { get; internal set; }
        public string Text { get; internal set; }
        public double CapturedGameTime { get; internal set; }
        public string ArchiveId { get; internal set; }
        public string ArchiveHead { get; internal set; }
        public NpcMoodCauseHistoryAvailability HistoryAvailability { get; internal set; }
        public double SupportedFromAt { get; internal set; }
    }

    public sealed partial class WorldStateStore
    {
        // Runtime binds personId to its actual speaker. Model output may supply a
        // bounded query only, never identity, branch, time, budget or authority.
        public bool TryCaptureNpcMemoryRead(string personId, string query, double now,
            int maximumCharacters, out NpcMemoryReadSnapshot read)
        {
            read = null;
            if (!ValidMemoryReadInput(personId, now, maximumCharacters, 2000) ||
                query == null || query.Length > 160) return false;
            string text = RecallConversation(personId, query, now, maximumCharacters);
            NpcTreatmentRecallEvidence treatment = CaptureNpcTreatmentRecall(personId, query, now, maximumCharacters);
            if (!string.IsNullOrEmpty(treatment.Text))
            {
                if (string.IsNullOrEmpty(text) || text.StartsWith("MEMORY ABSENT:", StringComparison.Ordinal))
                    text = treatment.Text;
                else text = CleanMemory(treatment.Text + " " + text, maximumCharacters);
            }
            var journal = MemoryFor(personId, false);
            double nextFuture = double.PositiveInfinity;
            if (journal != null)
                foreach (NpcMemoryEntry entry in journal.Entries)
                    if (entry.At > now && entry.At < nextFuture) nextFuture = entry.At;
            read = NewMemoryReadSnapshot(personId, now, maximumCharacters, NpcMemoryReadKind.Memory, text);
            read.JournalCount = journal == null ? 0 : journal.Entries.Count;
            read.NextFutureRecordAt = nextFuture;
            read.NextFutureTreatmentAt = treatment.NextFutureTreatmentAt;
            read.TreatmentAuthority = treatment.Authority;
            return true;
        }

        public bool TryCaptureNpcMoodCauseRead(string personId, double now,
            int maximumCharacters, out NpcMemoryReadSnapshot read)
        {
            read = null;
            if (!ValidMemoryReadInput(personId, now, maximumCharacters, 600)) return false;
            var history = ReadNpcMoodCauseHistory(personId, now, maximumCharacters);
            read = NewMemoryReadSnapshot(personId, now, maximumCharacters, NpcMemoryReadKind.MoodCauses,
                history.Availability == NpcMoodCauseHistoryAvailability.Available ? history.Text : null);
            read.HistoryAvailability = history.Availability;
            read.SupportedFromAt = history.SupportedFromAt;
            read.Inbox = state.NpcExperienceInbox;
            read.InboxDetailRevision = state.NpcExperienceInbox.DetailRevision;
            read.MoodRevision = CaptureConversationMood(personId).Revision;
            return true;
        }

        /// <summary>Recheck once at final publication, not every frame. Reads never refresh this receipt.</summary>
        public bool IsNpcMemoryReadCurrent(NpcMemoryReadSnapshot read, string expectedPersonId, double now)
        {
            if (read == null || !ReferenceEquals(read.Owner, this) || !ReferenceEquals(read.State, state) ||
                !string.Equals(read.PersonId, expectedPersonId, StringComparison.Ordinal) ||
                !Finite(now) || now < read.CapturedGameTime ||
                !string.Equals(read.ArchiveId, MemoryReadArchiveId(), StringComparison.Ordinal) ||
                !string.Equals(read.ArchiveHead, MemoryReadArchiveHead(), StringComparison.Ordinal)) return false;
            if (!string.Equals(read.CueArchiveHead, MemoryReadCueHead(), StringComparison.Ordinal)) return false;
            if (read.Kind == NpcMemoryReadKind.Memory)
            {
                var journal = MemoryFor(read.PersonId, false);
                // Journals are append-only between restores/saves. A save changes
                // the pinned head; restoring even that same head replaces State.
                return (journal == null ? 0 : journal.Entries.Count) == read.JournalCount &&
                    now < read.NextFutureRecordAt && now < read.NextFutureTreatmentAt &&
                    string.Equals(read.TreatmentAuthority, TreatmentRecallAuthority(read.PersonId), StringComparison.Ordinal);
            }
            if (read.Kind != NpcMemoryReadKind.MoodCauses || !ReferenceEquals(read.Inbox, state.NpcExperienceInbox) ||
                read.InboxDetailRevision != state.NpcExperienceInbox.DetailRevision ||
                read.MoodRevision != CaptureConversationMood(read.PersonId).Revision) return false;
            // Inbox.Revision counts enqueues, not consumed-entry replacements.
            // The immutable inbox reference above catches both; re-rendering only
            // handles age-window changes without any new ledger write.
            var currentHistory = ReadNpcMoodCauseHistory(read.PersonId, now, read.MaximumCharacters);
            return read.HistoryAvailability == currentHistory.Availability &&
                read.SupportedFromAt == currentHistory.SupportedFromAt &&
                string.Equals(read.Text, currentHistory.Text, StringComparison.Ordinal);
        }

        private NpcMemoryReadSnapshot NewMemoryReadSnapshot(string personId, double now, int maximumCharacters,
            NpcMemoryReadKind kind, string text)
        {
            return new NpcMemoryReadSnapshot { Owner = this, State = state, PersonId = personId, Kind = kind,
                CapturedGameTime = now, MaximumCharacters = maximumCharacters, Text = text,
                ArchiveId = MemoryReadArchiveId(), ArchiveHead = MemoryReadArchiveHead(),
                CueArchiveHead = MemoryReadCueHead() };
        }

        private string MemoryReadArchiveId()
        { return state.ConversationArchive == null ? "" : state.ConversationArchive.ArchiveId; }
        private string MemoryReadArchiveHead()
        { return state.ConversationArchive == null ? "" : state.ConversationArchive.Head; }
        private string MemoryReadCueHead()
        { return state.ConversationCueArchive == null ? "" : state.ConversationCueArchive.Head; }
        private static bool ValidMemoryReadInput(string personId, double now, int maximumCharacters, int ceiling)
        {
            if (string.IsNullOrWhiteSpace(personId) || personId.Length > 240 || !Finite(now) || now < 0 ||
                maximumCharacters < 256 || maximumCharacters > ceiling) return false;
            foreach (char c in personId) if (char.IsControl(c)) return false;
            return true;
        }
    }
}
