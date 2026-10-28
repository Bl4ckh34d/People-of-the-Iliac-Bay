using System;
using System.Collections.Generic;
using System.Linq;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class TimedConversationMoodTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(Test("Timed mood reads cool deterministically without mutating stored state", () =>
            {
                var store = new WorldStateStore(1234);
                long accepted;
                Check.True(store.TryCommitAcceptedSocialExchange("alice", 0, 0, 10, 100,
                    "day", "inn", "hello one", "hello", out accepted));
                Check.True(store.TryCommitAcceptedSocialExchange("alice", 1, 1, 10, 100,
                    "day", "inn", "hello", "hello", out accepted));
                var before = store.Snapshot();
                double halfLife = MoodRecovery.HalfLifeSeconds(1234, "alice");
                double queryTime = 100 + halfLife * 2;
                int expected = MoodRecovery.ValueAt(20, 100, queryTime, halfLife);

                var first = store.CaptureConversationMood("alice", queryTime);
                var second = store.CaptureConversationMood("alice", queryTime);
                Check.Equal(expected, first.Value);
                Check.Equal(first.Value, second.Value);
                Check.True(!typeof(ConversationMoodView).GetProperty("Revision").CanWrite);
                Check.True(!typeof(ConversationMoodView).GetProperty("HasTimeAnchor").CanWrite);
                Check.True(!typeof(ConversationMoodView).GetProperty("UpdatedAt").CanWrite);
                Check.Equal(20, store.CaptureConversationMood("alice").Value);
                Check.Equal(100d, first.UpdatedAt);
                Check.Equal(2L, first.Revision);
                Check.Equal(before.ConversationMoods[0].Value, store.Snapshot().ConversationMoods[0].Value);
                Check.Equal(before.ConversationMoods[0].UpdatedAt, store.Snapshot().ConversationMoods[0].UpdatedAt);

                var absent = store.CaptureConversationMood("nobody", queryTime);
                Check.Equal(0, absent.Value);
                Check.Equal(0, store.Snapshot().ConversationMoods.Count(item => item.PersonId == "nobody"));
                Check.Equal(20, store.CaptureConversationMood("alice", 99).Value);
                Check.Equal(100d, store.CaptureConversationMood("alice").UpdatedAt);
            }));

            cases.Add(Test("Accepted mood exchange folds decay and advances revision atomically", () =>
            {
                var store = new WorldStateStore(74);
                long accepted;
                Check.True(store.TryCommitAcceptedSocialExchange("alice", 0, 0, 10, 100,
                    "day", "inn", "first", "reply", out accepted));
                Check.True(store.TryCommitAcceptedSocialExchange("alice", 1, 1, 10, 100,
                    "day", "inn", "second", "reply", out accepted));
                Check.True(store.TryCommitAcceptedSocialExchange("alice", 2, 2, 10, 100,
                    "day", "inn", "third", "reply", out accepted));
                var first = store.CaptureConversationMood("alice");
                double halfLife = MoodRecovery.HalfLifeSeconds(74, "alice");
                double at = 100 + halfLife;
                int decayed = MoodRecovery.ValueAt(30, 100, at, halfLife);

                Check.True(store.TryCommitAcceptedActionExchange("alice", 3, first.Revision, -2, at,
                    "day", "inn", "fourth", "reply", out accepted));
                Check.Equal(4L, accepted);
                var folded = store.CaptureConversationMood("alice");
                Check.Equal(Math.Max(-100, Math.Min(100, decayed - 2)), folded.Value);
                Check.Equal(4L, folded.Revision);
                Check.Equal(at, folded.UpdatedAt);
                Check.True(folded.HasTimeAnchor);

                int transcriptCount = store.ConversationTranscript("alice", at).Count;
                int sharedCount = store.Snapshot().ConversationAttitudes.Single(item => item.PersonId == "alice").SharedExchangeCount;
                Check.True(!store.TryCommitAcceptedSocialExchange("alice", 4, first.Revision, 5, at + 1,
                    "day", "inn", "stale revision", "reply", out accepted));
                Check.Equal(0L, accepted);
                Check.Equal(4L, store.CaptureConversationMood("alice").AcceptedTurnSequence);
                Check.Equal(4L, store.CaptureConversationMood("alice").Revision);
                Check.Equal(transcriptCount, store.ConversationTranscript("alice", at + 1).Count);
                Check.Equal(sharedCount, store.Snapshot().ConversationAttitudes.Single(item => item.PersonId == "alice").SharedExchangeCount);
            }));

            cases.Add(Test("Timed reads preserve save anchor and independent branches", () =>
            {
                var source = new WorldStateStore(912);
                long accepted;
                Check.True(source.TryCommitAcceptedSocialExchange("alice", 0, 0, 10, 40,
                    "day", "inn", "first", "reply", out accepted));
                Check.True(source.TryCommitAcceptedSocialExchange("alice", 1, 1, 10, 40,
                    "day", "inn", "second", "reply", out accepted));
                Check.True(source.TryCommitAcceptedSocialExchange("alice", 2, 2, 4, 40,
                    "day", "inn", "third", "reply", out accepted));
                WorldState checkpoint = source.Snapshot();
                double halfLife = MoodRecovery.HalfLifeSeconds(912, "alice");
                source.CaptureConversationMood("alice", 40 + halfLife * 4);
                WorldState afterRead = source.Snapshot();
                Check.Equal(checkpoint.ConversationMoods[0].UpdatedAt, afterRead.ConversationMoods[0].UpdatedAt);
                Check.Equal(checkpoint.ConversationMoods[0].Value, afterRead.ConversationMoods[0].Value);

                var branchA = new WorldStateStore(1);
                var branchB = new WorldStateStore(2);
                branchA.Restore(checkpoint);
                branchB.Restore(checkpoint);
                var basis = branchA.CaptureConversationMood("alice", 40 + halfLife * 2).Value;
                Check.Equal(basis, branchB.CaptureConversationMood("alice", 40 + halfLife * 2).Value);
                var revisionA = branchA.CaptureConversationMood("alice").Revision;
                var revisionB = branchB.CaptureConversationMood("alice").Revision;
                Check.True(branchA.TryCommitAcceptedSocialExchange("alice", 3, revisionA, 1, 40 + halfLife * 2,
                    "day", "inn", "branch A", "reply", out accepted));
                Check.True(branchB.TryCommitAcceptedSocialExchange("alice", 3, revisionB, -1, 40 + halfLife * 2,
                    "day", "inn", "branch B", "reply", out accepted));
                Check.Equal(basis + 1, branchA.CaptureConversationMood("alice").Value);
                Check.Equal(basis - 1, branchB.CaptureConversationMood("alice").Value);
                Check.Equal(24, source.CaptureConversationMood("alice").Value);
            }));

            cases.Add(Test("Mood stays separate from disposition and shared-exchange accounting", () =>
            {
                var store = new WorldStateStore(42);
                store.SetConversationThreatDisposition("alice", 50, 100);
                int disposition = store.ConversationDisposition("alice");
                long accepted;
                Check.True(store.TryCommitAcceptedSocialExchange("alice", 0, 0, 8, 100,
                    "day", "inn", "hello", "hello", out accepted));
                Check.Equal(8, store.CaptureConversationMood("alice").Value);
                Check.Equal(disposition, store.ConversationDisposition("alice"));
                Check.Equal(1, store.Snapshot().ConversationAttitudes.Single(item => item.PersonId == "alice").SharedExchangeCount);
                Check.Equal(1L, store.CaptureConversationMood("alice").AcceptedTurnSequence);
                Check.Equal(1L, store.CaptureConversationMood("alice").Revision);
            }));

            cases.Add(Test("Profile-based mood recovery is stable, bounded, and personality dependent", () =>
            {
                const string personId = "personality-recovery";
                const uint seed = 7781;
                var baseProfile = NpcPersonalityGenerator.GenerateLegacyV2(personId, 991, NpcSocialBackground.Commoner);
                var resilient = NpcPersonalityProfile.CopyValidated(baseProfile);
                resilient.EmotionalStability = "resilient";
                resilient.Sensitivity = "thick-skinned";
                resilient.SelfEsteem = "confident";
                resilient.Humor = "playful";
                var sensitive = NpcPersonalityProfile.CopyValidated(baseProfile);
                sensitive.EmotionalStability = "sensitive";
                sensitive.Sensitivity = "highly sensitive";
                sensitive.SelfEsteem = "insecure";
                sensitive.Humor = "solemn";

                var overload = typeof(MoodRecovery).GetMethod("HalfLifeSeconds",
                    new[] { typeof(uint), typeof(NpcPersonalityProfile) });
                Check.True(overload != null);
                double resilientHalfLife = (double)overload.Invoke(null, new object[] { seed, resilient });
                double sensitiveHalfLife = (double)overload.Invoke(null, new object[] { seed, sensitive });
                double legacyHalfLife = MoodRecovery.HalfLifeSeconds(seed, personId);
                Check.True(sensitiveHalfLife > resilientHalfLife);
                Check.True(resilientHalfLife >= legacyHalfLife * 0.70);
                Check.True(resilientHalfLife <= legacyHalfLife * 1.40);
                Check.True(sensitiveHalfLife >= legacyHalfLife * 0.70);
                Check.True(sensitiveHalfLife <= legacyHalfLife * 1.40);
                Check.Equal(resilientHalfLife,
                    (double)overload.Invoke(null, new object[] { seed, resilient }));
                Check.Equal(0, MoodRecovery.ValueAt(0, 50, 50000, sensitiveHalfLife));
                Check.True(MoodRecovery.ValueAt(60, 100, 100 + resilientHalfLife, resilientHalfLife) < 60);
                Check.True(MoodRecovery.ValueAt(60, 100, 100 + resilientHalfLife, resilientHalfLife) > 0);
                Check.True(MoodRecovery.ValueAt(60, 100, 100 + legacyHalfLife, sensitiveHalfLife) >
                    MoodRecovery.ValueAt(60, 100, 100 + legacyHalfLife, resilientHalfLife));
                Check.Equal(legacyHalfLife, MoodRecovery.HalfLifeSeconds(seed, personId));
            }));

            cases.Add(Test("Mood rejects event clock rollback and unsupported future schema", () =>
            {
                var store = new WorldStateStore(42);
                long accepted;
                Check.True(store.TryCommitAcceptedSocialExchange("alice", 0, 0, 4, 200,
                    "day", "inn", "first", "reply", out accepted));
                var before = store.Snapshot();
                Check.True(!store.TryCommitAcceptedSocialExchange("alice", 1, 1, 2, 199,
                    "day", "inn", "old time", "reply", out accepted));
                Check.Equal(0L, accepted);
                Check.Equal(before.ConversationMoods[0].Value, store.CaptureConversationMood("alice").Value);
                Check.Equal(before.ConversationMoods[0].UpdatedAt, store.CaptureConversationMood("alice").UpdatedAt);
                Check.Equal(1, store.ConversationTranscript("alice", 200).Count);

                WorldState future = store.Snapshot();
                future.Version = new WorldState().Version + 1;
                Check.Throws<ArgumentException>(() => new WorldStateStore(1).Restore(future));
            }));
        }

        private static (string Name, Action Body) Test(string name, Action body)
        { return (name, body); }
    }
}
