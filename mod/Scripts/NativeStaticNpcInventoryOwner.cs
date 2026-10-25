using System;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Questing;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    // Short-lived lease over one verified static person's saved items. A layout
    // flat never becomes a mobile resident, home, bed, or service collection.
    public sealed class NativeStaticNpcInventoryOwner
    {
        public NativeNpcInventoryBinding Binding { get; private set; }
        public NativePerson Person { get; private set; }
        public NpcArchetype Role { get; private set; }

        private NativeStaticNpcInventoryOwner(NativeNpcInventoryBinding binding, NativePerson person, NpcArchetype role)
        { Binding = binding; Person = person; Role = role; }

        public static bool TryOpen(WorldStateStore store, StaticNPC npc, NativePerson person,
            NpcArchetype role, out NativeStaticNpcInventoryOwner owner, out string reason)
        {
            owner = null;
            reason = "The observed static person has no valid personal inventory.";
            if (store == null || !Matches(npc, person) || !IsRole(role)) return false;
            ItemCollection initial = null;
            if (!store.HasNpcInventory(person.Id))
                initial = NativeStaticNpcStartingBelongings.Create(store, person.Id, role, npc.Data.gender, npc.Data.race);
            NativeNpcInventoryBinding binding;
            if (!NativeNpcInventoryBridge.TryOpen(store, person.Id, initial,
                NativeNpcInventoryBridge.InitialWalletGold(store, person.Id), out binding, out reason)) return false;
            owner = new NativeStaticNpcInventoryOwner(binding, person, role);
            reason = null;
            return true;
        }

        public static bool Matches(StaticNPC npc, NativePerson person)
        {
            if (!npc || person == null || npc.IsChildNPC || npc.Data.context != StaticNPC.Context.Building ||
                npc.GetComponent<QuestResourceBehaviour>() != null || npc.GetComponent<DaggerfallAction>() != null ||
                !npc.gameObject.activeInHierarchy || !person.IsAlive || person.RetiredBedroomFlat) return false;
            var data = npc.Data;
            return person.SettlementId == data.mapID && person.WorkKey == data.buildingKey &&
                person.NativeHash == data.hash && person.FactionId == data.factionID &&
                person.Sex == (data.gender == Genders.Female ? ResidentSex.Female : ResidentSex.Male) &&
                person.Race == (int)data.race;
        }

        private static bool IsRole(NpcArchetype role)
        { return role >= NpcArchetype.Citizen && role <= NpcArchetype.Innkeeper; }
    }

    public static class NativeStaticNpcStartingBelongings
    {
        // Uses deterministic role, sex, and race facts from exact static owner.
        // Never reads shelf or guild-service collections.
        public static ItemCollection Create(WorldStateStore store, string personId, NpcArchetype role,
            Genders sex, Races race)
        {
            if (store == null || string.IsNullOrWhiteSpace(personId)) throw new ArgumentException("Static owner is invalid.");
            var items = new ItemCollection();
            foreach (var entry in NpcStartingBelongings.Generate(store.WorldSeed, personId, role))
            {
                ItemGroups group;
                switch (entry.Group)
                {
                    case NpcStartingItemGroup.UselessItems1: group = ItemGroups.UselessItems1; break;
                    case NpcStartingItemGroup.UselessItems2: group = ItemGroups.UselessItems2; break;
                    case NpcStartingItemGroup.MiscItems: group = ItemGroups.MiscItems; break;
                    case NpcStartingItemGroup.Jewellery: group = ItemGroups.Jewellery; break;
                    default: throw new InvalidOperationException("Static starting-item group is unsupported.");
                }
                var item = ItemBuilder.CreateItem(group, entry.TemplateIndex);
                item.stackCount = entry.Count;
                items.AddItem(item, noStack: true);
            }
            if (role == NpcArchetype.Healer)
                items.AddItem(ItemBuilder.CreateItem(ItemGroups.ReligiousItems,
                    NpcStartingBelongings.TempleReligiousTemplate(store.WorldSeed, personId)), noStack: true);
            if (role == NpcArchetype.Mage &&
                NpcStartingBelongings.ShouldStartWithMagicItem(store.WorldSeed, personId, role))
            {
                var magic = NativeNpcStartingBelongings.CreateDeterministicMageMagicItem(store, personId, sex, race);
                if (magic != null && !magic.IsArtifact && !magic.IsQuestItem) items.AddItem(magic, noStack: true);
            }
            if (sex == Genders.Male)
            {
                MensClothing clothing = role == NpcArchetype.Mage || role == NpcArchetype.Healer ?
                    MensClothing.Plain_robes : role == NpcArchetype.Merchant || role == NpcArchetype.Innkeeper ?
                    MensClothing.Formal_tunic : MensClothing.Short_tunic;
                items.AddItem(ItemBuilder.CreateMensClothing(clothing, race, 0, DyeColors.Blue), noStack: true);
            }
            else
            {
                WomensClothing clothing = role == NpcArchetype.Mage || role == NpcArchetype.Healer ?
                    WomensClothing.Plain_robes : role == NpcArchetype.Merchant || role == NpcArchetype.Innkeeper ?
                    WomensClothing.Day_gown : WomensClothing.Casual_dress;
                items.AddItem(ItemBuilder.CreateWomensClothing(clothing, race, 0, DyeColors.Blue), noStack: true);
            }
            return items;
        }
    }
}
