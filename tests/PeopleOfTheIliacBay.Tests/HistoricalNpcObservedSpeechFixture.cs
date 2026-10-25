using System;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    /// <summary>
    /// Imports an already-observed legacy speech row for appraisal and recall tests.
    /// This is historical save data, not authority to admit a new spoken turn.
    /// </summary>
    internal static class HistoricalNpcObservedSpeechFixture
    {
        internal static void Import(WorldStateStore store, NpcExperienceEvent evidence,
            NpcObservedConversationTurn turn, NpcExperienceAppliedEffectsMask mask)
        {
            Import(store, NpcObservedSpeechReceipt.Create(evidence, turn), mask);
        }

        internal static void Import(WorldStateStore store, NpcObservedSpeechReceipt receipt,
            NpcExperienceAppliedEffectsMask mask)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            var saved = store.Snapshot();
            var entry = NpcExperienceInboxEntry.FromObservedSpeech(receipt, mask);
            entry.EnqueueOrdinal = checked(++saved.NpcExperienceInbox.Revision);
            saved.NpcExperienceInbox.DetailRevision++;
            saved.NpcExperienceInbox.Entries.Add(entry);
            store.Restore(saved);
        }
    }
}
