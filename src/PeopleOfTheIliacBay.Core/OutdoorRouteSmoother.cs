using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public static class OutdoorRouteSmoother
    {
        public static List<SavedRoutePoint> Smooth(IList<SavedRoutePoint> route,
            Func<SavedRoutePoint,SavedRoutePoint,bool> supportedAndClear)
        {
            var operation=Begin(route,supportedAndClear);
            while(!operation.IsComplete) operation.Step(8);
            return operation.Points;
        }

        public static Operation Begin(IList<SavedRoutePoint> route,
            Func<SavedRoutePoint,SavedRoutePoint,bool> supportedAndClear,int maximumShortcutChecks=4096)
        { return new Operation(route,supportedAndClear,maximumShortcutChecks); }

        public static RoundingOperation BeginRounding(IList<SavedRoutePoint> route,
            Func<SavedRoutePoint,SavedRoutePoint,bool> supportedAndClear)
        {return new RoundingOperation(route,supportedAndClear);}

        public sealed class RoundingOperation
        {
            private readonly List<SavedRoutePoint> route=new List<SavedRoutePoint>();
            private readonly Func<SavedRoutePoint,SavedRoutePoint,bool> clear;
            private CornerOperation corner;
            private int index=1;
            public bool IsComplete {get;private set;}
            public int WorkDone {get;private set;}
            public int RoundedCorners {get;private set;}
            public List<SavedRoutePoint> Points {get;private set;}=new List<SavedRoutePoint>();
            internal RoundingOperation(IList<SavedRoutePoint> source,Func<SavedRoutePoint,SavedRoutePoint,bool> clear)
            {
                if(source==null || clear==null) throw new ArgumentNullException();
                this.clear=clear;
                foreach(var p in source) {
                    if(p==null || !Finite(p.X) || !Finite(p.Z)) throw new ArgumentException("Invalid outdoor route.");
                    route.Add(Copy(p));
                }
                if(route.Count<4) {Points=route;IsComplete=true;return;}
                Points.Add(Copy(route[0]));
            }
            public void Step(int maximumChecks)
            {
                if(maximumChecks<1) throw new ArgumentOutOfRangeException(nameof(maximumChecks));
                int remaining=maximumChecks;
                while(!IsComplete && remaining>0)
                {
                    if(index==route.Count-1)
                    {Points.Add(Copy(route[index]));IsComplete=true;break;}
                    // Preserve room for every remaining original control under
                    // the saved-route size limit, even on unusually dense paths.
                    if(Points.Count+route.Count-index+8>10000)
                    {Points.Add(Copy(route[index++]));continue;}
                    if(corner==null) corner=BeginCorner(Points[Points.Count-1],route[index],route[index+1],clear,
                        index==1,index==route.Count-2);
                    int before=corner.WorkDone;
                    corner.Step(remaining);
                    int spent=corner.WorkDone-before;remaining-=spent;WorkDone+=spent;
                    if(!corner.IsComplete) break;
                    Points.AddRange(corner.Points);if(corner.Points.Count>1) RoundedCorners++;
                    index++;corner=null;
                }
            }
        }

        public static List<SavedRoutePoint> RoundCorner(SavedRoutePoint before,SavedRoutePoint corner,SavedRoutePoint after,
            Func<SavedRoutePoint,SavedRoutePoint,bool> supportedAndClear,bool keepIncomingConnector=false,bool keepOutgoingConnector=false)
        {
            var operation=BeginCorner(before,corner,after,supportedAndClear,keepIncomingConnector,keepOutgoingConnector);
            while(!operation.IsComplete) operation.Step(2);
            return operation.Points;
        }
        public static CornerOperation BeginCorner(SavedRoutePoint before,SavedRoutePoint corner,SavedRoutePoint after,
            Func<SavedRoutePoint,SavedRoutePoint,bool> supportedAndClear,bool keepIncomingConnector=false,bool keepOutgoingConnector=false)
        {return new CornerOperation(before,corner,after,supportedAndClear,keepIncomingConnector,keepOutgoingConnector);}

        public sealed class CornerOperation
        {
            private readonly Func<SavedRoutePoint,SavedRoutePoint,bool> clear;
            private readonly List<List<SavedRoutePoint>> candidates=new List<List<SavedRoutePoint>>();
            private readonly SavedRoutePoint before,after;
            private readonly bool keepIncoming,keepOutgoing;
            private int candidate,segment;
            public bool IsComplete {get;private set;}
            public int WorkDone {get;private set;}
            public List<SavedRoutePoint> Points {get;private set;}
            internal CornerOperation(SavedRoutePoint before,SavedRoutePoint corner,SavedRoutePoint after,
                Func<SavedRoutePoint,SavedRoutePoint,bool> clear,bool keepIncoming,bool keepOutgoing)
            {
                if(before==null || corner==null || after==null || clear==null) throw new ArgumentNullException();
                this.before=Copy(before);this.after=Copy(after);this.clear=clear;
                this.keepIncoming=keepIncoming;this.keepOutgoing=keepOutgoing;
                Points=new List<SavedRoutePoint> {Copy(corner)};
                double ax=before.X-corner.X,az=before.Z-corner.Z,bx=after.X-corner.X,bz=after.Z-corner.Z;
                double a=Math.Sqrt(ax*ax+az*az),b=Math.Sqrt(bx*bx+bz*bz);
                if(!Finite(a) || !Finite(b) || a<0.5 || b<0.5 || keepIncoming&&keepOutgoing) {IsComplete=true;return;}
                ax/=a;az/=a;bx/=b;bz/=b;
                double dot=ax*bx+az*bz;
                if(dot<-0.995 || dot>0.8) {IsComplete=true;return;}
                double trim=Math.Min(1.5,Math.Min(a,b)*0.3);
                double nx=-(ax+bx),nz=-(az+bz),n=Math.Sqrt(nx*nx+nz*nz);nx/=n;nz/=n;
                foreach(double shift in new[] {0d,0.25,0.5,1,1.5})
                {
                    // Protected doorway segments remain exact. Bend only on the
                    // street side, with the incoming/outgoing tangent unchanged.
                    if((keepIncoming||keepOutgoing) && shift>0) break;
                    var entry=keepIncoming ? Copy(corner) : new SavedRoutePoint {X=corner.X+ax*trim+nx*shift,Z=corner.Z+az*trim+nz*shift};
                    var exit=keepOutgoing ? Copy(corner) : new SavedRoutePoint {X=corner.X+bx*trim+nx*shift,Z=corner.Z+bz*trim+nz*shift};
                    var curve=new List<SavedRoutePoint>();
                    for(int i=0;i<=8;i++)
                    {
                        double t=i/8d,u=1-t;
                        if(keepIncoming||keepOutgoing)
                        {
                            double control=trim/3;
                            curve.Add(new SavedRoutePoint {
                                X=u*u*u*entry.X+3*u*u*t*(entry.X-ax*control)+3*u*t*t*(exit.X-bx*control)+t*t*t*exit.X,
                                Z=u*u*u*entry.Z+3*u*u*t*(entry.Z-az*control)+3*u*t*t*(exit.Z-bz*control)+t*t*t*exit.Z});
                        }
                        else curve.Add(new SavedRoutePoint {X=u*u*entry.X+2*u*t*(corner.X+nx*shift)+t*t*exit.X,
                            Z=u*u*entry.Z+2*u*t*(corner.Z+nz*shift)+t*t*exit.Z});
                    }
                    candidates.Add(curve);
                }
            }
            public void Step(int maximumChecks)
            {
                if(maximumChecks<1) throw new ArgumentOutOfRangeException(nameof(maximumChecks));
                for(int work=0;work<maximumChecks && !IsComplete;work++)
                {
                    if(candidate>=candidates.Count) {IsComplete=true;break;}
                    var points=candidates[candidate];
                    var a=segment==0 ? before : points[segment-1];
                    var b=segment==points.Count ? after : points[segment];
                    // Exact connector was validated by the route adapter. Door
                    // approaches can lie outside the street's nav footprint.
                    if(segment==0 && keepIncoming || segment==points.Count && keepOutgoing)
                    {segment++;if(segment>points.Count) {Points=points;IsComplete=true;}continue;}
                    WorkDone++;
                    if(!clear(a,b)) {candidate++;segment=0;continue;}
                    if(++segment>points.Count) {Points=points;IsComplete=true;}
                }
            }
        }

        public sealed class Operation
        {
            // Each native callback samples at most 24 m. Work can be sliced by
            // callback count, independently of the number of original grid cells.
            private const int Lookahead=64;
            private readonly int maximumChecks;
            private const double MaximumDistanceSquared=24*24;
            private readonly List<SavedRoutePoint> route=new List<SavedRoutePoint>();
            private readonly Func<SavedRoutePoint,SavedRoutePoint,bool> clear;
            private int index,candidate;
            public bool IsComplete {get;private set;}
            public int WorkDone {get;private set;}
            public List<SavedRoutePoint> Points {get;private set;}=new List<SavedRoutePoint>();
            internal Operation(IList<SavedRoutePoint> source,Func<SavedRoutePoint,SavedRoutePoint,bool> supportedAndClear,int maximumShortcutChecks)
            {
                if(source==null || supportedAndClear==null) throw new ArgumentNullException();
                if(maximumShortcutChecks<1 || maximumShortcutChecks>4096) throw new ArgumentOutOfRangeException(nameof(maximumShortcutChecks));
                maximumChecks=maximumShortcutChecks;
                clear=supportedAndClear;
                foreach(var point in source)
                {
                    if(point==null || !Finite(point.X) || !Finite(point.Z)) throw new ArgumentException("Invalid outdoor route.");
                    route.Add(Copy(point));
                }
                if(route.Count<5) {Points=route;IsComplete=true;return;}
                // Door-to-grid and grid-to-door segments are protected. Gate
                // crossings are assembled from separate operations by the adapter.
                Points.Add(Copy(route[0]));Points.Add(Copy(route[1]));index=1;
                candidate=Math.Min(route.Count-2,index+Lookahead);
            }
            public void Step(int maximumChecks)
            {
                if(maximumChecks<1) throw new ArgumentOutOfRangeException(nameof(maximumChecks));
                int end=WorkDone+Math.Min(maximumChecks,this.maximumChecks-WorkDone);
                while(!IsComplete)
                {
                    if(index>=route.Count-2)
                    {Points.Add(Copy(route[route.Count-1]));IsComplete=true;break;}
                    if(WorkDone>=this.maximumChecks)
                    {
                        for(int i=index+1;i<route.Count;i++) Points.Add(Copy(route[i]));
                        IsComplete=true;break;
                    }
                    if(candidate<=index+1) {Append(index+1);continue;}
                    var a=route[index];var b=route[candidate];
                    double x=b.X-a.X,z=b.Z-a.Z;
                    if(x*x+z*z>MaximumDistanceSquared) {candidate--;continue;}
                    if(WorkDone>=end) break;
                    WorkDone++;
                    if(clear(Copy(a),Copy(b))) Append(candidate);
                    else candidate--;
                }
            }
            private void Append(int next)
            {Points.Add(Copy(route[next]));index=next;candidate=Math.Min(route.Count-2,index+Lookahead);}
        }
        private static SavedRoutePoint Copy(SavedRoutePoint point) {return new SavedRoutePoint {X=point.X,Z=point.Z};}
        private static bool Finite(double number) {return !double.IsNaN(number) && !double.IsInfinity(number);}
    }
}
