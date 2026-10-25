using PeopleOfTheIliacBay.Tests;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int assertions;

    private static void Main()
    {
        AcceptedPlayerAppraisalBindsToExactFlooredSource();
        TimeTextSpeakerAndObserverMismatchesStayUnknown();
        AmbiguousSourceAndReceiptMatchesStayUnknown();
        PendingAndDeferredRowsStayUnknown();
        NativeAppraisalWithoutSpeechSourceStaysUnknown();
        ProfileMissingIsUnknownAndCaptureIsReadOnly();
        FingerprintTracksAcceptedCueProvenance();
        CheckpointBindingRejectsForeignAndSiblingHeadsButAllowsRestoredHead();
        DuplicateAddressesStayAmbiguousAndCandidateCountIsBounded();
        MutableScanRecordEntryCannotFabricateAnArchivedSource();
        Console.WriteLine("Memory salience regressions passed: " + assertions + " assertions.");
    }

    private static void AcceptedPlayerAppraisalBindsToExactFlooredSource()
    {
        WithDirectory(root =>
        {
            Fixture fixture = Build(root,
                new[] { new MemorySpec(Primary, 12.1, "I will kill you.") },
                new[] { new SpeechSpec(Primary, Player, 12.9, "I will kill you.", 1, true, false, 4, 3) });
            NpcPersonalityProfile profile = fixture.Store.ReadNpcPersonality(fixture.PrimaryId);
            long revisionBefore = fixture.Store.Snapshot().NpcExperienceInbox.Revision;

            object snapshot = Capture(fixture.Store, fixture.PrimaryId, fixture.Records);
            Equal(profile.Description(), Read<string>(snapshot, "PersonalityDescription"),
                "A saved validated profile supplies stable trait context");
            Check.True(Count(snapshot, "EmotionCues") == 1,
                "One unique consumed player appraisal binds to its exact archived source");
            object cue = Cues(snapshot).Single();
            Equal(4, Read<int>(cue, "MoodDelta"), "Recorded positive mood delta stays numeric");
            Equal(3, Read<int>(cue, "FearDelta"), "Recorded fear delta stays numeric");
            Equal("player", Read<string>(cue, "SpeakerId"), "Cue preserves observed player speaker identity");
            Equal("I will kill you.", Read<string>(cue, "ObservedTurnText"),
                "Cue preserves exact accepted utterance evidence");
            Equal("receipt:1", Read<string>(cue, "ReceiptId"), "Cue keeps receipt provenance");
            object address = Read<object>(cue, "Address");
            Check.True(SameAddress(address, fixture.Records.Single().Address),
                "Cue uses exact archive, checkpoint, chunk, record and person address");
            Equal(revisionBefore, fixture.Store.Snapshot().NpcExperienceInbox.Revision,
                "Capturing salience context does not mutate experience inbox");
        });
    }

    private static void TimeTextSpeakerAndObserverMismatchesStayUnknown()
    {
        AssertNoCue(root => Build(root,
            new[] { new MemorySpec(Primary, 13.1, "I will kill you.") },
            new[] { new SpeechSpec(Primary, Player, 12.9, "I will kill you.", 1, true, false, 2, 4) }),
            "A receipt in a different floored native second cannot bind");
        AssertNoCue(root => Build(root,
            new[] { new MemorySpec(Primary, 12.1, "A different line") },
            new[] { new SpeechSpec(Primary, Player, 12.9, "I will kill you.", 1, true, false, 2, 4) }),
            "A text mismatch cannot bind by nearby wording");
        AssertNoCue(root => Build(root,
            new[] { new MemorySpec(Primary, 12.1, "I will kill you.") },
            new[] { new SpeechSpec(Primary, Secondary, 12.9, "I will kill you.", 1, true, false, 2, 4) }),
            "A non-player speaker cannot bind as player speech");
        AssertNoCue(root => Build(root,
            new[] { new MemorySpec(Primary, 12.1, "I will kill you.") },
            new[] { new SpeechSpec(Secondary, Player, 12.9, "I will kill you.", 1, true, false, 2, 4) }),
            "A different observer cannot bind to this person's memory");
    }

    private static void AmbiguousSourceAndReceiptMatchesStayUnknown()
    {
        AssertNoCue(root => Build(root,
            new[]
            {
                new MemorySpec(Primary, 12.1, "I will kill you."),
                new MemorySpec(Primary, 12.8, "I will kill you.")
            },
            new[] { new SpeechSpec(Primary, Player, 12.9, "I will kill you.", 1, true, false, 3, 5) }),
            "One receipt matching two archived candidates is ambiguous");

        AssertNoCue(root => Build(root,
            new[] { new MemorySpec(Primary, 12.1, "I will kill you.") },
            new[]
            {
                new SpeechSpec(Primary, Player, 12.2, "I will kill you.", 1, true, false, 3, 5),
                new SpeechSpec(Primary, Player, 12.9, "I will kill you.", 2, true, false, 4, 6)
            }),
            "Two accepted receipts matching one archived candidate are ambiguous");
    }

    private static void PendingAndDeferredRowsStayUnknown()
    {
        AssertNoCue(root => Build(root,
                new[] { new MemorySpec(Primary, 12.1, "I will kill you.") },
            new[] { new SpeechSpec(Primary, Player, 12.9, "I will kill you.", 1, false, false, 4, 5) }),
            "Pending appraisal cannot change memory salience");
        AssertNoCue(root => Build(root,
            new[] { new MemorySpec(Primary, 12.1, "I will kill you.") },
            new[] { new SpeechSpec(Primary, Player, 12.9, "I will kill you.", 1, false, true, 4, 5) }),
            "Deferred appraisal cannot change memory salience");
    }

    private static void NativeAppraisalWithoutSpeechSourceStaysUnknown()
    {
        WithDirectory(root =>
        {
            Fixture fixture = Build(root,
                new[] { new MemorySpec(Primary, 12.1, "A line with no receipt link") },
                Array.Empty<SpeechSpec>(), acceptNativeAppraisal: true);
            object snapshot = Capture(fixture.Store, fixture.PrimaryId, fixture.Records);
            Equal(0, Count(snapshot, "EmotionCues"),
                "Native event appraisal without exact utterance source stays unknown");
        });
    }

    private static void ProfileMissingIsUnknownAndCaptureIsReadOnly()
    {
        WithDirectory(root =>
        {
            Fixture fixture = Build(root, Array.Empty<MemorySpec>(), Array.Empty<SpeechSpec>());
            WorldState before = fixture.Store.Snapshot();
            long revisionBefore = before.NpcExperienceInbox.Revision;
            int entriesBefore = before.NpcExperienceInbox.Entries.Count;
            Check.True(fixture.Store.ReadNpcPersonality(fixture.PrimaryId) == null,
                "Fixture begins without a saved personality profile");

            object snapshot = Capture(fixture.Store, fixture.PrimaryId, fixture.Records);

            Check.True(Read<string>(snapshot, "PersonalityDescription") == null,
                "Missing saved profile stays unknown");
            Check.True(fixture.Store.ReadNpcPersonality(fixture.PrimaryId) == null,
                "Context capture does not generate or persist a profile");
            WorldState after = fixture.Store.Snapshot();
            Equal(revisionBefore, after.NpcExperienceInbox.Revision,
                "Context capture leaves inbox revision unchanged");
            Equal(entriesBefore, after.NpcExperienceInbox.Entries.Count,
                "Context capture leaves inbox entries unchanged");
            Equal(before.NpcPersonalities.Count, after.NpcPersonalities.Count,
                "Context capture leaves personality ledger unchanged");
            Equal(before.NpcRelationships.Revision, after.NpcRelationships.Revision,
                "Context capture leaves directed relationship ledger unchanged");
            Equal(before.ConversationMoods.Count, after.ConversationMoods.Count,
                "Context capture leaves saved mood ledger unchanged");
            Equal(before.ConversationArchive.Head, after.ConversationArchive.Head,
                "Context capture leaves archive ancestry unchanged");
        });
    }

    private static void FingerprintTracksAcceptedCueProvenance()
    {
        WithDirectory(root =>
        {
            Fixture fixture = Build(root,
                new[] { new MemorySpec(Primary, 12.1, "I will kill you.") },
                new[] { new SpeechSpec(Primary, Player, 12.9, "I will kill you.", 1, true, false, 4, 3) });
            object first = Capture(fixture.Store, fixture.PrimaryId, fixture.Records);
            object repeated = Capture(fixture.Store, fixture.PrimaryId, fixture.Records);
            string fingerprint = Read<string>(first, "ContextFingerprint");
            Equal(fingerprint, Read<string>(repeated, "ContextFingerprint"),
                "Unchanged saved context has stable fingerprint");

            WorldState changed = fixture.Store.Snapshot();
            NpcExperienceInboxEntry row = changed.NpcExperienceInbox.Entries.Single();
            row.ReceiptId = "receipt:changed";
            row.ObservedTurnReceiptId = row.ReceiptId;
            NpcExperienceEvent changedEvidence = NpcExperienceEvent.FromObservedSpeech(row.ObserverId,
                row.SubjectPersonId, row.ReceiptId, row.EpisodeId, row.UtteranceId, row.ObservedGameTime,
                row.AttributedSpeakerId);
            row.DeduplicationKey = changedEvidence.DeduplicationKey;
            var changedStore = new WorldStateStore(0, root);
            changedStore.Restore(changed);
            object changedContext = Capture(changedStore, fixture.PrimaryId, fixture.Records);
            Check.True(!string.Equals(fingerprint, Read<string>(changedContext, "ContextFingerprint"),
                StringComparison.Ordinal), "Changing matched receipt provenance changes context fingerprint");

            WorldState changedAppraisal = fixture.Store.Snapshot();
            NpcExperienceInboxEntry changedAppraisalRow = changedAppraisal.NpcExperienceInbox.Entries.Single();
            changedAppraisalRow.Appraisal.FearDelta++;
            var appraisalStore = new WorldStateStore(0, root);
            appraisalStore.Restore(changedAppraisal);
            object changedAppraisalContext = Capture(appraisalStore, fixture.PrimaryId, fixture.Records);
            Check.True(!string.Equals(fingerprint, Read<string>(changedAppraisalContext, "ContextFingerprint"),
                StringComparison.Ordinal), "Changing an accepted appraisal delta changes context fingerprint");
        });
    }

    private static void CheckpointBindingRejectsForeignAndSiblingHeadsButAllowsRestoredHead()
    {
        WithDirectory(root =>
        {
            MemorySpec[] memories = { new MemorySpec(Primary, 12.1, "I will kill you.") };
            SpeechSpec[] speeches = { new SpeechSpec(Primary, Player, 12.9, "I will kill you.", 1, true, false, 4, 3) };
            Fixture fixture = Build(root, memories, speeches);
            Equal(1, Count(Capture(fixture.Store, fixture.PrimaryId, fixture.Records), "EmotionCues"),
                "Current store checkpoint accepts its own exact source address");

            string foreignRoot = Path.Combine(root, "foreign-archive");
            Fixture foreign = Build(foreignRoot, memories, speeches);
            Equal(fixture.PrimaryId, foreign.PrimaryId,
                "Foreign archive fixture has the same person identity and source text");
            Throws<ArgumentException>(() => Capture(fixture.Store, fixture.PrimaryId, foreign.Records),
                "Same-person, same-text candidates from another archive namespace are rejected");

            WorldState savedOldBranch = fixture.Store.Snapshot();
            fixture.Store.RememberConversation(fixture.PrimaryId, 120.1, "native date", "the inn",
                "A later sibling-branch line.", "Recorded NPC reply.", "native");
            fixture.Store.SnapshotForSave();
            Throws<ArgumentException>(() => Capture(fixture.Store, fixture.PrimaryId, fixture.Records),
                "Candidates pinned to a sibling head are rejected after the store advances");

            var restoredOldStore = new WorldStateStore(0, root);
            restoredOldStore.Restore(savedOldBranch);
            Equal(1, Count(Capture(restoredOldStore, fixture.PrimaryId, fixture.Records), "EmotionCues"),
                "Restoring the matching older branch makes its exact source addresses valid again");
        });
    }

    private static void DuplicateAddressesStayAmbiguousAndCandidateCountIsBounded()
    {
        WithDirectory(root =>
        {
            Fixture fixture = Build(root,
                new[] { new MemorySpec(Primary, 12.1, "I will kill you.") },
                new[] { new SpeechSpec(Primary, Player, 12.9, "I will kill you.", 1, true, false, 4, 3) });
            var duplicated = fixture.Records.Concat(fixture.Records).ToList();
            Throws<ArgumentException>(() => Capture(fixture.Store, fixture.PrimaryId, duplicated),
                "Duplicate source addresses are rejected before they can be bound");

            var overLimit = Enumerable.Repeat(fixture.Records.Single(),
                ConversationMemoryCompactionContextSnapshot.MaximumCandidateCount + 1).ToList();
            Throws<ArgumentOutOfRangeException>(() => Capture(fixture.Store, fixture.PrimaryId, overLimit),
                "Public candidate bound rejects oversized context input");
        });
    }

    private static void MutableScanRecordEntryCannotFabricateAnArchivedSource()
    {
        WithDirectory(root =>
        {
            Fixture changedText = Build(root,
                new[] { new MemorySpec(Primary, 12.1, "Original archive words") },
                new[] { new SpeechSpec(Primary, Player, 12.9, "I will kill you.", 1, true, false, 4, 3) });
            SharedConversationArchive.ScanRecord textRecord = changedText.Records.Single();
            textRecord.Entry.PlayerText = "I will kill you.";
            Equal("Original archive words", textRecord.Entry.PlayerText,
                "Mutating a detached ScanRecord.Entry read does not alter the scan record");
            Equal(0, Count(Capture(changedText.Store, changedText.PrimaryId, changedText.Records), "EmotionCues"),
                "Mutating ScanRecord.PlayerText cannot turn an archived nonmatch into an accepted cue");

            Fixture changedTime = Build(Path.Combine(root, "time"),
                new[] { new MemorySpec(Primary, 11.1, "I will kill you.") },
                new[] { new SpeechSpec(Primary, Player, 12.9, "I will kill you.", 1, true, false, 4, 3) });
            SharedConversationArchive.ScanRecord timeRecord = changedTime.Records.Single();
            timeRecord.Entry.At = 12;
            Equal(11.0, timeRecord.Entry.At,
                "Mutating a detached ScanRecord.Entry time read does not alter the scan record");
            Equal(0, Count(Capture(changedTime.Store, changedTime.PrimaryId, changedTime.Records), "EmotionCues"),
                "Mutating ScanRecord.At cannot turn a different archived second into an accepted cue");

            Fixture changedBoth = Build(Path.Combine(root, "both"),
                new[] { new MemorySpec(Primary, 11.1, "Original archive words") },
                new[] { new SpeechSpec(Primary, Player, 12.9, "I will kill you.", 1, true, false, 4, 3) });
            SharedConversationArchive.ScanRecord combinedRecord = changedBoth.Records.Single();
            NpcMemoryEntry exposedEntry = combinedRecord.Entry;
            exposedEntry.At = 12;
            exposedEntry.PlayerText = "I will kill you.";
            Equal(11.0, combinedRecord.Entry.At,
                "Mutating exposed archive time cannot change the retained source record");
            Equal("Original archive words", combinedRecord.Entry.PlayerText,
                "Mutating exposed archive text cannot change the retained source record");
            Equal(0, Count(Capture(changedBoth.Store, changedBoth.PrimaryId, changedBoth.Records), "EmotionCues"),
                "Mutating both time and text cannot fabricate a receipt-matched archive source");
        });
    }

    private static void AssertNoCue(Func<string, Fixture> build, string message)
    {
        WithDirectory(root =>
        {
            Fixture fixture = build(root);
            object snapshot = Capture(fixture.Store, fixture.PrimaryId, fixture.Records);
            Equal(0, Count(snapshot, "EmotionCues"), message);
        });
    }

    private static Fixture Build(string root, MemorySpec[] memories, SpeechSpec[] speeches,
        bool acceptNativeAppraisal = false)
    {
        var store = new WorldStateStore(109, root);
        string primaryId = store.GetOrCreate(20, new[] { 200 }, 1).Households[0].Residents[0].Id;
        string secondaryId = store.GetOrCreate(21, new[] { 201 }, 1).Households[0].Residents[0].Id;
        foreach (MemorySpec memory in memories)
        {
            string personId = Resolve(memory.Person, primaryId, secondaryId);
            store.RememberConversation(personId, memory.At, "native date", "the inn",
                memory.PlayerText, "Recorded NPC reply.", "native");
        }

        foreach (SpeechSpec speech in speeches)
        {
            string observerId = Resolve(speech.Observer, primaryId, secondaryId);
            string speakerId = Resolve(speech.Speaker, primaryId, secondaryId);
            var evidence = NpcExperienceEvent.FromObservedSpeech(observerId, observerId,
                "receipt:" + speech.Occurrence, "episode:fixture",
                "utterance:" + speech.Occurrence, speech.At, speakerId);
            var turn = NpcObservedConversationTurn.Create(observerId, evidence.EpisodeId, speakerId,
                evidence.ReceiptId, speech.Text, speech.At);
            HistoricalNpcObservedSpeechFixture.Import(store, evidence, turn,
                NpcExperienceAppliedEffectsMask.None);
            if (!speech.Accept && !speech.Defer) continue;
            Check.True(store.TryCaptureNpcExperienceAppraisal(observerId, speech.At, out NpcExperienceAppraisalTicket ticket),
                "Fixture appraisal ticket captures queued evidence");
            if (speech.Defer)
            {
                MethodInfo defer = typeof(WorldStateStore).GetMethod("TryDeferNpcExperienceAppraisal",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Check.True(defer != null && (bool)defer.Invoke(store,
                    new object[] { observerId, ticket, speech.At + 1 }), "Fixture receipt becomes deferred");
            }
            else
            {
                NpcSpeechActClassification classification;
                Check.True(NpcSpeechActClassification.TryAccept(NpcSpeechActRequest.ForAppraisal(ticket.Request),
                    "DirectThreat", out classification), "Fixture speech act classification is accepted");
                Check.True(store.TryBindNpcExperienceSpeechClassification(ticket, classification),
                    "Fixture classification binds to exact appraisal request");
                Check.True(store.TryCommitNpcExperienceAppraisal(ticket,
                    Result(ticket.Request, speech.MoodDelta, speech.FearDelta, "DirectThreat", observerId)),
                    "Fixture appraisal is accepted and consumed");
            }
        }

        if (acceptNativeAppraisal)
        {
            var evidence = NpcExperienceEvent.FromNativeOutcome(primaryId, primaryId,
                "native-receipt:1", "native-episode:1", "native-outcome:1",
                NpcExperienceEventKind.Healing, 12.9,
                NpcResponsibleActorAttribution.Known, NpcDirectedRelationship.PlayerTargetId);
            Check.True(HistoricalNpcExperienceFixture.ImportHistoricalNativeEvent(store, evidence), "Historical native event imports for fixture");
            Check.True(store.TryCaptureNpcExperienceAppraisal(primaryId, 12.9, out NpcExperienceAppraisalTicket ticket),
                "Native appraisal ticket captures fixture event");
            Check.True(store.TryCommitNpcExperienceAppraisal(ticket, Result(ticket.Request, 4, 2)),
                "Native appraisal is accepted but has no exact speech source");
        }

        WorldState saved = store.SnapshotForSave();
        var archive = new SharedConversationArchive(root);
        var scan = archive.OpenScan(saved.ConversationArchive);
        var records = new List<SharedConversationArchive.ScanRecord>();
        while (!scan.IsComplete) records.AddRange(scan.ReadNextBatch(128));
        return new Fixture(store, primaryId, secondaryId, root, records);
    }

    private static object Capture(WorldStateStore store, string personId,
        IReadOnlyList<SharedConversationArchive.ScanRecord> records)
    {
        MethodInfo capture = typeof(WorldStateStore).GetMethod("CaptureConversationMemoryCompactionContext",
            BindingFlags.Instance | BindingFlags.Public);
        if (capture == null)
            throw new InvalidOperationException("Core bridge method CaptureConversationMemoryCompactionContext is missing.");
        return capture.Invoke(store, new object[] { personId, records });
    }

    private static string Result(NpcExperienceAppraisalRequest request, int mood, int fear,
        string speechAct = "NotApplicable", string speechTarget = "")
    {
        return "{\"schemaVersion\":" + NpcExperienceAppraisalContract.SchemaVersion +
            ",\"policyVersion\":" + NpcExperienceAppraisalContract.PolicyVersion +
            ",\"saveBranchId\":\"" + request.SaveBranchId + "\",\"observerId\":\"" + request.ObserverId +
            "\",\"subjectPersonId\":\"" + request.SubjectPersonId +
            "\",\"evidenceDeduplicationKey\":\"" + request.EvidenceDeduplicationKey +
            "\",\"canonicalEvidenceId\":\"" + request.CanonicalEvidenceId +
            "\",\"relationshipTargetId\":\"" + (request.RelationshipTargetId ?? "") +
            "\",\"expectedMoodRevision\":" + request.ExpectedMoodRevision +
            ",\"expectedAffinityRevision\":" + request.ExpectedAffinityRevision +
            ",\"expectedRelationshipRevision\":" + request.ExpectedRelationshipRevision +
            ",\"moodDelta\":" + mood + ",\"affinityDelta\":0,\"trustDelta\":0,\"resentmentDelta\":0,\"fearDelta\":" + fear +
            ",\"speechActKind\":\"" + speechAct + "\",\"speechTargetPersonId\":\"" + speechTarget + "\",\"conversationOption\":\"NoAction\"}";
    }

    private static string Resolve(string selector, string primary, string secondary)
    {
        if (selector == Primary) return primary;
        if (selector == Secondary) return secondary;
        if (selector == Player) return NpcDirectedRelationship.PlayerTargetId;
        throw new InvalidOperationException("Unknown fixture identity selector: " + selector);
    }

    private static List<object> Cues(object snapshot)
    {
        var cues = Read<object>(snapshot, "EmotionCues") as IEnumerable;
        if (cues == null) throw new InvalidOperationException("EmotionCues is not enumerable.");
        var result = new List<object>();
        foreach (object cue in cues) result.Add(cue);
        return result;
    }

    private static int Count(object snapshot, string property)
    { return Cues(snapshot).Count; }

    private static T Read<T>(object source, string name)
    {
        PropertyInfo property = source.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        if (property == null) throw new InvalidOperationException("Missing public property " + name + ".");
        object value = property.GetValue(source);
        return value == null ? default(T) : (T)value;
    }

    private static bool SameAddress(object address, SharedConversationArchive.RecordAddress expected)
    {
        if (address == null || expected == null) return false;
        return Read<string>(address, "ArchiveId") == expected.ArchiveId &&
            Read<string>(address, "CheckpointHead") == expected.CheckpointHead &&
            Read<string>(address, "ChunkHash") == expected.ChunkHash &&
            Read<int>(address, "RecordIndex") == expected.RecordIndex &&
            Read<string>(address, "PersonId") == expected.PersonId;
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        assertions++;
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException(message + "; expected " + expected + ", got " + actual);
    }

    private static void Throws<TException>(Action action, string message) where TException : Exception
    {
        assertions++;
        try { action(); }
        catch (TException) { return; }
        catch (TargetInvocationException exception) when (exception.InnerException is TException) { return; }
        throw new InvalidOperationException(message + "; expected " + typeof(TException).Name);
    }

    private static class Check
    {
        internal static void True(bool condition, string message)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException(message);
        }
    }

    private static void WithDirectory(Action<string> body)
    {
        string root = Path.Combine(Path.GetTempPath(), "memory-salience-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { body(root); }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private const string Primary = "primary";
    private const string Secondary = "secondary";
    private const string Player = "player";

    private sealed class MemorySpec
    {
        internal readonly string Person;
        internal readonly double At;
        internal readonly string PlayerText;
        internal MemorySpec(string person, double at, string playerText)
        { Person = person; At = at; PlayerText = playerText; }
    }

    private sealed class SpeechSpec
    {
        internal readonly string Observer, Speaker, Text;
        internal readonly double At;
        internal readonly int Occurrence, MoodDelta, FearDelta;
        internal readonly bool Accept, Defer;
        internal SpeechSpec(string observer, string speaker, double at, string text, int occurrence,
            bool accept, bool defer, int moodDelta, int fearDelta)
        {
            Observer = observer; Speaker = speaker; At = at; Text = text; Occurrence = occurrence;
            Accept = accept; Defer = defer; MoodDelta = moodDelta; FearDelta = fearDelta;
        }
    }

    private sealed class Fixture
    {
        internal readonly WorldStateStore Store;
        internal readonly string PrimaryId, SecondaryId, Root;
        internal readonly IReadOnlyList<SharedConversationArchive.ScanRecord> Records;
        internal Fixture(WorldStateStore store, string primaryId, string secondaryId, string root,
            IReadOnlyList<SharedConversationArchive.ScanRecord> records)
        { Store = store; PrimaryId = primaryId; SecondaryId = secondaryId; Root = root; Records = records; }
    }
}
