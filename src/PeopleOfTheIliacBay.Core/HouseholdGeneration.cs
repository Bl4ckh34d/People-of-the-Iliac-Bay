using System;
using System.Collections.Generic;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    // Pure factory only. The persisted recovery transaction must archive the old
    // household before installing this result; calling this never mutates a town.
    public static class HouseholdGeneration
    {
        public static Household Create(uint worldSeed,int settlementId,HomeProfile home,int generation)
        {
            if(home==null) throw new ArgumentNullException(nameof(home));
            if(generation<0) throw new ArgumentOutOfRangeException(nameof(generation));
            if(generation>0 && home.BedCount<=0)
                throw new ArgumentException("Replacement families require surveyed beds.",nameof(home));
            uint seed=generation==0 ? worldSeed :
                StableSeed.Combine(worldSeed,0x484f4d45u,(uint)generation,0x5245504cu);
            var household=Population.CreateFamilies(seed,settlementId,new[]{home}).Households[0];
            household.Generation=generation;
            // Preserve generation-zero IDs, appearances and family layout exactly.
            if(generation==0) return household;
            var ids=new Dictionary<string,string>(StringComparer.Ordinal);
            for(int slot=0;slot<household.Residents.Count;slot++)
            {
                var person=household.Residents[slot];
                string id=string.Format(CultureInfo.InvariantCulture,"s:{0}:h:{1}:g:{2}:r:{3}",
                    settlementId,home.BuildingKey,generation,slot);
                ids.Add(person.Id,id);
                person.Id=id;
            }
            foreach(var person in household.Residents)
            {
                person.PartnerId=Remap(ids,person.PartnerId);
                person.MotherId=Remap(ids,person.MotherId);
                person.FatherId=Remap(ids,person.FatherId);
            }
            household.HeadId=Remap(ids,household.HeadId);
            return household;
        }
        private static string Remap(Dictionary<string,string> ids,string id)
        { return id==null ? null : ids[id]; }
    }
}
