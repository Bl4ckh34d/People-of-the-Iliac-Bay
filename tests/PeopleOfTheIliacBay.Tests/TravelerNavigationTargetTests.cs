using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class TravelerNavigationTargetTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Traveler navigation keeps each travel phase's real endpoint", () =>
            {
                var source = new[] { new RoutePoint(2, 3), new RoutePoint(12, 3), new RoutePoint(12, 23) };
                var journey = TravelerJourney.Create(17, 12 * 3600, 1, 600, false, source);
                source[0] = new RoutePoint(200, 300);
                source[2] = new RoutePoint(400, 500);

                RoutePoint target;
                Check.True(journey.TryTravelEndpoint(journey.Evaluate(journey.EntryAt).Phase, out target));
                Check.Equal(12d, target.X);
                Check.Equal(23d, target.Z);
                Check.True(journey.TryTravelEndpoint(journey.Evaluate(journey.EntryAt + 300).Phase, out target));
                Check.Equal(12d, target.X);
                Check.Equal(23d, target.Z);

                Check.True(journey.TryTravelEndpoint(journey.Evaluate(journey.LeaveInnAt).Phase, out target));
                Check.Equal(2d, target.X);
                Check.Equal(3d, target.Z);
                Check.True(journey.TryTravelEndpoint(journey.Evaluate(journey.LeaveInnAt + 300).Phase, out target));
                Check.Equal(2d, target.X);
                Check.Equal(3d, target.Z);
            }));

            cases.Add(("Traveler navigation has no travel target during lodging or gate waits", () =>
            {
                var journey = TravelerJourney.Create(17, 6 * 3600, 1, 10 * 3600, true,
                    new[] { new RoutePoint(2, 3), new RoutePoint(12, 23) });
                RoutePoint target;
                Check.True(!journey.TryTravelEndpoint(TravelerPhase.NotArrived, out target));
                Check.True(!journey.TryTravelEndpoint(TravelerPhase.AtInn, out target));
                Check.True(!journey.TryTravelEndpoint(TravelerPhase.Sleeping, out target));
                Check.True(!journey.TryTravelEndpoint(journey.Evaluate(journey.CheckOutAt).Phase, out target));
                Check.Equal(TravelerPhase.WaitingForGate, journey.Evaluate(journey.CheckOutAt).Phase);
                Check.True(!journey.TryTravelEndpoint(TravelerPhase.Departed, out target));
            }));
        }
    }
}
