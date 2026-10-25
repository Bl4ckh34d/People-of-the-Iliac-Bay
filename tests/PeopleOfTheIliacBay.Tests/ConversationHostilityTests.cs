using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class ConversationHostilityTests
    {
        public static void Add(List<(string Name, System.Action Body)> cases)
        {
            cases.Add(("Hostility warns only in speaker owned place and refuses facts", () => {
                Check.Equal(ConversationHostilityAction.None, ConversationHostility.Decide(-49, true, true));
                Check.Equal(ConversationHostilityAction.Warn, ConversationHostility.Decide(-50, true, false));
                Check.Equal(ConversationHostilityAction.RefuseAndWarn, ConversationHostility.Decide(-50, true, true));
                Check.Equal(ConversationHostilityAction.None, ConversationHostility.Decide(-50, false, true));
            }));
            cases.Add(("Owned-place refusal uses speaker first-person wording", () => {
                Check.Equal("my shop", ConversationHostility.FirstPersonPlace("the shopkeeper's shop"));
                Check.Equal("my inn", ConversationHostility.FirstPersonPlace("the innkeeper's inn"));
                Check.Equal("my home", ConversationHostility.FirstPersonPlace("the NPC's home"));
            }));
            cases.Add(("Personal dislike may end conversation but cannot justify a guard report", () => {
                Check.Equal(ConversationHostilityAction.EndConversation, ConversationHostility.Decide(-100, true, false));
                Check.Equal(ConversationHostilityAction.EndConversation, ConversationHostility.Decide(-100, false, true));
            }));
            cases.Add(("Sincere apology is a recognized chance before terminal hostility", () => {
                var parsed = ConversationKnowledge.Parse("social|-1|apology", new List<ConversationTopic>(),
                    "I am sorry I insulted you", -1);
                Check.True(parsed != null && parsed.Tone == "apology");
                Check.Equal("apology",ConversationKnowledge.Parse("social|-1|apology|none",new List<ConversationTopic>(),
                    "I regret how I treated you",-1).Tone);
            }));
            cases.Add(("Exterior dialogue complaint retains route progress across save load", () => {
                var store = new WorldStateStore(7);
                store.SetDialogueComplaint(new DialogueComplaintRecord {
                    PersonId = "resident:7:owner", SourceReceiptId = "native-heard-v2-receipt-example-1",
                    SettlementId = 7, StartedAt = 10, LastUpdatedAt = 10, ExpiresAt = 100,
                    X = 1, Y = 0, Z = 2, CrimeX = -3, CrimeY = 0, CrimeZ = 6, GuardId = "guard:2", NextWaypoint = 2,
                    Route = new List<SavedRoutePoint> {
                        new SavedRoutePoint { X = 1, Z = 2 }, new SavedRoutePoint { X = 4, Z = 5 },
                        new SavedRoutePoint { X = 8, Z = 9 } }
                });
                var restored = new WorldStateStore(0);
                restored.Restore(store.Snapshot());
                var complaint = restored.GetDialogueComplaint("resident:7:owner");
                Check.Equal(2, complaint.NextWaypoint);
                Check.Equal("guard:2", complaint.GuardId);
                Check.Equal(3, complaint.Route.Count);
                Check.Equal(4d, complaint.Route[1].X);
                Check.Equal(-3d, complaint.CrimeX);
                Check.Equal(6d, complaint.CrimeZ);
                Check.Equal("native-heard-v2-receipt-example-1", complaint.SourceReceiptId);
                complaint.NextWaypoint = 1;
                Check.Equal(2, restored.GetDialogueComplaint("resident:7:owner").NextWaypoint);
                complaint.SourceReceiptId = "different-receipt";
                Check.Equal("native-heard-v2-receipt-example-1",
                    restored.GetDialogueComplaint("resident:7:owner").SourceReceiptId);
                var invalid = restored.GetDialogueComplaint("resident:7:owner");
                invalid.SourceReceiptId = " ";
                Check.Throws<System.ArgumentException>(() => restored.SetDialogueComplaint(invalid));
                var legacy = store.GetDialogueComplaint("resident:7:owner");
                legacy.SourceReceiptId = null;
                store.SetDialogueComplaint(legacy);
                var restoredLegacy = new WorldStateStore(0);
                restoredLegacy.Restore(store.Snapshot());
                Check.True(restoredLegacy.GetDialogueComplaint("resident:7:owner").SourceReceiptId == null);
            }));
            cases.Add(("Unbound legacy dialogue complaints retire without becoming crime evidence", (System.Action)(() => {
                var store=new WorldStateStore(7);
                store.SetDialogueComplaint(new DialogueComplaintRecord{PersonId="resident:legacy",SettlementId=7,
                    StartedAt=10,LastUpdatedAt=10,ExpiresAt=100,NextWaypoint=1,
                    Route=new List<SavedRoutePoint>{new SavedRoutePoint(),new SavedRoutePoint{X=1}}});
                var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
                Check.True(restored.TryRetireUnsupportedDialogueComplaint("resident:legacy",11));
                Check.True(restored.GetDialogueComplaint("resident:legacy")==null);
                Check.True(!restored.TryRetireUnsupportedDialogueComplaint("resident:legacy",12));
                Check.Equal(0,restored.Snapshot().AlarmIncidents.Count);
            })));
            cases.Add(("Receipt complaint start is idempotent and cannot replace another report", () => {
                var store = new WorldStateStore(7);
                var first = new DialogueComplaintRecord {
                    PersonId = "resident:7:owner", SourceReceiptId = "receipt:first", SettlementId = 7,
                    StartedAt = 10, LastUpdatedAt = 10, ExpiresAt = 100,
                    X = 1, Y = 0, Z = 2, CrimeX = -3, CrimeY = 0, CrimeZ = 6,
                    GuardId = null, NextWaypoint = 1,
                    Route = new List<SavedRoutePoint> {
                        new SavedRoutePoint { X = 1, Z = 2 }, new SavedRoutePoint { X = 1, Z = 2 } }
                };
                Check.True(store.TryStartReceiptDialogueComplaint(first));
                var replay = store.GetDialogueComplaint(first.PersonId);
                replay.CrimeX = 99;
                Check.True(store.TryStartReceiptDialogueComplaint(replay));
                Check.Equal(-3d, store.GetDialogueComplaint(first.PersonId).CrimeX);
                replay.SourceReceiptId = "receipt:second";
                Check.True(!store.TryStartReceiptDialogueComplaint(replay));
                Check.Equal("receipt:first", store.GetDialogueComplaint(first.PersonId).SourceReceiptId);
            }));
        }
    }
}
