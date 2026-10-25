using PeopleOfTheIliacBay.Tests;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

static class Program
{
    static int assertions;
    static void Main()
    {
        // Reflection permits a real executable RED before the new public contract exists.
        Check(typeof(WorldStateStore).GetMethod("TryCaptureNpcMemoryRead") != null, "Memory read capture API must exist");
        var root = Path.Combine(Path.GetTempPath(), "dfu-memory-read-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new WorldStateStore(42, root);
            var town = store.GetOrCreate(10, new[] { 20, 21 }, 1);
            string person = town.Households[0].Residents[0].Id;
            string other = town.Households[1].Residents[0].Id;
            Say(store, person, 10, "I own the silver ring.");
            Say(store, other, 10, "Secret emerald vault.");
            string before = JsonSerializer.Serialize(store.Snapshot(), new JsonSerializerOptions { IncludeFields = true });
            object read = Capture(store, "TryCaptureNpcMemoryRead", person, "silver", 20d, 1000);
            Check(Text(read).Contains("silver ring") && !Text(read).Contains("emerald"), "Bound speaker only");
            Check(Text(read).Length <= 1000, "Bounded source text");
            Check(before == JsonSerializer.Serialize(store.Snapshot(), new JsonSerializerOptions { IncludeFields = true }), "Reads never change saved state");
            Check(Current(store, read, person, 21), "Own unchanged source remains current");
            Check(!Current(store, read, other, 21), "Wrong speaker rejected");
            Check(!Current(store, read, person, 19), "Time rewind rejected");
            Check(!Current(new WorldStateStore(42, root), read, person, 21), "Foreign store rejected");
            Say(store, other, 21, "Other person changes.");
            Check(Current(store, read, person, 22), "Other person's pending memory does not invalidate own read");
            Say(store, person, 22, "The ring was sold.");
            Check(!Current(store, read, person, 23), "Own pending append invalidates old source");
            read = Capture(store, "TryCaptureNpcMemoryRead", person, "ring", 23d, 1000);
            var saved = store.SnapshotForSave();
            Check(!Current(store, read, person, 24), "Changed archive head invalidates read");
            read = Capture(store, "TryCaptureNpcMemoryRead", person, "ring", 24d, 1000);
            store.Restore(saved);
            Check(!Current(store, read, person, 24), "Same-head restore invalidates old receipt");
            Say(store, person, 100, "A future violet necklace.");
            read = Capture(store, "TryCaptureNpcMemoryRead", person, "necklace", 30d, 1000);
            Check(!Text(read).Contains("violet"), "Future source never exposed");
            Check(Current(store, read, person, 99), "Before future source eligibility unchanged");
            Check(!Current(store, read, person, 100), "Time crossing future source invalidates read");

            object mood = Capture(store, "TryCaptureNpcMoodCauseRead", person, 30d, 600);
            Check(Text(mood) == "", "Absent cause remains absent");
            Check(Current(store, mood, person, 30), "Unchanged empty cause has a valid receipt");
            var evidence = NpcExperienceEvent.FromNativeOutcome(person, person, "receipt:injury", "episode:injury",
                "outcome:injury", NpcExperienceEventKind.Injury, 30, NpcResponsibleActorAttribution.Unknown);
            Check(HistoricalNpcExperienceFixture.ImportHistoricalNativeEvent(store, evidence), "Historical injury fixture imported");
            Check(!Current(store, mood, person, 30), "New inbox evidence invalidates cause receipt even with same mood value");
            mood = Capture(store, "TryCaptureNpcMoodCauseRead", person, 30d, 600);
            var profile = store.GetOrCreateNpcPersonality(person, 42, NpcSocialBackground.Commoner);
            Check(store.TryCaptureNpcExperienceAppraisal(person, 30, out NpcExperienceAppraisalTicket ticket), "Appraisal captured");
            var r = ticket.Request;
            string response = JsonSerializer.Serialize(new {
                schemaVersion = NpcExperienceAppraisalContract.SchemaVersion, policyVersion = NpcExperienceAppraisalContract.PolicyVersion,
                saveBranchId = r.SaveBranchId, observerId = r.ObserverId, subjectPersonId = r.SubjectPersonId,
                evidenceDeduplicationKey = r.EvidenceDeduplicationKey, canonicalEvidenceId = r.CanonicalEvidenceId,
                relationshipTargetId = r.RelationshipTargetId ?? "", expectedMoodRevision = r.ExpectedMoodRevision,
                expectedAffinityRevision = r.ExpectedAffinityRevision, expectedRelationshipRevision = r.ExpectedRelationshipRevision,
                moodDelta = 0, affinityDelta = 0, trustDelta = 0, resentmentDelta = 0, fearDelta = 0,
                speechActKind = "NotApplicable", speechTargetPersonId = "", conversationOption = "NoAction"
            });
            // Use the existing accepted-appraisal contract, not mutation through reflection.
            Check(store.TryCommitNpcExperienceAppraisal(ticket, response), "Zero-effect appraisal consumed");
            Check(!Current(store, mood, person, 30), "Consumption invalidates when inbox count and mood value remain unchanged");
            Console.WriteLine("NPC memory read authority: " + assertions + " assertions passed.");
        }
        finally { Directory.Delete(root, true); }
    }
    static object Capture(WorldStateStore store, string name, params object[] args)
    {
        var method = typeof(WorldStateStore).GetMethod(name);
        Check(method != null, "Capture API " + name);
        var values = args.Concat(new object[] { null }).ToArray();
        Check((bool)method.Invoke(store, values), "Capture succeeds");
        return values[values.Length - 1];
    }
    static bool Current(WorldStateStore store, object read, string person, double now)
    { return (bool)typeof(WorldStateStore).GetMethod("IsNpcMemoryReadCurrent").Invoke(store, new object[] { read, person, now }); }
    static string Text(object read) { return (string)read.GetType().GetProperty("Text").GetValue(read); }
    static void Say(WorldStateStore store, string person, double at, string text)
    { store.RememberConversation(person, at, "native date", "inn", text, "I heard you.", "native"); }
    static void Check(bool value, string message) { assertions++; if (!value) throw new Exception(message); }
}
