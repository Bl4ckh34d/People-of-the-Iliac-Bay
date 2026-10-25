using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int assertions;

    private static void Main()
    {
        NativeCalendarMonthUsesDaggerfallEpochSeconds();
        WholeMonthWaitsForEveryRawSourceToAge();
        SelectionIsStrictIdOnlyAndPublishesOneSourceBoundCache();
        OversizedMonthStaysRawRatherThanPartiallyCovered();
        QueryKeepsAnOmittedRawHitAlongsideTheSelectedSummarySource();
        PendingDuplicatesRemainDistinctAndRaw();
        CheckpointAndPersonChangesFallBackToRaw();
        JobLocalSkipCanBeRetriedAfterRestart();
        IneligibleFirstPageDoesNotDropLaterEligibleGroup();
        CachedFirstPageDoesNotBlockTheUncachedSeventeenthGroup();
        CacheManifestSupportsMoreThan128MonthsForOnePerson();
        SameCheckpointContextChangeFallsBackToRaw();
        ContextChangeBeforeAcceptanceReissuesSelection();
        IndexedRecallBoundsRejectedSidecarReads();
        FullManifestCanReplaceTheCurrentMonth();
        CorruptSidecarFallsBackToRaw();
        Console.WriteLine("Conversation memory compaction regressions passed: " + assertions + " assertions.");
    }

    private static void NativeCalendarMonthUsesDaggerfallEpochSeconds()
    {
        const double month = 2592000d;
        Equal(0L, ConversationMemoryNativeMonth.IndexAt(0), "DFU epoch begins in month index zero");
        Equal(0L, ConversationMemoryNativeMonth.IndexAt(month - 1), "Last second before a month boundary stays in prior month");
        Equal(1L, ConversationMemoryNativeMonth.IndexAt(month), "Month boundary advances the native month index");
        Equal(24L, ConversationMemoryNativeMonth.IndexAt(24 * month + 17), "Thirty-day year rollover preserves absolute month numbering");
        Equal(24 * month, ConversationMemoryNativeMonth.StartAt(24), "Month start uses the DFU zero-offset epoch");
        Reject(() => ConversationMemoryNativeMonth.IndexAt(-1));
        Reject(() => ConversationMemoryNativeMonth.IndexAt(double.PositiveInfinity));
    }

    private static void WholeMonthWaitsForEveryRawSourceToAge()
    {
        const double month = 2592000d;
        WithDirectory(root => {
            var store = new WorldStateStore(7, root);
            Say(store, "alice", 1, "An old opening claim.", "The old reply.");
            Say(store, "alice", month - 1, "The last native-month claim.", "The last reply.");
            var saved = store.SnapshotForSave();
            var cache = Cache(root);

            var beforeWholeMonthAges = Job(root, saved, month + 1, cache);
            Drain(beforeWholeMonthAges);
            Check.True(!beforeWholeMonthAges.TryGetSelectionRequest(out _),
                "A month with one younger raw row cannot be marked covered by only its aged subset");
            Check.True(!cache.HasCompletedGroup(store.CaptureConversationArchiveCheckpoint(), "alice", 0),
                "A mixed-age month must have no published cache group");

            var afterWholeMonthAges = Job(root, saved, month * 2, cache);
            Drain(afterWholeMonthAges);
            Check.True(afterWholeMonthAges.TryGetSelectionRequest(out var request),
                "A complete calendar month becomes selectable after every source passes the strict age gate");
            Equal(0L, request.MonthIndex, "The request identifies its native month");
        });
    }

    private static void SelectionIsStrictIdOnlyAndPublishesOneSourceBoundCache()
    {
        const double month = 2592000d;
        WithDirectory(root => {
            var store = new WorldStateStore(8, root);
            Say(store, "alice", 10, "Claimed a silver ring.", "I heard about the ring.");
            Say(store, "alice", 20, "Discussed a road trip.", "We spoke about the road.");
            var saved = store.SnapshotForSave();
            var checkpoint = store.CaptureConversationArchiveCheckpoint();
            var cache = Cache(root);
            var job = Job(root, saved, month * 3, cache);
            Drain(job);
            Check.True(job.TryGetSelectionRequest(out var request), "A fully scanned eligible month yields a model-selection request");
            Equal("alice", request.PersonId, "Selection is scoped to one speaker identity");
            Equal(0L, request.MonthIndex, "Selection is scoped to the source month");
            Equal(128, request.ResponseTokens, "The classifier output budget stays bounded at 128 tokens");
            Check.True(Encoding.UTF8.GetByteCount(request.Prompt) <= ConversationMemoryCompactionJob.MaximumPromptUtf8Bytes,
                "The complete bounded prompt stays within the conservative model input budget");
            Check.True(request.Prompt.Contains("1") && request.Prompt.Contains("2") && request.Prompt.Contains("silver ring"),
                "The prompt exposes bounded candidate IDs and their source text");

            Check.True(!job.AcceptSelection(request.RequestId, "{\"ids\":[]}"), "Empty selection is rejected");
            Check.True(!job.AcceptSelection(request.RequestId, "{\"ids\":[1,1]}"), "Duplicate IDs are rejected");
            Check.True(!job.AcceptSelection(request.RequestId, "{\"ids\":[999]}"), "Unknown IDs are rejected");
            Check.True(!job.AcceptSelection(request.RequestId, "{\"ids\":[1],\"fact\":\"invented\"}"),
                "Free-form model facts are rejected");
            Check.True(job.AcceptSelection(request.RequestId, "{\"ids\":[1]}"), "A valid source ID is accepted");
            Check.True(cache.HasCompletedGroup(checkpoint, "alice", 0), "Only validated selection publishes a completed group");
            MethodInfo markSkipped = typeof(ConversationMemoryCompactionCache).GetMethod("MarkSkipped", BindingFlags.Instance | BindingFlags.NonPublic);
            Check.True(markSkipped != null && (bool)markSkipped.Invoke(cache,
                new object[] { checkpoint, "alice", 0L, new string('c', 64) }),
                "A retry status can coexist with a completed group in the bounded index");
            Check.True(cache.HasCompletedGroup(checkpoint, "alice", 0),
                "A valid completed group remains readable beside a skipped index entry");
            Check.True(cache.TryReadGroups(checkpoint, "alice", month * 3, out var groups), "The completed group survives cache reload");
            Equal(1, groups.Count, "Exactly one person/month group was published");
            Equal(1, groups[0].Sources.Count, "Only selected source rows enter the compacted view");
            Check.True(groups[0].Sources[0].Address.ArchiveId == checkpoint.ArchiveId &&
                groups[0].Sources[0].Address.CheckpointHead == checkpoint.Head &&
                groups[0].Sources[0].Address.PersonId == "alice",
                "Persisted selection remains bound to exact archive, checkpoint and person");
            Check.True(groups[0].Sources[0].Entry.PlayerText == "Discussed a road trip.",
                "Selected output is the exact source row associated with model ID 1");
            job.Cancel();
        });
    }

    private static void OversizedMonthStaysRawRatherThanPartiallyCovered()
    {
        const double month = 2592000d;
        WithDirectory(root => {
            var store = new WorldStateStore(9, root);
            for (int n = 0; n < 8; n++)
                Say(store, "alice", n + 1, (n == 0 ? "A distant emerald token " : "") + new string((char)('a' + n), 500), new string('r', 500));
            var saved = store.SnapshotForSave();
            int rawCount = new SharedConversationArchive(root).Load(saved.ConversationArchive).Single().Entries.Count;
            var cache = Cache(root);
            var job = Job(root, saved, month * 3, cache);
            Drain(job);
            Check.True(!job.TryGetSelectionRequest(out _), "An over-budget month yields no partial model request");
            Check.True(!cache.HasCompletedGroup(store.CaptureConversationArchiveCheckpoint(), "alice", 0),
                "An over-budget month is not marked covered");
            Equal(rawCount, new SharedConversationArchive(root).Load(saved.ConversationArchive).Single().Entries.Count,
                "Compaction never deletes or rewrites raw archive entries");
            Check.True(store.RecallConversationWithCompaction("alice", "emerald", month * 3, 2000, cache).Contains("emerald"),
                "Raw fallback retains oversized source history");
        });
    }

    private static void QueryKeepsAnOmittedRawHitAlongsideTheSelectedSummarySource()
    {
        const double month = 2592000d;
        WithDirectory(root => {
            var store = new WorldStateStore(10, root);
            Say(store, "alice", 10, "My mare Copper has a white star.", "I heard you describe Copper.");
            Say(store, "alice", 20, "We talked about the weather.", "The rain has passed.");
            Say(store, "alice", 30, "A third ordinary meeting.", "We exchanged greetings.");
            var saved = store.SnapshotForSave();
            var checkpoint = store.CaptureConversationArchiveCheckpoint();
            var cache = Cache(root);
            var job = Job(root, saved, month * 3, cache);
            Drain(job);
            Check.True(job.TryGetSelectionRequest(out var request), "Eligible month has one complete source-selection request");
            Check.True(job.AcceptSelection(request.RequestId, "{\"ids\":[1]}"), "One selected source is cached");

            string context = store.RecallConversationWithCompaction("alice", "What is my mare called? Copper", month * 3, 2000, cache);
            Check.True(context.Contains("Copper"), "A specific query can still retrieve an omitted raw source");
            Check.True(context.Contains("Player said:") && context.Contains("NPC said (native):"),
                "Selected and raw-hit lines retain exact speaker/source attribution");
            Check.True(context.Contains("rain has passed") || context.Contains("ordinary meeting"),
                "The source-selected compacted evidence remains available with the query-specific raw hit");
            Check.True(cache.HasCompletedGroup(checkpoint, "alice", 0), "The cache remains a derived prompt view for the exact checkpoint");
        });
    }

    private static void PendingDuplicatesRemainDistinctAndRaw()
    {
        const double month = 2592000d;
        WithDirectory(root => {
            var origin = new WorldStateStore(11, root);
            Say(origin, "alice", 10, "The player claimed a pearl.", "I heard the claim.");
            var saved = origin.SnapshotForSave();
            var cache = Cache(root);
            var job = Job(root, saved, month * 3, cache);
            Drain(job);
            Check.True(job.TryGetSelectionRequest(out var request), "Archived source is eligible for selection");
            Check.True(job.AcceptSelection(request.RequestId, "{\"ids\":[1]}"), "Archived source is compacted by reference");

            var restored = new WorldStateStore(1, root);
            restored.Restore(saved);
            Say(restored, "alice", 10, "The player claimed a pearl.", "I heard the claim.");
            string context = restored.RecallConversationWithCompaction("alice", "pearl", month * 3, 2000, cache);
            Equal(2, Count(context, "The player claimed a pearl"),
                "A pending duplicate is kept once as raw evidence beside the archived selected source");
        });
    }

    private static void CheckpointAndPersonChangesFallBackToRaw()
    {
        const double month = 2592000d;
        WithDirectory(root => {
            var origin = new WorldStateStore(12, root);
            Say(origin, "alice", 10, "Old answer about a silver key.", "I heard about the key.");
            Say(origin, "alice", 20, "Old answer about a copper seal.", "I heard about the seal.");
            var saved = origin.SnapshotForSave();
            var cache = Cache(root);
            var job = Job(root, saved, month * 3, cache);
            Drain(job);
            Check.True(job.TryGetSelectionRequest(out var request), "Initial branch produces a selection request");
            Check.True(job.AcceptSelection(request.RequestId, "{\"ids\":[1]}"), "Initial branch cache is written");

            var branch = new WorldStateStore(13, root);
            branch.Restore(saved);
            Say(branch, "alice", 30, "A backdated branch-only fact about an emerald.", "I heard the new fact.");
            var branchedSave = branch.SnapshotForSave();
            string context = branch.RecallConversationWithCompaction("alice", "emerald copper key", month * 3, 2000, cache);
            Check.True(context.Contains("silver key") && context.Contains("copper seal") && context.Contains("emerald"),
                "A changed checkpoint cannot consume a stale summary and falls back to its full raw branch");
            Check.True(!cache.HasCompletedGroup(branch.CaptureConversationArchiveCheckpoint(), "alice", 0),
                "A different person/checkpoint cache key is not treated as complete");

            string otherPerson = branch.RecallConversationWithCompaction("bob", "key", month * 3, 2000, cache);
            Check.True(otherPerson.Contains("MEMORY ABSENT"), "A cache never crosses person identities");
            Check.True(new SharedConversationArchive(root).Load(branchedSave.ConversationArchive).Single().Entries.Count == 3,
                "The raw branch retains every source record after stale-cache fallback");
        });
    }

    private static void JobLocalSkipCanBeRetriedAfterRestart()
    {
        const double month = 2592000d;
        WithDirectory(root => {
            var store = new WorldStateStore(14, root);
            Say(store, "alice", 1, "An old memory.", "An old reply.");
            var saved = store.SnapshotForSave();
            var cache = Cache(root);
            var job = Job(root, saved, month * 3, cache);
            Drain(job);
            Check.True(job.TryGetSelectionRequest(out var request), "First attempt exposes the eligible group");
            job.SkipSelection(request.RequestId);

            var restarted = Job(root, saved, month * 3, cache);
            Drain(restarted);
            Check.True(restarted.TryGetSelectionRequest(out _), "A retry skip is job-local and a restarted job can safely retry raw-backed work");
            Check.True(!cache.HasCompletedGroup(store.CaptureConversationArchiveCheckpoint(), "alice", 0),
                "Skipped work falls back to raw instead of publishing an empty summary");
            restarted.Cancel();
        });
    }

    private static void IneligibleFirstPageDoesNotDropLaterEligibleGroup()
    {
        const double month = 2592000d;
        WithDirectory(root => {
            var store = new WorldStateStore(15, root);
            // Commit order is person-sorted; archive scans newest records first.
            // These sixteen recent groups fill page one before the older p00 row.
            for (int i = 1; i <= 16; i++)
                Say(store, "p" + i.ToString("D2"), month * 4 - 10, "Recent claim " + i, "Recent reply " + i);
            Say(store, "p00", 1, "Old claim about a ruby.", "Old reply about the ruby.");
            var saved = store.SnapshotForSave();
            var job = Job(root, saved, month * 4, Cache(root));

            Drain(job);
            Check.True(!job.TryGetSelectionRequest(out _), "The first page has no work because its sixteen source groups are too young");
            Check.True(job.HasMorePages, "The completed first pass reports another archive page instead of dropping group 17");

            job.AdvanceScan(128);
            Drain(job);
            Check.True(job.TryGetSelectionRequest(out var request), "The next complete pass reaches the older eligible seventeenth group");
            Equal("p00", request.PersonId, "Paging preserves the older group behind sixteen ineligible groups");
        });
    }

    private static void CachedFirstPageDoesNotBlockTheUncachedSeventeenthGroup()
    {
        const double month = 2592000d;
        WithDirectory(root => {
            var store = new WorldStateStore(16, root);
            for (int i = 0; i <= 16; i++)
                Say(store, "p" + i.ToString("D2"), 1 + i, "Old claim " + i, "Old reply " + i);
            var saved = store.SnapshotForSave();
            var cache = Cache(root);

            var firstRun = Job(root, saved, month * 4, cache);
            Drain(firstRun);
            int accepted = 0;
            ConversationMemoryCompactionSelectionRequest request;
            while (firstRun.TryGetSelectionRequest(out request))
            {
                Check.True(firstRun.AcceptSelection(request.RequestId, "{\"ids\":[1]}"),
                    "Each first-page cache entry is source-validated before publication");
                accepted++;
            }
            Equal(16, accepted, "The first bounded page completes sixteen groups");
            Check.True(firstRun.HasMorePages, "The first page indicates that one source group remains");
            firstRun.Cancel();

            var resumed = Job(root, saved, month * 4, cache);
            Drain(resumed);
            Check.True(!resumed.TryGetSelectionRequest(out _), "The resumed first page recognizes its sixteen exact cache hits");
            Check.True(resumed.HasMorePages, "Cache hits do not consume or hide the remaining source group");
            resumed.AdvanceScan(128);
            Drain(resumed);
            Check.True(resumed.TryGetSelectionRequest(out var remaining), "The uncached seventeenth group eventually yields a request");
            Equal("p00", remaining.PersonId, "The request remains scoped to the unprocessed person group");
        });
    }

    private static void SameCheckpointContextChangeFallsBackToRaw()
    {
        const double month = 2592000d;
        WithDirectory(root => {
            var store = new WorldStateStore(17, root);
            Say(store, "alice", 10, "Old answer about a silver key.", "I heard about the key.");
            Say(store, "alice", 20, "Old answer about a copper seal.", "I heard about the seal.");
            var saved = store.SnapshotForSave();
            var checkpoint = store.CaptureConversationArchiveCheckpoint();
            var cache = Cache(root);
            Func<string, IReadOnlyList<SharedConversationArchive.ScanRecord>, ConversationMemoryCompactionContextSnapshot> firstContext =
                (person, records) => ContextSnapshot(person, 'a');
            var job = new ConversationMemoryCompactionJob(new SharedConversationArchive(root), checkpoint,
                month * 3, cache, firstContext);
            Drain(job);
            Check.True(job.TryGetSelectionRequest(out var request), "A source group with a captured context is selectable");
            Check.True(request.Prompt.Contains("do not prove emotion, truth, or player culpability") &&
                request.Prompt.Contains("not confirmed joy"),
                "Appraisal context cannot be promoted into claims about emotion, truth, or responsibility");
            Check.True(job.AcceptSelection(request.RequestId, "{\"ids\":[1]}"), "The source selection publishes under its context fingerprint");

            Func<string, IReadOnlyList<SharedConversationArchive.ScanRecord>, ConversationMemoryCompactionContextSnapshot> changedContext =
                (person, records) => ContextSnapshot(person, 'b');
            string recalled = store.RecallConversationWithCompaction("alice", "silver key copper seal", month * 3,
                2000, cache, changedContext);
            Check.True(recalled.Contains("copper seal") && recalled.Contains("silver key"),
                "A same-head context mismatch rejects the stale cache and restores all raw sources");
            Check.True(!cache.HasCompletedGroup(checkpoint, "alice", 0, new string('b', 64)),
                "The different context fingerprint is not treated as completed");
        });
    }

    private static void ContextChangeBeforeAcceptanceReissuesSelection()
    {
        const double month = 2592000d;
        WithDirectory(root => {
            var store = new WorldStateStore(19, root);
            Say(store, "alice", 10, "Old answer about a silver key.", "I heard about the key.");
            var saved = store.SnapshotForSave();
            var checkpoint = store.CaptureConversationArchiveCheckpoint();
            var cache = Cache(root);
            char capturedFingerprint = 'a';
            Func<string, IReadOnlyList<SharedConversationArchive.ScanRecord>, ConversationMemoryCompactionContextSnapshot> provider =
                (person, records) => ContextSnapshot(person, capturedFingerprint);
            var job = new ConversationMemoryCompactionJob(new SharedConversationArchive(root), checkpoint,
                month * 3, cache, provider);
            Drain(job);
            Check.True(job.TryGetSelectionRequest(out var original), "Initial salience context yields one selection request");

            capturedFingerprint = 'b';
            Check.True(!job.AcceptSelection(original.RequestId, "{\"ids\":[1]}"),
                "A response captured under a stale same-head context cannot publish");
            Check.True(job.TryGetSelectionRequest(out var refreshed), "A changed context offers a fresh bounded selection request");
            Check.True(original.RequestId != refreshed.RequestId, "The request identity changes with its source-context fingerprint");
            Check.True(!cache.HasCompletedGroup(checkpoint, "alice", 0, new string('a', 64)),
                "The stale context never becomes a completed cache entry");
            Check.True(job.AcceptSelection(refreshed.RequestId, "{\"ids\":[1]}"),
                "The refreshed context can be accepted against the same exact source group");
            Check.True(cache.HasCompletedGroup(checkpoint, "alice", 0, new string('b', 64)),
                "Only the revalidated context is published");
        });
    }

    private static void CacheManifestSupportsMoreThan128MonthsForOnePerson()
    {
        const double month = 2592000d;
        WithDirectory(root => {
            var store = new WorldStateStore(20, root);
            const int months = 129;
            for (int i = 0; i < months; i++)
                Say(store, "alice", month * i + 1, "Old monthly claim " + i, "Old monthly reply " + i);
            var saved = store.SnapshotForSave();
            var checkpoint = JobCheckpoint(root, saved);
            var cache = Cache(root);
            var job = Job(root, saved, month * 200, cache);
            int accepted = 0;
            int guard = 0;
            while (guard++ < 100)
            {
                Drain(job);
                ConversationMemoryCompactionSelectionRequest request;
                while (job.TryGetSelectionRequest(out request))
                {
                    Check.True(job.AcceptSelection(request.RequestId, "{\"ids\":[1]}"),
                        "Every page publishes a source-validated monthly group");
                    accepted++;
                }
                if (!job.HasMorePages) break;
                job.AdvanceScan(128);
            }
            Equal(months, accepted, "Paging completes every monthly group for one person beyond the former 128-entry limit");
            Check.True(!job.HasMorePages && job.ScanComplete, "The final bounded pass exhausts the full archive");
            Check.True(cache.HasCompletedGroup(checkpoint, "alice", 0) && cache.HasCompletedGroup(checkpoint, "alice", months - 1),
                "The compact manifest retains both the oldest and newest completed groups");
        });
    }

    private static void CorruptSidecarFallsBackToRaw()
    {
        const double month = 2592000d;
        WithDirectory(root => {
            var store = new WorldStateStore(18, root);
            Say(store, "alice", 10, "Old answer about a silver key.", "I heard about the key.");
            Say(store, "alice", 20, "Old answer about a copper seal.", "I heard about the seal.");
            store.SnapshotForSave();
            var checkpoint = store.CaptureConversationArchiveCheckpoint();
            var cache = Cache(root);
            var job = Job(root, store.Snapshot(), month * 3, cache);
            Drain(job);
            Check.True(job.TryGetSelectionRequest(out var request), "The source group is available for sidecar setup");
            Check.True(job.AcceptSelection(request.RequestId, "{\"ids\":[1]}"), "A complete cache sidecar is published");
            string groupDirectory = Path.Combine(root, "DerivedCompaction", "groups");
            string sidecar = Directory.GetFiles(groupDirectory, "*.cmp").Single();
            File.WriteAllBytes(sidecar, new byte[] { 0, 1, 2, 3 });

            string recalled = store.RecallConversationWithCompaction("alice", "silver key copper seal", month * 3,
                2000, cache);
            Check.True(recalled.Contains("silver key") && recalled.Contains("copper seal"),
                "A corrupt cache sidecar is rejected and the full raw journal remains available");
            Check.True(checkpoint.Head.Length == 64, "The source checkpoint remains an immutable archive head");
        });
    }

    private static void IndexedRecallBoundsRejectedSidecarReads()
    {
        const double month = 2592000d;
        WithDirectory(root => {
            var store = new WorldStateStore(21, root);
            for (int i = 0; i < 10; i++)
                Say(store, "alice", month * i + 10, "Old source for month " + i, "Reply for month " + i);
            var saved = store.SnapshotForSave();
            var cache = Cache(root);
            var job = Job(root, saved, month * 30, cache);
            Drain(job);
            int accepted = 0;
            while (job.TryGetSelectionRequest(out var request))
            {
                Check.True(job.AcceptSelection(request.RequestId, "{\"ids\":[1]}"),
                    "A valid monthly source can be cached for the stale-sidecar bound test");
                accepted++;
            }
            Equal(10, accepted, "Ten eligible monthly sidecars are available for bounded recall");

            int contextChecks = 0;
            Func<string, IReadOnlyList<SharedConversationArchive.ScanRecord>, ConversationMemoryCompactionContextSnapshot> staleContext =
                (person, records) => { contextChecks++; return ContextSnapshot(person, 'b'); };
            string result = store.RecallConversationWithCompaction("alice", "month source", month * 30,
                2000, cache, staleContext);
            Check.True(result.Contains("Old source for month"), "Rejected stale sidecars fall back to raw recall");
            Equal(8, contextChecks,
                "Indexed recall performs at most eight sidecar reads when every successfully read group has stale context");
        });
    }

    private static void FullManifestCanReplaceTheCurrentMonth()
    {
        const double month = 2592000d;
        WithDirectory(root => {
            var store = new WorldStateStore(22, root);
            Say(store, "alice", 10, "Old answer about a silver key.", "I heard about the key.");
            var saved = store.SnapshotForSave();
            var checkpoint = store.CaptureConversationArchiveCheckpoint();
            var cache = Cache(root);
            var initial = Job(root, saved, month * 3, cache);
            Drain(initial);
            Check.True(initial.TryGetSelectionRequest(out var first), "Initial summary fills the target month");
            Check.True(initial.AcceptSelection(first.RequestId, "{\"ids\":[1]}"), "Initial summary is published");

            string indexPath = (string)typeof(ConversationMemoryCompactionCache).GetMethod(
                "IndexPath", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(cache,
                new object[] { checkpoint.ArchiveId, checkpoint.Head, "alice" });
            byte[] original = File.ReadAllBytes(indexPath);
            string archiveId, head, personId;
            long targetMonth;
            string targetSource, targetContext, targetGroupFile;
            using (var stream = new MemoryStream(original))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                Check.True(reader.ReadInt32() == 0x434D4931 && reader.ReadInt32() == 2,
                    "Test fixture reads the current derived index format");
                archiveId = ReadTestString(reader); head = ReadTestString(reader); personId = ReadTestString(reader);
                Equal(1, reader.ReadInt32(), "One original completed index entry exists");
                targetMonth = reader.ReadInt64(); targetSource = ReadTestString(reader);
                targetContext = ReadTestString(reader); byte status = reader.ReadByte();
                targetGroupFile = ReadTestString(reader);
                Check.True(status == 1 && stream.Position == stream.Length, "The existing monthly group is a complete entry");
            }

            const int maximumEntries = 4096;
            using (var stream = new FileStream(indexPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8))
            {
                writer.Write(0x434D4931); writer.Write(2);
                WriteTestString(writer, archiveId); WriteTestString(writer, head); WriteTestString(writer, personId);
                writer.Write(maximumEntries);
                for (int i = 0; i < maximumEntries - 1; i++)
                {
                    writer.Write((long)i + 1); WriteTestString(writer, i.ToString("x64"));
                    WriteTestString(writer, ""); writer.Write((byte)2); WriteTestString(writer, "");
                }
                writer.Write(targetMonth); WriteTestString(writer, targetSource); WriteTestString(writer, targetContext);
                writer.Write((byte)1); WriteTestString(writer, targetGroupFile);
            }

            var replacement = new ConversationMemoryCompactionJob(new SharedConversationArchive(root), checkpoint,
                month * 3, cache, (person, records) => ContextSnapshot(person, 'd'));
            Drain(replacement);
            Check.True(replacement.TryGetSelectionRequest(out var refreshed),
                "A changed context asks for a replacement even with the manifest at capacity");
            Check.True(replacement.AcceptSelection(refreshed.RequestId, "{\"ids\":[1]}"),
                "A same-month replacement removes the superseded entry before enforcing capacity");
            Check.True(cache.HasCompletedGroup(checkpoint, "alice", 0, new string('d', 64)),
                "The replacement is readable in the full manifest");
        });
    }

    private static string ReadTestString(BinaryReader reader)
    {
        int length = reader.ReadInt32();
        return Encoding.UTF8.GetString(reader.ReadBytes(length));
    }

    private static void WriteTestString(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length); writer.Write(bytes);
    }

    private static ConversationMemoryCompactionContextSnapshot ContextSnapshot(string personId, char fingerprintCharacter)
    {
        ConstructorInfo constructor = typeof(ConversationMemoryCompactionContextSnapshot).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            new[] { typeof(string), typeof(string), typeof(IList<ConversationMemoryCompactionEmotionCue>), typeof(string) }, null);
        if (constructor == null) throw new InvalidOperationException("Context snapshot constructor was not found.");
        return (ConversationMemoryCompactionContextSnapshot)constructor.Invoke(new object[] {
            personId, null, null, new string(fingerprintCharacter, 64) });
    }

    private static ConversationMemoryCompactionCache Cache(string root)
    { return new ConversationMemoryCompactionCache(Path.Combine(root, "DerivedCompaction")); }

    private static ConversationMemoryCompactionJob Job(string root, WorldState saved, double now,
        ConversationMemoryCompactionCache cache)
    {
        var checkpointStore = new WorldStateStore(0, root);
        checkpointStore.Restore(saved);
        var checkpoint = checkpointStore.CaptureConversationArchiveCheckpoint();
        return new ConversationMemoryCompactionJob(new SharedConversationArchive(root), checkpoint, now, cache);
    }

    private static ConversationArchiveCheckpointView JobCheckpoint(string root, WorldState saved)
    {
        var checkpointStore = new WorldStateStore(0, root);
        checkpointStore.Restore(saved);
        return checkpointStore.CaptureConversationArchiveCheckpoint();
    }

    private static void Drain(ConversationMemoryCompactionJob job)
    {
        int guard = 0;
        while (!job.ScanComplete && guard++ < 1000) job.AdvanceScan(128);
        Check.True(job.ScanComplete, "Bounded scan reaches completion within the fixture archive");
    }

    private static void Say(WorldStateStore store, string person, double at, string player, string npc)
    { store.RememberConversation(person, at, "native date", "the inn", player, npc, "native"); }

    private static int Count(string text, string needle)
    {
        int count = 0, at = 0;
        while ((at = text.IndexOf(needle, at, StringComparison.Ordinal)) >= 0) { count++; at += needle.Length; }
        return count;
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(message + "; expected " + expected + ", got " + actual);
    }

    private static class Check
    {
        internal static void True(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }
    }

    private static void Reject(Action action)
    {
        assertions++;
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Expected invalid native game time to be rejected.");
    }

    private static void WithDirectory(Action<string> body)
    {
        string path = Path.Combine(Path.GetTempPath(), "conversation-compaction-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try { body(path); }
        finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
    }
}
