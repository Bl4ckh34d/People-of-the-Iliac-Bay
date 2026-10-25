using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcExperienceAffinityPolicyTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Native event affinity uses the existing positive encounter and credit governor", new Action(() => {
                var attitude = new ConversationAttitude { PersonId = "npc:observer", Offset = 8,
                    PositiveCredit = 5, PositiveCreditUpdatedAt = 120, PositiveThisEncounter = 4,
                    Day = 0, PositiveToday = 2, Insults = 0 };
                var evidence = Native(NpcExperienceEventKind.MoneyReceived, "player");
                NpcExperienceAppraisalRequest request;
                var appraisal = Appraisal(evidence, affinity: 3, currentAffinity: 8, revision: 6,
                    positiveCredit: 5, capturedTime: 120, resultRequest: out request);

                ConversationAffinityEventUpdate update; string rejection;
                Check.True(ConversationAffinityEventPolicy.TryCreate("branch:a", evidence, request, appraisal,
                    attitude, 6, out update, out rejection));
                Check.Equal(1, update.AppliedDelta);
                Check.Equal(9, update.NextAttitude.Offset);
                Check.Equal(5, update.NextAttitude.PositiveThisEncounter);
                Check.Equal(7L, update.NextRevision);
                Check.Equal(6L, update.ExpectedRevision);
                Check.Equal(8, attitude.Offset);
                Check.Equal(8, update.SourceAttitude.Offset);
                update.NextAttitude.Offset = 99;
                Check.Equal(8, attitude.Offset);
            })));

            cases.Add(("Severe negative native affinity bypasses the positive gain governor", new Action(() => {
                var attitude = new ConversationAttitude { PersonId = "npc:observer", Offset = 40,
                    PositiveCredit = 0, PositiveCreditUpdatedAt = 120, PositiveThisEncounter = 5,
                    PositiveEarned = 20, PositiveProgressionStage = 3, Day = 0 };
                var evidence = NpcExperienceEvent.FromAttributedSpeech("npc:observer", "npc:observer",
                    "receipt:threat", "episode:threat", "utterance:threat",
                    NpcExperienceEventKind.Injury, 110, "player");
                NpcExperienceAppraisalRequest request;
                var appraisal = SpeechAppraisal(evidence, affinity: -25, currentAffinity: 40, revision: 3,
                    positiveCredit: 0, capturedTime: 120, resultRequest: out request);

                ConversationAffinityEventUpdate update; string rejection;
                Check.True(ConversationAffinityEventPolicy.TryCreate("branch:a", evidence, request, appraisal,
                    attitude, 3, out update, out rejection));
                Check.Equal(-25, update.AppliedDelta);
                Check.Equal(15, update.NextAttitude.Offset);
                Check.Equal(0d, update.NextAttitude.PositiveCredit);
                Check.Equal(5, update.NextAttitude.PositiveThisEncounter);
                Check.Equal(25, update.NextAttitude.NegativeToday);
            })));

            cases.Add(("Affinity event policy rejects stale revision, mismatched target, and regressed clock", new Action(() => {
                var attitude = new ConversationAttitude { PersonId = "npc:observer", Offset = 8,
                    PositiveCredit = 2, PositiveCreditUpdatedAt = 120, Day = 0 };
                var evidence = Native(NpcExperienceEventKind.MoneyReceived, "player");
                NpcExperienceAppraisalRequest request;
                var appraisal = Appraisal(evidence, affinity: 1, currentAffinity: 8, revision: 6,
                    positiveCredit: 2, capturedTime: 120, resultRequest: out request);
                ConversationAffinityEventUpdate update; string rejection;
                Check.True(!ConversationAffinityEventPolicy.TryCreate("branch:a", evidence, request, appraisal,
                    attitude, 7, out update, out rejection));
                Check.Equal("stale-affinity-revision", rejection);

                var otherActor = Native(NpcExperienceEventKind.MoneyReceived, "npc:other");
                NpcExperienceAppraisalRequest otherActorRequest;
                var otherActorProposal = Appraisal(otherActor, affinity: 1, currentAffinity: 8, revision: 6,
                    positiveCredit: 2, capturedTime: 120, resultRequest: out otherActorRequest);
                Check.True(!ConversationAffinityEventPolicy.TryCreate("branch:a", otherActor, otherActorRequest, otherActorProposal,
                    attitude, 6, out update, out rejection));

                var laterAttitude = new ConversationAttitude { PersonId = "npc:observer", Offset = 8,
                    PositiveCredit = 2, PositiveCreditUpdatedAt = 130, Day = 0 };
                Check.True(!ConversationAffinityEventPolicy.TryCreate("branch:a", evidence, request, appraisal,
                    laterAttitude, 6, out update, out rejection));
                Check.Equal("affinity-time-regression", rejection);
            })));

            cases.Add(("Positive event credit accrues through the shared friendship governor", new Action(() => {
                double now = ConversationEffects.FriendshipIntervalSeconds("npc:observer", 0) / 2;
                var attitude = new ConversationAttitude { PersonId = "npc:observer", Offset = 0,
                    PositiveCredit = 0, PositiveCreditUpdatedAt = 0, Day = 0 };
                var evidence = Native(NpcExperienceEventKind.MoneyReceived, "player");
                NpcExperienceAppraisalRequest request;
                var appraisal = Appraisal(evidence, affinity: 2, currentAffinity: 0, revision: 1,
                    positiveCredit: 2, capturedTime: now, resultRequest: out request);

                ConversationAffinityEventUpdate update; string rejection;
                Check.True(ConversationAffinityEventPolicy.TryCreate("branch:a", evidence, request, appraisal,
                    attitude, 1, out update, out rejection));
                Check.Equal(2, update.AppliedDelta);
                Check.Equal(2, update.NextAttitude.Offset);
                Check.Near(0.5, update.NextAttitude.PositiveCredit, 0.000001);
                Check.Equal(now, update.NextAttitude.PositiveCreditUpdatedAt);
            })));

            cases.Add(("Native baseline clamping remains distinct from stored disposition offset", new Action(() => {
                var attitude = new ConversationAttitude { PersonId = "npc:observer", Offset = 100,
                    NativeBaseline = 50, HasNativeBaseline = true, PositiveCredit = 5,
                    PositiveCreditUpdatedAt = 120, Day = 0 };
                var evidence = Native(NpcExperienceEventKind.MoneyReceived, "player");
                NpcExperienceAppraisalRequest request;
                var appraisal = Appraisal(evidence, affinity: 1, currentAffinity: 100, revision: 2,
                    positiveCredit: 5, capturedTime: 120, resultRequest: out request);
                ConversationAffinityEventUpdate update; string rejection;
                Check.True(ConversationAffinityEventPolicy.TryCreate("branch:a", evidence, request, appraisal,
                    attitude, 2, out update, out rejection));
                Check.Equal(0, update.AppliedDelta);
                Check.Equal(100, update.NextAttitude.Offset);
                Check.Equal(2L, update.NextRevision);
            })));
        }

        private static NpcExperienceEvent Native(NpcExperienceEventKind kind, string actor)
        {
            return NpcExperienceEvent.FromNativeOutcome("npc:observer", "npc:observer", "receipt:affinity",
                "episode:affinity", "outcome:affinity", kind, 110,
                actor == null ? NpcResponsibleActorAttribution.Unknown : NpcResponsibleActorAttribution.Known, actor);
        }

        private static NpcExperienceAppraisal Appraisal(NpcExperienceEvent evidence, int affinity,
            int currentAffinity, long revision, int positiveCredit, double capturedTime,
            out NpcExperienceAppraisalRequest resultRequest)
        {
            var request = NpcExperienceAppraisalContract.CreateRequest("branch:a", evidence,
                NpcPersonalityGenerator.Generate(evidence.ObserverId, 1, NpcSocialBackground.Commoner),
                0, 1, evidence.ObservedGameTime, currentAffinity, revision, positiveCredit, capturedTime,
                null, null);
            resultRequest = request;
            string json = "{\"schemaVersion\":" + NpcExperienceAppraisalContract.SchemaVersion +
                ",\"policyVersion\":" + NpcExperienceAppraisalContract.PolicyVersion +
                ",\"saveBranchId\":\"branch:a\",\"observerId\":\"" + evidence.ObserverId +
                "\",\"subjectPersonId\":\"" + evidence.SubjectPersonId +
                "\",\"evidenceDeduplicationKey\":\"" + evidence.DeduplicationKey +
                "\",\"canonicalEvidenceId\":\"" + evidence.OutcomeId +
                "\",\"relationshipTargetId\":\"" + (request.RelationshipTargetId ?? "") +
                "\",\"expectedMoodRevision\":1,\"expectedAffinityRevision\":" + revision +
                ",\"expectedRelationshipRevision\":-1,\"moodDelta\":0,\"affinityDelta\":" + affinity +
                ",\"trustDelta\":0,\"resentmentDelta\":0,\"fearDelta\":0," +
                "\"speechActKind\":\"NotApplicable\",\"speechTargetPersonId\":\"\",\"conversationOption\":\"NoAction\"}";
            NpcExperienceAppraisal result;
            Check.True(NpcExperienceAppraisalContract.TryParseResponse(request, json, out result));
            return result;
        }

        private static NpcExperienceAppraisal SpeechAppraisal(NpcExperienceEvent evidence, int affinity,
            int currentAffinity, long revision, int positiveCredit, double capturedTime,
            out NpcExperienceAppraisalRequest resultRequest)
        {
            var utterance = NpcObservedConversationTurn.Create(evidence.ObserverId, evidence.EpisodeId,
                evidence.AttributedSpeakerId, evidence.ReceiptId, "I will kill you", evidence.ObservedGameTime);
            var request = NpcExperienceAppraisalContract.CreateRequest("branch:a", evidence,
                NpcPersonalityGenerator.Generate(evidence.ObserverId, 1, NpcSocialBackground.Commoner),
                0, 1, evidence.ObservedGameTime, currentAffinity, revision, positiveCredit, capturedTime,
                null, null, observedUtterance: utterance);
            resultRequest = request;
            string json = "{\"schemaVersion\":" + NpcExperienceAppraisalContract.SchemaVersion +
                ",\"policyVersion\":" + NpcExperienceAppraisalContract.PolicyVersion +
                ",\"saveBranchId\":\"branch:a\",\"observerId\":\"" + evidence.ObserverId +
                "\",\"subjectPersonId\":\"" + evidence.SubjectPersonId +
                "\",\"evidenceDeduplicationKey\":\"" + evidence.DeduplicationKey +
                "\",\"canonicalEvidenceId\":\"" + evidence.UtteranceId +
                "\",\"relationshipTargetId\":\"player\",\"expectedMoodRevision\":1," +
                "\"expectedAffinityRevision\":" + revision + ",\"expectedRelationshipRevision\":-1," +
                "\"moodDelta\":0,\"affinityDelta\":" + affinity + ",\"trustDelta\":0," +
                "\"resentmentDelta\":0,\"fearDelta\":0,\"speechActKind\":\"DirectThreat\"," +
                "\"speechTargetPersonId\":\"npc:observer\",\"conversationOption\":\"NoAction\"}";
            NpcExperienceAppraisal result;
            Check.True(NpcExperienceAppraisalContract.TryParseResponse(request, json, out result));
            return result;
        }
    }
}


