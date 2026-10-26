using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcCompanionWageTests
    {
        internal static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Companion daily wage uses ten integer gold per actual level",()=>
            {
                int gold;
                foreach(int level in new[]{1,5,12,20,30})
                {Check.True(NpcPaidCompanionAction.TryDailyGold(level,out gold));Check.Equal(level*10,gold);}
            }));
            cases.Add(("Companion wage rejects unknown level and integer overflow",()=>
            {
                int gold;
                foreach(int level in new[]{0,-1,int.MinValue,int.MaxValue})
                {Check.True(!NpcPaidCompanionAction.TryDailyGold(level,out gold));Check.Equal(0,gold);}
                Check.True(NpcPaidCompanionAction.TryDailyGold(int.MaxValue/10,out gold));
                Check.Equal((int.MaxValue/10)*10,gold);
            }));
        }
    }
}
