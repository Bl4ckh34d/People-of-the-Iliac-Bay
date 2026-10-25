using System;
using System.Collections.Generic;
using System.Linq;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcAcceptedSpeechBridgeTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("accepted native dialogue commits mood memory and observed speech atomically", AcceptedTurnIsAtomic));
            cases.Add(("stale accepted dialogue leaves mood memory and observed speech unchanged", StaleTurnLeavesNoSpeech));
            cases.Add(("fractional speech time after native commit clock rejects before whole-second source succeeds", NativeClockBoundary));
            cases.Add(("unavailable reply can retain exact heard speech without claiming a mood effect", UnavailableReplySpeechOnly));
        }

        private static void AcceptedTurnIsAtomic()
        {
            string observer;
            var store = NpcExperienceCommitTests.Create(out observer);
            var ticket = NpcObservedSpeechTestFixture.Issue(store, observer, "I will help you.", 10);
            var speech = ticket.Receipt;
            var mask = NpcExperienceAppliedEffectsMask.Mood | NpcExperienceAppliedEffectsMask.Affinity;
            long sequence;
            True(store.TryCommitAcceptedSocialExchangeWithObservedSpeech(observer, 0, 0, 3, 11,
                "day", "inn", "I will help you.", "Thank you.", ticket, speech, mask, out sequence));
            Equal(1L, sequence);
            Equal(3, store.CaptureConversationMood(observer, 11).Value);
            var saved = store.Snapshot();
            Equal(1, saved.NpcExperienceInbox.Entries.Count);
            var entry = saved.NpcExperienceInbox.Entries.Single();
            Equal(NpcExperienceEventOrigin.AttributedSpeech, entry.Origin);
            Equal(mask, entry.AlreadyAppliedEffectsMask);
            Equal("I will help you.", entry.ToObservedTurn().Text);
            Equal(speech.Evidence.UtteranceId, entry.UtteranceId);
            Equal(1L, saved.ConversationMoods.Single(x => x.PersonId == observer).AcceptedTurnSequence);

            var restored = new WorldStateStore(0);
            restored.Restore(saved);
            True(!restored.TryEnqueueNpcObservedSpeech(speech.Evidence, speech.Turn, mask));
            Equal(1L, restored.Snapshot().NpcExperienceInbox.Revision);
            Equal(1L, restored.CaptureConversationMood(observer).AcceptedTurnSequence);
        }

        private static void StaleTurnLeavesNoSpeech()
        {
            string observer;
            var store = NpcExperienceCommitTests.Create(out observer);
            var ticket = NpcObservedSpeechTestFixture.Issue(store, observer, "Could you wait?", 10);
            var speech = ticket.Receipt;
            long sequence;
            True(!store.TryCommitAcceptedSocialExchangeWithObservedSpeech(observer, 1, 0, 0, 11,
                "day", "inn", "Could you wait?", "I cannot.", ticket, speech,
                NpcExperienceAppliedEffectsMask.Mood, out sequence));
            True(!store.TryCommitAcceptedSocialExchangeWithObservedSpeech(observer, 0, 0, 0, 11,
                "day", "inn", "different text", "I cannot.", ticket, speech,
                NpcExperienceAppliedEffectsMask.Mood, out sequence));
            True(!store.TryCommitAcceptedSocialExchangeWithObservedSpeech(observer, 0, 0, 0, 11,
                "day", "inn", "Could you wait?", "I cannot.", ticket, speech,
                NpcExperienceAppliedEffectsMask.None, out sequence));
            Equal(0L, sequence);
            Equal(0L, store.CaptureConversationMood(observer).AcceptedTurnSequence);
            Equal(0, store.Snapshot().ConversationMemories.Count);
            Equal(0L, store.Snapshot().NpcExperienceInbox.Revision);
            Equal(0, store.Snapshot().NpcExperienceInbox.Entries.Count);
        }

        private static void NativeClockBoundary()
        {
            string observer;
            var store = NpcExperienceCommitTests.Create(out observer);
            const string text = "Tell me about this street.";
            var fractional = NpcObservedSpeechTestFixture.Issue(store, observer, text, 100.5);
            long sequence;
            True(!store.TryCommitAcceptedSocialExchangeWithObservedSpeech(observer, 0, 0, 1, 100,
                "day", "street", text, "It is quiet.", fractional, fractional.Receipt,
                NpcExperienceAppliedEffectsMask.Mood, out sequence));
            Equal(0L, sequence);
            Equal(0L, store.CaptureConversationMood(observer).AcceptedTurnSequence);
            Equal(0, store.Snapshot().ConversationMemories.Count);
            Equal(0L, store.Snapshot().NpcExperienceInbox.Revision);

            var nativeClock = NpcObservedSpeechTestFixture.Issue(store, observer, text, 100);
            True(store.TryCommitAcceptedSocialExchangeWithObservedSpeech(observer, 0, 0, 1, 100,
                "day", "street", text, "It is quiet.", nativeClock, nativeClock.Receipt,
                NpcExperienceAppliedEffectsMask.Mood, out sequence));
            Equal(1L, sequence);
            Equal(1L, store.Snapshot().NpcExperienceInbox.Revision);
            Equal(nativeClock.Receipt.Evidence.ReceiptId,
                store.Snapshot().NpcExperienceInbox.Entries.Single().ReceiptId);
        }

        private static void UnavailableReplySpeechOnly()
        {
            string observer;
            var store = NpcExperienceCommitTests.Create(out observer);
            var ticket = NpcObservedSpeechTestFixture.Issue(store, observer, "Please answer me.", 10);
            var speech = ticket.Receipt;
            var mask = NpcExperienceAppliedEffectsMask.Affinity;
            Equal(NpcObservedSpeechAcceptanceResult.Projected,
                store.TryAcceptNpcObservedSpeechAfterAppend(ticket, speech, mask));
            Equal(0L, store.CaptureConversationMood(observer).AcceptedTurnSequence);
            var entry = store.Snapshot().NpcExperienceInbox.Entries.Single();
            Equal(mask, entry.AlreadyAppliedEffectsMask);
            Equal("Please answer me.", entry.ToObservedTurn().Text);
            Equal(NpcObservedSpeechAcceptanceResult.Rejected,
                store.TryAcceptNpcObservedSpeechAfterAppend(ticket, speech, mask));
            Equal(1L, store.Snapshot().NpcExperienceInbox.Revision);
        }

        private static void True(bool value)
        { if (!value) throw new InvalidOperationException("Expected true."); }
        private static void Equal<T>(T expected, T actual)
        { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException("Expected " + expected + ", got " + actual + "."); }
    }
}
