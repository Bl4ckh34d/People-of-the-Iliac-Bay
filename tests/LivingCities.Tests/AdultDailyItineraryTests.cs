using System;
using System.Collections.Generic;
using LivingCities.Core;
namespace LivingCities.Tests
{
    internal static class AdultDailyItineraryTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Adult itinerary deterministically plans multiple open-hour stops and returns home before sleep", () =>
            {
                var request = Request(12345);
                ResidentDestination destination;
                Check.True(AdultDailyItineraryPlanner.TryCreate(request, out destination));
                var repeated = AdultDailyItineraryPlanner.TryCreate(Request(12345), out var same);
                Check.True(repeated);
                Check.Equal(Signature(destination), Signature(same));
                Check.True(destination.Stops.Count >= 3);
                var journey = ResidentVisitJourney.Create(request.DepartureAt, destination);
                Check.True(journey.EndsAt >= request.DepartureAt + request.MinimumAwaySeconds);
                Check.True(journey.EndsAt <= request.LatestHomeAt);
                double activeSeconds = destination.ExitWalkSeconds;
                foreach (var stop in destination.Stops) activeSeconds += stop.WalkSeconds + stop.DwellSeconds;
                Check.True(activeSeconds >= request.MinimumAwaySeconds);
                foreach (var visit in journey.ScheduledVisits())
                {
                    Check.True(visit.EnterAt % 86400 >= 9 * 3600);
                    Check.True(visit.LeaveAt % 86400 <= 18 * 3600);
                }
                var purposes = new HashSet<ResidentVisitPurpose>();
                foreach (var stop in destination.Stops) purposes.Add(stop.Purpose);
                Check.True(purposes.Count >= 3);
                Check.Near(0, destination.ExitRoute[destination.ExitRoute.Count - 1].X);
                Check.Near(0, destination.ExitRoute[destination.ExitRoute.Count - 1].Z);
            }));
            cases.Add(("Adult itinerary seed changes available stop order", () =>
            {
                ResidentDestination first, second;
                Check.True(AdultDailyItineraryPlanner.TryCreate(Request(1), out first));
                Check.True(AdultDailyItineraryPlanner.TryCreate(Request(2), out second));
                Check.True(Signature(first) != Signature(second), Signature(first) + " == " + Signature(second));
            }));
            cases.Add(("Adult itinerary refuses outing when route or daily window cannot fit", () =>
            {
                var noRoute = Request(3);
                noRoute.Routes.RemoveAll(route => route.FromBuildingKey == noRoute.HomeBuildingKey);
                ResidentDestination destination;
                Check.True(!AdultDailyItineraryPlanner.TryCreate(noRoute, out destination));
                Check.True(destination == null);
                var noTime = Request(4);
                noTime.LatestHomeAt = noTime.DepartureAt + 60;
                noTime.MinimumAwaySeconds = 30;
                Check.True(!AdultDailyItineraryPlanner.TryCreate(noTime, out destination));
                var longWait = Request(5);
                longWait.DepartureAt = 30 * 60;
                longWait.MinimumAwaySeconds = 10 * 3600;
                Check.True(!AdultDailyItineraryPlanner.TryCreate(longWait, out destination));
                var lodgingPurpose = Request(6);
                lodgingPurpose.Options[0].Purpose = ResidentVisitPurpose.Inn;
                Check.Throws<ArgumentException>(() => AdultDailyItineraryPlanner.TryCreate(lodgingPurpose, out destination));
            }));
        }
        private static string Signature(ResidentDestination destination)
        {
            var parts = new List<string>();
            foreach (var stop in destination.Stops) parts.Add(stop.BuildingKey + ":" + stop.DwellSeconds.ToString("R"));
            return string.Join(",", parts);
        }
        private static AdultDailyItineraryRequest Request(uint seed)
        {
            var routes = new List<AdultDailyItineraryLeg>();
            int[] keys = { 0, 10, 20, 30, 40, 50 };
            foreach (int from in keys) foreach (int to in keys)
            {
                if (from == to) continue;
                routes.Add(new AdultDailyItineraryLeg
                {
                    FromBuildingKey = from, ToBuildingKey = to, WalkSeconds = 300,
                    Route = Path(from, to)
                });
            }
            return new AdultDailyItineraryRequest
            {
                HomeBuildingKey = 0, DepartureAt = 8 * 3600, LatestHomeAt = 19 * 3600,
                MinimumAwaySeconds = 6 * 3600, MinimumStops = 3, MaximumStops = 4, Seed = seed,
                Options = new List<AdultDailyItineraryOption>
                {
                    Option(10, ResidentVisitPurpose.Shop), Option(20, ResidentVisitPurpose.Social),
                    Option(30, ResidentVisitPurpose.Temple), Option(40, ResidentVisitPurpose.Guild),
                    Option(50, ResidentVisitPurpose.Castle)
                },
                Routes = routes
            };
        }
        private static AdultDailyItineraryOption Option(int key, ResidentVisitPurpose purpose)
        {
            return new AdultDailyItineraryOption { BuildingKey = key, Purpose = purpose,
                OpenMinute = 9 * 60, CloseMinute = 18 * 60,
                MinimumDwellSeconds = 90 * 60, MaximumDwellSeconds = 2 * 60 * 60 };
        }
        private static List<SavedRoutePoint> Path(int from, int to)
        {
            return new List<SavedRoutePoint> { new SavedRoutePoint { X = from, Z = 0 }, new SavedRoutePoint { X = to, Z = 0 } };
        }
    }
}
