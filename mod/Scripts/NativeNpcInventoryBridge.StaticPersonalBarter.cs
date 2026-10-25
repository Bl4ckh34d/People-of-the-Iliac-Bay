using System;
using System.Collections.Generic;
using DaggerfallWorkshop.Game.Entity;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public static partial class NativeNpcInventoryBridge
    {
        // Static owners share the entity transaction. They have no body weight;
        // only their existing persisted item-count bound applies.
        public static bool TryPrepareStaticPersonalBarter(WorldStateStore store, NativeStaticNpcInventoryOwner owner,
            PlayerEntity player, string transactionId, IList<PersonalBarterLine> lines, int netGoldToNpc,
            PersonalBarterLiveChecks checks, out PersonalBarterOrder prepared, out string reason)
        {
            prepared = null;
            reason = "The static personal trade is no longer available.";
            NativeNpcInventoryBinding npc;
            NpcInventoryState saved;
            if (!ValidateStaticBarterInputs(store, owner, player, transactionId, lines, checks, out npc, out saved)) return false;
            return TryPreparePersonalBarterForInventory(store, npc, player, transactionId, lines, netGoldToNpc,
                checks, ignoredDelta=>StaticInventoryWithinBound(npc), out prepared, out reason);
        }

        public static bool TryStaticPersonalBarter(WorldStateStore store, NativeStaticNpcInventoryOwner owner,
            PlayerEntity player, PersonalBarterOrder order, double committedGameTime, PersonalBarterLiveChecks checks,
            NpcCommerceHaggleBasketBinding haggleBinding, Func<int> nativeBaseNetGoldNow,
            NpcCommerceHaggleBasketRound submittedRound, out PersonalBarterReceipt receipt, out string reason)
        {
            receipt = null;
            reason = "The static personal trade is no longer available.";
            NativeNpcInventoryBinding npc;
            NpcInventoryState saved;
            if (order == null || !ValidateStaticBarterInputs(store, owner, player, order.TransactionId, order.Lines,
                checks, out npc, out saved)) return false;
            return TryPersonalBarterForInventory(store, npc, player, order, committedGameTime, checks, haggleBinding,
                nativeBaseNetGoldNow, submittedRound, ignoredDelta=>StaticInventoryWithinBound(npc), out receipt, out reason);
        }

        private static bool ValidateStaticBarterInputs(WorldStateStore store, NativeStaticNpcInventoryOwner owner,
            PlayerEntity player, string transactionId, IList<PersonalBarterLine> lines, PersonalBarterLiveChecks checks,
            out NativeNpcInventoryBinding npc, out NpcInventoryState saved)
        {
            npc = owner == null ? null : owner.Binding;
            saved = null;
            if (store == null || owner == null || owner.Person == null || npc == null || player == null || player.Items == null ||
                npc.Items == null || string.IsNullOrWhiteSpace(transactionId) || transactionId.Length > 240 || lines == null ||
                checks == null || checks.NpcAdultNow == null || checks.NpcEligibleNow == null || checks.NpcWillingNow == null ||
                checks.PlayerConsentedNow == null || checks.NpcValuesItemNow == null || checks.OccupationallyDisposableNow == null ||
                checks.ItemUniqueNow == null || checks.ItemSourceAllowedNow == null || !StaticInventoryWithinBound(npc)) return false;
            saved = CheckBinding(store, npc, transactionId);
            return saved != null;
        }

        private static bool StaticInventoryWithinBound(NativeNpcInventoryBinding npc)
        { return npc != null && npc.Items != null && npc.Items.Count <= NpcInventoryLimits.MaximumItems; }
    }
}
