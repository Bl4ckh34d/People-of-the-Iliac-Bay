using System;
using PeopleOfTheIliacBay.Core;

internal static class Schema50Cases
{
    internal static void Run(Action<string, Action> run)
    {
        run("ticket one-use, release and restore epoch", TicketLifecycle);
        run("pending restore and degraded overflow", PendingRestoreAndDegradedOverflow);
        run("sequence overflow disposition", SequenceOverflow);
        run("legacy migration and invalid save", LegacyMigrationAndInvalidSave);
        run("released ticket cannot return", ReleasedTicketCannotReturn);
        run("retained receipt rejects lowered cursor", RetainedReceiptRejectsLoweredCursor);
        run("unexplained source gaps reject restore", UnexplainedSourceGapsRejectRestore);
        run("historical gap cannot hide lost pending", HistoricalGapCannotHideLostPending);
        run("earlier gap permits later exact pending", EarlierGapPermitsLaterPending);
        run("degraded count saturation", DegradedCountSaturation);
    }

    private static void TicketLifecycle()
    {
        string person; var store = NewStore(out person); var bundle = new object(); var binding = new object();
        object epoch = store.CaptureNpcVitalsWriteEpoch();
        var ticket = store.TryIssueNpcPoisonAssignment(epoch, person, binding, bundle);
        Check(ticket != null && store.TryIssueNpcPoisonAssignment(epoch, person, binding, bundle) == null, "ticket must be unique per active assignment");
        Check(store.TryRecordNpcPoisonAssignment(ticket, "poison:classic", 10, out var first) ==
            NpcPoisonCaptureDisposition.Queued && first.NativeImpactDetails.StatusOnsetSequence == 1, "first assignment");
        Check(store.TryRecordNpcPoisonAssignment(ticket, "poison:classic", 10, out _) ==
            NpcPoisonCaptureDisposition.Rejected, "ticket replay");
        Check(store.ReleaseNpcPoisonAssignment(epoch, person, binding, bundle), "release");
        ticket = store.TryIssueNpcPoisonAssignment(epoch, person, binding, bundle);
        Check(ticket != null && store.TryRecordNpcPoisonAssignment(ticket, "poison:classic", 11, out var second) ==
            NpcPoisonCaptureDisposition.Queued && second.NativeImpactDetails.StatusOnsetSequence == 2,
            "later real assignment of same object");
        store.Restore(store.Snapshot());
        Check(store.TryRecordNpcPoisonAssignment(ticket, "poison:classic", 12, out _) ==
            NpcPoisonCaptureDisposition.Rejected &&
            store.TryIssueNpcPoisonAssignment(epoch, person, binding, new object()) == null, "stale branch");
    }

    private static void PendingRestoreAndDegradedOverflow()
    {
        string person; var store = NewStore(out person); var saved = store.Snapshot();
        saved.NpcPoisonOnsets.Add(new NpcPoisonOnsetState { PersonId = person, LastIssuedSequence = 1,
            Pending = new NpcPoisonPendingOnset { Sequence = 1, NativeEffectKey = "poison:classic", ObservedGameTime = 10 } });
        store.Restore(saved);
        var ticket = store.TryIssueNpcPoisonAssignment(store.CaptureNpcVitalsWriteEpoch(), person, new object(), new object());
        Check(store.TryRecordNpcPoisonAssignment(ticket, "poison:classic", 11, out var omitted) ==
            NpcPoisonCaptureDisposition.Degraded && omitted == null, "bounded pending overflow");
        var gap = store.Snapshot().NpcPoisonOnsets[0];
        Check(gap.LastIssuedSequence == 2 && gap.DegradedCount == 1 && gap.DegradedFirstAt == 11 &&
            gap.DegradedLastAt == 11 && gap.UnprojectedDegradedCount == 1 &&
            gap.Pending.Sequence == 1, "gap state");
        store.Restore(store.Snapshot());
        Check(store.TryProjectPendingNpcPoisonOnset(person, out var first) &&
            first.NativeImpactDetails.StatusOnsetSequence == 1, "pending drain after restore");
        Check(!store.TryProjectPendingNpcPoisonOnset(person, out _), "pending replay");
    }

    private static void SequenceOverflow()
    {
        string person; var store = NewStore(out person); var saved = store.Snapshot();
        saved.NpcPoisonOnsets.Add(new NpcPoisonOnsetState { PersonId = person, LastIssuedSequence = long.MaxValue,
            LastProjectedSequence = long.MaxValue });
        store.Restore(saved);
        var ticket = store.TryIssueNpcPoisonAssignment(store.CaptureNpcVitalsWriteEpoch(), person, new object(), new object());
        Check(store.TryRecordNpcPoisonAssignment(ticket, "poison:classic", 20, out var evidence) ==
            NpcPoisonCaptureDisposition.Degraded && evidence == null, "overflow disposition");
        var after = store.Snapshot().NpcPoisonOnsets[0];
        Check(after.LastIssuedSequence == long.MaxValue && after.SequenceExhausted && after.DegradedCount == 1,
            "no sequence wrap");
    }

