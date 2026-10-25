using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private sealed class PersonalCommerceCatalog
        {
            public string PersonId;
            public int Session;
            public DaggerfallTalkWindow Window;
            public List<PlayerCommerceCandidate> Candidates;
        }

        private PersonalCommerceCatalog personalCommerceCatalog;
        private Action personalCommerceBeforeClose;

        private void RememberPersonalCommerceHandoff(DaggerfallTalkWindow window,string question,string answer)
        {
            // Immediate publication is required: PopToHUD cancels deferred audio
            // and clears the speaker/session. Opening a window is not a completed sale.
            AppendUnrecordedDialoguePair(window,question,answer,false);
            if(memoryWindow==window)RememberDialogueExchange(question,answer,"action");
        }

        private void InvalidatePersonalCommerceCatalog()
        { personalCommerceCatalog = null; }

        private bool CurrentPersonalCommerceCatalog(DaggerfallTalkWindow window)
        {
            return personalCommerceCatalog != null && personalCommerceCatalog.Window == window &&
                personalCommerceCatalog.Session == memorySession &&
                personalCommerceCatalog.PersonId == conversationPersonId &&
                CurrentMemorySession(window, memorySession);
        }

        // Called after the existing top-level semantic route. The model sees
        // only the already displayed, bounded candidate IDs and labels.
        private bool TryPersonalCommerceConversation(DaggerfallTalkWindow window, string text,
            string history, ConversationContextSnapshot snapshot, string modelIntent)
        {
            if (snapshot == null || !IsCurrentConversationContext(snapshot, window)) return false;
            if (!CurrentPersonalCommerceCatalog(window)) InvalidatePersonalCommerceCatalog();
            // The language model already resolved the speech act. Native
            // willingness and inventory checks still decide whether UI opens.
            var direction = modelIntent == "trade" ? PersonalCommerceCatalogDirection.Ambiguous :
                modelIntent == "borrow" ? PersonalCommerceCatalogDirection.BorrowFromNpc :
                modelIntent == "return_loan" ? PersonalCommerceCatalogDirection.ReturnLoan :
                PersonalCommerceCatalogDirection.None;
            bool actionRoute = direction != PersonalCommerceCatalogDirection.None;
            // The owned two-inventory window replaces the former one-item
            // catalogue/confirmation path. A stale list cannot survive into a
            // later chat turn and therefore cannot commit after Talk closes.
            if (personalCommerceCatalog != null)
            {
                InvalidatePersonalCommerceCatalog();
                AppendDialoguePair(window, text, "That old item list is closed. Ask to trade again to open both inventories.");
                return true;
            }
            if (!actionRoute || direction == PersonalCommerceCatalogDirection.None) return false;
            if (direction == PersonalCommerceCatalogDirection.BorrowFromNpc || direction == PersonalCommerceCatalogDirection.ReturnLoan)
            {
                string loanReason;
                var purpose = direction == PersonalCommerceCatalogDirection.BorrowFromNpc ? LoanPurpose.Lend : LoanPurpose.Return;
                personalCommerceBeforeClose=()=>RememberPersonalCommerceHandoff(window,text,
                    purpose==LoanPurpose.Lend?"Let us look at what I can lend you.":"Let us look at what you are returning.");
                try
                {
                    if (!TryOpenPersonalItemLoanWindow(window, purpose, out loanReason))
                        AppendDialoguePair(window, text, string.IsNullOrWhiteSpace(loanReason) ? "I cannot lend an item now." : loanReason);
                }
                finally { personalCommerceBeforeClose=null; }
                return true;
            }
            // A request to trade or barter needs no direction clarification:
            // this window already supports both inventories in one exchange.
            string tradeReason;
            personalCommerceBeforeClose=()=>RememberPersonalCommerceHandoff(window,text,"Let us see what we can trade.");
            try
            {
                if (!TryOpenConversationPersonalTradeWindow(window, out tradeReason))
                    AppendDialoguePair(window, text, string.IsNullOrWhiteSpace(tradeReason)
                        ? "I cannot trade right now." : tradeReason);
            }
            finally { personalCommerceBeforeClose=null; }
            // Kept only as a source-compatible private fallback while old saves
            // may still hold a transient catalogue; it is never admitted.
            if (!LegacyPersonalCommerceSingleItemFlowEnabled()) return true;
            if (personalCommerceCatalog != null && personalCommerceCatalog.Candidates.Count > 0 &&
                (direction == PersonalCommerceCatalogDirection.BuyFromNpc || direction == PersonalCommerceCatalogDirection.SellToNpc) &&
                personalCommerceCatalog.Candidates[0].Order.Kind !=
                    (direction == PersonalCommerceCatalogDirection.BuyFromNpc ? PlayerCommerceKind.Buy : PlayerCommerceKind.Sell))
            {
                InvalidatePersonalCommerceCatalog();
                actionRoute = true;
            }
            if (personalCommerceCatalog == null)
            {
                if (!actionRoute) return false;
                if (direction == PersonalCommerceCatalogDirection.None) return false;
                conversationBusy = false;
                if (direction == PersonalCommerceCatalogDirection.Ambiguous)
                {
                    AppendDialoguePair(window, text, "Do you want to buy something from me, or sell something to me?");
                    return true;
                }
                bool buying = direction == PersonalCommerceCatalogDirection.BuyFromNpc;
                var rows = BuildPersonalCommerceCatalog(buying ? PlayerCommerceKind.Buy : PlayerCommerceKind.Sell);
                if (rows.Count == 0)
                {
                    AppendDialoguePair(window, text,
                        buying ? "I cannot offer a personal item trade now. Ordinary shop sales remain with the shopkeeper."
                            : "I cannot make a personal purchase now.");
                    return true;
                }
                personalCommerceCatalog = new PersonalCommerceCatalog { PersonId = conversationPersonId,
                    Session = memorySession, Window = window, Candidates = rows };
                var listed = new StringBuilder(buying
                    ? "These are the personal items I can offer. Name one listed number if you want a firm offer:\n"
                    : "These are the items I could use. Name one listed number if you want my offer:\n");
                foreach (var row in rows)
                    listed.Append(row.Id.ToString(CultureInfo.InvariantCulture)).Append(": ")
                        .Append(row.ItemLabel).Append(" x")
                        .Append(row.Order.Quantity.ToString(CultureInfo.InvariantCulture))
                        .Append(" - ").Append(row.Order.QuotedTotalGold.ToString(CultureInfo.InvariantCulture))
                        .Append(" gold\n");
                AppendDialoguePair(window, text, listed.ToString().TrimEnd());
                return true;
            }

            var catalog = personalCommerceCatalog;
            int session = memorySession;
            string person = conversationPersonId;
            long stage = BeginConversationContextStage(snapshot, window);
            if (stage <= 0)
            { HandleRejectedConversationStage(snapshot, window, stage, text, "No trade was accepted."); return true; }
            string prompt = PlayerCommerceSelection.Prompt(text, catalog.Candidates);
            string grammar = PlayerCommerceSelection.Grammar(catalog.Candidates);
            if (prompt == null || grammar == null)
            {
                if (!TryConsumeConversationContextStage(snapshot, window, stage))
                { HandleRejectedConversationStage(snapshot, window, stage, text, "No trade was accepted."); return true; }
                conversationBusy = false;
                InvalidatePersonalCommerceCatalog();
                AppendDialoguePair(window, text, "That list is no longer available. Please ask again.");
                return true;
            }
            if (!localDialogue.Classify(NewDialogueConnection(), prompt, history, output =>
            {
                if (!CurrentMemorySession(window, session) || conversationPersonId != person ||
                    !ReferenceEquals(personalCommerceCatalog, catalog)) return;
                if (!TryConsumeConversationContextStage(snapshot, window, stage))
                { HandleRejectedConversationStage(snapshot, window, stage, text, "No trade was accepted."); return; }
                conversationBusy = false;
                PlayerCommerceSelectionResult choice;
                if (output == LocalDialogueClient.Unavailable || output == LocalDialogueClient.ContextLimit ||
                    !PlayerCommerceSelection.TryBind(output, catalog.Candidates, out choice))
                {
                    InvalidatePersonalCommerceCatalog();
                    AppendDialoguePair(window, text, "No exact listed personal item was selected. No trade occurred.");
                    return;
                }
                var selected = choice.SelectedOrder;
                PlayerCommerceState fresh;
                DaggerfallUnityItem item;
                int quote;
                PlayerCommerceOrder offer;
                if (selected == null || !TryReadPersonalCommerceState(selected, out fresh, out item, out quote) ||
                    !PlayerCommerceSelection.TryPrepareOffer(choice, fresh, fresh.NpcWilling,
                        IsCatalogItemStillVisible(catalog, selected), quote, out offer))
                {
                    InvalidatePersonalCommerceCatalog();
                    AppendDialoguePair(window, text, "That item or price has changed. No trade occurred.");
                    return;
                }
                InvalidatePersonalCommerceCatalog();
                string label = item.LongName;
                string exactOffer = (offer.Kind == PlayerCommerceKind.Buy ? "I can sell you " : "I can buy ") +
                    offer.Quantity.ToString(CultureInfo.InvariantCulture) + " x " + label +
                    (offer.Kind == PlayerCommerceKind.Sell ? " from you" : "") + " for " + offer.QuotedTotalGold.ToString(CultureInfo.InvariantCulture) +
                    " gold. Do you accept?";
                AppendDialoguePair(window, text, exactOffer);
                var confirm = new PersonalCommerceConfirmBox(exactOffer, accepted =>
                {
                    if (!CurrentMemorySession(window, session) || conversationPersonId != person) return;
                    if (!accepted)
                    { AppendDialoguePair(window, "No", "Very well. No trade occurred."); return; }
                    PlayerCommerceState current;
                    DaggerfallUnityItem currentItem;
                    int currentQuote;
                    PlayerCommerceOrder prepared;
                    if (!TryReadPersonalCommerceState(offer, out current, out currentItem, out currentQuote) ||
                        !PlayerCommerceSelection.TryPrepareOffer(choice, current, current.NpcWilling,
                            IsCatalogItemStillVisible(catalog, offer), currentQuote, out prepared))
                    {
                        AppendDialoguePair(window, "Yes", "The item or price changed. No trade occurred.");
                        return;
                    }
                    var actor = conversationActor;
                    var owner = actor ? actor.GetComponent<NativeNpcInventoryOwner>() : null;
                    var behaviour = actor ? actor.GetComponent<DaggerfallEntityBehaviour>() : null;
                    var game = GameManager.Instance;
                    PlayerCommerceReceipt receipt = null;
                    string reason = null;
                    bool committed = false;
                    try
                    {
                        committed = owner != null && behaviour != null && game != null &&
                            NativeNpcInventoryBridge.TryPersonalTrade(store, owner.Binding, behaviour.Entity,
                                game.PlayerEntity, prepared, ConversationActionTime(),
                                () => CurrentPersonalCommerceActor(person),
                                () => CurrentPersonalCommerceActor(person),
                                () => PersonalCommerceWilling(person),
                                () => CurrentMemorySession(window, session) && conversationPersonId == person,
                                () => PersonalCommerceInterestedIn(person, currentItem),
                                () => PersonalCommerceDisposable(currentItem) && currentItem.value > 0,
                                () => PersonalCommerceItemQuote(person, currentItem, prepared.Quantity, prepared.Kind),
                                PersonalCommerceDisposable, itemNow => itemNow.IsArtifact ||
                                    !string.IsNullOrEmpty(itemNow.GetSaveData().className),
                                itemNow => PersonalCommerceItemAllowed(person, itemNow, prepared.Kind),
                                out receipt, out reason);
                    }
                    catch (PersonalTradeCommitOutcomeUnknownException error)
                    {
                        Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_PERSONAL_COMMERCE_UNKNOWN_OUTCOME: " + error);
                        try { AppendDialoguePair(window, "Yes", "The trade outcome is uncertain. Check your belongings and gold before asking again."); }
                        catch (Exception replyError)
                        { Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_PERSONAL_COMMERCE_UNKNOWN_REPLY: " + replyError); }
                        return;
                    }
                    if (!committed)
                    {
                        if (!string.IsNullOrWhiteSpace(reason))
                            Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_PERSONAL_COMMERCE_REJECTED: " + reason);
                        AppendDialoguePair(window, "Yes", "The item or price changed. No trade occurred.");
                        return;
                    }
                    try
                    {
                        PlayerCommerceMercantileTallyReservation reservation;
                        if (store.TryReservePlayerCommerceMercantileTally(receipt.TransactionId, out reservation))
                            game.PlayerEntity.TallySkill(DFCareer.Skills.Mercantile, 1);
                    }
                    catch (Exception error)
                    { Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_PERSONAL_COMMERCE_SKILL_TALLY: " + error); }
                    try
                    {
                        AppendDialoguePair(window, "Yes", "Agreed. The item and " +
                            receipt.PaidGold.ToString(CultureInfo.InvariantCulture) + " gold changed hands.");
                    }
                    catch (Exception error)
                    { Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_PERSONAL_COMMERCE_REPLY: " + error); }
                });
                DaggerfallUI.UIManager.PushWindow(confirm);
            }, grammar, classificationTrace: SemanticActionModelCaptureProbe.ForStage("personal-commerce-selection")))
            {
                if (!TryConsumeConversationContextStage(snapshot, window, stage))
                { HandleRejectedConversationStage(snapshot, window, stage, text, "No trade was accepted."); return true; }
                InvalidatePersonalCommerceCatalog();
                ReportActionDialogueUnavailable(window, text, "No personal trade was accepted.");
            }
            return true;
        }

        private static bool LegacyPersonalCommerceSingleItemFlowEnabled()
        { return false; }

        private sealed class PersonalCommerceConfirmBox : DaggerfallMessageBox
        {
            private Action<bool> completed;
            private bool accepted;
            public PersonalCommerceConfirmBox(string offer, Action<bool> completion) : base(DaggerfallUI.UIManager)
            {
                completed = completion;
                SetText(new[] { offer });
                AddButton(MessageBoxButtons.Yes);
                AddButton(MessageBoxButtons.No, true);
                OnButtonClick += (sender, button) => { accepted = button == MessageBoxButtons.Yes; CloseWindow(); };
            }
            public override void OnPop()
            {
                base.OnPop();
                var callback = completed; completed = null;
                if (callback != null) callback(accepted);
            }
        }

        private bool CurrentPersonalCommerceActor(string person)
        {
            var actor = conversationActor;
            var game = GameManager.Instance;
            if (saveFault || !options.Enabled || store == null || actor == null || game == null ||
                game.PlayerEntity == null || game.PlayerMotor == null || streets == null || !streets.Ready ||
                actor.Resident == null || actor.Resident.Id != person || !actor.Resident.IsAlive ||
                actor.Resident.IsQuestProtected || actor.Resident.AgeGroup != ResidentAgeGroup.Adult ||
                actor.IsSleeping || !actor.gameObject.activeInHierarchy || streets.IsAlarmed(person) ||
                Vector3.Distance(actor.transform.position, game.PlayerMotor.transform.position) > 20f)
                return false;
            var progress = store.ReadNpcProgress(person);
            return progress == null || progress.Archetype != NpcArchetype.Merchant &&
                progress.Archetype != NpcArchetype.Innkeeper;
        }

        private bool PersonalCommerceWilling(string person)
        {
            if (!CurrentPersonalCommerceActor(person)) return false;
            int disposition;
            if (!store.TryGetActualConversationDisposition(person, out disposition)) return false;
            var relation = store.ReadNpcRelationship(person, NpcDirectedRelationship.PlayerTargetId,
                ConversationActionTime());
            var personality = store.ReadNpcPersonality(person);
            if (personality == null || relation != null &&
                (relation.ProjectedInterpersonalFear >= 40 || relation.ProjectedResentment >= 50 ||
                 relation.TrustKnown && relation.ReliabilityTrust < 25)) return false;
            // Current urgent personal needs outrank a discretionary exchange.
            // A money need can motivate a sale; it does not force consent.
            var needs = store.ReadNpcMotivationState(person);
            if (needs != null)
                foreach (var need in needs.Needs)
                    if (need.KnowledgeState == NpcMotivationKnowledgeState.Known &&
                        need.Intensity.HasValue && need.Intensity.Value >= 70 &&
                        (need.Kind == NpcMotivationNeedKind.Rest ||
                         need.Kind == NpcMotivationNeedKind.Safety ||
                         need.Kind == NpcMotivationNeedKind.Health ||
                         need.Kind == NpcMotivationNeedKind.Equipment)) return false;
            int minimum = string.Equals(personality.Greed, "greedy", StringComparison.Ordinal) ? -10 : 0;
            if (string.Equals(personality.Generosity, "stingy", StringComparison.Ordinal)) minimum += 20;
            if (string.Equals(personality.Generosity, "generous", StringComparison.Ordinal)) minimum -= 10;
            return disposition >= minimum;
        }

        private static bool PersonalCommerceDisposable(DaggerfallUnityItem item)
        {
            return item != null && !item.IsQuestItem && !item.IsEquipped && !item.IsSummoned &&
                !item.IsArtifact && !(item is HouseholdPropertyKeyItem) &&
                (item.ItemGroup == ItemGroups.UselessItems1 || item.ItemGroup == ItemGroups.UselessItems2) &&
                string.IsNullOrEmpty(item.GetSaveData().className);
        }

        private int PersonalCommerceQuote(string person, ulong uid, int quantity)
        {
            var actor = conversationActor;
            var owner = actor ? actor.GetComponent<NativeNpcInventoryOwner>() : null;
            var item = owner == null || owner.Binding == null ? null : owner.Binding.Items.GetItem(uid);
            return PersonalCommerceItemQuote(person, item, quantity);
        }

        // The bridge checks source ownership separately. Repricing the same item
        // must also work after its observed native move, before ledger commit.
        private int PersonalCommerceItemQuote(string person, DaggerfallUnityItem item, int quantity,
            PlayerCommerceKind kind = PlayerCommerceKind.Buy)
        {
            if (!CurrentPersonalCommerceActor(person) || item == null || !PersonalCommerceDisposable(item) ||
                quantity < 1 || quantity > item.stackCount) return 0;
            var personality = store.ReadNpcPersonality(person);
            bool buying = kind == PlayerCommerceKind.Buy;
            int multiplier = buying ? (personality != null && personality.Greed == "greedy" ? 3 : 2) : 1;
            long total = (long)Math.Max(1, item.value) * quantity * multiplier;
            if (total < 1 || total > NpcInventoryLimits.MaximumWalletGold) return 0;
            // Personal sellers have no shop quality; use DFU's neutral counterparty.
            int price = Math.Max(1, FormulaHelper.CalculateTradePrice((int)total, 10, !buying));
            int disposition;
            return dispositionCommerceEnabled && conversationDispositionEnabled &&
                store.TryGetActualConversationDisposition(person, out disposition)
                ? (buying ? DispositionPricing.ApplyPurchaseDispositionPrice(price, disposition)
                    : DispositionPricing.ApplySaleDispositionPrice(price, disposition)) : price;
        }

        private List<PlayerCommerceCandidate> BuildPersonalCommerceCandidates()
        { return BuildPersonalCommerceCatalog(PlayerCommerceKind.Buy); }

        private bool PersonalCommerceItemAllowed(string person, DaggerfallUnityItem item, PlayerCommerceKind kind)
        {
            if (item == null) return false;
            string owner = store.NpcInventoryItemOwner(item.UID);
            return kind == PlayerCommerceKind.Buy ? owner == person
                : owner == null && !store.IsKnownStolenPlayerItem(item.UID);
        }

        private bool PersonalCommerceInterestedIn(string person, DaggerfallUnityItem item)
        {
            if (!CurrentPersonalCommerceActor(person) || !PersonalCommerceDisposable(item) || item.value <= 0) return false;
            var owner = conversationActor.GetComponent<NativeNpcInventoryOwner>();
            if (!owner || owner.Binding == null) return false;
            for (int i = 0; i < owner.Binding.Items.Count; i++)
            {
                var owned = owner.Binding.Items.GetItem(i);
                if (owned.ItemGroup == item.ItemGroup && owned.TemplateIndex == item.TemplateIndex)
                    return false;
            }
            return true;
        }

        private List<PlayerCommerceCandidate> BuildPersonalCommerceCatalog(PlayerCommerceKind kind)
        {
            var result = new List<PlayerCommerceCandidate>();
            string person = conversationPersonId;
            if (!CurrentPersonalCommerceActor(person) || !PersonalCommerceWilling(person)) return result;
            var actor = conversationActor;
            var owner = actor.GetComponent<NativeNpcInventoryOwner>();
            var behaviour = actor.GetComponent<DaggerfallEntityBehaviour>();
            var game = GameManager.Instance;
            if (owner == null || owner.Binding == null || behaviour == null || behaviour.Entity == null ||
                game.PlayerEntity.Items == null || !ReferenceEquals(behaviour.Entity.Items, owner.Binding.Items)) return result;
            var saved = store.ReadNpcInventory(person);
            if (!NpcInventoryReadAuthority.Matches(saved, person, owner.Binding.Revision,
                NativeNpcInventoryBridge.CaptureItems(owner.Binding.Items))) return result;
            bool buying = kind == PlayerCommerceKind.Buy;
            var source = buying ? owner.Binding.Items : game.PlayerEntity.Items;
            for (int i = 0; i < source.Count && result.Count < PlayerCommerceSelection.MaximumCandidates; i++)
            {
                var item = source.GetItem(i);
                if (!PersonalCommerceDisposable(item) || item.stackCount < 1 ||
                    item.stackCount > 1 && !item.IsAStack() ||
                    string.IsNullOrWhiteSpace(item.LongName) || item.LongName.Length > 120 ||
                    item.LongName.IndexOfAny(new[] { '\n', '\r', '|' }) >= 0 ||
                    !PersonalCommerceItemAllowed(person, item, kind) || !buying && !PersonalCommerceInterestedIn(person, item)) continue;
                int quote = PersonalCommerceItemQuote(person, item, 1, kind);
                if (quote < 1 || (buying ? game.PlayerEntity.GoldPieces : saved.WalletGold) < quote) continue;
                var order = new PlayerCommerceOrder { TransactionId = "personal-commerce:" + Guid.NewGuid().ToString("N"),
                    NpcId = person, SourceOwnerId = buying ? person : "player", TargetOwnerId = buying ? "player" : person,
                    Kind = kind, Venue = PlayerCommerceVenue.Personal,
                    ItemUid = item.UID, Quantity = 1, QuotedTotalGold = quote,
                    ExpectedSourceInventoryRevision = buying ? saved.Revision : 0,
                    ExpectedTargetInventoryRevision = buying ? 0 : saved.Revision, ExpectedPayerWalletRevision = buying ? 0 : saved.Revision,
                    ExpectedPayeeWalletRevision = buying ? saved.Revision : 0,
                    PlayerInventoryContainerId = "player-items:" + store.NpcFrameworkScopeId,
                    PlayerWalletContainerId = "player-wallet:" + store.NpcFrameworkScopeId,
                    ExpectedPlayerInventoryToken = PlayerCommerceContract.InventorySnapshotToken("player",
                        "player-items:" + store.NpcFrameworkScopeId, buying ? 0 : item.UID, buying ? 0 : item.stackCount),
                    ExpectedPlayerWalletToken = PlayerCommerceContract.WalletSnapshotToken("player",
                        "player-wallet:" + store.NpcFrameworkScopeId, game.PlayerEntity.GoldPieces) };
                PlayerCommerceState state;
                DaggerfallUnityItem current;
                int liveQuote;
                if (!TryReadPersonalCommerceState(order, out state, out current, out liveQuote) ||
                    !PlayerCommerceContract.CanBegin(order, state)) continue;
                result.Add(new PlayerCommerceCandidate { Id = result.Count + 1,
                    ItemLabel = item.LongName, VisibleToPlayer = true, Order = order, State = state });
            }
            return result;
        }

        private bool TryReadPersonalCommerceState(PlayerCommerceOrder order, out PlayerCommerceState state,
            out DaggerfallUnityItem item, out int quote)
        {
            state = null; item = null; quote = 0;
            if (order == null || order.Kind != PlayerCommerceKind.Buy && order.Kind != PlayerCommerceKind.Sell || order.Venue != PlayerCommerceVenue.Personal ||
                !CurrentPersonalCommerceActor(order.NpcId) || !PersonalCommerceWilling(order.NpcId)) return false;
            var owner = conversationActor.GetComponent<NativeNpcInventoryOwner>();
            var behaviour = conversationActor.GetComponent<DaggerfallEntityBehaviour>();
            var player = GameManager.Instance.PlayerEntity;
            if (owner == null || owner.Binding == null || behaviour == null || behaviour.Entity == null ||
                player == null || player.Items == null || owner.Binding.PersonId != order.NpcId ||
                !ReferenceEquals(behaviour.Entity.Items, owner.Binding.Items)) return false;
            var saved = store.ReadNpcInventory(order.NpcId);
            if (!NpcInventoryReadAuthority.Matches(saved, order.NpcId, owner.Binding.Revision,
                NativeNpcInventoryBridge.CaptureItems(owner.Binding.Items))) return false;
            bool buying = order.Kind == PlayerCommerceKind.Buy;
            item = (buying ? owner.Binding.Items : player.Items).GetItem(order.ItemUid);
            if (item == null || !PersonalCommerceDisposable(item) || item.stackCount < order.Quantity ||
                (buying ? player.Items : owner.Binding.Items).GetItem(order.ItemUid) != null ||
                !PersonalCommerceItemAllowed(order.NpcId, item, order.Kind)) return false;
            quote = PersonalCommerceItemQuote(order.NpcId, item, order.Quantity, order.Kind);
            float unit = item.EffectiveUnitWeightInKg();
            float available = buying ? player.MaxEncumbrance - player.CarriedWeight
                : behaviour.Entity.MaxEncumbrance - owner.Binding.Items.GetWeight();
            if (quote < 1 || unit < 0 || available < 0) return false;
            int capacity = unit == 0 ? 1000000 : Math.Max(0,
                (int)Math.Min(1000000d, Math.Floor(available / unit)));
            state = new PlayerCommerceState { SourceOwnerId = buying ? order.NpcId : "player", TargetOwnerId = buying ? "player" : order.NpcId,
                PayerOwnerId = buying ? "player" : order.NpcId, PayeeOwnerId = buying ? order.NpcId : "player", ItemUid = order.ItemUid,
                SourceQuantity = item.stackCount, RecipientItemUid = 0, RecipientQuantity = 0,
                ReceiverCapacity = capacity, PayerGold = buying ? player.GoldPieces : saved.WalletGold, PayeeGold = buying ? saved.WalletGold : player.GoldPieces,
                SourceInventoryRevision = buying ? saved.Revision : 0, TargetInventoryRevision = buying ? 0 : saved.Revision,
                PayerWalletRevision = buying ? 0 : saved.Revision, PayeeWalletRevision = buying ? saved.Revision : 0,
                PlayerInventoryContainerId = order.PlayerInventoryContainerId,
                PlayerWalletContainerId = order.PlayerWalletContainerId,
                PlayerInventoryToken = PlayerCommerceContract.InventorySnapshotToken("player",
                    order.PlayerInventoryContainerId, buying ? 0 : item.UID, buying ? 0 : item.stackCount),
                PlayerWalletToken = PlayerCommerceContract.WalletSnapshotToken("player",
                    order.PlayerWalletContainerId, player.GoldPieces),
                SourceAccessible = true, PlayerConsented = true, NpcConsented = true,
                NpcAdult = true, NpcEligible = true, NpcWilling = PersonalCommerceWilling(order.NpcId),
                NpcNeedsItem = !buying && PersonalCommerceInterestedIn(order.NpcId, item), NpcValuesItem = item.value > 0,
                PlayerMercantile = player.Skills.GetLiveSkillValue(DFCareer.Skills.Mercantile),
                ItemEquipped = item.IsEquipped, ItemOccupationallyNecessary = !PersonalCommerceDisposable(item),
                ItemUnique = item.IsArtifact, ItemKnownStolen = false,
                ItemQuestProtected = item.IsQuestItem || item.IsSummoned };
            return quote == order.QuotedTotalGold && PlayerCommerceContract.CanBegin(order, state);
        }

        private static bool IsCatalogItemStillVisible(PersonalCommerceCatalog catalog, PlayerCommerceOrder order)
        {
            if (catalog == null || order == null) return false;
            foreach (var row in catalog.Candidates)
                if (row.Order.TransactionId == order.TransactionId && row.Order.ItemUid == order.ItemUid &&
                    row.Order.Quantity == order.Quantity && row.Order.QuotedTotalGold == order.QuotedTotalGold)
                    return true;
            return false;
        }
    }
}
