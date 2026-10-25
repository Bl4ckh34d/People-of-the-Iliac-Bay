using System;

namespace PeopleOfTheIliacBay.Core
{
    [Serializable]
    public sealed class HouseholdSimulatedCursor
    {
        public int Segment;
        public double Distance;
        public SavedRoutePoint Position;
        internal static HouseholdSimulatedCursor Copy(HouseholdSimulatedCursor source)
        {
            if(source==null || source.Segment<0 || source.Segment>=10000 || !HouseholdJointProgress.Time(source.Distance) ||
                source.Distance>10000000 || !HouseholdJointProgress.PointValid(source.Position))
                throw new ArgumentException("Invalid simulated household cursor.");
            return new HouseholdSimulatedCursor{Segment=source.Segment,Distance=source.Distance,Position=HouseholdJointProgress.Point(source.Position)};
        }
        internal HouseholdRouteCursor GeometryCursor()
        {return new HouseholdRouteCursor{Segment=Segment,Distance=Distance,Position=HouseholdJointProgress.Point(Position)};}
    }
    [Serializable]
    public sealed class HouseholdJointSimulation
    {
        public int ModelVersion=1,Revision;
        public string Provenance="world-time-projection",PauseReason,Outcome,ServiceAdmissionId,ServiceReturnReason;
        public HouseholdJointProgress Base;
        public HouseholdHomeArrival HomeArrival;
        public HouseholdJointPhase Phase;
        public HouseholdSimulatedCursor First,Second;
        public double StartedAt,AdvancedThrough,PhaseSince,DepartedAt,ReturnStartedAt,InnClockOffset;
        public double? ReconstructedAt,ReleasedHomeAt,CancelledAt;
        public bool Active {get{return !ReconstructedAt.HasValue && !ReleasedHomeAt.HasValue && !CancelledAt.HasValue;}}
        public bool RequiresRecovery {get{return PauseReason=="leisure-expired" || PauseReason=="duties-changed" || PauseReason=="source-unavailable";}}
        public static HouseholdJointSimulation Begin(HouseholdDayPlan source,HouseholdJointProgress actual,double now)
        {return HouseholdJointMovement.BeginSimulation(source,actual,now);}
        public static HouseholdJointSimulation Advance(HouseholdDayPlan source,HouseholdJointSimulation saved,double now,
            bool firstAvailable=true,bool secondAvailable=true,bool paused=false)
        {return HouseholdJointMovement.AdvanceSimulation(source,saved,now,firstAvailable,secondAvailable,paused);}
        public static HouseholdJointSimulation Copy(HouseholdJointSimulation source)
        {
            if(source==null || source.ModelVersion!=1 && source.ModelVersion!=2 || source.Provenance!="world-time-projection" || source.Revision<0 ||
                source.ModelVersion==1 && (source.ServiceAdmissionId!=null || source.ServiceReturnReason!=null) ||
                source.ReleasedHomeAt.HasValue && (source.ReconstructedAt.HasValue || source.Phase!=HouseholdJointPhase.Completed ||
                    !HouseholdJointProgress.Time(source.ReleasedHomeAt.Value) || source.ReleasedHomeAt.Value<source.AdvancedThrough) ||
                source.CancelledAt.HasValue && (source.ReconstructedAt.HasValue || source.ReleasedHomeAt.HasValue ||
                    source.Phase!=HouseholdJointPhase.Waiting || !HouseholdJointProgress.Time(source.CancelledAt.Value) || source.CancelledAt.Value<source.AdvancedThrough) ||
                source.ModelVersion==2 && (source.Phase==HouseholdJointPhase.Visiting?
                    source.ServiceAdmissionId==null || source.ServiceReturnReason!=null:
                    (source.Phase!=HouseholdJointPhase.Returning && source.Phase!=HouseholdJointPhase.Completed) || source.ServiceReturnReason==null) ||
                source.ServiceReturnReason!=null && source.ServiceReturnReason!="service-completed" && source.ServiceReturnReason!="service-unavailable" &&
                    source.ServiceReturnReason!="observed-visit-completed" ||
                !HouseholdJointProgress.Time(source.InnClockOffset) || source.InnClockOffset>source.AdvancedThrough ||
                source.ServiceAdmissionId!=null && source.ServiceAdmissionId!=source.Base?.HouseholdDayId+":inn-service" ||
                !Enum.IsDefined(typeof(HouseholdJointPhase),source.Phase) || source.Phase==HouseholdJointPhase.Cancelled ||
                !HouseholdJointProgress.Time(source.StartedAt) || !HouseholdJointProgress.Time(source.AdvancedThrough) ||
                source.AdvancedThrough<source.StartedAt || !HouseholdJointProgress.Time(source.PhaseSince) || source.PhaseSince>source.AdvancedThrough ||
                !HouseholdJointProgress.Time(source.DepartedAt) || !HouseholdJointProgress.Time(source.ReturnStartedAt) ||
                source.ReconstructedAt.HasValue && (!HouseholdJointProgress.Time(source.ReconstructedAt.Value) || source.ReconstructedAt.Value<source.AdvancedThrough) ||
                source.PauseReason!=null && source.PauseReason!="game-paused" && source.PauseReason!="partner-unavailable" &&
                    source.PauseReason!="leisure-expired" && source.PauseReason!="duties-changed" &&
                    source.PauseReason!="source-unavailable" && source.PauseReason!="route-proof-unavailable" ||
                (source.Phase==HouseholdJointPhase.Completed?source.Outcome!="returned-home":source.Outcome!=null))
                throw new ArgumentException("Invalid household simulation identity, time or outcome.");
            var actual=HouseholdJointProgress.Copy(source.Base);
            if(!actual.Active || source.StartedAt<actual.ObservedAt || source.PhaseSince<actual.CreatedAt ||
                source.DepartedAt>source.AdvancedThrough || source.ReturnStartedAt>source.AdvancedThrough ||
                (source.Phase==HouseholdJointPhase.Waiting?source.DepartedAt!=0:source.DepartedAt<actual.CreatedAt) ||
                (source.Phase==HouseholdJointPhase.Returning || source.Phase==HouseholdJointPhase.Completed?
                    source.ReturnStartedAt<source.DepartedAt:source.ReturnStartedAt!=0) ||
                source.ModelVersion==1 && (actual.Phase==HouseholdJointPhase.Waiting && source.Phase!=HouseholdJointPhase.Waiting &&
                    source.Phase!=HouseholdJointPhase.Outbound && source.Phase!=HouseholdJointPhase.AtInn ||
                actual.Phase==HouseholdJointPhase.Outbound && source.Phase!=HouseholdJointPhase.Outbound && source.Phase!=HouseholdJointPhase.AtInn ||
                actual.Phase==HouseholdJointPhase.AtInn && source.Phase!=HouseholdJointPhase.AtInn ||
                actual.Phase==HouseholdJointPhase.Visiting && source.Phase!=HouseholdJointPhase.Visiting ||
                actual.Phase==HouseholdJointPhase.Returning && source.Phase!=HouseholdJointPhase.Returning && source.Phase!=HouseholdJointPhase.Completed))
                throw new ArgumentException("Simulated household chronology disagrees with its actual anchor.");
            return new HouseholdJointSimulation{ModelVersion=source.ModelVersion,Revision=source.Revision,Provenance=source.Provenance,
                PauseReason=source.PauseReason,Outcome=source.Outcome,ServiceAdmissionId=source.ServiceAdmissionId,
                ServiceReturnReason=source.ServiceReturnReason,Base=actual,Phase=source.Phase,
                HomeArrival=HouseholdHomeArrival.Copy(source.HomeArrival,source),
                First=HouseholdSimulatedCursor.Copy(source.First),Second=HouseholdSimulatedCursor.Copy(source.Second),
                StartedAt=source.StartedAt,AdvancedThrough=source.AdvancedThrough,PhaseSince=source.PhaseSince,InnClockOffset=source.InnClockOffset,
                DepartedAt=source.DepartedAt,ReturnStartedAt=source.ReturnStartedAt,ReconstructedAt=source.ReconstructedAt,ReleasedHomeAt=source.ReleasedHomeAt,CancelledAt=source.CancelledAt};
        }
    }

