using System;
namespace LivingCities.Core
{
    // Observed activity progress: time alone cannot complete a walking leg.
    public sealed class ShopPhysicalProgress
    {
        private readonly ShopVisitJourney journey;
        private bool arrived;
        private double dwellUntil,lastTime=-1;
        public int Leg { get; private set; }
        public ShopVisitPhase Phase { get; private set; }
        public double RemainingDwell(double now)
        { return arrived ? Math.Max(0,dwellUntil-now) : journey.DwellSeconds(Leg); }
        public ShopPhysicalProgress(ShopVisitJourney journey)
        {
            if(journey==null) throw new ArgumentNullException(nameof(journey));
            this.journey=journey; Phase=journey.LegCount==1 ? ShopVisitPhase.Leaving : ShopVisitPhase.Walking;
        }
        public void Advance(double now,bool physicallyArrived)
        {
            if(double.IsNaN(now) || double.IsInfinity(now) || now<0 || now<lastTime)
                throw new ArgumentOutOfRangeException(nameof(now));
            double elapsed=lastTime<0 ? 0 : now-lastTime;
            lastTime=now;
            if(Phase==ShopVisitPhase.Complete) return;
            if(arrived && !physicallyArrived)
            {
                dwellUntil+=elapsed;
                Phase=Leg==journey.LegCount-1 ? ShopVisitPhase.Leaving : ShopVisitPhase.Walking;
                return;
            }
            if(arrived) Phase=journey.StopPhase(Leg);
            if(!arrived)
            {
                if(!physicallyArrived) return;
                arrived=true; dwellUntil=now+journey.DwellSeconds(Leg);
                Phase=journey.StopPhase(Leg);
            }
            if(Phase==ShopVisitPhase.Complete || now<dwellUntil) return;
            Leg++;arrived=false;
            Phase=Leg==journey.LegCount-1 ? ShopVisitPhase.Leaving : ShopVisitPhase.Walking;
        }
    }
}
