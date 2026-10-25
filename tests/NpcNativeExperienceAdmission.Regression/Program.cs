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
            ("free native event has no admission authority", FreeNativeEventRejected),
            ("different kind cannot reuse an appointment source identity", DifferentKindSameSourceRejected),
            ("real health episode projects once across restore", HealthEpisodeProjectsOnce),
            ("real appointment projects once and imported pending row remains appraisable", AppointmentProjectsAndImports),
            ("real completed transfer receipt projects once", TransferReceiptProjectsOnce)
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

    private static void FreeNativeEventRejected()
    {
        var store = Create(out var owner);
        var eventClaim = NpcExperienceEvent.FromNativeOutcome(owner, owner,
            "free-receipt", "free-episode", "free-outcome", NpcExperienceEventKind.AppointmentAttended,
            1000, NpcResponsibleActorAttribution.Known, "player");
        string before = SnapshotText(store);
        True(!store.TryEnqueueNpcExperience(eventClaim), "Caller-created native event was accepted.");
        Equal(before, SnapshotText(store));
        Equal(0, store.ReadPendingNpcExperiences(owner, 10).Count);
    }

    private static void DifferentKindSameSourceRejected()
    {
        var store = AttendedAppointment(out var owner);
        var appointment = store.FindAppointment("source-appointment");
        True(NpcAppointmentExperience.TryCreate(appointment, out var canonical),
            "Appointment did not yield canonical evidence.");
        var alteredKind = NpcExperienceEvent.FromNativeOutcome(owner, owner,
            canonical.ReceiptId, canonical.EpisodeId, canonical.OutcomeId,
            NpcExperienceEventKind.AppointmentNoShow, canonical.ObservedGameTime,
            canonical.ResponsibleActorAttribution, canonical.ResponsibleActorId);
        True(alteredKind.DeduplicationKey != canonical.DeduplicationKey,
            "Kind change must exercise a distinct deduplication key.");
        string before = SnapshotText(store);
        True(!store.TryEnqueueNpcExperience(alteredKind),
            "Different event kind reused an existing appointment source.");
        Equal(before, SnapshotText(store));
        Equal(1, store.ReadPendingNpcExperiences(owner, 10).Count);
    }

    private static void HealthEpisodeProjectsOnce()
    {
        var store = Create(out var owner);
        var baseline = store.ReadNpcVitals(owner, NpcArchetype.Citizen);
        object epoch = store.CaptureNpcVitalsWriteEpoch();
        True(store.TryCaptureObservedNpcVitalsChange(epoch, owner, NpcArchetype.Citizen,
            baseline.CurrentHealth, baseline.CurrentMagicka, baseline.CurrentHealth - 1,
            baseline.CurrentMagicka, 100, baseline.Revision), "Native health observation failed.");
        Equal(1, store.FlushNpcHealthExperiences(160, 60, 10).Count);
        var pending = store.ReadPendingNpcExperiences(owner, 10);
        Equal(1, pending.Count);
        Equal(NpcExperienceEventKind.Injury, pending[0].EventKind.Value);
        Equal(0, store.FlushNpcHealthExperiences(220, 60, 10).Count);
        var restored = new WorldStateStore(0);
        restored.Restore(store.Snapshot());
        Equal(0, restored.FlushNpcHealthExperiences(280, 60, 10).Count);
        Equal(1, restored.ReadPendingNpcExperiences(owner, 10).Count);
    }

    private static void AppointmentProjectsAndImports()
    {
        var store = AttendedAppointment(out var owner);
        Equal(1, store.ReadPendingNpcExperiences(owner, 10).Count);
        Equal(0, store.FlushNpcAppointmentExperiences(1011, 1));
        var restored = new WorldStateStore(0);
        restored.Restore(store.Snapshot());
        Equal(0, restored.FlushNpcAppointmentExperiences(2000, 1));
        Equal(1, restored.ReadPendingNpcExperiences(owner, 10).Count);
        True(restored.TryCaptureNpcExperienceAppraisal(owner, 1010, out var ticket),
            "Imported canonical pending appointment lost appraisal authority.");
        Equal("source-appointment:attendance", ticket.Request.CanonicalEvidenceId);
        Equal(owner, ticket.Request.ObserverId);
    }

    private static void TransferReceiptProjectsOnce()
    {
        var store = Create(out var owner);
        var action = new NpcActionJob
        {
            Id = "admission-transfer", ProposalId = "admission-proposal", ResidentId = owner,
            Kind = NpcActionKind.Transfer,
            Target = new NpcActionTarget { Kind = NpcActionTargetKind.Person, PersonId = "player",
                ItemId = "777", Quantity = 2, TransferDirection = NpcActionTransferDirection.PlayerToNpc },
            Phase = NpcActionPhase.Executing, Revision = 2, AcceptedAt = 10, UpdatedAt = 11, DeadlineAt = 100
        };
        var receipt = NpcActionTransferReceipt.CreatePlayerToNpc(action, action.Id + ":transfer",
            777, 778, 778, 2, owner, "player", 12,
            NpcExperienceNativeValuationStatus.Unknown, 0);
        action.Phase = NpcActionPhase.Completed;
        action.Revision = 3;
        action.UpdatedAt = 12;
        action.CompletedEffectId = action.Id + ":observed";
        action.ItemTransferReceipt = receipt;
        var saved = store.Snapshot();
        saved.NpcActions.Add(action); // Imported legacy completed work with an exact native receipt.
        store.Restore(saved);
        Equal(1, store.DrainNpcItemTransferReceipts(32));
        Equal(0, store.DrainNpcItemTransferReceipts(32));
        Equal(1, store.ReadPendingNpcExperiences(owner, 10).Count);
        True(store.ReadNpcAction(action.Id).ProjectedItemTransferReceipt != null,
            "Successful transfer projection did not retain its durable marker.");
        var restored = new WorldStateStore(0);
        restored.Restore(store.Snapshot());
        Equal(0, restored.DrainNpcItemTransferReceipts(32));
        Equal(1, restored.ReadPendingNpcExperiences(owner, 10).Count);
    }

    private static WorldStateStore AttendedAppointment(out string owner)
    {
        var store = Create(out owner);
        var booking = new AppointmentRecord { Id = "source-appointment", ResidentId = owner,
            Purpose = "meet", StartsAt = 1000, EndsAt = 1060, GraceSeconds = 30,
            Place = new AppointmentPlace { Kind = "building", SettlementId = 20, BuildingKey = 200 } };
        True(store.TryReserveAppointment(booking, 500, out var reason), reason);
        store.ObserveAppointment(booking.Id, 1000, true, false, false);
        store.ObserveAppointment(booking.Id, 1010, true, true, false);
        Equal("attended", store.FindAppointment(booking.Id).Status);
        return store;
    }

    private static WorldStateStore Create(out string owner)
    {
        var store = new WorldStateStore(109);
        owner = store.GetOrCreate(20, new[] { 200 }, 1).Households[0].Residents[0].Id;
        return store;
    }

    private static string SnapshotText(WorldStateStore store)
    { return JsonSerializer.Serialize(store.Snapshot(), SnapshotOptions); }

    private static void True(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException("Expected " + expected + ", got " + actual + "."); }
}
