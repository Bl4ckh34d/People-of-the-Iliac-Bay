using System;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.UserInterface;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        // All live authority is captured before Talk closes. Session callbacks
        // never consult conversationPersonId, memorySession, or conversationActor.
        private bool TryOpenPersonalTradeWindow(DaggerfallTalkWindow talk, out string reason)
        {
            PersonalTradeWindowSession session;
            if (!TryCreatePersonalTradeWindowSession(out session, out reason)) return false;
            CapturePersonalTradePortrait(talk, session);
            var actor = conversationActor;
            string person = conversationPersonId;
            bool pauseCurrentWork = PersonalTradeCurrentWorkCanPause(store, person);
            DaggerfallUI.Instance.PopToHUD();
            // The detached trade session already owns the live evidence. A
            // harmless Follow/Wait pauses physically without cancelling it.
            if (pauseCurrentWork && actor != null && actor.Resident != null && actor.Resident.Id == person)
                actor.SetConversationFacing(true);
            PersonalTradeWindow.Open(DaggerfallUI.UIManager, null, session, mod);
            return true;
        }

        private bool TryCreatePersonalTradeWindowSession(out PersonalTradeWindowSession session, out string reason)
        {
            session = null;
            reason = "This person cannot trade now.";
            var actor = conversationActor;
            string person = conversationPersonId;
            var game = GameManager.Instance;
            if (!PersonalTradeActorAvailable(actor, person) || !PersonalTradeWilling(actor, person) || game == null ||
                game.PlayerEntity == null || game.PlayerEntity.Items == null) return false;
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
            var checks = new PersonalBarterLiveChecks
            {
                NpcAdultNow = () => PersonalTradeActorAvailable(sessionStore, actor, person),
                NpcEligibleNow = () => PersonalTradeActorAvailable(sessionStore, actor, person),
                NpcWillingNow = () => PersonalTradeWilling(sessionStore, actor, person),
                PlayerConsentedNow = () => !saveFault && ReferenceEquals(store, sessionStore) && GameManager.Instance != null &&
                    ReferenceEquals(GameManager.Instance.PlayerEntity, player),
                NpcValuesItemNow = PersonalTradeItemDisposable,
                OccupationallyDisposableNow = PersonalTradeItemDisposable,
                ItemUniqueNow = item => item != null && item.IsArtifact,
                // Bridge separately binds direction to the exact source
                // collection. This common safety callback must accept either
                // legitimate NPC stock or the player's own non-stolen item.
                ItemSourceAllowedNow = item => PersonalTradeItemSourceAllowed(sessionStore, person, item) ||
                    PersonalTradeItemSourceAllowed(sessionStore, null, item)
            };
            PersonalBarterOrder prepared = null;
            NpcCommerceHaggleBasketBinding haggle = null;
            NpcCommerceHaggleBasketRound acceptedRound = null;
            PersonalTradeWindowQuote acceptedQuote = null;
            string fingerprint = null;
            int currentNetGoldToNpc = 0, nextProposalRound = 1;
            var submittedProposalEffects = new HashSet<string>();
            var committedReceiptEffects = new HashSet<string>();
            string sessionDate = memoryDate ?? CurrentConversationTimestamp();
            string sessionPlace = ConversationMemoryPlace();
            Action clearHaggle = () =>
            {
                prepared = null; haggle = null; acceptedRound = null; acceptedQuote = null;
                fingerprint = null; currentNetGoldToNpc = 0; nextProposalRound = 1;
            };
            Action<string, int, string, string, HashSet<string>> applyMoodOnce =
                (effectId, delta, playerText, npcText, effects) =>
            {
                if (delta == 0 || string.IsNullOrWhiteSpace(effectId) || effects == null || !effects.Add(effectId)) return;
                double now = ConversationActionTime();
                var mood = sessionStore.CaptureConversationMood(person, now);
                long ignored;
                sessionStore.TryCommitAcceptedActionExchange(person, mood.AcceptedTurnSequence, mood.Revision,
                    delta, now, sessionDate, sessionPlace, playerText, npcText, out ignored);
            };
            PersonalTradeWindowPrepare prepare = (PersonalTradeWindowProposal proposal, out PersonalTradeWindowQuote quote, out string failure) =>
            {
                quote = null; failure = "The personal trade changed.";
                if (!PersonalTradeProposalCurrent(proposal, person, epoch, sessionStore, binding, entity, player, checks)) return false;
                List<PersonalBarterLine> lines;
                if (!TryBuildPersonalTradeLines(proposal, out lines))
                { failure = "Choose at least one item to trade."; return false; }
                double now = ConversationActionTime();
                var relationship = sessionStore.ReadNpcRelationship(person, NpcDirectedRelationship.PlayerTargetId, now);
                int? trust = relationship != null && relationship.TrustKnown ? (int?)relationship.ReliabilityTrust : null;
                int mood = sessionStore.CaptureConversationMood(person, now).Value;
                string basket = PersonalTradeBasketFingerprint(proposal);
                if (haggle == null || fingerprint != basket)
                {
                    int baseNet;
                    if (!TryQuotePersonalTrade(actor, person, lines, out baseNet))
                    { failure = "The current basket cannot be priced."; return false; }
                    if (!NativeNpcInventoryBridge.TryPreparePersonalBarter(sessionStore, binding, entity, player,
                        "personal-barter:" + Guid.NewGuid().ToString("N"), lines, baseNet, checks, out prepared, out failure, quoteOnly:true))
                    { clearHaggle(); return false; }
                    haggle = NpcCommerceHaggleBasketBinding.Bind(prepared, NpcCommerceHagglingPolicy.MinimumBasketNetGold(baseNet, trust, mood));
                    currentNetGoldToNpc = baseNet;
                    nextProposalRound = 1;
                    fingerprint = basket;
                }
                else
                {
                    PersonalBarterOrder fresh;
                    if (!NativeNpcInventoryBridge.TryPreparePersonalBarter(sessionStore, binding, entity, player,
                        prepared.TransactionId, lines, haggle.InitialNetGoldToNpc, checks, out fresh, out failure, quoteOnly:true) ||
                        !haggle.Matches(fresh)) { clearHaggle(); return false; }
                    prepared = fresh;
                }
                int disposition;
                if (!sessionStore.TryGetActualConversationDisposition(person, out disposition))
                { clearHaggle(); failure = "Their current disposition is unavailable."; return false; }
                if (!proposal.HasPlayerOffer)
                {
                    // This is the NPC's fresh signed baseline, not an implicit
                    // zero-gold player bid. SEND can explicitly accept it.
                    acceptedRound = null;
                    quote = new PersonalTradeWindowQuote { NetGoldToNpc = currentNetGoldToNpc,
                        CanSubmit = false, Status = "" };
                    acceptedQuote = quote;
                    return true;
                }
                var round = haggle.CreateRound(currentNetGoldToNpc, proposal.PlayerOfferGold, nextProposalRound,
                    Math.Max(0, Math.Min(100, (int)player.Skills.GetLiveSkillValue(DFCareer.Skills.Mercantile))),
                    Math.Max(1, Math.Min(100, (int)player.Stats.LivePersonality)), disposition, trust, mood);
                var result = NpcCommerceHagglingPolicy.Decide(round);
                acceptedRound = result.Disposition == NpcCommerceHaggleDisposition.Accept ? round : null;
                quote = new PersonalTradeWindowQuote { NetGoldToNpc = result.PriceGold,
                    ExpectedNetGoldToNpc = currentNetGoldToNpc,
                    CanSubmit = acceptedRound != null,
                    Disposition = result.Disposition, ProposalMoodDelta = result.ProposalMoodDelta,
                    ProposalRound = round.Round, ProposalEffectId = NpcCommerceHagglingPolicy.ProposalEffectId(haggle.NegotiationId, round.Round),
                    Status = acceptedRound != null ? "The offer is accepted." : result.Disposition == NpcCommerceHaggleDisposition.Reject
                        ? "They refuse that offer." : "They suggest another amount." };
                acceptedQuote = quote;
                return true;
            };
            PersonalTradeWindowProposalSubmitted proposalSubmitted = (proposal, quote) =>
            {
                if (proposal == null || quote == null || quote.ProposalRound < 1 || haggle == null ||
                    fingerprint != PersonalTradeBasketFingerprint(proposal)) return;
                applyMoodOnce(quote.ProposalEffectId, quote.ProposalMoodDelta,
                    "Personal trade offer: " + PersonalTradeGoldText(proposal.PlayerOfferGold), quote.Status, submittedProposalEffects);
                if (quote.Disposition == NpcCommerceHaggleDisposition.CounterOffer)
                    currentNetGoldToNpc = quote.NetGoldToNpc;
                if (quote.ProposalRound >= nextProposalRound) nextProposalRound = quote.ProposalRound + 1;
            };
            PersonalTradeWindowSubmit submit = (PersonalTradeWindowProposal proposal, PersonalTradeWindowQuote quote, out string receiptId, out string failure) =>
            {
                receiptId = null; failure = "The personal trade changed.";
                if (prepared == null || haggle == null || acceptedRound == null || !ReferenceEquals(quote, acceptedQuote) ||
                    !quote.CanSubmit || fingerprint != PersonalTradeBasketFingerprint(proposal) ||
                    !PersonalTradeProposalCurrent(proposal, person, epoch, sessionStore, binding, entity, player, checks)) return false;
                PersonalBarterReceipt receipt;
                if (!NativeNpcInventoryBridge.TryPersonalBarter(sessionStore, binding, entity, player, prepared,
                    ConversationActionTime(), checks, haggle, () => PersonalTradeCurrentBaseNet(actor, person, prepared), acceptedRound,
                    out receipt, out failure)) return false;
                receiptId = PersonalTradeCommitBoundary.SourceBoundId("receipt", receipt.TransactionId);
                if (sessionStore.TryReservePersonalBarterMercantileTally(receipt.TransactionId))
                    player.TallySkill(DFCareer.Skills.Mercantile, 1);
                applyMoodOnce(NpcCommerceHagglingPolicy.TradeEffectId(receiptId),
                    NpcCommerceHagglingPolicy.CommittedTradeMoodDelta(haggle, receipt.NetGoldToNpc),
                    "Personal trade accepted.", "Trade completed.", committedReceiptEffects);
                clearHaggle();
                return true;
            };
            string label = actor.Person == null || string.IsNullOrWhiteSpace(actor.Person.NameNPC) ? "NPC" : actor.Person.NameNPC;
            session = new PersonalTradeWindowSession(person, epoch, player.Items, binding.Items,
                () => player.GoldPieces, () => { var current = sessionStore.ReadNpcInventory(person); return current == null ? 0 : current.WalletGold; },
                prepare, submit, (side, item) => PersonalTradeItemDisposable(item) &&
                    (side == PersonalTradeSide.Npc ? PersonalTradeItemSourceAllowed(sessionStore, person, item)
                        : PersonalTradeItemSourceAllowed(sessionStore, null, item)), "Player", label,
                (committed, closeReason) =>
                {
                    if (actor != null && actor.Resident != null && actor.Resident.Id == person)
                        actor.SetConversationFacing(false);
                }, proposalSubmitted);
            reason = null;
            return true;
        }

        private bool PersonalTradeActorAvailable(ResidentActor actor, string person) { return PersonalTradeActorAvailable(store, actor, person); }
        private bool PersonalTradeActorAvailable(WorldStateStore expected, ResidentActor actor, string person)
        {
            var game = GameManager.Instance;
            return !saveFault && ReferenceEquals(store, expected) && expected != null && options.Enabled && actor != null &&
                game != null && game.PlayerEntity != null && game.PlayerMotor != null && streets != null && streets.Ready &&
                actor.Resident != null && actor.Resident.Id == person && actor.Resident.IsAlive && !actor.Resident.IsQuestProtected &&
                actor.Resident.AgeGroup == ResidentAgeGroup.Adult && !actor.IsSleeping && actor.gameObject.activeInHierarchy &&
                !streets.IsAlarmed(person) && !PersonalTradeWorkIsBusy(expected, person) &&
                Vector3.Distance(actor.transform.position, game.PlayerMotor.transform.position) <= 20f;
        }

        private bool PersonalTradeWilling(ResidentActor actor, string person) { return PersonalTradeWilling(store, actor, person); }
        private bool PersonalTradeWilling(WorldStateStore expected, ResidentActor actor, string person)
        {
            if (!PersonalTradeActorAvailable(expected, actor, person)) return false;
            int disposition;
            if (!expected.TryGetActualConversationDisposition(person, out disposition)) return false;
            var relation = expected.ReadNpcRelationship(person, NpcDirectedRelationship.PlayerTargetId, ConversationActionTime());
            var personality = expected.ReadNpcPersonality(person);
            if (personality == null || relation != null && (relation.ProjectedInterpersonalFear >= 40 || relation.ProjectedResentment >= 50)) return false;
            var needs = expected.ReadNpcMotivationState(person);
            if (needs != null) foreach (var need in needs.Needs)
                if (need.KnowledgeState == NpcMotivationKnowledgeState.Known && need.Intensity.HasValue && need.Intensity.Value >= 70 &&
                    (need.Kind == NpcMotivationNeedKind.Rest || need.Kind == NpcMotivationNeedKind.Safety ||
                     need.Kind == NpcMotivationNeedKind.Health || need.Kind == NpcMotivationNeedKind.Equipment)) return false;
            int minimum = personality.Greed == "greedy" ? -10 : 0;
            if (personality.Generosity == "stingy") minimum += 20;
            if (personality.Generosity == "generous") minimum -= 10;
            return disposition >= minimum;
        }

        private static bool PersonalTradeItemDisposable(DaggerfallUnityItem item)
        { return item != null && item.stackCount > 0 && !item.IsQuestItem && !item.IsEquipped && !item.IsSummoned && !item.IsArtifact &&
            !item.RepairData.IsBeingRepaired() && !(item is HouseholdPropertyKeyItem) && item.ItemGroup != ItemGroups.Currency &&
            item.ItemGroup != ItemGroups.Transportation && item.ItemGroup != ItemGroups.Deeds; }

        private static bool PersonalTradeItemSourceAllowed(WorldStateStore store, string npc, DaggerfallUnityItem item)
        {
            if (store == null || item == null) return false;
            string owner = store.NpcInventoryItemOwner(item.UID);
            return npc == null ? owner == null && !store.IsKnownStolenPlayerItem(item.UID) : owner == npc;
        }

        private static bool PersonalTradeProposalCurrent(PersonalTradeWindowProposal proposal, string person, long epoch,
            WorldStateStore store, NativeNpcInventoryBinding binding, DaggerfallEntity entity, PlayerEntity player, PersonalBarterLiveChecks checks)
        { return proposal != null && proposal.NpcId == person && proposal.StoreEpoch == epoch && store != null && binding != null &&
            binding.PersonId == person && entity != null && player != null && checks != null && store.ReadNpcInventory(person) != null &&
            store.ReadNpcInventory(person).Revision == epoch; }

        private static bool TryBuildPersonalTradeLines(PersonalTradeWindowProposal proposal, out List<PersonalBarterLine> lines)
        {
            lines = new List<PersonalBarterLine>();
            if (proposal == null || proposal.Lines == null || proposal.Lines.Count > PersonalBarterContract.MaximumLines) return false;
            foreach (var line in proposal.Lines)
            {
                if (line == null || line.ItemUid == 0 || line.Quantity < 1) return false;
                lines.Add(new PersonalBarterLine { Direction = line.Side == PersonalTradeSide.Npc ? PersonalBarterDirection.NpcToPlayer : PersonalBarterDirection.PlayerToNpc,
                    ItemUid = line.ItemUid, Quantity = line.Quantity });
            }
            return lines.Count > 0;
        }
        private static bool PersonalTradeWorkIsBusy(WorldStateStore expected, string person)
        {
            if (expected == null || string.IsNullOrWhiteSpace(person)) return true;
            var task = expected.ActiveNpcTask(person);
            if (task != null) return !PersonalTradeCurrentWorkCanPause(expected, person);
            var action = expected.ActiveNpcAction(person);
            return action != null && !NpcActionExecutor.IsTerminal(action) && !PersonalTradePausableAction(action);
        }

        private static bool PersonalTradeCurrentWorkCanPause(WorldStateStore expected, string person)
        {
            if (expected == null || string.IsNullOrWhiteSpace(person)) return false;
            var task = expected.ActiveNpcTask(person);
            if (task != null)
            {
                if (task.Phase != NpcTaskPhase.Running || string.IsNullOrWhiteSpace(task.ActiveActionId)) return false;
                var action = expected.ReadNpcAction(task.ActiveActionId);
                return action != null && action.ResidentId == person && PersonalTradePausableAction(action);
            }
            return PersonalTradePausableAction(expected.ActiveNpcAction(person));
        }

        private static bool PersonalTradePausableAction(NpcActionJob action)
        { return action != null && !NpcActionExecutor.IsTerminal(action) &&
            (action.Kind == NpcActionKind.Follow || action.Kind == NpcActionKind.Wait); }

        private bool TryQuotePersonalTrade(ResidentActor actor, string person, IList<PersonalBarterLine> lines, out int net)
        {
            net = 0; var owner = actor == null ? null : actor.GetComponent<NativeNpcInventoryOwner>();
            var player = GameManager.Instance == null ? null : GameManager.Instance.PlayerEntity;
            if (owner == null || owner.Binding == null || player == null || lines == null) return false;
            long total = 0;
            foreach (var line in lines)
            {
                var source = line.Direction == PersonalBarterDirection.NpcToPlayer ? owner.Binding.Items : player.Items;
                var item = source.GetItem(line.ItemUid);
                int price = PersonalTradeItemQuote(actor, person, item, line.Quantity,
                    line.Direction == PersonalBarterDirection.NpcToPlayer ? PlayerCommerceKind.Buy : PlayerCommerceKind.Sell);
                if (price < 0) return false;
                total += line.Direction == PersonalBarterDirection.NpcToPlayer ? price : -price;
            }
            if (total < -NpcInventoryLimits.MaximumWalletGold || total > NpcInventoryLimits.MaximumWalletGold) return false;
            net = (int)total; return true;
        }
        private int PersonalTradeCurrentBaseNet(ResidentActor actor, string person, PersonalBarterOrder order)
        { int result; return order != null && TryQuotePersonalTrade(actor, person, order.Lines, out result) ? result : int.MaxValue; }
        private int PersonalTradeItemQuote(ResidentActor actor, string person, DaggerfallUnityItem item, int quantity, PlayerCommerceKind kind)
        {
            if (!PersonalTradeActorAvailable(actor, person) || item == null || !PersonalTradeItemDisposable(item) || quantity < 1 || quantity > item.stackCount) return -1;
            if (item.value == 0) return 0;
            var personality = store.ReadNpcPersonality(person); bool buying = kind == PlayerCommerceKind.Buy;
            int multiplier = buying ? personality != null && personality.Greed == "greedy" ? 3 : 2 : 1;
            long total = (long)item.value * quantity * multiplier;
            if (total < 1 || total > NpcInventoryLimits.MaximumWalletGold) return -1;
            int price = Math.Max(1, DaggerfallWorkshop.Game.Formulas.FormulaHelper.CalculateTradePrice((int)total, 10, !buying)); int disposition;
            return dispositionCommerceEnabled && conversationDispositionEnabled && store.TryGetActualConversationDisposition(person, out disposition)
                ? buying ? DispositionPricing.ApplyPurchaseDispositionPrice(price, disposition) : DispositionPricing.ApplySaleDispositionPrice(price, disposition) : price;
        }
        private static string PersonalTradeBasketFingerprint(PersonalTradeWindowProposal proposal)
        { if (proposal == null || proposal.Lines == null) return null; string result = proposal.NpcId + "|" + proposal.StoreEpoch; foreach (var line in proposal.Lines) result += "|" + (int)line.Side + ":" + line.ItemUid + ":" + line.Quantity; return result; }
        private static string PersonalTradeGoldText(int net) { return net >= 0 ? net + " gold to them" : -net + " gold to you"; }
    }
}
