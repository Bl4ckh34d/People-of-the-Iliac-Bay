using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class ResidentClockMigrationTests
    {
        public static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Observed visit clock migration preserves walking position and calendar commitments", Walking));
            cases.Add(("Observed visit clock migration preserves current dwell and completed visits", Dwelling));
        }
        private static ObservedVisit Visit()
        {
            return ObservedVisit.Copy(new ObservedVisit { ResidentId="resident",SettlementId=1,BuildingKey=2,
                ObservedAt=100,PlannedEndAt=1000,ResumeUntil=1100,SecondsPerMetre=12,
                Legs=new List<ObservedVisitLeg> {
                    new ObservedVisitLeg {Route=new List<GuardPoint3>{new GuardPoint3{X=0},new GuardPoint3{X=10}},
                        DwellSeconds=100,AfterPhase=ShopVisitPhase.Browsing},
                    new ObservedVisitLeg {Route=new List<GuardPoint3>{new GuardPoint3{X=10},new GuardPoint3{X=0}},
                        AfterPhase=ShopVisitPhase.Complete} } });
        }
        private static void Walking()
        {
            var visit=Visit();var before=visit.Evaluate(160);
            visit.MigrateClockRate(160,12,2);
            var copy=ObservedVisit.Copy(visit);
            Check.Near(before.Position.X,copy.Evaluate(160).Position.X);
            Check.Near(10,copy.Evaluate(170).Position.X);
            Check.Equal(ShopVisitPhase.Browsing,copy.Evaluate(170).Phase);
            Check.Near(1000,copy.PlannedEndAt);Check.Near(1100,copy.ResumeUntil);
            Check.Near(290,copy.EndsAt);
        }
        private static void Dwelling()
        {
            var visit=Visit();visit.MigrateClockRate(250,12,2);
            Check.Equal(ShopVisitPhase.Browsing,visit.Evaluate(319).Phase);
            Check.Equal(ShopVisitPhase.Leaving,visit.Evaluate(320).Phase);
            Check.Near(10,visit.Evaluate(320).Position.X);
            Check.Near(340,visit.EndsAt);
            var completed=Visit();double end=completed.EndsAt;
            completed.MigrateClockRate(500,12,2);
            Check.Near(end,completed.EndsAt);
            Check.True(completed.Evaluate(500).Completed);
        }
    }
}
