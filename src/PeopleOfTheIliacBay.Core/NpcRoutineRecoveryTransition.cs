using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    // A measured physical doorway route or outdoor join to a saved routine node.
    // Applies to terminal actions returning to routine during player scene changes.
    [Serializable]
    public sealed class NpcRoutineRecoveryTransition
    {
        public NpcActionTarget Destination;
        public List<NpcActionTarget> SourceRoute=new List<NpcActionTarget>();
        public int OriginBuildingKey=-1;
        public double StartedAt,ArrivesAt;

        internal static void Validate(NpcActionJob job)
        {
            var transit=job.RoutineRecoveryTransition;if(transit==null)return;
            if(!NpcActionExecutor.IsTerminal(job) || !job.ReturningToRoutine || job.LastPosition==null ||
                transit.SourceRoute==null || transit.SourceRoute.Count<2 || transit.SourceRoute.Count>128 ||
                !LocalSpace(job.LastPosition) || !LocalSpace(transit.Destination) ||
                transit.OriginBuildingKey<0 ||
                transit.Destination.SettlementId!=job.LastPosition.SettlementId ||
                !Finite(transit.StartedAt) || !Finite(transit.ArrivesAt) ||
                transit.StartedAt<job.AcceptedAt || transit.StartedAt>job.UpdatedAt ||
                transit.ArrivesAt<=transit.StartedAt || transit.ArrivesAt-transit.StartedAt>120)
                throw new ArgumentException("Invalid routine recovery scene transition or outdoor join.");
            bool sameOutdoorWorld=job.LastPosition.Kind==NpcActionTargetKind.WorldPoint &&
                transit.Destination.Kind==NpcActionTargetKind.WorldPoint &&
                NpcFollowTransition.SameSpace(job.LastPosition,transit.Destination);
            if(!sameOutdoorWorld && transit.Destination.Kind==job.LastPosition.Kind)
                throw new ArgumentException("Routine recovery transition must change scene or join a saved outdoor node.");
            if(transit.Destination.Kind==NpcActionTargetKind.IndoorPoint &&
                transit.Destination.BuildingKey!=transit.OriginBuildingKey ||
                job.LastPosition.Kind==NpcActionTargetKind.IndoorPoint &&
                job.LastPosition.BuildingKey!=transit.OriginBuildingKey)
                throw new ArgumentException("Routine recovery transition cannot use a different player's building.");
            NpcActionContract.ValidateTarget(transit.Destination);
            double length=0;
            for(int i=0;i<transit.SourceRoute.Count;i++)
            {
                var point=transit.SourceRoute[i];
                NpcActionContract.ValidateTarget(point);
                if(!NpcFollowTransition.SameSpace(point,job.LastPosition))
                    throw new ArgumentException("Routine recovery route leaves its source scene.");
                if(i==0 && NpcFollowTransition.Distance(point,job.LastPosition)>0.01)
                    throw new ArgumentException("Routine recovery route does not start at the saved position.");
                if(i>0)length+=NpcFollowTransition.Distance(transit.SourceRoute[i-1],point);
            }
            if(!Finite(length) || length>64)throw new ArgumentException("Routine recovery route is outside its measured bounds.");
            if(sameOutdoorWorld && NpcFollowTransition.Distance(transit.SourceRoute[transit.SourceRoute.Count-1],transit.Destination)>0.01)
                throw new ArgumentException("Outdoor routine recovery route does not end at its saved node.");
        }

        public static NpcRoutineRecoveryTransition Copy(NpcRoutineRecoveryTransition value)
        {
            if(value==null)return null;
            var copy=new NpcRoutineRecoveryTransition {Destination=NpcActionExecutor.CopyTarget(value.Destination),
                OriginBuildingKey=value.OriginBuildingKey,StartedAt=value.StartedAt,ArrivesAt=value.ArrivesAt};
            if(value.SourceRoute==null){copy.SourceRoute=null;return copy;}
            foreach(var point in value.SourceRoute)copy.SourceRoute.Add(NpcActionExecutor.CopyTarget(point));
            return copy;
        }

        public NpcActionTarget SourcePosition(double now)
        {
            double length=0;
            for(int i=1;i<SourceRoute.Count;i++)length+=NpcFollowTransition.Distance(SourceRoute[i-1],SourceRoute[i]);
            double remaining=length*Math.Max(0,Math.Min(1,(now-StartedAt)/(ArrivesAt-StartedAt)));
            for(int i=1;i<SourceRoute.Count;i++)
            {
                var a=SourceRoute[i-1];var b=SourceRoute[i];double span=NpcFollowTransition.Distance(a,b);
                if(span>0 && remaining<span)
                {
                    var point=NpcActionExecutor.CopyTarget(a);double fraction=remaining/span;
                    point.X+=(b.X-a.X)*fraction;point.Y+=(b.Y-a.Y)*fraction;point.Z+=(b.Z-a.Z)*fraction;return point;
                }
                remaining-=span;
            }
            return NpcActionExecutor.CopyTarget(SourceRoute[SourceRoute.Count-1]);
        }

        private static bool LocalSpace(NpcActionTarget point)
        {
            return point!=null && point.SettlementId>=0 &&
                (point.Kind==NpcActionTargetKind.IndoorPoint || point.Kind==NpcActionTargetKind.WorldPoint &&
                point.WorldSpaceId=="settlement:"+point.SettlementId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        private static bool Finite(double value){return !double.IsNaN(value)&&!double.IsInfinity(value);}
    }

    public sealed partial class WorldStateStore
    {
        private NpcActionJob MutableRoutineRecoveryTransition(string id,long revision,double now)
        {
            var job=ReadNpcAction(id);
            if(job==null || job.Revision!=revision || revision==long.MaxValue || !Finite(now) || now<job.UpdatedAt ||
                !NpcActionExecutor.IsTerminal(job) || !job.ReturningToRoutine || job.RoutineRecoveryTransition!=null)return null;
            var resident=ResolveResident(job.ResidentId);
            return resident!=null && resident.IsAlive && !resident.IsQuestProtected?job:null;
        }
        private bool CommitRoutineRecoveryTransition(NpcActionJob job,double now)
        {
            job.Revision++;job.UpdatedAt=now;
            try{NpcActionExecutor.Validate(job);}catch(ArgumentException){return false;}
            state.NpcActions[state.NpcActions.FindIndex(j=>j.Id==job.Id)]=job;IndexNpcAction(job);return true;
        }
        public bool TryBeginNpcRoutineRecoveryTransition(string id,long revision,double now,NpcActionTarget destination,
            IList<NpcActionTarget> sourceRoute,double secondsPerUnit,int originBuildingKey)
        {
            var job=MutableRoutineRecoveryTransition(id,revision,now);
            if(job==null || destination==null || sourceRoute==null || sourceRoute.Count<2 || sourceRoute.Count>128 ||
                !Finite(secondsPerUnit) || secondsPerUnit<0.1 || secondsPerUnit>20 || originBuildingKey<0)return false;
            var transit=new NpcRoutineRecoveryTransition {Destination=NpcActionExecutor.CopyTarget(destination),
                OriginBuildingKey=originBuildingKey,StartedAt=now};
            foreach(var point in sourceRoute){if(point==null)return false;transit.SourceRoute.Add(NpcActionExecutor.CopyTarget(point));}
            transit.ArrivesAt=now+Math.Max(0.5,NpcFollowTransition.Length(transit.SourceRoute)*secondsPerUnit);
            if(transit.ArrivesAt-now>120)return false;
            job.RoutineRecoveryTransition=transit;return CommitRoutineRecoveryTransition(job,now);
        }
        public bool TryArriveNpcRoutineRecoveryTransition(string id,long revision,double now,NpcActionTarget landing)
        {
            var job=ReadNpcAction(id);var transit=job==null?null:job.RoutineRecoveryTransition;
            if(job==null || job.Revision!=revision || revision==long.MaxValue || !Finite(now) || now<job.UpdatedAt ||
                transit==null || now<transit.ArrivesAt || !NpcFollowTransition.SameSpace(transit.Destination,landing))return false;
            var resident=ResolveResident(job.ResidentId);
            if(resident==null || !resident.IsAlive || resident.IsQuestProtected)return false;
            job.LastPosition=NpcActionExecutor.CopyTarget(landing);job.RoutineRecoveryTransition=null;
            return CommitRoutineRecoveryTransition(job,now);
        }
        public bool TryResumeNpcRoutineRecoverySource(string id,long revision,double now)
        {
            var job=ReadNpcAction(id);var transit=job==null?null:job.RoutineRecoveryTransition;
            if(job==null || job.Revision!=revision || revision==long.MaxValue || !Finite(now) || now<job.UpdatedAt || transit==null)return false;
            var resident=ResolveResident(job.ResidentId);
            if(resident==null || !resident.IsAlive || resident.IsQuestProtected)return false;
            job.LastPosition=transit.SourcePosition(now);job.RoutineRecoveryTransition=null;
            return CommitRoutineRecoveryTransition(job,now);
        }
    }
}
