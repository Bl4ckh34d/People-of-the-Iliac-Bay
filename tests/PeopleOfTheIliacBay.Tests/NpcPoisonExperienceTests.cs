using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcPoisonExperienceTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("poison status impact validates type key sequence and event kind", TypedStatusValidation));
            cases.Add(("Current inbox status rows without typed impact remain unclassified", CurrentStatusRowsRemainUnknown));
            cases.Add(("poison onset sequence appends atomically with unknown cause and survives restore", AtomicSequenceAndUnknownCause));
            cases.Add(("unclassified poison onset claim cannot mint native evidence", UnclassifiedOnsetClaimIsRejected));
            cases.Add(("poison onset rejects stale epoch and invalid observer without partial append", RejectedCaptureDoesNotMutate));
            cases.Add(("poison onset sequence is isolated per observer", SequenceIsObserverScoped));
            cases.Add(("current save restores prior health and transfer impacts without status reinterpretation", ExistingImpactsSurviveRestore));
            cases.Add(("poison onset requires living unprotected adult resident", ResidentEligibilityIsRequired));
            cases.Add(("poison onset sequence overflow records a typed gap without inbox mutation", SequenceOverflowDegradesWithoutInboxMutation));
            cases.Add(("status onset appraisal cannot grant relationship trust", StatusOnsetCannotGrantTrust));
        }

        private static void TypedStatusValidation()
        {
            var details = NpcExperienceImpactDetails.ForStatusOnset(
                NpcExperienceStatusKind.Poison, "native-effect-key:unclassified", 1);
            Check.Equal(NpcExperienceImpactKind.StatusOnset, details.Kind);
            Check.Equal(NpcExperienceStatusKind.Poison, details.Status);
            Check.Equal("native-effect-key:unclassified", details.NativeEffectKey);
            Check.Equal(1L, details.StatusOnsetSequence);

            var detached = NpcExperienceImpactDetails.CopyValidated(details);
            Check.Equal(details.Status, detached.Status);
            Check.Equal(details.NativeEffectKey, detached.NativeEffectKey);
            Check.Equal(details.StatusOnsetSequence, detached.StatusOnsetSequence);

            Check.Throws<ArgumentException>(() => NpcExperienceImpactDetails.ForStatusOnset(
                (NpcExperienceStatusKind)0, "effect", 1));
            Check.Throws<ArgumentException>(() => NpcExperienceImpactDetails.ForStatusOnset(
                (NpcExperienceStatusKind)99, "effect", 1));
            Check.Throws<ArgumentException>(() => NpcExperienceImpactDetails.ForStatusOnset(
                NpcExperienceStatusKind.Poison, "", 1));
            Check.Throws<ArgumentException>(() => NpcExperienceImpactDetails.ForStatusOnset(
                NpcExperienceStatusKind.Poison, new string('x', 4096), 1));
            Check.Throws<ArgumentException>(() => NpcExperienceImpactDetails.ForStatusOnset(
                NpcExperienceStatusKind.Poison, "effect\u0001key", 1));
            Check.Throws<ArgumentException>(() => NpcExperienceImpactDetails.ForStatusOnset(
                NpcExperienceStatusKind.Poison, "effect", 0));
            Check.Throws<ArgumentException>(() => NpcExperienceImpactDetails.ForStatusOnset(
                NpcExperienceStatusKind.Poison, "effect", -1));

            string observer;
            NpcExperienceCommitTests.Create(out observer);
            Check.Throws<ArgumentException>(() => NpcExperienceEvent.FromNativeOutcome(
                observer, observer, "receipt:status-kind", "episode:status-kind", "outcome:status-kind",
                NpcExperienceEventKind.Injury, 10, NpcResponsibleActorAttribution.Unknown, null, details));

            var eventRow = NpcExperienceInboxEntry.FromNativeEvent(
                NpcExperienceEvent.FromNativeOutcome(observer, observer, "receipt:status-fields",
                    "episode:status-fields", "outcome:status-fields", NpcExperienceEventKind.StatusOnset,
                    10, NpcResponsibleActorAttribution.Unknown, null, details));
            eventRow.TransferQuantity = 1;
            Check.Throws<ArgumentException>(() => NpcExperienceInboxEntry.CopyValidated(eventRow));
        }

        private static void CurrentStatusRowsRemainUnknown()
        {
            string observer;
            NpcExperienceCommitTests.Create(out observer);
            foreach (int version in new[] { NpcExperienceInbox.VersionCurrent })
            {
                var evidence = NpcExperienceEvent.FromNativeOutcome(observer, observer,
                    "receipt:legacy-status:" + version, "episode:legacy-status:" + version,
                    "outcome:legacy-status:" + version, NpcExperienceEventKind.StatusOnset, 10,
                    NpcResponsibleActorAttribution.Unknown);
                var inbox = new NpcExperienceInbox
                {
                    Version = version,
                    Revision = 1,
                    Entries = new List<NpcExperienceInboxEntry>
                    {
                        NpcExperienceInboxEntry.FromNativeEvent(evidence)
                    }
                };
                inbox.Entries[0].EnqueueOrdinal = 1;
                var migrated = NpcExperienceInbox.CopyValidated(inbox, id => id == observer);
                Check.Equal(NpcExperienceInbox.VersionCurrent, migrated.Version);
                Check.Equal(NpcExperienceEventKind.StatusOnset, migrated.Entries[0].EventKind);
                Check.Equal<NpcExperienceImpactDetails>(null, migrated.Entries[0].ToEvent().NativeImpactDetails);
                Check.Equal(0, migrated.Entries[0].StatusKind);
                Check.Equal<string>(null, migrated.Entries[0].StatusNativeEffectKey);
                Check.Equal(0L, migrated.Entries[0].StatusOnsetSequence);
            }
        }

        private static void AtomicSequenceAndUnknownCause()
        {
            string observer;
            var store = NpcExperienceCommitTests.Create(out observer);
            object epoch = store.CaptureNpcVitalsWriteEpoch();

            NpcExperienceEvent first;
            Check.Equal(NpcPoisonCaptureDisposition.Queued,
                RecordAssigned(store, epoch, observer, "poison:classic", 10, out first));
            Check.Equal(NpcExperienceEventOrigin.NativeOutcome, first.Origin);
            Check.Equal(NpcExperienceEventKind.StatusOnset, first.EventKind.Value);
            Check.Equal(observer, first.SubjectPersonId);
            Check.Equal(NpcResponsibleActorAttribution.Unknown, first.ResponsibleActorAttribution);
            Check.Equal<string>(null, first.ResponsibleActorId);
            Check.Equal(NpcExperienceStatusKind.Poison, first.NativeImpactDetails.Status);
            Check.Equal("poison:classic", first.NativeImpactDetails.NativeEffectKey);
            Check.Equal(1L, first.NativeImpactDetails.StatusOnsetSequence);
            Check.Equal(1L, store.Snapshot().NpcExperienceInbox.Revision);
            long relationshipRevision = store.NpcRelationshipLedgerRevision;
            Check.True(store.ReadNpcRelationship(observer, "player") == null);

            NpcExperienceEvent second;
            Check.Equal(NpcPoisonCaptureDisposition.Queued,
                RecordAssigned(store, epoch, observer, "poison:classic", 11, out second));
            Check.Equal(2L, second.NativeImpactDetails.StatusOnsetSequence);
            Check.Equal(2L, store.Snapshot().NpcExperienceInbox.Revision);
            Check.Equal(relationshipRevision, store.NpcRelationshipLedgerRevision);
            Check.True(store.ReadNpcRelationship(observer, "player") == null);

            var saved = store.Snapshot();
            store.Restore(saved);
            object restoredEpoch = store.CaptureNpcVitalsWriteEpoch();
            NpcExperienceEvent third;
            Check.Equal(NpcPoisonCaptureDisposition.Queued,
                RecordAssigned(store, restoredEpoch, observer, "poison:classic", 12, out third));
            Check.Equal(3L, third.NativeImpactDetails.StatusOnsetSequence);
            Check.Equal(3L, store.Snapshot().NpcExperienceInbox.Revision);
        }

        private static void UnclassifiedOnsetClaimIsRejected()
        {
            string observer;
            var store = NpcExperienceCommitTests.Create(out observer);
            NpcExperienceEvent rejected;
            Check.True(!store.TryCaptureNpcPoisonOnset(store.CaptureNpcVitalsWriteEpoch(),
                observer, "poison:classic", 10, out rejected));
            Check.Equal<NpcExperienceEvent>(null, rejected);
            Check.Equal(0L, store.Snapshot().NpcExperienceInbox.Revision);
        }

        private static void RejectedCaptureDoesNotMutate()
        {
            string observer;
            var store = NpcExperienceCommitTests.Create(out observer);
            object staleEpoch = store.CaptureNpcVitalsWriteEpoch();
            NpcExperienceEvent accepted;
            Check.Equal(NpcPoisonCaptureDisposition.Queued,
                RecordAssigned(store, staleEpoch, observer, "poison:classic", 10, out accepted));
            var before = store.Snapshot().NpcExperienceInbox;
            store.Restore(store.Snapshot());

            NpcExperienceEvent rejected;
            Check.Equal(NpcPoisonCaptureDisposition.Rejected,
                RecordAssigned(store, staleEpoch, observer, "poison:classic", 11, out rejected));
            Check.Equal<NpcExperienceEvent>(null, rejected);
            Check.Equal(NpcPoisonCaptureDisposition.Rejected,
                RecordAssigned(store, store.CaptureNpcVitalsWriteEpoch(),
                    "npc:unknown-observer", "poison:classic", 11, out rejected));
            Check.Equal(NpcPoisonCaptureDisposition.Rejected,
                RecordAssigned(store, store.CaptureNpcVitalsWriteEpoch(),
                    observer, "poison:classic", double.NaN, out rejected));
            Check.Equal(NpcPoisonCaptureDisposition.Rejected,
                RecordAssigned(store, store.CaptureNpcVitalsWriteEpoch(),
                    observer, "poison:classic", double.PositiveInfinity, out rejected));
            Check.Equal(NpcPoisonCaptureDisposition.Rejected,
                RecordAssigned(store, store.CaptureNpcVitalsWriteEpoch(),
                    observer, "poison:classic", double.NegativeInfinity, out rejected));
            Check.Equal(NpcPoisonCaptureDisposition.Rejected,
                RecordAssigned(store, store.CaptureNpcVitalsWriteEpoch(),
                    observer, "poison:classic", -1, out rejected));
            var after = store.Snapshot().NpcExperienceInbox;
            Check.Equal(before.Revision, after.Revision);
            Check.Equal(before.Entries.Count, after.Entries.Count);
            Check.Equal(1L, after.Entries[0].StatusOnsetSequence);
        }

        private static void SequenceIsObserverScoped()
        {
            var store = CreateChildResident(out _);
            var residents = store.Snapshot().Settlements[0].Households[0].Residents;
            var adults = residents.FindAll(r => r.AgeGroup == ResidentAgeGroup.Adult);
            Check.True(adults.Count >= 2);
            object epoch = store.CaptureNpcVitalsWriteEpoch();
            NpcExperienceEvent first;
            NpcExperienceEvent other;
            NpcExperienceEvent next;
            Check.Equal(NpcPoisonCaptureDisposition.Queued,
                RecordAssigned(store, epoch, adults[0].Id, "poison:a", 10, out first));
            Check.Equal(NpcPoisonCaptureDisposition.Queued,
                RecordAssigned(store, epoch, adults[1].Id, "poison:b", 10, out other));
            Check.Equal(NpcPoisonCaptureDisposition.Queued,
                RecordAssigned(store, epoch, adults[0].Id, "poison:a", 11, out next));
            Check.Equal(1L, first.NativeImpactDetails.StatusOnsetSequence);
            Check.Equal(1L, other.NativeImpactDetails.StatusOnsetSequence);
            Check.Equal(2L, next.NativeImpactDetails.StatusOnsetSequence);
            Check.True(first.ReceiptId != other.ReceiptId);
        }

        private static void ExistingImpactsSurviveRestore()
        {
            string observer;
            var store = NpcExperienceCommitTests.Create(out observer);
            var save = store.Snapshot();
            var injury = NpcExperienceEvent.FromNativeOutcome(observer, observer,
                "receipt:legacy-health", "episode:legacy-health", "outcome:legacy-health",
                NpcExperienceEventKind.Injury, 8, NpcResponsibleActorAttribution.Unknown, null,
                NpcExperienceImpactDetails.ForHealthEpisode(40, 35, 50, 5, 0, 1));
            var transfer = NpcExperienceEvent.FromNativeOutcome(observer, observer,
                "receipt:legacy-transfer", "episode:legacy-transfer", "outcome:legacy-transfer",
                NpcExperienceEventKind.MoneyReceived, 9, NpcResponsibleActorAttribution.Known, "player",
                NpcExperienceImpactDetails.ForGoldTransfer(4));
            var legacyStatus = NpcExperienceEvent.FromNativeOutcome(observer, observer,
                "receipt:legacy-status-null", "episode:legacy-status-null", "outcome:legacy-status-null",
                NpcExperienceEventKind.StatusOnset, 10, NpcResponsibleActorAttribution.Unknown);
            save.NpcExperienceInbox = new NpcExperienceInbox
            {
                Version = NpcExperienceInbox.VersionCurrent,
                Revision = 3,
                Entries = new List<NpcExperienceInboxEntry>
                {
                    NpcExperienceInboxEntry.FromNativeEvent(injury),
                    NpcExperienceInboxEntry.FromNativeEvent(transfer),
                    NpcExperienceInboxEntry.FromNativeEvent(legacyStatus)
                }
            };
            for (int index = 0; index < save.NpcExperienceInbox.Entries.Count; index++)
                save.NpcExperienceInbox.Entries[index].EnqueueOrdinal = index + 1;
            var restored = new WorldStateStore(0);
            restored.Restore(save);
            var entries = restored.Snapshot().NpcExperienceInbox.Entries;
            Check.Equal(3, entries.Count);
            Check.Equal(35, entries[0].ToEvent().NativeImpactDetails.FinalHealth);
            Check.Equal(4L, entries[1].ToEvent().NativeImpactDetails.Quantity);
            Check.Equal<NpcExperienceImpactDetails>(null, entries[2].ToEvent().NativeImpactDetails);
            Check.Equal(0, entries[2].StatusKind);
            Check.Equal<string>(null, entries[2].StatusNativeEffectKey);
            Check.Equal(0L, entries[2].StatusOnsetSequence);
        }

        private static void StatusOnsetCannotGrantTrust()
        {
            string observer;
            var store = NpcExperienceCommitTests.Create(out observer);
            Check.Equal(NpcPoisonCaptureDisposition.Queued,
                RecordAssigned(store, store.CaptureNpcVitalsWriteEpoch(),
                    observer, "poison:classic", 10, out _));
            NpcExperienceAppraisalTicket ticket;
            Check.True(store.TryCaptureNpcExperienceAppraisal(observer, 11, out ticket));
            Check.True(ticket.Request.Prompt.Contains(
                "nativeStatus={kind=Poison,key=\"poison:classic\",onsetSequence=1,responsibleActor=Unknown}"));

            // Status evidence never identifies a responsible actor, even if another caller attempts to add one.
            var attributed = NpcExperienceEvent.FromNativeOutcome(observer, observer,
                "receipt:status-actor", "episode:status-actor", "outcome:status-actor",
                NpcExperienceEventKind.StatusOnset, 10, NpcResponsibleActorAttribution.Known, "player",
                NpcExperienceImpactDetails.ForStatusOnset(NpcExperienceStatusKind.Poison, "poison:classic", 1));
            Check.Throws<ArgumentException>(() => NpcExperienceInboxEntry.FromNativeEvent(attributed));
            var policyRequest = ticket.Request;
            NpcExperienceAppraisal parsed;
            Check.True(!NpcExperienceAppraisalContract.TryParseResponse(policyRequest,
                NpcExperienceCommitTests.Result(policyRequest, mood: -2, trust: 1), out parsed));
            Check.True(NpcExperienceAppraisalContract.TryParseResponse(policyRequest,
                NpcExperienceCommitTests.Result(policyRequest, mood: -2, trust: 0), out parsed));
            long ledgerBefore = store.NpcRelationshipLedgerRevision;
            Check.True(store.TryCommitNpcExperienceAppraisal(ticket,
                NpcExperienceCommitTests.Result(policyRequest, mood: -2, trust: 0)));
            Check.Equal(ledgerBefore, store.NpcRelationshipLedgerRevision);
            Check.True(store.ReadNpcRelationship(observer, "player") == null);
        }

        private static void ResidentEligibilityIsRequired()
        {
            string observer;
            var protectedStore = NpcExperienceCommitTests.Create(out observer);
            var protectedSave = protectedStore.Snapshot();
            protectedSave.Settlements[0].Households[0].Residents[0].IsQuestProtected = true;
            protectedStore.Restore(protectedSave);
            NpcExperienceEvent rejected;
            Check.Equal(NpcPoisonCaptureDisposition.Rejected,
                RecordAssigned(protectedStore, protectedStore.CaptureNpcVitalsWriteEpoch(),
                    observer, "poison:classic", 10, out rejected));
            Check.Equal(0L, protectedStore.Snapshot().NpcExperienceInbox.Revision);

            var deadStore = NpcExperienceCommitTests.Create(out observer);
            var deadSave = deadStore.Snapshot();
            deadSave.Settlements[0].Households[0].Residents[0].IsAlive = false;
            deadStore.Restore(deadSave);
            Check.Equal(NpcPoisonCaptureDisposition.Rejected,
                RecordAssigned(deadStore, deadStore.CaptureNpcVitalsWriteEpoch(),
                    observer, "poison:classic", 10, out rejected));
            Check.Equal(0L, deadStore.Snapshot().NpcExperienceInbox.Revision);

            var childStore = CreateChildResident(out var childId);
            Check.Equal(NpcPoisonCaptureDisposition.Rejected,
                RecordAssigned(childStore, childStore.CaptureNpcVitalsWriteEpoch(),
                    childId, "poison:classic", 10, out rejected));
            Check.Equal(0L, childStore.Snapshot().NpcExperienceInbox.Revision);
        }

        private static void SequenceOverflowDegradesWithoutInboxMutation()
        {
            string observer;
            var store = NpcExperienceCommitTests.Create(out observer);
            Check.Equal(NpcPoisonCaptureDisposition.Queued,
                RecordAssigned(store, store.CaptureNpcVitalsWriteEpoch(),
                    observer, "poison:classic", 10, out NpcExperienceEvent first));
            var legacy = store.Snapshot();
            var overflow = NpcExperienceEvent.FromNativeOutcome(observer, observer,
                WithSourceSequence(first.ReceiptId, long.MaxValue),
                WithSourceSequence(first.EpisodeId, long.MaxValue),
                WithSourceSequence(first.OutcomeId, long.MaxValue),
                NpcExperienceEventKind.StatusOnset, 10, NpcResponsibleActorAttribution.Unknown, null,
                NpcExperienceImpactDetails.ForStatusOnset(NpcExperienceStatusKind.Poison, "poison:classic", long.MaxValue));
            legacy.NpcPoisonOnsets[0].LastIssuedSequence = long.MaxValue;
            legacy.NpcPoisonOnsets[0].LastProjectedSequence = long.MaxValue;
            legacy.NpcExperienceInbox.Entries.Clear();
            var row = NpcExperienceInboxEntry.FromNativeEvent(overflow);
            row.EnqueueOrdinal = 1;
            row.SourceKind = NpcExperienceSourceKind.Poison;
            legacy.NpcExperienceInbox.Entries.Add(row);
            legacy.NpcExperienceInbox.Revision = 1;
            store.Restore(legacy);
            var before = store.Snapshot().NpcExperienceInbox;
            NpcExperienceEvent rejected;
            Check.Equal(NpcPoisonCaptureDisposition.Degraded,
                RecordAssigned(store, store.CaptureNpcVitalsWriteEpoch(),
                    observer, "poison:classic", 11, out rejected));
            Check.Equal<NpcExperienceEvent>(null, rejected);
            var after = store.Snapshot().NpcExperienceInbox;
            Check.Equal(before.Revision, after.Revision);
            Check.Equal(before.Entries.Count, after.Entries.Count);
            var source = store.Snapshot().NpcPoisonOnsets.Find(row => row.PersonId == observer);
            Check.True(source != null);
            Check.Equal(long.MaxValue, source.LastIssuedSequence);
            Check.True(source.SequenceExhausted);
            Check.Equal(1L, source.DegradedCount);
        }

        private static string WithSourceSequence(string canonicalId, long sequence)
        {
            return canonicalId.Substring(0, canonicalId.LastIndexOf(':') + 1) + sequence;
        }

        private static NpcPoisonCaptureDisposition RecordAssigned(WorldStateStore store, object epoch,
            string observer, string nativeEffectKey, double observedGameTime, out NpcExperienceEvent evidence)
        {
            evidence = null;
            object nativeBindingOwner = new object();
            object nativeBundleIdentity = new object();
            var ticket = store.TryIssueNpcPoisonAssignment(epoch, observer,
                nativeBindingOwner, nativeBundleIdentity);
            if (ticket == null) return NpcPoisonCaptureDisposition.Rejected;
            return store.TryRecordNpcPoisonAssignment(ticket, nativeEffectKey, observedGameTime,
                out evidence);
        }

        private static WorldStateStore CreateChildResident(out string childId)
        {
            for (uint seed = 1; seed < 100; seed++)
            {
                var store = new WorldStateStore(seed);
                store.GetOrCreate(20, new[] { 200 }, 3);
                var saved = store.Snapshot();
                var residents = saved.Settlements[0].Households[0].Residents;
                if (residents.Count < 3) continue;
                var child = residents[0];
                var father = residents[1];
                var mother = residents[2];
                father.AgeGroup = ResidentAgeGroup.Adult;
                father.Sex = ResidentSex.Male;
                mother.AgeGroup = ResidentAgeGroup.Adult;
                mother.Sex = ResidentSex.Female;
                child.AgeGroup = ResidentAgeGroup.Child;
                child.FatherId = father.Id;
                child.MotherId = mother.Id;
                childId = child.Id;
                store.Restore(saved);
                return store;
            }
            throw new InvalidOperationException("Could not build controlled child resident fixture.");
        }
    }
}
