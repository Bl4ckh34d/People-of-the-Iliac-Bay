using System;
using System.Collections.Generic;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcPresentationStateTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("NPC presentation capture rejects invalid identity and time", RejectsInvalidInput));
            cases.Add(("NPC presentation capture keeps mood without a saved profile", KeepsMoodWithoutProfile));
            cases.Add(("NPC presentation capture freezes timed projections and later state changes", FreezesTimedProjection));
            cases.Add(("NPC presentation contact fear affects fear projection without changing relationship state", ContactFearPreservesRelationship));
            cases.Add(("NPC presentation capture uses projected resentment after game-time decay", CapturesProjectedResentment));
            cases.Add(("NPC presentation delivery context copies captured values and saved traits", DeliveryUsesCapturedValues));
        }

        private static void RejectsInvalidInput()
        {
            var store = new WorldStateStore(42);
            Check.True(NpcPresentationState.Capture(null, "person", 0, false) == null);
            Check.True(NpcPresentationState.Capture(store, null, 0, false) == null);
            Check.True(NpcPresentationState.Capture(store, "  ", 0, false) == null);
            Check.True(NpcPresentationState.Capture(store, new string('x', 241), 0, false) == null);
            Check.True(NpcPresentationState.Capture(store, "person\n1", 0, false) == null);
            Check.True(NpcPresentationState.Capture(store, "person", double.NaN, false) == null);
            Check.True(NpcPresentationState.Capture(store, "person", double.PositiveInfinity, false) == null);
            Check.True(NpcPresentationState.Capture(store, "person", -1, false) == null);
        }

        private static void KeepsMoodWithoutProfile()
        {
            var store = new WorldStateStore(42);
            long sequence;
            Check.True(store.TryCommitAcceptedSocialExchange("npc:unknown-presentation", 0, 1, 12,
                "day", "place", "hello", "hello", out sequence));

            string before = Serialize(store.Snapshot());
            var capture = NpcPresentationState.Capture(store, "npc:unknown-presentation", 12, false);
            Check.Equal(before, Serialize(store.Snapshot()));
            Check.True(capture != null);
            Check.Equal("npc:unknown-presentation", capture.PersonId);
            Check.Equal(12d, capture.GameTime);
            Check.Equal(0, capture.Disposition);
            Check.True(!capture.DispositionKnown);
            Check.True(!capture.TrustKnown);
            Check.True(!capture.RelationshipKnown);
            Check.True(!capture.AcuteFearKnown);
            Check.Equal(1, capture.Mood);
            Check.True(capture.CreateDeliveryContext() == null);
            Check.Equal(0, store.Snapshot().NpcPersonalities.Count);
            Check.Equal(0, store.Snapshot().NpcRelationships.Relationships.Count);
        }

        private static void FreezesTimedProjection()
        {
            var store = KnownStore(out string personId);
            store.GetOrCreateNpcPersonality(personId, 17, NpcSocialBackground.Commoner);
            long sequence;
            Check.True(store.TryCommitAcceptedSocialExchange(personId, 0, 10, 100,
                "day", "place", "hello", "hello", out sequence), "Initial mood exchange must commit.");
            double gameTime = 100 + 86400;
            var capture = NpcPresentationState.Capture(store, personId, gameTime, false);
            Check.True(capture != null, "Valid capture must exist.");
            Check.Equal(MoodRecovery.ValueAt(10, 100, gameTime,
                MoodRecovery.HalfLifeSeconds(store.WorldSeed, store.ReadNpcPersonality(personId))), capture.Mood);
            Check.True(capture.Mood < 10, "Timed mood projection must decay below saved mood.");
            int savedMood = capture.Mood;
            int encounters = capture.Encounters;
            string trait = capture.SavedTraits["warmth"];

            store.RecordConversationEncounter(personId, gameTime);
            Check.Equal(savedMood, capture.Mood);
            Check.Equal(encounters, capture.Encounters);
            Check.Equal(trait, capture.SavedTraits["warmth"]);
            Check.Equal(1, store.ConversationEncounters(personId));
        }

        private static void ContactFearPreservesRelationship()
        {
            var store = KnownStore(out string personId);
            store.GetOrCreateNpcPersonality(personId, 17, NpcSocialBackground.Commoner);
            var evidence = NpcExperienceEvent.FromNativeOutcome(personId, personId,
                "receipt:present", "episode:present", "outcome:present",
                NpcExperienceEventKind.Healing, 100, NpcResponsibleActorAttribution.Known, "player");
            var proposal = new NpcRelationshipLedgerUpdateProposal
            {
                ExpectedLedgerRevision = 0,
                ObserverId = personId,
                TargetId = "player",
                RelationshipUpdate = new NpcRelationshipUpdateProposal
                {
                    ExpectedRevision = 0,
                    Evidence = evidence,
                    TrustDelta = 6,
                    ResentmentDelta = 2,
                    InterpersonalFearDelta = 8
                }
            };
            Check.True(store.TryCommitNpcRelationship(proposal, 0));
            store.SetConversationBaseline(personId, 20, 100);
            Check.True(store.TryObserveNpcAcuteFear(personId, "wolf:1", NpcFearSeverity.Moderate, 100));

            var rawBefore = store.ReadNpcRelationship(personId, "player");
            var projectedBefore = store.ReadNpcRelationship(personId, "player", 1000);
            string before = Serialize(store.Snapshot());
            var capture = NpcPresentationState.Capture(store, personId, 1000, true);
            Check.Equal(before, Serialize(store.Snapshot()));
            var projectedAfter = store.ReadNpcRelationship(personId, "player", 1000);
            var rawAfter = store.ReadNpcRelationship(personId, "player");

            Check.True(capture.RelationshipKnown);
            Check.True(capture.TrustKnown);
            Check.Equal(projectedBefore.ReliabilityTrust, capture.Trust);
            Check.Equal(projectedBefore.ProjectedResentment, capture.Resentment);
            Check.Equal(NpcPlayerContactFearPolicy.Project(projectedBefore.ProjectedInterpersonalFear, true), capture.PlayerFear);
            Check.True(capture.PlayerFear >= projectedBefore.ProjectedInterpersonalFear);
            Check.True(capture.AcuteFearKnown);
            Check.True(capture.AcuteFearIntensity > 0);
            Check.Equal(Math.Max(capture.PlayerFear, (int)Math.Round(capture.AcuteFearIntensity,
                MidpointRounding.AwayFromZero)), capture.DeliveryFear);
            Check.Equal(rawBefore.ReliabilityTrust, rawAfter.ReliabilityTrust);
            Check.Equal(rawBefore.InterpersonalFear, rawAfter.InterpersonalFear);
            Check.Equal(rawBefore.FearRecoveryAnchorGameTime, rawAfter.FearRecoveryAnchorGameTime);
            Check.Equal(rawBefore.FearRecoveryHalfLifeSeconds, rawAfter.FearRecoveryHalfLifeSeconds);
            Check.Equal(rawBefore.ResentmentRecoveryAnchorGameTime, rawAfter.ResentmentRecoveryAnchorGameTime);
            Check.Equal(rawBefore.ResentmentRecoveryHalfLifeSeconds, rawAfter.ResentmentRecoveryHalfLifeSeconds);
            Check.Equal(projectedBefore.ReliabilityTrust, projectedAfter.ReliabilityTrust);
            Check.Equal(projectedBefore.Resentment, projectedAfter.Resentment);
        }

        private static void DeliveryUsesCapturedValues()
        {
            var store = KnownStore(out string personId);
            store.GetOrCreateNpcPersonality(personId, 17, NpcSocialBackground.Commoner);
            store.SetConversationBaseline(personId, 20, 10);
            store.RecordConversationEncounter(personId, 10);
            var capture = NpcPresentationState.Capture(store, personId, 10, false);
            var delivery = capture.CreateDeliveryContext();

            Check.True(delivery != null);
            Check.Equal(capture.PersonId, delivery.PersonId);
            Check.Equal(capture.Mood, delivery.Mood);
            Check.Equal(capture.DispositionKnown, delivery.DispositionKnown);
            Check.Equal(capture.Disposition, delivery.Disposition);
            Check.Equal(capture.TrustKnown, delivery.TrustKnown);
            Check.Equal(capture.Trust, delivery.Trust);
            Check.Equal(capture.DeliveryFear, delivery.Fear);
            Check.Equal(capture.Resentment, delivery.Resentment);
            Check.Equal(capture.Encounters, delivery.Encounters);
            Check.Equal(capture.SavedTraits.Count, delivery.Traits.Count);
            foreach (var trait in capture.SavedTraits)
                Check.Equal(trait.Value, delivery.Traits[trait.Key]);
            Check.Equal(60, delivery.Disposition);
        }

        private static void CapturesProjectedResentment()
        {
            var store = KnownStore(out string personId);
            var evidence = NpcExperienceEvent.FromNativeOutcome(personId, personId,
                "receipt:resentment", "episode:resentment", "outcome:resentment",
                NpcExperienceEventKind.Healing, 0, NpcResponsibleActorAttribution.Known, "player");
            var proposal = new NpcRelationshipLedgerUpdateProposal
            {
                ExpectedLedgerRevision = 0,
                ObserverId = personId,
                TargetId = "player",
                RelationshipUpdate = new NpcRelationshipUpdateProposal
                {
                    ExpectedRevision = 0,
                    Evidence = evidence,
                    ResentmentDelta = 10
                }
            };
            Check.True(store.TryCommitNpcRelationship(proposal, 0));

            const double gameTime = 28 * 24 * 60 * 60d;
            var projected = store.ReadNpcRelationship(personId, "player", gameTime);
            var capture = NpcPresentationState.Capture(store, personId, gameTime, false);

            Check.True(projected.ProjectedResentment < projected.StoredResentment);
            Check.Equal(projected.ProjectedResentment, capture.Resentment);
        }

        private static WorldStateStore KnownStore(out string personId)
        {
            var store = new WorldStateStore(42);
            personId = store.GetOrCreate(1, new[] { 5 }).Households[0].Residents[0].Id;
            return store;
        }

        private static string Serialize(WorldState state)
        {
            return JsonSerializer.Serialize(state, new JsonSerializerOptions { IncludeFields = true });
        }
    }
}
