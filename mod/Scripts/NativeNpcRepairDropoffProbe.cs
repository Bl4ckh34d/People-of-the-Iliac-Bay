using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Opt-in acceptance of the existing native-item-repair service. It uses one
    // existing NPC-owned UID and the normal Purchase runtime; no service state,
    // stock, actor, or repair completion is synthesized by the probe.
    internal static class NativeNpcRepairDropoffProbe
    {
        internal const string RunFlag = "-peopleoftheiliacbay-native-repair-probe";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private sealed class Fixture
        {
            internal ResidentActor Actor;
            internal NativeNpcInventoryOwner Owner;
            internal DaggerfallUnityItem Item;
            internal int DamagedCondition, Price, Duration;
        }

        internal static IEnumerator Run(PeopleOfTheIliacBayMod host, StreetPopulation streets, string character)
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), RunFlag) < 0 || string.IsNullOrWhiteSpace(character) ||
                !character.StartsWith("PeopleOfTheIliacBayTest-", StringComparison.Ordinal))
                throw new InvalidOperationException("Native repair probe requires its explicit flag and owned disposable character.");
            var game = GameManager.Instance;
            var store = Store(host);
            if (!host || !streets || !streets.Ready || !game || game.PlayerEntity == null || game.PlayerEntity.Name != character ||
                !game.PlayerEnterExit.Interior || !game.PlayerEnterExit.IsPlayerInsideOpenShop ||
                !RMBLayout.IsRepairShop(game.PlayerEnterExit.Interior.BuildingData.BuildingType) ||
                !PlayerActivate.IsBuildingOpen(game.PlayerEnterExit.Interior.BuildingData.BuildingType) || store == null)
                throw new InvalidOperationException("Native repair probe requires an open, loaded native repair shop on its owned character.");

            Fixture fixture = FindFixture(streets, store);
            if (fixture == null)
                throw new InvalidOperationException("Native repair probe needs a nearby eligible NPC with one repairable owned item and enough own gold.");
            fixture.Item.currentCondition = fixture.DamagedCondition;
            string captureReason;
            if (!NativeNpcInventoryBridge.TryCapture(store, fixture.Owner.Binding, out captureReason))
                throw new InvalidOperationException("Native repair probe could not persist the controlled damaged owned item: " + captureReason);

            string itemName = fixture.Item.LongName;
            ulong itemUid = fixture.Item.UID;
            string residentId = fixture.Actor.Resident.Id;
            int walletBefore = store.ReadNpcInventory(residentId).WalletGold;
            NpcActionOption dropoff = FindRepairOption(streets, residentId, "Repair my " + itemName + ".", itemUid, false);
            if (dropoff == null || dropoff.Target.QuotedGold != fixture.Price || dropoff.Target.QuotedGold <= 0)
                throw new InvalidOperationException("Native repair drop-off did not retain the exact native repair quote for the owned UID.");
            string reason;
            if (!NpcProbeCommandSource.TryAcceptAction(streets, residentId, "native-repair-dropoff:" + Guid.NewGuid().ToString("N"),
                new NpcActionDecision { Option = Copy(dropoff), DurationSeconds = 300 }, out reason))
                throw new InvalidOperationException("Native repair drop-off was not accepted: " + reason);
            var acceptedDropoff = store.ActiveNpcAction(residentId);
            if (acceptedDropoff == null || acceptedDropoff.ServiceCode != NpcShopCommerce.RepairServiceCode ||
                acceptedDropoff.Target.ItemId != itemUid.ToString(CultureInfo.InvariantCulture) || acceptedDropoff.ServicePrice != fixture.Price ||
                acceptedDropoff.Target.QuotedGold != fixture.Price || acceptedDropoff.Target.Quantity != 1)
                throw new InvalidOperationException("Native repair drop-off lost its exact UID, native price, or single-item contract.");
            yield return WaitForTerminal(store, acceptedDropoff.Id, 180f);
            var dropped = store.ReadNpcAction(acceptedDropoff.Id);
            if (dropped == null || dropped.Phase != NpcActionPhase.Completed)
                throw new InvalidOperationException("Native repair drop-off did not complete: " + TerminalReason(dropped));
            DaggerfallUnityItem live = fixture.Owner.Binding.Items.GetItem(itemUid);
            if (live == null || live.UID != itemUid || !live.RepairData.IsBeingRepaired() || live.currentCondition != fixture.DamagedCondition ||
                live.RepairData.RepairTime != fixture.Duration || store.ReadNpcInventory(residentId).WalletGold != walletBefore - fixture.Price)
                throw new InvalidOperationException("Native repair drop-off did not preserve UID, native duration, pending condition, or one own-wallet payment.");
            bool replayApplied = false;
            if (store.TryCompleteNpcActionEffect(dropped.Id, dropped.Revision, Now(), () => { replayApplied = true; return true; }) || replayApplied)
                throw new InvalidOperationException("Completed native repair drop-off accepted a duplicate effect.");
            if (FindRepairOption(streets, residentId, "Collect my " + itemName + ".", itemUid, true) != null ||
                live.RepairData.IsRepairFinished() || live.currentCondition == live.maxCondition)
                throw new InvalidOperationException("Native repair collection became available before the native repair duration completed.");

            ulong dueAt = live.RepairData.GetTimeDone();
            double now = Now();
            if (dueAt <= now) throw new InvalidOperationException("Native repair duration was not future-dated after drop-off.");
            DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)(dueAt - now + 1));
            yield return null;
            live = fixture.Owner.Binding.Items.GetItem(itemUid);
            if (live == null || !live.RepairData.IsBeingRepaired() || !live.RepairData.IsRepairFinished() || live.currentCondition != fixture.DamagedCondition)
                throw new InvalidOperationException("Native repair duration did not finish while preserving damaged condition until collection.");

            NpcActionOption collect = FindRepairOption(streets, residentId, "Collect my " + itemName + ".", itemUid, true);
            if (collect == null || collect.Target.QuotedGold != 0 || collect.Target.PersonId != dropped.Target.PersonId ||
                collect.Target.SettlementId != dropped.Target.SettlementId || collect.Target.BuildingKey != dropped.Target.BuildingKey)
                throw new InvalidOperationException("Finished native repair could not be collected from the same open repair keeper without a second quote.");
            int walletAfterDropoff = store.ReadNpcInventory(residentId).WalletGold;
            if (!NpcProbeCommandSource.TryAcceptAction(streets, residentId, "native-repair-collect:" + Guid.NewGuid().ToString("N"),
                new NpcActionDecision { Option = Copy(collect), DurationSeconds = 300 }, out reason))
                throw new InvalidOperationException("Native repair collection was not accepted: " + reason);
            var acceptedCollection = store.ActiveNpcAction(residentId);
            if (acceptedCollection == null || acceptedCollection.ServiceCode != NpcShopCommerce.RepairServiceCode ||
                acceptedCollection.ServicePrice != 0 || acceptedCollection.Target.QuotedGold != 0 ||
                acceptedCollection.Target.ItemId != itemUid.ToString(CultureInfo.InvariantCulture))
                throw new InvalidOperationException("Native repair collection lost same UID or introduced a second payment.");
            yield return WaitForTerminal(store, acceptedCollection.Id, 180f);
            var collected = store.ReadNpcAction(acceptedCollection.Id);
            live = fixture.Owner.Binding.Items.GetItem(itemUid);
            if (collected == null || collected.Phase != NpcActionPhase.Completed || live == null || live.UID != itemUid ||
                live.RepairData.IsBeingRepaired() || live.currentCondition != live.maxCondition ||
                store.ReadNpcInventory(residentId).WalletGold != walletAfterDropoff)
                throw new InvalidOperationException("Native repair collection did not restore the exact owned item only after completion or charged twice.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_REPAIR_PASS: resident=" + residentId + "; keeper=" + dropped.Target.PersonId +
                "; itemUid=" + itemUid + "; price=" + fixture.Price + "; duration=" + fixture.Duration +
                "; onePayment=true; pendingCondition=true; collectedCondition=full; sameKeeper=true.");
        }

        private static Fixture FindFixture(StreetPopulation streets, WorldStateStore store)
        {
            foreach (ResidentActor actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
            {
                if (!actor || !actor.gameObject.activeInHierarchy || actor.Resident == null || !actor.Resident.IsAlive || actor.IsSleeping ||
                    actor.IsConversing || actor.Resident.IsQuestProtected || streets.IsAlarmed(actor.Resident.Id) ||
                    Vector3.Distance(actor.transform.position, GameManager.Instance.PlayerMotor.transform.position) > 8f) continue;
                var owner = actor.GetComponent<NativeNpcInventoryOwner>();
                string ignored;
                if (!owner || owner.Binding == null || !NativeNpcInventoryBridge.TryCapture(store, owner.Binding, out ignored)) continue;
                var inventory = store.ReadNpcInventory(actor.Resident.Id);
                if (inventory == null) continue;
                for (int index = 0; index < owner.Binding.Items.Count; index++)
                {
                    DaggerfallUnityItem item = owner.Binding.Items.GetItem(index);
                    if (item == null || item.UID == 0 || item.IsQuestItem || item.IsEquipped || item.IsSummoned || item.ItemTemplate.isNotRepairable ||
                        item.RepairData.IsBeingRepaired() || item.maxCondition < 2 || item.stackCount != 1 ||
                        item.IsEnchanted && !DaggerfallUnity.Settings.AllowMagicRepairs) continue;
                    int condition = Math.Max(1, item.maxCondition - 1);
                    int price = FormulaHelper.CalculateItemRepairCost(item.value, GameManager.Instance.PlayerEnterExit.BuildingDiscoveryData.quality,
                        condition, item.maxCondition, null);
                    int duration = FormulaHelper.CalculateItemRepairTime(condition, item.maxCondition);
                    if (price > 0 && duration >= DaggerfallDateTime.SecondsPerDay && price <= inventory.WalletGold)
                        return new Fixture { Actor = actor, Owner = owner, Item = item, DamagedCondition = condition, Price = price, Duration = duration };
                }
            }
            return null;
        }

        private static NpcActionOption FindRepairOption(StreetPopulation streets, string residentId, string request, ulong itemUid, bool collection)
        {
            foreach (NpcActionOption option in streets.ConversationActionOptions(residentId, request))
                if (option != null && option.Kind == NpcActionKind.Purchase && option.Target != null &&
                    option.Target.ContainerId == NpcShopCommerce.RepairTargetContainerId && option.Target.ItemId == itemUid.ToString(CultureInfo.InvariantCulture) &&
                    (collection ? option.Target.QuotedGold == 0 : option.Target.QuotedGold > 0)) return option;
            return null;
        }

        private static IEnumerator WaitForTerminal(WorldStateStore store, string actionId, float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                NpcActionJob job = store.ReadNpcAction(actionId);
                if (job == null || NpcActionExecutor.IsTerminal(job)) yield break;
                yield return null;
            }
        }

        private static NpcActionOption Copy(NpcActionOption option)
        { return new NpcActionOption { Id = option.Id, Kind = option.Kind, Label = option.Label, ItemSourceStep = option.ItemSourceStep,
            MaximumQuantity = option.MaximumQuantity, Target = NpcActionExecutor.CopyTarget(option.Target) }; }
        private static WorldStateStore Store(PeopleOfTheIliacBayMod host)
        { var field = typeof(PeopleOfTheIliacBayMod).GetField("store", Private); return field == null ? null : field.GetValue(host) as WorldStateStore; }
        private static double Now() { return DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(); }
        private static string TerminalReason(NpcActionJob job) { return job == null ? "missing" : job.Phase + " " + job.FailureReason; }
    }
}
