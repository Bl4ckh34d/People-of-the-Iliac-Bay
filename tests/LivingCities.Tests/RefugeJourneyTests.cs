using System;
using System.Collections.Generic;
using LivingCities.Core;

namespace LivingCities.Tests
{
    internal static class RefugeJourneyTests
    {
        private static GuardRefugeJourney Journey()
        {
            return GuardRefugeJourney.Create(7, new[] { new GuardPoint3(), new GuardPoint3 { X=10 } },
                new[] { new GuardPoint3(), new GuardPoint3 { Z=4 } }, 100, 2, 12);
        }
        internal static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Refuge journeys reject corrupt routes, rates and release timestamps", () => {
                var journey=Journey(); journey.SecondsPerMetre=double.PositiveInfinity;
                Check.Throws<ArgumentException>(()=>GuardRefugeJourney.CopyValidated(journey));
                journey=Journey(); journey.ReleasedAt=100;
                Check.Throws<ArgumentException>(()=>GuardRefugeJourney.CopyValidated(journey));
                journey=Journey(); journey.Exterior.Clear();
                Check.Throws<ArgumentException>(()=>GuardRefugeJourney.CopyValidated(journey));
                Check.Throws<ArgumentException>(()=>Journey().Evaluate(double.NaN));
                Check.Throws<ArgumentException>(()=>Journey().Release(-1));
            }));
            cases.Add(("Refuge travel reaches door before entering and remains sheltered until released", () => {
                var journey=Journey();
                Check.Equal(RefugePhase.Approaching,journey.Evaluate(130).Phase);
                Check.Near(5,journey.Evaluate(130).Position.X);
                Check.Equal(RefugePhase.Entering,journey.Evaluate(160).Phase);
                Check.Near(2,journey.Evaluate(172).Position.Z);
                Check.Equal(RefugePhase.Sheltered,journey.Evaluate(184).Phase);
                Check.Equal(RefugePhase.Sheltered,journey.Evaluate(90000).Phase);
            }));
            cases.Add(("Released refuge journeys exit before retracing street route without teleporting", () => {
                var journey=Journey(); journey.Release(200);
                var copy=GuardRefugeJourney.CopyValidated(journey);
                Check.Equal(RefugePhase.Exiting,copy.Evaluate(212).Phase); Check.Near(2,copy.Evaluate(212).Position.Z);
                Check.Equal(RefugePhase.Returning,copy.Evaluate(224).Phase); Check.Near(10,copy.Evaluate(224).Position.X);
                Check.Near(5,copy.Evaluate(254).Position.X);
                Check.Equal(RefugePhase.Complete,copy.Evaluate(284).Phase); Check.Near(0,copy.Evaluate(284).Position.X);
                copy.Release(999); Check.Near(200,copy.ReleasedAt.Value);
            }));
            cases.Add(("Early stand-down waits for arrival and saved routes are defensive copies", () => {
                var journey=Journey(); journey.Release(120);
                Check.Near(184,journey.ReleasedAt.Value);
                var copy=GuardRefugeJourney.CopyValidated(journey); journey.Exterior[1].X=100;
                Check.Near(5,copy.Evaluate(130).Position.X);
                copy.Interior[0].Y=double.NaN;
                Check.Throws<ArgumentException>(()=>GuardRefugeJourney.CopyValidated(copy));
            }));
        }
    }
}
