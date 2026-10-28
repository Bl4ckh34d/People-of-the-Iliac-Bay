using System;
using System.Collections.Generic;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    internal static class NativeMarketStock
    {
        internal static ItemCollection CreateInitialStock(WorldStateStore store, string personId, NativeMarketPost post)
        {
            if (store == null || string.IsNullOrWhiteSpace(personId) || post == null)
                throw new ArgumentException("Market stock lacks its observed owner and post.");
            var items = new ItemCollection(); var seen = new HashSet<int>();
            foreach (var evidence in post.Goods)
            {
                int key = evidence.Archive * 1000 + evidence.Record;
                string name; int price;
                if (!seen.Add(key) || !NativeMarketGoodsItem.Describe(evidence.Archive, evidence.Record, out name, out price)) continue;
                var item = new NativeMarketGoodsItem(evidence.Archive, evidence.Record);
                item.stackCount = 3 + (int)(StableSeed.Combine(store.WorldSeed, unchecked((uint)key),
                    unchecked((uint)NativeIdentityHash(personId)), 0x4d53544bu) % 8);
                items.AddItem(item, noStack: true);
            }
            if (post.Role == MarketPostRole.Weapons)
                foreach (var weapon in new[] { Weapons.Dagger, Weapons.Shortsword, Weapons.Longsword, Weapons.Mace, Weapons.Staff })
                    items.AddItem(ItemBuilder.CreateWeapon(weapon, WeaponMaterialTypes.Iron), noStack: true);
            return items;
        }
        internal static bool IsSaleItem(DaggerfallUnityItem item, NativeMarketPost post)
        {
            if (item == null || post == null || item.IsEquipped || item.IsQuestItem || item.IsArtifact ||
                item.RepairData.IsBeingRepaired() || item is HouseholdPropertyKeyItem) return false;
            var goods = item as NativeMarketGoodsItem;
            if (goods != null)
            {
                foreach (var evidence in post.Goods)
                    if (goods.GoodsArchive == evidence.Archive && goods.GoodsRecord == evidence.Record) return true;
                return false;
            }
            return post.Role == MarketPostRole.Weapons && item.ItemGroup == ItemGroups.Weapons;
        }
        internal static bool EnsureInitialInventory(WorldStateStore store, NativePerson person, NativeMarketPost post, out string reason)
        {
            reason = null;
            if (store.HasNpcInventory(person.Id)) return true;
            var initial = NativeNpcStartingBelongings.CreateInitialItems(store, person.Id,
                resident: store.ResolveResident(person.Id), explicitGender: person.Sex == ResidentSex.Female ? Genders.Female : Genders.Male,
                explicitRace: (Races)person.Race);
            if (post != null && post.Role != MarketPostRole.Performer && post.Role != MarketPostRole.Unknown)
            {
                var stock = CreateInitialStock(store, person.Id, post);
                while (stock.Count > 0)
                { var item = stock.GetItem(0); stock.RemoveItem(item); initial.AddItem(item, noStack: true); }
            }
            NativeNpcInventoryBinding binding;
            return NativeNpcInventoryBridge.TryOpen(store, person.Id, initial,
                NativeNpcInventoryBridge.InitialWalletGold(store, person.Id), out binding, out reason);
        }
        private static int NativeIdentityHash(string id)
        { unchecked { uint hash = 2166136261u; foreach (char value in id) hash = (hash ^ value) * 16777619u; return (int)hash; } }
    }
}
