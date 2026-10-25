using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FullSerializer;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { IncludeFields = true };

    private static int Main()
    {
        int failures = 0;
        Run("attended appointment cannot replay after v5 retirement and branch restore", AppointmentReplay, ref failures);
        Run("native item receipt cannot replay after v5 retirement and branch restore", TransferReplay, ref failures);
        Console.WriteLine((2 - failures) + "/2 passed");
        return failures == 0 ? 0 : 1;
    }

    private static void AppointmentReplay()
    {
        var store = NewStore(out string person);
        var booking = new AppointmentRecord { Id = "v5-appointment", ResidentId = person,
            Purpose = "meet", StartsAt = 1000, EndsAt = 1060, GraceSeconds = 30,
            Place = new AppointmentPlace { Kind = "building", SettlementId = 20, BuildingKey = 200 } };
        True(store.TryReserveAppointment(booking, 500, out string reason), reason);
        store.ObserveAppointment(booking.Id, 1000, true, false, false);
        store.ObserveAppointment(booking.Id, 1010, true, true, false);
        Equal(NpcAppointmentExperienceProjection.Projected,
            store.FindAppointment(booking.Id).ExperienceProjection);
        Appraise(store, person, 1011);
        RetireOne(store, person);

        var branch = JsonRestore(store);
        var before = branch.Snapshot();
        Equal(NpcAppointmentExperienceProjection.Projected,
            branch.FindAppointment(booking.Id).ExperienceProjection);
        Equal(0, branch.FlushNpcAppointmentExperiences(8 * 86400 + 30, 1));
        branch.ObserveAppointment(booking.Id, 8 * 86400 + 31, true, true, false);
        Equal(JsonSerializer.Serialize(before, Json), JsonSerializer.Serialize(branch.Snapshot(), Json));
        var replayBranch = JsonRestore(branch);
        Equal(0, replayBranch.FlushNpcAppointmentExperiences(8 * 86400 + 40, 1));
        Equal(JsonSerializer.Serialize(before, Json), JsonSerializer.Serialize(replayBranch.Snapshot(), Json));
    }

    private static void TransferReplay()
    {
        var store = NewStore(out string person);
        var action = new NpcActionJob { Id = "v5-transfer", ProposalId = "v5-transfer-proposal",
            ResidentId = person, Kind = NpcActionKind.Transfer,
            Target = new NpcActionTarget { Kind = NpcActionTargetKind.Person, PersonId = "player",
                ItemId = "777", Quantity = 2,
                TransferDirection = NpcActionTransferDirection.PlayerToNpc },
            Phase = NpcActionPhase.Executing, Revision = 2, AcceptedAt = 10, UpdatedAt = 11,
            DeadlineAt = 100 };
        action.ItemTransferReceipt = NpcActionTransferReceipt.CreatePlayerToNpc(action,
            action.Id + ":transfer", 777, 778, 778, 2, person, "player", 12,
            NpcExperienceNativeValuationStatus.Unknown, 0);
        action.Phase = NpcActionPhase.Completed;
        action.Revision = 3;
        action.UpdatedAt = 12;
        action.CompletedEffectId = action.Id + ":observed";
        var historical = store.Snapshot();
        historical.NpcActions.Add(action); // Persisted completed native work, not a forged inbox row.
        store.Restore(historical);
        Equal(1, store.DrainNpcItemTransferReceipts(32));
        var marked = store.ReadNpcAction(action.Id);
        True(marked.ProjectedItemTransferReceipt != null, "Native receipt lacked its durable projection marker.");
        Appraise(store, person, 13);
        RetireOne(store, person);

        var branch = JsonRestore(store);
        var before = branch.Snapshot();
        Equal(0, branch.DrainNpcItemTransferReceipts(32));
        Equal(JsonSerializer.Serialize(before, Json), JsonSerializer.Serialize(branch.Snapshot(), Json));
        var replayed = branch.ReadNpcAction(action.Id);
        True(replayed.ProjectedItemTransferReceipt != null, "Receipt marker was lost after JSON restore.");
        Equal(3L, replayed.Revision);
        Equal(12d, replayed.UpdatedAt);
        var replayBranch = JsonRestore(branch);
        Equal(0, replayBranch.DrainNpcItemTransferReceipts(32));
        Equal(JsonSerializer.Serialize(before, Json), JsonSerializer.Serialize(replayBranch.Snapshot(), Json));
    }

    private static void RetireOne(WorldStateStore store, string person)
    {
        var accepted = store.Snapshot().NpcExperienceInbox;
        Equal(1L, accepted.Revision);
        Equal(1, accepted.Entries.Count);
        Equal(NpcExperienceInboxStatus.Consumed, accepted.Entries[0].Status);
        int retired = 0;
        for (int i = 0; i < 8 && retired == 0; i++)
            retired += store.MaintainNpcExperienceInboxHistory(8 * 86400 + 20, 16, 4, true).Retired;
        Equal(1, retired);
        var after = store.Snapshot().NpcExperienceInbox;
        Equal(1L, after.Revision);
        Equal(1L, after.RetiredCount);
        Equal(0, after.Entries.Count);
        Equal(0, store.ReadPendingNpcExperiences(person, 10).Count);
    }

    private static void Appraise(WorldStateStore store, string person, double at)
    {
        True(store.TryCaptureNpcExperienceAppraisal(person, at, out var ticket),
            "Native source did not produce an appraisable experience.");
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
        True(store.TryCommitNpcExperienceAppraisal(ticket, neutral), "Valid neutral appraisal failed.");
    }

    private static WorldStateStore NewStore(out string person)
    {
        var store = new WorldStateStore(109);
        person = store.GetOrCreate(20, new[] { 200 }, 1).Households[0].Residents[0].Id;
        return store;
    }

    private static WorldStateStore JsonRestore(WorldStateStore store)
    {
        var serializer = new fsSerializer();
        fsData saved;
        True(serializer.TrySerialize(store.Snapshot(), out saved).Succeeded,
            "Native FullSerializer could not encode saved world state.");
        string serialized = fsJsonPrinter.CompressedJson(saved);
        WorldState wire = null;
        True(serializer.TryDeserialize(fsJsonParser.Parse(serialized), ref wire).Succeeded && wire != null,
            "Native FullSerializer could not decode saved JSON.");
        var branch = new WorldStateStore(0);
        branch.Restore(wire);
        return branch;
    }

    private static void Run(string name, Action test, ref int failures)
    {
        try { test(); Console.WriteLine("PASS " + name); }
        catch (Exception error) { failures++; Console.WriteLine("FAIL " + name + ": " + error); }
    }

    private static void True(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    private static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException("Expected " + expected + ", got " + actual + "."); }
}
