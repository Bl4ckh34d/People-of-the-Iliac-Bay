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
            var actorOwner=npc.GetComponent<NativeNpcInventoryOwner>();
            if(actorOwner && actorOwner.Binding!=null)
            {
                if(actorOwner.Binding.PersonId!=person.Id)return false;
                owner=new NativeStaticNpcInventoryOwner(actorOwner.Binding,person,role);
                reason=null;return true;
            }
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
            var life=npc?npc.GetComponent<NativeStationaryLife>():null;
            if(life && life.Person!=null && person!=null && life.Person.Id==person.Id)
                return npc.gameObject.activeInHierarchy && person.IsAlive && !person.RetiredBedroomFlat;
            if (!npc || person == null || npc.IsChildNPC ||
                npc.GetComponent<QuestResourceBehaviour>() != null || npc.GetComponent<DaggerfallAction>() != null ||
                !npc.gameObject.activeInHierarchy || !person.IsAlive || person.RetiredBedroomFlat) return false;
            var data = npc.Data;
            int exteriorTown,exteriorHash;
            if(NativeExteriorNpcIdentity.TryRead(npc,out exteriorTown,out exteriorHash))
                return person.Id==NativeExteriorNpcIdentity.PersonId(exteriorTown,exteriorHash) &&
                    person.SettlementId==exteriorTown && person.WorkKey==-1 && person.NativeHash==exteriorHash &&
                    person.FactionId==data.factionID && person.TextureArchive==data.billboardArchiveIndex &&
                    person.TextureRecord==data.billboardRecordIndex && person.Race==(int)data.race &&
                    person.Sex==(data.gender==Genders.Female?ResidentSex.Female:ResidentSex.Male);
            if(data.context!=StaticNPC.Context.Building)return false;
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
            var items = NativeNpcStartingBelongings.CreateInitialItems(store, personId,
                resident: store.ResolveResident(personId), explicitArchetype: role, explicitGender: sex, explicitRace: race);
            if (role == NpcArchetype.Merchant)
                foreach (var entry in NpcMerchantStock.Generate(store.WorldSeed, personId))
                {
                    var group = entry.Group == NpcStartingItemGroup.UselessItems1 ? ItemGroups.UselessItems1 :
                        entry.Group == NpcStartingItemGroup.UselessItems2 ? ItemGroups.UselessItems2 : ItemGroups.Jewellery;
                    var item = ItemBuilder.CreateItem(group, entry.TemplateIndex);
                    item.stackCount = entry.Count; items.AddItem(item, noStack: true);
                }
            if (role == NpcArchetype.Healer)
                items.AddItem(ItemBuilder.CreateItem(ItemGroups.ReligiousItems,
                    NpcStartingBelongings.TempleReligiousTemplate(store.WorldSeed, personId)), noStack: true);
            return items;
        }
    }
}
