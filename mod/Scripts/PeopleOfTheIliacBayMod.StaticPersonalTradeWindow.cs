using System;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Exact static speaker and inventory binding are captured before Talk
    // closes. The detached window owns no ResidentActor or body.
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private bool TryOpenConversationPersonalTradeWindow(DaggerfallTalkWindow talk, out string reason)
        {
            bool vendor;
            bool opened=TryOpenConversationNativeVendorTrade(talk,out vendor,out reason);
            if(vendor)return opened;
            var manager = GameManager.Instance == null ? null : GameManager.Instance.TalkManager;
            return manager != null && manager.CurrentNPCType == TalkManager.NPCType.Static
                ? TryOpenStaticPersonalTradeWindow(talk, out reason)
                : TryOpenPersonalTradeWindow(talk, out reason);
        }

        private bool TryOpenStaticPersonalTradeWindow(DaggerfallTalkWindow talk, out string reason)
        {
            PersonalTradeWindowSession session;
            if (!TryCreateStaticPersonalTradeWindowSession(talk, out session, out reason)) return false;
            CapturePersonalTradePortrait(talk, session);
            personalCommerceBeforeClose?.Invoke();
            DaggerfallUI.Instance.PopToHUD();
            PersonalTradeWindow.Open(DaggerfallUI.UIManager, null, session, mod);
            return true;
        }

        private bool TryResolveStaticPersonalTradeParty(DaggerfallTalkWindow talk, out StaticNPC npc,
            out NativePerson person, out NpcArchetype role)
        {
            npc = null;
            person = null;
            role = NpcArchetype.Citizen;
            var game = GameManager.Instance;
            var manager = game == null ? null : game.TalkManager;
            if (saveFault || store == null || !options.Enabled || talk == null || !conversationSessionOpen ||
                !ReferenceEquals(talk, memoryWindow) || DaggerfallUI.UIManager.TopWindow != talk ||
                manager == null || manager.CurrentNPCType != TalkManager.NPCType.Static || !manager.StaticNPC) return false;
            npc = manager.StaticNPC;
            string id = StaticConversationPersonId(npc);
            if (!string.Equals(id, conversationPersonId, StringComparison.Ordinal)) return false;
            int exteriorTown,exteriorHash;
            int settlement=NativeExteriorNpcIdentity.TryRead(npc,out exteriorTown,out exteriorHash)?exteriorTown:npc.Data.mapID;
            person = store.NativePersonInSettlement(settlement, id);
            return NativeStaticNpcInventoryOwner.Matches(npc, person) && StaticPersonalTradeRole(npc, out role) &&
                StaticPersonalTradeAvailable(store, npc, person, role, null, game.PlayerEntity);
        }

        private static bool StaticPersonalTradeRole(StaticNPC npc, out NpcArchetype role)
        {
            role = NpcArchetype.Citizen;
            var game = GameManager.Instance;
            int exteriorTown,exteriorHash;
            if(NativeExteriorNpcIdentity.TryRead(npc,out exteriorTown,out exteriorHash))
            {
                role=NativeExteriorNpcIdentity.IsMerchant(npc)?NpcArchetype.Merchant:NpcArchetype.Citizen;
                return true;
            }
            var directory = game == null ? null : game.StreamingWorld.GetCurrentBuildingDirectory();
            BuildingSummary building;
            if (!npc || game==null || !game.PlayerEnterExit.IsPlayerInsideBuilding || !game.PlayerEnterExit.Interior ||
                game.PlayerEnterExit.Interior.EntryDoor.buildingKey != npc.Data.buildingKey || !directory ||
                !directory.GetBuildingSummary(npc.Data.buildingKey, out building)) return false;
            if (building.BuildingType == DFLocation.BuildingTypes.Tavern && NativeBusinessServices.IsKeeper(npc, building.BuildingType))
            { role = NpcArchetype.Innkeeper; return true; }
            if (RMBLayout.IsShop(building.BuildingType) && NativeBusinessServices.IsKeeper(npc, building.BuildingType))
            { role = NpcArchetype.Merchant; return true; }
            if (building.BuildingType == DFLocation.BuildingTypes.Temple)
            { role = NpcArchetype.Healer; return true; }
            if (building.BuildingType == DFLocation.BuildingTypes.GuildHall)
            { role = NpcArchetype.Mage; return true; }
            return false;
        }

        private bool StaticPersonalTradeAvailable(WorldStateStore expected, StaticNPC npc, NativePerson person,
            NpcArchetype role, NativeStaticNpcInventoryOwner owner, PlayerEntity player)
        {
            var game = GameManager.Instance;
            NpcArchetype liveRole;
            return !saveFault && ReferenceEquals(store, expected) && expected != null && options.Enabled && game != null &&
                ReferenceEquals(game.PlayerEntity, player) && NativeStaticNpcInventoryOwner.Matches(npc, person) &&
                StaticPersonalTradeRole(npc, out liveRole) && liveRole == role &&
                (owner == null || owner.Person == person && owner.Binding != null && owner.Binding.PersonId == person.Id) &&
                streets != null && streets.Ready && !streets.KeeperServiceBlocked(npc) &&
                !PersonalTradeWorkIsBusy(expected, person.Id);
        }

        private bool StaticPersonalTradeWilling(WorldStateStore expected, StaticNPC npc, NativePerson person,
            NpcArchetype role, NativeStaticNpcInventoryOwner owner, PlayerEntity player)
        {
            if (!StaticPersonalTradeAvailable(expected, npc, person, role, owner, player)) return false;
            int disposition;
            if (!expected.TryGetActualConversationDisposition(person.Id, out disposition)) return false;
            var personality = expected.ReadNpcPersonality(person.Id);
            var relation = expected.ReadNpcRelationship(person.Id, NpcDirectedRelationship.PlayerTargetId, ConversationActionTime());
            if (personality == null || relation != null &&
                (relation.ProjectedInterpersonalFear >= 40 || relation.ProjectedResentment >= 50)) return false;
            var needs = expected.ReadNpcMotivationState(person.Id);
            if (needs != null) foreach (var need in needs.Needs)
                if (need.KnowledgeState == NpcMotivationKnowledgeState.Known && need.Intensity.HasValue && need.Intensity.Value >= 70 &&
                    (need.Kind == NpcMotivationNeedKind.Rest || need.Kind == NpcMotivationNeedKind.Safety ||
                     need.Kind == NpcMotivationNeedKind.Health || need.Kind == NpcMotivationNeedKind.Equipment)) return false;
            int minimum = personality.Greed == "greedy" ? -10 : 0;
            if (personality.Generosity == "stingy") minimum += 20;
            if (personality.Generosity == "generous") minimum -= 10;
            return disposition >= minimum;
        }

        private bool TryCreateStaticPersonalTradeWindowSession(DaggerfallTalkWindow talk,
            out PersonalTradeWindowSession session, out string reason)
        {
            session = null;
            reason = "I cannot arrange a trade right now.";
            StaticNPC npc;
            NativePerson person;
            NpcArchetype role;
            var game = GameManager.Instance;
            if (!TryResolveStaticPersonalTradeParty(talk, out npc, out person, out role) || game == null ||
                game.PlayerEntity == null || game.PlayerEntity.Items == null) return false;
            if (!StaticPersonalTradeWilling(store, npc, person, role, null, game.PlayerEntity))
            {reason="I don't want to trade with you right now.";return false;}
            NativeStaticNpcInventoryOwner owner;
            string bindingReason;
            if (!NativeStaticNpcInventoryOwner.TryOpen(store, npc, person, role, out owner, out bindingReason) ||
                owner == null || owner.Binding == null || !StaticPersonalTradeWilling(store, npc, person, role, owner, game.PlayerEntity))
                return false;
            var saved = store.ReadNpcInventory(person.Id);
            if (!NpcInventoryReadAuthority.Matches(saved, person.Id, owner.Binding.Revision,
                NativeNpcInventoryBridge.CaptureItems(owner.Binding.Items))) return false;

            WorldStateStore sessionStore = store;
            NativeStaticNpcInventoryOwner sessionOwner = owner;
            NativeNpcInventoryBinding binding = owner.Binding;
            PlayerEntity player = game.PlayerEntity;
            string personId = person.Id;
            long epoch = saved.Revision;
            int priceFavour=dispositionCommerceEnabled && conversationDispositionEnabled?sessionStore.PersonalTradeFavour(personId,ConversationActionTime()):0;
            var checks = new PersonalBarterLiveChecks {
                NpcAdultNow = () => StaticPersonalTradeAvailable(sessionStore, npc, person, role, sessionOwner, player),
                NpcEligibleNow = () => StaticPersonalTradeAvailable(sessionStore, npc, person, role, sessionOwner, player),
                NpcWillingNow = () => StaticPersonalTradeWilling(sessionStore, npc, person, role, sessionOwner, player),
                PlayerConsentedNow = () => !saveFault && ReferenceEquals(store, sessionStore) && GameManager.Instance != null &&
                    ReferenceEquals(GameManager.Instance.PlayerEntity, player),
                NpcValuesItemNow = PersonalTradeItemDisposable,
                OccupationallyDisposableNow = PersonalTradeItemDisposable,
                ItemUniqueNow = item => item != null && item.IsArtifact,
                ItemSourceAllowedNow = item => PersonalTradeItemSourceAllowed(sessionStore, personId, item) ||
                    PersonalTradeItemSourceAllowed(sessionStore, null, item),
            };
            PersonalBarterOrder prepared = null;
            NpcCommerceHaggleBasketBinding haggle = null;
            NpcCommerceHaggleBasketRound acceptedRound = null;
            PersonalTradeWindowQuote acceptedQuote = null;
            string fingerprint = null;
            int currentNetGoldToNpc = 0, nextProposalRound = 1;
            string sessionDate = memoryDate ?? CurrentConversationTimestamp();
            string sessionPlace = ConversationMemoryPlace();
            var proposalEffects = new HashSet<string>();
            var committedEffects = new HashSet<string>();
            Action clear = () => { prepared = null; haggle = null; acceptedRound = null; acceptedQuote = null;
                fingerprint = null; currentNetGoldToNpc = 0; nextProposalRound = 1; };
            Action<string, int, string, string, HashSet<string>> mood = (id, delta, playerText, npcText, effects) =>
            {
                if (delta == 0 || string.IsNullOrWhiteSpace(id) || !effects.Add(id)) return;
                var current = sessionStore.CaptureConversationMood(personId, ConversationActionTime());
                long ignored;
                sessionStore.TryCommitAcceptedActionExchange(personId, current.AcceptedTurnSequence, current.Revision,
                    delta, ConversationActionTime(), sessionDate, sessionPlace, playerText, npcText, out ignored);
            };
            PersonalTradeWindowPrepare prepare = (PersonalTradeWindowProposal proposal, out PersonalTradeWindowQuote quote, out string failure) =>
            {
                quote = null;
                failure = "The static personal trade changed.";
                if (!StaticPersonalTradeProposalCurrent(proposal, personId, epoch, sessionStore, binding, player, checks)) return false;
                List<PersonalBarterLine> lines;
                if (!TryBuildPersonalTradeLines(proposal, out lines)) { failure = "Choose at least one item to trade."; return false; }
                double now = ConversationActionTime();
                var relationship = sessionStore.ReadNpcRelationship(personId, NpcDirectedRelationship.PlayerTargetId, now);
                int? trust = relationship != null && relationship.TrustKnown ? (int?)relationship.ReliabilityTrust : null;
                int moodValue = sessionStore.CaptureConversationMood(personId, now).Value;
                string basket = PersonalTradeBasketFingerprint(proposal);
                if (haggle == null || fingerprint != basket)
                {
                    int baseNet;
                    if (!TryQuoteStaticPersonalTrade(sessionStore, npc, person, role, sessionOwner, player, lines, out baseNet, priceFavour))
                    { failure = "The current basket cannot be priced."; return false; }
                    if (!NativeNpcInventoryBridge.TryPreparePersonalBarterForInventory(sessionStore, binding, player,
                        "static-personal-barter:" + Guid.NewGuid().ToString("N"), lines, baseNet, checks,
                        ignoredDelta => binding != null && binding.Items != null && binding.Items.Count <= NpcInventoryLimits.MaximumItems,
                        out prepared, out failure, quoteOnly:true))
                    { clear(); return false; }
                    haggle = NpcCommerceHaggleBasketBinding.Bind(prepared, NpcCommerceHagglingPolicy.MinimumBasketNetGold(baseNet, trust, moodValue));
                    currentNetGoldToNpc = baseNet;
                    nextProposalRound = 1;
                    fingerprint = basket;
                }
                else
                {
                    PersonalBarterOrder fresh;
                    if (!NativeNpcInventoryBridge.TryPreparePersonalBarterForInventory(sessionStore, binding, player,
                        prepared.TransactionId, lines, haggle.InitialNetGoldToNpc, checks,
                        ignoredDelta => binding != null && binding.Items != null && binding.Items.Count <= NpcInventoryLimits.MaximumItems,
                        out fresh, out failure, quoteOnly:true) || !haggle.Matches(fresh))
                    { clear(); return false; }
                    prepared = fresh;
                }
                int disposition;
                if (!sessionStore.TryGetActualConversationDisposition(personId, out disposition))
                { clear(); failure = "Their current disposition is unavailable."; return false; }
                if (!proposal.HasPlayerOffer)
                {
                    acceptedRound = null;
                    quote = new PersonalTradeWindowQuote { NetGoldToNpc = currentNetGoldToNpc, CanSubmit = false,
                        Status = "" };
                    acceptedQuote = quote;
                    return true;
                }
                var round = haggle.CreateRound(currentNetGoldToNpc, proposal.PlayerOfferGold, nextProposalRound,
                    Mathf.Clamp(player.Skills.GetLiveSkillValue(DFCareer.Skills.Mercantile), 0, 100),
                    Mathf.Clamp(player.Stats.LivePersonality, 1, 100), disposition, trust, moodValue);
                var result = NpcCommerceHagglingPolicy.Decide(round);
                acceptedRound = result.Disposition == NpcCommerceHaggleDisposition.Accept ? round : null;
                quote = new PersonalTradeWindowQuote { NetGoldToNpc = result.PriceGold, CanSubmit = acceptedRound != null,
                    ExpectedNetGoldToNpc = currentNetGoldToNpc,
                    Disposition = result.Disposition, ProposalMoodDelta = result.ProposalMoodDelta, ProposalRound = round.Round,
                    ProposalEffectId = NpcCommerceHagglingPolicy.ProposalEffectId(haggle.NegotiationId, round.Round),
                    Status = acceptedRound != null ? "The offer is accepted." : result.Disposition == NpcCommerceHaggleDisposition.Reject
                        ? npc.DisplayName + " refuses that offer." : npc.DisplayName + " suggests another amount." };
                acceptedQuote = quote;
                return true;
            };
            PersonalTradeWindowProposalSubmitted proposed = (proposal, quote) =>
            {
                if (proposal == null || quote == null || quote.ProposalRound < 1 || haggle == null ||
                    fingerprint != PersonalTradeBasketFingerprint(proposal)) return;
                mood(quote.ProposalEffectId, quote.ProposalMoodDelta,
                    "Personal trade offer: " + PersonalTradeGoldText(proposal.PlayerOfferGold), quote.Status, proposalEffects);
                if (quote.Disposition == NpcCommerceHaggleDisposition.CounterOffer) currentNetGoldToNpc = quote.NetGoldToNpc;
                if (quote.ProposalRound >= nextProposalRound) nextProposalRound = quote.ProposalRound + 1;
            };
            PersonalTradeWindowSubmit submit = (PersonalTradeWindowProposal proposal, PersonalTradeWindowQuote quote, out string receiptId, out string failure) =>
            {
                receiptId = null;
                failure = "The static personal trade changed.";
                if (prepared == null || haggle == null || acceptedRound == null || !ReferenceEquals(quote, acceptedQuote) ||
                    !quote.CanSubmit || fingerprint != PersonalTradeBasketFingerprint(proposal) ||
                    !StaticPersonalTradeProposalCurrent(proposal, personId, epoch, sessionStore, binding, player, checks)) return false;
                PersonalBarterReceipt receipt;
                string recollection=CapturePersonalTradeMemory(proposal,player.Items,binding.Items,haggle.InitialNetGoldToNpc);
                if (!NativeNpcInventoryBridge.TryPersonalBarterForInventory(sessionStore, binding, player, prepared,
                    ConversationActionTime(), checks, haggle,
                    () => StaticPersonalTradeCurrentBaseNet(sessionStore, npc, person, role, sessionOwner, player, prepared, priceFavour),
                    acceptedRound, ignoredDelta => binding != null && binding.Items != null && binding.Items.Count <= NpcInventoryLimits.MaximumItems,
                    out receipt, out failure)) return false;
                receiptId = PersonalTradeCommitBoundary.SourceBoundId("receipt", receipt.TransactionId);
                RememberPersonalTrade(sessionStore,personId,receiptId,recollection,NpcEventValence.Trade(receipt.NetGoldToNpc,haggle.InitialNetGoldToNpc));
                if (sessionStore.TryReservePersonalBarterMercantileTally(receipt.TransactionId))
                    player.TallySkill(DFCareer.Skills.Mercantile, 1);
                mood(NpcCommerceHagglingPolicy.TradeEffectId(receiptId),
                    NpcCommerceHagglingPolicy.CommittedTradeMoodDelta(haggle, receipt.NetGoldToNpc),
                    "Personal trade accepted.", "Trade completed.", committedEffects);
                clear();
                return true;
            };
            session = new PersonalTradeWindowSession(personId, epoch, player.Items, binding.Items,
                () => player.GoldPieces,
                () => { var current = sessionStore.ReadNpcInventory(personId); return current == null ? 0 : current.WalletGold; },
                prepare, submit,
                (side, item) => PersonalTradeItemDisposable(item) && (side == PersonalTradeSide.Npc
                    ? PersonalTradeItemSourceAllowed(sessionStore, personId, item)
                    : PersonalTradeItemSourceAllowed(sessionStore, null, item)),
                "Player", person.Name, PersonalTradeCloseMemory(sessionStore,personId), proposed);
            reason = null;
            return true;
        }

        private static bool StaticPersonalTradeProposalCurrent(PersonalTradeWindowProposal proposal, string personId,
            long epoch, WorldStateStore sessionStore, NativeNpcInventoryBinding binding, PlayerEntity player,
            PersonalBarterLiveChecks checks)
        {
            return proposal != null && proposal.NpcId == personId && proposal.StoreEpoch == epoch && sessionStore != null &&
                binding != null && binding.PersonId == personId && player != null && checks != null &&
                sessionStore.ReadNpcInventory(personId) != null && sessionStore.ReadNpcInventory(personId).Revision == epoch;
        }

        private bool TryQuoteStaticPersonalTrade(WorldStateStore sessionStore, StaticNPC npc, NativePerson person,
            NpcArchetype role, NativeStaticNpcInventoryOwner owner, PlayerEntity player, IList<PersonalBarterLine> lines,
            out int net, int priceFavour=0)
        {
            net = 0;
            if (!StaticPersonalTradeAvailable(sessionStore, npc, person, role, owner, player) || lines == null) return false;
            long total = 0;
            foreach (var line in lines)
            {
                var source = line.Direction == PersonalBarterDirection.NpcToPlayer ? owner.Binding.Items : player.Items;
                var item = source.GetItem(line.ItemUid);
                int price = StaticPersonalTradeItemQuote(sessionStore, npc, person, role, owner, player, item, line.Quantity,
                    line.Direction == PersonalBarterDirection.NpcToPlayer ? PlayerCommerceKind.Buy : PlayerCommerceKind.Sell, priceFavour);
                if (price < 0) return false;
                total += line.Direction == PersonalBarterDirection.NpcToPlayer ? price : -price;
            }
            if (total < -NpcInventoryLimits.MaximumWalletGold || total > NpcInventoryLimits.MaximumWalletGold) return false;
            net = (int)total;
            return true;
        }

        private int StaticPersonalTradeCurrentBaseNet(WorldStateStore sessionStore, StaticNPC npc, NativePerson person,
            NpcArchetype role, NativeStaticNpcInventoryOwner owner, PlayerEntity player, PersonalBarterOrder order, int priceFavour=0)
        {
            int net;
            return order != null && TryQuoteStaticPersonalTrade(sessionStore, npc, person, role, owner, player, order.Lines, out net, priceFavour)
                ? net : int.MaxValue;
        }

        private int StaticPersonalTradeItemQuote(WorldStateStore sessionStore, StaticNPC npc, NativePerson person,
            NpcArchetype role, NativeStaticNpcInventoryOwner owner, PlayerEntity player, DaggerfallUnityItem item,
            int quantity, PlayerCommerceKind kind, int priceFavour=0)
        {
            if (!StaticPersonalTradeAvailable(sessionStore, npc, person, role, owner, player) || item == null ||
                !PersonalTradeItemDisposable(item) || quantity < 1 || quantity > item.stackCount) return -1;
            bool buying = kind == PlayerCommerceKind.Buy;
            return NativePersonalTradePricing.Quote(item,quantity,buying,favour:priceFavour);
        }
    }
}
