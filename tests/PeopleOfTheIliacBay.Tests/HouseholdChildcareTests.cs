using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class HouseholdChildcareTests
    {
        private static DailySchedule BriefOuting(double start,double end)
        {return DailySchedule.PointOutingDay(7,new[]{new RoutePoint(0,0),new RoutePoint(1,0)},
            new[]{new RoutePoint(1,0),new RoutePoint(0,0)},new RoutePoint(1,0),start,start+(end-start)/3,
            start+2*(end-start)/3,end,360,1380);}
        private static Household Family()
        {return new Household{BuildingKey=7,Residents=new List<Resident>{
            new Resident{Id="mother",HomeKey=7,AgeGroup=ResidentAgeGroup.Adult},
            new Resident{Id="father",HomeKey=7,AgeGroup=ResidentAgeGroup.Adult},
            new Resident{Id="child",HomeKey=7,AgeGroup=ResidentAgeGroup.Child,MotherId="mother",FatherId="father"}}};}
        public static void Add(List<(string,Action)> cases)
        {
            cases.Add(("Persisted legacy parent route cannot masquerade as a stale home schedule",(Action)(()=>{
                var store=new WorldStateStore(13);var town=store.GetOrCreate(1,new[]{7,8},1);
                var parent=town.Households[0].Residents.Find(p=>p.AgeGroup==ResidentAgeGroup.Adult);
                store.SetRoutines(new TownRoutineState{SettlementId=1,Residents=new List<ResidentRoutine>{new ResidentRoutine{
                    ResidentId=parent.Id,DestinationKey=8,OpenMinute=480,CloseMinute=1080,
                    Route=new List<SavedRoutePoint>{new SavedRoutePoint(),new SavedRoutePoint{X=100}}}}});
                var read=store.GetResidentDialogueRoutine(1,parent.Id);var points=new List<RoutePoint>();
                foreach(var p in read.Route)points.Add(new RoutePoint(p.X,p.Z));
                var latest=DailySchedule.DayVisit(7,8,points,480,1080,60,1,1,0,360,1320);
                var family=Family();family.Residents[1].Id=parent.Id;family.Residents[2].FatherId=parent.Id;
                Check.True(!HouseholdChildcare.CanTakeOptionalOuting(family,"mother",6*3600,22*3600,p=>latest),
                    "a route omitted by the read adapter invented the parent's home presence");
                Check.Equal(2,read.Route.Count);read.Route[0].X=999;
                Check.Near(0,store.GetResidentDialogueRoutine(1,parent.Id).Route[0].X);
            })));
            cases.Add(("Observed visit projection blocks childcare through the shifted return tail",(Action)(()=>{
                var visit=new ObservedVisit{ResidentId="father",SettlementId=1,BuildingKey=8,ObservedAt=100,
                    PlannedEndAt=110,ResumeUntil=200,SecondsPerMetre=1,
                    Legs=new List<ObservedVisitLeg>{new ObservedVisitLeg{AfterPhase=ShopVisitPhase.Leaving,
                        Route=new List<GuardPoint3>{new GuardPoint3(),new GuardPoint3{X=20}}}}};
                Check.Near(120,visit.EndsAt);
                Check.True(visit.AffectsRoutineDuring(180,205),"a finished visit can still delay the parent's return");
                Check.True(!visit.AffectsRoutineDuring(210,230));
                Check.True(!visit.AffectsRoutineDuring(90,100));
            })));
            cases.Add(("Home coverage detects a subsecond outing between two home observations",(Action)(()=>{
                var schedule=BriefOuting(36000.1,36000.4);
                Check.True(!schedule.IsHomeThroughout(35000,37000));
                Check.True(schedule.IsHomeThroughout(35000,36000.1));
                Check.True(schedule.IsHomeThroughout(36000.4,37000));
            })));
            cases.Add(("Home coverage checks later composed outings and the following day",(Action)(()=>{
                var schedule=DailySchedule.OutingsDay(7,new[]{BriefOuting(9*3600,10*3600),BriefOuting(17*3600,18*3600)},360,1380);
                Check.True(schedule.IsHomeThroughout(10*3600,17*3600));
                Check.True(!schedule.IsHomeThroughout(16*3600,19*3600));
                Check.True(schedule.IsHomeThroughout(23*3600,86400+9*3600));
                Check.True(!schedule.IsHomeThroughout(23*3600,86400+9*3600+0.2));
            })));
            cases.Add(("Home coverage treats on-site work as presence and external commute as absence",(Action)(()=>{
                var home=DailySchedule.Workday(7,7,new RoutePoint[0],480,1020,0,360,1380);
                Check.True(home.IsHomeThroughout(0,86400));
                var work=DailySchedule.Workday(7,8,new[]{new RoutePoint(0,0),new RoutePoint(1,0)},480,1020,30,360,1380);
                Check.True(work.IsHomeThroughout(0,450*60));
                Check.True(!work.IsHomeThroughout(450*60,480*60));
                Check.True(work.IsHomeThroughout(1050*60,86400));
            })));
            cases.Add(("Home coverage respects an absolute recovery origin and restores recurring days afterwards",(Action)(()=>{
                var ordinary=DailySchedule.HomeDay(7,360,1380);
                var recovery=new RoutineRecoveryPlan{ResidentId="father",RoutineFingerprint="routine",AppointmentFingerprint="appointments",
                    SettlementId=1,AbsoluteDay=0,HomeBuildingKey=7,OriginBuildingKey=8,WakeMinute=360,SleepMinute=1380,
                    CreatedAt=8*3600,DepartAt=10*3600,EndsAt=11*3600,
                    Legs=new List<RoutineRecoveryLeg>{new RoutineRecoveryLeg{BuildingKey=7,RouteNodeKey=7,ReturnsHome=true,
                        WalkAt=10*3600,ArriveAt=11*3600,EnterAt=11*3600,LeaveAt=11*3600,
                        Route=new List<SavedRoutePoint>{new SavedRoutePoint{X=1},new SavedRoutePoint()}}}};
                var schedule=DailySchedule.ContinueDay(ordinary,recovery);
                Check.True(schedule.IsHomeThroughout(7*3600,8*3600));
                Check.True(!schedule.IsHomeThroughout(7*3600,9*3600));
                Check.True(schedule.IsHomeThroughout(11*3600,12*3600));
                Check.True(schedule.IsHomeThroughout(86400+7*3600,86400+12*3600));
            })));
            cases.Add(("Childcare uses the actual other parent throughout the whole optional outing",(Action)(()=>{
                var family=Family();var home=DailySchedule.HomeDay(7,360,1380);var away=BriefOuting(36000.1,36000.4);
                Check.True(HouseholdChildcare.CanTakeOptionalOuting(family,"mother",35000,37000,p=>home));
                Check.True(!HouseholdChildcare.CanTakeOptionalOuting(family,"mother",35000,37000,p=>away));
                family.Residents.Add(new Resident{Id="unrelated",HomeKey=7,AgeGroup=ResidentAgeGroup.Adult});
                Check.True(!HouseholdChildcare.CanTakeOptionalOuting(family,"mother",35000,37000,p=>p.Id=="father"?away:home));
                Check.True(!HouseholdChildcare.CanTakeOptionalOuting(family,"mother",35000,37000,p=>null));
                Check.True(!HouseholdChildcare.CanTakeOptionalOuting(family,"mother",35000,37000,
                    p=>DailySchedule.HomeDay(8,360,1380)),"presence at another home is not childcare here");
                family.Residents[1].IsAlive=false;
                Check.True(!HouseholdChildcare.CanTakeOptionalOuting(family,"mother",35000,37000,p=>home));
                Check.True(HouseholdChildcare.CanTakeOptionalOuting(family,"unrelated",35000,37000,p=>away));
                family.Residents[2].IsAlive=false;
                Check.True(HouseholdChildcare.CanTakeOptionalOuting(family,"mother",35000,37000,p=>away));
            })));
            cases.Add(("Home coverage rejects invalid or unbounded intervals",(Action)(()=>{
                var home=DailySchedule.HomeDay(7,360,1380);
                Check.Throws<ArgumentOutOfRangeException>(()=>home.IsHomeThroughout(double.NaN,10));
                Check.Throws<ArgumentOutOfRangeException>(()=>home.IsHomeThroughout(10,10));
                Check.Throws<ArgumentOutOfRangeException>(()=>home.IsHomeThroughout(0,86401));
            })));
        }
    }
}
