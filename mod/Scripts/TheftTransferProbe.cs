using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public static class TheftTransferProbe
    {
        public static IEnumerator Run(StreetPopulation streets, bool shop = true)
        {
            var game = GameManager.Instance;
            var room = game.PlayerEnterExit.Interior;
            if (!room || (shop && room.EntryDoor.buildingKey != 131343)) throw new InvalidOperationException("Theft transfer fixture missing.");
            var hidden = new HashSet<GameObject>();
            foreach (var npc in room.GetComponentsInChildren<StaticNPC>()) hidden.Add(npc.gameObject);
            foreach (var npc in room.GetComponentsInChildren<MobilePersonNPC>()) hidden.Add(npc.gameObject);
            foreach (var npc in room.GetComponentsInChildren<GuardActor>()) hidden.Add(npc.gameObject);
            var item = ItemBuilder.CreateWeapon(Weapons.Dagger, WeaponMaterialTypes.Iron);
            var priorCrime = game.PlayerEntity.CrimeCommitted;
            GameObject privateContainer = null;
            try
            {
                foreach (var obj in hidden) obj.SetActive(false);
                if (!shop)
                {
                    privateContainer = new GameObject("Private theft test container");
                    var loot = privateContainer.AddComponent<DaggerfallLoot>();
                    loot.houseOwned = true; loot.Items.AddItem(item);
                    var inventory = new DaggerfallInventoryWindow(DaggerfallUI.UIManager) { LootTarget = loot };
                    DaggerfallUI.UIManager.PushWindow(inventory);
                    yield return null;
                    if (streets.HasTheftWitness() != false) throw new InvalidOperationException("Controlled empty home has a witness.");
                    var modes = typeof(DaggerfallInventoryWindow).GetNestedType("ActionModes", BindingFlags.NonPublic);
                    typeof(DaggerfallInventoryWindow).GetMethod("RemoteItemListScroller_OnItemClick", BindingFlags.Instance | BindingFlags.NonPublic,
                        null, new[] { typeof(DaggerfallUnityItem), modes }, null).Invoke(inventory, new[] { (object)item, Enum.Parse(modes, "Remove") });
                    inventory.CloseWindow();
                    if (!game.PlayerEntity.Items.Contains(item) || loot.Items.Contains(item) || game.PlayerEntity.CrimeCommitted != priorCrime)
                        throw new InvalidOperationException("Private-property theft failed transfer or added crime on inventory close.");
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PRIVATE_THEFT_PASS: native private loot transferred and inventory close added no crime.");
                    yield break;
                }
                var window = new DaggerfallTradeWindow(DaggerfallUI.UIManager, null, DaggerfallTradeWindow.WindowModes.Buy);
                DaggerfallUI.UIManager.PushWindow(window);
                yield return null;
                if (streets.HasTheftWitness() != false) throw new InvalidOperationException("Controlled empty shop has a witness.");
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                var basket = (ItemCollection)typeof(DaggerfallTradeWindow).GetField("basketItems", flags).GetValue(window);
                basket.AddItem(item);
                typeof(DaggerfallTradeWindow).GetField("cost", flags).SetValue(window, 1);
                typeof(DaggerfallTradeWindow).GetMethod("DoSteal", flags).Invoke(window, null);
                if (!game.PlayerEntity.Items.Contains(item) || basket.Contains(item) || game.PlayerEntity.CrimeCommitted != priorCrime)
                    throw new InvalidOperationException("Unobserved shop theft failed actual item transfer or changed crime.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_THEFT_TRANSFER_PASS: native shop basket transferred to player without crime.");
            }
            finally
            {
                DaggerfallUI.Instance.PopToHUD();
                game.PlayerEntity.Items.RemoveItem(item);
                if (privateContainer) UnityEngine.Object.Destroy(privateContainer);
                foreach (var obj in hidden) if (obj) obj.SetActive(true);
            }
        }
    }
}
