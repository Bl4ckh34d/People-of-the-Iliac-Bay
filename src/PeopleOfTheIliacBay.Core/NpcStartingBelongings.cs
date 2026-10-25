using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    // Semantic item-group values keep Core independent from Unity and DFU assemblies.
    public enum NpcStartingItemGroup { UselessItems1, UselessItems2, MiscItems, Jewellery }

    public sealed class NpcStartingBelonging
    {
        public NpcStartingItemGroup Group { get; private set; }
        public int TemplateIndex { get; private set; }
        public int Count { get; private set; }

        internal NpcStartingBelonging(NpcStartingItemGroup group, int templateIndex, int count)
        {
            Group = group;
            TemplateIndex = templateIndex;
            Count = count;
        }
    }

    // Pure, reproducible first-inventory proposal. Runtime must call only when no saved
    // inventory ledger exists; persisted native inventory remains authoritative afterward.
    public static class NpcStartingBelongings
    {
        public const int MaximumDistinctItems = 3;
        private const uint GenerationVersion = 1;
        private const uint Domain = 0x42454c31u; // BEL1
        private const uint HealingPotionGenerationVersion = 1;
        private const uint HealingPotionDomain = 0x48504f31u; // HPO1
        private const uint MagicItemGenerationVersion = 1;
        private const uint MagicItemDomain = 0x4d414731u; // MAG1
        private const uint TempleItemGenerationVersion = 1;
        private const uint TempleItemDomain = 0x544d5031u; // TMP1
        private static readonly int[] TempleReligiousTemplates = { 258, 260, 262, 263, 267, 269, 271 };

        public static bool ShouldStartWithHealingPotion(uint worldSeed, string personId, NpcArchetype archetype)
        {
            ValidateInputs(personId, archetype);
            uint identityHash = HashIdentity(personId);
            uint seed = StableSeed.Combine(worldSeed, identityHash, (uint)archetype,
                HealingPotionDomain ^ HealingPotionGenerationVersion);
            uint roll = StableSeed.Combine(seed, HealingPotionDomain, 0, HealingPotionGenerationVersion) % 10000u;
            return roll < HealingPotionChance(archetype);
        }

        public static bool ShouldStartWithMagicItem(uint worldSeed, string personId, NpcArchetype archetype)
        {
            ValidateInputs(personId, archetype);
            if (archetype != NpcArchetype.Mage) return false;
            uint seed = StableSeed.Combine(worldSeed, HashIdentity(personId), (uint)archetype,
                MagicItemDomain ^ MagicItemGenerationVersion);
            return StableSeed.Combine(seed, MagicItemDomain, 0, MagicItemGenerationVersion) % 10000u < 300u;
        }

        public static int TempleReligiousTemplate(uint worldSeed, string personId)
        {
            if (string.IsNullOrWhiteSpace(personId) || personId.Length > 240)
                throw new ArgumentException("Invalid NPC identity.");
            uint seed = StableSeed.Combine(worldSeed, HashIdentity(personId), TempleItemDomain, TempleItemGenerationVersion);
            return TempleReligiousTemplates[seed % (uint)TempleReligiousTemplates.Length];
        }

        // A property key is valid only for the one saved household that owns this
        // exact resident/home pair. Runtime materializes the native key item.
        public static bool TryResolveVerifiedResidentHome(WorldState state, Resident resident, out int settlementId)
        {
            settlementId = 0;
            if (state == null || resident == null || string.IsNullOrWhiteSpace(resident.Id) || resident.HomeKey < 0)
                return false;
            int matches = 0;
            foreach (var town in state.Settlements)
                foreach (var household in town.Households)
                    if (household.BuildingKey == resident.HomeKey)
                    {
                        foreach(var member in household.Residents)
                            if(member.Id==resident.Id && member.HomeKey==resident.HomeKey)
                            {
                                matches++;
                                settlementId=town.SettlementId;
                                break;
                            }
                    }
            if (matches == 1 && settlementId > 0) return true;
            settlementId = 0;
            return false;
        }

        public static IReadOnlyList<NpcStartingBelonging> Generate(uint worldSeed, string personId, NpcArchetype archetype)
        {
            ValidateInputs(personId, archetype);
            uint identityHash = HashIdentity(personId);

            uint seed = StableSeed.Combine(worldSeed, identityHash, (uint)archetype, Domain ^ GenerationVersion);
            int chancePerSlot = ChancePerSlot(archetype);
            var result = new List<NpcStartingBelonging>(MaximumDistinctItems);
            for (uint slot = 0; slot < MaximumDistinctItems; slot++)
            {
                uint roll = StableSeed.Combine(seed, Domain, slot, GenerationVersion) % 10000u;
                if (roll >= chancePerSlot) continue;

                uint templateRoll = StableSeed.Combine(seed, 0x54454d50u, slot, GenerationVersion) % 4u;
                if (templateRoll == 3 && archetype == NpcArchetype.Healer)
                    result.Add(new NpcStartingBelonging(NpcStartingItemGroup.UselessItems2, 249, 1)); // Bandage
                else if (templateRoll == 3 && archetype == NpcArchetype.Mage)
                    result.Add(new NpcStartingBelonging(NpcStartingItemGroup.MiscItems, 132, 1)); // Spellbook
                else if (templateRoll == 3 && (archetype == NpcArchetype.Merchant || archetype == NpcArchetype.Innkeeper))
                    result.Add(new NpcStartingBelonging(NpcStartingItemGroup.Jewellery, 133, 1)); // Amulet
                else if (templateRoll == 0)
                    result.Add(new NpcStartingBelonging(NpcStartingItemGroup.UselessItems1, 83, 1)); // Glass Bottle
                else if (templateRoll == 1)
                    result.Add(new NpcStartingBelonging(NpcStartingItemGroup.UselessItems1, 86, 1)); // Small Sack
                else
                    result.Add(new NpcStartingBelonging(NpcStartingItemGroup.UselessItems2, 279, 1)); // Parchment
            }
            return result;
        }

        private static void ValidateInputs(string personId, NpcArchetype archetype)
        {
            if (string.IsNullOrWhiteSpace(personId) || personId.Length > 240)
                throw new ArgumentException("Invalid NPC identity.");
            if ((int)archetype < 0 || (int)archetype > 7)
                throw new ArgumentOutOfRangeException(nameof(archetype));
        }

        private static uint HashIdentity(string personId)
        {
            uint identityHash = 2166136261u;
            unchecked
            {
                foreach (char value in personId) identityHash = (identityHash ^ value) * 16777619u;
            }
            return identityHash;
        }

        private static int HealingPotionChance(NpcArchetype archetype)
        {
            switch (archetype)
            {
                case NpcArchetype.Healer: return 1600;
                case NpcArchetype.Mage: return 900;
                case NpcArchetype.Warrior: return 700;
                case NpcArchetype.Guard: return 600;
                case NpcArchetype.Thief: return 500;
                case NpcArchetype.Citizen: return 400;
                case NpcArchetype.Merchant:
                case NpcArchetype.Innkeeper: return 300;
                default: throw new ArgumentOutOfRangeException(nameof(archetype));
            }
        }

        private static int ChancePerSlot(NpcArchetype archetype)
        {
            switch (archetype)
            {
                case NpcArchetype.Merchant:
                case NpcArchetype.Innkeeper: return 2700;
                case NpcArchetype.Thief: return 2400;
                case NpcArchetype.Citizen: return 2000;
                case NpcArchetype.Healer: return 1800;
                case NpcArchetype.Warrior:
                case NpcArchetype.Guard: return 1400;
                case NpcArchetype.Mage: return 1200;
                default: throw new ArgumentOutOfRangeException(nameof(archetype));
            }
        }
    }
}
