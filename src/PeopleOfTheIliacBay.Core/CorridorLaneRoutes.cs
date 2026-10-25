using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public struct LaneCrossSection
    {
        public double Left,Right,Center;
        public LaneCrossSection(double left,double right,double center=0) {Left=left;Right=right;Center=center;}
    }
    public static class CorridorLaneRoutes
    {
        public static List<RoutePoint> ApproachCenterline(IList<RoutePoint> route)
        {
            var result=new List<RoutePoint>();
            int start=route.Count>=3 ? 1 : 0,end=route.Count>=3 ? route.Count-1 : route.Count;
            for(int i=start;i<end;i++) result.Add(route[i]);return result;
        }
        public static List<RoutePoint> OffsetWithApproaches(IList<RoutePoint> route,uint seed,
            Func<RoutePoint,RoutePoint,LaneCrossSection> survey,Func<RoutePoint,RoutePoint,bool> clear)
        {
            var result=Offset(ApproachCenterline(route),seed,survey,clear);
            if(route.Count>=3) {result.Insert(0,route[0]);result.Add(route[route.Count-1]);}
            return result;
        }
        public static List<RoutePoint> Offset(IList<RoutePoint> route,uint seed,
            Func<RoutePoint,RoutePoint,LaneCrossSection> survey,Func<RoutePoint,RoutePoint,bool> clear)
        {
            var operation=Begin(route,seed,survey,clear);
            while(!operation.IsComplete) operation.Step(16);
            return operation.Points;
        }
        public static Operation Begin(IList<RoutePoint> route,uint seed,
            Func<RoutePoint,RoutePoint,LaneCrossSection> survey,Func<RoutePoint,RoutePoint,bool> clear)
        {return new Operation(route,seed,survey,clear);}
        public sealed class Operation
        {
            private readonly IList<RoutePoint> route;
            private readonly uint seed;
            private readonly Func<RoutePoint,RoutePoint,LaneCrossSection> survey;
            private readonly Func<RoutePoint,RoutePoint,bool> clear;
            private readonly IEnumerator<int> work;
            public bool IsComplete {get;private set;}
            public List<RoutePoint> Points {get;private set;}
            internal Operation(IList<RoutePoint> route,uint seed,Func<RoutePoint,RoutePoint,LaneCrossSection> survey,Func<RoutePoint,RoutePoint,bool> clear)
            {
                if(route==null || survey==null || clear==null) throw new ArgumentNullException();
                this.route=new List<RoutePoint>(route);this.seed=seed;this.survey=survey;this.clear=clear;work=Run().GetEnumerator();
            }
            public void Step(int maximumCallbacks)
            {if(maximumCallbacks<1) throw new ArgumentOutOfRangeException(nameof(maximumCallbacks));
                for(int i=0;i<maximumCallbacks && !IsComplete;i++) if(!work.MoveNext()) IsComplete=true;}
            private IEnumerable<int> Run()
            {
            if(route.Count>2048) {Points=new List<RoutePoint>(route);yield break;}
            var controls=Controls(route);
            if(controls.Count<2) {Points=controls;yield break;}
            for(int i=1;i<controls.Count;i++)
                if(Distance(controls[i-1],controls[i])>4.001) {Points=new List<RoutePoint>(route);yield break;}
            double total=0;
            var distance=new double[controls.Count];
            for(int i=1;i<controls.Count;i++) {total+=Distance(controls[i-1],controls[i]);distance[i]=total;}
            var normals=new RoutePoint[controls.Count];
            var offsets=new double[controls.Count];
            double fraction=Percentile(seed);
            for(int i=1;i<controls.Count-1;i++)
            {
                var normal=NormalAt(controls,i);normals[i]=normal;
                var width=survey(controls[i],normal);
                yield return 0;
                if(!Finite(width.Left) || !Finite(width.Right) || !Finite(width.Center) || width.Left<0 || width.Right<0 ||
                    width.Left>16 || width.Right>16 || Math.Abs(width.Center)>1)
                    throw new ArgumentException("Invalid surveyed road width.");
                double taper=Math.Min(1,Math.Min(distance[i],total-distance[i])/2);
                offsets[i]=(width.Center-width.Right+fraction*(width.Left+width.Right))*taper;
            }
            // Only shrink controls adjoining a rejected segment. A narrow door or
            // obstacle must not force the whole street back onto one centerline.
            for(int attempt=0;attempt<=8;attempt++)
            {
                LimitLateralChanges(offsets,distance);
                var result=Build(controls,normals,offsets);
                var shrink=new bool[result.Count];bool accepted=true;
                for(int i=1;i<result.Count;i++)
                {
                    bool spanClear=clear(result[i-1],result[i]);yield return 0;
                    if(!spanClear)
                    {
                        accepted=false;
                        bool fromBlocked=!clear(result[i-1],result[i-1]);yield return 0;
                        bool toBlocked=!clear(result[i],result[i]);yield return 0;
                        shrink[i-1]|=fromBlocked || !toBlocked;shrink[i]|=toBlocked || !fromBlocked;
                    }
                }
                if(accepted) {Points=result;yield break;}
                for(int i=1;i<offsets.Length-1;i++)
                    if(shrink[i]) offsets[i]=attempt==7 ? 0 : offsets[i]*0.5;
            }
            Points=new List<RoutePoint>(route);
            }
        }
        public static List<RoutePoint> Controls(IList<RoutePoint> route)
        {
            if(route==null) throw new ArgumentNullException(nameof(route));
            var points=new List<RoutePoint>();
            foreach(var p in route)
            {
                if(!Finite(p.X) || !Finite(p.Z)) throw new ArgumentException("Invalid lane point.");
                if(points.Count==0 || Distance(points[points.Count-1],p)>0.00001) points.Add(p);
            }
            if(points.Count<2) return new List<RoutePoint>(route);
            var controls=new List<RoutePoint> {points[0]};
            for(int i=1;i<points.Count;i++)
            {
                double length=Distance(points[i-1],points[i]);
                int count=(int)Math.Min(2048,Math.Ceiling(length/4));
                if(controls.Count+count>2048) return new List<RoutePoint>(route);
                for(int j=1;j<=count;j++)
                {
                    double t=j/(double)count;
                    controls.Add(new RoutePoint(points[i-1].X+(points[i].X-points[i-1].X)*t,
                        points[i-1].Z+(points[i].Z-points[i-1].Z)*t));
                }
            }
            return controls;
        }
        public static RoutePoint NormalAt(IList<RoutePoint> controls,int index)
        {
            double x=controls[index+1].X-controls[index-1].X,z=controls[index+1].Z-controls[index-1].Z;
            double length=Math.Sqrt(x*x+z*z);
            return length<0.00001 ? new RoutePoint(0,0) : new RoutePoint(-z/length,x/length);
        }
        public static double Percentile(uint seed)
        {
            unchecked
            {
                seed^=seed>>16;seed*=0x7feb352du;seed^=seed>>15;seed*=0x846ca68bu;seed^=seed>>16;
                return (seed&0xffffu)/65535d;
            }
        }
        private static List<RoutePoint> Build(List<RoutePoint> controls,RoutePoint[] normals,double[] offsets)
        {
            var result=new List<RoutePoint>(controls.Count);
            for(int i=0;i<controls.Count;i++) result.Add(new RoutePoint(controls[i].X+normals[i].X*offsets[i],
                controls[i].Z+normals[i].Z*offsets[i]));
            return result;
        }
        private static void LimitLateralChanges(double[] offsets,double[] distance)
        {
            // A road widening or one rejected control must not move a person
            // sideways across several metres in a single grid step. Propagate
            // narrowing in both directions, retaining the seed's lane farther
            // along the road. Only shrink toward the original validated line;
            // the normal swept checks still validate every resulting segment.
            for(int i=1;i<offsets.Length;i++)
                offsets[i]=LimitedOffset(offsets[i],offsets[i-1],0.6*(distance[i]-distance[i-1]));
            for(int i=offsets.Length-2;i>=0;i--)
                offsets[i]=LimitedOffset(offsets[i],offsets[i+1],0.6*(distance[i+1]-distance[i]));
        }
        private static double LimitedOffset(double value,double neighbour,double change)
        {
            return value>0 ? Math.Min(value,Math.Max(0,neighbour+change)) :
                Math.Max(value,Math.Min(0,neighbour-change));
        }
        private static double Distance(RoutePoint a,RoutePoint b)
        {double x=b.X-a.X,z=b.Z-a.Z;return Math.Sqrt(x*x+z*z);}
        private static bool Finite(double n) {return !double.IsNaN(n) && !double.IsInfinity(n);}
    }
}
