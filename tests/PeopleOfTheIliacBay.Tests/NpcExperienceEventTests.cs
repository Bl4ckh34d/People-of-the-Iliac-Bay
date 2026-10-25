using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcExperienceEventTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(Test("Native experience records preserve observer and known or unknown attribution", () =>
            {
                var unknown = NpcExperienceEvent.FromNativeOutcome("witness", "injured-npc", "receipt-a",
                    "combat-episode-7", "outcome:injury:1", NpcExperienceEventKind.Injury, 120.5,
                    NpcResponsibleActorAttribution.Unknown);
                Check.Equal(NpcExperienceEventOrigin.NativeOutcome, unknown.Origin);
                Check.Equal(NpcExperienceEventKind.Injury, unknown.EventKind.Value);
                Check.Equal("witness", unknown.ObserverId);
                Check.Equal("injured-npc", unknown.SubjectPersonId);
                Check.Equal(NpcResponsibleActorAttribution.Unknown, unknown.ResponsibleActorAttribution);
                Check.True(unknown.ResponsibleActorId == null);

                var attributed = NpcExperienceEvent.FromNativeOutcome("witness", "injured-npc", "receipt-b",
                    "combat-episode-7", "outcome:injury:1", NpcExperienceEventKind.Injury, 120.5,
                    NpcResponsibleActorAttribution.Known, "identified-attacker");
                Check.Equal(NpcResponsibleActorAttribution.Known, attributed.ResponsibleActorAttribution);
                Check.Equal("identified-attacker", attributed.ResponsibleActorId);
                Check.True(unknown.DeduplicationKey == attributed.DeduplicationKey);
            }));

            cases.Add(Test("Semantic dedupe ignores adapter receipt IDs and keeps observer and outcome scope", () =>
            {
                var adapterA = NpcExperienceEvent.FromNativeOutcome("alice", "alice", "combat-receipt-1",
                    "combat-episode-9", "outcome:help:one", NpcExperienceEventKind.CombatHelp, 500,
                    NpcResponsibleActorAttribution.Known, "bob");
                var adapterB = NpcExperienceEvent.FromNativeOutcome("alice", "alice", "task-receipt-2",
                    "combat-episode-9", "outcome:help:one", NpcExperienceEventKind.CombatHelp, 500,
                    NpcResponsibleActorAttribution.Known, "bob");
                Check.True(adapterA.ReceiptId != adapterB.ReceiptId);
                Check.Equal(adapterA.DeduplicationKey, adapterB.DeduplicationKey);

                var otherObserver = NpcExperienceEvent.FromNativeOutcome("carol", "alice", "combat-receipt-3",
                    "combat-episode-9", "outcome:help:one", NpcExperienceEventKind.CombatHelp, 500,
                    NpcResponsibleActorAttribution.Known, "bob");
                var otherOutcome = NpcExperienceEvent.FromNativeOutcome("alice", "alice", "combat-receipt-4",
                    "combat-episode-9", "outcome:heal:two", NpcExperienceEventKind.Healing, 500,
                    NpcResponsibleActorAttribution.Known, "bob");
                var otherHelper = NpcExperienceEvent.FromNativeOutcome("alice", "alice", "combat-receipt-5",
                    "combat-episode-9", "outcome:help:two", NpcExperienceEventKind.CombatHelp, 500,
                    NpcResponsibleActorAttribution.Known, "carol");
                Check.True(adapterA.DeduplicationKey != otherObserver.DeduplicationKey);
                Check.True(adapterA.DeduplicationKey != otherOutcome.DeduplicationKey);
                Check.True(adapterA.DeduplicationKey != otherHelper.DeduplicationKey);
            }));

            cases.Add(Test("Attributed speech stays a claim and cannot create native actor attribution", () =>
            {
                var claim = NpcExperienceEvent.FromAttributedSpeech("listener", "patient", "dialogue-row-5",
                    "appointment-42", "utterance:fulfilled-1", NpcExperienceEventKind.ObligationFulfilled, 800,
                    "speaker", "alleged-helper");
                Check.Equal(NpcExperienceEventOrigin.AttributedSpeech, claim.Origin);
                Check.True(!claim.EventKind.HasValue);
                Check.Equal(NpcExperienceEventKind.ObligationFulfilled, claim.ClaimedEventKind.Value);
                Check.Equal(NpcResponsibleActorAttribution.Unknown, claim.ResponsibleActorAttribution);
                Check.True(claim.ResponsibleActorId == null);
                Check.Equal("speaker", claim.AttributedSpeakerId);
                Check.Equal("alleged-helper", claim.ClaimedResponsibleActorId);

                var native = NpcExperienceEvent.FromNativeOutcome("listener", "patient", "receipt-5",
                    "appointment-42", "outcome:fulfilled-1", NpcExperienceEventKind.ObligationFulfilled, 800,
                    NpcResponsibleActorAttribution.Known, "alleged-helper");
                Check.True(claim.DeduplicationKey != native.DeduplicationKey);

                var copy = NpcExperienceEvent.CopyValidated(claim);
                Check.True(!object.ReferenceEquals(claim, copy));
                Check.Equal(claim.DeduplicationKey, copy.DeduplicationKey);
                Check.Equal(claim.ClaimedResponsibleActorId, copy.ClaimedResponsibleActorId);

                var anotherClaim = NpcExperienceEvent.FromAttributedSpeech("listener", "patient", "dialogue-row-6",
                    "appointment-42", "utterance:fulfilled-2", NpcExperienceEventKind.ObligationFulfilled, 801,
                    "speaker", "alleged-helper");
                Check.True(claim.DeduplicationKey != anotherClaim.DeduplicationKey);
                var speechAlias = NpcExperienceEvent.FromAttributedSpeech("listener", "patient", "dialogue-row-7",
                    "appointment-42", "utterance:fulfilled-1", NpcExperienceEventKind.ObligationFulfilled, 800,
                    "speaker", "alleged-helper");
                Check.Equal(claim.DeduplicationKey, speechAlias.DeduplicationKey);
            }));

            cases.Add(Test("Observed speech can carry receipt-bound evidence without a claimed world event", () =>
            {
                var speech = NpcExperienceEvent.FromObservedSpeech("listener", "patient", "dialogue-row-8",
                    "conversation-9", "utterance:9", 801, "speaker");
                Check.Equal(NpcExperienceEventOrigin.AttributedSpeech, speech.Origin);
                Check.True(!speech.EventKind.HasValue);
                Check.True(!speech.ClaimedEventKind.HasValue);
                Check.Equal("speaker", speech.AttributedSpeakerId);
                Check.Equal(NpcResponsibleActorAttribution.Unknown, speech.ResponsibleActorAttribution);
                Check.True(speech.ResponsibleActorId == null);
                Check.True(speech.ClaimedResponsibleActorId == null);
                var copy = NpcExperienceEvent.CopyValidated(speech);
                Check.Equal(speech.DeduplicationKey, copy.DeduplicationKey);
                Check.Equal(speech.DeduplicationKey, NpcExperienceEvent.FromObservedSpeech("listener", "patient",
                    "dialogue-row-alias", "conversation-9", "utterance:9", 801, "speaker").DeduplicationKey);
                Check.True(speech.DeduplicationKey != NpcExperienceEvent.FromObservedSpeech("other-listener", "patient",
                    "dialogue-row-9", "conversation-9", "utterance:9", 801, "speaker").DeduplicationKey);
                Check.True(speech.DeduplicationKey != NpcExperienceEvent.FromAttributedSpeech("listener", "patient",
                    "dialogue-row-10", "conversation-9", "utterance:9", NpcExperienceEventKind.StatusOnset, 801,
                    "speaker").DeduplicationKey);
            }));

            cases.Add(Test("Experience event validation rejects invalid identity, time and attribution", () =>
            {
                Check.Throws<ArgumentException>(() => NpcExperienceEvent.FromNativeOutcome(" ", "alice", "r", "e", "o",
                    NpcExperienceEventKind.Injury, 1, NpcResponsibleActorAttribution.Unknown));
                Check.Throws<ArgumentException>(() => NpcExperienceEvent.FromNativeOutcome("alice", "bob", "r", "e\n", "o",
                    NpcExperienceEventKind.Injury, 1, NpcResponsibleActorAttribution.Unknown));
                Check.Throws<ArgumentException>(() => NpcExperienceEvent.FromNativeOutcome("alice", "bob", "r", "e", " ",
                    NpcExperienceEventKind.Injury, 1, NpcResponsibleActorAttribution.Unknown));
                Check.Throws<ArgumentException>(() => NpcExperienceEvent.FromNativeOutcome("alice", "bob", "r", "e", "r",
                    NpcExperienceEventKind.Injury, 1, NpcResponsibleActorAttribution.Unknown));
                Check.Throws<ArgumentException>(() => NpcExperienceEvent.FromAttributedSpeech("alice", "bob", "speech", "e", "e",
                    NpcExperienceEventKind.Injury, 1, "speaker"));
                Check.Throws<ArgumentException>(() => NpcExperienceEvent.FromObservedSpeech("alice", "bob", " ", "episode", "utterance", 1, "speaker"));
                Check.Throws<ArgumentException>(() => NpcExperienceEvent.FromObservedSpeech("alice", "bob", "receipt", "episode", "episode", 1, "speaker"));
                Check.Throws<ArgumentException>(() => NpcExperienceEvent.FromObservedSpeech("alice", "bob", "receipt", "episode", "utterance", 1, " "));
                Check.Throws<ArgumentOutOfRangeException>(() => NpcExperienceEvent.FromObservedSpeech("alice", "bob", "receipt", "episode", "utterance", double.NaN, "speaker"));
                Check.Throws<ArgumentOutOfRangeException>(() => NpcExperienceEvent.FromNativeOutcome("alice", "bob", "r", "e", "o",
                    NpcExperienceEventKind.Injury, double.NaN, NpcResponsibleActorAttribution.Unknown));
                Check.Throws<ArgumentOutOfRangeException>(() => NpcExperienceEvent.FromNativeOutcome("alice", "bob", "r", "e", "o",
                    NpcExperienceEventKind.Injury, -1, NpcResponsibleActorAttribution.Unknown));
                Check.Throws<ArgumentException>(() => NpcExperienceEvent.FromNativeOutcome("alice", "bob", "r", "e", "o",
                    NpcExperienceEventKind.Injury, 1, NpcResponsibleActorAttribution.Known));
                Check.Throws<ArgumentException>(() => NpcExperienceEvent.FromNativeOutcome("alice", "bob", "r", "e", "o",
                    NpcExperienceEventKind.Injury, 1, NpcResponsibleActorAttribution.Unknown, "guessed-actor"));
                Check.Throws<ArgumentOutOfRangeException>(() => NpcExperienceEvent.FromNativeOutcome("alice", "bob", "r", "e", "o",
                    (NpcExperienceEventKind)100, 1, NpcResponsibleActorAttribution.NotApplicable));
                Check.Throws<ArgumentNullException>(() => NpcExperienceEvent.CopyValidated(null));
            }));
        }

        private static (string Name, Action Body) Test(string name, Action body)
        { return (name, body); }
    }
}
