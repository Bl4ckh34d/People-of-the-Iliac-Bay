using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class ResidentTripNativeDestinationTests
    {
        private static List<SavedRoutePoint> Path(double start, double end)
        {
            return new List<SavedRoutePoint> {
                new SavedRoutePoint { X = start }, new SavedRoutePoint { X = end }
            };
        }

        private static ResidentDestination Visit(double dwell)
        {
            return new ResidentDestination {
                Stops = new List<ResidentVisitStop> {
                    new ResidentVisitStop { BuildingKey = 7, Purpose = ResidentVisitPurpose.Inn,
                        WalkSeconds = 2, DwellSeconds = dwell, Route = Path(1000, 1002) }
                },
                ExitRoute = Path(1002, 1000), ExitWalkSeconds = 2
            };
        }

        private static WorldStateStore VisitingStore(out Resident resident)
        {
            var store = new WorldStateStore(12);
            resident = store.GetOrCreate(100, new[] { 1 }, 1).Households[0].Residents[0];
            foreach (int town in new[] { 200, 300, 400 }) store.GetOrCreate(town, new[] { 7 }, 1);
            var trip = ResidentTrip.Create(resident.Id, 100, 200, 100, 40, 10,
                new[] { new RoutePoint(0, 0), new RoutePoint(1, 0), new RoutePoint(2, 0),
                    new RoutePoint(3, 0), new RoutePoint(4, 0) });
            trip.HomeRoute = Path(0, 1); trip.HomeWalkSeconds = 10;
            trip.Destination = Visit(40);
            trip.OutboundStops.Add(new ResidentRoadStop { RoadNode = 1, SettlementId = 300, Visit = Visit(20) });
            trip.ReturnStops.Add(new ResidentRoadStop { RoadNode = 2, SettlementId = 400, Visit = Visit(30) });
            store.ScheduleResidentTrip(trip);
            return store;
        }

        private static void CheckDestination(WorldStateStore store, Resident resident, int town,
            double now, double expected)
        {
            ActivitySnapshot state;
            Check.True(store.TryDestinationResidentState(resident.Id, town, now, out state));
            Check.Equal(Activity.Travel, state.Activity);
            Check.True(state.TravelDestination.HasValue, "Walking visitor lost native destination.");
            Check.Near(expected, state.TravelDestination.Value.X);
        }

        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Destination and road-stop visitors retain stable endpoints across walking and leaving", () => {
                Resident resident;
                var store = VisitingStore(out resident);
                CheckDestination(store, resident, 300, 121, 1002);
                CheckDestination(store, resident, 300, 143, 1000);
                CheckDestination(store, resident, 200, 175, 1002);
                CheckDestination(store, resident, 200, 217, 1000);
                bool returnStopArrival = false, returnStopDeparture = false;
                for (double now = 200; now < 400; now += 0.5)
                {
                    var visit = store.ResidentTripAt(resident.Id, now);
                    if (visit.SettlementId != 400 || visit.Phase != ResidentTripPhase.Visiting) continue;
                    if (visit.VisitPhase == ResidentVisitPhase.Walking && !returnStopArrival)
                    {
                        CheckDestination(store, resident, 400, now, 1002);
                        returnStopArrival = true;
                    }
                    if (visit.VisitPhase == ResidentVisitPhase.Leaving && !returnStopDeparture)
                    {
                        CheckDestination(store, resident, 400, now, 1000);
                        returnStopDeparture = true;
                    }
                }
                Check.True(returnStopArrival && returnStopDeparture,
                    "Return road stop never exercised walking and leaving.");
                ActivitySnapshot inside;
                Check.True(store.TryDestinationResidentState(resident.Id, 300, 130, out inside));
                Check.Equal(Activity.Visit, inside.Activity);
                Check.True(!inside.TravelDestination.HasValue);
                store.Restore(store.Snapshot());
                CheckDestination(store, resident, 300, 121, 1002);
            }));

            cases.Add(("Home-city trip walks target gate or home door while gate waits stay at gate", () => {
                var store = new WorldStateStore(12);
                var resident = store.GetOrCreate(100, new[] { 1 }, 1).Households[0].Residents[0];
                var trip = ResidentTrip.Create(resident.Id, 100, 200, 17 * 3600 + 55 * 60,
                    600, 10, new[] { new RoutePoint(0, 0), new RoutePoint(1, 0) });
                trip.HomeRoute = Path(0, 10); trip.HomeWalkSeconds = 120; trip.HomeWalled = true;
                store.ScheduleResidentTrip(trip);
                ActivitySnapshot outbound = store.LocalResidentState(resident.Id, trip.HomeLeavesAt + 60,
                    default(ActivitySnapshot));
                Check.Equal(Activity.Travel, outbound.Activity);
                Check.True(outbound.TravelDestination.HasValue);
                Check.Near(10, outbound.TravelDestination.Value.X);
                ActivitySnapshot waiting = store.LocalResidentState(resident.Id, trip.RoadReturnsAt + 10,
                    default(ActivitySnapshot));
                Check.Equal(ResidentTripPhase.WaitingHomeGate, store.ResidentTripAt(resident.Id, trip.RoadReturnsAt + 10).Phase);
                Check.Near(10, waiting.Position.X);
                Check.True(waiting.TravelDestination.HasValue);
                Check.Near(10, waiting.TravelDestination.Value.X);
                ActivitySnapshot inbound = store.LocalResidentState(resident.Id, trip.HomeEntryAt + 60,
                    default(ActivitySnapshot));
                Check.Equal(Activity.Travel, inbound.Activity);
                Check.True(inbound.TravelDestination.HasValue);
                Check.Near(0, inbound.TravelDestination.Value.X);
                store.Restore(store.Snapshot());
                inbound = store.LocalResidentState(resident.Id, trip.HomeEntryAt + 60, default(ActivitySnapshot));
                Check.True(inbound.TravelDestination.HasValue);
                Check.Near(0, inbound.TravelDestination.Value.X);
            }));
        }
    }
}
