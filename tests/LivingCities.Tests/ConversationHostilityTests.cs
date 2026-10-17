using System.Collections.Generic;
using LivingCities.Core;

namespace LivingCities.Tests
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
            cases.Add(("Maximum hostility ends conversation and reports", () => {
                Check.Equal(ConversationHostilityAction.EndAndReport, ConversationHostility.Decide(-100, true, false));
                Check.Equal(ConversationHostilityAction.EndAndReport, ConversationHostility.Decide(-100, false, true));
            }));
            cases.Add(("Sincere apology is a recognized chance before terminal hostility", () => {
                var parsed = ConversationKnowledge.Parse("social|-1|apology", new List<ConversationTopic>(),
                    "I am sorry I insulted you", -1);
                Check.True(parsed != null && parsed.Tone == "apology");
                Check.True(ConversationKnowledge.Prompt("I am sorry I insulted you", new List<ConversationTopic>(), -1)
                    .Contains("apology (a direct sincere apology"));
            }));
            cases.Add(("Exterior dialogue complaint retains route progress across save load", () => {
                var store = new WorldStateStore(7);
                store.SetDialogueComplaint(new DialogueComplaintRecord {
                    PersonId = "resident:7:owner", SettlementId = 7, StartedAt = 10, LastUpdatedAt = 10, ExpiresAt = 100,
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
                complaint.NextWaypoint = 1;
                Check.Equal(2, restored.GetDialogueComplaint("resident:7:owner").NextWaypoint);
            }));
        }
    }
}
