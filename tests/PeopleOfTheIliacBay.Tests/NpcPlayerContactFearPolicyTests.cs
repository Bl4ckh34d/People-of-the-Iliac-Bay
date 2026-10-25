using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcPlayerContactFearPolicyTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Verified player contact amplifies residual directed fear with bounded rounding", ContactAmplifiesResidualFear));
            cases.Add(("Missing verified contact preserves directed fear", MissingContactPreservesFear));
            cases.Add(("Repeated contact projection from one residual stays stable", RepeatedProjectionUsesSameResidual));
            cases.Add(("Player-contact fear validates directed fear range", InvalidFearIsRejected));
            cases.Add(("Day fourteen contact amplifies decayed fear without changing relationship state", DayFourteenContactPreservesRelationshipState));
        }

        private static void ContactAmplifiesResidualFear()
        {
            Check.Equal(0, NpcPlayerContactFearPolicy.Project(0, true));
            Check.Equal(1, NpcPlayerContactFearPolicy.Project(1, true));
            Check.Equal(50, NpcPlayerContactFearPolicy.Project(40, true));
            Check.Equal(100, NpcPlayerContactFearPolicy.Project(90, true));
            Check.Equal(100, NpcPlayerContactFearPolicy.Project(100, true));
        }

        private static void MissingContactPreservesFear()
        {
            Check.Equal(0, NpcPlayerContactFearPolicy.Project(0, false));
            Check.Equal(1, NpcPlayerContactFearPolicy.Project(1, false));
            Check.Equal(40, NpcPlayerContactFearPolicy.Project(40, false));
            Check.Equal(90, NpcPlayerContactFearPolicy.Project(90, false));
            Check.Equal(100, NpcPlayerContactFearPolicy.Project(100, false));
        }

        private static void RepeatedProjectionUsesSameResidual()
        {
            int decayedSource = 40;
            int first = NpcPlayerContactFearPolicy.Project(decayedSource, true);
            int repeated = NpcPlayerContactFearPolicy.Project(decayedSource, true);

            Check.Equal(50, first);
            Check.Equal(first, repeated);
            Check.Equal(40, decayedSource);
        }

        private static void InvalidFearIsRejected()
        {
            Check.Throws<ArgumentOutOfRangeException>(() => NpcPlayerContactFearPolicy.Project(-1, false));
            Check.Throws<ArgumentOutOfRangeException>(() => NpcPlayerContactFearPolicy.Project(101, true));
        }

        private static void DayFourteenContactPreservesRelationshipState()
        {
            const double day = 24 * 60 * 60;
            var relationship = NpcDirectedRelationship.Create("npc:contact", NpcDirectedRelationship.PlayerTargetId);
            relationship.Revision = 1;
            relationship.LastGameTime = 0;
            relationship.TrustKnown = true;
            relationship.ReliabilityTrust = 67;
            relationship.TrustConfidence = 10;
            relationship.TrustEvidenceCount = 1;
            relationship.Resentment = 31;
            relationship.InterpersonalFear = 80;
            relationship.HasFearRecoveryAnchor = true;
            relationship.FearRecoveryAnchorGameTime = 0;
            relationship.FearRecoveryHalfLifeSeconds = 7 * day;

            var profile = NpcPersonalityGenerator.Generate("npc:contact", 47, NpcSocialBackground.Commoner);
            var decayed = relationship.ProjectFear(14 * day, profile);
            Check.Equal(20, decayed.ProjectedInterpersonalFear);
            Check.Equal(25, NpcPlayerContactFearPolicy.Project(decayed.ProjectedInterpersonalFear, true));

            Check.Equal(80, relationship.InterpersonalFear);
            Check.Equal(67, relationship.ReliabilityTrust);
            Check.Equal(31, relationship.Resentment);
            Check.Equal(0d, relationship.FearRecoveryAnchorGameTime);
            Check.Equal(7 * day, relationship.FearRecoveryHalfLifeSeconds);
            Check.Equal(67, decayed.ReliabilityTrust);
            Check.Equal(31, decayed.Resentment);
            Check.Equal(0d, decayed.FearRecoveryAnchorGameTime);
        }
    }
}
