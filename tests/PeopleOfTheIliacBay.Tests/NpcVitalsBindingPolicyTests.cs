using System;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcVitalsBindingPolicyTests
    {
        public static void Add(System.Collections.Generic.List<(string Name,Action Body)> cases)
        {
            cases.Add(("Vitals binding is limited to live, unprotected adults", (Action)(() => {
                Check.True(NpcVitalsBindingPolicy.Eligible(true, true, false));
                Check.True(!NpcVitalsBindingPolicy.Eligible(false, true, false));
                Check.True(!NpcVitalsBindingPolicy.Eligible(true, false, false));
                Check.True(!NpcVitalsBindingPolicy.Eligible(true, true, true));
            })));
            cases.Add(("Vitals use pinned archetype or explicit Citizen fallback", (Action)(() => {
                Check.Equal(NpcArchetype.Mage, NpcVitalsBindingPolicy.ResolveArchetype(true, NpcArchetype.Mage));
                Check.Equal(NpcArchetype.Citizen, NpcVitalsBindingPolicy.ResolveArchetype(false, NpcArchetype.Mage));
            })));
        }
    }
}
