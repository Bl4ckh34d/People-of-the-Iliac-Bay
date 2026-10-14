using System;
using System.Collections.Generic;
namespace LivingCities.Core
{
    public enum ShopVisitPhase { Walking, Browsing, Haggling, Leaving, Complete }
    public struct ShopVisitSnapshot { public ShopVisitPhase Phase; public GuardPoint3 Position; }
    public sealed class ShopVisitJourney
    {
        private readonly List<List<GuardPoint3>> routes=new List<List<GuardPoint3>>();
        private readonly double[] lengths=new double[4];
        private double start,end,secondsPerMetre,pause;
        public static ShopVisitJourney Create(double startsAt,double endsAt,IList<List<GuardPoint3>> paths,double speed,double timeScale)
        {
            if(!Finite(startsAt) || startsAt<0 || !Finite(endsAt) || endsAt<=startsAt ||
                !Finite(speed) || speed<=0 || !Finite(timeScale) || timeScale<=0 || paths==null || paths.Count!=4)
                throw new ArgumentException("Invalid shop visit.");
            var journey=new ShopVisitJourney { start=startsAt,end=endsAt,secondsPerMetre=timeScale/speed };
            double travel=0;
            for(int leg=0;leg<4;leg++)
            {
                if(paths[leg]==null || paths[leg].Count<2) throw new ArgumentException("Missing shop route.");
                var copy=new List<GuardPoint3>();
                foreach(var point in paths[leg])
                {
                    if(point==null || !Finite(point.X) || !Finite(point.Y) || !Finite(point.Z)) throw new ArgumentException("Invalid shop route point.");
                    copy.Add(new GuardPoint3 { X=point.X,Y=point.Y,Z=point.Z });
                }
                if(leg>0 && Distance(journey.routes[leg-1][journey.routes[leg-1].Count-1],copy[0])>0.001)
                    throw new ArgumentException("Disconnected shop routes.");
                journey.routes.Add(copy);
                for(int i=1;i<copy.Count;i++) journey.lengths[leg]+=Distance(copy[i-1],copy[i]);
                travel+=journey.lengths[leg]*journey.secondsPerMetre;
            }
            journey.pause=endsAt-startsAt-travel;
            if(!Finite(travel) || journey.pause<60) throw new ArgumentException("Visit cannot fit travel and activities.");
            return journey;
        }
        public ShopVisitSnapshot Evaluate(double now)
        {
            if(!Finite(now) || now<0) throw new ArgumentOutOfRangeException(nameof(now));
            if(now>=end) return Sample(ShopVisitPhase.Complete,3,lengths[3]);
            double elapsed=Math.Max(0,now-start);
            for(int leg=0;leg<4;leg++)
            {
                double travel=lengths[leg]*secondsPerMetre;
                if(elapsed<travel) return Sample(leg==3 ? ShopVisitPhase.Leaving : ShopVisitPhase.Walking,leg,elapsed/secondsPerMetre);
                elapsed-=travel;
                double stop=leg<2 ? pause*0.4 : leg==2 ? pause*0.2 : 0;
                if(elapsed<stop) return Sample(leg==2 ? ShopVisitPhase.Haggling : ShopVisitPhase.Browsing,leg,lengths[leg]);
                elapsed-=stop;
            }
            return Sample(ShopVisitPhase.Complete,3,lengths[3]);
        }
        private ShopVisitSnapshot Sample(ShopVisitPhase phase,int leg,double along)
        {
            var route=routes[leg];
            for(int i=1;i<route.Count;i++)
            {
                double length=Distance(route[i-1],route[i]);
                if(length>0 && along<=length)
                {
                    double t=along/length; var a=route[i-1]; var b=route[i];
                    return new ShopVisitSnapshot { Phase=phase,Position=new GuardPoint3 { X=a.X+(b.X-a.X)*t,Y=a.Y+(b.Y-a.Y)*t,Z=a.Z+(b.Z-a.Z)*t } };
                }
                along-=length;
            }
            var last=route[route.Count-1];
            return new ShopVisitSnapshot { Phase=phase,Position=new GuardPoint3 { X=last.X,Y=last.Y,Z=last.Z } };
        }
        private static double Distance(GuardPoint3 a,GuardPoint3 b) { double x=a.X-b.X,y=a.Y-b.Y,z=a.Z-b.Z; return Math.Sqrt(x*x+y*y+z*z); }
        private static bool Finite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }
    }
}
