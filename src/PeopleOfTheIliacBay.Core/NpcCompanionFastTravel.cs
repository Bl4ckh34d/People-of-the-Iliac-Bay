using System;

namespace PeopleOfTheIliacBay.Core
{
    public static class NpcCompanionFastTravel
    {
        public static bool TryPrice(int soloTotal,int soloPieces,int companions,out int total,out int pieces)
        {
            total=pieces=0;
            if(soloTotal<0 || soloPieces<0 || soloPieces>soloTotal || companions<0)return false;
            long party=(long)companions+1,all=(long)soloTotal*party,coins=(long)soloPieces*party;
            if(all>int.MaxValue || coins>int.MaxValue)return false;
            total=(int)all;pieces=(int)coins;return true;
        }
        public static bool CanFollow(WorldStateStore store,NpcActionJob job,double now,bool allowDoorTransition=false)
        {
            if(store==null || job==null || double.IsNaN(now) || double.IsInfinity(now) || NpcActionExecutor.IsTerminal(job) || job.Kind!=NpcActionKind.Follow ||
                job.Target==null || job.Target.Kind!=NpcActionTargetKind.Person || job.Target.PersonId!="player" || job.FollowTransition!=null && !allowDoorTransition ||
                now<job.AcceptedAt || now>=job.DeadlineAt || job.FinishAt>0 && now>=job.FinishAt ||
                job.CompanionRecovery!=null && job.CompanionRecovery.Phase!=NpcCompanionRecoveryPhase.Following)return false;
            var person=store.ResolveResident(job.ResidentId);var current=store.ActiveNpcAction(job.ResidentId);
            if(person==null || !person.IsAlive || person.IsQuestProtected || current==null || current.Id!=job.Id)return false;
            NpcServiceContract contract;
            return !NpcPaidCompanionAction.IsPaidFollowJob(job) || NpcPaidCompanionTravel.TryReadActiveTravel(store,job,now,out contract);
        }
    }
    public sealed partial class WorldStateStore
    {
        public bool TrySetFastTravelFollowOwner(string id,long revision,double now,bool owned)
        {
            var job=ReadNpcAction(id);
            if(job==null || job.Revision!=revision || revision==long.MaxValue || now<job.UpdatedAt ||
                !NpcCompanionFastTravel.CanFollow(this,job,now))return false;
            if(job.FastTravelFollowOwner==owned)return true;
            var person=ResolveResident(job.ResidentId);
            if(person==null || !person.IsAlive || person.IsQuestProtected)return false;
            job.FastTravelFollowOwner=owned;job.UpdatedAt=now;job.Revision++;
            NpcActionExecutor.Validate(job);
            state.NpcActions[state.NpcActions.FindIndex(value=>value.Id==id)]=job;IndexNpcAction(job);return true;
        }
    }
}
