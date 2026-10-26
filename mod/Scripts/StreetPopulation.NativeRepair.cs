using System;
using System.Collections.Generic;
using System.Globalization;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        // Native ItemRepairData owns completion timing, shop identity and save
        // payload. Drop-off and later explicit collection keep the same NPC UID.
        private bool IsNativeRepairTarget(NpcActionTarget target)
        {
            return target != null && target.Kind == NpcActionTargetKind.IndoorPoint &&
                target.ContainerId == NpcShopCommerce.RepairTargetContainerId;
        }

        private bool TryNativeRepairKeeper(out NativePerson keeper, out StaticNPC npc)
        {
            keeper = null;
            npc = null;
            if (!interior || !RMBLayout.IsRepairShop(interior.BuildingData.BuildingType) || !TryShopSeller(out keeper))
                return false;
            foreach (var candidate in interior.GetComponentsInChildren<StaticNPC>())
            {
                if (!candidate.gameObject.activeInHierarchy || !NativeBusinessServices.IsKeeper(candidate, interior.BuildingData.BuildingType))
                    continue;
                var candidateKeeper = KeeperPerson(candidate);
                if (candidateKeeper != null && candidateKeeper.Id == keeper.Id)
                {
                    npc = candidate;
                    return true;
                }
            }
            keeper = null;
            return false;
        }

        private bool TryNativeRepairApproach(ResidentActor actor, StaticNPC keeper, out Vector3 chosen)
        {
            chosen = Vector3.zero;
            if (!actor || !keeper || !interior || indoorGeometry==null || shopGeometry==null)
                return false;
            Vector3 localKeeper = interior.transform.InverseTransformPoint(keeper.transform.position);
            Vector3 from = interior.transform.InverseTransformPoint(actor.transform.position - Vector3.up);
            foreach (var local in indoorGeometry.NearbyApproachPoints(localKeeper, 1.3f, 2.5f, 24))
            {
                Vector3 world = interior.transform.TransformPoint(local);
                if (Vector3.Distance(world + Vector3.up, keeper.transform.position) > 2.5f ||
                    !CivilianResponses.Sees(world + Vector3.up, keeper.transform.position, 3.5f))
                    continue;
                if (Vector3.Distance(from, local) > 0.25f && shopGeometry.ActivityRouteBetween(from, local).Count < 2)
                    continue;
                chosen = world;
                return true;
            }
            return false;
        }

        private static bool RepairableOwnedNpcItem(DaggerfallUnityItem item)
        {
            // A repair receipt identifies one native UID. Do not mark an entire
            // mutable stack as though it were a single independently collected item.
            return item != null && item.UID != 0 && item.stackCount == 1 && !item.IsQuestItem && !item.IsEquipped && !item.IsSummoned &&
                !item.ItemTemplate.isNotRepairable && item.currentCondition < item.maxCondition &&
                !item.RepairData.IsBeingRepaired() &&
                (!item.IsEnchanted || DaggerfallUnity.Settings.AllowMagicRepairs);
        }

        private static bool TryNativeRepairQuote(DaggerfallUnityItem item, int quality, out int price, out int duration)
        {
            price = 0;
            duration = 0;
            if (!RepairableOwnedNpcItem(item) || quality < 0 || quality > 20)
                return false;
            price = FormulaHelper.CalculateItemRepairCost(item.value, quality, item.currentCondition, item.maxCondition, null);
            duration = FormulaHelper.CalculateItemRepairTime(item.currentCondition, item.maxCondition);
            return price > 0 && duration >= DaggerfallDateTime.SecondsPerDay;
        }

        private static bool CollectableNativeRepairItem(DaggerfallUnityItem item)
        {
            return item != null && item.UID != 0 && item.stackCount == 1 && item.RepairData.IsBeingRepaired() &&
                item.RepairData.IsRepairFinished() && item.RepairData.IsBeingRepairedHere();
        }

        private void OfferNativeRepairAction(ResidentActor actor, string text, List<NpcActionOption> options, bool semantic = true)
        {
            if (!actor || options == null || options.Count >= (semantic ? NpcActionCandidateCatalog.MaximumCandidates : 24))
                return;
            NativePerson keeper;
            StaticNPC keeperNpc;
            if (!TryNativeRepairKeeper(out keeper, out keeperNpc))
                return;
            var owner = actor.GetComponent<NativeNpcInventoryOwner>();
            string reason;
            if (!owner || owner.Binding == null || !NativeNpcInventoryBridge.TryCapture(worldStore, owner.Binding, out reason))
                return;
            Vector3 approach = Vector3.zero;
            if (!TryNativeRepairApproach(actor, keeperNpc, out approach))
                return;
            int quality = GameManager.Instance.PlayerEnterExit.BuildingDiscoveryData.quality;
            var inventory = worldStore.ReadNpcInventory(actor.Resident.Id);
            if (inventory == null)
                return;
            for (int index = 0; index < owner.Binding.Items.Count && options.Count < (semantic ? NpcActionCandidateCatalog.MaximumCandidates : 24); index++)
            {
                var item = owner.Binding.Items.GetItem(index);
                int price, duration;
                bool collection = CollectableNativeRepairItem(item);
                if (collection)
                {
                    price = 0;
                }
                else if (!TryNativeRepairQuote(item, quality, out price, out duration) || price > inventory.WalletGold)
                    continue;
                var target = ActionPosition(approach);
                target.PersonId = keeper.Id;
                target.ContainerId = NpcShopCommerce.RepairTargetContainerId;
                target.ItemId = item.UID.ToString(CultureInfo.InvariantCulture);
                target.Quantity = 1;
                target.QuotedGold = price;
                options.Add(new NpcActionOption
                {
                    Id = options.Count + 1,
                    Kind = NpcActionKind.Purchase,
                    Target = target,
                    Label = collection ? "collect my finished " + item.LongName + " from this same native repair keeper" :
                        "leave my damaged " + item.LongName + " with this native repair keeper for " +
                        price.ToString(CultureInfo.InvariantCulture) + " gold from my own wallet; native repair remains pending until collected",
                });
                if (!semantic)
                    return;
            }
        }

        private bool PrepareNativeRepairAction(ResidentActor actor, NpcActionJob job, out string reason)
        {
            reason = "The exact native repair service or owned damaged item is no longer available.";
            if (!actor || job == null || !IsNativeRepairTarget(job.Target))
                return false;
            NativePerson keeper;
            StaticNPC keeperNpc;
            if (!TryNativeRepairKeeper(out keeper, out keeperNpc) || job.Target.PersonId != keeper.Id)
                return false;
            var owner = actor.GetComponent<NativeNpcInventoryOwner>();
            ulong uid;
            if (!owner || owner.Binding == null || !ulong.TryParse(job.Target.ItemId, NumberStyles.None, CultureInfo.InvariantCulture, out uid) ||
                !NativeNpcInventoryBridge.TryCapture(worldStore, owner.Binding, out reason))
                return false;
            var item = owner.Binding.Items.GetItem(uid);
            int price, duration;
            int quality = GameManager.Instance.PlayerEnterExit.BuildingDiscoveryData.quality;
            var inventory = worldStore.ReadNpcInventory(actor.Resident.Id);
            Vector3 approach = Vector3.zero;
            bool collection = job.Target.QuotedGold == 0;
            if (collection)
            {
                if (!CollectableNativeRepairItem(item) || inventory == null || inventory.Revision != owner.Binding.Revision ||
                    !TryNativeRepairApproach(actor, keeperNpc, out approach))
                    return false;
                price = 0;
            }
            else if (!TryNativeRepairQuote(item, quality, out price, out duration) || price != job.Target.QuotedGold ||
                inventory == null || inventory.Revision != owner.Binding.Revision || inventory.WalletGold < price ||
                !TryNativeRepairApproach(actor, keeperNpc, out approach))
                return false;
            var target = ActionPosition(approach);
            target.PersonId = keeper.Id;
            target.ContainerId = NpcShopCommerce.RepairTargetContainerId;
            target.ItemId = uid.ToString(CultureInfo.InvariantCulture);
            target.Quantity = 1;
            target.QuotedGold = price;
            job.Target = target;
            job.ServiceCode = NpcShopCommerce.RepairServiceCode;
            job.ServicePrice = price;
            job.ServiceStep = 0;
            job.ExpectedInventoryRevision = owner.Binding.Revision;
            return true;
        }

        private bool ExecuteNativeRepairAction(NpcActionJob requested, ResidentActor actor, double now, out string reason)
        {
            reason = "The native repair drop-off, owner or wallet changed.";
            if (requested == null || actor == null || requested.ServiceCode != NpcShopCommerce.RepairServiceCode ||
                !IsNativeRepairTarget(requested.Target))
                return false;
            var owner = actor.GetComponent<NativeNpcInventoryOwner>();
            if (!owner || owner.Binding == null || owner.Binding.PersonId != requested.ResidentId ||
                owner.Binding.Revision != requested.ExpectedInventoryRevision)
                return false;
            string failure = null;
            bool completed = worldStore.TryCompleteNpcActionEffect(requested.Id, requested.Revision, now, () =>
            {
                var job = worldStore.ReadNpcAction(requested.Id);
                NativePerson keeper;
                StaticNPC keeperNpc;
                ulong uid;
                if (job == null || job.Revision != requested.Revision || job.ServiceCode != NpcShopCommerce.RepairServiceCode ||
                    job.Phase != NpcActionPhase.Executing || job.ExpectedInventoryRevision != owner.Binding.Revision ||
                    !TryNativeRepairKeeper(out keeper, out keeperNpc) || job.Target.PersonId != keeper.Id ||
                    !ulong.TryParse(job.Target.ItemId, NumberStyles.None, CultureInfo.InvariantCulture, out uid) ||
                    Vector3.Distance(actor.transform.position - Vector3.up, ActionWorldPoint(job.Target)) > 0.75f ||
                    Vector3.Distance(actor.transform.position, keeperNpc.transform.position) > 3.5f)
                    return false;
                var item = owner.Binding.Items.GetItem(uid);
                int price, duration;
                int quality = GameManager.Instance.PlayerEnterExit.BuildingDiscoveryData.quality;
                var inventory = worldStore.ReadNpcInventory(job.ResidentId);
                bool collection = job.ServicePrice == 0;
                if (collection)
                {
                    if (!CollectableNativeRepairItem(item) || job.Target.QuotedGold != 0 || inventory == null ||
                        inventory.Revision != owner.Binding.Revision)
                        return false;
                    price = 0;
                    duration = 0;
                }
                else if (!TryNativeRepairQuote(item, quality, out price, out duration) || price != job.ServicePrice ||
                    price != job.Target.QuotedGold || inventory == null || inventory.Revision != owner.Binding.Revision ||
                    inventory.WalletGold < price)
                    return false;
                int priorCondition = item.currentCondition;
                var priorRepair = item.RepairData.GetSaveData();
                if (collection)
                {
                    // This mirrors the native repair window's finished-item path:
                    // condition becomes whole only when collected at this same shop.
                    item.currentCondition = item.maxCondition;
                    item.RepairData.Collect();
                }
                else
                    item.RepairData.LeaveForRepair(duration);
                if (!collection && !item.RepairData.IsBeingRepaired())
                {
                    failure = "The native repair service did not accept the exact item.";
                    return false;
                }
                bool committed = false;
                try
                {
                    committed = worldStore.CommitNpcInventoryTransaction(job.Id + ":repair", new[]
                    {
                        new NpcInventoryUpdate
                        {
                            PersonId = job.ResidentId,
                            ExpectedRevision = owner.Binding.Revision,
                            WalletGold = inventory.WalletGold - price,
                            Items = NativeNpcInventoryBridge.CaptureItems(owner.Binding.Items),
                        },
                    });
                }
                catch (Exception error)
                {
                    failure = error.Message;
                }
                if (!committed)
                {
                    item.currentCondition = priorCondition;
                    if (priorRepair == null)
                        item.RepairData.Collect();
                    else
                        item.RepairData.RestoreRepairData(priorRepair);
                    if (failure == null)
                        failure = "The repair wallet or exact item revision changed before native drop-off.";
                    return false;
                }
                owner.Binding.Revision++;
                return true;
            });
            if (failure != null)
                reason = failure;
            else if (completed)
                reason = null;
            return completed;
        }
    }
}
