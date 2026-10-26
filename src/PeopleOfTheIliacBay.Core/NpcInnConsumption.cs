using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcInnConsumptionKind { None, Drink, Food }
    public enum NpcInnConsumptionOutcome { Purchased, InsufficientFunds, Refused, Unavailable }

    [Serializable]
    public sealed class NpcInnConsumptionReceipt
    {
        public string VisitId, BuyerId, PayerId, KeeperId, ProductName, Reason;
        public NpcInnConsumptionKind Kind;
        public NpcInnConsumptionOutcome Outcome;
        public int MenuIndex = -1, QuotedPriceGold, ChargedGold;
        public double CompletedAt;
        public bool FoodFulfilled, DrinkFulfilled;
    }

    [Serializable]
    public sealed class NpcInnConsumptionCursor
    {
        public string BuyerId;
        public string SourceKind;
        public double? ServiceStartsAt;
        public string HouseholdDayId,HouseholdSourceFingerprint;
        public int SettlementId, BuildingKey;
        public double ObservedAt, PlannedEndAt;
        public NpcInnConsumptionReceipt Receipt;
    }

    public static class NpcInnConsumption
    {
        private const int MaximumProductName = 80;
        private const int MaximumReason = 160;

        // DaggerfallTavernWindow's native menu contains four drinks followed by seven foods.
        // A caller-supplied label must never turn a drink into a fulfilled meal.
        internal static bool MatchesNativeMenuKind(NpcInnConsumptionKind kind, int menuIndex)
        {
            return kind == NpcInnConsumptionKind.Drink && menuIndex >= 0 && menuIndex < 4 ||
                kind == NpcInnConsumptionKind.Food && menuIndex >= 4 && menuIndex <= 10;
        }

        public static bool ShouldRunCounterPurchaseCallback(bool isTavern, bool purchases,
            ShopVisitPhase phase, bool arrived)
        { return isTavern && purchases && phase == ShopVisitPhase.Haggling && arrived; }

        public static int SelectMenuIndex(IList<int> prices, int walletGold, uint stableSeed)
        { return SelectMenuIndex(prices, walletGold, stableSeed, 0, 10); }

        private static int SelectMenuIndex(IList<int> prices, int walletGold, uint stableSeed,
            int firstIndex, int lastIndex)
        {
            if (prices == null || prices.Count != 11 || walletGold < 0 ||
                walletGold > NpcInventoryLimits.MaximumWalletGold || firstIndex < 0 ||
                lastIndex >= prices.Count || firstIndex > lastIndex)
                throw new ArgumentException("Invalid native tavern menu or wallet.");

            var affordable = new List<int>();
            var cheapest = new List<int>();
            int lowest = int.MaxValue;
            for (int index = 0; index < prices.Count; index++)
            {
                int price = prices[index];
                if (price < 1 || price > 255) throw new ArgumentException("Invalid native tavern menu price.");
                if (index < firstIndex || index > lastIndex) continue;
                if (price <= walletGold) affordable.Add(index);
                if (price < lowest)
                {
                    lowest = price;
                    cheapest.Clear();
                    cheapest.Add(index);
                }
                else if (price == lowest) cheapest.Add(index);
            }

            List<int> choices = affordable.Count > 0 ? affordable : cheapest;
            return choices[(int)(stableSeed % (uint)choices.Count)];
        }

        public static string VisitId(ObservedVisit visit)
        {return ServiceVisitId(NpcInnServiceIdentity.FromObserved(visit));}

        public static string ServiceVisitId(NpcInnServiceIdentity source)
        {return ServiceVisitIdValidated(NpcInnServiceIdentity.Copy(source));}

        private static string ServiceVisitIdValidated(NpcInnServiceIdentity visit)
        {
            // All visits use the native save writer's precision. Otherwise a
            // fractional campaign time changes its payment ID after saving.
            string end=HouseholdInnVisitProvenance.WireTime(visit.EndsAt);
            string identity = string.Format(CultureInfo.InvariantCulture, "{0}\n{1}\n{2}\n{3}",
                visit.BuyerId, visit.SettlementId, visit.BuildingKey, end);
            if(visit.HouseholdDayId!=null)identity+="\n"+visit.HouseholdDayId+"\n"+visit.HouseholdSourceFingerprint;
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(identity));
                var result = new StringBuilder("inn:", 68);
                foreach (byte value in hash) result.Append(value.ToString("x2", CultureInfo.InvariantCulture));
                return result.ToString();
            }
        }

        internal static bool SameVisit(ObservedVisit left, ObservedVisit right)
        { return left != null && right != null && VisitId(left) == VisitId(right); }

        internal static NpcInnConsumptionCursor CreateCursor(ObservedVisit visit, NpcInnConsumptionReceipt receipt)
        {
            return CopyCursor(new NpcInnConsumptionCursor
            {
                BuyerId = visit.ResidentId, SettlementId = visit.SettlementId, BuildingKey = visit.BuildingKey,
                HouseholdDayId=visit.HouseholdDayId,HouseholdSourceFingerprint=visit.HouseholdSourceFingerprint,
                ObservedAt = visit.ObservedAt, PlannedEndAt = visit.PlannedEndAt, Receipt = receipt,
            });
        }

        internal static NpcInnConsumptionCursor CopyCursor(NpcInnConsumptionCursor source)
        {
            bool projected=source!=null && source.SourceKind=="world-time-projection";
            if (source == null || string.IsNullOrWhiteSpace(source.BuyerId) || source.BuyerId.Length > 256 ||
                source.SettlementId < 0 || source.BuildingKey < 0 || !Finite(source.ObservedAt) ||
                !Finite(source.PlannedEndAt) || source.ObservedAt < 0 || source.PlannedEndAt < 0 ||
                source.Receipt == null || source.Receipt.CompletedAt < source.ObservedAt ||
                (projected ? source.ObservedAt!=0 || !source.ServiceStartsAt.HasValue || !Finite(source.ServiceStartsAt.Value) ||
                    source.ServiceStartsAt.Value<0 || source.Receipt.CompletedAt<source.ServiceStartsAt.Value :
                    source.SourceKind!=null || source.ServiceStartsAt.HasValue))
                throw new ArgumentException("Invalid NPC inn-consumption cursor.");
            var sourceVisit = new NpcInnServiceIdentity { BuyerId = source.BuyerId,
                HouseholdDayId=source.HouseholdDayId,HouseholdSourceFingerprint=source.HouseholdSourceFingerprint,
                SettlementId = source.SettlementId, BuildingKey = source.BuildingKey,
                StartsAt = source.ServiceStartsAt ?? source.ObservedAt, EndsAt = source.PlannedEndAt };
            return new NpcInnConsumptionCursor
            {
                BuyerId = source.BuyerId, SettlementId = source.SettlementId, BuildingKey = source.BuildingKey,
                HouseholdDayId=source.HouseholdDayId,HouseholdSourceFingerprint=source.HouseholdSourceFingerprint,
                ObservedAt = source.ObservedAt, PlannedEndAt = source.PlannedEndAt,
                SourceKind=source.SourceKind,ServiceStartsAt=source.ServiceStartsAt,
                Receipt = CopyServiceReceipt(source.Receipt, sourceVisit),
            };
        }

        internal static bool SameReceipt(NpcInnConsumptionReceipt a, NpcInnConsumptionReceipt b)
        {
            return a != null && b != null && a.VisitId == b.VisitId && a.BuyerId == b.BuyerId &&
                a.PayerId == b.PayerId && a.KeeperId == b.KeeperId && a.ProductName == b.ProductName && a.Reason == b.Reason &&
                a.Kind == b.Kind && a.Outcome == b.Outcome && a.MenuIndex == b.MenuIndex &&
                a.QuotedPriceGold == b.QuotedPriceGold && a.ChargedGold == b.ChargedGold &&
                a.CompletedAt == b.CompletedAt && a.FoodFulfilled == b.FoodFulfilled &&
                a.DrinkFulfilled == b.DrinkFulfilled;
        }

        internal static NpcInnConsumptionReceipt CopyReceipt(NpcInnConsumptionReceipt source, ObservedVisit visit)
        {return source==null?null:CopyServiceReceipt(source,NpcInnServiceIdentity.FromObserved(visit));}

        public static NpcInnConsumptionReceipt CopyServiceReceipt(NpcInnConsumptionReceipt source,NpcInnServiceIdentity identity)
        {
            if (source == null) return null;
            var visit=NpcInnServiceIdentity.Copy(identity);
            if(source.PayerId!=null && (string.IsNullOrWhiteSpace(source.PayerId) || source.PayerId.Length>256 ||
                source.Outcome!=NpcInnConsumptionOutcome.Purchased || visit.HouseholdDayId==null))
                throw new ArgumentException("Invalid shared Inn payer receipt.");
            if (source.VisitId != ServiceVisitIdValidated(visit) || source.BuyerId != visit.BuyerId ||
                source.VisitId.Length > 80 || string.IsNullOrWhiteSpace(source.VisitId) ||
                string.IsNullOrWhiteSpace(source.BuyerId) || source.BuyerId.Length > 256 ||
                source.KeeperId != null && (string.IsNullOrWhiteSpace(source.KeeperId) || source.KeeperId.Length > 240) ||
                !Enum.IsDefined(typeof(NpcInnConsumptionKind), source.Kind) ||
                !Enum.IsDefined(typeof(NpcInnConsumptionOutcome), source.Outcome) ||
                !Finite(source.CompletedAt) || source.CompletedAt < 0 ||
                source.CompletedAt > Math.Max(visit.EndsAt, visit.StartsAt) + 1 ||
                source.QuotedPriceGold < 0 || source.QuotedPriceGold > 255 ||
                source.ChargedGold < 0 || source.ChargedGold > source.QuotedPriceGold ||
                source.MenuIndex < -1 || source.MenuIndex > 10 ||
                source.ProductName != null && (string.IsNullOrWhiteSpace(source.ProductName) || source.ProductName.Length > MaximumProductName) ||
                source.Reason != null && (string.IsNullOrWhiteSpace(source.Reason) || source.Reason.Length > MaximumReason))
                throw new ArgumentException("Invalid NPC inn-consumption receipt.");

            bool purchased = source.Outcome == NpcInnConsumptionOutcome.Purchased;
            bool fulfilledMatchingKind = source.Kind == NpcInnConsumptionKind.Food &&
                source.FoodFulfilled && !source.DrinkFulfilled ||
                source.Kind == NpcInnConsumptionKind.Drink && source.DrinkFulfilled && !source.FoodFulfilled;
            bool serviceUnavailable = source.Outcome == NpcInnConsumptionOutcome.Refused ||
                source.Outcome == NpcInnConsumptionOutcome.Unavailable;
            if ((purchased || source.Outcome == NpcInnConsumptionOutcome.InsufficientFunds) &&
                    !MatchesNativeMenuKind(source.Kind, source.MenuIndex) ||
                purchased && (!fulfilledMatchingKind || source.MenuIndex < 0 ||
                    string.IsNullOrWhiteSpace(source.ProductName) || source.KeeperId == null ||
                    source.QuotedPriceGold < 1 || source.ChargedGold != source.QuotedPriceGold || source.Reason != null) ||
                !purchased && (source.FoodFulfilled || source.DrinkFulfilled || source.ChargedGold != 0) ||
                source.Outcome == NpcInnConsumptionOutcome.InsufficientFunds &&
                    ((source.Kind != NpcInnConsumptionKind.Food && source.Kind != NpcInnConsumptionKind.Drink) ||
                     source.KeeperId == null || source.MenuIndex < 0 || string.IsNullOrWhiteSpace(source.ProductName) ||
                     source.QuotedPriceGold < 1 || string.IsNullOrWhiteSpace(source.Reason)) ||
                serviceUnavailable && (source.Kind != NpcInnConsumptionKind.None || source.MenuIndex != -1 ||
                    source.ProductName != null || source.QuotedPriceGold != 0 || string.IsNullOrWhiteSpace(source.Reason)))
                throw new ArgumentException("NPC inn-consumption receipt outcome is inconsistent.");

            return new NpcInnConsumptionReceipt
            {
                VisitId = source.VisitId,
                BuyerId = source.BuyerId,
                PayerId = source.PayerId,
                KeeperId = source.KeeperId,
                ProductName = source.ProductName,
                Reason = source.Reason,
                Kind = source.Kind,
                Outcome = source.Outcome,
                MenuIndex = source.MenuIndex,
                QuotedPriceGold = source.QuotedPriceGold,
                ChargedGold = source.ChargedGold,
                CompletedAt = source.CompletedAt,
                FoodFulfilled = source.FoodFulfilled,
                DrinkFulfilled = source.DrinkFulfilled,
            };
        }

        internal static NpcInnConsumptionReceipt CreateNoPurchase(ObservedVisit visit, string keeperId,
            double now, NpcInnConsumptionOutcome outcome, string reason)
        {
            return CopyReceipt(new NpcInnConsumptionReceipt
            {
                VisitId = VisitId(visit),
                BuyerId = visit.ResidentId,
                KeeperId = keeperId,
                Kind = NpcInnConsumptionKind.None,
                Outcome = outcome,
                MenuIndex = -1,
                CompletedAt = now,
                Reason = reason,
            }, visit);
        }

        internal static NpcInnConsumptionReceipt CreatePurchase(ObservedVisit visit, string keeperId,
            double now, NpcInnConsumptionKind kind, int menuIndex, string productName, int price, bool affordable)
        {
            return CopyReceipt(new NpcInnConsumptionReceipt
            {
                VisitId = VisitId(visit),
                BuyerId = visit.ResidentId,
                KeeperId = keeperId,
                Kind = kind,
                Outcome = affordable ? NpcInnConsumptionOutcome.Purchased : NpcInnConsumptionOutcome.InsufficientFunds,
                MenuIndex = menuIndex,
                ProductName = productName,
                QuotedPriceGold = price,
                ChargedGold = affordable ? price : 0,
                CompletedAt = now,
                FoodFulfilled = affordable && kind == NpcInnConsumptionKind.Food,
                DrinkFulfilled = affordable && kind == NpcInnConsumptionKind.Drink,
                Reason = affordable ? null : "insufficient funds",
            }, visit);
        }

        internal static NpcInnConsumptionCursor CreateProjectedCursor(NpcInnServiceIdentity identity,NpcInnConsumptionReceipt receipt)
        {
            return CopyCursor(new NpcInnConsumptionCursor{BuyerId=identity.BuyerId,SettlementId=identity.SettlementId,
                BuildingKey=identity.BuildingKey,HouseholdDayId=identity.HouseholdDayId,
                HouseholdSourceFingerprint=identity.HouseholdSourceFingerprint,SourceKind="world-time-projection",
                ServiceStartsAt=identity.StartsAt,PlannedEndAt=identity.EndsAt,Receipt=receipt});
        }

        internal static NpcInnConsumptionReceipt CreateServicePurchase(NpcInnServiceIdentity identity,string keeper,
            double now,NpcInnMenuQuote quote,bool affordable)
        {
            return CopyServiceReceipt(new NpcInnConsumptionReceipt{VisitId=ServiceVisitId(identity),BuyerId=identity.BuyerId,
                KeeperId=keeper,CompletedAt=now,Kind=quote.Kind,MenuIndex=quote.MenuIndex,ProductName=quote.ProductName,
                QuotedPriceGold=quote.PriceGold,ChargedGold=affordable?quote.PriceGold:0,
                Outcome=affordable?NpcInnConsumptionOutcome.Purchased:NpcInnConsumptionOutcome.InsufficientFunds,
                FoodFulfilled=affordable && quote.Kind==NpcInnConsumptionKind.Food,
                DrinkFulfilled=affordable && quote.Kind==NpcInnConsumptionKind.Drink,Reason=affordable?null:"insufficient funds"},identity);
        }

        internal static NpcInnConsumptionReceipt CreateServiceRefusal(NpcInnServiceIdentity identity,string keeper,
            double now,string reason)
        {
            return CopyServiceReceipt(new NpcInnConsumptionReceipt{VisitId=ServiceVisitId(identity),BuyerId=identity.BuyerId,
                KeeperId=keeper,CompletedAt=now,Kind=NpcInnConsumptionKind.None,MenuIndex=-1,
                Outcome=NpcInnConsumptionOutcome.Unavailable,Reason=reason},identity);
        }

        private static bool Finite(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }

    public sealed partial class WorldStateStore
    {
        private WorldState innConsumptionCursorIndexOwner;
        private Dictionary<string, NpcInnConsumptionCursor> innConsumptionCursorIndex;

        private NpcInnConsumptionCursor IndexedInnConsumptionCursor(string buyerId)
        {
            if (innConsumptionCursorIndexOwner != state)
            {
                innConsumptionCursorIndex = new Dictionary<string, NpcInnConsumptionCursor>(StringComparer.Ordinal);
                foreach (NpcInnConsumptionCursor cursor in state.NpcInnConsumptionCursors)
                    innConsumptionCursorIndex.Add(cursor.BuyerId, cursor);
                innConsumptionCursorIndexOwner = state;
            }
            NpcInnConsumptionCursor found;
            return buyerId != null && innConsumptionCursorIndex.TryGetValue(buyerId, out found) ? found : null;
        }

        private void PublishInnConsumptionCursor(NpcInnConsumptionCursor previous, NpcInnConsumptionCursor next)
        {
            if (previous == null) state.NpcInnConsumptionCursors.Add(next);
            else state.NpcInnConsumptionCursors[state.NpcInnConsumptionCursors.IndexOf(previous)] = next;
            innConsumptionCursorIndex[next.BuyerId] = next;
        }

        private static List<NpcInnConsumptionCursor> CopyNpcInnConsumptionCursors(WorldState source, WorldState validated)
        {
            var result = new List<NpcInnConsumptionCursor>();
            var buyerIds = new HashSet<string>(StringComparer.Ordinal);
            if (source.Version >= 45)
            {
                Require(source.NpcInnConsumptionCursors != null, "Missing NPC inn-consumption cursors.");
                foreach (NpcInnConsumptionCursor cursor in source.NpcInnConsumptionCursors)
                {
                    Require(source.Version>=74 || cursor.SourceKind==null && !cursor.ServiceStartsAt.HasValue,
                        "Old schema contains future projected Inn receipt provenance.");
                    Require(source.Version>=70 || cursor.HouseholdDayId==null && cursor.HouseholdSourceFingerprint==null,
                        "Old schema contains future shared Inn cursor fields.");
                    NpcInnConsumptionCursor copy = NpcInnConsumption.CopyCursor(cursor);
                    bool alive;
                    Require(buyerIds.Add(copy.BuyerId) && KnownNpcIdentity(validated, copy.BuyerId, out alive),
                        "Unknown or duplicate NPC inn-consumption buyer.");
                    result.Add(copy);
                }
            }
            else
            {
                // Older saves only retain the receipt attached to the current visit.
                foreach (ObservedVisit visit in validated.ObservedVisits)
                    if (visit.InnConsumption != null)
                    {
                        result.Add(NpcInnConsumption.CreateCursor(visit, visit.InnConsumption));
                        buyerIds.Add(visit.ResidentId);
                    }
            }
            foreach (ObservedVisit visit in validated.ObservedVisits)
                if (visit.InnConsumption != null)
                {
                    NpcInnConsumptionCursor cursor = result.Find(item => item.BuyerId == visit.ResidentId);
                    bool retired = cursor != null && visit.PlannedEndAt <= cursor.PlannedEndAt &&
                        visit.InnConsumption.Outcome == NpcInnConsumptionOutcome.Unavailable &&
                        visit.InnConsumption.CompletedAt >= cursor.Receipt.CompletedAt;
                    Require(cursor != null && (NpcInnConsumption.SameReceipt(cursor.Receipt, visit.InnConsumption) || retired),
                        "Observed inn receipt does not match settled cursor.");
                }
            return result;
        }

        public bool IsNpcInnConsumptionRetired(string buyerId)
        {
            ObservedVisit visit = IndexedObservedVisit(buyerId);
            NpcInnConsumptionCursor cursor = IndexedInnConsumptionCursor(buyerId);
            return visit != null && cursor != null && visit.PlannedEndAt <= cursor.PlannedEndAt &&
                NpcInnConsumption.VisitId(visit) != cursor.Receipt.VisitId && !CanSupersedeEqualHouseholdInnCursor(visit,cursor);
        }

        public NpcInnConsumptionReceipt ReadNpcInnConsumption(string residentId)
        {
            ObservedVisit visit = IndexedObservedVisit(residentId);
            if (visit == null) return null;
            if (visit.InnConsumption != null) return NpcInnConsumption.CopyReceipt(visit.InnConsumption, visit);
            NpcInnConsumptionCursor cursor = IndexedInnConsumptionCursor(residentId);
            return cursor != null && cursor.Receipt.VisitId == NpcInnConsumption.VisitId(visit)
                ? NpcInnConsumption.CopyCursor(cursor).Receipt : null;
        }

        public NpcInnConsumptionReceipt ReadNpcInnServiceConsumption(NpcInnServiceIdentity identity)
        {
            var copy=NpcInnServiceIdentity.Copy(identity);var cursor=IndexedInnConsumptionCursor(copy.BuyerId);
            return cursor!=null && cursor.Receipt.VisitId==NpcInnConsumption.ServiceVisitId(copy)?
                NpcInnConsumption.CopyServiceReceipt(cursor.Receipt,copy):null;
        }

        public bool TryBuyNpcInnConsumption(string buyerId, string keeperId, double now,
            NpcInnConsumptionKind kind, int menuIndex, string productName, int priceGold,
            out NpcInnConsumptionReceipt receipt,IList<AppointmentPlaceRoute> householdPointRoutes=null)
        {
            receipt = null;
            if (!ValidTime(now) || string.IsNullOrWhiteSpace(buyerId) || string.IsNullOrWhiteSpace(keeperId) ||
                buyerId == keeperId || !NpcInnConsumption.MatchesNativeMenuKind(kind, menuIndex) ||
                string.IsNullOrWhiteSpace(productName) || productName.Length > 80 ||
                priceGold < 1 || priceGold > 255) return false;

            ObservedVisit visit = IndexedObservedVisit(buyerId);
            if (!AtInnCounter(visit, now) || visit.InnConsumption != null || !CanSettleHouseholdInn(visit,now,householdPointRoutes)) return false;
            NpcInnConsumptionCursor previous = IndexedInnConsumptionCursor(buyerId);
            if (previous != null)
            {
                if (previous.Receipt.VisitId == NpcInnConsumption.VisitId(visit))
                {
                    NpcInnConsumptionReceipt settled = previous.Receipt;
                    if (settled.Outcome != NpcInnConsumptionOutcome.Purchased &&
                        settled.Outcome != NpcInnConsumptionOutcome.InsufficientFunds ||
                        settled.KeeperId != keeperId || settled.Kind != kind || settled.MenuIndex != menuIndex ||
                        settled.ProductName != productName || settled.QuotedPriceGold != priceGold) return false;
                    receipt = NpcInnConsumption.CopyCursor(previous).Receipt;
                    return true;
                }
                if (visit.PlannedEndAt <= previous.PlannedEndAt && !CanSupersedeEqualHouseholdInnCursor(visit,previous) ||
                    now < previous.Receipt.CompletedAt) return false;
            }
            bool buyerAlive, keeperAlive;
            if (!KnownNpcIdentity(state, buyerId, out buyerAlive) || !buyerAlive ||
                !KnownNpcIdentity(state, keeperId, out keeperAlive) || !keeperAlive ||
                !IsVisitKeeper(visit, keeperId)) return false;
            NpcInventoryState buyer = IndexedNpcInventory(buyerId);
            if (buyer == null) return false;
            var family=FundedHouseholdInn(visit.HouseholdDayId,buyerId);
            string payerId=family?.JointInn.PayerId;
            var payer=payerId==null?buyer:IndexedNpcInventory(payerId);
            bool payerAlive;
            if(payer==null || !KnownNpcIdentity(state,payer.PersonId,out payerAlive) || !payerAlive)return false;
            bool affordable = payer.WalletGold >= priceGold;
            var proposedReceipt = NpcInnConsumption.CreatePurchase(visit, keeperId, now, kind,
                menuIndex, productName, priceGold, affordable);
            if(affordable && payerId!=null)proposedReceipt.PayerId=payerId;
            NpcInnConsumptionCursor next = NpcInnConsumption.CreateCursor(visit, proposedReceipt);
            if (previous != null && state.NpcInnConsumptionCursors.IndexOf(previous) < 0) return false;
            NpcInnConsumptionReceipt detachedReceipt = NpcInnConsumption.CopyReceipt(proposedReceipt, visit);
            var memories = PrepareInnPurchaseMemories(next);
            if (affordable)
            {
                NpcInventoryState buyerCopy = CopyNpcInventory(payer);
                string transactionId = "inn-consumption:" + proposedReceipt.VisitId;
                if (!CommitNpcInventoryTransaction(transactionId, new[]
                {
                    new NpcInventoryUpdate { PersonId = payer.PersonId, ExpectedRevision = payer.Revision,
                        WalletGold = payer.WalletGold - priceGold, Items = buyerCopy.Items },
                })) return false;
            }

            PublishInnConsumptionCursor(previous, next);
            visit.InnConsumption = proposedReceipt;
            receipt = detachedReceipt;
            PublishInnPurchaseMemories(memories);
            return true;
        }

        public bool TryRecordNpcInnNoPurchase(string buyerId, string keeperId, double now,
            NpcInnConsumptionOutcome outcome, string reason, out NpcInnConsumptionReceipt receipt,IList<AppointmentPlaceRoute> householdPointRoutes=null)
        {
            receipt = null;
            if (!ValidTime(now) || string.IsNullOrWhiteSpace(buyerId) ||
                (outcome != NpcInnConsumptionOutcome.Refused && outcome != NpcInnConsumptionOutcome.Unavailable) ||
                string.IsNullOrWhiteSpace(reason) || reason.Length > 160 ||
                keeperId != null && string.IsNullOrWhiteSpace(keeperId)) return false;
            ObservedVisit visit = IndexedObservedVisit(buyerId);
            if (!AtInnCounter(visit, now) || visit.InnConsumption != null || !CanSettleHouseholdInn(visit,now,householdPointRoutes)) return false;
            NpcInnConsumptionCursor previous = IndexedInnConsumptionCursor(buyerId);
            if (previous != null)
            {
                if (previous.Receipt.VisitId == NpcInnConsumption.VisitId(visit))
                {
                    NpcInnConsumptionReceipt settled = previous.Receipt;
                    if (settled.Outcome != outcome || settled.KeeperId != keeperId || settled.Reason != reason)
                        return false;
                    receipt = NpcInnConsumption.CopyCursor(previous).Receipt;
                    return true;
                }
                if (visit.PlannedEndAt <= previous.PlannedEndAt && !CanSupersedeEqualHouseholdInnCursor(visit,previous))
                {
                    if (outcome != NpcInnConsumptionOutcome.Unavailable || now < previous.Receipt.CompletedAt)
                        return false;
                    var retiredReceipt = NpcInnConsumption.CreateNoPurchase(visit, keeperId, now, outcome, reason);
                    visit.InnConsumption = retiredReceipt;
                    receipt = NpcInnConsumption.CopyReceipt(retiredReceipt, visit);
                    return true;
                }
                if (now < previous.Receipt.CompletedAt) return false;
            }
            bool buyerAlive;
            if (!KnownNpcIdentity(state, buyerId, out buyerAlive) || !buyerAlive) return false;
            if (keeperId != null)
            {
                bool keeperAlive;
                if (!KnownNpcIdentity(state, keeperId, out keeperAlive) || !IsVisitKeeper(visit, keeperId)) return false;
            }
            var proposedReceipt = NpcInnConsumption.CreateNoPurchase(visit, keeperId, now, outcome, reason);
            NpcInnConsumptionCursor next = NpcInnConsumption.CreateCursor(visit, proposedReceipt);
            if (previous != null && state.NpcInnConsumptionCursors.IndexOf(previous) < 0) return false;
            NpcInnConsumptionReceipt detachedReceipt = NpcInnConsumption.CopyReceipt(proposedReceipt, visit);
            PublishInnConsumptionCursor(previous, next);
            visit.InnConsumption = proposedReceipt;
            receipt = detachedReceipt;
            return true;
        }

        private static bool AtInnCounter(ObservedVisit visit, double now)
        {
            if (visit == null || now < visit.ObservedAt || now > visit.EndsAt) return false;
            // A reconstructed pending service can have no timed counter dwell.
            // The native arrival callback captures a one-point counter leg at
            // this exact instant, before Evaluate advances to the table route.
            var first=visit.Legs[0];
            if(now==visit.ObservedAt && first.AfterPhase==ShopVisitPhase.Haggling &&
                first.Route.Count==1 && first.DwellSeconds==0)return true;
            ObservedVisitSnapshot snapshot;
            try { snapshot = visit.Evaluate(now); }
            catch (ArgumentOutOfRangeException) { return false; }
            return snapshot.Phase == ShopVisitPhase.Haggling;
        }

        private bool IsVisitKeeper(ObservedVisit visit, string keeperId)
        {
            NativePerson keeper = state.NativePeople.Find(person => person.Id == keeperId);
            return keeper != null && keeper.SettlementId == visit.SettlementId &&
                keeper.WorkKey == visit.BuildingKey;
        }
    }
}
