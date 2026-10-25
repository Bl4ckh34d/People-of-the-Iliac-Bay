using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcCommerceHaggleDisposition { Reject, CounterOffer, Accept }

    // An immutable, exact-item negotiation witness. The native quote is bound
    // once; callers must not alter the underlying commerce order in place.
    public sealed class NpcCommerceHaggleBinding
    {
        public string NegotiationId { get; private set; }
        public PlayerCommerceKind Kind { get; private set; }
        public ulong ItemUid { get; private set; }
        public int Quantity { get; private set; }
        public int InitialQuoteGold { get; private set; }
        public int NpcLimitGold { get; private set; }

        private NpcCommerceHaggleBinding(PlayerCommerceOrder order, int npcLimitGold)
        {
            NegotiationId = order.TransactionId;
            Kind = order.Kind;
            ItemUid = order.ItemUid;
            Quantity = order.Quantity;
            InitialQuoteGold = order.QuotedTotalGold;
            NpcLimitGold = npcLimitGold;
        }

        public static NpcCommerceHaggleBinding Bind(PlayerCommerceOrder order, int npcLimitGold)
        {
            if (order == null || order.Venue != PlayerCommerceVenue.Personal ||
                (order.Kind != PlayerCommerceKind.Buy && order.Kind != PlayerCommerceKind.Sell) ||
                string.IsNullOrWhiteSpace(order.TransactionId) || order.TransactionId.Length > 240 ||
                order.ItemUid == 0 || order.Quantity < 1 || order.QuotedTotalGold < 1 ||
                (order.Kind == PlayerCommerceKind.Buy ? npcLimitGold > order.QuotedTotalGold :
                    npcLimitGold < order.QuotedTotalGold) || npcLimitGold < 1)
                throw new ArgumentOutOfRangeException(nameof(order));
            return new NpcCommerceHaggleBinding(order, npcLimitGold);
        }

        public NpcCommerceHaggleRound CreateRound(int currentQuoteGold, int playerOfferGold, int round,
            int playerMercantile, int playerPersonality, int disposition)
        {
            return new NpcCommerceHaggleRound
            {
                NegotiationId = NegotiationId,
                Kind = Kind,
                InitialQuoteGold = InitialQuoteGold,
                NpcLimitGold = NpcLimitGold,
                CurrentQuoteGold = currentQuoteGold,
                PlayerOfferGold = playerOfferGold,
                Quantity = Quantity,
                Round = round,
                PlayerMercantile = playerMercantile,
                PlayerPersonality = playerPersonality,
                Disposition = disposition,
            };
        }
    }

    // A mixed basket is not represented by a synthetic item. This binds every
    // source line plus the native net quote that was used to price the basket.
    public sealed class NpcCommerceHaggleBasketBinding
    {
        private sealed class LineFingerprint
        {
            public PersonalBarterDirection Direction;
            public ulong ItemUid;
            public int Quantity;
            public string PlayerSourceToken;
        }

        private readonly List<LineFingerprint> lines;
        public string NegotiationId { get; private set; }
        public string NpcId { get; private set; }
        public long ExpectedNpcRevision { get; private set; }
        public string PlayerWalletToken { get; private set; }
        public int InitialNetGoldToNpc { get; private set; }
        public int NpcMinimumNetGoldToNpc { get; private set; }
        public int TotalItemQuantity { get; private set; }

        private NpcCommerceHaggleBasketBinding(PersonalBarterOrder order, int npcMinimumNetGoldToNpc)
        {
            NegotiationId = order.TransactionId;
            NpcId = order.NpcId;
            ExpectedNpcRevision = order.ExpectedNpcRevision;
            PlayerWalletToken = order.ExpectedPlayerWalletToken;
            InitialNetGoldToNpc = order.NetGoldToNpc;
            NpcMinimumNetGoldToNpc = npcMinimumNetGoldToNpc;
            lines = new List<LineFingerprint>();
            long total = 0;
            foreach (var line in order.Lines)
            {
                lines.Add(new LineFingerprint { Direction = line.Direction, ItemUid = line.ItemUid,
                    Quantity = line.Quantity, PlayerSourceToken = line.ExpectedPlayerInventoryToken });
                total += line.Quantity;
            }
            TotalItemQuantity = (int)total;
        }

        public static NpcCommerceHaggleBasketBinding Bind(PersonalBarterOrder order, int npcMinimumNetGoldToNpc)
        {
            if (!ValidOrder(order) || npcMinimumNetGoldToNpc > order.NetGoldToNpc ||
                npcMinimumNetGoldToNpc < -NpcInventoryLimits.MaximumWalletGold ||
                order.NetGoldToNpc > NpcInventoryLimits.MaximumWalletGold)
                throw new ArgumentOutOfRangeException(nameof(order));
            return new NpcCommerceHaggleBasketBinding(order, npcMinimumNetGoldToNpc);
        }

        // Any changed basket line, source token, native revision, wallet token
        // or native base quote invalidates the prior counter/acceptance.
        public bool Matches(PersonalBarterOrder order)
        {
            if (!ValidOrder(order) || order.TransactionId != NegotiationId || order.NpcId != NpcId ||
                order.ExpectedNpcRevision != ExpectedNpcRevision || order.ExpectedPlayerWalletToken != PlayerWalletToken ||
                order.NetGoldToNpc != InitialNetGoldToNpc || order.Lines.Count != lines.Count) return false;
            for (int index = 0; index < lines.Count; index++)
            {
                var expected = lines[index];
                var actual = order.Lines[index];
                if (expected.Direction != actual.Direction || expected.ItemUid != actual.ItemUid ||
                    expected.Quantity != actual.Quantity || expected.PlayerSourceToken != actual.ExpectedPlayerInventoryToken)
                    return false;
            }
            return true;
        }

        public NpcCommerceHaggleBasketRound CreateRound(int currentNetGoldToNpc, int playerNetGoldToNpc,
            int round, int playerMercantile, int playerPersonality, int disposition, int? npcTrust = null, int npcMood = 0)
        {
            return new NpcCommerceHaggleBasketRound
            {
                NegotiationId = NegotiationId,
                InitialNetGoldToNpc = InitialNetGoldToNpc,
                NpcMinimumNetGoldToNpc = NpcMinimumNetGoldToNpc,
                CurrentNetGoldToNpc = currentNetGoldToNpc,
                PlayerNetGoldToNpc = playerNetGoldToNpc,
                TotalItemQuantity = TotalItemQuantity,
                Round = round,
                PlayerMercantile = playerMercantile,
                PlayerPersonality = playerPersonality,
                Disposition = disposition,
                NpcTrust = npcTrust,
                NpcMood = npcMood,
            };
        }

        private static bool ValidOrder(PersonalBarterOrder order)
        {
            if (order == null || string.IsNullOrWhiteSpace(order.TransactionId) || order.TransactionId.Length > 240 ||
                string.IsNullOrWhiteSpace(order.NpcId) || order.NpcId == "player" || order.NpcId.Length > 240 ||
                order.ExpectedNpcRevision < 0 || string.IsNullOrWhiteSpace(order.ExpectedPlayerWalletToken) ||
                order.ExpectedPlayerWalletToken.Length > 1024 || order.Lines == null || order.Lines.Count < 1 ||
                order.Lines.Count > PersonalBarterContract.MaximumLines) return false;
            var seen = new HashSet<ulong>();
            bool fromNpc = false, fromPlayer = false;
            long total = 0;
            foreach (var line in order.Lines)
            {
                if (line == null || (line.Direction != PersonalBarterDirection.NpcToPlayer &&
                    line.Direction != PersonalBarterDirection.PlayerToNpc) || line.ItemUid == 0 || !seen.Add(line.ItemUid) ||
                    line.Quantity < 1 || line.Quantity > 1000000) return false;
                total += line.Quantity;
                if (total > int.MaxValue) return false;
                if (line.Direction == PersonalBarterDirection.NpcToPlayer)
                {
                    fromNpc = true;
                    if (line.ExpectedPlayerInventoryToken != null) return false;
                }
                else
                {
                    fromPlayer = true;
                    if (string.IsNullOrWhiteSpace(line.ExpectedPlayerInventoryToken) ||
                        line.ExpectedPlayerInventoryToken.Length > 1024) return false;
                }
            }
            return fromNpc || fromPlayer;
        }
    }

    // Mutable request-shaped data is deliberately evaluated once per submitted
    // proposal, never from changing dialogue text or a per-frame discovery loop.
    public sealed class NpcCommerceHaggleRound
    {
        public string NegotiationId;
        public PlayerCommerceKind Kind;
        public int InitialQuoteGold, NpcLimitGold, CurrentQuoteGold, PlayerOfferGold;
        public int Quantity, Round, PlayerMercantile, PlayerPersonality, Disposition;
    }

    public sealed class NpcCommerceHaggleResult
    {
        public NpcCommerceHaggleDisposition Disposition;
        public int PriceGold, ProposalMoodDelta;
    }

    public sealed class NpcCommerceHaggleBasketRound
    {
        public string NegotiationId;
        public int InitialNetGoldToNpc, NpcMinimumNetGoldToNpc, CurrentNetGoldToNpc, PlayerNetGoldToNpc;
        public int TotalItemQuantity, Round, PlayerMercantile, PlayerPersonality, Disposition;
        public int? NpcTrust;
        public int NpcMood;
    }

    public static class NpcCommerceHagglingPolicy
    {
        // Preserve native item pricing. Only the personal bargaining margin is
        // adjusted; unknown trust is neutral, not distrust. Bound once per basket.
        public static int MinimumBasketNetGold(int nativeNetGold, int? npcTrust, int npcMood)
        {
            double margin = 20d + PersonalBargainingAdjustment(npcTrust, npcMood);
            if (nativeNetGold < -NpcInventoryLimits.MaximumWalletGold || nativeNetGold > NpcInventoryLimits.MaximumWalletGold)
                throw new ArgumentOutOfRangeException(nameof(nativeNetGold));
            if (nativeNetGold == 0) return 0;
            double net = nativeNetGold - Math.Abs((double)nativeNetGold) * margin / 100d;
            return (int)Math.Max(-NpcInventoryLimits.MaximumWalletGold,
                Math.Round(net, MidpointRounding.AwayFromZero));
        }

        private static double PersonalBargainingAdjustment(int? trust, int mood)
        {
            if (trust.HasValue && (trust.Value < 0 || trust.Value > 100) || mood < -100 || mood > 100)
                throw new ArgumentOutOfRangeException("Invalid personal bargaining context.");
            return (trust.HasValue ? (trust.Value - 50) / 5d : 0d) + mood / 20d;
        }

        public static NpcCommerceHaggleResult Decide(NpcCommerceHaggleRound round)
        {
            Validate(round);
            bool buy = round.Kind == PlayerCommerceKind.Buy;
            if (buy ? round.PlayerOfferGold < round.NpcLimitGold : round.PlayerOfferGold > round.NpcLimitGold)
                return new NpcCommerceHaggleResult
                {
                    Disposition = NpcCommerceHaggleDisposition.Reject,
                    PriceGold = round.CurrentQuoteGold,
                    ProposalMoodDelta = PoorProposalMood(round),
                };

            int distance = Math.Abs(round.CurrentQuoteGold - round.NpcLimitGold);
            int step = Math.Max(1, (distance * ConcessionPercent(round) + 99) / 100);
            int counter = buy ? Math.Max(round.NpcLimitGold, round.CurrentQuoteGold - step) :
                Math.Min(round.NpcLimitGold, round.CurrentQuoteGold + step);
            bool accepted = buy ? round.PlayerOfferGold >= counter : round.PlayerOfferGold <= counter;
            return new NpcCommerceHaggleResult
            {
                Disposition = accepted ? NpcCommerceHaggleDisposition.Accept : NpcCommerceHaggleDisposition.CounterOffer,
                PriceGold = accepted ? round.PlayerOfferGold : counter,
                ProposalMoodDelta = 0,
            };
        }

        public static NpcCommerceHaggleResult Decide(NpcCommerceHaggleBasketRound round)
        {
            Validate(round);
            if (round.PlayerNetGoldToNpc < round.NpcMinimumNetGoldToNpc)
                return new NpcCommerceHaggleResult
                {
                    Disposition = NpcCommerceHaggleDisposition.Reject,
                    PriceGold = round.CurrentNetGoldToNpc,
                    ProposalMoodDelta = BasketProposalMood(round),
                };
            long distance = (long)round.CurrentNetGoldToNpc - round.NpcMinimumNetGoldToNpc;
            double percent = Math.Max(10d, Math.Min(50d, ConcessionPercent(round.PlayerMercantile,
                round.PlayerPersonality, round.Disposition) + PersonalBargainingAdjustment(round.NpcTrust, round.NpcMood)));
            long step = Math.Max(1L, (long)Math.Ceiling(distance * percent / 100d));
            int counter = checked((int)Math.Max(round.NpcMinimumNetGoldToNpc,
                (long)round.CurrentNetGoldToNpc - step));
            bool accepted = round.PlayerNetGoldToNpc >= counter;
            return new NpcCommerceHaggleResult
            {
                Disposition = accepted ? NpcCommerceHaggleDisposition.Accept : NpcCommerceHaggleDisposition.CounterOffer,
                PriceGold = accepted ? round.PlayerNetGoldToNpc : counter,
                ProposalMoodDelta = BasketProposalMood(round),
            };
        }

        // Commit owners call this only after rereading the full unmodified
        // native basket. It resolves an earlier accepted proposal, never a
        // synthetic single-item order, into the approved signed net gold.
        public static bool TryResolveAcceptedBasketNetGold(NpcCommerceHaggleBasketBinding binding,
            PersonalBarterOrder freshNativeBaseOrder, int nativeBaseNetGoldNow,
            NpcCommerceHaggleBasketRound submittedRound, out int approvedNetGoldToNpc)
        {
            approvedNetGoldToNpc = 0;
            if (binding == null || !binding.Matches(freshNativeBaseOrder) ||
                freshNativeBaseOrder.NetGoldToNpc != nativeBaseNetGoldNow ||
                nativeBaseNetGoldNow != binding.InitialNetGoldToNpc || !Matches(binding, submittedRound)) return false;
            try
            {
                var result = Decide(submittedRound);
                if (result.Disposition != NpcCommerceHaggleDisposition.Accept) return false;
                approvedNetGoldToNpc = result.PriceGold;
                return true;
            }
            catch (ArgumentException) { return false; }
        }

        // Apply only after a committed receipt. Receipt IDs make a completed
        // trade idempotent; advantage is per item rather than per stack total.
        public static int CommittedTradeMoodDelta(NpcCommerceHaggleBinding binding, int paidGold)
        {
            if (binding == null || paidGold < 1 ||
                (binding.Kind == PlayerCommerceKind.Buy &&
                    (paidGold < binding.NpcLimitGold || paidGold > binding.InitialQuoteGold)) ||
                (binding.Kind == PlayerCommerceKind.Sell &&
                    (paidGold < binding.InitialQuoteGold || paidGold > binding.NpcLimitGold)))
                throw new ArgumentOutOfRangeException(nameof(paidGold));
            int advantage = (binding.Kind == PlayerCommerceKind.Buy ? paidGold - binding.NpcLimitGold :
                binding.NpcLimitGold - paidGold) / binding.Quantity;
            return advantage >= 60 ? 3 : advantage >= 30 ? 2 : advantage >= 10 ? 1 : 0;
        }

        public static int CommittedTradeMoodDelta(NpcCommerceHaggleBasketBinding binding, int netGoldToNpc)
        {
            if (binding == null || netGoldToNpc < binding.NpcMinimumNetGoldToNpc)
                throw new ArgumentOutOfRangeException(nameof(netGoldToNpc));
            // Personal baskets apply their nudge once on OFFER, not again on receipt.
            return 0;
        }

        public static string ProposalEffectId(string negotiationId, int round)
        {
            ValidateId(negotiationId);
            if (round < 1 || round > 1000) throw new ArgumentOutOfRangeException(nameof(round));
            return "haggling:" + negotiationId + ":proposal:" + round;
        }

        public static string TradeEffectId(string receiptId)
        {
            ValidateId(receiptId);
            return "haggling:" + receiptId + ":trade";
        }

        private static int ConcessionPercent(NpcCommerceHaggleRound round)
        {
            return ConcessionPercent(round.PlayerMercantile, round.PlayerPersonality, round.Disposition);
        }

        private static int ConcessionPercent(int playerMercantile, int playerPersonality, int disposition)
        {
            int value = 15 + (playerMercantile - 60) / 4 + (playerPersonality - 50) / 5 + disposition / 10;
            return Math.Max(10, Math.Min(50, value));
        }

        private static int PoorProposalMood(NpcCommerceHaggleRound round)
        {
            int gap = round.Kind == PlayerCommerceKind.Buy ? round.NpcLimitGold - round.PlayerOfferGold :
                round.PlayerOfferGold - round.NpcLimitGold;
            long percent = (long)gap * 100 / round.NpcLimitGold;
            return percent > 35 ? -4 : percent > 15 ? -2 : -1;
        }

        private static int BasketProposalMood(NpcCommerceHaggleBasketRound round)
        {
            long benefit = (long)round.PlayerNetGoldToNpc - round.CurrentNetGoldToNpc;
            // Integer gold throughout. A zero-price basket has no percentage
            // baseline and receives no proportional mood reward or penalty.
            long scale = Math.Abs((long)round.CurrentNetGoldToNpc);
            if(scale==0)return 0;
            long steps=Math.Abs(benefit)*2/scale;
            return Math.Sign(benefit)*(int)Math.Min(100L,steps);
        }

        private static void Validate(NpcCommerceHaggleRound round)
        {
            if (round == null || (round.Kind != PlayerCommerceKind.Buy && round.Kind != PlayerCommerceKind.Sell) ||
                round.InitialQuoteGold < 1 || round.NpcLimitGold < 1 || round.CurrentQuoteGold < 1 || round.PlayerOfferGold < 1 ||
                round.Quantity < 1 || round.Quantity > 1000000 || round.Round < 1 || round.Round > 1000 ||
                round.PlayerMercantile < 0 || round.PlayerMercantile > 100 || round.PlayerPersonality < 1 ||
                round.PlayerPersonality > 100 || round.Disposition < -100 || round.Disposition > 100)
                throw new ArgumentOutOfRangeException();
            ValidateId(round.NegotiationId);
            bool buy = round.Kind == PlayerCommerceKind.Buy;
            if (buy ? round.NpcLimitGold > round.InitialQuoteGold || round.CurrentQuoteGold < round.NpcLimitGold ||
                    round.CurrentQuoteGold > round.InitialQuoteGold || round.PlayerOfferGold > round.InitialQuoteGold :
                round.NpcLimitGold < round.InitialQuoteGold || round.CurrentQuoteGold < round.InitialQuoteGold ||
                    round.CurrentQuoteGold > round.NpcLimitGold || round.PlayerOfferGold < round.InitialQuoteGold)
                throw new ArgumentOutOfRangeException();
        }

        private static void Validate(NpcCommerceHaggleBasketRound round)
        {
            if (round == null || round.TotalItemQuantity < 1 || round.Round < 1 || round.Round > 1000 ||
                round.PlayerMercantile < 0 || round.PlayerMercantile > 100 || round.PlayerPersonality < 1 ||
                round.PlayerPersonality > 100 || round.Disposition < -100 || round.Disposition > 100 ||
                round.NpcMinimumNetGoldToNpc > round.InitialNetGoldToNpc ||
                round.CurrentNetGoldToNpc < round.NpcMinimumNetGoldToNpc ||
                round.CurrentNetGoldToNpc > round.InitialNetGoldToNpc ||
                round.PlayerNetGoldToNpc < -NpcInventoryLimits.MaximumWalletGold ||
                round.PlayerNetGoldToNpc > NpcInventoryLimits.MaximumWalletGold)
                throw new ArgumentOutOfRangeException();
            ValidateId(round.NegotiationId);
            PersonalBargainingAdjustment(round.NpcTrust, round.NpcMood);
        }

        private static bool Matches(NpcCommerceHaggleBasketBinding binding, NpcCommerceHaggleBasketRound round)
        {
            return round != null && round.NegotiationId == binding.NegotiationId &&
                round.InitialNetGoldToNpc == binding.InitialNetGoldToNpc &&
                round.NpcMinimumNetGoldToNpc == binding.NpcMinimumNetGoldToNpc &&
                round.TotalItemQuantity == binding.TotalItemQuantity;
        }

        private static void ValidateId(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 240)
                throw new ArgumentException("Negotiation identity is invalid.");
        }
    }
}
