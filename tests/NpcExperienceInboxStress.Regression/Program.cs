using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private const int EventCount = 10001;
    private const int ObserverCount = 8;
    private const double DaySeconds = 86400;
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };
    private static readonly List<double> EnqueueMilliseconds = new List<double>(EventCount);
    private static readonly List<double> MaintenanceMilliseconds = new List<double>();
    private static int peakGlobal, peakPerson, restoreCount;
    private static long previousRevision, previousRetired, previousDetailRevision;
    private static long[] projectedVitals;
    private static object epoch;

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0) Run();
            else if (args.Length == 1 && args[0] == "--near-cap") RunNearCap();
            else throw new ArgumentException("Use no argument or --near-cap.");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL source-backed experience inbox stress: " + error);
            return 1;
        }
    }

    private static void Run()
    {
        // Controlled Core source simulation, not proof of a DFU engine observation.
        // Every event enters through the public observed-vitals boundary and health flush.
        var store = new WorldStateStore(109);
        var buildingKeys = Enumerable.Range(200, ObserverCount).ToArray();
        var people = store.GetOrCreate(20, buildingKeys, 1).Households
            .SelectMany(home => home.Residents).Select(person => person.Id)
            .Distinct(StringComparer.Ordinal).Take(ObserverCount).ToArray();
        Require(people.Length == ObserverCount, "Fixture did not create distinct health observers.");
        foreach (string person in people)
        {
            store.GetOrCreateNpcPersonality(person, 109, NpcSocialBackground.Commoner);
            var vitals = store.ReadNpcVitals(person, NpcArchetype.Citizen);
            Require(vitals.MaximumHealth > 1, "Fixture needs reversible health changes.");
        }
        projectedVitals = new long[ObserverCount];
        epoch = store.CaptureNpcVitalsWriteEpoch();

        for (int index = 0; index < EventCount; index++)
        {
            int ownerIndex = index % people.Length;
            string person = people[ownerIndex];
            double observedAt = 1000d + index * DaySeconds;
            var vitals = store.ReadNpcVitals(person, NpcArchetype.Citizen);
            int changedHealth = vitals.CurrentHealth == vitals.MaximumHealth
                ? vitals.MaximumHealth - 1 : vitals.MaximumHealth;

            long started = Stopwatch.GetTimestamp();
            Require(store.TryCaptureObservedNpcVitalsChange(epoch, person, NpcArchetype.Citizen,
                vitals.CurrentHealth, vitals.CurrentMagicka, changedHealth, vitals.CurrentMagicka,
                observedAt, vitals.Revision), "Observed native-health boundary rejected event " + index + ".");
            var flushed = store.FlushNpcHealthExperiences(observedAt + 60, 60, 1);
            Require(flushed.Count == 1 && flushed[0].ObserverId == person,
                "Health source did not enqueue exactly once at event " + index + ".");
            EnqueueMilliseconds.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);

            var progress = store.ReadNpcProgress(person);
            Require(progress.HealthExperienceProjectedThroughVitalsRevision > projectedVitals[ownerIndex],
                "Health projection cursor failed to advance at event " + index + ".");
            projectedVitals[ownerIndex] = progress.HealthExperienceProjectedThroughVitalsRevision;
            Require(store.ReadPendingNpcExperiences(person, 10).Count == 1,
                "Health event did not remain available for appraisal.");
            Require(store.TryCaptureNpcExperienceAppraisal(person, observedAt + 61, out var ticket),
                "Canonical health event lost appraisal authority.");
            Require(store.TryCommitNpcExperienceAppraisal(ticket, NeutralResult(ticket.Request)),
                "Deterministic neutral appraisal failed at event " + index + ".");
            Require(store.ReadPendingNpcExperiences(person, 10).Count == 0,
                "Appraised health event remained pending.");

            if ((index + 1) % 64 == 0)
            {
                // Capture the high-water mark before this batch can retire rows.
                CheckSnapshot(store.Snapshot(), index + 1);
                // A neutral mood delta makes the consumed rows naturally unpinned.
                // This stress uses the API's allowed maximum budget, 64/32;
                // runtime's ordinary maintenance budget is smaller.
                for (int pass = 0; pass < 3; pass++)
                    Maintain(store, observedAt + 61);
                CheckSnapshot(store.Snapshot(), index + 1);
            }
            if ((index + 1) % 2000 == 0)
                store = RestoreAndCheck(store, people, observedAt + 62, index + 1);
        }

        double finalNow = 1000d + (EventCount - 1) * DaySeconds + 62;
        CheckSnapshot(store.Snapshot(), EventCount);
        for (int pass = 0; pass < 8; pass++) Maintain(store, finalNow);
        var finalState = store.Snapshot();
        CheckSnapshot(finalState, EventCount);
        Require(finalState.NpcExperienceInbox.Revision == EventCount,
            "Absolute enqueue revision did not count all accepted source events.");
        Require(finalState.NpcExperienceInbox.RetiredCount > EventCount / 2,
            "Stress run did not exercise substantial real retirement.");
        store = RestoreAndCheck(store, people, finalNow + 1, EventCount);
        finalState = store.Snapshot();
        int serializedBytes = JsonSerializer.SerializeToUtf8Bytes(finalState, Json).Length;
        Console.WriteLine("PASS source-backed experience inbox stress: accepted=" + EventCount +
            " years=" + ((EventCount - 1) * DaySeconds / (365d * DaySeconds)).ToString("F2") +
            " restores=" + restoreCount + " revision=" + finalState.NpcExperienceInbox.Revision +
            " retired=" + finalState.NpcExperienceInbox.RetiredCount +
            " retained=" + finalState.NpcExperienceInbox.Entries.Count +
            " peakPerson=" + peakPerson + " peakGlobal=" + peakGlobal +
            " serializedBytes=" + serializedBytes);
        ReportLatency("sourceCaptureAndEnqueue", EnqueueMilliseconds);
        ReportLatency("maintenance", MaintenanceMilliseconds);
    }

    private static void Maintain(WorldStateStore store, double now)
    {
        long started = Stopwatch.GetTimestamp();
        var result = store.MaintainNpcExperienceInboxHistory(now, 64, 32, true);
        MaintenanceMilliseconds.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        Require(result.Examined <= 64 && result.Retired <= 32,
            "Maintenance exceeded its per-call work budget.");
    }

    private static WorldStateStore RestoreAndCheck(WorldStateStore store, string[] people,
        double now, int accepted)
    {
        var before = store.Snapshot();
        CheckSnapshot(before, accepted);
        byte[] serialized = JsonSerializer.SerializeToUtf8Bytes(before, Json);
        Require(serialized.Length > 0, "Snapshot serialization produced no bytes.");
        WorldState decoded = JsonSerializer.Deserialize<WorldState>(serialized, Json);
        Require(decoded != null, "Snapshot JSON did not decode to WorldState.");
        object staleEpoch = epoch;
        var restored = new WorldStateStore(0);
        restored.Restore(decoded);
        epoch = restored.CaptureNpcVitalsWriteEpoch();
        Require(!ReferenceEquals(staleEpoch, epoch), "Restore retained a stale native-write epoch.");
        var first = restored.ReadNpcVitals(people[0], NpcArchetype.Citizen);
        int changedHealth = first.CurrentHealth == first.MaximumHealth
            ? first.MaximumHealth - 1 : first.MaximumHealth;
        Require(!restored.TryCaptureObservedNpcVitalsChange(staleEpoch, people[0], NpcArchetype.Citizen,
            first.CurrentHealth, first.CurrentMagicka, changedHealth, first.CurrentMagicka,
            now, first.Revision), "Pre-restore native ticket replay changed health.");
        Require(restored.FlushNpcHealthExperiences(now, 60, 100).Count == 0,
            "Already projected health source replayed after restore.");
        foreach (var pair in people.Select((id, index) => new { id, index }))
            Require(restored.ReadNpcProgress(pair.id).HealthExperienceProjectedThroughVitalsRevision ==
                projectedVitals[pair.index], "Projected health cursor changed on restore.");
        CheckSnapshot(restored.Snapshot(), accepted);
        restoreCount++;
        return restored;
    }

    private static void CheckSnapshot(WorldState state, int accepted)
    {
        var inbox = state.NpcExperienceInbox;
        Require(inbox.Version == NpcExperienceInbox.VersionCurrent,
            "Stress fixture did not use current inbox schema.");
        Require(inbox.Revision == accepted, "Absolute enqueue revision changed or skipped an event.");
        Require(inbox.Revision == inbox.RetiredCount + inbox.Entries.Count,
            "Retired and retained rows do not account for all accepted events.");
        Require(inbox.Revision >= previousRevision && inbox.RetiredCount >= previousRetired &&
            inbox.DetailRevision >= previousDetailRevision, "Inbox counters decreased.");
        previousRevision = inbox.Revision;
        previousRetired = inbox.RetiredCount;
        previousDetailRevision = inbox.DetailRevision;
        int currentPeakPerson = inbox.Entries.GroupBy(row => row.ObserverId, StringComparer.Ordinal)
            .Select(group => group.Count()).DefaultIfEmpty(0).Max();
        peakGlobal = Math.Max(peakGlobal, inbox.Entries.Count);
        peakPerson = Math.Max(peakPerson, currentPeakPerson);
        Require(inbox.Entries.Count <= NpcExperienceInbox.MaximumRetainedEntries &&
            currentPeakPerson <= NpcExperienceInbox.MaximumRetainedPerObserver,
            "Inbox exceeded retained global or per-person cap.");
        long priorOrdinal = 0;
        foreach (var row in inbox.Entries)
        {
            Require(row.EnqueueOrdinal > priorOrdinal && row.EnqueueOrdinal <= inbox.Revision,
                "Retained enqueue ordinals are not absolute and increasing.");
            Require(row.SourceVitalsRevision > 0 && row.Status == NpcExperienceInboxStatus.Consumed,
                "Stress row lost exact Health-source revision or neutral appraisal.");
            priorOrdinal = row.EnqueueOrdinal;
        }
    }

    private static string NeutralResult(NpcExperienceAppraisalRequest request)
    {
        return "{\"schemaVersion\":" + NpcExperienceAppraisalContract.SchemaVersion +
            ",\"policyVersion\":" + NpcExperienceAppraisalContract.PolicyVersion +
            ",\"saveBranchId\":\"" + request.SaveBranchId +
            "\",\"observerId\":\"" + request.ObserverId +
            "\",\"subjectPersonId\":\"" + request.SubjectPersonId +
            "\",\"evidenceDeduplicationKey\":\"" + request.EvidenceDeduplicationKey +
            "\",\"canonicalEvidenceId\":\"" + request.CanonicalEvidenceId +
            "\",\"relationshipTargetId\":\"" + (request.RelationshipTargetId ?? "") +
            "\",\"expectedMoodRevision\":" + request.ExpectedMoodRevision +
            ",\"expectedAffinityRevision\":" + request.ExpectedAffinityRevision +
            ",\"expectedRelationshipRevision\":" + request.ExpectedRelationshipRevision +
            ",\"moodDelta\":0,\"affinityDelta\":0,\"trustDelta\":0,\"resentmentDelta\":0,\"fearDelta\":0," +
            "\"speechActKind\":\"NotApplicable\",\"speechTargetPersonId\":\"\",\"conversationOption\":\"NoAction\"}";
    }

    private static void ReportLatency(string name, List<double> samples)
    {
        Require(samples.Count > 0, "No latency samples for " + name + ".");
        samples.Sort();
        Console.WriteLine(name + " count=" + samples.Count + " p50Ms=" + Percentile(samples, 0.50).ToString("F3") +
            " p95Ms=" + Percentile(samples, 0.95).ToString("F3") +
            " maxMs=" + samples[samples.Count - 1].ToString("F3"));
    }

    private static double Percentile(List<double> ordered, double fraction)
    { return ordered[Math.Max(0, (int)Math.Ceiling(ordered.Count * fraction) - 1)]; }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static void RunNearCap()
    {
        const int observers = 32;
        const int speechPerObserver = 8;
        const int healthPerObserver = 256 - speechPerObserver;
        const int expectedRows = observers * (speechPerObserver + healthPerObserver);
        Require(expectedRows == NpcExperienceInbox.MaximumRetainedEntries,
            "Near-cap fixture no longer matches global capacity.");
        var store = new WorldStateStore(109);
        var buildingKeys = Enumerable.Range(300, observers).ToArray();
        var people = store.GetOrCreate(20, buildingKeys, 1).Households
            .SelectMany(home => home.Residents).Select(person => person.Id)
            .Distinct(StringComparer.Ordinal).Take(observers).ToArray();
        Require(people.Length == observers, "Near-cap fixture needs 32 distinct residents.");
        var enqueueMilliseconds = new List<double>(expectedRows);
        var maintenanceMilliseconds = new List<double>(expectedRows / 16);
        long referencesInspected = 0, examined = 0, pinned = 0;
        int peakGlobalRetained = 0, peakPerPersonRetained = 0;

        // These are controlled Core-issued speech tickets, not observed DFU audio.
        // Pending speech rows exercise the retention context-reference index.
        for (int owner = 0; owner < people.Length; owner++)
        {
            string person = people[owner];
            Require(store.TryBeginNpcObservedSpeechSession(person, "near-cap-session:" + owner,
                out var session), "Could not open a controlled speech source.");
            for (int ordinal = 1; ordinal <= speechPerObserver; ordinal++)
            {
                long started = Stopwatch.GetTimestamp();
                double at = 100d + owner * 100d + ordinal;
                Require(store.TryIssueNpcObservedSpeech(session, ordinal,
                    "Controlled source text " + owner + ":" + ordinal, at, out var ticket),
                    "Could not issue source-bound speech.");
                Require(store.TryAcceptNpcObservedSpeechAfterAppend(ticket, ticket.Receipt,
                    NpcExperienceAppliedEffectsMask.None) == NpcObservedSpeechAcceptanceResult.Projected,
                    "Source-bound speech did not project to pending inbox.");
                enqueueMilliseconds.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            }
            Require(store.TryCloseNpcObservedSpeechSession(session), "Could not close speech source.");
        }

        object observedVitalsEpoch = store.CaptureNpcVitalsWriteEpoch();
        double lastObservedAt = 0;
        for (int round = 0; round < healthPerObserver; round++)
            for (int owner = 0; owner < people.Length; owner++)
            {
                string person = people[owner];
                double at = 10000d + (round * people.Length + owner) * DaySeconds;
                lastObservedAt = at;
                var vitals = store.ReadNpcVitals(person, NpcArchetype.Citizen);
                Require(vitals.MaximumHealth > 1, "Near-cap resident cannot alternate health state.");
                int changedHealth = vitals.CurrentHealth == vitals.MaximumHealth
                    ? vitals.MaximumHealth - 1 : vitals.MaximumHealth;
                long started = Stopwatch.GetTimestamp();
                Require(store.TryCaptureObservedNpcVitalsChange(observedVitalsEpoch, person,
                    NpcArchetype.Citizen, vitals.CurrentHealth, vitals.CurrentMagicka,
                    changedHealth, vitals.CurrentMagicka, at, vitals.Revision),
                    "Observed health source rejected near-cap event.");
                Require(store.FlushNpcHealthExperiences(at + 60, 60, 1).Count == 1,
                    "Source-backed health event failed before inbox capacity.");
                enqueueMilliseconds.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                if ((round * people.Length + owner + 1) % 512 == 0)
                {
                    var sample = store.Snapshot().NpcExperienceInbox;
                    peakGlobalRetained = Math.Max(peakGlobalRetained, sample.Entries.Count);
                    peakPerPersonRetained = Math.Max(peakPerPersonRetained,
                        sample.Entries.GroupBy(row => row.ObserverId, StringComparer.Ordinal)
                            .Select(group => group.Count()).DefaultIfEmpty(0).Max());
                    Require(sample.Entries.Count <= NpcExperienceInbox.MaximumRetainedEntries &&
                        peakPerPersonRetained <= NpcExperienceInbox.MaximumRetainedPerObserver,
                        "Near-cap admission exceeded retained capacity.");
                }
            }

        var full = store.Snapshot();
        var inbox = full.NpcExperienceInbox;
        peakGlobalRetained = Math.Max(peakGlobalRetained, inbox.Entries.Count);
        peakPerPersonRetained = Math.Max(peakPerPersonRetained,
            inbox.Entries.GroupBy(row => row.ObserverId, StringComparer.Ordinal)
                .Select(group => group.Count()).DefaultIfEmpty(0).Max());
        Require(inbox.Entries.Count == expectedRows && inbox.Revision == expectedRows &&
            inbox.RetiredCount == 0, "Fixture did not reach exactly 8192 retained source rows.");
        Require(inbox.Entries.GroupBy(row => row.ObserverId, StringComparer.Ordinal)
            .All(group => group.Count() == NpcExperienceInbox.MaximumRetainedPerObserver),
            "Fixture did not reach exactly 256 retained rows per observer.");
        Require(peakGlobalRetained == NpcExperienceInbox.MaximumRetainedEntries &&
            peakPerPersonRetained == NpcExperienceInbox.MaximumRetainedPerObserver,
            "Reported near-cap peaks missed the actual full inbox.");
        Require(inbox.Entries.Count(row => row.Origin == NpcExperienceEventOrigin.AttributedSpeech) ==
            observers * speechPerObserver, "Speech context fraction changed.");
        Require(inbox.Entries.All(row => row.Status == NpcExperienceInboxStatus.Pending),
            "Near-cap sweep requires genuine pending rows, not fabricated pins.");

        // The next real health change remains in its source after a full inbox rejects projection.
        string blockedPerson = people[0];
        var blockedVitals = store.ReadNpcVitals(blockedPerson, NpcArchetype.Citizen);
        int blockedHealth = blockedVitals.CurrentHealth == blockedVitals.MaximumHealth
            ? blockedVitals.MaximumHealth - 1 : blockedVitals.MaximumHealth;
        double blockedAt = lastObservedAt + DaySeconds;
        Require(store.TryCaptureObservedNpcVitalsChange(observedVitalsEpoch, blockedPerson,
            NpcArchetype.Citizen, blockedVitals.CurrentHealth, blockedVitals.CurrentMagicka,
            blockedHealth, blockedVitals.CurrentMagicka, blockedAt, blockedVitals.Revision),
            "Over-cap health source was not captured.");
        Require(store.FlushNpcHealthExperiences(blockedAt + 60, 60, 1).Count == 0 &&
            store.ReadNpcProgress(blockedPerson).HasPendingHealthEpisode,
            "Over-cap source was discarded instead of staying pending.");

        double maintenanceAt = blockedAt + 61;
        bool completedSweep = false, allPinned = false;
        for (int call = 0; call < expectedRows / 16 + 1; call++)
        {
            long started = Stopwatch.GetTimestamp();
            var result = store.MaintainNpcExperienceInboxHistory(maintenanceAt, 16, 4, false);
            maintenanceMilliseconds.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            Require(result.Examined <= 16 && result.Retired == 0 && result.Deferred == 0,
                "Runtime-budget sweep retired or deferred a pending row.");
            referencesInspected += result.ReferenceRowsInspected;
            examined += result.Examined;
            pinned += result.Pinned;
            if (!result.ScanComplete) continue;
            completedSweep = true;
            allPinned = result.AllPinned;
            break;
        }
        Require(completedSweep && allPinned && examined >= expectedRows && pinned == examined,
            "Full 16/4 retention sweep did not report all rows pinned.");
        var after = store.Snapshot();
        Require(after.NpcExperienceInbox.Revision == expectedRows &&
            after.NpcExperienceInbox.Entries.Count == expectedRows &&
            after.NpcExperienceInbox.RetiredCount == 0 &&
            store.ReadNpcProgress(blockedPerson).HasPendingHealthEpisode,
            "All-pinned sweep changed saved inbox or lost blocked health source.");
        byte[] serialized = JsonSerializer.SerializeToUtf8Bytes(after, Json);
        var restored = new WorldStateStore(0);
        restored.Restore(JsonSerializer.Deserialize<WorldState>(serialized, Json));
        Require(restored.Snapshot().NpcExperienceInbox.Entries.Count == expectedRows &&
            restored.ReadNpcProgress(blockedPerson).HasPendingHealthEpisode &&
            restored.FlushNpcHealthExperiences(maintenanceAt + 60, 60, 1).Count == 0,
            "Near-cap JSON restore lost pending source or bypassed capacity.");

        Console.WriteLine("PASS source-backed near-cap inbox: rows=" + expectedRows +
            " observers=" + observers + " speech=" + observers * speechPerObserver +
            " health=" + observers * healthPerObserver +
            " peakPerson=" + peakPerPersonRetained + " peakGlobal=" + peakGlobalRetained +
            " allPinned=" + allPinned + " sweepCalls=" + maintenanceMilliseconds.Count +
            " examined=" + examined + " pinned=" + pinned +
            " referenceRowsInspected=" + referencesInspected +
            " serializedBytes=" + serialized.Length);
        ReportLatency("nearCapSourceCaptureAndEnqueue", enqueueMilliseconds);
        ReportLatency("nearCapMaintenance16x4", maintenanceMilliseconds);
    }
}
