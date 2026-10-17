using System;

namespace LivingCities.Core
{
    // Main-thread admission only. A delayed frame cannot release a burst of work
    // that was nominally due at different times before the stall.
    public sealed class WorkStartGate
    {
        private readonly double interval;
        private double next;
        public WorkStartGate(double interval)
        {
            if(double.IsNaN(interval) || double.IsInfinity(interval) || interval<=0)
                throw new ArgumentOutOfRangeException(nameof(interval));
            this.interval=interval;
        }
        public bool TryStart(double now)
        {
            if(double.IsNaN(now) || double.IsInfinity(now) || now<0)
                throw new ArgumentOutOfRangeException(nameof(now));
            if(now<next) return false;
            next=now+interval;
            return true;
        }
    }
}
