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
        public ulong ItemUid { get; internal set; }
        public int Price { get; internal set; }
        public string ContainerId { get; internal set; }
        public long SellerInventoryRevision { get; internal set; }
    }

    /// <summary>
    /// Adapts persisted finite stock to DFU's actual StaticNPC guild providers.
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
            Func<StaticNPC, NativePerson> resolveProvider, long gameDay, double now,
            Func<DaggerfallUnityItem, int> quoteUnitPrice, out NativeNpcGuildPotionOffer offer, out string reason)
        {
            offer = null;
            reason = "No verified native healing-potion offer is available.";
            if (store == null || !patient || !providerNpc || gameDay < 0 || !ValidTime(now) || quoteUnitPrice == null)
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

            NpcGuildPotionStockState existing = NpcGuildPotionSupply.ReadStock(store, provider.Id);
            if (existing != null && gameDay < existing.RestockedDay)
            {
                reason = "The provider's saved stock day is newer than the current game day.";
                return false;
            }

            int recipeKey = 0;
            if (existing == null || existing.RestockedDay < gameDay)
            {
                if (!NativeNpcHealingItem.TryFindHealingPotionRecipeKey(out recipeKey))
                {
                    reason = "DFU has no supported single-effect HealHealth potion recipe.";
                    return false;
                }

                var newStock = new ItemCollection();
                DaggerfallUnityItem priceSample = ItemBuilder.CreatePotion(recipeKey);
                if (!NativeNpcHealingItem.IsSupportedPotion(priceSample))
                {
                    reason = "DFU did not create a supported native healing potion.";
                    return false;
                }
                newStock.AddItem(priceSample, noStack: true);
                int unitPrice;
                try { unitPrice = quoteUnitPrice(priceSample); }
                catch (Exception error) { reason = error.Message; return false; }
                if (!NpcGuildPotionSupply.CanOfferPatient(store, resident.Id, archetype, unitPrice, now))
                {
                    reason = "The resident is ineligible or cannot afford a native healing potion.";
                    return false;
                }
                for (int i = 1; i < NpcGuildPotionSupply.DailyStockUnits; i++)
                {
                    DaggerfallUnityItem potion = ItemBuilder.CreatePotion(recipeKey);
                    if (!NativeNpcHealingItem.IsSupportedPotion(potion))
                    {
                        reason = "DFU did not create a supported native healing potion.";
                        return false;
                    }
                    try
                    {
                        if (quoteUnitPrice(potion) != unitPrice)
                        {
                            reason = "The native potion quote changed within one restock batch.";
                            return false;
                        }
                    }
                    catch (Exception error) { reason = error.Message; return false; }
                    newStock.AddItem(potion, noStack: true);
                }

                NativeNpcInventoryBinding initialSeller;
                if (!NativeNpcInventoryBridge.TryOpen(store, provider.Id, null,
                    NativeNpcInventoryBridge.InitialWalletGold(store, provider.Id), out initialSeller, out reason)) return false;

                IList<NpcInventoryItem> exactItems;
                try { exactItems = NativeNpcInventoryBridge.CaptureItems(newStock); }
                catch (Exception error) { reason = error.Message; return false; }
                if (!NpcGuildPotionSupply.TryRestockForOffer(store, resident.Id, archetype, provider.Id,
                    gameDay, now, unitPrice, initialSeller.Revision, exactItems, out reason)) return false;
            }

            NativeNpcInventoryBinding sellerBinding;
            if (!NativeNpcInventoryBridge.TryOpen(store, provider.Id, null,
                NativeNpcInventoryBridge.InitialWalletGold(store, provider.Id), out sellerBinding, out reason)) return false;

            NpcGuildPotionStockState stock = NpcGuildPotionSupply.ReadStock(store, provider.Id);
            if (stock == null || stock.RestockedDay != gameDay) return false;
            var ownReservations = new List<ulong>();
            var otherReservations = new HashSet<ulong>();
            ReadTaskReservations(store, provider.Id, resident.Id, ownReservations, otherReservations);
            var candidates = new List<ulong>();
            foreach (ulong uid in ownReservations) if (!candidates.Contains(uid)) candidates.Add(uid);
            foreach (ulong uid in stock.ItemUids)
                if (!otherReservations.Contains(uid) && !candidates.Contains(uid)) candidates.Add(uid);
            foreach (ulong uid in stock.ReservedItemUids)
                if (ownReservations.Contains(uid) && !candidates.Contains(uid)) candidates.Add(uid);
            foreach (ulong uid in candidates)
            {
                DaggerfallUnityItem item = sellerBinding.Items.GetItem(uid);
                if (!NativeNpcHealingItem.IsSupportedPotion(item)) continue;
                int price;
                try { price = quoteUnitPrice(item); }
                catch (Exception error) { reason = error.Message; return false; }
                NpcGuildPotionOffer coreOffer;
                if (!NpcGuildPotionSupply.TryCreateOffer(store, resident.Id, archetype, provider.Id,
                    gameDay, uid, price, now, out coreOffer)) continue;
                offer = new NativeNpcGuildPotionOffer { Provider = provider, SellerBinding = sellerBinding,
                    Item = item, ItemUid = uid, Price = price, ContainerId = coreOffer.ContainerId,
                    SellerInventoryRevision = coreOffer.SellerInventoryRevision };
                reason = null;
                return true;
            }

            reason = "Today's finite provider stock is depleted or no longer available.";
            return false;
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

        private static void ReadTaskReservations(WorldStateStore store, string providerId, string patientId,
            List<ulong> ownReservations, HashSet<ulong> otherReservations)
        {
            string containerId = NpcGuildPotionSupply.GuildPotionContainerId.ToString();
            foreach (NpcTaskPlan task in store.PendingNpcTasks())
            {
                if (task == null || task.Phase == NpcTaskPhase.Completed || task.Phase == NpcTaskPhase.Failed ||
                    task.Phase == NpcTaskPhase.Cancelled || task.Steps == null) continue;
                for (int index = task.CurrentStepIndex; index < task.Steps.Count; index++)
                {
                    NpcTaskStep step = task.Steps[index];
                    NpcActionTarget target = step == null ? null : step.Target;
                    ulong uid;
                    if (step == null || step.Kind != NpcActionKind.Purchase || target == null ||
                        target.Kind != NpcActionTargetKind.IndoorPoint || target.PersonId != providerId ||
                        target.ContainerId != containerId ||
                        !ulong.TryParse(target.ItemId, out uid) || uid == 0) continue;
                    List<ulong> destination = task.ResidentId == patientId ? ownReservations : null;
                    if (destination != null)
                    {
                        if (!destination.Contains(uid)) destination.Add(uid);
                    }
                    else otherReservations.Add(uid);
                }
            }
        }

        private static bool ValidTime(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0; }
    }
}
