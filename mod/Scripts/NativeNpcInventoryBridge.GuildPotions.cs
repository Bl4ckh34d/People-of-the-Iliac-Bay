using System;
using System.Globalization;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public static partial class NativeNpcInventoryBridge
    {
        /// <summary>
        /// Completes one exact-UID purchase from a persisted NPC-owned guild
        /// stock row. Unlike shelf stock, the source item remains under the
        /// seller's inventory ownership until the two-wallet/two-inventory
        /// transaction commits.
        /// </summary>
        public static bool TryCompleteActionGuildPotionPurchase(WorldStateStore store, NpcActionJob requested,
            double now, long gameDay, NativeNpcInventoryBinding buyer, NativeNpcInventoryBinding seller,
            StaticNPC providerNpc, Func<StaticNPC, NativePerson> resolveProvider,
            Func<DaggerfallUnityItem, int> quoteUnitPrice, Func<bool> arrivalAccessIsValid, out string reason)
        {
            reason = "The guild purchase, provider stock, price or inventory revision changed.";
            if (store == null || requested == null || buyer == null || seller == null || providerNpc == null ||
                resolveProvider == null || quoteUnitPrice == null || arrivalAccessIsValid == null || gameDay < 0 ||
                double.IsNaN(now) || double.IsInfinity(now)) return false;

            NpcActionJob job = store.ReadNpcAction(requested.Id);
            ulong uid;
            if (job == null || job.Revision != requested.Revision || job.Kind != NpcActionKind.Purchase ||
                job.ServiceCode != NpcShopCommerce.ServiceCode || job.Target == null ||
                job.Target.Kind != NpcActionTargetKind.IndoorPoint || job.Target.Quantity != 1 ||
                job.Target.QuotedGold != job.ServicePrice ||
                job.Target.ContainerId != NpcGuildPotionSupply.GuildPotionContainerId.ToString(CultureInfo.InvariantCulture) ||
                job.Target.PersonId != seller.PersonId || buyer.PersonId != job.ResidentId ||
                seller.PersonId == buyer.PersonId || buyer.Revision != job.ExpectedInventoryRevision ||
                seller.Revision != job.ExpectedTargetInventoryRevision ||
                !ulong.TryParse(job.Target.ItemId, NumberStyles.None, CultureInfo.InvariantCulture, out uid) || uid == 0)
                return false;

            try { NpcShopCommerce.ValidateService(job); }
            catch (ArgumentException) { return false; }

            NativePerson provider;
            string providerReason;
            if (!NativeNpcGuildPotionSupply.TryResolveProvider(providerNpc, resolveProvider, out provider, out providerReason) ||
                provider.Id != seller.PersonId || job.Target.SettlementId != provider.SettlementId ||
                job.Target.BuildingKey != provider.WorkKey) return false;

            NpcArchetype archetype = store.ResolveNpcArchetype(job.ResidentId);
            if (!NpcGuildPotionSupply.CanOffer(store, job.ResidentId, archetype, provider.Id, job.ServicePrice, now))
                return false;

            NpcGuildPotionStockState stock = NpcGuildPotionSupply.ReadStock(store, provider.Id);
            bool reservedForTask = IsGuildPotionPurchaseReserved(store, provider.Id, job.ResidentId, uid);
            bool reservedByAnotherPatient = IsGuildPotionPurchaseReservedByAnotherPatient(store, provider.Id,
                job.ResidentId, uid);
            bool listedStock = stock != null &&
                (stock.ItemUids.Contains(uid) || stock.ReservedItemUids.Contains(uid));
            bool currentStock = stock != null && stock.ItemUids.Contains(uid) && stock.RestockedDay == gameDay;
            bool carriedReservation = stock != null && stock.ReservedItemUids.Contains(uid) && reservedForTask;
            if (stock == null || !listedStock || reservedByAnotherPatient || gameDay < stock.RestockedDay ||
                (!currentStock && !carriedReservation && !reservedForTask)) return false;

            string failure = null;
            bool completed = store.TryCompleteNpcActionEffect(job.Id, job.Revision, now, () =>
            {
                bool access;
                try
                {
                    access = NativeNpcGuildPotionSupply.TryResolveProvider(providerNpc, resolveProvider,
                        out NativePerson currentProvider, out string ignored) && currentProvider.Id == provider.Id &&
                        arrivalAccessIsValid();
                }
                catch (Exception error) { failure = error.Message; return false; }
                if (!access || !NpcGuildPotionSupply.CanOffer(store, job.ResidentId, archetype,
                        provider.Id, job.ServicePrice, now)) return false;

                DaggerfallUnityItem item = seller.Items == null ? null : seller.Items.GetItem(uid);
                if (item == null || !NativeNpcHealingItem.IsSupportedPotion(item)) return false;
                int currentPrice;
                try { currentPrice = quoteUnitPrice(item); }
                catch (Exception error) { failure = error.Message; return false; }
                if (currentPrice != job.ServicePrice || !TryPurchaseOwnedGuildPotion(store, buyer, seller, item,
                    job.ServicePrice, job.Id + ":purchase", out failure)) return false;
                store.ReleaseNpcGuildPotionReservationAfterTransfer(provider.Id, uid);
                return true;
            });
            if (failure != null) reason = failure;
            else if (completed) reason = null;
            return completed;
        }

        private static bool TryPurchaseOwnedGuildPotion(WorldStateStore store, NativeNpcInventoryBinding buyer,
            NativeNpcInventoryBinding seller, DaggerfallUnityItem item, int price, string transactionId, out string reason)
        {
            reason = "The exact native potion or either persisted inventory changed.";
            bool touched = false;
            bool committed = false;
            DaggerfallUnityItem moved = null;
            try
            {
                if (store == null || buyer == null || seller == null || item == null || buyer.Items == null ||
                    seller.Items == null || ReferenceEquals(buyer.Items, seller.Items) || buyer.PersonId == seller.PersonId ||
                    price < 1 || price > NpcInventoryLimits.MaximumWalletGold || string.IsNullOrWhiteSpace(transactionId))
                    return false;

                NpcInventoryState buyerState = CheckBinding(store, buyer, transactionId);
                NpcInventoryState sellerState = CheckBinding(store, seller, transactionId);
                if (buyerState == null || sellerState == null || buyerState.WalletGold < price ||
                    sellerState.WalletGold > NpcInventoryLimits.MaximumWalletGold - price ||
                    sellerState.Revision >= NpcInventoryLimits.MaximumRevision ||
                    buyerState.Revision >= NpcInventoryLimits.MaximumRevision ||
                    store.NpcInventoryItemOwner(item.UID) != seller.PersonId ||
                    seller.Items.GetItem(item.UID) != item || item.stackCount != 1 || item.IsQuestItem ||
                    item.IsEquipped || item.IsSummoned || buyer.Items.GetItem(item.UID) != null ||
                    !NativeNpcHealingItem.IsSupportedPotion(item)) return false;

                touched = true;
                seller.Items.RemoveItem(item);
                buyer.Items.AddItem(item, noStack: true);
                moved = item;
                if (seller.Items.Contains(item) || !ReferenceEquals(buyer.Items.GetItem(item.UID), item))
                    throw new InvalidOperationException("DFU did not transfer the exact healing potion UID.");

                var updates = new[]
                {
                    new NpcInventoryUpdate { PersonId = buyer.PersonId, ExpectedRevision = buyerState.Revision,
                        WalletGold = buyerState.WalletGold - price, Items = CaptureItems(buyer.Items) },
                    new NpcInventoryUpdate { PersonId = seller.PersonId, ExpectedRevision = sellerState.Revision,
                        WalletGold = sellerState.WalletGold + price, Items = CaptureItems(seller.Items) }
                };
                if (!store.CommitNpcInventoryTransaction(transactionId, updates))
                    throw new InvalidOperationException("The native potion and wallet transaction was rejected.");

                committed = true;
                buyer.Revision++;
                seller.Revision++;
                reason = null;
                return true;
            }
            catch (Exception error)
            {
                reason = error.Message;
                return false;
            }
            finally
            {
                if (touched && !committed && moved != null)
                {
                    buyer.Items.RemoveItem(moved);
                    if (!seller.Items.Contains(moved)) seller.Items.AddItem(moved, noStack: true);
                }
            }
        }

        private static bool IsGuildPotionPurchaseReserved(WorldStateStore store, string providerId,
            string patientId, ulong uid)
        {
            foreach (NpcTaskPlan task in store.PendingNpcTasks())
            {
                if (task == null || task.ResidentId != patientId || task.Phase == NpcTaskPhase.Completed ||
                    task.Phase == NpcTaskPhase.Failed || task.Phase == NpcTaskPhase.Cancelled || task.Steps == null) continue;
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

        private static bool IsGuildPotionPurchaseReservedByAnotherPatient(WorldStateStore store,
            string providerId, string patientId, ulong uid)
        {
            foreach (NpcTaskPlan task in store.PendingNpcTasks())
            {
                if (task == null || task.ResidentId == patientId || task.Phase == NpcTaskPhase.Completed ||
                    task.Phase == NpcTaskPhase.Failed || task.Phase == NpcTaskPhase.Cancelled || task.Steps == null) continue;
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
    }
}
