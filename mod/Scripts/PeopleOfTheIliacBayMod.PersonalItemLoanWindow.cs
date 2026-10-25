using System;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        // A loan is a one-way, zero-gold use of the existing personal-trade
        // window. The window stages only; this factory owns the sole native
        // transfer/receipt commit and never starts an NPC action.
        internal bool TryOpenPersonalItemLoanWindow(DaggerfallTalkWindow talk, LoanPurpose purpose,
            out string reason)
        {
            PersonalTradeWindowSession session;
            var actor = conversationActor;
            string person = conversationPersonId;
            bool pauseCurrentWork = PersonalTradeCurrentWorkCanPause(store, person);
            Action<bool, string> closed = (committed, closeReason) =>
            {
                if (actor != null && actor.Resident != null && actor.Resident.Id == person)
                    actor.SetConversationFacing(false);
            };
            if (!TryCreatePersonalItemLoanWindowSession(purpose, closed, out session, out reason)) return false;
            personalCommerceBeforeClose?.Invoke();
            DaggerfallUI.Instance.PopToHUD();
            if (pauseCurrentWork && actor != null && actor.Resident != null && actor.Resident.Id == person)
                actor.SetConversationFacing(true);
            PersonalTradeWindow.Open(DaggerfallUI.UIManager, null, session, mod);
            return true;
        }

        internal bool TryCreatePersonalItemLoanWindowSession(LoanPurpose purpose,
            out PersonalTradeWindowSession session, out string reason)
        { return TryCreatePersonalItemLoanWindowSession(purpose, null, out session, out reason); }

        private bool TryCreatePersonalItemLoanWindowSession(LoanPurpose purpose, Action<bool, string> closed,
            out PersonalTradeWindowSession session, out string reason)
        {
            session = null;
            reason = "This item loan is no longer available.";
            bool lending = purpose == LoanPurpose.Lend;
            bool returning = purpose == LoanPurpose.Return;
            if (!lending && !returning) return false;
            var actor = conversationActor;
            string person = conversationPersonId;
            var game = GameManager.Instance;
            if (!PersonalTradeActorAvailable(actor, person) || game == null || game.PlayerEntity == null ||
                game.PlayerEntity.Items == null || lending &&
                (!store.CanBeginPlayerItemLoan(person) || !PersonalTradeWilling(actor, person))) return false;
            var owner = actor.GetComponent<NativeNpcInventoryOwner>();
            var behaviour = actor.GetComponent<DaggerfallEntityBehaviour>();
            if (owner == null || owner.Binding == null || behaviour == null || behaviour.Entity == null ||
                !ReferenceEquals(behaviour.Entity.Items, owner.Binding.Items)) return false;
            var saved = store.ReadNpcInventory(person);
            if (!NpcInventoryReadAuthority.Matches(saved, person, owner.Binding.Revision,
                NativeNpcInventoryBridge.CaptureItems(owner.Binding.Items))) return false;

            WorldStateStore sessionStore = store;
            NativeNpcInventoryBinding binding = owner.Binding;
            DaggerfallEntity entity = behaviour.Entity;
            PlayerEntity player = game.PlayerEntity;
            long epoch = saved.Revision;
            PersonalTradeSide requiredSide = lending ? PersonalTradeSide.Npc : PersonalTradeSide.Player;
            PlayerCommerceOrder prepared = null;
            PersonalTradeWindowQuote acceptedQuote = null;
            string fingerprint = null;
            PersonalTradeWindowPrepare prepare = (PersonalTradeWindowProposal proposal,
                out PersonalTradeWindowQuote quote, out string failure) =>
            {
                quote = null;
                failure = "The item loan changed.";
                if (!PersonalItemLoanWindowCurrent(proposal, person, epoch, sessionStore, binding, entity, player,
                    lending) || proposal.PlayerOfferGold != 0 || lending &&
                    !PersonalTradeWilling(sessionStore, actor, person)) return false;
                PersonalTradeLine line;
                if (!TrySinglePersonalItemLoanLine(proposal, requiredSide, out line))
                { failure = lending ? "Choose one of their items to borrow." : "Choose the exact borrowed item to return."; return false; }
                string originReceiptId = null;
                if (returning)
                {
                    PlayerItemLoanView open;
                    if (!sessionStore.TryFindOpenPlayerItemLoan(person, line.ItemUid, line.Quantity, out open))
                    { failure = "That is not an exact open loan from this person."; return false; }
                    originReceiptId = open.OriginReceiptId;
                }
                if (!TryBuildPersonalItemLoanOrder(sessionStore, binding, player, person, lending, line,
                    originReceiptId, out prepared)) return false;
                quote = new PersonalTradeWindowQuote {
                    NetGoldToNpc = 0, CanSubmit = true,
                    Status = lending ? "Borrow this exact item for no gold." : "Return this exact borrowed item for no gold."
                };
                acceptedQuote = quote;
                fingerprint = PersonalItemLoanFingerprint(proposal);
                return true;
            };
            PersonalTradeWindowSubmit submit = (PersonalTradeWindowProposal proposal,
                PersonalTradeWindowQuote quote, out string receiptId, out string failure) =>
            {
                receiptId = null;
                failure = "The item loan changed.";
                if (prepared == null || !ReferenceEquals(quote, acceptedQuote) || !quote.CanSubmit ||
                    fingerprint != PersonalItemLoanFingerprint(proposal) || proposal.PlayerOfferGold != 0 ||
                    !PersonalItemLoanWindowCurrent(proposal, person, epoch, sessionStore, binding, entity, player, lending) ||
                    lending && !PersonalTradeWilling(sessionStore, actor, person)) return false;
                PersonalTradeLine line;
                if (!TrySinglePersonalItemLoanLine(proposal, requiredSide, out line)) return false;
                string originReceiptId = null;
                if (returning)
                {
                    PlayerItemLoanView open;
                    if (!sessionStore.TryFindOpenPlayerItemLoan(person, line.ItemUid, line.Quantity, out open)) return false;
                    originReceiptId = open.OriginReceiptId;
                }
                PlayerCommerceReceipt receipt;
                bool committed = NativeNpcInventoryBridge.TryPersonalTrade(sessionStore, binding, entity, player,
                    prepared, ConversationActionTime(),
                    () => PersonalTradeActorAvailable(sessionStore, actor, person),
                    () => PersonalTradeActorAvailable(sessionStore, actor, person),
                    () => lending ? PersonalTradeActorAvailable(sessionStore, actor, person) &&
                        sessionStore.CanBeginPlayerItemLoan(person) && PersonalTradeWilling(sessionStore, actor, person) :
                        PersonalTradeActorAvailable(sessionStore, actor, person),
                    () => !saveFault && ReferenceEquals(store, sessionStore) && GameManager.Instance != null &&
                        ReferenceEquals(GameManager.Instance.PlayerEntity, player),
                    () => false, () => false, () => 0, PersonalTradeItemDisposable,
                    item => item != null && item.IsArtifact,
                    item => PersonalTradeItemSourceAllowed(sessionStore, lending ? person : null, item),
                    out receipt, out failure);
                if (!committed) return false;
                receiptId = receipt.TransactionId;
                prepared = null;
                return true;
            };
            string label = actor.Person == null || string.IsNullOrWhiteSpace(actor.Person.NameNPC) ? "NPC" : actor.Person.NameNPC;
            session = new PersonalTradeWindowSession(person, epoch, player.Items, binding.Items,
                () => player.GoldPieces, () => { var current = sessionStore.ReadNpcInventory(person); return current == null ? 0 : current.WalletGold; },
                prepare, submit,
                (side, item) => side == requiredSide && PersonalTradeItemDisposable(item) &&
                    PersonalTradeItemSourceAllowed(sessionStore, lending ? person : null, item),
                "Player", label, closed);
            reason = null;
            return true;
        }

        private static bool TrySinglePersonalItemLoanLine(PersonalTradeWindowProposal proposal,
            PersonalTradeSide requiredSide, out PersonalTradeLine selected)
        {
            selected = null;
            if (proposal == null || proposal.Lines == null || proposal.Lines.Count != 1) return false;
            var line = proposal.Lines[0];
            if (line == null || line.Side != requiredSide || line.ItemUid == 0 || line.Quantity < 1) return false;
            selected = line;
            return true;
        }

        private static string PersonalItemLoanFingerprint(PersonalTradeWindowProposal proposal)
        {
            if (proposal == null || proposal.Lines == null || proposal.Lines.Count != 1) return null;
            var line = proposal.Lines[0];
            return line == null ? null : proposal.NpcId + "|" + proposal.StoreEpoch + "|" + proposal.PlayerOfferGold + "|" +
                (int)line.Side + ":" + line.ItemUid + ":" + line.Quantity;
        }

        private static bool PersonalItemLoanWindowCurrent(PersonalTradeWindowProposal proposal, string person,
            long epoch, WorldStateStore sessionStore, NativeNpcInventoryBinding binding, DaggerfallEntity entity,
            PlayerEntity player, bool lending)
        {
            return proposal != null && proposal.NpcId == person && proposal.StoreEpoch == epoch && sessionStore != null &&
                binding != null && binding.PersonId == person && entity != null && player != null &&
                sessionStore.ReadNpcInventory(person) != null && sessionStore.ReadNpcInventory(person).Revision == epoch &&
                (!lending || sessionStore.CanBeginPlayerItemLoan(person));
        }

        private static bool TryBuildPersonalItemLoanOrder(WorldStateStore sessionStore,
            NativeNpcInventoryBinding binding, PlayerEntity player, string person, bool lending,
            PersonalTradeLine line, string originReceiptId, out PlayerCommerceOrder order)
        {
            order = null;
            if (sessionStore == null || binding == null || player == null || player.Items == null || line == null) return false;
            var saved = sessionStore.ReadNpcInventory(person);
            var source = lending ? binding.Items : player.Items;
            var item = source == null ? null : source.GetItem(line.ItemUid);
            if (saved == null || item == null || item.UID != line.ItemUid || item.stackCount < line.Quantity) return false;
            string inventory = "player-items:" + person;
            string wallet = "player-wallet:" + person;
            bool npcSource = lending;
            order = new PlayerCommerceOrder {
                TransactionId = "personal-loan:" + Guid.NewGuid().ToString("N"), NpcId = person,
                Kind = lending ? PlayerCommerceKind.Take : PlayerCommerceKind.Give,
                Venue = PlayerCommerceVenue.Personal, SourceOwnerId = npcSource ? person : "player",
                TargetOwnerId = npcSource ? "player" : person, ItemUid = line.ItemUid, Quantity = line.Quantity,
                QuotedTotalGold = 0, ServiceCode = null, LoanPurpose = lending ? LoanPurpose.Lend : LoanPurpose.Return,
                OriginReceiptId = originReceiptId,
                ExpectedSourceInventoryRevision = npcSource ? saved.Revision : 0,
                ExpectedTargetInventoryRevision = npcSource ? 0 : saved.Revision,
                ExpectedPayerWalletRevision = saved.Revision, ExpectedPayeeWalletRevision = 0,
                PlayerInventoryContainerId = inventory, PlayerWalletContainerId = wallet,
                ExpectedPlayerInventoryToken = PlayerCommerceContract.InventorySnapshotToken("player", inventory,
                    npcSource ? 0 : line.ItemUid, npcSource ? 0 : item.stackCount),
                ExpectedPlayerWalletToken = PlayerCommerceContract.WalletSnapshotToken("player", wallet, player.GoldPieces)
            };
            return PlayerCommerceContract.InventorySnapshotToken("player", inventory,
                npcSource ? 0 : line.ItemUid, npcSource ? 0 : item.stackCount) != null &&
                PlayerCommerceContract.CanBegin(order, new PlayerCommerceState {
                    SourceOwnerId = order.SourceOwnerId, TargetOwnerId = order.TargetOwnerId,
                    PayerOwnerId = person, PayeeOwnerId = "player", ItemUid = line.ItemUid,
                    SourceQuantity = item.stackCount, RecipientItemUid = 0, RecipientQuantity = 0,
                    ReceiverCapacity = item.stackCount, PayerGold = saved.WalletGold, PayeeGold = player.GoldPieces,
                    SourceInventoryRevision = order.ExpectedSourceInventoryRevision,
                    TargetInventoryRevision = order.ExpectedTargetInventoryRevision,
                    PayerWalletRevision = saved.Revision, PayeeWalletRevision = 0,
                    PlayerInventoryContainerId = inventory, PlayerWalletContainerId = wallet,
                    PlayerInventoryToken = order.ExpectedPlayerInventoryToken,
                    PlayerWalletToken = order.ExpectedPlayerWalletToken, SourceAccessible = true,
                    PlayerConsented = true, NpcConsented = true
                });
        }
    }
}
