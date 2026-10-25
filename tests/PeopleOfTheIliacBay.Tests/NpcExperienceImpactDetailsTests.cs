using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcExperienceImpactDetailsTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("native health episode impact details validate and survive event and inbox copies", HealthEpisodeRoundTrip));
            cases.Add(("health episode details reject invalid totals endpoints and native kind mismatch", InvalidHealthEpisodeRejected));
            cases.Add(("speech evidence cannot carry native health impact details", SpeechCannotClaimNativeImpact));
            cases.Add(("corrupted health impact save is rejected without replacing live state", CorruptImpactRestoreIsAtomic));
            cases.Add(("native gold transfer quantity survives event and inbox copies", GoldTransferRoundTrip));
            cases.Add(("native item transfer carries explicit known or unknown valuation", ItemTransferValuationRoundTrip));
            cases.Add(("transfer impact details reject invalid magnitude valuation and event kind", InvalidTransferImpactRejected));
            cases.Add(("appraisal prompt shows native gift magnitude without inventing unknown price", TransferPromptUsesOnlyNativeValue));
        }

        private static void HealthEpisodeRoundTrip()
        {
            string id;
            var store = NpcExperienceCommitTests.Create(out id);
            var details = NpcExperienceImpactDetails.ForHealthEpisode(90, 55, 100, 40, 25, 3);
            var evidence = NpcExperienceEvent.FromNativeOutcome(id, id, "receipt:health", "episode:health",
                "outcome:health", NpcExperienceEventKind.Injury, 18, NpcResponsibleActorAttribution.Unknown,
                null, details);
            True(HistoricalNpcExperienceFixture.ImportHistoricalNativeEvent(store, evidence));
            var saved = store.Snapshot().NpcExperienceInbox.Entries[0];
            Equal(1, saved.ImpactDetailsKind);
            Equal(90, saved.HealthInitial);
            Equal(55, saved.HealthFinal);
            Equal(40L, saved.HealthDamageTotal);
            Equal(25L, saved.HealthHealingTotal);
            var roundTrip = saved.ToEvent();
            Equal(90, roundTrip.NativeImpactDetails.InitialHealth);
            Equal(55, roundTrip.NativeImpactDetails.FinalHealth);
            Equal(100, roundTrip.NativeImpactDetails.MaximumHealth);
            Equal(3, roundTrip.NativeImpactDetails.ChangeCount);
            NpcExperienceAppraisalTicket ticket;
            True(store.TryCaptureNpcExperienceAppraisal(id, 20, out ticket));
            True(ticket.Request.Prompt.Contains("totalDamage=40"));
            var detached = NpcExperienceInboxEntry.CopyValidated(saved);
            detached.HealthInitial = 1;
            Equal(90, store.Snapshot().NpcExperienceInbox.Entries[0].HealthInitial);
        }

        private static void InvalidHealthEpisodeRejected()
        {
            Throws<ArgumentException>(() => NpcExperienceImpactDetails.ForHealthEpisode(101, 55, 100, 40, 25, 3));
            Throws<ArgumentException>(() => NpcExperienceImpactDetails.ForHealthEpisode(90, 55, 100, -1, 25, 3));
            Throws<ArgumentException>(() => NpcExperienceImpactDetails.ForHealthEpisode(90, 55, 100, 40, 25, 0));
            string id;
            NpcExperienceCommitTests.Create(out id);
            var details = NpcExperienceImpactDetails.ForHealthEpisode(90, 55, 100, 40, 25, 3);
            Throws<ArgumentException>(() => NpcExperienceEvent.FromNativeOutcome(id, id, "receipt:bad-kind",
                "episode:health", "outcome:bad-kind", NpcExperienceEventKind.Healing, 18,
                NpcResponsibleActorAttribution.Unknown, null, details));
        }

        private static void SpeechCannotClaimNativeImpact()
        {
            string id;
            var store = NpcExperienceCommitTests.Create(out id);
            var speech = NpcExperienceEvent.FromObservedSpeech(id, id, "speech:health", "episode:health",
                "utterance:health", 18, "player");
            var turn = NpcObservedConversationTurn.Create(id, speech.EpisodeId, "player", speech.ReceiptId,
                "I was injured for 40 points.", 18);
            var row = NpcExperienceInboxEntry.FromObservedSpeech(speech, turn, NpcExperienceAppliedEffectsMask.None);
            row.ImpactDetailsKind = 1;
            row.HealthInitial = 90;
            row.HealthFinal = 55;
            row.HealthMaximum = 100;
            row.HealthDamageTotal = 40;
            row.HealthHealingTotal = 25;
            row.HealthChangeCount = 3;
            Throws<ArgumentException>(() => NpcExperienceInboxEntry.CopyValidated(row));
            var fakeGift = NpcExperienceInboxEntry.FromObservedSpeech(speech, turn,
                NpcExperienceAppliedEffectsMask.None);
            fakeGift.ImpactDetailsKind = (int)NpcExperienceImpactKind.MonetaryTransfer;
            fakeGift.TransferQuantity = 1000;
            fakeGift.TransferCurrency = (int)NpcExperienceCurrency.Gold;
            Throws<ArgumentException>(() => NpcExperienceInboxEntry.CopyValidated(fakeGift));
            HistoricalNpcObservedSpeechFixture.Import(store, speech, turn,
                NpcExperienceAppliedEffectsMask.None);
            NpcExperienceAppraisalRequest request = NpcExperienceAppraisalContract.CreateRequest("branch:speech", speech,
                store.ReadNpcPersonality(id), 0, 0, 18, 0, 0, 0, 18, null, null,
                observedUtterance: turn);
            True(!request.Prompt.Contains("nativeKnownHealthEpisode"));
        }

        private static void CorruptImpactRestoreIsAtomic()
        {
            string id;
            var store = NpcExperienceCommitTests.Create(out id);
            var details = NpcExperienceImpactDetails.ForHealthEpisode(90, 55, 100, 40, 25, 3);
            var evidence = NpcExperienceEvent.FromNativeOutcome(id, id, "receipt:restore-health", "episode:health",
                "outcome:restore-health", NpcExperienceEventKind.Injury, 18, NpcResponsibleActorAttribution.Unknown,
                null, details);
            True(HistoricalNpcExperienceFixture.ImportHistoricalNativeEvent(store, evidence));
            var corrupt = store.Snapshot();
            corrupt.NpcExperienceInbox.Entries[0].HealthDamageTotal = -2;
            Throws<ArgumentException>(() => store.Restore(corrupt));
            Equal(1, store.Snapshot().NpcExperienceInbox.Entries.Count);
            Equal(40L, store.Snapshot().NpcExperienceInbox.Entries[0].HealthDamageTotal);
        }

        private static void GoldTransferRoundTrip()
        {
            string id;
            var store = NpcExperienceCommitTests.Create(out id);
            var details = NpcExperienceImpactDetails.ForGoldTransfer(1000);
            var evidence = NpcExperienceEvent.FromNativeOutcome(id, id, "receipt:gold", "episode:gold",
                "outcome:gold", NpcExperienceEventKind.MoneyReceived, 18, NpcResponsibleActorAttribution.Unknown,
                null, details);
            True(HistoricalNpcExperienceFixture.ImportHistoricalNativeEvent(store, evidence));
            var saved = store.Snapshot().NpcExperienceInbox.Entries[0];
            Equal(2, saved.ImpactDetailsKind);
            Equal(1000L, saved.TransferQuantity);
            Equal(NpcExperienceCurrency.Gold, (NpcExperienceCurrency)saved.TransferCurrency);
            var restored = saved.ToEvent().NativeImpactDetails;
            Equal(NpcExperienceImpactKind.MonetaryTransfer, restored.Kind);
            Equal(1000L, restored.Quantity);
            Equal(NpcExperienceCurrency.Gold, restored.Currency);
            Equal(NpcExperienceNativeValuationStatus.NotApplicable, restored.NativeValuationStatus);
            NpcExperienceAppraisalTicket ticket;
            True(store.TryCaptureNpcExperienceAppraisal(id, 20, out ticket));
            True(ticket.Request.Prompt.Contains("nativeKnownMonetaryTransfer={quantity=1000,currency=Gold}"));

            string smallId;
            var smallStore = NpcExperienceCommitTests.Create(out smallId);
            var oneGold = NpcExperienceEvent.FromNativeOutcome(smallId, smallId, "receipt:one-gold", "episode:one-gold",
                "outcome:one-gold", NpcExperienceEventKind.MoneyReceived, 18, NpcResponsibleActorAttribution.Unknown,
                null, NpcExperienceImpactDetails.ForGoldTransfer(1));
            True(HistoricalNpcExperienceFixture.ImportHistoricalNativeEvent(smallStore, oneGold));
            True(smallStore.TryCaptureNpcExperienceAppraisal(smallId, 20, out ticket));
            True(ticket.Request.Prompt.Contains("nativeKnownMonetaryTransfer={quantity=1,currency=Gold}"));
            True(!ticket.Request.Prompt.Contains("quantity=1000"));
        }

        private static void ItemTransferValuationRoundTrip()
        {
            string id;
            var store = NpcExperienceCommitTests.Create(out id);
            var known = NpcExperienceImpactDetails.ForItemTransfer(2, NpcExperienceNativeValuationStatus.Known, 850);
            var evidence = NpcExperienceEvent.FromNativeOutcome(id, id, "receipt:gift", "episode:gift",
                "outcome:gift", NpcExperienceEventKind.GiftReceived, 18, NpcResponsibleActorAttribution.Unknown,
                null, known);
            True(HistoricalNpcExperienceFixture.ImportHistoricalNativeEvent(store, evidence));
            var row = store.Snapshot().NpcExperienceInbox.Entries[0];
            Equal(3, row.ImpactDetailsKind);
            Equal(2L, row.TransferQuantity);
            Equal(2, row.NativeValuationStatus);
            Equal(850L, row.NativeGoldValue);
            var roundTrip = row.ToEvent().NativeImpactDetails;
            Equal(NpcExperienceImpactKind.ItemTransfer, roundTrip.Kind);
            Equal(2L, roundTrip.Quantity);
            Equal(NpcExperienceNativeValuationStatus.Known, roundTrip.NativeValuationStatus);
            Equal(850L, roundTrip.NativeGoldValue);
            var unknown = NpcExperienceImpactDetails.ForItemTransfer(1,
                NpcExperienceNativeValuationStatus.Unknown, 0);
            var unknownEvent = NpcExperienceEvent.FromNativeOutcome(id, id, "receipt:gift-unknown", "episode:gift",
                "outcome:gift-unknown", NpcExperienceEventKind.GiftReceived, 19,
                NpcResponsibleActorAttribution.Unknown, null, unknown);
            True(HistoricalNpcExperienceFixture.ImportHistoricalNativeEvent(store, unknownEvent));
            NpcExperienceAppraisalTicket ticket;
            True(store.TryCaptureNpcExperienceAppraisal(id, 20, out ticket));
            True(ticket.Request.Prompt.Contains("nativeKnownItemTransfer={quantity=2,nativeValuation=known,nativeGoldValue=850}"));
            True(!ticket.Request.Prompt.Contains("nativeGoldValue=0"));
        }

        private static void InvalidTransferImpactRejected()
        {
            Throws<ArgumentException>(() => NpcExperienceImpactDetails.ForGoldTransfer(0));
            Throws<ArgumentOutOfRangeException>(() => NpcExperienceImpactDetails.ForGoldTransfer(1000000000001L));
            Throws<ArgumentException>(() => NpcExperienceImpactDetails.ForItemTransfer(0,
                NpcExperienceNativeValuationStatus.Unknown, 0));
            Throws<ArgumentException>(() => NpcExperienceImpactDetails.ForItemTransfer(1,
                NpcExperienceNativeValuationStatus.Unknown, 1));
            Throws<ArgumentException>(() => NpcExperienceImpactDetails.ForItemTransfer(1,
                NpcExperienceNativeValuationStatus.Known, -1));
            Throws<ArgumentException>(() => NpcExperienceImpactDetails.ForItemTransfer(1,
                NpcExperienceNativeValuationStatus.Known, 1000000000001L));
            string id;
            NpcExperienceCommitTests.Create(out id);
            var item = NpcExperienceImpactDetails.ForItemTransfer(1,
                NpcExperienceNativeValuationStatus.Unknown, 0);
            Throws<ArgumentException>(() => NpcExperienceEvent.FromNativeOutcome(id, id, "receipt:wrong-item-kind",
                "episode:gift", "outcome:wrong-item-kind", NpcExperienceEventKind.MoneyReceived, 18,
                NpcResponsibleActorAttribution.Unknown, null, item));
            var gold = NpcExperienceImpactDetails.ForGoldTransfer(25);
            Throws<ArgumentException>(() => NpcExperienceEvent.FromNativeOutcome(id, id, "receipt:wrong-gold-kind",
                "episode:gift", "outcome:wrong-gold-kind", NpcExperienceEventKind.GiftReceived, 18,
                NpcResponsibleActorAttribution.Unknown, null, gold));

            var store = NpcExperienceCommitTests.Create(out id);
            var valid = NpcExperienceEvent.FromNativeOutcome(id, id, "receipt:corrupt-transfer", "episode:gift",
                "outcome:corrupt-transfer", NpcExperienceEventKind.GiftReceived, 18,
                NpcResponsibleActorAttribution.Unknown, null, item);
            True(HistoricalNpcExperienceFixture.ImportHistoricalNativeEvent(store, valid));
            var corrupt = store.Snapshot();
            corrupt.NpcExperienceInbox.Entries[0].NativeGoldValue = 9;
            Throws<ArgumentException>(() => store.Restore(corrupt));
            Equal(1, store.ReadPendingNpcExperiences(id, 10).Count);

            var attributedGift = NpcExperienceEvent.FromNativeOutcome(id, id, "receipt:gift-trust", "episode:gift",
                "outcome:gift-trust", NpcExperienceEventKind.GiftReceived, 18,
                NpcResponsibleActorAttribution.Known, "player", item);
            NpcDirectedRelationship ignored;
            True(!NpcDirectedRelationship.TryApply(NpcDirectedRelationship.Create(id, "player"),
                new NpcRelationshipUpdateProposal { ExpectedRevision = 0, Evidence = attributedGift, TrustDelta = 1 },
                out ignored));
        }

        private static void TransferPromptUsesOnlyNativeValue()
        {
            string id;
            var store = NpcExperienceCommitTests.Create(out id);
            var unknown = NpcExperienceImpactDetails.ForItemTransfer(1,
                NpcExperienceNativeValuationStatus.Unknown, 0);
            True(HistoricalNpcExperienceFixture.ImportHistoricalNativeEvent(store, NpcExperienceEvent.FromNativeOutcome(id, id,
                "receipt:gift-prompt", "episode:gift-prompt", "outcome:gift-prompt",
                NpcExperienceEventKind.GiftReceived, 18, NpcResponsibleActorAttribution.Unknown, null, unknown)));
            NpcExperienceAppraisalTicket ticket;
            True(store.TryCaptureNpcExperienceAppraisal(id, 20, out ticket));
            True(ticket.Request.Prompt.Contains("nativeKnownItemTransfer={quantity=1,nativeValuation=unknown}"));
            True(!ticket.Request.Prompt.Contains("nativeGoldValue="));
        }

        private static void True(bool value) { if (!value) throw new InvalidOperationException("Expected true."); }
        private static void Equal<T>(T expected, T actual)
        { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException("Expected " + expected + ", got " + actual + "."); }
        private static void Throws<T>(Action action) where T : Exception
        { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name + "."); }
    }
}
