using System;

namespace PeopleOfTheIliacBay.Core
{
    [Serializable]
    public sealed class CorpseDepartureState
    {
        public const double AbsenceSeconds=1800d;
        public bool Away,Completed;
        public double FirstDepartureAt=-1,AwaySince=-1,LastObservedAt,CompletedAt=-1;

        public static CorpseDepartureState CopyValidated(CorpseDepartureState value,double diedAt)
        {
            if(value==null)return null;
            if(!Finite(diedAt) || !Finite(value.LastObservedAt) || value.LastObservedAt<diedAt ||
                (value.FirstDepartureAt!=-1 && (!Finite(value.FirstDepartureAt) || value.FirstDepartureAt<diedAt || value.FirstDepartureAt>value.LastObservedAt)) ||
                (value.Away ? !Finite(value.AwaySince) || value.AwaySince<diedAt || value.AwaySince>value.LastObservedAt :
                    value.AwaySince!=-1) ||
                value.Away && (value.FirstDepartureAt<0 || value.AwaySince<value.FirstDepartureAt) ||
                (value.Completed ? !value.Away || !Finite(value.CompletedAt) ||
                    value.CompletedAt!=value.AwaySince+AbsenceSeconds || value.CompletedAt>value.LastObservedAt :
                    value.CompletedAt!=-1))throw new ArgumentException("Invalid corpse departure chronology.");
            return new CorpseDepartureState {FirstDepartureAt=value.FirstDepartureAt,Away=value.Away,AwaySince=value.AwaySince,
                LastObservedAt=value.LastObservedAt,Completed=value.Completed,CompletedAt=value.CompletedAt};
        }

        public static CorpseDepartureState Observe(CorpseDepartureState current,double diedAt,bool playerOnSite,double now)
        {
            if(!Finite(now) || !Finite(diedAt) || now<diedAt)throw new ArgumentException("Invalid corpse presence observation.");
            var next=CopyValidated(current,diedAt) ?? new CorpseDepartureState {LastObservedAt=now};
            if(next.Completed)return next;
            if(now<next.LastObservedAt)throw new ArgumentException("Corpse presence observation cannot rewind time.");
            next.LastObservedAt=now;
            // Catch up before registering the player's return, including after an unloaded town or reload.
            if(next.Away && now>=next.AwaySince+AbsenceSeconds)
            {next.Completed=true;next.CompletedAt=next.AwaySince+AbsenceSeconds;return next;}
            if(playerOnSite){next.Away=false;next.AwaySince=-1;}
            else if(!next.Away){next.Away=true;next.AwaySince=now;if(next.FirstDepartureAt<0)next.FirstDepartureAt=now;}
            return next;
        }
        private static bool Finite(double value){return !double.IsNaN(value)&&!double.IsInfinity(value)&&value>=0;}
    }
}
