using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public enum HouseholdJointPhase { Waiting,Outbound,AtInn,Visiting,Returning,Completed,Cancelled }
    [Serializable]
    public sealed class HouseholdRouteCursor
    {
        public int Segment;
        public double Distance;
        public SavedRoutePoint Position;
    }
    [Serializable]
    public sealed class HouseholdJointProgress
    {
        public string HouseholdDayId,SourceFingerprint,FirstResidentId,SecondResidentId;
        public int SettlementId,HomeKey,Generation,AbsoluteDay,Sequence;
        public HouseholdJointPhase Phase;
        public double CreatedAt,ObservedAt,DepartedAt,EnteredAt,ReturnStartedAt,RetiredAt;
        public string RetirementReason;
        public HouseholdRouteCursor First,Second;
        public bool Retired {get{return RetirementReason!=null;}}
        public bool Active {get{return !Retired && Phase!=HouseholdJointPhase.Completed && Phase!=HouseholdJointPhase.Cancelled;}}
        public static HouseholdJointProgress Copy(HouseholdJointProgress source)
        {
            if(source==null || source.SettlementId<0 || source.HomeKey<0 || source.Generation<0 || source.AbsoluteDay<0 || source.Sequence<0 ||
                source.HouseholdDayId!=HouseholdDayPlan.Identity(source.SettlementId,source.HomeKey,source.Generation,source.AbsoluteDay) ||
                !Text(source.FirstResidentId) || !Text(source.SecondResidentId) || source.FirstResidentId==source.SecondResidentId ||
                source.SourceFingerprint==null || source.SourceFingerprint.Length!=64 ||
                !Enum.IsDefined(typeof(HouseholdJointPhase),source.Phase) ||
                !Time(source.CreatedAt) || !Time(source.ObservedAt) || source.ObservedAt<source.CreatedAt ||
                !Time(source.DepartedAt) || !Time(source.EnteredAt) || !Time(source.ReturnStartedAt) ||
                source.CreatedAt<source.AbsoluteDay*86400d || source.CreatedAt>=(source.AbsoluteDay+1d)*86400d)
                throw new ArgumentException("Invalid shared household progress.");
            foreach(char c in source.SourceFingerprint)if(!(c>='0'&&c<='9' || c>='a'&&c<='f'))
                throw new ArgumentException("Invalid shared household source fingerprint.");
            if(!Time(source.RetiredAt) || (source.Retired?source.RetirementReason!="source-unavailable-at-home" &&
                source.RetirementReason!="event-cancelled-at-home" && source.RetirementReason!="physical-recovery-at-home" &&
                source.RetirementReason!="participants-dead" && source.RetirementReason!="simulated-return-home" && source.RetirementReason!="missed-shared-departure" ||
                source.RetiredAt<source.ObservedAt || source.Sequence<1:source.RetiredAt!=0))
                throw new ArgumentException("Invalid shared progress retirement.");
            bool departed=source.Phase!=HouseholdJointPhase.Waiting && source.Phase!=HouseholdJointPhase.Cancelled;
            bool returning=source.Phase==HouseholdJointPhase.Returning || source.Phase==HouseholdJointPhase.Completed;
            if(departed ? source.DepartedAt<source.CreatedAt || source.DepartedAt>source.ObservedAt : source.DepartedAt!=0 || source.ReturnStartedAt!=0)
                throw new ArgumentException("Invalid shared household departure chronology.");
            if(returning ? source.ReturnStartedAt<source.DepartedAt || source.ReturnStartedAt>source.ObservedAt : source.ReturnStartedAt!=0)
                throw new ArgumentException("Invalid shared household return chronology.");
            bool entered=source.EnteredAt>0;
            if(source.Phase==HouseholdJointPhase.Visiting && !entered || entered &&
                (source.Phase!=HouseholdJointPhase.Visiting && !returning || source.EnteredAt<source.DepartedAt ||
                 source.EnteredAt>source.ObservedAt || returning && source.EnteredAt>source.ReturnStartedAt))
                throw new ArgumentException("Invalid shared Inn entry chronology.");
            return new HouseholdJointProgress{HouseholdDayId=source.HouseholdDayId,SourceFingerprint=source.SourceFingerprint,
                FirstResidentId=source.FirstResidentId,SecondResidentId=source.SecondResidentId,
                SettlementId=source.SettlementId,HomeKey=source.HomeKey,Generation=source.Generation,AbsoluteDay=source.AbsoluteDay,
                Phase=source.Phase,Sequence=source.Sequence,CreatedAt=source.CreatedAt,ObservedAt=source.ObservedAt,DepartedAt=source.DepartedAt,
                EnteredAt=source.EnteredAt,ReturnStartedAt=source.ReturnStartedAt,RetiredAt=source.RetiredAt,RetirementReason=source.RetirementReason,
                First=CopyCursor(source.First),Second=CopyCursor(source.Second)};
        }
        private static HouseholdRouteCursor CopyCursor(HouseholdRouteCursor cursor)
        {
            if(cursor==null || cursor.Segment<0 || cursor.Segment>=10000 || !Time(cursor.Distance) || cursor.Distance>10000000 ||
                !PointValid(cursor.Position))throw new ArgumentException("Invalid shared route cursor.");
            return new HouseholdRouteCursor{Segment=cursor.Segment,Distance=cursor.Distance,Position=Point(cursor.Position)};
        }
        internal static HouseholdJointProgress CopyLegacy69(HouseholdJointProgress source)
        {
            if(source==null || source.EnteredAt!=0)throw new ArgumentException("Old schema contains future shared Inn entry fields.");
            var legacy=(HouseholdJointProgress)source.MemberwiseClone();
            // Visiting had no intermediate position updates in schema69; its
            // last observation is exactly the accepted EnterInn transition.
            if(legacy.Phase==HouseholdJointPhase.Visiting)legacy.EnteredAt=legacy.ObservedAt;
            return Copy(legacy);
        }
        internal static SavedRoutePoint Point(SavedRoutePoint p){return new SavedRoutePoint{X=p.X,Z=p.Z};}
        internal static bool PointValid(SavedRoutePoint p)
        {return p!=null && Finite(p.X) && Finite(p.Z) && Math.Abs(p.X)<=10000000 && Math.Abs(p.Z)<=10000000;}
        internal static bool Time(double value){return Finite(value) && value>=0;}
        private static bool Finite(double value){return !double.IsNaN(value) && !double.IsInfinity(value);}
        private static bool Text(string value){return !string.IsNullOrWhiteSpace(value) && value.Length<=256;}
        internal static bool Equivalent(HouseholdJointProgress a,HouseholdJointProgress b)
        {
            return a.HouseholdDayId==b.HouseholdDayId && a.SourceFingerprint==b.SourceFingerprint &&
                a.FirstResidentId==b.FirstResidentId && a.SecondResidentId==b.SecondResidentId &&
                a.SettlementId==b.SettlementId && a.HomeKey==b.HomeKey && a.Generation==b.Generation && a.AbsoluteDay==b.AbsoluteDay &&
                a.Sequence==b.Sequence && a.Phase==b.Phase && a.CreatedAt==b.CreatedAt && a.ObservedAt==b.ObservedAt &&
                a.DepartedAt==b.DepartedAt && a.EnteredAt==b.EnteredAt && a.ReturnStartedAt==b.ReturnStartedAt && a.RetiredAt==b.RetiredAt &&
                a.RetirementReason==b.RetirementReason && SameCursor(a.First,b.First) && SameCursor(a.Second,b.Second);
        }
        private static bool SameCursor(HouseholdRouteCursor a,HouseholdRouteCursor b)
        {return a.Segment==b.Segment && a.Distance==b.Distance && a.Position.X==b.Position.X && a.Position.Z==b.Position.Z;}
    }
    public struct HouseholdJointTargets
    {
        public bool MoveFirst,MoveSecond,NeedsRejoin;
        public SavedRoutePoint First,Second;
    }
    // Physical progress is observation driven. A later clock, query, reload or
    // absent partner never silently moves either participant along a route.
    public sealed partial class HouseholdJointMovement
    {
        public const double MaximumObservedStep=0.55,ArrivalTolerance=0.4,MaximumLead=1.5;
        private const double StartTolerance=0.6,CursorTolerance=2,MaximumLaneSeparation=1.5,MaximumPairSeparation=2;
        private readonly HouseholdDayPlan source;
        private readonly Path[] outbound,returning;
        private HouseholdJointProgress progress;
        private sealed class Path
        {
            internal readonly List<SavedRoutePoint> Points;
            internal readonly double[] Cumulative;
            internal double Length {get{return Cumulative[Cumulative.Length-1];}}
            internal Path(List<SavedRoutePoint> route)
            {
                Points=new List<SavedRoutePoint>();
                foreach(var point in route)
                {
                    if(!HouseholdJointProgress.PointValid(point))throw new ArgumentException("Invalid shared route point.");
                    if(Points.Count==0 || Distance(Points[Points.Count-1],point)>0.000001)Points.Add(HouseholdJointProgress.Point(point));
                }
                if(Points.Count<2)throw new ArgumentException("Shared travel needs a non-empty physical route.");
                Cumulative=new double[Points.Count];
                for(int i=1;i<Points.Count;i++)Cumulative[i]=Cumulative[i-1]+Distance(Points[i-1],Points[i]);
                if(Length>10000000)throw new ArgumentException("Shared route exceeds movement bounds.");
            }
            internal SavedRoutePoint At(HouseholdRouteCursor cursor)
            {
                int index=cursor.Segment;double fraction=(cursor.Distance-Cumulative[index])/(Cumulative[index+1]-Cumulative[index]);
                return new SavedRoutePoint{X=Points[index].X+(Points[index+1].X-Points[index].X)*fraction,
                    Z=Points[index].Z+(Points[index+1].Z-Points[index].Z)*fraction};
            }
            internal double Project(int segment,SavedRoutePoint point)
            {
                var a=Points[segment];var b=Points[segment+1];double x=b.X-a.X,z=b.Z-a.Z;
                double fraction=Math.Max(0,Math.Min(1,((point.X-a.X)*x+(point.Z-a.Z)*z)/(x*x+z*z)));
                return Cumulative[segment]+fraction*(Cumulative[segment+1]-Cumulative[segment]);
            }
            internal SavedRoutePoint AtDistance(double distance)
            {
                int segment=Array.BinarySearch(Cumulative,distance);
                segment=segment>=0?Math.Min(segment,Points.Count-2):Math.Max(0,~segment-1);
                return At(new HouseholdRouteCursor{Segment=segment,Distance=distance});
            }
        }
        private HouseholdJointMovement(HouseholdDayPlan plan)
        {
            source=HouseholdDayPlan.Copy(plan);
            if(source.JointInn==null)throw new ArgumentException("Household day has no shared Inn event.");
            var a=source.JointInn.First.Itinerary;var b=source.JointInn.Second.Itinerary;
            outbound=new[]{new Path(a.Stops[0].Route),new Path(b.Stops[0].Route)};
            returning=new[]{new Path(a.ExitRoute),new Path(b.ExitRoute)};
            ValidateAligned(outbound[0],outbound[1]);ValidateAligned(returning[0],returning[1]);
        }
        public static string SourceFingerprint(HouseholdDayPlan plan)
        {
            var copy=HouseholdDayPlan.Copy(plan);copy.Status="planned";copy.JointSuppressed=false;
            return RoutineRecoveryIdentity.HouseholdDay(copy);
        }
        public static HouseholdJointMovement Create(HouseholdDayPlan plan,double now)
        {
            var motion=new HouseholdJointMovement(plan);
            if(plan.Status!="planned" || plan.JointSuppressed || !HouseholdJointProgress.Time(now) ||
                now<plan.PlannedAt || now<plan.AbsoluteDay*86400d || now>motion.DepartureAt)
                throw new ArgumentException("New shared travel must be prepared before its departure.");
            motion.progress=new HouseholdJointProgress{HouseholdDayId=plan.Id,SourceFingerprint=SourceFingerprint(plan),
                FirstResidentId=plan.First.ResidentId,SecondResidentId=plan.Second.ResidentId,
                SettlementId=plan.SettlementId,HomeKey=plan.HomeKey,Generation=plan.Generation,AbsoluteDay=plan.AbsoluteDay,
                CreatedAt=now,ObservedAt=now,Phase=HouseholdJointPhase.Waiting,
                First=Start(motion.outbound[0],motion.outbound[0].Points[0]),Second=Start(motion.outbound[1],motion.outbound[1].Points[0])};
            return motion;
        }
        public static HouseholdJointMovement Restore(HouseholdDayPlan plan,HouseholdJointProgress saved)
        {
            var motion=new HouseholdJointMovement(plan);var copy=HouseholdJointProgress.Copy(saved);
            if(copy.HouseholdDayId!=plan.Id || copy.SourceFingerprint!=SourceFingerprint(plan) || copy.CreatedAt<plan.PlannedAt ||
                copy.FirstResidentId!=plan.First.ResidentId || copy.SecondResidentId!=plan.Second.ResidentId ||
                copy.CreatedAt>motion.DepartureAt || copy.DepartedAt!=0 && copy.DepartedAt<motion.DepartureAt)
                throw new ArgumentException("Shared movement source does not match this household event.");
            var paths=copy.Phase==HouseholdJointPhase.Returning || copy.Phase==HouseholdJointPhase.Completed?motion.returning:motion.outbound;
            ValidateCursor(paths[0],copy.First);ValidateCursor(paths[1],copy.Second);
            bool home=copy.Phase==HouseholdJointPhase.Waiting || copy.Phase==HouseholdJointPhase.Cancelled;
            bool arrived=copy.Phase==HouseholdJointPhase.AtInn || copy.Phase==HouseholdJointPhase.Visiting || copy.Phase==HouseholdJointPhase.Completed;
            if(home && (copy.First.Distance!=0 || copy.Second.Distance!=0 ||
                Distance(copy.First.Position,paths[0].Points[0])>StartTolerance || Distance(copy.Second.Position,paths[1].Points[0])>StartTolerance) ||
                arrived && (!Arrived(paths[0],copy.First) || !Arrived(paths[1],copy.Second)))
                throw new ArgumentException("Shared movement phase lacks both physical arrivals.");
            motion.progress=copy;return motion;
        }
        public HouseholdJointProgress Snapshot(){return HouseholdJointProgress.Copy(progress);}
        public bool RequiresRecovery {get{return (source.Status!="planned" || source.JointSuppressed) && progress.Active &&
            progress.Phase!=HouseholdJointPhase.Returning;}}
        private double DepartureAt {get{return source.AbsoluteDay*86400d+Math.Max(source.JointInn.First.DepartureSeconds,source.JointInn.Second.DepartureSeconds);}}
        private Path[] Paths {get{return progress.Phase==HouseholdJointPhase.Returning || progress.Phase==HouseholdJointPhase.Completed?returning:outbound;}}
        public void Depart(double now,SavedRoutePoint first,SavedRoutePoint second)
        {
            RequireTime(now);
            if(progress.Phase!=HouseholdJointPhase.Waiting || source.Status!="planned" || source.JointSuppressed || now<DepartureAt ||
                now>=source.AbsoluteDay*86400d+Math.Min(source.JointInn.First.Itinerary.Stops[0].CloseMinute,source.JointInn.Second.Itinerary.Stops[0].CloseMinute)*60d)
                throw new ArgumentException("Both participants must wait for the shared departure and service window.");
            var a=Start(outbound[0],first);var b=Start(outbound[1],second);
            progress.First=a;progress.Second=b;progress.Phase=HouseholdJointPhase.Outbound;
            progress.DepartedAt=now;progress.ObservedAt=now;
            progress.Sequence++;
        }
        public HouseholdJointTargets Targets(bool firstAvailable,bool secondAvailable,bool paused=false)
        {
            var paths=Paths;var result=new HouseholdJointTargets{First=HouseholdJointProgress.Point(paths[0].Points[progress.First.Segment+1]),
                Second=HouseholdJointProgress.Point(paths[1].Points[progress.Second.Segment+1])};
            if(paused || !firstAvailable || !secondAvailable || RequiresRecovery || progress.Retired ||
                progress.Phase!=HouseholdJointPhase.Outbound && progress.Phase!=HouseholdJointPhase.Returning)return result;
            if(Distance(progress.First.Position,progress.Second.Position)>MaximumPairSeparation)
            {result.NeedsRejoin=true;return result;}
            double commonLength=Math.Min(paths[0].Length,paths[1].Length);
            double a=progress.First.Distance/paths[0].Length*commonLength,b=progress.Second.Distance/paths[1].Length*commonLength;
            result.MoveFirst=!Arrived(paths[0],progress.First) && a-b<=MaximumLead;
            result.MoveSecond=!Arrived(paths[1],progress.Second) && b-a<=MaximumLead;
            return result;
        }
        public void Observe(double now,SavedRoutePoint first,SavedRoutePoint second)
        {
            RequireTime(now);
            if(RequiresRecovery || progress.Phase!=HouseholdJointPhase.Outbound && progress.Phase!=HouseholdJointPhase.Returning)
                throw new ArgumentException("Only actual shared travel accepts movement observations.");
            var paths=Paths;var a=Advance(paths[0],progress.First,first);var b=Advance(paths[1],progress.Second,second);
            // Prepare both before publishing either; a bad second observation
            // cannot leave the first parent on a different progress generation.
            progress.First=a;progress.Second=b;progress.ObservedAt=now;
            if(Arrived(paths[0],a) && Arrived(paths[1],b))
                progress.Phase=progress.Phase==HouseholdJointPhase.Outbound?HouseholdJointPhase.AtInn:HouseholdJointPhase.Completed;
            progress.Sequence++;
        }
        public HouseholdJointProgress PreviewObservation(double now,SavedRoutePoint first,SavedRoutePoint second)
        {
            // Share only immutable route/source geometry. A rejected observation
            // or failed durable write must leave the current owner untouched.
            var candidate=(HouseholdJointMovement)MemberwiseClone();candidate.progress=Snapshot();
            candidate.Observe(now,first,second);return candidate.Snapshot();
        }
        public HouseholdJointTargets RejoinTargets(bool firstAvailable,bool secondAvailable,bool paused=false)
        {
            var result=Targets(firstAvailable,secondAvailable,paused);if(!result.NeedsRejoin)return result;
            var paths=Paths;double common=Math.Min(paths[0].Length,paths[1].Length);
            double first=progress.First.Distance/paths[0].Length*common,second=progress.Second.Distance/paths[1].Length*common;
            if(first-second>MaximumLead){result.MoveSecond=true;return result;}
            if(second-first>MaximumLead){result.MoveFirst=true;return result;}
            // Lateral drift: rejoin each actor's own current segment, never the
            // partner's position or a globally nearest later route segment.
            result.First=paths[0].At(progress.First);result.Second=paths[1].At(progress.Second);
            result.MoveFirst=Distance(result.First,progress.First.Position)>0.02;
            result.MoveSecond=Distance(result.Second,progress.Second.Position)>0.02;
            return result;
        }
        public void EnterInn(double now)
        {
            RequireTime(now);
            if(progress.Phase!=HouseholdJointPhase.AtInn || source.Status!="planned" || source.JointSuppressed ||
                now>=source.AbsoluteDay*86400d+Math.Min(source.JointInn.First.Itinerary.Stops[0].CloseMinute,source.JointInn.Second.Itinerary.Stops[0].CloseMinute)*60d)
                throw new ArgumentException("Shared Inn entry requires both actual arrivals and an open venue.");
            progress.Phase=HouseholdJointPhase.Visiting;progress.EnteredAt=now;progress.ObservedAt=now;
            progress.Sequence++;
        }
        public void BeginReturn(double now,SavedRoutePoint first,SavedRoutePoint second)
        {
            RequireTime(now);
            if(progress.Phase!=HouseholdJointPhase.AtInn && progress.Phase!=HouseholdJointPhase.Visiting)
                throw new ArgumentException("Shared return requires both participants at their actual Inn doorway.");
            var a=Start(returning[0],first);var b=Start(returning[1],second);
            progress.First=a;progress.Second=b;progress.Phase=HouseholdJointPhase.Returning;
            progress.ReturnStartedAt=now;progress.ObservedAt=now;
            progress.Sequence++;
        }
        public void CancelBeforeDeparture(double now)
        {
            RequireTime(now);
            if(progress.Phase!=HouseholdJointPhase.Waiting)throw new ArgumentException("Active shared travel needs physical recovery.");
            progress.Phase=HouseholdJointPhase.Cancelled;progress.ObservedAt=now;
            progress.Sequence++;
        }
        private void RequireTime(double now)
        {if(!HouseholdJointProgress.Time(now) || now<progress.ObservedAt || progress.Sequence==int.MaxValue || progress.Retired)
            throw new ArgumentException("Shared observation time or sequence cannot advance.");}
        internal HouseholdJointMovement VerifiedNext(HouseholdJointProgress candidate)
        {
            var copy=HouseholdJointProgress.Copy(candidate);
            if(progress.Sequence==int.MaxValue || copy.Sequence!=progress.Sequence+1)
                throw new ArgumentException("Shared checkpoint sequence is stale or skipped.");
            // Immutable source/routes can be shared. Only the small progress
            // record is forked; a rejected transition cannot alter this owner.
            var next=(HouseholdJointMovement)MemberwiseClone();next.progress=Snapshot();
            if(progress.Phase==HouseholdJointPhase.Waiting && copy.Phase==HouseholdJointPhase.Outbound)
                next.Depart(copy.ObservedAt,copy.First.Position,copy.Second.Position);
            else if(progress.Phase==HouseholdJointPhase.Waiting && copy.Phase==HouseholdJointPhase.Cancelled)
                next.CancelBeforeDeparture(copy.ObservedAt);
            else if(progress.Phase==HouseholdJointPhase.Outbound && (copy.Phase==HouseholdJointPhase.Outbound || copy.Phase==HouseholdJointPhase.AtInn) ||
                progress.Phase==HouseholdJointPhase.Returning && (copy.Phase==HouseholdJointPhase.Returning || copy.Phase==HouseholdJointPhase.Completed))
                next.Observe(copy.ObservedAt,copy.First.Position,copy.Second.Position);
            else if(progress.Phase==HouseholdJointPhase.AtInn && copy.Phase==HouseholdJointPhase.Visiting)
                next.EnterInn(copy.ObservedAt);
            else if((progress.Phase==HouseholdJointPhase.AtInn || progress.Phase==HouseholdJointPhase.Visiting) && copy.Phase==HouseholdJointPhase.Returning)
                next.BeginReturn(copy.ObservedAt,copy.First.Position,copy.Second.Position);
            else throw new ArgumentException("Shared checkpoint transition is not executable.");
            if(!HouseholdJointProgress.Equivalent(copy,next.progress))
                throw new ArgumentException("Shared checkpoint lacks the exact paired movement transition.");
            return next;
        }
        private static HouseholdRouteCursor Start(Path path,SavedRoutePoint actual)
        {
            if(!HouseholdJointProgress.PointValid(actual) || Distance(path.Points[0],actual)>StartTolerance)
                throw new ArgumentException("Shared travel must start at its actual saved doorway.");
            var cursor=new HouseholdRouteCursor{Position=HouseholdJointProgress.Point(actual),Distance=path.Project(0,actual)};
            ValidateCursor(path,cursor);return cursor;
        }
        private static HouseholdRouteCursor Advance(Path path,HouseholdRouteCursor previous,SavedRoutePoint actual)
        {
            if(!HouseholdJointProgress.PointValid(actual) || Distance(previous.Position,actual)>MaximumObservedStep+0.000001)
                throw new ArgumentException("Shared actor observation exceeds the physical movement step.");
            int segment=previous.Segment;
            if(segment>0)
            {
                var incomingFrom=path.Points[segment-1];var incomingTo=path.Points[segment];
                double reverse=(actual.X-previous.Position.X)*(incomingTo.X-incomingFrom.X)+
                    (actual.Z-previous.Position.Z)*(incomingTo.Z-incomingFrom.Z);
                var current=new HouseholdRouteCursor{Segment=segment,Distance=path.Project(segment,actual)};
                var behind=new HouseholdRouteCursor{Segment=segment-1,Distance=path.Project(segment-1,actual)};
                // Only observed backwards motion can undo a reached corner.
                // A stationary float-rounded endpoint can project just behind
                // it; a second observation must not fabricate reverse travel.
                if(reverse< -0.0000000001 && Distance(actual,path.Points[segment])>ArrivalTolerance &&
                    behind.Distance<path.Cumulative[segment]-0.000001 &&
                    Distance(path.At(behind),actual)+0.000001<Distance(path.At(current),actual))segment--;
            }
            var from=path.Points[segment];var to=path.Points[segment+1];
            double dx=to.X-from.X,dz=to.Z-from.Z,length=path.Cumulative[segment+1]-path.Cumulative[segment];
            double fraction=((actual.X-from.X)*dx+(actual.Z-from.Z)*dz)/(length*length);
            // Walking waypoints use the same practical radius as arrival.
            // Actual approach is still required: standing near several short
            // segments must not fabricate movement through them.
            double directed=(actual.X-previous.Position.X)*dx+(actual.Z-previous.Position.Z)*dz;
            bool nativeEndpoint=Distance(actual,to)<=ArrivalTolerance && directed>0.0000000001;
            if((fraction>=1-0.0000001 || nativeEndpoint) && Distance(actual,to)<=CursorTolerance)
                segment=Math.Min(segment+1,path.Points.Count-2);
            double distance=path.Project(segment,actual);
            var result=new HouseholdRouteCursor{Segment=segment,Distance=distance,Position=HouseholdJointProgress.Point(actual)};
            ValidateCursor(path,result);return result;
        }
        private static void ValidateCursor(Path path,HouseholdRouteCursor cursor)
        {
            if(cursor.Segment>=path.Points.Count-1 || cursor.Distance<path.Cumulative[cursor.Segment]-0.000001 ||
                cursor.Distance>path.Cumulative[cursor.Segment+1]+0.000001 ||
                Math.Abs(cursor.Distance-path.Project(cursor.Segment,cursor.Position))>0.000001 ||
                Distance(path.At(cursor),cursor.Position)>CursorTolerance)
                throw new ArgumentException("Shared route cursor does not match its actual local segment.");
        }
        private static bool Arrived(Path path,HouseholdRouteCursor cursor)
        {return cursor.Segment==path.Points.Count-2 && Distance(cursor.Position,path.Points[path.Points.Count-1])<=ArrivalTolerance;}
        private static void ValidateAligned(Path first,Path second)
        {
            // The union of both normalized vertex distances partitions two
            // piecewise linear paths. Separation on each interval is convex,
            // so checking all interval endpoints also bounds its interior.
            int a=0,b=0;
            while(a<first.Points.Count || b<second.Points.Count)
            {
                double nextA=a<first.Points.Count?first.Cumulative[a]/first.Length:double.PositiveInfinity;
                double nextB=b<second.Points.Count?second.Cumulative[b]/second.Length:double.PositiveInfinity;
                double fraction=Math.Min(nextA,nextB);
                if(Distance(first.AtDistance(fraction*first.Length),second.AtDistance(fraction*second.Length))>MaximumLaneSeparation)
                    throw new ArgumentException("Shared walkers need aligned physical lanes, not divergent detours.");
                if(nextA<=fraction)a++;if(nextB<=fraction)b++;
            }
        }
        private static double Distance(SavedRoutePoint a,SavedRoutePoint b)
        {double x=a.X-b.X,z=a.Z-b.Z;return Math.Sqrt(x*x+z*z);}
    }
}
