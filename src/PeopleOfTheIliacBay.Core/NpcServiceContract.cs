using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcServiceRole { Fighter = 1, Archer = 2, Mage = 3 }

    [Flags]
    /// <summary>Service scope only; Follow does not authorize suicidal orders or override protected-target checks.</summary>
    public enum NpcServiceCoverage
    {
        None = 0,
        Follow = 1,
        Combat = 2,
        Travel = 4,
        Rest = 8,
        Resupply = 16,
        Expedition = 32
    }

    public enum NpcServiceContractStatus { Reserved = 1, Active = 2, Suspended = 3, Terminated = 4 }
    /// <summary>Saved owner of a service pause; legacy pauses have no health-care resume authority.</summary>
    public enum NpcServiceSuspensionOwnerKind { None = 0, LegacyUnknown = 1, HealthCare = 2 }
    /// <summary>OrdinaryCombat never authorizes a clear suicidal order. BoundedExpedition only overrides routine schedule boundaries.</summary>
    [Flags]
    public enum NpcServiceRiskScope { None = 0, OrdinaryCombat = 1, BoundedExpedition = 2 }
    /// <summary>Rest availability controls service suspension; it never prorates a started daily fee.</summary>
    public enum NpcServiceRestTreatment { RestNotIncluded = 1, CountsAsPaidAvailability = 2, SuspendBillingDuringRest = 3 }
    public enum NpcServiceResupplyResponsibility { PlayerProvides = 1, CompanionUsesExistingSupplies = 2, SeparatelyNegotiated = 3 }
    public enum NpcServiceReservedObligationKind { Appointment = 1, Family = 2, Quest = 3, Service = 4 }
    public enum NpcServiceObligationTreatment { AdmissionConstraint = 1, MayInterruptService = 2 }
    public enum NpcServiceProtectedTargetCategory { Family = 1, QuestProtected = 2, PersonalRedLine = 3 }
    public enum NpcServiceEndReason
    {
        CancelledBeforeService = 1,
        NpcResigned = 2,
        EmergencyWithdrawal = 3,
        NewlyExcludedQuestActor = 4,
        PlayerDismissal = 5,
        Abandonment = 6,
        NaturalExpiry = 7,
        RenewalDeclined = 8,
        Death = 9,
        EmployerBreach = 10
    }
    public enum NpcServiceTransferKind { Funding = 1, Earnings = 2, Refund = 3 }

    /// <summary>Stable protected-person reference resolved from native relationship or quest metadata.</summary>
    [Serializable]
    public sealed class NpcServiceProtectedTarget
    {
        private NpcServiceProtectedTarget() { }
        public string ActorId { get; private set; }
        public NpcServiceProtectedTargetCategory Category { get; private set; }
        public long SourceRevision { get; private set; }
        public static bool TryCreate(string actorId, NpcServiceProtectedTargetCategory category, long sourceRevision,
            Func<string, NpcServiceProtectedTargetCategory, long, bool> knownProtectedTarget,
            out NpcServiceProtectedTarget target)
        {
            target = null;
            if (!ValidId(actorId) || !Enum.IsDefined(typeof(NpcServiceProtectedTargetCategory), category) ||
                sourceRevision <= 0 || knownProtectedTarget == null ||
                !knownProtectedTarget(actorId, category, sourceRevision)) return false;
            target = new NpcServiceProtectedTarget { ActorId = actorId, Category = category, SourceRevision = sourceRevision };
            return true;
        }
        internal NpcServiceProtectedTarget CopyValidated()
        {
            if (!ValidId(ActorId) || !Enum.IsDefined(typeof(NpcServiceProtectedTargetCategory), Category) || SourceRevision <= 0)
                throw new ArgumentException("Invalid protected service target.");
            return new NpcServiceProtectedTarget { ActorId = ActorId, Category = Category, SourceRevision = SourceRevision };
        }
        internal static bool TryRestoreShape(string actorId, NpcServiceProtectedTargetCategory category,
            long sourceRevision, out NpcServiceProtectedTarget target)
        {
            target = null;
            try
            {
                var candidate = new NpcServiceProtectedTarget { ActorId = actorId, Category = category, SourceRevision = sourceRevision };
                target = candidate.CopyValidated();
                return true;
            }
            catch (ArgumentException) { return false; }
        }
        private static bool ValidId(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 240 &&
            !ContainsControl(value);
        private static bool ContainsControl(string value)
        { for (int i = 0; i < value.Length; i++) if (char.IsControl(value[i])) return true; return false; }
    }

    /// <summary>Captured dated commitment reference; it never mutates the referenced appointment, quest or duty.</summary>
    [Serializable]
    public sealed class NpcServiceReservedObligation
    {
        private NpcServiceReservedObligation() { }
        public string ObligationId { get; private set; }
        public long SourceRevision { get; private set; }
        public NpcServiceReservedObligationKind Kind { get; private set; }
        public NpcServiceObligationTreatment Treatment { get; private set; }
        public double StartsAt { get; private set; }
        public double EndsAt { get; private set; }

        public static bool TryCreate(string obligationId, long sourceRevision,
            NpcServiceReservedObligationKind kind, NpcServiceObligationTreatment treatment,
            double startsAt, double endsAt,
            Func<string, long, NpcServiceReservedObligationKind, double, double, bool> knownObligation,
            out NpcServiceReservedObligation obligation)
        {
            obligation = null;
            if (!ValidId(obligationId) || sourceRevision <= 0 ||
                !Enum.IsDefined(typeof(NpcServiceReservedObligationKind), kind) ||
                !Enum.IsDefined(typeof(NpcServiceObligationTreatment), treatment) ||
                !Finite(startsAt) || !Finite(endsAt) || startsAt < 0 || endsAt <= startsAt ||
                knownObligation == null || !knownObligation(obligationId, sourceRevision, kind, startsAt, endsAt)) return false;
            obligation = new NpcServiceReservedObligation { ObligationId = obligationId,
                SourceRevision = sourceRevision, Kind = kind, Treatment = treatment,
                StartsAt = startsAt, EndsAt = endsAt };
            return true;
        }

        internal NpcServiceReservedObligation CopyValidated()
        {
            if (!ValidId(ObligationId) || SourceRevision <= 0 ||
                !Enum.IsDefined(typeof(NpcServiceReservedObligationKind), Kind) ||
                !Enum.IsDefined(typeof(NpcServiceObligationTreatment), Treatment) ||
                !Finite(StartsAt) || !Finite(EndsAt) || StartsAt < 0 || EndsAt <= StartsAt)
                throw new ArgumentException("Invalid reserved service obligation.");
            return new NpcServiceReservedObligation { ObligationId = ObligationId, SourceRevision = SourceRevision,
                Kind = Kind, Treatment = Treatment, StartsAt = StartsAt, EndsAt = EndsAt };
        }

        internal static bool TryRestoreShape(string obligationId, long sourceRevision,
            NpcServiceReservedObligationKind kind, NpcServiceObligationTreatment treatment,
            double startsAt, double endsAt, out NpcServiceReservedObligation obligation)
        {
            obligation = null;
            try
            {
                var candidate = new NpcServiceReservedObligation { ObligationId = obligationId, SourceRevision = sourceRevision,
                    Kind = kind, Treatment = treatment, StartsAt = startsAt, EndsAt = endsAt };
                obligation = candidate.CopyValidated();
                return true;
            }
            catch (ArgumentException) { return false; }
        }

        private static bool ValidId(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 240 &&
            !ContainsControl(value);
        private static bool ContainsControl(string value)
        { for (int i = 0; i < value.Length; i++) if (char.IsControl(value[i])) return true; return false; }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    /// <summary>
    /// Typed record shape for a completed native currency transfer. The factory validates fields, not that currency moved;
    /// only a trusted adapter may construct it from a native completed-transfer receipt. Model output and dialogue are not evidence.
    /// </summary>
    public sealed class NpcServiceTransferReceipt
    {
        private NpcServiceTransferReceipt() { }
        public string ReceiptId { get; private set; }
        public string ContractId { get; private set; }
        public NpcServiceTransferKind Kind { get; private set; }
        public string FromOwnerId { get; private set; }
        public string ToOwnerId { get; private set; }
        public int Amount { get; private set; }
        public double GameTime { get; private set; }

        public static NpcServiceTransferReceipt Create(string receiptId, string contractId,
            NpcServiceTransferKind kind, string fromOwnerId, string toOwnerId, int amount, double gameTime)
        {
            if (!ValidId(receiptId, 240) || !ValidId(contractId, 128) || !Enum.IsDefined(typeof(NpcServiceTransferKind), kind) ||
                !ValidId(fromOwnerId, 240) || !ValidId(toOwnerId, 240) || fromOwnerId == toOwnerId || amount <= 0 ||
                !Finite(gameTime) || gameTime < 0)
                throw new ArgumentException("Invalid native service transfer receipt.");
            return new NpcServiceTransferReceipt { ReceiptId = receiptId, ContractId = contractId, Kind = kind,
                FromOwnerId = fromOwnerId, ToOwnerId = toOwnerId, Amount = amount, GameTime = gameTime };
        }

        internal static NpcServiceTransferReceipt CopyValidated(NpcServiceTransferReceipt source)
        {
            if (source == null) throw new ArgumentException("Missing service transfer receipt.", nameof(source));
            return Create(source.ReceiptId, source.ContractId, source.Kind, source.FromOwnerId,
                source.ToOwnerId, source.Amount, source.GameTime);
        }

        private static bool ValidId(string value, int max)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > max) return false;
            for (int i = 0; i < value.Length; i++) if (char.IsControl(value[i])) return false;
            return true;
        }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }

    /// <summary>Explicit native player authorization for changing standing renewal permission.</summary>
    public sealed class NpcServiceRenewalAuthorization
    {
        private NpcServiceRenewalAuthorization() { }
        public string ReceiptId { get; private set; }
        public string ContractId { get; private set; }
        public string PayerId { get; private set; }
        public bool Permission { get; private set; }
        public double GameTime { get; private set; }
        public static NpcServiceRenewalAuthorization Create(string receiptId, string contractId,
            string payerId, bool permission, double gameTime)
        {
            if (string.IsNullOrWhiteSpace(receiptId) || receiptId.Length > 240 || string.IsNullOrWhiteSpace(contractId) ||
                contractId.Length > 128 || string.IsNullOrWhiteSpace(payerId) || payerId.Length > 240 ||
                !Finite(gameTime) || gameTime < 0)
                throw new ArgumentException("Invalid renewal authorization receipt.");
            return new NpcServiceRenewalAuthorization { ReceiptId = receiptId, ContractId = contractId,
                PayerId = payerId, Permission = permission, GameTime = gameTime };
        }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }

    /// <summary>Immutable, validated terms. Rate is an agreed amount, never selected or calculated by a model.</summary>
    public sealed class NpcServiceContractTerms
    {
        private const int AllCoverage = (int)(NpcServiceCoverage.Follow | NpcServiceCoverage.Combat |
            NpcServiceCoverage.Travel | NpcServiceCoverage.Rest | NpcServiceCoverage.Resupply | NpcServiceCoverage.Expedition);
        private readonly List<NpcServiceProtectedTarget> protectedTargets;
        private readonly List<NpcServiceReservedObligation> reservedObligations;
        private NpcServiceContractTerms()
        {
            protectedTargets = new List<NpcServiceProtectedTarget>();
            ProtectedTargets = new ReadOnlyCollection<NpcServiceProtectedTarget>(protectedTargets);
            reservedObligations = new List<NpcServiceReservedObligation>();
            ReservedObligations = new ReadOnlyCollection<NpcServiceReservedObligation>(reservedObligations);
        }
        public string ContractId { get; private set; }
        public string PayerId { get; private set; }
        public string NpcId { get; private set; }
        public string HomeReference { get; private set; }
        public NpcServiceRole Role { get; private set; }
        public NpcServiceCoverage Coverage { get; private set; }
        public int RatePer24GameHours { get; private set; }
        public bool StandingRenewalPermission { get; private set; }
        public NpcServiceRiskScope RiskScope { get; private set; }
        public IReadOnlyList<NpcServiceProtectedTarget> ProtectedTargets { get; private set; }
        public NpcServiceRestTreatment RestTreatment { get; private set; }
        public NpcServiceResupplyResponsibility ResupplyResponsibility { get; private set; }
        public IReadOnlyList<NpcServiceReservedObligation> ReservedObligations { get; private set; }

        public static bool TryCreate(string contractId, string payerId, string npcId, string homeReference,
            NpcServiceRole role, NpcServiceCoverage coverage, int ratePer24GameHours,
            bool standingRenewalPermission, NpcServiceRiskScope riskScope,
            IEnumerable<NpcServiceProtectedTarget> protectedTargets, NpcServiceRestTreatment restTreatment,
            NpcServiceResupplyResponsibility resupplyResponsibility,
            IEnumerable<NpcServiceReservedObligation> obligations,
            Func<string, bool> knownActor, Func<string, bool> knownHome,
            Func<string, NpcServiceProtectedTargetCategory, long, bool> knownProtectedTarget,
            Func<string, long, NpcServiceReservedObligationKind, double, double, bool> knownObligation,
            out NpcServiceContractTerms terms)
        {
            terms = null;
            const int allRisks = (int)(NpcServiceRiskScope.OrdinaryCombat | NpcServiceRiskScope.BoundedExpedition);
            if (protectedTargets == null || obligations == null ||
                (((int)riskScope & ~allRisks) != 0) ||
                !Enum.IsDefined(typeof(NpcServiceRestTreatment), restTreatment) ||
                !Enum.IsDefined(typeof(NpcServiceResupplyResponsibility), resupplyResponsibility)) return false;
            if (!ValidId(contractId, 128) || !ValidId(payerId, 240) || !ValidId(npcId, 240) ||
                !ValidId(homeReference, 240) || payerId == npcId || !Enum.IsDefined(typeof(NpcServiceRole), role) ||
                coverage == NpcServiceCoverage.None || (((int)coverage & ~AllCoverage) != 0) || ratePer24GameHours <= 0 ||
                knownActor == null || knownHome == null || !knownActor(payerId) || !knownActor(npcId) || !knownHome(homeReference))
                return false;
            bool combat = (coverage & NpcServiceCoverage.Combat) != 0;
            bool expedition = (coverage & NpcServiceCoverage.Expedition) != 0;
            if (combat != ((riskScope & NpcServiceRiskScope.OrdinaryCombat) != 0) ||
                expedition != ((riskScope & NpcServiceRiskScope.BoundedExpedition) != 0) ||
                ((coverage & NpcServiceCoverage.Rest) != 0) != (restTreatment != NpcServiceRestTreatment.RestNotIncluded) ||
                ((coverage & NpcServiceCoverage.Resupply) != 0) !=
                    (resupplyResponsibility != NpcServiceResupplyResponsibility.SeparatelyNegotiated)) return false;
            terms = new NpcServiceContractTerms { ContractId = contractId, PayerId = payerId, NpcId = npcId,
                HomeReference = homeReference, Role = role, Coverage = coverage,
                RatePer24GameHours = ratePer24GameHours, StandingRenewalPermission = standingRenewalPermission,
                RiskScope = riskScope, RestTreatment = restTreatment, ResupplyResponsibility = resupplyResponsibility };
            var uniqueActors = new HashSet<string>(StringComparer.Ordinal);
            foreach (var target in protectedTargets)
            {
                if (terms.protectedTargets.Count == 64 || target == null) { terms = null; return false; }
                NpcServiceProtectedTarget copy;
                try { copy = target.CopyValidated(); }
                catch (ArgumentException) { terms = null; return false; }
                if (copy.ActorId == npcId || !knownActor(copy.ActorId) || knownProtectedTarget == null ||
                    !knownProtectedTarget(copy.ActorId, copy.Category, copy.SourceRevision) || !uniqueActors.Add(copy.ActorId))
                { terms = null; return false; }
                terms.protectedTargets.Add(copy);
            }
            var uniqueObligations = new HashSet<string>(StringComparer.Ordinal);
            foreach (var obligation in obligations)
            {
                if (terms.reservedObligations.Count == 64 || obligation == null ||
                    !uniqueObligations.Add(obligation.ObligationId)) { terms = null; return false; }
                NpcServiceReservedObligation copy;
                try { copy = obligation.CopyValidated(); }
                catch (ArgumentException) { terms = null; return false; }
                if (knownObligation == null || !knownObligation(copy.ObligationId, copy.SourceRevision, copy.Kind,
                    copy.StartsAt, copy.EndsAt))
                { terms = null; return false; }
                terms.reservedObligations.Add(copy);
            }
            return true;
        }

        private static bool ValidId(string value, int max)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > max) return false;
            for (int i = 0; i < value.Length; i++) if (char.IsControl(value[i])) return false;
            return true;
        }
    }

    /// <summary>
    /// Pure, detached candidate for a funded non-quest service contract. It records transfer receipts;
    /// it never debits a player, credits an NPC, creates gold, or proves that a role adapter exists.
    /// </summary>
    [Serializable]
    public sealed class NpcServiceContract
    {
        public const int VersionCurrent = 2;
        public const double BillingPeriodSeconds = 86400d;
        private readonly List<NpcServiceTransferReceipt> receipts;
        private readonly List<NpcServiceProtectedTarget> protectedTargets;
        private readonly List<NpcServiceReservedObligation> reservedObligations;
        [NonSerialized] private NpcServiceContract derivedFrom;

        private NpcServiceContract()
        {
            receipts = new List<NpcServiceTransferReceipt>();
            TransferReceipts = new ReadOnlyCollection<NpcServiceTransferReceipt>(receipts);
            protectedTargets = new List<NpcServiceProtectedTarget>();
            ProtectedTargets = new ReadOnlyCollection<NpcServiceProtectedTarget>(protectedTargets);
            reservedObligations = new List<NpcServiceReservedObligation>();
            ReservedObligations = new ReadOnlyCollection<NpcServiceReservedObligation>(reservedObligations);
        }

        public int Version { get; private set; }
        public string ContractId { get; private set; }
        public string PayerId { get; private set; }
        public string NpcId { get; private set; }
        public string HomeReference { get; private set; }
        public NpcServiceRole Role { get; private set; }
        public NpcServiceCoverage Coverage { get; private set; }
        public int RatePer24GameHours { get; private set; }
        public bool StandingRenewalPermission { get; private set; }
        public NpcServiceRiskScope RiskScope { get; private set; }
        public IReadOnlyList<NpcServiceProtectedTarget> ProtectedTargets { get; private set; }
        public NpcServiceRestTreatment RestTreatment { get; private set; }
        public NpcServiceResupplyResponsibility ResupplyResponsibility { get; private set; }
        public IReadOnlyList<NpcServiceReservedObligation> ReservedObligations { get; private set; }
        public NpcServiceContractStatus Status { get; private set; }
        public NpcServiceSuspensionOwnerKind SuspensionOwnerKind { get; private set; }
        public string SuspensionOwnerId { get; private set; }
        public NpcServiceEndReason? EndReason { get; private set; }
        public long Revision { get; private set; }
        public double AcceptedAt { get; private set; }
        public double BlockStartAt { get; private set; }
        public double BlockEndAt { get; private set; }
        public double LastServiceUpdateAt { get; private set; }
        /// <summary>Actual native settlement receipt time, separate from the effective service update time.</summary>
        public double SettlementRecordedAt { get; private set; }
        public double EligibleServiceSeconds { get; private set; }
        public double CurrentBlockEligibleSeconds { get; private set; }
        public int FundedGold { get; private set; }
        public int EscrowGold { get; private set; }
        public int EarnedGold { get; private set; }
        public int ReleasedGold { get; private set; }
        public int RefundedGold { get; private set; }
        public IReadOnlyList<NpcServiceTransferReceipt> TransferReceipts { get; private set; }

        internal static bool TryReserve(NpcServiceContractTerms terms, NpcServiceTransferReceipt funding,
            out NpcServiceContract reserved)
        {
            reserved = null;
            if (terms == null || funding == null ||
                funding.ContractId != terms.ContractId || funding.Kind != NpcServiceTransferKind.Funding ||
                funding.FromOwnerId != terms.PayerId || funding.ToOwnerId != EscrowOwner(terms.ContractId) ||
                funding.Amount != terms.RatePer24GameHours) return false;
            var receipt = NpcServiceTransferReceipt.CopyValidated(funding);
            var value = new NpcServiceContract
            {
                Version = VersionCurrent, ContractId = terms.ContractId, PayerId = terms.PayerId,
                NpcId = terms.NpcId, HomeReference = terms.HomeReference, Role = terms.Role,
                Coverage = terms.Coverage, RatePer24GameHours = terms.RatePer24GameHours,
                StandingRenewalPermission = terms.StandingRenewalPermission,
                RiskScope = terms.RiskScope, RestTreatment = terms.RestTreatment,
                ResupplyResponsibility = terms.ResupplyResponsibility,
                Status = NpcServiceContractStatus.Reserved, Revision = 1, AcceptedAt = receipt.GameTime,
                BlockStartAt = -1, BlockEndAt = -1, LastServiceUpdateAt = -1, SettlementRecordedAt = -1,
                FundedGold = receipt.Amount, EscrowGold = receipt.Amount
            };
            foreach (var target in terms.ProtectedTargets) value.protectedTargets.Add(target.CopyValidated());
            foreach (var obligation in terms.ReservedObligations) value.reservedObligations.Add(obligation.CopyValidated());
            value.receipts.Add(receipt); reserved = value; return true;
        }

        public bool TryStart(long expectedRevision, double actualServiceStartAt, out NpcServiceContract updated)
        {
            updated = null;
            if (Status != NpcServiceContractStatus.Reserved || expectedRevision != Revision || Revision == long.MaxValue ||
                !Finite(actualServiceStartAt) || actualServiceStartAt < AcceptedAt ||
                actualServiceStartAt > double.MaxValue - BillingPeriodSeconds || EscrowGold < RatePer24GameHours ||
                HasAdmissionConflict(actualServiceStartAt, actualServiceStartAt + BillingPeriodSeconds)) return false;
            var copy = Copy(); copy.Status = NpcServiceContractStatus.Active;
            copy.BlockStartAt = actualServiceStartAt; copy.BlockEndAt = actualServiceStartAt + BillingPeriodSeconds;
            // A started 24-hour block earns its agreed fee once. Calendar midnight,
            // pauses and early dismissal do not create another charge or a partial refund.
            copy.CurrentBlockEarnedGold = copy.RatePer24GameHours;
            copy.EarnedGold = copy.FundedGold;
            copy.LastServiceUpdateAt = actualServiceStartAt; copy.Revision++; updated = copy; return true;
        }

        public bool TryAccrue(long expectedRevision, double now, out NpcServiceContract updated)
        {
            updated = null;
            if (Status != NpcServiceContractStatus.Active || expectedRevision != Revision || Revision == long.MaxValue ||
                !Finite(now) || now < LastServiceUpdateAt || now > BlockEndAt) return false;
            updated = AccrueCandidate(now); return true;
        }

        public bool TryReleaseEarnings(long expectedRevision, double now,
            NpcServiceTransferReceipt earningsReceipt, out NpcServiceContract updated)
        {
            updated = null;
            if (Status != NpcServiceContractStatus.Active || expectedRevision != Revision || Revision == long.MaxValue ||
                !Finite(now) || now < LastServiceUpdateAt || now > BlockEndAt) return false;
            var candidate = AccrueCandidate(now);
            int amount = candidate.EarnedGold - candidate.ReleasedGold;
            if (!ValidateTransfer(earningsReceipt, NpcServiceTransferKind.Earnings, EscrowOwner(ContractId), NpcId, amount, now)) return false;
            if (amount > 0)
            {
                candidate.AddReceipt(earningsReceipt); candidate.ReleasedGold += amount; candidate.EscrowGold -= amount;
                candidate.CurrentBlockReleasedGold += amount;
            }
            candidate.Revision++; updated = candidate; return true;
        }

        public bool TrySuspend(long expectedRevision, double now, out NpcServiceContract updated)
        {
            updated = null;
            if (Status != NpcServiceContractStatus.Active || expectedRevision != Revision || Revision == long.MaxValue ||
                !Finite(now) || now < LastServiceUpdateAt || now > BlockEndAt) return false;
            var copy = AccrueCandidate(now); copy.Status = NpcServiceContractStatus.Suspended; copy.Revision++;
            updated = copy; return true;
        }

        public bool TrySuspendForHealthCare(long expectedRevision, double now, string careEpisodeId,
            out NpcServiceContract updated)
        {
            updated = null;
            if (!NpcHealthCare.ValidEpisodeId(careEpisodeId) || !TrySuspend(expectedRevision, now, out var paused)) return false;
            paused.SuspensionOwnerKind = NpcServiceSuspensionOwnerKind.HealthCare;
            paused.SuspensionOwnerId = careEpisodeId;
            updated = paused; return true;
        }

        public bool TryResume(long expectedRevision, double now, out NpcServiceContract updated)
        {
            updated = null;
            if (Status != NpcServiceContractStatus.Suspended || SuspensionOwnerKind == NpcServiceSuspensionOwnerKind.HealthCare ||
                expectedRevision != Revision || Revision == long.MaxValue ||
                !Finite(now) || now < LastServiceUpdateAt || now >= BlockEndAt) return false;
            var copy = Copy(); copy.Status = NpcServiceContractStatus.Active; copy.LastServiceUpdateAt = now;
            copy.SuspensionOwnerKind = NpcServiceSuspensionOwnerKind.None; copy.SuspensionOwnerId = null;
            copy.Revision++; updated = copy; return true;
        }

        public bool TryResumeFromHealthCare(long expectedRevision, double now, string careEpisodeId,
            out NpcServiceContract updated)
        {
            updated = null;
            if (Status != NpcServiceContractStatus.Suspended ||
                SuspensionOwnerKind != NpcServiceSuspensionOwnerKind.HealthCare ||
                !NpcHealthCare.ValidEpisodeId(careEpisodeId) || SuspensionOwnerId != careEpisodeId ||
                expectedRevision != Revision || Revision == long.MaxValue || !Finite(now) ||
                now < LastServiceUpdateAt || now >= BlockEndAt) return false;
            var copy = Copy(); copy.Status = NpcServiceContractStatus.Active; copy.LastServiceUpdateAt = now;
            copy.SuspensionOwnerKind = NpcServiceSuspensionOwnerKind.None; copy.SuspensionOwnerId = null;
            copy.Revision++; updated = copy; return true;
        }

        /// <summary>Moves an existing service pause to a later care episode without changing the daily fee or moving gold.</summary>
        public bool TryTransferHealthCareSuspensionOwner(long expectedRevision,
            string expectedOldCareEpisodeId, string newCareEpisodeId, out NpcServiceContract updated)
        {
            updated = null;
            if (Status != NpcServiceContractStatus.Suspended ||
                SuspensionOwnerKind != NpcServiceSuspensionOwnerKind.HealthCare ||
                expectedRevision != Revision || Revision == long.MaxValue ||
                !NpcHealthCare.ValidEpisodeId(expectedOldCareEpisodeId) ||
                !NpcHealthCare.ValidEpisodeId(newCareEpisodeId) ||
                expectedOldCareEpisodeId == newCareEpisodeId ||
                SuspensionOwnerId != expectedOldCareEpisodeId) return false;
            var copy = Copy();
            copy.SuspensionOwnerId = newCareEpisodeId;
            copy.Revision++;
            updated = copy;
            return true;
        }

        public bool TrySetRenewalPermission(long expectedRevision, NpcServiceRenewalAuthorization authorization,
            out NpcServiceContract updated)
        {
            updated = null;
            if (authorization == null || expectedRevision != Revision || Revision == long.MaxValue ||
                authorization.ContractId != ContractId || authorization.PayerId != PayerId ||
                authorization.Permission == StandingRenewalPermission ||
                Status == NpcServiceContractStatus.Terminated || authorization.GameTime < AcceptedAt ||
                Status != NpcServiceContractStatus.Reserved && authorization.GameTime < LastServiceUpdateAt ||
                HasReceipt(authorization.ReceiptId)) return false;
            var copy = Copy(); copy.StandingRenewalPermission = authorization.Permission;
            // Authorization receipt shares the idempotency namespace but moves no currency.
            copy.authorizationReceiptIds.Add(authorization.ReceiptId); copy.Revision++; updated = copy; return true;
        }

        public bool TryRenew(long expectedRevision, double boundaryAt, NpcServiceTransferReceipt funding,
            NpcServiceTransferReceipt priorEarnings, NpcServiceTransferReceipt priorRefund,
            out NpcServiceContract updated)
        {
            updated = null;
            if (Status != NpcServiceContractStatus.Active || !StandingRenewalPermission || expectedRevision != Revision ||
                Revision == long.MaxValue || !Finite(boundaryAt) || boundaryAt != BlockEndAt ||
                LastServiceUpdateAt > boundaryAt || boundaryAt > double.MaxValue - BillingPeriodSeconds ||
                HasAdmissionConflict(boundaryAt, boundaryAt + BillingPeriodSeconds)) return false;
            var closed = AccrueCandidate(boundaryAt);
            int earnedDue = closed.EarnedGold - closed.ReleasedGold;
            int refundDue = closed.EscrowGold - earnedDue;
            if (!ValidateTransfer(priorEarnings, NpcServiceTransferKind.Earnings, EscrowOwner(ContractId), NpcId, earnedDue, boundaryAt) ||
                !ValidateTransfer(priorRefund, NpcServiceTransferKind.Refund, EscrowOwner(ContractId), PayerId, refundDue, boundaryAt) ||
                !ValidateTransfer(funding, NpcServiceTransferKind.Funding, PayerId, EscrowOwner(ContractId), RatePer24GameHours, boundaryAt))
                return false;
            if (earnedDue > 0) { closed.AddReceipt(priorEarnings); closed.ReleasedGold += earnedDue; closed.CurrentBlockReleasedGold += earnedDue; closed.EscrowGold -= earnedDue; }
            if (refundDue > 0) { closed.AddReceipt(priorRefund); closed.RefundedGold += refundDue; closed.EscrowGold -= refundDue; }
            if (closed.EscrowGold != 0 || closed.FundedGold > int.MaxValue - RatePer24GameHours) return false;
            closed.AddReceipt(funding); closed.FundedGold += RatePer24GameHours; closed.EscrowGold = RatePer24GameHours;
            closed.CurrentBlockEligibleSeconds = 0; closed.CurrentBlockEarnedGold = RatePer24GameHours;
            closed.EarnedGold = closed.FundedGold;
            closed.CurrentBlockReleasedGold = 0; closed.BlockStartAt = boundaryAt;
            closed.BlockEndAt = boundaryAt + BillingPeriodSeconds; closed.LastServiceUpdateAt = boundaryAt;
            if (!Finite(closed.BlockEndAt)) return false;
            closed.Revision++; updated = closed; return true;
        }

        /// <param name="settlementRecipientId">For death, the caller must supply the native persistent corpse/estate owner ID.</param>
        public bool TrySettle(long expectedRevision, double now, NpcServiceEndReason reason,
            string settlementRecipientId, NpcServiceTransferReceipt earningsReceipt,
            NpcServiceTransferReceipt refundReceipt, out NpcServiceContract updated)
        {
            updated = null;
            if (Status == NpcServiceContractStatus.Terminated || expectedRevision != Revision || Revision == long.MaxValue ||
                !Enum.IsDefined(typeof(NpcServiceEndReason), reason) || !Finite(now) || now < AcceptedAt ||
                !ValidEndPoint(now, reason) || Status != NpcServiceContractStatus.Reserved && now < LastServiceUpdateAt) return false;
            if (Status == NpcServiceContractStatus.Reserved && reason != NpcServiceEndReason.CancelledBeforeService ||
                Status != NpcServiceContractStatus.Reserved && reason == NpcServiceEndReason.CancelledBeforeService) return false;
            bool lateExpiry = Status != NpcServiceContractStatus.Reserved &&
                reason == NpcServiceEndReason.NaturalExpiry && now > BlockEndAt;
            var candidate = Status == NpcServiceContractStatus.Active
                ? AccrueCandidate(lateExpiry ? BlockEndAt : now) : Copy();
            int earnedDue = candidate.EarnedGold - candidate.ReleasedGold;
            int refundDue = candidate.EscrowGold - earnedDue;
            string recipient = reason == NpcServiceEndReason.Death ? settlementRecipientId : NpcId;
            if (!ValidId(recipient, 240) || reason == NpcServiceEndReason.Death &&
                string.IsNullOrWhiteSpace(recipient) || reason != NpcServiceEndReason.Death && settlementRecipientId != NpcId ||
                !ValidateTransfer(earningsReceipt, NpcServiceTransferKind.Earnings, EscrowOwner(ContractId), recipient, earnedDue, now) ||
                !ValidateTransfer(refundReceipt, NpcServiceTransferKind.Refund, EscrowOwner(ContractId), PayerId, refundDue, now)) return false;
            if (earnedDue > 0) { candidate.AddReceipt(earningsReceipt); candidate.ReleasedGold += earnedDue; candidate.CurrentBlockReleasedGold += earnedDue; candidate.EscrowGold -= earnedDue; }
            if (refundDue > 0) { candidate.AddReceipt(refundReceipt); candidate.RefundedGold += refundDue; candidate.EscrowGold -= refundDue; }
            if (candidate.EscrowGold != 0) return false;
            candidate.Status = NpcServiceContractStatus.Terminated; candidate.EndReason = reason;
            candidate.SuspensionOwnerKind = NpcServiceSuspensionOwnerKind.None;
            candidate.SuspensionOwnerId = null;
            if (!lateExpiry) candidate.LastServiceUpdateAt = now;
            candidate.SettlementRecordedAt = now;
            candidate.Revision++; updated = candidate; return true;
        }

        private bool ValidEndPoint(double now, NpcServiceEndReason reason)
        {
            if (Status == NpcServiceContractStatus.Reserved) return now >= AcceptedAt;
            if (Status == NpcServiceContractStatus.Terminated) return false;
            if (reason == NpcServiceEndReason.NaturalExpiry) return now >= BlockEndAt;
            if (now > BlockEndAt) return false;
            return reason == NpcServiceEndReason.RenewalDeclined
                ? now == BlockEndAt : now <= BlockEndAt;
        }

        private NpcServiceContract AccrueCandidate(double now)
        {
            var copy = Copy();
            double through = Math.Min(now, copy.BlockEndAt);
            if (copy.Status == NpcServiceContractStatus.Active)
            {
                double elapsed = through - copy.LastServiceUpdateAt;
                if (elapsed > 0)
                {
                    copy.CurrentBlockEligibleSeconds = Math.Min(BillingPeriodSeconds, copy.CurrentBlockEligibleSeconds + elapsed);
                    copy.EligibleServiceSeconds += elapsed;
                }
            }
            copy.LastServiceUpdateAt = through;
            return copy;
        }

        private bool ValidateTransfer(NpcServiceTransferReceipt receipt, NpcServiceTransferKind kind,
            string from, string to, int amount, double at)
        {
            if (amount == 0) return receipt == null;
            if (receipt == null || HasReceipt(receipt.ReceiptId)) return false;
            NpcServiceTransferReceipt copy;
            try { copy = NpcServiceTransferReceipt.CopyValidated(receipt); }
            catch (ArgumentException) { return false; }
            return copy.ContractId == ContractId && copy.Kind == kind && copy.FromOwnerId == from &&
                copy.ToOwnerId == to && copy.Amount == amount && copy.GameTime == at;
        }

        private bool HasReceipt(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            for (int i = 0; i < receipts.Count; i++) if (receipts[i].ReceiptId == id) return true;
            return authorizationReceiptIds.Contains(id);
        }

        public bool IsProtectedActor(string actorId)
        {
            if (string.IsNullOrWhiteSpace(actorId)) return false;
            foreach (var target in protectedTargets) if (target.ActorId == actorId) return true;
            return false;
        }

        public bool HasAdmissionConflict(double startAt, double endAt)
        {
            foreach (var obligation in reservedObligations)
                if (obligation.Treatment == NpcServiceObligationTreatment.AdmissionConstraint &&
                    obligation.StartsAt < endAt && startAt < obligation.EndsAt) return true;
            return false;
        }

        internal bool HasAuthorizationReceipt(string id) { return authorizationReceiptIds.Contains(id); }
        internal IReadOnlyList<string> ReadAuthorizationReceiptIds() { return authorizationReceiptIds.AsReadOnly(); }
        internal int AuthorizationReceiptIdsCount { get { return authorizationReceiptIds.Count; } }
        internal string ReadAuthorizationReceiptId(int index) { return authorizationReceiptIds[index]; }

        internal bool HasSameSavedState(NpcServiceContract other)
        {
            if (other == null || Version != other.Version || ContractId != other.ContractId || PayerId != other.PayerId ||
                NpcId != other.NpcId || HomeReference != other.HomeReference || Role != other.Role || Coverage != other.Coverage ||
                RatePer24GameHours != other.RatePer24GameHours || StandingRenewalPermission != other.StandingRenewalPermission ||
                RiskScope != other.RiskScope || RestTreatment != other.RestTreatment ||
                ResupplyResponsibility != other.ResupplyResponsibility || Status != other.Status || EndReason != other.EndReason ||
                SuspensionOwnerKind != other.SuspensionOwnerKind || SuspensionOwnerId != other.SuspensionOwnerId ||
                Revision != other.Revision || AcceptedAt != other.AcceptedAt || BlockStartAt != other.BlockStartAt ||
                BlockEndAt != other.BlockEndAt || LastServiceUpdateAt != other.LastServiceUpdateAt ||
                SettlementRecordedAt != other.SettlementRecordedAt ||
                EligibleServiceSeconds != other.EligibleServiceSeconds || CurrentBlockEligibleSeconds != other.CurrentBlockEligibleSeconds ||
                CurrentBlockEarnedGold != other.CurrentBlockEarnedGold || CurrentBlockReleasedGold != other.CurrentBlockReleasedGold ||
                FundedGold != other.FundedGold || EscrowGold != other.EscrowGold || EarnedGold != other.EarnedGold ||
                ReleasedGold != other.ReleasedGold || RefundedGold != other.RefundedGold || receipts.Count != other.receipts.Count ||
                authorizationReceiptIds.Count != other.authorizationReceiptIds.Count || protectedTargets.Count != other.protectedTargets.Count ||
                reservedObligations.Count != other.reservedObligations.Count) return false;
            for (int i = 0; i < receipts.Count; i++) if (!SameReceipt(receipts[i], other.receipts[i])) return false;
            for (int i = 0; i < authorizationReceiptIds.Count; i++)
                if (authorizationReceiptIds[i] != other.authorizationReceiptIds[i]) return false;
            for (int i = 0; i < protectedTargets.Count; i++)
                if (protectedTargets[i].ActorId != other.protectedTargets[i].ActorId ||
                    protectedTargets[i].Category != other.protectedTargets[i].Category ||
                    protectedTargets[i].SourceRevision != other.protectedTargets[i].SourceRevision) return false;
            for (int i = 0; i < reservedObligations.Count; i++)
                if (!SameObligation(reservedObligations[i], other.reservedObligations[i])) return false;
            return true;
        }

        internal bool IsDirectlyDerivedFrom(NpcServiceContract source)
        { return ReferenceEquals(derivedFrom, source); }

        internal bool HasSameImmutableTerms(NpcServiceContract other)
        {
            if (other == null || ContractId != other.ContractId || PayerId != other.PayerId || NpcId != other.NpcId ||
                HomeReference != other.HomeReference || Role != other.Role || Coverage != other.Coverage ||
                RatePer24GameHours != other.RatePer24GameHours || RiskScope != other.RiskScope ||
                RestTreatment != other.RestTreatment || ResupplyResponsibility != other.ResupplyResponsibility ||
                protectedTargets.Count != other.protectedTargets.Count || reservedObligations.Count != other.reservedObligations.Count) return false;
            for (int i = 0; i < protectedTargets.Count; i++)
                if (protectedTargets[i].ActorId != other.protectedTargets[i].ActorId ||
                    protectedTargets[i].Category != other.protectedTargets[i].Category ||
                    protectedTargets[i].SourceRevision != other.protectedTargets[i].SourceRevision) return false;
            for (int i = 0; i < reservedObligations.Count; i++)
                if (!SameObligation(reservedObligations[i], other.reservedObligations[i])) return false;
            return true;
        }

        internal static bool SameReceipt(NpcServiceTransferReceipt first, NpcServiceTransferReceipt second)
        {
            return first != null && second != null && first.ReceiptId == second.ReceiptId && first.ContractId == second.ContractId &&
                first.Kind == second.Kind && first.FromOwnerId == second.FromOwnerId && first.ToOwnerId == second.ToOwnerId &&
                first.Amount == second.Amount && first.GameTime == second.GameTime;
        }

        internal static bool SameObligation(NpcServiceReservedObligation first, NpcServiceReservedObligation second)
        {
            return first != null && second != null && first.ObligationId == second.ObligationId &&
                first.SourceRevision == second.SourceRevision && first.Kind == second.Kind && first.Treatment == second.Treatment &&
                first.StartsAt == second.StartsAt && first.EndsAt == second.EndsAt;
        }

        internal NpcServiceContract CopyValidated()
        {
            if (Version != VersionCurrent || !ValidId(ContractId, 128) || !ValidId(PayerId, 240) ||
                !ValidId(NpcId, 240) || !ValidId(HomeReference, 240) || PayerId == NpcId ||
                !Enum.IsDefined(typeof(NpcServiceRole), Role) || !Enum.IsDefined(typeof(NpcServiceContractStatus), Status) ||
                (((int)Coverage & ~((int)NpcServiceCoverage.Follow | (int)NpcServiceCoverage.Combat |
                    (int)NpcServiceCoverage.Travel | (int)NpcServiceCoverage.Rest |
                    (int)NpcServiceCoverage.Resupply | (int)NpcServiceCoverage.Expedition)) != 0) ||
                (((int)RiskScope & ~((int)NpcServiceRiskScope.OrdinaryCombat | (int)NpcServiceRiskScope.BoundedExpedition)) != 0) ||
                ((Coverage & NpcServiceCoverage.Combat) != 0) != ((RiskScope & NpcServiceRiskScope.OrdinaryCombat) != 0) ||
                ((Coverage & NpcServiceCoverage.Expedition) != 0) != ((RiskScope & NpcServiceRiskScope.BoundedExpedition) != 0) ||
                !Enum.IsDefined(typeof(NpcServiceRestTreatment), RestTreatment) ||
                !Enum.IsDefined(typeof(NpcServiceResupplyResponsibility), ResupplyResponsibility) ||
                (((Coverage & NpcServiceCoverage.Rest) != 0) != (RestTreatment != NpcServiceRestTreatment.RestNotIncluded)) ||
                (((Coverage & NpcServiceCoverage.Resupply) != 0) !=
                    (ResupplyResponsibility != NpcServiceResupplyResponsibility.SeparatelyNegotiated)) ||
                Coverage == NpcServiceCoverage.None || RatePer24GameHours <= 0 || Revision <= 0 ||
                !Finite(AcceptedAt) || AcceptedAt < 0 || FundedGold < 0 || EscrowGold < 0 ||
                EarnedGold < 0 || EarnedGold > FundedGold || ReleasedGold < 0 || RefundedGold < 0 || ReleasedGold > EarnedGold ||
                (long)ReleasedGold + RefundedGold + EscrowGold != FundedGold ||
                receipts.Count > 4096 || authorizationReceiptIds.Count > 4096 ||
                protectedTargets.Count > 64 || reservedObligations.Count > 64 ||
                !Finite(EligibleServiceSeconds) || EligibleServiceSeconds < 0 ||
                !Finite(CurrentBlockEligibleSeconds) || CurrentBlockEligibleSeconds < 0 ||
                CurrentBlockEligibleSeconds > BillingPeriodSeconds || CurrentBlockEarnedGold < 0 ||
                CurrentBlockReleasedGold < 0 || CurrentBlockReleasedGold > CurrentBlockEarnedGold ||
                EarnedGold < CurrentBlockEarnedGold || ReleasedGold < CurrentBlockReleasedGold ||
                CurrentBlockEarnedGold != (BlockStartAt < 0 ? 0 : RatePer24GameHours) ||
                BlockStartAt >= 0 && (EarnedGold != FundedGold || RefundedGold != 0))
                throw new ArgumentException("Invalid service contract state.");
            if (!Enum.IsDefined(typeof(NpcServiceSuspensionOwnerKind), SuspensionOwnerKind) ||
                (Status != NpcServiceContractStatus.Suspended &&
                    (SuspensionOwnerKind != NpcServiceSuspensionOwnerKind.None || SuspensionOwnerId != null)) ||
                (Status == NpcServiceContractStatus.Suspended &&
                    (SuspensionOwnerKind == NpcServiceSuspensionOwnerKind.HealthCare
                        ? !NpcHealthCare.ValidEpisodeId(SuspensionOwnerId)
                        : SuspensionOwnerId != null)) ||
                (Status == NpcServiceContractStatus.Terminated
                    ? !Finite(SettlementRecordedAt) || SettlementRecordedAt < AcceptedAt ||
                        SettlementRecordedAt < LastServiceUpdateAt
                    : SettlementRecordedAt != -1))
                throw new ArgumentException("Invalid service suspension owner or settlement time.");
            bool cancelledBeforeStart = Status == NpcServiceContractStatus.Terminated &&
                EndReason == NpcServiceEndReason.CancelledBeforeService;
            if (Status == NpcServiceContractStatus.Reserved || cancelledBeforeStart)
            {
                if (BlockStartAt != -1 || BlockEndAt != -1 ||
                    (cancelledBeforeStart ? !Finite(LastServiceUpdateAt) || LastServiceUpdateAt < AcceptedAt : LastServiceUpdateAt != -1) ||
                    EligibleServiceSeconds != 0 || EarnedGold != 0 || ReleasedGold != 0 ||
                    (cancelledBeforeStart ? RefundedGold != FundedGold || EscrowGold != 0 : RefundedGold != 0 || EndReason.HasValue || EscrowGold != FundedGold))
                    throw new ArgumentException("Invalid pre-service contract state.");
            }
            else if (BlockStartAt < AcceptedAt || !Finite(BlockStartAt) || !Finite(BlockEndAt) ||
                BlockEndAt != BlockStartAt + BillingPeriodSeconds || !Finite(LastServiceUpdateAt) ||
                LastServiceUpdateAt < BlockStartAt || LastServiceUpdateAt > BlockEndAt ||
                EndReason.HasValue != (Status == NpcServiceContractStatus.Terminated) ||
                EndReason.HasValue && !Enum.IsDefined(typeof(NpcServiceEndReason), EndReason.Value) ||
                Status == NpcServiceContractStatus.Terminated && EscrowGold != 0)
                throw new ArgumentException("Invalid started service contract state.");
            if (Status == NpcServiceContractStatus.Terminated && !cancelledBeforeStart &&
                (EndReason == NpcServiceEndReason.NaturalExpiry && SettlementRecordedAt < BlockEndAt ||
                 EndReason == NpcServiceEndReason.RenewalDeclined && SettlementRecordedAt != BlockEndAt ||
                 EndReason != NpcServiceEndReason.NaturalExpiry && EndReason != NpcServiceEndReason.RenewalDeclined &&
                    SettlementRecordedAt > BlockEndAt))
                throw new ArgumentException("Service end reason conflicts with actual settlement time.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var receipt in receipts)
            {
                if (!ids.Add(receipt.ReceiptId) || receipt.ContractId != ContractId)
                    throw new ArgumentException("Invalid or duplicate service transfer receipt.");
                NpcServiceTransferReceipt.CopyValidated(receipt);
            }
            foreach (var id in authorizationReceiptIds)
                if (!ValidId(id, 240) || !ids.Add(id)) throw new ArgumentException("Invalid or duplicate service authorization receipt.");
            var protectedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var target in protectedTargets)
                if (target == null || target.ActorId == NpcId || !protectedIds.Add(target.ActorId))
                    throw new ArgumentException("Invalid or duplicate protected service actor.");
                else target.CopyValidated();
            var obligationIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var obligation in reservedObligations)
                if (obligation == null || !obligationIds.Add(obligation.ObligationId))
                    throw new ArgumentException("Invalid or duplicate reserved service obligation.");
                else obligation.CopyValidated();
            return Copy();
        }

        internal NpcServiceContractState ToState()
        {
            var state = new NpcServiceContractState
            {
                Version = NpcServiceContractState.VersionCurrent, ContractId = ContractId, PayerId = PayerId,
                NpcId = NpcId, HomeReference = HomeReference, Role = Role, Coverage = Coverage,
                RatePer24GameHours = RatePer24GameHours, StandingRenewalPermission = StandingRenewalPermission,
                RiskScope = RiskScope, RestTreatment = RestTreatment, ResupplyResponsibility = ResupplyResponsibility,
                Status = Status, SuspensionOwnerKind = SuspensionOwnerKind, SuspensionOwnerId = SuspensionOwnerId,
                HasEndReason = EndReason.HasValue, EndReason = EndReason.GetValueOrDefault(),
                Revision = Revision, AcceptedAt = AcceptedAt, BlockStartAt = BlockStartAt, BlockEndAt = BlockEndAt,
                LastServiceUpdateAt = LastServiceUpdateAt, SettlementRecordedAt = SettlementRecordedAt,
                EligibleServiceSeconds = EligibleServiceSeconds,
                CurrentBlockEligibleSeconds = CurrentBlockEligibleSeconds, CurrentBlockEarnedGold = CurrentBlockEarnedGold,
                CurrentBlockReleasedGold = CurrentBlockReleasedGold, FundedGold = FundedGold, EscrowGold = EscrowGold,
                EarnedGold = EarnedGold, ReleasedGold = ReleasedGold, RefundedGold = RefundedGold
            };
            state.AuthorizationReceiptIds.AddRange(authorizationReceiptIds);
            foreach (var receipt in receipts)
                state.TransferReceipts.Add(new NpcServiceTransferReceiptState { ReceiptId = receipt.ReceiptId,
                    ContractId = receipt.ContractId, Kind = receipt.Kind, FromOwnerId = receipt.FromOwnerId,
                    ToOwnerId = receipt.ToOwnerId, Amount = receipt.Amount, GameTime = receipt.GameTime });
            foreach (var target in protectedTargets)
                state.ProtectedTargets.Add(new NpcServiceProtectedTargetState { ActorId = target.ActorId,
                    Category = target.Category, SourceRevision = target.SourceRevision });
            foreach (var obligation in reservedObligations)
                state.ReservedObligations.Add(new NpcServiceReservedObligationState { ObligationId = obligation.ObligationId,
                    SourceRevision = obligation.SourceRevision, Kind = obligation.Kind, Treatment = obligation.Treatment,
                    StartsAt = obligation.StartsAt, EndsAt = obligation.EndsAt });
            return state;
        }

        internal static bool TryFromState(NpcServiceContractState state,
            Func<string, bool> knownActor, Func<string, bool> knownHome,
            Func<string, NpcServiceProtectedTargetCategory, long, bool> knownProtectedTarget,
            Func<string, long, NpcServiceReservedObligationKind, double, double, bool> knownObligation,
            Func<NpcServiceTransferReceiptState, bool> nativeTransferExists,
            Func<string, string, bool> nativeAuthorizationExists,
            out NpcServiceContract restored)
        {
            return TryFromStateCore(state, knownActor, knownHome, knownProtectedTarget, knownObligation,
                nativeTransferExists, nativeAuthorizationExists, null, true, out restored);
        }

        internal static bool TryFromHistoricalState(NpcServiceContractState state,
            Func<string, bool> knownActor, NpcServiceNativeEvidenceState evidence,
            out NpcServiceContract restored)
        {
            return TryFromStateCore(state, knownActor, null, null, null, null, null, evidence, false, out restored);
        }

        private static bool TryFromStateCore(NpcServiceContractState state,
            Func<string, bool> knownActor, Func<string, bool> knownHome,
            Func<string, NpcServiceProtectedTargetCategory, long, bool> knownProtectedTarget,
            Func<string, long, NpcServiceReservedObligationKind, double, double, bool> knownObligation,
            Func<NpcServiceTransferReceiptState, bool> nativeTransferExists,
            Func<string, string, bool> nativeAuthorizationExists,
            NpcServiceNativeEvidenceState evidence, bool requireCurrentReferences,
            out NpcServiceContract restored)
        {
            restored = null;
            if (state == null || (state.Version != 1 && state.Version != NpcServiceContractState.VersionCurrent) ||
                state.Version == 1 && (state.SuspensionOwnerKind != NpcServiceSuspensionOwnerKind.None ||
                    state.SuspensionOwnerId != null || state.SettlementRecordedAt != -1) ||
                knownActor == null || requireCurrentReferences && (knownHome == null || knownProtectedTarget == null || knownObligation == null ||
                    nativeTransferExists == null || nativeAuthorizationExists == null) ||
                !requireCurrentReferences && evidence == null ||
                (!state.HasEndReason && state.EndReason != default(NpcServiceEndReason)) ||
                (state.HasEndReason && !Enum.IsDefined(typeof(NpcServiceEndReason), state.EndReason)) ||
                state.TransferReceipts == null || state.TransferReceipts.Count > 4096 ||
                state.AuthorizationReceiptIds == null || state.AuthorizationReceiptIds.Count > 4096 ||
                state.ProtectedTargets == null || state.ProtectedTargets.Count > 64 ||
                state.ReservedObligations == null || state.ReservedObligations.Count > 64 ||
                (requireCurrentReferences
                    ? !knownActor(state.PayerId)
                    : state.PayerId != NpcDirectedRelationship.PlayerTargetId && !knownActor(state.PayerId)) ||
                !knownActor(state.NpcId) ||
                requireCurrentReferences && !knownHome(state.HomeReference)) return false;
            try
            {
                var value = new NpcServiceContract
                {
                    Version = VersionCurrent, ContractId = state.ContractId, PayerId = state.PayerId, NpcId = state.NpcId,
                    HomeReference = state.HomeReference, Role = state.Role, Coverage = state.Coverage,
                    RatePer24GameHours = state.RatePer24GameHours, StandingRenewalPermission = state.StandingRenewalPermission,
                    RiskScope = state.RiskScope, RestTreatment = state.RestTreatment,
                    ResupplyResponsibility = state.ResupplyResponsibility, Status = state.Status,
                    SuspensionOwnerKind = state.Version == 1 && state.Status == NpcServiceContractStatus.Suspended
                        ? NpcServiceSuspensionOwnerKind.LegacyUnknown : state.SuspensionOwnerKind,
                    SuspensionOwnerId = state.SuspensionOwnerId,
                    EndReason = state.HasEndReason ? (NpcServiceEndReason?)state.EndReason : null, Revision = state.Revision,
                    AcceptedAt = state.AcceptedAt, BlockStartAt = state.BlockStartAt, BlockEndAt = state.BlockEndAt,
                    LastServiceUpdateAt = state.LastServiceUpdateAt,
                    SettlementRecordedAt = state.Version == 1 && state.Status == NpcServiceContractStatus.Terminated
                        ? state.LastServiceUpdateAt : state.SettlementRecordedAt,
                    EligibleServiceSeconds = state.EligibleServiceSeconds,
                    CurrentBlockEligibleSeconds = state.CurrentBlockEligibleSeconds,
                    CurrentBlockEarnedGold = state.CurrentBlockEarnedGold,
                    CurrentBlockReleasedGold = state.CurrentBlockReleasedGold, FundedGold = state.FundedGold,
                    EscrowGold = state.EscrowGold, EarnedGold = state.EarnedGold,
                    ReleasedGold = state.ReleasedGold, RefundedGold = state.RefundedGold
                };
                foreach (var item in state.TransferReceipts)
                {
                    if (item == null) return false;
                    var receipt = NpcServiceTransferReceipt.Create(item.ReceiptId, item.ContractId, item.Kind,
                        item.FromOwnerId, item.ToOwnerId, item.Amount, item.GameTime);
                    if (requireCurrentReferences ? !nativeTransferExists(item) : !HasExactTransferEvidence(evidence, item)) return false;
                    value.receipts.Add(receipt);
                }
                foreach (var id in state.AuthorizationReceiptIds)
                {
                    if (!ValidId(id, 240) || (requireCurrentReferences
                        ? !nativeAuthorizationExists(state.ContractId, id)
                        : !HasAuthorizationEvidence(evidence, state.ContractId, id))) return false;
                    value.authorizationReceiptIds.Add(id);
                }
                foreach (var item in state.ProtectedTargets)
                {
                    NpcServiceProtectedTarget target;
                    if (item == null || !knownActor(item.ActorId)) return false;
                    if (requireCurrentReferences)
                    {
                        if (!NpcServiceProtectedTarget.TryCreate(item.ActorId, item.Category, item.SourceRevision,
                            knownProtectedTarget, out target)) return false;
                    }
                    else if (!NpcServiceProtectedTarget.TryRestoreShape(item.ActorId, item.Category, item.SourceRevision,
                        out target)) return false;
                    value.protectedTargets.Add(target);
                }
                foreach (var item in state.ReservedObligations)
                {
                    NpcServiceReservedObligation obligation;
                    if (item == null) return false;
                    if (requireCurrentReferences)
                    {
                        if (!NpcServiceReservedObligation.TryCreate(item.ObligationId, item.SourceRevision,
                            item.Kind, item.Treatment, item.StartsAt, item.EndsAt, knownObligation, out obligation)) return false;
                    }
                    else if (!NpcServiceReservedObligation.TryRestoreShape(item.ObligationId, item.SourceRevision,
                        item.Kind, item.Treatment, item.StartsAt, item.EndsAt, out obligation)) return false;
                    value.reservedObligations.Add(obligation);
                }
                var candidate = value.CopyValidated();
                if (!candidate.HasConsistentReceiptAccounting()) return false;
                restored = candidate;
                return true;
            }
            catch (ArgumentException) { return false; }
            catch (OverflowException) { return false; }
        }

        private static bool HasExactTransferEvidence(NpcServiceNativeEvidenceState evidence,
            NpcServiceTransferReceiptState receipt)
        {
            if (evidence.CompletedTransfers == null) return false;
            int matches = 0;
            foreach (var saved in evidence.CompletedTransfers)
                if (saved != null && saved.ReceiptId == receipt.ReceiptId && saved.ContractId == receipt.ContractId &&
                    saved.Kind == receipt.Kind && saved.FromOwnerId == receipt.FromOwnerId &&
                    saved.ToOwnerId == receipt.ToOwnerId && saved.Amount == receipt.Amount &&
                    saved.GameTime == receipt.GameTime) matches++;
            return matches == 1;
        }

        private static bool HasAuthorizationEvidence(NpcServiceNativeEvidenceState evidence, string contractId, string receiptId)
        {
            if (evidence.Authorizations == null) return false;
            int matches = 0;
            foreach (var saved in evidence.Authorizations)
                if (saved != null && saved.ContractId == contractId && saved.ReceiptId == receiptId) matches++;
            return matches == 1;
        }

        internal bool HasConsistentReceiptAccounting()
        {
            long funding = 0, earnings = 0, refunds = 0;
            int fundingCount = 0;
            double priorReceiptTime = AcceptedAt;
            string escrow = EscrowOwner(ContractId);
            for (int i = 0; i < receipts.Count; i++)
            {
                var receipt = receipts[i];
                if (receipt.GameTime < priorReceiptTime || receipt.GameTime < AcceptedAt ||
                    receipt.GameTime > (Status == NpcServiceContractStatus.Reserved ? AcceptedAt :
                        Status == NpcServiceContractStatus.Terminated ? SettlementRecordedAt : LastServiceUpdateAt)) return false;
                priorReceiptTime = receipt.GameTime;
                if (receipt.Kind == NpcServiceTransferKind.Funding)
                {
                    if (receipt.FromOwnerId != PayerId || receipt.ToOwnerId != escrow ||
                        receipt.Amount != RatePer24GameHours || (funding == 0 && receipt.GameTime != AcceptedAt)) return false;
                    fundingCount++;
                    funding += receipt.Amount;
                }
                else if (receipt.Kind == NpcServiceTransferKind.Earnings)
                {
                    if (receipt.FromOwnerId != escrow ||
                        (receipt.ToOwnerId != NpcId && !(Status == NpcServiceContractStatus.Terminated && EndReason == NpcServiceEndReason.Death)))
                        return false;
                    earnings += receipt.Amount;
                }
                else if (receipt.Kind == NpcServiceTransferKind.Refund)
                {
                    if (receipt.FromOwnerId != escrow || receipt.ToOwnerId != PayerId) return false;
                    refunds += receipt.Amount;
                }
                else return false;
            }
            if (fundingCount == 0 || (long)fundingCount * RatePer24GameHours != FundedGold ||
                funding != FundedGold || earnings != ReleasedGold || refunds != RefundedGold) return false;
            if (Status == NpcServiceContractStatus.Reserved ||
                Status == NpcServiceContractStatus.Terminated && EndReason == NpcServiceEndReason.CancelledBeforeService)
                return fundingCount == 1;
            if (BlockStartAt < AcceptedAt + (fundingCount - 1) * BillingPeriodSeconds) return false;
            for (int i = 0; i < receipts.Count; i++)
                if (receipts[i].Kind == NpcServiceTransferKind.Funding && i > 0 &&
                    receipts[i].GameTime != BlockStartAt - (fundingCount - 1 - FundingReceiptOrdinal(i)) * BillingPeriodSeconds)
                    return false;
            return true;
        }

        private int FundingReceiptOrdinal(int receiptIndex)
        {
            int ordinal = -1;
            for (int i = 0; i <= receiptIndex; i++) if (receipts[i].Kind == NpcServiceTransferKind.Funding) ordinal++;
            return ordinal;
        }

        private readonly List<string> authorizationReceiptIds = new List<string>();
        private void AddReceipt(NpcServiceTransferReceipt receipt)
        {
            if (!ValidateTransfer(receipt, receipt.Kind, receipt.FromOwnerId, receipt.ToOwnerId, receipt.Amount, receipt.GameTime))
                throw new ArgumentException("Duplicate or invalid service receipt.", nameof(receipt));
            receipts.Add(NpcServiceTransferReceipt.CopyValidated(receipt));
        }

        private NpcServiceContract Copy()
        {
            var copy = new NpcServiceContract
            {
                Version = Version, ContractId = ContractId, PayerId = PayerId, NpcId = NpcId,
                HomeReference = HomeReference, Role = Role, Coverage = Coverage,
                RatePer24GameHours = RatePer24GameHours, StandingRenewalPermission = StandingRenewalPermission,
                RiskScope = RiskScope, RestTreatment = RestTreatment, ResupplyResponsibility = ResupplyResponsibility,
                Status = Status, SuspensionOwnerKind = SuspensionOwnerKind, SuspensionOwnerId = SuspensionOwnerId,
                EndReason = EndReason, Revision = Revision, AcceptedAt = AcceptedAt,
                BlockStartAt = BlockStartAt, BlockEndAt = BlockEndAt, LastServiceUpdateAt = LastServiceUpdateAt,
                SettlementRecordedAt = SettlementRecordedAt,
                EligibleServiceSeconds = EligibleServiceSeconds, CurrentBlockEligibleSeconds = CurrentBlockEligibleSeconds,
                CurrentBlockEarnedGold = CurrentBlockEarnedGold, CurrentBlockReleasedGold = CurrentBlockReleasedGold,
                FundedGold = FundedGold, EscrowGold = EscrowGold, EarnedGold = EarnedGold,
                ReleasedGold = ReleasedGold, RefundedGold = RefundedGold,
                derivedFrom = this
            };
            foreach (var receipt in receipts) copy.receipts.Add(NpcServiceTransferReceipt.CopyValidated(receipt));
            copy.authorizationReceiptIds.AddRange(authorizationReceiptIds);
            foreach (var target in protectedTargets) copy.protectedTargets.Add(target.CopyValidated());
            foreach (var obligation in reservedObligations) copy.reservedObligations.Add(obligation.CopyValidated());
            return copy;
        }

        private int CurrentBlockEarnedGold { get; set; }
        private int CurrentBlockReleasedGold { get; set; }

        private static string EscrowOwner(string id) { return "contract:" + id + ":escrow"; }
        private static bool ValidId(string value, int max)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > max) return false;
            for (int i = 0; i < value.Length; i++) if (char.IsControl(value[i])) return false;
            return true;
        }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }

    /// <summary>Save-owned service contracts and global receipt-id deduplication boundary.</summary>
    [Serializable]
    public sealed class NpcServiceContractLedger
    {
        public const int VersionCurrent = 1;
        public int Version { get; private set; }
        public long Revision { get; private set; }
        public IReadOnlyList<NpcServiceContract> Contracts { get; private set; }
        private readonly List<NpcServiceContract> contracts;
        [NonSerialized] private NpcServiceContractLedger derivedFrom;

        private NpcServiceContractLedger()
        {
            contracts = new List<NpcServiceContract>();
            Contracts = new ReadOnlyCollection<NpcServiceContract>(contracts);
            Version = VersionCurrent;
        }

        public static NpcServiceContractLedger Create() { return new NpcServiceContractLedger(); }

        /// <summary>True only for a single detached successor constructed from this exact in-memory ledger instance.</summary>
        internal bool IsDirectlyDerivedFrom(NpcServiceContractLedger source)
        { return ReferenceEquals(derivedFrom, source); }

        public NpcServiceContractLedgerState ToSaveState(string saveBranchId)
        {
            if (!ValidId(saveBranchId, 240) || Version != VersionCurrent || Revision < 0 ||
                contracts.Count > 4096 || !ValidUniqueReceiptIds())
                throw new ArgumentException("Invalid service contract ledger; cannot create save state.");
            var state = new NpcServiceContractLedgerState
            { Version = NpcServiceContractLedgerState.VersionCurrent, SaveBranchId = saveBranchId, Revision = Revision };
            foreach (var contract in contracts)
            {
                var validated = contract.CopyValidated();
                if (validated.Revision > Revision || !validated.HasConsistentReceiptAccounting())
                    throw new ArgumentException("Invalid service transfer accounting; cannot create save state.");
                state.Contracts.Add(validated.ToState());
            }
            return state;
        }

        public static bool TryRestore(NpcServiceContractLedgerState state, string expectedSaveBranchId,
            Func<string, bool> knownActor, Func<string, bool> knownHome,
            Func<string, NpcServiceProtectedTargetCategory, long, bool> knownProtectedTarget,
            Func<string, long, NpcServiceReservedObligationKind, double, double, bool> knownObligation,
            Func<NpcServiceTransferReceiptState, bool> nativeTransferExists,
            Func<string, string, bool> nativeAuthorizationExists,
            out NpcServiceContractLedger restored)
        {
            restored = null;
            if (state == null || state.Version != NpcServiceContractLedgerState.VersionCurrent ||
                !ValidId(expectedSaveBranchId, 240) || state.SaveBranchId != expectedSaveBranchId ||
                state.Revision < 0 || state.Contracts == null || state.Contracts.Count > 4096 ||
                knownActor == null || knownHome == null || knownProtectedTarget == null || knownObligation == null ||
                nativeTransferExists == null || nativeAuthorizationExists == null) return false;
            var candidate = new NpcServiceContractLedger { Revision = state.Revision };
            try
            {
                foreach (var contractState in state.Contracts)
                {
                    NpcServiceContract contract;
                    if (!NpcServiceContract.TryFromState(contractState, knownActor, knownHome,
                        knownProtectedTarget, knownObligation, nativeTransferExists, nativeAuthorizationExists, out contract))
                        return false;
                    if (contract.Revision > state.Revision || candidate.Read(contract.ContractId) != null) return false;
                    candidate.contracts.Add(contract);
                }
                if (!candidate.ValidUniqueReceiptIds()) return false;
                restored = candidate;
                return true;
            }
            catch (ArgumentException) { return false; }
            catch (OverflowException) { return false; }
        }

        /// <summary>
        /// Restores historical save data using the save-owned native evidence registry. Current home,
        /// protected-target and obligation authority is deliberately not rechecked for old contracts.
        /// This does not authorize new service; admission and updates must use current native evidence.
        /// </summary>
        internal static bool TryRestoreHistorical(NpcServiceContractLedgerState state, string expectedSaveScopeId,
            Func<string, bool> knownActor, NpcServiceNativeEvidenceState evidence,
            out NpcServiceContractLedger restored)
        {
            restored = null;
            if (state == null || state.Version != NpcServiceContractLedgerState.VersionCurrent ||
                !ValidId(expectedSaveScopeId, 240) || state.SaveBranchId != expectedSaveScopeId ||
                state.Revision < 0 || state.Contracts == null || state.Contracts.Count > 4096 ||
                knownActor == null || !ValidEvidenceRegistry(evidence)) return false;
            var candidate = new NpcServiceContractLedger { Revision = state.Revision };
            try
            {
                foreach (var contractState in state.Contracts)
                {
                    NpcServiceContract contract;
                    if (!NpcServiceContract.TryFromHistoricalState(contractState, knownActor, evidence, out contract) ||
                        contract.Revision > state.Revision || candidate.Read(contract.ContractId) != null) return false;
                    candidate.contracts.Add(contract);
                }
                if (!candidate.ValidUniqueReceiptIds() || !EvidenceRegistryExactlyReferenced(candidate, evidence)) return false;
                restored = candidate;
                return true;
            }
            catch (ArgumentException) { return false; }
            catch (OverflowException) { return false; }
        }

        internal static bool ValidEvidenceRegistry(NpcServiceNativeEvidenceState evidence)
        {
            if (evidence == null || evidence.Version != NpcServiceNativeEvidenceState.VersionCurrent || evidence.Revision < 0 ||
                evidence.CompletedTransfers == null || evidence.Authorizations == null ||
                evidence.CompletedTransfers.Count > 16384 || evidence.Authorizations.Count > 16384 ||
                evidence.Revision != (long)evidence.CompletedTransfers.Count + evidence.Authorizations.Count) return false;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                foreach (var item in evidence.CompletedTransfers)
                {
                    if (item == null || !ids.Add(item.ReceiptId)) return false;
                    NpcServiceTransferReceipt.Create(item.ReceiptId, item.ContractId, item.Kind,
                        item.FromOwnerId, item.ToOwnerId, item.Amount, item.GameTime);
                }
                foreach (var item in evidence.Authorizations)
                {
                    if (item == null || !ids.Add(item.ReceiptId)) return false;
                    NpcServiceRenewalAuthorization.Create(item.ReceiptId, item.ContractId, item.PayerId,
                        item.Permission, item.GameTime);
                }
                return true;
            }
            catch (ArgumentException) { return false; }
        }

        private static bool EvidenceRegistryExactlyReferenced(NpcServiceContractLedger ledger,
            NpcServiceNativeEvidenceState evidence)
        {
            var referenced = new HashSet<string>(StringComparer.Ordinal);
            foreach (var contract in ledger.Contracts)
            {
                foreach (var receipt in contract.TransferReceipts)
                    if (!referenced.Add(receipt.ReceiptId)) return false;
                var authIds = contract.ReadAuthorizationReceiptIds();
                double previousAuthorizationAt = contract.AcceptedAt;
                bool hasAuthorization = false;
                bool lastPermission = false;
                foreach (var id in authIds)
                {
                    if (!referenced.Add(id)) return false;
                    NpcServiceAuthorizationReceiptState match = null;
                    int matches = 0;
                    foreach (var item in evidence.Authorizations)
                        if (item.ContractId == contract.ContractId && item.ReceiptId == id)
                        { match = item; matches++; }
                    if (matches != 1 || match.PayerId != contract.PayerId || match.GameTime < previousAuthorizationAt ||
                        contract.Status != NpcServiceContractStatus.Reserved && match.GameTime > contract.LastServiceUpdateAt)
                        return false;
                    previousAuthorizationAt = match.GameTime;
                    lastPermission = match.Permission;
                    hasAuthorization = true;
                }
                if (hasAuthorization && lastPermission != contract.StandingRenewalPermission) return false;
            }
            if (referenced.Count != evidence.CompletedTransfers.Count + evidence.Authorizations.Count) return false;
            foreach (var receipt in evidence.CompletedTransfers)
                if (!referenced.Contains(receipt.ReceiptId)) return false;
            foreach (var authorization in evidence.Authorizations)
                if (!referenced.Contains(authorization.ReceiptId)) return false;
            return true;
        }

        public NpcServiceContract Read(string contractId)
        {
            for (int i = 0; i < contracts.Count; i++)
                if (contracts[i].ContractId == contractId) return contracts[i];
            return null;
        }

        public static bool TryReserve(NpcServiceContractLedger current, long expectedLedgerRevision,
            NpcServiceContractTerms terms, NpcServiceTransferReceipt funding,
            out NpcServiceContractLedger updated)
        {
            updated = null;
            if (current == null || current.Version != VersionCurrent || current.Revision != expectedLedgerRevision ||
                current.Revision == long.MaxValue || terms == null || funding == null || current.Read(terms.ContractId) != null ||
                current.HasReceiptId(funding.ReceiptId)) return false;
            NpcServiceContract contract;
            if (!NpcServiceContract.TryReserve(terms, funding, out contract)) return false;
            var copy = current.Copy(); copy.contracts.Add(contract); copy.Revision++; copy.derivedFrom = current;
            updated = copy; return true;
        }

        public bool HasReceiptId(string receiptId)
        {
            if (string.IsNullOrWhiteSpace(receiptId)) return false;
            foreach (var contract in contracts)
            {
                foreach (var receipt in contract.TransferReceipts)
                    if (receipt.ReceiptId == receiptId) return true;
                if (contract.HasAuthorizationReceipt(receiptId)) return true;
            }
            return false;
        }

        public static bool TryReplace(NpcServiceContractLedger current, long expectedLedgerRevision,
            NpcServiceContract updatedContract, out NpcServiceContractLedger updated)
        {
            updated = null;
            if (current == null || updatedContract == null || current.Revision != expectedLedgerRevision ||
                current.Revision == long.MaxValue || current.Version != VersionCurrent) return false;
            int index = -1;
            for (int i = 0; i < current.contracts.Count; i++)
                if (current.contracts[i].ContractId == updatedContract.ContractId) { index = i; break; }
            if (index < 0 || updatedContract.Revision != current.contracts[index].Revision + 1 ||
                updatedContract.ContractId != current.contracts[index].ContractId ||
                !updatedContract.IsDirectlyDerivedFrom(current.contracts[index])) return false;
            NpcServiceContract validated;
            try { validated = updatedContract.CopyValidated(); }
            catch (ArgumentException) { return false; }
            var copy = current.Copy(); copy.contracts[index] = validated; copy.Revision++; copy.derivedFrom = current;
            if (!copy.ValidUniqueReceiptIds()) return false;
            updated = copy; return true;
        }

        private bool ValidUniqueReceiptIds()
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var contract in contracts)
            {
                foreach (var receipt in contract.TransferReceipts)
                    if (!ids.Add(receipt.ReceiptId)) return false;
                foreach (var id in contract.ReadAuthorizationReceiptIds())
                    if (!ids.Add(id)) return false;
            }
            return true;
        }

        private NpcServiceContractLedger Copy()
        {
            var copy = new NpcServiceContractLedger { Revision = Revision };
            foreach (var contract in contracts) copy.contracts.Add(contract.CopyValidated());
            return copy;
        }

        private static bool ValidId(string value, int max)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > max) return false;
            for (int i = 0; i < value.Length; i++) if (char.IsControl(value[i])) return false;
            return true;
        }
    }
}
