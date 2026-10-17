using System;
using System.Collections.Generic;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class RoutineStoreTests
    {
        internal static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Dialogue routine reads one resident without exposing saved schedule", () => {
                var store = Filled();
                var resident = store.GetOrCreate(100, new[] { 10 }).Households[0].Residents[0];
                store.SetRoutines(Visit(100, resident.Id, 30));
                var dialogue = store.GetResidentDialogueRoutine(100, resident.Id);
                Check.Equal(30, dialogue.DestinationKey);
                dialogue.DestinationKey = 99;
                Check.Equal(30, store.GetResidentDialogueRoutine(100, resident.Id).DestinationKey);
                Check.True(store.GetResidentDialogueRoutine(100, "unknown") == null);
            }));
            cases.Add(("Dialogue routine retains visit timing and routes for the selected resident", () => {
                var store = Filled();
                var resident = store.GetOrCreate(100, new[] { 10 }).Households[0].Residents[0];
                var itinerary = new ResidentDestination { Stops = new List<ResidentVisitStop> {
                    new ResidentVisitStop { BuildingKey = 30, Purpose = ResidentVisitPurpose.Shop,
                        OpenMinute = 540, CloseMinute = 1080, WalkSeconds = 300, DwellSeconds = 1200,
                        Route = new List<SavedRoutePoint> { new SavedRoutePoint(), new SavedRoutePoint { X = 5 } } } },
                    ExitWalkSeconds = 300, ExitRoute = new List<SavedRoutePoint> {
                        new SavedRoutePoint { X = 5 }, new SavedRoutePoint() } };
                store.SetRoutines(new TownRoutineState { SettlementId = 100, ItineraryVersion = 1,
                    Residents = new List<ResidentRoutine> { new ResidentRoutine { ResidentId = resident.Id,
                        ItineraryDepartureSeconds = 8 * 3600, Itinerary = itinerary } } });
                var dialogue = store.GetResidentDialogueRoutine(100, resident.Id);
                Check.Equal(1, new List<ResidentVisitSnapshot>(ResidentVisitJourney.Create(
                    dialogue.ItineraryDepartureSeconds, dialogue.Itinerary).ScheduledVisits()).Count);
                dialogue.Itinerary.Stops[0].Route[1].X = 999;
                Check.Equal(5d, store.GetResidentDialogueRoutine(100, resident.Id).Itinerary.Stops[0].Route[1].X);
            }));
            cases.Add(("Routine reads ignore unrelated malformed settlement data", () => {
                var store = Filled();
                var resident = store.GetOrCreate(100, new[] { 10 }).Households[0].Residents[0];
                store.SetRoutines(Visit(100, resident.Id, 30));
                var unrelated = store.GetOrCreate(200, new[] { 20 });
                unrelated.Households[0].Residents[0].Id = "";

                Check.Equal(resident.Id, store.GetRoutines(100).Residents[0].ResidentId);
                store.SetRoutines(Visit(100, resident.Id, 31));
                Check.Equal(31, store.GetRoutines(100).Residents[0].DestinationKey);
            }));

            cases.Add(("Routine writes preserve unrelated towns and live residents", () => {
                var store = Filled();
                var resident = store.GetOrCreate(100, new[] { 10 }).Households[0].Residents[0];
                var otherResident = store.GetOrCreate(200, new[] { 20 }).Households[0].Residents[0];
                store.SetRoutines(Visit(100, resident.Id, 30));
                store.SetRoutines(Visit(200, otherResident.Id, 40));
                var otherTown = store.GetOrCreate(200, new[] { 20 });

                store.SetRoutines(Visit(100, resident.Id, 31));

                Check.True(ReferenceEquals(otherTown, store.GetOrCreate(200, new[] { 20 })));
                Check.True(ReferenceEquals(resident, store.GetOrCreate(100, new[] { 10 }).Households[0].Residents[0]));
                Check.Equal(31, store.GetRoutines(100).Residents[0].DestinationKey);
                Check.Equal(40, store.GetRoutines(200).Residents[0].DestinationKey);
            }));

            cases.Add(("Routine reads and writes isolate route lists and reject atomically", () => {
                var store = Filled();
                var resident = store.GetOrCreate(100, new[] { 10 }).Households[0].Residents[0];
                var source = Visit(100, resident.Id, 30);
                store.SetRoutines(source);
                source.Residents[0].Route[1].X = 99;
                Check.Equal(5d, store.GetRoutines(100).Residents[0].Route[1].X);

                var read = store.GetRoutines(100);
                read.Residents[0].Route[1].X = 88;
                Check.Equal(5d, store.GetRoutines(100).Residents[0].Route[1].X);

                var invalid = Visit(100, resident.Id, 31);
                invalid.Residents[0].Route[0].X = double.NaN;
                Check.Throws<ArgumentException>(() => store.SetRoutines(invalid));
                Check.Equal(30, store.GetRoutines(100).Residents[0].DestinationKey);
                Check.Equal(5d, store.GetRoutines(100).Residents[0].Route[1].X);
                Check.True(ReferenceEquals(resident, store.GetOrCreate(100, new[] { 10 }).Households[0].Residents[0]));
            }));
        }

        private static WorldStateStore Filled()
        {
            var store = new WorldStateStore(42);
            store.GetOrCreate(100, new[] { 10 });
            store.GetOrCreate(200, new[] { 20 });
            return store;
        }

        private static TownRoutineState Visit(int settlement, string residentId, int destination)
        {
            return new TownRoutineState { SettlementId = settlement, Residents = new List<ResidentRoutine> {
                new ResidentRoutine { ResidentId = residentId, DestinationKey = destination, OpenMinute = 600,
                    CloseMinute = 1000, Route = new List<SavedRoutePoint> {
                        new SavedRoutePoint(), new SavedRoutePoint { X = 5 } } } } };
        }
    }
}
