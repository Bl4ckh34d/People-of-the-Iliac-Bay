using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    // Native caller supplies only currently visible, exact inventory rows.
    // One row fixes item UID, quantity and total quote; the model cannot resize it.
    public sealed class PlayerCommerceCandidate
    {
        public int Id;
        public string ItemLabel;
        public bool VisibleToPlayer;
        public PlayerCommerceOrder Order;
        public PlayerCommerceState State;
    }

    public sealed class PlayerCommerceSelectionResult
    {
        internal readonly PlayerCommerceOrder Order;
        internal PlayerCommerceSelectionResult(PlayerCommerceOrder order) { Order = order; }
        public PlayerCommerceOrder SelectedOrder { get { return PlayerCommerceSelection.Copy(Order); } }
    }

    // This is an interpretation boundary, not dialogue publication or execution.
    public static class PlayerCommerceSelection
    {
        public const int MaximumCandidates = 16;

        public static bool TryBind(string semanticOutput, IList<PlayerCommerceCandidate> candidates,
            out PlayerCommerceSelectionResult selection)
        {
            selection = null;
            if (!ValidCandidates(candidates) || string.IsNullOrWhiteSpace(semanticOutput) ||
                semanticOutput.Length > 96) return false;
            string[] fields = semanticOutput.Trim().Split('|');
            PlayerCommerceKind kind;
            int id, quantity, quote;
            if (fields.Length != 5 || fields[0] != "DIRECT" ||
                !Enum.TryParse(fields[1], false, out kind) || !Enum.IsDefined(typeof(PlayerCommerceKind), kind) ||
                fields[1] != kind.ToString() ||
                !int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out id) ||
                !int.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out quantity) ||
                !int.TryParse(fields[4], NumberStyles.None, CultureInfo.InvariantCulture, out quote))
                return false;
            foreach (var candidate in candidates)
            {
                var order = candidate.Order;
                if (candidate.Id != id) continue;
                if (order.Kind != kind || order.Quantity != quantity || order.QuotedTotalGold != quote)
                    return false;
                selection = new PlayerCommerceSelectionResult(Copy(order));
                return true;
            }
            return false;
        }

        public static bool TryPrepareOffer(PlayerCommerceSelectionResult selection,
            PlayerCommerceState currentState, bool npcWillingNow, bool itemVisibleNow,
            int currentQuotedTotalGold, out PlayerCommerceOrder prepared)
        {
            prepared = null;
            if (selection == null || !npcWillingNow || !itemVisibleNow ||
                currentQuotedTotalGold != selection.Order.QuotedTotalGold ||
                !PlayerCommerceContract.CanBegin(selection.Order, currentState)) return false;
            prepared = Copy(selection.Order);
            return true;
        }

        // Feed this through the existing semantic classifier after its top-level
        // action route, never through a phrase match. No candidate means no prompt.
        public static string Prompt(string currentPlayerText, IList<PlayerCommerceCandidate> candidates)
        {
            if (!ValidCandidates(candidates) || string.IsNullOrWhiteSpace(currentPlayerText) ||
                currentPlayerText.Length > 4000) return null;
            var text = new StringBuilder("Interpret the current player's requested commerce effect using only the listed visible native items. Buy/Sell/Give/Take are from the player's perspective. Select one exact candidate; preserve its kind, ID, quantity and total gold quote. Do not decide NPC willingness, publish an offer, or claim any transaction occurred. Output DIRECT|Kind|id|quantity|quotedGold for one current request. Output NONE for no match or ambiguity; NEGATED, REPORTED, HYPOTHETICAL or CONDITIONAL for those non-current uses. Polite and indirect current requests count.\nVisible candidates:\n");
            foreach (var candidate in candidates)
            {
                var order = candidate.Order;
                text.Append(order.Kind).Append('|').Append(candidate.Id.ToString(CultureInfo.InvariantCulture))
                    .Append('|').Append(order.Quantity.ToString(CultureInfo.InvariantCulture))
                    .Append('|').Append(order.QuotedTotalGold.ToString(CultureInfo.InvariantCulture))
                    .Append(" item=").Append(candidate.ItemLabel).Append('\n');
            }
            return text.Append("Current player message:\n").Append(currentPlayerText).Append("\nDecision:").ToString();
        }

        public static string Grammar(IList<PlayerCommerceCandidate> candidates)
        {
            if (!ValidCandidates(candidates)) return null;
            var choices = new List<string> { "\"NONE\"", "\"NEGATED\"", "\"REPORTED\"",
                "\"HYPOTHETICAL\"", "\"CONDITIONAL\"" };
            foreach (var candidate in candidates)
            {
                var order = candidate.Order;
                choices.Add("\"DIRECT|" + order.Kind + "|" + candidate.Id.ToString(CultureInfo.InvariantCulture) +
                    "|" + order.Quantity.ToString(CultureInfo.InvariantCulture) + "|" +
                    order.QuotedTotalGold.ToString(CultureInfo.InvariantCulture) + "\"");
            }
            return "root ::= " + string.Join(" | ", choices) + "\n";
        }

        private static bool ValidCandidates(IList<PlayerCommerceCandidate> candidates)
        {
            if (candidates == null || candidates.Count < 1 || candidates.Count > MaximumCandidates)
                return false;
            var ids = new HashSet<int>();
            foreach (var candidate in candidates)
            {
                if (candidate == null || candidate.Id < 1 || !ids.Add(candidate.Id) ||
                    !candidate.VisibleToPlayer || candidate.Order == null || candidate.State == null ||
                    string.IsNullOrWhiteSpace(candidate.ItemLabel) || candidate.ItemLabel.Length > 120 ||
                    candidate.ItemLabel.IndexOfAny(new[] { '\n', '\r', '|' }) >= 0)
                    return false;
                // Interpret facts before willingness or consent. The current
                // state and both consents are checked again before any offer.
                var evidence = Copy(candidate.State);
                evidence.PlayerConsented = true;
                evidence.NpcConsented = true;
                evidence.NpcWilling = true;
                if (!PlayerCommerceContract.CanBegin(candidate.Order, evidence)) return false;
            }
            return true;
        }

        internal static PlayerCommerceOrder Copy(PlayerCommerceOrder source)
        {
            return new PlayerCommerceOrder {
                TransactionId = source.TransactionId, NpcId = source.NpcId,
                SourceOwnerId = source.SourceOwnerId, TargetOwnerId = source.TargetOwnerId,
                ServiceCode = source.ServiceCode, Kind = source.Kind, Venue = source.Venue,
                ItemUid = source.ItemUid, Quantity = source.Quantity, QuotedTotalGold = source.QuotedTotalGold,
                ExpectedSourceInventoryRevision = source.ExpectedSourceInventoryRevision,
                ExpectedTargetInventoryRevision = source.ExpectedTargetInventoryRevision,
                ExpectedPayerWalletRevision = source.ExpectedPayerWalletRevision,
                ExpectedPayeeWalletRevision = source.ExpectedPayeeWalletRevision,
                PlayerInventoryContainerId = source.PlayerInventoryContainerId,
                PlayerWalletContainerId = source.PlayerWalletContainerId,
                ExpectedPlayerInventoryToken = source.ExpectedPlayerInventoryToken,
                ExpectedPlayerWalletToken = source.ExpectedPlayerWalletToken
            };
        }

        private static PlayerCommerceState Copy(PlayerCommerceState source)
        {
            return new PlayerCommerceState {
                SourceOwnerId = source.SourceOwnerId, TargetOwnerId = source.TargetOwnerId,
                PayerOwnerId = source.PayerOwnerId, PayeeOwnerId = source.PayeeOwnerId,
                ServiceCode = source.ServiceCode, ItemUid = source.ItemUid,
                RecipientItemUid = source.RecipientItemUid, SourceQuantity = source.SourceQuantity,
                RecipientQuantity = source.RecipientQuantity, ReceiverCapacity = source.ReceiverCapacity,
                PayerGold = source.PayerGold, PayeeGold = source.PayeeGold,
                SourceInventoryRevision = source.SourceInventoryRevision,
                TargetInventoryRevision = source.TargetInventoryRevision,
                PayerWalletRevision = source.PayerWalletRevision,
                PayeeWalletRevision = source.PayeeWalletRevision,
                PlayerInventoryContainerId = source.PlayerInventoryContainerId,
                PlayerWalletContainerId = source.PlayerWalletContainerId,
                PlayerInventoryToken = source.PlayerInventoryToken,
                PlayerWalletToken = source.PlayerWalletToken,
                SourceAccessible = source.SourceAccessible, ServiceAvailable = source.ServiceAvailable,
                PlayerConsented = source.PlayerConsented, NpcConsented = source.NpcConsented,
                NpcAdult = source.NpcAdult, NpcEligible = source.NpcEligible,
                NpcWilling = source.NpcWilling, NpcNeedsItem = source.NpcNeedsItem,
                NpcValuesItem = source.NpcValuesItem, PlayerMercantile = source.PlayerMercantile,
                ItemEquipped = source.ItemEquipped,
                ItemOccupationallyNecessary = source.ItemOccupationallyNecessary,
                ItemUnique = source.ItemUnique, ItemKnownStolen = source.ItemKnownStolen,
                ItemQuestProtected = source.ItemQuestProtected
            };
        }
    }
}
