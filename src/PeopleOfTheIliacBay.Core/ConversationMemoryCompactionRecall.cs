using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        private sealed class CompactionRecallMapping
        {
            internal readonly HashSet<NpcMemoryEntry> CoveredArchiveRows = new HashSet<NpcMemoryEntry>();
            internal readonly List<NpcMemoryEntry> SelectedRows = new List<NpcMemoryEntry>();
        }

        public string RecallConversationWithCompaction(string personId, string query, double now,
            int maximumCharacters, ConversationMemoryCompactionCache cache,
            Func<string, IReadOnlyList<SharedConversationArchive.ScanRecord>, ConversationMemoryCompactionContextSnapshot> contextProvider = null)
        {
            if (!Finite(now) || now < 0 || maximumCharacters < 256)
                throw new ArgumentOutOfRangeException(nameof(now), "Invalid memory retrieval limits.");
            if (cache == null) return RecallConversation(personId, query, now, maximumCharacters);
            NpcConversationMemory journal = MemoryFor(personId, false);
            if (journal == null) return RecallConversation(personId, query, now, maximumCharacters);

            ConversationArchiveCheckpointView checkpoint;
            try { checkpoint = CaptureConversationArchiveCheckpoint(); }
            catch { return RecallConversation(personId, query, now, maximumCharacters); }

            var mappings = new Dictionary<string, CompactionRecallMapping>(StringComparer.Ordinal);
            Func<ConversationMemoryCompactionCacheGroup, bool> validateSource = group =>
            {
                CompactionRecallMapping mapping;
                if (!TryMapCompactionGroupToJournal(group, journal.Entries, now, out mapping)) return false;
                mappings[CompactionGroupKey(group)] = mapping;
                return true;
            };
            List<ConversationMemoryCompactionCacheGroup> groups;
            if (!cache.TryReadIndexedGroups(checkpoint, personId, now, contextProvider, validateSource, out groups) || groups.Count == 0)
                return RecallConversation(personId, query, now, maximumCharacters);

            var eligible = journal.Entries.Where(entry => entry.At <= now && entry.Observation != "Conversation started.").ToList();
            if (eligible.Count == 0) return RecallConversation(personId, query, now, maximumCharacters);

            ConversationFeatureIndex readyIndex;
            bool indexed = conversationFeatureIndexes.TryGetReady(personId, journal.Entries, state, out readyIndex);
            ConversationRetrieval retrieval;
            List<NpcMemoryEntry> ranked;
            NpcMemoryEntry meeting;
            HashSet<string> queryFeatures;
            if (indexed)
            {
                ranked = readyIndex.Rank(query, now).Select(readyIndex.EntryAt)
                    .Where(entry => entry.At <= now && entry.Observation != "Conversation started.").ToList();
                retrieval = new ConversationRetrieval(readyIndex, query, now);
                queryFeatures = readyIndex.ResolveQueryFeatures(query, now);
                meeting = readyIndex.LatestMeeting(now);
            }
            else
            {
                var retrievalEntries = eligible.Select(entry => entry.Source == "unavailable" ? new NpcMemoryEntry
                {
                    At = entry.At, Date = entry.Date, Place = entry.Place, PlayerText = entry.PlayerText,
                    NpcText = "", Observation = entry.Observation, Source = entry.Source
                } : entry).ToList();
                retrieval = new ConversationRetrieval(retrievalEntries, query);
                ranked = eligible.Select((entry, index) => new { Entry = entry, Index = index, Score = retrieval.Score(retrievalEntries[index]) })
                    .OrderByDescending(item => item.Score).ThenByDescending(item => item.Entry.At)
                    .ThenByDescending(item => item.Index).Select(item => item.Entry).ToList();
                queryFeatures = ConversationFeatureExtractor.Extract(query);
                if (queryFeatures.Count == 0)
                {
                    NpcMemoryEntry latest = eligible.Where(entry => entry.Source != "observed")
                        .OrderByDescending(entry => entry.At).FirstOrDefault();
                    if (latest != null) queryFeatures = ConversationFeatureExtractor.Extract(latest.PlayerText + " " + latest.NpcText);
                }
                meeting = eligible.LastOrDefault(entry => entry.Source == "observed" &&
                    entry.Observation == "Met and spoke with the player here.");
            }

            var coveredArchiveRows = new HashSet<NpcMemoryEntry>();
            foreach (ConversationMemoryCompactionCacheGroup group in groups)
            {
                CompactionRecallMapping mapping;
                if (!mappings.TryGetValue(CompactionGroupKey(group), out mapping))
                    return RecallConversation(personId, query, now, maximumCharacters);
                coveredArchiveRows.UnionWith(mapping.CoveredArchiveRows);
            }

            // Query-matched raw sources outrank every selected source. Only top
            // ranked matches are retained; the rest of a covered month is omitted.
            var outputOrder = new List<NpcMemoryEntry>();
            var added = new HashSet<NpcMemoryEntry>();
            if (queryFeatures.Count > 0)
            {
                int rawHitCount = 0;
                foreach (NpcMemoryEntry entry in ranked.Take(64))
                {
                    if (!HasQueryFeature(entry, queryFeatures) || !added.Add(entry)) continue;
                    outputOrder.Add(entry);
                    if (++rawHitCount == 8) break;
                }
            }

            foreach (ConversationMemoryCompactionCacheGroup group in groups.OrderByDescending(item => item.MonthIndex))
            {
                CompactionRecallMapping mapping = mappings[CompactionGroupKey(group)];
                foreach (NpcMemoryEntry entry in mapping.SelectedRows)
                    if (added.Add(entry)) outputOrder.Add(entry);
            }

            foreach (NpcMemoryEntry entry in ranked)
                if (!coveredArchiveRows.Contains(entry) && added.Add(entry)) outputOrder.Add(entry);

            return FormatConversationMemory(eligible.Count, outputOrder, meeting, now, maximumCharacters, retrieval);
        }

        private static bool TryMapCompactionGroupToJournal(ConversationMemoryCompactionCacheGroup group,
            IList<NpcMemoryEntry> journal, double now, out CompactionRecallMapping mapping)
        {
            mapping = null;
            if (group == null || journal == null || group.Candidates.Count == 0 || group.Sources.Count == 0) return false;
            var candidateCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (SharedConversationArchive.ScanRecord candidate in group.Candidates)
            {
                if (candidate == null || candidate.Entry.At > now ||
                    !ConversationMemoryCompactionEligibility.IsEligible(candidate.Entry.At, now)) return false;
                string fingerprint = ConversationMemoryCompactionCache.EntryFingerprint(candidate.Entry);
                int count; candidateCounts.TryGetValue(fingerprint, out count); candidateCounts[fingerprint] = count + 1;
            }
            var remaining = new Dictionary<string, int>(candidateCounts, StringComparer.Ordinal);
            var archiveRows = new Dictionary<string, List<NpcMemoryEntry>>(StringComparer.Ordinal);
            foreach (NpcMemoryEntry entry in journal)
            {
                if (entry.At > now || ConversationMemoryNativeMonth.IndexAt(entry.At) != group.MonthIndex) continue;
                string fingerprint = ConversationMemoryCompactionCache.EntryFingerprint(entry);
                int count;
                if (!remaining.TryGetValue(fingerprint, out count) || count == 0) continue;
                remaining[fingerprint] = count - 1;
                List<NpcMemoryEntry> rows;
                if (!archiveRows.TryGetValue(fingerprint, out rows))
                { rows = new List<NpcMemoryEntry>(); archiveRows.Add(fingerprint, rows); }
                rows.Add(entry);
            }
            if (remaining.Values.Any(count => count != 0)) return false;

            var selectedCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (SharedConversationArchive.ScanRecord selected in group.Sources)
            {
                string fingerprint = ConversationMemoryCompactionCache.EntryFingerprint(selected.Entry);
                int count; selectedCounts.TryGetValue(fingerprint, out count); selectedCounts[fingerprint] = count + 1;
            }
            var result = new CompactionRecallMapping();
            foreach (KeyValuePair<string, int> pair in candidateCounts)
            {
                List<NpcMemoryEntry> rows = archiveRows[pair.Key];
                foreach (NpcMemoryEntry row in rows) result.CoveredArchiveRows.Add(row);
                int selectedCount;
                if (!selectedCounts.TryGetValue(pair.Key, out selectedCount)) continue;
                if (selectedCount < 0 || selectedCount > rows.Count) return false;
                result.SelectedRows.AddRange(rows.Take(selectedCount));
            }
            if (selectedCounts.Any(pair => !candidateCounts.ContainsKey(pair.Key))) return false;
            mapping = result;
            return true;
        }

        private static bool HasQueryFeature(NpcMemoryEntry entry, HashSet<string> queryFeatures)
        {
            string text = entry.Source == "unavailable" ? entry.PlayerText :
                entry.PlayerText + " " + entry.NpcText + " " + entry.Place + " " + entry.Date + " " + entry.Observation;
            HashSet<string> features = ConversationFeatureExtractor.Extract(text);
            return features.Overlaps(queryFeatures);
        }

        private static string CompactionGroupKey(ConversationMemoryCompactionCacheGroup group)
        { return group.MonthIndex.ToString(CultureInfo.InvariantCulture) + ":" + group.SourceFingerprint + ":" + group.ContextFingerprint; }
    }
}
