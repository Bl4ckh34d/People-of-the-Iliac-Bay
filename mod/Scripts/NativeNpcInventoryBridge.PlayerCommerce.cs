using System;
using DaggerfallConnect;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public static partial class NativeNpcInventoryBridge
    {
        // Personal trade only. Merchant stock keeps its normal shop adapter.
        // Callbacks read current actor, relationship, item interest and source safety.
        // The bridge independently verifies exact native source ownership.
        public static bool TryPersonalTrade(WorldStateStore store, NativeNpcInventoryBinding npc,
            DaggerfallEntity npcEntity, PlayerEntity player, PlayerCommerceOrder order,
            double committedGameTime, Func<bool> npcAdultNow,
            Func<bool> npcEligibleNow, Func<bool> npcWillingNow, Func<bool> playerConsentedNow,
            Func<bool> npcNeedsItemNow, Func<bool> npcValuesItemNow, Func<int> quoteNow,
            Func<DaggerfallUnityItem, bool> occupationallyDisposableNow,
            Func<DaggerfallUnityItem, bool> itemUniqueNow,
            Func<DaggerfallUnityItem, bool> itemSourceAllowedNow,
            out PlayerCommerceReceipt receipt, out string reason)
        {
            receipt = null;
            reason = "The personal trade is no longer available.";
            DaggerfallUnityItem original = null, moved = null;
            ItemCollection source = null, destination = null;
            int originalCount = 0, playerGoldBefore = 0;
            bool touched = false, committed = false, commitOutcomeUnknown = false;
            try
            {
                if (store == null || npc == null || npcEntity == null || player == null ||
                    player.Items == null || npc.Items == null || !ReferenceEquals(npcEntity.Items, npc.Items) ||
                    order == null || order.Venue != PlayerCommerceVenue.Personal ||
                    (order.Kind != PlayerCommerceKind.Buy && order.Kind != PlayerCommerceKind.Sell &&
                     order.Kind != PlayerCommerceKind.Give && order.Kind != PlayerCommerceKind.Take) ||
                    order.NpcId != npc.PersonId || order.ItemUid == 0 || order.Quantity < 1 ||
                    string.IsNullOrWhiteSpace(order.TransactionId) ||
                    double.IsNaN(committedGameTime) || double.IsInfinity(committedGameTime) ||
                    committedGameTime < 0 ||
                    npcAdultNow == null || npcEligibleNow == null || npcWillingNow == null ||
                    playerConsentedNow == null || npcNeedsItemNow == null || npcValuesItemNow == null ||
                    quoteNow == null ||
                    occupationallyDisposableNow == null || itemUniqueNow == null ||
                    itemSourceAllowedNow == null ||
                    string.IsNullOrWhiteSpace(order.PlayerInventoryContainerId) ||
                    string.IsNullOrWhiteSpace(order.PlayerWalletContainerId)) return false;

                var saved = CheckBinding(store, npc, order.TransactionId);
                if (saved == null || saved.Revision >= NpcInventoryLimits.MaximumRevision) return false;
                bool npcSource = order.Kind == PlayerCommerceKind.Buy || order.Kind == PlayerCommerceKind.Take;
                bool buying = order.Kind == PlayerCommerceKind.Buy;
                bool paid = order.Kind == PlayerCommerceKind.Buy || order.Kind == PlayerCommerceKind.Sell;
                if (order.LoanPurpose == LoanPurpose.Lend && !store.CanBeginPlayerItemLoan(npc.PersonId)) return false;
                if (order.LoanPurpose == LoanPurpose.Return && !store.TryReadOpenPlayerItemLoan(npc.PersonId,
                    order.OriginReceiptId, order.ItemUid, order.Quantity, out _)) return false;
                source = npcSource ? npc.Items : player.Items;
                destination = npcSource ? player.Items : npc.Items;
                if (ReferenceEquals(source, destination)) return false;
                original = source.GetItem(order.ItemUid);
                if (original == null || original.UID != order.ItemUid ||
                    original.stackCount < order.Quantity || destination.GetItem(order.ItemUid) != null ||
                    order.Quantity < original.stackCount &&
                        (!original.IsAStack() || !string.IsNullOrEmpty(original.GetSaveData().className))) return false;
                string owner = store.NpcInventoryItemOwner(order.ItemUid);
                if (npcSource ? owner != npc.PersonId : owner != null) return false;

                float unitWeight = original.EffectiveUnitWeightInKg();
                float remainingWeight = npcSource ? player.MaxEncumbrance - player.CarriedWeight :
                    npcEntity.MaxEncumbrance - npcEntity.Items.GetWeight();
                if (unitWeight < 0 || remainingWeight < 0) return false;
                int capacity = unitWeight == 0 ? 1000000 :
                    Math.Max(0, (int)Math.Min(1000000d, Math.Floor(remainingWeight / unitWeight)));
                if (capacity < order.Quantity) return false;
                if (!npcSource && !CanAppendExactItem(npc.Items, original, out reason)) return false;

                playerGoldBefore = player.GoldPieces;
                if (playerGoldBefore < 0 || playerGoldBefore > NpcInventoryLimits.MaximumWalletGold) return false;
                bool protectedItem = original.IsQuestItem || original.IsSummoned ||
                    original is HouseholdPropertyKeyItem || original.ItemGroup == ItemGroups.Currency ||
                    original.ItemGroup == ItemGroups.Transportation || original.ItemGroup == ItemGroups.Deeds;
                var state = new PlayerCommerceState {
                    SourceOwnerId = npcSource ? npc.PersonId : "player",
                    TargetOwnerId = npcSource ? "player" : npc.PersonId,
                    PayerOwnerId = buying ? "player" : npc.PersonId,
                    PayeeOwnerId = buying ? npc.PersonId : "player",
                    ItemUid = order.ItemUid, SourceQuantity = original.stackCount,
                    RecipientItemUid = 0, RecipientQuantity = 0, ReceiverCapacity = capacity,
                    PayerGold = buying ? playerGoldBefore : saved.WalletGold,
                    PayeeGold = buying ? saved.WalletGold : playerGoldBefore,
                    SourceInventoryRevision = npcSource ? saved.Revision : 0,
                    TargetInventoryRevision = npcSource ? 0 : saved.Revision,
                    PayerWalletRevision = buying ? 0 : saved.Revision,
                    PayeeWalletRevision = buying ? saved.Revision : 0,
                    PlayerInventoryContainerId = order.PlayerInventoryContainerId,
                    PlayerWalletContainerId = order.PlayerWalletContainerId,
                    PlayerInventoryToken = PlayerCommerceContract.InventorySnapshotToken("player",
                        order.PlayerInventoryContainerId, npcSource ? 0 : order.ItemUid,
                        npcSource ? 0 : original.stackCount),
                    PlayerWalletToken = PlayerCommerceContract.WalletSnapshotToken("player",
                        order.PlayerWalletContainerId, playerGoldBefore),
                    SourceAccessible = true, ServiceAvailable = false, ServiceCode = null,
                    PlayerConsented = playerConsentedNow(), NpcConsented = npcWillingNow(),
                    NpcAdult = npcAdultNow(), NpcEligible = npcEligibleNow(),
                    NpcWilling = npcWillingNow(), NpcNeedsItem = npcNeedsItemNow(),
                    NpcValuesItem = npcValuesItemNow(),
                    PlayerMercantile = player.Skills.GetLiveSkillValue(DFCareer.Skills.Mercantile),
                    ItemEquipped = original.IsEquipped,
                    ItemOccupationallyNecessary = !occupationallyDisposableNow(original),
                    ItemUnique = original.IsArtifact || itemUniqueNow(original),
                    ItemKnownStolen = !itemSourceAllowedNow(original),
                    ItemQuestProtected = protectedItem
                };
                if (quoteNow() != order.QuotedTotalGold || !PlayerCommerceContract.CanBegin(order, state))
                    return false;

                originalCount = original.stackCount;
                touched = true;
                moved = order.Quantity == originalCount ? original : source.SplitStack(original, order.Quantity);
                if (moved == null || moved.stackCount != order.Quantity || moved.UID == 0)
                    throw new InvalidOperationException("Native item split changed the agreed quantity.");
                if (!ReferenceEquals(source.GetItem(moved.UID), moved))
                    throw new InvalidOperationException("Native split UID collided with another source item.");
                source.RemoveItem(moved);
                destination.AddItem(moved, noStack: true);
                if (source.Contains(moved) || !ReferenceEquals(destination.GetItem(moved.UID), moved) ||
                    (order.Quantity < originalCount && original.stackCount != originalCount - order.Quantity))
                    throw new InvalidOperationException("Native transfer changed exact item identity or quantity.");
                int playerGoldAfter = buying ? playerGoldBefore - order.QuotedTotalGold :
                    order.Kind == PlayerCommerceKind.Sell ? checked(playerGoldBefore + order.QuotedTotalGold) : playerGoldBefore;
                int npcGoldAfter = buying ? checked(saved.WalletGold + order.QuotedTotalGold) :
                    order.Kind == PlayerCommerceKind.Sell ? saved.WalletGold - order.QuotedTotalGold : saved.WalletGold;
                player.GoldPieces = playerGoldAfter;
                if (player.GoldPieces != playerGoldAfter || player.CarriedWeight > player.MaxEncumbrance ||
                    npcEntity.Items.GetWeight() > npcEntity.MaxEncumbrance ||
                    !npcAdultNow() || !npcEligibleNow() || !npcWillingNow() || !playerConsentedNow() ||
                    // Delivery itself satisfies the inventory need. It was checked
                    // before the synchronous move; value and consent still apply.
                    (order.Kind == PlayerCommerceKind.Sell && !npcValuesItemNow()) ||
                    quoteNow() != order.QuotedTotalGold ||
                    original.IsQuestItem || original.IsEquipped || original.IsSummoned || original.IsArtifact ||
                    !occupationallyDisposableNow(original) || itemUniqueNow(original) ||
                    !itemSourceAllowedNow(original))
                    throw new InvalidOperationException("Trade capacity, consent, quote or item safety changed.");

                // Preflight contract on exact native item/coin observation.
                // Final receipt below uses the committed NPC revision.
                var observation = new PlayerCommerceObservation {
                    TransactionId = order.TransactionId, SourceOwnerId = state.SourceOwnerId,
                    TargetOwnerId = state.TargetOwnerId, SourceItemUid = order.ItemUid,
                    RecipientItemUid = moved.UID, Quantity = order.Quantity,
                    SourceQuantityAfter = source.GetItem(order.ItemUid) == null ? 0 :
                        source.GetItem(order.ItemUid).stackCount,
                    RecipientQuantityAfter = destination.GetItem(moved.UID).stackCount,
                    PayerGoldAfter = buying ? player.GoldPieces : npcGoldAfter,
                    PayeeGoldAfter = buying ? npcGoldAfter : player.GoldPieces,
                    SourceInventoryRevisionAfter = npcSource ? saved.Revision + 1 : 0,
                    TargetInventoryRevisionAfter = npcSource ? 0 : saved.Revision + 1,
                    PayerWalletRevisionAfter = buying ? 0 : saved.Revision + (paid ? 1 : 0),
                    PayeeWalletRevisionAfter = buying ? saved.Revision + 1 : 0,
                    PlayerInventoryTokenAfter = PlayerCommerceContract.InventorySnapshotToken("player",
                        order.PlayerInventoryContainerId, npcSource ? moved.UID : order.ItemUid,
                        npcSource ? order.Quantity : originalCount - order.Quantity),
                    PlayerWalletTokenAfter = PlayerCommerceContract.WalletSnapshotToken("player",
                        order.PlayerWalletContainerId, player.GoldPieces),
                    NativeItemTransferObserved = true, NativePaymentObserved = paid
                };
                PlayerCommerceReceipt preflight;
                if (PlayerCommerceContract.Confirm(order, state, observation, null, out preflight) !=
                    PlayerCommerceConfirmation.Committed)
                    throw new InvalidOperationException("Native exchange failed the personal trade contract.");
                // Derive identities from the accepted transaction, before the
                // uncertain commit boundary. A retry reuses exactly these IDs.
                string receiptId = PersonalTradeCommitBoundary.SourceBoundId("receipt", order.TransactionId);
                string effectId = PersonalTradeCommitBoundary.SourceBoundId("effect", order.TransactionId);
                // NPC inventory and the receipt ledger become visible in one
                // store swap. No fallible readback runs after that commit.
                var update = new NpcInventoryUpdate {
                    PersonId = npc.PersonId, ExpectedRevision = saved.Revision,
                    WalletGold = npcGoldAfter, Items = CaptureItems(npc.Items) };
                commitOutcomeUnknown = true;
                PlayerCommerceReceipt committedReceipt;
                bool accepted = PersonalTradeCommitBoundary.TryCommit(
                    () => store.TryCommitPlayerCommerce(update, preflight, receiptId, effectId,
                        committedGameTime),
                    preflight, out committedReceipt);
                commitOutcomeUnknown = false;
                if (!accepted)
                    throw new InvalidOperationException("Personal trade inventory, receipt or ownership changed.");
                committed = true;
                npc.Revision = saved.Revision + 1;
                receipt = committedReceipt;
                reason = null;
                return true;
            }
            catch (Exception error)
            {
                if (commitOutcomeUnknown) throw;
                reason = error.Message;
                return false;
            }
            finally
            {
                if (touched && !committed && !commitOutcomeUnknown)
                {
                    PersonalTradeCommitBoundary.Restore(original, moved, originalCount,
                        item => item.UID, uid => source.GetItem(uid), uid => destination.GetItem(uid),
                        item => source.AddItem(item, noStack: true), item => source.RemoveItem(item),
                        item => destination.RemoveItem(item), (item, count) => item.stackCount = count,
                        () => player.GoldPieces = playerGoldBefore,
                        () => player.GoldPieces == playerGoldBefore);
                }
            }
        }
    }
}
