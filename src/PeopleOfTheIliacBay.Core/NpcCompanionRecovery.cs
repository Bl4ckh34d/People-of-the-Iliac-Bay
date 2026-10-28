using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public static class NpcCompanionWaitPolicy
    {
        // Existing directed trust, native disposition and conversation familiarity.
        // A pleasant first meeting or payment alone never grants friendship waits.
        public static int[] Hours(bool trustKnown,int trust,int disposition,int encounters)
        {
            if(trustKnown && trust>=80 && disposition>=75 && encounters>=5)return new[]{72,72};
            if(trustKnown && trust>=60 && disposition>=50 && encounters>=3)return new[]{12,24};
            return new[]{3,6};
        }
        public static double UnpausedStallSeconds(double accumulated,double frameSeconds,bool unpaused,bool madeProgress)
        {return madeProgress?0:unpaused?accumulated+Math.Max(0,frameSeconds):accumulated;}
        // DaggerfallCityGate uses WorldTime.IsNight: closed 18:00–06:00.
        // Require the actual measured gate prefix to finish before closing.
        public static double GateApproachStart(double desired,double gateWalkSeconds)
        {
            double day=Math.Floor(desired/86400d)*86400d;
            double start=Math.Max(desired,day+6*3600d);
            return start+gateWalkSeconds>=day+18*3600d?day+30*3600d:start;
        }
    }

    public enum NpcCompanionRecoveryPhase {Following=0,WaitingInside=1,ReturningToExit=2,WaitingOutside=3,ReturningToOrigin=4,ResumedRoutine=5}

    [Serializable]
    public sealed class NpcCompanionRecovery
    {
        public NpcActionTarget RecruitmentOrigin,InteriorEntrance,ExteriorEntrance;
        public NpcCompanionRecoveryPhase Phase;
        public double WaitStartedAt,WaitUntil;
        public double WalkSecondsPerUnit;
        public int InsideHours,OutsideHours;
        public bool NoticeDelivered;
        public NpcFollowTransition MeasuredRoute;
        public List<NpcActionTarget> RecruitmentRoute;
        // Compact native on-foot travel estimate between known locations. The
        // approach uses the existing destination settlement survey, not a new world path.
        public NpcActionTarget RecruitmentArrival;
        public List<NpcActionTarget> RecruitmentApproach;
        public double OverlandSeconds,OverlandStartedAt,OverlandArrivesAt;
        public double NativeReturnEstimateSeconds;
        public double RecruitmentGateWalkSeconds;

        public static NpcCompanionRecovery Copy(NpcCompanionRecovery value)
        {
            if(value==null)return null;
            var result=new NpcCompanionRecovery {
                RecruitmentOrigin=NpcActionExecutor.CopyTarget(value.RecruitmentOrigin),
                InteriorEntrance=NpcActionExecutor.CopyTarget(value.InteriorEntrance),ExteriorEntrance=NpcActionExecutor.CopyTarget(value.ExteriorEntrance),
                Phase=value.Phase,WaitStartedAt=value.WaitStartedAt,WaitUntil=value.WaitUntil,WalkSecondsPerUnit=value.WalkSecondsPerUnit,
                InsideHours=value.InsideHours,OutsideHours=value.OutsideHours,NoticeDelivered=value.NoticeDelivered,
                MeasuredRoute=NpcFollowTransition.Copy(value.MeasuredRoute),RecruitmentArrival=NpcActionExecutor.CopyTarget(value.RecruitmentArrival),
                OverlandSeconds=value.OverlandSeconds,OverlandStartedAt=value.OverlandStartedAt,OverlandArrivesAt=value.OverlandArrivesAt,
                NativeReturnEstimateSeconds=value.NativeReturnEstimateSeconds,RecruitmentGateWalkSeconds=value.RecruitmentGateWalkSeconds};
            if(value.RecruitmentRoute!=null)
            {result.RecruitmentRoute=new List<NpcActionTarget>();foreach(var point in value.RecruitmentRoute)result.RecruitmentRoute.Add(NpcActionExecutor.CopyTarget(point));}
            if(value.RecruitmentApproach!=null)
            {result.RecruitmentApproach=new List<NpcActionTarget>();foreach(var point in value.RecruitmentApproach)result.RecruitmentApproach.Add(NpcActionExecutor.CopyTarget(point));}
            return result;
        }
        public static void Validate(NpcActionJob job)
        {
            var value=job.CompanionRecovery;if(value==null)return;
            if(job.Kind!=NpcActionKind.Follow || job.Target==null || job.Target.PersonId!="player" ||
                value.RecruitmentOrigin==null || (int)value.Phase<0 || (int)value.Phase>5)
                throw new ArgumentException("Invalid companion recovery owner or origin.");
            if(value.RecruitmentOrigin.Kind!=NpcActionTargetKind.WorldPoint && value.RecruitmentOrigin.Kind!=NpcActionTargetKind.IndoorPoint)
                throw new ArgumentException("Companion recruitment origin must be an observed spatial point.");
            NpcActionContract.ValidateTarget(value.RecruitmentOrigin);
            if(!Finite(value.NativeReturnEstimateSeconds) || value.NativeReturnEstimateSeconds<0 || value.NativeReturnEstimateSeconds>365*86400d)
                throw new ArgumentException("Invalid native return estimate.");
            if(!Finite(value.RecruitmentGateWalkSeconds) || value.RecruitmentGateWalkSeconds<0 || value.RecruitmentGateWalkSeconds>=12*3600d)
                throw new ArgumentException("Invalid measured recruitment gate duration.");
            if(value.InteriorEntrance!=null)NpcActionContract.ValidateTarget(value.InteriorEntrance);
            if(value.ExteriorEntrance!=null)NpcActionContract.ValidateTarget(value.ExteriorEntrance);
            if((value.InteriorEntrance==null)!=(value.ExteriorEntrance==null) || value.InteriorEntrance!=null &&
                (!NpcFollowTransition.IsDungeonSpace(value.InteriorEntrance) || NpcFollowTransition.IsDungeonSpace(value.ExteriorEntrance) ||
                 value.ExteriorEntrance.Kind!=NpcActionTargetKind.WorldPoint || value.InteriorEntrance.SettlementId!=value.ExteriorEntrance.SettlementId))
                throw new ArgumentException("Companion recovery requires the actual paired dungeon entrance.");
            if(value.RecruitmentRoute!=null)
                ValidateRoute(value.RecruitmentRoute,value.ExteriorEntrance,value.RecruitmentOrigin);
            if(value.RecruitmentArrival!=null)
            {
                NpcActionContract.ValidateTarget(value.RecruitmentArrival);
                if(value.RecruitmentArrival.Kind!=NpcActionTargetKind.WorldPoint || value.RecruitmentArrival.SettlementId!=value.RecruitmentOrigin.SettlementId ||
                    value.RecruitmentOrigin.Kind==NpcActionTargetKind.IndoorPoint && value.RecruitmentArrival.BuildingKey!=value.RecruitmentOrigin.BuildingKey ||
                    value.RecruitmentOrigin.Kind==NpcActionTargetKind.WorldPoint && (!NpcFollowTransition.SameSpace(value.RecruitmentArrival,value.RecruitmentOrigin) ||
                        NpcFollowTransition.Distance(value.RecruitmentArrival,value.RecruitmentOrigin)>0.55) ||
                    !Finite(value.OverlandSeconds) || value.OverlandSeconds<60 || value.OverlandSeconds>365*86400d ||
                    !Finite(value.OverlandStartedAt) || !Finite(value.OverlandArrivesAt) || value.OverlandStartedAt<0 ||
                    value.OverlandStartedAt>0 && (value.OverlandArrivesAt-value.OverlandStartedAt!=value.OverlandSeconds || value.OverlandStartedAt<job.AcceptedAt))
                    throw new ArgumentException("Invalid native companion return location or travel estimate.");
                ValidateRoute(value.RecruitmentApproach,value.RecruitmentApproach==null || value.RecruitmentApproach.Count==0?null:value.RecruitmentApproach[0],value.RecruitmentArrival);
            }
            else if(value.OverlandSeconds!=0 || value.OverlandStartedAt!=0 || value.OverlandArrivesAt!=0 || value.RecruitmentApproach!=null || value.RecruitmentGateWalkSeconds!=0)
                throw new ArgumentException("Companion overland return lacks its real destination.");
            if(value.MeasuredRoute!=null)
            {
                var route=value.MeasuredRoute;
                bool exiting=value.Phase==NpcCompanionRecoveryPhase.WaitingInside || value.Phase==NpcCompanionRecoveryPhase.ReturningToExit;
                var origin=value.RecruitmentArrival??value.RecruitmentOrigin;
                if(!exiting && value.Phase!=NpcCompanionRecoveryPhase.WaitingOutside && value.Phase!=NpcCompanionRecoveryPhase.ReturningToOrigin ||
                    !Finite(value.WalkSecondsPerUnit) || value.WalkSecondsPerUnit<0.1 || value.WalkSecondsPerUnit>20 ||
                    !Finite(route.StartedAt) || !Finite(route.ArrivesAt) || route.StartedAt<job.AcceptedAt ||
                    route.ArrivesAt<=route.StartedAt || route.ArrivesAt-route.StartedAt>86400 ||
                    route.Destination==null || !NpcFollowTransition.SameSpace(route.Destination,exiting?value.ExteriorEntrance:origin) ||
                    NpcFollowTransition.Distance(route.Destination,exiting?value.ExteriorEntrance:origin)>0.01)
                    throw new ArgumentException("Invalid measured companion recovery timing or destination.");
                ValidateRoute(route.SourceRoute,route.SourceRoute==null || route.SourceRoute.Count==0?null:route.SourceRoute[0],exiting?value.InteriorEntrance:origin);
                if(!NpcFollowTransition.SameSpace(job.LastPosition,route.SourceRoute[0]))throw new ArgumentException("Recovery left its measured source space.");
            }
            if(value.Phase==NpcCompanionRecoveryPhase.Following)return;
            if(value.InteriorEntrance==null || value.InsideHours<1 || value.InsideHours>72 || value.OutsideHours<1 || value.OutsideHours>72 ||
                !Finite(value.WaitStartedAt) || !Finite(value.WaitUntil) || value.WaitStartedAt<job.AcceptedAt ||
                value.WaitUntil<value.WaitStartedAt || value.WaitUntil-value.WaitStartedAt>72*3600)
                throw new ArgumentException("Invalid bounded companion recovery wait.");
        }
        private static bool Finite(double value) {return !double.IsNaN(value) && !double.IsInfinity(value);}
        private static void ValidateRoute(IList<NpcActionTarget> route,NpcActionTarget start,NpcActionTarget end)
        {
            if(route==null || route.Count<2 || route.Count>128 || start==null || end==null ||
                !NpcFollowTransition.SameSpace(route[0],start) || NpcFollowTransition.Distance(route[0],start)>0.01 ||
                !NpcFollowTransition.SameSpace(route[route.Count-1],end) || NpcFollowTransition.Distance(route[route.Count-1],end)>0.55)
                throw new ArgumentException("Companion recovery route lacks connected measured endpoints.");
            foreach(var point in route)
            {NpcActionContract.ValidateTarget(point);if(!NpcFollowTransition.SameSpace(point,start))throw new ArgumentException("Measured recovery path leaves its source space.");}
            if(NpcFollowTransition.Length(route)>4096)throw new ArgumentException("Measured recovery path exceeds its bounded length.");
        }
    }

    public sealed partial class WorldStateStore
    {
        // Native caller proves the two points belong to the actual crossed doorway.
        public bool TryCaptureCompanionDungeonEntrance(string id,long revision,double now,NpcActionTarget inside,NpcActionTarget outside)
        {
            var job=RecoveryJob(id,revision,now);if(job==null || job.CompanionRecovery==null ||
                job.CompanionRecovery.Phase!=NpcCompanionRecoveryPhase.Following)return false;
            job.CompanionRecovery.InteriorEntrance=NpcActionExecutor.CopyTarget(inside);
            job.CompanionRecovery.ExteriorEntrance=NpcActionExecutor.CopyTarget(outside);
            job.CompanionRecovery.RecruitmentRoute=null;job.CompanionRecovery.MeasuredRoute=null;
            job.CompanionRecovery.RecruitmentArrival=null;job.CompanionRecovery.RecruitmentApproach=null;
            job.CompanionRecovery.OverlandSeconds=job.CompanionRecovery.OverlandStartedAt=job.CompanionRecovery.OverlandArrivesAt=0;
            job.CompanionRecovery.NativeReturnEstimateSeconds=0;
            job.CompanionRecovery.RecruitmentGateWalkSeconds=0;
            return CommitCompanionRecovery(job,now);
        }
        public bool TryBeginCompanionObstacleWait(string id,long revision,double now)
        {
            var job=RecoveryJob(id,revision,now);var recovery=job==null?null:job.CompanionRecovery;
            if(recovery==null || recovery.Phase!=NpcCompanionRecoveryPhase.Following || recovery.InteriorEntrance==null ||
                !NpcFollowTransition.IsDungeonSpace(job.LastPosition) || !NpcFollowTransition.SameSpace(job.LastPosition,recovery.InteriorEntrance) || job.FollowTransition!=null)return false;
            var relationship=ReadNpcRelationship(job.ResidentId,"player",now);int disposition;
            if(!TryGetActualConversationDisposition(job.ResidentId,out disposition))disposition=ConversationDisposition(job.ResidentId);
            var hours=NpcCompanionWaitPolicy.Hours(relationship!=null && relationship.TrustKnown,
                relationship==null?0:relationship.ReliabilityTrust,disposition,ConversationEncounters(job.ResidentId));
            recovery.InsideHours=hours[0];recovery.OutsideHours=hours[1];recovery.Phase=NpcCompanionRecoveryPhase.WaitingInside;
            recovery.WaitStartedAt=now;recovery.WaitUntil=now+hours[0]*3600;recovery.NoticeDelivered=false;
            return CommitCompanionRecovery(job,now);
        }
        public bool TryAdvanceCompanionRecovery(string id,long revision,double now,NpcCompanionRecoveryPhase next,NpcActionTarget observedPosition=null)
        {
            var job=RecoveryJob(id,revision,now);var value=job==null?null:job.CompanionRecovery;
            if(value==null)return false;
            bool valid=value.Phase==NpcCompanionRecoveryPhase.WaitingInside && next==NpcCompanionRecoveryPhase.ReturningToExit && now>=value.WaitUntil ||
                value.Phase==NpcCompanionRecoveryPhase.ReturningToExit && next==NpcCompanionRecoveryPhase.WaitingOutside &&
                    observedPosition!=null && NpcFollowTransition.SameSpace(job.LastPosition,value.InteriorEntrance) &&
                    NpcFollowTransition.Distance(job.LastPosition,value.InteriorEntrance)<=0.55 &&
                    NpcFollowTransition.SameSpace(observedPosition,value.ExteriorEntrance) && NpcFollowTransition.Distance(observedPosition,value.ExteriorEntrance)<=0.55 ||
                value.Phase==NpcCompanionRecoveryPhase.WaitingOutside && next==NpcCompanionRecoveryPhase.ReturningToOrigin && now>=value.WaitUntil ||
                value.Phase==NpcCompanionRecoveryPhase.ReturningToOrigin && next==NpcCompanionRecoveryPhase.ResumedRoutine &&
                    observedPosition!=null && NpcFollowTransition.SameSpace(observedPosition,value.RecruitmentArrival??value.RecruitmentOrigin) &&
                    NpcFollowTransition.SameSpace(observedPosition,job.LastPosition) && NpcFollowTransition.Distance(observedPosition,job.LastPosition)<=0.55 &&
                    NpcFollowTransition.Distance(observedPosition,value.RecruitmentArrival??value.RecruitmentOrigin)<=0.55;
            if(!valid)return false;
            value.Phase=next;
            value.MeasuredRoute=null;
            if(next==NpcCompanionRecoveryPhase.WaitingOutside){value.WaitStartedAt=now;value.WaitUntil=now+value.OutsideHours*3600;}
            if(observedPosition!=null)job.LastPosition=NpcActionExecutor.CopyTarget(observedPosition);
            if(next==NpcCompanionRecoveryPhase.ResumedRoutine)job.ReturningToRoutine=false;
            return CommitCompanionRecovery(job,now);
        }
        public bool TryMarkCompanionRecoveryNotice(string id,long revision,double now)
        {
            var job=RecoveryJob(id,revision,now);if(job==null || job.CompanionRecovery==null || job.CompanionRecovery.NoticeDelivered)return false;
            job.CompanionRecovery.NoticeDelivered=true;return CommitCompanionRecovery(job,now);
        }
        // Native caller proves the player has physically returned by a short,
        // complete path. This resumes only the still-active agreed service.
        public bool TryResumeCompanionFollow(string id,long revision,double now)
        {
            var job=RecoveryJob(id,revision,now);var value=job==null?null:job.CompanionRecovery;
            if(value==null || NpcActionExecutor.IsTerminal(job) || now>=job.DeadlineAt || job.FinishAt>0 && now>=job.FinishAt ||
                value.Phase==NpcCompanionRecoveryPhase.Following || value.Phase==NpcCompanionRecoveryPhase.ResumedRoutine)return false;
            value.Phase=NpcCompanionRecoveryPhase.Following;value.MeasuredRoute=null;value.WaitStartedAt=value.WaitUntil=0;
            value.InsideHours=value.OutsideHours=0;value.NoticeDelivered=false;return CommitCompanionRecovery(job,now);
        }
        public bool TryCaptureCompanionRecruitmentRoute(string id,long revision,double now,IList<NpcActionTarget> route)
        {
            var job=RecoveryJob(id,revision,now);if(job==null || job.CompanionRecovery==null || job.CompanionRecovery.ExteriorEntrance==null || route==null)return false;
            job.CompanionRecovery.RecruitmentRoute=new List<NpcActionTarget>();
            foreach(var point in route)job.CompanionRecovery.RecruitmentRoute.Add(NpcActionExecutor.CopyTarget(point));
            return CommitCompanionRecovery(job,now);
        }
        public bool TryCaptureCompanionRecoveryRoute(string id,long revision,double now,IList<NpcActionTarget> route,NpcActionTarget destination,double secondsPerUnit)
        {
            var job=RecoveryJob(id,revision,now);var value=job==null?null:job.CompanionRecovery;
            if(value==null || route==null || route.Count<2 || route.Count>128 || !Finite(secondsPerUnit) || secondsPerUnit<0.1 || secondsPerUnit>20 ||
                !NpcFollowTransition.SameSpace(route[0],job.LastPosition) || NpcFollowTransition.Distance(route[0],job.LastPosition)>0.01 ||
                value.Phase==NpcCompanionRecoveryPhase.Following || value.Phase==NpcCompanionRecoveryPhase.ResumedRoutine)return false;
            double start=value.Phase==NpcCompanionRecoveryPhase.WaitingInside || value.Phase==NpcCompanionRecoveryPhase.WaitingOutside?Math.Max(now,value.WaitUntil):now;
            var measured=new NpcFollowTransition {Destination=NpcActionExecutor.CopyTarget(destination),StartedAt=start};
            foreach(var point in route){if(point==null)return false;measured.SourceRoute.Add(NpcActionExecutor.CopyTarget(point));}
            measured.ArrivesAt=start+Math.Max(0.5,NpcFollowTransition.Length(measured.SourceRoute)*secondsPerUnit);
            value.WalkSecondsPerUnit=secondsPerUnit;value.MeasuredRoute=measured;return CommitCompanionRecovery(job,now);
        }
        public bool TryClearCompanionRecoveryRoute(string id,long revision,double now)
        {
            var job=RecoveryJob(id,revision,now);if(job==null || job.CompanionRecovery==null || job.CompanionRecovery.MeasuredRoute==null)return false;
            job.CompanionRecovery.MeasuredRoute=null;return CommitCompanionRecovery(job,now);
        }
        public bool TryAdvanceUnobservedCompanionRecovery(string id,long revision,double now)
        {
            var job=RecoveryJob(id,revision,now);var value=job==null?null:job.CompanionRecovery;var route=value==null?null:value.MeasuredRoute;
            if(route==null || now<route.StartedAt)return false;
            bool exiting=value.Phase==NpcCompanionRecoveryPhase.WaitingInside || value.Phase==NpcCompanionRecoveryPhase.ReturningToExit;
            if(now<route.ArrivesAt)
            {
                value.Phase=exiting?NpcCompanionRecoveryPhase.ReturningToExit:NpcCompanionRecoveryPhase.ReturningToOrigin;
                job.LastPosition=route.SourcePosition(now);
            }
            else
            {
                job.LastPosition=NpcActionExecutor.CopyTarget(route.Destination);value.MeasuredRoute=null;
                value.Phase=exiting?NpcCompanionRecoveryPhase.WaitingOutside:NpcCompanionRecoveryPhase.ResumedRoutine;
                if(exiting)
                {
                    value.WaitStartedAt=route.ArrivesAt;value.WaitUntil=route.ArrivesAt+value.OutsideHours*3600;
                    if(value.RecruitmentRoute!=null)
                    {
                        var origin=new NpcFollowTransition {Destination=NpcActionExecutor.CopyTarget(value.RecruitmentOrigin),StartedAt=value.WaitUntil};
                        foreach(var point in value.RecruitmentRoute)origin.SourceRoute.Add(NpcActionExecutor.CopyTarget(point));
                        origin.ArrivesAt=origin.StartedAt+Math.Max(0.5,NpcFollowTransition.Length(origin.SourceRoute)*value.WalkSecondsPerUnit);
                        value.MeasuredRoute=origin;
                        if(now>=origin.StartedAt)
                        {
                            value.Phase=NpcCompanionRecoveryPhase.ReturningToOrigin;
                            job.LastPosition=origin.SourcePosition(now);
                            if(now>=origin.ArrivesAt){job.LastPosition=NpcActionExecutor.CopyTarget(value.RecruitmentOrigin);value.Phase=NpcCompanionRecoveryPhase.ResumedRoutine;value.MeasuredRoute=null;}
                        }
                    }
                }
                if(value.Phase==NpcCompanionRecoveryPhase.ResumedRoutine)job.ReturningToRoutine=false;
            }
            // The first obstacle notification is one saved notice. Catch-up or
            // intermediate stages never re-open an already delivered Talk window.
            return CommitCompanionRecovery(job,now);
        }
        public bool TryCaptureCompanionOverlandReturn(string id,long revision,double now,NpcActionTarget arrival,IList<NpcActionTarget> approach,double travelSeconds,double secondsPerUnit,double gateWalkSeconds=0)
        {
            var job=RecoveryJob(id,revision,now);var value=job==null?null:job.CompanionRecovery;
            if(value==null || value.ExteriorEntrance==null || approach==null || value.OverlandStartedAt>0 ||
                !Finite(secondsPerUnit) || secondsPerUnit<0.1 || secondsPerUnit>20)return false;
            value.RecruitmentArrival=NpcActionExecutor.CopyTarget(arrival);value.RecruitmentApproach=new List<NpcActionTarget>();
            foreach(var point in approach)value.RecruitmentApproach.Add(NpcActionExecutor.CopyTarget(point));
            value.OverlandSeconds=travelSeconds;value.WalkSecondsPerUnit=secondsPerUnit;
            value.RecruitmentGateWalkSeconds=gateWalkSeconds;
            return CommitCompanionRecovery(job,now);
        }
        public bool TryCaptureCompanionNativeReturnEstimate(string id,long revision,double now,double travelSeconds)
        {
            var job=RecoveryJob(id,revision,now);if(job==null || job.CompanionRecovery==null ||
                !Finite(travelSeconds) || travelSeconds<60 || travelSeconds>365*86400d)return false;
            job.CompanionRecovery.NativeReturnEstimateSeconds=travelSeconds;return CommitCompanionRecovery(job,now);
        }
        public bool TryBeginCompanionOverlandReturn(string id,long revision,double now,double departureAt)
        {
            var job=RecoveryJob(id,revision,now);var value=job==null?null:job.CompanionRecovery;
            if(value==null || value.RecruitmentArrival==null || value.OverlandStartedAt!=0 ||
                value.Phase!=NpcCompanionRecoveryPhase.WaitingOutside && value.Phase!=NpcCompanionRecoveryPhase.ReturningToOrigin ||
                !Finite(departureAt) || departureAt<value.WaitUntil || departureAt>now ||
                !NpcFollowTransition.SameSpace(job.LastPosition,value.ExteriorEntrance))return false;
            value.Phase=NpcCompanionRecoveryPhase.ReturningToOrigin;value.MeasuredRoute=null;
            value.OverlandStartedAt=departureAt;value.OverlandArrivesAt=departureAt+value.OverlandSeconds;
            return CommitCompanionRecovery(job,now);
        }
        public bool TryArriveCompanionOverlandReturn(string id,long revision,double now)
        {
            var job=RecoveryJob(id,revision,now);var value=job==null?null:job.CompanionRecovery;
            if(value==null || value.OverlandStartedAt<=0 || now<value.OverlandArrivesAt || value.MeasuredRoute!=null ||
                NpcFollowTransition.SameSpace(job.LastPosition,value.RecruitmentArrival))return false;
            var route=new NpcFollowTransition {StartedAt=value.RecruitmentGateWalkSeconds>0?
                NpcCompanionWaitPolicy.GateApproachStart(value.OverlandArrivesAt,value.RecruitmentGateWalkSeconds):value.OverlandArrivesAt,
                Destination=NpcActionExecutor.CopyTarget(value.RecruitmentArrival)};
            foreach(var point in value.RecruitmentApproach)route.SourceRoute.Add(NpcActionExecutor.CopyTarget(point));
            route.ArrivesAt=route.StartedAt+Math.Max(0.5,NpcFollowTransition.Length(route.SourceRoute)*value.WalkSecondsPerUnit);
            job.LastPosition=NpcActionExecutor.CopyTarget(route.SourceRoute[0]);value.MeasuredRoute=route;
            return CommitCompanionRecovery(job,now);
        }
        private NpcActionJob RecoveryJob(string id,long revision,double now)
        {
            var job=ReadNpcAction(id);
            if(job==null || job.Kind!=NpcActionKind.Follow || job.Revision!=revision || revision==long.MaxValue || !Finite(now) || now<job.UpdatedAt)return null;
            var resident=ResolveResident(job.ResidentId);return resident!=null && resident.IsAlive && !resident.IsQuestProtected?job:null;
        }
        private bool CommitCompanionRecovery(NpcActionJob job,double now)
        {
            if(!Finite(now) || now<job.UpdatedAt || job.Revision==long.MaxValue)return false;
            job.Revision++;job.UpdatedAt=now;
            try{NpcActionExecutor.Validate(job);}catch(ArgumentException){return false;}
            state.NpcActions[state.NpcActions.FindIndex(value=>value.Id==job.Id)]=job;IndexNpcAction(job);return true;
        }
    }
}
