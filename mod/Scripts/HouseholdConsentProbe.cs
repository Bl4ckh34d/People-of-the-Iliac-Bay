using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Banking;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>Runs against one real Gothway household and its native private container.</summary>
    public static class HouseholdConsentProbe
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

        public static IEnumerator Run(PeopleOfTheIliacBayMod adapter, StreetPopulation streets)
        {
            if (!adapter || !streets || !streets.Ready)
                throw new InvalidOperationException("Household consent probe requires a ready town.");

            var game = GameManager.Instance;
            var player = game.PlayerEntity;
            var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            int settlementId = game.PlayerGPS.CurrentLocation.MapTableData.MapId;
            var store = (WorldStateStore)typeof(PeopleOfTheIliacBayMod).GetField("store", PrivateInstance).GetValue(adapter);
            var savedWorld = store.Snapshot();
            var town = savedWorld.Settlements.Find(s => s.SettlementId == settlementId);
            if (town == null) throw new InvalidOperationException("Consent probe has no current saved town.");

            Resident resident = null;
            ConversationAttitude attitudeSnapshot = null;
            for (int i = savedWorld.ConversationAttitudes.Count - 1; i >= 0 && resident == null; i--)
            {
                var attitude = savedWorld.ConversationAttitudes[i];
                if (attitude == null || !attitude.HasNativeBaseline || !attitude.HasNativeFactionId) continue;
                foreach (var household in town.Households)
                {
                    var match = household.Residents.Find(r => r.Id == attitude.PersonId && r.IsAlive && r.HomeKey == household.BuildingKey);
                    if (match == null) continue;
                    resident = match;
                    attitudeSnapshot = attitude;
                    break;
                }
            }
            if (resident == null) throw new InvalidOperationException("Dialogue probe left no baselined living home resident.");

            Household home = town.Households.Find(h => h.BuildingKey == resident.HomeKey);
            if (home == null) throw new InvalidOperationException("Talked resident has no exact saved household.");
            BuildingSummary summary;
            if (!directory.GetBuildingSummary(home.BuildingKey, out summary) || summary.FactionId != 0 ||
                summary.BuildingType < DFLocation.BuildingTypes.House1 || summary.BuildingType > DFLocation.BuildingTypes.House6 ||
                RMBLayout.IsShop(summary.BuildingType) || DaggerfallBankManager.IsHouseOwned(home.BuildingKey))
                throw new InvalidOperationException("Talked resident's saved home is not a private residential building.");

            DaggerfallStaticDoors owner;
            StaticDoor entry;
            FindEntry(directory, home.BuildingKey, out owner, out entry);

            int factionId = attitudeSnapshot.NativeFactionId;
            FactionFile.FactionData faction;
            if (!player.FactionData.GetFactionData(factionId, out faction) || faction.sgroup < 0 || faction.sgroup >= player.SGroupReputations.Length)
                throw new InvalidOperationException("Talked resident's native faction cannot be refreshed.");

            var liveStateField = typeof(WorldStateStore).GetField("state", PrivateInstance);
            var liveState = (WorldState)liveStateField.GetValue(store);
            var liveAttitude = liveState.ConversationAttitudes.Find(a => a.PersonId == resident.Id);
            if (liveAttitude == null) throw new InvalidOperationException("Conversation baseline disappeared before consent probe.");

            short[] socialReputations = player.SGroupReputations;
            short oldSocialReputation = socialReputations[faction.sgroup];
            int oldNativeBaseline = liveAttitude.NativeBaseline;
            bool oldHasNativeBaseline = liveAttitude.HasNativeBaseline;
            int oldFactionId = liveAttitude.NativeFactionId;
            bool oldHasFactionId = liveAttitude.HasNativeFactionId;
            int oldOffset = liveAttitude.Offset;
            var otherOffsets = new List<KeyValuePair<ConversationAttitude, int>>();
            foreach (var householdResident in home.Residents)
            {
                if (householdResident == null || householdResident.Id == resident.Id) continue;
                var otherAttitude = liveState.ConversationAttitudes.Find(a => a.PersonId == householdResident.Id);
                if (otherAttitude == null) continue;
                otherOffsets.Add(new KeyValuePair<ConversationAttitude, int>(otherAttitude, otherAttitude.Offset));
                otherAttitude.Offset = -200;
            }
            var priorCrime = player.CrimeCommitted;
            Vector3 priorPosition = game.PlayerMotor.transform.position;
            Quaternion priorRotation = game.PlayerMotor.transform.rotation;
            bool discoveredBefore = game.PlayerGPS.HasDiscoveredBuilding(home.BuildingKey);
            DaggerfallInterior interior = null;
            DaggerfallInventoryWindow inventory = null;
            DaggerfallLoot loot = null;
            List<DaggerfallUnityItem> originalContents = null;
            int oldStockedDate = 0;
            bool oldHouseOwned = false, oldPlayerOwned = false;
            DaggerfallUnityItem transferItem = null;

            try
            {
                // Lift this NPC's current native reaction to 100, then use the stored
                // personal offset to land exactly on the two sides of the consent gate.
                socialReputations[faction.sgroup] = 100;
                int freshNative = Clamp(game.TalkManager.GetReactionToPlayer(faction));
                if (freshNative != 100) throw new InvalidOperationException("Temporary native reaction did not reach 100.");
                store.SetConversationBaseline(resident.Id, freshNative, DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(), factionId);

                TownSmokeHarness.EnterInterior(owner.transform, entry);
                yield return new WaitForSecondsRealtime(1f);
                DaggerfallUI.Instance.PopToHUD();
                interior = game.PlayerEnterExit.Interior;
                if (!game.PlayerEnterExit.IsPlayerInsideBuilding || !interior || interior.EntryDoor.buildingKey != home.BuildingKey)
                    throw new InvalidOperationException("Native transition did not enter the resident's real home.");

                foreach (var candidate in interior.GetComponentsInChildren<DaggerfallLoot>(true))
                    if (candidate && candidate.ContainerType == LootContainerTypes.HouseContainers && !candidate.playerOwned)
                    { loot = candidate; break; }
                if (!loot) throw new InvalidOperationException("Real home has no native HouseContainers loot target.");

                originalContents = new List<DaggerfallUnityItem>();
                for (int i = 0; i < loot.Items.Count; i++) originalContents.Add(loot.Items.GetItem(i));
                oldStockedDate = loot.stockedDate;
                oldHouseOwned = loot.houseOwned;
                oldPlayerOwned = loot.playerOwned;
                loot.stockedDate = DaggerfallLoot.CreateStockedDate(DaggerfallUnity.Instance.WorldTime.Now);
                loot.houseOwned = true;
                loot.playerOwned = false;
                transferItem = ItemBuilder.CreateWeapon(Weapons.Dagger, WeaponMaterialTypes.Iron);
                loot.Items.AddItem(transferItem);

                inventory = new DaggerfallInventoryWindow(DaggerfallUI.UIManager) { LootTarget = loot };
                DaggerfallUI.UIManager.PushWindow(inventory);
                yield return null;

                var runtimeDecision = typeof(StreetPopulation).GetMethod("CanTakeHouseholdLoot", PrivateInstance);
                if (runtimeDecision == null) throw new InvalidOperationException("Runtime household consent gate is unavailable.");
                var theftHooks = Type.GetType("PeopleOfTheIliacBay.QuestHooks.NativeHouseholdLootConsentHooks, PeopleOfTheIliacBay.QuestHooks", true);
                var beforeTheft = theftHooks.GetMethod("BeforeTheft", PrivateStatic);
                if (beforeTheft == null) throw new InvalidOperationException("Native theft prefix is unavailable.");

                liveAttitude.Offset = 74 - freshNative;
                int disposition;
                if (!adapter.TryGetFreshConversationDisposition(resident.Id, out disposition) || disposition != 74 ||
                    (bool)runtimeDecision.Invoke(streets, new object[] { adapter, inventory }) ||
                    !(bool)beforeTheft.Invoke(null, new object[] { inventory }))
                    throw new InvalidOperationException("+74 did not deny household consent and preserve native theft handling.");

                liveAttitude.Offset = 75 - freshNative;
                if (!adapter.TryGetFreshConversationDisposition(resident.Id, out disposition) || disposition != 75 ||
                    !(bool)runtimeDecision.Invoke(streets, new object[] { adapter, inventory }) ||
                    (bool)beforeTheft.Invoke(null, new object[] { inventory }))
                    throw new InvalidOperationException("+75 did not grant consent through the native theft prefix.");

                // Explicit policy exclusions: a visitor's conversation cannot make a
                // different household's containers consented; shops and inns stay theft-sensitive.
                Household otherHome = town.Households.Find(h => h.BuildingKey != home.BuildingKey);
                Func<string, int?> consent = id => id == resident.Id ? (int?)75 : null;
                if (otherHome != null && HouseholdLootConsent.AllowsPrivateHomeContainer(settlementId,
                    home.BuildingKey, otherHome, true, false, false, false, false, false, false, consent))
                    throw new InvalidOperationException("Visitor consent crossed into a different household.");
                if (HouseholdLootConsent.AllowsPrivateHomeContainer(settlementId, home.BuildingKey, home,
                    true, true, false, false, false, false, false, consent) ||
                    HouseholdLootConsent.AllowsPrivateHomeContainer(settlementId, home.BuildingKey, home,
                    true, false, true, false, false, false, false, consent))
                    throw new InvalidOperationException("Shop or inn stock accepted household consent.");

                int removeMode = Convert.ToInt32(Enum.Parse(
                    typeof(DaggerfallInventoryWindow).GetNestedType("ActionModes", BindingFlags.NonPublic), "Remove"));
                var actionModes = typeof(DaggerfallInventoryWindow).GetNestedType("ActionModes", BindingFlags.NonPublic);
                var click = typeof(DaggerfallInventoryWindow).GetMethod("RemoteItemListScroller_OnItemClick", PrivateInstance,
                    null, new[] { typeof(DaggerfallUnityItem), actionModes }, null);
                if (click == null) throw new InvalidOperationException("Native inventory transfer entry point is unavailable.");
                click.Invoke(inventory, new object[] { transferItem, Enum.ToObject(actionModes, removeMode) });
                inventory.CloseWindow();
                inventory = null;
                if (!player.Items.Contains(transferItem) || loot.Items.Contains(transferItem) || player.CrimeCommitted != priorCrime)
                    throw new InvalidOperationException("+75 native transfer or close flagged Theft or failed to transfer the item.");

                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_CONSENT_PASS: resident=" + resident.Id + "; home=" + home.BuildingKey +
                    "; +74 denied; +75 native transfer/close clean; visitor/shop/inn exclusions.");
                player.Items.RemoveItem(transferItem);
                RestoreLoot(loot, originalContents, oldStockedDate, oldHouseOwned, oldPlayerOwned);
                game.PlayerEnterExit.TransitionExterior();
                yield return new WaitForSecondsRealtime(1f);
            }
            finally
            {
                if (inventory != null) DaggerfallUI.Instance.PopToHUD();
                if (transferItem != null) player.Items.RemoveItem(transferItem);
                RestoreLoot(loot, originalContents, oldStockedDate, oldHouseOwned, oldPlayerOwned);
                socialReputations[faction.sgroup] = oldSocialReputation;
                liveAttitude.NativeBaseline = oldNativeBaseline;
                liveAttitude.HasNativeBaseline = oldHasNativeBaseline;
                liveAttitude.NativeFactionId = oldFactionId;
                liveAttitude.HasNativeFactionId = oldHasFactionId;
                liveAttitude.Offset = oldOffset;
                foreach (var item in otherOffsets) item.Key.Offset = item.Value;
                player.CrimeCommitted = priorCrime;
                if (game.PlayerEnterExit.IsPlayerInsideBuilding) game.PlayerEnterExit.TransitionExterior();
                if (!discoveredBefore) game.PlayerGPS.UndiscoverBuilding(home.BuildingKey);
                game.PlayerMotor.transform.position = priorPosition;
                game.PlayerMotor.transform.rotation = priorRotation;
                Physics.SyncTransforms();
                DaggerfallUI.Instance.PopToHUD();
            }
        }

        private static void FindEntry(BuildingDirectory directory, int key, out DaggerfallStaticDoors owner, out StaticDoor entry)
        {
            owner = null;
            entry = default(StaticDoor);
            foreach (var candidate in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
            {
                if (candidate.Doors == null) continue;
                foreach (var door in candidate.Doors)
                    if (door.doorType == DoorTypes.Building && door.buildingKey == key)
                    { owner = candidate; entry = door; return; }
            }
            throw new InvalidOperationException("Resident home has no exterior entrance.");
        }

        private static int Clamp(int value) { return Math.Max(-100, Math.Min(100, value)); }

        private static void RestoreLoot(DaggerfallLoot loot, List<DaggerfallUnityItem> items, int stockedDate,
            bool houseOwned, bool playerOwned)
        {
            if (!loot) return;
            loot.Items.Clear();
            if (items != null)
                foreach (var item in items) if (item != null) loot.Items.AddItem(item);
            loot.stockedDate = stockedDate;
            loot.houseOwned = houseOwned;
            loot.playerOwned = playerOwned;
        }
    }
}
