using System;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>Small bridge from verified native use to sparse companion skill progression.</summary>
    public static class NativeNpcCompanionExperience
    {
        public const long ExperiencePerSuccessfulUse=1;

        public static bool TryAwardSuccessfulUse(WorldStateStore store,string personId,int nativeSkill,double now)
        {int gained;return TryAwardSuccessfulUse(store,personId,nativeSkill,now,out gained);}

        public static bool TryAwardSuccessfulUse(WorldStateStore store,string personId,int nativeSkill,double now,out int skillGain)
        {
            skillGain=0;
            if(store==null || nativeSkill<0 || nativeSkill>=35 || !Finite(now) || now<0 ||
                !IsBoundToPlayer(store,personId,now))return false;
            try
            {
                var actor=store.ReadNpcActor(personId,store.ResolveNpcArchetype(personId));
                if(!store.AwardNpcSkillExperience(personId,actor.Profile.Archetype,nativeSkill,
                    ExperiencePerSuccessfulUse,actor.Revision))return false;
                skillGain=Math.Max(0,store.ReadNpcActor(personId,actor.Profile.Archetype).Skill(nativeSkill)-actor.Skill(nativeSkill));
                return true;
            }
            catch(ArgumentException) {return false;}
        }

        private static bool IsBoundToPlayer(WorldStateStore store,string personId,double now)
        {
            if(string.IsNullOrWhiteSpace(personId))return false;
            var action=store.ActiveNpcAction(personId);
            if(action!=null && !NpcActionExecutor.IsTerminal(action) && !action.ReturningToRoutine &&
                now>=action.AcceptedAt && now>=action.UpdatedAt && now<action.DeadlineAt &&
                action.Kind==NpcActionKind.Follow && action.Target!=null &&
                action.Target.Kind==NpcActionTargetKind.Person &&
                action.Target.PersonId==NpcDirectedRelationship.PlayerTargetId)return true;
            foreach(var contract in store.ReadNpcServiceContracts().Contracts)
                if(contract.NpcId==personId && contract.PayerId==NpcDirectedRelationship.PlayerTargetId &&
                    contract.Status==NpcServiceContractStatus.Active && now>=contract.BlockStartAt && now<contract.BlockEndAt &&
                    (contract.Coverage & NpcServiceCoverage.Follow)!=0)return true;
            return false;
        }

        private static bool Finite(double value) {return !double.IsNaN(value) && !double.IsInfinity(value);}
    }
}
