using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcStartingBelongingsTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Mundane starting goods never create native potion-template glass bottles",()=>
            {
                for(uint seed=0;seed<100;seed++)foreach(var item in NpcStartingBelongings.Generate(seed,"person",NpcArchetype.Citizen))
                    Check.True(item.TemplateIndex!=83,"Native Glass_Bottle is a potion, not ordinary junk.");
            }));
            cases.Add(("NPC starting belongings regenerate from world seed identity and pinned archetype", () =>
            {
                var first = NpcStartingBelongings.Generate(42, "citizen:one", NpcArchetype.Citizen);
                var again = NpcStartingBelongings.Generate(42, "citizen:one", NpcArchetype.Citizen);
                Check.Equal(first.Count, again.Count);
                for (int i = 0; i < first.Count; i++)
                {
                    Check.Equal(first[i].Group, again[i].Group);
                    Check.Equal(first[i].TemplateIndex, again[i].TemplateIndex);
                    Check.Equal(first[i].Count, again[i].Count);
                }

                bool identityChanged = false;
                for (uint seed = 0; seed < 100; seed++)
                {
                    var a = NpcStartingBelongings.Generate(seed, "citizen:one", NpcArchetype.Citizen);
                    var b = NpcStartingBelongings.Generate(seed, "citizen:two", NpcArchetype.Citizen);
                    if (!Same(a, b)) { identityChanged = true; break; }
                }
                Check.True(identityChanged, "Identity did not affect any sampled belongings plan.");
            }));

            cases.Add(("NPC starting belongings use bounded utility templates and detached output", () =>
            {
                foreach (NpcArchetype archetype in Enum.GetValues(typeof(NpcArchetype)))
                for (uint seed = 0; seed < 100; seed++)
                {
                    var plan = NpcStartingBelongings.Generate(seed, "person", archetype);
                    Check.True(plan.Count <= NpcStartingBelongings.MaximumDistinctItems);
                    foreach (var item in plan)
                    {
                        Check.True(item.Count == 1, "Starting goods must remain modest single-item entries.");
                        Check.True(item.TemplateIndex == 82 || item.TemplateIndex == 86 || item.TemplateIndex == 132 ||
                            item.TemplateIndex == 133 || item.TemplateIndex == 249 || item.TemplateIndex == 279,
                            "Plan contains unsupported template.");
                    }
                }

                var first = NpcStartingBelongings.Generate(13, "person", NpcArchetype.Citizen);
                var second = NpcStartingBelongings.Generate(13, "person", NpcArchetype.Citizen);
                Check.True(!ReferenceEquals(first, second), "Plans must not share collection state.");
                if (first.Count > 0)
                {
                    Check.True(!ReferenceEquals(first[0], second[0]), "Plans must not share item DTOs.");
                    var mutableFirst = (IList<NpcStartingBelonging>)first;
                    mutableFirst[0] = null;
                    Check.True(second[0].TemplateIndex == 83 || second[0].TemplateIndex == 86 || second[0].TemplateIndex == 132 ||
                        second[0].TemplateIndex == 133 || second[0].TemplateIndex == 249 || second[0].TemplateIndex == 279);
                }
            }));

            cases.Add(("NPC starting belongings reject invalid identity and archetype", () =>
            {
                Check.Throws<ArgumentException>(() => NpcStartingBelongings.Generate(1, "", NpcArchetype.Citizen));
                Check.Throws<ArgumentOutOfRangeException>(() => NpcStartingBelongings.Generate(1, "person", (NpcArchetype)99));
            }));

            cases.Add(("Pinned NPC archetypes apply bounded class-specific starting-item odds", () =>
            {
                int merchant = 0, mage = 0;
                for (uint seed = 0; seed < 500; seed++)
                {
                    merchant += NpcStartingBelongings.Generate(seed, "person", NpcArchetype.Merchant).Count;
                    mage += NpcStartingBelongings.Generate(seed, "person", NpcArchetype.Mage).Count;
                }
                Check.True(merchant > mage, "Class-specific starting-item odds did not affect sampled plans.");
            }));

            cases.Add(("Starting pools keep deterministic class-specific mundane supplies", () =>
            {
                bool healerBandage = false, mageParchment = false, mageSpellbook = false, merchantAmulet = false;
                for (uint seed = 0; seed < 2000 && (!healerBandage || !mageParchment || !mageSpellbook || !merchantAmulet); seed++)
                {
                    foreach (var item in NpcStartingBelongings.Generate(seed, "healer", NpcArchetype.Healer))
                        healerBandage |= item.Group == NpcStartingItemGroup.UselessItems2 && item.TemplateIndex == 249;
                    foreach (var item in NpcStartingBelongings.Generate(seed, "mage", NpcArchetype.Mage))
                    {
                        mageParchment |= item.Group == NpcStartingItemGroup.UselessItems2 && item.TemplateIndex == 279;
                        mageSpellbook |= item.Group == NpcStartingItemGroup.MiscItems && item.TemplateIndex == 132;
                    }
                    foreach (var item in NpcStartingBelongings.Generate(seed, "merchant", NpcArchetype.Merchant))
                        merchantAmulet |= item.Group == NpcStartingItemGroup.Jewellery && item.TemplateIndex == 133;
                }
                Check.True(healerBandage, "Healer pool never produced a native bandage.");
                Check.True(mageParchment, "Mage pool never produced native parchment.");
                Check.True(mageSpellbook, "Mage pool never produced a native spellbook.");
                Check.True(merchantAmulet, "Merchant pool never produced a native amulet.");
            }));

            cases.Add(("Mage magic-item chance is deterministic and excludes ordinary roles", new Action(() =>
            {
                bool mageMagic = false;
                for (uint seed = 0; seed < 2000; seed++)
                {
                    bool first = NpcStartingBelongings.ShouldStartWithMagicItem(seed, "mage", NpcArchetype.Mage);
                    Check.True(first == NpcStartingBelongings.ShouldStartWithMagicItem(seed, "mage", NpcArchetype.Mage),
                        "Mage magic-item chance changed for the same identity.");
                    mageMagic |= first;
                    Check.True(!NpcStartingBelongings.ShouldStartWithMagicItem(seed, "citizen", NpcArchetype.Citizen),
                        "Ordinary citizen received a generated magic item.");
                }
                Check.True(mageMagic, "Mage magic-item chance never selected a native item.");
            })));

            cases.Add(("Temple staff religious pool is deterministic and never a quest template", new Action(() =>
            {
                int[] allowed = { 258, 260, 262, 263, 267, 269, 271 };
                for (uint seed = 0; seed < 100; seed++)
                {
                    int item = NpcStartingBelongings.TempleReligiousTemplate(seed, "npc:temple:" + seed);
                    Check.True(Array.IndexOf(allowed, item) >= 0, "Temple pool selected an unsupported or quest item.");
                    Check.Equal(item, NpcStartingBelongings.TempleReligiousTemplate(seed, "npc:temple:" + seed));
                }
            })));

            cases.Add(("Verified resident home key resolves only from exact saved household ownership", new Action(() =>
            {
                var store = new WorldStateStore(991);
                var town = store.GetOrCreate(725, new[] { 10, 20 }, 1);
                var resident = town.Households[0].Residents[0];
                int settlement;
                Check.True(NpcStartingBelongings.TryResolveVerifiedResidentHome(store.Snapshot(), resident, out settlement),
                    "Saved household resident did not resolve to its exact home.");
                Check.Equal(725, settlement);

                var impostor = new Resident { Id = resident.Id + ":other", HomeKey = resident.HomeKey };
                Check.True(!NpcStartingBelongings.TryResolveVerifiedResidentHome(store.Snapshot(), impostor, out settlement),
                    "Unowned identity resolved to another resident's key.");

                var noHome = new Resident { Id = resident.Id, HomeKey = -1 };
                Check.True(!NpcStartingBelongings.TryResolveVerifiedResidentHome(store.Snapshot(), noHome, out settlement),
                    "Resident without a home key resolved to a property key.");
            })));

            cases.Add(("Home-key lookup does not copy unrelated world state", new Action(() =>
            {
                var store=new WorldStateStore(991);
                var town=store.GetOrCreate(725,new[]{10,20},1);
                var resident=town.Households[0].Residents[0];
                for(int i=0;i<100;i++)store.GetOrCreate(1000+i,new[]{10,20,30,40},2);
                int settlement;
                Check.True(store.TryResolveVerifiedResidentHome(resident,out settlement));
                long before=GC.GetAllocatedBytesForCurrentThread();
                for(int i=0;i<100;i++)
                {
                    Check.True(store.TryResolveVerifiedResidentHome(resident,out settlement));
                    Check.Equal(725,settlement);
                }
                Check.True(GC.GetAllocatedBytesForCurrentThread()-before<16384,
                    "A scalar home lookup must not allocate a world snapshot.");
                Check.True(!store.TryResolveVerifiedResidentHome(new Resident{Id=resident.Id,HomeKey=30},out settlement));
                Check.True(!store.TryResolveVerifiedResidentHome(new Resident{Id="unowned",HomeKey=10},out settlement));
                Check.True(!store.TryResolveVerifiedResidentHome(null,out settlement));
            })));
        }

        private static bool Same(IReadOnlyList<NpcStartingBelonging> a, IReadOnlyList<NpcStartingBelonging> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i].Group != b[i].Group || a[i].TemplateIndex != b[i].TemplateIndex || a[i].Count != b[i].Count)
                    return false;
            return true;
        }
    }
}
