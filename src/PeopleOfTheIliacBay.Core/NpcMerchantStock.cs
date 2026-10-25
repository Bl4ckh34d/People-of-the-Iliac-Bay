using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    // A first inventory for an observed merchant, not a restocking economy.
    // Native item template IDs: containers, travelling supplies and jewellery.
    // Saved native items/UIDs take authority after the first successful opening.
    public static class NpcMerchantStock
    {
        private static readonly int[] Templates={82,83,84,85,86,87,88,89,247,248,249,252,253,279,133,134,135,136,137,138,139};

        public static IReadOnlyList<NpcStartingBelonging> Generate(uint worldSeed,string personId)
        {
            if(string.IsNullOrWhiteSpace(personId) || personId.Length>240)throw new ArgumentException("Invalid merchant identity.");
            uint identity=2166136261u;
            unchecked { foreach(char value in personId)identity=(identity^value)*16777619u; }
            uint seed=StableSeed.Combine(worldSeed,identity,0x53544f43u,1);
            var indices=(int[])Templates.Clone();
            for(int i=indices.Length-1;i>0;i--)
            {
                int other=(int)(StableSeed.Combine(seed,(uint)i,0,1)%(uint)(i+1));
                int saved=indices[i];indices[i]=indices[other];indices[other]=saved;
            }
            int count=10+(int)(seed%5u);
            var result=new List<NpcStartingBelonging>(count);
            for(int i=0;i<count;i++)
            {
                int template=indices[i];
                var group=template<100?NpcStartingItemGroup.UselessItems1:
                    template<200?NpcStartingItemGroup.Jewellery:NpcStartingItemGroup.UselessItems2;
                result.Add(new NpcStartingBelonging(group,template,1));
            }
            return result;
        }
    }
}
