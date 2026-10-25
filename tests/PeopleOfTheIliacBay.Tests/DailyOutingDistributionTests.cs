using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
namespace PeopleOfTheIliacBay.Tests
{
    internal static class DailyOutingDistributionTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Redistributing a ten-o-clock shop never creates an early queue and spreads arrivals over the morning",()=>
            {
                double earliest=double.MaxValue,latest=0;
                for(uint seed=1;seed<=500;seed++)
                {
                    var source=Routine();source.Itinerary.Stops[0].OpenMinute=600;
                    var result=DailyOutingDistribution.Spread(source,seed,6,22);
                    double arrival=result.ItineraryDepartureSeconds+result.Itinerary.Stops[0].WalkSeconds;
                    Check.True(arrival>=36000,"Departure jitter must respect actual arrival, not the clamped entry time.");
                    earliest=Math.Min(earliest,arrival);latest=Math.Max(latest,arrival);
                }
                Check.True(latest-earliest>=90*60);
            }));
            cases.Add(("Native service windows retain late shops, all-day taverns and early closures", () =>
            {
                DailyServiceWindow window;
                Check.True(DailyServiceHours.TryFromNativeHours(7,22,out window));
                Check.Equal(420,window.OpenMinute);Check.Equal(1320,window.CloseMinute);
                Check.True(DailyServiceHours.TryFromNativeHours(0,25,out window));
                Check.Equal(0,window.OpenMinute);Check.Equal(1439,window.CloseMinute);
                Check.True(DailyServiceHours.TryFromNativeHours(8,16,out window));
                Check.Equal(480,window.OpenMinute);Check.Equal(960,window.CloseMinute);
                Check.True(!DailyServiceHours.TryFromNativeHours(0,0,out window));
                Check.True(!DailyServiceHours.TryFromNativeHours(24,25,out window));
                Check.True(!DailyServiceHours.TryFromNativeHours(-1,20,out window));
                Check.True(!DailyServiceHours.TryFromNativeHours(7,26,out window));
            }));
            cases.Add(("Refreshing saved service hours preserves paths, dwell and source isolation", () =>
            {
                var source=Routine();
                var result=DailyServiceHours.Refresh(source,key=>key==2?new DailyServiceWindow(0,1439):null);
                Check.Equal(0,result.Itinerary.Stops[0].OpenMinute);
                Check.Equal(1439,result.Itinerary.Stops[0].CloseMinute);
                Check.Equal(540,source.Itinerary.Stops[0].OpenMinute);
                Check.Equal(source.ItineraryDepartureSeconds,result.ItineraryDepartureSeconds);
                Check.Equal(source.Itinerary.Stops[0].DwellSeconds,result.Itinerary.Stops[0].DwellSeconds);
                result.Itinerary.Stops[0].Route[0].X=999;
                Check.Equal(0d,source.Itinerary.Stops[0].Route[0].X);
                Check.True(ReferenceEquals(source,DailyServiceHours.Refresh(source,key=>null)));
                var same=DailyServiceHours.Refresh(source,key=>new DailyServiceWindow(540,1080));
                Check.True(ReferenceEquals(source,same));
                Check.Throws<ArgumentException>(()=>DailyServiceHours.Refresh(source,key=>new DailyServiceWindow(540,550)));
                Check.Equal(1080,source.Itinerary.Stops[0].CloseMinute);
            }));
            cases.Add(("Explicit town return limit still protects haunting and leaves service duration unchanged", () =>
            {
                for(uint seed=1;seed<=100;seed++)
                {
                    var source=Routine();source.Itinerary.Stops[0].OpenMinute=0;source.Itinerary.Stops[0].CloseMinute=1439;
                    var result=DailyOutingDistribution.Spread(source,seed,6,23,18*3600+45*60);
                    Check.True(ResidentVisitJourney.Create(result.ItineraryDepartureSeconds,result.Itinerary).EndsAt<=18*3600+45*60);
                    Check.Equal(3600d,result.Itinerary.Stops[0].DwellSeconds);
                }
            }));
            cases.Add(("Redistribution preserves valid evening social visits despite all-day Inn opening hours", () =>
            {
                for(uint seed=1;seed<=300;seed++)
                {
                    var source=Routine();var stop=source.Itinerary.Stops[0];
                    source.ItineraryDepartureSeconds=20*3600;
                    stop.Purpose=ResidentVisitPurpose.Social;stop.OpenMinute=0;stop.CloseMinute=1439;
                    var result=DailyOutingDistribution.Spread(source,seed,6,23);
                    foreach(var visit in ResidentVisitJourney.Create(result.ItineraryDepartureSeconds,result.Itinerary).ScheduledVisits())
                        Check.True(visit.EnterAt>=16*3600);
                    Check.True(ResidentVisitJourney.Create(result.ItineraryDepartureSeconds,result.Itinerary).EndsAt<23*3600);
                    Check.Equal(20*3600d,source.ItineraryDepartureSeconds);
                }
            }));
            cases.Add(("First daily outings use varied morning starts without changing dwell or opening windows", () =>
            {
                var departures=new HashSet<double>();
                for (uint seed = 1; seed <= 300; seed++)
                {
                    var source = Routine();
                    var result = DailyOutingDistribution.Spread(source, seed, 6, 22);
                    var journey = ResidentVisitJourney.Create(result.ItineraryDepartureSeconds, result.Itinerary);
                    Check.True(result.ItineraryDepartureSeconds>=8*3600 && result.ItineraryDepartureSeconds<=12*3600);
                    departures.Add(result.ItineraryDepartureSeconds);
                    Check.True(journey.EndsAt <= 18 * 3600 + 45 * 60);
                    foreach (var visit in journey.ScheduledVisits())
                    { Check.True(visit.EnterAt >= 9 * 3600); Check.True(visit.LeaveAt <= 18 * 3600); }
                    Check.Equal(3600d, result.Itinerary.Stops[0].DwellSeconds);
                    Check.Equal(8 * 3600d, source.ItineraryDepartureSeconds);
                    Check.Equal(result.ItineraryDepartureSeconds,
                        DailyOutingDistribution.Spread(result, seed, 6, 22).ItineraryDepartureSeconds);
                }
                Check.True(departures.Count>250);
            }));
            cases.Add(("Daily distribution preserves unavailable and native overnight routines", () =>
            {
                var empty = new ResidentRoutine();
                Check.True(ReferenceEquals(empty, DailyOutingDistribution.Spread(empty, 1, 6, 22)));
                var source = Routine(); source.Itinerary.Walled = true;
                Check.True(ReferenceEquals(source, DailyOutingDistribution.Spread(source, 1, 6, 22)));
            }));
            cases.Add(("Redistributed outings cannot publish while either schedule is outside", () =>
            {
                var source=Routine();
                var candidate=DailyOutingDistribution.Spread(source,123,6,22);
                var before=DailySchedule.ItineraryDay(1,source.Itinerary,source.ItineraryDepartureSeconds,360,1320);
                var after=DailySchedule.ItineraryDay(1,candidate.Itinerary,candidate.ItineraryDepartureSeconds,360,1320);
                Check.True(!OutdoorRoutineMigration.CanPublishAtHome(before.EvaluateSeconds(8*3600+100),after.EvaluateSeconds(8*3600+100),1));
                Check.True(OutdoorRoutineMigration.CanPublishAtHome(before.EvaluateSeconds(21*3600),after.EvaluateSeconds(21*3600),1));
            }));
            cases.Add(("Daily distribution respects early closing and sleeping", () =>
            {
                var source=Routine(); source.Itinerary.Stops[0].CloseMinute=660;
                for(uint seed=1;seed<=50;seed++)
                {
                    var result=DailyOutingDistribution.Spread(source,seed,6,12);
                    var journey=ResidentVisitJourney.Create(result.ItineraryDepartureSeconds,result.Itinerary);
                    Check.True(journey.EndsAt<12*3600);
                    foreach(var visit in journey.ScheduledVisits()) Check.True(visit.LeaveAt<=11*3600);
                }
            }));
        }
        private static ResidentRoutine Routine()
        {
            var points = new List<SavedRoutePoint> { new SavedRoutePoint { X=0,Z=0 }, new SavedRoutePoint { X=10,Z=0 } };
            var back = new List<SavedRoutePoint> { new SavedRoutePoint { X=10,Z=0 }, new SavedRoutePoint { X=0,Z=0 } };
            return new ResidentRoutine { ResidentId="test", DestinationKey=2,OpenMinute=540,CloseMinute=1080, Route=points, ItineraryDepartureSeconds=8*3600,
                Itinerary=new ResidentDestination { Stops=new List<ResidentVisitStop> {
                    new ResidentVisitStop { BuildingKey=2, OpenMinute=540, CloseMinute=1080,
                        WalkSeconds=300,DwellSeconds=3600,Route=points } }, ExitRoute=back,ExitWalkSeconds=300 } };
        }
    }
}
