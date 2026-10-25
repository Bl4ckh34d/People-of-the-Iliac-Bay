using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
namespace PeopleOfTheIliacBay.Tests
{
    internal static class AdultDailyItineraryTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Bounded daily search reserves attempts for shorter valid itineraries", () =>
            {
                var request = Request(13);
                request.DepartureAt = 9 * 3600;
                request.LatestHomeAt = 11 * 3600 + 15 * 60;
                request.MinimumAwaySeconds = 1;
                request.MinimumStops = 1;
                request.MaximumStops = 5;
                request.Options.Add(Option(60, ResidentVisitPurpose.Shop));
                foreach (var option in request.Options)
                    option.MinimumDwellSeconds = option.MaximumDwellSeconds = 45 * 60;
                foreach (int key in new[] { 0, 10, 20, 30, 40, 50 })
                {
                    request.Routes.Add(new AdultDailyItineraryLeg { FromBuildingKey = key, ToBuildingKey = 60,
                        WalkSeconds = 300, Route = Path(key, 60) });
                    request.Routes.Add(new AdultDailyItineraryLeg { FromBuildingKey = 60, ToBuildingKey = key,
                        WalkSeconds = 300, Route = Path(60, key) });
                }
                int budget = 512;
                Check.True(AdultDailyItineraryPlanner.TryCreate(request, ref budget, out var destination));
                Check.Equal(2, destination.Stops.Count);
                Check.True(budget >= 0 && budget < 512);
                foreach (var stop in destination.Stops) Check.True(stop.Purpose != ResidentVisitPurpose.Social);
            }));
            cases.Add(("Initial local itinerary keeps morning errands without social Inn visits or invented opening waits", () =>
            {
                for (uint seed = 1; seed <= 32; seed++)
                {
                    var request = Request(seed);
                    request.DepartureAt = 9 * 3600;
                    request.LatestHomeAt = 12 * 3600;
                    request.MinimumAwaySeconds = 1;
                    request.MinimumStops = 1;
                    request.MaximumStops = 2;
                    request.Options = new List<AdultDailyItineraryOption> {
                        Option(10, ResidentVisitPurpose.Shop), Option(20, ResidentVisitPurpose.Social) };
                    foreach (var option in request.Options)
                    {
                        option.OpenMinute = 0; option.CloseMinute = 24 * 60 - 1;
                        option.MinimumDwellSeconds = option.MaximumDwellSeconds = 15 * 60;
                    }
                    Check.True(AdultDailyItineraryPlanner.TryCreate(request, out var destination));
                    Check.Equal(1, destination.Stops.Count);
                    Check.Equal(ResidentVisitPurpose.Shop, destination.Stops[0].Purpose);
                    foreach (var visit in ResidentVisitJourney.Create(request.DepartureAt, destination).ScheduledVisits())
                        Check.Near(request.DepartureAt + 300, visit.EnterAt);
                }
            }));
            cases.Add(("Initial local itinerary permits an evening Inn but never waits out the morning there", () =>
            {
                var request = Request(3);
                request.MinimumAwaySeconds = 1;
                request.MinimumStops = request.MaximumStops = 1;
                request.Options = new List<AdultDailyItineraryOption> { Option(20, ResidentVisitPurpose.Social) };
                request.Options[0].OpenMinute = 0;
                request.Options[0].CloseMinute = 24 * 60 - 1;
                Check.True(!AdultDailyItineraryPlanner.TryCreate(request, out var destination));
                request.DepartureAt = 16 * 3600;
                Check.True(AdultDailyItineraryPlanner.TryCreate(request, out destination));
                Check.Equal(ResidentVisitPurpose.Social, destination.Stops[0].Purpose);
            }));
            cases.Add(("Initial local itinerary uses actual Inn arrival time after afternoon errands", () =>
            {
                var request = Request(7);
                request.DepartureAt = 15 * 3600 + 30 * 60;
                request.MinimumAwaySeconds = 1;
                request.MinimumStops = request.MaximumStops = 2;
                request.Options = new List<AdultDailyItineraryOption> {
                    Option(10, ResidentVisitPurpose.Shop), Option(20, ResidentVisitPurpose.Social) };
                foreach (var option in request.Options)
                {
                    option.OpenMinute = 0; option.CloseMinute = 24 * 60 - 1;
                    option.MinimumDwellSeconds = option.MaximumDwellSeconds = 45 * 60;
                }
                Check.True(AdultDailyItineraryPlanner.TryCreate(request, out var destination));
                Check.Equal(ResidentVisitPurpose.Shop, destination.Stops[0].Purpose);
                Check.Equal(ResidentVisitPurpose.Social, destination.Stops[1].Purpose);
                foreach (var visit in ResidentVisitJourney.Create(request.DepartureAt, destination).ScheduledVisits())
                    if (visit.BuildingKey == 20) Check.Near(16 * 3600 + 25 * 60, visit.EnterAt);
            }));
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
