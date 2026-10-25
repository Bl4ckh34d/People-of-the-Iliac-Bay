using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class DailySchedule
    {
        public int HomeBuildingKey {get{return home;}}
        // Half-open interval: a parent's return may coincide exactly with the
        // other parent's departure. Evaluate every state transition, not a clock
        // sampling interval that could miss a short appointment or commute.
        public bool IsHomeThroughout(double startsAt,double endsAt)
        {
            if(!Finite(startsAt) || !Finite(endsAt) || startsAt<0 || endsAt<=startsAt || endsAt-startsAt>86400)
                throw new ArgumentOutOfRangeException(nameof(endsAt));
            var boundaries=new List<double>{startsAt,endsAt};
            double firstDay=Math.Floor(startsAt/86400)*86400;
            for(int day=0;day<=1;day++)CollectHomeBoundaries(firstDay+day*86400,boundaries);
            boundaries.Sort();
            double previous=startsAt;
            foreach(double boundary in boundaries)
            {
                if(boundary<startsAt || boundary>endsAt)continue;
                if(boundary>previous && !AtHome(EvaluateSeconds(previous+(boundary-previous)/2)))return false;
                if(boundary<endsAt && !AtHome(EvaluateSeconds(boundary)))return false;
                previous=boundary;
            }
            return true;
        }
        private bool AtHome(ActivitySnapshot snapshot)
        {return snapshot.BuildingKey==home && snapshot.Activity!=Activity.Travel;}
        private void CollectHomeBoundaries(double day,List<double> boundaries)
        {
            boundaries.Add(day);boundaries.Add(day+86400);boundaries.Add(day+wake*60d);boundaries.Add(day+sleep*60d);
            if(dateSource!=null)
            {
                boundaries.Add(selectedDay*86400d);boundaries.Add((selectedDay+1d)*86400d);
                dateSource.CollectHomeBoundaries(day,boundaries);dateSchedule.CollectHomeBoundaries(day,boundaries);return;
            }
            if(recoveryPlan!=null)
            {
                recoverySource.CollectHomeBoundaries(day,boundaries);
                boundaries.Add(recoveryPlan.CreatedAt);boundaries.Add((recoveryPlan.AbsoluteDay+1d)*86400d);
                foreach(var leg in recoveryPlan.Legs)
                {boundaries.Add(leg.WalkAt);boundaries.Add(leg.ArriveAt);boundaries.Add(leg.EnterAt);boundaries.Add(leg.LeaveAt);}
                return;
            }
            if(composedOutings!=null)
            {foreach(var outing in composedOutings)outing.CollectHomeBoundaries(day,boundaries);return;}
            if(itineraryJourney!=null)
            {
                boundaries.Add(day+itineraryDepartureSeconds);boundaries.Add(day+itineraryEndsAt);
                foreach(var visit in itineraryVisits)
                {boundaries.Add(day+visit.ArriveAt);boundaries.Add(day+visit.EnterAt);boundaries.Add(day+visit.LeaveAt);}
                return;
            }
            if(pointOuting!=null)
            {
                boundaries.Add(day+pointOuting.Departure);boundaries.Add(day+pointOuting.Arrival);
                boundaries.Add(day+pointOuting.Leave);boundaries.Add(day+pointOuting.Return);return;
            }
            if(work<0)return;
            boundaries.Add(day+(open-commute)*60d);boundaries.Add(day+open*60d);
            boundaries.Add(day+close*60d);boundaries.Add(day+(close+commute)*60d);
        }
    }
}
