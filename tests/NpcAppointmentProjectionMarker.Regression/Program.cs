using System;
using System.Collections.Generic;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static readonly JsonSerializerOptions SnapshotOptions = new JsonSerializerOptions { IncludeFields = true };

    private static int Main()
    {
        var cases = new List<(string Name, Action Body)>
        {
            ("projected marker prevents replay after retained detail removal", ProjectedMarkerSurvivesRemoval),
            ("failed enqueue remains pending and retries after restore", FailedEnqueueRetries),
            ("exact old receipt repairs pending marker but conflicting same key does not", ExactReceiptRepair),
            ("schema 48 migration distinguishes exact, absent and future markers", Schema48Migration),
            ("schemas 26 and 34 retain old confirmed and unresolved appointments", PreInboxSchemaMigration),
            ("unsupported and unresolved rows cannot claim projection", InvalidCombinations)
        };
        int failed = 0;
        foreach (var test in cases)
        {
            try { test.Body(); Console.WriteLine("PASS " + test.Name); }
            catch (Exception ex) { failed++; Console.WriteLine("FAIL " + test.Name + ": " + ex.Message); }
        }
        Console.WriteLine((cases.Count - failed) + "/" + cases.Count + " passed");
        return failed == 0 ? 0 : 1;
    }

    private static void ProjectedMarkerSurvivesRemoval()
    {
        var store = Attended("retired-appointment", out var owner);
        Equal(NpcAppointmentExperienceProjection.Projected,
            store.FindAppointment("retired-appointment").ExperienceProjection);
        Appraise(store, owner, 1011);
        Equal(1, store.MaintainNpcExperienceInboxHistory(8 * 86400 + 20, 16, 4, true).Retired);
        var saved = store.Snapshot();
        Equal(1L, saved.NpcExperienceInbox.Revision);
        Equal(1L, saved.NpcExperienceInbox.RetiredCount);
        Equal(0, saved.NpcExperienceInbox.Entries.Count);
        var restored = new WorldStateStore(0);
        restored.Restore(saved);
        Equal(NpcAppointmentExperienceProjection.Projected,
            restored.FindAppointment("retired-appointment").ExperienceProjection);
        Equal(0, restored.FlushNpcAppointmentExperiences(100000, 1));
        Equal(0, restored.ReadPendingNpcExperiences(owner, 10).Count);
        Equal(1, ObservationCount(restored, owner, "attended our meet appointment"));
        var again = new WorldStateStore(0);
        again.Restore(restored.Snapshot());
        Equal(0, again.FlushNpcAppointmentExperiences(100001, 1));
    }

    private static void FailedEnqueueRetries()
    {
        var store = Book("pending-no-show", 2000, out var owner, 3);
        double deadline = AppointmentTiming.ArrivalDeadline(store.FindAppointment("pending-no-show"));
        for (double at = deadline - 40; at <= deadline; at += 10)
            store.ObserveAppointment("pending-no-show", at, true, false, false);
        var liveInbox = LiveWorld(store).NpcExperienceInbox;
        liveInbox.Revision = long.MaxValue;
        store.ObserveAppointment("pending-no-show", deadline + 1, false, false, false);
        var pending = store.FindAppointment("pending-no-show");
        Equal(NpcAppointmentExperienceProjection.Pending, pending.ExperienceProjection);
        Equal(deadline + 1, pending.ResolvedAt);
        Equal(-pending.NoShowPenalty, store.ConversationDisposition(owner));
        Equal(1, ObservationCount(store, owner, "missed our meet appointment"));
        Equal(0, store.FlushNpcAppointmentExperiences(deadline + 2, 1));
        Equal(NpcAppointmentExperienceProjection.Pending,
            store.FindAppointment("pending-no-show").ExperienceProjection);
        liveInbox.Revision = 0;
        var restored = new WorldStateStore(0);
        restored.Restore(store.Snapshot());
        Equal(1, restored.FlushNpcAppointmentExperiences(deadline + 86400, 1));
        Equal(NpcAppointmentExperienceProjection.Projected,
            restored.FindAppointment("pending-no-show").ExperienceProjection);
        Equal(deadline + 1, restored.ReadPendingNpcExperiences(owner, 10)[0].ObservedGameTime);
        Equal(-pending.NoShowPenalty, restored.ConversationDisposition(owner));
        Equal(1, ObservationCount(restored, owner, "missed our meet appointment"));
        Equal(0, restored.FlushNpcAppointmentExperiences(deadline + 86401, 1));
    }

    private static void ExactReceiptRepair()
    {
        var store = Attended("repair-appointment", out var owner);
        var saved = store.Snapshot();
        saved.Appointments[0].ExperienceProjection = NpcAppointmentExperienceProjection.Pending;
        var restored = new WorldStateStore(0);
        restored.Restore(saved);
        Equal(0, restored.FlushNpcAppointmentExperiences(2000, 1));
        Equal(NpcAppointmentExperienceProjection.Projected,
            restored.FindAppointment("repair-appointment").ExperienceProjection);
        Equal(1, restored.ReadPendingNpcExperiences(owner, 10).Count);

        saved = store.Snapshot();
        saved.Appointments[0].ExperienceProjection = NpcAppointmentExperienceProjection.Pending;
        saved.NpcExperienceInbox.Entries[0].ReceiptId = "conflicting-receipt";
        var conflict = new WorldStateStore(0);
        conflict.Restore(saved);
        string before = SnapshotText(conflict);
        Equal(0, conflict.FlushNpcAppointmentExperiences(2000, 1));
        Equal(before, SnapshotText(conflict));
        Equal(NpcAppointmentExperienceProjection.Pending,
            conflict.FindAppointment("repair-appointment").ExperienceProjection);
    }

    private static void Schema48Migration()
    {
        var current = AsSchema48(Attended("legacy-appointment", out var owner).Snapshot());
        current.Appointments[0].ExperienceProjection = NpcAppointmentExperienceProjection.None;
        var exact = new WorldStateStore(0);
        exact.Restore(current);
        Equal(NpcAppointmentExperienceProjection.Projected,
            exact.FindAppointment("legacy-appointment").ExperienceProjection);
        Equal(0, exact.FlushNpcAppointmentExperiences(2000, 1));

        var absent = AsSchema48(Attended("legacy-missing", out owner).Snapshot());
        absent.Appointments[0].ExperienceProjection = NpcAppointmentExperienceProjection.None;
        absent.NpcExperienceInbox.Entries.Clear();
        absent.NpcExperienceInbox.Revision = 0;
        var unavailable = new WorldStateStore(0);
        unavailable.Restore(absent);
        Equal(NpcAppointmentExperienceProjection.LegacyUnavailable,
            unavailable.FindAppointment("legacy-missing").ExperienceProjection);
        Equal(0, unavailable.FlushNpcAppointmentExperiences(2000, 1));
        Equal(0, unavailable.ReadPendingNpcExperiences(owner, 10).Count);

        current.Appointments[0].ExperienceProjection = NpcAppointmentExperienceProjection.Projected;
        ThrowsArgument(() => new WorldStateStore(0).Restore(current));
    }

    private static WorldState AsSchema48(WorldState saved)
    {
        saved.Version = 48;
        saved.ConversationCueArchive = null;
        saved.NpcExperienceInbox.Version = 4;
        saved.NpcExperienceInbox.RetiredCount = 0;
        saved.NpcExperienceInbox.DetailRevision = 0;
        saved.NpcExperienceInbox.LegacyImportedRevision = 0;
        foreach (var row in saved.NpcExperienceInbox.Entries)
        {
            row.EnqueueOrdinal = 0;
            row.SourceKind = NpcExperienceSourceKind.LegacyUnknown;
            row.SourceVitalsRevision = 0;
        }
        return saved;
    }

    private static void InvalidCombinations()
    {
        var unresolvedStore = Book("unresolved-marker", 1000, out _);
        var unresolved = unresolvedStore.Snapshot();
        unresolved.Appointments[0].ExperienceProjection = NpcAppointmentExperienceProjection.Pending;
        ThrowsArgument(() => new WorldStateStore(0).Restore(unresolved));

        var unsupportedStore = Book("unsupported-marker", 1000, out _);
        unsupportedStore.CancelAppointment("unsupported-marker", 600, "cancelled");
        Equal(NpcAppointmentExperienceProjection.None,
            unsupportedStore.FindAppointment("unsupported-marker").ExperienceProjection);
        var unsupported = unsupportedStore.Snapshot();
        unsupported.Appointments[0].ExperienceProjection = NpcAppointmentExperienceProjection.Projected;
        ThrowsArgument(() => new WorldStateStore(0).Restore(unsupported));

        var supported = Attended("missing-marker", out _).Snapshot();
        supported.Appointments[0].ExperienceProjection = NpcAppointmentExperienceProjection.None;
        ThrowsArgument(() => new WorldStateStore(0).Restore(supported));
        supported.Appointments[0].ExperienceProjection = NpcAppointmentExperienceProjection.LegacyUnavailable;
        ThrowsArgument(() => new WorldStateStore(0).Restore(supported));
        supported.Appointments[0].ExperienceProjection = (NpcAppointmentExperienceProjection)99;
        ThrowsArgument(() => new WorldStateStore(0).Restore(supported));
    }

    private static void PreInboxSchemaMigration()
    {
        foreach (int version in new[] { 26, 34 })
        {
            var confirmed = Attended("pre-inbox-confirmed-" + version, out var owner).Snapshot();
            confirmed.Version = version;
            confirmed.NpcExperienceInbox = null; // This schema did not persist experience rows.
            confirmed.Appointments[0].ExperienceProjection = NpcAppointmentExperienceProjection.None;
            var restored = new WorldStateStore(0);
            restored.Restore(confirmed);
            Equal(NpcAppointmentExperienceProjection.LegacyUnavailable,
                restored.FindAppointment("pre-inbox-confirmed-" + version).ExperienceProjection);
            Equal(0, restored.FlushNpcAppointmentExperiences(100000, 1));
            Equal(0, restored.ReadPendingNpcExperiences(owner, 10).Count);
            Equal(1, ObservationCount(restored, owner, "attended our meet appointment"));

            var open = Book("pre-inbox-open-" + version, 2000, out owner).Snapshot();
            open.Version = version;
            open.NpcExperienceInbox = null;
            open.Appointments[0].ExperienceProjection = NpcAppointmentExperienceProjection.None;
            restored.Restore(open);
            Equal(NpcAppointmentExperienceProjection.None,
                restored.FindAppointment("pre-inbox-open-" + version).ExperienceProjection);
            Equal(0, restored.FlushNpcAppointmentExperiences(100000, 1));
            Equal(0, restored.ReadPendingNpcExperiences(owner, 10).Count);
        }
    }

    private static WorldStateStore Attended(string id, out string owner)
    {
        var store = Book(id, 1000, out owner);
        store.ObserveAppointment(id, 1000, true, false, false);
        store.ObserveAppointment(id, 1010, true, true, false);
        Equal("attended", store.FindAppointment(id).Status);
        Equal(1, store.ReadPendingNpcExperiences(owner, 10).Count);
        return store;
    }

    private static void Appraise(WorldStateStore store, string person, double at)
    {
        True(store.TryCaptureNpcExperienceAppraisal(person, at, out var ticket),
            "Appointment source was not appraisable.");
        var r = ticket.Request;
        string neutral = "{\"schemaVersion\":" + NpcExperienceAppraisalContract.SchemaVersion +
            ",\"policyVersion\":" + NpcExperienceAppraisalContract.PolicyVersion +
            ",\"saveBranchId\":\"" + r.SaveBranchId + "\",\"observerId\":\"" + r.ObserverId +
            "\",\"subjectPersonId\":\"" + r.SubjectPersonId +
            "\",\"evidenceDeduplicationKey\":\"" + r.EvidenceDeduplicationKey +
            "\",\"canonicalEvidenceId\":\"" + r.CanonicalEvidenceId +
            "\",\"relationshipTargetId\":\"" + (r.RelationshipTargetId ?? "") +
            "\",\"expectedMoodRevision\":" + r.ExpectedMoodRevision +
            ",\"expectedAffinityRevision\":" + r.ExpectedAffinityRevision +
            ",\"expectedRelationshipRevision\":" + r.ExpectedRelationshipRevision +
            ",\"moodDelta\":0,\"affinityDelta\":0,\"trustDelta\":0,\"resentmentDelta\":0," +
            "\"fearDelta\":0,\"speechActKind\":\"NotApplicable\",\"speechTargetPersonId\":\"\"," +
            "\"conversationOption\":\"NoAction\"}";
        True(store.TryCommitNpcExperienceAppraisal(ticket, neutral), "Appointment appraisal failed.");
    }

    private static WorldStateStore Book(string id, double start, out string owner, int importance = 1)
    {
        var store = new WorldStateStore(42);
        owner = store.GetOrCreate(100, new[] { 10 }, 1).Households[0].Residents[0].Id;
        var booking = new AppointmentRecord { Id = id, ResidentId = owner, Purpose = "meet",
            StartsAt = start, EndsAt = start + 60, GraceSeconds = 30, Importance = importance,
            Place = new AppointmentPlace { Kind = "building", SettlementId = 100, BuildingKey = 20 } };
        True(store.TryReserveAppointment(booking, 500, out var reason), reason);
        return store;
    }

    private static WorldState LiveWorld(WorldStateStore store)
    {
        var field = typeof(WorldStateStore).GetField("state",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        True(field != null, "Store state field unavailable.");
        return (WorldState)field.GetValue(store);
    }

    private static int ObservationCount(WorldStateStore store, string owner, string phrase)
    {
        var memory = LiveWorld(store).ConversationMemories.Find(item => item.PersonId == owner);
        if (memory == null) return 0;
        int count = 0;
        foreach (var entry in memory.Entries)
            if (entry.Source == "observed" && entry.Observation != null && entry.Observation.Contains(phrase)) count++;
        return count;
    }

    private static string SnapshotText(WorldStateStore store)
    { return JsonSerializer.Serialize(store.Snapshot(), SnapshotOptions); }

    private static void ThrowsArgument(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Expected ArgumentException.");
    }

    private static void True(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException("Expected " + expected + ", got " + actual + "."); }
}
