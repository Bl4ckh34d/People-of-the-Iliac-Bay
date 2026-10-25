using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcDirectedRelationshipResentmentRecoveryTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Resentment recovery uses slower personality and severity rates than fear", () =>
            {
                var profile = NpcPersonalityGenerator.GenerateLegacyV2("npc:resentment-rate", 73,
                    NpcSocialBackground.Commoner);
                profile.EmotionalStability = "even-tempered";
                profile.Sensitivity = "attuned";
                profile.SelfEsteem = "grounded";
                profile.Humor = "wry";
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

                var resentment = NpcResentmentRecoveryPolicy.ProvisionalDefault;
                var fear = NpcFearRecoveryPolicy.ProvisionalDefault;
                Check.Equal(14d * 24 * 60 * 60, NpcResentmentRecoveryPolicy.ProvisionalDefaultHalfLifeSeconds);
                Check.Equal(2 * fear.HalfLifeSeconds(profile, NpcFearSeverity.Moderate),
                    resentment.HalfLifeSeconds(profile, NpcFearSeverity.Moderate));
                Check.True(resentment.HalfLifeSeconds(profile, NpcFearSeverity.Mild) <
                    resentment.HalfLifeSeconds(profile, NpcFearSeverity.Moderate));
                Check.True(resentment.HalfLifeSeconds(profile, NpcFearSeverity.Moderate) <
                    resentment.HalfLifeSeconds(profile, NpcFearSeverity.Severe));
                Check.True(resentment.HalfLifeSeconds(profile, NpcFearSeverity.Severe) <
                    resentment.HalfLifeSeconds(profile, NpcFearSeverity.Grave));
                Check.True(resentment.HalfLifeSeconds(resilient, NpcFearSeverity.Moderate) <
                    resentment.HalfLifeSeconds(profile, NpcFearSeverity.Moderate));
                Check.True(resentment.HalfLifeSeconds(profile, NpcFearSeverity.Moderate) <
                    resentment.HalfLifeSeconds(sensitive, NpcFearSeverity.Moderate));
                Check.True(resentment.HalfLifeSeconds(profile, NpcFearSeverity.Moderate) <
                    NpcResentmentRecoveryPolicy.MaximumHalfLifeSeconds);
                Check.True(resentment.HalfLifeSeconds(sensitive, NpcFearSeverity.Moderate) <
                    NpcResentmentRecoveryPolicy.MaximumHalfLifeSeconds);
                Check.True(resentment.HalfLifeSeconds(profile, NpcFearSeverity.Moderate) >
                    fear.HalfLifeSeconds(profile, NpcFearSeverity.Moderate));
            }));

            cases.Add(("Combined timed projection ages fear and resentment without changing saved state", () =>
            {
                var state = NpcDirectedRelationship.Create("npc:resentment-projection", "player");
                state.Revision = 1;
                state.LastGameTime = 100;
                state.TrustKnown = true;
                state.ReliabilityTrust = 37;
                state.TrustConfidence = 10;
                state.TrustEvidenceCount = 1;
                state.InterpersonalFear = 60;
                state.Resentment = 80;
                state.HasFearRecoveryAnchor = true;
                state.FearRecoveryAnchorGameTime = 100;
                state.FearRecoveryHalfLifeSeconds = 100;
                state.HasResentmentRecoveryAnchor = true;
                state.ResentmentRecoveryAnchorGameTime = 100;
                state.ResentmentRecoveryHalfLifeSeconds = 200;
                var profile = NpcPersonalityGenerator.GenerateLegacyV2("npc:resentment-projection", 17,
                    NpcSocialBackground.Commoner);
                var fear = NpcFearRecoveryPolicy.Create(100, 0.70, 1.50);
                var resentment = NpcResentmentRecoveryPolicy.Create(200, 0.70, 1.50);

                var first = state.ProjectRecovery(300, profile, fear, resentment);
                var second = state.ProjectRecovery(300, profile, fear, resentment);
                Check.Equal(15, first.ProjectedInterpersonalFear);
                Check.Equal(40, first.ProjectedResentment);
                Check.Equal(first.ProjectedInterpersonalFear, second.ProjectedInterpersonalFear);
                Check.Equal(first.ProjectedResentment, second.ProjectedResentment);
                Check.Equal(60, first.StoredInterpersonalFear);
                Check.Equal(80, first.StoredResentment);
                Check.Equal(60, state.InterpersonalFear);
                Check.Equal(80, state.Resentment);
                Check.Equal(37, first.ReliabilityTrust);
                Check.Equal(1L, first.Revision);
                Check.Equal(100d, state.ResentmentRecoveryAnchorGameTime);
            }));

            cases.Add(("Late resentment event ages old grievance before adding new contribution", () =>
            {
                var state = RecoveryRow("npc:late-resentment", 60, 100, 100);
                state.InterpersonalFear = 30;
                state.HasFearRecoveryAnchor = true;
                state.FearRecoveryAnchorGameTime = 100;
                state.FearRecoveryHalfLifeSeconds = 100;
                var profile = NpcPersonalityGenerator.GenerateLegacyV2("npc:late-resentment", 89,
                    NpcSocialBackground.Commoner);
                var evidence = Native("npc:late-resentment", "outcome:late-resentment", 200);
                var proposal = Proposal(1, evidence, trust: 0, resentment: 10, fear: 0);

                NpcDirectedRelationship updated;
                Check.True(NpcDirectedRelationship.TryApply(state, proposal, profile,
                    NpcFearRecoveryPolicy.Create(100, 0.70, 1.50),
                    NpcResentmentRecoveryPolicy.Create(100, 0.70, 1.50), out updated));

                Check.Equal(40, updated.Resentment);
                Check.Equal(200d, updated.ResentmentRecoveryAnchorGameTime);
                Check.Equal(100d, updated.FearRecoveryAnchorGameTime);
                Check.Equal(30, updated.InterpersonalFear);
                Check.Equal(0, updated.ReliabilityTrust);
                Check.Equal(2L, updated.Revision);
                Check.Equal(40, updated.ProjectRecovery(200, profile,
                    NpcFearRecoveryPolicy.Create(100, 0.70, 1.50),
                    NpcResentmentRecoveryPolicy.Create(100, 0.70, 1.50)).ProjectedResentment);
            }));

            cases.Add(("Delayed appraisal projects resentment at capture and ages old and new grievance separately", () =>
            {
                var state = RecoveryRow("npc:delayed-resentment", 60, 100, 100);
                var profile = NpcPersonalityGenerator.GenerateLegacyV2("npc:delayed-resentment", 92,
                    NpcSocialBackground.Commoner);
                profile.EmotionalStability = "even-tempered";
                profile.Sensitivity = "attuned";
                profile.SelfEsteem = "grounded";
                profile.Humor = "wry";
                var fearPolicy = NpcFearRecoveryPolicy.Create(100, 0.70, 1.50);
                var resentmentPolicy = NpcResentmentRecoveryPolicy.Create(100, 0.70, 1.50);
                var evidence = Native("npc:delayed-resentment", "outcome:delayed-resentment", 200,
                    NpcExperienceEventKind.AppointmentAttended);
                var request = NpcExperienceAppraisalContract.CreateRequest("branch:resentment",
                    evidence, profile, 0, 0, 0, 0, 0, 0, 300, state.ToView(), null);
                int expectedRequestValue = resentmentPolicy.Project(60, 100, 300, 100);
                Check.Equal(expectedRequestValue, request.Relationship.ProjectedResentment);
                Check.True(request.Prompt.Contains(",resentment=" + expectedRequestValue.ToString(
                    System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));

                NpcExperienceAppraisal appraisal;
                Check.True(NpcExperienceAppraisalContract.TryParseResponse(request,
                    AppraisalResponse(request, resentment: 10), out appraisal));
                NpcDirectedRelationship updated;
                Check.True(NpcDirectedRelationship.TryApplyAppraisal(state, evidence, appraisal, profile,
                    fearPolicy, resentmentPolicy, out updated));

                double eventHalfLife = resentmentPolicy.HalfLifeSeconds(profile, NpcFearSeverity.Moderate);
                int expectedAtCommit = Math.Min(100,
                    resentmentPolicy.Project(60, 100, 300, 100) +
                    resentmentPolicy.Project(10, 200, 300, eventHalfLife));
                Check.Equal(expectedAtCommit, updated.Resentment);
                Check.Equal(300d, updated.ResentmentRecoveryAnchorGameTime);
                Check.Equal(eventHalfLife, updated.ResentmentRecoveryHalfLifeSeconds);
                Check.Equal(-1d, updated.FearRecoveryAnchorGameTime);
            }));

            cases.Add(("Negative resentment reduction preserves fractional baseline and separate fear clock", () =>
            {
                var state = RecoveryRow("npc:negative-resentment", 60, 100, 100);
                state.InterpersonalFear = 20;
                state.HasFearRecoveryAnchor = true;
                state.FearRecoveryAnchorGameTime = 90;
                state.FearRecoveryHalfLifeSeconds = 150;
                var profile = NpcPersonalityGenerator.GenerateLegacyV2("npc:negative-resentment", 90,
                    NpcSocialBackground.Commoner);
                var fearPolicy = NpcFearRecoveryPolicy.Create(150, 0.70, 1.50);
                var resentmentPolicy = NpcResentmentRecoveryPolicy.Create(100, 0.70, 1.50);
                var evidence = Native("npc:negative-resentment", "outcome:negative-resentment", 125);

                NpcDirectedRelationship reduced;
                Check.True(NpcDirectedRelationship.TryApply(state,
                    Proposal(1, evidence, trust: 0, resentment: -5, fear: 0), profile,
                    fearPolicy, resentmentPolicy, out reduced));

                Check.Equal(100d, reduced.ResentmentRecoveryAnchorGameTime);
                double projectedAtReduction = resentmentPolicy.Project(60, 100, 125, 100);
                double decayFraction = resentmentPolicy.DecayFraction(100, 125, 100);
                double expectedBaseline = (projectedAtReduction - 5) / decayFraction;
                double expectedRemainder = expectedBaseline -
                    Math.Round(expectedBaseline, MidpointRounding.AwayFromZero);
                Check.Near(expectedRemainder, reduced.ResentmentRecoveryBaselineRemainder);
                Check.Equal(45, reduced.ProjectRecovery(125, profile, fearPolicy, resentmentPolicy).ProjectedResentment);
                Check.Equal(90d, reduced.FearRecoveryAnchorGameTime);
                Check.Equal(20, reduced.InterpersonalFear);
                Check.Equal(0, reduced.ReliabilityTrust);
            }));

            cases.Add(("Trust and fear outcomes do not restart resentment recovery", () =>
            {
                var state = RecoveryRow("npc:unrelated-resentment", 45, 100, 300);
                state.TrustKnown = true;
                state.ReliabilityTrust = 10;
                state.TrustConfidence = 10;
                state.TrustEvidenceCount = 1;
                var profile = NpcPersonalityGenerator.GenerateLegacyV2("npc:unrelated-resentment", 91,
                    NpcSocialBackground.Commoner);
                var evidence = Native("npc:unrelated-resentment", "outcome:unrelated-fear", 300,
                    NpcExperienceEventKind.AppointmentAttended);

                NpcDirectedRelationship updated;
                Check.True(NpcDirectedRelationship.TryApply(state,
                    Proposal(1, evidence, trust: 1, resentment: 0, fear: 5), profile,
                    NpcFearRecoveryPolicy.ProvisionalDefault,
                    NpcResentmentRecoveryPolicy.ProvisionalDefault, out updated));

                Check.Equal(100d, updated.ResentmentRecoveryAnchorGameTime);
                Check.Equal(300d, updated.FearRecoveryAnchorGameTime);
                Check.Equal(45, updated.Resentment);
                Check.Equal(NpcResentmentRecoveryPolicy.ProvisionalDefault.Project(45, 100, 300, 300),
                    updated.ProjectRecovery(300, profile).ProjectedResentment);
                Check.Equal(11, updated.ReliabilityTrust);
            }));

            cases.Add(("Timed world reads project resentment while raw reads and revisions remain unchanged", () =>
            {
                var store = new WorldStateStore(42);
                string observer = store.GetOrCreate(20, new[] { 200 }, 1).Households[0].Residents[0].Id;
                store.GetOrCreateNpcPersonality(observer, 42, NpcSocialBackground.Commoner);
                var profile = new NpcPersonalityProfile { PersonId = observer };
                profile.EmotionalStability = "even-tempered";
                profile.Sensitivity = "attuned";
                profile.SelfEsteem = "grounded";
                var personalitySave = store.Snapshot();
                personalitySave.NpcPersonalities[0] = profile;
                store.Restore(personalitySave);
                var evidence = Native(observer, "outcome:store-resentment", 0);
                var proposal = LedgerProposal(0, observer, 0, evidence,
                    trust: 0, resentment: 10, fear: 0);
                Check.True(store.TryCommitNpcRelationship(proposal, 0));
                var replay = LedgerProposal(1, observer, 1, evidence,
                    trust: 0, resentment: 10, fear: 0);
                Check.True(!store.TryCommitNpcRelationship(replay, 1));
                var raw = store.ReadNpcRelationship(observer, "player");
                double savedHalfLife = raw.ResentmentRecoveryHalfLifeSeconds;
                var changedProfile = NpcPersonalityProfile.CopyValidated(profile);
                changedProfile.EmotionalStability = "resilient";
                var changedSave = store.Snapshot();
                changedSave.NpcPersonalities[0] = changedProfile;
                store.Restore(changedSave);
                double changedProfileHalfLife = NpcResentmentRecoveryPolicy.ProvisionalDefault
                    .HalfLifeSeconds(changedProfile, NpcFearSeverity.Moderate);
                Check.True(savedHalfLife != changedProfileHalfLife);
                Check.True(savedHalfLife < NpcResentmentRecoveryPolicy.MaximumHalfLifeSeconds);
                Check.True(changedProfileHalfLife < NpcResentmentRecoveryPolicy.MaximumHalfLifeSeconds);
                var projected = store.ReadNpcRelationship(observer, "player",
                    14d * 24 * 60 * 60);
                var rawAfter = store.ReadNpcRelationship(observer, "player");

                Check.Equal(10, projected.StoredResentment);
                Check.Equal(NpcResentmentRecoveryPolicy.ProvisionalDefault.Project(10, 0,
                    14d * 24 * 60 * 60, savedHalfLife), projected.ProjectedResentment);
                Check.Equal(10, raw.Resentment);
                Check.Equal(10, rawAfter.Resentment);
                Check.Equal(raw.Revision, projected.Revision);
                Check.Equal(raw.Revision, rawAfter.Revision);
                Check.Equal(0, projected.ReliabilityTrust);
                Check.Equal(savedHalfLife, rawAfter.ResentmentRecoveryHalfLifeSeconds);
            }));

            cases.Add(("Resentment-only commit preserves known trust value and evidence", () =>
            {
                var store = new WorldStateStore(44);
                string observer = store.GetOrCreate(22, new[] { 220 }, 1).Households[0].Residents[0].Id;
                store.GetOrCreateNpcPersonality(observer, 44, NpcSocialBackground.Commoner);
                var positive = Native(observer, "outcome:trust-establish", 10,
                    NpcExperienceEventKind.AppointmentAttended);
                Check.True(store.TryCommitNpcRelationship(LedgerProposal(0, observer, 0, positive,
                    trust: 3, resentment: 0, fear: 0), 0));
                var before = store.ReadNpcRelationship(observer, "player");
                var resentful = Native(observer, "outcome:resentment-only", 20,
                    NpcExperienceEventKind.ObligationBreached);
                Check.True(store.TryCommitNpcRelationship(LedgerProposal(1, observer, 1, resentful,
                    trust: 0, resentment: 5, fear: 0), 1));
                var after = store.ReadNpcRelationship(observer, "player");

                Check.True(before.TrustKnown && after.TrustKnown);
                Check.Equal(before.ReliabilityTrust, after.ReliabilityTrust);
                Check.Equal(before.TrustConfidence, after.TrustConfidence);
                Check.Equal(before.TrustEvidenceCount, after.TrustEvidenceCount);
                Check.Equal(5, after.Resentment);
                Check.Equal(2L, after.Revision);
            }));

            cases.Add(("Schema 41 migration preserves resentment without inventing a recovery anchor", () =>
            {
                var store = new WorldStateStore(43);
                string observer = store.GetOrCreate(21, new[] { 210 }, 1).Households[0].Residents[0].Id;
                var evidence = Native(observer, "outcome:schema41-resentment", 500,
                    NpcExperienceEventKind.AppointmentAttended);
                Check.True(store.TryCommitNpcRelationship(LedgerProposal(0, observer, 0, evidence,
                    trust: 2, resentment: 10, fear: 9), 0));
                foreach (int schemaVersion in new[] { 40, 41 })
                {
                    var legacy = store.Snapshot();
                    HistoricalNpcExperienceFixture.PrepareLegacyEnvelope(legacy, schemaVersion);
                    legacy.NpcRelationships.Version = 2;
                    var row = legacy.NpcRelationships.Relationships[0];
                    row.Version = 2;
                    row.HasResentmentRecoveryAnchor = false;
                    row.ResentmentRecoveryAnchorGameTime = -1;
                    row.ResentmentRecoveryHalfLifeSeconds = 0;
                    row.ResentmentRecoveryBaselineRemainder = 0;

                    var restored = new WorldStateStore(0);
                    restored.Restore(legacy);
                    var migrated = restored.ReadNpcRelationship(observer, "player");

                    Check.Equal(10, migrated.Resentment);
                    Check.True(!migrated.HasResentmentRecoveryAnchor);
                    Check.Equal(-1d, migrated.ResentmentRecoveryAnchorGameTime);
                    Check.Equal(9, migrated.StoredInterpersonalFear);
                    Check.True(migrated.HasFearRecoveryAnchor);
                    Check.Equal(52, migrated.ReliabilityTrust);
                    Check.Equal(1L, migrated.Revision);
                    Check.Equal(1, restored.Snapshot().NpcRelationships.AcceptedOutcomes.Count);
                }
            }));
        }

        private static NpcDirectedRelationship RecoveryRow(string observer, int resentment,
            double anchor, double halfLife)
        {
            var state = NpcDirectedRelationship.Create(observer, "player");
            state.Revision = 1;
            state.LastGameTime = anchor;
            state.Resentment = resentment;
            state.HasResentmentRecoveryAnchor = true;
            state.ResentmentRecoveryAnchorGameTime = anchor;
            state.ResentmentRecoveryHalfLifeSeconds = halfLife;
            return state;
        }

        private static NpcExperienceEvent Native(string observer, string outcome, double time,
            NpcExperienceEventKind kind = NpcExperienceEventKind.ObligationBreached)
        {
            return NpcExperienceEvent.FromNativeOutcome(observer, observer, "receipt:" + outcome,
                "episode:resentment-recovery", outcome, kind, time,
                NpcResponsibleActorAttribution.Known, "player");
        }

        private static NpcRelationshipLedgerUpdateProposal LedgerProposal(long ledgerRevision,
            string observer, long relationshipRevision, NpcExperienceEvent evidence,
            int trust, int resentment, int fear)
        {
            return new NpcRelationshipLedgerUpdateProposal
            {
                ExpectedLedgerRevision = ledgerRevision,
                ObserverId = observer,
                TargetId = "player",
                RelationshipUpdate = Proposal(relationshipRevision, evidence, trust, resentment, fear)
            };
        }

        private static string AppraisalResponse(NpcExperienceAppraisalRequest request, int resentment)
        {
            return "{\"schemaVersion\":" + NpcExperienceAppraisalContract.SchemaVersion +
                ",\"policyVersion\":" + NpcExperienceAppraisalContract.PolicyVersion +
                ",\"saveBranchId\":\"" + request.SaveBranchId + "\"" +
                ",\"observerId\":\"" + request.ObserverId + "\"" +
                ",\"subjectPersonId\":\"" + request.SubjectPersonId + "\"" +
                ",\"evidenceDeduplicationKey\":\"" + request.EvidenceDeduplicationKey + "\"" +
                ",\"canonicalEvidenceId\":\"" + request.CanonicalEvidenceId + "\"" +
                ",\"relationshipTargetId\":\"" + request.RelationshipTargetId + "\"" +
                ",\"expectedMoodRevision\":" + request.ExpectedMoodRevision +
                ",\"expectedAffinityRevision\":" + request.ExpectedAffinityRevision +
                ",\"expectedRelationshipRevision\":" + request.ExpectedRelationshipRevision +
                ",\"moodDelta\":0,\"affinityDelta\":0,\"trustDelta\":0" +
                ",\"resentmentDelta\":" + resentment + ",\"fearDelta\":0" +
                ",\"speechActKind\":\"NotApplicable\",\"speechTargetPersonId\":\"\"" +
                ",\"conversationOption\":\"NoAction\"}";
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
