using System;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    // Called only by an owned native acceptance run on an actually bound person.
    internal static class NativeMarketPersonProbe
    {
        internal static string Verify(StaticNPC npc, WorldStateStore store, NativePerson person, NativeMarketPost post)
        {
            Require(npc && store != null && person != null, "Market person fixture is unavailable.");
            var actor = npc.GetComponent<ResidentActor>(); var body = npc.GetComponent<DaggerfallEntityBehaviour>();
            var owner = npc.GetComponent<NativeNpcInventoryOwner>(); var member = store.ResolveResident(person.Id);
            Require(actor && actor.IsStationary && actor.gameObject == npc.gameObject && actor.Resident == member,
                "Market flat and resident do not share one body and household member.");
            Require(member != null && ReferenceEquals(member.NativeIdentity, person) && member.HomeKey == person.HomeKey &&
                member.BedSlot == person.BedSlot && person.HomeKey >= 0, "Market person's real household bed is missing.");
            Require(body && body.Entity is CivilianEntity && owner && owner.Binding != null &&
                owner.Binding.PersonId == person.Id && ReferenceEquals(owner.Binding.Items, body.Entity.Items),
                "Stationary person does not use the shared native entity inventory.");
            var role = store.ResolveNpcArchetype(person.Id); var stats = store.ReadNpcActor(person.Id, role);
            var vitals = store.ReadNpcVitals(person.Id, role);
            Require(body.Entity.Level >= 1 && body.Entity.MaxHealth == vitals.MaximumHealth &&
                body.Entity.MaxMagicka == vitals.MaximumMagicka, "Stationary native level or vital bounds differ.");
            for (int i = 0; i < 8; i++)
                Require(body.Entity.Stats.GetPermanentStatValue(i) == stats.Profile.Attribute(i), "Stationary native attribute differs.");
            for (int i = 0; i < 35; i++)
                Require(body.Entity.Skills.GetPermanentSkillValue(i) == stats.Skill(i), "Stationary native skill differs.");
            bool key = false, worn = false, sale = false;
            foreach (var item in NativeItems(body.Entity.Items))
            {
                key |= HouseholdPropertyKeyItem.IsMatching(item, person.SettlementId, person.HomeKey);
                worn |= item.IsEquipped && (item.ItemGroup == ItemGroups.MensClothing || item.ItemGroup == ItemGroups.WomensClothing);
                sale |= post != null && NativeMarketStock.IsSaleItem(item, post);
            }
            Require(key && worn, "Stationary native outfit or verified property key is absent.");
            Require(post == null || post.Role == MarketPostRole.Performer || post.Role == MarketPostRole.Unknown || sale,
                "Verified market vendor has no actual goods.");
            string reason;
            Require(NativeNpcInventoryBridge.TryCapture(store, owner.Binding, out reason), reason);
            var restored = new WorldStateStore(0); restored.Restore(store.Snapshot());
            var restoredPerson = restored.NativePersonInSettlement(person.SettlementId, person.Id);
            Require(ReferenceEquals(restored.ResolveResident(person.Id).NativeIdentity, restoredPerson) &&
                restoredPerson.Name == person.Name && restored.ResolveResident(person.Id).BedSlot == person.BedSlot,
                "Stationary household alias or name changed across restore.");
            NativeNpcInventoryBinding roundtrip;
            Require(NativeNpcInventoryBridge.TryOpen(restored, person.Id, null, 0, out roundtrip, out reason), reason);
            var original = NativeNpcInventoryBridge.CaptureItems(owner.Binding.Items);
            var decoded = NativeNpcInventoryBridge.CaptureItems(roundtrip.Items);
            Require(original.Count == decoded.Count, "Market item count changed across native restore.");
            for (int i = 0; i < original.Count; i++)
                Require(original[i].Uid == decoded[i].Uid && original[i].NativePayload == decoded[i].NativePayload,
                    "Market item UID or payload changed across native restore.");
            return "shared-body shared-entity shared-inventory native-stats worn-outfit home-key real-bed household-alias exact-native-restore";
        }
        private static System.Collections.Generic.IEnumerable<DaggerfallUnityItem> NativeItems(ItemCollection items)
        { for (int i = 0; i < items.Count; i++) yield return items.GetItem(i); }
        private static void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); }
    }
}
