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
            // FinishAt ends service. DeadlineAt can include time to return to routine.
            if(action!=null && (NpcActionExecutor.IsTerminal(action) || action.ReturningToRoutine ||
                now<action.AcceptedAt || now<action.UpdatedAt || now>=action.DeadlineAt ||
                action.FinishAt>0 && now>=action.FinishAt))return false;
            string paidContractId=NpcPaidCompanionAction.ContractId(action);
            if(action!=null && paidContractId==null &&
                action.Kind==NpcActionKind.Follow && action.Target!=null &&
                action.Target.Kind==NpcActionTargetKind.Person &&
                action.Target.PersonId==NpcDirectedRelationship.PlayerTargetId)return true;
            bool combat=action!=null && (action.Kind==NpcActionKind.Attack || action.Kind==NpcActionKind.Defend);
            NpcServiceCoverage required=combat?NpcServiceCoverage.Combat:NpcServiceCoverage.Follow;
            bool paidService=false;
            foreach(var contract in store.ReadNpcServiceContracts().Contracts)
                if(contract.NpcId==personId && contract.PayerId==NpcDirectedRelationship.PlayerTargetId &&
                    contract.Status!=NpcServiceContractStatus.Terminated)
                {
                    paidService=true;
                    if(paidContractId!=null && contract.ContractId!=paidContractId ||
                        contract.Status!=NpcServiceContractStatus.Active || now<contract.BlockStartAt || now>=contract.BlockEndAt ||
                        now<contract.LastServiceUpdateAt || (contract.Coverage & required)==0)continue;
                    // Dismissal is authoritative before a refused native wage transfer settles.
                    // The terminal job may already have left the active-action index.
                    foreach(var pending in store.PendingPaidFollowSettlements())
                        if(NpcPaidCompanionAction.ContractId(pending)==contract.ContractId)return false;
                    return true;
                }
            // Follow and assistance are exclusive physical actions. A genuine adventurer
            // who accepts one exact player command is a companion for that action only.
            // Ordinary households and ordinary travelers never become adventurers this way.
            if(paidService || action==null || action.Admission==null ||
                action.Admission.Kind!=NpcOperationAdmissionKind.Conversation ||
                !(combat || action.Kind==NpcActionKind.CastSpell && action.CompanionCast!=null))return false;
            var traveler=store.Traveler(personId);
            return traveler!=null && traveler.IsAlive &&
                TravelerAdventurerPolicy.IsAdventurer(store.ResolveNpcArchetype(personId));
        }

        private static bool Finite(double value) {return !double.IsNaN(value) && !double.IsInfinity(value);}
    }
}
