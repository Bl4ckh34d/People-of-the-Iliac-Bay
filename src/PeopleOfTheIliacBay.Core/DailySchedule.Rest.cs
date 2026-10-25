using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class DailySchedule
    {
        // Only the latest ordinary night matters for topping up remaining injury.
        // Reuse the existing exact schedule transitions; no background replay.
        public double HomeSleepSeconds(double from,double until)
        {
            if(!Finite(from) || !Finite(until) || from<0 || until<=from)return 0;
            from=Math.Max(from,until-86400);
            var boundaries=new List<double>{from,until};
            double day=Math.Floor(from/86400)*86400;
            CollectHomeBoundaries(day,boundaries);CollectHomeBoundaries(day+86400,boundaries);
            boundaries.Sort();double seconds=0,previous=from;
            foreach(double end in boundaries)
            {
                if(end<=previous || end>until)continue;
                var state=EvaluateSeconds(previous+(end-previous)/2);
                if(state.Activity==Activity.Sleep && state.BuildingKey==home)seconds+=end-previous;
                previous=end;
            }
            return seconds;
        }
    }
}
