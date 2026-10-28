using System;
using DaggerfallConnect;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>
    /// Owned-fixture native tally check. Exercises the live PlayerEntity skill-use API
    /// against isolated accepted speech; restores the player's original skill-use array.
    /// No model request, active mod-store mutation, or save write occurs.
    /// </summary>
    internal static class NativeSocialSkillTallyProbe
    {
        internal const string RunFlag = "-peopleoftheiliacbay-social-skill-tally-probe";
        private const string FixtureCharacter = "Hakon Ironhand";
        private const string FixtureSave = "People - Gothway Garden";
        private const NpcExperienceAppliedEffectsMask AppliedMask =
            NpcExperienceAppliedEffectsMask.Mood | NpcExperienceAppliedEffectsMask.Affinity;

        internal static void Run(PeopleOfTheIliacBayMod host, string character, string save)
        {
            var game = GameManager.Instance;
            Require(Array.IndexOf(Environment.GetCommandLineArgs(), RunFlag) >= 0 &&
                character == FixtureCharacter && save == FixtureSave && host && game != null &&
                game.IsReady && game.IsPlayingGame() && game.PlayerEntity != null &&
                game.PlayerEntity.Name == FixtureCharacter &&
                game.SaveLoadManager.FindSaveFolderByNames(character, save) >= 0,
                "Social-skill tally probe requires the exact loaded owned fixture.");
            PlayerEntity player = game.PlayerEntity;
            short[] originalUses = player.SkillUses;
            Require(originalUses != null && originalUses.Length > (int)DFCareer.Skills.Streetwise,
                "Native player skill-use array is unavailable.");
            short[] isolatedUses = (short[])originalUses.Clone();
            isolatedUses[(int)DFCareer.Skills.Etiquette] = 0;
            isolatedUses[(int)DFCareer.Skills.Streetwise] = 0;
            player.SkillUses = isolatedUses;
            try
            {
                VerifyGroup(host, player, 3, "Etiquette", true);
                VerifyGroup(host, player, 2, "Etiquette", true);
                VerifyGroup(host, player, 0, "Streetwise", false);
                VerifyGroup(host, player, 1, "Streetwise", false);
                VerifyGroup(host, player, 4, "Streetwise", false);
                VerifyNoAward(host, player);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SOCIAL_SKILL_TALLY_PROBE_PASS: " +
                    "positiveAccepted=oncePerNpcPerDay; replay=none; sameDay=none; nextDay=once; " +
                    "nativeSerializerReload=guarded; neutral=none; hostile=none; stale=none; " +
                    "groups=Etiquette(2,3),Streetwise(0,1,4); activeSaveWrites=false; modelCalls=false.");
            }
            finally
            {
                player.SkillUses = originalUses;
            }
        }

        private static void VerifyGroup(PeopleOfTheIliacBayMod host, PlayerEntity player,
            int group, string expected, bool formal)
        {
            string person;
            var store = CreateStore(out person);
            string text = "Thank you for your help.";
            var speech = CommitSpeech(store, person, 10, text, "polite", 0, 0,
                out long sequence, out double committedAt);
            int etiquetteBefore = Uses(player, DFCareer.Skills.Etiquette);
            int streetwiseBefore = Uses(player, DFCareer.Skills.Streetwise);
            host.TryAwardNativeSocialSkillUse(store, person, sequence, speech, "polite", group, committedAt);
            Require(Uses(player, DFCareer.Skills.Etiquette) == etiquetteBefore + (formal ? 1 : 0) &&
                Uses(player, DFCareer.Skills.Streetwise) == streetwiseBefore + (formal ? 0 : 1),
                "Positive accepted speech did not tally " + expected + " exactly once for social group " + group + ".");
            host.TryAwardNativeSocialSkillUse(store, person, sequence, speech, "polite", group, committedAt);
            Require(Uses(player, DFCareer.Skills.Etiquette) == etiquetteBefore + (formal ? 1 : 0) &&
                Uses(player, DFCareer.Skills.Streetwise) == streetwiseBefore + (formal ? 0 : 1),
                "Replayed accepted speech tallied a second skill use.");

            var sameDay = CommitSpeech(store, person, 20, "Thank you again.", "polite", sequence,
                sequence, out long secondSequence, out double secondAt);
            host.TryAwardNativeSocialSkillUse(store, person, secondSequence, sameDay, "polite", group, secondAt);
            Require(Uses(player, DFCareer.Skills.Etiquette) == etiquetteBefore + (formal ? 1 : 0) &&
                Uses(player, DFCareer.Skills.Streetwise) == streetwiseBefore + (formal ? 0 : 1),
                "Second same-day accepted speech tallied another skill use.");

            string serialized = SaveLoadManager.Serialize(typeof(WorldState), store.Snapshot());
            var decoded = SaveLoadManager.Deserialize(typeof(WorldState), serialized, true) as WorldState;
            Require(decoded != null, "Native FullSerializer did not decode social-skill award state.");
            var restored = new WorldStateStore(0);
            restored.Restore(decoded);
            host.TryAwardNativeSocialSkillUse(restored, person, secondSequence, sameDay, "polite", group, secondAt);
            Require(Uses(player, DFCareer.Skills.Etiquette) == etiquetteBefore + (formal ? 1 : 0) &&
                Uses(player, DFCareer.Skills.Streetwise) == streetwiseBefore + (formal ? 0 : 1),
                "Reload permitted a duplicate same-day skill use.");

            var nextDay = CommitSpeech(restored, person, 86410, "Thank you on a new day.", "polite",
                secondSequence, secondSequence, out long thirdSequence, out double thirdAt);
            host.TryAwardNativeSocialSkillUse(restored, person, thirdSequence, nextDay, "polite", group, thirdAt);
            Require(Uses(player, DFCareer.Skills.Etiquette) == etiquetteBefore + (formal ? 2 : 0) &&
                Uses(player, DFCareer.Skills.Streetwise) == streetwiseBefore + (formal ? 0 : 2),
                "Verified next-day rapport did not tally exactly one new use.");
        }

        private static void VerifyNoAward(PeopleOfTheIliacBayMod host, PlayerEntity player)
        {
            string person;
            var store = CreateStore(out person);
            int etiquetteBefore = Uses(player, DFCareer.Skills.Etiquette);
            int streetwiseBefore = Uses(player, DFCareer.Skills.Streetwise);
            var neutral = CommitSpeech(store, person, 10, "Hello.", "neutral", 0, 0,
                out long firstSequence, out double firstAt);
            host.TryAwardNativeSocialSkillUse(store, person, firstSequence, neutral, "neutral", 0, firstAt);
            var hostile = CommitSpeech(store, person, 20, "You fool.", "insult", firstSequence,
                firstSequence, out long secondSequence, out double secondAt);
            host.TryAwardNativeSocialSkillUse(store, person, secondSequence, hostile, "insult", 3, secondAt);
            NpcObservedSpeechTicket staleTicket = Issue(store, person, "Thank you.", 30);
            var stale = staleTicket.Receipt.WithAppliedAffinity(1);
            long rejectedSequence;
            Require(!store.TryCommitAcceptedSocialExchangeWithObservedSpeech(person, 99, 99, 0, 31,
                "day", "inn", "Thank you.", "You are welcome.", staleTicket, stale,
                AppliedMask, out rejectedSequence), "Stale speech unexpectedly committed.");
            host.TryAwardNativeSocialSkillUse(store, person, secondSequence + 1, stale, "polite", 0, 31);
            Require(Uses(player, DFCareer.Skills.Etiquette) == etiquetteBefore &&
                Uses(player, DFCareer.Skills.Streetwise) == streetwiseBefore,
                "Neutral, hostile, or stale speech tallied a social skill.");
        }

        private static WorldStateStore CreateStore(out string person)
        {
            var store = new WorldStateStore(109);
            person = store.GetOrCreate(20, new[] { 200 }, 1).Households[0].Residents[0].Id;
            store.GetOrCreateNpcPersonality(person, 109, NpcSocialBackground.Commoner);
            return store;
        }

        private static NpcObservedSpeechReceipt CommitSpeech(WorldStateStore store, string person,
            double observedAt, string text, string tone, long expectedSequence, long expectedRevision,
            out long committedSequence, out double committedAt)
        {
            NpcObservedSpeechTicket ticket = Issue(store, person, text, observedAt);
            int affinity = store.ApplyConversationTone(person, tone, text, observedAt);
            var speech = ticket.Receipt.WithAppliedAffinity(affinity);
            committedAt = observedAt + 1;
            Require(store.TryCommitAcceptedSocialExchangeWithObservedSpeech(person, expectedSequence,
                expectedRevision, 0, committedAt, "day", "inn", text, "Acknowledged.", ticket,
                speech, AppliedMask, out committedSequence), "Controlled social exchange did not commit.");
            return speech;
        }

        private static NpcObservedSpeechTicket Issue(WorldStateStore store, string person,
            string text, double at)
        {
            NpcObservedSpeechSession session;
            NpcObservedSpeechTicket ticket = null;
            Require(store.TryBeginNpcObservedSpeechSession(person,
                "native-social-skill-probe:" + Guid.NewGuid().ToString("N"), out session) &&
                store.TryIssueNpcObservedSpeech(session, 0, text, at, out ticket),
                "Controlled observed-speech ticket could not be issued.");
            return ticket;
        }

        private static int Uses(PlayerEntity player, DFCareer.Skills skill)
        { return player.SkillUses[(int)skill]; }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
