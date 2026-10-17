using System;
using System.Collections.Generic;
using LivingCities.Core;
namespace LivingCities.Tests
{
    internal static class ShopPhysicalProgressTests
    {
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Resumed shop visit preserves remaining dwell and exit phase",()=>{
                var saved=new ObservedVisit {ResidentId="resident",SettlementId=1,BuildingKey=1,ObservedAt=100,
                    PlannedEndAt=200,ResumeUntil=300,SecondsPerMetre=1,Legs=new List<ObservedVisitLeg> {
                    new ObservedVisitLeg {Route=new List<GuardPoint3>{new GuardPoint3(),new GuardPoint3{X=2}},DwellSeconds=10,AfterPhase=ShopVisitPhase.Browsing},
                    new ObservedVisitLeg {Route=new List<GuardPoint3>{new GuardPoint3{X=2},new GuardPoint3()},AfterPhase=ShopVisitPhase.Complete}}};
                var journey=ShopVisitJourney.Resume(saved,107);
                Check.Near(5,journey.DwellSeconds(0));
                var progress=new ShopPhysicalProgress(journey);progress.Advance(107,true);
                Check.Near(3,progress.RemainingDwell(109));
                progress.Advance(109,false);
                Check.Equal(ShopVisitPhase.Walking,progress.Phase);
                Check.Near(5,progress.RemainingDwell(109));
                progress.Advance(109,true);
                progress.Advance(114,true);Check.Equal(ShopVisitPhase.Leaving,progress.Phase);
                var exit=new ShopPhysicalProgress(ShopVisitJourney.Resume(saved,113));
                Check.Equal(ShopVisitPhase.Leaving,exit.Phase);
                Check.Throws<ArgumentException>(()=>ShopVisitJourney.Resume(saved,114));
            }));
            cases.Add(("Shop activity clock waits for physical arrival and full dwell",()=>{
                var paths=new List<List<GuardPoint3>> {
                    new List<GuardPoint3>{new GuardPoint3(),new GuardPoint3{X=2}},
                    new List<GuardPoint3>{new GuardPoint3{X=2},new GuardPoint3{X=4}},
                    new List<GuardPoint3>{new GuardPoint3{X=4},new GuardPoint3()} };
                var journey=ShopVisitJourney.Create(100,500,paths,1,12,true,0);
                var progress=new ShopPhysicalProgress(journey);
                progress.Advance(1000,false);
                Check.Equal(0,progress.Leg); Check.Equal(ShopVisitPhase.Walking,progress.Phase);
                progress.Advance(1000,true);
                Check.Equal(ShopVisitPhase.Browsing,progress.Phase);
                progress.Advance(1000+journey.DwellSeconds(0)-0.01,true);
                Check.Equal(0,progress.Leg);
                progress.Advance(1000+journey.DwellSeconds(0),true);
                Check.Equal(1,progress.Leg);Check.Equal(ShopVisitPhase.Walking,progress.Phase);
                progress.Advance(2000,false); Check.Equal(1,progress.Leg);
                progress.Advance(2000,true);Check.Equal(ShopVisitPhase.Haggling,progress.Phase);
                progress.Advance(2000+journey.DwellSeconds(1),true);
                Check.Equal(ShopVisitPhase.Leaving,progress.Phase);
                progress.Advance(3000,false);Check.Equal(ShopVisitPhase.Leaving,progress.Phase);
                progress.Advance(3000,true);Check.Equal(ShopVisitPhase.Complete,progress.Phase);
                var route=journey.LegRoute(0);route[0].X=999;
                Check.Equal(0d,journey.LegRoute(0)[0].X);
            }));
        }
    }
}
