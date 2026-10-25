using System;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.Tests;

internal static class Program
{
    private static int checks;
    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private static NpcActionTarget Point(double x, int settlement = 7, string space = "settlement:7")
    { return new NpcActionTarget { Kind = NpcActionTargetKind.WorldPoint, SettlementId = settlement,
        BuildingKey = -1, WorldSpaceId = space, X = x }; }

    private static NpcActionJob Job(string id, string resident)
    {
        return new NpcActionJob { Id = id, ProposalId = id, ResidentId = resident,
            Kind = NpcActionKind.Lead, Phase = NpcActionPhase.Accepted, Revision = 1,
            Target = new NpcActionTarget { Kind = NpcActionTargetKind.Building,
                SettlementId = 7, BuildingKey = 88 }, LastPosition = Point(0),
            AcceptedAt = 100, UpdatedAt = 100, DeadlineAt = 200 };
    }

    private static void MainTest()
    {
        var job = Job("lead:one", "resident:one");
        var destination = Point(10);
        Check(NpcLeadMotion.Evaluate(job, Point(0), destination, 2, 0.25, 110) == NpcLeadProgress.Walking,
            "near player permits walking to saved building entrance");
        Check(NpcLeadMotion.Evaluate(job, Point(2), destination, 8.01, 0.25, 111) == NpcLeadProgress.WaitingForPlayer,
            "player crossing eight metres pauses before next movement frame");
        Check(NpcLeadMotion.Evaluate(job, Point(2), destination, 7.99, 0.25, 112) == NpcLeadProgress.Walking,
            "returning player resumes same destination");
        Check(NpcLeadMotion.Evaluate(job, Point(10), destination, 8.01, 0.25, 113) == NpcLeadProgress.WaitingForPlayer,
            "NPC at destination cannot finish without player");
        Check(NpcLeadMotion.Evaluate(job, Point(10), destination, 8, 0.25, 114) == NpcLeadProgress.Arrived,
            "exact boundary and exact destination permit completion");
        Check(NpcLeadMotion.Evaluate(job, Point(10.26), destination, 2, 0.25, 115) == NpcLeadProgress.Walking,
            "outside arrival radius cannot finish");
        Check(NpcLeadMotion.Evaluate(job, Point(10), destination, 2, 0.25, 200) == NpcLeadProgress.Expired,
            "deadline wins over apparent arrival");
        Check(NpcLeadMotion.Evaluate(job, Point(10), Point(10, 8, "settlement:8"), 2, 0.25, 116) == NpcLeadProgress.Invalid,
            "another settlement cannot prove arrival");
        Check(NpcLeadMotion.Evaluate(job, Point(10, 8, "settlement:8"), destination, 2, 0.25, 116) == NpcLeadProgress.Invalid,
            "another actor space cannot prove arrival");
        Check(NpcLeadMotion.Evaluate(job, null, destination, 2, 0.25, 116) == NpcLeadProgress.Unobserved,
            "missing native actor observation pauses without failure");
        Check(NpcLeadMotion.Evaluate(job, Point(10), destination, double.NaN, 0.25, 116) == NpcLeadProgress.Unobserved,
            "temporarily missing player observation pauses without failure");

        var store = new WorldStateStore(101u);
        var resident = store.GetOrCreate(7, new[] { 88 }, 1).Households[0].Residents[0];
        var accepted = Job("lead:save", resident.Id);
        string reason;
        Check(ClassifiedAdmissionFixture.TryAcceptAction(store, accepted, true, true, true, out reason),
            "classified Lead admission: " + reason);
        var current = store.ReadNpcAction(accepted.Id);
        Check(store.SaveNpcActionPosition(current.Id, current.Revision, Point(3), false, 120),
            "observed route progress saved");
        var json = JsonSerializer.Serialize(store.Snapshot(), new JsonSerializerOptions { IncludeFields = true });
        var restored = new WorldStateStore(0);
        restored.Restore(JsonSerializer.Deserialize<WorldState>(json,
            new JsonSerializerOptions { IncludeFields = true }));
        current = restored.ReadNpcAction(accepted.Id);
        Check(NpcLeadMotion.Evaluate(current, null, destination, double.NaN, 0.25, 120) == NpcLeadProgress.Unobserved &&
            restored.ReadNpcAction(accepted.Id).Phase == NpcActionPhase.Accepted,
            "transient scene reconstruction does not complete or cancel saved Lead");
        Check(current.Target.BuildingKey == 88 && current.LastPosition.X == 3 &&
            NpcLeadMotion.Evaluate(current, current.LastPosition, destination, 9, 0.25, 121) == NpcLeadProgress.WaitingForPlayer,
            "reload retains exact goal and current pause");
        Check(NpcLeadMotion.Evaluate(current, current.LastPosition, destination, 2, 0.25, 122) == NpcLeadProgress.Walking,
            "rejoin resumes from saved position toward same goal");
        Check(restored.ObserveNpcAction(current.Id, current.Revision, 123,
            NpcActionObservation.Cancelled("The player asked the NPC to stop.")),
            "cancel accepted after reload");
        current = restored.ReadNpcAction(accepted.Id);
        Check(NpcLeadMotion.Evaluate(current, current.LastPosition, destination, 2, 0.25, 124) == NpcLeadProgress.Invalid &&
            NpcActionFeedback.Describe(current, "Mara", null, null) == "Mara stops the agreed action.",
            "cancel never reports destination reached");
    }

    private static int Main()
    {
        MainTest();
        Console.WriteLine("NpcLead regression passed: " + checks + " checks");
        return 0;
    }
}
