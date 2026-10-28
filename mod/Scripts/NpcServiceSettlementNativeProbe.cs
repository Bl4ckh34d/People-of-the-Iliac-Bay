using System;
using System.Collections.Generic;
using System.IO;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>
    /// Controlled, synchronous native-currency fixture. It never recruits a player companion
    /// or writes the human save. The caller runs it only in a disposable native test process.
    /// </summary>
    internal static class NpcServiceSettlementNativeProbe
    {
        private const uint Seed = 0x53564350;
        private const string ContractId = "fixture-native-service-settlement";
        private const string FundingId = "fixture-native-service-funding";

        private sealed class CreditSnapshot
        {
            internal DaggerfallUnityItem Item;
            internal ulong Uid;
            internal int Value;
        }

        internal static void Verify()
        {
            var game = GameManager.Instance;
            var unity = DaggerfallUnity.Instance;
            Require(game != null && game.PlayerEntity != null && unity != null &&
                unity.WorldTime != null && unity.ItemHelper != null,
                "Controlled service settlement has no native player or clock.");
            PlayerEntity player = game.PlayerEntity;
            DaggerfallDateTime liveClock = unity.WorldTime.Now;
            DaggerfallDateTime originalClock = liveClock.Clone();
            int originalCoins = player.GoldPieces;
            int originalTotal = player.GetGoldAmount();
            int creditGroup = unity.ItemHelper.GetGroupIndex(ItemGroups.MiscItems,
                (int)MiscItems.Letter_of_credit);
            var originalCredits = new List<CreditSnapshot>();
            for (int i = 0; i < player.Items.Count; i++)
            {
                DaggerfallUnityItem item = player.Items.GetItem(i);
                if (item != null && item.ItemGroup == ItemGroups.MiscItems && item.GroupIndex == creditGroup)
                    originalCredits.Add(new CreditSnapshot { Item = item, Uid = item.UID, Value = item.value });
            }

            DaggerfallUnityItem fixtureCredit = null;
            string tempPath = Path.Combine(Path.GetTempPath(),
                "poti-service-settlement-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                player.GoldPieces = 10;
                foreach (CreditSnapshot credit in originalCredits) credit.Item.value = 0;
                fixtureCredit = ItemBuilder.CreateItem(ItemGroups.MiscItems,
                    (int)MiscItems.Letter_of_credit);
                Require(fixtureCredit != null, "Could not create controlled player letter of credit.");
                fixtureCredit.value = 200;
                player.Items.AddItem(fixtureCredit, noStack: true);
                ulong creditUid = fixtureCredit.UID;
                Require(player.GetGoldAmount() == 210 && ContainsExactCredit(player, fixtureCredit),
                    "Controlled mixed native purse was not established.");

                var store = new WorldStateStore(Seed);
                const int settlementId = 73121;
                const int buildingKey = 83121;
                SettlementPopulation population = store.GetOrCreate(settlementId, new[] { buildingKey }, 1);
                Require(population.Households.Count == 1 && population.Households[0].Residents.Count == 1,
                    "Controlled service resident was not created.");
                Resident worker = population.Households[0].Residents[0];
                Require(worker.IsAlive && worker.AgeGroup == ResidentAgeGroup.Adult &&
                    !worker.IsQuestProtected, "Controlled service resident is not eligible.");
                Require(store.InitializeNpcInventory(worker.Id, 7, new NpcInventoryItem[0]),
                    "Controlled service wallet could not be opened.");

                const string home = "fixture-service-home";
                NpcServiceContractTerms terms;
                Require(NpcServiceContractTerms.TryCreate(ContractId,
                    NpcDirectedRelationship.PlayerTargetId, worker.Id, home, NpcServiceRole.Fighter,
                    NpcServiceCoverage.Follow, 120, false, NpcServiceRiskScope.None,
                    new NpcServiceProtectedTarget[0], NpcServiceRestTreatment.RestNotIncluded,
                    NpcServiceResupplyResponsibility.SeparatelyNegotiated,
                    new NpcServiceReservedObligation[0],
                    id => id == NpcDirectedRelationship.PlayerTargetId || id == worker.Id,
                    id => id == home, (id, category, revision) => false,
                    (id, revision, kind, starts, ends) => false, out terms),
                    "Controlled service terms were rejected.");

                double startedAt = liveClock.ToSeconds();
                NpcServiceTransferReceipt funding;
                NpcServiceContractLedger funded;
                Exception failure;
                Require(NativeNpcServiceContractFunding.TryReserveFromPlayer(store,
                    store.CaptureNpcServiceContractWriteTicket(), player, terms, startedAt, FundingId,
                    out funding, out funded, out failure),
                    "Actual mixed coin/LOC funding failed: " + Failure(failure));
                Require(funding != null && funding.Amount == 120 && funding.GameTime == startedAt &&
                    player.GoldPieces == 10 && player.GetGoldAmount() == 90 &&
                    fixtureCredit.UID == creditUid && fixtureCredit.value == 80 &&
                    ContainsExactCredit(player, fixtureCredit),
                    "Native funding did not preserve exact mixed-currency identity and balance.");

                CommitTransition(store, current =>
                {
                    NpcServiceContract started;
                    return current.TryStart(current.Revision, startedAt, out started) ? started : null;
                });
                liveClock.RaiseTime(43201f);
                double pausedAt = liveClock.ToSeconds();
                NpcVitalsView baseline = store.ReadNpcVitals(worker.Id, NpcArchetype.Citizen);
                Require(baseline != null && baseline.MaximumHealth >= 8 &&
                    store.TryCaptureNpcVitals(worker.Id, NpcArchetype.Citizen,
                        baseline.MaximumHealth / 2, baseline.MaximumMagicka,
                        pausedAt, baseline.Revision) &&
                    store.TryBeginNpcHealthCare(worker.Id, NpcArchetype.Citizen, pausedAt),
                    "Controlled saved injury did not establish a health-care pause owner.");
                NpcHealthCareRecord care = store.ReadNpcHealthCare(worker.Id);
                Require(care != null && care.Phase == NpcHealthCarePhase.Seeking,
                    "Controlled health-care episode was not persisted.");
                CommitTransition(store, current =>
                {
                    NpcServiceContract paused;
                    return current.TrySuspendForHealthCare(current.Revision, pausedAt,
                        care.Id, out paused) ? paused : null;
                });
                NpcServiceContract suspended = store.ReadNpcServiceContracts().Read(ContractId);
                Require(suspended != null && suspended.Status == NpcServiceContractStatus.Suspended &&
                    suspended.SuspensionOwnerKind == NpcServiceSuspensionOwnerKind.HealthCare &&
                    suspended.SuspensionOwnerId == care.Id && suspended.EarnedGold == 120 &&
                    suspended.EscrowGold == 0 && suspended.ReleasedGold == 120,
                    "Health pause changed the fixed day wage or its saved owner.");

                double secondsToLateExpiry = suspended.BlockEndAt - liveClock.ToSeconds() + 1;
                Require(secondsToLateExpiry > 0 && secondsToLateExpiry < 86400,
                    "Controlled service period has no finite late-expiry interval.");
                liveClock.RaiseTime((float)secondsToLateExpiry);
                double settledAt = liveClock.ToSeconds();
                Require(settledAt > suspended.BlockEndAt,
                    "Controlled settlement did not observe actual time after fixed expiry.");

                NpcServiceContractWriteTicket staleTicket = store.CaptureNpcServiceContractWriteTicket();
                WorldState beforeRestore = store.Snapshot();
                store.Restore(beforeRestore);
                AssertRejectedWithoutCurrencyChange(store, staleTicket, player, worker.Id,
                    "fixture-stale-wage", null, fixtureCredit, creditUid,
                    "Pre-restore service ticket");
                var sibling = new WorldStateStore(Seed);
                AssertRejectedWithoutCurrencyChange(store, sibling.CaptureNpcServiceContractWriteTicket(),
                    player, worker.Id, "fixture-foreign-wage", null,
                    fixtureCredit, creditUid, "Foreign-owner service ticket");
                AssertRejectedWithoutCurrencyChange(store, store.CaptureNpcServiceContractWriteTicket(),
                    player, worker.Id, FundingId, null,
                    fixtureCredit, creditUid, "Reused funding receipt ID");
                NpcServiceContractLedger rejectedLedger;
                NpcServiceTransferReceipt rejectedWage, rejectedRefund;
                Require(!NativeNpcServiceContractSettlement.TrySettle(store,
                    store.CaptureNpcServiceContractWriteTicket(), player, ContractId,
                    suspended.Revision, NpcServiceEndReason.Death,
                    "fixture-death-wage", "fixture-death-refund",
                    out rejectedLedger, out rejectedWage, out rejectedRefund, out failure) &&
                    failure != null && player.GoldPieces == 10 && player.GetGoldAmount() == 90,
                    "Unproven estate settlement changed the actual player purse.");

                string beforeRejectedCommit = SaveLoadManager.Serialize(typeof(WorldState), store.Snapshot());
                bool dailyWageReachedCommit = false;
                NativeNpcServiceContractSettlement.CommitSettlement rejectDailyWage =
                    delegate(WorldStateStore ignoredStore, NpcServiceContractWriteTicket ignoredTicket,
                        NpcServiceContractLedger ignoredCandidate, NpcInventoryUpdate ignoredCredit,
                        IList<NpcServiceTransferReceipt> ignoredReceipts, out string error)
                    {
                        dailyWageReachedCommit = player.GoldPieces == 10 && player.GetGoldAmount() == 90 &&
                            fixtureCredit.value == 80 && fixtureCredit.UID == creditUid &&
                            ContainsExactCredit(player, fixtureCredit);
                        error = "controlled daily-wage commit rejection";
                        return false;
                    };
                Require(!NativeNpcServiceContractSettlement.TrySettleWithCommitter(store,
                    store.CaptureNpcServiceContractWriteTicket(), player, ContractId,
                    suspended.Revision, NpcServiceEndReason.NaturalExpiry,
                    null, null, rejectDailyWage,
                    out rejectedLedger, out rejectedWage, out rejectedRefund, out failure) &&
                    failure != null && dailyWageReachedCommit,
                    "Forced Core refusal did not reach the daily-wage commit with an unchanged player purse.");
                Require(player.GoldPieces == 10 && player.GetGoldAmount() == 90 &&
                    fixtureCredit.value == 80 && fixtureCredit.UID == creditUid &&
                    ContainsExactCredit(player, fixtureCredit) &&
                    SaveLoadManager.Serialize(typeof(WorldState), store.Snapshot()) == beforeRejectedCommit,
                    "Daily-wage refusal changed player currency or saved Core ledgers.");

                NpcServiceContractWriteTicket finalTicket = store.CaptureNpcServiceContractWriteTicket();
                NpcServiceContractLedger completed;
                NpcServiceTransferReceipt wage, refund;
                Require(NativeNpcServiceContractSettlement.TrySettle(store, finalTicket, player,
                    ContractId, suspended.Revision, NpcServiceEndReason.NaturalExpiry,
                    null, null, out completed, out wage, out refund, out failure),
                    "Actual native late settlement failed: " + Failure(failure));
                VerifySettled(store, worker.Id, care.Id, settledAt, funding, wage, refund);
                Require(player.GoldPieces == 10 && player.GetGoldAmount() == 90 &&
                    fixtureCredit.UID == creditUid && fixtureCredit.value == 80 &&
                    ContainsExactCredit(player, fixtureCredit),
                    "Daily settlement changed the player purse or LOC identity/value.");

                WorldState saved = store.Snapshot();
                File.WriteAllText(tempPath, SaveLoadManager.Serialize(typeof(WorldState), saved));
                WorldState decoded = SaveLoadManager.Deserialize(typeof(WorldState),
                    File.ReadAllText(tempPath), true) as WorldState;
                Require(decoded != null && decoded.Version == WorldState.CurrentVersion,
                    "Native FullSerializer changed settlement schema or failed to decode.");
                var restored = new WorldStateStore(Seed);
                restored.Restore(decoded);
                VerifySettled(restored, worker.Id, care.Id, settledAt, funding, wage, refund);
                Require(!NativeNpcServiceContractSettlement.TrySettle(restored,
                    restored.CaptureNpcServiceContractWriteTicket(), player, ContractId,
                    suspended.Revision, NpcServiceEndReason.NaturalExpiry,
                    null, null, out rejectedLedger, out rejectedWage,
                    out rejectedRefund, out failure) && failure != null &&
                    player.GoldPieces == 10 && player.GetGoldAmount() == 90,
                    "Restored settled contract accepted replay or changed the actual player purse.");

            }
            finally
            {
                try
                {
                    if (fixtureCredit != null && ContainsExactCredit(player, fixtureCredit))
                        player.Items.RemoveItem(fixtureCredit);
                    player.GoldPieces = originalCoins;
                    foreach (CreditSnapshot credit in originalCredits)
                    {
                        credit.Item.value = credit.Value;
                        if (!ContainsExactCredit(player, credit.Item))
                            player.Items.AddItem(credit.Item, noStack: true);
                        Require(credit.Item.UID == credit.Uid && ContainsExactCredit(player, credit.Item),
                            "Controlled settlement did not restore original LOC identity.");
                    }
                }
                finally
                {
                    liveClock.Year = originalClock.Year;
                    liveClock.Month = originalClock.Month;
                    liveClock.Day = originalClock.Day;
                    liveClock.Hour = originalClock.Hour;
                    liveClock.Minute = originalClock.Minute;
                    liveClock.Second = originalClock.Second;
                    if (File.Exists(tempPath)) File.Delete(tempPath);
                }
                Require(player.GoldPieces == originalCoins && player.GetGoldAmount() == originalTotal &&
                    (fixtureCredit == null || !ContainsExactCredit(player, fixtureCredit)) &&
                    liveClock.Year == originalClock.Year && liveClock.Month == originalClock.Month &&
                    liveClock.Day == originalClock.Day && liveClock.Hour == originalClock.Hour &&
                    liveClock.Minute == originalClock.Minute && liveClock.Second == originalClock.Second,
                    "Controlled settlement did not restore exact player currency and native clock.");
            }
            Debug.Log("NPC_FRAMEWORK_SERVICE_SETTLEMENT_NATIVE_PASS: source=controlled-disposable-fixture; " +
                "mixed-player-coins-and-LOC-funding=true; health-owned-half-period-pause=true; " +
                "direct-start-payment-to-NPC-wallet=120; late-expiry-new-payment=0; " +
                "daily-wage-Core-refusal-unchanged=true; FullSerializer-roundtrip=true; " +
                "stale-foreign-extra-payment-rejected=true; gameplay-recruitment=false; human-save=false; " +
                "original-player-currency-and-clock-restored=true");
        }

        private static void CommitTransition(WorldStateStore store,
            Func<NpcServiceContract, NpcServiceContract> derive)
        {
            NpcServiceContractWriteTicket ticket = store.CaptureNpcServiceContractWriteTicket();
            NpcServiceContract current = ticket.SourceLedger.Read(ContractId);
            NpcServiceContract updated = current == null ? null : derive(current);
            NpcServiceContractLedger candidate;
            string error;
            Require(updated != null && NpcServiceContractLedger.TryReplace(ticket.SourceLedger,
                ticket.ExpectedLedgerRevision, updated, out candidate) &&
                store.TryCommitNpcServiceContracts(ticket, candidate,
                    new NpcServiceTransferReceipt[0], new NpcServiceRenewalAuthorization[0], out error),
                "Controlled service start or health-owned pause failed.");
        }

        private static void AssertRejectedWithoutCurrencyChange(WorldStateStore store,
            NpcServiceContractWriteTicket ticket, PlayerEntity player, string workerId,
            string wageId, string refundId, DaggerfallUnityItem credit, ulong creditUid,
            string label)
        {
            string before = SaveLoadManager.Serialize(typeof(WorldState), store.Snapshot());
            NpcServiceContractLedger candidate;
            NpcServiceTransferReceipt wage, refund;
            Exception failure;
            Require(!NativeNpcServiceContractSettlement.TrySettle(store, ticket, player,
                ContractId, store.ReadNpcServiceContracts().Read(ContractId).Revision,
                NpcServiceEndReason.NaturalExpiry, wageId, refundId,
                out candidate, out wage, out refund, out failure) && failure != null &&
                player.GoldPieces == 10 && player.GetGoldAmount() == 90 &&
                credit.UID == creditUid && credit.value == 80 && ContainsExactCredit(player, credit) &&
                SaveLoadManager.Serialize(typeof(WorldState), store.Snapshot()) == before,
                label + " altered native currency or saved settlement state.");
        }

        private static void VerifySettled(WorldStateStore store, string workerId,
            string careId, double settledAt, NpcServiceTransferReceipt funding,
            NpcServiceTransferReceipt wage, NpcServiceTransferReceipt refund)
        {
            NpcServiceContract contract = store.ReadNpcServiceContracts().Read(ContractId);
            NpcInventoryState wallet = store.ReadNpcInventory(workerId);
            WorldState saved = store.Snapshot();
            Require(contract != null && contract.Status == NpcServiceContractStatus.Terminated &&
                contract.EndReason == NpcServiceEndReason.NaturalExpiry &&
                contract.SuspensionOwnerKind == NpcServiceSuspensionOwnerKind.None &&
                contract.SuspensionOwnerId == null && contract.FundedGold == 120 &&
                contract.EarnedGold == 120 && contract.ReleasedGold == 120 &&
                contract.RefundedGold == 0 && contract.EscrowGold == 0 &&
                contract.EligibleServiceSeconds == 0 &&
                contract.LastServiceUpdateAt < contract.BlockEndAt &&
                contract.SettlementRecordedAt == settledAt &&
                contract.TransferReceipts.Count == 1 &&
                SameReceipt(contract.TransferReceipts[0], funding) &&
                wage == null && refund == null &&
                wallet != null && wallet.WalletGold == 127 && wallet.Revision == 1 &&
                wallet.LastTransactionId == FundingId &&
                saved.NpcServiceEvidence.CompletedTransfers.Count == 1 &&
                SameReceipt(saved.NpcServiceEvidence.CompletedTransfers[0], funding) &&
                store.ReadNpcHealthCare(workerId).Id == careId,
                "Native settlement lost exact wage, refund, wallet, pause, or receipt evidence.");
        }

        private static bool SameReceipt(NpcServiceTransferReceipt saved,
            NpcServiceTransferReceipt expected)
        {
            return saved != null && expected != null &&
                saved.ReceiptId == expected.ReceiptId &&
                saved.ContractId == expected.ContractId && saved.Kind == expected.Kind &&
                saved.FromOwnerId == expected.FromOwnerId &&
                saved.ToOwnerId == expected.ToOwnerId && saved.Amount == expected.Amount &&
                saved.GameTime == expected.GameTime;
        }

        private static bool SameReceipt(NpcServiceTransferReceiptState saved,
            NpcServiceTransferReceipt expected)
        {
            return saved != null && expected != null &&
                saved.ReceiptId == expected.ReceiptId &&
                saved.ContractId == expected.ContractId && saved.Kind == expected.Kind &&
                saved.FromOwnerId == expected.FromOwnerId &&
                saved.ToOwnerId == expected.ToOwnerId && saved.Amount == expected.Amount &&
                saved.GameTime == expected.GameTime;
        }

        private static bool ContainsExactCredit(PlayerEntity player, DaggerfallUnityItem item)
        {
            if (player == null || player.Items == null || item == null) return false;
            for (int i = 0; i < player.Items.Count; i++)
                if (ReferenceEquals(player.Items.GetItem(i), item)) return true;
            return false;
        }

        private static string Failure(Exception failure)
        { return failure == null ? "unknown" : failure.ToString(); }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
