using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class FreeHealingPotionCommandTests
    {
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Free healing command requires an explicit own healing-item request",() =>
            {
                var potion=new NpcActionOption {Id=1,Kind=NpcActionKind.UseHealingItem,
                    Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="self",ItemId="17",Quantity=1}};
                Check.True(NpcActionChat.ParseSemantic("DIRECT|UseHealingItem|1|1|30",new[]{potion})!=null);
                Check.True(NpcActionChat.ParseSemantic("DIRECT|UseHealingItem|1|2|30",new[]{potion})==null);
                Check.True(NpcActionChat.ParseSemantic("NEGATED",new[]{potion})==null);
            }));
        }
    }
}
