using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcExperienceAppraisalTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Speech stage binds exact request and blocks later class substitution", SpeechStageBinding));
            cases.Add(("Indirect speech stage cannot punish the speaker or request consequences", SpeechStageProtectedEffects));
            cases.Add(("Appraisal binds native evidence, state revisions and exact response schema", BindsResponse));
            cases.Add(("Appraisal completion uses the request-bound JSON grammar, not the generic classifier", AppraisalCompletionKeepsBoundGrammar));
            cases.Add(("Appraisal numeric grammar matches prompt and parser bounds", NumericGrammarMatchesParserBounds));
            cases.Add(("Bound speech labels constrain appraisal grammar to parser bounds", BoundSpeechGrammarMatchesParser));
            cases.Add(("New direct hostility labels retain conservative appraisal bounds", NewHostilityLabelsUseConservativeBounds));
            cases.Add(("Native unknown actor permits mood only and gift cannot create trust", BlocksUnsupportedRelationshipEffects));
            cases.Add(("Attributed speech stays a claim and severe threat is bounded", SpeechProposalBounds));
            cases.Add(("Noble consequence choices require native adapter eligibility", ConsequencesRequireEligibility));
            cases.Add(("No-show appraisal requires observer wait evidence", NoShowRequiresWait));
            cases.Add(("Speech context stays in the same observer episode and preserves receipt times", SpeechContextIsBounded));
            cases.Add(("Speech-only appraisal does not fabricate a world event kind", SpeechOnlyEvidence));
            cases.Add(("Motivation context is immutable, bounded and observer-source-bound", MotivationContextIsImmutableAndBounded));
            cases.Add(("Appraisal request projects directed fear at capture time and keeps stored audit value", AppraisalProjectsFearAtCaptureTime));
        }

        private static NpcExperienceAppraisalRequest SpeechStageRequest()
        {
            var evidence = NpcExperienceEvent.FromObservedSpeech("npc:observer", "npc:observer", "speech:receipt",
                "episode:1", "utterance:1", 110, "player");
            return NpcExperienceAppraisalContract.CreateRequest("branch:a", evidence,
                NpcPersonalityGenerator.Generate("npc:observer", 1, NpcSocialBackground.Commoner),
                0, 4, 100, 20, 5, 2, 120, Relationship("player", true), null,
                observedUtterance: Utterance(evidence, "The robber said he would kill you."));
        }

        private static void SpeechStageBinding()
        {
            var request = SpeechStageRequest();
            var speech = NpcSpeechActRequest.ForAppraisal(request);
            NpcSpeechActClassification classification;
            Check.True(NpcSpeechActClassification.TryAccept(speech, "ReportedThreat", out classification));
            var bound = NpcExperienceAppraisalContract.WithSpeechClassification(request, classification);
            Check.Equal(NpcExperienceSpeechActKind.ReportedThreat, bound.SpeechClassification.Kind);
            Check.True(request.SpeechClassification == null);
            Check.Throws<ArgumentException>(() => NpcExperienceAppraisalContract.WithSpeechClassification(SpeechStageRequest(), classification));
            Check.Throws<ArgumentException>(() => NpcExperienceAppraisalContract.WithSpeechClassification(bound, classification));
            Check.True(!NpcSpeechActClassification.TryAccept(speech, "NotApplicable", out classification));
            Check.True(!NpcSpeechActClassification.TryAccept(speech, "3", out classification));
            NpcExperienceAppraisal result;
            Check.True(NpcExperienceAppraisalContract.TryParseResponse(bound, Result(bound, speechAct: "ReportedThreat"), out result));
            Check.True(!NpcExperienceAppraisalContract.TryParseResponse(bound, Result(bound, speechAct: "DirectThreat", target: bound.ObserverId), out result));
            string grammar = NpcExperienceAppraisalContract.ResponseGrammar(bound);
            Check.True(grammar.Contains("speech-act ::= \"\\\"ReportedThreat\\\"\""));
            Check.True(!grammar.Contains("\\\"DirectThreat\\\""));
        }

        private static void SpeechStageProtectedEffects()
        {
            foreach (string label in new[] { "ReportedThreat", "HypotheticalThreat", "AmbiguousThreat", "Other" })
            {
                var request = SpeechStageRequest();
                NpcSpeechActClassification classification;
                Check.True(NpcSpeechActClassification.TryAccept(NpcSpeechActRequest.ForAppraisal(request), label, out classification));
                var bound = NpcExperienceAppraisalContract.WithSpeechClassification(request, classification);
                NpcExperienceAppraisal parsed;
                Check.True(NpcExperienceAppraisalContract.TryParseResponse(bound, Result(bound, mood: -3, speechAct: label), out parsed));
                foreach (string invalid in new[] {
                    Result(bound, affinity: -1, speechAct: label), Result(bound, trust: -1, speechAct: label),
                    Result(bound, resentment: 1, speechAct: label), Result(bound, fear: 1, speechAct: label),
                    Result(bound, speechAct: label, target: bound.ObserverId),
                    Result(bound, speechAct: label, option: "EndConversation") })
                    Check.True(!NpcExperienceAppraisalContract.TryParseResponse(bound, invalid, out parsed));
            }
        }

        private static void AppraisalProjectsFearAtCaptureTime()
        {
            var row = NpcDirectedRelationship.Create("npc:observer", "player");
            row.Revision = 3;
            row.LastGameTime = 100;
            row.InterpersonalFear = 60;
            row.HasFearRecoveryAnchor = true;
            row.FearRecoveryAnchorGameTime = 100;
            row.FearRecoveryHalfLifeSeconds = 100;
            var request = Request(
                Native(NpcExperienceEventKind.AppointmentAttended, NpcResponsibleActorAttribution.Known, "player"),
                row.ToView());

            int expected = NpcFearRecoveryPolicy.ProvisionalDefault.Project(60, 100, 120, 100);
            Check.Equal(expected, request.Relationship.ProjectedInterpersonalFear);
            Check.Equal(60, request.Relationship.StoredInterpersonalFear);
            Check.Equal(120d, request.Relationship.ProjectedAtGameTime);
            Check.True(request.Prompt.Contains(",interpersonalFear=" + expected.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));
        }

        private static void AppraisalCompletionKeepsBoundGrammar()
        {
            NpcExperienceAppraisalRequest request = Request(
                Native(NpcExperienceEventKind.AppointmentAttended, NpcResponsibleActorAttribution.Known, "player"),
                Relationship("player", true));
            LocalDialogueConnection connection = new LocalDialogueConnection("http://127.0.0.1:8080/completion", 1000);
            DialogueCompletion completion = LocalDialogueCompletion.BuildAppraisal(connection, request);
            DialogueCompletion genericClassifier = LocalDialogueCompletion.Classify(
                connection, request.Prompt, string.Empty, null, 512);
            string escapedGrammar = LocalDialogueCompletion.EscapeJson(
                NpcExperienceAppraisalContract.ResponseGrammar(request));
            string escapedPrompt = LocalDialogueCompletion.EscapeJson(request.Prompt);
            string genericGrammar = LocalDialogueCompletion.EscapeJson(
                "root ::= intent \"|\" number \"|\" tone \"|\" social-intent\n" +
                "intent ::= \"social\" | \"topic\" | \"map\" | \"unknown\" | \"action\" | \"appointment\" | \"home\"\n" +
                "number ::= \"-1\" | [0-9]+\n" +
                "tone ::= \"neutral\" | \"polite\" | \"apology\" | \"rude\" | \"insult\" | \"flirt\"\n" +
                "social-intent ::= \"none\" | \"busy_pause\" | \"family_threat\"\n");

            Check.Equal(request.Prompt, completion.Prompt);
            Check.Equal(1024, completion.ResponseTokens);
            Check.True(completion.JsonBody.Contains("\"prompt\":\"" + escapedPrompt + "\""));
            Check.True(completion.JsonBody.Contains("\"grammar\":\"" + escapedGrammar + "\""));
            Check.True(!completion.JsonBody.Contains("\"grammar\":\"" + genericGrammar + "\""));
            Check.Equal(512, genericClassifier.ResponseTokens);
            Check.True(genericClassifier.JsonBody.Contains("\"grammar\":\"" + genericGrammar + "\""));
            Check.True(!genericClassifier.JsonBody.Contains("\"grammar\":\"" + escapedGrammar + "\""));
        }

        private static void NumericGrammarMatchesParserBounds()
        {
            NpcExperienceAppraisalRequest native = NpcExperienceAppraisalContract.CreateRequest("branch:a",
                Native(NpcExperienceEventKind.AppointmentAttended, NpcResponsibleActorAttribution.Known, "player"),
                NpcPersonalityGenerator.Generate("npc:observer", 1, NpcSocialBackground.Commoner),
                0, 4, 100, 20, 5, 2, 120, Relationship("player", true), null);
            string nativeGrammar = NpcExperienceAppraisalContract.ResponseGrammar(native);
            Check.True(nativeGrammar.Contains(IntegerRule("mood-delta", -10, 10), StringComparison.Ordinal));
            Check.True(nativeGrammar.Contains(IntegerRule("affinity-delta", -10, 2), StringComparison.Ordinal));
            Check.True(nativeGrammar.Contains(IntegerRule("trust-delta", 0, 10), StringComparison.Ordinal));
            Check.True(nativeGrammar.Contains(IntegerRule("resentment-delta", -10, 10), StringComparison.Ordinal));
            Check.True(nativeGrammar.Contains(IntegerRule("fear-delta", -10, 10), StringComparison.Ordinal));
            Check.True(!nativeGrammar.Contains("integer ::= \"-\" [0-9]+ | [0-9]+", StringComparison.Ordinal));
            Check.True(native.Prompt.Contains("moodDelta [-10,10]", StringComparison.Ordinal));
            Check.True(native.Prompt.Contains("affinityDelta [-10,2]", StringComparison.Ordinal));
            Check.True(native.Prompt.Contains("trustDelta [0,10]", StringComparison.Ordinal));

            // The old generic integer production admitted mood 15 and affinity 3; the authoritative parser rejects both.
            NpcExperienceAppraisal parsed;
            Check.True(NpcExperienceAppraisalContract.TryParseResponse(native,
                Result(native, mood: 10, affinity: 2, trust: 10, resentment: 10, fear: 10), out parsed));
            Check.True(!NpcExperienceAppraisalContract.TryParseResponse(native, Result(native, mood: 15), out parsed));
            Check.True(!NpcExperienceAppraisalContract.TryParseResponse(native, Result(native, mood: 11), out parsed));
            Check.True(!NpcExperienceAppraisalContract.TryParseResponse(native, Result(native, affinity: 3), out parsed));
            Check.True(!NpcExperienceAppraisalContract.TryParseResponse(native, Result(native, trust: 11), out parsed));

            NpcExperienceAppraisalRequest unknownTarget = Request(
                Native(NpcExperienceEventKind.Injury, NpcResponsibleActorAttribution.Unknown, null), null);
            string unknownTargetGrammar = NpcExperienceAppraisalContract.ResponseGrammar(unknownTarget);
            Check.True(unknownTargetGrammar.Contains(IntegerRule("affinity-delta", 0, 0), StringComparison.Ordinal));
            Check.True(unknownTargetGrammar.Contains(IntegerRule("trust-delta", 0, 0), StringComparison.Ordinal));
            Check.True(unknownTargetGrammar.Contains(IntegerRule("resentment-delta", 0, 0), StringComparison.Ordinal));
            Check.True(unknownTargetGrammar.Contains(IntegerRule("fear-delta", 0, 0), StringComparison.Ordinal));
            Check.True(unknownTarget.Prompt.Contains("relationshipTargetId is empty", StringComparison.Ordinal));
            Check.True(!NpcExperienceAppraisalContract.TryParseResponse(unknownTarget,
                Result(unknownTarget, affinity: 1), out parsed));

            NpcExperienceAppraisalRequest noShow = NpcExperienceAppraisalContract.CreateRequest("branch:a",
                Native(NpcExperienceEventKind.AppointmentNoShow, NpcResponsibleActorAttribution.Known, "player"),
                NpcPersonalityGenerator.Generate("npc:observer", 1, NpcSocialBackground.Commoner),
                0, 4, 100, 20, 5, 2, 120, Relationship("player", true), null, true);
            Check.True(NpcExperienceAppraisalContract.ResponseGrammar(noShow).Contains(IntegerRule("affinity-delta", 0, 0), StringComparison.Ordinal));
            Check.True(noShow.Prompt.Contains("affinityDelta [0,0]", StringComparison.Ordinal));
            Check.True(!NpcExperienceAppraisalContract.TryParseResponse(noShow, Result(noShow, affinity: 1), out parsed));

            NpcExperienceEvent speechEvidence = NpcExperienceEvent.FromAttributedSpeech("npc:observer", "npc:observer",
                "speech:receipt", "episode:1", "utterance:1", NpcExperienceEventKind.StatusOnset, 110, "player");
            NpcExperienceAppraisalRequest speech = NpcExperienceAppraisalContract.CreateRequest("branch:a", speechEvidence,
                NpcPersonalityGenerator.Generate("npc:observer", 1, NpcSocialBackground.Commoner),
                0, 4, 100, 20, 5, 2, 120, Relationship("player", true), null, false,
                Utterance(speechEvidence, "I will kill you."), null, new[] { "npc:family" });
            string speechGrammar = NpcExperienceAppraisalContract.ResponseGrammar(speech);
            Check.True(speechGrammar.Contains(IntegerRule("affinity-delta", -50, 2), StringComparison.Ordinal));
            Check.True(speechGrammar.Contains(IntegerRule("trust-delta", -25, 0), StringComparison.Ordinal));
            Check.True(speechGrammar.Contains(IntegerRule("resentment-delta", -10, 25), StringComparison.Ordinal));
            Check.True(speechGrammar.Contains(IntegerRule("fear-delta", -10, 25), StringComparison.Ordinal));
            Check.True(speech.Prompt.Contains("DirectThreat", StringComparison.Ordinal));
            Check.True(speech.Prompt.Contains("Speech trustDelta can never be positive.", StringComparison.Ordinal));
            Check.True(NpcExperienceAppraisalContract.TryParseResponse(speech,
                Result(speech, affinity: -50, trust: -25, resentment: 25, fear: 25,
                    speechAct: "DirectThreat", target: "npc:observer"), out parsed));
            Check.True(!NpcExperienceAppraisalContract.TryParseResponse(speech,
                Result(speech, affinity: 2, speechAct: "DirectThreat", target: "npc:observer"), out parsed));

            NpcExperienceAppraisalRequest alreadyApplied = NpcExperienceAppraisalContract.CreateRequest("branch:a",
                speechEvidence, NpcPersonalityGenerator.Generate("npc:observer", 1, NpcSocialBackground.Commoner),
                0, 4, 100, 20, 5, 2, 120, Relationship("player", true), null, false,
                Utterance(speechEvidence, "I will kill you."), null, null, null,
                alreadyAppliedEffectsMask: NpcExperienceAppliedEffectsMask.Mood | NpcExperienceAppliedEffectsMask.Affinity);
            string appliedGrammar = NpcExperienceAppraisalContract.ResponseGrammar(alreadyApplied);
            Check.True(appliedGrammar.Contains(IntegerRule("mood-delta", 0, 0), StringComparison.Ordinal));
            Check.True(appliedGrammar.Contains(IntegerRule("affinity-delta", -50, 0), StringComparison.Ordinal));
            Check.True(alreadyApplied.Prompt.Contains("moodDelta [0,0]", StringComparison.Ordinal));
            Check.True(alreadyApplied.Prompt.Contains("affinityDelta [-50,0]", StringComparison.Ordinal));
            Check.True(NpcExperienceAppraisalContract.TryParseResponse(alreadyApplied,
                Result(alreadyApplied, affinity: -50, speechAct: "DirectThreat", target: "npc:observer"), out parsed));
            Check.True(!NpcExperienceAppraisalContract.TryParseResponse(alreadyApplied,
                Result(alreadyApplied, mood: 1, speechAct: "DirectThreat", target: "npc:observer"), out parsed));
        }

        private static void BoundSpeechGrammarMatchesParser()
        {
            NpcExperienceAppraisalRequest unclassified = SpeechStageRequest();
            NpcExperienceAppraisalRequest insult = BindSpeechClassification(unclassified, "DirectInsult");
            string insultGrammar = NpcExperienceAppraisalContract.ResponseGrammar(insult);
            Check.True(insultGrammar.Contains(IntegerRule("affinity-delta", -25, 0), StringComparison.Ordinal));
            Check.True(insultGrammar.Contains(IntegerRule("trust-delta", -10, 0), StringComparison.Ordinal));
            Check.True(insultGrammar.Contains(IntegerRule("resentment-delta", 0, 10), StringComparison.Ordinal));
            Check.True(insultGrammar.Contains(IntegerRule("fear-delta", 0, 10), StringComparison.Ordinal));
            Check.True(insultGrammar.Contains("speech-target ::= \"\\\"\\\"", StringComparison.Ordinal));
            NpcExperienceAppraisal parsed;
            Check.True(NpcExperienceAppraisalContract.TryParseResponse(insult,
                Result(insult, mood: -5, affinity: -25, trust: -10, resentment: 10, fear: 10,
                    speechAct: "DirectInsult", target: insult.ObserverId), out parsed));
            Check.True(NpcExperienceAppraisalContract.TryParseResponse(insult,
                Result(insult, speechAct: "DirectInsult"), out parsed));
            Check.True(!NpcExperienceAppraisalContract.TryParseResponse(insult,
                Result(insult, affinity: -25, speechAct: "DirectInsult"), out parsed));
            Check.True(!NpcExperienceAppraisalContract.TryParseResponse(insult,
                Result(insult, resentment: 11, speechAct: "DirectInsult", target: insult.ObserverId), out parsed));
            Check.True(!NpcExperienceAppraisalContract.TryParseResponse(insult,
                Result(insult, affinity: 1, speechAct: "DirectInsult", target: insult.ObserverId), out parsed));
            Check.True(!NpcExperienceAppraisalContract.TryParseResponse(insult,
                Result(insult, resentment: -1, speechAct: "DirectInsult", target: insult.ObserverId), out parsed));

            NpcExperienceAppraisalRequest threat = BindSpeechClassification(unclassified, "DirectThreat");
            string threatGrammar = NpcExperienceAppraisalContract.ResponseGrammar(threat);
            Check.True(threatGrammar.Contains(IntegerRule("affinity-delta", -50, 0), StringComparison.Ordinal));
            Check.True(threatGrammar.Contains(IntegerRule("trust-delta", -25, 0), StringComparison.Ordinal));
            Check.True(threatGrammar.Contains(IntegerRule("resentment-delta", 0, 25), StringComparison.Ordinal));
            Check.True(threatGrammar.Contains(IntegerRule("fear-delta", 0, 25), StringComparison.Ordinal));
            Check.True(NpcExperienceAppraisalContract.TryParseResponse(threat,
                Result(threat, affinity: -50, trust: -25, resentment: 25, fear: 25,
                    speechAct: "DirectThreat", target: threat.ObserverId), out parsed));
            Check.True(!NpcExperienceAppraisalContract.TryParseResponse(threat,
                Result(threat, fear: -1, speechAct: "DirectThreat", target: threat.ObserverId), out parsed));
        }

        private static NpcExperienceAppraisalRequest BindSpeechClassification(
            NpcExperienceAppraisalRequest request, string label)
        {
            NpcSpeechActClassification classification;
            Check.True(NpcSpeechActClassification.TryAccept(NpcSpeechActRequest.ForAppraisal(request), label, out classification));
            return NpcExperienceAppraisalContract.WithSpeechClassification(request, classification);
        }

        private static void NewHostilityLabelsUseConservativeBounds()
        {
            foreach (string label in new[] { "DirectHarassment", "SevereAbuse", "PersistentAbuse" })
            {
                var request = BindSpeechClassification(SpeechStageRequest(), label);
                string grammar = NpcExperienceAppraisalContract.ResponseGrammar(request);
                Check.True(grammar.Contains(IntegerRule("affinity-delta", -10, 0), StringComparison.Ordinal));
                Check.True(grammar.Contains(IntegerRule("trust-delta", -10, 0), StringComparison.Ordinal));
                Check.True(grammar.Contains(IntegerRule("resentment-delta", 0, 10), StringComparison.Ordinal));
                Check.True(grammar.Contains(IntegerRule("fear-delta", 0, 10), StringComparison.Ordinal));
                NpcExperienceAppraisal appraisal;
                Check.True(NpcExperienceAppraisalContract.TryParseResponse(request,
                    Result(request, affinity: -10, trust: -10, resentment: 10, fear: 10,
                        speechAct: label, target: request.ObserverId), out appraisal));
                Check.True(!NpcExperienceAppraisalContract.TryParseResponse(request,
                    Result(request, affinity: -11, speechAct: label, target: request.ObserverId), out appraisal));
                Check.True(!NpcExperienceAppraisalContract.TryParseResponse(request,
                    Result(request, affinity: 1, speechAct: label, target: request.ObserverId), out appraisal));
            }
        }

        private static string IntegerRule(string name, int minimum, int maximum)
        {
            var values = new List<string>();
            for (int value = minimum; value <= maximum; value++) values.Add("\"" + value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\"");
            return name + " ::= " + string.Join(" | ", values);
        }

        private static void MotivationContextIsImmutableAndBounded()
        {
            const string observer = "npc:observer";
            const string player = "player";
            const string branch = "branch:a";
            NpcMotivationEvidenceRef NeedEvidence(string id, NpcMotivationEvidenceKind kind = NpcMotivationEvidenceKind.ResourceLedger,
                string owner = observer, double time = 900, long revision = 2, string saveBranch = branch)
                => new NpcMotivationEvidenceRef(saveBranch, owner, id, kind, revision, time);

            var clock = new NpcMotivationGameClock(1000, 3,
                NeedEvidence("clock:1000", NpcMotivationEvidenceKind.WorldClock, time: 1000, revision: 3));
            var profile = new NpcMotivationProfileReference(observer, 4,
                NeedEvidence("profile:observer", NpcMotivationEvidenceKind.PersonalityProfile, revision: 4));
            var relationship = new NpcMotivationRelationshipReference(observer, player, 7,
                NeedEvidence("relationship:observer-player", NpcMotivationEvidenceKind.RelationshipLedger, revision: 7));
            var needs = new List<NpcMotivationNeed>
            {
                new NpcMotivationNeed("need:rest", NpcMotivationNeedKind.Rest,
                    NpcMotivationKnowledgeState.Known, 80, 2, NeedEvidence("need:rest-source")),
                new NpcMotivationNeed("need:food-unknown", NpcMotivationNeedKind.Rest,
                    NpcMotivationKnowledgeState.Unknown, null, 1, null)
            };
            var goals = new List<NpcMotivationGoal>
            {
                new NpcMotivationGoal("goal:family", NpcMotivationGoalKind.FamilyCare,
                    NpcMotivationGoalStatus.Active, NpcMotivationPriorityLevel.Urgent,
                    NpcMotivationKnowledgeState.Known, 5, NeedEvidence("goal:family-source", NpcMotivationEvidenceKind.AuthoredRecord, revision: 5))
            };
            var priorities = new List<NpcMotivationPriority>
            {
                new NpcMotivationPriority("priority:family", "goal:family", NpcMotivationPriorityKind.Family,
                    1, NpcMotivationKnowledgeState.Known, 1,
                    NeedEvidence("priority:family-source", NpcMotivationEvidenceKind.AuthoredRecord, revision: 1))
            };
            var capabilities = new List<NpcMotivationCapabilityConstraint>
            {
                new NpcMotivationCapabilityConstraint("capability:ranged", NpcMotivationCapabilityKind.RangedCombat,
                    NpcMotivationAvailability.Unavailable, NpcMotivationKnowledgeState.Known, 2,
                    NeedEvidence("capability:ranged-source", NpcMotivationEvidenceKind.NativeObservation))
            };
            var resources = new List<NpcMotivationResourceConstraint>
            {
                new NpcMotivationResourceConstraint("resource:arrows", NpcMotivationResourceKind.Ammunition,
                    NpcMotivationKnowledgeState.Known, 0, 1, 2,
                    NeedEvidence("resource:arrows-source", NpcMotivationEvidenceKind.ResourceLedger))
            };
            var threats = new List<NpcMotivationThreatObservation>
            {
                new NpcMotivationThreatObservation("risk:hostile-nearby", NpcMotivationThreatKind.HostileActor,
                    NpcMotivationKnowledgeState.Known, 70, 3,
                    NeedEvidence("risk:hostile-source", NpcMotivationEvidenceKind.NativeObservation, revision: 3))
            };
            var duties = new List<NpcMotivationDuty>
            {
                new NpcMotivationDuty("duty:family", NpcMotivationDutyKind.Family,
                    NpcMotivationDutyScope.Household | NpcMotivationDutyScope.Travel,
                    NpcMotivationDutyStatus.Active, 800, 1500, 4,
                    NeedEvidence("duty:family-source", NpcMotivationEvidenceKind.CommitmentLedger, revision: 4))
            };

            NpcMotivationContextSnapshot snapshot = NpcMotivationContextSnapshot.Capture("branch:a", observer, 9,
                clock, profile, relationship, needs, goals, priorities, capabilities, resources, threats, duties);
            needs.Clear(); goals.Clear(); priorities.Clear(); capabilities.Clear(); resources.Clear(); threats.Clear(); duties.Clear();
            Check.Equal(9L, snapshot.Revision);
            Check.Equal(1000d, snapshot.CapturedGameTime);
            Check.Equal(2, snapshot.Needs.Count);
            Check.Equal(NpcMotivationKnowledgeState.Unknown, snapshot.Needs[1].KnowledgeState);
            Check.Equal(NpcMotivationKnowledgeState.Known, snapshot.Resources[0].KnowledgeState);
            Check.Equal(NpcMotivationDutyScope.Household | NpcMotivationDutyScope.Travel, snapshot.Duties[0].Scope);
            Check.Equal(player, snapshot.Relationship.TargetId);
            Check.Throws<NotSupportedException>(() => ((IList<NpcMotivationNeed>)snapshot.Needs).Clear());
            string projection = NpcMotivationContextFormatter.ForModels(snapshot);
            Check.Equal(projection, NpcMotivationContextFormatter.ForModels(snapshot));
            Check.True(projection.Contains("state=Unknown revision=1 source=none intensity=unknown"));
            Check.True(projection.Contains("intensity=80"));
            Check.True(projection.Contains("commitmentsAndDuties: id=duty:family"));
            Check.True(projection.Contains("not action authorization"));
            string compactProjection = NpcMotivationContextFormatter.ForModels(snapshot, 1024);
            Check.True(compactProjection.Length <= 1024);
            Check.True(compactProjection.Contains("omittedFacts="));

            Check.Throws<ArgumentException>(() => new NpcMotivationNeed("need:unknown-with-value",
                NpcMotivationNeedKind.Rest, NpcMotivationKnowledgeState.Unknown, 20, 0, null));
            Check.Throws<ArgumentException>(() => new NpcMotivationNeed("need:claimed-native",
                NpcMotivationNeedKind.Rest, NpcMotivationKnowledgeState.AttributedClaim, 20, 0,
                NeedEvidence("claim:rest", NpcMotivationEvidenceKind.ResourceLedger, revision: 0)));
            Check.Throws<ArgumentException>(() => new NpcMotivationNeed("need:claim-as-fact",
                NpcMotivationNeedKind.Rest, NpcMotivationKnowledgeState.Known, 20, 2,
                NeedEvidence("claim:rest-known", NpcMotivationEvidenceKind.ConversationReceipt)));
            Check.Throws<ArgumentException>(() => new NpcMotivationNeed("need:revision-mismatch",
                NpcMotivationNeedKind.Rest, NpcMotivationKnowledgeState.Known, 20, 3,
                NeedEvidence("need:revision-source", revision: 2)));
            Check.Throws<ArgumentException>(() => NpcMotivationContextSnapshot.Capture("branch:a", observer, 9,
                clock, profile, relationship, new[] {
                    new NpcMotivationNeed("need:dup", NpcMotivationNeedKind.Rest, NpcMotivationKnowledgeState.Known, 20, 0, NeedEvidence("need:dup-a", revision: 0)),
                    new NpcMotivationNeed("need:dup", NpcMotivationNeedKind.Rest, NpcMotivationKnowledgeState.Known, 30, 0, NeedEvidence("need:dup-b", revision: 0))
                }, null, null, null, null, null, null));
            Check.Throws<ArgumentException>(() => NpcMotivationContextSnapshot.Capture("branch:a", observer, 9,
                clock, profile, relationship, new[] {
                    new NpcMotivationNeed("need:foreign-observer", NpcMotivationNeedKind.Rest, NpcMotivationKnowledgeState.Known, 20, 2,
                        NeedEvidence("need:foreign", owner: "npc:other"))
                }, null, null, null, null, null, null));
            Check.Throws<ArgumentException>(() => NpcMotivationContextSnapshot.Capture(branch, observer, 9,
                clock, profile, relationship, new[] {
                    new NpcMotivationNeed("need:foreign-branch", NpcMotivationNeedKind.Rest, NpcMotivationKnowledgeState.Known, 20, 2,
                        NeedEvidence("need:other-branch", saveBranch: "branch:b"))
                }, null, null, null, null, null, null));
            var future = new NpcMotivationNeed("need:future", NpcMotivationNeedKind.Rest, NpcMotivationKnowledgeState.Known, 20, 2,
                NeedEvidence("need:future-source", time: 1001));
            Check.Throws<ArgumentException>(() => NpcMotivationContextSnapshot.Capture(branch, observer, 9,
                clock, profile, relationship, new[] { future }, null, null, null, null, null, null));
        }

        private static void BindsResponse()
        {
            var evidence = Native(NpcExperienceEventKind.Healing, NpcResponsibleActorAttribution.Known, "player");
            var relation = Relationship("player", false);
            var request = Request(evidence, relation);
            string json = Result(request, mood: 2);
            NpcExperienceAppraisal result;
            Check.True(NpcExperienceAppraisalContract.TryParseResponse(request, json, out result));
            Check.True(NpcExperienceAppraisalContract.TryParseResponse(request, " \n" + json + " \t", out result));
            Check.Equal("branch:a", result.SaveBranchId);
            Check.Equal(evidence.DeduplicationKey, result.EvidenceDeduplicationKey);
            Check.Equal(evidence.OutcomeId, result.CanonicalEvidenceId);
            Check.Equal(4L, result.ExpectedMoodRevision);
            Check.Equal(6L, result.ExpectedRelationshipRevision);
            Check.Equal(110d, result.ObservedGameTime);
            Check.Equal(120d, result.CapturedGameTime);
            Check.True(!NpcExperienceAppraisalContract.TryParseResponse(request,
                json.Replace("\"saveBranchId\":\"branch:a\"", "\"saveBranchId\":\"branch:b\""), out result));
            Check.True(!NpcExperienceAppraisalContract.TryParseResponse(request,
                json.Substring(0, json.Length - 1) + ",\"extra\":0}", out result));
            Check.True(!NpcExperienceAppraisalContract.TryParseResponse(request,
                json.Substring(0, json.Length - 1) + ",\"moodDelta\":2}", out result));
        }

        private static void BlocksUnsupportedRelationshipEffects()
        {
            var unknown = Request(Native(NpcExperienceEventKind.Injury, NpcResponsibleActorAttribution.Unknown, null), null);
            NpcExperienceAppraisal result;
            Check.True(NpcExperienceAppraisalContract.TryParseResponse(unknown, Result(unknown, mood: -6), out result));
            Check.True(!NpcExperienceAppraisalContract.TryParseResponse(unknown, Result(unknown, fear: 2), out result));

            var gift = Request(Native(NpcExperienceEventKind.MoneyReceived, NpcResponsibleActorAttribution.Known, "player"), Relationship("player", true));
            Check.True(NpcExperienceAppraisalContract.TryParseResponse(gift, Result(gift, mood: 3, affinity: 2), out result));
            Check.True(!NpcExperienceAppraisalContract.TryParseResponse(gift, Result(gift, trust: 1), out result));
            Check.True(!NpcExperienceAppraisalContract.TryParseResponse(gift, Result(gift, affinity: 6), out result));
            var noRowGift = Request(Native(NpcExperienceEventKind.MoneyReceived, NpcResponsibleActorAttribution.Known, "player"), null);
            Check.True(NpcExperienceAppraisalContract.TryParseResponse(noRowGift, Result(noRowGift, affinity: 2), out result));
            Check.True(!NpcExperienceAppraisalContract.TryParseResponse(noRowGift, Result(noRowGift, trust: 1), out result));

            var fulfillment = Request(Native(NpcExperienceEventKind.ObligationFulfilled, NpcResponsibleActorAttribution.Known, "player"), Relationship("player", true));
            Check.True(NpcExperienceAppraisalContract.TryParseResponse(fulfillment, Result(fulfillment, trust: 1), out result));
        }

        private static void SpeechProposalBounds()
        {
            var evidence = NpcExperienceEvent.FromAttributedSpeech("npc:noble", "npc:noble", "speech:receipt",
                "episode:1", "utterance:1", NpcExperienceEventKind.StatusOnset, 110, "player");
            var profile = NpcPersonalityGenerator.Generate("npc:noble", 22, NpcSocialBackground.Noble);
            var relation = Relationship("player", true, "npc:noble");
            var request = NpcExperienceAppraisalContract.CreateRequest("branch:a", evidence, profile,
                -4, 4, 100, 30, 5, 2, 120, relation, null, false,
                Utterance(evidence, "I will kill you."), null, new[] { "npc:family" }, "duke", null);
            Check.True(request.Prompt.Contains("observedUtterance=\"I will kill you.\""));
            Check.True(request.Prompt.Contains("knownSpeakerRankId=\"duke\""));
            Check.True(request.Prompt.Contains("observerBackground=Noble"));
            Check.True(request.Prompt.Contains("Words said do not prove a future plan or action."));
            NpcExperienceAppraisal result;
            Check.True(NpcExperienceAppraisalContract.TryParseResponse(request,
                Result(request, affinity: -50, trust: -25, resentment: 25, fear: 25,
                    speechAct: "DirectThreat", target: "npc:noble"), out result));
            Check.True(!NpcExperienceAppraisalContract.TryParseResponse(request,
                Result(request, affinity: -51, speechAct: "DirectThreat", target: "npc:noble"), out result));
            Check.True(!NpcExperienceAppraisalContract.TryParseResponse(request,
                Result(request, affinity: -30, speechAct: "QuotedThreat", target: "npc:noble"), out result));
            Check.True(!NpcExperienceAppraisalContract.TryParseResponse(request,
                Result(request, trust: 1, speechAct: "DirectThreat", target: "npc:noble"), out result));
            Check.True(!NpcExperienceAppraisalContract.TryParseResponse(request,
                Result(request, affinity: 1, speechAct: "DirectThreat", target: "npc:noble"), out result));
        }

        private static void ConsequencesRequireEligibility()
        {
            var evidence = NpcExperienceEvent.FromAttributedSpeech("npc:noble", "npc:noble", "speech:receipt",
                "episode:1", "utterance:2", NpcExperienceEventKind.StatusOnset, 110, "player");
            var profile = NpcPersonalityGenerator.Generate("npc:noble", 22, NpcSocialBackground.Noble);
            var request = NpcExperienceAppraisalContract.CreateRequest("branch:a", evidence, profile,
                0, 1, 100, 0, 1, 0, 120, null, null, false, Utterance(evidence, "Leave at once."),
                null, null, "duke", new[] { NpcExperienceConversationOption.EndConversation });
            NpcExperienceAppraisal result;
            Check.True(!NpcExperienceAppraisalContract.TryParseResponse(request,
                Result(request, speechAct: "DirectInsult", target: "npc:noble", option: "RequestNearbyGuard"), out result));
            Check.True(NpcExperienceAppraisalContract.TryParseResponse(request,
                Result(request, speechAct: "DirectInsult", target: "npc:noble", option: "EndConversation"), out result));
            Check.Equal(NpcExperienceConversationOption.EndConversation, result.ConversationOption);
            Check.True(request.Prompt.Contains("Never invent legal charges, crime facts, quest outcomes"));
        }

        private static void NoShowRequiresWait()
        {
            var evidence = Native(NpcExperienceEventKind.AppointmentNoShow, NpcResponsibleActorAttribution.Known, "player");
            Check.Throws<ArgumentException>(() => Request(evidence, Relationship("player", true)));
            var request = NpcExperienceAppraisalContract.CreateRequest("branch:a", evidence,
                NpcPersonalityGenerator.Generate("npc:observer", 1, NpcSocialBackground.Commoner),
                0, 1, 100, 0, 1, 2, 120, Relationship("player", true), null, true);
            Check.True(request.Prompt.Contains("observedWait=true"));
        }

        private static void SpeechContextIsBounded()
        {
            var evidence = NpcExperienceEvent.FromAttributedSpeech("npc:observer", "npc:observer", "speech:current",
                "episode:1", "utterance:1", NpcExperienceEventKind.StatusOnset, 110, "player");
            var prior = NpcObservedConversationTurn.Create("npc:observer", "episode:1", "npc:observer",
                "speech:prior", "We are discussing a play.", 109);
            var request = NpcExperienceAppraisalContract.CreateRequest("branch:a", evidence,
                NpcPersonalityGenerator.Generate("npc:observer", 1, NpcSocialBackground.Commoner),
                0, 1, 100, 0, 1, 0, 120, null, null, false, Utterance(evidence, "I will kill you."), new[] { prior });
            Check.True(request.Prompt.Contains("speech:prior"));
            Check.True(request.Prompt.Contains("observedGameTime=109"));
            Check.Throws<ArgumentException>(() => NpcExperienceAppraisalContract.CreateRequest("branch:a", evidence,
                NpcPersonalityGenerator.Generate("npc:observer", 1, NpcSocialBackground.Commoner),
                0, 1, 100, 0, 1, 0, 120, null, null, false, Utterance(evidence, "I will kill you."),
                new[] { NpcObservedConversationTurn.Create("npc:observer", "episode:other", "npc:observer", "speech:prior", "text", 109) }));
            Check.Throws<ArgumentException>(() => NpcExperienceAppraisalContract.CreateRequest("branch:a", evidence,
                NpcPersonalityGenerator.Generate("npc:observer", 1, NpcSocialBackground.Commoner),
                0, 1, 100, 0, 1, 0, 120, null, null, false,
                NpcObservedConversationTurn.Create("npc:observer", "episode:1", "player", "speech:wrong", "I will kill you.", 110)));
        }

        private static void SpeechOnlyEvidence()
        {
            var evidence = NpcExperienceEvent.FromObservedSpeech("npc:observer", "npc:observer", "speech:receipt",
                "episode:1", "utterance:only-speech", 110, "player");
            var request = NpcExperienceAppraisalContract.CreateRequest("branch:a", evidence,
                NpcPersonalityGenerator.Generate("npc:observer", 1, NpcSocialBackground.Commoner),
                0, 1, 100, 0, 1, 0, 120, Relationship("player", true), null, false,
                Utterance(evidence, "I will kill you."));
            Check.Equal("utterance:only-speech", request.CanonicalEvidenceId);
            Check.Equal("player", request.RelationshipTargetId);
            Check.True(request.Prompt.Contains("kind=ObservedSpeech"));
            Check.True(request.Prompt.Contains("observedUtterance=\"I will kill you.\""));
            NpcExperienceAppraisal result;
            Check.True(NpcExperienceAppraisalContract.TryParseResponse(request,
                Result(request, speechAct: "DirectThreat", target: "npc:observer"), out result));
            Check.Throws<ArgumentException>(() => NpcExperienceAppraisalContract.CreateRequest("branch:a", evidence,
                NpcPersonalityGenerator.Generate("npc:observer", 1, NpcSocialBackground.Commoner),
                0, 1, 100, 0, 1, 0, 120, Relationship("player", true), null, false,
                NpcObservedConversationTurn.Create("npc:observer", "episode:1", "another-speaker", "speech:receipt",
                    "I will kill you.", 110)));
            Check.Throws<ArgumentException>(() => NpcExperienceAppraisalContract.CreateRequest("branch:a", evidence,
                NpcPersonalityGenerator.Generate("npc:observer", 1, NpcSocialBackground.Commoner),
                0, 1, 100, 0, 1, 0, 120, Relationship("player", true), null, false,
                NpcObservedConversationTurn.Create("npc:observer", "episode:1", "player", "speech:other-receipt",
                    "I will kill you.", 110)));
            Check.Throws<ArgumentException>(() => NpcExperienceAppraisalContract.CreateRequest("branch:a", evidence,
                NpcPersonalityGenerator.Generate("npc:observer", 1, NpcSocialBackground.Commoner),
                0, 1, 100, 0, 1, 0, 120, Relationship("player", true), null, false,
                NpcObservedConversationTurn.Create("npc:observer", "episode:1", "player", "speech:receipt",
                    "I will kill you.", 111)));
        }

        private static NpcObservedConversationTurn Utterance(NpcExperienceEvent evidence, string text)
        {
            return NpcObservedConversationTurn.Create(evidence.ObserverId, evidence.EpisodeId,
                evidence.AttributedSpeakerId, evidence.ReceiptId, text, evidence.ObservedGameTime);
        }

        private static NpcExperienceAppraisalRequest Request(NpcExperienceEvent evidence, NpcDirectedRelationshipView relation)
        {
            return NpcExperienceAppraisalContract.CreateRequest("branch:a", evidence,
                NpcPersonalityGenerator.Generate("npc:observer", 1, NpcSocialBackground.Commoner),
                0, 4, 100, 20, 5, 2, 120, relation, null);
        }

        private static NpcExperienceEvent Native(NpcExperienceEventKind kind, NpcResponsibleActorAttribution attribution, string actor)
        {
            return NpcExperienceEvent.FromNativeOutcome("npc:observer", "npc:observer", "receipt:1",
                "episode:1", "outcome:1", kind, 110, attribution, actor);
        }

        private static NpcDirectedRelationshipView Relationship(string target, bool trustKnown, string observer = "npc:observer")
        {
            var row = NpcDirectedRelationship.Create(observer, target);
            if (trustKnown)
            {
                row.TrustKnown = true; row.ReliabilityTrust = 50; row.TrustEvidenceCount = 1; row.TrustConfidence = 10;
            }
            row.Revision = 6; row.LastGameTime = 100;
            return row.ToView();
        }

        private static string Result(NpcExperienceAppraisalRequest request, int mood = 0, int affinity = 0,
            int trust = 0, int resentment = 0, int fear = 0, string speechAct = "NotApplicable",
            string target = "", string option = "NoAction")
        {
            return "{\"schemaVersion\":" + NpcExperienceAppraisalContract.SchemaVersion +
                ",\"policyVersion\":" + NpcExperienceAppraisalContract.PolicyVersion +
                ",\"saveBranchId\":\"" + request.SaveBranchId + "\",\"observerId\":\"" + request.ObserverId +
                "\",\"subjectPersonId\":\"" + request.SubjectPersonId + "\",\"evidenceDeduplicationKey\":\"" + request.EvidenceDeduplicationKey +
                "\",\"canonicalEvidenceId\":\"" + request.CanonicalEvidenceId + "\",\"relationshipTargetId\":\"" + (request.RelationshipTargetId ?? "") +
                "\",\"expectedMoodRevision\":" + request.ExpectedMoodRevision + ",\"expectedAffinityRevision\":" + request.ExpectedAffinityRevision +
                ",\"expectedRelationshipRevision\":" + request.ExpectedRelationshipRevision + ",\"moodDelta\":" + mood +
                ",\"affinityDelta\":" + affinity + ",\"trustDelta\":" + trust + ",\"resentmentDelta\":" + resentment +
                ",\"fearDelta\":" + fear + ",\"speechActKind\":\"" + speechAct + "\",\"speechTargetPersonId\":\"" + target +
                "\",\"conversationOption\":\"" + option + "\"}";
        }
    }
}
