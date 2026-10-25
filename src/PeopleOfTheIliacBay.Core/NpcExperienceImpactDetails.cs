using System;

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcExperienceImpactKind
    {
        Health = 1,
        MonetaryTransfer = 2,
        ItemTransfer = 3,
        StatusOnset = 4
    }

    public enum NpcExperienceStatusKind
    {
        Poison = 1
    }

    public enum NpcExperienceCurrency
    {
        None = 0,
        Gold = 1
    }

    public enum NpcExperienceNativeValuationStatus
    {
        NotApplicable = 0,
        Unknown = 1,
        Known = 2
    }

    /// <summary>
    /// Bounded magnitude metadata from trusted native adapters. It carries no source authority and cannot be
    /// supplied as an appraisal result. Unknown item value is explicit; model-generated prices are never accepted.
    /// </summary>
    public sealed class NpcExperienceImpactDetails
    {
        public const int VersionCurrent = 4;
        public const int MaximumNativeEffectKeyLength = 240;
        private const long MaximumMagnitude = 1000000000000L;

        public int Version { get; private set; }
        public NpcExperienceImpactKind Kind { get; private set; }
        public int InitialHealth { get; private set; }
        public int FinalHealth { get; private set; }
        public int MaximumHealth { get; private set; }
        public long TotalDamage { get; private set; }
        public long TotalHealing { get; private set; }
        public int ChangeCount { get; private set; }
        public long Quantity { get; private set; }
        public NpcExperienceCurrency Currency { get; private set; }
        public NpcExperienceNativeValuationStatus NativeValuationStatus { get; private set; }
        public long NativeGoldValue { get; private set; }
        public ulong RetainedDestinationItemUid { get; private set; }
        public NpcExperienceStatusKind Status { get; private set; }
        public string NativeEffectKey { get; private set; }
        public long StatusOnsetSequence { get; private set; }

        private NpcExperienceImpactDetails() { }

        public static NpcExperienceImpactDetails ForHealthEpisode(int initialHealth, int finalHealth,
            int maximumHealth, long totalDamage, long totalHealing, int changeCount)
        {
            if (maximumHealth < 1 || initialHealth < 0 || initialHealth > maximumHealth ||
                finalHealth < 0 || finalHealth > maximumHealth || totalDamage < 0 || totalHealing < 0 ||
                totalDamage > MaximumMagnitude || totalHealing > MaximumMagnitude ||
                totalDamage == 0 && totalHealing == 0 || changeCount < 1 || changeCount > 1000000)
                throw new ArgumentException("Invalid native health-episode impact details.");
            return new NpcExperienceImpactDetails { Version = VersionCurrent, Kind = NpcExperienceImpactKind.Health,
                InitialHealth = initialHealth, FinalHealth = finalHealth, MaximumHealth = maximumHealth,
                TotalDamage = totalDamage, TotalHealing = totalHealing, ChangeCount = changeCount };
        }

        /// <summary>Gold amount is a quantity of gold pieces, not an inferred appraisal value.</summary>
        public static NpcExperienceImpactDetails ForGoldTransfer(long goldQuantity)
        {
            ValidateQuantity(goldQuantity);
            return new NpcExperienceImpactDetails { Version = VersionCurrent,
                Kind = NpcExperienceImpactKind.MonetaryTransfer, Quantity = goldQuantity,
                Currency = NpcExperienceCurrency.Gold,
                NativeValuationStatus = NpcExperienceNativeValuationStatus.NotApplicable };
        }

        /// <summary>Native valuation is the total value for the entire transferred item quantity.</summary>
        public static NpcExperienceImpactDetails ForItemTransfer(long quantity,
            NpcExperienceNativeValuationStatus nativeValuationStatus, long nativeGoldValue)
        {
            ValidateQuantity(quantity);
            if (nativeValuationStatus != NpcExperienceNativeValuationStatus.Unknown &&
                nativeValuationStatus != NpcExperienceNativeValuationStatus.Known)
                throw new ArgumentOutOfRangeException(nameof(nativeValuationStatus));
            if (nativeGoldValue < 0 || nativeGoldValue > MaximumMagnitude ||
                nativeValuationStatus == NpcExperienceNativeValuationStatus.Unknown && nativeGoldValue != 0)
                throw new ArgumentException("Invalid native item valuation.", nameof(nativeGoldValue));
            return new NpcExperienceImpactDetails { Version = VersionCurrent,
                Kind = NpcExperienceImpactKind.ItemTransfer, Quantity = quantity,
                NativeValuationStatus = nativeValuationStatus, NativeGoldValue = nativeGoldValue };
        }

        public static NpcExperienceImpactDetails ForReceivedItemTransfer(ulong retainedDestinationItemUid,
            long quantity, NpcExperienceNativeValuationStatus nativeValuationStatus, long nativeGoldValue)
        {
            if (retainedDestinationItemUid == 0) throw new ArgumentOutOfRangeException(nameof(retainedDestinationItemUid));
            var details = ForItemTransfer(quantity, nativeValuationStatus, nativeGoldValue);
            details.RetainedDestinationItemUid = retainedDestinationItemUid;
            return details;
        }

        public static NpcExperienceImpactDetails ForStatusOnset(NpcExperienceStatusKind status,
            string nativeEffectKey, long onsetSequence)
        {
            if (status != NpcExperienceStatusKind.Poison)
                throw new ArgumentException("Unsupported native status kind.", nameof(status));
            if (string.IsNullOrWhiteSpace(nativeEffectKey) || nativeEffectKey.Length > MaximumNativeEffectKeyLength)
                throw new ArgumentException("Invalid native status effect key.", nameof(nativeEffectKey));
            for (int i = 0; i < nativeEffectKey.Length; i++)
                if (char.IsControl(nativeEffectKey[i]))
                    throw new ArgumentException("Invalid native status effect key.", nameof(nativeEffectKey));
            if (onsetSequence < 1) throw new ArgumentException("Status onset sequence must be positive.", nameof(onsetSequence));
            return new NpcExperienceImpactDetails { Version = VersionCurrent,
                Kind = NpcExperienceImpactKind.StatusOnset, Status = status,
                NativeEffectKey = nativeEffectKey, StatusOnsetSequence = onsetSequence };
        }

        internal static NpcExperienceImpactDetails CopyValidated(NpcExperienceImpactDetails source)
        {
            if (source == null) return null;
            if (source.Version < 2 || source.Version > VersionCurrent) throw new ArgumentOutOfRangeException(nameof(source.Version));
            if (source.Kind == NpcExperienceImpactKind.StatusOnset && source.Version < VersionCurrent)
                throw new ArgumentOutOfRangeException(nameof(source.Version));
            switch (source.Kind)
            {
                case NpcExperienceImpactKind.Health:
                    if (source.Quantity != 0 || source.Currency != NpcExperienceCurrency.None ||
                        source.NativeValuationStatus != NpcExperienceNativeValuationStatus.NotApplicable || source.NativeGoldValue != 0 ||
                        source.Status != 0 || source.NativeEffectKey != null || source.StatusOnsetSequence != 0)
                        throw new ArgumentException("Health impact contains transfer-only fields.", nameof(source));
                    if (source.RetainedDestinationItemUid != 0) throw new ArgumentException("Health impact contains an item UID.", nameof(source));
                    return ForHealthEpisode(source.InitialHealth, source.FinalHealth, source.MaximumHealth,
                        source.TotalDamage, source.TotalHealing, source.ChangeCount);
                case NpcExperienceImpactKind.MonetaryTransfer:
                    if (source.InitialHealth != 0 || source.FinalHealth != 0 || source.MaximumHealth != 0 ||
                        source.TotalDamage != 0 || source.TotalHealing != 0 || source.ChangeCount != 0 ||
                        source.Currency != NpcExperienceCurrency.Gold ||
                        source.NativeValuationStatus != NpcExperienceNativeValuationStatus.NotApplicable || source.NativeGoldValue != 0 ||
                        source.Status != 0 || source.NativeEffectKey != null || source.StatusOnsetSequence != 0)
                        throw new ArgumentException("Monetary impact contains invalid fields.", nameof(source));
                    if (source.RetainedDestinationItemUid != 0) throw new ArgumentException("Monetary impact contains an item UID.", nameof(source));
                    return ForGoldTransfer(source.Quantity);
                case NpcExperienceImpactKind.ItemTransfer:
                    if (source.InitialHealth != 0 || source.FinalHealth != 0 || source.MaximumHealth != 0 ||
                        source.TotalDamage != 0 || source.TotalHealing != 0 || source.ChangeCount != 0 ||
                        source.Currency != NpcExperienceCurrency.None || source.Status != 0 ||
                        source.NativeEffectKey != null || source.StatusOnsetSequence != 0)
                        throw new ArgumentException("Item impact contains invalid fields.", nameof(source));
                    return source.RetainedDestinationItemUid == 0
                        ? ForItemTransfer(source.Quantity, source.NativeValuationStatus, source.NativeGoldValue)
                        : ForReceivedItemTransfer(source.RetainedDestinationItemUid, source.Quantity,
                            source.NativeValuationStatus, source.NativeGoldValue);
                case NpcExperienceImpactKind.StatusOnset:
                    if (source.InitialHealth != 0 || source.FinalHealth != 0 || source.MaximumHealth != 0 ||
                        source.TotalDamage != 0 || source.TotalHealing != 0 || source.ChangeCount != 0 ||
                        source.Quantity != 0 || source.Currency != NpcExperienceCurrency.None ||
                        source.NativeValuationStatus != NpcExperienceNativeValuationStatus.NotApplicable ||
                        source.NativeGoldValue != 0 || source.RetainedDestinationItemUid != 0)
                        throw new ArgumentException("Status impact contains health- or transfer-only fields.", nameof(source));
                    return ForStatusOnset(source.Status, source.NativeEffectKey, source.StatusOnsetSequence);
                default:
                    throw new ArgumentOutOfRangeException(nameof(source.Kind));
            }
        }

        internal void ValidateEventKind(NpcExperienceEventKind eventKind)
        {
            bool valid;
            switch (Kind)
            {
                case NpcExperienceImpactKind.Health:
                    valid = (TotalDamage > 0 && eventKind == NpcExperienceEventKind.Injury) ||
                        (TotalDamage == 0 && TotalHealing > 0 && eventKind == NpcExperienceEventKind.Healing);
                    break;
                case NpcExperienceImpactKind.MonetaryTransfer:
                    valid = eventKind == NpcExperienceEventKind.MoneyReceived;
                    break;
                case NpcExperienceImpactKind.ItemTransfer:
                    valid = (eventKind == NpcExperienceEventKind.GiftReceived && RetainedDestinationItemUid == 0) ||
                        (eventKind == NpcExperienceEventKind.ItemReceived && RetainedDestinationItemUid > 0);
                    break;
                case NpcExperienceImpactKind.StatusOnset:
                    valid = eventKind == NpcExperienceEventKind.StatusOnset;
                    break;
                default:
                    valid = false;
                    break;
            }
            if (!valid)
                throw new ArgumentException("Native impact details must match their event kind.", nameof(eventKind));
        }

        private static void ValidateQuantity(long quantity)
        {
            if (quantity < 1 || quantity > MaximumMagnitude)
                throw new ArgumentOutOfRangeException(nameof(quantity));
        }
    }
}
