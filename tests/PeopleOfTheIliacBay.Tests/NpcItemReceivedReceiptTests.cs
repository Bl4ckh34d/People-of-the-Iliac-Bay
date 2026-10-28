using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcItemReceivedReceiptTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Completed player item receive stores a validated durable receipt", ReceiptIsAttachedToCompletedAction));
            cases.Add(("Completed item receipt survives later action position and routine recovery updates", ReceiptSurvivesLaterActionUpdates));
            cases.Add(("Durable item receipt drains once as neutral ItemReceived evidence", ReceiptDrainsOnce));
            cases.Add(("Bounded item receipt drain makes progress past nonreceipt actions", ReceiptDrainIsBoundedAndFair));
            cases.Add(("Merged item receipt names retained destination UID and rejects missing UID", RetainedUidIsRequired));
            cases.Add(("ItemReceived cannot create relationship trust", ItemReceivedCannotChangeTrust));
        }

        private static void ReceiptIsAttachedToCompletedAction()
        {
            string resident;
            var store = CreateStore(out resident);
            var job = AcceptIncomingTransfer(store, resident, "item-receipt-action", "777", 2);
            var receipt = NpcActionTransferReceipt.CreatePlayerToNpc(job, job.Id + ":transfer", 777, 778,
                778, 2, resident, "player", 12, NpcExperienceNativeValuationStatus.Unknown, 0);
            Check.True(store.TryCompleteNpcActionEffectWithReceipt(job.Id, job.Revision, 12,
                () => NpcActionEffectResult.Success(receipt)));
            receipt.Quantity = 0;
            var completed = store.ReadNpcAction(job.Id);
            Check.Equal(NpcActionPhase.Completed, completed.Phase);
            Check.Equal(778UL, completed.ItemTransferReceipt.RetainedDestinationItemUid);
            Check.Equal(777UL, completed.ItemTransferReceipt.SourceItemUid);
            Check.Equal(778UL, completed.ItemTransferReceipt.MovedItemUid);
            Check.Equal(2, completed.ItemTransferReceipt.Quantity);
            Check.Equal(0, store.Snapshot().NpcExperienceInbox.Entries.Count);

            var corrupt = store.Snapshot();
            corrupt.NpcActions[0].ItemTransferReceipt.Quantity = 0;
            Check.Throws<ArgumentException>(() => new WorldStateStore(0).Restore(corrupt));
        }

        private static void LegacyCompletedActionRestores()
        {
            string resident;
            var store = CreateStore(out resident);
            var job = AcceptIncomingTransfer(store, resident, "item-receipt-legacy", "779", 1);
            Check.True(store.TryCompleteNpcActionEffect(job.Id, job.Revision, 12, () => true));
            var schema38 = store.Snapshot();
            HistoricalNpcExperienceFixture.PrepareLegacyEnvelope(schema38, 38);
            schema38.NpcActions[0].Admission = null;
            schema38.NpcConversationCommandCursors = null;
            var restored = new WorldStateStore(0);
            restored.Restore(schema38);
            Check.Equal(new WorldState().Version, restored.Snapshot().Version);
            Check.True(restored.ReadNpcAction(job.Id).ItemTransferReceipt == null);
        }

        private static void ReceiptSurvivesLaterActionUpdates()
        {
            string resident;
            var store = CreateStore(out resident);
            var job = AcceptIncomingTransfer(store, resident, "item-receipt-return", "788", 1);
            Check.Throws<ArgumentException>(() => NpcActionTransferReceipt.CreatePlayerToNpc(job,
                job.Id + ":transfer", 788, 789, 789, 1, resident, "player", 10,
                NpcExperienceNativeValuationStatus.Unknown, 0));
            Check.Throws<ArgumentException>(() => NpcActionTransferReceipt.CreatePlayerToNpc(job,
                job.Id + ":transfer", 788, 789, 789, 1, resident, "player", 9,
                NpcExperienceNativeValuationStatus.Unknown, 0));
            var receipt = NpcActionTransferReceipt.CreatePlayerToNpc(job, job.Id + ":transfer", 788, 789,
                789, 1, resident, "player", 12, NpcExperienceNativeValuationStatus.Unknown, 0);
            Check.True(store.TryCompleteNpcActionEffectWithReceipt(job.Id, job.Revision, 12,
                () => NpcActionEffectResult.Success(receipt)));
            var futureReceiptAction = NpcActionExecutor.Copy(store.ReadNpcAction(job.Id));
            futureReceiptAction.ItemTransferReceipt.ObservedGameTime = 13;
            Check.Throws<ArgumentException>(() => NpcActionExecutor.Validate(futureReceiptAction));
            var reversedReceiptAction = NpcActionExecutor.Copy(store.ReadNpcAction(job.Id));
            reversedReceiptAction.ItemTransferReceipt.ObservedGameTime = 9;
            Check.Throws<ArgumentException>(() => NpcActionExecutor.Validate(reversedReceiptAction));
            var origin = new NpcActionTarget { Kind = NpcActionTargetKind.WorldPoint,
                SettlementId = 20, WorldSpaceId = "settlement:20", X = 0, Z = 0 };
            var destination = new NpcActionTarget { Kind = NpcActionTargetKind.WorldPoint,
                SettlementId = 20, WorldSpaceId = "settlement:20", X = 1, Z = 0 };
            job = store.ReadNpcAction(job.Id);
            Check.True(store.SaveNpcActionPosition(job.Id, job.Revision, origin, true, 20));
            job = store.ReadNpcAction(job.Id);
            Check.True(store.TryBeginNpcRoutineRecoveryTransition(job.Id, job.Revision, 21,
                destination, new List<NpcActionTarget> { origin, destination }, 1, 200));
            job = store.ReadNpcAction(job.Id);
            Check.True(store.TryArriveNpcRoutineRecoveryTransition(job.Id, job.Revision, 23, destination));

            var restored = new WorldStateStore(0);
            restored.Restore(store.Snapshot());
            var completed = restored.ReadNpcAction(job.Id);
            Check.Equal(23d, completed.UpdatedAt);
            Check.Equal(12d, completed.ItemTransferReceipt.ObservedGameTime);
            Check.Equal(789UL, completed.ItemTransferReceipt.RetainedDestinationItemUid);
        }

        private static void ReceiptDrainsOnce()
        {
            string resident;
            var store = CreateStore(out resident);
            var job = AcceptIncomingTransfer(store, resident, "item-receipt-drain", "901", 1);
            var receipt = NpcActionTransferReceipt.CreatePlayerToNpc(job, job.Id + ":transfer", 901, 902,
                902, 1, resident, "player", 15, NpcExperienceNativeValuationStatus.Unknown, 0);
            Check.True(store.TryCompleteNpcActionEffectWithReceipt(job.Id, job.Revision, 15,
                () => NpcActionEffectResult.Success(receipt)));

            Check.Equal(1, store.DrainNpcItemTransferReceipts(32));
            Check.Equal(0, store.DrainNpcItemTransferReceipts(32));
            var saved = store.Snapshot();
            Check.Equal(1, saved.NpcExperienceInbox.Entries.Count);
            var entry = saved.NpcExperienceInbox.Entries[0];
            Check.Equal(NpcExperienceEventKind.ItemReceived, entry.EventKind);
            Check.Equal(NpcResponsibleActorAttribution.Known, entry.ResponsibleActorAttribution);
            Check.Equal("player", entry.ResponsibleActorId);
            Check.Equal((int)NpcExperienceImpactKind.ItemTransfer, entry.ImpactDetailsKind);
            Check.Equal(902UL, entry.ReceivedItemUid);
            Check.Equal(1L, entry.TransferQuantity);
            Check.Equal((int)NpcExperienceNativeValuationStatus.Unknown, entry.NativeValuationStatus);
            Check.Equal(0L, entry.NativeGoldValue);

            var restored = new WorldStateStore(0);
            restored.Restore(saved);
            Check.Equal(0, restored.DrainNpcItemTransferReceipts(32));
            Check.Equal(1, restored.Snapshot().NpcExperienceInbox.Entries.Count);
        }

        private static void RetainedUidIsRequired()
        {
            string observer;
            var store = CreateStore(out observer);
            var impact = NpcExperienceImpactDetails.ForReceivedItemTransfer(3002, 3,
                NpcExperienceNativeValuationStatus.Unknown, 0);
            var evidence = NpcExperienceEvent.FromNativeOutcome(observer, observer, "receipt:merged-item",
                "episode:merged-item", "outcome:merged-item", NpcExperienceEventKind.ItemReceived, 30,
                NpcResponsibleActorAttribution.Known, "player", impact);
            Check.True(HistoricalNpcExperienceFixture.ImportHistoricalNativeEvent(store, evidence));
            var restored = new WorldStateStore(0);
            restored.Restore(store.Snapshot());
            Check.Equal(3002UL, restored.ReadPendingNpcExperiences(observer, 1)[0]
                .NativeImpactDetails.RetainedDestinationItemUid);
            Check.Throws<ArgumentException>(() => NpcExperienceImpactDetails.ForReceivedItemTransfer(0, 1,
                NpcExperienceNativeValuationStatus.Unknown, 0));

            // Previously stored confirmed-gift records have no retained UID; they remain valid.
            var legacyGift = NpcExperienceEvent.FromNativeOutcome(observer, observer, "receipt:old-gift",
                "episode:old-gift", "outcome:old-gift", NpcExperienceEventKind.GiftReceived, 31,
                NpcResponsibleActorAttribution.Unknown, null,
                NpcExperienceImpactDetails.ForItemTransfer(1, NpcExperienceNativeValuationStatus.Unknown, 0));
            Check.True(HistoricalNpcExperienceFixture.ImportHistoricalNativeEvent(restored, legacyGift));
        }

        private static void ReceiptDrainIsBoundedAndFair()
        {
            string resident;
            var store = CreateStore(out resident);
            const int receiptCount = 40;
            for (int i = 0; i < receiptCount; i++)
            {
                var ordinary = AcceptIncomingTransfer(store, resident, "batch-ordinary-" + i,
                    (1000 + i).ToString(), 1);
                Check.True(store.TryCompleteNpcActionEffect(ordinary.Id, ordinary.Revision, 12, () => true));

                var receiptJob = AcceptIncomingTransfer(store, resident, "batch-receipt-" + i,
                    (2000 + i).ToString(), 1);
                ulong sourceUid = (ulong)(2000 + i);
                var receipt = NpcActionTransferReceipt.CreatePlayerToNpc(receiptJob,
                    receiptJob.Id + ":transfer", sourceUid, (ulong)(3000 + i), (ulong)(4000 + i),
                    1, resident, "player", 15, NpcExperienceNativeValuationStatus.Unknown, 0);
                Check.True(store.TryCompleteNpcActionEffectWithReceipt(receiptJob.Id, receiptJob.Revision, 15,
                    () => NpcActionEffectResult.Success(receipt)));
            }

            int firstBatch = store.DrainNpcItemTransferReceipts(32);
            Check.True(firstBatch <= 32);
            Check.Equal(16, firstBatch);
            Check.Equal(16, store.DrainNpcItemTransferReceipts(32));
            Check.Equal(8, store.DrainNpcItemTransferReceipts(32));
            Check.Equal(0, store.DrainNpcItemTransferReceipts(32));
            var saved = store.Snapshot();
            Check.Equal(receiptCount, saved.NpcExperienceInbox.Entries.Count);
            Check.Equal(receiptCount, new HashSet<string>(saved.NpcExperienceInbox.Entries
                .ConvertAll(entry => entry.DeduplicationKey)).Count);
        }

        private static void ItemReceivedCannotChangeTrust()
        {
            string observer;
            var store = CreateStore(out observer);
            var evidence = NpcExperienceEvent.FromNativeOutcome(observer, observer, "receipt:item-trust",
                "episode:item-trust", "outcome:item-trust", NpcExperienceEventKind.ItemReceived, 42,
                NpcResponsibleActorAttribution.Known, "player",
                NpcExperienceImpactDetails.ForReceivedItemTransfer(99, 1,
                    NpcExperienceNativeValuationStatus.Unknown, 0));
            var proposal = new NpcRelationshipLedgerUpdateProposal
            {
                ExpectedLedgerRevision = 0,
                ObserverId = observer,
                TargetId = "player",
                RelationshipUpdate = new NpcRelationshipUpdateProposal
                { ExpectedRevision = 0, Evidence = evidence, TrustDelta = 1 }
            };
            Check.True(!store.TryCommitNpcRelationship(proposal, 0));
            proposal.RelationshipUpdate.TrustDelta = -1;
            Check.True(!store.TryCommitNpcRelationship(proposal, 0));
        }

        private static NpcActionJob AcceptIncomingTransfer(WorldStateStore store, string resident,
            string id, string uid, int quantity)
        {
            var proposed = new NpcActionJob
            {
                Id = id,
                ProposalId = id + "-proposal",
                ResidentId = resident,
                Kind = NpcActionKind.Transfer,
                Target = new NpcActionTarget
                {
                    Kind = NpcActionTargetKind.Person,
                    PersonId = "player",
                    ItemId = uid,
                    Quantity = quantity,
                    TransferDirection = NpcActionTransferDirection.PlayerToNpc
                },
                Phase = NpcActionPhase.Accepted,
                Revision = 1,
                AcceptedAt = 10,
                UpdatedAt = 10,
                DeadlineAt = 100
            };
            string reason;
            Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store, proposed, true, true, true, out reason), reason);
            Check.True(store.ObserveNpcAction(proposed.Id, 1, 11, NpcActionObservation.Arrival()));
            return store.ReadNpcAction(proposed.Id);
        }

        private static WorldStateStore CreateStore(out string resident)
        {
            var store = new WorldStateStore(109);
            resident = store.GetOrCreate(20, new[] { 200 }, 1).Households[0].Residents[0].Id;
            return store;
        }
    }
}
