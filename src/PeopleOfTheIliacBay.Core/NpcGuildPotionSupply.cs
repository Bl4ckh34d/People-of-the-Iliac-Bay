using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    [Serializable]
    public sealed class NpcGuildPotionStockState
    {
        public string ProviderPersonId;
        public long RestockedDay;
        public List<ulong> ItemUids = new List<ulong>();
        // Up to one previous generation can remain live for tasks already on
        // the way. Total generated stock per provider stays capped at sixteen.
        public List<ulong> ReservedItemUids = new List<ulong>();
    }

    public sealed class NpcGuildPotionOffer
    {
        public string SellerPersonId { get; internal set; }
        public string ContainerId { get; internal set; }
        public ulong ItemUid { get; internal set; }
        public int Price { get; internal set; }
        public long SellerInventoryRevision { get; internal set; }
    }

    /// <summary>
    /// Finite, exact-UID stock for real native guild potion providers. Native
    /// context and guild-service checks belong to the runtime adapter; this
    /// ledger guarantees that only an eligible persisted patient can cause a
    /// provider's daily stock to be materialized.
    /// </summary>
    public static class NpcGuildPotionSupply
    {
        public const int DailyStockUnits = 8;
        // NpcShopCommerce validates ContainerId as a non-zero native LoadID.
        // Reserve max ulong for an NPC-owned provider stock source.
        public const ulong GuildPotionContainerId = ulong.MaxValue;

        public static bool CanOffer(WorldStateStore store, string patientId, NpcArchetype archetype,
            string providerId, int price, double now)
        {
            if (!CanOfferPatient(store, patientId, archetype, price, now) ||
                string.IsNullOrWhiteSpace(providerId) || patientId == providerId) return false;

            if (!store.IsLivingNativePerson(providerId)) return false;
            NpcInventoryState buyer = store.ReadNpcInventory(patientId);
            NpcInventoryState seller = store.ReadNpcInventory(providerId);
            return buyer != null && seller != null && buyer.WalletGold >= price &&
                seller.Revision >= 0 && seller.Items != null;
        }

        public static bool CanOfferPatient(WorldStateStore store, string patientId, NpcArchetype archetype,
            int price, double now)
        {
            if (store == null || !ValidTime(now) || price < 1 || price > NpcInventoryLimits.MaximumWalletGold ||
                string.IsNullOrWhiteSpace(patientId) || !Enum.IsDefined(typeof(NpcArchetype), archetype)) return false;

            Resident patient = store.ResolveResident(patientId);
            if (patient == null || !NpcVitalsBindingPolicy.Eligible(patient.IsAlive,
                patient.AgeGroup == ResidentAgeGroup.Adult, patient.IsQuestProtected)) return false;

            NpcVitalsView vitals;
            try { vitals = store.ReadNpcVitals(patientId, archetype); }
            catch (ArgumentException) { return false; }
            if (vitals == null || !vitals.IsAlive || !vitals.IsPersisted || vitals.Revision < 1 ||
                vitals.MaximumHealth < 1 || vitals.CurrentHealth < 1 || vitals.CurrentHealth > vitals.MaximumHealth ||
                !ValidTime(vitals.CapturedAt) || vitals.CapturedAt > now ||
                (long)vitals.CurrentHealth * 4 > (long)vitals.MaximumHealth * 3) return false;
            NpcInventoryState buyer = store.ReadNpcInventory(patientId);
            return buyer != null && buyer.WalletGold >= price;
        }

        public static NpcGuildPotionStockState ReadStock(WorldStateStore store, string providerId)
        {
            if (store == null || string.IsNullOrWhiteSpace(providerId)) return null;
            NpcGuildPotionStockState saved = store.ReadNpcGuildPotionStock(providerId);
            return saved;
        }

        public static bool TryRestockForOffer(WorldStateStore store, string patientId, NpcArchetype archetype,
            string providerId, long gameDay, double now, int price, long expectedSellerRevision,
            IList<NpcInventoryItem> exactNewItems, out string reason)
        {
            reason = "The patient, provider, wallet or stock proposal is invalid.";
            if (store == null || gameDay < 0 || expectedSellerRevision < 0 || exactNewItems == null ||
                exactNewItems.Count != DailyStockUnits || !CanOffer(store, patientId, archetype, providerId, price, now))
                return false;

            NpcGuildPotionStockState current = ReadStock(store, providerId);
            if (current != null && gameDay <= current.RestockedDay)
            {
                reason = gameDay == current.RestockedDay ? "The provider already has its finite stock for this day." :
                    "The provider stock day cannot move backwards.";
                return false;
            }

            if (!store.TryCommitNpcGuildPotionRestock(providerId, gameDay, expectedSellerRevision, exactNewItems, out reason))
                return false;
            reason = null;
            return true;
        }

        public static bool TryCreateOffer(WorldStateStore store, string patientId, NpcArchetype archetype,
            string providerId, long gameDay, ulong itemUid, int price, double now, out NpcGuildPotionOffer offer)
        {
            offer = null;
            if (store == null || gameDay < 0 || itemUid == 0 || !CanOffer(store, patientId, archetype, providerId, price, now))
                return false;
            NpcGuildPotionStockState stock = ReadStock(store, providerId);
            NpcInventoryState seller = store.ReadNpcInventory(providerId);
            if (stock == null || stock.RestockedDay != gameDay || seller == null ||
                !IsGeneratedOrReservedForPatient(store, stock, patientId, providerId, itemUid) ||
                !seller.Items.Exists(item => item.Uid == itemUid)) return false;

            offer = new NpcGuildPotionOffer { SellerPersonId = providerId,
                ContainerId = GuildPotionContainerId.ToString(CultureInfo.InvariantCulture),
                ItemUid = itemUid, Price = price, SellerInventoryRevision = seller.Revision };
            return true;
        }

        private static bool IsGeneratedOrReservedForPatient(WorldStateStore store, NpcGuildPotionStockState stock,
            string patientId, string providerId, ulong uid)
        {
            bool generated = stock.ItemUids.Contains(uid);
            bool reserved = stock.ReservedItemUids != null && stock.ReservedItemUids.Contains(uid);
            if (!generated && !reserved) return false;

            bool reservedForPatient = false;
            bool reservedByOtherPatient = false;
            foreach (NpcTaskPlan task in store.PendingNpcTasks())
            {
                if (task == null || task.Phase == NpcTaskPhase.Completed || task.Phase == NpcTaskPhase.Failed ||
                    task.Phase == NpcTaskPhase.Cancelled || task.Steps == null) continue;
                for (int index = task.CurrentStepIndex; index < task.Steps.Count; index++)
                {
                    NpcTaskStep step = task.Steps[index];
                    NpcActionTarget target = step == null ? null : step.Target;
                    ulong requested;
                    if (step == null || step.Kind != NpcActionKind.Purchase || target == null ||
                        target.Kind != NpcActionTargetKind.IndoorPoint || target.PersonId != providerId ||
                        target.ContainerId != GuildPotionContainerId.ToString(CultureInfo.InvariantCulture) ||
                        !ulong.TryParse(target.ItemId, NumberStyles.None, CultureInfo.InvariantCulture, out requested) || requested != uid)
                        continue;
                    if (task.ResidentId == patientId) reservedForPatient = true;
                    else reservedByOtherPatient = true;
                }
            }
            if (reservedByOtherPatient) return false;
            return generated || reserved && reservedForPatient;
        }

        private static bool ValidTime(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0; }
    }

    public sealed partial class WorldStateStore
    {
        public bool IsLivingNativePerson(string personId)
        {
            NativePerson person = IndexedNativePerson(personId);
            return person != null && person.IsAlive;
        }

        public NpcGuildPotionStockState ReadNpcGuildPotionStock(string providerId)
        {
            if (string.IsNullOrWhiteSpace(providerId) || state.NpcGuildPotionStocks == null) return null;
            NpcGuildPotionStockState saved = state.NpcGuildPotionStocks.Find(row => row != null && row.ProviderPersonId == providerId);
            return CopyNpcGuildPotionStock(saved);
        }

        internal bool TryCommitNpcGuildPotionRestock(string providerId, long gameDay, long expectedSellerRevision,
            IList<NpcInventoryItem> exactNewItems, out string reason)
        {
            reason = "Provider inventory or stock proposal is invalid.";
            if (string.IsNullOrWhiteSpace(providerId) || gameDay < 0 || expectedSellerRevision < 0 ||
                exactNewItems == null || exactNewItems.Count != NpcGuildPotionSupply.DailyStockUnits ||
                state.NpcGuildPotionStocks == null) return false;

            NativePerson provider = FindGuildPotionProvider(providerId);
            NpcInventoryState seller = ReadNpcInventory(providerId);
            if (provider == null || !provider.IsAlive || seller == null || seller.Revision != expectedSellerRevision ||
                seller.Revision >= NpcInventoryLimits.MaximumRevision || seller.Items == null) return false;

            NpcGuildPotionStockState previous = ReadNpcGuildPotionStock(providerId);
            if (previous != null && gameDay <= previous.RestockedDay) return false;

            var priorGenerated = new HashSet<ulong>();
            var previousUids = new List<ulong>();
            if (previous != null)
            {
                foreach (ulong uid in previous.ItemUids) { priorGenerated.Add(uid); previousUids.Add(uid); }
                foreach (ulong uid in previous.ReservedItemUids) { priorGenerated.Add(uid); previousUids.Add(uid); }
            }
            var preservedReservations = new List<ulong>();
            foreach (ulong uid in previousUids)
                if (IsGuildPotionUidReserved(providerId, uid) && seller.Items.Exists(item => item.Uid == uid))
                    preservedReservations.Add(uid);
            if (preservedReservations.Count > NpcGuildPotionSupply.DailyStockUnits)
            {
                reason = "Too many earlier care tasks still reserve this provider's finite stock.";
                return false;
            }
            var uniqueNew = new HashSet<ulong>();
            var nextItems = new List<NpcInventoryItem>();
            foreach (NpcInventoryItem existing in seller.Items)
                if (!priorGenerated.Contains(existing.Uid) || preservedReservations.Contains(existing.Uid))
                    nextItems.Add(new NpcInventoryItem { Uid = existing.Uid, NativePayload = existing.NativePayload });

            var generatedUids = new List<ulong>(NpcGuildPotionSupply.DailyStockUnits);
            foreach (NpcInventoryItem incoming in exactNewItems)
            {
                if (incoming == null || incoming.Uid == 0 || !uniqueNew.Add(incoming.Uid) ||
                    NpcInventoryItemOwner(incoming.Uid) != null || !NpcInventoryLimits.CanAppendExactItem(nextItems, incoming))
                {
                    reason = "New native stock must contain eight unique, unowned exact item records.";
                    return false;
                }
                nextItems.Add(new NpcInventoryItem { Uid = incoming.Uid, NativePayload = incoming.NativePayload });
                generatedUids.Add(incoming.Uid);
            }

            var proposedStock = new NpcGuildPotionStockState { ProviderPersonId = providerId,
                RestockedDay = gameDay, ItemUids = generatedUids, ReservedItemUids = preservedReservations };
            try { ValidateNpcGuildPotionStock(proposedStock); }
            catch (ArgumentException error) { reason = error.Message; return false; }

            string transactionId = GuildPotionRestockTransactionId(providerId, gameDay);
            if (!CommitNpcInventoryTransaction(transactionId, new[] { new NpcInventoryUpdate {
                PersonId = providerId, ExpectedRevision = seller.Revision,
                WalletGold = seller.WalletGold, Items = nextItems } }))
            {
                reason = "Provider inventory revision or stock UID conflict rejected the restock.";
                return false;
            }

            int index = state.NpcGuildPotionStocks.FindIndex(row => row != null && row.ProviderPersonId == providerId);
            if (index < 0) state.NpcGuildPotionStocks.Add(proposedStock);
            else state.NpcGuildPotionStocks[index] = proposedStock;
            reason = null;
            return true;
        }

        private NativePerson FindGuildPotionProvider(string providerId)
        {
            foreach (NativePerson person in state.NativePeople)
                if (person != null && person.Id == providerId) return person;
            return null;
        }

        private bool IsGuildPotionUidReserved(string providerId, ulong uid)
        {
            foreach (NpcTaskPlan task in PendingNpcTasks())
            {
                if (task == null || task.Phase == NpcTaskPhase.Completed || task.Phase == NpcTaskPhase.Failed ||
                    task.Phase == NpcTaskPhase.Cancelled || task.Steps == null) continue;
                for (int index = task.CurrentStepIndex; index < task.Steps.Count; index++)
                {
                    NpcTaskStep step = task.Steps[index];
                    NpcActionTarget target = step == null ? null : step.Target;
                    ulong requested;
                    if (step != null && step.Kind == NpcActionKind.Purchase && target != null &&
                        target.Kind == NpcActionTargetKind.IndoorPoint && target.PersonId == providerId &&
                        target.ContainerId == NpcGuildPotionSupply.GuildPotionContainerId.ToString(CultureInfo.InvariantCulture) &&
                        ulong.TryParse(target.ItemId, NumberStyles.None, CultureInfo.InvariantCulture, out requested) &&
                        requested == uid) return true;
                }
            }
            return false;
        }

        public bool ReleaseNpcGuildPotionReservationAfterTransfer(string providerId, ulong itemUid)
        {
            if (string.IsNullOrWhiteSpace(providerId) || itemUid == 0 || state.NpcGuildPotionStocks == null) return false;
            if (NpcInventoryItemOwner(itemUid) == providerId) return false;
            int index = state.NpcGuildPotionStocks.FindIndex(row => row != null && row.ProviderPersonId == providerId);
            if (index < 0 || !state.NpcGuildPotionStocks[index].ReservedItemUids.Contains(itemUid)) return false;
            NpcGuildPotionStockState copy = CopyNpcGuildPotionStock(state.NpcGuildPotionStocks[index]);
            copy.ReservedItemUids.Remove(itemUid);
            state.NpcGuildPotionStocks[index] = copy;
            return true;
        }

        private static string GuildPotionRestockTransactionId(string providerId, long gameDay)
        {
            byte[] digest;
            using (SHA256 hash = SHA256.Create())
                digest = hash.ComputeHash(Encoding.UTF8.GetBytes(providerId + "\n" + gameDay.ToString(CultureInfo.InvariantCulture)));
            var hex = new StringBuilder(digest.Length * 2);
            foreach (byte value in digest) hex.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            return "guild-potion-restock:" + hex;
        }

        private static NpcGuildPotionStockState CopyNpcGuildPotionStock(NpcGuildPotionStockState source)
        {
            if (source == null) return null;
            return new NpcGuildPotionStockState { ProviderPersonId = source.ProviderPersonId,
                RestockedDay = source.RestockedDay, ItemUids = new List<ulong>(source.ItemUids),
                ReservedItemUids = new List<ulong>(source.ReservedItemUids ?? new List<ulong>()) };
        }

        private static void ValidateNpcGuildPotionStock(NpcGuildPotionStockState row)
        {
            if (row == null || string.IsNullOrWhiteSpace(row.ProviderPersonId) || row.ProviderPersonId.Length > 240 ||
                row.RestockedDay < 0 || row.ItemUids == null || row.ItemUids.Count != NpcGuildPotionSupply.DailyStockUnits ||
                row.ReservedItemUids != null && row.ReservedItemUids.Count > NpcGuildPotionSupply.DailyStockUnits)
                throw new ArgumentException("Invalid finite guild potion stock record.");
            var seen = new HashSet<ulong>();
            foreach (ulong uid in row.ItemUids)
                if (uid == 0 || !seen.Add(uid)) throw new ArgumentException("Invalid or duplicate generated potion UID.");
            if (row.ReservedItemUids != null)
                foreach (ulong uid in row.ReservedItemUids)
                    if (uid == 0 || !seen.Add(uid)) throw new ArgumentException("Invalid or duplicate reserved potion UID.");
        }

        private static List<NpcGuildPotionStockState> CopyNpcGuildPotionStocks(WorldState source, WorldState validated)
        {
            var result = new List<NpcGuildPotionStockState>();
            // Older schema-44 snapshots created before this ledger and all older
            // schemas migrate to empty; no provider items are fabricated.
            if (source.Version < 44 || source.NpcGuildPotionStocks == null) return result;
            Require(source.NpcGuildPotionStocks.Count <= 100000, "Invalid guild potion stock ledger.");
            var providers = new HashSet<string>(StringComparer.Ordinal);
            var knownProviders = new HashSet<string>(StringComparer.Ordinal);
            foreach (NativePerson person in validated.NativePeople) knownProviders.Add(person.Id);
            var inventoryOwners = new HashSet<string>(StringComparer.Ordinal);
            foreach (NpcInventoryState inventory in validated.NpcInventories) inventoryOwners.Add(inventory.PersonId);
            var stockUids = new HashSet<ulong>();
            foreach (NpcGuildPotionStockState row in source.NpcGuildPotionStocks)
            {
                ValidateNpcGuildPotionStock(row);
                Require(knownProviders.Contains(row.ProviderPersonId) && inventoryOwners.Contains(row.ProviderPersonId) &&
                    providers.Add(row.ProviderPersonId), "Unknown or duplicate guild potion provider.");
                foreach (ulong uid in row.ItemUids)
                    Require(stockUids.Add(uid), "Potion UID appears in multiple provider stock records.");
                if (row.ReservedItemUids != null)
                    foreach (ulong uid in row.ReservedItemUids)
                        Require(stockUids.Add(uid), "Reserved potion UID appears in multiple provider stock records.");
                result.Add(CopyNpcGuildPotionStock(row));
            }
            return result;
        }
    }
}
