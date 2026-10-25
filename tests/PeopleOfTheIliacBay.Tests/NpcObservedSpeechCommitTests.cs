using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcObservedSpeechCommitTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("receipt-bound observed speech queues only exact native turn and survives detached copy", ExactObservedTurn));
            cases.Add(("observed speech rejects mismatched receipt speaker time branch identities and mask bits", InvalidObservedTurnRejected));
            cases.Add(("applied mood affinity mask binds into request and parser rejects duplicate effects", MaskBoundToRequest));
            cases.Add(("masked speech transaction consumes once without reapplying mood or affinity", MaskedSpeechTransaction));
            cases.Add(("unmasked direct threat applies severe bounded speech consequences atomically", SevereSpeechTransaction));
            cases.Add(("masked consumed journal corruption is rejected on restore", MaskedJournalValidation));
        }

        private static void ExactObservedTurn()
        {
            string observer;
            var store = NpcExperienceCommitTests.Create(out observer);
            var ticket = NpcObservedSpeechTestFixture.Issue(store, observer, "I will kill you.", 10);
            var receipt = ticket.Receipt;
            var evidence = receipt.Evidence;
            var turn = receipt.Turn;
            var mask = NpcExperienceAppliedEffectsMask.Mood | NpcExperienceAppliedEffectsMask.Affinity;
            var alias = NpcExperienceEvent.FromObservedSpeech(observer, observer, "speech:alias",
                evidence.EpisodeId, evidence.UtteranceId, 10, "player");
            var aliasReceipt = NpcObservedSpeechReceipt.Create(alias,
                NpcObservedConversationTurn.Create(observer, alias.EpisodeId, "player",
                    alias.ReceiptId, "I will kill you.", 10));
            Equal(NpcObservedSpeechAcceptanceResult.Rejected,
                store.TryAcceptNpcObservedSpeechAfterAppend(ticket, aliasReceipt, mask));
            CommitMaskedSpeech(store, observer, ticket, 0);
            var snapshot = store.Snapshot();
            Equal(1, snapshot.NpcExperienceInbox.Entries.Count);
            var saved = snapshot.NpcExperienceInbox.Entries[0];
            Equal(NpcExperienceEventOrigin.AttributedSpeech, saved.Origin);
            Equal(evidence.UtteranceId, saved.UtteranceId);
            Equal("player", saved.AttributedSpeakerId);
            Equal(mask, saved.AlreadyAppliedEffectsMask);
            Equal("I will kill you.", saved.ToObservedTurn().Text);
            Equal(evidence.ReceiptId, saved.ToObservedTurn().ReceiptId);
            Equal(NpcExperienceEventOrigin.AttributedSpeech, saved.ToEvent().Origin);
            Equal(evidence.UtteranceId, saved.ToEvent().UtteranceId);
            Equal(NpcObservedSpeechAcceptanceResult.Rejected,
                store.TryAcceptNpcObservedSpeechAfterAppend(ticket, receipt, mask));
            Equal(1L, store.Snapshot().NpcExperienceInbox.Revision);
        }

        private static void InvalidObservedTurnRejected()
        {
            string observer;
            var store = NpcExperienceCommitTests.Create(out observer);
            var ticket = NpcObservedSpeechTestFixture.Issue(store, observer, "I will kill you.", 10);
            var evidence = ticket.Receipt.Evidence;
            var validText = "I will kill you.";
            var mismatches = new[]
            {
                NpcObservedConversationTurn.Create(observer, evidence.EpisodeId, "another-npc", evidence.ReceiptId, validText, 10),
                NpcObservedConversationTurn.Create(observer, evidence.EpisodeId, "player", "speech:wrong", validText, 10),
                NpcObservedConversationTurn.Create(observer, evidence.EpisodeId, "player", evidence.ReceiptId, validText, 11),
                NpcObservedConversationTurn.Create(observer, "episode:wrong", "player", evidence.ReceiptId, validText, 10)
            };
            foreach (var turn in mismatches)
                Throws<ArgumentException>(() => NpcObservedSpeechReceipt.Create(evidence, turn));
            Throws<ArgumentException>(() => NpcObservedSpeechReceipt.Create(evidence,
                NpcObservedConversationTurn.Create("npc:unknown", evidence.EpisodeId, "player", evidence.ReceiptId, validText, 10)));
            Throws<ArgumentException>(() => NpcObservedSpeechReceipt.Create(evidence,
                NpcObservedConversationTurn.Create(observer, evidence.EpisodeId, "npc:unknown", evidence.ReceiptId, validText, 10)));
            Equal(NpcObservedSpeechAcceptanceResult.Rejected,
                store.TryAcceptNpcObservedSpeechAfterAppend(ticket, ticket.Receipt, (NpcExperienceAppliedEffectsMask)4));
            True(!store.TryEnqueueNpcExperience(evidence));
            Equal(0L, store.Snapshot().NpcExperienceInbox.Revision);
            Equal(0, store.Snapshot().NpcExperienceInbox.Entries.Count);
        }

        private static void MaskBoundToRequest()
        {
            string observer;
            var store = NpcExperienceCommitTests.Create(out observer);
            var source = NpcObservedSpeechTestFixture.Issue(store, observer, "I will kill you.", 10);
            var receipt = source.Receipt;
            var evidence = receipt.Evidence;
            var turn = receipt.Turn;
            var mask = NpcExperienceAppliedEffectsMask.Mood | NpcExperienceAppliedEffectsMask.Affinity;
            CommitMaskedSpeech(store, observer, source, 0);
            NpcExperienceAppraisalTicket ticket;
            True(store.TryCaptureNpcExperienceAppraisal(observer, 10, out ticket));
            Equal("I will kill you.", ticket.Request.ObservedUtterance);
            Equal(mask, ticket.Request.AlreadyAppliedEffectsMask);
            NpcExperienceAppraisal parsed;
            True(!NpcExperienceAppraisalContract.TryParseResponse(ticket.Request,
                Result(ticket.Request, mood: -1), out parsed));
            True(!NpcExperienceAppraisalContract.TryParseResponse(ticket.Request,
                Result(ticket.Request, affinity: -1), out parsed));
            True(NpcExperienceAppraisalContract.TryParseResponse(ticket.Request,
                Result(ticket.Request, trust: -2, resentment: 3, fear: 1, speechAct: "DirectThreat", target: observer), out parsed));
            Throws<ArgumentException>(() => NpcExperienceAppraisalContract.CreateRequest("branch:test", evidence,
                NpcPersonalityGenerator.Generate(observer, 109, NpcSocialBackground.Commoner),
                0, 0, 0, 0, 0, 0, 10, null, null, false, turn, null, null, null, null,
                (NpcExperienceAppliedEffectsMask)8));
        }

        private static void MaskedSpeechTransaction()
        {
            string observer;
            var store = NpcExperienceCommitTests.Create(out observer);
            store.SetConversationBaseline(observer, 40, 10);
            long sequence;
            var source = NpcObservedSpeechTestFixture.Issue(store, observer, "I will kill you.", 10);
            True(store.TryCommitAcceptedSocialExchangeWithObservedSpeech(observer, 0, 0, 4, 10,
                "date", "place", "I will kill you.", "I understand.", source, source.Receipt,
                NpcExperienceAppliedEffectsMask.Mood | NpcExperienceAppliedEffectsMask.Affinity, out sequence));
            int moodBefore = store.CaptureConversationMood(observer, 10).Value;
            long moodRevisionBefore = store.CaptureConversationMood(observer).Revision;
            int affinityBefore;
            True(store.TryGetActualConversationDisposition(observer, out affinityBefore));
            var mask = NpcExperienceAppliedEffectsMask.Mood | NpcExperienceAppliedEffectsMask.Affinity;
            Equal(mask, store.Snapshot().NpcExperienceInbox.Entries[0].AlreadyAppliedEffectsMask);
            NpcExperienceAppraisalTicket ticket;
            True(store.TryCaptureNpcExperienceAppraisal(observer, 10, out ticket));
            string response = Result(ticket.Request, trust: -5, resentment: 10, fear: 10,
                speechAct: "DirectThreat", target: observer);
            NpcExperienceCommitTests.BindSpeech(store, ticket, "DirectThreat");
            NpcExperienceAppraisal parsed;
            True(NpcExperienceAppraisalContract.TryParseResponse(ticket.Request, response, out parsed));
            True(store.TryCommitNpcExperienceAppraisal(ticket, response));
            Equal(moodBefore, store.CaptureConversationMood(observer, 10).Value);
            Equal(moodRevisionBefore, store.CaptureConversationMood(observer).Revision);
            int affinityAfter;
            True(store.TryGetActualConversationDisposition(observer, out affinityAfter));
            Equal(affinityBefore, affinityAfter);
            Equal(0, store.ReadPendingNpcExperiences(observer, 10).Count);
            True(!store.TryCommitNpcExperienceAppraisal(ticket, response));
            var saved = store.Snapshot();
            var restored = new WorldStateStore(0);
            restored.Restore(saved);
            Equal(0, restored.ReadPendingNpcExperiences(observer, 10).Count);
            Equal(moodBefore, restored.CaptureConversationMood(observer, 10).Value);
            Equal(NpcObservedSpeechAcceptanceResult.Rejected,
                restored.TryAcceptNpcObservedSpeechAfterAppend(source, source.Receipt, mask));
        }

        private static void SevereSpeechTransaction()
        {
            string observer;
            var store = NpcExperienceCommitTests.Create(out observer);
            store.SetConversationBaseline(observer, 50, 10);
            var source = NpcObservedSpeechTestFixture.Issue(store, observer, "I will kill you.", 10);
            Equal(NpcObservedSpeechAcceptanceResult.Projected,
                store.TryAcceptNpcObservedSpeechAfterAppend(source, source.Receipt,
                    NpcExperienceAppliedEffectsMask.None));
            NpcExperienceAppraisalTicket ticket;
            True(store.TryCaptureNpcExperienceAppraisal(observer, 10, out ticket));
            string response = Result(ticket.Request, mood: -10, affinity: -50, trust: -25, resentment: 25, fear: 25,
                speechAct: "DirectThreat", target: observer);
            NpcExperienceCommitTests.BindSpeech(store, ticket, "DirectThreat");
            NpcExperienceAppraisal parsed;
            True(NpcExperienceAppraisalContract.TryParseResponse(ticket.Request, response, out parsed));
            True(store.TryCommitNpcExperienceAppraisal(ticket, response));
            Equal(-10, store.CaptureConversationMood(observer, 10).Value);
            Equal(0, store.ReadPendingNpcExperiences(observer, 10).Count);
            int affinity;
            True(store.TryGetActualConversationDisposition(observer, out affinity));
            Equal(0, affinity);
            var relation = store.ReadNpcRelationship(observer, "player");
            True(relation.TrustKnown);
            Equal(25, relation.Resentment);
            Equal(25, relation.InterpersonalFear);
        }

        private static void MaskedJournalValidation()
        {
            string observer;
            var store = NpcExperienceCommitTests.Create(out observer);
            var source = NpcObservedSpeechTestFixture.Issue(store, observer, "I will kill you.", 10);
            CommitMaskedSpeech(store, observer, source, 0);
            NpcExperienceAppraisalTicket ticket;
            True(store.TryCaptureNpcExperienceAppraisal(observer, 10, out ticket));
            string response = Result(ticket.Request, trust: -1, speechAct: "DirectThreat", target: observer);
            NpcExperienceCommitTests.BindSpeech(store, ticket, "DirectThreat");
            NpcExperienceAppraisal parsed;
            True(NpcExperienceAppraisalContract.TryParseResponse(ticket.Request, response, out parsed));
            True(store.TryCommitNpcExperienceAppraisal(ticket, response));
            var valid = store.Snapshot();
            var corrupt = store.Snapshot();
            corrupt.NpcExperienceInbox.Entries[0].Appraisal.MoodDelta = -1;
            Throws<ArgumentException>(() => store.Restore(corrupt));
            Equal(0, store.ReadPendingNpcExperiences(observer, 10).Count);
            Equal(0, store.CaptureConversationMood(observer, 10).Value);
            var restored = new WorldStateStore(0);
            restored.Restore(valid);
            Equal(0, restored.ReadPendingNpcExperiences(observer, 10).Count);
        }

        private static NpcExperienceEvent Speech(string observer, string utterance, string receipt = null)
        {
            return NpcExperienceEvent.FromObservedSpeech(observer, observer, receipt ?? "speech:" + utterance.Substring("utterance:".Length),
                "episode:speech", utterance, 10, "player");
        }
        private static void CommitMaskedSpeech(WorldStateStore store, string observer,
            NpcObservedSpeechTicket source, int moodDelta)
        {
            long sequence;
            var mood = store.CaptureConversationMood(observer);
            True(store.TryCommitAcceptedSocialExchangeWithObservedSpeech(observer,
                mood.AcceptedTurnSequence, mood.Revision, moodDelta, 10, "date", "place",
                source.ExactText, "I understand.", source, source.Receipt,
                NpcExperienceAppliedEffectsMask.Mood | NpcExperienceAppliedEffectsMask.Affinity,
                out sequence));
            Equal(mood.AcceptedTurnSequence + 1, sequence);
        }
        private static NpcObservedConversationTurn Turn(NpcExperienceEvent evidence, string text)
        {
            return NpcObservedConversationTurn.Create(evidence.ObserverId, evidence.EpisodeId, evidence.AttributedSpeakerId,
                evidence.ReceiptId, text, evidence.ObservedGameTime);
        }
        private static string Result(NpcExperienceAppraisalRequest request, int mood = 0, int affinity = 0,
            int trust = 0, int resentment = 0, int fear = 0, string speechAct = "NotApplicable", string target = "",
            string option = "NoAction")
        {
            return "{\"schemaVersion\":" + NpcExperienceAppraisalContract.SchemaVersion + ",\"policyVersion\":" + NpcExperienceAppraisalContract.PolicyVersion +
                ",\"saveBranchId\":\"" + request.SaveBranchId + "\",\"observerId\":\"" + request.ObserverId +
                "\",\"subjectPersonId\":\"" + request.SubjectPersonId + "\",\"evidenceDeduplicationKey\":\"" + request.EvidenceDeduplicationKey +
                "\",\"canonicalEvidenceId\":\"" + request.CanonicalEvidenceId + "\",\"relationshipTargetId\":\"" + (request.RelationshipTargetId ?? "") +
                "\",\"expectedMoodRevision\":" + request.ExpectedMoodRevision + ",\"expectedAffinityRevision\":" + request.ExpectedAffinityRevision +
                ",\"expectedRelationshipRevision\":" + request.ExpectedRelationshipRevision + ",\"moodDelta\":" + mood + ",\"affinityDelta\":" + affinity +
                ",\"trustDelta\":" + trust + ",\"resentmentDelta\":" + resentment + ",\"fearDelta\":" + fear +
                ",\"speechActKind\":\"" + speechAct + "\",\"speechTargetPersonId\":\"" + target + "\",\"conversationOption\":\"" + option + "\"}";
        }
        private static void True(bool value)
        { if (!value) throw new InvalidOperationException("Expected true."); }
        private static void Equal<T>(T expected, T actual)
        { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException("Expected " + expected + ", got " + actual + "."); }
        private static void Throws<T>(Action action) where T : Exception
        { try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name + "."); }
    }
}
