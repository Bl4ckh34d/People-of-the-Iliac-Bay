using System;
using System.Globalization;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public static partial class NativeNpcInventoryBridge
    {
        /// <summary>Creates and purchases one native potion without an artificial stock ledger.</summary>
        public static bool TryCompleteActionGuildPotionPurchase(WorldStateStore store, NpcActionJob requested,
            double now, NativeNpcInventoryBinding buyer, NativeNpcInventoryBinding seller,
            StaticNPC providerNpc, Func<StaticNPC, NativePerson> resolveProvider,
            Func<DaggerfallUnityItem, int> quoteUnitPrice, Func<bool> arrivalAccessIsValid, out string reason)
        {
            reason = "The guild purchase, provider recipe, price or inventory revision changed.";
            if (store == null || requested == null || buyer == null || seller == null || providerNpc == null ||
                resolveProvider == null || quoteUnitPrice == null || arrivalAccessIsValid == null ||
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

            int recipeKey;
            if (!NpcGuildPotionSupply.TryRecipeKey(uid, out recipeKey)) return false;

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

                DaggerfallUnityItem item = ItemBuilder.CreatePotion(recipeKey);
                if (item == null || !NativeNpcHealingItem.IsSupportedPotion(item)) return false;
                int currentPrice;
                try { currentPrice = quoteUnitPrice(item); }
                catch (Exception error) { failure = error.Message; return false; }
                if (currentPrice != job.ServicePrice || !TryPurchaseCreatedItem(store, buyer, seller.PersonId, seller.Revision, item,
                    job.ServicePrice, arrivalAccessIsValid, job.Id + ":purchase", out failure)) return false;
                return true;
            });
            if (failure != null) reason = failure;
            else if (completed) reason = null;
            return completed;
        }

    }
}
