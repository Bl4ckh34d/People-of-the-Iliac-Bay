using System;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int Main()
    {
        int passed = 0, failed = 0;
        void Run(string name, Action test)
        {
            try { test(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception error) { failed++; Console.WriteLine("FAIL " + name + ": " + error); }
        }
        Schema50Cases.Run(Run);
        Run("retired poison detail does not reset source sequence", RetiredDetailCannotReusePoisonSourceSequence);
        Console.WriteLine(passed + "/" + (passed + failed) + " passed");
        return failed == 0 ? 0 : 1;
    }

    private static void RetiredDetailCannotReusePoisonSourceSequence()
    {
        var original = new WorldStateStore(42);
        string observer = original.GetOrCreate(100, new[] { 10 }, 1).Households[0].Residents[0].Id;
        object firstEpoch = original.CaptureNpcVitalsWriteEpoch();
        var firstTicket = original.TryIssueNpcPoisonAssignment(firstEpoch, observer, new object(), new object());
        Require(original.TryRecordNpcPoisonAssignment(firstTicket, "poison:classic", 10,
            out NpcExperienceEvent first) == NpcPoisonCaptureDisposition.Queued,
            "first native poison assignment was rejected");
        Require(first.NativeImpactDetails.StatusOnsetSequence == 1,
            "first native poison assignment did not receive sequence 1");

        // Inbox detail may be retired independently of the durable source cursor.
        var saved = original.Snapshot();
        int removed = saved.NpcExperienceInbox.Entries.RemoveAll(row =>
            row.ObserverId == observer &&
            row.ImpactDetailsKind == (int)NpcExperienceImpactKind.StatusOnset &&
            row.StatusKind == (int)NpcExperienceStatusKind.Poison);
        Require(removed == 1, "expected exactly one retained poison detail");
        saved.NpcExperienceInbox.Revision = saved.NpcExperienceInbox.Entries.Count;

        var restored = new WorldStateStore(0);
        restored.Restore(saved);
        Require(restored.Snapshot().NpcExperienceInbox.Entries.Count == 0,
            "retired poison detail unexpectedly survived restore");

        object nextEpoch = restored.CaptureNpcVitalsWriteEpoch();
        var secondTicket = restored.TryIssueNpcPoisonAssignment(nextEpoch, observer, new object(), new object());
        Require(restored.TryRecordNpcPoisonAssignment(secondTicket, "poison:classic", 11,
            out NpcExperienceEvent second) == NpcPoisonCaptureDisposition.Queued,
            "new native poison assignment was rejected");
        Require(second.NativeImpactDetails.StatusOnsetSequence == 2,
            "new native poison assignment reused sequence " +
            second.NativeImpactDetails.StatusOnsetSequence + " after retained detail removal");
        Require(second.DeduplicationKey != first.DeduplicationKey,
            "new native poison assignment reused the first source identity");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
