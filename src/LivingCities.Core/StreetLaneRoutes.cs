using System;
using System.Collections.Generic;

namespace LivingCities.Core
{
    public static class StreetLaneRoutes
    {
        public static List<RoutePoint> Offset(IEnumerable<RoutePoint> route,uint residentSeed,double maximumOffset,
            double endpointTaper,Func<RoutePoint,RoutePoint,bool> clearSegment)
        {
            if(route==null || clearSegment==null || !Finite(maximumOffset) || maximumOffset<0 ||
                !Finite(endpointTaper) || endpointTaper<=0)
                throw new ArgumentException("Invalid street lane route.");
            var centerline=ExpandTwoPointRoute(route,endpointTaper);
            if(centerline.Count<2) throw new ArgumentException("Street lane route requires two points.");
            var cumulative=new double[centerline.Count];
            for(int i=0;i<centerline.Count;i++)
            {
                if(!Finite(centerline[i].X) || !Finite(centerline[i].Z))
                    throw new ArgumentException("Street lane route contains an invalid point.");
                if(i>0)
                {
                    double dx=centerline[i].X-centerline[i-1].X,dz=centerline[i].Z-centerline[i-1].Z;
                    double length=Math.Sqrt(dx*dx+dz*dz);
                    if(!Finite(length) || length<=0) throw new ArgumentException("Street lane route contains a zero segment.");
                    cumulative[i]=cumulative[i-1]+length;
                }
            }
            var lane=LaneOffset(residentSeed,maximumOffset);
            for(int attempt=0;attempt<=8;attempt++)
            {
                double scale=1-attempt/8d;
                var candidate=Build(centerline,cumulative,lane*scale,endpointTaper);
                bool clear=true;
                for(int i=1;i<candidate.Count;i++)
                    if(!clearSegment(candidate[i-1],candidate[i])) {clear=false;break;}
                if(clear) return candidate;
            }
            // Centerline is already surveyed and collision checked by its caller.
            return centerline;
        }

        private static List<RoutePoint> Build(List<RoutePoint> route,double[] cumulative,double offset,double taper)
        {
            double total=cumulative[cumulative.Length-1];
            var result=new List<RoutePoint>(route.Count);
            for(int i=0;i<route.Count;i++)
            {
                if(i==0 || i==route.Count-1 || offset==0)
                {result.Add(route[i]);continue;}
                var before=route[i-1];var point=route[i];var after=route[i+1];
                double inX=point.X-before.X,inZ=point.Z-before.Z;
                double outX=after.X-point.X,outZ=after.Z-point.Z;
                double inLength=Math.Sqrt(inX*inX+inZ*inZ),outLength=Math.Sqrt(outX*outX+outZ*outZ);
                inX/=inLength;inZ/=inLength;outX/=outLength;outZ/=outLength;
                double normalX=-(inZ+outZ),normalZ=inX+outX;
                double normalLength=Math.Sqrt(normalX*normalX+normalZ*normalZ);
                if(normalLength<0.000001) {normalX=-inZ;normalZ=inX;normalLength=1;}
                normalX/=normalLength;normalZ/=normalLength;
                double denominator=Math.Abs(normalX*(-inZ)+normalZ*inX);
                double miter=denominator<0.25 ? 1/0.25 : Math.Min(1/denominator,2);
                double ramp=Math.Min(1,Math.Min(cumulative[i]/taper,(total-cumulative[i])/taper));
                double distance=offset*miter*Math.Max(0,ramp);
                result.Add(new RoutePoint(point.X+normalX*distance,point.Z+normalZ*distance));
            }
            return result;
        }

        // Route simplification can reduce a long, straight street to its two
        // doorway endpoints. Add stable interior controls so lane placement still
        // has room to taper back to those exact approaches.
        private static List<RoutePoint> ExpandTwoPointRoute(IEnumerable<RoutePoint> route,double endpointTaper)
        {
            var points=new List<RoutePoint>(route);
            if(points.Count!=2) return points;
            double dx=points[1].X-points[0].X,dz=points[1].Z-points[0].Z;
            double length=Math.Sqrt(dx*dx+dz*dz);
            double inset=Math.Min(endpointTaper,length/3);
            var expanded=new List<RoutePoint>(4) { points[0],
                new RoutePoint(points[0].X+dx*inset/length,points[0].Z+dz*inset/length),
                new RoutePoint(points[1].X-dx*inset/length,points[1].Z-dz*inset/length),
                points[1] };
            return expanded;
        }

        private static double LaneOffset(uint seed,double maximum)
        {
            unchecked
            {
                uint value=seed;
                value^=value>>16;value*=0x7feb352du;
                value^=value>>15;value*=0x846ca68bu;
                value^=value>>16;
                return ((value&0xffffu)/32767.5-1)*maximum;
            }
        }

        private static bool Finite(double value) {return !double.IsNaN(value) && !double.IsInfinity(value);}
    }
}
