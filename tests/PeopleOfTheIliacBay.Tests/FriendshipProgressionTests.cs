using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class FriendshipProgressionTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Positive conversation credit grows through continuous 24 30 and 36 hour stages", new Action(() => {
                var store = new WorldStateStore(1);
                double interval0 = ExpectedIntervalSeconds("npc", 0);
                double interval1 = ExpectedIntervalSeconds("npc", 1);
                double interval2 = ExpectedIntervalSeconds("npc", 2);
                double time12 = interval0 / 2 + 1;
                double time24 = interval0 + 1;
                double time39 = time24 + interval1 / 2 + 1;
                double time57 = time39 + interval1 * 0.6 + 1;
                double time75 = time57 + interval2 / 2 + 1;
                store.RecordConversationEncounter("npc", 0);
                for (int i = 0; i < 5; i++)
                    Check.Equal(1, store.ApplyConversationTone("npc", "polite", "Initial " + i, i));
                Check.Equal(5, store.ConversationDisposition("npc"));
                Check.Equal(0, store.ApplyConversationTone("npc", "polite", "Same encounter", time12));

                store.RecordConversationEncounter("npc", time12);
                Check.Equal(1, store.ApplyConversationTone("npc", "polite", "After twelve one", time12));
                Check.Equal(1, store.ApplyConversationTone("npc", "polite", "After twelve two", time12));
                Check.Equal(0, store.ApplyConversationTone("npc", "polite", "Fraction remains", time12));
                Check.Equal(7, store.ConversationDisposition("npc"));

                store.RecordConversationEncounter("npc", time24);
                for (int i = 0; i < 3; i++)
                    Check.Equal(1, store.ApplyConversationTone("npc", "polite", "Stage two " + i, time24));
                Check.Equal(10, store.ConversationDisposition("npc"));
                Check.Equal(0, store.ApplyConversationTone("npc", "polite", "Stage two empty", time24));

                store.RecordConversationEncounter("npc", time39);
                Check.Equal(1, store.ApplyConversationTone("npc", "polite", "Stage three one", time39));
                Check.Equal(1, store.ApplyConversationTone("npc", "polite", "Stage three two", time39));
                Check.Equal(0, store.ApplyConversationTone("npc", "polite", "Stage three fraction", time39));
                Check.Equal(12, store.ConversationDisposition("npc"));

                store.RecordConversationEncounter("npc", time57);
                for (int i = 0; i < 3; i++)
                    Check.Equal(1, store.ApplyConversationTone("npc", "polite", "Stage four " + i, time57));
                Check.Equal(15, store.ConversationDisposition("npc"));

                store.RecordConversationEncounter("npc", time75);
                Check.Equal(1, store.ApplyConversationTone("npc", "polite", "Stage four one", time75));
                Check.Equal(1, store.ApplyConversationTone("npc", "polite", "Stage four two", time75));
                Check.Equal(1, store.ApplyConversationTone("npc", "polite", "Stage four three", time75));
                Check.Equal(0, store.ApplyConversationTone("npc", "polite", "Stage four empty", time75));
                Check.Equal(18, store.ConversationDisposition("npc"));
            })));

            cases.Add(("Friendship accrual rate varies by stable person identity", new Action(() => {
                Check.Equal(4, EarnAfterElapsedDay("npc"));
                Check.Equal(5, EarnAfterElapsedDay("friendship-slow"));
            })));

            cases.Add(("Friendship intervals use stable bounded identity factors across stages and restore", new Action(() => {
                double npc0 = ConversationEffects.FriendshipIntervalSeconds("npc", 0);
                double npc1 = ConversationEffects.FriendshipIntervalSeconds("npc", 1);
                double npc2 = ConversationEffects.FriendshipIntervalSeconds("npc", 2);
                double npc3 = ConversationEffects.FriendshipIntervalSeconds("npc", 3);
                double npcMillion = ConversationEffects.FriendshipIntervalSeconds("npc", 1000000);
                Check.Near(96111.36, npc0, 0.000001);
                Check.Near(120139.2, npc1, 0.000001);
                Check.Near(129600.0, npc2, 0.000001);
                Check.True(npc0 < npc1 && npc1 < npc2);
                Check.Near(npc0 / 86400d, npc1 / 108000d, 0.0000000001);
                Check.Equal(npc2, npc3);
                Check.Equal(npc2, npcMillion);
                Check.True(npc2 <= 129600.0);
                Check.Near(129600.0, ConversationEffects.FriendshipIntervalSeconds("npc", 1000000), 0.000001);

                var store = new WorldStateStore(1);
                store.RecordConversationEncounter("npc", 0);
                store.ApplyConversationTone("npc", "polite", "Persist identity", 0);
                var restored = new WorldStateStore(2);
                restored.Restore(store.Snapshot());
                string restoredPersonId = restored.Snapshot().ConversationAttitudes[0].PersonId;
                Check.Equal("npc", restoredPersonId);
                Check.Equal(npc0, ConversationEffects.FriendshipIntervalSeconds(restoredPersonId, 0));
                Check.Equal(npc0, ConversationEffects.FriendshipIntervalSeconds("npc", 0));
                Check.True(ConversationEffects.FriendshipIntervalSeconds("friendship-slow", 0) !=
                    ConversationEffects.FriendshipIntervalSeconds("friendship-fast", 0));
                Check.Near(73440d, ConversationEffects.FriendshipIntervalSeconds("range-5775", 0), 0.000001);
                Check.Near(99360d, ConversationEffects.FriendshipIntervalSeconds("range-1044", 0), 0.000001);

                string[] ids = { "a", "NPC", "npc", "npc-é", "npc-漢字", new string('x', 240) };
                foreach (string id in ids)
                {
                    double factor = ConversationEffects.FriendshipIntervalSeconds(id, 0) / 86400d;
                    Check.True(factor >= 0.85 && factor <= 1.15);
                    double stage2 = ConversationEffects.FriendshipIntervalSeconds(id, 2);
                    double lateStage = ConversationEffects.FriendshipIntervalSeconds(id, 1000000);
                    Check.True(stage2 <= 129600d);
                    Check.Equal(stage2, lateStage);
                }
            })));

            cases.Add(("Friendship interval rejects invalid person identities and stages", new Action(() => {
                Check.Throws<ArgumentException>(() => ConversationEffects.FriendshipIntervalSeconds(null, 0));
                Check.Throws<ArgumentException>(() => ConversationEffects.FriendshipIntervalSeconds("", 0));
                Check.Throws<ArgumentException>(() => ConversationEffects.FriendshipIntervalSeconds("   ", 0));
                Check.Throws<ArgumentException>(() => ConversationEffects.FriendshipIntervalSeconds(new string('x', 241), 0));
                Check.Throws<ArgumentException>(() => ConversationEffects.FriendshipIntervalSeconds("npc", -1));
                Check.Throws<ArgumentException>(() => ConversationEffects.FriendshipIntervalSeconds("npc", 1000001));
                Check.Equal(129600d, ConversationEffects.FriendshipIntervalSeconds("npc", 1000000));
            })));

            cases.Add(("Friendship credit and encounter cap survive save restore and chat reopen", new Action(() => {
                var store = new WorldStateStore(1);
                store.RecordConversationEncounter("npc", 0);
                for (int i = 0; i < 5; i++)
                    store.ApplyConversationTone("npc", "polite", "Opening " + i, i);

                var restored = new WorldStateStore(2);
                restored.Restore(store.Snapshot());
                Check.Equal(0, restored.ApplyConversationTone("npc", "polite", "Still same chat", 10 * 3600));

                restored.RecordConversationEncounter("npc", 12 * 3600);
                Check.Equal(1, restored.ApplyConversationTone("npc", "polite", "Earn one", 12 * 3600));
                restored.RecordConversationEncounter("npc", 12 * 3600);
                Check.Equal(1, restored.ApplyConversationTone("npc", "polite", "Earn remaining point", 12 * 3600));
                var saved = new WorldStateStore(3);
                saved.Restore(restored.Snapshot());
                saved.RecordConversationEncounter("npc", 12 * 3600);
                Check.Equal(0, saved.ApplyConversationTone("npc", "polite", "Fraction is not a point", 12 * 3600));
                Check.Equal(7, saved.ConversationDisposition("npc"));
            })));

            cases.Add(("Elapsed time alone does not change friendship and positive growth stops at maximum", new Action(() => {
                var store = new WorldStateStore(1);
                store.RecordConversationEncounter("npc", 0);
                store.ApplyConversationTone("npc", "polite", "One", 0);
                Check.Equal(1, store.ConversationDisposition("npc"));
                Check.Equal(1, store.ConversationDisposition("npc"));

                var mature = new WorldState { ConversationAttitudes = new List<ConversationAttitude> {
                    new ConversationAttitude { PersonId = "mature", Offset = 99 }
                } };
                store.Restore(mature);
                store.RecordConversationEncounter("mature", 0);
                Check.Equal(1, store.ApplyConversationTone("mature", "polite", "At maximum", 0));
                Check.Equal(100, store.ConversationDisposition("mature"));
                Check.Equal(0, store.ApplyConversationTone("mature", "polite", "Beyond maximum", 1));
            })));

            cases.Add(("Negative interactions preserve slower friendship accrual stage", new Action(() => {
                var store = new WorldStateStore(1);
                double interval0 = ExpectedIntervalSeconds("npc", 0);
                double interval1 = ExpectedIntervalSeconds("npc", 1);
                double time24 = interval0 + 1;
                double time39 = time24 + interval1 / 2 + 1;
                store.RecordConversationEncounter("npc", 0);
                for (int i = 0; i < 5; i++)
                    store.ApplyConversationTone("npc", "polite", "First allowance " + i, i);
                store.RecordConversationEncounter("npc", time24);
                for (int i = 0; i < 5; i++)
                    store.ApplyConversationTone("npc", "polite", "Second allowance " + i, time24);
                Check.Equal(-10, store.ApplyConversationTone("npc", "insult", "First insult", time24));
                Check.Equal(-25, store.ApplyConversationTone("npc", "insult", "Second insult", time24));

                var snapshot = store.Snapshot();
                var savedAttitude = snapshot.ConversationAttitudes[0];
                Check.Equal(1, savedAttitude.PositiveProgressionStage);
                Check.Equal(interval1, ConversationEffects.FriendshipIntervalSeconds(
                    savedAttitude.PersonId, savedAttitude.PositiveProgressionStage));
                var restored = new WorldStateStore(2);
                restored.Restore(snapshot);
                Check.Equal(interval1, ConversationEffects.FriendshipIntervalSeconds(
                    restored.Snapshot().ConversationAttitudes[0].PersonId, 1));
                restored.RecordConversationEncounter("npc", time39);
                Check.Equal(1, restored.ApplyConversationTone("npc", "polite", "After insult one", time39));
                Check.Equal(1, restored.ApplyConversationTone("npc", "polite", "After insult two", time39));
                Check.Equal(0, restored.ApplyConversationTone("npc", "polite", "No faster recovery", time39));
            })));

            cases.Add(("Legacy five point friendship migrates without a reload bonus", new Action(() => {
                var legacy = new WorldState { Version = 30, ConversationAttitudes = new List<ConversationAttitude> {
                    new ConversationAttitude { PersonId = "legacy", Offset = 5, Day = 0, LastEncounterAt = 100 }
                } };
                var store = new WorldStateStore(1);
                store.Restore(legacy);
                Check.Equal(0, store.ApplyConversationTone("legacy", "polite", "No legacy bonus", 100));
                double halfInterval = ExpectedIntervalSeconds("legacy", 0) / 2;
                double halfIntervalAt = 100 + halfInterval;
                store.RecordConversationEncounter("legacy", halfIntervalAt);
                Check.Equal(1, store.ApplyConversationTone("legacy", "polite", "Half day one", halfIntervalAt));
                Check.Equal(1, store.ApplyConversationTone("legacy", "polite", "Half day two", halfIntervalAt));
                Check.Equal(0, store.ApplyConversationTone("legacy", "polite", "Half day fraction", halfIntervalAt));
                Check.Equal(7, store.ConversationDisposition("legacy"));
            })));

            cases.Add(("Friendship save rejects invalid credit and stage fields", new Action(() => {
                var store = new WorldStateStore(1);
                store.RecordConversationEncounter("npc", 0);
                store.ApplyConversationTone("npc", "polite", "One point", 0);
                var snapshot = store.Snapshot();
                snapshot.ConversationAttitudes[0].PositiveCredit = double.NaN;
                Check.Throws<ArgumentException>(() => new WorldStateStore(2).Restore(snapshot));
                snapshot = store.Snapshot();
                snapshot.ConversationAttitudes[0].PositiveCredit = 5.1;
                Check.Throws<ArgumentException>(() => new WorldStateStore(2).Restore(snapshot));
                snapshot = store.Snapshot();
                snapshot.ConversationAttitudes[0].PositiveProgressionStage = 1;
                Check.Throws<ArgumentException>(() => new WorldStateStore(2).Restore(snapshot));
            })));
        }

        private static int EarnAfterElapsedDay(string personId)
        {
            var store = new WorldStateStore(1);
            store.RecordConversationEncounter(personId, 0);
            for (int i = 0; i < 5; i++)
                store.ApplyConversationTone(personId, "polite", "Opening " + i, i);
            store.RecordConversationEncounter(personId, 86400);
            int earned = 0;
            for (int i = 0; i < 5; i++)
                earned += store.ApplyConversationTone(personId, "polite", "Elapsed day " + i, 86400);
            return earned;
        }

        private static double ExpectedIntervalSeconds(string personId, int stage)
        {
            uint hash = 2166136261u;
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes("friendship-accrual-v1:" + personId);
            unchecked
            {
                for (int i = 0; i < bytes.Length; i++)
                {
                    hash ^= bytes[i];
                    hash *= 16777619u;
                }
            }
            double factor = (8500 + hash % 3001) / 10000d;
            return Math.Min(129600d, (86400d + 21600d * stage) * factor);
        }
    }
}
