using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcRelationshipTrustPolicyTests
    {
        private static readonly HashSet<string> Known = new HashSet<string>(StringComparer.Ordinal)
        { "npc:observer", "npc:subject", "npc:attacker" };

        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Native harmful outcomes cap trust loss at ten in relationship updates", NativeHarmfulOutcomesCapAtTen));
            cases.Add(("Native harmful outcomes cap trust loss at ten through ledger and restore", LedgerAndRestoreCapNativeTrustLossAtTen));
            cases.Add(("Typed direct threats retain the twenty-five-point trust loss", DirectThreatRetainsTwentyFivePointBound));
            cases.Add(("Fear-only updates and fear decay never change reliability trust", FearDoesNotChangeTrust));
            cases.Add(("New reliability evidence can recover trust but gifts and speech cannot", ReliabilityEvidenceAloneCanRecoverTrust));
            cases.Add(("Unknown player blame is rejected while known non-player relationships remain valid", AttributionStaysTargeted));
        }

        private static void NativeHarmfulOutcomesCapAtTen()
        {
            foreach (NpcExperienceEventKind kind in HarmfulKinds())
            {
                var state = NpcDirectedRelationship.Create("npc:observer", "player");
                NpcDirectedRelationship updated;
                Check.True(NpcDirectedRelationship.TryApply(state,
                    Update(0, Native(kind, "player", 10, "accepted-" + kind), -10, 0, 0), out updated));
                Check.True(updated.TrustKnown);
                Check.Equal(40, updated.ReliabilityTrust);

                var excessive = NpcDirectedRelationship.Create("npc:observer", "player");
                Check.True(!NpcDirectedRelationship.TryApply(excessive,
                    Update(0, Native(kind, "player", 20, "excessive-" + kind), -25, 0, 0), out updated));
                Check.True(updated == null);
            }
        }

        private static void LedgerAndRestoreCapNativeTrustLossAtTen()
        {
            foreach (NpcExperienceEventKind kind in HarmfulKinds())
            {
                var ledger = NpcRelationshipLedger.Create();
                NpcRelationshipLedger updated;
                var accepted = Native(kind, "player", 10, "ledger-accepted-" + kind);
                Check.True(NpcRelationshipLedger.TryApply(ledger,
                    LedgerUpdate(0, 0, "player", accepted, -10), IsKnown, out updated));
                Check.Equal(40, updated.Read("npc:observer", "player").ReliabilityTrust);
                var restored = NpcRelationshipLedger.CopyValidated(updated, IsKnown);
                Check.Equal(40, restored.Read("npc:observer", "player").ReliabilityTrust);

                var excessive = Native(kind, "player", 20, "ledger-excessive-" + kind);
                Check.True(!NpcRelationshipLedger.TryApply(restored,
                    LedgerUpdate(restored.Revision, restored.Read("npc:observer", "player").Revision,
                        "player", excessive, -25), IsKnown, out updated));
                Check.True(updated == null);

                // Simulate a serialized native outcome created by an older or bypassing writer.
                var malformed = new NpcRelationshipLedger
                {
                    Revision = 1,
                    Relationships = new List<NpcDirectedRelationship>
                    {
                        new NpcDirectedRelationship
                        {
                            ObserverId = "npc:observer", TargetId = "player", Revision = 1,
                            LastGameTime = 20, TrustKnown = true, ReliabilityTrust = 25,
                            TrustConfidence = 10, TrustEvidenceCount = 1
                        }
                    },
                    AcceptedOutcomes = new List<NpcAcceptedRelationshipOutcome>
                    { RawNativeOutcome(excessive, -25) }
                };
                Check.Throws<ArgumentException>(() => NpcRelationshipLedger.CopyValidated(malformed, IsKnown));
            }
        }

        private static void DirectThreatRetainsTwentyFivePointBound()
        {
            var evidence = NpcExperienceEvent.FromObservedSpeech("npc:observer", "npc:observer",
                "speech:receipt", "episode:threat", "utterance:threat", 10, "player");
            var accepted = new NpcAcceptedRelationshipOutcome
            {
                ObserverId = "npc:observer", TargetId = "player", SubjectPersonId = "npc:observer",
                ReceiptId = "speech:receipt", EpisodeId = "episode:threat", OutcomeId = "utterance:threat",
                Origin = NpcExperienceEventOrigin.AttributedSpeech,
                AttributedSpeakerId = "player", SpeechActKind = NpcExperienceSpeechActKind.DirectThreat,
                SpeechTargetPersonId = "npc:observer", ObservedGameTime = 10, CapturedGameTime = 10,
                TrustDelta = -25, ResentmentDelta = 25, FearDelta = 25, AffinityDelta = -50,
                DeduplicationKey = evidence.DeduplicationKey
            };
            var copy = NpcAcceptedRelationshipOutcome.CopyValidated(accepted);
            Check.Equal(-25, copy.TrustDelta);
            Check.Equal(25, copy.FearDelta);
        }

        private static void FearDoesNotChangeTrust()
        {
            var profile = NpcPersonalityGenerator.Generate("npc:observer", 91, NpcSocialBackground.Commoner);
            var recovery = NpcFearRecoveryPolicy.ProvisionalDefault;
            var state = NpcDirectedRelationship.Create("npc:observer", "player");
            NpcDirectedRelationship afterReliability;
            Check.True(NpcDirectedRelationship.TryApply(state,
                Update(0, Native(NpcExperienceEventKind.ObligationFulfilled, "player", 1, "fear-seed"), 10, 0, 0),
                profile, recovery, out afterReliability));
            NpcDirectedRelationship afterHarm;
            Check.True(NpcDirectedRelationship.TryApply(afterReliability,
                Update(1, Native(NpcExperienceEventKind.Injury, "player", 2, "fear-harm"), -10, 0, 0),
                profile, recovery, out afterHarm));
            Check.Equal(50, afterHarm.ReliabilityTrust);

            NpcDirectedRelationship afterFearOne;
            Check.True(NpcDirectedRelationship.TryApplyFearEvent(afterHarm,
                Native(NpcExperienceEventKind.Injury, "player", 3, "fear-one"), 20, 3,
                profile, NpcFearSeverity.Moderate, recovery, out afterFearOne));
            NpcDirectedRelationship afterFearTwo;
            Check.True(NpcDirectedRelationship.TryApplyFearEvent(afterFearOne,
                Native(NpcExperienceEventKind.Injury, "player", 4, "fear-two"), 20, 4,
                profile, NpcFearSeverity.Moderate, recovery, out afterFearTwo));
            Check.Equal(50, afterFearTwo.ReliabilityTrust);

            var projected = afterFearTwo.ProjectFear(4 + 30 * 24 * 60 * 60, profile, recovery);
            Check.True(projected.ProjectedInterpersonalFear < projected.StoredInterpersonalFear);
            Check.Equal(50, projected.ReliabilityTrust);
        }

        private static void ReliabilityEvidenceAloneCanRecoverTrust()
        {
            var state = NpcDirectedRelationship.Create("npc:observer", "player");
            NpcDirectedRelationship low;
            Check.True(NpcDirectedRelationship.TryApply(state,
                Update(0, Native(NpcExperienceEventKind.Injury, "player", 1, "trust-loss"), -10, 0, 0), out low));
            Check.Equal(40, low.ReliabilityTrust);

            NpcDirectedRelationship recovered;
            Check.True(NpcDirectedRelationship.TryApply(low,
                Update(1, Native(NpcExperienceEventKind.CombatHelp, "player", 2, "trust-recovery"), 10, 0, 0), out recovered));
            Check.Equal(50, recovered.ReliabilityTrust);

            Check.True(!NpcDirectedRelationship.TryApply(recovered,
                Update(2, Native(NpcExperienceEventKind.GiftReceived, "player", 3, "gift"), 10, 0, 0), out low));
            var speech = NpcExperienceEvent.FromAttributedSpeech("npc:observer", "npc:observer", "speech:gift",
                "episode:gift", "utterance:gift", NpcExperienceEventKind.GiftReceived, 4, "player", "player");
            Check.True(!NpcDirectedRelationship.TryApply(recovered, Update(2, speech, 10, 0, 0), out low));
            Check.Equal(50, recovered.ReliabilityTrust);
        }

        private static void AttributionStaysTargeted()
        {
            var playerRelationship = NpcDirectedRelationship.Create("npc:observer", "player");
            var unknown = NpcExperienceEvent.FromNativeOutcome("npc:observer", "npc:subject",
                "receipt:unknown", "episode:trust", "outcome:unknown", NpcExperienceEventKind.Injury,
                1, NpcResponsibleActorAttribution.Unknown);
            NpcDirectedRelationship updated;
            Check.True(!NpcDirectedRelationship.TryApply(playerRelationship,
                Update(0, unknown, -10, 0, 0), out updated));
            Check.True(updated == null);

            var otherRelationship = NpcDirectedRelationship.Create("npc:observer", "npc:attacker");
            Check.True(NpcDirectedRelationship.TryApply(otherRelationship,
                Update(0, Native(NpcExperienceEventKind.Injury, "npc:attacker", 2, "other-actor"), -10, 0, 0), out updated));
            Check.Equal(40, updated.ReliabilityTrust);
        }

        private static IEnumerable<NpcExperienceEventKind> HarmfulKinds()
        {
            yield return NpcExperienceEventKind.Injury;
            yield return NpcExperienceEventKind.AppointmentNoShow;
            yield return NpcExperienceEventKind.ObligationBreached;
        }

        private static bool IsKnown(string id) { return Known.Contains(id); }

        private static NpcExperienceEvent Native(NpcExperienceEventKind kind, string actor, double time, string suffix)
        {
            return NpcExperienceEvent.FromNativeOutcome("npc:observer", "npc:subject",
                "receipt:" + suffix, "episode:trust-policy", "outcome:" + suffix, kind, time,
                NpcResponsibleActorAttribution.Known, actor);
        }

        private static NpcRelationshipUpdateProposal Update(long revision, NpcExperienceEvent evidence,
            int trust, int resentment, int fear)
        {
            return new NpcRelationshipUpdateProposal
            {
                ExpectedRevision = revision, Evidence = evidence, TrustDelta = trust,
                ResentmentDelta = resentment, InterpersonalFearDelta = fear
            };
        }

        private static NpcRelationshipLedgerUpdateProposal LedgerUpdate(long revision, long relationshipRevision,
            string target, NpcExperienceEvent evidence, int trust)
        {
            return new NpcRelationshipLedgerUpdateProposal
            {
                ExpectedLedgerRevision = revision, ObserverId = "npc:observer", TargetId = target,
                RelationshipUpdate = Update(relationshipRevision, evidence, trust, 0, 0)
            };
        }

        private static NpcAcceptedRelationshipOutcome RawNativeOutcome(NpcExperienceEvent evidence, int trust)
        {
            return new NpcAcceptedRelationshipOutcome
            {
                ObserverId = evidence.ObserverId, TargetId = evidence.ResponsibleActorId,
                SubjectPersonId = evidence.SubjectPersonId, ReceiptId = evidence.ReceiptId,
                EpisodeId = evidence.EpisodeId, OutcomeId = evidence.OutcomeId,
                EventKind = evidence.EventKind.Value, Origin = NpcExperienceEventOrigin.NativeOutcome,
                ObservedGameTime = evidence.ObservedGameTime, CapturedGameTime = evidence.ObservedGameTime,
                TrustDelta = trust, DeduplicationKey = evidence.DeduplicationKey
            };
        }
    }
}
