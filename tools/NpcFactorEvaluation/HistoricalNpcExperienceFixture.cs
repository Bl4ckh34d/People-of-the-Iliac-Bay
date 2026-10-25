using System;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.NpcFactorEvaluation
{
    /// <summary>
    /// Inserts synthetic historical inbox evidence into an isolated evaluation save.
    /// This tests saved-event appraisal; it does not attest a live native adapter outcome.
    /// </summary>
    internal static class HistoricalNpcExperienceFixture
    {
        internal static bool TryRestoreNativeOutcome(WorldStateStore store, NpcExperienceEvent syntheticEvidence)
        {
            NpcExperienceInboxEntry row;
            try { row = NpcExperienceInboxEntry.FromNativeEvent(syntheticEvidence); }
            catch (ArgumentException) { return false; }
            return TryRestoreRow(store, row);
        }

        internal static bool TryRestoreObservedSpeech(WorldStateStore store, NpcObservedSpeechReceipt syntheticReceipt,
            NpcExperienceAppliedEffectsMask appliedEffects)
        {
            NpcExperienceInboxEntry row;
            try { row = NpcExperienceInboxEntry.FromObservedSpeech(syntheticReceipt, appliedEffects); }
            catch (ArgumentException) { return false; }
            return TryRestoreRow(store, row);
        }

        private static bool TryRestoreRow(WorldStateStore store, NpcExperienceInboxEntry row)
        {
            if (store == null || row == null) return false;
            WorldState saved = store.Snapshot();
            if (saved.NpcExperienceInbox == null || saved.NpcExperienceInbox.Entries == null ||
                saved.NpcExperienceInbox.Revision == long.MaxValue) return false;
            if (saved.NpcExperienceInbox.HasOutcome(row.ObserverId, row.DeduplicationKey)) return false;
            // A historical import must remain a contiguous prefix; never relabel live events.
            if (saved.NpcExperienceInbox.LegacyImportedRevision != saved.NpcExperienceInbox.Revision) return false;
            row.EnqueueOrdinal = saved.NpcExperienceInbox.Revision + 1;
            saved.NpcExperienceInbox.Entries.Add(row);
            saved.NpcExperienceInbox.Revision++;
            saved.NpcExperienceInbox.LegacyImportedRevision = saved.NpcExperienceInbox.Revision;

            // Restore validates the complete saved WorldState and known resident identities.
            // Preflight on a detached store keeps a malformed fixture out of the caller's store.
            try
            {
                var validated = new WorldStateStore(saved.WorldSeed);
                validated.Restore(saved);
                if (!ContainsExactPending(validated, row)) return false;
                store.Restore(saved);
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            if (!ContainsExactPending(store, row))
                throw new InvalidOperationException("Validated historical experience fixture was not restored exactly.");
            return true;
        }

        private static bool ContainsExactPending(WorldStateStore store, NpcExperienceInboxEntry row)
        {
            foreach (NpcExperienceInboxEntry pending in store.Snapshot().NpcExperienceInbox.Entries)
                if (pending.Status == NpcExperienceInboxStatus.Pending && pending.Origin == row.Origin &&
                    pending.ObserverId == row.ObserverId && pending.SubjectPersonId == row.SubjectPersonId &&
                    pending.DeduplicationKey == row.DeduplicationKey && pending.ReceiptId == row.ReceiptId &&
                    pending.EpisodeId == row.EpisodeId && pending.OutcomeId == row.OutcomeId &&
                    pending.UtteranceId == row.UtteranceId && pending.AttributedSpeakerId == row.AttributedSpeakerId &&
                    pending.EventKind == row.EventKind && pending.AlreadyAppliedEffectsMask == row.AlreadyAppliedEffectsMask &&
                    pending.HasKnownImmediateAffinityDelta == row.HasKnownImmediateAffinityDelta &&
                    pending.ImmediateAffinityDelta == row.ImmediateAffinityDelta &&
                    pending.AffinityExclusion == row.AffinityExclusion &&
                    pending.ObservedTurnText == row.ObservedTurnText &&
                    pending.ObservedTurnGameTime == row.ObservedTurnGameTime)
                    return true;
            return false;
        }
    }
}
