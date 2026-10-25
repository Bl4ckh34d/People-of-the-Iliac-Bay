using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public sealed class DailyServiceWindow
    {
        public int OpenMinute {get;private set;}
        public int CloseMinute {get;private set;}
        public DailyServiceWindow(int openMinute,int closeMinute)
        {
            if(openMinute<0 || closeMinute>=1440 || openMinute>=closeMinute)
                throw new ArgumentException("Daily service window must fit inside one day.");
            OpenMinute=openMinute;CloseMinute=closeMinute;
        }
    }

    // Native DFU tables use 25 as the all-day closing sentinel. The daily
    // resident never crosses midnight; exact sleep/return constraints apply later.
    public static class DailyServiceHours
    {
        public static bool TryFromNativeHours(int opens,int closes,out DailyServiceWindow window)
        {
            window=null;
            if(opens<0 || opens>=24 || closes<0 || closes>25)return false;
            int end=Math.Min(1439,closes*60);
            if(opens*60>=end)return false;
            window=new DailyServiceWindow(opens*60,end);return true;
        }

        public static ResidentRoutine Refresh(ResidentRoutine source,Func<int,DailyServiceWindow> readWindow)
        {
            if(source==null || readWindow==null)throw new ArgumentNullException();
            bool changed=false;
            DailyServiceWindow legacy=source.DestinationKey<0?null:readWindow(source.DestinationKey);
            if(legacy!=null && (source.OpenMinute!=legacy.OpenMinute || source.CloseMinute!=legacy.CloseMinute))changed=true;
            var outings=ResidentRoutineDay.CopyOutings(source);
            var windows=new List<DailyServiceWindow>();
            foreach(var outing in outings)
                foreach(var stop in outing.Itinerary.Stops)
                {
                    var window=readWindow(stop.BuildingKey);windows.Add(window);
                    if(window!=null && (stop.OpenMinute!=window.OpenMinute || stop.CloseMinute!=window.CloseMinute))changed=true;
                }
            if(!changed)return source;
            var result=new ResidentRoutine {ResidentId=source.ResidentId,DestinationKey=source.DestinationKey,
                RouteGeometryVersion=source.RouteGeometryVersion,PersonalLaneVersion=source.PersonalLaneVersion,
                OpenMinute=legacy==null?source.OpenMinute:legacy.OpenMinute,
                CloseMinute=legacy==null?source.CloseMinute:legacy.CloseMinute,
                ItineraryDepartureSeconds=source.ItineraryDepartureSeconds,
                Itinerary=outings.Count==0?null:outings[0].Itinerary};
            for(int i=1;i<outings.Count;i++)result.AdditionalOutings.Add(outings[i]);
            foreach(var point in source.Route)result.Route.Add(new SavedRoutePoint{X=point.X,Z=point.Z});
            int index=0;
            foreach(var outing in outings)
                foreach(var stop in outing.Itinerary.Stops)
                {
                    var window=windows[index++];
                    if(window!=null){stop.OpenMinute=window.OpenMinute;stop.CloseMinute=window.CloseMinute;}
                }
            ResidentRoutineDay.CopyOutings(result);
            return result;
        }
    }
}
