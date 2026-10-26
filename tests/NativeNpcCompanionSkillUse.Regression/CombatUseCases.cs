using System;
using System.Collections.Generic;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.Items;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.DFU;
using UnityEngine;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class CombatUseCases
    {
        internal static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Real swing adapter awards only actual weapon skill after health loss",()=>SwingCase("hit")));
            cases.Add(("Native formula miss awards no companion experience",()=>SwingCase("miss")));
            cases.Add(("Native damage refusal awards no companion experience",()=>SwingCase("no-effect")));
            cases.Add(("Ordinary resident hit leaves seeded skill unchanged",()=>SwingCase("resident")));
            cases.Add(("Cancelled native swing awards no companion experience",()=>SwingCase("cancelled")));
            cases.Add(("Cancellation during native damage awards no companion experience",()=>SwingCase("cancel-during-hit")));
            cases.Add(("Expired Follow hit awards no companion experience",()=>SwingCase("expired")));
            cases.Add(("One successful swing crosses ledger threshold on real actor skill",()=>SwingCase("threshold")));
            cases.Add(("Admitted adventurer attack reaches native hit and persisted skill gain",()=>SwingCase("adventurer-attack")));
        }

        private static void SwingCase(string scenario)
        {
            var store=new WorldStateStore(42);
            var person=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0];
            var job=new NpcActionJob {Id="skill-swing",ProposalId="skill-swing",ResidentId=person.Id,
                Kind=NpcActionKind.Follow,Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player"},
                Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=100,UpdatedAt=100,FinishAt=150,DeadlineAt=200};
            string reason;
            if(scenario!="resident")Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store,job,true,true,true,out reason),reason);
            if(scenario=="adventurer-attack")
            {
                store=NativeNpcCompanionExperienceTests.AssistanceFixture(NpcActionKind.Attack,true,true,out job);
                person=store.ResolveResident(job.ResidentId);
                Check.Equal(NpcActionPhase.Executing,job.Phase);
            }
            var actor=new GameObject().Add<ResidentActor>();actor.Resident=person;
            var body=actor.gameObject.Add<DaggerfallEntityBehaviour>();
            var weapon=new DaggerfallUnityItem {Skill=29};
            body.Entity.Items.Weapon=weapon;body.Entity.ItemEquipTable.Weapon=weapon;
            var profile=store.ReadNpcActor(person.Id,store.ResolveNpcArchetype(person.Id));
            int beforeSkill=profile.Skill(29);long beforeXp=0;
            if(scenario=="threshold" || scenario=="adventurer-attack")
            {
                // Fixture places the existing progression ledger one real use below its next gain.
                long threshold=1;
                while(NpcSkillProgression.Value(profile.Profile.BaseSkill(29),threshold,profile.Profile.TrainingRatePermille(29))==beforeSkill)threshold++;
                beforeXp=threshold-1;
                Check.True(store.AwardNpcSkillExperience(person.Id,profile.Profile.Archetype,29,beforeXp,profile.Revision));
            }
            body.Entity.Skills.SetPermanentSkillValue(29,(short)beforeSkill);
            var target=new GameObject().Add<DaggerfallEntityBehaviour>();target.Entity=new EnemyEntity();
            target.gameObject.Add<DaggerfallEnemy>();
            target.IgnoreDamage=scenario=="no-effect";
            if(scenario=="cancel-during-hit")target.OnDamage=()=>Check.True(store.ObserveNpcAction(
                job.Id,job.Revision,101,NpcActionObservation.Cancelled("Dismissed during native damage.")));
            FormulaHelper.Damage=scenario=="miss"?0:3;
            int damage;
            bool executed=NativeNpcCombatAssist.TrySwing(actor,target,store,scenario=="expired"?150:101,
                ()=>scenario!="cancelled",out damage);
            Check.Equal(scenario!="cancelled",executed);
            bool awards=scenario=="hit" || scenario=="threshold" || scenario=="adventurer-attack";
            var progress=store.ReadNpcProgress(person.Id);
            if(!awards)Check.Equal(null,progress);
            else
            {
                Check.Equal(1,progress.Skills.Count);Check.Equal(29,progress.Skills[0].Skill);
                Check.Equal(beforeXp+1,progress.Skills[0].Experience);
            }
            int expected=beforeSkill+(scenario=="threshold" || scenario=="adventurer-attack"?1:0);
            Check.Equal(expected,(int)body.Entity.Skills.GetPermanentSkillValue(29));
            var restored=new WorldStateStore(9);restored.Restore(store.Snapshot());
            Check.Equal(expected,restored.ReadNpcActor(person.Id,restored.ResolveNpcArchetype(person.Id)).Skill(29));
        }
    }
}
