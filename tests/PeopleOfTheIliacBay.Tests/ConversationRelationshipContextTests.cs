using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class ConversationRelationshipContextTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Relationship context attaches as immutable snapshot successor", SnapshotSuccessors));
            cases.Add(("Relationship projections keep liking separate from evidence-based reliability", RelationshipFormatting));
            cases.Add(("Large traits cannot evict captured clock and current mood", CompactProjectionPriority));
        }

        private static void SnapshotSuccessors()
        {
            var original = Snapshot(20, "patient, reserved", 3600, new ConversationContextMood(12, 4, 2));
            Check.True(original.DirectedRelationship == null, "Existing constructors must leave optional relationship unknown");
            Check.Equal(3, typeof(ConversationContextSnapshot).GetConstructors().Length);

            var view = Relationship("npc:observer", trustKnown: false, trust: 0, confidence: 0,
                evidenceCount: 0, resentment: 8, fear: 3, revision: 0);
            var attached = original.WithDirectedRelationship(view);
            Check.True(ReferenceEquals(view, attached.DirectedRelationship), "Snapshot must retain exact immutable captured view");
            Check.True(original.DirectedRelationship == null, "Attaching relationship must not mutate parent snapshot");
            Check.Equal(original.Revision + 1, attached.Revision);
            Check.True(attached.IsSuccessorOf(original));

            var dispositionSuccessor = attached.WithCommittedDisposition(-20);
            Check.True(ReferenceEquals(view, dispositionSuccessor.DirectedRelationship),
                "Disposition successor must preserve exact relationship snapshot");
            Check.True(dispositionSuccessor.IsSuccessorOf(attached));
            Check.True(typeof(ConversationContextSnapshot).GetProperty("DirectedRelationship").GetSetMethod(false) == null,
                "Snapshot relationship property must be read-only");

            Check.Throws<ArgumentException>(() => original.WithDirectedRelationship(Relationship(
                "npc:other", false, 0, 0, 0, 0, 0, 0)));
            var ephemeral = Snapshot(0, "patient", 3600, null, stablePerson: false);
            Check.Throws<ArgumentException>(() => ephemeral.WithDirectedRelationship(view));
        }

        private static void RelationshipFormatting()
        {
            var noRecord = Snapshot(65, "warm", 3600, new ConversationContextMood(9, 1, 1));
            string noRecordSocial = ConversationContextFormatter.ForSocialReply(noRecord, 2048);
            Check.True(!noRecordSocial.Contains("reliability trust="),
                "A missing relationship record must remain absent rather than become numeric zero or distrust");

            var unknown = noRecord.WithDirectedRelationship(Relationship("npc:observer", false, 0, 0, 0,
                resentment: 4, fear: 2, revision: 0));
            string unknownText = AllRelationshipProjections(unknown);
            Check.True(unknownText.Contains("reliability trust=unknown"));
            Check.True(unknownText.Contains("not evidence of unreliability"));
            Check.True(unknownText.Contains("resentment=4/100") && unknownText.Contains("interpersonal fear=2/100"));

            var positiveLikingLowTrust = Snapshot(78, "friendly", 3600, new ConversationContextMood(10, 2, 2))
                .WithDirectedRelationship(Relationship("npc:observer", true, 15, 10, 1, 0, 0, 1));
            string warmUnreliable = AllRelationshipProjections(positiveLikingLowTrust);
            Check.True(warmUnreliable.Contains("liking/disposition=78/100"));
            Check.True(warmUnreliable.Contains("reliability trust=15/100"));
            Check.True(warmUnreliable.Contains("from 1 observed outcomes"));

            var negativeLikingHighTrust = Snapshot(-72, "careful", 3600, new ConversationContextMood(-8, 2, 2))
                .WithDirectedRelationship(Relationship("npc:observer", true, 84, 20, 2, 6, 1, 2));
            string dislikedReliable = AllRelationshipProjections(negativeLikingHighTrust);
            Check.True(dislikedReliable.Contains("liking/disposition=-72/100"));
            Check.True(dislikedReliable.Contains("reliability trust=84/100"));
            Check.True(dislikedReliable.Contains("support=20/100 from 2 observed outcomes; not a probability"));
            Check.True(dislikedReliable.Contains("These factors do not prove intent."));
            Check.True(!dislikedReliable.Contains("obedience") && !dislikedReliable.Contains("guilty"));

            var recovering = NpcDirectedRelationship.Create("npc:observer", "player");
            recovering.Revision = 1;
            recovering.LastGameTime = 100;
            recovering.Resentment = 80;
            recovering.HasResentmentRecoveryAnchor = true;
            recovering.ResentmentRecoveryAnchorGameTime = 100;
            recovering.ResentmentRecoveryHalfLifeSeconds = 100;
            var projectedRelationship = recovering.ProjectRecovery(200, null,
                NpcFearRecoveryPolicy.Create(100, 0.70, 1.50),
                NpcResentmentRecoveryPolicy.Create(100, 0.70, 1.50));
            string projectedText = AllRelationshipProjections(Snapshot(0, "steady", 3600,
                new ConversationContextMood(0, 0, 0)).WithDirectedRelationship(projectedRelationship));
            Check.True(projectedText.Contains("resentment=40/100"));
            Check.True(!projectedText.Contains("resentment=80/100"));
        }

        private static void CompactProjectionPriority()
        {
            var snapshot = Snapshot(25, new string('x', 512), 46800, new ConversationContextMood(24, 8, 3))
                .WithDirectedRelationship(Relationship("npc:observer", true, 62, 30, 3, 10, 4, 3));
            string action = ConversationContextFormatter.ForActionVoice(snapshot, 280);
            Check.True(action.Length <= 280);
            Check.True(action.Contains("Captured game clock: 13:00."), "Clock must survive large personality text");
            Check.True(action.Contains("Persistent conversation mood: 24/100"), "Current mood must survive large personality text");
            Check.True(!action.Contains(new string('x', 100)), "Low-priority personality text should yield to clock and mood");
        }

        private static string AllRelationshipProjections(ConversationContextSnapshot snapshot)
        {
            string social = ConversationContextFormatter.ForSocialReply(snapshot, 4096);
            string action = ConversationContextFormatter.ForActionVoice(snapshot, 4096);
            string semantic = ConversationContextFormatter.ForSemanticClassifier(snapshot, 4096);
            Check.True(social.Contains("Relationship toward player:"));
            Check.True(action.Contains("Relationship toward player:"));
            Check.True(semantic.Contains("Relationship toward player:"));
            return social + "\n" + action + "\n" + semantic;
        }

        private static NpcDirectedRelationshipView Relationship(string observer, bool trustKnown, int trust,
            int confidence, int evidenceCount, int resentment, int fear, long revision)
        {
            var state = NpcDirectedRelationship.Create(observer, NpcDirectedRelationship.PlayerTargetId);
            state.TrustKnown = trustKnown;
            state.ReliabilityTrust = trust;
            state.TrustConfidence = confidence;
            state.TrustEvidenceCount = evidenceCount;
            state.Resentment = resentment;
            state.InterpersonalFear = fear;
            state.Revision = revision;
            state.LastGameTime = revision == 0 ? -1 : 10;
            return state.ToView();
        }

        private static ConversationContextSnapshot Snapshot(int disposition, string traits, double now,
            ConversationContextMood mood, bool stablePerson = true)
        {
            string personId = stablePerson ? "npc:observer" : null;
            var binding = new ConversationContextBinding(ConversationActorCategory.Mobile, personId,
                stablePerson ? null : "speaker:temporary", "session:relationship", 1, 2,
                "0123456789abcdef0123456789abcdef", "", "scene:town", 1);
            var clock = Verified("13:00", ConversationContextFactSource.LiveObservation, now);
            var name = Verified("Mira", ConversationContextFactSource.NativeActor, now);
            var role = ConversationContextFact<ConversationDialogueRole>.Verified(
                ConversationDialogueRole.ManagedResident, ConversationContextFactSource.NativeActor, now);
            var scene = Verified("Outside", ConversationContextFactSource.LiveObservation, now);
            var npcScene = Verified("scene:town", ConversationContextFactSource.LiveObservation, now);
            var position = ConversationContextFact<ConversationNpcPosition>.Verified(
                new ConversationNpcPosition(1, 2, 3), ConversationContextFactSource.LiveObservation, now);
            var activity = ConversationContextFact<ConversationNpcActivity>.Verified(
                ConversationNpcActivity.Stationary, ConversationContextFactSource.LiveObservation, now);
            var outdoors = ConversationContextFact<bool>.Verified(false, ConversationContextFactSource.LiveObservation, now);
            var weather = ConversationContextFact<ConversationNpcWeatherObservation>.Unknown();
            var personality = Verified(traits, ConversationContextFactSource.Derived, now);
            return new ConversationContextSnapshot(binding, now, clock, name, role, scene, npcScene, position, activity,
                outdoors, weather, personality, disposition, 7, null,
                Array.Empty<ConversationContextCommitment>(), Array.Empty<ConversationContextSourceHandle>(),
                ConversationContextPersonalState.Unknown(), mood);
        }

        private static ConversationContextFact<string> Verified(string value, ConversationContextFactSource source,
            double now)
        { return ConversationContextFact<string>.Verified(value, source, now); }
    }
}
