using System;
using System.Collections.Generic;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    // A route verified in the source scene before a real native doorway transition.
    // The unobserved remainder advances at the resident's normal walking pace.
    [Serializable]
    public sealed class NpcFollowTransition
    {
        public NpcActionTarget Destination;
        public List<NpcActionTarget> SourceRoute=new List<NpcActionTarget>();
        public double StartedAt,ArrivesAt;

        public static bool SameSpace(NpcActionTarget a,NpcActionTarget b)
        {
            return a!=null && b!=null && a.Kind==b.Kind && a.SettlementId==b.SettlementId &&
                (a.Kind==NpcActionTargetKind.IndoorPoint?a.BuildingKey==b.BuildingKey:
                a.Kind==NpcActionTargetKind.WorldPoint && a.WorldSpaceId==b.WorldSpaceId);
        }
        private static bool LocalSpace(NpcActionTarget p)
        {
            return p!=null && p.SettlementId>=0 && (p.Kind==NpcActionTargetKind.IndoorPoint ||
                p.Kind==NpcActionTargetKind.WorldPoint && p.WorldSpaceId=="settlement:"+p.SettlementId.ToString(CultureInfo.InvariantCulture));
        }
        internal static double Distance(NpcActionTarget a,NpcActionTarget b)
        {double x=a.X-b.X,y=a.Y-b.Y,z=a.Z-b.Z;return Math.Sqrt(x*x+y*y+z*z);}
        internal static double Length(IList<NpcActionTarget> route)
        {double length=0;for(int i=1;i<route.Count;i++)length+=Distance(route[i-1],route[i]);return length;}
        public static void Validate(NpcActionJob job)
        {
            var transit=job.FollowTransition;if(transit==null)return;
            if(job.Kind!=NpcActionKind.Follow || job.Target.Kind!=NpcActionTargetKind.Person || job.Target.PersonId!="player" ||
                NpcActionExecutor.IsTerminal(job) || job.Phase!=NpcActionPhase.Approaching || transit.SourceRoute==null ||
                transit.SourceRoute.Count<2 || transit.SourceRoute.Count>128 || !LocalSpace(job.LastPosition) || !LocalSpace(transit.Destination) ||
                transit.Destination.Kind==job.LastPosition.Kind || transit.Destination.SettlementId!=job.LastPosition.SettlementId ||
                double.IsNaN(transit.StartedAt) || double.IsNaN(transit.ArrivesAt) || transit.StartedAt<job.AcceptedAt ||
                transit.StartedAt>job.UpdatedAt || transit.ArrivesAt<=transit.StartedAt || transit.ArrivesAt>=job.DeadlineAt ||
                job.FinishAt>0 && transit.ArrivesAt>=job.FinishAt || transit.ArrivesAt-transit.StartedAt>120)
                throw new ArgumentException("Invalid NPC follow doorway transit.");
            NpcActionContract.ValidateTarget(transit.Destination);
            foreach(var point in transit.SourceRoute)
            {NpcActionContract.ValidateTarget(point);if(!SameSpace(point,job.LastPosition))throw new ArgumentException("Follow route leaves its source scene.");}
            if(Distance(transit.SourceRoute[0],job.LastPosition)>0.01 || Length(transit.SourceRoute)>64)
                throw new ArgumentException("Follow route is not a bounded continuation of the observed position.");
        }
        public static NpcFollowTransition Copy(NpcFollowTransition value)
        {
            if(value==null)return null;
            var copy=new NpcFollowTransition {Destination=NpcActionExecutor.CopyTarget(value.Destination),StartedAt=value.StartedAt,ArrivesAt=value.ArrivesAt};
            if(value.SourceRoute==null){copy.SourceRoute=null;return copy;}
            foreach(var point in value.SourceRoute)copy.SourceRoute.Add(NpcActionExecutor.CopyTarget(point));return copy;
        }
        public NpcActionTarget SourcePosition(double now)
        {
            double remaining=Length(SourceRoute)*Math.Max(0,Math.Min(1,(now-StartedAt)/(ArrivesAt-StartedAt)));
            for(int i=1;i<SourceRoute.Count;i++)
            {
                var a=SourceRoute[i-1];var b=SourceRoute[i];double distance=Distance(a,b);
                if(distance>0 && remaining<distance)
                {var point=NpcActionExecutor.CopyTarget(a);double t=remaining/distance;point.X+=(b.X-a.X)*t;point.Y+=(b.Y-a.Y)*t;point.Z+=(b.Z-a.Z)*t;return point;}
                remaining-=distance;
            }
            return NpcActionExecutor.CopyTarget(SourceRoute[SourceRoute.Count-1]);
        }
    }

    public sealed partial class WorldStateStore
    {
        private NpcActionJob MutableFollow(string id,long revision,double now)
        {
            var job=ReadNpcAction(id);
            if(job==null || job.Revision!=revision || revision==long.MaxValue || !Finite(now) || now<job.UpdatedAt ||
                NpcActionExecutor.IsTerminal(job) || job.Kind!=NpcActionKind.Follow || job.Target.Kind!=NpcActionTargetKind.Person ||
                job.Target.PersonId!="player" || now>=job.DeadlineAt || job.FinishAt>0 && now>=job.FinishAt)return null;
            var resident=ResolveResident(job.ResidentId);
            return resident!=null && resident.IsAlive && !resident.IsQuestProtected?job:null;
        }
        private bool CommitFollow(NpcActionJob job,double now)
        {
            job.Revision++;job.UpdatedAt=now;
            try{NpcActionExecutor.Validate(job);}catch(ArgumentException){return false;}
            state.NpcActions[state.NpcActions.FindIndex(j=>j.Id==job.Id)]=job;IndexNpcAction(job);return true;
        }
        public bool TryBeginNpcFollowTransition(string id,long revision,double now,NpcActionTarget destination,IList<NpcActionTarget> sourceRoute,double secondsPerUnit)
        {
            var job=MutableFollow(id,revision,now);
            if(job==null || job.FollowTransition!=null || sourceRoute==null || sourceRoute.Count<2 || sourceRoute.Count>128 ||
                !Finite(secondsPerUnit) || secondsPerUnit<0.1 || secondsPerUnit>20)return false;
            var transit=new NpcFollowTransition {Destination=NpcActionExecutor.CopyTarget(destination),StartedAt=now};
            foreach(var point in sourceRoute){if(point==null)return false;transit.SourceRoute.Add(NpcActionExecutor.CopyTarget(point));}
            transit.ArrivesAt=now+Math.Max(0.5,NpcFollowTransition.Length(transit.SourceRoute)*secondsPerUnit);
            job.FollowTransition=transit;job.Phase=NpcActionPhase.Approaching;return CommitFollow(job,now);
        }
        public bool TryArriveNpcFollowTransition(string id,long revision,double now,NpcActionTarget landing)
        {
            var job=MutableFollow(id,revision,now);
            if(job==null || job.FollowTransition==null || now<job.FollowTransition.ArrivesAt ||
                !NpcFollowTransition.SameSpace(job.FollowTransition.Destination,landing))return false;
            job.LastPosition=NpcActionExecutor.CopyTarget(landing);job.FollowTransition=null;return CommitFollow(job,now);
        }
        public bool TryResumeNpcFollowSource(string id,long revision,double now)
        {
            var job=MutableFollow(id,revision,now);if(job==null || job.FollowTransition==null)return false;
            job.LastPosition=job.FollowTransition.SourcePosition(now);job.FollowTransition=null;return CommitFollow(job,now);
        }
    }
}
