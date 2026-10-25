using System;
using System.Collections.Generic;
using System.Globalization;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcExperienceCrossFactorTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Event-created three-trait profile and appraisal effects persist atomically across save branches", () =>
            {
                var store = new WorldStateStore(2701);
                var resident = store.GetOrCreate(20, new[] { 200 }, 1).Households[0].Residents[0];
                string id = resident.Id;
                uint seed = FindSensitiveResilientSeed(id);
                resident.AppearanceSeed = seed;
                Check.True(store.ReadNpcPersonality(id) == null);
                store.SetConversationBaseline(id, 40, 9);

                var injury = NpcExperienceEvent.FromNativeOutcome(id, id, "receipt:cross-injury",
                    "episode:cross-injury", "outcome:cross-injury", NpcExperienceEventKind.Injury, 10,
                    NpcResponsibleActorAttribution.Known, "player",
                    NpcExperienceImpactDetails.ForHealthEpisode(90, 78, 100, 12, 0, 1));
                Check.True(HistoricalNpcExperienceFixture.ImportHistoricalNativeEvent(store, injury));
                Check.True(!store.TryEnqueueNpcExperience(injury));

                NpcExperienceAppraisalTicket ticket;
                Check.True(store.TryCaptureNpcExperienceAppraisal(id, 10, out ticket));
                var profile = store.ReadNpcPersonality(id);
                Check.Equal(seed, profile.Seed);
                Check.Equal(3, profile.SelectedTraits.Count);
                Check.Equal("highly sensitive", profile.Sensitivity);
                Check.Equal("resilient", profile.EmotionalStability);
                Check.True(ticket.Request.Prompt.Contains("stablePersonality=\"" + profile.Description() + "\""));
                Check.True(ticket.Request.Prompt.Contains("sensitivity highly sensitive"));
                Check.True(ticket.Request.Prompt.Contains("emotional stability resilient"));
                Check.Equal(40, ticket.Request.CurrentAffinity);
                Check.True(ticket.Request.Relationship != null && !ticket.Request.Relationship.TrustKnown);

                const int mood = -6, affinity = -3, trust = -4, resentment = 3, fear = 2;
                string response = Response(ticket.Request, mood, affinity, trust, resentment, fear);
                Check.True(NpcExperienceAppraisalContract.TryParseResponse(ticket.Request, response, out _));

                // A loaded fork gets a new branch identity. A response from the first branch must not commit there.
                var fork = new WorldStateStore(1);
                fork.Restore(store.Snapshot());
                NpcExperienceAppraisalTicket forkTicket;
                Check.True(fork.TryCaptureNpcExperienceAppraisal(id, 10, out forkTicket));
                Check.True(!string.Equals(ticket.Request.SaveBranchId, forkTicket.Request.SaveBranchId, StringComparison.Ordinal));
                Check.True(!fork.TryCommitNpcExperienceAppraisal(forkTicket, response));
                Check.Equal(0L, fork.CaptureConversationMood(id).Revision);
                Check.Equal(1, fork.ReadPendingNpcExperiences(id, 10).Count);

                string forkResponse = Response(forkTicket.Request, mood, affinity, trust, resentment, fear);
                Check.True(fork.TryCommitNpcExperienceAppraisal(forkTicket, forkResponse));
                Check.True(!fork.TryCommitNpcExperienceAppraisal(forkTicket, forkResponse));
                Check.Equal(mood, fork.CaptureConversationMood(id, 10).Value);
                Check.Equal(-3, fork.ConversationDisposition(id));
                int actualAffinity;
                Check.True(fork.TryGetActualConversationDisposition(id, out actualAffinity));
                Check.Equal(37, actualAffinity);
                var forkRelation = fork.ReadNpcRelationship(id, "player");
                Check.True(forkRelation.TrustKnown);
                Check.Equal(46, forkRelation.ReliabilityTrust);
                Check.Equal(3, forkRelation.Resentment);
                Check.Equal(2, forkRelation.InterpersonalFear);
                Check.Equal(0, fork.ReadPendingNpcExperiences(id, 10).Count);

                Check.True(store.TryCommitNpcExperienceAppraisal(ticket, response));
                Check.True(!store.TryCommitNpcExperienceAppraisal(ticket, response));
                Check.Equal(mood, store.CaptureConversationMood(id, 10).Value);
                Check.Equal(-3, store.ConversationDisposition(id));
                Check.True(store.TryGetActualConversationDisposition(id, out actualAffinity));
                Check.Equal(37, actualAffinity);
                var relation = store.ReadNpcRelationship(id, "player");
                Check.True(relation.TrustKnown);
                Check.Equal(46, relation.ReliabilityTrust);
                Check.Equal(3, relation.Resentment);
                Check.Equal(2, relation.InterpersonalFear);
                Check.Equal(0, store.ReadPendingNpcExperiences(id, 10).Count);

                var committedSave = store.Snapshot();
                var restored = new WorldStateStore(2);
                restored.Restore(committedSave);
                var restoredProfile = restored.ReadNpcPersonality(id);
                Check.Equal(profile.Description(), restoredProfile.Description());
                Check.Equal(seed, restoredProfile.Seed);
                Check.Equal(3, restoredProfile.SelectedTraits.Count);
                Check.Equal(mood, restored.CaptureConversationMood(id, 10).Value);
                Check.Equal(-3, restored.ConversationDisposition(id));
                Check.True(restored.TryGetActualConversationDisposition(id, out actualAffinity));
                Check.Equal(37, actualAffinity);
                Check.Equal(46, restored.ReadNpcRelationship(id, "player").ReliabilityTrust);
                var journal = restored.Snapshot().NpcExperienceInbox.Entries[0].Appraisal;
                Check.Equal(mood, journal.MoodDelta);
                Check.Equal(affinity, journal.AffinityDelta);
                Check.Equal(trust, journal.TrustDelta);
                Check.Equal(resentment, journal.ResentmentDelta);
                Check.Equal(fear, journal.FearDelta);
                Check.True(!restored.TryCommitNpcExperienceAppraisal(ticket, response));
                Check.Equal(0, restored.ReadPendingNpcExperiences(id, 10).Count);

                double recoveryTime = 10 + restored.ConversationMoodHalfLifeSeconds(id);
                int expectedRecovered = MoodRecovery.ValueAt(mood, 10, recoveryTime,
                    restored.ConversationMoodHalfLifeSeconds(id));
                Check.Equal(expectedRecovered, restored.CaptureConversationMood(id, recoveryTime).Value);
            }));

            cases.Add(("Unknown gift source cannot establish trust despite known transferred-item valuation", () =>
            {
                var store = NewResident(2702, out var id, out var seed);
                var gift = NpcExperienceEvent.FromNativeOutcome(id, id, "receipt:unknown-gift",
                    "episode:gift", "outcome:gift", NpcExperienceEventKind.GiftReceived, 20,
                    NpcResponsibleActorAttribution.Unknown, null,
                    NpcExperienceImpactDetails.ForItemTransfer(1, NpcExperienceNativeValuationStatus.Known, 3));
                Check.True(HistoricalNpcExperienceFixture.ImportHistoricalNativeEvent(store, gift));
                NpcExperienceAppraisalTicket ticket;
                Check.True(store.TryCaptureNpcExperienceAppraisal(id, 20, out ticket));
                Check.Equal(seed, store.ReadNpcPersonality(id).Seed);
                Check.True(ticket.Request.RelationshipTargetId == null);
                Check.True(ticket.Request.Prompt.Contains("responsibleActorAttribution=Unknown,responsibleActor=unknown"));
                Check.True(ticket.Request.Prompt.Contains("nativeGoldValue=3"));
                Check.True(!NpcExperienceAppraisalContract.TryParseResponse(ticket.Request,
                    Response(ticket.Request, 2, 0, 1, 0, 0), out _));
                Check.True(store.TryCommitNpcExperienceAppraisal(ticket,
                    Response(ticket.Request, 2, 0, 0, 0, 0)));
                Check.True(store.ReadNpcRelationship(id, "player") == null);
                Check.Equal(2, store.CaptureConversationMood(id, 20).Value);
                Check.Equal(0, store.ReadPendingNpcExperiences(id, 20).Count);
            }));

            cases.Add(("Native rescue evidence supports positive reliability without inventing a gift or conversation", () =>
            {
                var store = NewResident(2703, out var id, out _);
                var rescue = NpcExperienceEvent.FromNativeOutcome(id, id, "receipt:rescue",
                    "episode:rescue", "outcome:rescue", NpcExperienceEventKind.CombatHelp, 30,
                    NpcResponsibleActorAttribution.Known, "player");
                Check.True(HistoricalNpcExperienceFixture.ImportHistoricalNativeEvent(store, rescue));
                NpcExperienceAppraisalTicket ticket;
                Check.True(store.TryCaptureNpcExperienceAppraisal(id, 30, out ticket));
                Check.True(ticket.Request.ObservedUtterance == null);
                Check.Equal(0, ticket.Request.ConversationContext.Count);
                Check.True(ticket.Request.Prompt.Contains("kind=CombatHelp"));
                Check.True(store.TryCommitNpcExperienceAppraisal(ticket,
                    Response(ticket.Request, 2, 0, 3, 0, 0)));
                var relationship = store.ReadNpcRelationship(id, "player");
                Check.True(relationship.TrustKnown);
                Check.Equal(53, relationship.ReliabilityTrust);
                Check.Equal(0, store.ReadPendingNpcExperiences(id, 30).Count);
            }));

            cases.Add(("Unattributed status onset leaves a possible poisoner unidentified", () =>
            {
                var store = NewResident(2704, out var id, out _);
                var onset = NpcExperienceEvent.FromNativeOutcome(id, id, "receipt:status-onset",
                    "episode:status-onset", "outcome:status-onset", NpcExperienceEventKind.StatusOnset, 40,
                    NpcResponsibleActorAttribution.Unknown);
                Check.True(HistoricalNpcExperienceFixture.ImportHistoricalNativeEvent(store, onset));
                NpcExperienceAppraisalTicket ticket;
                Check.True(store.TryCaptureNpcExperienceAppraisal(id, 40, out ticket));
                Check.True(ticket.Request.RelationshipTargetId == null);
                Check.True(ticket.Request.Prompt.Contains("responsibleActor=unknown"));
                Check.True(!NpcExperienceAppraisalContract.TryParseResponse(ticket.Request,
                    Response(ticket.Request, -2, 0, -1, 1, 2), out _));
                Check.True(store.TryCommitNpcExperienceAppraisal(ticket,
                    Response(ticket.Request, -2, 0, 0, 0, 0)));
                Check.True(store.ReadNpcRelationship(id, "player") == null);
            }));

            cases.Add(("No-show appraisal requires actual wait and cannot replay its already-applied affinity penalty", () =>
            {
                var store = new WorldStateStore(2705);
                var resident = store.GetOrCreate(100, new[] { 10 }, 1).Households[0].Residents[0];
                string id = resident.Id;
                var booking = new AppointmentRecord
                {
                    Id = "crossfactor-no-show", ResidentId = id, Purpose = "meet",
                    StartsAt = 2000, EndsAt = 2060, GraceSeconds = 30, Importance = 3,
                    Place = new AppointmentPlace { Kind = "building", SettlementId = 100, BuildingKey = 20 }
                };
                Check.True(store.TryReserveAppointment(booking, 500, out var reason), reason);
                double deadline = AppointmentTiming.ArrivalDeadline(store.FindAppointment(booking.Id));
                for (double at = deadline - 40; at < deadline; at += 10)
                    store.ObserveAppointment(booking.Id, at, true, false, false);
                store.ObserveAppointment(booking.Id, deadline, true, false, false);
                store.ObserveAppointment(booking.Id, deadline + 1, false, false, false);
                int nativePenalty = store.ConversationDisposition(id);
                Check.True(nativePenalty < 0);

                double now = deadline + 1;
                NpcExperienceAppraisalTicket ticket;
                Check.True(store.TryCaptureNpcExperienceAppraisal(id, now, out ticket));
                Check.True(ticket.Request.WaitedForAppointment);
                Check.Equal(nativePenalty, ticket.Request.CurrentAffinity);
                Check.True(ticket.Request.Prompt.Contains("affinityDelta must be 0"));
                Check.True(store.TryCommitNpcExperienceAppraisal(ticket,
                    Response(ticket.Request, -2, 0, -2, 1, 1)));
                Check.Equal(nativePenalty, store.ConversationDisposition(id));
                var relationship = store.ReadNpcRelationship(id, "player");
                Check.Equal(48, relationship.ReliabilityTrust);
                Check.Equal(1, relationship.Resentment);
                Check.Equal(1, relationship.InterpersonalFear);
                Check.True(!store.TryCommitNpcExperienceAppraisal(ticket,
                    Response(ticket.Request, -2, 0, -2, 1, 1)));
                Check.Equal(nativePenalty, store.ConversationDisposition(id));
                Check.Equal(0, store.ReadPendingNpcExperiences(id, 10).Count);
                var profile = store.ReadNpcPersonality(id);
                Check.Equal(resident.AppearanceSeed, profile.Seed);
                Check.Equal(3, profile.SelectedTraits.Count);
            }));
        }

        private static WorldStateStore NewResident(uint worldSeed, out string id, out uint seed)
        {
            var store = new WorldStateStore(worldSeed);
            var resident = store.GetOrCreate(20, new[] { 200 }, 1).Households[0].Residents[0];
            id = resident.Id;
            seed = resident.AppearanceSeed;
            Check.True(store.ReadNpcPersonality(id) == null);
            return store;
        }

        private static uint FindSensitiveResilientSeed(string id)
        {
            for (uint seed = 1; seed <= 2048; seed++)
            {
                var profile = NpcPersonalityGenerator.Generate(id, seed, NpcSocialBackground.Unknown);
                if (profile.Sensitivity == "highly sensitive" && profile.EmotionalStability == "resilient")
                    return seed;
            }
            throw new InvalidOperationException("No fixture seed supplied the requested independent personality axes.");
        }

        private static string Response(NpcExperienceAppraisalRequest request, int mood, int affinity,
            int trust, int resentment, int fear)
        {
            return "{\"schemaVersion\":" + NpcExperienceAppraisalContract.SchemaVersion.ToString(CultureInfo.InvariantCulture) +
                ",\"policyVersion\":" + NpcExperienceAppraisalContract.PolicyVersion.ToString(CultureInfo.InvariantCulture) +
                ",\"saveBranchId\":" + Quote(request.SaveBranchId) + ",\"observerId\":" + Quote(request.ObserverId) +
                ",\"subjectPersonId\":" + Quote(request.SubjectPersonId) +
                ",\"evidenceDeduplicationKey\":" + Quote(request.EvidenceDeduplicationKey) +
                ",\"canonicalEvidenceId\":" + Quote(request.CanonicalEvidenceId) +
                ",\"relationshipTargetId\":" + Quote(request.RelationshipTargetId ?? "") +
                ",\"expectedMoodRevision\":" + request.ExpectedMoodRevision.ToString(CultureInfo.InvariantCulture) +
                ",\"expectedAffinityRevision\":" + request.ExpectedAffinityRevision.ToString(CultureInfo.InvariantCulture) +
                ",\"expectedRelationshipRevision\":" + request.ExpectedRelationshipRevision.ToString(CultureInfo.InvariantCulture) +
                ",\"moodDelta\":" + mood.ToString(CultureInfo.InvariantCulture) +
                ",\"affinityDelta\":" + affinity.ToString(CultureInfo.InvariantCulture) +
                ",\"trustDelta\":" + trust.ToString(CultureInfo.InvariantCulture) +
                ",\"resentmentDelta\":" + resentment.ToString(CultureInfo.InvariantCulture) +
                ",\"fearDelta\":" + fear.ToString(CultureInfo.InvariantCulture) +
                ",\"speechActKind\":\"NotApplicable\",\"speechTargetPersonId\":\"\",\"conversationOption\":\"NoAction\"}";
        }

        private static string Quote(string value)
        { return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\""; }
    }
}
