using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcExperienceCommitTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Speech classification is required, exact-request bound, stale-safe and committed once", () =>
            {
                string id; var store = Create(out id);
                EnqueueSpeech(store, id, "utterance:stage", "The robber said he would hurt you.", 10);
                NpcExperienceAppraisalTicket first, second;
                Check.True(store.TryCaptureNpcExperienceAppraisal(id, 10, out first));
                Check.True(store.TryCaptureNpcExperienceAppraisal(id, 10, out second));
                string neutral = Result(first.Request).Replace("\"NotApplicable\"", "\"ReportedThreat\"");
                Check.True(!store.TryCommitNpcExperienceAppraisal(first, neutral));
                NpcSpeechActClassification classification;
                Check.True(NpcSpeechActClassification.TryAccept(NpcSpeechActRequest.ForAppraisal(first.Request), "ReportedThreat", out classification));
                Check.True(!store.TryBindNpcExperienceSpeechClassification(second, classification));
                Check.True(store.TryBindNpcExperienceSpeechClassification(first, classification));
                Check.True(!store.TryBindNpcExperienceSpeechClassification(first, classification));
                string substituted = neutral.Replace("\"ReportedThreat\"", "\"DirectThreat\"");
                Check.True(!store.TryCommitNpcExperienceAppraisal(first, substituted));
                Check.Equal(1, store.ReadPendingNpcExperiences(id, 10).Count);
                Check.True(store.TryCommitNpcExperienceAppraisal(first, neutral));
                Check.True(!store.TryCommitNpcExperienceAppraisal(first, neutral));
                Check.Equal(0, store.ConversationDisposition(id));
                Check.Equal(NpcExperienceSpeechActKind.ReportedThreat, store.Snapshot().NpcExperienceInbox.Entries[0].Appraisal.SpeechActKind);

                EnqueueSpeech(store, id, "utterance:stage-stale", "Would that be a threat in a play?", 11);
                Check.True(store.TryCaptureNpcExperienceAppraisal(id, 11, out first));
                Check.True(NpcSpeechActClassification.TryAccept(NpcSpeechActRequest.ForAppraisal(first.Request), "HypotheticalThreat", out classification));
                store.Restore(store.Snapshot());
                Check.True(!store.TryBindNpcExperienceSpeechClassification(first, classification));
                Check.True(!store.TryCommitNpcExperienceAppraisal(first, neutral));
                Check.Equal(1, store.ReadPendingNpcExperiences(id, 10).Count);
            }));
            cases.Add(("Native event capture initializes and saves a deterministic profile for an eligible unseen resident", new Action(() =>
            {
                var store = new WorldStateStore(109);
                var resident = store.GetOrCreate(20, new[] { 200 }, 1).Households[0].Residents[0];
                string id = resident.Id;
                Check.True(store.ReadNpcPersonality(id) == null);
                Enqueue(store, id, "first-event-profile", 10);
                var duplicate = NpcExperienceEvent.FromNativeOutcome(id, id, "receipt:first-event-profile",
                    "episode:commit", "first-event-profile", NpcExperienceEventKind.Healing, 10,
                    NpcResponsibleActorAttribution.Known, "player");
                Check.True(!store.TryEnqueueNpcExperience(duplicate));

                NpcExperienceAppraisalTicket ticket;
                Check.True(store.TryCaptureNpcExperienceAppraisal(id, 10, out ticket));
                var profile = store.ReadNpcPersonality(id);
                Check.True(profile != null);
                Check.Equal(resident.AppearanceSeed, profile.Seed);
                Check.Equal(NpcSocialBackground.Unknown, profile.Background);
                Check.Equal(3, profile.SelectedTraits.Count);
                // The request carries an empty revision-0 CAS view for the native responsible actor;
                // trust itself must stay unknown until evidence creates a relationship entry.
                Check.Equal(0L, ticket.Request.ExpectedRelationshipRevision);
                Check.True(ticket.Request.Relationship != null && !ticket.Request.Relationship.TrustKnown);
                Check.True(store.ReadNpcRelationship(id, "player") == null);
                Check.True(store.TryCommitNpcExperienceAppraisal(ticket, Result(ticket.Request, mood: 2)));

                var restored = new WorldStateStore(0);
                restored.Restore(store.Snapshot());
                var restoredProfile = restored.ReadNpcPersonality(id);
                Check.Equal(profile.Description(), restoredProfile.Description());
                Check.Equal(profile.Seed, restoredProfile.Seed);
                Check.Equal(profile.Background, restoredProfile.Background);
                Check.Equal(3, restoredProfile.SelectedTraits.Count);
                Check.Equal(0, restored.ReadPendingNpcExperiences(id, 10).Count);
                Check.True(restored.ReadNpcRelationship(id, "player") == null);
            })));

            cases.Add(("Native event capture keeps ordinary personality for a generic sidequest resident", new Action(() =>
            {
                var store = new WorldStateStore(109);
                var resident = store.GetOrCreate(20, new[] { 200 }, 1).Households[0].Residents[0];
                resident.IsQuestProtected = true;
                Enqueue(store, resident.Id, "protected-event-profile", 10);
                NpcExperienceAppraisalTicket ticket;
                Check.True(store.TryCaptureNpcExperienceAppraisal(resident.Id, 10, out ticket));
                Check.True(ticket != null);
                Check.Equal(3, store.ReadNpcPersonality(resident.Id).SelectedTraits.Count);
                Check.Equal(1, store.ReadPendingNpcExperiences(resident.Id, 10).Count);
            })));

            cases.Add(("Native event capture defers a factioned resident until background is authoritative", new Action(() =>
            {
                var store = new WorldStateStore(109);
                var resident = store.GetOrCreate(20, new[] { 200 }, 1).Households[0].Residents[0];
                Enqueue(store, resident.Id, "factioned-event-profile", 10);
                resident = store.GetOrCreate(20, new[] { 200 }, 1).Households[0].Residents[0];
                resident.NativeIdentity = new NativePerson { Id = resident.Id, FactionId = 42 };

                NpcExperienceAppraisalTicket ticket;
                Check.True(!store.TryCaptureNpcExperienceAppraisal(resident.Id, 10, out ticket));
                Check.True(ticket == null);
                Check.True(store.ReadNpcPersonality(resident.Id) == null);
                Check.Equal(1, store.ReadPendingNpcExperiences(resident.Id, 10).Count);

                uint seed;
                int factionId;
                bool questProtected;
                Check.True(store.TryReadNpcPersonalityIdentity(resident.Id, out seed, out factionId, out questProtected));
                Check.Equal(resident.AppearanceSeed, seed);
                Check.Equal(42, factionId);
                Check.True(!questProtected);
                Check.True(store.ReadNpcPersonality(resident.Id) == null);

                Check.True(store.TryCaptureNpcExperienceAppraisal(resident.Id, 10, NpcSocialBackground.Noble, out ticket));
                var profile = store.ReadNpcPersonality(resident.Id);
                Check.Equal(NpcSocialBackground.Noble, profile.Background);
                Check.Equal(resident.AppearanceSeed, profile.Seed);
            })));

            cases.Add(("Native faction zero is a no-faction sentinel for event-time Unknown profile capture", new Action(() =>
            {
                var store = new WorldStateStore(109);
                var resident = store.GetOrCreate(20, new[] { 200 }, 1).Households[0].Residents[0];
                NativePerson native = store.RegisterNativePerson(20, 200, 731, "Factionless native",
                    resident.Sex, 0, 0, 0, 0);
                resident.NativeIdentity = native;
                Enqueue(store, resident.Id, "faction-zero-native-health-event", 10);

                NpcExperienceAppraisalTicket ticket;
                Check.True(store.TryCaptureNpcExperienceAppraisal(resident.Id, 10, out ticket));
                Check.True(ticket != null && ticket.Request.ObserverId == resident.Id);
                NpcPersonalityProfile profile = store.ReadNpcPersonality(resident.Id);
                Check.True(profile != null);
                Check.Equal(NpcSocialBackground.Unknown, profile.Background);
                Check.Equal(resident.AppearanceSeed, profile.Seed);
                Check.Equal(1, store.ReadPendingNpcExperiences(resident.Id, 10).Count);
            })));

            cases.Add(("Unresolved positive native faction defers event-time profile without saving Unknown", new Action(() =>
            {
                var store = new WorldStateStore(109);
                var resident = store.GetOrCreate(20, new[] { 200 }, 1).Households[0].Residents[0];
                Enqueue(store, resident.Id, "unresolved-positive-faction-health-event", 10);
                resident = store.GetOrCreate(20, new[] { 200 }, 1).Households[0].Residents[0];
                resident.NativeIdentity = new NativePerson { Id = resident.Id, FactionId = 2147483647 };

                NpcExperienceAppraisalTicket ticket;
                Check.True(!store.TryCaptureNpcExperienceAppraisal(resident.Id, 10, out ticket));
                Check.True(ticket == null);
                Check.True(store.ReadNpcPersonality(resident.Id) == null);
                Check.Equal(1, store.ReadPendingNpcExperiences(resident.Id, 10).Count);
            })));

            cases.Add(("Experience commit consumes pending event and mood exactly once without adding a chat turn", () =>
            {
                string id; var store = Create(out id); Enqueue(store,id,"one",10);
                NpcExperienceAppraisalTicket ticket;
                Check.True(store.TryCaptureNpcExperienceAppraisal(id,10,out ticket));
                Check.True(store.TryCommitNpcExperienceAppraisal(ticket,Result(ticket.Request,4)));
                Check.Equal(4,store.CaptureConversationMood(id,10).Value);
                Check.Equal(0L,store.CaptureConversationMood(id).AcceptedTurnSequence);
                Check.Equal(1L,store.CaptureConversationMood(id).Revision);
                Check.Equal(0,store.ReadPendingNpcExperiences(id,10).Count);
                Check.True(!store.TryCommitNpcExperienceAppraisal(ticket,Result(ticket.Request,4)));
            }));
            cases.Add(("Experience stale reply and restore epoch reject every effect and keep event pending", () =>
            {
                string id; var store = Create(out id); Enqueue(store,id,"stale",10);
                NpcExperienceAppraisalTicket ticket;
                Check.True(store.TryCaptureNpcExperienceAppraisal(id,10,out ticket));
                long sequence;
                Check.True(store.TryCommitAcceptedSocialExchange(id,0,0,2,11,"date","place","hi","hello",out sequence));
                Check.True(!store.TryCommitNpcExperienceAppraisal(ticket,Result(ticket.Request,4)));
                Check.Equal(2,store.CaptureConversationMood(id,11).Value);
                Check.Equal(1,store.ReadPendingNpcExperiences(id,10).Count);
                Check.True(store.TryCaptureNpcExperienceAppraisal(id,12,out ticket));
                store.Restore(store.Snapshot());
                Check.True(!store.TryCommitNpcExperienceAppraisal(ticket,Result(ticket.Request,4)));
                Check.Equal(1,store.ReadPendingNpcExperiences(id,10).Count);
            }));
            cases.Add(("Overdue experience decays before application instead of blocking behind later conversation", () =>
            {
                string id; var store=Create(out id); Enqueue(store,id,"old",10);
                double now=10+store.ConversationMoodHalfLifeSeconds(id);
                long sequence;
                Check.True(store.TryCommitAcceptedSocialExchange(id,0,0,6,now,"date","place","hi","hello",out sequence));
                NpcExperienceAppraisalTicket ticket;
                Check.True(store.TryCaptureNpcExperienceAppraisal(id,now,out ticket));
                Check.True(store.TryCommitNpcExperienceAppraisal(ticket,Result(ticket.Request,-4)));
                Check.Equal(4,store.CaptureConversationMood(id,now).Value);
                Check.Equal(1L,store.CaptureConversationMood(id).AcceptedTurnSequence);
            }));
            cases.Add(("Experience pending queue is ordered and detached while malformed response changes nothing", () =>
            {
                string id; var store=Create(out id); Enqueue(store,id,"later",20); Enqueue(store,id,"earlier",10);
                NpcExperienceAppraisalTicket ticket;
                Check.True(store.TryCaptureNpcExperienceAppraisal(id,30,out ticket));
                Check.Equal("earlier",ticket.Request.CanonicalEvidenceId);
                Check.True(!store.TryCommitNpcExperienceAppraisal(ticket,"{}"));
                Check.Equal(2,store.ReadPendingNpcExperiences(id,10).Count);
                Check.Equal(0L,store.CaptureConversationMood(id).Revision);
                Check.True(store.TryCommitNpcExperienceAppraisal(ticket,Result(ticket.Request,0)));
                Check.True(store.TryCaptureNpcExperienceAppraisal(id,30,out ticket));
                Check.Equal("later",ticket.Request.CanonicalEvidenceId);
            }));
            cases.Add(("Experience zero appraisal persists consumed state and rollback restores pending outcome", () =>
            {
                string id; var store=Create(out id); Enqueue(store,id,"zero",10); var before=store.Snapshot();
                NpcExperienceAppraisalTicket ticket;
                Check.True(store.TryCaptureNpcExperienceAppraisal(id,10,out ticket));
                Check.True(store.TryCommitNpcExperienceAppraisal(ticket,Result(ticket.Request,0)));
                var after=store.Snapshot();
                var restored=new WorldStateStore(1); restored.Restore(after);
                Check.Equal(0,restored.ReadPendingNpcExperiences(id,10).Count);
                Check.True(!restored.TryCommitNpcExperienceAppraisal(ticket,Result(ticket.Request,0)));
                restored.Restore(before);
                Check.Equal(1,restored.ReadPendingNpcExperiences(id,10).Count);
            }));

            cases.Add(("Speech appraisal context contains only prior persisted receipt-bound turns", () =>
            {
                string id; var store = Create(out id);
                EnqueueSpeech(store, id, "utterance:prior", "prior receipt text", 10);
                NpcExperienceAppraisalTicket ticket;
                Check.True(store.TryCaptureNpcExperienceAppraisal(id, 10, out ticket));
                Check.Equal("prior receipt text", ticket.Request.ObservedUtterance);
                Check.Equal(0, ticket.Request.ConversationContext.Count);
                Check.True(store.TryCommitNpcExperienceAppraisal(ticket, SpeechResult(store, ticket)));

                EnqueueSpeech(store, id, "utterance:current", "current receipt text", 11);
                Check.True(store.TryCaptureNpcExperienceAppraisal(id, 11, out ticket));
                Check.Equal("current receipt text", ticket.Request.ObservedUtterance);
                Check.Equal(1, ticket.Request.ConversationContext.Count);
                Check.Equal("speech:utterance:prior", ticket.Request.ConversationContext[0].ReceiptId);
                Check.Equal("prior receipt text", ticket.Request.ConversationContext[0].Text);
                Check.Equal(10d, ticket.Request.ConversationContext[0].ObservedGameTime);
                Check.Equal("player", ticket.Request.ConversationContext[0].SpeakerId);
            }));

            cases.Add(("Masked direct insult applies bounded supplemental affinity once", () =>
            {
                string id; var store = Create(out id);
                store.SetConversationBaseline(id, 60, 10);
                var evidence = NpcExperienceEvent.FromObservedSpeech(id, id, "speech:masked-insult",
                    "episode:heard", "utterance:masked-insult", 10, "player");
                var turn = NpcObservedConversationTurn.Create(id, evidence.EpisodeId, evidence.AttributedSpeakerId,
                    evidence.ReceiptId, "You are worthless.", 10);
                HistoricalNpcObservedSpeechFixture.Import(store, evidence, turn,
                    NpcExperienceAppliedEffectsMask.Affinity);

                NpcExperienceAppraisalTicket ticket;
                Check.True(store.TryCaptureNpcExperienceAppraisal(id, 10, out ticket));
                Check.True(!ticket.Request.ImmediateAffinityDelta.HasValue);
                BindSpeech(store, ticket, "DirectInsult");
                string result = Result(ticket.Request, affinity: -12)
                    .Replace("\"speechActKind\":\"NotApplicable\"", "\"speechActKind\":\"DirectInsult\"")
                    .Replace("\"speechTargetPersonId\":\"\"", "\"speechTargetPersonId\":\"" + id + "\"");
                Check.True(store.TryCommitNpcExperienceAppraisal(ticket, result));

                Check.Equal(-12, store.ConversationDisposition(id));
                Check.Equal(-12, store.Snapshot().NpcExperienceInbox.Entries[0].Appraisal.AppliedAffinityDelta);
                Check.True(!store.Snapshot().NpcExperienceInbox.Entries[0].HasKnownImmediateAffinityDelta);
                Check.True(!store.TryCommitNpcExperienceAppraisal(ticket, result));
                Check.Equal(-12, store.ConversationDisposition(id));
                var legacyRestored = new WorldStateStore(0);
                legacyRestored.Restore(store.Snapshot());
                Check.True(!legacyRestored.Snapshot().NpcExperienceInbox.Entries[0].HasKnownImmediateAffinityDelta);
                Check.Equal(-12, legacyRestored.ConversationDisposition(id));
            }));

            cases.Add(("Known native tone delta and severe supplement persist as separate receipt-bound effects", () =>
            {
                string id; var store = Create(out id);
                store.SetConversationBaseline(id, 60, 10);
                int nativeDelta = store.ApplyConversationTone(id, "insult", "You are worthless.", 10);
                Check.Equal(-10, nativeDelta);
                var evidence = NpcExperienceEvent.FromObservedSpeech(id, id, "speech:known-insult",
                    "episode:heard", "utterance:known-insult", 10, "player");
                var turn = NpcObservedConversationTurn.Create(id, evidence.EpisodeId, evidence.AttributedSpeakerId,
                    evidence.ReceiptId, "You are worthless.", 10);
                var receipt = NpcObservedSpeechReceipt.Create(evidence, turn).WithAppliedAffinity(nativeDelta);
                HistoricalNpcObservedSpeechFixture.Import(store, receipt,
                    NpcExperienceAppliedEffectsMask.Affinity);

                NpcExperienceAppraisalTicket ticket;
                Check.True(store.TryCaptureNpcExperienceAppraisal(id, 10, out ticket));
                Check.Equal(-10, ticket.Request.ImmediateAffinityDelta.Value);
                BindSpeech(store, ticket, "DirectInsult");
                Check.Equal(NpcObservedSpeechAffinityExclusion.None, ticket.Request.AffinityExclusion);
                string result = Result(ticket.Request, affinity: -12)
                    .Replace("\"speechActKind\":\"NotApplicable\"", "\"speechActKind\":\"DirectInsult\"")
                    .Replace("\"speechTargetPersonId\":\"\"", "\"speechTargetPersonId\":\"" + id + "\"");
                Check.True(store.TryCommitNpcExperienceAppraisal(ticket, result));
                Check.Equal(-22, store.ConversationDisposition(id));

                var saved = store.Snapshot();
                var savedEntry = saved.NpcExperienceInbox.Entries[0];
                Check.True(savedEntry.HasKnownImmediateAffinityDelta);
                Check.Equal(-10, savedEntry.ImmediateAffinityDelta);
                Check.Equal(NpcObservedSpeechAffinityExclusion.None, savedEntry.AffinityExclusion);
                Check.Equal(-12, savedEntry.Appraisal.AffinityDelta);
                Check.Equal(-12, savedEntry.Appraisal.AppliedAffinityDelta);
                Check.Equal(NpcExperienceSpeechActKind.DirectInsult, savedEntry.Appraisal.SpeechActKind);
                Check.Equal(id, savedEntry.Appraisal.SpeechTargetPersonId);

                var restored = new WorldStateStore(0);
                restored.Restore(saved);
                var restoredEntry = restored.Snapshot().NpcExperienceInbox.Entries[0];
                Check.True(restoredEntry.HasKnownImmediateAffinityDelta);
                Check.Equal(-10, restoredEntry.ImmediateAffinityDelta);
                Check.Equal(-12, restoredEntry.Appraisal.AppliedAffinityDelta);
                Check.Equal(-22, restored.ConversationDisposition(id));
                Check.True(!store.TryCommitNpcExperienceAppraisal(ticket, result));
                Check.Equal(-22, store.ConversationDisposition(id));

                var tamperedActBound = store.Snapshot();
                tamperedActBound.NpcExperienceInbox.Entries[0].Appraisal.AffinityDelta = -50;
                tamperedActBound.NpcExperienceInbox.Entries[0].Appraisal.AppliedAffinityDelta = -50;
                var invalidActBoundRestore = new WorldStateStore(0);
                Check.Throws<ArgumentException>(() => invalidActBoundRestore.Restore(tamperedActBound));
            }));

            cases.Add(("Family-threat floor excludes affinity supplement while mood remains eligible", new Action(() =>
            {
                string id; var store = Create(out id);
                var seeded = store.Snapshot();
                seeded.ConversationAttitudes.Add(new ConversationAttitude
                {
                    PersonId = id, Offset = 100, NativeBaseline = 100, HasNativeBaseline = true,
                    NativeFactionId = -1, HasNativeFactionId = false
                });
                store.Restore(seeded);
                Check.Equal(100, store.ConversationDisposition(id));
                int beforeFloor = store.ConversationDisposition(id);
                store.SetConversationThreatDisposition(id, 100, 101 * 86400d);
                int familyFloorDelta = store.ConversationDisposition(id) - beforeFloor;
                Check.Equal(-300, familyFloorDelta);

                var evidence = NpcExperienceEvent.FromObservedSpeech(id, id, "speech:family-threat",
                    "episode:family", "utterance:family-threat", 101 * 86400d, "player");
                var turn = NpcObservedConversationTurn.Create(id, evidence.EpisodeId, evidence.AttributedSpeakerId,
                    evidence.ReceiptId, "I will hurt your family.", 101 * 86400d);
                var receipt = NpcObservedSpeechReceipt.Create(evidence, turn).WithAppliedAffinity(familyFloorDelta,
                    NpcObservedSpeechAffinityExclusion.FamilyThreatDispositionFloor);
                HistoricalNpcObservedSpeechFixture.Import(store, receipt,
                    NpcExperienceAppliedEffectsMask.Affinity);

                NpcExperienceAppraisalTicket ticket;
                Check.True(store.TryCaptureNpcExperienceAppraisal(id, 101 * 86400d, out ticket));
                Check.Equal(-300, ticket.Request.ImmediateAffinityDelta.Value);
                Check.Equal(NpcObservedSpeechAffinityExclusion.FamilyThreatDispositionFloor, ticket.Request.AffinityExclusion);
                Check.True(ticket.Request.Prompt.Contains("affinityDelta [0,0]", StringComparison.Ordinal));
                string forbidden = Result(ticket.Request, affinity: -12)
                    .Replace("\"speechActKind\":\"NotApplicable\"", "\"speechActKind\":\"DirectThreat\"")
                    .Replace("\"speechTargetPersonId\":\"\"", "\"speechTargetPersonId\":\"" + id + "\"");
                Check.True(!store.TryCommitNpcExperienceAppraisal(ticket, forbidden));
                Check.Equal(-200, store.ConversationDisposition(id));
                Check.Equal(1, store.ReadPendingNpcExperiences(id, 10).Count);

                BindSpeech(store, ticket, "DirectThreat");
                string permitted = Result(ticket.Request, mood: 2)
                    .Replace("\"speechActKind\":\"NotApplicable\"", "\"speechActKind\":\"DirectThreat\"")
                    .Replace("\"speechTargetPersonId\":\"\"", "\"speechTargetPersonId\":\"" + id + "\"");
                Check.True(store.TryCommitNpcExperienceAppraisal(ticket, permitted));
                Check.Equal(-200, store.ConversationDisposition(id));
                Check.Equal(2, store.CaptureConversationMood(id, 101 * 86400d).Value);
                Check.Equal(0, store.Snapshot().NpcExperienceInbox.Entries[0].Appraisal.AppliedAffinityDelta);
            })));

            cases.Add(("Native affinity provenance rejects forged binding and missing affinity mask", () =>
            {
                string id; var store = Create(out id);
                var source = NpcObservedSpeechTestFixture.Issue(store, id, "You are rude.", 10);
                var evidence = source.Receipt.Evidence;
                var validTurn = source.Receipt.Turn;
                var mismatchedTurn = NpcObservedConversationTurn.Create(id, evidence.EpisodeId, "npc:other",
                    evidence.ReceiptId, "You are rude.", 10);
                Check.Throws<ArgumentException>(() => NpcObservedSpeechReceipt.Create(evidence, mismatchedTurn));

                var receipt = source.Receipt.WithAppliedAffinity(-10);
                Check.Equal(NpcObservedSpeechAcceptanceResult.Rejected,
                    store.TryAcceptNpcObservedSpeechAfterAppend(source, receipt, NpcExperienceAppliedEffectsMask.None));
                Check.Equal(0, store.Snapshot().NpcExperienceInbox.Entries.Count);
                Check.Throws<InvalidOperationException>(() => receipt.WithAppliedAffinity(-2));
                Check.Throws<ArgumentOutOfRangeException>(() =>
                    NpcObservedSpeechReceipt.Create(evidence, validTurn).WithAppliedAffinity(-301));

                Check.Equal(NpcObservedSpeechAcceptanceResult.Projected,
                    store.TryAcceptNpcObservedSpeechAfterAppend(source, receipt, NpcExperienceAppliedEffectsMask.Affinity));
                var tampered = store.Snapshot();
                tampered.NpcExperienceInbox.Entries[0].ImmediateAffinityDelta = 301;
                var restored = new WorldStateStore(0);
                Check.Throws<ArgumentException>(() => restored.Restore(tampered));

                var tamperedMask = store.Snapshot();
                tamperedMask.NpcExperienceInbox.Entries[0].AlreadyAppliedEffectsMask = NpcExperienceAppliedEffectsMask.None;
                var invalidMaskRestore = new WorldStateStore(0);
                Check.Throws<ArgumentException>(() => invalidMaskRestore.Restore(tamperedMask));
            }));

            cases.Add(("Known zero immediate affinity remains distinct from legacy unknown through restore", () =>
            {
                string id; var store = Create(out id);
                var evidence = NpcExperienceEvent.FromObservedSpeech(id, id, "speech:known-zero",
                    "episode:zero", "utterance:known-zero", 10, "player");
                var turn = NpcObservedConversationTurn.Create(id, evidence.EpisodeId, evidence.AttributedSpeakerId,
                    evidence.ReceiptId, "I said hello.", 10);
                var receipt = NpcObservedSpeechReceipt.Create(evidence, turn).WithAppliedAffinity(0);
                HistoricalNpcObservedSpeechFixture.Import(store, receipt,
                    NpcExperienceAppliedEffectsMask.Affinity);
                Check.True(store.TryCaptureNpcExperienceAppraisal(id, 10, out var ticket));
                Check.True(ticket.Request.ImmediateAffinityDelta.HasValue);
                Check.Equal(0, ticket.Request.ImmediateAffinityDelta.Value);
                Check.True(store.TryCommitNpcExperienceAppraisal(ticket, SpeechResult(store, ticket)));
                var saved = store.Snapshot();
                Check.True(saved.NpcExperienceInbox.Entries[0].HasKnownImmediateAffinityDelta);
                Check.Equal(0, saved.NpcExperienceInbox.Entries[0].ImmediateAffinityDelta);
                saved.NpcExperienceInbox.Entries[0].Appraisal.Version = 1;
                saved.NpcExperienceInbox.Entries[0].Appraisal.SpeechActKind = NpcExperienceSpeechActKind.NotApplicable;
                saved.NpcExperienceInbox.Entries[0].Appraisal.SpeechTargetPersonId = null;
                var restored = new WorldStateStore(0);
                restored.Restore(saved);
                var restoredEntry = restored.Snapshot().NpcExperienceInbox.Entries[0];
                Check.True(restoredEntry.HasKnownImmediateAffinityDelta);
                Check.Equal(0, restoredEntry.ImmediateAffinityDelta);
                Check.Equal(1, restoredEntry.Appraisal.Version);
            }));

            cases.Add(("Speech context is bounded by complete recent turns in deterministic order", () =>
            {
                string id; var store = Create(out id);
                string longText = new string('x', 850);
                for (int i = 0; i < 3; i++)
                {
                    EnqueueSpeech(store, id, "utterance:bounded-" + i, longText + i, 20 + i);
                    NpcExperienceAppraisalTicket prior;
                    Check.True(store.TryCaptureNpcExperienceAppraisal(id, 20 + i, out prior));
                    Check.True(store.TryCommitNpcExperienceAppraisal(prior, SpeechResult(store, prior)));
                }

                EnqueueSpeech(store, id, "utterance:bounded-current", "current", 23);
                NpcExperienceAppraisalTicket current;
                Check.True(store.TryCaptureNpcExperienceAppraisal(id, 23, out current));
                Check.Equal(2, current.Request.ConversationContext.Count);
                Check.Equal("speech:utterance:bounded-1", current.Request.ConversationContext[0].ReceiptId);
                Check.Equal("speech:utterance:bounded-2", current.Request.ConversationContext[1].ReceiptId);
                Check.Equal(longText + "1", current.Request.ConversationContext[0].Text);
                Check.Equal(longText + "2", current.Request.ConversationContext[1].Text);
            }));

            cases.Add(("Native outcome appraisal does not invent a speech turn or speaker rank", () =>
            {
                string id; var store = Create(out id);
                EnqueueSpeech(store, id, "utterance:prior-only", "prior speech", 10);
                NpcExperienceAppraisalTicket speech;
                Check.True(store.TryCaptureNpcExperienceAppraisal(id, 10, out speech));
                Check.True(store.TryCommitNpcExperienceAppraisal(speech, SpeechResult(store, speech)));

                Enqueue(store, id, "native-after-speech", 11);
                NpcExperienceAppraisalTicket native;
                Check.True(store.TryCaptureNpcExperienceAppraisal(id, 11, out native));
                Check.True(native.Request.ObservedUtterance == null);
                Check.Equal(0, native.Request.ConversationContext.Count);
                Check.True(native.Request.KnownSpeakerRankId == null);
            }));

            cases.Add(("Mood affinity and trust commit together and survive restore exactly once", new Action(() =>
            {
                string id; var store = Create(out id);
                store.SetConversationBaseline(id, 40, 10);
                Enqueue(store, id, "combined", 10);
                NpcExperienceAppraisalTicket ticket;
                Check.True(store.TryCaptureNpcExperienceAppraisal(id, 10, out ticket));
                Check.Equal(40, ticket.Request.CurrentAffinity);
                string result = Result(ticket.Request, mood: 4, affinity: 3, trust: 4);
                Check.True(store.TryCommitNpcExperienceAppraisal(ticket, result));
                Check.Equal(4, store.CaptureConversationMood(id, 10).Value);
                Check.Equal(1L, store.CaptureConversationMood(id).Revision);
                int actualAffinity;
                Check.True(store.TryGetActualConversationDisposition(id, out actualAffinity));
                Check.Equal(43, actualAffinity);
                Check.Equal(3, store.ConversationDisposition(id));
                var relation = store.ReadNpcRelationship(id, "player");
                Check.True(relation.TrustKnown);
                Check.Equal(54, relation.ReliabilityTrust);
                Check.Equal(0, store.ReadPendingNpcExperiences(id, 10).Count);
                Check.True(!store.TryCommitNpcExperienceAppraisal(ticket, result));

                var saved = store.Snapshot();
                var restored = new WorldStateStore(0);
                restored.Restore(saved);
                Check.Equal(0, restored.ReadPendingNpcExperiences(id, 10).Count);
                Check.Equal(4, restored.CaptureConversationMood(id, 10).Value);
                Check.True(restored.TryGetActualConversationDisposition(id, out actualAffinity));
                Check.Equal(43, actualAffinity);
                Check.Equal(54, restored.ReadNpcRelationship(id, "player").ReliabilityTrust);
                Check.Equal(4, saved.NpcExperienceInbox.Entries[0].Appraisal.MoodDelta);
                Check.Equal(3, saved.NpcExperienceInbox.Entries[0].Appraisal.AppliedAffinityDelta);
            })));

            cases.Add(("Stale affinity and invalid affinity proposal leave the transaction untouched", new Action(() =>
            {
                string id; var store = Create(out id);
                store.SetConversationBaseline(id, 20, 10);
                Enqueue(store, id, "stale-affinity", 10);
                NpcExperienceAppraisalTicket ticket;
                Check.True(store.TryCaptureNpcExperienceAppraisal(id, 10, out ticket));
                string response = Result(ticket.Request, mood: 4, affinity: 1, trust: 3);
                store.SetConversationBaseline(id, 21, 10);
                Check.True(!store.TryCommitNpcExperienceAppraisal(ticket, response));
                Check.Equal(0L, store.CaptureConversationMood(id).Revision);
                Check.Equal(1, store.ReadPendingNpcExperiences(id, 10).Count);
                Check.Equal(0L, store.NpcRelationshipLedgerRevision);
                Check.True(store.ReadNpcRelationship(id, "player") == null);
                int actualAffinity;
                Check.True(store.TryGetActualConversationDisposition(id, out actualAffinity));
                Check.Equal(21, actualAffinity);

                Check.True(store.TryCaptureNpcExperienceAppraisal(id, 10, out ticket));
                Check.True(!store.TryCommitNpcExperienceAppraisal(ticket,
                    Result(ticket.Request, mood: 4, affinity: 6, trust: 3)));
                Check.Equal(0L, store.CaptureConversationMood(id).Revision);
                Check.Equal(1, store.ReadPendingNpcExperiences(id, 10).Count);
                Check.Equal(0L, store.NpcRelationshipLedgerRevision);
                Check.True(store.ReadNpcRelationship(id, "player") == null);
                Check.True(store.TryGetActualConversationDisposition(id, out actualAffinity));
                Check.Equal(21, actualAffinity);
            })));

            cases.Add(("Corrupted consumed appraisal journal is rejected without replacing live state", new Action(() =>
            {
                string id; var store = Create(out id); Enqueue(store, id, "corrupt-journal", 10);
                NpcExperienceAppraisalTicket ticket;
                Check.True(store.TryCaptureNpcExperienceAppraisal(id, 10, out ticket));
                Check.True(store.TryCommitNpcExperienceAppraisal(ticket, Result(ticket.Request, mood: 2, trust: 2)));
                var valid = store.Snapshot();
                var corrupt = store.Snapshot();
                corrupt.NpcExperienceInbox.Entries[0].Appraisal.TrustDelta = 99;
                Check.Throws<ArgumentException>(() => store.Restore(corrupt));
                Check.Equal(0, store.ReadPendingNpcExperiences(id, 10).Count);
                Check.Equal(2, store.CaptureConversationMood(id, 10).Value);
                Check.Equal(52, store.ReadNpcRelationship(id, "player").ReliabilityTrust);

                var restored = new WorldStateStore(0);
                restored.Restore(valid);
                Check.Throws<ArgumentException>(() => restored.Restore(corrupt));
                Check.Equal(0, restored.ReadPendingNpcExperiences(id, 10).Count);
                Check.Equal(2, restored.CaptureConversationMood(id, 10).Value);
                Check.Equal(52, restored.ReadNpcRelationship(id, "player").ReliabilityTrust);
            })));

            cases.Add(("Processing native appointment no-show does not repeat its existing penalty", new Action(() =>
            {
                var store = new WorldStateStore(42);
                var resident = store.GetOrCreate(100, new[] { 10 }, 1).Households[0].Residents[0];
                store.GetOrCreateNpcPersonality(resident.Id, 42, NpcSocialBackground.Commoner);
                var booking = new AppointmentRecord
                {
                    Id = "commit-no-show", ResidentId = resident.Id, Purpose = "meet",
                    StartsAt = 2000, EndsAt = 2060, GraceSeconds = 30, Importance = 3,
                    Place = new AppointmentPlace { Kind = "building", SettlementId = 100, BuildingKey = 20 }
                };
                Check.True(store.TryReserveAppointment(booking, 500, out var reason), reason);
                double deadline = AppointmentTiming.ArrivalDeadline(store.FindAppointment(booking.Id));
                for (double at = deadline - 40; at < deadline; at += 10)
                    store.ObserveAppointment(booking.Id, at, true, false, false);
                store.ObserveAppointment(booking.Id, deadline, true, false, false);
                store.ObserveAppointment(booking.Id, deadline + 1, false, false, false);
                var resolved = store.FindAppointment(booking.Id);
                Check.Equal("player-absent", resolved.Status);
                Check.True(resolved.NoShowPenalty > 0);
                int onePenalty = store.ConversationDisposition(resident.Id);
                Check.Equal(-resolved.NoShowPenalty, onePenalty);

                double now = deadline + 1;
                NpcExperienceAppraisalTicket ticket;
                Check.True(store.TryCaptureNpcExperienceAppraisal(resident.Id, now, out ticket));
                Check.True(ticket.Request.WaitedForAppointment);
                Check.True(!store.TryCommitNpcExperienceAppraisal(ticket,
                    Result(ticket.Request, mood: -3, affinity: -5, trust: -4)));
                Check.Equal(0L, store.CaptureConversationMood(resident.Id).Revision);
                Check.Equal(1, store.ReadPendingNpcExperiences(resident.Id, 10).Count);
                Check.Equal(onePenalty, store.ConversationDisposition(resident.Id));
                Check.True(store.ReadNpcRelationship(resident.Id, "player") == null);

                Check.True(store.TryCommitNpcExperienceAppraisal(ticket,
                    Result(ticket.Request, mood: -3, trust: -4)));
                Check.Equal(onePenalty, store.ConversationDisposition(resident.Id));
                Check.Equal(46, store.ReadNpcRelationship(resident.Id, "player").ReliabilityTrust);
                Check.Equal(0, store.ReadPendingNpcExperiences(resident.Id, 10).Count);
                Check.Equal(1, store.Snapshot().NpcExperienceInbox.Entries.Count);
                Check.True(!store.TryCommitNpcExperienceAppraisal(ticket,
                    Result(ticket.Request, mood: -3, trust: -4)));
                Check.Equal(onePenalty, store.ConversationDisposition(resident.Id));
            })));
        }

        internal static WorldStateStore Create(out string id)
        {
            var store=new WorldStateStore(109);
            id=store.GetOrCreate(20,new[]{200},1).Households[0].Residents[0].Id;
            store.GetOrCreateNpcPersonality(id,109,NpcSocialBackground.Commoner);
            return store;
        }
        internal static void Enqueue(WorldStateStore store,string id,string outcome,double at)
        { Check.True(HistoricalNpcExperienceFixture.ImportHistoricalNativeEvent(store, NpcExperienceEvent.FromNativeOutcome(id,id,"receipt:"+outcome,
            "episode:commit",outcome,NpcExperienceEventKind.Healing,at,NpcResponsibleActorAttribution.Known,"player"))); }

        private static void EnqueueSpeech(WorldStateStore store, string id, string utterance, string text, double at)
        {
            var evidence = NpcExperienceEvent.FromObservedSpeech(id, id, "speech:" + utterance, "episode:heard",
                utterance, at, "player");
            var turn = NpcObservedConversationTurn.Create(id, evidence.EpisodeId, evidence.AttributedSpeakerId,
                evidence.ReceiptId, text, at);
            HistoricalNpcObservedSpeechFixture.Import(store, evidence, turn,
                NpcExperienceAppliedEffectsMask.None);
        }

        internal static string Result(NpcExperienceAppraisalRequest r,int mood=0,int affinity=0,int trust=0)
        {
            return "{\"schemaVersion\":"+NpcExperienceAppraisalContract.SchemaVersion+",\"policyVersion\":"+NpcExperienceAppraisalContract.PolicyVersion+
                ",\"saveBranchId\":\""+r.SaveBranchId+"\",\"observerId\":\""+r.ObserverId+"\",\"subjectPersonId\":\""+r.SubjectPersonId+
                "\",\"evidenceDeduplicationKey\":\""+r.EvidenceDeduplicationKey+"\",\"canonicalEvidenceId\":\""+r.CanonicalEvidenceId+
                "\",\"relationshipTargetId\":\""+(r.RelationshipTargetId??"")+"\",\"expectedMoodRevision\":"+r.ExpectedMoodRevision+
                ",\"expectedAffinityRevision\":"+r.ExpectedAffinityRevision+",\"expectedRelationshipRevision\":"+r.ExpectedRelationshipRevision+
                ",\"moodDelta\":"+mood+",\"affinityDelta\":"+affinity+",\"trustDelta\":"+trust+
                ",\"resentmentDelta\":0,\"fearDelta\":0,\"speechActKind\":\"NotApplicable\",\"speechTargetPersonId\":\"\",\"conversationOption\":\"NoAction\"}";
        }

        internal static void BindSpeech(WorldStateStore store, NpcExperienceAppraisalTicket ticket, string label)
        {
            NpcSpeechActClassification classification;
            Check.True(NpcSpeechActClassification.TryAccept(NpcSpeechActRequest.ForAppraisal(ticket.Request), label, out classification));
            Check.True(store.TryBindNpcExperienceSpeechClassification(ticket, classification));
        }

        private static string SpeechResult(WorldStateStore store, NpcExperienceAppraisalTicket ticket)
        {
            BindSpeech(store, ticket, "Other");
            return Result(ticket.Request).Replace("\"speechActKind\":\"NotApplicable\"", "\"speechActKind\":\"Other\"");
        }
    }
}
