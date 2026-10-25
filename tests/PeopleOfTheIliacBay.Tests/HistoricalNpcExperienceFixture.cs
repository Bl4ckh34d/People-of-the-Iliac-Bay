using System;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    /// <summary>
    /// Seeds an already-observed native outcome as historical save data for appraisal/read tests.
    /// This is a fixture import, not an admission path for a new native observation.
    /// </summary>
    internal static class HistoricalNpcExperienceFixture
    {
        internal static bool ImportHistoricalNativeEvent(WorldStateStore store, NpcExperienceEvent evidence)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            var saved = store.Snapshot();
            var entry = NpcExperienceInboxEntry.FromNativeEvent(evidence);
            entry.EnqueueOrdinal = checked(++saved.NpcExperienceInbox.Revision);
            saved.NpcExperienceInbox.DetailRevision++;
            saved.NpcExperienceInbox.Entries.Add(entry);
            store.Restore(saved); // Full schema and known-identity validation, atomically.
            return true;
        }

        // Construct the old envelope before a migration/corruption test changes its target domain.
        // Never use this to downgrade retired history or as evidence of a production migration.
        internal static void PrepareLegacyEnvelope(WorldState saved, int schema)
        {
            if (schema >= 54) throw new ArgumentOutOfRangeException(nameof(schema));
            saved.Version = schema;
            saved.ConversationCueArchive = null;
            saved.NpcMoodCauseHistoryFloors.Clear();
            if (saved.NpcExperienceInbox != null)
                PrepareLegacyInbox(saved.NpcExperienceInbox, schema < 38 ? 1 : 4);
        }

        internal static void PrepareLegacyInbox(NpcExperienceInbox inbox, int version)
        {
            if (inbox.RetiredCount != 0)
                throw new InvalidOperationException("A legacy fixture cannot discard retired history.");
            inbox.Version = version;
            inbox.DetailRevision = 0;
            inbox.LegacyImportedRevision = 0;
            foreach (var row in inbox.Entries)
            {
                row.EnqueueOrdinal = 0;
                row.SourceVitalsRevision = 0;
                row.SourceKind = NpcExperienceSourceKind.LegacyUnknown;
            }
        }
    }
}
