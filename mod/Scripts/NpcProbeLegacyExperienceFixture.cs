using System;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    // Probe-only migration fixture for historical native outcomes with explicit
    // receipt IDs. Call only before capturing tickets or attaching live actors:
    // Restore deliberately invalidates all store binding epochs.
    internal static class NpcProbeLegacyExperienceFixture
    {
        internal static void Import(WorldStateStore isolatedStore, params NpcExperienceEvent[] historicalOutcomes)
        {
            if (isolatedStore == null || historicalOutcomes == null || historicalOutcomes.Length == 0)
                throw new ArgumentException("Historical experience fixture needs a store and outcomes.");
            WorldState snapshot = isolatedStore.Snapshot();
            NpcExperienceInbox inbox = snapshot.NpcExperienceInbox;
            if (inbox == null || inbox.Entries == null || inbox.Revision > long.MaxValue - historicalOutcomes.Length)
                throw new InvalidOperationException("Historical experience fixture inbox is invalid or full.");
            foreach (NpcExperienceEvent raw in historicalOutcomes)
            {
                NpcExperienceEvent evidence = NpcExperienceEvent.CopyValidated(raw);
                if (evidence.Origin != NpcExperienceEventOrigin.NativeOutcome || !evidence.EventKind.HasValue ||
                    inbox.HasOutcome(evidence.ObserverId, evidence.DeduplicationKey))
                    throw new InvalidOperationException("Historical experience fixture must contain distinct native outcomes.");
                var entry = NpcExperienceInboxEntry.FromNativeEvent(evidence);
                entry.EnqueueOrdinal = ++inbox.Revision;
                inbox.Entries.Add(entry);
            }
            isolatedStore.Restore(snapshot); // Full WorldState/Inbox validation, including known identities.
        }
    }
}
