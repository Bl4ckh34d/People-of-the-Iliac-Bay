using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    [Serializable]
    public sealed class PlayerCommerceReceiptLedgerEntry
    {
        public long CommitRevision, MercantileTallyReservationRevision;
        public string TransactionId, ReceiptId, EffectId, NpcId;
        public PlayerCommerceKind Kind;
        public LoanPurpose LoanPurpose;
        public string OriginReceiptId;
        public PlayerCommerceVenue Venue;
        public ulong SourceItemUid, RecipientItemUid;
        public int Quantity, Gold;
        public long NpcInventoryRevisionAfter, NpcWalletRevisionAfter;
        public long SourceInventoryRevisionAfter, TargetInventoryRevisionAfter;
        public long PayerWalletRevisionAfter, PayeeWalletRevisionAfter;
        public string PlayerInventoryTokenAfter, PlayerWalletTokenAfter;
        public double CommittedGameTime;
        public bool MercantileTallyReserved;

        public PlayerCommerceReceiptLedgerEntry Copy()
        {
            return new PlayerCommerceReceiptLedgerEntry {
                CommitRevision = CommitRevision,
                MercantileTallyReservationRevision = MercantileTallyReservationRevision,
                TransactionId = TransactionId, ReceiptId = ReceiptId, EffectId = EffectId,
                NpcId = NpcId, Kind = Kind, LoanPurpose = LoanPurpose,
                OriginReceiptId = OriginReceiptId, Venue = Venue, SourceItemUid = SourceItemUid,
                RecipientItemUid = RecipientItemUid, Quantity = Quantity, Gold = Gold,
                NpcInventoryRevisionAfter = NpcInventoryRevisionAfter,
                NpcWalletRevisionAfter = NpcWalletRevisionAfter,
                SourceInventoryRevisionAfter = SourceInventoryRevisionAfter,
                TargetInventoryRevisionAfter = TargetInventoryRevisionAfter,
                PayerWalletRevisionAfter = PayerWalletRevisionAfter,
                PayeeWalletRevisionAfter = PayeeWalletRevisionAfter,
                PlayerInventoryTokenAfter = PlayerInventoryTokenAfter,
                PlayerWalletTokenAfter = PlayerWalletTokenAfter,
                CommittedGameTime = CommittedGameTime,
                MercantileTallyReserved = MercantileTallyReserved
            };
        }
    }

    [Serializable]
    public sealed class PlayerCommerceReceiptLedgerState
    {
        public const int CurrentVersion = 1;
        public int Version = CurrentVersion;
        public string SaveBranchId;
        public long Revision;
        public List<PlayerCommerceReceiptLedgerEntry> Entries = new List<PlayerCommerceReceiptLedgerEntry>();

        public PlayerCommerceReceiptLedgerState Copy()
        {
            var copy = new PlayerCommerceReceiptLedgerState {
                Version = Version, SaveBranchId = SaveBranchId, Revision = Revision,
                Entries = new List<PlayerCommerceReceiptLedgerEntry>()
            };
            if (Entries != null)
                foreach (var entry in Entries) copy.Entries.Add(entry == null ? null : entry.Copy());
            else copy.Entries = null;
            return copy;
        }
    }

    // Persist this reservation before applying Mercantile experience. Consumers
    // can inspect the saved entry after a crash instead of awarding twice.
    public sealed class PlayerCommerceMercantileTallyReservation
    {
        internal PlayerCommerceMercantileTallyReservation(PlayerCommerceReceiptLedgerEntry entry)
        {
            TransactionId = entry.TransactionId; NpcId = entry.NpcId;
            Kind = entry.Kind; Gold = entry.Gold;
            ReservationRevision = entry.MercantileTallyReservationRevision;
        }
        public string TransactionId { get; private set; }
        public string NpcId { get; private set; }
        public PlayerCommerceKind Kind { get; private set; }
        public int Gold { get; private set; }
        public long ReservationRevision { get; private set; }
    }

    /// <summary>Branch-bound, versioned evidence ledger; no native effect occurs here.</summary>
    public sealed class PlayerCommerceReceiptLedger
    {
        public const int MaximumEntries = 100000;
        private readonly string branchId;
        private readonly List<PlayerCommerceReceiptLedgerEntry> entries;
        private long revision;

        public PlayerCommerceReceiptLedger(string saveBranchId)
        {
            if (!Id(saveBranchId, 128)) throw new ArgumentException("Invalid commerce save branch.", nameof(saveBranchId));
            branchId = saveBranchId;
            entries = new List<PlayerCommerceReceiptLedgerEntry>();
        }

        private PlayerCommerceReceiptLedger(PlayerCommerceReceiptLedgerState state)
        {
            branchId = state.SaveBranchId;
            revision = state.Revision;
            entries = new List<PlayerCommerceReceiptLedgerEntry>(state.Entries.Count);
            foreach (var entry in state.Entries) entries.Add(entry.Copy());
        }

        public PlayerCommerceReceiptLedgerState Snapshot()
        {
            var result = new PlayerCommerceReceiptLedgerState {
                SaveBranchId = branchId, Revision = revision
            };
            foreach (var entry in entries) result.Entries.Add(entry.Copy());
            return result;
        }

        public static bool TryRestore(PlayerCommerceReceiptLedgerState saved, string expectedBranchId,
            out PlayerCommerceReceiptLedger restored)
        {
            restored = null;
            if (!Validate(saved, expectedBranchId)) return false;
            restored = new PlayerCommerceReceiptLedger(saved);
            return true;
        }

        public static bool Validate(PlayerCommerceReceiptLedgerState saved, string expectedBranchId)
        {
            if (saved == null || !Id(expectedBranchId, 128) ||
                saved.Version != PlayerCommerceReceiptLedgerState.CurrentVersion ||
                saved.SaveBranchId != expectedBranchId || saved.Revision < 0 ||
                saved.Entries == null || saved.Entries.Count > MaximumEntries) return false;
            var transactions = new HashSet<string>(StringComparer.Ordinal);
            var receipts = new HashSet<string>(StringComparer.Ordinal);
            var effects = new HashSet<string>(StringComparer.Ordinal);
            var revisions = new HashSet<long>();
            var npcRevisions = new Dictionary<string, long>(StringComparer.Ordinal);
            long previousCommitRevision = 0;
            double previousTime = 0;
            foreach (var entry in saved.Entries)
            {
                if (!ValidEntry(entry) || !transactions.Add(entry.TransactionId) ||
                    !receipts.Add(entry.ReceiptId) || !effects.Add(entry.EffectId) ||
                    entry.CommitRevision <= previousCommitRevision || entry.CommitRevision > saved.Revision ||
                    !revisions.Add(entry.CommitRevision) || entry.CommittedGameTime < previousTime)
                    return false;
                long lastNpcRevision;
                if (npcRevisions.TryGetValue(entry.NpcId, out lastNpcRevision) &&
                    entry.NpcInventoryRevisionAfter <= lastNpcRevision) return false;
                npcRevisions[entry.NpcId] = entry.NpcInventoryRevisionAfter;
                if (entry.MercantileTallyReserved &&
                    (entry.MercantileTallyReservationRevision <= entry.CommitRevision ||
                     entry.MercantileTallyReservationRevision > saved.Revision ||
                     !revisions.Add(entry.MercantileTallyReservationRevision))) return false;
                previousCommitRevision = entry.CommitRevision;
                previousTime = entry.CommittedGameTime;
            }
            foreach (var entry in saved.Entries)
                if (!ValidLoanLink(saved.Entries, entry)) return false;
            return revisions.Count == saved.Revision;
        }

        public bool TryCommit(string expectedBranchId, PlayerCommerceReceipt confirmedReceipt,
            string receiptId, string effectId, double committedGameTime)
        {
            if (expectedBranchId != branchId || confirmedReceipt == null ||
                !Id(confirmedReceipt.TransactionId, 240) || !Id(confirmedReceipt.NpcId, 240) ||
                !Id(receiptId, 240) || !Id(effectId, 240) ||
                double.IsNaN(committedGameTime) || double.IsInfinity(committedGameTime) ||
                committedGameTime < 0 || entries.Count >= MaximumEntries || revision == long.MaxValue ||
                confirmedReceipt.SourceItemUid == 0 || confirmedReceipt.RecipientItemUid == 0 ||
                confirmedReceipt.Quantity < 1 || confirmedReceipt.Quantity > 1000000 ||
                confirmedReceipt.PaidGold < 0 || confirmedReceipt.PaidGold > NpcInventoryLimits.MaximumWalletGold ||
                !ValidGold(confirmedReceipt.Kind, confirmedReceipt.PaidGold) ||
                string.IsNullOrWhiteSpace(confirmedReceipt.PlayerInventoryTokenAfter) ||
                string.IsNullOrWhiteSpace(confirmedReceipt.PlayerWalletTokenAfter)) return false;
            long npcInventoryRevision = confirmedReceipt.Kind == PlayerCommerceKind.Buy ||
                confirmedReceipt.Kind == PlayerCommerceKind.Take ?
                confirmedReceipt.SourceInventoryRevisionAfter : confirmedReceipt.TargetInventoryRevisionAfter;
            long npcWalletRevision = confirmedReceipt.Kind == PlayerCommerceKind.Buy ?
                confirmedReceipt.PayeeWalletRevisionAfter : confirmedReceipt.PayerWalletRevisionAfter;
            if (npcInventoryRevision < 1 || npcWalletRevision < 0) return false;
            foreach (var entry in entries)
            {
                if (entry.TransactionId == confirmedReceipt.TransactionId ||
                    entry.ReceiptId == receiptId || entry.EffectId == effectId) return false;
                if (entry.NpcId == confirmedReceipt.NpcId &&
                    entry.NpcInventoryRevisionAfter >= npcInventoryRevision) return false;
            }
            if (entries.Count > 0 && committedGameTime < entries[entries.Count - 1].CommittedGameTime)
                return false;
            var next = new PlayerCommerceReceiptLedgerEntry {
                CommitRevision = revision + 1,
                TransactionId = confirmedReceipt.TransactionId, ReceiptId = receiptId,
                EffectId = effectId, NpcId = confirmedReceipt.NpcId,
                Kind = confirmedReceipt.Kind, LoanPurpose = confirmedReceipt.LoanPurpose,
                OriginReceiptId = confirmedReceipt.OriginReceiptId, Venue = confirmedReceipt.Venue,
                SourceItemUid = confirmedReceipt.SourceItemUid,
                RecipientItemUid = confirmedReceipt.RecipientItemUid,
                Quantity = confirmedReceipt.Quantity, Gold = confirmedReceipt.PaidGold,
                NpcInventoryRevisionAfter = npcInventoryRevision,
                NpcWalletRevisionAfter = npcWalletRevision,
                SourceInventoryRevisionAfter = confirmedReceipt.SourceInventoryRevisionAfter,
                TargetInventoryRevisionAfter = confirmedReceipt.TargetInventoryRevisionAfter,
                PayerWalletRevisionAfter = confirmedReceipt.PayerWalletRevisionAfter,
                PayeeWalletRevisionAfter = confirmedReceipt.PayeeWalletRevisionAfter,
                PlayerInventoryTokenAfter = confirmedReceipt.PlayerInventoryTokenAfter,
                PlayerWalletTokenAfter = confirmedReceipt.PlayerWalletTokenAfter,
                CommittedGameTime = committedGameTime
            };
            if (!ValidEntry(next) || !ValidLoanLink(entries, next)) return false;
            entries.Add(next);
            revision++;
            return true;
        }

        public bool TryReserveMercantileTally(string expectedBranchId, string transactionId,
            out PlayerCommerceMercantileTallyReservation reservation)
        {
            reservation = null;
            if (expectedBranchId != branchId || !Id(transactionId, 240) || revision == long.MaxValue)
                return false;
            foreach (var entry in entries)
            {
                if (entry.TransactionId != transactionId) continue;
                if (entry.MercantileTallyReserved || !Paid(entry.Kind) || entry.Gold < 1) return false;
                entry.MercantileTallyReserved = true;
                entry.MercantileTallyReservationRevision = ++revision;
                reservation = new PlayerCommerceMercantileTallyReservation(entry);
                return true;
            }
            return false;
        }

        private static bool ValidEntry(PlayerCommerceReceiptLedgerEntry entry)
        {
            return entry != null && entry.CommitRevision >= 1 &&
                Id(entry.TransactionId, 240) && Id(entry.ReceiptId, 240) &&
                Id(entry.EffectId, 240) && Id(entry.NpcId, 240) &&
                Enum.IsDefined(typeof(PlayerCommerceKind), entry.Kind) &&
                Enum.IsDefined(typeof(LoanPurpose), entry.LoanPurpose) &&
                Enum.IsDefined(typeof(PlayerCommerceVenue), entry.Venue) &&
                entry.SourceItemUid > 0 && entry.RecipientItemUid > 0 &&
                entry.Quantity >= 1 && entry.Quantity <= 1000000 &&
                ValidGold(entry.Kind, entry.Gold) &&
                entry.NpcInventoryRevisionAfter >= 1 &&
                entry.NpcInventoryRevisionAfter <= NpcInventoryLimits.MaximumRevision &&
                entry.NpcWalletRevisionAfter >= 0 &&
                entry.NpcWalletRevisionAfter <= NpcInventoryLimits.MaximumRevision &&
                ValidRevisions(entry) &&
                Id(entry.PlayerInventoryTokenAfter, 1024) &&
                Id(entry.PlayerWalletTokenAfter, 1024) &&
                !double.IsNaN(entry.CommittedGameTime) &&
                !double.IsInfinity(entry.CommittedGameTime) && entry.CommittedGameTime >= 0 &&
                entry.MercantileTallyReserved == (entry.MercantileTallyReservationRevision > 0) &&
                (!entry.MercantileTallyReserved || Paid(entry.Kind));
        }

        private static bool ValidLoanLink(IList<PlayerCommerceReceiptLedgerEntry> prior,
            PlayerCommerceReceiptLedgerEntry entry)
        {
            if (entry.LoanPurpose == LoanPurpose.None)
                return entry.OriginReceiptId == null;
            if (entry.LoanPurpose == LoanPurpose.Lend)
                return (entry.Kind == PlayerCommerceKind.Give || entry.Kind == PlayerCommerceKind.Take) &&
                    entry.OriginReceiptId == null;
            if (entry.LoanPurpose != LoanPurpose.Return ||
                (entry.Kind != PlayerCommerceKind.Give && entry.Kind != PlayerCommerceKind.Take) ||
                !Id(entry.OriginReceiptId, 240)) return false;
            PlayerCommerceReceiptLedgerEntry origin = null;
            foreach (var candidate in prior)
            {
                if (ReferenceEquals(candidate, entry)) continue;
                if (candidate.ReceiptId == entry.OriginReceiptId) origin = candidate;
                if (candidate.LoanPurpose == LoanPurpose.Return &&
                    candidate.OriginReceiptId == entry.OriginReceiptId) return false;
            }
            return origin != null && origin.LoanPurpose == LoanPurpose.Lend &&
                origin.NpcId == entry.NpcId && origin.RecipientItemUid == entry.SourceItemUid &&
                origin.Quantity == entry.Quantity && origin.Kind != entry.Kind &&
                origin.CommitRevision < entry.CommitRevision;
        }

        private static bool ValidGold(PlayerCommerceKind kind, int gold)
        {
            return Enum.IsDefined(typeof(PlayerCommerceKind), kind) &&
                (Paid(kind) ? gold >= 1 && gold <= NpcInventoryLimits.MaximumWalletGold : gold == 0);
        }
        private static bool Paid(PlayerCommerceKind kind)
        { return kind == PlayerCommerceKind.Buy || kind == PlayerCommerceKind.Sell; }
        private static bool ValidRevisions(PlayerCommerceReceiptLedgerEntry entry)
        {
            bool npcSource = entry.Kind == PlayerCommerceKind.Buy || entry.Kind == PlayerCommerceKind.Take;
            bool playerPayer = entry.Kind == PlayerCommerceKind.Buy;
            return (npcSource ?
                    entry.SourceInventoryRevisionAfter == entry.NpcInventoryRevisionAfter &&
                    entry.TargetInventoryRevisionAfter == 0 :
                    entry.SourceInventoryRevisionAfter == 0 &&
                    entry.TargetInventoryRevisionAfter == entry.NpcInventoryRevisionAfter) &&
                (playerPayer ?
                    entry.PayerWalletRevisionAfter == 0 &&
                    entry.PayeeWalletRevisionAfter == entry.NpcWalletRevisionAfter :
                    entry.PayerWalletRevisionAfter == entry.NpcWalletRevisionAfter &&
                    entry.PayeeWalletRevisionAfter == 0);
        }
        private static bool Id(string value, int maximum)
        { return !string.IsNullOrWhiteSpace(value) && value.Length <= maximum; }
    }
}