    public sealed partial class HouseholdJointMovement
    {
        internal static HouseholdJointSimulation ContinueService(HouseholdDayPlan source,HouseholdJointSimulation saved,
            HouseholdInnServiceAdmission admission,double now,double unavailableAt,double? observedExit=null)
        {
            var result=HouseholdJointSimulation.Copy(saved);ValidateSimulation(source,result);
            if(result.Phase!=HouseholdJointPhase.AtInn && result.Phase!=HouseholdJointPhase.Visiting)
                throw new ArgumentException("Service continuation needs both projected exterior Inn arrivals.");
            result.ModelVersion=2;result.ServiceAdmissionId=admission?.Id;
            if(admission!=null && admission.Outcome=="pending")return result;
            double exit=observedExit??(admission==null?unavailableAt:admission.ReturnAt+result.InnClockOffset);
            if(now<exit)
            {
                result.Phase=HouseholdJointPhase.Visiting;result.AdvancedThrough=now;result.Revision++;
                return HouseholdJointSimulation.Copy(result);
            }
            var motion=Restore(source,result.Base);
            result.First=VirtualAt(motion.returning[0],0);result.Second=VirtualAt(motion.returning[1],0);
            result.Phase=HouseholdJointPhase.Returning;result.ReturnStartedAt=Math.Max(exit,result.AdvancedThrough);
            result.PhaseSince=result.ReturnStartedAt;result.AdvancedThrough=result.ReturnStartedAt;result.PauseReason=null;
            result.ServiceReturnReason=observedExit.HasValue?"observed-visit-completed":admission==null?"service-unavailable":"service-completed";result.Revision++;
            ValidateSimulation(source,result);return HouseholdJointSimulation.Copy(result);
        }
        internal static HouseholdJointProgress ReconstructSimulation(HouseholdDayPlan source,HouseholdJointSimulation saved,double now,
            HouseholdJointPlacement first,HouseholdJointPlacement second)
        {
            var sim=HouseholdJointSimulation.Copy(saved);ValidateSimulation(source,sim);
            if(!sim.Active || !HouseholdJointProgress.Time(now) || now<sim.AdvancedThrough || sim.Base.Sequence==int.MaxValue ||
                sim.Phase==HouseholdJointPhase.Visiting)
                throw new ArgumentException("Shared materialization needs an active exterior projection; Inn bodies require their own visit proof.");
            var motion=Restore(source,sim.Base);
            var paths=sim.Phase==HouseholdJointPhase.Returning || sim.Phase==HouseholdJointPhase.Completed?motion.returning:motion.outbound;
            var result=HouseholdJointProgress.Copy(sim.Base);
            result.First=MaterializedCursor(paths[0],sim.First,first,sim.Base.FirstResidentId);
            result.Second=MaterializedCursor(paths[1],sim.Second,second,sim.Base.SecondResidentId);
            result.Phase=sim.Phase==HouseholdJointPhase.Completed?HouseholdJointPhase.Returning:sim.Phase;
            if(result.Phase==HouseholdJointPhase.AtInn && (!Arrived(paths[0],result.First) || !Arrived(paths[1],result.Second)))
                result.Phase=HouseholdJointPhase.Outbound;
            result.Sequence++;result.ObservedAt=now;result.DepartedAt=sim.DepartedAt;result.ReturnStartedAt=sim.ReturnStartedAt;
            return Restore(source,result).Snapshot();
        }
        private static HouseholdRouteCursor MaterializedCursor(Path path,HouseholdSimulatedCursor expected,
            HouseholdJointPlacement actual,string resident)
        {
            if(actual==null || actual.ResidentId!=resident || !HouseholdJointProgress.PointValid(actual.Position) ||
                Distance(actual.Position,expected.Position)>MaximumObservedStep)
                throw new ArgumentException("Both correctly identified bodies must be near their own projected positions.");
            var result=new HouseholdRouteCursor{Segment=expected.Segment,Distance=path.Project(expected.Segment,actual.Position),
                Position=HouseholdJointProgress.Point(actual.Position)};
            ValidateCursor(path,result);return result;
        }
        internal static HouseholdJointSimulation BeginSimulation(HouseholdDayPlan source,HouseholdJointProgress actual,double now)
        {
            var motion=Restore(source,actual);var anchor=motion.Snapshot();
            if(!anchor.Active || motion.RequiresRecovery || !HouseholdJointProgress.Time(now) || now<anchor.ObservedAt)
                throw new ArgumentException("Shared simulation needs a current eligible physical handoff.");
            double since=anchor.Phase==HouseholdJointPhase.Returning?anchor.ReturnStartedAt:
                anchor.Phase==HouseholdJointPhase.Visiting?anchor.EnteredAt:
                anchor.Phase==HouseholdJointPhase.Outbound?anchor.DepartedAt:anchor.ObservedAt;
            return HouseholdJointSimulation.Copy(new HouseholdJointSimulation{Base=anchor,Phase=anchor.Phase,
                PauseReason=anchor.Phase==HouseholdJointPhase.Waiting && now>=SimulationLeisureUntil(motion.source)?"leisure-expired":null,
                StartedAt=now,AdvancedThrough=now,PhaseSince=since,DepartedAt=anchor.DepartedAt,ReturnStartedAt=anchor.ReturnStartedAt,
                First=VirtualCursor(anchor.First),Second=VirtualCursor(anchor.Second)});
        }
        private static HouseholdSimulatedCursor VirtualCursor(HouseholdRouteCursor actual)
        {return new HouseholdSimulatedCursor{Segment=actual.Segment,Distance=actual.Distance,Position=HouseholdJointProgress.Point(actual.Position)};}
        internal static void ValidateSimulation(HouseholdDayPlan source,HouseholdJointSimulation saved)
        {
            var motion=Restore(source,saved.Base);
            var paths=saved.Phase==HouseholdJointPhase.Returning || saved.Phase==HouseholdJointPhase.Completed?motion.returning:motion.outbound;
            ValidateCursor(paths[0],saved.First.GeometryCursor());ValidateCursor(paths[1],saved.Second.GeometryCursor());
            bool arrived=saved.Phase==HouseholdJointPhase.AtInn || saved.Phase==HouseholdJointPhase.Visiting || saved.Phase==HouseholdJointPhase.Completed;
            if(arrived && (!Arrived(paths[0],saved.First.GeometryCursor()) || !Arrived(paths[1],saved.Second.GeometryCursor())) ||
                saved.Phase==HouseholdJointPhase.Waiting && (saved.First.Distance!=0 || saved.Second.Distance!=0))
                throw new ArgumentException("Projected household phase disagrees with its own virtual cursors.");
        }
        internal static HouseholdJointSimulation AdvanceSimulation(HouseholdDayPlan source,HouseholdJointSimulation saved,double now,
            bool firstAvailable,bool secondAvailable,bool paused)
        {
            var result=HouseholdJointSimulation.Copy(saved);ValidateSimulation(source,result);
            var motion=Restore(source,result.Base);
            if(!result.Active || !HouseholdJointProgress.Time(now) || now<result.AdvancedThrough || motion.RequiresRecovery)
                throw new ArgumentException("Shared simulation cannot advance this source or clock.");
            if(now==result.AdvancedThrough || result.RequiresRecovery || result.Phase==HouseholdJointPhase.Completed)return result;
            if(result.Revision==int.MaxValue)throw new ArgumentException("Shared simulation revision exhausted.");
            if(paused || !firstAvailable || !secondAvailable)
            {result.PauseReason=paused?"game-paused":"partner-unavailable";
                if(result.Phase==HouseholdJointPhase.Visiting)result.InnClockOffset+=now-result.AdvancedThrough;
                result.AdvancedThrough=now;result.Revision++;return result;}
            if(result.Phase==HouseholdJointPhase.AtInn || result.Phase==HouseholdJointPhase.Visiting)
            {
                if(result.PauseReason!=null){result.PauseReason=null;result.Revision++;}
                return result;
            }
            result.PauseReason=null;double from=result.AdvancedThrough;
            double leisure=SimulationLeisureUntil(motion.source);
            if(result.Phase==HouseholdJointPhase.Waiting)
            {
                if(from>=leisure){result.PauseReason="leisure-expired";result.Revision++;return result;}
                double departure=Math.Max(from,motion.DepartureAt);
                if(now<departure){result.AdvancedThrough=now;result.Revision++;return result;}
                result.DepartedAt=departure;result.PhaseSince=departure;result.Phase=HouseholdJointPhase.Outbound;from=departure;
            }
            bool homeward=result.Phase==HouseholdJointPhase.Returning;
            double until=now;
            if(!homeward)
            {
                until=Math.Max(from,Math.Min(now,leisure));
                if(now>=leisure)result.PauseReason="leisure-expired";
            }
            var paths=homeward?motion.returning:motion.outbound;
            double seconds=Math.Max(homeward?motion.source.JointInn.First.Itinerary.ExitWalkSeconds:motion.source.JointInn.First.Itinerary.Stops[0].WalkSeconds,
                homeward?motion.source.JointInn.Second.Itinerary.ExitWalkSeconds:motion.source.JointInn.Second.Itinerary.Stops[0].WalkSeconds);
            double common=Math.Min(paths[0].Length,paths[1].Length),speed=common/seconds;
            double a=result.First.Distance/paths[0].Length*common,b=result.Second.Distance/paths[1].Length*common;
            double budget=(until-from)*speed,needed=common-Math.Min(a,b),used=Math.Min(needed,budget);
            double firstWait=Math.Max(0,a-b-MaximumLead),secondWait=Math.Max(0,b-a-MaximumLead);
            a=Math.Min(common,a+Math.Max(0,used-firstWait));b=Math.Min(common,b+Math.Max(0,used-secondWait));
            result.First=VirtualAt(paths[0],a/common*paths[0].Length);result.Second=VirtualAt(paths[1],b/common*paths[1].Length);
            result.AdvancedThrough=until;result.Revision++;
            if(budget>=needed)
            {
                result.First=VirtualAt(paths[0],paths[0].Length);result.Second=VirtualAt(paths[1],paths[1].Length);
                result.Phase=homeward?HouseholdJointPhase.Completed:HouseholdJointPhase.AtInn;
                result.AdvancedThrough=from+needed/speed;result.PhaseSince=result.AdvancedThrough;result.PauseReason=null;
                result.Outcome=homeward?"returned-home":null;
            }
            ValidateSimulation(source,result);return HouseholdJointSimulation.Copy(result);
        }
        private static double SimulationLeisureUntil(HouseholdDayPlan source)
        {
            double end=double.PositiveInfinity;
            foreach(var outing in new[]{source.JointInn.First,source.JointInn.Second})
                foreach(var visit in ResidentVisitJourney.Create(outing.DepartureSeconds,outing.Itinerary).ScheduledVisits())
                    end=Math.Min(end,source.AbsoluteDay*86400d+visit.LeaveAt);
            return end;
        }
        private static HouseholdSimulatedCursor VirtualAt(Path path,double distance)
        {
            distance=Math.Max(0,Math.Min(path.Length,distance));int segment=Array.BinarySearch(path.Cumulative,distance);
            segment=segment>=0?Math.Min(segment,path.Points.Count-2):Math.Max(0,~segment-1);
            return new HouseholdSimulatedCursor{Segment=segment,Distance=distance,Position=path.AtDistance(distance)};
        }
    }
}
