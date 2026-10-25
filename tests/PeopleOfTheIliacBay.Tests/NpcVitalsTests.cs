using System;
using System.Collections.Generic;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcVitalsTests
    {
        private static WorldStateStore Create(out string id)
        {
            var store=new WorldStateStore(9042);
            id=store.GetOrCreate(11,new[]{23},1).Households[0].Residents[0].Id;
            return store;
        }

        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Reading seeded NPC vitals returns full stable resources without creating progress rows",(Action)(()=>{
                string id;var store=Create(out id);
                var expected=NpcProfileGenerator.Generate(store.WorldSeed,id,NpcArchetype.Mage);
                for(int i=0;i<100;i++)
                {
                    var read=store.ReadNpcVitals(id,NpcArchetype.Mage);
                    Check.True(read.IsAlive);Check.True(!read.IsPersisted);
                    Check.Equal(expected.MaximumHealth,read.MaximumHealth);Check.Equal(expected.MaximumHealth,read.CurrentHealth);
                    Check.Equal(expected.MaximumMagicka,read.MaximumMagicka);Check.Equal(expected.MaximumMagicka,read.CurrentMagicka);
                    Check.Equal(0L,read.Revision);
                }
                Check.Equal(0,store.Snapshot().NpcActors.Count);
            })));

            cases.Add(("Changed NPC vitals persist through detached snapshots and restore",(Action)(()=>{
                string id;var store=Create(out id);var initial=store.ReadNpcVitals(id,NpcArchetype.Warrior);
                Check.True(store.TryCaptureNpcVitals(id,NpcArchetype.Warrior,initial.MaximumHealth-7,
                    initial.MaximumMagicka-3,100,initial.Revision));
                var current=store.ReadNpcVitals(id,NpcArchetype.Warrior);
                Check.True(current.IsPersisted);Check.Equal(initial.MaximumHealth-7,current.CurrentHealth);
                Check.Equal(initial.MaximumMagicka-3,current.CurrentMagicka);Check.Equal(100d,current.CapturedAt);
                Check.Equal(1L,current.Revision);Check.Equal(1,store.Snapshot().NpcActors.Count);

                var save=store.Snapshot();save.NpcActors[0].CurrentHealth=1;
                Check.Equal(initial.MaximumHealth-7,store.ReadNpcVitals(id,NpcArchetype.Warrior).CurrentHealth);
                var json=JsonSerializer.Serialize(store.Snapshot(),new JsonSerializerOptions{IncludeFields=true});
                var restored=new WorldStateStore(0);
                restored.Restore(JsonSerializer.Deserialize<WorldState>(json,new JsonSerializerOptions{IncludeFields=true}));
                var after=restored.ReadNpcVitals(id,NpcArchetype.Warrior);
                Check.Equal(current.CurrentHealth,after.CurrentHealth);Check.Equal(current.CurrentMagicka,after.CurrentMagicka);
                Check.Equal(current.CapturedAt,after.CapturedAt);Check.True(after.IsPersisted);
            })));

            cases.Add(("Legacy skill-only actor rows retain seeded full vitals without being rewritten",(Action)(()=>{
                string id;var store=Create(out id);var save=store.Snapshot();
                save.NpcActors.Add(new NpcActorProgress {PersonId=id,Archetype=NpcArchetype.Merchant,Revision=1,
                    Skills=new List<NpcSkillExperience>{new NpcSkillExperience{Skill=14,Experience=120}}});
                var legacyJsonOptions=new JsonSerializerOptions{IncludeFields=true,DefaultIgnoreCondition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault};
                string legacyJson=JsonSerializer.Serialize(save,legacyJsonOptions);
                var legacy=JsonSerializer.Deserialize<WorldState>(legacyJson,legacyJsonOptions);
                var restored=new WorldStateStore(0);restored.Restore(legacy);
                var vitals=restored.ReadNpcVitals(id,NpcArchetype.Merchant);
                Check.True(vitals.IsAlive);Check.True(!vitals.IsPersisted);Check.Equal(vitals.MaximumHealth,vitals.CurrentHealth);
                Check.Equal(vitals.MaximumMagicka,vitals.CurrentMagicka);Check.Equal(0L,vitals.Revision);
                var progress=restored.ReadNpcProgress(id);
                Check.Equal(1L,progress.Revision);Check.Equal(120L,progress.Skills[0].Experience);
                Check.True(!progress.VitalsInitialized);Check.Equal(0,restored.Snapshot().NpcActors[0].CurrentHealth);
            })));

            cases.Add(("Vitals revisions stay independent from learned skill revisions",(Action)(()=>{
                string id;var store=Create(out id);
                Check.True(store.AwardNpcSkillExperience(id,NpcArchetype.Warrior,20,1000,0));
                var skills=store.ReadNpcActor(id,NpcArchetype.Warrior);
                var vitals=store.ReadNpcVitals(id,NpcArchetype.Warrior);
                Check.True(store.TryCaptureNpcVitals(id,NpcArchetype.Warrior,vitals.MaximumHealth-4,
                    vitals.MaximumMagicka,400,vitals.Revision));
                Check.Equal(1L,store.ReadNpcActor(id,NpcArchetype.Warrior).Revision);
                Check.Equal(skills.Revision,store.ReadNpcActor(id,NpcArchetype.Warrior).Revision);
                Check.True(store.AwardNpcSkillExperience(id,NpcArchetype.Warrior,20,1000,skills.Revision));
                var after=store.ReadNpcProgress(id);
                Check.Equal(2L,after.Revision);Check.True(after.VitalsInitialized);Check.Equal(1L,after.VitalsRevision);
                Check.Equal(vitals.MaximumHealth-4,after.CurrentHealth);
            })));

            cases.Add(("Capturing unchanged full vitals does not expand an existing skill-only row",(Action)(()=>{
                string id;var store=Create(out id);
                Check.True(store.AwardNpcSkillExperience(id,NpcArchetype.Merchant,14,1000,0));
                var full=store.ReadNpcVitals(id,NpcArchetype.Merchant);
                Check.True(store.TryCaptureNpcVitals(id,NpcArchetype.Merchant,full.CurrentHealth,full.CurrentMagicka,500,full.Revision));
                var progress=store.ReadNpcProgress(id);
                Check.Equal(1L,progress.Revision);Check.Equal(1,progress.Skills.Count);
                Check.True(!progress.VitalsInitialized);Check.Equal(0L,progress.VitalsRevision);
            })));

            cases.Add(("Unchanged resource captures advance timestamp so delayed samples cannot rewind state",(Action)(()=>{
                string id;var store=Create(out id);var baseline=store.ReadNpcVitals(id,NpcArchetype.Healer);
                Check.True(store.TryCaptureNpcVitals(id,NpcArchetype.Healer,baseline.MaximumHealth-2,
                    baseline.MaximumMagicka-1,600,baseline.Revision));
                var first=store.ReadNpcVitals(id,NpcArchetype.Healer);
                Check.True(store.TryCaptureNpcVitals(id,NpcArchetype.Healer,first.CurrentHealth,
                    first.CurrentMagicka,700,first.Revision));
                var observed=store.ReadNpcVitals(id,NpcArchetype.Healer);
                Check.Equal(700d,observed.CapturedAt);Check.Equal(first.Revision,observed.Revision);
                Check.True(!store.TryCaptureNpcVitals(id,NpcArchetype.Healer,baseline.MaximumHealth-5,
                    baseline.MaximumMagicka-2,650,observed.Revision));
                var after=store.ReadNpcVitals(id,NpcArchetype.Healer);
                Check.Equal(first.CurrentHealth,after.CurrentHealth);Check.Equal(700d,after.CapturedAt);
            })));

            cases.Add(("NPC vitals reject stale revisions and older captures while accepting ordered updates",(Action)(()=>{
                string id;var store=Create(out id);var initial=store.ReadNpcVitals(id,NpcArchetype.Thief);
                Check.True(store.TryCaptureNpcVitals(id,NpcArchetype.Thief,initial.MaximumHealth-2,
                    initial.MaximumMagicka,200,initial.Revision));
                var first=store.ReadNpcVitals(id,NpcArchetype.Thief);
                Check.True(!store.TryCaptureNpcVitals(id,NpcArchetype.Thief,initial.MaximumHealth-5,
                    initial.MaximumMagicka-1,199,first.Revision));
                Check.True(!store.TryCaptureNpcVitals(id,NpcArchetype.Thief,initial.MaximumHealth-5,
                    initial.MaximumMagicka-1,201,initial.Revision));
                var unchanged=store.ReadNpcVitals(id,NpcArchetype.Thief);
                Check.Equal(initial.MaximumHealth-2,unchanged.CurrentHealth);Check.Equal(200d,unchanged.CapturedAt);
                Check.True(store.TryCaptureNpcVitals(id,NpcArchetype.Thief,initial.MaximumHealth-5,
                    initial.MaximumMagicka-1,201,first.Revision));
                var later=store.ReadNpcVitals(id,NpcArchetype.Thief);
                Check.Equal(initial.MaximumHealth-5,later.CurrentHealth);Check.Equal(201d,later.CapturedAt);
                Check.Equal(first.Revision+1,later.Revision);
                Check.True(store.TryCaptureNpcVitals(id,NpcArchetype.Thief,later.CurrentHealth,
                    later.CurrentMagicka,202,later.Revision));
                Check.Equal(later.Revision,store.ReadNpcVitals(id,NpcArchetype.Thief).Revision);
            })));

            cases.Add(("Dead NPC identity cannot be restored to positive health or captured alive",(Action)(()=>{
                string id;var store=Create(out id);var initial=store.ReadNpcVitals(id,NpcArchetype.Guard);
                Check.True(store.TryCaptureNpcVitals(id,NpcArchetype.Guard,initial.MaximumHealth-4,
                    initial.MaximumMagicka,300,initial.Revision));
                store.ResolveResident(id).IsAlive=false;
                var dead=store.ReadNpcVitals(id,NpcArchetype.Guard);
                Check.True(!dead.IsAlive);Check.Equal(0,dead.CurrentHealth);
                Check.True(!store.TryCaptureNpcVitals(id,NpcArchetype.Guard,initial.MaximumHealth,
                    initial.MaximumMagicka,301,dead.Revision));
                var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
                var after=restored.ReadNpcVitals(id,NpcArchetype.Guard);
                Check.True(!after.IsAlive);Check.Equal(0,after.CurrentHealth);
            })));

            cases.Add(("Old actor vitals capture token cannot write into a restored save branch",(Action)(()=>{
                string id;var store=Create(out id);var baseline=store.ReadNpcVitals(id,NpcArchetype.Warrior);
                Check.True(store.TryCaptureNpcVitals(id,NpcArchetype.Warrior,baseline.MaximumHealth-2,
                    baseline.MaximumMagicka-1,100,baseline.Revision));
                var staleEpoch=store.CaptureNpcVitalsWriteEpoch();
                var branch=store.Snapshot();branch.NpcActors[0].CurrentHealth=baseline.MaximumHealth-4;
                branch.NpcActors[0].CurrentMagicka=baseline.MaximumMagicka-3;
                store.Restore(branch);
                var restored=store.ReadNpcVitals(id,NpcArchetype.Warrior);
                Check.True(!store.IsNpcVitalsWriteEpochCurrent(staleEpoch));
                Check.True(!store.TryCaptureNpcVitals(staleEpoch,id,NpcArchetype.Warrior,baseline.MaximumHealth-6,
                    baseline.MaximumMagicka-5,200,restored.Revision));
                var after=store.ReadNpcVitals(id,NpcArchetype.Warrior);
                Check.Equal(restored.CurrentHealth,after.CurrentHealth);Check.Equal(restored.CurrentMagicka,after.CurrentMagicka);
                var currentEpoch=store.CaptureNpcVitalsWriteEpoch();
                Check.True(store.TryCaptureNpcVitals(currentEpoch,id,NpcArchetype.Warrior,baseline.MaximumHealth-6,
                    baseline.MaximumMagicka-5,200,restored.Revision));
            })));

            cases.Add(("NPC vitals reject invalid live resources and corrupt persisted records",(Action)(()=>{
                string id;var store=Create(out id);var current=store.ReadNpcVitals(id,NpcArchetype.Mage);
                Check.True(!store.TryCaptureNpcVitals(id,NpcArchetype.Mage,0,current.MaximumMagicka,10,current.Revision));
                Check.True(!store.TryCaptureNpcVitals(id,NpcArchetype.Mage,current.MaximumHealth,current.MaximumMagicka+1,10,current.Revision));
                Check.True(!store.TryCaptureNpcVitals(id,NpcArchetype.Mage,current.MaximumHealth-1,current.MaximumMagicka,double.NaN,current.Revision));
                Check.True(store.TryCaptureNpcVitals(id,NpcArchetype.Mage,current.MaximumHealth-1,current.MaximumMagicka,11,current.Revision));
                var broken=store.Snapshot();broken.NpcActors[0].CurrentHealth=0;
                Check.Throws<ArgumentException>(()=>new WorldStateStore(0).Restore(broken));
                broken=store.Snapshot();broken.NpcActors[0].CurrentMagicka=current.MaximumMagicka+1;
                Check.Throws<ArgumentException>(()=>new WorldStateStore(0).Restore(broken));
                broken=store.Snapshot();broken.NpcActors[0].VitalsCapturedAt=double.PositiveInfinity;
                Check.Throws<ArgumentException>(()=>new WorldStateStore(0).Restore(broken));
            })));
        }
    }
}
