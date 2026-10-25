using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class HouseholdGenerationTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Household generation zero preserves legacy identities and appearance", Legacy));
            cases.Add(("Replacement generations never reuse identities or family links", Generations));
            cases.Add(("Replacement generation respects beds and rejects invalid input", Capacity));
        }
        private static void Legacy()
        {
            var home=new HomeProfile(17,5,230);
            var old=Population.CreateFamilies(42,3,new[]{home}).Households[0];
            var current=HouseholdGeneration.Create(42,3,home,0);
            Check.Equal(old.SurnameSeed,current.SurnameSeed);
            Check.Equal(old.Residents.Count,current.Residents.Count);
            for(int i=0;i<old.Residents.Count;i++)
            {
                Check.Equal(old.Residents[i].Id,current.Residents[i].Id);
                Check.Equal(old.Residents[i].AppearanceSeed,current.Residents[i].AppearanceSeed);
                Check.Equal(old.Residents[i].PartnerId,current.Residents[i].PartnerId);
                Check.Equal(old.Residents[i].FatherId,current.Residents[i].FatherId);
                Check.Equal(old.Residents[i].MotherId,current.Residents[i].MotherId);
            }
        }
        private static void Generations()
        {
            var all=new HashSet<string>();
            bool children=false;
            for(int generation=0;generation<100;generation++)
            {
                var home=new HomeProfile(17,6,250);
                var family=HouseholdGeneration.Create(42,3,home,generation);
                var repeat=HouseholdGeneration.Create(42,3,home,generation);
                Check.Equal(family.SurnameSeed,repeat.SurnameSeed);
                Check.Equal(family.Residents.Count,repeat.Residents.Count);
                var ids=new HashSet<string>();
                foreach(var person in family.Residents)
                { Check.True(all.Add(person.Id),"A new generation reused a historical ID."); ids.Add(person.Id); }
                for(int i=0;i<family.Residents.Count;i++)
                {
                    var person=family.Residents[i];
                    Check.Equal(person.Id,repeat.Residents[i].Id);
                    Check.Equal(person.AppearanceSeed,repeat.Residents[i].AppearanceSeed);
                    Check.True(person.IsAlive && person.NativeIdentity==null,"Replacement reused dead/native identity state.");
                    foreach(var relation in new[]{person.PartnerId,person.MotherId,person.FatherId})
                        Check.True(relation==null || ids.Contains(relation),"Family relation points to another generation.");
                    if(person.AgeGroup==ResidentAgeGroup.Child) children=true;
                }
            }
            Check.True(children,"Fixture must exercise child-parent remapping.");
        }
        private static void Capacity()
        {
            for(int beds=1;beds<8;beds++)
            {
                var family=HouseholdGeneration.Create(1,2,new HomeProfile(3,beds,300),7);
                Check.Equal(beds,family.BedCapacity);
                Check.True(family.Residents.Count>0 && family.Residents.Count<=beds,"One resident per bed exceeded.");
            }
            Check.Throws<ArgumentOutOfRangeException>(()=>HouseholdGeneration.Create(1,2,new HomeProfile(3,1),-1));
            Check.Throws<ArgumentException>(()=>HouseholdGeneration.Create(1,2,new HomeProfile(3,0),1));
            Check.Throws<ArgumentException>(()=>HouseholdGeneration.Create(1,2,new HomeProfile(3,-1),1));
            Check.Throws<ArgumentNullException>(()=>HouseholdGeneration.Create(1,2,null,1));
        }
    }
}
