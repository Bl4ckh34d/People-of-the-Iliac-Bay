using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class GuardTests
    {
        private static GuardRoster Roster(int count = 4)
        {
            return GuardRoster.Create(1, 42, count, Enumerable.Range(0, count).Select(i => new SavedRoutePoint { X = i * 10 }).ToList());
        }
        private static List<SavedRoutePoint> Route(double from, double to)
        { return new List<SavedRoutePoint> { new SavedRoutePoint { X = from }, new SavedRoutePoint { X = to } }; }
        internal static void Add(List<(string, Action)> cases)
        {
            cases.Add(("Haunting shelter interrupts exterior response from its actual position with surveyed home return",()=>{
                foreach(double at in new[]{101d,105d,405d,102d})
                {
                    var roster=Roster(2);var guard=roster.Guards[0];
                    roster.Dispatch("haunting-fixture",100,new Dictionary<string,List<SavedRoutePoint>>{{guard.Id,Route(0,4)}},1,1,1);
                    roster.Advance(at);
                    if(at==102)roster.Engage(guard.Id,at);
                    var position=roster.Position(guard.Id,at);
                    var plan=GuardRefugeJourney.Create(7,new[]{new GuardPoint3{X=position.X},new GuardPoint3{X=20}},
                        new[]{new GuardPoint3(),new GuardPoint3{Z=5}},at,1,1);
                    if(at!=102)Check.Throws<ArgumentException>(()=>roster.TakeRefuge(guard.Id,plan,at,Route(position.X,0)));
                    plan.HauntingShelter=true;
                    var invented=GuardRefugeJourney.Create(7,new[]{new GuardPoint3{X=position.X+1},new GuardPoint3{X=20}},
                        new[]{new GuardPoint3(),new GuardPoint3{Z=5}},at,1,1);
                    invented.HauntingShelter=true;
                    Check.Throws<ArgumentException>(()=>roster.TakeRefuge(guard.Id,invented,at,Route(position.X,0)));
                    roster.TakeRefuge(guard.Id,plan,at,Route(position.X,0));
                    Check.Equal(GuardDuty.Refuge,guard.Duty);
                    Check.True(guard.IncidentId==null && guard.Refuge.HauntingShelter);
                    Check.Near(position.X,guard.Refuge.Exterior[0].X);
                    Check.Near(0,guard.Refuge.ReturnExterior[guard.Refuge.ReturnExterior.Count-1].X);
                    GuardRoster.CopyValidated(roster);
                }
            }));
            cases.Add(("Wall trespass remains restricted through both shifts independently of haunting staffing",()=>{
                Func<double,bool,bool> enforced=GuardShifts.WallTrespassEnforced;
                Check.True(enforced(64799,false));
                Check.True(enforced(64800,false));
                Check.True(enforced(0,false));
                Check.True(enforced(21599,false));
                Check.True(enforced(21600,false));
                Check.True(enforced(64800,true));
            }));
            cases.Add(("Multiple wall posts share real stair access without reserving the upper doorway",()=>{
                var roster=Roster(2);
                for(int i=0;i<2;i++)
                {
                    var journey=GuardRefugeJourney.Create(7,new[]{new GuardPoint3{X=i*10},new GuardPoint3{X=5}},
                        new[]{new GuardPoint3(),new GuardPoint3{Y=6}},0,1,1);
                    journey.WallDuty=true;journey.LowerDoorIndex=0;journey.UpperDoorIndex=1;
                    journey.WallRoute=new List<GuardPoint3>{new GuardPoint3{X=5,Y=6},new GuardPoint3{X=20+i*20,Y=6}};
                    roster.TakeRefuge(roster.Guards[i].Id,journey,0);
                }
                Check.Equal(2,GuardRoster.CopyValidated(roster).Guards.Count(g=>g.Refuge!=null));
            }));
            cases.Add(("Verified wall posts add two shifts without consuming ordinary patrol or gate coverage",()=>{
                Check.Equal(128,GuardRoster.TargetWithWallSlots(48,4,0));
                Check.Equal(256,GuardRoster.TargetWithWallSlots(48,4,64));
                Check.Equal(1688,GuardRoster.TargetWithWallSlots(700,4,128));
                Check.Throws<ArgumentException>(()=>GuardRoster.TargetWithWallSlots(48,4,129));
            }));
            cases.Add(("Unresolved Daggerfall haunting overrides every night guard role",()=>{
                Check.True(GuardShifts.HauntingShelterRequired(1291010263,false,18*3600));
                Check.True(GuardShifts.HauntingShelterRequired(1291010263,false,5*3600));
                Check.True(!GuardShifts.HauntingShelterRequired(1291010263,false,6*3600));
                Check.True(!GuardShifts.HauntingShelterRequired(1291010263,true,20*3600));
                Check.True(!GuardShifts.HauntingShelterRequired(1,false,20*3600));
            }));
            cases.Add(("Wall watch traverses lower door interior upper door and elevated route both ways",()=>{
                var journey=GuardRefugeJourney.Create(7,new[]{new GuardPoint3(),new GuardPoint3{X=10}},
                    new[]{new GuardPoint3(),new GuardPoint3{Y=10}},100,1,1);
                journey.WallDuty=true;journey.LowerDoorIndex=0;journey.UpperDoorIndex=1;
                journey.WallRoute=new List<GuardPoint3>{new GuardPoint3{X=10,Y=10},new GuardPoint3{X=20,Y=10}};
                Check.True(journey.ReleaseToReturnBy(200));
                journey=GuardRefugeJourney.CopyValidated(journey);
                Check.Equal(RefugePhase.Approaching,journey.Evaluate(105).Phase);
                Check.Equal(RefugePhase.Entering,journey.Evaluate(115).Phase);
                Check.Equal(RefugePhase.WallApproaching,journey.Evaluate(125).Phase);
                Check.Equal(RefugePhase.WallPosted,journey.Evaluate(135).Phase);
                Check.Near(10,journey.Evaluate(135).Position.Y);
                Check.Equal(RefugePhase.WallReturning,journey.Evaluate(175).Phase);
                Check.Equal(RefugePhase.Exiting,journey.Evaluate(185).Phase);
                Check.Equal(RefugePhase.Returning,journey.Evaluate(195).Phase);
                Check.Equal(RefugePhase.Complete,journey.Evaluate(200).Phase);
                journey.UpperDoorIndex=0;
                Check.Throws<ArgumentException>(()=>GuardRefugeJourney.CopyValidated(journey));
            }));
            cases.Add(("Patrol allocation gives meaningful proportional coverage while retaining town posts",()=>{
                Check.Equal(0,GuardShifts.PatrolPairTarget(2,8));
                Check.Equal(1,GuardShifts.PatrolPairTarget(3,8));
                Check.Equal(2,GuardShifts.PatrolPairTarget(16,8));
                Check.Equal(5,GuardShifts.PatrolPairTarget(32,8));
                Check.Equal(8,GuardShifts.PatrolPairTarget(48,8));
                Check.Equal(8,GuardShifts.PatrolPairTarget(700,8));
                Check.Equal(2,GuardShifts.PatrolPairTarget(48,2));
                Check.Equal(0,GuardShifts.PatrolPairTarget(48,0));
            }));
            cases.Add(("Guard shifts change at native dawn and dusk including the next day",()=>{
                Check.Equal(GuardShift.Night,GuardShifts.At(21599));
                Check.Equal(GuardShift.Day,GuardShifts.At(21600));
                Check.Equal(GuardShift.Day,GuardShifts.At(64799));
                Check.Equal(GuardShift.Night,GuardShifts.At(64800));
                Check.Near(21600,GuardShifts.NextChange(0));
                Check.Near(64800,GuardShifts.NextChange(21600));
                Check.Near(108000,GuardShifts.NextChange(64800));
            }));
            cases.Add(("Guard bed rest leaves early enough to physically return for shift start",()=>{
                var journey=GuardRefugeJourney.Create(7,new[] {new GuardPoint3(),new GuardPoint3 {X=10}},
                    new[] {new GuardPoint3(),new GuardPoint3 {Z=8}},100,1,1);
                journey.WallDuty=true;journey.RestBedRecord=3;
                Check.True(journey.ReleaseToReturnBy(200));
                Check.Near(182,journey.ReleasedAt.Value);
                Check.Equal(RefugePhase.Returning,journey.Evaluate(199).Phase);
                Check.Equal(RefugePhase.Complete,journey.Evaluate(200).Phase);
                var shortRest=GuardRefugeJourney.Create(7,new[] {new GuardPoint3(),new GuardPoint3 {X=10}},
                    new[] {new GuardPoint3(),new GuardPoint3 {Z=8}},100,1,1);
                shortRest.WallDuty=true;shortRest.RestBedRecord=3;
                Check.True(!shortRest.ReleaseToReturnBy(120));
                Check.True(!shortRest.ReleasedAt.HasValue);
            }));
            cases.Add(("Guard shift membership persists and replacement inherits the dead guard shift",()=>{
                var roster=Roster(6);
                Check.Equal(3,roster.Guards.Count(g=>GuardShifts.OnDuty(g,12*3600)));
                Check.Equal(3,roster.Guards.Count(g=>GuardShifts.OnDuty(g,0)));
                roster.Guards[1].Shift=GuardShift.Day;
                roster.Kill(roster.Guards[1].Id,1);
                roster.Recruit(1+7*86400);
                var restored=GuardRoster.CopyValidated(roster);
                Check.Equal(GuardShift.Day,restored.Guards[6].Shift);
                Check.Near(roster.Guards[1].ReliefPost.X,restored.Guards[6].ReliefPost.X);
                Check.True(!GuardShifts.OnDuty(restored.Guards[1],12*3600));
                restored.Guards[0].Shift=(GuardShift)99;
                Check.Throws<ArgumentException>(()=>GuardRoster.CopyValidated(restored));
            }));
            cases.Add(("Assigning legacy guard shifts balances once without changing existing duty",()=>{
                var roster=Roster(6);
                foreach(var guard in roster.Guards)guard.Shift=GuardShift.Unassigned;
                roster.AssignShifts();
                var before=string.Join(",",roster.Guards.Select(g=>g.Shift));
                roster.AssignShifts();
                Check.Equal(before,string.Join(",",roster.Guards.Select(g=>g.Shift)));
                Check.Equal(3,roster.Guards.Count(g=>g.Shift==GuardShift.Day));
                Check.True(roster.Guards.All(g=>g.Duty==GuardDuty.Posted));
            }));
            cases.Add(("Wall bed reservations share quarters but never the same bed",()=>{
                var roster=Roster(3);
                for(int i=0;i<3;i++) {
                    roster.SchedulePostRecall(roster.Guards[i].Id,Route(i*10,i*10+5),200,220,1,1);
                    var shift=GuardRefugeJourney.Create(7,new[] {new GuardPoint3 {X=i*10},new GuardPoint3 {X=i*10+5},new GuardPoint3 {X=35}},
                        new[] {new GuardPoint3(),new GuardPoint3 {Z=8}},200,1,1);
                    shift.WallDuty=true; shift.RestBedRecord=i==2?0:i;
                    shift.ReturnExterior=new List<GuardPoint3> {new GuardPoint3 {X=35},new GuardPoint3 {X=i*10+5}};
                    shift.Release(300);
                    if(i==2) Check.Throws<ArgumentException>(()=>roster.ScheduleWallDuty(roster.Guards[i].Id,shift));
                    else roster.ScheduleWallDuty(roster.Guards[i].Id,shift);
                }
                var restored=GuardRoster.CopyValidated(roster);
                Check.Equal(1,restored.Guards[1].PostRecall.WallDuty.RestBedRecord.Value);
                restored.Guards[1].PostRecall.WallDuty.RestBedRecord=0;
                Check.Throws<ArgumentException>(()=>GuardRoster.CopyValidated(restored));
                roster.Advance(250);
                var active=GuardRoster.CopyValidated(roster);
                Check.Equal(1,active.Guards[1].Refuge.RestBedRecord.Value);
                active.Guards[1].Refuge.RestBedRecord=0;
                Check.Throws<ArgumentException>(()=>GuardRoster.CopyValidated(active));
            }));
            cases.Add(("Active wall refuge rejects an already reserved bed",()=>{
                var roster=Roster(2); var first=roster.Guards[0].Id; var second=roster.Guards[1].Id;
                var occupied=GuardRefugeJourney.Create(7,new[] {new GuardPoint3(),new GuardPoint3 {X=5}},
                    new[] {new GuardPoint3(),new GuardPoint3 {Z=8}},100,1,1);
                occupied.WallDuty=true; occupied.RestBedRecord=3;
                roster.TakeRefuge(first,occupied,100);
                roster.Guards[0].Refuge.Release(300);
                var duplicate=GuardRefugeJourney.Create(7,new[] {new GuardPoint3 {X=10},new GuardPoint3 {X=5}},
                    new[] {new GuardPoint3(),new GuardPoint3 {Z=8}},100,1,1);
                duplicate.WallDuty=true; duplicate.RestBedRecord=3;
                Check.Throws<ArgumentException>(()=>roster.TakeRefuge(second,duplicate,100));
            }));
            cases.Add(("Night patrol rest intent survives return and save load",(Action)(()=>{
                var roster=Roster(2); string a=roster.Guards[0].Id,b=roster.Guards[1].Id;
                var circuit=new List<SavedRoutePoint> {new SavedRoutePoint {X=0},new SavedRoutePoint {X=20},
                    new SavedRoutePoint {X=20,Z=20},new SavedRoutePoint {Z=20},new SavedRoutePoint()};
                var plan=PatrolPairPlan.Create(a,b,Route(0,0),Route(10,2),circuit,100,200,1,1,2);
                roster.StartPatrol(plan,100); roster.StopPatrol(a,150);
                roster.ScheduleDayRest(a,300,150); roster.ScheduleDayRest(b,300,150);
                var options=new JsonSerializerOptions {IncludeFields=true};
                roster=GuardRoster.CopyValidated(JsonSerializer.Deserialize<GuardRoster>(JsonSerializer.Serialize(roster,options),options));
                Check.Near(300,roster.Guards[0].DayRestUntil); Check.Near(300,roster.Guards[1].DayRestUntil);
                roster.Advance(301);
                Check.Near(0,roster.Guards[0].DayRestUntil);
            })));
            cases.Add(("Emergency response cancels a guard's pending daytime rest",()=>{
                var roster=Roster(1); var id=roster.Guards[0].Id;
                roster.ScheduleDayRest(id,300,10);
                roster.Dispatch("emergency",10,new Dictionary<string,List<SavedRoutePoint>> {{id,Route(0,20)}},1,2,1);
                Check.Near(0,roster.Guards[0].DayRestUntil);
            }));
            cases.Add(("Wall bed rest stays at its bed, survives reload and leaves exactly at shift time",()=>{
                var shift=GuardRefugeJourney.Create(7,new[] {new GuardPoint3(),new GuardPoint3 {X=10}},
                    new[] {new GuardPoint3(),new GuardPoint3 {Z=8}},200,1,1);
                shift.WallDuty=true; shift.RestBedRecord=42; shift.Release(301);
                var options=new JsonSerializerOptions {IncludeFields=true};
                shift=GuardRefugeJourney.CopyValidated(JsonSerializer.Deserialize<GuardRefugeJourney>(JsonSerializer.Serialize(shift,options),options));
                Check.Equal(42,shift.RestBedRecord.Value); Check.Near(301,shift.ReleasedAt.Value);
                Check.Equal(RefugePhase.Sheltered,shift.Evaluate(220).Phase);
                Check.Near(8,shift.Evaluate(220).Position.Z); Check.Near(8,shift.Evaluate(299).Position.Z);
                Check.Equal(RefugePhase.Exiting,shift.Evaluate(302).Phase); Check.Near(7,shift.Evaluate(302).Position.Z);
                shift.WallDuty=false;
                Check.Throws<ArgumentException>(()=>GuardRefugeJourney.CopyValidated(shift));
            }));
            cases.Add(("Wall shift patrol survives save, moves indoors, and returns to inner staging at dawn",()=>{
                var roster=Roster(1); var id=roster.Guards[0].Id;
                roster.SchedulePostRecall(id,Route(0,5),200,220,1,1);
                var shift=GuardRefugeJourney.Create(7,new[] { new GuardPoint3(),new GuardPoint3 { X=5 },new GuardPoint3 { X=10 } },
                    new[] { new GuardPoint3(),new GuardPoint3 { Z=8 } },200,1,1);
                shift.WallDuty=true;
                shift.ReturnExterior=new List<GuardPoint3> { new GuardPoint3 { X=10 },new GuardPoint3 { X=5 } };
                shift.Release(300);
                roster.ScheduleWallDuty(id,shift);
                var options=new JsonSerializerOptions { IncludeFields=true };
                roster=GuardRoster.CopyValidated(JsonSerializer.Deserialize<GuardRoster>(JsonSerializer.Serialize(roster,options),options));
                roster.Advance(203); Check.Equal(GuardDuty.Refuge,roster.Guards[0].Duty);
                Check.Near(3,roster.Position(id,203).X);
                var journey=roster.Guards[0].Refuge;
                Check.Equal(RefugePhase.Sheltered,journey.Evaluate(220).Phase);
                Check.True(Math.Abs(journey.Evaluate(220).Position.Z-journey.Evaluate(222).Position.Z)>0.1);
                Check.True(journey.Evaluate(224).Position.Z>0);
                roster.Advance(500); Check.Equal(GuardDuty.Posted,roster.Guards[0].Duty);
                Check.Near(5,roster.Position(id,500).X); Check.True(!roster.KnowsPlayerCrime(id));
            }));
            cases.Add(("Wall shift rejects mismatched recall and duplicate quarter reservations",()=>{
                var roster=Roster(2); var id=roster.Guards[0].Id;
                roster.SchedulePostRecall(id,Route(0,5),200,220,1,1);
                var shift=GuardRefugeJourney.Create(7,new[] { new GuardPoint3(),new GuardPoint3 { X=5 },new GuardPoint3 { X=15 } },
                    new[] { new GuardPoint3(),new GuardPoint3 { Z=8 } },200,1,1);
                shift.WallDuty=true; shift.ReturnExterior=new List<GuardPoint3> {new GuardPoint3 {X=15},new GuardPoint3 {X=5}};
                shift.Release(300); roster.ScheduleWallDuty(id,shift);
                var other=roster.Guards[1].Id; roster.SchedulePostRecall(other,Route(10,15),200,220,1,1);
                Check.Throws<ArgumentException>(()=>roster.ScheduleWallDuty(other,shift));
                shift.Exterior[0].X=90;
                Check.Near(0,roster.Guards[0].PostRecall.WallDuty.Exterior[0].X);
                roster.Guards[0].PostRecall.WallDuty.SecondsPerMetre=2;
                Check.Throws<ArgumentException>(()=>GuardRoster.CopyValidated(roster));
            }));
            cases.Add(("Scheduled wall duty yields to crime response and retains guard health",()=>{
                var roster=Roster(1); var id=roster.Guards[0].Id;
                roster.Guards[0].HealthFraction=0.4;
                roster.SchedulePostRecall(id,Route(0,5),200,220,1,1);
                var shift=GuardRefugeJourney.Create(7,new[] { new GuardPoint3(),new GuardPoint3 { X=5 },new GuardPoint3 { X=10 } },
                    new[] { new GuardPoint3(),new GuardPoint3 { Z=8 } },200,1,1);
                shift.WallDuty=true; shift.ReturnExterior=new List<GuardPoint3> {new GuardPoint3 {X=10},new GuardPoint3 {X=5}};
                shift.Release(300); roster.ScheduleWallDuty(id,shift);
                roster.Dispatch("crime",150,new Dictionary<string,List<SavedRoutePoint>> { {id,Route(0,50)} },1,2,1);
                roster.Advance(210);
                Check.True(roster.Guards[0].PostRecall==null && roster.Guards[0].Refuge==null);
                Check.Near(0.4,roster.Guards[0].HealthFraction);
            }));
            cases.Add(("Repeated gate duty keeps routine report storage bounded",()=>{
                var roster=Roster(1); var id=roster.Guards[0].Id;
                for(int day=1;day<=30;day++)
                {
                    roster.MovePost(id,Route(roster.Guards[0].Post.X,20),day*1000,2,1);
                    roster.SchedulePostRecall(id,Route(20,5),day*1000+200,day*1000+220,1,1);
                    roster.Advance(day*1000+205);
                    roster=GuardRoster.CopyValidated(roster);
                    Check.True(roster.Reports.Count<=1);
                    roster.Advance(day*1000+300);
                    Check.Equal(0,roster.Reports.Count);
                }
            }));
            cases.Add(("Gate recall advances while unloaded and survives a saved mid-route journey",()=>{
                var roster=Roster(1); var id=roster.Guards[0].Id;
                roster.MovePost(id,Route(0,20),100,2,1);
                roster.SchedulePostRecall(id,Route(20,5),200,220,1,1);
                var options=new JsonSerializerOptions { IncludeFields=true };
                roster=GuardRoster.CopyValidated(JsonSerializer.Deserialize<GuardRoster>(JsonSerializer.Serialize(roster,options),options));
                Check.Near(20,roster.Position(id,199).X);
                Check.Near(15,roster.Position(id,205).X);
                Check.Equal(GuardDuty.Returning,roster.Guards[0].Duty);
                roster=GuardRoster.CopyValidated(roster);
                Check.Near(5,roster.Position(id,1000).X);
                Check.Equal(GuardDuty.Posted,roster.Guards[0].Duty);
                Check.True(roster.Guards[0].PostRecall==null);
                Check.Equal(1,roster.Guards.Count);
                Check.True(!roster.KnowsPlayerCrime(id));
            }));
            cases.Add(("Gate recall cannot replace an emergency or resurrect a dead guard",()=>{
                var roster=Roster(1); var id=roster.Guards[0].Id;
                roster.SchedulePostRecall(id,Route(0,5),200,220,1,1);
                roster.Dispatch("crime",150,new Dictionary<string,List<SavedRoutePoint>> { { id,Route(0,50) } },1,2,1);
                roster.Advance(1000);
                Check.True(roster.Guards[0].PostRecall==null);
                Check.Near(0,roster.Guards[0].Post.X);
                var dead=Roster(1); id=dead.Guards[0].Id;
                dead.SchedulePostRecall(id,Route(0,5),200,220,1,1);
                dead.Kill(id,160); dead.Advance(1000);
                Check.Equal(GuardDuty.Dead,dead.Guards[0].Duty);
                Check.Near(0,dead.Position(id,1000).X);
            }));
            cases.Add(("Gate recall rejects missed closing deadlines and corrupted saved routes",()=>{
                var roster=Roster(1); var id=roster.Guards[0].Id;
                Check.Throws<ArgumentException>(()=>roster.SchedulePostRecall(id,Route(0,50),200,220,1,1));
                Check.Throws<ArgumentException>(()=>roster.SchedulePostRecall(id,Route(9,5),200,220,1,1));
                roster.SchedulePostRecall(id,Route(0,5),200,220,1,1);
                var copied=GuardRoster.CopyValidated(roster);
                copied.Guards[0].PostRecall.Route[0].X=90;
                Check.Near(0,roster.Guards[0].PostRecall.Route[0].X);
                roster.Guards[0].PostRecall.ArrivesAt=230;
                Check.Throws<ArgumentException>(()=>GuardRoster.CopyValidated(roster));
            }));
            cases.Add(("Gate guard posts flank opening and face away from wall",()=>{
                var plan=GateGuardPosts.Build(10,20,1,0,0,1,2,3,1.5,1.25);
                Check.Near(13.5,plan.InsideLeft.X); Check.Near(13.5,plan.InsideRight.X);
                Check.Near(40,plan.InsideLeft.Z+plan.InsideRight.Z);
                Check.Near(3,plan.InsideReturnLeft.X-plan.InsideLeft.X);
                Check.Near(6.5,plan.OutsideLeft.X); Check.Near(6.5,plan.OutsideRight.X);
                Check.Near(1,plan.InsideFacing.X); Check.Near(0,plan.InsideFacing.Z);
                Check.Near(-1,plan.OutsideFacing.X); Check.Near(0,plan.OutsideFacing.Z);
                var rotated=GateGuardPosts.Build(0,0,0,-4,3,0,1,2,1,1);
                Check.Near(-3,rotated.InsideLeft.X); Check.Near(-2,rotated.InsideLeft.Z);
                Check.Near(0,rotated.OutsideFacing.X); Check.Near(1,rotated.OutsideFacing.Z);
                Check.Throws<ArgumentException>(()=>GateGuardPosts.Build(0,0,1,0,1,0,1,2,1,1));
            }));
            cases.Add(("Guard effect snapshot survives validated roster copy without shared mutable state",()=>{
                var roster=Roster(1);
                roster.Guards[0].Effects=new GuardEffectSnapshot { Json="[]",MagicMinute=42 };
                var copy=GuardRoster.CopyValidated(roster);
                Check.Equal("[]",copy.Guards[0].Effects.Json);
                Check.Equal(42u,copy.Guards[0].Effects.MagicMinute);
                copy.Guards[0].Effects.Json="changed";
                Check.Equal("[]",roster.Guards[0].Effects.Json);
                roster.Guards[0].Effects.Json=" ";
                Check.Throws<ArgumentException>(()=>GuardRoster.CopyValidated(roster));
                roster.Guards[0].Effects.Json=new string('x',1048577);
                Check.Throws<ArgumentException>(()=>GuardRoster.CopyValidated(roster));
                Check.True(Roster(1).Guards[0].Effects==null);
            }));
            cases.Add(("Building defense with unavailable responders remains retryable without a phantom report",()=>{
                var roster=Roster(1); var id=roster.Guards[0].Id;
                roster.CacheBuildingApproach(73,new SavedRoutePoint { X=100 },
                    new Dictionary<string,List<SavedRoutePoint>> { { id,Route(0,100) } });
                Check.Equal(0,roster.DispatchBuilding("danger",73,0,0,2,10,threatId:"enemy:1").Count);
                Check.Equal(0,roster.Reports.Count);
                roster.Dispatch("busy",0,new Dictionary<string,List<SavedRoutePoint>> { { id,Route(0,20) } },1,2,10);
                Check.Equal(0,roster.DispatchBuilding("danger",73,1,1,2,10,threatId:"enemy:1").Count);
                Check.Equal(1,roster.Reports.Count);
                roster.Advance(500);
                Check.Equal(1,roster.DispatchBuilding("danger",73,501,1,2,10,threatId:"enemy:1").Count);
                Check.Equal("enemy:1",GuardRoster.CopyValidated(roster).Reports.Find(r=>r.Id=="danger").ThreatId);
            }));
            cases.Add(("Live post reassignment walks the checked route and survives reload",()=>{
                var roster=Roster(1); var id=roster.Guards[0].Id;
                roster.MovePost(id,Route(0,20),100,2,10);
                Check.Equal(GuardDuty.Returning,roster.Guards[0].Duty);
                Check.Near(0,roster.Position(id,100).X);
                roster=GuardRoster.CopyValidated(roster);
                Check.Near(10,roster.Position(id,150).X);
                roster.Advance(200); Check.Equal(GuardDuty.Posted,roster.Guards[0].Duty);
                Check.Near(20,roster.Position(id,200).X);
            }));
            cases.Add(("Indoor pursuer retraces to doorway then resumes exterior pursuit after reload",()=>{
                var roster=Roster(1); var id=roster.Guards[0].Id;
                roster.CacheBuildingApproach(73,new SavedRoutePoint { X=100 },
                    new Dictionary<string,List<SavedRoutePoint>> { { id,Route(0,100) } });
                roster.DispatchBuilding("shop",73,0,1,2,10);
                roster.EnterBuilding(id,73,500,new GuardPoint3());
                roster.ObserveInterior(id,new GuardPoint3 { X=10 },0.5,501,true,new GuardPoint3 { X=12 });
                roster.PursueOutside(id,502);
                Check.True(roster.Guards[0].Interior.Returning);
                double exit=roster.Guards[0].Interior.ExitAt;
                Check.True(exit>502);
                roster=GuardRoster.CopyValidated(roster);
                var dead=GuardRoster.CopyValidated(roster); dead.Kill(id,503);
                Check.True(GuardRoster.CopyValidated(dead).Guards[0].Interior.ExteriorPursuit==null);
                var forged=GuardRoster.CopyValidated(roster); forged.Guards[0].Interior.ExteriorPursuit.X+=1;
                Check.Throws<ArgumentException>(()=>GuardRoster.CopyValidated(forged));
                Check.Near(5,roster.InteriorPosition(id,(502+exit)/2).X);
                roster.Advance(exit);
                Check.True(roster.Guards[0].Interior==null);
                Check.Equal(GuardDuty.Engaged,roster.Guards[0].Duty);
                Check.Near(100,roster.Position(id,exit).X);
                Check.Near(100,roster.Guards[0].LastSeenSuspect.X);
                Check.Near(0.5,roster.Guards[0].HealthFraction);
                Check.Equal("shop",GuardRoster.CopyValidated(roster).Guards[0].IncidentId);
                roster.ReturnFromEngagement(id,exit+1,Route(100,0),2,10);
                roster.Advance(exit+501); Check.Equal(GuardDuty.Posted,roster.Guards[0].Duty);
            }));
            cases.Add(("Post reassignment preserves identity and health and rejects active duties",()=>{
                var roster=Roster(1); var guard=roster.Guards[0]; guard.HealthFraction=0.5;
                roster.CacheBuildingApproach(73,new SavedRoutePoint { X=100 },
                    new Dictionary<string,List<SavedRoutePoint>> { { guard.Id,Route(0,100) } });
                string id=guard.Id; uint appearance=guard.AppearanceSeed;
                roster.ReassignPost(id,new SavedRoutePoint { X=20 },0);
                var copy=GuardRoster.CopyValidated(roster);
                Check.Equal(id,copy.Guards[0].Id); Check.Equal(appearance,copy.Guards[0].AppearanceSeed);
                Check.Near(0.5,copy.Guards[0].HealthFraction); Check.Near(20,copy.Position(id,0).X);
                Check.Equal(0,copy.BuildingApproaches.Count);
                copy.Dispatch("busy",0,new Dictionary<string,List<SavedRoutePoint>> { { id,Route(20,100) } },1,2,10);
                Check.Throws<ArgumentException>(()=>copy.ReassignPost(id,new SavedRoutePoint { X=30 },0));
                Check.Near(20,copy.Guards[0].Post.X);
                copy.Kill(id,1);
                Check.Throws<ArgumentException>(()=>copy.ReassignPost(id,new SavedRoutePoint { X=40 },2));
            }));
            cases.Add(("Junction posts require three paved branches and exclude a straight road",()=>{
                Func<int,int,int> cross=(x,z)=>x==0 || z==0 ? 15 : 0;
                Check.True(GuardPostSites.IsJunction(0,0,cross,6));
                Check.True(!GuardPostSites.IsJunction(0,0,(x,z)=>z==0 ? 15 : 0,6));
                Check.True(GuardPostSites.IsJunction(0,0,(x,z)=>4,6));
                Check.True(!GuardPostSites.IsJunction(0,0,(x,z)=>12,6));
            }));
            cases.Add(("Building defense preserves target through delayed entry and return", () => {
                var roster=Roster(1); var id=roster.Guards[0].Id;
                roster.CacheBuildingApproach(73,new SavedRoutePoint { X=100 },
                    new Dictionary<string,List<SavedRoutePoint>> { { id,Route(0,100) } });
                var sent=roster.DispatchBuilding("hostile-shop",73,0,1,2,10,false,null,"native:123");
                Check.Equal(1,sent.Count); Check.Equal("native:123",roster.Reports[0].ThreatId);
                Check.Throws<ArgumentException>(()=>roster.EnterBuilding(id,73,499,new GuardPoint3()));
                roster=GuardRoster.CopyValidated(roster);
                roster.EnterBuilding(id,73,500,new GuardPoint3());
                roster.ObserveInterior(id,new GuardPoint3 { X=3 },0.5,501,true,new GuardPoint3 { X=5 });
                roster=GuardRoster.CopyValidated(roster);
                Check.Equal("native:123",roster.Reports[0].ThreatId);
                Check.Near(0.5,roster.Guards[0].HealthFraction);
                roster.EndInteriorSearch(id,502);
                double exit=roster.Guards[0].Interior.ExitAt;
                roster.Advance(exit+500);
                Check.Equal(GuardDuty.Posted,roster.Guards[0].Duty);
            }));
            cases.Add(("Queued building defense retains target and forbids criminal conversion", () => {
                var roster=Roster(1);
                roster.QueueBuildingReport("danger",73,"native:123");
                roster=GuardRoster.CopyValidated(roster);
                Check.Equal("native:123",roster.Reports[0].ThreatId);
                Check.Throws<ArgumentException>(()=>roster.QueueBuildingReport("danger",73));
                Check.Throws<ArgumentException>(()=>roster.DispatchBuilding("danger",73,0,1,2,10));
                Check.Throws<ArgumentException>(()=>roster.DispatchBuilding("other",73,0,1,2,10,true,null,"native:123"));
            }));
            cases.Add(("Queued native building alarm survives save and dispatches only after a surveyed route", () => {
                var roster = Roster(1);
                roster.QueueBuildingReport("native:shop", 73);
                roster.QueueBuildingReport("native:shop", 73);
                Check.Equal(1, roster.Reports.Count);
                Check.Equal(0, roster.DispatchBuilding("native:shop", 73, 10, 1, 2, 10).Count);
                var copy = GuardRoster.CopyValidated(JsonSerializer.Deserialize<GuardRoster>(
                    JsonSerializer.Serialize(roster, new JsonSerializerOptions { IncludeFields = true }),
                    new JsonSerializerOptions { IncludeFields = true }));
                Check.Equal(73, copy.Reports[0].BuildingKey.Value);
                Check.Equal(0, copy.Reports[0].GuardIds.Count);
                Check.Throws<ArgumentException>(() => copy.QueueBuildingReport("native:shop", 74));
                copy.CacheBuildingApproach(73, new SavedRoutePoint { X = 100 },
                    new Dictionary<string, List<SavedRoutePoint>> { { copy.Guards[0].Id, Route(0, 100) } });
                Check.Equal(1, copy.DispatchBuilding("native:shop", 73, 100, 1, 2, 10).Count);
                Check.Near(600, copy.Guards[0].ArrivesAt);
                Check.Equal(1, copy.DispatchBuilding("native:shop", 73, 110, 4, 2, 10).Count);
                Check.Near(600, copy.Guards[0].ArrivesAt);
                GuardRoster.CopyValidated(copy);
            }));
            cases.Add(("Queued building report validation rejects fake responders and missing queue markers", () => {
                var roster = Roster(1); roster.QueueBuildingReport("native:shop", 73);
                var broken = GuardRoster.CopyValidated(roster);
                broken.Reports[0].GuardIds.Add(broken.Guards[0].Id);
                Check.Throws<ArgumentException>(() => GuardRoster.CopyValidated(broken));
                broken = GuardRoster.CopyValidated(roster); broken.Reports[0].AwaitingApproach = false;
                Check.Throws<ArgumentException>(() => GuardRoster.CopyValidated(broken));
                broken = GuardRoster.CopyValidated(roster); broken.Reports[0].Investigation = true;
                Check.Throws<ArgumentException>(() => GuardRoster.CopyValidated(broken));
                broken = GuardRoster.CopyValidated(roster); broken.Reports[0].Entrance = new SavedRoutePoint();
                Check.Throws<ArgumentException>(() => GuardRoster.CopyValidated(broken));
                Check.Throws<ArgumentException>(() => roster.DispatchBuilding("native:shop", 73, 1, 1, 2, 10, true));
                Check.Equal(1, roster.Reports.Count);
            }));
            cases.Add(("Building alarm dispatches only guards who received the report", () => {
                var roster = Roster();
                roster.CacheBuildingApproach(73, new SavedRoutePoint { X = 100 },
                    roster.Guards.ToDictionary(g => g.Id, g => Route(g.Post.X, 100)));
                var informed = new HashSet<string> { roster.Guards[0].Id };
                var sent = roster.DispatchBuilding("witnessed-entry", 73, 0, 4, 2, 12, false, informed);
                Check.Equal(1, sent.Count);
                Check.Equal(roster.Guards[0].Id, sent[0].Id);
                Check.True(roster.Guards.Skip(1).All(g => g.Duty == GuardDuty.Posted));
                var copy = GuardRoster.CopyValidated(roster);
                Check.Equal(1, copy.Reports.Single().GuardIds.Count);
                var none = Roster();
                none.CacheBuildingApproach(73, new SavedRoutePoint { X = 100 },
                    none.Guards.ToDictionary(g => g.Id, g => Route(g.Post.X, 100)));
                Check.Equal(0, none.DispatchBuilding("unseen-entry", 73, 0, 4, 2, 12, false, new HashSet<string>()).Count);
            }));
            cases.Add(("Indoor guard entry waits for physical arrival and saves its interior coordinates", () => {
                var roster = Roster(1); var id = roster.Guards[0].Id;
                roster.CacheBuildingApproach(73, new SavedRoutePoint { X = 100 },
                    new Dictionary<string, List<SavedRoutePoint>> { { id, Route(0, 100) } });
                roster.DispatchBuilding("shop", 73, 0, 1, 2, 10);
                Check.Throws<ArgumentException>(() => roster.EnterBuilding(id, 73, 499, new GuardPoint3 { Y = 2 }));
                Check.Throws<ArgumentException>(() => roster.EnterBuilding(id, 74, 500, new GuardPoint3 { Y = 2 }));
                roster.EnterBuilding(id, 73, 500, new GuardPoint3 { Y = 2 });
                roster.ObserveInterior(id, new GuardPoint3 { X = 5, Y = 3 }, 0.5, 510, true, new GuardPoint3 { X = 8, Y = 3 });
                var copy = GuardRoster.CopyValidated(roster);
                Check.Equal(73, copy.Guards[0].Interior.BuildingKey);
                Check.Near(5, copy.InteriorPosition(id, 520).X); Check.Near(3, copy.InteriorPosition(id, 520).Y);
                Check.Near(810, copy.Guards[0].SearchUntil); Check.Near(0.5, copy.Guards[0].HealthFraction);
                Check.Near(100, copy.Position(id, 520).X);
            }));
            cases.Add(("Indoor guards retrace their path before starting exterior return", () => {
                var roster = Roster(1); var id = roster.Guards[0].Id;
                roster.CacheBuildingApproach(73, new SavedRoutePoint { X = 100 },
                    new Dictionary<string, List<SavedRoutePoint>> { { id, Route(0, 100) } });
                roster.DispatchBuilding("shop", 73, 0, 1, 2, 10);
                roster.EnterBuilding(id, 73, 500, new GuardPoint3());
                roster.ObserveInterior(id, new GuardPoint3 { X = 10 }, 1, 501, false, null);
                roster.Advance(800);
                Check.True(roster.Guards[0].Interior.Returning);
                Check.Near(10, roster.InteriorPosition(id, 800).X);
                double exit = roster.Guards[0].Interior.ExitAt;
                Check.Near(5, roster.InteriorPosition(id, (800 + exit) / 2).X);
                var restored = GuardRoster.CopyValidated(roster);
                restored.Advance(exit);
                Check.True(restored.Guards[0].Interior == null);
                Check.Equal(GuardDuty.Returning, restored.Guards[0].Duty);
                Check.Near(100, restored.Position(id, exit).X);
                restored.Advance(exit + 500);
                Check.Equal(GuardDuty.Posted, restored.Guards[0].Duty);
            }));
            cases.Add(("Indoor stand-down returns through the entrance before the original search deadline", () => {
                var roster = Roster(1); var id = roster.Guards[0].Id;
                roster.CacheBuildingApproach(73, new SavedRoutePoint { X = 100 },
                    new Dictionary<string, List<SavedRoutePoint>> { { id, Route(0, 100) } });
                roster.DispatchBuilding("shop", 73, 0, 1, 2, 10);
                roster.EnterBuilding(id, 73, 500, new GuardPoint3());
                roster.ObserveInterior(id, new GuardPoint3 { X = 10 }, 1, 501, true, new GuardPoint3 { X = 12 });
                roster.EndInteriorSearch(id, 502);
                Check.True(roster.Guards[0].Interior.Returning); Check.Near(502, roster.Guards[0].Interior.ReturnAt);
                Check.Near(10, GuardRoster.CopyValidated(roster).InteriorPosition(id, 502).X);
                Check.Throws<ArgumentException>(() => roster.ObserveInterior(id, new GuardPoint3(), 1, 503, true, new GuardPoint3()));
            }));
            cases.Add(("Late indoor materialization preserves deadline and death preserves current room position", () => {
                var roster = Roster(1); var id = roster.Guards[0].Id;
                roster.CacheBuildingApproach(73, new SavedRoutePoint { X = 100 },
                    new Dictionary<string, List<SavedRoutePoint>> { { id, Route(0, 100) } });
                roster.DispatchBuilding("shop", 73, 0, 1, 2, 10);
                roster.EnterBuilding(id, 73, 700, new GuardPoint3());
                Check.Near(800, roster.Guards[0].SearchUntil);
                roster.Guards[0].Interior.Position = new GuardPoint3 { X = 7, Y = 2, Z = 3 };
                roster.Kill(id, 701);
                var copy = GuardRoster.CopyValidated(roster);
                Check.Near(7, copy.InteriorPosition(id, 9999).X);
                Check.Near(2, copy.InteriorPosition(id, 9999).Y);
                Check.True(!copy.Guards[0].IsAlive);
            }));
            cases.Add(("Building responses retain destination and travel deadline through reload", () => {
                var roster = Roster(2);
                var routes = roster.Guards.ToDictionary(g => g.Id, g => Route(g.Post.X, 100));
                roster.CacheBuildingApproach(73, new SavedRoutePoint { X = 100 }, routes);
                routes[roster.Guards[0].Id][1].X = 999;
                var sent = roster.DispatchBuilding("shop", 73, 10, 1, 2, 10);
                Check.Equal(1, sent.Count); Check.Equal(roster.Guards[1].Id, sent[0].Id);
                Check.Near(460, sent[0].ArrivesAt);
                var copy = GuardRoster.CopyValidated(roster);
                Check.Equal(73, copy.Reports[0].BuildingKey.Value);
                Check.Near(100, copy.Reports[0].Entrance.X);
                Check.Near(60, copy.Position(sent[0].Id, 260).X);
                Check.Equal(1, copy.DispatchBuilding("shop", 73, 270, 4, 2, 10).Count);
                Check.Near(460, copy.Guards[1].ArrivesAt);
            }));
            cases.Add(("Building report cannot retarget or dispatch to mismatched door endpoints", () => {
                var roster = Roster(2);
                var routes = roster.Guards.ToDictionary(g => g.Id, g => Route(g.Post.X, 100));
                roster.CacheBuildingApproach(73, new SavedRoutePoint { X = 100 }, routes);
                roster.DispatchBuilding("shop", 73, 0, 1, 2, 10);
                Check.Throws<ArgumentException>(() => roster.DispatchBuilding("shop", 74, 1, 1, 2, 10));
                Check.Throws<ArgumentException>(() => roster.CacheBuildingApproach(74, new SavedRoutePoint { X = 101 }, routes));
                Check.Equal(1, roster.BuildingApproaches.Count); Check.Equal(1, roster.Reports.Count);
                var broken = GuardRoster.CopyValidated(roster); broken.Reports[0].Entrance.X = double.NaN;
                Check.Throws<ArgumentException>(() => GuardRoster.CopyValidated(broken));
            }));
            cases.Add(("Missing building route dispatches nobody and corrupt cached origins reject restore", () => {
                var roster = Roster(1);
                Check.Equal(0, roster.DispatchBuilding("unknown", 99, 0, 4, 2, 10).Count);
                Check.Equal(0, roster.Reports.Count);
                roster.CacheBuildingApproach(73, new SavedRoutePoint { X = 100 },
                    new Dictionary<string, List<SavedRoutePoint>> { { roster.Guards[0].Id, Route(0, 100) } });
                var copy = GuardRoster.CopyValidated(roster);
                copy.BuildingApproaches[0].Routes[0].Points[0].X = 50;
                Check.Throws<ArgumentException>(() => GuardRoster.CopyValidated(copy));
                Check.Equal(1, roster.DispatchBuilding("shop", 73, 0, 1, 2, 10).Count);
            }));
            cases.Add(("Recruited guard uses the saved post approach without reviving its predecessor", () => {
                var roster = Roster(1); string oldId = roster.Guards[0].Id;
                roster.CacheBuildingApproach(73, new SavedRoutePoint { X = 100 },
                    new Dictionary<string, List<SavedRoutePoint>> { { oldId, Route(0, 100) } });
                roster.Kill(oldId, 1); double now = 1 + 7 * 86400;
                Check.Equal(1, roster.Recruit(now));
                var restored = GuardRoster.CopyValidated(roster);
                var sent = restored.DispatchBuilding("later", 73, now, 4, 2, 10);
                Check.Equal(1, sent.Count); Check.True(sent[0].Id != oldId);
                Check.True(!restored.Guards[0].IsAlive); Check.Near(now + 500, sent[0].ArrivesAt);
                var legacy = Roster(1); legacy.BuildingApproaches = null;
                Check.Equal(0, GuardRoster.CopyValidated(legacy).BuildingApproaches.Count);
            }));
            cases.Add(("Guard engagement preserves actual position and wounded health through reload", () => {
                var roster = Roster(1);
                roster.Dispatch("fight", 10, new Dictionary<string, List<SavedRoutePoint>> { { roster.Guards[0].Id, Route(0, 50) } }, 1, 2, 12);
                roster.Engage(roster.Guards[0].Id, 20);
                roster.ObserveEngagement(roster.Guards[0].Id, new SavedRoutePoint { X = 4, Z = 3 }, 0.4, 30, true, new SavedRoutePoint { X = 9 });
                var saved = GuardRoster.CopyValidated(roster);
                Check.Equal(GuardDuty.Engaged, saved.Guards[0].Duty);
                Check.Equal(4d, saved.Position(saved.Guards[0].Id, 40).X);
                Check.Equal(0.4, saved.Guards[0].HealthFraction);
                Check.Equal(330d, saved.Guards[0].SearchUntil);
                saved.ObserveEngagement(saved.Guards[0].Id, new SavedRoutePoint { X = 5 }, 0.3, 50, false, new SavedRoutePoint { X = 99 });
                Check.Equal(9d, GuardRoster.CopyValidated(saved).Guards[0].LastSeenSuspect.X);
                Check.Equal(330d, saved.Guards[0].SearchUntil);
                Check.Equal(0.3, GuardRoster.CopyValidated(saved).Guards[0].HealthFraction);
            }));
            cases.Add(("Guard return starts from actual engagement position without teleporting", () => {
                var roster = Roster(1); string id = roster.Guards[0].Id;
                roster.Dispatch("fight", 10, new Dictionary<string, List<SavedRoutePoint>> { { id, Route(0, 50) } }, 1, 2, 12);
                roster.Engage(id, 20);
                roster.ObserveEngagement(id, new SavedRoutePoint { X = 30 }, 0.5, 30, false);
                Check.Throws<ArgumentException>(() => roster.ReturnFromEngagement(id, 40, Route(99, 0), 2, 12));
                Check.Equal(GuardDuty.Engaged, roster.Guards[0].Duty);
                roster.ReturnFromEngagement(id, 40, Route(30, 0), 2, 12);
                Check.Equal(30d, roster.Position(id, 40).X);
                Check.Equal(15d, roster.Position(id, 130).X);
                Check.Equal(GuardDuty.Returning, GuardRoster.CopyValidated(roster).Guards[0].Duty);
                roster.Advance(220);
                Check.Equal(GuardDuty.Posted, roster.Guards[0].Duty);
                Check.Equal(0.5, roster.Guards[0].HealthFraction);
            }));
            cases.Add(("Guard policy doubles each old settlement quota including floors rounding and cap", () => {
                Check.Equal(2, GuardRoster.TargetCount(SettlementScale.Hamlet, 12));
                Check.Equal(16, GuardRoster.TargetCount(SettlementScale.Town, 30));
                Check.Equal(32, GuardRoster.TargetCount(SettlementScale.City, 80));
                Check.Equal(160, GuardRoster.TargetCount(SettlementScale.City, 1000));
                Check.Equal(2, GuardRoster.TargetCount(SettlementScale.Hamlet, 39));
                Check.Equal(16, GuardRoster.TargetCount(SettlementScale.Hamlet, 40));
                Check.Equal(30, GuardRoster.TargetCount(SettlementScale.Town, 181));
                Check.Equal(48, GuardRoster.TargetCount(SettlementScale.City, 290));
                Check.Equal(700, GuardRoster.TargetCount(SettlementScale.City, int.MaxValue));
            }));
            cases.Add(("Gate slots are extra for every settlement size and survive the base cap", () => {
                foreach (SettlementScale scale in Enum.GetValues(typeof(SettlementScale)))
                    foreach (int population in new[] { 0, 12, 39, 40, 181, 290, 1000, int.MaxValue })
                    {
                        int ordinary = GuardRoster.TargetCount(scale, population);
                        Check.Equal(2 * Math.Max(3, ordinary), GuardRoster.TargetWithGateSlots(ordinary, 0));
                        Check.Equal(2 * (Math.Max(3, ordinary) + 16), GuardRoster.TargetWithGateSlots(ordinary, 4));
                    }
                Check.Equal(128, GuardRoster.TargetWithGateSlots(GuardRoster.TargetCount(SettlementScale.City, 290), 4));
                Check.Throws<ArgumentException>(() => GuardRoster.TargetWithGateSlots(700, -1));
                Check.Throws<ArgumentException>(() => GuardRoster.TargetWithGateSlots(700, GuardRoster.MaximumGateCount + 1));
            }));
            cases.Add(("Old guard roster expansion preserves deaths injuries and identities without repeated growth", () => {
                var roster = Roster(24); string dead = roster.Guards[0].Id, injured = roster.Guards[1].Id;
                roster.Kill(dead, 100); roster.Guards[1].HealthFraction = 0.4;
                var posts = Enumerable.Range(0, 128).Select(i => new SavedRoutePoint { X = i * 10 }).ToList();
                int target = GuardRoster.TargetWithGateSlots(GuardRoster.TargetCount(SettlementScale.City, 290), 4);
                Check.Equal(104, roster.IncreaseTarget(target, posts));
                var options = new JsonSerializerOptions { IncludeFields = true };
                var restored = GuardRoster.CopyValidated(JsonSerializer.Deserialize<GuardRoster>(JsonSerializer.Serialize(roster, options), options));
                Check.Equal(0, restored.IncreaseTarget(target, posts));
                Check.Equal(128, restored.Target); Check.Equal(128, restored.Guards.Count);
                Check.Equal(127, restored.Guards.Count(g => g.IsAlive));
                Check.True(!restored.Guards.Find(g => g.Id == dead).IsAlive);
                Check.Near(0.4, restored.Guards.Find(g => g.Id == injured).HealthFraction);
                Check.Equal(128, restored.Guards.Select(g => g.Id).Distinct().Count());
            }));
            cases.Add(("Large doubled roster with gate extras validates and restores above old cap", () => {
                int target = GuardRoster.TargetWithGateSlots(GuardRoster.TargetCount(SettlementScale.City, int.MaxValue), 4);
                var roster = Roster(target);
                Check.Equal(1432, GuardRoster.CopyValidated(roster).Target);
                Check.Throws<ArgumentException>(() => Roster(GuardRoster.MaximumTarget + 1));
            }));
            cases.Add(("Roster has permanent unique deterministic identities", () => {
                var a = Roster(); var b = Roster();
                Check.Equal(4, a.Guards.Count); Check.Equal(4, a.Guards.Select(g => g.Id).Distinct().Count());
                Check.Equal(string.Join(",", a.Guards.Select(g => g.Id)), string.Join(",", b.Guards.Select(g => g.Id)));
            }));
            cases.Add(("Dispatch picks nearest reachable available guards and deduplicates a report", () => {
                var roster = Roster();
                var routes = roster.Guards.ToDictionary(g => g.Id, g => Route(g.Post.X, 100));
                var sent = roster.Dispatch("theft-1", 0, routes, 2, 2, 12);
                Check.Equal(2, sent.Count); Check.Equal(roster.Guards[3].Id, sent[0].Id);
                Check.Near(420, sent[0].ArrivesAt);
                Check.Equal(2, roster.Dispatch("theft-1", 1, routes, 4, 2, 12).Count);
                Check.Equal(2, roster.Guards.Count(g => g.Duty == GuardDuty.Responding));
                var second = roster.Dispatch("theft-2", 1, routes, 4, 2, 12);
                Check.Equal(2, second.Count);
                Check.Equal(0, roster.Dispatch("theft-3", 2, routes, 4, 2, 12).Count);
            }));
            cases.Add(("Guard location advances with game time, searches, then returns along its route", () => {
                var roster = Roster(1); var guard = roster.Guards[0];
                roster.Dispatch("alarm", 100, new Dictionary<string, List<SavedRoutePoint>> { { guard.Id, Route(0, 100) } }, 1, 2, 10);
                Check.Near(50, roster.Position(guard.Id, 350).X);
                roster.Advance(600); Check.Equal(GuardDuty.Searching, guard.Duty);
                roster.Advance(900); Check.Equal(GuardDuty.Returning, guard.Duty);
                Check.Near(50, roster.Position(guard.Id, 1150).X);
                roster.Advance(1400); Check.Equal(GuardDuty.Posted, guard.Duty); Check.Near(0, roster.Position(guard.Id, 1400).X);
                Check.Equal(1, roster.Dispatch("alarm", 1500, new Dictionary<string, List<SavedRoutePoint>>(), 4, 2, 10).Count);
                Check.Equal(GuardDuty.Posted, guard.Duty);
            }));
            cases.Add(("Guard deaths persist and recruitment is delayed, bounded and creates new identities", () => {
                var roster = Roster(); var ids = roster.Guards.Select(g => g.Id).ToArray();
                foreach (var id in ids) roster.Kill(id, 100);
                Check.Equal(0, roster.Recruit(100 + 7 * 86400 - 1));
                Check.Equal(1, roster.Recruit(100 + 7 * 86400));
                Check.Equal(0, roster.Recruit(100 + 7 * 86400));
                Check.Equal(1, roster.Recruit(100 + 8 * 86400));
                Check.True(roster.Guards.Where(g => g.IsAlive).All(g => !ids.Contains(g.Id)));
                Check.Equal(4, roster.Guards.Count(g => !g.IsAlive));
            }));
            cases.Add(("Missing routes and dead guards never create reinforcements", () => {
                var roster = Roster(); roster.Kill(roster.Guards[0].Id, 0);
                var routes = new Dictionary<string, List<SavedRoutePoint>> { { roster.Guards[0].Id, Route(0, 5) } };
                Check.Equal(0, roster.Dispatch("empty", 1, routes, 4, 2, 12).Count);
                Check.Equal(4, roster.Guards.Count);
            }));
            cases.Add(("A guard killed mid-response remains at the death location", () => {
                var roster = Roster(1); var id = roster.Guards[0].Id;
                roster.Dispatch("attack", 0, new Dictionary<string, List<SavedRoutePoint>> { { id, Route(0, 100) } }, 1, 2, 10);
                roster.Kill(id, 250);
                Check.Near(50, roster.Position(id, 1000).X);
                Check.Near(50, GuardRoster.CopyValidated(roster).Position(id, 2000).X);
            }));
            cases.Add(("Guard restore rejects impossible search timers and route endpoints", () => {
                var roster = Roster(1); var id = roster.Guards[0].Id;
                roster.Dispatch("alarm", 100, new Dictionary<string, List<SavedRoutePoint>> { { id, Route(0, 100) } }, 1, 2, 10);
                roster.Guards[0].SearchUntil = 599;
                Check.Throws<ArgumentException>(() => GuardRoster.CopyValidated(roster));
                roster.Guards[0].SearchUntil = 900; roster.Guards[0].Route[0].X = 20;
                Check.Throws<ArgumentException>(() => GuardRoster.CopyValidated(roster));
            }));
            cases.Add(("Bad guard routes reject transactionally without partial dispatch", () => {
                var roster = Roster();
                var routes = roster.Guards.ToDictionary(g => g.Id, g => Route(g.Post.X, 100));
                routes[roster.Guards[3].Id][1].X = double.NaN;
                Check.Throws<ArgumentException>(() => roster.Dispatch("bad", 0, routes, 4, 2, 12));
                Check.True(roster.Guards.All(g => g.Duty == GuardDuty.Posted));
                Check.Equal(0, roster.Reports.Count);
            }));
            cases.Add(("Invalid route origin does not advance another guard before rejecting", () => {
                var roster = Roster(2); var first = roster.Guards[0]; var second = roster.Guards[1];
                roster.Dispatch("valid", 0, new Dictionary<string, List<SavedRoutePoint>> { { first.Id, Route(0, 100) } }, 1, 2, 10);
                Check.Throws<ArgumentException>(() => roster.Dispatch("invalid", 600,
                    new Dictionary<string, List<SavedRoutePoint>> { { second.Id, Route(999, 100) } }, 1, 2, 10));
                Check.Equal(GuardDuty.Responding, first.Duty); Check.Equal(1, roster.Reports.Count);
            }));
            cases.Add(("A pending report can use newly reachable guards without duplicating responders", () => {
                var roster = Roster(1); var guard = roster.Guards[0];
                Check.Equal(0, roster.Dispatch("pending", 0, new Dictionary<string, List<SavedRoutePoint>>(), 1, 2, 10).Count);
                var route = new Dictionary<string, List<SavedRoutePoint>> { { guard.Id, Route(0, 100) } };
                Check.Equal(1, roster.Dispatch("pending", 10, route, 1, 2, 10).Count);
                Check.Equal(1, roster.Dispatch("pending", 20, route, 1, 2, 10).Count);
                Check.Equal(1, roster.Reports.Count); Check.Equal(1, roster.Guards.Count);
            }));
            cases.Add(("Guard roster snapshot preserves journeys, deaths and exhausted report identities", () => {
                var store = new WorldStateStore(42); store.GetOrCreate(1, new[] { 1 });
                var roster = store.GetOrCreateGuards(1, 4, Enumerable.Range(0, 4).Select(i => new SavedRoutePoint { X = i * 10 }).ToList());
                var routes = roster.Guards.ToDictionary(g => g.Id, g => Route(g.Post.X, 100));
                roster.Dispatch("saved", 100, routes, 2, 2, 12); roster.Kill(roster.Guards[0].Id, 150);
                var options = new JsonSerializerOptions { IncludeFields = true };
                string json = JsonSerializer.Serialize(store.Snapshot(), options);
                var restored = new WorldStateStore(0); restored.Restore(JsonSerializer.Deserialize<WorldState>(json, options));
                Check.Equal(json, JsonSerializer.Serialize(restored.Snapshot(), options));
                var same = restored.GetOrCreateGuards(1, 99, new List<SavedRoutePoint>());
                Check.Equal(4, same.Guards.Count); Check.Equal(2, same.Dispatch("saved", 160, routes, 4, 2, 12).Count);
                var broken = restored.Snapshot(); broken.Garrisons[0].Guards[1].Id = broken.Garrisons[0].Guards[0].Id;
                Check.Throws<ArgumentException>(() => restored.Restore(broken));
                Check.Equal(json, JsonSerializer.Serialize(restored.Snapshot(), options));
            }));
        }
    }
}
