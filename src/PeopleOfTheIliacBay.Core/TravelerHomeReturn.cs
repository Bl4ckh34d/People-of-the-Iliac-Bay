using System;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        private static double TravelerHomeAt(TravelerPerson person,TravelerVisit visit)
        {
            var journey=TravelerReservedJourney(person,visit);
            return Math.Max(journey.ExitAt,visit.ReturnDepartureNotBefore)+visit.NativeReturnSeconds;
        }
        private static double TravelerVisitConflictUntil(TravelerPerson person,TravelerVisit visit)
        {return visit.NativeReturnSeconds>0?TravelerHomeAt(person,visit):Journey(person,visit).ExitAt;}
        public void ObserveTravelerDepartureDelay(string visitId,double now)
        {
            Require(DefenseTime(now),"Invalid observed traveler departure time.");
            var visit=state.TravelerVisits.Find(value=>value.Id==visitId);
            if(visit==null || visit.NativeReturnSeconds<=0 || now<=visit.ReturnDepartureNotBefore ||
                now<Journey(Traveler(visit.PersonId),visit).ExitAt)return;
            // A body still physically inside town cannot already be on its
            // regional return. Keep the native duration after this lower bound.
            visit.ReturnDepartureNotBefore=now;
        }
    }
}
