using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    [Serializable]
    public sealed class ConversationMood
    {
        public string PersonId;
        public int Value;
        public long AcceptedTurnSequence;
        public bool HasTimeAnchor;
        public double UpdatedAt;
        public long Revision;
        // Frozen for this anchor so a policy migration cannot reinterpret already elapsed recovery.
        public double RecoveryHalfLifeSeconds;
    }

    public sealed class ConversationMoodView
    {
        public int Value { get; }
        public long AcceptedTurnSequence { get; }
        public long Revision { get; }
        public bool HasTimeAnchor { get; }
        public double UpdatedAt { get; }

        internal ConversationMoodView(int value, long acceptedTurnSequence, long revision,
            bool hasTimeAnchor, double updatedAt)
        {
            Value = value;
            AcceptedTurnSequence = acceptedTurnSequence;
            Revision = revision;
            HasTimeAnchor = hasTimeAnchor;
            UpdatedAt = updatedAt;
        }
    }

    public sealed partial class WorldStateStore
    {
        private static List<ConversationMood> CopyConversationMoods(WorldState source)
        {
            var result = new List<ConversationMood>();
            Require(source.ConversationMoods != null, "Missing conversation mood records.");

            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in source.ConversationMoods)
            {
                Require(item != null && !string.IsNullOrWhiteSpace(item.PersonId) && item.PersonId.Length <= 240 &&
                    identities.Add(item.PersonId) && item.Value >= -100 && item.Value <= 100 &&
                    item.AcceptedTurnSequence >= 0, "Invalid conversation mood.");

                bool hasTimeAnchor = item.HasTimeAnchor;
                double updatedAt = item.UpdatedAt;
                long revision = item.Revision;
                double halfLife = item.RecoveryHalfLifeSeconds;
                Require(Finite(updatedAt) && updatedAt >= 0 && revision >= 0,
                    "Invalid conversation mood timing or revision.");
                Require(Finite(halfLife) && halfLife >= 0 && halfLife <= 86400,
                    "Invalid anchored mood recovery policy.");
                if (hasTimeAnchor && halfLife == 0) halfLife = MoodRecovery.HalfLifeSeconds(source.WorldSeed, item.PersonId);
                result.Add(new ConversationMood
                {
                    PersonId = item.PersonId,
                    Value = item.Value,
                    AcceptedTurnSequence = item.AcceptedTurnSequence,
                    HasTimeAnchor = hasTimeAnchor,
                    UpdatedAt = updatedAt,
                    Revision = revision,
                    RecoveryHalfLifeSeconds = halfLife
                });
            }
            return result;
        }

        // Compatibility read: raw persisted value, with no evaluation or mutation.
        public ConversationMoodView CaptureConversationMood(string personId)
        {
            Require(!string.IsNullOrWhiteSpace(personId) && personId.Length <= 240,
                "Invalid conversation mood identity.");
            var mood = state.ConversationMoods.Find(item => item.PersonId == personId);
            return mood == null
                ? new ConversationMoodView(0, 0, 0, false, 0)
                : new ConversationMoodView(mood.Value, mood.AcceptedTurnSequence, mood.Revision,
                    mood.HasTimeAnchor, mood.UpdatedAt);
        }

        // Timed read is pure. It does not insert missing state or move the saved anchor.
        public ConversationMoodView CaptureConversationMood(string personId, double now)
        {
            Require(!string.IsNullOrWhiteSpace(personId) && personId.Length <= 240,
                "Invalid conversation mood identity.");
            Require(Finite(now) && now >= 0, "Invalid conversation mood read time.");
            var mood = state.ConversationMoods.Find(item => item.PersonId == personId);
            if (mood == null)
                return new ConversationMoodView(0, 0, 0, false, 0);
            int value = mood.HasTimeAnchor
                ? MoodRecovery.ValueAt(mood.Value, mood.UpdatedAt, now,
                    AnchoredMoodHalfLife(mood))
                : mood.Value;
            return new ConversationMoodView(value, mood.AcceptedTurnSequence, mood.Revision,
                mood.HasTimeAnchor, mood.UpdatedAt);
        }

        public bool TryCommitAcceptedSocialExchange(string personId, long expectedSequence, int moodDelta, double at,
            string date, string place, string playerText, string npcText, out long committedSequence)
        {
            long currentRevision = CurrentMoodRevision(personId);
            return TryCommitAcceptedSocialExchange(personId, expectedSequence, currentRevision, moodDelta, at,
                date, place, playerText, npcText, out committedSequence);
        }

        public bool TryCommitAcceptedSocialExchange(string personId, long expectedSequence, long expectedMoodRevision,
            int moodDelta, double at, string date, string place, string playerText, string npcText,
            out long committedSequence)
        {
            return TryCommitAcceptedMoodExchange(personId, expectedSequence, expectedMoodRevision, moodDelta, at,
                date, place, playerText, npcText, "model", null, null, NpcExperienceAppliedEffectsMask.None, out committedSequence);
        }

        public bool TryCommitAcceptedSocialExchangeWithObservedSpeech(string personId, long expectedSequence,
            long expectedMoodRevision, int moodDelta, double at, string date, string place, string playerText,
            string npcText, NpcObservedSpeechReceipt observedSpeech,
            NpcExperienceAppliedEffectsMask alreadyAppliedEffectsMask, out long committedSequence)
        {
            committedSequence = 0;
            return false;
        }

        public bool TryCommitAcceptedSocialExchangeWithObservedSpeech(string personId, long expectedSequence,
            long expectedMoodRevision, int moodDelta, double at, string date, string place, string playerText,
            string npcText, NpcObservedSpeechTicket ticket, NpcObservedSpeechReceipt observedSpeech,
            NpcExperienceAppliedEffectsMask alreadyAppliedEffectsMask, out long committedSequence)
        {
            return TryCommitAcceptedMoodExchange(personId, expectedSequence, expectedMoodRevision, moodDelta, at,
                date, place, playerText, npcText, "model", ticket, observedSpeech, alreadyAppliedEffectsMask, out committedSequence);
        }

        public bool TryCommitAcceptedActionExchange(string personId, long expectedSequence, int moodDelta, double at,
            string date, string place, string playerText, string npcText, out long committedSequence)
        {
            long currentRevision = CurrentMoodRevision(personId);
            return TryCommitAcceptedActionExchange(personId, expectedSequence, currentRevision, moodDelta, at,
                date, place, playerText, npcText, out committedSequence);
        }

        private long CurrentMoodRevision(string personId)
        {
            if (string.IsNullOrWhiteSpace(personId) || personId.Length > 240) return 0;
            var mood = state.ConversationMoods.Find(item => item.PersonId == personId);
            return mood == null ? 0 : mood.Revision;
        }

        public bool TryCommitAcceptedActionExchange(string personId, long expectedSequence, long expectedMoodRevision,
            int moodDelta, double at, string date, string place, string playerText, string npcText,
            out long committedSequence)
        {
            return TryCommitAcceptedMoodExchange(personId, expectedSequence, expectedMoodRevision, moodDelta, at,
                date, place, playerText, npcText, "action", null, null, NpcExperienceAppliedEffectsMask.None, out committedSequence);
        }

        public bool TryCommitAcceptedActionExchangeWithObservedSpeech(string personId, long expectedSequence,
            long expectedMoodRevision, int moodDelta, double at, string date, string place, string playerText,
            string npcText, NpcObservedSpeechReceipt observedSpeech,
            NpcExperienceAppliedEffectsMask alreadyAppliedEffectsMask, out long committedSequence)
        {
            committedSequence = 0;
            return false;
        }

        public bool TryCommitAcceptedActionExchangeWithObservedSpeech(string personId, long expectedSequence,
            long expectedMoodRevision, int moodDelta, double at, string date, string place, string playerText,
            string npcText, NpcObservedSpeechTicket ticket, NpcObservedSpeechReceipt observedSpeech,
            NpcExperienceAppliedEffectsMask alreadyAppliedEffectsMask, out long committedSequence)
        {
            return TryCommitAcceptedMoodExchange(personId, expectedSequence, expectedMoodRevision, moodDelta, at,
                date, place, playerText, npcText, "action", ticket, observedSpeech, alreadyAppliedEffectsMask, out committedSequence);
        }

        private bool TryCommitAcceptedMoodExchange(string personId, long expectedSequence, long expectedMoodRevision,
            int moodDelta, double at, string date, string place, string playerText, string npcText, string source,
            NpcObservedSpeechTicket ticket, NpcObservedSpeechReceipt observedSpeech,
            NpcExperienceAppliedEffectsMask alreadyAppliedEffectsMask,
            out long committedSequence)
        {
            committedSequence = 0;
            if (string.IsNullOrWhiteSpace(personId) || personId.Length > 240 || expectedSequence < 0 ||
                expectedMoodRevision < 0 || moodDelta < -10 || moodDelta > 10 || !Finite(at) || at < 0)
                return false;

            var mood = state.ConversationMoods.Find(item => item.PersonId == personId);
            long currentSequence = mood == null ? 0 : mood.AcceptedTurnSequence;
            long currentRevision = mood == null ? 0 : mood.Revision;
            if (currentSequence != expectedSequence || currentRevision != expectedMoodRevision ||
                currentSequence == long.MaxValue || currentRevision == long.MaxValue ||
                mood != null && mood.HasTimeAnchor && at < mood.UpdatedAt)
                return false;

            var entry = new NpcMemoryEntry
            {
                At = at,
                Date = CleanMemory(date, 120),
                Place = CleanMemory(place, 300),
                PlayerText = CleanMemory(playerText, 600),
                NpcText = CleanMemory(npcText, 1600),
                Observation = "",
                Source = source
            };
            try
            {
                ValidateMemoryEntry(entry);
            }
            catch (ArgumentException)
            {
                return false;
            }

            NpcExperienceInbox nextSpeechInbox = null;
            List<NpcObservedSpeechSourceState> nextSpeechSources = null;
            if (observedSpeech != null)
            {
                const NpcExperienceAppliedEffectsMask allowedSpeechMask =
                    NpcExperienceAppliedEffectsMask.Mood | NpcExperienceAppliedEffectsMask.Affinity;
                if (alreadyAppliedEffectsMask != (alreadyAppliedEffectsMask & allowedSpeechMask) ||
                    (alreadyAppliedEffectsMask & NpcExperienceAppliedEffectsMask.Mood) == 0) return false;
                try
                {
                    var speech = NpcObservedSpeechReceipt.CopyValidated(observedSpeech);
                    if (speech.Evidence.ObserverId != personId || speech.Turn.Text != playerText ||
                        speech.Turn.ObservedGameTime > at) return false;
                    NpcExperienceInboxEntry speechEntry;
                    if (!TryPrepareObservedSpeechAcceptance(ticket, speech, alreadyAppliedEffectsMask,
                        out nextSpeechSources, out speechEntry) ||
                        !TryPrepareObservedSpeechInbox(speechEntry, out nextSpeechInbox)) return false;
                    nextSpeechSources.Find(x => x.PersonId == personId).LastProjectedOrdinal = ticket.Ordinal;
                }
                catch (ArgumentException) { return false; }
                catch (InvalidOperationException) { return false; }
            }
            else if (alreadyAppliedEffectsMask != NpcExperienceAppliedEffectsMask.None)
                return false;

            int currentValue = mood == null ? 0 : mood.Value;
            if (mood != null && mood.HasTimeAnchor)
                currentValue = MoodRecovery.ValueAt(currentValue, mood.UpdatedAt, at,
                    AnchoredMoodHalfLife(mood));
            int acceptedMoodDelta = AcceptedMoodDelta(personId, source, currentValue, moodDelta, at);
            int nextValue = Math.Max(-100, Math.Min(100, currentValue + acceptedMoodDelta));
            double nextHalfLife = ProfileMoodHalfLife(personId);
            long nextSequence = currentSequence + 1;
            long nextRevision = currentRevision + 1;

            // Allocate each persisted row and backing-list capacity before commit.
            var journal = MemoryFor(personId, false) ?? new NpcConversationMemory { PersonId = personId };
            var pending = state.ConversationMemories.Find(item => item.PersonId == personId);
            bool addPending = pending == null;
            if (addPending) pending = new NpcConversationMemory { PersonId = personId };
            bool addMood = mood == null;
            if (addMood) mood = new ConversationMood { PersonId = personId };
            var attitude = state.ConversationAttitudes.Find(item => item.PersonId == personId);
            bool addAttitude = attitude == null;
            if (addAttitude) attitude = new ConversationAttitude { PersonId = personId, Day = Math.Floor(at / 86400d) };

            entry.At = Math.Floor(entry.At);
            journal.Entries.Capacity = Math.Max(journal.Entries.Capacity, journal.Entries.Count + 1);
            pending.Entries.Capacity = Math.Max(pending.Entries.Capacity, pending.Entries.Count + 1);
            if (addPending)
                state.ConversationMemories.Capacity = Math.Max(state.ConversationMemories.Capacity,
                    state.ConversationMemories.Count + 1);
            if (addMood)
                state.ConversationMoods.Capacity = Math.Max(state.ConversationMoods.Capacity, state.ConversationMoods.Count + 1);
            if (addAttitude)
                state.ConversationAttitudes.Capacity = Math.Max(state.ConversationAttitudes.Capacity,
                    state.ConversationAttitudes.Count + 1);

            // Cache updates are lazy: rebuilt journals invalidate any old feature index by list identity.
            journal.Entries.Add(entry);
            pending.Entries.Add(entry);
            if (addPending) state.ConversationMemories.Add(pending);
            pendingConversationMemoryEpoch = new object();
            if (addMood) state.ConversationMoods.Add(mood);
            mood.Value = nextValue;
            mood.HasTimeAnchor = true;
            mood.UpdatedAt = at;
            mood.AcceptedTurnSequence = nextSequence;
            mood.Revision = nextRevision;
            mood.RecoveryHalfLifeSeconds = nextHalfLife;
            if (nextSpeechInbox != null) state.NpcExperienceInbox = nextSpeechInbox;
            if (nextSpeechSources != null) { state.NpcObservedSpeechSources = nextSpeechSources; ticket.Accepted = true; }
            if (addAttitude) state.ConversationAttitudes.Add(attitude);
            if (attitude.SharedExchangeCount < 1000000) attitude.SharedExchangeCount++;
            sharedExchangeLookups.Remove(personId);
            memoryIndex = null;
            memoryIndexOwner = null;
            committedSequence = nextSequence;
            return true;
        }

        private int AcceptedMoodDelta(string personId, string source, int currentValue,
            int proposedDelta, double at)
        {
            // A saved hostile boundary represents a serious offense. Friendly model replies can
            // ease its mood impact over accepted turns, but cannot erase it in one or two exchanges.
            if (source != "model" || currentValue >= 0 || proposedDelta <= 2) return proposedDelta;
            NpcConversationBoundaryRecord boundary = ReadNpcConversationBoundary(personId, at);
            return boundary == null ? proposedDelta : 2;
        }

        public double ConversationMoodHalfLifeSeconds(string personId)
        {
            Require(!string.IsNullOrWhiteSpace(personId) && personId.Length <= 240, "Invalid mood recovery identity.");
            var mood = state.ConversationMoods.Find(x => x.PersonId == personId);
            return mood != null && mood.HasTimeAnchor ? AnchoredMoodHalfLife(mood) : ProfileMoodHalfLife(personId);
        }
        private double ProfileMoodHalfLife(string personId)
        {
            var profile = ActiveNpcPersonality(state, personId);
            return profile == null ? MoodRecovery.HalfLifeSeconds(state.WorldSeed, personId)
                : MoodRecovery.HalfLifeSeconds(state.WorldSeed, profile);
        }
        private double AnchoredMoodHalfLife(ConversationMood mood)
        { return mood.RecoveryHalfLifeSeconds > 0 ? mood.RecoveryHalfLifeSeconds : MoodRecovery.HalfLifeSeconds(state.WorldSeed,mood.PersonId); }
    }
}
