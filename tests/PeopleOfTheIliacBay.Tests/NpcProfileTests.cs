using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcProfileTests
    {
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("NPC profiles regenerate exactly from identity seed archetype and version",()=>{
                var first=NpcProfileGenerator.Generate(42,"citizen:one",NpcArchetype.Thief);
                var again=NpcProfileGenerator.Generate(42,"citizen:one",NpcArchetype.Thief);
                for(int i=0;i<8;i++) Check.Equal(first.Attribute(i),again.Attribute(i));
                for(int i=0;i<35;i++) Check.Equal(first.BaseSkill(i),again.BaseSkill(i));
                Check.Equal(first.MaximumHealth,again.MaximumHealth);Check.Equal(first.MaximumMagicka,again.MaximumMagicka);
                var other=NpcProfileGenerator.Generate(42,"citizen:two",NpcArchetype.Thief);bool different=false;
                for(int i=0;i<35;i++) different|=first.BaseSkill(i)!=other.BaseSkill(i);
                Check.True(different,"Distinct identities share every base skill.");
            }));
            cases.Add(("Seeded NPC profiles respect native index and resource bounds",()=>{
                foreach(NpcArchetype type in Enum.GetValues(typeof(NpcArchetype))) for(uint seed=0;seed<30;seed++)
                {
                    var profile=NpcProfileGenerator.Generate(seed,"citizen",type);
                    for(int i=0;i<8;i++) Check.True(profile.Attribute(i)>=1 && profile.Attribute(i)<=100);
                    for(int i=0;i<35;i++) Check.True(profile.BaseSkill(i)>=1 && profile.BaseSkill(i)<=100);
                    Check.True(profile.MaximumHealth>1 && profile.MaximumMagicka>=0);
                    Check.Throws<ArgumentOutOfRangeException>(()=>profile.BaseSkill(35));
                    Check.Throws<ArgumentOutOfRangeException>(()=>profile.Attribute(-1));
                }
                Check.Throws<ArgumentException>(()=>NpcProfileGenerator.Generate(42,"",NpcArchetype.Citizen));
                Check.Throws<ArgumentOutOfRangeException>(()=>NpcProfileGenerator.Generate(42,"npc",NpcArchetype.Citizen,2));
            }));
            cases.Add(("NPC class affinities accelerate practiced class skills",()=>{
                var thief=NpcProfileGenerator.Generate(42,"npc",NpcArchetype.Thief);
                Check.True(thief.TrainingRatePermille(13)>thief.TrainingRatePermille(22));
                Check.True(NpcSkillProgression.Value(20,1000,thief.TrainingRatePermille(13))>
                    NpcSkillProgression.Value(20,1000,thief.TrainingRatePermille(22)));
                var mage=NpcProfileGenerator.Generate(42,"npc",NpcArchetype.Mage);
                Check.True(mage.TrainingRatePermille(22)>mage.TrainingRatePermille(13));
            }));
            cases.Add(("Skill progression uses exact cumulative integer thresholds",()=>{
                Check.Equal(20,NpcSkillProgression.Value(20,299,1000));
                Check.Equal(21,NpcSkillProgression.Value(20,300,1000));
                Check.Equal(21,NpcSkillProgression.Value(20,609,1000));
                Check.Equal(22,NpcSkillProgression.Value(20,610,1000));
                Check.Equal(21,NpcSkillProgression.Value(20,200,1500));
            }));
            cases.Add(("Skill progression is monotone bounded and safe for large saved experience",()=>{
                int previous=1;
                for(int xp=0;xp<20000;xp+=37)
                {int current=NpcSkillProgression.Value(1,xp,1100);Check.True(current>=previous && current<=100);previous=current;}
                Check.Equal(100,NpcSkillProgression.Value(5,1000000000000L,2500));
                Check.Throws<ArgumentOutOfRangeException>(()=>NpcSkillProgression.Value(20,-1,1000));
                Check.Throws<ArgumentOutOfRangeException>(()=>NpcSkillProgression.Value(20,long.MaxValue,1000));
                Check.Throws<ArgumentOutOfRangeException>(()=>NpcSkillProgression.Value(20,1,0));
            }));
        }
    }
}
