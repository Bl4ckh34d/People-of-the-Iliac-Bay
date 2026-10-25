using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcDirectedRelationshipTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Directed relationships begin with unknown trust and expose immutable views", () =>
            {
                var state = NpcDirectedRelationship.Create("npc:1:2:3", NpcDirectedRelationship.PlayerTargetId);
                var view = state.ToView();
                Check.Equal("npc:1:2:3", view.ObserverId);
                Check.Equal(NpcDirectedRelationship.PlayerTargetId, view.TargetId);
                Check.True(!view.TrustKnown);
                Check.Equal(0, view.ReliabilityTrust);
                Check.Equal(0, view.TrustConfidence);
                Check.Equal(0, view.TrustEvidenceCount);
                Check.Equal(0, view.Resentment);
                Check.Equal(0, view.InterpersonalFear);
                Check.Throws<ArgumentException>(() => NpcDirectedRelationship.Create("npc:1:2:3", "npc:1:2:3"));
            }));
            cases.Add(("Verified relationship updates are directed and revision-bound", () =>
            {
                var current = NpcDirectedRelationship.Create("npc:1:2:3", NpcDirectedRelationship.PlayerTargetId);
                var eventProof = NativeEvent("npc:1:2:3", "npc:1:2:3", "player", NpcExperienceEventKind.ObligationFulfilled, 12);
                var proposal = Proposal(0, eventProof, 5, 2, 1);
                NpcDirectedRelationship next;
                Check.True(NpcDirectedRelationship.TryApply(current, proposal, out next));
                Check.True(next.TrustKnown);
                Check.Equal(55, next.ReliabilityTrust);
                Check.Equal(1, next.TrustEvidenceCount);
                Check.Equal(10, next.TrustConfidence);
                Check.Equal(2, next.Resentment);
                Check.Equal(1, next.InterpersonalFear);
                Check.Equal(1L, next.Revision);
                Check.Equal(12d, next.LastGameTime);
                Check.True(!NpcDirectedRelationship.TryApply(next, proposal, out current));
                Check.True(current == null);
                Check.Equal(55, next.ReliabilityTrust);
            }));
            cases.Add(("Unknown attribution and attributed claims cannot directly change relationships", () =>
            {
                var current = NpcDirectedRelationship.Create("npc:observer", "player");
                NpcDirectedRelationship next;
                var unknown = NativeEvent("npc:observer", "npc:observer", null,
                    NpcExperienceEventKind.ObligationBreached, 10, NpcResponsibleActorAttribution.Unknown);
                Check.True(!NpcDirectedRelationship.TryApply(current, Proposal(0, unknown, -5, 0, 0), out next));
                var claim = NpcExperienceEvent.FromAttributedSpeech("npc:observer", "npc:observer", "speech:1", "turn:1",
                    "utterance:1", NpcExperienceEventKind.ObligationFulfilled, 10, "player", "player");
                Check.True(!NpcDirectedRelationship.TryApply(current, Proposal(0, claim, 5, 0, 0), out next));
                Check.True(next == null);
                Check.True(!current.TrustKnown);
            }));
            cases.Add(("Gifts, mood and claims cannot raise reliability trust", () =>
            {
                var current = NpcDirectedRelationship.Create("npc:observer", "player");
                NpcDirectedRelationship next;
                foreach (NpcExperienceEventKind kind in new[] { NpcExperienceEventKind.MoneyReceived })
                {
                    var eventProof = NativeEvent("npc:observer", "npc:observer", "player", kind, 10);
                    Check.True(!NpcDirectedRelationship.TryApply(current, Proposal(0, eventProof, 1, 0, 0), out next));
                }
                Check.True(!current.TrustKnown);
            }));
            cases.Add(("Resentment and interpersonal fear update without defining trust", () =>
            {
                var current = NpcDirectedRelationship.Create("npc:observer", "player");
                var evidence = NativeEvent("npc:observer", "npc:observer", "player", NpcExperienceEventKind.Injury, 10);
                NpcDirectedRelationship next;
                Check.True(NpcDirectedRelationship.TryApply(current, Proposal(0, evidence, 0, 4, 6), out next));
                Check.True(!next.TrustKnown);
                Check.Equal(0, next.ReliabilityTrust);
                Check.Equal(4, next.Resentment);
                Check.Equal(6, next.InterpersonalFear);
            }));
            cases.Add(("Elapsed days reduce prior fear before a later fear event adds new impact", () =>
            {
                var current = NpcDirectedRelationship.Create("npc:recovery", NpcDirectedRelationship.PlayerTargetId);
                NpcDirectedRelationship afterFirstEvent;
                var first = Proposal(0, NativeEvent("npc:recovery", "npc:recovery", "player",
                    NpcExperienceEventKind.Injury, 0), 0, 0, 10);
                Check.True(NpcDirectedRelationship.TryApply(current, first, out afterFirstEvent));

                NpcDirectedRelationship afterLaterEvent;
                var later = Proposal(1, NativeEvent("npc:recovery", "npc:recovery", "player",
                    NpcExperienceEventKind.Injury, 7 * 24 * 60 * 60), 0, 0, 10);
                Check.True(NpcDirectedRelationship.TryApply(afterFirstEvent, later, out afterLaterEvent));

                Check.True(afterLaterEvent.InterpersonalFear < 20);
                Check.True(afterLaterEvent.InterpersonalFear > 0);
            }));
            cases.Add(("Relationship updates reject stale revisions, rewound time, and unbounded deltas", () =>
            {
                var current = NpcDirectedRelationship.Create("npc:observer", "player");
                var first = Proposal(0, NativeEvent("npc:observer", "npc:observer", "player",
                    NpcExperienceEventKind.ObligationFulfilled, 10), 1, 0, 0);
                NpcDirectedRelationship updated;
                Check.True(NpcDirectedRelationship.TryApply(current, first, out updated));
                var stale = Proposal(0, NativeEvent("npc:observer", "npc:observer", "player",
                    NpcExperienceEventKind.ObligationFulfilled, 11), 1, 0, 0);
                Check.True(!NpcDirectedRelationship.TryApply(updated, stale, out current));
                var rewound = Proposal(1, NativeEvent("npc:observer", "npc:observer", "player",
                    NpcExperienceEventKind.ObligationFulfilled, 9), 1, 0, 0);
                Check.True(!NpcDirectedRelationship.TryApply(updated, rewound, out current));
                var excessive = Proposal(1, NativeEvent("npc:observer", "npc:observer", "player",
                    NpcExperienceEventKind.ObligationFulfilled, 11), 11, 0, 0);
                Check.True(!NpcDirectedRelationship.TryApply(updated, excessive, out current));
            }));
            cases.Add(("Validated relationship copies reject corrupted dimensions and detach data", () =>
            {
                var source = NpcDirectedRelationship.Create("npc:observer", "player");
                NpcDirectedRelationship next;
                Check.True(NpcDirectedRelationship.TryApply(source, Proposal(0, NativeEvent("npc:observer", "npc:observer",
                    "player", NpcExperienceEventKind.ObligationFulfilled, 1), 2, 0, 0), out next));
                var copy = NpcDirectedRelationship.CopyValidated(next);
                Check.True(!ReferenceEquals(copy, next));
                var view = next.ToView();
                next.Resentment = 100;
                Check.Equal(0, view.Resentment);
                var invalid = NpcDirectedRelationship.CopyValidated(copy);
                invalid.InterpersonalFear = 101;
                Check.Throws<ArgumentException>(() => NpcDirectedRelationship.CopyValidated(invalid));
            }));
        }

        private static NpcExperienceEvent NativeEvent(string observer, string subject, string responsible,
            NpcExperienceEventKind kind, double time, NpcResponsibleActorAttribution attribution = NpcResponsibleActorAttribution.Known)
        {
            string occurrenceId = "outcome:" + time.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return NpcExperienceEvent.FromNativeOutcome(observer, subject, "receipt:" + time, "episode:relationship",
                occurrenceId, kind, time, attribution, responsible);
        }

        private static NpcRelationshipUpdateProposal Proposal(long revision, NpcExperienceEvent eventProof,
            int trust, int resentment, int fear)
        {
            return new NpcRelationshipUpdateProposal
            {
                ExpectedRevision = revision,
                Evidence = eventProof,
                TrustDelta = trust,
                ResentmentDelta = resentment,
                InterpersonalFearDelta = fear
            };
        }
    }
}
