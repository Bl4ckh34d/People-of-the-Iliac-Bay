using System;
using System.Collections.Generic;
namespace LivingCities.Core
{
    public enum ShopVisitPhase { Walking, Browsing, Haggling, Leaving, Complete }
    public struct ShopVisitSnapshot { public ShopVisitPhase Phase; public GuardPoint3 Position; }
    public struct ShopOccupancy { public double Start,End; public GuardPoint3 Position; }
    public sealed class ShopVisitJourney
    {
        private readonly List<List<GuardPoint3>> routes=new List<List<GuardPoint3>>();
        private double[] lengths,stops;
        private int counterLeg;
        private double start,end,secondsPerMetre;
        public bool Purchases { get { return counterLeg>=0; } }
        public int LegCount { get { return routes.Count; } }
        public double PlannedEndAt { get { return end; } }
        public static ShopVisitJourney Resume(ObservedVisit checkpoint,double now)
        {
            var saved=ObservedVisit.Copy(checkpoint);
            var legs=saved.RemainingLegs(now);
            if(legs.Count==0) throw new ArgumentException("Completed visit has no remaining route.",nameof(checkpoint));
            var result=new ShopVisitJourney {start=now,end=saved.EndsAt,secondsPerMetre=saved.SecondsPerMetre,
                lengths=new double[legs.Count],stops=new double[legs.Count],counterLeg=-1};
            for(int i=0;i<legs.Count;i++)
            {
                result.routes.Add(legs[i].Route);result.stops[i]=legs[i].DwellSeconds;
                if(legs[i].AfterPhase==ShopVisitPhase.Haggling) result.counterLeg=i;
                for(int j=1;j<legs[i].Route.Count;j++) result.lengths[i]+=Distance(legs[i].Route[j-1],legs[i].Route[j]);
            }
            return result;
        }
        public double DwellSeconds(int leg) { return stops[leg]; }
        public ShopVisitPhase StopPhase(int leg)
        { return leg==routes.Count-1 ? ShopVisitPhase.Complete : leg==counterLeg ? ShopVisitPhase.Haggling : ShopVisitPhase.Browsing; }
        public List<GuardPoint3> LegRoute(int leg)
        {
            var copy=new List<GuardPoint3>();
            foreach(var point in routes[leg]) copy.Add(new GuardPoint3 {X=point.X,Y=point.Y,Z=point.Z});
            return copy;
        }
        public IEnumerable<ShopOccupancy> Occupancies()
        {
            double at=start;
            for(int leg=0;leg<routes.Count;leg++)
            {
                at+=lengths[leg]*secondsPerMetre;
                if(stops[leg]>0)
                {
                    var point=routes[leg][routes[leg].Count-1];
                    yield return new ShopOccupancy {Start=at,End=at+stops[leg],
                        Position=new GuardPoint3 {X=point.X,Y=point.Y,Z=point.Z}};
                }
                at+=stops[leg];
            }
        }
        public static ShopVisitJourney Create(double startsAt,double endsAt,IList<List<GuardPoint3>> paths,double speed,double timeScale,
            bool purchase=true,uint visitSeed=0,bool counterFirst=false)
        {
            if(!Finite(startsAt) || startsAt<0 || !Finite(endsAt) || endsAt<=startsAt ||
                !Finite(speed) || speed<=0 || !Finite(timeScale) || timeScale<=0 || paths==null || paths.Count<(purchase?3:2))
                throw new ArgumentException("Invalid shop visit.");
            var journey=new ShopVisitJourney { start=startsAt,end=endsAt,secondsPerMetre=timeScale/speed,
                lengths=new double[paths.Count],stops=new double[paths.Count],counterLeg=purchase?(counterFirst?0:paths.Count-2):-1 };
            double travel=0;
            for(int leg=0;leg<paths.Count;leg++)
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
            double pause=endsAt-startsAt-travel;
            if(!Finite(travel) || pause<60) throw new ArgumentException("Visit cannot fit travel and activities.");
            int browsing=paths.Count-(purchase?2:1);
            double counter=purchase?timeScale*(3+visitSeed%5):0;
            if(pause<counter+12*browsing) throw new ArgumentException("Visit cannot fit browsing and purchase.");
            double weight=0;
            for(int i=0;i<paths.Count-1;i++) if(i!=journey.counterLeg)
            { journey.stops[i]=1+(StableSeed.Combine(visitSeed,0,0,(uint)i)%5)*0.25; weight+=journey.stops[i]; }
            for(int i=0;i<paths.Count-1;i++) if(i!=journey.counterLeg) journey.stops[i]=(pause-counter)*journey.stops[i]/weight;
            if(purchase) journey.stops[journey.counterLeg]=counter;
            return journey;
        }
        public ShopVisitSnapshot Evaluate(double now)
        {
            if(!Finite(now) || now<0) throw new ArgumentOutOfRangeException(nameof(now));
            int last=routes.Count-1;
            if(now>=end) return Sample(ShopVisitPhase.Complete,last,lengths[last]);
            double elapsed=Math.Max(0,now-start);
            for(int leg=0;leg<routes.Count;leg++)
            {
                double travel=lengths[leg]*secondsPerMetre;
                if(elapsed<travel) return Sample(leg==last ? ShopVisitPhase.Leaving : ShopVisitPhase.Walking,leg,elapsed/secondsPerMetre);
                elapsed-=travel;
                double stop=stops[leg];
                if(elapsed<stop) return Sample(leg==counterLeg ? ShopVisitPhase.Haggling : ShopVisitPhase.Browsing,leg,lengths[leg]);
                elapsed-=stop;
            }
            return Sample(ShopVisitPhase.Complete,last,lengths[last]);
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
