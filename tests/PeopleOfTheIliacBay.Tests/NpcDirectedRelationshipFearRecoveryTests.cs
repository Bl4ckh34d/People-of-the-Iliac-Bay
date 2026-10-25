using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcDirectedRelationshipFearRecoveryTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Public fear recovery math rejects clocks before its anchor", () =>
            {
                var policy = NpcFearRecoveryPolicy.Create(100, 0.70, 1.50);
                Check.Throws<ArgumentOutOfRangeException>(() => policy.Project(20, 100, 99, 100));
                Check.Throws<ArgumentOutOfRangeException>(() => policy.DecayFraction(100, 99, 100));
            }));

            cases.Add(("Directed fear projection ages from its own anchor without mutating saved state", () =>
            {
                var state = NpcDirectedRelationship.Create("npc:fear", NpcDirectedRelationship.PlayerTargetId);
                state.Revision = 1;
                state.LastGameTime = 100;
                state.InterpersonalFear = 60;
                state.HasFearRecoveryAnchor = true;
                state.FearRecoveryAnchorGameTime = 100;
                state.FearRecoveryHalfLifeSeconds = 100;
                var profile = NpcPersonalityGenerator.GenerateLegacyV2("npc:fear", 23, NpcSocialBackground.Commoner);
                var policy = NpcFearRecoveryPolicy.Create(100, 0.70, 1.50);

                var first = state.ProjectFear(300, profile, policy);
                Check.Equal(15, first.ProjectedInterpersonalFear);
                Check.Equal(60, first.StoredInterpersonalFear);
                Check.Equal(60, state.InterpersonalFear);
                Check.Equal(100d, state.FearRecoveryAnchorGameTime);

                var evidence = NativeEvent("npc:fear", "npc:fear", "player",
                    NpcExperienceEventKind.ObligationFulfilled, 300);
                NpcDirectedRelationship afterTrust;
                Check.True(NpcDirectedRelationship.TryApply(state, Proposal(1, evidence, 1, 0, 0), out afterTrust));
                var afterUnrelatedChange = afterTrust.ProjectFear(300, profile, policy);
                Check.Equal(15, afterUnrelatedChange.ProjectedInterpersonalFear);
                Check.Equal(100d, afterTrust.FearRecoveryAnchorGameTime);
                Check.Equal(300d, afterTrust.LastGameTime);
            }));

            cases.Add(("Late positive fear appraisal ages prior aggregate and event contribution independently", () =>
            {
                var state = NpcDirectedRelationship.Create("npc:late-fear", NpcDirectedRelationship.PlayerTargetId);
                state.Revision = 1;
                state.LastGameTime = 100;
                state.InterpersonalFear = 60;
                state.HasFearRecoveryAnchor = true;
                state.FearRecoveryAnchorGameTime = 100;
                state.FearRecoveryHalfLifeSeconds = 100;
                var profile = NpcPersonalityGenerator.GenerateLegacyV2("npc:late-fear", 24, NpcSocialBackground.Commoner);
                var policy = NpcFearRecoveryPolicy.Create(100, 0.70, 1.50);
                var evidence = NativeEvent("npc:late-fear", "npc:late-fear", "player",
                    NpcExperienceEventKind.Injury, 200);

                NpcDirectedRelationship updated;
                Check.True(NpcDirectedRelationship.TryApplyFearEvent(state, evidence, 20, 300,
                    profile, NpcFearSeverity.Moderate, policy, out updated));

                int oldResidual = policy.Project(60, 100, 300, 100);
                int eventResidual = policy.Project(20, 200, 300,
                    policy.HalfLifeSeconds(profile, NpcFearSeverity.Moderate));
                int expected = oldResidual + eventResidual;
                Check.Equal(expected, updated.InterpersonalFear);
                Check.Equal(300d, updated.FearRecoveryAnchorGameTime);
                Check.Equal(300d, updated.LastGameTime);
                Check.True(updated.InterpersonalFear != policy.Project(80, 200, 300, 100));
                Check.True(updated.InterpersonalFear != oldResidual + 20);
            }));

            cases.Add(("Negative fear reductions rebase residual at commit and decay only from that point", () =>
            {
                var state = NpcDirectedRelationship.Create("npc:negative-fear", NpcDirectedRelationship.PlayerTargetId);
                state.Revision = 1;
                state.LastGameTime = 100;
                state.InterpersonalFear = 60;
                state.HasFearRecoveryAnchor = true;
                state.FearRecoveryAnchorGameTime = 100;
                state.FearRecoveryHalfLifeSeconds = 100;
                var profile = NpcPersonalityGenerator.GenerateLegacyV2("npc:negative-fear", 29, NpcSocialBackground.Commoner);
                var policy = NpcFearRecoveryPolicy.Create(100, 0.70, 1.50);
                var evidence = NativeEvent("npc:negative-fear", "npc:negative-fear", "player",
                    NpcExperienceEventKind.Injury, 120);
                int expectedAtCommit = Math.Max(0, policy.Project(60, 100, 150, 100) - 5);

                NpcDirectedRelationship reduced;
                Check.True(NpcDirectedRelationship.TryApplyFearEvent(state, evidence, -5, 150,
                    profile, NpcFearSeverity.Moderate, policy, out reduced));
                Check.Equal(expectedAtCommit, policy.Project(
                    reduced.InterpersonalFear + reduced.FearRecoveryBaselineRemainder,
                    reduced.FearRecoveryAnchorGameTime, 150, reduced.FearRecoveryHalfLifeSeconds));
                Check.Equal(100d, reduced.FearRecoveryAnchorGameTime);
                Check.Equal(expectedAtCommit, reduced.ProjectFear(150, profile, policy).ProjectedInterpersonalFear);
                Check.Equal(policy.Project(expectedAtCommit, 150, 200, 100),
                    reduced.ProjectFear(200, profile, policy).ProjectedInterpersonalFear);
                Check.Equal(policy.Project(expectedAtCommit, 150, 200, 100),
                    reduced.ProjectFear(200, profile, policy).ProjectedInterpersonalFear);

                var later = NativeEvent("npc:negative-fear", "npc:negative-fear", "player",
                    NpcExperienceEventKind.Injury, 200);
                NpcDirectedRelationship reducedAgain;
                Check.True(NpcDirectedRelationship.TryApplyFearEvent(reduced, later, -3, 200,
                    profile, NpcFearSeverity.Moderate, policy, out reducedAgain));
                Check.Equal(Math.Max(0, policy.Project(expectedAtCommit, 150, 200, 100) - 3),
                    reducedAgain.ProjectFear(200, profile, policy).ProjectedInterpersonalFear);
            }));

            cases.Add(("Fear projection rejects pre-anchor clocks and another observer profile", () =>
            {
                var state = NpcDirectedRelationship.Create("npc:projection-owner", "player");
                state.Revision = 1;
                state.LastGameTime = 100;
                state.InterpersonalFear = 20;
                state.HasFearRecoveryAnchor = true;
                state.FearRecoveryAnchorGameTime = 100;
                state.FearRecoveryHalfLifeSeconds = 100;
                var view = state.ToView();
                var owner = NpcPersonalityGenerator.GenerateLegacyV2("npc:projection-owner", 1, NpcSocialBackground.Commoner);
                var other = NpcPersonalityGenerator.GenerateLegacyV2("npc:another-owner", 1, NpcSocialBackground.Commoner);
                Check.Throws<ArgumentOutOfRangeException>(() => view.ProjectFear(99, owner));
                Check.Throws<ArgumentException>(() => view.ProjectFear(100, other));
            }));

            cases.Add(("Fear recovery rate stays bounded and responds to saved traits and event severity", () =>
            {
                var profile = NpcPersonalityGenerator.GenerateLegacyV2("npc:rate", 25, NpcSocialBackground.Commoner);
                var resilient = NpcPersonalityProfile.CopyValidated(profile);
                resilient.EmotionalStability = "resilient";
                resilient.Sensitivity = "thick-skinned";
                resilient.SelfEsteem = "confident";
                resilient.Humor = "playful";
                var sensitive = NpcPersonalityProfile.CopyValidated(profile);
                sensitive.EmotionalStability = "volatile";
                sensitive.Sensitivity = "highly sensitive";
                sensitive.SelfEsteem = "insecure";
                sensitive.Humor = "solemn";
                var policy = NpcFearRecoveryPolicy.ProvisionalDefault;

                double fast = policy.HalfLifeSeconds(resilient, NpcFearSeverity.Mild);
                double typical = policy.HalfLifeSeconds(profile, NpcFearSeverity.Moderate);
                double slow = policy.HalfLifeSeconds(sensitive, NpcFearSeverity.Grave);
                Check.True(fast < typical);
                Check.True(typical < slow);
                Check.True(fast >= NpcFearRecoveryPolicy.ProvisionalDefaultHalfLifeSeconds * 0.70);
                Check.True(slow <= NpcFearRecoveryPolicy.ProvisionalDefaultHalfLifeSeconds * 1.50);
            }));

            cases.Add(("Legacy fear migrates only when retained outcomes prove its recovery anchor", () =>
            {
                var attributed = CreateLegacyLedger("npc:legacy-known", new[] { (0d, 10) });
                var migrated = NpcRelationshipLedger.CopyValidated(attributed, _ => true);
                var known = migrated.Relationships[0];
                Check.True(known.HasFearRecoveryAnchor);
                Check.Equal(0d, known.FearRecoveryAnchorGameTime);
                Check.Equal(10, known.InterpersonalFear);

                var ambiguous = CreateLegacyLedger("npc:legacy-unknown", new[] { (0d, 10), (100d, 10) });
                var conservative = NpcRelationshipLedger.CopyValidated(ambiguous, _ => true).Relationships[0];
                Check.True(!conservative.HasFearRecoveryAnchor);
                Check.Equal(-1d, conservative.FearRecoveryAnchorGameTime);
                Check.Equal(20, conservative.InterpersonalFear);
            }));
        }

        private static NpcRelationshipLedger CreateLegacyLedger(string observer, (double Time, int Delta)[] events)
        {
            var ledger = NpcRelationshipLedger.Create();
            foreach (var item in events)
            {
                var evidence = NativeEvent(observer, observer, "player", NpcExperienceEventKind.Injury, item.Time);
                var proposal = new NpcRelationshipLedgerUpdateProposal
                {
                    ExpectedLedgerRevision = ledger.Revision,
                    ObserverId = observer,
                    TargetId = "player",
                    RelationshipUpdate = Proposal(ledger.Relationships.Count, evidence, 0, 0, item.Delta)
                };
                NpcRelationshipLedger next;
                Check.True(NpcRelationshipLedger.TryApply(ledger, proposal, _ => true, out next));
                ledger = next;
            }
            ledger.Version = 1;
            ledger.Relationships[0].Version = 1;
            return ledger;
        }

        private static NpcExperienceEvent NativeEvent(string observer, string subject, string responsible,
            NpcExperienceEventKind kind, double time)
        {
            string suffix = time.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return NpcExperienceEvent.FromNativeOutcome(observer, subject, "receipt:" + suffix,
                "episode:fear-recovery", "outcome:" + suffix, kind, time,
                NpcResponsibleActorAttribution.Known, responsible);
        }

        private static NpcRelationshipUpdateProposal Proposal(long revision, NpcExperienceEvent evidence,
            int trust, int resentment, int fear)
        {
            return new NpcRelationshipUpdateProposal
            {
                ExpectedRevision = revision,
                Evidence = evidence,
                TrustDelta = trust,
                ResentmentDelta = resentment,
                InterpersonalFearDelta = fear
            };
        }
    }
}
