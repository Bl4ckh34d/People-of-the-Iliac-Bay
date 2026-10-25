using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using PeopleOfTheIliacBay.Core;

// CPU-only diagnostic. These are .NET timings, not Unity frame-time claims.
internal static class Program
{
    private static void Main()
    {
        Console.WriteLine("scenario,runs,p50_ms,p95_ms,max_ms,allocated_bytes_per_run,gen0_collections");
        foreach(int points in new[]{32,256,1024})
        {
            var request=Request(points,false);
            Measure("daily-plan-"+points+"-points",()=>{
                if(!AdultDailyItineraryPlanner.TryCreate(request,out _))throw new Exception("Expected valid outing.");
            });
            var impossible=Request(points,true);
            Measure("rejected-plan-"+points+"-points",()=>{
                if(AdultDailyItineraryPlanner.TryCreate(impossible,out _))throw new Exception("Expected impossible outing.");
            });
        }
    }
    private static void Measure(string name,Action action)
    {
        for(int i=0;i<5;i++)action();
        const int runs=30;
        var samples=new double[runs];long bytes=0;int collections=GC.CollectionCount(0);
        for(int i=0;i<runs;i++)
        {
            long allocated=GC.GetAllocatedBytesForCurrentThread();long before=Stopwatch.GetTimestamp();
            action();samples[i]=Stopwatch.GetElapsedTime(before).TotalMilliseconds;
            bytes+=GC.GetAllocatedBytesForCurrentThread()-allocated;
        }
        Array.Sort(samples);
        Console.WriteLine(string.Join(",",name,runs,samples[runs/2].ToString("F3",CultureInfo.InvariantCulture),
            samples[(int)Math.Ceiling(runs*.95)-1].ToString("F3",CultureInfo.InvariantCulture),
            samples[runs-1].ToString("F3",CultureInfo.InvariantCulture),bytes/runs,GC.CollectionCount(0)-collections));
    }
    private static AdultDailyItineraryRequest Request(int points,bool impossible)
    {
        var request=new AdultDailyItineraryRequest{HomeBuildingKey=0,DepartureAt=9*3600,
            LatestHomeAt=impossible?9*3600+60:19*3600,MinimumAwaySeconds=30,
            MinimumStops=3,MaximumStops=5,Seed=123};
        for(int key=1;key<=6;key++)request.Options.Add(new AdultDailyItineraryOption{BuildingKey=key,
            Purpose=ResidentVisitPurpose.Shop,OpenMinute=9*60,CloseMinute=18*60,
            MinimumDwellSeconds=20*60,MaximumDwellSeconds=60*60});
        for(int from=0;from<=6;from++)for(int to=0;to<=6;to++)
        {
            if(from==to)continue;
            var leg=new AdultDailyItineraryLeg{FromBuildingKey=from,ToBuildingKey=to,WalkSeconds=300};
            for(int point=0;point<points;point++)leg.Route.Add(new SavedRoutePoint{
                X=from*20+(to-from)*20d*point/(points-1),Z=0});
            request.Routes.Add(leg);
        }
        return request;
    }
}