    private static void LegacyMigrationAndInvalidSave()
    {
        string person; var store = NewStore(out person);
        var ticket = store.TryIssueNpcPoisonAssignment(store.CaptureNpcVitalsWriteEpoch(), person,
            new object(), new object());
        Check(store.TryRecordNpcPoisonAssignment(ticket, "poison:classic", 10, out _) ==
            NpcPoisonCaptureDisposition.Queued, "migration seed");
        var legacy = store.Snapshot(); legacy.Version = 49; legacy.NpcPoisonOnsets.Clear();
        // Keep the v49 poison evidence used to reconstruct its cursor, without v54 accounting.
        legacy.NpcExperienceInbox.Version = 4;
        legacy.NpcExperienceInbox.RetiredCount = 0;
        legacy.NpcExperienceInbox.DetailRevision = 0;
        legacy.NpcExperienceInbox.LegacyImportedRevision = 0;
        foreach (var entry in legacy.NpcExperienceInbox.Entries)
        {
            entry.EnqueueOrdinal = 0;
            entry.SourceVitalsRevision = 0;
            entry.SourceKind = NpcExperienceSourceKind.LegacyUnknown;
        }
        store.Restore(legacy);
        Check(store.ReadNpcPoisonOnsetState(person).LastIssuedSequence == 1, "v49 canonical cursor migration");
        var next = store.TryIssueNpcPoisonAssignment(store.CaptureNpcVitalsWriteEpoch(), person,
            new object(), new object());
        Check(store.TryRecordNpcPoisonAssignment(next, "poison:classic", 11, out var second) ==
            NpcPoisonCaptureDisposition.Queued && second.NativeImpactDetails.StatusOnsetSequence == 2,
            "v49 migration reused source sequence");
        var invalid = store.Snapshot();
        invalid.NpcPoisonOnsets[0].LastProjectedSequence = 3;
        bool rejected = false;
        try { store.Restore(invalid); } catch (ArgumentException) { rejected = true; }
        Check(rejected && invalid.NpcPoisonOnsets[0].LastProjectedSequence == 3 &&
            store.ReadNpcPoisonOnsetState(person).LastProjectedSequence == 2,
            "invalid restore changed input or live state");
        Check(!store.TryCaptureNpcPoisonOnset(store.CaptureNpcVitalsWriteEpoch(), person,
            "poison:classic", 12, out _), "legacy caller bypassed ticket");
    }

    private static void ReleasedTicketCannotReturn()
    {
        string person; var store = NewStore(out person); object owner = new object(), bundle = new object();
        object epoch = store.CaptureNpcVitalsWriteEpoch();
        var stale = store.TryIssueNpcPoisonAssignment(epoch, person, owner, bundle);
        Check(store.ReleaseNpcPoisonAssignment(epoch, person, owner, bundle), "initial release");
        var current = store.TryIssueNpcPoisonAssignment(epoch, person, owner, bundle);
        Check(current != null && store.TryRecordNpcPoisonAssignment(stale, "poison:classic", 10, out _) ==
            NpcPoisonCaptureDisposition.Rejected, "released ticket revived");
        Check(store.TryRecordNpcPoisonAssignment(current, "poison:classic", 10, out _) ==
            NpcPoisonCaptureDisposition.Queued, "new assignment rejected");
        Check(store.ReleaseAllNpcPoisonAssignments(epoch, person, owner) == 1 &&
            store.TryRecordNpcPoisonAssignment(current, "poison:classic", 11, out _) ==
            NpcPoisonCaptureDisposition.Rejected, "unbind failed to revoke");
    }

    private static void RetainedReceiptRejectsLoweredCursor()
    {
        string person; var store = NewStore(out person);
        var ticket = store.TryIssueNpcPoisonAssignment(store.CaptureNpcVitalsWriteEpoch(), person,
            new object(), new object());
        Check(store.TryRecordNpcPoisonAssignment(ticket, "poison:classic", 10, out _) ==
            NpcPoisonCaptureDisposition.Queued, "retained-row seed");
        var tampered = store.Snapshot();
        tampered.NpcPoisonOnsets[0].LastProjectedSequence = 0;
        bool rejected = false;
        try { store.Restore(tampered); } catch (ArgumentException) { rejected = true; }
        Check(rejected && tampered.NpcPoisonOnsets[0].LastProjectedSequence == 0 &&
            store.ReadNpcPoisonOnsetState(person).LastProjectedSequence == 1,
            "lowered source cursor accepted or input mutated");
        var legacy = store.Snapshot(); legacy.Version = 49; legacy.NpcExperienceInbox = new NpcExperienceInbox { Version = 4 };
        rejected = false;
        try { store.Restore(legacy); } catch (ArgumentException) { rejected = true; }
        Check(rejected && legacy.Version == 49 && legacy.NpcPoisonOnsets.Count == 1,
            "legacy envelope accepted future poison cursor");
    }

