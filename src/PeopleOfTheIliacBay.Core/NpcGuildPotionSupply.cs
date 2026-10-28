using System;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    public sealed class NpcGuildPotionOffer
    {
        public string SellerPersonId { get; internal set; }
        public string ContainerId { get; internal set; }
        public ulong RecipeOfferId { get; internal set; }
        public int Price { get; internal set; }
        public long SellerInventoryRevision { get; internal set; }
    }

    /// <summary>Eligibility and stable recipe identity for native guild potion purchases.</summary>
    public static class NpcGuildPotionSupply
    {
        // Guild recipe offers are distinct from native shelf LoadIDs.
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

        // The recipe offer ID is not an owned item UID.
        // The adapter creates the actual native item only when buying it.
        public static bool TryCreateOffer(WorldStateStore store, string patientId, NpcArchetype archetype,
            string providerId, ulong recipeOfferId, int price, double now, out NpcGuildPotionOffer offer)
        {
            offer = null;
            int recipeKey;
            if (!TryRecipeKey(recipeOfferId, out recipeKey) ||
                !CanOffer(store, patientId, archetype, providerId, price, now)) return false;
            NpcInventoryState seller = store.ReadNpcInventory(providerId);
            offer = new NpcGuildPotionOffer { SellerPersonId = providerId,
                ContainerId = GuildPotionContainerId.ToString(CultureInfo.InvariantCulture),
                RecipeOfferId = recipeOfferId, Price = price, SellerInventoryRevision = seller.Revision };
            return true;
        }

        public static ulong RecipeOfferId(int recipeKey)
        { return unchecked((uint)recipeKey); }

        public static bool TryRecipeKey(ulong offerId, out int recipeKey)
        {
            recipeKey = unchecked((int)offerId);
            return offerId > 0 && offerId <= uint.MaxValue && recipeKey != 0;
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

    }
}
