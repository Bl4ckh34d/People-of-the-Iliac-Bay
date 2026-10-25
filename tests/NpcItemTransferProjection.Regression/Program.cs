using PeopleOfTheIliacBay.Tests;
using System;
using System.Collections.Generic;
using System.Reflection;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int Main()
    {
        var cases = new List<(string Name, Action Body)>
        {
            ("projected receipt survives valid future inbox retention", ProjectionSurvivesInboxRetention),
            ("matching canonical inbox outcome repairs legacy projection marker", MatchingOutcomeMarksReceipt),
            ("conflicting inbox outcome never marks the receipt", ConflictingOutcomeDoesNotMark),
            ("failed enqueue leaves action and inbox projection unmarked", FailedEnqueueDoesNotMark),
            ("projection marker requires the exact completed receipt", InvalidMarkerRejected),
            ("legacy completed transfer without receipt stays unmarked", MissingReceiptStaysUnmarked)
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

    private static void ProjectionSurvivesInboxRetention()
    {
        var store = CreateCompletedTransfer(true);
        Equal(1, store.DrainNpcItemTransferReceipts(32));
        var marked = store.ReadNpcAction("projection-action");
        True(marked.ProjectedItemTransferReceipt != null, "Successful enqueue must mark the completed receipt.");
        True(!ReferenceEquals(marked.ItemTransferReceipt, marked.ProjectedItemTransferReceipt),
            "Projection marker must be a detached receipt copy.");
        marked.ProjectedItemTransferReceipt.Quantity = 0;
        Equal(2, store.ReadNpcAction(marked.Id).ProjectedItemTransferReceipt.Quantity);
        Equal(3L, marked.Revision);
        Equal(12d, marked.UpdatedAt);
        Equal(0, store.DrainNpcItemTransferReceipts(32));
        var position = new NpcActionTarget { Kind = NpcActionTargetKind.WorldPoint,
            SettlementId = 20, WorldSpaceId = "settlement:20", X = 1, Z = 1 };
        True(store.SaveNpcActionPosition(marked.Id, marked.Revision, position, false, 20),
            "Projection metadata must not stale a later action position update.");
        Equal(4L, store.ReadNpcAction(marked.Id).Revision);
        True(store.ReadNpcAction(marked.Id).ProjectedItemTransferReceipt != null,
            "Later action updates must retain the projection marker.");

        Appraise(store, marked.ResidentId, 13);
        Equal(1, store.MaintainNpcExperienceInboxHistory(8 * 86400 + 20, 16, 4, true).Retired);
        var saved = store.Snapshot();
        Equal(1L, saved.NpcExperienceInbox.Revision);
        Equal(1L, saved.NpcExperienceInbox.RetiredCount);
        Equal(0, saved.NpcExperienceInbox.Entries.Count);
        var restored = new WorldStateStore(0);
        restored.Restore(saved);
        Equal(0, restored.DrainNpcItemTransferReceipts(32));
        Equal(0, restored.Snapshot().NpcExperienceInbox.Entries.Count);
        True(restored.ReadNpcAction("projection-action").ProjectedItemTransferReceipt != null,
            "Projection marker must survive snapshot and restore.");
    }

    private static void MatchingOutcomeMarksReceipt()
    {
        var store = CreateCompletedTransfer(true);
        var action = store.ReadNpcAction("projection-action");
        True(HistoricalNpcExperienceFixture.ImportHistoricalNativeEvent(store, action.ItemTransferReceipt.ToNativeEvent(action.Id)),
            "Canonical fixture outcome was rejected.");
        Equal(0, store.DrainNpcItemTransferReceipts(32));
        True(store.ReadNpcAction(action.Id).ProjectedItemTransferReceipt != null,
            "Exact existing evidence should repair an unmarked legacy action.");
        Equal(1, store.Snapshot().NpcExperienceInbox.Entries.Count);
    }

    private static void ConflictingOutcomeDoesNotMark()
    {
        var store = CreateCompletedTransfer(true);
        var action = store.ReadNpcAction("projection-action");
        var conflicting = NpcExperienceEvent.FromNativeOutcome(action.ResidentId, action.ResidentId,
            action.ItemTransferReceipt.TransactionId, action.Id + ":item-transfer", action.Id + ":item-received",
            NpcExperienceEventKind.ItemReceived, 12, NpcResponsibleActorAttribution.Known, "player",
            NpcExperienceImpactDetails.ForReceivedItemTransfer(999, 2,
                NpcExperienceNativeValuationStatus.Unknown, 0));
        True(HistoricalNpcExperienceFixture.ImportHistoricalNativeEvent(store, conflicting), "Conflicting historical fixture outcome was not imported.");
        Equal(0, store.DrainNpcItemTransferReceipts(32));
        True(store.ReadNpcAction(action.Id).ProjectedItemTransferReceipt == null,
            "Deduplication key alone must not attest receipt projection.");
    }

    private static void InvalidMarkerRejected()
    {
        var store = CreateCompletedTransfer(true);
        Equal(1, store.DrainNpcItemTransferReceipts(32));
        var saved = store.Snapshot();
        saved.NpcActions[0].ProjectedItemTransferReceipt.Quantity++;
        Throws<ArgumentException>(() => new WorldStateStore(0).Restore(saved));

        saved = store.Snapshot();
        saved.NpcActions[0].ItemTransferReceipt = null;
        Throws<ArgumentException>(() => new WorldStateStore(0).Restore(saved));

        saved = store.Snapshot();
        saved.NpcActions[0].Phase = NpcActionPhase.Failed;
        Throws<ArgumentException>(() => new WorldStateStore(0).Restore(saved));
    }

    private static void FailedEnqueueDoesNotMark()
    {
        var store = CreateCompletedTransfer(true);
        var field = typeof(WorldStateStore).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic);
        True(field != null, "Fault injection could not reach the private world state.");
        var live = (WorldState)field.GetValue(store);
        live.NpcExperienceInbox.Revision = long.MaxValue; // Invalid inbox simulates enqueue rejection.
        Equal(0, store.DrainNpcItemTransferReceipts(32));
        var unchanged = store.ReadNpcAction("projection-action");
        True(unchanged.ProjectedItemTransferReceipt == null, "Failed enqueue must leave marker unset.");
        Equal(3L, unchanged.Revision);
        Equal(12d, unchanged.UpdatedAt);
        Equal(0, live.NpcExperienceInbox.Entries.Count);
        live.NpcExperienceInbox.Revision = 0;
        Equal(1, store.DrainNpcItemTransferReceipts(32));
        True(store.ReadNpcAction("projection-action").ProjectedItemTransferReceipt != null,
            "Drain must retry after inbox repair.");
    }

    private static void MissingReceiptStaysUnmarked()
    {
        var store = CreateCompletedTransfer(false);
        Equal(0, store.DrainNpcItemTransferReceipts(32));
        True(store.ReadNpcAction("projection-action").ProjectedItemTransferReceipt == null,
            "Legacy completed action without a native transfer receipt must stay unmarked.");
    }

    private static WorldStateStore CreateCompletedTransfer(bool withReceipt)
    {
        var store = new WorldStateStore(109);
        string resident = store.GetOrCreate(20, new[] { 200 }, 1).Households[0].Residents[0].Id;
        var action = new NpcActionJob
        {
            Id = "projection-action", ProposalId = "projection-proposal", ResidentId = resident,
            Kind = NpcActionKind.Transfer,
            Target = new NpcActionTarget { Kind = NpcActionTargetKind.Person, PersonId = "player",
                ItemId = "777", Quantity = 2, TransferDirection = NpcActionTransferDirection.PlayerToNpc },
            Phase = NpcActionPhase.Executing, Revision = 2, AcceptedAt = 10, UpdatedAt = 11, DeadlineAt = 100
        };
        var receipt = withReceipt
            ? NpcActionTransferReceipt.CreatePlayerToNpc(action, action.Id + ":transfer", 777, 778, 778, 2,
                resident, "player", 12, NpcExperienceNativeValuationStatus.Unknown, 0)
            : null;
        action.Phase = NpcActionPhase.Completed;
        action.Revision = 3;
        action.UpdatedAt = 12;
        action.CompletedEffectId = action.Id + ":observed";
        action.ItemTransferReceipt = receipt;
        var saved = store.Snapshot();
        saved.NpcActions.Add(action);
        store.Restore(saved);
        return store;
    }

    private static void Appraise(WorldStateStore store, string person, double at)
    {
        True(store.TryCaptureNpcExperienceAppraisal(person, at, out var ticket),
            "Transfer source was not appraisable.");
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
        True(store.TryCommitNpcExperienceAppraisal(ticket, neutral), "Transfer appraisal failed.");
    }

    private static void True(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException("Expected " + expected + ", got " + actual + "."); }
    private static void Throws<T>(Action action) where T : Exception
    { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name + "."); }
}