    private static void UnexplainedSourceGapsRejectRestore()
    {
        string person; var store = NewStore(out person);
        var absent = store.Snapshot();
        absent.NpcPoisonOnsets.Add(new NpcPoisonOnsetState { PersonId = person,
            LastIssuedSequence = 10, LastProjectedSequence = 0 });
        bool rejected = false;
        try { store.Restore(absent); } catch (ArgumentException) { rejected = true; }
        Check(rejected && store.ReadNpcPoisonOnsetState(person) == null &&
            absent.NpcPoisonOnsets[0].LastIssuedSequence == 10,
            "unexplained issued suffix accepted or restore mutated input/live state");

        var skipped = store.Snapshot();
        skipped.NpcPoisonOnsets.Add(new NpcPoisonOnsetState { PersonId = person,
            LastIssuedSequence = 3, LastProjectedSequence = 0,
            Pending = new NpcPoisonPendingOnset { Sequence = 3, NativeEffectKey = "poison:classic",
                ObservedGameTime = 11 } });
        rejected = false;
        try { store.Restore(skipped); } catch (ArgumentException) { rejected = true; }
        Check(rejected && store.ReadNpcPoisonOnsetState(person) == null,
            "pending sequence skipped two events without a gap record");
    }

    private static void EarlierGapPermitsLaterPending()
    {
        string person; var store = NewStore(out person);
        var saved = store.Snapshot();
        saved.NpcPoisonOnsets.Add(new NpcPoisonOnsetState { PersonId = person,
            LastIssuedSequence = 5, LastProjectedSequence = 2, DegradedCount = 2,
            UnprojectedDegradedCount = 2,
            DegradedFirstAt = 11, DegradedLastAt = 12,
            Pending = new NpcPoisonPendingOnset { Sequence = 5, NativeEffectKey = "poison:classic",
                ObservedGameTime = 13 } });
        store.Restore(saved);
        Check(store.ReadNpcPoisonOnsetState(person).Pending.Sequence == 5,
            "historical gap incorrectly blocked a later exact pending event");
        Check(store.TryProjectPendingNpcPoisonOnset(person, out var projected) &&
            projected.NativeImpactDetails.StatusOnsetSequence == 5,
            "later pending event could not project across earlier gap");
        var after = store.ReadNpcPoisonOnsetState(person);
        Check(after.LastProjectedSequence == 5 && after.Pending == null && after.DegradedCount == 2 &&
            after.UnprojectedDegradedCount == 0,
            "projecting later evidence erased typed historical gap");
    }

    private static void HistoricalGapCannotHideLostPending()
    {
        string person; var store = NewStore(out person); var saved = store.Snapshot();
        saved.NpcPoisonOnsets.Add(new NpcPoisonOnsetState { PersonId = person,
            LastIssuedSequence = 4, LastProjectedSequence = 3,
            DegradedCount = 1, DegradedFirstAt = 10, DegradedLastAt = 10,
            Pending = new NpcPoisonPendingOnset { Sequence = 4,
                NativeEffectKey = "poison:classic", ObservedGameTime = 11 } });
        store.Restore(saved);
        var lost = store.Snapshot(); lost.NpcPoisonOnsets[0].Pending = null;
        bool rejected = false;
        try { store.Restore(lost); } catch (ArgumentException) { rejected = true; }
        Check(rejected && lost.NpcPoisonOnsets[0].Pending == null &&
            store.ReadNpcPoisonOnsetState(person).Pending.Sequence == 4,
            "cumulative old gap concealed lost exact pending onset");
    }

    private static void DegradedCountSaturation()
    {
        string person; var store = NewStore(out person); var saved = store.Snapshot();
        saved.NpcPoisonOnsets.Add(new NpcPoisonOnsetState { PersonId = person,
            LastIssuedSequence = 2, Pending = new NpcPoisonPendingOnset { Sequence = 1,
                NativeEffectKey = "poison:classic", ObservedGameTime = 10 },
            UnprojectedDegradedCount = 1, DegradedCount = long.MaxValue,
            DegradedFirstAt = 11, DegradedLastAt = 11 });
        store.Restore(saved);
        var ticket = store.TryIssueNpcPoisonAssignment(store.CaptureNpcVitalsWriteEpoch(), person,
            new object(), new object());
        Check(store.TryRecordNpcPoisonAssignment(ticket, "poison:classic", 12, out _) ==
            NpcPoisonCaptureDisposition.Degraded, "saturated gap was not durable");
        var after = store.ReadNpcPoisonOnsetState(person);
        Check(after.LastIssuedSequence == 3 && after.UnprojectedDegradedCount == 2 &&
            after.DegradedCount == long.MaxValue &&
            after.DegradedCountSaturated && after.DegradedLastAt == 12,
            "degraded count saturation wrapped or lost time span");
    }

    private static WorldStateStore NewStore(out string person)
    {
        var store = new WorldStateStore(42);
        person = store.GetOrCreate(100, new[] { 10 }, 1).Households[0].Residents[0].Id;
        return store;
    }

    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
