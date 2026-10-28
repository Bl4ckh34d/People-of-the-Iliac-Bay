using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class ConversationMoodTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(Test("Action mood preserves action provenance and shares the accepted sequence with social replies", () =>
            {
                var store = new WorldStateStore(42);
                long sequence;
                Check.True(store.TryCommitAcceptedActionExchange("alice", 0, -3, 10, "day", "shop",
                    "Would you help?", "I cannot take that on.", out sequence));
                Check.Equal(1L, sequence);
                Check.Equal(-3, store.CaptureConversationMood("alice").Value);
                var transcript = store.ConversationTranscript("alice", 10);
                Check.Equal(1, transcript.Count);
                Check.Equal("action", transcript[0].Source);
                Check.True(!store.TryCommitAcceptedSocialExchange("alice", 0, 5, 11, "day", "shop",
                    "stale", "stale reply", out sequence));
                Check.True(!store.TryCommitAcceptedActionExchange("alice", 1, 11, 11, "day", "shop",
                    "invalid", "invalid reply", out sequence));
                Check.Equal(1, store.ConversationTranscript("alice", 11).Count);
                Check.True(store.TryCommitAcceptedSocialExchange("alice", 1, 2, 12, "day", "shop",
                    "I understand.", "Thank you.", out sequence));
                Check.Equal(2L, sequence);
                Check.Equal(-1, store.CaptureConversationMood("alice").Value);
                Check.Equal(2, store.Snapshot().ConversationAttitudes.Single(x => x.PersonId == "alice").SharedExchangeCount);
                var saved = store.Snapshot();
                store.Restore(saved);
                transcript = store.ConversationTranscript("alice", 12);
                Check.Equal("action", transcript[0].Source);
                Check.Equal("model", transcript[1].Source);
                Check.Equal(2L, store.CaptureConversationMood("alice").AcceptedTurnSequence);
            }));
            cases.Add(Test("Conversation mood defaults to neutral without inserting saved state", () =>
            {
                var store = new WorldStateStore(42);
                var mood = store.CaptureConversationMood("alice");
                Check.Equal(0, mood.Value);
                Check.Equal(0L, mood.AcceptedTurnSequence);
                Check.Equal(0, store.Snapshot().ConversationMoods.Count);
                long committed;
                Check.True(store.TryCommitAcceptedSocialExchange("bob", 0, 0, 10, "day", "inn",
                    "neutral exchange", "neutral reply", out committed));
                Check.Equal(0, store.CaptureConversationMood("bob").Value);
                Check.Equal(1L, store.CaptureConversationMood("bob").AcceptedTurnSequence);
            }));
            cases.Add(Test("Conversation mood commits once per accepted sequence and clamps without changing disposition", () =>
            {
                var store = new WorldStateStore(42);
                store.SetConversationThreatDisposition("alice", 10, 1);
                int disposition = store.ConversationDisposition("alice");
                long committed;
                Check.True(store.TryCommitAcceptedSocialExchange("alice", 0, 10, 10, "day", "inn",
                    "same words", "same reply", out committed));
                Check.Equal(1L, committed);
                Check.Equal(10, store.CaptureConversationMood("alice").Value);
                Check.Equal(disposition, store.ConversationDisposition("alice"));
                var before = store.Snapshot();
                Check.True(!store.TryCommitAcceptedSocialExchange("alice", 0, -10, 11, "day", "inn",
                    "same words", "same reply", out committed));
                Check.Equal(0L, committed);
                Check.Equal(10, store.CaptureConversationMood("alice").Value);
                Check.Equal(1L, store.CaptureConversationMood("alice").AcceptedTurnSequence);
                Check.Equal(before.ConversationMemories.Count, store.Snapshot().ConversationMemories.Count);
                Check.True(!store.TryCommitAcceptedSocialExchange("alice", 3, 10, 11, "day", "inn",
                    "same words", "same reply", out committed));
                Check.True(store.TryCommitAcceptedSocialExchange("alice", 1, 10, 12, "day", "inn",
                    "same words", "same reply", out committed));
                Check.Equal(2L, committed);
                Check.Equal(20, store.CaptureConversationMood("alice").Value);
                Check.Equal(2, store.ConversationTranscript("alice", 12).Count);
                Check.Equal(2, store.Snapshot().ConversationAttitudes.Single(item => item.PersonId == "alice").SharedExchangeCount);
                Check.True(store.TryCommitAcceptedSocialExchange("alice", 2, 10, 13, "day", "inn",
                    "third", "reply", out committed));
                Check.Equal(30, store.CaptureConversationMood("alice").Value);
                Check.True(store.TryCommitAcceptedSocialExchange("alice", 3, 10, 14, "day", "inn",
                    "fourth", "reply", out committed));
                Check.Equal(40, store.CaptureConversationMood("alice").Value);
                Check.True(store.TryCommitAcceptedSocialExchange("alice", 4, 10, 15, "day", "inn",
                    "fifth", "reply", out committed));
                Check.Equal(50, store.CaptureConversationMood("alice").Value);
                for (int i = 0; i < 5; i++)
                {
                    Check.True(store.TryCommitAcceptedSocialExchange("alice", 5 + i, 10, 16 + i, "day", "inn",
                        "positive", "reply", out committed));
                }
                Check.Equal(100, store.CaptureConversationMood("alice").Value);
                Check.True(store.TryCommitAcceptedSocialExchange("alice", 10, -10, 21, "day", "inn",
                    "negative", "reply", out committed));
                Check.Equal(90, store.CaptureConversationMood("alice").Value);
                var transcript = store.ConversationTranscript("alice", 21);
                Check.Equal("model", transcript[0].Source);
            }));
            cases.Add(Test("Conversation mood snapshots and views stay detached", () =>
            {
                var store = new WorldStateStore(42);
                long committed;
                Check.True(store.TryCommitAcceptedSocialExchange("alice", 0, -4, 10, "day", "inn",
                    "player", "npc", out committed));
                WorldState snapshot = store.Snapshot();
                snapshot.ConversationMoods[0].Value = 88;
                snapshot.ConversationMoods[0].AcceptedTurnSequence = 99;
                Check.Equal(-4, store.CaptureConversationMood("alice").Value);
                Check.Equal(1L, store.CaptureConversationMood("alice").AcceptedTurnSequence);
                Check.True(!typeof(ConversationMoodView).GetProperty("Value").CanWrite);
                Check.True(!typeof(ConversationMoodView).GetProperty("AcceptedTurnSequence").CanWrite);
                store.Restore(snapshot);
                Check.Equal(88, store.CaptureConversationMood("alice").Value);
                Check.Equal(99L, store.CaptureConversationMood("alice").AcceptedTurnSequence);
            }));
            cases.Add(Test("Invalid social exchanges leave mood, sequence, memory and shared count unchanged", () =>
            {
                var store = new WorldStateStore(42);
                long committed;
                Check.True(store.TryCommitAcceptedSocialExchange("alice", 0, 2, 10, "day", "inn",
                    "player", "npc", out committed));
                Check.True(store.TryCommitAcceptedSocialExchange("alice", 1, 1, 11, "day", "inn",
                    new string('x', 601), new string('y', 1601), out committed));
                var bounded = store.ConversationTranscript("alice", 11).Last();
                Check.Equal(600, bounded.PlayerText.Length);
                Check.Equal(1600, bounded.NpcText.Length);
                AssertRejected(store, 2, 12, "day", "inn", "", "npc", 1);
                AssertRejected(store, 2, 12, "day", "inn", "player", null, 1);
                AssertRejected(store, -1, 11, "day", "inn", "player", "npc", 1);
                AssertRejected(store, 2, double.NaN, "day", "inn", "player", "npc", 1);
                AssertRejected(store, 2, 12, "day", "inn", "player", "npc", 11);
                AssertRejected(store, 2, 12, "day", "inn", "player", "npc", -11);
                AssertRejected(store, 2, 12, "day", "inn", "player", "npc", 1, new string('p', 241));
                AssertRejected(store, 2, 12, "day", "inn", "player", "npc", 1, "alice", -1);
                Check.Equal(3, store.CaptureConversationMood("alice").Value);
                Check.Equal(2L, store.CaptureConversationMood("alice").AcceptedTurnSequence);
                Check.Equal(2, store.ConversationTranscript("alice", 12).Count);
                Check.Equal(2, store.Snapshot().ConversationAttitudes.Single(item => item.PersonId == "alice").SharedExchangeCount);
            }));
            cases.Add(Test("Conversation mood follows selected archive ancestry across rollback and sibling branches", () => InDirectory(root =>
            {
                var origin = new WorldStateStore(42, root);
                long committed;
                Check.True(origin.TryCommitAcceptedSocialExchange("alice", 0, 3, 10, "day", "inn",
                    "ancestor", "reply", out committed));
                WorldState ancestor = origin.SnapshotForSave();
                Check.True(origin.TryCommitAcceptedSocialExchange("alice", 1, 4, 11, "day", "inn",
                    "future", "reply", out committed));
                WorldState future = origin.SnapshotForSave();

                var branchA = new WorldStateStore(1, root);
                branchA.Restore(ancestor);
                Check.Equal(3, branchA.CaptureConversationMood("alice").Value);
                Check.Equal(1L, branchA.CaptureConversationMood("alice").AcceptedTurnSequence);
                Check.True(branchA.TryCommitAcceptedSocialExchange("alice", 1, -2, 12, "day", "inn",
                    "branch A", "reply", out committed));
                WorldState savedA = branchA.SnapshotForSave();

                var branchB = new WorldStateStore(2, root);
                branchB.Restore(future);
                Check.Equal(7, branchB.CaptureConversationMood("alice").Value);
                Check.Equal(2L, branchB.CaptureConversationMood("alice").AcceptedTurnSequence);
                Check.True(branchB.TryCommitAcceptedSocialExchange("alice", 2, 6, 13, "day", "inn",
                    "branch B", "reply", out committed));
                WorldState savedB = branchB.SnapshotForSave();

                var readA = new WorldStateStore(3, root); readA.Restore(savedA);
                var readB = new WorldStateStore(4, root); readB.Restore(savedB);
                Check.Equal(1, readA.CaptureConversationMood("alice").Value);
                Check.Equal(2L, readA.CaptureConversationMood("alice").AcceptedTurnSequence);
                Check.Equal(13, readB.CaptureConversationMood("alice").Value);
                Check.Equal(3L, readB.CaptureConversationMood("alice").AcceptedTurnSequence);
                Check.Equal(2, readA.ConversationTranscript("alice", 20).Count);
                Check.Equal(3, readB.ConversationTranscript("alice", 20).Count);
                Check.True(readA.ConversationTranscript("alice", 20).All(entry => !entry.PlayerText.Contains("branch B")));
                Check.True(readB.ConversationTranscript("alice", 20).Any(entry => entry.PlayerText == "future"));
            })));
        }

        private static (string Name, Action Body) Test(string name, Action body)
        { return (name, body); }

        private static void AssertRejected(WorldStateStore store, long expectedSequence, double at, string date,
            string place, string playerText, string npcText, int moodDelta, string personId = "alice", long? sequence = null)
        {
            long committed;
            Check.True(!store.TryCommitAcceptedSocialExchange(personId, sequence ?? expectedSequence, moodDelta, at,
                date, place, playerText, npcText, out committed));
            Check.Equal(0L, committed);
        }

        private static void InDirectory(Action<string> action)
        {
            string root = Path.Combine(Path.GetTempPath(), "conversation-mood-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try { action(root); }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
    }
}
