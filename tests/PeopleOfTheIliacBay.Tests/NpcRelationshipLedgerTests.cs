using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcRelationshipLedgerTests
    {
        private static readonly HashSet<string> Known = new HashSet<string>(StringComparer.Ordinal)
        { "npc:observer", "npc:subject", "npc:other" };

        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Relationship ledger is sparse, detached, and reads do not allocate", () =>
            {
                var ledger = NpcRelationshipLedger.Create();
                Check.Equal(0, ledger.Relationships.Count);
                Check.True(ledger.Read("npc:observer", "player") == null);
                Check.Equal(0, ledger.Relationships.Count);
                var copy = NpcRelationshipLedger.CopyValidated(ledger, IsKnown);
                Check.True(!ReferenceEquals(ledger, copy));
                Check.True(!ReferenceEquals(ledger.Relationships, copy.Relationships));
                Check.True(!ReferenceEquals(ledger.AcceptedOutcomes, copy.AcceptedOutcomes));
            }));

            cases.Add(("A canonical native outcome applies all relationship dimensions once", () =>
            {
                var ledger = NpcRelationshipLedger.Create();
                var evidence = Native("npc:observer", "npc:subject", "player", "outcome:help-1",
                    NpcExperienceEventKind.CombatHelp, 12);
                var proposal = Proposal(0, "npc:observer", "player", 0, evidence, 4, 2, 1);
                NpcRelationshipLedger updated;
                Check.True(NpcRelationshipLedger.TryApply(ledger, proposal, IsKnown, out updated));
                Check.Equal(0L, ledger.Revision);
                Check.Equal(0, ledger.Relationships.Count);
                Check.Equal(1L, updated.Revision);
                Check.Equal(1, updated.Relationships.Count);
                Check.Equal(1, updated.AcceptedOutcomes.Count);
                var view = updated.Read("npc:observer", "player");
                Check.True(view.TrustKnown);
                Check.Equal(54, view.ReliabilityTrust);
                Check.Equal(2, view.Resentment);
                Check.Equal(1, view.InterpersonalFear);
                Check.Equal(1L, view.Revision);
            }));

            cases.Add(("Canonical outcome deduplication survives copied restore and ignores receipt aliases", () =>
            {
                var ledger = NpcRelationshipLedger.Create();
                var first = Native("npc:observer", "npc:subject", "player", "outcome:once",
                    NpcExperienceEventKind.ObligationFulfilled, 2, "receipt:a");
                NpcRelationshipLedger updated;
                Check.True(NpcRelationshipLedger.TryApply(ledger,
                    Proposal(0, "npc:observer", "player", 0, first, 2, 0, 0), IsKnown, out updated));

                var restored = NpcRelationshipLedger.CopyValidated(updated, IsKnown);
                var aliasedReceipt = Native("npc:observer", "npc:subject", "player", "outcome:once",
                    NpcExperienceEventKind.ObligationFulfilled, 2, "receipt:b");
                NpcRelationshipLedger rejected;
                Check.True(!NpcRelationshipLedger.TryApply(restored,
                    Proposal(1, "npc:observer", "player", 1, aliasedReceipt, 2, 0, 0), IsKnown, out rejected));
                Check.True(rejected == null);
                Check.Equal(52, restored.Read("npc:observer", "player").ReliabilityTrust);
                Check.Equal(1, restored.AcceptedOutcomes.Count);
            }));

            cases.Add(("Outcome acceptance is scoped to the observing NPC", () =>
            {
                var ledger = NpcRelationshipLedger.Create();
                NpcRelationshipLedger afterFirst;
                var first = Native("npc:observer", "npc:subject", "player", "outcome:shared-world-event",
                    NpcExperienceEventKind.ObligationFulfilled, 2);
                Check.True(NpcRelationshipLedger.TryApply(ledger,
                    Proposal(0, "npc:observer", "player", 0, first, 1, 0, 0), IsKnown, out afterFirst));

                NpcRelationshipLedger afterSecond;
                var second = Native("npc:other", "npc:subject", "player", "outcome:shared-world-event",
                    NpcExperienceEventKind.ObligationFulfilled, 2);
                Check.True(NpcRelationshipLedger.TryApply(afterFirst,
                    Proposal(1, "npc:other", "player", 0, second, 1, 0, 0), IsKnown, out afterSecond));
                Check.Equal(2, afterSecond.AcceptedOutcomes.Count);
                Check.Equal(1L, afterSecond.Read("npc:observer", "player").Revision);
                Check.Equal(1L, afterSecond.Read("npc:other", "player").Revision);
            }));

            cases.Add(("Rollback snapshot restores both state and deduplication boundary", () =>
            {
                var before = NpcRelationshipLedger.Create();
                var evidence = Native("npc:observer", "npc:subject", "player", "outcome:branch",
                    NpcExperienceEventKind.ObligationFulfilled, 7);
                NpcRelationshipLedger after;
                Check.True(NpcRelationshipLedger.TryApply(before,
                    Proposal(0, "npc:observer", "player", 0, evidence, 3, 0, 0), IsKnown, out after));
                var postRestore = NpcRelationshipLedger.CopyValidated(after, IsKnown);
                var replay = Proposal(1, "npc:observer", "player", 1, evidence, 3, 0, 0);
                NpcRelationshipLedger discarded;
                Check.True(!NpcRelationshipLedger.TryApply(postRestore, replay, IsKnown, out discarded));

                var rollback = NpcRelationshipLedger.CopyValidated(before, IsKnown);
                Check.True(NpcRelationshipLedger.TryApply(rollback,
                    Proposal(0, "npc:observer", "player", 0, evidence, 3, 0, 0), IsKnown, out discarded));
                Check.Equal(1, discarded.AcceptedOutcomes.Count);
                Check.Equal(53, discarded.Read("npc:observer", "player").ReliabilityTrust);
                Check.Equal(0, before.AcceptedOutcomes.Count);
            }));

            cases.Add(("Wrong observer, target, unknown identity, and stale revisions reject atomically", () =>
            {
                var ledger = NpcRelationshipLedger.Create();
                var valid = Native("npc:observer", "npc:subject", "player", "outcome:guard",
                    NpcExperienceEventKind.ObligationFulfilled, 3);
                NpcRelationshipLedger result;
                Check.True(!NpcRelationshipLedger.TryApply(ledger,
                    Proposal(0, "npc:other", "player", 0, valid, 1, 0, 0), IsKnown, out result));
                Check.True(!NpcRelationshipLedger.TryApply(ledger,
                    Proposal(0, "npc:observer", "npc:other", 0, valid, 1, 0, 0), IsKnown, out result));
                Check.True(!NpcRelationshipLedger.TryApply(ledger,
                    Proposal(0, "npc:unknown", "player", 0, valid, 1, 0, 0), IsKnown, out result));
                Check.Equal(0L, ledger.Revision);
                Check.Equal(0, ledger.Relationships.Count);
                Check.Equal(0, ledger.AcceptedOutcomes.Count);

                Check.True(NpcRelationshipLedger.TryApply(ledger,
                    Proposal(0, "npc:observer", "player", 0, valid, 1, 0, 0), IsKnown, out result));
                Check.True(!NpcRelationshipLedger.TryApply(result,
                    Proposal(1, "npc:observer", "player", 0,
                        Native("npc:observer", "npc:subject", "player", "outcome:stale",
                            NpcExperienceEventKind.ObligationFulfilled, 4), 1, 0, 0), IsKnown, out ledger));
                Check.True(ledger == null);
                Check.Equal(1L, result.Revision);
                Check.Equal(1, result.AcceptedOutcomes.Count);
            }));

            cases.Add(("Attributed claims cannot enter accepted native outcome history", () =>
            {
                var ledger = NpcRelationshipLedger.Create();
                var claim = NpcExperienceEvent.FromAttributedSpeech("npc:observer", "npc:subject", "speech:receipt",
                    "episode:claim", "utterance:claim", NpcExperienceEventKind.ObligationFulfilled,
                    8, "player", "player");
                NpcRelationshipLedger updated;
                Check.True(!NpcRelationshipLedger.TryApply(ledger,
                    Proposal(0, "npc:observer", "player", 0, claim, 5, 0, 0), IsKnown, out updated));
                Check.True(updated == null);
                Check.Equal(0, ledger.Relationships.Count);
                Check.Equal(0, ledger.AcceptedOutcomes.Count);
            }));

            cases.Add(("Typed native appraisals and direct threats apply once at captured time", () =>
            {
                var evidence = Native("npc:observer", "npc:subject", "player", "outcome:late-help",
                    NpcExperienceEventKind.CombatHelp, 10);
                var relation = NpcDirectedRelationship.Create("npc:observer", "player").ToView();
                var request = NpcExperienceAppraisalContract.CreateRequest("branch:a", evidence,
                    NpcPersonalityGenerator.Generate("npc:observer", 1, NpcSocialBackground.Commoner),
                    0, 0, 10, 0, 0, 0, 30, relation, null);
                NpcExperienceAppraisal nativeAppraisal;
                Check.True(NpcExperienceAppraisalContract.TryParseResponse(request,
                    AppraisalJson(request, trust: 10), out nativeAppraisal));
                var ledger = NpcRelationshipLedger.Create();
                NpcRelationshipLedger nativeResult;
                NpcDirectedRelationshipView applied;
                Check.True(!NpcRelationshipLedger.TryApplyAppraisal(ledger, 0, "branch:a", "branch:rollback",
                    evidence, nativeAppraisal, IsKnown, out nativeResult, out applied));
                Check.True(nativeResult == null && applied == null);
                Check.True(NpcRelationshipLedger.TryApplyAppraisal(ledger, 0, "branch:a", "branch:a",
                    evidence, nativeAppraisal, IsKnown, out nativeResult, out applied));
                Check.Equal(30d, applied.LastGameTime);
                Check.Equal(30d, nativeResult.AcceptedOutcomes[0].CapturedGameTime);
                Check.Equal(10d, nativeResult.AcceptedOutcomes[0].ObservedGameTime);
                Check.True(!NpcRelationshipLedger.TryApplyAppraisal(nativeResult, 1, "branch:a", "branch:a",
                    evidence, nativeAppraisal, IsKnown, out ledger, out applied));

                var speech = NpcExperienceEvent.FromAttributedSpeech("npc:observer", "npc:observer", "speech:receipt",
                    "episode:speech", "utterance:threat", NpcExperienceEventKind.StatusOnset, 31, "player");
                var speechRelation = nativeResult.Read("npc:observer", "player");
                var speechRequest = NpcExperienceAppraisalContract.CreateRequest("branch:a", speech,
                    NpcPersonalityGenerator.Generate("npc:observer", 1, NpcSocialBackground.Commoner),
                    0, 0, 30, 0, 0, 0, 32,
                    RecreateView(speechRelation), null, false,
                    NpcObservedConversationTurn.Create("npc:observer", "episode:speech", "player", "speech:receipt",
                        "I will kill you.", 31), null, null, null, null);
                NpcExperienceAppraisal threat;
                Check.True(NpcExperienceAppraisalContract.TryParseResponse(speechRequest,
                    AppraisalJson(speechRequest, trust: -25, resentment: 25, fear: 25,
                        speech: NpcExperienceSpeechActKind.DirectThreat, speechTarget: "npc:observer"), out threat));
                NpcRelationshipLedger speechResult;
                Check.True(NpcRelationshipLedger.TryApplyAppraisal(nativeResult, 1, "branch:a", "branch:a",
                    speech, threat, IsKnown, out speechResult, out applied));
                Check.Equal(25, applied.InterpersonalFear);
                Check.Equal(25, applied.Resentment);
                Check.Equal(35, applied.ReliabilityTrust);
                Check.Equal(32d, applied.LastGameTime);
                Check.Equal(NpcExperienceEventOrigin.AttributedSpeech, speechResult.AcceptedOutcomes[1].Origin);

                var observedOnly = NpcExperienceEvent.FromObservedSpeech("npc:observer", "npc:observer",
                    "speech:receipt-only", "episode:speech-only", "utterance:threat-only", 33, "player");
                var observedOnlyRequest = NpcExperienceAppraisalContract.CreateRequest("branch:a", observedOnly,
                    NpcPersonalityGenerator.Generate("npc:observer", 1, NpcSocialBackground.Commoner),
                    0, 0, 32, 0, 0, 0, 34, RecreateView(speechResult.Read("npc:observer", "player")), null,
                    false, NpcObservedConversationTurn.Create("npc:observer", "episode:speech-only", "player",
                        "speech:receipt-only", "I will kill you.", 33));
                NpcExperienceAppraisal observedOnlyThreat;
                Check.True(NpcExperienceAppraisalContract.TryParseResponse(observedOnlyRequest,
                    AppraisalJson(observedOnlyRequest, trust: -25, resentment: 25, fear: 25,
                        speech: NpcExperienceSpeechActKind.DirectThreat, speechTarget: "npc:observer"), out observedOnlyThreat));
                NpcRelationshipLedger observedOnlyResult;
                Check.True(NpcRelationshipLedger.TryApplyAppraisal(speechResult, 2, "branch:a", "branch:a",
                    observedOnly, observedOnlyThreat, IsKnown, out observedOnlyResult, out applied));
                var restoredObservedOnly = NpcRelationshipLedger.CopyValidated(observedOnlyResult, IsKnown);
                var acceptedSpeech = restoredObservedOnly.AcceptedOutcomes[2];
                Check.Equal(0, (int)acceptedSpeech.EventKind);
                Check.Equal(0, (int)acceptedSpeech.ClaimedEventKind);
                Check.Equal(observedOnly.DeduplicationKey, acceptedSpeech.DeduplicationKey);
            }));

            cases.Add(("Restored ledgers reject malformed rows, duplicate outcomes, and unknown identities", () =>
            {
                var source = NpcRelationshipLedger.Create();
                source.Relationships.Add(NpcDirectedRelationship.Create("npc:observer", "player"));
                source.Relationships.Add(NpcDirectedRelationship.Create("npc:observer", "player"));
                Check.Throws<ArgumentException>(() => NpcRelationshipLedger.CopyValidated(source, IsKnown));

                var unknown = NpcRelationshipLedger.Create();
                unknown.Relationships.Add(NpcDirectedRelationship.Create("npc:unknown", "player"));
                Check.Throws<ArgumentException>(() => NpcRelationshipLedger.CopyValidated(unknown, IsKnown));

                var badAcceptance = new NpcRelationshipLedger
                {
                    Relationships = new List<NpcDirectedRelationship>
                    { NpcDirectedRelationship.Create("npc:observer", "player") },
                    AcceptedOutcomes = new List<NpcAcceptedRelationshipOutcome>
                    {
                        NpcAcceptedRelationshipOutcome.Create("npc:observer", "player",
                            Native("npc:observer", "npc:subject", "player", "outcome:duplicate",
                                NpcExperienceEventKind.ObligationFulfilled, 5)),
                        NpcAcceptedRelationshipOutcome.Create("npc:observer", "player",
                            Native("npc:observer", "npc:subject", "player", "outcome:duplicate",
                                NpcExperienceEventKind.ObligationFulfilled, 5, "receipt:alias"))
                    }
                };
                Check.Throws<ArgumentException>(() => NpcRelationshipLedger.CopyValidated(badAcceptance, IsKnown));
            }));
        }

        private static bool IsKnown(string identity) { return Known.Contains(identity); }

        private static NpcExperienceEvent Native(string observer, string subject, string responsible,
            string outcomeId, NpcExperienceEventKind kind, double time, string receipt = null)
        {
            return NpcExperienceEvent.FromNativeOutcome(observer, subject, receipt ?? "receipt:" + outcomeId,
                "episode:ledger", outcomeId, kind, time, NpcResponsibleActorAttribution.Known, responsible);
        }

        private static NpcRelationshipLedgerUpdateProposal Proposal(long ledgerRevision, string observer,
            string target, long relationshipRevision, NpcExperienceEvent evidence, int trust, int resentment, int fear)
        {
            return new NpcRelationshipLedgerUpdateProposal
            {
                ExpectedLedgerRevision = ledgerRevision,
                ObserverId = observer,
                TargetId = target,
                RelationshipUpdate = new NpcRelationshipUpdateProposal
                {
                    ExpectedRevision = relationshipRevision,
                    Evidence = evidence,
                    TrustDelta = trust,
                    ResentmentDelta = resentment,
                    InterpersonalFearDelta = fear
                }
            };
        }

        private static NpcDirectedRelationshipView RecreateView(NpcDirectedRelationshipView view)
        {
            var row = NpcDirectedRelationship.Create(view.ObserverId, view.TargetId);
            row.Revision = view.Revision;
            row.LastGameTime = view.LastGameTime;
            row.TrustKnown = view.TrustKnown;
            row.ReliabilityTrust = view.ReliabilityTrust;
            row.TrustConfidence = view.TrustConfidence;
            row.TrustEvidenceCount = view.TrustEvidenceCount;
            row.Resentment = view.Resentment;
            row.InterpersonalFear = view.InterpersonalFear;
            return row.ToView();
        }

        private static string AppraisalJson(NpcExperienceAppraisalRequest request, int trust = 0,
            int resentment = 0, int fear = 0, NpcExperienceSpeechActKind speech = NpcExperienceSpeechActKind.NotApplicable,
            string speechTarget = "")
        {
            return "{\"schemaVersion\":" + NpcExperienceAppraisalContract.SchemaVersion +
                ",\"policyVersion\":" + NpcExperienceAppraisalContract.PolicyVersion +
                ",\"saveBranchId\":\"" + request.SaveBranchId + "\",\"observerId\":\"" + request.ObserverId +
                "\",\"subjectPersonId\":\"" + request.SubjectPersonId + "\",\"evidenceDeduplicationKey\":\"" + request.EvidenceDeduplicationKey +
                "\",\"canonicalEvidenceId\":\"" + request.CanonicalEvidenceId + "\",\"relationshipTargetId\":\"" + request.RelationshipTargetId +
                "\",\"expectedMoodRevision\":" + request.ExpectedMoodRevision + ",\"expectedAffinityRevision\":" + request.ExpectedAffinityRevision +
                ",\"expectedRelationshipRevision\":" + request.ExpectedRelationshipRevision + ",\"moodDelta\":0,\"affinityDelta\":0," +
                "\"trustDelta\":" + trust + ",\"resentmentDelta\":" + resentment + ",\"fearDelta\":" + fear +
                ",\"speechActKind\":\"" + speech + "\",\"speechTargetPersonId\":\"" + speechTarget + "\",\"conversationOption\":\"NoAction\"}";
        }
    }
}
