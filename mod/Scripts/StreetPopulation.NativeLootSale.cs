using System;
using System.Collections.Generic;
using System.Globalization;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.Items;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        // Uses current native shelves as merchant acceptance only. It creates no
        // merchant ledger, restock rule, or independent economic state.
        private bool TryNativeLootSaleKeeper(out NativePerson keeper, out StaticNPC keeperNpc)
        {
            keeper = null;
            keeperNpc = null;
            if (!TryShopSeller(out keeper)) return false;
            foreach (var candidate in interior.GetComponentsInChildren<StaticNPC>())
            {
                if (!candidate.gameObject.activeInHierarchy || !NativeBusinessServices.IsKeeper(candidate, interior.BuildingData.BuildingType))
                    continue;
                var person = KeeperPerson(candidate);
                if (person != null && person.Id == keeper.Id) { keeperNpc = candidate; return true; }
            }
            keeper = null;
            return false;
        }

        private bool TryNativeLootSaleShelf(out DaggerfallLoot shelf)
        {
            shelf = null;
            var ids = new List<ulong>();
            foreach (var candidate in interior.GetComponentsInChildren<DaggerfallLoot>())
                if (VerifiedShopStock(candidate)) ids.Add(candidate.LoadID);
            ulong selected;
            if (!NpcShopLootSale.TrySelectMerchantShelfLoadId(ids, out selected)) return false;
            foreach (var candidate in interior.GetComponentsInChildren<DaggerfallLoot>())
            {
                if (!VerifiedShopStock(candidate) || candidate.LoadID != selected) continue;
                if (shelf != null) return false; // Duplicate native LoadID is not a merchant identity.
                shelf = candidate;
            }
            return shelf != null;
        }

        private bool TryNativeLootSaleApproach(ResidentActor actor, StaticNPC keeper, out Vector3 chosen)
        {
            chosen = Vector3.zero;
            if (!actor || !keeper || !interior || indoorGeometry == null || shopGeometry == null) return false;
            Vector3 localKeeper = interior.transform.InverseTransformPoint(keeper.transform.position);
            Vector3 from = interior.transform.InverseTransformPoint(actor.transform.position - Vector3.up);
            foreach (var local in indoorGeometry.NearbyApproachPoints(localKeeper, 1.3f, 2.5f, 24))
            {
                Vector3 world = interior.transform.TransformPoint(local);
                if (Vector3.Distance(world + Vector3.up, keeper.transform.position) > 2.5f ||
                    !CivilianResponses.Sees(world + Vector3.up, keeper.transform.position, 3.5f) ||
                    Vector3.Distance(from, local) > 0.25f && shopGeometry.ActivityRouteBetween(from, local).Count < 2)
                    continue;
                chosen = world;
                return true;
            }
            return false;
        }

        private static bool SellableOwnedLootItem(DaggerfallUnityItem item)
        {
            return TransferableActionItem(item) && item.UID != 0 && !item.IsQuestItem && !item.IsEquipped && !item.IsSummoned &&
                !item.RepairData.IsBeingRepaired() && !item.IsArtifact &&
                item.ItemGroup != ItemGroups.Currency && item.ItemGroup != ItemGroups.Transportation &&
                item.ItemGroup != ItemGroups.Deeds && item.value > 0 && item.stackCount == 1;
        }

        private bool TryQuoteNativeLootSale(ResidentActor actor, DaggerfallUnityItem item, out int price)
        {
            price = 0;
            if (!actor || !SellableOwnedLootItem(item)) return false;
            var owner = worldStore.ReadNpcActor(actor.Resident.Id, worldStore.ResolveNpcArchetype(actor.Resident.Id));
            int quality = GameManager.Instance.PlayerEnterExit.BuildingDiscoveryData.quality;
            int nativeCost = FormulaHelper.CalculateCost(item.value, quality, item.ConditionPercentage);
            return NpcShopLootSale.TryPrice(nativeCost, quality, owner.Skill(14), owner.Profile.Attribute(5), out price);
        }

        private void OfferNativeLootSaleAction(ResidentActor actor, string text, List<NpcActionOption> options, bool semantic = true)
        {
            if (!actor || options == null || options.Count >= (semantic ? NpcActionCandidateCatalog.MaximumCandidates : 24)) return;
            NativePerson keeper;
            StaticNPC keeperNpc;
            var owner = actor.GetComponent<NativeNpcInventoryOwner>();
            string reason;
            DaggerfallLoot shelf;
            if (!TryNativeLootSaleKeeper(out keeper, out keeperNpc) || !TryNativeLootSaleShelf(out shelf) || !owner || owner.Binding == null ||
                !NativeNpcInventoryBridge.TryCapture(worldStore, owner.Binding, out reason)) return;
            Vector3 approach;
            if (!TryNativeLootSaleApproach(actor, keeperNpc, out approach)) return;
            for (int index = 0; index < owner.Binding.Items.Count && options.Count < (semantic ? NpcActionCandidateCatalog.MaximumCandidates : 24); index++)
            {
                var item = owner.Binding.Items.GetItem(index);
                int price;
                if (!TryQuoteNativeLootSale(actor, item, out price)) continue;
                var target = ActionPosition(approach);
                target.PersonId = keeper.Id;
                target.ContainerId = NpcShopLootSale.TargetContainerId;
                target.ItemId = item.UID.ToString(CultureInfo.InvariantCulture);
                target.Quantity = 1;
                target.QuotedGold = price;
                options.Add(new NpcActionOption {
                    Id = options.Count + 1, Kind = NpcActionKind.Purchase, Target = target,
                    Label = "sell my exact owned " + item.LongName + " to this open native shop for " +
                        price.ToString(CultureInfo.InvariantCulture) + " gold; merchant acceptance and price rechecked before sale",
                });
                if (!semantic) return;
            }
        }

        private bool PrepareNativeLootSaleAction(ResidentActor actor, NpcActionJob job, out string reason)
        {
            reason = "The native shop, owned item, or quote is no longer available.";
            if (!actor || job == null || job.Target == null || job.Target.ContainerId != NpcShopLootSale.TargetContainerId) return false;
            NativePerson keeper;
            StaticNPC keeperNpc;
            var owner = actor.GetComponent<NativeNpcInventoryOwner>();
            ulong uid;
            DaggerfallLoot shelf;
            if (!TryNativeLootSaleKeeper(out keeper, out keeperNpc) || !TryNativeLootSaleShelf(out shelf) || job.Target.PersonId != keeper.Id || !owner || owner.Binding == null ||
                !ulong.TryParse(job.Target.ItemId, NumberStyles.None, CultureInfo.InvariantCulture, out uid) ||
                !NativeNpcInventoryBridge.TryCapture(worldStore, owner.Binding, out reason)) return false;
            int price;
            if (!TryQuoteNativeLootSale(actor, owner.Binding.Items.GetItem(uid), out price) || price != job.Target.QuotedGold) return false;
            Vector3 approach;
            if (!TryNativeLootSaleApproach(actor, keeperNpc, out approach)) return false;
            var target = ActionPosition(approach);
            target.PersonId = keeper.Id;
            target.ContainerId = NpcShopLootSale.TargetContainerId;
            target.ItemId = uid.ToString(CultureInfo.InvariantCulture);
            target.Quantity = 1;
            target.QuotedGold = price;
            job.Target = target;
            job.ServiceCode = NpcShopLootSale.ServiceCode;
            job.ServicePrice = price;
            job.ServiceStep = 0;
            job.ExpectedInventoryRevision = owner.Binding.Revision;
            return true;
        }

        private bool ExecuteNativeLootSaleAction(NpcActionJob requested, ResidentActor actor, double now, out string reason)
        {
            reason = "The native shop sale, item ownership, or wallet changed.";
            if (requested == null || actor == null || requested.ServiceCode != NpcShopLootSale.ServiceCode) return false;
            var owner = actor.GetComponent<NativeNpcInventoryOwner>();
            if (!owner || owner.Binding == null || owner.Binding.PersonId != requested.ResidentId || owner.Binding.Revision != requested.ExpectedInventoryRevision)
                return false;
            string failure = null;
            bool completed = worldStore.TryCompleteNpcActionEffect(requested.Id, requested.Revision, now, () =>
            {
                var job = worldStore.ReadNpcAction(requested.Id);
                NativePerson keeper;
                StaticNPC keeperNpc;
                DaggerfallLoot shelf;
                ulong uid;
                if (job == null || job.Revision != requested.Revision || job.Phase != NpcActionPhase.Executing ||
                    job.ServiceCode != NpcShopLootSale.ServiceCode || job.ExpectedInventoryRevision != owner.Binding.Revision ||
                    !TryNativeLootSaleKeeper(out keeper, out keeperNpc) || job.Target.PersonId != keeper.Id ||
                    !TryNativeLootSaleShelf(out shelf) || !ulong.TryParse(job.Target.ItemId, NumberStyles.None, CultureInfo.InvariantCulture, out uid) ||
                    Vector3.Distance(actor.transform.position - Vector3.up, ActionWorldPoint(job.Target)) > 0.75f ||
                    Vector3.Distance(actor.transform.position, keeperNpc.transform.position) > 3.5f)
                    return false;
                var item = owner.Binding.Items.GetItem(uid);
                int price;
                var inventory = worldStore.ReadNpcInventory(job.ResidentId);
                if (!TryQuoteNativeLootSale(actor, item, out price) || price != job.ServicePrice || price != job.Target.QuotedGold ||
                    inventory == null || inventory.Revision != owner.Binding.Revision ||
                    inventory.WalletGold > NpcInventoryLimits.MaximumWalletGold - price || shelf.Items.GetItem(uid) != null)
                    return false;
                bool moved = false;
                bool committed = false;
                try
                {
                    owner.Binding.Items.RemoveItem(item);
                    shelf.Items.AddItem(item, noStack: true);
                    moved = shelf.Items.GetItem(uid) == item && owner.Binding.Items.GetItem(uid) == null;
                    if (!moved) throw new InvalidOperationException("Native shop did not accept the exact item UID.");
                    committed = worldStore.CommitNpcInventoryTransaction(job.Id + ":sale", new[] {
                        new NpcInventoryUpdate { PersonId = job.ResidentId, ExpectedRevision = owner.Binding.Revision,
                            WalletGold = inventory.WalletGold + price, Items = NativeNpcInventoryBridge.CaptureItems(owner.Binding.Items) },
                    });
                }
                catch (Exception error) { failure = error.Message; }
                if (!committed)
                {
                    if (moved) shelf.Items.RemoveItem(item);
                    if (owner.Binding.Items.GetItem(uid) == null) owner.Binding.Items.AddItem(item, noStack: true);
                    if (failure == null) failure = "The native sale wallet or exact item revision changed before commit.";
                    return false;
                }
                owner.Binding.Revision++;
                return true;
            });
            if (failure != null) reason = failure;
            else if (completed) reason = null;
            return completed;
        }

        private string NativeLootSaleItemName(NpcActionJob job, ResidentActor actor)
        {
            ulong uid;
            if (job == null || actor == null || job.Target == null || !ulong.TryParse(job.Target.ItemId, out uid)) return null;
            var owner = actor.GetComponent<NativeNpcInventoryOwner>();
            var item = owner == null || owner.Binding == null ? null : owner.Binding.Items.GetItem(uid);
            return item == null ? null : item.LongName;
        }
    }
}
