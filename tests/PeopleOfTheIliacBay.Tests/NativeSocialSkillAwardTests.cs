using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NativeSocialSkillAwardTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Accepted positive social receipt earns at most one contextual skill use per person per day", AcceptedPositive));
            cases.Add(("Unaccepted, neutral, hostile and mismatched receipts earn no social skill use", RejectedEvidence));
            cases.Add(("Native social-group evidence selects the context skill without rewarding hostility", GroupMapping));
        }

        private static void AcceptedPositive()
        {
            string person;
            var store = NpcExperienceCommitTests.Create(out person);
            var ticket = NpcObservedSpeechTestFixture.Issue(store, person, "Thank you for helping.", 10);
            var receipt = ticket.Receipt.WithAppliedAffinity(1);
            long sequence;
            Check.True(store.TryCommitAcceptedSocialExchangeWithObservedSpeech(person, 0, 0, 0, 11,
                "day", "inn", "Thank you for helping.", "You are welcome.", ticket, receipt,
                NpcExperienceAppliedEffectsMask.Mood | NpcExperienceAppliedEffectsMask.Affinity, out sequence));
            NativeSocialSkillKind skill;
            Check.True(store.TryMarkAcceptedSocialSkillUse(person, sequence, receipt.Evidence.ReceiptId,
                0, "polite", 11, out skill));
            Check.Equal(NativeSocialSkillKind.Streetwise, skill);
            Check.True(!store.TryMarkAcceptedSocialSkillUse(person, sequence, receipt.Evidence.ReceiptId,
                0, "polite", 11, out skill));
            var restored = new WorldStateStore(0);
            restored.Restore(store.Snapshot());
            Check.True(!restored.TryMarkAcceptedSocialSkillUse(person, sequence, receipt.Evidence.ReceiptId,
                0, "polite", 11, out skill));
            var next = NpcObservedSpeechTestFixture.Issue(restored, person, "Thank you again.", 86410);
            var nextReceipt = next.Receipt.WithAppliedAffinity(1);
            long nextSequence;
            Check.True(restored.TryCommitAcceptedSocialExchangeWithObservedSpeech(person, sequence, 1, 0, 86411,
                "next day", "inn", "Thank you again.", "You are welcome.", next, nextReceipt,
                NpcExperienceAppliedEffectsMask.Mood | NpcExperienceAppliedEffectsMask.Affinity, out nextSequence));
            Check.True(restored.TryMarkAcceptedSocialSkillUse(person, nextSequence,
                nextReceipt.Evidence.ReceiptId, 0, "neutral", 86411, out skill));
            Check.Equal(NativeSocialSkillKind.Streetwise, skill);
        }

        private static void GroupMapping()
        {
            Check.Equal(NativeSocialSkillKind.Etiquette, NativeSocialSkillAward.SkillFor(3, "polite"));
            Check.Equal(NativeSocialSkillKind.Etiquette, NativeSocialSkillAward.SkillFor(2, "apology"));
            Check.Equal(NativeSocialSkillKind.None, NativeSocialSkillAward.SkillFor(3, "neutral"));
            Check.Equal(NativeSocialSkillKind.Streetwise, NativeSocialSkillAward.SkillFor(0, "polite"));
            Check.Equal(NativeSocialSkillKind.Streetwise, NativeSocialSkillAward.SkillFor(1, "neutral"));
            Check.Equal(NativeSocialSkillKind.Streetwise, NativeSocialSkillAward.SkillFor(4, "neutral"));
            Check.Equal(NativeSocialSkillKind.None, NativeSocialSkillAward.SkillFor(3, "insult"));
            Check.Equal(NativeSocialSkillKind.None, NativeSocialSkillAward.SkillFor(0, "rude"));
            Check.Equal(NativeSocialSkillKind.None, NativeSocialSkillAward.SkillFor(7, "polite"));
        }

        private static void RejectedEvidence()
        {
            string person;
            var store = NpcExperienceCommitTests.Create(out person);
            var ticket = NpcObservedSpeechTestFixture.Issue(store, person, "Hello.", 10);
            var receipt = ticket.Receipt.WithAppliedAffinity(0);
            NativeSocialSkillKind skill;
            Check.True(!store.TryMarkAcceptedSocialSkillUse(person, 1, receipt.Evidence.ReceiptId,
                3, "polite", 11, out skill));
            long sequence;
            Check.True(store.TryCommitAcceptedSocialExchangeWithObservedSpeech(person, 0, 0, 0, 11,
                "day", "court", "Hello.", "Hello.", ticket, receipt,
                NpcExperienceAppliedEffectsMask.Mood | NpcExperienceAppliedEffectsMask.Affinity, out sequence));
            Check.True(!store.TryMarkAcceptedSocialSkillUse(person, sequence, receipt.Evidence.ReceiptId,
                3, "polite", 11, out skill));
            Check.True(!store.TryMarkAcceptedSocialSkillUse(person, sequence, "wrong-receipt",
                3, "polite", 11, out skill));
            Check.True(!store.TryMarkAcceptedSocialSkillUse(person, sequence, receipt.Evidence.ReceiptId,
                3, "insult", 11, out skill));
        }
    }
}
