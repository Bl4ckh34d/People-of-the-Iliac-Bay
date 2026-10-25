using System;
using System.Collections.Generic;
using System.Globalization;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcExperienceSharedContextTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("event appraisal captures detached same-actor motivation and epistemic knowledge", CaptureSharedContext));
            cases.Add(("event appraisal rejects cross-actor shared context", RejectCrossActorContext));
            cases.Add(("event appraisal remains stale when captured motivation or knowledge changes", RejectStaleContext));
            cases.Add(("event appraisal commits when captured motivation and knowledge remain current", AcceptUnchangedContext));
        }

        private static void CaptureSharedContext()
        {
            var store = NewStore(out string actor);
            AddNeed(store, actor, "need:food-initial", 12, 64);
            int initialIntensity = store.ReadNpcMotivationState(actor).Needs[0].Intensity.Value;
            AddKnowledge(store, actor, "topic:home", "north-gate", 11);
            NpcExperienceAppraisalTicket ticket = Capture(store, actor, "shared-capture");

            Check.True(ticket.Request.MotivationContext != null, "capture should include observer motivation");
            Check.Equal(actor, ticket.Request.MotivationContext.ObserverId);
            Check.Equal(ticket.Request.SaveBranchId, ticket.Request.MotivationContext.SaveBranchId);
            Check.Equal(2L, ticket.Request.MotivationContext.Revision);
            Check.Equal(initialIntensity, ticket.Request.MotivationContext.Needs[0].Intensity.Value);
            Check.True(ticket.Request.KnowledgeSnapshot != null, "capture should include observer knowledge");
            Check.Equal(actor, ticket.Request.KnowledgeSnapshot.ActorId);
            Check.Equal(1L, ticket.Request.KnowledgeSnapshot.Revision);
            Check.True(ticket.Request.KnowledgeSnapshot.TryGet("topic:home", out NpcKnowledgeFactView known), "captured knowledge should contain native topic");
            Check.Equal("north-gate", known.Statement.ValueId);
            Check.True(!ticket.Request.KnowledgeSnapshot.TryGet("topic:unknown", out _), "an absent topic must remain unknown");
            Check.True(ticket.Request.Prompt.Contains("motivationSnapshot="), "prompt should include bounded motivation projection");
            Check.True(ticket.Request.Prompt.Contains("knowledgeSnapshot actor=\"" + actor + "\""), "prompt should bind knowledge actor");
            Check.True(ticket.Request.Prompt.Contains("topic:home:VerifiedTruth"), "prompt should carry epistemic topic label");
            Check.True(ticket.Request.Prompt.Contains("missing topics remain unknown"), "prompt should preserve unknown-topic rule");

            AddNeed(store, actor, "need:food-update", 14, 25);
            AddKnowledge(store, actor, "topic:home", "south-gate", 15);
            Check.Equal(initialIntensity, ticket.Request.MotivationContext.Needs[0].Intensity.Value);
            Check.True(store.ReadNpcMotivationState(actor).Needs[0].Intensity.Value != initialIntensity);
            Check.True(ticket.Request.KnowledgeSnapshot.TryGet("topic:home", out known));
            Check.Equal("north-gate", known.Statement.ValueId);
            Check.Equal(2L, ticket.Request.ExpectedMotivationRevision);
            Check.Equal(1L, ticket.Request.ExpectedKnowledgeRevision);
        }

        private static void RejectCrossActorContext()
        {
            var store = NewStore(out string actor);
            string branch = "branch:shared";
            var profile = NpcPersonalityGenerator.Generate(actor, 7, NpcSocialBackground.Unknown);
            var evidence = Event(actor, "cross-context");
            var foreignMotivation = Motivation("npc:other", branch, 10, 3);
            var foreignKnowledge = NpcKnowledgeLedger.Create("npc:other", "scope:shared");
            NpcKnowledgeSnapshot foreignKnowledgeSnapshot;
            Check.True(NpcKnowledgeLedger.TryCapture(foreignKnowledge, "npc:other", "scope:shared", out foreignKnowledgeSnapshot));

            Check.Throws<ArgumentException>(() => NpcExperienceAppraisalContract.CreateRequest(branch, evidence,
                profile, 0, 0, 0, 0, 0, 0, 10, null, null, motivationContext: foreignMotivation));
            Check.Throws<ArgumentException>(() => NpcExperienceAppraisalContract.CreateRequest(branch, evidence,
                profile, 0, 0, 0, 0, 0, 0, 10, null, null, knowledgeSnapshot: foreignKnowledgeSnapshot));
        }

        private static void RejectStaleContext()
        {
            var store = NewStore(out string actor);
            AddNeed(store, actor, "need:food-initial", 12, 64);
            AddKnowledge(store, actor, "topic:home", "north-gate", 11);
            NpcExperienceAppraisalTicket ticket = Capture(store, actor, "stale-context");
            string response = Response(ticket.Request);

            AddNeed(store, actor, "need:food-update", 14, 25);
            Check.True(!store.TryCommitNpcExperienceAppraisal(ticket, response), "changed motivation revision must stale the response");
            Check.Equal(1, store.ReadPendingNpcExperiences(actor, 10).Count);

            Check.True(store.TryCaptureNpcExperienceAppraisal(actor, 20, out NpcExperienceAppraisalTicket current),
                "Recapture the same pending experience after the motivation revision changed.");
            response = Response(current.Request);
            AddKnowledge(store, actor, "topic:home", "south-gate", 15);
            Check.True(!store.TryCommitNpcExperienceAppraisal(current, response), "changed knowledge revision must stale the response");
            Check.Equal(1, store.ReadPendingNpcExperiences(actor, 10).Count);
        }

        private static void AcceptUnchangedContext()
        {
            var store = NewStore(out string actor);
            NpcExperienceAppraisalTicket ticket = Capture(store, actor, "unchanged-context");
            Check.True(ticket.Request.MotivationContext != null, "known actor should receive an empty-or-populated motivation snapshot");
            Check.True(ticket.Request.KnowledgeSnapshot != null, "known actor with no knowledge ledger should receive an empty unknown snapshot");
            Check.True(store.TryCommitNpcExperienceAppraisal(ticket, Response(ticket.Request)),
                "unchanged captured snapshots should permit a valid zero-effect appraisal");
            Check.Equal(0, store.ReadPendingNpcExperiences(actor, 10).Count);
        }

        private static WorldStateStore NewStore(out string actor)
        {
            var store = new WorldStateStore(9123);
            var adult = store.GetOrCreate(10, new[] { 20 }, 1).Households[0].Residents
                .Find(person => person.AgeGroup == ResidentAgeGroup.Adult);
            Check.True(adult != null, "fixture requires an adult resident");
            actor = adult.Id;
            return store;
        }

        private static void AddNeed(WorldStateStore store, string actor, string operation, double time, int intensity)
        {
            var previous = store.ReadNpcVitals(actor, NpcArchetype.Warrior);
            int missingHealth = Math.Max(1, (int)Math.Round(previous.MaximumHealth * intensity / 100.0));
            int currentHealth = Math.Max(1, previous.MaximumHealth - missingHealth);
            Check.True(store.TryCaptureNpcVitals(actor, NpcArchetype.Warrior, currentHealth,
                previous.CurrentMagicka, time, previous.Revision), "fixture injury must be saved: " + operation);
            Check.True(NpcHealthNeedLifecycle.Observe(store, actor, NpcArchetype.Warrior, time),
                "fixture motivation must derive from saved native injury: " + operation);
        }

        private static void AddKnowledge(WorldStateStore store, string actor, string topic, string value, double time)
        {
            Check.True(store.TryCaptureNpcKnowledgeWrite(actor, out NpcKnowledgeWriteTicket ticket));
            long sourceRevision = ticket.ExpectedRevision + 1;
            var update = NpcKnowledgeUpdate.FromNativeEvidence(actor, "entry:" + topic + ":r" + sourceRevision.ToString(CultureInfo.InvariantCulture), topic, value,
                NpcDisclosureProtection.Public, NpcKnowledgeEvidenceOrigin.NativeObservation,
                "source:scene", "receipt:" + topic + ":" + time.ToString(CultureInfo.InvariantCulture),
                sourceRevision, time);
            Check.True(store.TryCommitNpcKnowledge(ticket, update),
                "fixture knowledge update must use a fresh immutable entry and receipt for " + topic + " at revision " + sourceRevision);
        }

        private static NpcExperienceAppraisalTicket Capture(WorldStateStore store, string actor, string suffix)
        {
            Check.True(HistoricalNpcExperienceFixture.ImportHistoricalNativeEvent(store, Event(actor, suffix)), "historical fixture event should import: " + suffix);
            Check.True(store.TryCaptureNpcExperienceAppraisal(actor, 20, out NpcExperienceAppraisalTicket ticket),
                "fixture appraisal capture should succeed: " + suffix);
            return ticket;
        }

        private static NpcExperienceEvent Event(string actor, string suffix)
        {
            return NpcExperienceEvent.FromNativeOutcome(actor, actor, "receipt:" + suffix,
                "episode:" + suffix, "outcome:" + suffix, NpcExperienceEventKind.Injury, 10,
                NpcResponsibleActorAttribution.Unknown, null,
                NpcExperienceImpactDetails.ForHealthEpisode(90, 80, 100, 10, 0, 1));
        }

        private static NpcMotivationContextSnapshot Motivation(string actor, string branch, double time, long revision)
        {
            long clockRevision = BitConverter.DoubleToInt64Bits(time);
            var clockEvidence = new NpcMotivationEvidenceRef(branch, actor, "clock:test", NpcMotivationEvidenceKind.WorldClock,
                clockRevision, time);
            var profileEvidence = new NpcMotivationEvidenceRef(branch, actor, "profile:test", NpcMotivationEvidenceKind.PersonalityProfile,
                1, time);
            return NpcMotivationContextSnapshot.Capture(branch, actor, revision,
                new NpcMotivationGameClock(time, clockRevision, clockEvidence),
                new NpcMotivationProfileReference(actor, 1, profileEvidence), null, null, null, null, null, null, null, null);
        }

        private static string Response(NpcExperienceAppraisalRequest request)
        {
            return "{\"schemaVersion\":" + NpcExperienceAppraisalContract.SchemaVersion.ToString(CultureInfo.InvariantCulture) +
                ",\"policyVersion\":" + NpcExperienceAppraisalContract.PolicyVersion.ToString(CultureInfo.InvariantCulture) +
                ",\"saveBranchId\":\"" + request.SaveBranchId + "\",\"observerId\":\"" + request.ObserverId +
                "\",\"subjectPersonId\":\"" + request.SubjectPersonId + "\",\"evidenceDeduplicationKey\":\"" + request.EvidenceDeduplicationKey +
                "\",\"canonicalEvidenceId\":\"" + request.CanonicalEvidenceId + "\",\"relationshipTargetId\":\"\",\"expectedMoodRevision\":" +
                request.ExpectedMoodRevision.ToString(CultureInfo.InvariantCulture) + ",\"expectedAffinityRevision\":" +
                request.ExpectedAffinityRevision.ToString(CultureInfo.InvariantCulture) + ",\"expectedRelationshipRevision\":" +
                request.ExpectedRelationshipRevision.ToString(CultureInfo.InvariantCulture) +
                ",\"moodDelta\":0,\"affinityDelta\":0,\"trustDelta\":0,\"resentmentDelta\":0,\"fearDelta\":0," +
                "\"speechActKind\":\"NotApplicable\",\"speechTargetPersonId\":\"\",\"conversationOption\":\"NoAction\"}";
        }
    }
}
