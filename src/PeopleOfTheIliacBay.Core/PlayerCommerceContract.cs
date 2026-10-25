using System;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    // Player perspective. Existing NPC Purchase and Transfer jobs retain their
    // persisted meanings; this contract does not reinterpret those action kinds.
    public enum PlayerCommerceKind { Buy, Sell, Give, Take }
    public enum LoanPurpose { None, Lend, Return }
    public enum PlayerCommerceVenue { Merchant, Personal }
    public enum PlayerCommerceConfirmation { Rejected, Committed, Replay }

    public sealed class PlayerCommerceOrder
    {
        public string TransactionId, NpcId, SourceOwnerId, TargetOwnerId, ServiceCode, OriginReceiptId;
        public string PlayerInventoryContainerId, PlayerWalletContainerId;
        public string ExpectedPlayerInventoryToken, ExpectedPlayerWalletToken;
        public PlayerCommerceKind Kind;
        public LoanPurpose LoanPurpose;
        public PlayerCommerceVenue Venue;
        public ulong ItemUid;
        public int Quantity, QuotedTotalGold;
        public long ExpectedSourceInventoryRevision, ExpectedTargetInventoryRevision;
        public long ExpectedPayerWalletRevision, ExpectedPayeeWalletRevision;
    }

    // NPC revisions are persisted. Player tokens are computed from native
    // owner/container identity and the affected item quantity or gold.
    public sealed class PlayerCommerceState
    {
        public string SourceOwnerId, TargetOwnerId, PayerOwnerId, PayeeOwnerId, ServiceCode;
        public string PlayerInventoryContainerId, PlayerWalletContainerId;
        public string PlayerInventoryToken, PlayerWalletToken;
        public ulong ItemUid, RecipientItemUid;
        public int SourceQuantity, RecipientQuantity, ReceiverCapacity, PayerGold, PayeeGold;
        public long SourceInventoryRevision, TargetInventoryRevision;
        public long PayerWalletRevision, PayeeWalletRevision;
        public bool SourceAccessible, ServiceAvailable, PlayerConsented, NpcConsented;
        public bool NpcAdult, NpcEligible, NpcWilling, NpcNeedsItem, NpcValuesItem;
        public bool ItemEquipped, ItemOccupationallyNecessary, ItemUnique, ItemKnownStolen, ItemQuestProtected;
        public int PlayerMercantile;
    }

    // Native observation after an atomic transfer. This is evidence, not a
    // command to execute a second transfer or payment.
    public sealed class PlayerCommerceObservation
    {
        public string TransactionId, SourceOwnerId, TargetOwnerId;
        public string PlayerInventoryTokenAfter, PlayerWalletTokenAfter;
        public ulong SourceItemUid, RecipientItemUid;
        public int Quantity, SourceQuantityAfter, RecipientQuantityAfter;
        public int PayerGoldAfter, PayeeGoldAfter;
        public long SourceInventoryRevisionAfter, TargetInventoryRevisionAfter;
        public long PayerWalletRevisionAfter, PayeeWalletRevisionAfter;
        public bool NativeItemTransferObserved, NativePaymentObserved;
    }

    public sealed class PlayerCommerceReceipt
    {
        public string TransactionId { get; private set; }
        public PlayerCommerceKind Kind { get; private set; }
        public PlayerCommerceVenue Venue { get; private set; }
        public string NpcId { get; private set; }
        public ulong SourceItemUid { get; private set; }
        public ulong RecipientItemUid { get; private set; }
        public int Quantity { get; private set; }
        public int PaidGold { get; private set; }
        public LoanPurpose LoanPurpose { get; private set; }
        public string OriginReceiptId { get; private set; }
        public long SourceInventoryRevisionAfter { get; private set; }
        public long TargetInventoryRevisionAfter { get; private set; }
        public long PayerWalletRevisionAfter { get; private set; }
        public long PayeeWalletRevisionAfter { get; private set; }
        public string PlayerInventoryTokenAfter { get; private set; }
        public string PlayerWalletTokenAfter { get; private set; }
        private string sourceOwnerId, targetOwnerId, serviceCode;
        private string playerInventoryContainerId, playerWalletContainerId;
        private string expectedPlayerInventoryToken, expectedPlayerWalletToken;
        private long expectedSourceRevision, expectedTargetRevision, expectedPayerRevision, expectedPayeeRevision;
        private int sourceQuantityAfter, recipientQuantityAfter, payerGoldAfter, payeeGoldAfter;

        internal PlayerCommerceReceipt(PlayerCommerceOrder order, PlayerCommerceObservation observed)
        {
            TransactionId = order.TransactionId; Kind = order.Kind; Venue = order.Venue; NpcId = order.NpcId;
            SourceItemUid = order.ItemUid; RecipientItemUid = observed.RecipientItemUid;
            Quantity = order.Quantity; PaidGold = order.QuotedTotalGold;
            LoanPurpose=order.LoanPurpose;OriginReceiptId=order.OriginReceiptId;
            SourceInventoryRevisionAfter = observed.SourceInventoryRevisionAfter;
            TargetInventoryRevisionAfter = observed.TargetInventoryRevisionAfter;
            PayerWalletRevisionAfter = observed.PayerWalletRevisionAfter;
            PayeeWalletRevisionAfter = observed.PayeeWalletRevisionAfter;
            PlayerInventoryTokenAfter = observed.PlayerInventoryTokenAfter;
            PlayerWalletTokenAfter = observed.PlayerWalletTokenAfter;
            sourceOwnerId = order.SourceOwnerId; targetOwnerId = order.TargetOwnerId;
            serviceCode = order.ServiceCode;
            playerInventoryContainerId = order.PlayerInventoryContainerId;
            playerWalletContainerId = order.PlayerWalletContainerId;
            expectedPlayerInventoryToken = order.ExpectedPlayerInventoryToken;
            expectedPlayerWalletToken = order.ExpectedPlayerWalletToken;
            expectedSourceRevision = order.ExpectedSourceInventoryRevision;
            expectedTargetRevision = order.ExpectedTargetInventoryRevision;
            expectedPayerRevision = order.ExpectedPayerWalletRevision;
            expectedPayeeRevision = order.ExpectedPayeeWalletRevision;
            sourceQuantityAfter = observed.SourceQuantityAfter;
            recipientQuantityAfter = observed.RecipientQuantityAfter;
            payerGoldAfter = observed.PayerGoldAfter; payeeGoldAfter = observed.PayeeGoldAfter;
        }

        internal bool Matches(PlayerCommerceOrder order, PlayerCommerceObservation observed)
        {
            return order != null && observed != null && TransactionId == order.TransactionId &&
                TransactionId == observed.TransactionId && Kind == order.Kind && Venue == order.Venue && NpcId == order.NpcId &&
                SourceItemUid == order.ItemUid && SourceItemUid == observed.SourceItemUid &&
                RecipientItemUid == observed.RecipientItemUid && Quantity == order.Quantity &&
                Quantity == observed.Quantity && PaidGold == order.QuotedTotalGold && LoanPurpose==order.LoanPurpose &&
                OriginReceiptId==order.OriginReceiptId &&
                sourceOwnerId == order.SourceOwnerId && sourceOwnerId == observed.SourceOwnerId &&
                targetOwnerId == order.TargetOwnerId && targetOwnerId == observed.TargetOwnerId &&
                serviceCode == order.ServiceCode &&
                playerInventoryContainerId == order.PlayerInventoryContainerId &&
                playerWalletContainerId == order.PlayerWalletContainerId &&
                expectedPlayerInventoryToken == order.ExpectedPlayerInventoryToken &&
                expectedPlayerWalletToken == order.ExpectedPlayerWalletToken &&
                expectedSourceRevision == order.ExpectedSourceInventoryRevision &&
                expectedTargetRevision == order.ExpectedTargetInventoryRevision &&
                expectedPayerRevision == order.ExpectedPayerWalletRevision &&
                expectedPayeeRevision == order.ExpectedPayeeWalletRevision &&
                sourceQuantityAfter == observed.SourceQuantityAfter &&
                recipientQuantityAfter == observed.RecipientQuantityAfter &&
                payerGoldAfter == observed.PayerGoldAfter && payeeGoldAfter == observed.PayeeGoldAfter &&
                SourceInventoryRevisionAfter == observed.SourceInventoryRevisionAfter &&
                TargetInventoryRevisionAfter == observed.TargetInventoryRevisionAfter &&
                PayerWalletRevisionAfter == observed.PayerWalletRevisionAfter &&
                PayeeWalletRevisionAfter == observed.PayeeWalletRevisionAfter &&
                PlayerInventoryTokenAfter == observed.PlayerInventoryTokenAfter &&
                PlayerWalletTokenAfter == observed.PlayerWalletTokenAfter;
        }

        internal bool MatchesNpcUpdate(NpcInventoryState before, NpcInventoryUpdate after)
        {
            if (before == null || after == null || before.PersonId != NpcId || after.PersonId != NpcId ||
                after.ExpectedRevision != before.Revision || before.Revision >= NpcInventoryLimits.MaximumRevision ||
                after.Items == null) return false;
            bool npcSource = Kind == PlayerCommerceKind.Buy || Kind == PlayerCommerceKind.Take;
            long expectedInventory = npcSource ? expectedSourceRevision : expectedTargetRevision;
            long observedInventory = npcSource ? SourceInventoryRevisionAfter : TargetInventoryRevisionAfter;
            long expectedWallet = Kind == PlayerCommerceKind.Buy ? expectedPayeeRevision : expectedPayerRevision;
            long observedWallet = Kind == PlayerCommerceKind.Buy ? PayeeWalletRevisionAfter : PayerWalletRevisionAfter;
            int walletBefore = Kind == PlayerCommerceKind.Buy ? payeeGoldAfter - PaidGold : payerGoldAfter + PaidGold;
            int walletAfter = Kind == PlayerCommerceKind.Buy ? payeeGoldAfter : payerGoldAfter;
            if (before.Revision != expectedInventory || before.Revision != expectedWallet ||
                observedInventory != before.Revision + 1 || observedWallet != before.Revision + (PaidGold > 0 ? 1 : 0) ||
                before.WalletGold != walletBefore || after.WalletGold != walletAfter) return false;
            ulong affectedUid = npcSource ? SourceItemUid : RecipientItemUid;
            bool beforeHas = false, afterHas = false, affectedChanged = false;
            string oldPayload = null, newPayload = null;
            foreach (var item in before.Items)
                if (item.Uid == affectedUid) { beforeHas = true; oldPayload = item.NativePayload; }
            foreach (var item in after.Items)
                if (item != null && item.Uid == affectedUid) { afterHas = true; newPayload = item.NativePayload; }
            affectedChanged = beforeHas != afterHas || oldPayload != newPayload;
            if (!(npcSource ? beforeHas && affectedChanged : afterHas && affectedChanged)) return false;
            foreach (var item in before.Items)
            {
                if (item.Uid == affectedUid) continue;
                bool identical = false;
                foreach (var next in after.Items)
                    if (next != null && next.Uid == item.Uid && next.NativePayload == item.NativePayload)
                    { identical = true; break; }
                if (!identical) return false;
            }
            foreach (var item in after.Items)
            {
                if (item == null || item.Uid == affectedUid) continue;
                bool identical = false;
                foreach (var prior in before.Items)
                    if (prior.Uid == item.Uid && prior.NativePayload == item.NativePayload)
                    { identical = true; break; }
                if (!identical) return false;
            }
            return true;
        }
    }

    public static class PlayerCommerceContract
    {
        // Compatibility for native adapters compiled against the former gate.
        // Zero deliberately means Mercantile never controls trade access.
        public const int MinimumPersonalTradeMercantile = 0;

        public static string InventorySnapshotToken(string ownerId, string containerId, ulong itemUid, int quantity)
        {
            if (!Id(ownerId) || !Id(containerId) || itemUid == 0 && quantity != 0 ||
                quantity < 0 || quantity > 1000000) return null;
            return "inventory|" + Segment(ownerId) + "|" + Segment(containerId) + "|" +
                itemUid.ToString(CultureInfo.InvariantCulture) + "|" + quantity.ToString(CultureInfo.InvariantCulture);
        }

        public static string WalletSnapshotToken(string ownerId, string containerId, int gold)
        {
            if (!Id(ownerId) || !Id(containerId) || gold < 0 || gold > NpcInventoryLimits.MaximumWalletGold)
                return null;
            return "wallet|" + Segment(ownerId) + "|" + Segment(containerId) + "|" +
                gold.ToString(CultureInfo.InvariantCulture);
        }

        public static bool CanBegin(PlayerCommerceOrder order, PlayerCommerceState state)
        {
            if (order == null || state == null || !Id(order.TransactionId) || !Id(order.NpcId) ||
                order.NpcId == "player" || (int)order.Kind < 0 || (int)order.Kind > 3 ||
                (int)order.LoanPurpose < 0 || (int)order.LoanPurpose > 2 ||
                (int)order.Venue < 0 || (int)order.Venue > 1 ||
                order.ItemUid == 0 || order.Quantity < 1 || order.Quantity > 1000000 ||
                order.ExpectedSourceInventoryRevision < 0 || order.ExpectedTargetInventoryRevision < 0 ||
                order.ExpectedPayerWalletRevision < 0 || order.ExpectedPayeeWalletRevision < 0 ||
                order.ExpectedSourceInventoryRevision >= NpcInventoryLimits.MaximumRevision ||
                order.ExpectedTargetInventoryRevision >= NpcInventoryLimits.MaximumRevision ||
                order.ExpectedPayerWalletRevision >= NpcInventoryLimits.MaximumRevision ||
                order.ExpectedPayeeWalletRevision >= NpcInventoryLimits.MaximumRevision)
                return false;
            bool freeTransfer = order.Kind == PlayerCommerceKind.Give || order.Kind == PlayerCommerceKind.Take;
            if ((!freeTransfer && (order.LoanPurpose != LoanPurpose.None || order.OriginReceiptId != null)) ||
                (order.LoanPurpose == LoanPurpose.None && order.OriginReceiptId != null) ||
                (order.LoanPurpose == LoanPurpose.Lend && order.OriginReceiptId != null) ||
                (order.LoanPurpose == LoanPurpose.Return && !Id(order.OriginReceiptId))) return false;
            bool npcSource = order.Kind == PlayerCommerceKind.Buy || order.Kind == PlayerCommerceKind.Take;
            bool paid = order.Kind == PlayerCommerceKind.Buy || order.Kind == PlayerCommerceKind.Sell;
            string source = npcSource ? order.NpcId : "player";
            string target = npcSource ? "player" : order.NpcId;
            string payer = order.Kind == PlayerCommerceKind.Buy ? "player" : order.NpcId;
            string payee = order.Kind == PlayerCommerceKind.Buy ? order.NpcId : "player";
            ulong playerItemUid = npcSource ? state.RecipientItemUid : state.ItemUid;
            int playerItemQuantity = npcSource ? state.RecipientQuantity : state.SourceQuantity;
            int playerGold = payer == "player" ? state.PayerGold : state.PayeeGold;
            if (order.SourceOwnerId != source || order.TargetOwnerId != target ||
                state.SourceOwnerId != source || state.TargetOwnerId != target ||
                state.PayerOwnerId != payer || state.PayeeOwnerId != payee ||
                state.ItemUid != order.ItemUid || !state.SourceAccessible ||
                !state.PlayerConsented || !state.NpcConsented ||
                state.ItemEquipped || state.ItemOccupationallyNecessary || state.ItemUnique ||
                state.ItemKnownStolen || state.ItemQuestProtected ||
                state.SourceQuantity < order.Quantity || state.SourceQuantity > 1000000 ||
                state.RecipientQuantity < 0 || state.RecipientQuantity > 1000000 - order.Quantity ||
                (state.RecipientItemUid == 0) != (state.RecipientQuantity == 0) ||
                state.ReceiverCapacity < order.Quantity || state.ReceiverCapacity > 1000000 ||
                state.PayerGold < 0 || state.PayeeGold < 0 ||
                state.PayerGold > NpcInventoryLimits.MaximumWalletGold ||
                state.PayeeGold > NpcInventoryLimits.MaximumWalletGold ||
                state.SourceInventoryRevision != order.ExpectedSourceInventoryRevision ||
                state.TargetInventoryRevision != order.ExpectedTargetInventoryRevision ||
                state.PayerWalletRevision != order.ExpectedPayerWalletRevision ||
                state.PayeeWalletRevision != order.ExpectedPayeeWalletRevision ||
                source == "player" && state.SourceInventoryRevision != 0 ||
                target == "player" && state.TargetInventoryRevision != 0 ||
                payer == "player" && state.PayerWalletRevision != 0 ||
                payee == "player" && state.PayeeWalletRevision != 0 ||
                !Id(order.PlayerInventoryContainerId) || !Id(order.PlayerWalletContainerId) ||
                order.PlayerInventoryContainerId != state.PlayerInventoryContainerId ||
                order.PlayerWalletContainerId != state.PlayerWalletContainerId ||
                order.ExpectedPlayerInventoryToken != state.PlayerInventoryToken ||
                order.ExpectedPlayerWalletToken != state.PlayerWalletToken ||
                state.PlayerInventoryToken != InventorySnapshotToken("player", state.PlayerInventoryContainerId,
                    playerItemUid, playerItemQuantity) ||
                state.PlayerWalletToken != WalletSnapshotToken("player", state.PlayerWalletContainerId, playerGold))
                return false;
            if (paid)
            {
                if (!state.NpcAdult || !state.NpcEligible ||
                    order.QuotedTotalGold <= 0 || order.QuotedTotalGold > state.PayerGold ||
                    order.QuotedTotalGold > NpcInventoryLimits.MaximumWalletGold - state.PayeeGold)
                    return false;
                if (order.Venue == PlayerCommerceVenue.Merchant)
                    return Id(order.ServiceCode) && state.ServiceAvailable &&
                        state.ServiceCode == order.ServiceCode;
                return order.ServiceCode == null && state.NpcWilling &&
                    (order.Kind != PlayerCommerceKind.Sell || state.NpcNeedsItem && state.NpcValuesItem);
            }
            return order.ServiceCode == null && order.QuotedTotalGold == 0;
        }

        public static PlayerCommerceConfirmation Confirm(PlayerCommerceOrder order, PlayerCommerceState state,
            PlayerCommerceObservation observed, PlayerCommerceReceipt prior, out PlayerCommerceReceipt receipt)
        {
            receipt = null;
            bool paid = order != null && order.QuotedTotalGold > 0;
            bool playerSource = order != null && order.SourceOwnerId == "player";
            bool playerPayer = order != null && order.Kind == PlayerCommerceKind.Buy;
            ulong playerItemUid = observed == null ? 0 : playerSource ? order.ItemUid : observed.RecipientItemUid;
            int playerItemQuantity = observed == null ? 0 : playerSource ? observed.SourceQuantityAfter : observed.RecipientQuantityAfter;
            int playerGold = observed == null ? 0 : playerPayer ? observed.PayerGoldAfter : observed.PayeeGoldAfter;
            if (!CanBegin(order, state) || observed == null || !observed.NativeItemTransferObserved ||
                observed.TransactionId != order.TransactionId || observed.SourceItemUid != order.ItemUid ||
                observed.SourceOwnerId != order.SourceOwnerId || observed.TargetOwnerId != order.TargetOwnerId ||
                observed.RecipientItemUid == 0 || observed.Quantity != order.Quantity ||
                state.RecipientItemUid != 0 && observed.RecipientItemUid != state.RecipientItemUid ||
                observed.SourceQuantityAfter != state.SourceQuantity - order.Quantity ||
                observed.RecipientQuantityAfter != state.RecipientQuantity + order.Quantity ||
                observed.SourceInventoryRevisionAfter != state.SourceInventoryRevision + (playerSource ? 0 : 1) ||
                observed.TargetInventoryRevisionAfter != state.TargetInventoryRevision + (playerSource ? 1 : 0) ||
                observed.PayerGoldAfter != state.PayerGold - order.QuotedTotalGold ||
                observed.PayeeGoldAfter != state.PayeeGold + order.QuotedTotalGold ||
                observed.PayerWalletRevisionAfter != state.PayerWalletRevision + (paid && !playerPayer ? 1 : 0) ||
                observed.PayeeWalletRevisionAfter != state.PayeeWalletRevision + (paid && playerPayer ? 1 : 0) ||
                observed.NativePaymentObserved != paid ||
                observed.PlayerInventoryTokenAfter != InventorySnapshotToken("player",
                    state.PlayerInventoryContainerId, playerItemUid, playerItemQuantity) ||
                observed.PlayerInventoryTokenAfter == state.PlayerInventoryToken ||
                observed.PlayerWalletTokenAfter != WalletSnapshotToken("player",
                    state.PlayerWalletContainerId, playerGold) ||
                (observed.PlayerWalletTokenAfter == state.PlayerWalletToken) != !paid)
                return PlayerCommerceConfirmation.Rejected;
            if (prior != null)
            {
                if (!prior.Matches(order, observed)) return PlayerCommerceConfirmation.Rejected;
                receipt = prior;
                return PlayerCommerceConfirmation.Replay;
            }
            receipt = new PlayerCommerceReceipt(order, observed);
            return PlayerCommerceConfirmation.Committed;
        }

        private static bool Id(string value) { return !string.IsNullOrWhiteSpace(value) && value.Length <= 240; }
        private static string Segment(string value) { return value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value; }
    }
}
