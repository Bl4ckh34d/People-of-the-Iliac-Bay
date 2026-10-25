using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.Items;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Uses the existing open-shop fixture. It never creates stock, items,
    // merchants, wallets, or save data.
    public static class NativeNpcShopLootSaleProbe
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        public static IEnumerator RunScene(StreetPopulation streets, string expectedCharacter, string preferredResidentId)
        {
            var game = GameManager.Instance;
            Require(Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-shop-loot-sale-probe") >= 0 &&
                expectedCharacter != null && expectedCharacter.StartsWith("PeopleOfTheIliacBayTest-", StringComparison.Ordinal) &&
                game.PlayerEntity.Name == expectedCharacter && streets && streets.Ready && game.PlayerEnterExit.IsPlayerInsideOpenShop,
                "Loot-sale probe requires its owned character, explicit flag, and an open native shop.");

            var store = (WorldStateStore)typeof(StreetPopulation).GetField("worldStore", Private).GetValue(streets);
            ResidentActor actor = null;
            NativeNpcInventoryOwner owner = null;
            DaggerfallUnityItem item = null;
            NpcActionOption sale = null;
            var diagnostics = new List<string>();
            foreach (var candidate in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
            {
                if (preferredResidentId != null && candidate.Resident.Id != preferredResidentId) continue;
                var candidateOwner = candidate.GetComponent<NativeNpcInventoryOwner>();
                if (!candidateOwner || candidateOwner.Binding == null) { diagnostics.Add("resident=" + candidate.Resident.Id + "; binding=missing"); continue; }
                string captureReason;
                if (!NativeNpcInventoryBridge.TryCapture(store, candidateOwner.Binding, out captureReason))
                { diagnostics.Add("resident=" + candidate.Resident.Id + "; capture=" + captureReason); continue; }
                diagnostics.Add("resident=" + candidate.Resident.Id + "; items=" + candidateOwner.Binding.Items.Count);
                for (int index = 0; index < candidateOwner.Binding.Items.Count && sale == null; index++)
                {
                    var candidateItem = candidateOwner.Binding.Items.GetItem(index);
                    if (candidateItem == null) { diagnostics.Add("item[" + index + "]=null"); continue; }
                    if (candidateItem.UID == 0 || candidateItem.stackCount != 1)
                    { diagnostics.Add(ItemState(index,candidateItem,"uid-or-stack")); continue; }
                    var offers = streets.ConversationActionOptions(candidate.Resident.Id,
                        "Could you sell my " + candidateItem.LongName + " to this shop?");
                    bool matched = false;
                    foreach (var option in offers)
                    {
                        if (option.Kind != NpcActionKind.Purchase || option.Target == null ||
                            option.Target.ContainerId != NpcShopLootSale.TargetContainerId ||
                            option.Target.ItemId != candidateItem.UID.ToString(CultureInfo.InvariantCulture) || option.Target.Quantity != 1)
                            continue;
                        matched = true;
                        actor = candidate;
                        owner = candidateOwner;
                        item = candidateItem;
                        sale = option;
                        break;
                    }
                    if (!matched) diagnostics.Add(ItemState(index,candidateItem,"offer=none; options=" + offers.Count));
                }
                if (sale != null) break;
            }
            Require(actor && owner && item != null && sale != null,
                "No actual owned, single-unit, sellable item was offered by the scheduled shop visitor. " +
                string.Join(" | ",diagnostics.ToArray()));
            Require(!item.IsQuestItem && !item.IsEquipped && !item.IsSummoned && !item.RepairData.IsBeingRepaired() && !item.IsArtifact,
                "Sale offer exposed protected, equipped, summoned, repair-pending, or artifact item.");

            int quality = game.PlayerEnterExit.BuildingDiscoveryData.quality;
            int nativeCost = FormulaHelper.CalculateCost(item.value, quality, item.ConditionPercentage);
            var profile = store.ReadNpcActor(actor.Resident.Id, store.ResolveNpcArchetype(actor.Resident.Id));
            int expectedPrice;
            Require(NpcShopLootSale.TryPrice(nativeCost, quality, profile.Skill(14), profile.Profile.Attribute(5), out expectedPrice) &&
                sale.Target.QuotedGold == expectedPrice,
                "Sale offer did not use own Mercantile, own Personality, and native item cost formula.");

            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-named-other-handover-probe")>=0)
                yield return NativeNamedOtherHandoverProbe.Run(streets,actor,game.PlayerEnterExit.Interior);

            string reason;
            var wrongKeeper = Decision(sale);
            wrongKeeper.Option.Target.PersonId = "wrong-native-keeper";
            Require(!NpcProbeCommandSource.TryAcceptAction(streets, actor.Resident.Id, "native-loot-sale-wrong-keeper:" + Guid.NewGuid().ToString("N"),
                wrongKeeper, out reason), "Wrong merchant sale was accepted.");
            Require(store.ActiveNpcAction(actor.Resident.Id) == null, "Wrong merchant sale left active action.");

            var missingItem = Decision(sale);
            missingItem.Option.Target.ItemId = ulong.MaxValue.ToString(CultureInfo.InvariantCulture);
            Require(!NpcProbeCommandSource.TryAcceptAction(streets, actor.Resident.Id, "native-loot-sale-missing-item:" + Guid.NewGuid().ToString("N"),
                missingItem, out reason), "Missing item sale was accepted.");
            Require(store.ActiveNpcAction(actor.Resident.Id) == null, "Missing item sale left active action.");

            var before = store.ReadNpcInventory(actor.Resident.Id);
            int goldBefore = before.WalletGold;
            int playerGold = game.PlayerEntity.GetGoldAmount();
            int ownedCount = owner.Binding.Items.Count;
            ulong uid = item.UID;
            Require(!AnyShelfHasUid(game.PlayerEnterExit.Interior, uid), "Shop shelf already owns NPC sale UID.");
            Require(NpcProbeCommandSource.TryAcceptAction(streets, actor.Resident.Id, "native-loot-sale:" + Guid.NewGuid().ToString("N"),
                Decision(sale), out reason), reason);
            var accepted = store.ActiveNpcAction(actor.Resident.Id);
            Require(accepted != null && accepted.ServiceCode == NpcShopLootSale.ServiceCode && accepted.Target.ItemId == uid.ToString(CultureInfo.InvariantCulture) &&
                accepted.Target.Quantity == 1 && accepted.ServicePrice == expectedPrice, "Accepted sale lost exact UID, quantity, or price.");

            float deadline = Time.realtimeSinceStartup + 90;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (NpcActionExecutor.IsTerminal(store.ReadNpcAction(accepted.Id))) break;
                yield return null;
            }
            var completed = store.ReadNpcAction(accepted.Id);
            Require(completed != null && completed.Phase == NpcActionPhase.Completed, "Native loot sale did not complete: " + (completed == null ? "missing" : completed.FailureReason));
            Require(owner.Binding.Items.GetItem(uid) == null && owner.Binding.Items.Count == ownedCount - 1 &&
                store.ReadNpcInventory(actor.Resident.Id).WalletGold == goldBefore + expectedPrice && game.PlayerEntity.GetGoldAmount() == playerGold,
                "Sale did not remove exact owned UID or credit own wallet once.");
            Require(CountShelvesWithUid(game.PlayerEnterExit.Interior, uid) == 1,
                "Native shop did not receive exact sale UID exactly once.");

            for (int frame = 0; frame < 10; frame++) yield return null;
            Require(owner.Binding.Items.GetItem(uid) == null && store.ReadNpcInventory(actor.Resident.Id).WalletGold == goldBefore + expectedPrice &&
                CountShelvesWithUid(game.PlayerEnterExit.Interior, uid) == 1,
                "Completed sale replay changed ownership or gold.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SHOP_LOOT_SALE_PASS resident=" + actor.Resident.Id + "; uid=" + uid +
                "; quantity=1; gold=" + expectedPrice + "; item=" + item.LongName);
        }

        private static NpcActionDecision Decision(NpcActionOption source)
        {
            return new NpcActionDecision { DurationSeconds = 300, Option = new NpcActionOption {
                Id = source.Id, Kind = source.Kind, Label = source.Label, Target = NpcActionExecutor.CopyTarget(source.Target) } };
        }

        private static bool AnyShelfHasUid(DaggerfallInterior room, ulong uid)
        { return CountShelvesWithUid(room, uid) != 0; }

        private static int CountShelvesWithUid(DaggerfallInterior room, ulong uid)
        {
            int found = 0;
            foreach (var shelf in room.GetComponentsInChildren<DaggerfallLoot>())
                if (shelf.Items.GetItem(uid) != null) found++;
            return found;
        }

        private static string ItemState(int index,DaggerfallUnityItem item,string result)
        {
            return "item[" + index + "]=" + item.LongName + "; uid=" + item.UID + "; stack=" + item.stackCount +
                "; value=" + item.value + "; group=" + item.ItemGroup + "; quest=" + item.IsQuestItem +
                "; equipped=" + item.IsEquipped + "; summoned=" + item.IsSummoned +
                "; repairing=" + item.RepairData.IsBeingRepaired() + "; artifact=" + item.IsArtifact + "; " + result;
        }

        private static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }
    }
}
