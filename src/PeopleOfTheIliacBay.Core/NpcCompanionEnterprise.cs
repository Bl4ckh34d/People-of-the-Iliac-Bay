using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcCompanionEnterpriseKind {DungeonCrawl=1,Raid=2,Burglary=3,Murder=4}

    [Serializable]
    public sealed class NpcCompanionDefeat
    {
        public string EnemyId;
        public ulong CorpseLoadId;
    }

    [Serializable]
    public sealed class NpcCompanionEnterprise
    {
        public string Id,ActionId,PlanRequestId,GrantRequestId;
        public NpcCompanionEnterpriseKind Kind;
        public long Revision;
        public bool Active,OwnDefeatedEnemies,Containers;
        public double StartedAt,UpdatedAt;
        public List<NpcCompanionDefeat> Defeats=new List<NpcCompanionDefeat>();
        public static NpcCompanionEnterprise Copy(NpcCompanionEnterprise value)
        {
            if(value==null)return null;
            var copy=new NpcCompanionEnterprise {Id=value.Id,ActionId=value.ActionId,PlanRequestId=value.PlanRequestId,
            GrantRequestId=value.GrantRequestId,Kind=value.Kind,Revision=value.Revision,Active=value.Active,
            OwnDefeatedEnemies=value.OwnDefeatedEnemies,Containers=value.Containers,StartedAt=value.StartedAt,UpdatedAt=value.UpdatedAt};
            if(value.Defeats==null){copy.Defeats=null;return copy;}
            foreach(var defeat in value.Defeats)copy.Defeats.Add(defeat==null?null:new NpcCompanionDefeat {EnemyId=defeat.EnemyId,CorpseLoadId=defeat.CorpseLoadId});
            return copy;
        }
        public static void Validate(NpcActionJob job)
        {
            var value=job.CompanionEnterprise;if(value==null)return;
            if(job.Kind!=NpcActionKind.Follow || job.Target.PersonId!="player" || value.ActionId!=job.Id ||
                !IdValid(value.Id) || !IdValid(value.PlanRequestId) || value.Revision<1 ||
                (int)value.Kind<1 || (int)value.Kind>4 || double.IsNaN(value.StartedAt) || double.IsInfinity(value.StartedAt) ||
                double.IsNaN(value.UpdatedAt) || double.IsInfinity(value.UpdatedAt) || value.StartedAt<job.AcceptedAt ||
                value.UpdatedAt<value.StartedAt || value.UpdatedAt>job.UpdatedAt ||
                value.GrantRequestId!=null && !IdValid(value.GrantRequestId) ||
                (value.OwnDefeatedEnemies || value.Containers) && value.GrantRequestId==null)
                throw new ArgumentException("Invalid expedition-specific companion loot permission.");
            if(value.Defeats==null || value.Defeats.Count>256)throw new ArgumentException("Invalid bounded expedition defeat receipts.");
            var enemies=new HashSet<string>(StringComparer.Ordinal);var corpses=new HashSet<ulong>();
            foreach(var defeat in value.Defeats)
                if(defeat==null || !IdValid(defeat.EnemyId) || !enemies.Add(defeat.EnemyId) || defeat.CorpseLoadId!=0 && !corpses.Add(defeat.CorpseLoadId))
                    throw new ArgumentException("Invalid or duplicate native companion defeat.");
        }
        private static bool IdValid(string value) {return !string.IsNullOrWhiteSpace(value) && value.Length<=240;}
        // Native caller must prove exact killer and corpse identity. A generic
        // container or someone else's kill never inherits the own-kill grant.
        public bool AllowsOwnKill(string enterpriseId,string companionId,string actualKillerId,bool isEnemyCorpse)
        {return Active && Id==enterpriseId && OwnDefeatedEnemies && isEnemyCorpse && !string.IsNullOrWhiteSpace(companionId) && companionId==actualKillerId;}
        public bool AllowsContainer(string enterpriseId) {return Active && Id==enterpriseId && Containers;}
        // Native caller independently proves corpse attribution or an unowned,
        // non-quest dungeon treasure source. Permission never supplies that proof.
        public bool AllowsLoadedLoot(string enterpriseId,bool ownDefeatedCorpse,bool unownedDungeonTreasure)
        {return Active && Id==enterpriseId && (OwnDefeatedEnemies && ownDefeatedCorpse || Containers && unownedDungeonTreasure);}
    }

    public sealed partial class WorldStateStore
    {
        public NpcCompanionEnterprise ReadCompanionEnterprise(string person)
        {
            var job=ActiveNpcAction(person);return job==null || NpcActionExecutor.IsTerminal(job)?null:NpcCompanionEnterprise.Copy(job.CompanionEnterprise);
        }
        public bool TryBeginCompanionEnterprise(string person,long expectedActionRevision,string planRequestId,NpcCompanionEnterpriseKind kind,double now,out NpcCompanionEnterprise enterprise)
        {
            enterprise=null;var job=ActiveNpcAction(person);
            if(job==null || job.Revision!=expectedActionRevision || NpcActionExecutor.IsTerminal(job) || job.Kind!=NpcActionKind.Follow ||
                job.Target.PersonId!="player" || now>=job.DeadlineAt || job.FinishAt>0 && now>=job.FinishAt ||
                string.IsNullOrWhiteSpace(planRequestId) || planRequestId.Length>240)return false;
            job.CompanionEnterprise=new NpcCompanionEnterprise {Id="enterprise:"+Guid.NewGuid().ToString("N"),ActionId=job.Id,
                PlanRequestId=planRequestId,Kind=kind,Revision=1,Active=true,StartedAt=now,UpdatedAt=now};
            if(!CommitCompanionRecovery(job,now))return false;enterprise=NpcCompanionEnterprise.Copy(job.CompanionEnterprise);return true;
        }
        public bool TryGrantCompanionLoot(string person,string enterpriseId,long expectedEnterpriseRevision,string grantRequestId,bool ownDefeatedEnemies,bool containers,double now)
        {
            var job=ActiveNpcAction(person);var value=job==null?null:job.CompanionEnterprise;
            if(job==null || NpcActionExecutor.IsTerminal(job) || value==null || !value.Active || value.Id!=enterpriseId ||
                value.Revision!=expectedEnterpriseRevision || value.Revision==long.MaxValue || now<job.UpdatedAt ||
                now>=job.DeadlineAt || job.FinishAt>0 && now>=job.FinishAt || string.IsNullOrWhiteSpace(grantRequestId) || grantRequestId.Length>240)return false;
            value.GrantRequestId=grantRequestId;value.OwnDefeatedEnemies=ownDefeatedEnemies;value.Containers=containers;
            value.Revision++;value.UpdatedAt=now;return CommitCompanionRecovery(job,now);
        }
        public bool TryEndCompanionEnterprise(string person,string enterpriseId,double now)
        {
            var job=ActiveNpcAction(person);var value=job==null?null:job.CompanionEnterprise;
            if(value==null || !value.Active || value.Id!=enterpriseId || value.Revision==long.MaxValue || now<job.UpdatedAt)return false;
            value.Active=false;value.OwnDefeatedEnemies=false;value.Containers=false;value.Revision++;value.UpdatedAt=now;
            return CommitCompanionRecovery(job,now);
        }
        // Native adapter owns actual killer/health/corpse proof. The saved
        // receipt is never inferred from a nearby corpse or from loot wording.
        public bool TryRecordCompanionDefeat(string person,string enterpriseId,string enemyId,ulong corpseLoadId,double now)
        {
            var job=ActiveNpcAction(person);var value=job==null?null:job.CompanionEnterprise;
            if(job==null || NpcActionExecutor.IsTerminal(job) || value==null || !value.Active || value.Id!=enterpriseId ||
                value.Revision==long.MaxValue || now<job.UpdatedAt || now>=job.DeadlineAt || job.FinishAt>0 && now>=job.FinishAt ||
                string.IsNullOrWhiteSpace(enemyId) || enemyId.Length>240)return false;
            var existing=value.Defeats.Find(item=>item.EnemyId==enemyId);
            if(existing==null)
            {if(value.Defeats.Count>=256)return false;value.Defeats.Add(new NpcCompanionDefeat {EnemyId=enemyId,CorpseLoadId=corpseLoadId});}
            else
            {if(existing.CorpseLoadId!=0 || corpseLoadId==0)return false;existing.CorpseLoadId=corpseLoadId;}
            value.Revision++;value.UpdatedAt=now;return CommitCompanionRecovery(job,now);
        }
    }
}
