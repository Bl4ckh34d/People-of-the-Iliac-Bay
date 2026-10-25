using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcGoalPendingClarificationTests
    {
        private static ConversationContextBinding Binding(long revision = 1, string person = "Mara",
            long epoch = 1, string scene = "inn", long sceneRevision = 1, string session = "session") =>
            new ConversationContextBinding(ConversationActorCategory.Mobile, person, null, session,
                revision, epoch, new string('a', 32), "", scene, sceneRevision);

        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Pending plan serializes player turns as escaped attributed data", () =>
            {
                const string original = "Wait\r\n\"two\" minutes; do not enter the cellar.";
                const string answer = "Ralen said 'ten hours', but I mean three minutes.\t";
                var pending = new NpcGoalPendingClarification(Binding(), original);
                Check.True(pending.TryAppend(Binding(2), answer));
                using (var data = System.Text.Json.JsonDocument.Parse(pending.ToExtractionData()))
                {
                    var turns = data.RootElement.GetProperty("playerTurns");
                    Check.Equal(2, turns.GetArrayLength());
                    Check.Equal("original-request", turns[0].GetProperty("role").GetString());
                    Check.Equal("clarification", turns[1].GetProperty("role").GetString());
                    Check.Equal("player", turns[0].GetProperty("speaker").GetString());
                    Check.Equal("Mara", turns[1].GetProperty("recipientPersonId").GetString());
                    Check.Equal(2L, turns[1].GetProperty("requestRevision").GetInt64());
                    Check.Equal(original, turns[0].GetProperty("text").GetString());
                    Check.Equal(answer, turns[1].GetProperty("text").GetString());
                }
            }));
            cases.Add(("Pending plan preserves exact attributed turns and immutable snapshots", () =>
            {
                const string original = " Go there, wait, then follow me.\r\n";
                var pending = new NpcGoalPendingClarification(Binding(), original);
                var before = pending.Turns;
                Check.True(pending.TryAppend(Binding(2), "The Old Smithy."));
                Check.True(pending.TryAppend(Binding(3), "For ninety seconds."));
                Check.Equal(original, pending.OriginalText);
                Check.Equal(1, before.Count);
                Check.Equal(3, pending.Turns.Count);
                Check.Equal("The Old Smithy.", pending.Turns[1].Text);
                Check.Equal("Mara", pending.Turns[1].Binding.PersonId);
                Check.Equal(ConversationTextSpeaker.Player, pending.Turns[2].Speaker);
                Check.Equal(3L, pending.Turns[2].Binding.RequestRevision);
            }));
            cases.Add(("Pending plan rejects owner epoch scene and session transitions permanently", () =>
            {
                foreach (var changed in new[] { Binding(2, person: "Ralen"), Binding(2, epoch: 2),
                    Binding(2, scene: "street"), Binding(2, sceneRevision: 2), Binding(2, session: "new") })
                {
                    var pending = new NpcGoalPendingClarification(Binding(), "Wait.");
                    Check.True(!pending.TryAppend(changed, "Three minutes."));
                    Check.True(!pending.IsActive);
                    Check.True(!pending.TryAppend(Binding(3), "Four minutes."));
                    Check.Equal(1, pending.Turns.Count);
                }
            }));
            cases.Add(("Pending plan proposal budget is three and new authoritative turns invalidate old callbacks", () =>
            {
                var pending = new NpcGoalPendingClarification(Binding(), "Wait.");
                Check.True(pending.TryBeginProposal(Binding()));
                Check.True(pending.TryBeginProposal(Binding()));
                Check.True(pending.TryBeginProposal(Binding()));
                Check.True(!pending.TryBeginProposal(Binding()));
                Check.True(pending.TryAppend(Binding(2), "Three minutes."));
                Check.Equal(0, pending.ProposalCount);
                Check.True(!pending.IsCurrent(Binding()));
                Check.True(!pending.TryBeginProposal(Binding()));
                Check.True(pending.TryBeginProposal(Binding(2)));
                pending.Cancel();
                Check.True(!pending.IsCurrent(Binding(2)));
                Check.True(!pending.TryBeginProposal(Binding(2)));
                Check.True(!pending.TryAppend(Binding(3), "Four minutes."));
            }));
            cases.Add(("Pending plan rejects duplicate or older turns without losing current data", () =>
            {
                var pending = new NpcGoalPendingClarification(Binding(3), "Wait.");
                Check.True(!pending.TryAppend(Binding(3), "Duplicate."));
                Check.True(!pending.TryAppend(Binding(2), "Old."));
                Check.True(pending.IsActive);
                Check.Equal(1, pending.Turns.Count);
            }));
            cases.Add(("Pending plan bounds exact data without silently truncating", () =>
            {
                var pending = new NpcGoalPendingClarification(Binding(), "Wait.");
                Check.True(!pending.TryAppend(Binding(2), new string('x', NpcGoalChat.MaximumTurnLength + 1)));
                Check.True(!pending.TryAppend(Binding(2), " "));
                for (int i = 2; i <= NpcGoalPendingClarification.MaximumTurns; i++)
                    Check.True(pending.TryAppend(Binding(i), "Exact " + i));
                Check.True(!pending.TryAppend(Binding(100), "Overflow"));
                Check.Equal(NpcGoalPendingClarification.MaximumTurns, pending.Turns.Count);
            }));
        }
    }
}
