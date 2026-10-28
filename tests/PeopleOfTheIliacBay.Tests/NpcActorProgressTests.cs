using System;
using System.Collections.Generic;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcActorProgressTests
    {
        private static WorldStateStore Create(out string id)
        {var store=new WorldStateStore(42);id=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0].Id;return store;}
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Batch actor reads provide all skills once and remain detached after practice",()=>{
                string id;var store=Create(out id);var before=store.ReadNpcActor(id,NpcArchetype.Thief);
                for(int skill=0;skill<35;skill++) Check.Equal(before.Profile.BaseSkill(skill),before.Skill(skill));
                Check.Equal(0,store.Snapshot().NpcActors.Count);
                store.AwardNpcSkillExperience(id,NpcArchetype.Thief,13,1000,0);
                var after=store.ReadNpcActor(id,NpcArchetype.Thief);
                Check.True(after.Skill(13)>before.Skill(13));Check.Equal(1L,after.Revision);Check.Equal(0L,before.Revision);
            }));
            cases.Add(("Reading seeded actor profiles and skills never populates the save ledger",()=>{
                string id;var store=Create(out id);
                for(int i=0;i<100;i++) {store.ReadNpcProfile(id,NpcArchetype.Thief);store.ReadNpcSkill(id,NpcArchetype.Thief,13);}
                Check.Equal(0,store.Snapshot().NpcActors.Count);
            }));
            cases.Add(("Actor experience stores only trained skills and rejects stale retry revisions",()=>{
                string id;var store=Create(out id);int before=store.ReadNpcSkill(id,NpcArchetype.Thief,13);
                Check.True(store.AwardNpcSkillExperience(id,NpcArchetype.Thief,13,1000,0));
                Check.True(store.ReadNpcSkill(id,NpcArchetype.Thief,13)>before);
                Check.True(!store.AwardNpcSkillExperience(id,NpcArchetype.Thief,13,1000,0));
                Check.True(!store.AwardNpcSkillExperience(id,NpcArchetype.Mage,13,1000,1));
                var progress=store.ReadNpcProgress(id);
                Check.Equal(1L,progress.Revision);Check.Equal(1,progress.Skills.Count);Check.Equal(1000L,progress.Skills[0].Experience);
                progress.Skills.Clear();Check.Equal(1,store.ReadNpcProgress(id).Skills.Count);
                Check.True(!store.AwardNpcSkillExperience("unknown",NpcArchetype.Thief,13,1000,0));
            }));
            cases.Add(("Actor progression follows independent save branches",()=>{
                string id;var store=Create(out id);store.AwardNpcSkillExperience(id,NpcArchetype.Thief,13,1000,0);
                var save=store.Snapshot();store.AwardNpcSkillExperience(id,NpcArchetype.Thief,13,500,1);
                store.Restore(save);Check.Equal(1000L,store.ReadNpcProgress(id).Skills[0].Experience);
                Check.True(store.AwardNpcSkillExperience(id,NpcArchetype.Thief,16,700,1));
                Check.Equal(1,save.NpcActors[0].Skills.Count);Check.Equal(2,store.ReadNpcProgress(id).Skills.Count);
                var json=JsonSerializer.Serialize(store.Snapshot(),new JsonSerializerOptions{IncludeFields=true});
                var restored=new WorldStateStore(0);restored.Restore(JsonSerializer.Deserialize<WorldState>(json,new JsonSerializerOptions{IncludeFields=true}));
                Check.Equal(store.ReadNpcSkill(id,NpcArchetype.Thief,16),restored.ReadNpcSkill(id,NpcArchetype.Thief,16));
            }));
            cases.Add(("Actor progression restore rejects invalid identities counters and duplicates",()=>{
                string id;var store=Create(out id);store.AwardNpcSkillExperience(id,NpcArchetype.Thief,13,1000,0);
                var broken=store.Snapshot();broken.NpcActors[0].PersonId="unknown";
                Check.Throws<ArgumentException>(()=>store.Restore(broken));
                broken=store.Snapshot();broken.NpcActors[0].Skills.Add(new NpcSkillExperience{Skill=13,Experience=20});
                Check.Throws<ArgumentException>(()=>store.Restore(broken));
                broken=store.Snapshot();broken.NpcActors[0].Skills[0].Experience=-1;
                Check.Throws<ArgumentException>(()=>store.Restore(broken));
                Check.Equal(1000L,store.ReadNpcProgress(id).Skills[0].Experience);
            }));
        }
    }
}
