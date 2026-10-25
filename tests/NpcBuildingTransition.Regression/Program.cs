using System;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.Tests;

internal static class Program
{
    private static int failures;
    private static void Check(bool condition, string message)
    {
        if (!condition) { failures++; Console.WriteLine("FAIL " + message); }
    }

    private static NpcActionJob Job(NpcActionKind kind = NpcActionKind.Enter)
    {
        return new NpcActionJob
        {
            Id = "enter:1", ProposalId = "proposal:1", ResidentId = "resident:1", Kind = kind,
            Target = new NpcActionTarget { Kind = NpcActionTargetKind.Building,
                SettlementId = 7, BuildingKey = 88, DoorId = "door:17",
                WorldSpaceId = "exterior:7", DestinationSpaceId = "interior:7:88" },
            Phase = NpcActionPhase.Executing, Revision = 2,
            AcceptedAt = 100, UpdatedAt = 110, DeadlineAt = 200
        };
    }

    private static void MainTest()
    {
        Check((int)NpcActionKind.Lead == 14 && (int)NpcActionKind.Enter == 15 && (int)NpcActionKind.Exit == 16,
            "persisted action kinds must append");
        var choices = new[] {
            new NpcActionOption { Id=1, Kind=NpcActionKind.Enter, Target=Job().Target },
            new NpcActionOption { Id=2, Kind=NpcActionKind.Exit, Target=Job(NpcActionKind.Exit).Target }
        };
        foreach (var option in choices) option.Target.Quantity=1;
        Check(NpcActionChat.ParseSemantic("DIRECT|Enter|1|1|300",choices)!=null,"classified entry binds native candidate");
        Check(NpcActionChat.ParseSemantic("DIRECT|Exit|2|1|300",choices)!=null,"classified exit binds native candidate");
        Check(NpcActionChat.ParseSemantic("NONE",choices)==null,"no NPC request selects no action");
        Check(NpcActionChat.ParseSemantic("NEGATED",choices)==null,"negated classification selects no action");
        Check(NpcActionChat.ParseSemantic("DIRECT|Enter|2|1|300",choices)==null,"wrong native candidate rejects");
        foreach (NpcActionKind kind in new[] { NpcActionKind.Enter, NpcActionKind.Exit })
        {
            var job = Job(kind);
            NpcActionExecutor.Validate(job);
            Check(NpcTaskContract.Supported(kind), kind + " must be task supported");
            NpcActionJob completed;
            Check(!NpcActionExecutor.Advance(job, 2, 111, NpcActionObservation.Success("enter:1:observed"), out completed),
                kind + " generic success must fail");
            var receipt = NpcBuildingTransitionReceipt.Create(job, "resident:1", 7, 88, "door:17",
                "exterior:7", "interior:7:88", 111);
            Check(NpcActionExecutor.Advance(job, 2, 111, NpcActionObservation.BuildingTransitionSuccess(receipt), out completed),
                kind + " observed transition must complete");
            Check(completed.BuildingTransitionReceipt != null &&
                completed.BuildingTransitionReceipt.DoorId == "door:17" &&
                completed.BuildingTransitionReceipt.SourceSpaceId == "exterior:7" &&
                completed.BuildingTransitionReceipt.DestinationSpaceId == "interior:7:88",
                kind + " must persist exact receipt");
            NpcActionExecutor.Validate(NpcActionExecutor.Copy(completed));
            completed.BuildingTransitionReceipt.DoorId = "door:18";
            bool rejected = false;
            try { NpcActionExecutor.Validate(completed); } catch (ArgumentException) { rejected = true; }
            Check(rejected, kind + " changed receipt must fail validation");
        }

        var candidate = Job();
        foreach (string door in new[] { null, "" })
        {
            candidate.Target.DoorId = door;
            bool rejected = false;
            try { NpcActionExecutor.Validate(candidate); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "missing exact door must fail");
        }
        candidate = Job();
        candidate.Target.DestinationSpaceId = candidate.Target.WorldSpaceId;
        bool sameSpaceRejected = false;
        try { NpcActionExecutor.Validate(candidate); } catch (ArgumentException) { sameSpaceRejected = true; }
        Check(sameSpaceRejected, "same source and destination must fail");

        candidate = Job();
        var copiedTarget = NpcActionExecutor.CopyTarget(candidate.Target);
        Check(NpcTaskContract.TargetMatches(candidate.Target, copiedTarget),
            "task target copy must retain door and both spaces");
        copiedTarget.DoorId = "door:18";
        Check(!NpcTaskContract.TargetMatches(candidate.Target, copiedTarget),
            "task target matching must reject another door");
        copiedTarget = NpcActionExecutor.CopyTarget(candidate.Target);
        copiedTarget.DestinationSpaceId = "interior:other";
        Check(!NpcTaskContract.TargetMatches(candidate.Target, copiedTarget),
            "task target matching must reject another destination");

        candidate = Job();
        foreach (string mismatch in new[] { "door:18", "" })
        {
            bool rejected = false;
            try { NpcBuildingTransitionReceipt.Create(candidate, "resident:1", 7, 88, mismatch,
                "exterior:7", "interior:7:88", 111); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "wrong observed door must fail");
        }
        bool wrongSpaceRejected = false;
        try { NpcBuildingTransitionReceipt.Create(candidate, "resident:1", 7, 88, "door:17",
            "exterior:8", "interior:7:88", 111); } catch (ArgumentException) { wrongSpaceRejected = true; }
        Check(wrongSpaceRejected, "wrong source must fail");
        bool wrongActorRejected = false;
        try { NpcBuildingTransitionReceipt.Create(candidate, "other", 7, 88, "door:17",
            "exterior:7", "interior:7:88", 111); } catch (ArgumentException) { wrongActorRejected = true; }
        Check(wrongActorRejected, "wrong actor must fail");
        bool wrongBuildingRejected = false;
        try { NpcBuildingTransitionReceipt.Create(candidate, "resident:1", 7, 89, "door:17",
            "exterior:7", "interior:7:88", 111); } catch (ArgumentException) { wrongBuildingRejected = true; }
        Check(wrongBuildingRejected, "wrong observed building must fail");

        foreach (bool dead in new[] { false, true })
        {
            var store = new WorldStateStore(dead ? 301u : 300u);
            var resident = store.GetOrCreate(7, new[] { 88 }, 1).Households[0].Residents[0];
            var acceptedJob = Job();
            acceptedJob.Id = dead ? "dead-enter" : "quest-enter";
            acceptedJob.ProposalId = acceptedJob.Id;
            acceptedJob.ResidentId = resident.Id;
            acceptedJob.Phase = NpcActionPhase.Accepted;
            acceptedJob.Revision = 1;
            acceptedJob.UpdatedAt = acceptedJob.AcceptedAt;
            string reason;
            Check(ClassifiedAdmissionFixture.TryAcceptAction(store, acceptedJob, true, true, true, out reason),
                "transition acceptance: " + reason);
            var current = store.ReadNpcAction(acceptedJob.Id);
            Check(store.ObserveNpcAction(current.Id, current.Revision, 110, NpcActionObservation.Arrival()),
                "transition arrival");
            current = store.ReadNpcAction(acceptedJob.Id);
            var observed = NpcBuildingTransitionReceipt.Create(current, resident.Id, 7, 88, "door:17",
                "exterior:7", "interior:7:88", 111);
            Check(!store.ObserveNpcAction(current.Id, current.Revision, 111,
                NpcActionObservation.BuildingTransitionSuccess(observed)),
                "generic observation path must refuse transition completion");
            resident.IsAlive = !dead;
            resident.IsQuestProtected = !dead;
            Check(!store.TryCompleteNpcBuildingTransition(current.Id, current.Revision, 111, observed),
                "death or quest control must prevent completion");
            Check(store.ReadNpcAction(current.Id).Phase == NpcActionPhase.Executing,
                "failed transition must stay executing");
            resident.IsAlive = true;
            resident.IsQuestProtected = false;
            Check(store.TryCompleteNpcBuildingTransition(current.Id, current.Revision, 111, observed),
                "living unprotected actor must complete observed transition");
            Check(store.ReadNpcAction(current.Id).BuildingTransitionReceipt != null,
                "transition receipt must persist in store");
            current=store.ReadNpcAction(current.Id);
            var returnedPosition=new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,
                SettlementId=7,WorldSpaceId="exterior:7",X=1,Z=2};
            Check(store.SaveNpcActionPosition(current.Id,current.Revision,returnedPosition,true,112),
                "later return movement must preserve the actual earlier door receipt");
            var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
            Check(restored.ReadNpcAction(current.Id).BuildingTransitionReceipt.ObservedGameTime==111,
                "return position must not rewrite native door event time");
            var landingField=typeof(NpcBuildingTransitionReceipt).GetField("DestinationObserved");
            Check(landingField!=null && !(bool)landingField.GetValue(store.ReadNpcAction(current.Id).BuildingTransitionReceipt),
                "unseen destination must remain distinguishable from later return travel");
            current=store.ReadNpcAction(current.Id);
            var otherBuilding=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=7,BuildingKey=89,X=3,Y=4,Z=5};
            Check(store.SaveNpcActionPosition(current.Id,current.Revision,otherBuilding,true,112.5),"observe a different return room");
            current=store.ReadNpcAction(current.Id);
            Check(landingField!=null && !(bool)landingField.GetValue(current.BuildingTransitionReceipt),
                "a different building must not claim the exact destination was observed");
            var destination=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=7,BuildingKey=88,X=3,Y=4,Z=5};
            Check(store.SaveNpcActionPosition(current.Id,current.Revision,destination,true,113),"observe native destination position");
            current=store.ReadNpcAction(current.Id);
            Check(landingField!=null && (bool)landingField.GetValue(current.BuildingTransitionReceipt),
                "observed destination must retire the initial door handoff");
            Check(store.SaveNpcActionPosition(current.Id,current.Revision,returnedPosition,true,114),"walk back into the source world later");
            restored.Restore(store.Snapshot());
            Check(landingField!=null && (bool)landingField.GetValue(restored.ReadNpcAction(current.Id).BuildingTransitionReceipt),
                "later source-world return and restore must not rearm the old handoff");
        }
    }

    private static int Main()
    {
        MainTest();
        Console.WriteLine(failures == 0 ? "NpcBuildingTransition regression passed" : failures + " failures");
        return failures == 0 ? 0 : 1;
    }
}
