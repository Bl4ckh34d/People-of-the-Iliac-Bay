using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Guilds;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Questing;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>One live, exact native potion offer from a registered guild keeper.</summary>
    public sealed class NativeNpcGuildPotionOffer
    {
        public NativePerson Provider { get; internal set; }
        public NativeNpcInventoryBinding SellerBinding { get; internal set; }
        public DaggerfallUnityItem Item { get; internal set; }
        public ulong RecipeOfferId { get; internal set; }
        public int Price { get; internal set; }
        public string ContainerId { get; internal set; }
        public long SellerInventoryRevision { get; internal set; }
    }

    /// <summary>
    /// Adapts a native healing recipe to DFU's actual StaticNPC guild providers.
    /// Provider registration is checked from the live service faction; this
    /// path never checks or changes the player's guild membership.
    /// </summary>
    public static class NativeNpcGuildPotionSupply
    {
        public static bool TryResolveProvider(StaticNPC npc, Func<StaticNPC, NativePerson> resolveProvider,
            out NativePerson provider, out string reason)
        {
            provider = null;
            reason = "The target is not a live, unprotected native guild potion provider.";
            if (!npc || resolveProvider == null || npc.Data.context != StaticNPC.Context.Building ||
                npc.GetComponent<QuestResourceBehaviour>() != null || npc.GetComponent<DaggerfallAction>() != null ||
                !Services.HasGuildService(npc.Data.factionID) ||
                Services.GetService((GuildNpcServices)npc.Data.factionID) != GuildServices.BuyPotions) return false;

            NativePerson resolved;
            try { resolved = resolveProvider(npc); }
            catch { return false; }
            if (resolved == null || !resolved.IsAlive || resolved.RetiredBedroomFlat ||
                resolved.FactionId != npc.Data.factionID ||
                resolved.SettlementId != npc.Data.mapID || resolved.WorkKey != npc.Data.buildingKey ||
                resolved.NativeHash != npc.Data.hash) return false;

            provider = resolved;
            reason = null;
            return true;
        }

        public static bool TryPrepareOffer(WorldStateStore store, ResidentActor patient, StaticNPC providerNpc,
            Func<StaticNPC, NativePerson> resolveProvider, double now,
            Func<DaggerfallUnityItem, int> quoteUnitPrice, out NativeNpcGuildPotionOffer offer, out string reason)
        {
            offer = null;
            reason = "No verified native healing-potion offer is available.";
            if (store == null || !patient || !providerNpc || !ValidTime(now) || quoteUnitPrice == null)
                return false;

            NativePerson provider;
            if (!TryResolveProvider(providerNpc, resolveProvider, out provider, out reason)) return false;
            Resident resident = patient.Resident;
            if (resident == null || resident.IsQuestProtected || !resident.IsAlive ||
                resident.AgeGroup != ResidentAgeGroup.Adult || store.ResolveResident(resident.Id) == null)
            {
                reason = "Only a living, ordinary adult resident can receive the provider offer.";
                return false;
            }

            NpcArchetype archetype = PatientArchetype(store, resident.Id);
            NativeNpcVitalsBinding vitalsBinding = patient.GetComponent<NativeNpcVitalsBinding>();
            NpcVitalsView savedVitals = store.ReadNpcVitals(resident.Id, archetype);
            int liveHealth, maximumHealth, liveMagicka, maximumMagicka;
            if (vitalsBinding == null || !vitalsBinding.TryReadCurrent(out liveHealth, out maximumHealth,
                    out liveMagicka, out maximumMagicka) || savedVitals == null || !savedVitals.IsPersisted ||
                liveHealth != savedVitals.CurrentHealth || maximumHealth != savedVitals.MaximumHealth ||
                liveMagicka != savedVitals.CurrentMagicka || maximumMagicka != savedVitals.MaximumMagicka ||
                savedVitals.CapturedAt > now || savedVitals.CurrentHealth >= savedVitals.MaximumHealth)
            {
                reason = "The resident's current native injury is not confirmed by saved vitals.";
                return false;
            }

            NativeNpcInventoryOwner buyerOwner = patient.GetComponent<NativeNpcInventoryOwner>();
            NpcInventoryState buyerSaved = store.ReadNpcInventory(resident.Id);
            if (buyerOwner == null || buyerOwner.Binding == null || buyerOwner.Binding.PersonId != resident.Id ||
                buyerSaved == null || buyerOwner.Binding.Revision != buyerSaved.Revision)
            {
                reason = "The resident's saved buyer inventory is unavailable or stale.";
                return false;
            }
            try
            {
                if (!SameItems(buyerSaved.Items, NativeNpcInventoryBridge.CaptureItems(buyerOwner.Binding.Items)))
                {
                    reason = "The resident's saved buyer inventory is unavailable or stale.";
                    return false;
                }
            }
            catch (Exception error) { reason = error.Message; return false; }

            int recipeKey;
            if (!NativeNpcHealingItem.TryFindHealingPotionRecipeKey(out recipeKey))
            { reason = "DFU has no supported native healing potion recipe."; return false; }
            DaggerfallUnityItem sample = ItemBuilder.CreatePotion(recipeKey);
            if (!NativeNpcHealingItem.IsSupportedPotion(sample)) return false;
            int price;
            try { price = quoteUnitPrice(sample); }
            catch (Exception error) { reason = error.Message; return false; }
            if (!NpcGuildPotionSupply.CanOfferPatient(store, resident.Id, archetype, price, now)) return false;
            NativeNpcInventoryBinding seller;
            if (!NativeNpcInventoryBridge.TryOpen(store, provider.Id, null,
                NativeNpcInventoryBridge.InitialWalletGold(store, provider.Id), out seller, out reason)) return false;
            NpcGuildPotionOffer coreOffer;
            if (!NpcGuildPotionSupply.TryCreateOffer(store, resident.Id, archetype, provider.Id,
                NpcGuildPotionSupply.RecipeOfferId(recipeKey), price, now, out coreOffer)) return false;
            offer = new NativeNpcGuildPotionOffer { Provider = provider, SellerBinding = seller,
                Item = sample, RecipeOfferId = coreOffer.RecipeOfferId, Price = price, ContainerId = coreOffer.ContainerId,
                SellerInventoryRevision = seller.Revision };
            reason = null; return true;
        }

        internal static bool IsCurrentPatientOfferEligible(WorldStateStore store, string patientId,
            NpcArchetype archetype, int price, double now)
        { return NpcGuildPotionSupply.CanOfferPatient(store, patientId, archetype, price, now); }

        private static NpcArchetype PatientArchetype(WorldStateStore store, string patientId)
        {
            return store.ResolveNpcArchetype(patientId);
        }

        private static bool SameItems(IList<NpcInventoryItem> saved, IList<NpcInventoryItem> native)
        {
            if (saved == null || native == null || saved.Count != native.Count) return false;
            var byUid = new Dictionary<ulong, string>();
            foreach (NpcInventoryItem item in saved)
            {
                if (item == null || item.Uid == 0 || byUid.ContainsKey(item.Uid)) return false;
                byUid.Add(item.Uid, item.NativePayload);
            }
            foreach (NpcInventoryItem item in native)
            {
                string payload;
                if (item == null || !byUid.TryGetValue(item.Uid, out payload) || payload != item.NativePayload) return false;
            }
            return true;
        }

        private static bool ValidTime(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0; }
    }
}
