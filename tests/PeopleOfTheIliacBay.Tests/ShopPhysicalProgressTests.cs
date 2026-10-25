using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
namespace PeopleOfTheIliacBay.Tests
{
    internal static class ShopPhysicalProgressTests
    {
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Resolved ambient purchase survives observed visit copies and save serialization",()=>{
                var saved=Shared().Checkpoint("parent",2,10,1200);
                var field=typeof(ObservedVisit).GetField("CounterPurchaseResolved");
                Check.True(field!=null,"Observed visits must remember an already settled shop purchase.");
                field.SetValue(saved,true);
                var copy=ObservedVisit.Copy(saved);
                Check.True((bool)field.GetValue(copy));
                var serializer=new FullSerializer.fsSerializer();
                FullSerializer.fsData data;serializer.TrySerialize(copy,out data).AssertSuccess();
                ObservedVisit restored=null;serializer.TryDeserialize(data,ref restored).AssertSuccess();
                Check.True((bool)field.GetValue(restored));
            }));
            cases.Add(("Actual restore accepts only native-equivalent rounded observation times",()=>{
                const double actual=12597868915.123456;
                double persisted=double.Parse(actual.ToString("G15",System.Globalization.CultureInfo.InvariantCulture),System.Globalization.CultureInfo.InvariantCulture);
                Check.True(persisted>actual);
                var saved=Shared().Checkpoint("parent",2,10,1200);saved.ObservedAt=persisted;
                var restored=ShopVisitJourney.ResumeAtActual(saved,actual);
                Check.Equal(saved.Legs.Count,restored.LegCount);Check.Equal(persisted,restored.Checkpoint("parent",2,10,persisted+100).ObservedAt);
                Check.Near(saved.Legs[0].DwellSeconds,restored.DwellSeconds(0));
                Check.Throws<ArgumentOutOfRangeException>(()=>ShopVisitJourney.ResumeAtActual(saved,actual-0.001));
                Check.Throws<ArgumentOutOfRangeException>(()=>ShopVisitJourney.ResumeAtActual(saved,double.NaN));
            }));
            cases.Add(("Actual shared checkpoint restore never advances its route or dwell from elapsed time",()=>{
                var saved=Shared().Checkpoint("parent",2,10,1200);
                var method=typeof(ShopVisitJourney).GetMethod("ResumeAtActual");
                if(method==null)throw new InvalidOperationException("Missing actual-position visit restoration.");
                var restored=(ShopVisitJourney)method.Invoke(null,new object[]{saved,7200d});
                Check.Equal(saved.Legs.Count,restored.LegCount);Check.Near(saved.Legs[0].Route[0].X,restored.LegRoute(0)[0].X);
                Check.Near(saved.Legs[0].DwellSeconds,restored.DwellSeconds(0));Check.Equal("shared-chair",restored.SeatId(2));
            }));
            cases.Add(("Actual zero-distance exit restore still waits for a physical doorway observation",()=>{
                var saved=new ObservedVisit{ResidentId="parent",SettlementId=2,BuildingKey=10,ObservedAt=100,PlannedEndAt=200,
                    ResumeUntil=200,SecondsPerMetre=1,Legs=new List<ObservedVisitLeg>{new ObservedVisitLeg{
                        Route=new List<GuardPoint3>{new GuardPoint3()},AfterPhase=ShopVisitPhase.Complete}}};
                var method=typeof(ShopVisitJourney).GetMethod("ResumeAtActual");
                if(method==null)throw new InvalidOperationException("Missing actual-position visit restoration.");
                var restored=(ShopVisitJourney)method.Invoke(null,new object[]{saved,500d});var progress=new ShopPhysicalProgress(restored);
                progress.Advance(501,false);Check.Equal(ShopVisitPhase.Leaving,progress.Phase);
                progress.Advance(502,true);Check.Equal(ShopVisitPhase.Complete,progress.Phase);
            }));
            cases.Add(("Shared Inn journey buys first then clears the counter before its assigned chair",()=>{
                var visit=Shared();Check.Equal(4,visit.LegCount);Check.Equal(ShopVisitPhase.Haggling,visit.StopPhase(0));
                Check.Equal<string>(null,visit.SeatId(1));Check.Near(0,visit.DwellSeconds(1));Check.Equal("shared-chair",visit.SeatId(2));
                Check.True(visit.DwellSeconds(2)>0);Check.Equal(ShopVisitPhase.Complete,visit.StopPhase(3));
                Check.Equal(2,new List<ShopOccupancy>(visit.Occupancies()).Count);
            }));
            cases.Add(("Paused shared service preserves full remaining physical dwell",()=>{
                var visit=Shared();var progress=new ShopPhysicalProgress(visit);progress.Advance(100,true);
                double remaining=progress.RemainingDwell(100);
                var method=typeof(ShopPhysicalProgress).GetMethod("Pause");
                if(method==null)throw new InvalidOperationException("Missing physical shop pause.");
                method.Invoke(progress,new object[]{200d});Check.Near(remaining,progress.RemainingDwell(200));
                Check.Equal(0,progress.Leg);progress.Advance(201,true);Check.Near(remaining-1,progress.RemainingDwell(201));
            }));
            cases.Add(("Aborted shared Inn service still requires the actual final doorway arrival",()=>{
                var visit=Shared();var progress=new ShopPhysicalProgress(visit);progress.Advance(100,true);
                var method=typeof(ShopPhysicalProgress).GetMethod("BeginExit");
                if(method==null)throw new InvalidOperationException("Missing physical shop exit request.");
                method.Invoke(progress,new object[]{101d});Check.Equal(3,progress.Leg);Check.Equal(ShopVisitPhase.Leaving,progress.Phase);
                progress.Advance(10000,false);Check.Equal(ShopVisitPhase.Leaving,progress.Phase);
                progress.Advance(10001,true);Check.Equal(ShopVisitPhase.Complete,progress.Phase);
            }));
            cases.Add(("Two-hour skipped visits resume at their seat with remaining dwell, not the entrance",()=>{
                var paths=new List<List<GuardPoint3>> {
                    new List<GuardPoint3>{new GuardPoint3(),new GuardPoint3{X=2}},
                    new List<GuardPoint3>{new GuardPoint3{X=2},new GuardPoint3{X=4}},
                    new List<GuardPoint3>{new GuardPoint3{X=4},new GuardPoint3()} };
                var visit=ShopVisitJourney.Create(9*3600,12*3600,paths,1,2,true,0,true);
                visit.AssignSeat(1,"chair-1");
                var saved=visit.Checkpoint("patron",1,1,13*3600);
                var sample=saved.Evaluate(11*3600);
                Check.Equal(ShopVisitPhase.Browsing,sample.Phase);Check.Near(4,sample.Position.X);
                var resumed=ShopVisitJourney.Resume(saved,11*3600);
                Check.Equal("chair-1",resumed.SeatId(0));Check.Equal(1,resumed.LegRoute(0).Count);
                Check.Near(3592,resumed.DwellSeconds(0));
                Check.True(saved.Evaluate(12*3600).Completed);
                Check.Throws<ArgumentException>(()=>ShopVisitJourney.Resume(saved,12*3600));
                saved.Legs[0].Route[0].X=100;
                Check.Near(0,visit.LegRoute(0)[0].X);
            }));
            cases.Add(("Clock catch-up distinguishes skipped time from pause and low frame rate",()=>{
                Check.True(ShopVisitJourney.NeedsCatchup(9*3600,11*3600,2,2));
                Check.True(ShopVisitJourney.NeedsCatchup(100,160,0.1,2));
                Check.True(!ShopVisitJourney.NeedsCatchup(100,100,7200,2));
                Check.True(!ShopVisitJourney.NeedsCatchup(100,220,60,2));
                Check.True(!ShopVisitJourney.NeedsCatchup(100,101,0.5,2));
                Check.True(!ShopVisitJourney.NeedsCatchup(-1,7200,1,2));
            }));
            cases.Add(("Keeper conversations last five to fifteen real seconds independent of game clock",()=>{
                var paths=new List<List<GuardPoint3>> {
                    new List<GuardPoint3>{new GuardPoint3(),new GuardPoint3{X=2}},
                    new List<GuardPoint3>{new GuardPoint3{X=2},new GuardPoint3{X=4}},
                    new List<GuardPoint3>{new GuardPoint3{X=4},new GuardPoint3()} };
                bool shortest=false,longest=false;
                foreach(double scale in new[]{2d,12d}) for(uint seed=0;seed<100;seed++)
                {
                    var visit=ShopVisitJourney.Create(100,2100,paths,1.3,scale,true,seed,true);
                    double realSeconds=visit.DwellSeconds(0)/scale;
                    Check.True(realSeconds>=5 && realSeconds<=15,"Keeper conversation outside requested real-time range.");
                    shortest|=realSeconds==5;longest|=realSeconds==15;
                }
                Check.True(shortest && longest);
            }));
            cases.Add(("Early tavern arrival starts keeper service immediately and retains full social dwell",()=>{
                var paths=new List<List<GuardPoint3>> {
                    new List<GuardPoint3>{new GuardPoint3(),new GuardPoint3{X=2}},
                    new List<GuardPoint3>{new GuardPoint3{X=2},new GuardPoint3{X=4}},
                    new List<GuardPoint3>{new GuardPoint3{X=4},new GuardPoint3()} };
                var journey=ShopVisitJourney.Create(1000,1600,paths,1.3,2,true,17,true);
                var progress=new ShopPhysicalProgress(journey);
                progress.Advance(100,false);
                Check.Equal(ShopVisitPhase.Walking,progress.Phase);
                progress.Advance(101,true);
                Check.Equal(ShopVisitPhase.Haggling,progress.Phase);
                progress.Advance(101+journey.DwellSeconds(0),true);
                Check.Equal(ShopVisitPhase.Walking,progress.Phase);
                progress.Advance(150,true);
                Check.Equal(ShopVisitPhase.Browsing,progress.Phase);
                Check.Near(journey.DwellSeconds(1),progress.RemainingDwell(150));
                progress.Advance(151,true);
                Check.Near(journey.DwellSeconds(1)-1,progress.RemainingDwell(151));
            }));
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
        private static ShopVisitJourney Shared()
        {
            var paths=new List<List<GuardPoint3>>{new List<GuardPoint3>{new GuardPoint3(),new GuardPoint3{X=2}},
                new List<GuardPoint3>{new GuardPoint3{X=2},new GuardPoint3{X=4}},
                new List<GuardPoint3>{new GuardPoint3{X=4},new GuardPoint3{X=4.5}},
                new List<GuardPoint3>{new GuardPoint3{X=4.5},new GuardPoint3()}};
            var method=typeof(ShopVisitJourney).GetMethod("CreateSharedInn");
            if(method==null)throw new InvalidOperationException("Missing shared Inn journey.");
            return (ShopVisitJourney)method.Invoke(null,new object[]{100d,1000d,paths,1d,2d,(uint)0,"shared-chair"});
        }
    }
}
