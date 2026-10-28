using System;
using System.Collections.Generic;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class HouseholdDayPlanTests
    {
        private static Household Family()
        {return new Household{BuildingKey=7,HeadId="mother",Surname="Example",BedCapacity=3,Residents=new List<Resident>{
            new Resident{Id="mother",FullName="Mother Example",BedSlot=0,HomeKey=7,Sex=ResidentSex.Female,PartnerId="father"},
            new Resident{Id="father",FullName="Father Example",BedSlot=1,HomeKey=7,PartnerId="mother"},
            new Resident{Id="child",FullName="Child Example",BedSlot=2,HomeKey=7,AgeGroup=ResidentAgeGroup.Child,MotherId="mother",FatherId="father"}}};}
        internal static ResidentDestination Visit(int building,double walk=300,double dwell=1800,int close=1380)
        {return new ResidentDestination{Stops=new List<ResidentVisitStop>{new ResidentVisitStop{
            BuildingKey=building,Purpose=ResidentVisitPurpose.Shop,OpenMinute=480,CloseMinute=close,
            WalkSeconds=walk,DwellSeconds=dwell,Route=new List<SavedRoutePoint>{new SavedRoutePoint(),new SavedRoutePoint{X=building}}}},
            ExitWalkSeconds=walk,ExitRoute=new List<SavedRoutePoint>{new SavedRoutePoint{X=building},new SavedRoutePoint()}};}
        private static ResidentRoutine Routine(string id)
        {return new ResidentRoutine{ResidentId=id,DestinationKey=8,OpenMinute=480,CloseMinute=1380,
            Route=new List<SavedRoutePoint>{new SavedRoutePoint(),new SavedRoutePoint{X=8}},
            ItineraryDepartureSeconds=9*3600,Itinerary=Visit(8),
            AdditionalOutings=new List<ResidentDailyOuting>{new ResidentDailyOuting{DepartureSeconds=15*3600,Itinerary=Visit(9)}}};}
        internal static HouseholdDayRequest Request(int day=3)
        {return new HouseholdDayRequest{Household=Family(),SettlementId=2,AbsoluteDay=day,WorldSeed=21,
            WakeMinute=360,SleepMinute=1380,PlannedAt=day*86400d+8*3600,
            FirstRoutine=Routine("mother"),SecondRoutine=Routine("father")};}
        internal static HouseholdDayPlan Create(HouseholdDayRequest request)
        {Check.True(HouseholdDayPlanner.TryCreate(request,out var plan,out var reason),reason);return plan;}
        internal static WorldStateStore Store(HouseholdDayRequest request)
        {
            var world=new WorldState{WorldSeed=request.WorldSeed};
            world.Settlements.Add(new SettlementPopulation{SettlementId=2,Households=new List<Household>{request.Household}});
            world.Routines.Add(new TownRoutineState{SettlementId=2,ItineraryVersion=2,
                Residents=new List<ResidentRoutine>{request.FirstRoutine,request.SecondRoutine}});
            var store=new WorldStateStore(0);store.Restore(world);return store;
        }
        internal static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Stored planned family reserves both routine sources before native cache activation",()=>{
                var request=Request();var store=Store(request);var plan=Create(request);
                Check.True(!store.HasPlannedHouseholdDay(2,"mother",3));
                Check.True(store.TryPublishHouseholdDay(plan,request.PlannedAt,out var reason),reason);
                Check.True(store.HasPlannedHouseholdDay(2,"mother",3));
                Check.True(store.HasPlannedHouseholdDay(2,"father",3));
                Check.True(!store.HasPlannedHouseholdDay(2,"child",3));
                Check.True(!store.HasPlannedHouseholdDay(3,"mother",3));
                Check.True(!store.HasPlannedHouseholdDay(2,"mother",4));
                Check.True(store.CancelHouseholdDay(plan.Id,request.PlannedAt+1));
                Check.True(!store.HasPlannedHouseholdDay(2,"mother",3));
            }));
            cases.Add(("Family route projection keeps shifted departure and original visit indexes after omissions",()=>{
                var request=HouseholdDayAppointmentTests.SharedRequest();var plan=Create(request);
                var father=HouseholdDayPlanner.RoutesFor(plan,"father",request.SecondRoutine);
                Check.Near(plan.Second.Outings[0].DepartureSeconds,father[0].Outing.DepartureSeconds);
                Check.Near(9*3600,father[0].Outing.DepartureSeconds);Check.Equal(0,father[0].FirstVisitIndex);
                plan.First.Outings.RemoveAt(0);var routes=HouseholdDayPlanner.RoutesFor(plan,"mother",request.FirstRoutine);
                Check.Equal(2,routes.Count);Check.Equal(1,routes[0].FirstVisitIndex);
                Check.Equal(HouseholdDayPlanner.JointVisitIndex,routes[1].FirstVisitIndex);
                Check.Equal(10,routes[1].Outing.Itinerary.Stops[0].BuildingKey);
            }));
            cases.Add(("Family route projection owns its paths and refuses cancelled or stale sources",()=>{
                var request=HouseholdDayAppointmentTests.SharedRequest();var plan=Create(request);
                var routes=HouseholdDayPlanner.RoutesFor(plan,"mother",request.FirstRoutine);
                routes[0].Outing.Itinerary.Stops[0].Route[0].X=99;
                routes[routes.Count-1].Outing.Itinerary.ExitRoute[0].X=99;
                Check.Near(0,request.FirstRoutine.Itinerary.Stops[0].Route[0].X);
                Check.Near(10,plan.JointInn.First.Itinerary.ExitRoute[0].X);
                plan.Status="cancelled";Check.Throws<ArgumentException>(()=>HouseholdDayPlanner.RoutesFor(plan,"mother",request.FirstRoutine));
                plan.Status="planned";request.FirstRoutine.ItineraryDepartureSeconds+=1;
                Check.Throws<ArgumentException>(()=>HouseholdDayPlanner.RoutesFor(plan,"mother",request.FirstRoutine));
            }));
            cases.Add(("Dated schedule timed metadata drops expired recovery visits on neighboring days",()=>{
                var ordinary=ResidentRoutineDay.Schedule(7,Routine("mother"),360,1380);
                var continuation=new RoutineRecoveryPlan{ResidentId="mother",RoutineFingerprint="routine",AppointmentFingerprint="appointments",
                    SettlementId=2,AbsoluteDay=0,HomeBuildingKey=7,OriginBuildingKey=8,WakeMinute=360,SleepMinute=1380,
                    CreatedAt=8*3600,DepartAt=9*3600,EndsAt=11*3600,Legs=new List<RoutineRecoveryLeg>{
                        new RoutineRecoveryLeg{BuildingKey=10,OriginalStopIndex=0,WalkAt=9*3600,ArriveAt=10*3600,
                            EnterAt=10*3600,LeaveAt=10.5*3600,Route=new List<SavedRoutePoint>{new SavedRoutePoint{X=8},new SavedRoutePoint{X=10}}},
                        new RoutineRecoveryLeg{BuildingKey=7,ReturnsHome=true,WalkAt=10.5*3600,ArriveAt=11*3600,
                            EnterAt=11*3600,LeaveAt=11*3600,Route=new List<SavedRoutePoint>{new SavedRoutePoint{X=10},new SavedRoutePoint()}}}};
                var recovery=DailySchedule.ContinueDay(ordinary,continuation);
                var dated=DailySchedule.ForDay(recovery,DailySchedule.HomeDay(7,360,1380),1);
                double nextDay=2*86400+9*3600+600;
                Check.Equal(8,dated.EvaluateSeconds(nextDay).BuildingKey);
                Check.Equal(2,dated.ScheduledVisits(nextDay).Count);
                Check.True(dated.ContainsVisitBuilding(8,nextDay));Check.True(!dated.ContainsVisitBuilding(10,nextDay));
            }));
            cases.Add(("Household day publishes both parents atomically and resolves only its exact date",()=>{
                var request=Request();var plan=Create(request);var store=Store(request);
                Check.True(store.TryPublishHouseholdDay(plan,request.PlannedAt,out var reason),reason);
                Check.True(store.TryHouseholdDaySchedules(2,7,3,out var first,out var second,out reason),reason);
                Check.Equal(2,first.ScheduledVisits().Count);Check.Equal(2,second.ScheduledVisits().Count);
                Check.True(!store.TryHouseholdDaySchedules(2,7,4,out first,out second,out reason));
                var options=new JsonSerializerOptions{IncludeFields=true};var saved=store.Snapshot();
                var restored=new WorldStateStore(0);restored.Restore(JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(saved,options),options));
                Check.True(restored.TryHouseholdDaySchedules(2,7,3,out first,out second,out reason),reason);
                plan.First.Outings.Clear();Check.Equal(2,restored.ReadHouseholdDay(2,7,3).First.Outings.Count);
                var read=store.ReadHouseholdDay(2,7,3);read.Second.Outings.Clear();
                Check.Equal(2,store.ReadHouseholdDay(2,7,3).Second.Outings.Count);
            }));
            cases.Add(("Bad second member leaves household plan and both recurring routines untouched",()=>{
                var request=Request();var store=Store(request);var plan=Create(request);
                plan.Second.Outings[0].SourceOutingIndex=4;
                Check.True(!store.TryPublishHouseholdDay(plan,request.PlannedAt,out var reason));
                Check.True(store.ReadHouseholdDay(2,7,3)==null);
                var town=store.GetRoutines(2);Check.Near(9*3600,town.Residents[0].ItineraryDepartureSeconds);
                Check.Near(9*3600,town.Residents[1].ItineraryDepartureSeconds);
                Check.Equal(0,store.Snapshot().HouseholdDays.Count);
            }));
            cases.Add(("Household day retries cannot reroll or reactivate cancelled plans",()=>{
                var request=Request();var store=Store(request);var plan=Create(request);
                Check.True(store.TryPublishHouseholdDay(plan,request.PlannedAt,out var reason));
                int revision=store.HouseholdDayRevision;
                Check.True(store.TryPublishHouseholdDay(plan,request.PlannedAt+1,out reason));Check.Equal(revision,store.HouseholdDayRevision);
                var changed=HouseholdDayPlan.Copy(plan);changed.First.Outings.Clear();
                Check.True(!store.TryPublishHouseholdDay(changed,request.PlannedAt+1,out reason));
                Check.True(store.CancelHouseholdDay(plan.Id,request.PlannedAt+2));
                Check.True(!store.TryPublishHouseholdDay(plan,request.PlannedAt+3,out reason));
                Check.True(!store.TryHouseholdDaySchedules(2,7,3,out var first,out var second,out reason));
                Check.Equal("cancelled",store.ReadHouseholdDay(2,7,3).Status);
            }));
            cases.Add(("Dated household storage is bounded and cannot replace an unfinished day",()=>{
                var request=Request();var store=Store(request);var first=Create(request);
                Check.True(store.TryPublishHouseholdDay(first,request.PlannedAt,out var reason));
                var tomorrow=Request(4);var second=Create(tomorrow);
                Check.True(!store.TryPublishHouseholdDay(second,request.PlannedAt+1,out reason));
                Check.True(store.TryPublishHouseholdDay(second,tomorrow.PlannedAt,out reason),reason);
                Check.Equal(1,store.Snapshot().HouseholdDays.Count);Check.True(store.ReadHouseholdDay(2,7,3)==null);
                Check.True(!store.TryPublishHouseholdDay(first,tomorrow.PlannedAt+1,out reason));
            }));
            cases.Add(("Changing either routine invalidates the whole dated household plan without altering source history",()=>{
                var request=Request();var store=Store(request);var plan=Create(request);
                Check.True(store.TryPublishHouseholdDay(plan,request.PlannedAt,out var reason));
                var routine=store.GetRoutines(2).Residents.Find(r=>r.ResidentId=="father");
                routine.ItineraryDepartureSeconds+=1;store.SetResidentRoutine(2,routine);
                Check.True(!store.TryPublishHouseholdDay(plan,request.PlannedAt+1,out reason));
                Check.True(!store.TryHouseholdDaySchedules(2,7,3,out var first,out var second,out reason));
                store.Restore(store.Snapshot());
                Check.True(!store.TryHouseholdDaySchedules(2,7,3,out first,out second,out reason));
                Check.Equal(plan.Id,store.ReadHouseholdDay(2,7,3).Id);
            }));
            cases.Add(("Delayed household publication cannot invent active or already completed shifted trips",()=>{
                var request=Request();request.IsAvailable=(id,start,end)=>id!="mother" || start>=3*86400d+11*3600;
                var store=Store(request);var plan=Create(request);
                Check.True(!store.TryPublishHouseholdDay(plan,3*86400d+11*3600+900,out var reason));
                Check.True(store.ReadHouseholdDay(2,7,3)==null);
                Check.True(!store.TryPublishHouseholdDay(plan,3*86400d+12*3600,out reason));
                Check.True(store.ReadHouseholdDay(2,7,3)==null);
            }));
            cases.Add(("Rejected replacement and malformed restore retain the previously published household day",()=>{
                var request=Request();var store=Store(request);var plan=Create(request);
                Check.True(store.TryPublishHouseholdDay(plan,request.PlannedAt,out var reason));
                var tomorrow=Request(4);var bad=Create(tomorrow);bad.Second.Outings[0].SourceOutingIndex=4;
                Check.True(!store.TryPublishHouseholdDay(bad,tomorrow.PlannedAt,out reason));
                Check.Equal(plan.Id,store.ReadHouseholdDay(2,7,3).Id);
                var corrupted=store.Snapshot();corrupted.HouseholdDays[0].Second.Outings[0].SourceOutingIndex=4;
                Check.Throws<ArgumentException>(()=>store.Restore(corrupted));
                Check.True(store.TryHouseholdDaySchedules(2,7,3,out var first,out var second,out reason),reason);
            }));
            cases.Add(("Household date restore rejects duplicate day records",()=>{
                var request=Request();var store=Store(request);var saved=store.Snapshot();
                var restored=new WorldStateStore(0);restored.Restore(saved);
                saved.HouseholdDays.Add(Create(request));
                saved.HouseholdDays.Add(HouseholdDayPlan.Copy(saved.HouseholdDays[0]));
                Check.Throws<ArgumentException>(()=>restored.Restore(saved));
            }));
            cases.Add(("Dated family day alternates full parental absences and preserves source routes",()=>{
                var request=Request();var plan=Create(request);double day=3*86400d;
                var first=HouseholdDayPlanner.Schedule(plan,"mother",request.FirstRoutine);
                var second=HouseholdDayPlanner.Schedule(plan,"father",request.SecondRoutine);
                Check.Equal(2,first.ScheduledVisits().Count);Check.Equal(2,second.ScheduledVisits().Count);
                foreach(var member in new[]{plan.First,plan.Second})foreach(var outing in member.Outings)
                {
                    var routine=member.ResidentId=="mother"?request.FirstRoutine:request.SecondRoutine;
                    var other=member.ResidentId=="mother"?second:first;
                    var original=ResidentRoutineDay.CopyOutings(routine)[outing.SourceOutingIndex];
                    double end=ResidentVisitJourney.Create(outing.DepartureSeconds,original.Itinerary).EndsAt;
                    Check.True(other.IsHomeThroughout(day+outing.DepartureSeconds,day+end));
                }
                Check.Near(9*3600,request.FirstRoutine.ItineraryDepartureSeconds);
                Check.Near(9*3600,request.SecondRoutine.ItineraryDepartureSeconds);
                Check.Equal(0,first.ScheduledVisits()[0].VisitIndex);Check.Equal(1,first.ScheduledVisits()[1].VisitIndex);
            }));
            cases.Add(("Family day drops an infeasible optional outing rather than violating service hours",()=>{
                var request=Request();request.FirstRoutine.Itinerary.Stops[0].CloseMinute=580;
                request.SecondRoutine.Itinerary.Stops[0].CloseMinute=580;
                var plan=Create(request);
                Check.Equal(3,plan.First.Outings.Count+plan.Second.Outings.Count);
                foreach(var member in new[]{plan.First,plan.Second})
                {
                    var routine=member.ResidentId=="mother"?request.FirstRoutine:request.SecondRoutine;
                    var schedule=HouseholdDayPlanner.Schedule(plan,member.ResidentId,routine);
                    Check.Equal(1,schedule.ScheduledVisits()[schedule.ScheduledVisits().Count-1].VisitIndex);
                }
            }));
            cases.Add(("Dated household planning preserves completed history and refuses an active outing",()=>{
                var request=Request();request.PlannedAt=3*86400d+12*3600;
                var plan=Create(request);
                Check.Near(9*3600,plan.First.Outings[0].DepartureSeconds);Check.Near(9*3600,plan.Second.Outings[0].DepartureSeconds);
                request.PlannedAt=3*86400d+9*3600+10;
                Check.True(!HouseholdDayPlanner.TryCreate(request,out var ignored,out var reason));
                Check.True(reason.Contains("active"),reason);
            }));
            cases.Add(("Family day respects external duties and actual child links",()=>{
                var request=Request();request.IsAvailable=(id,start,end)=>id!="mother" || start>=3*86400d+11*3600;
                var plan=Create(request);
                Check.True(plan.First.Outings[0].DepartureSeconds>=11*3600);
                request=Request();request.Household.Residents[1].PartnerId="stranger";
                Check.True(!HouseholdDayPlanner.TryCreate(request,out var ignored,out var reason));
                request=Request();request.Household.Residents[2].FatherId="stranger";
                Check.True(!HouseholdDayPlanner.TryCreate(request,out ignored,out reason));
            }));
            cases.Add(("Duty-delayed family outings keep original visit order and past social history",()=>{
                var request=Request();request.FirstRoutine.AdditionalOutings[0].DepartureSeconds=10*3600;
                request.IsAvailable=(id,start,end)=>id!="mother" || start>=3*86400d+11*3600;
                var plan=Create(request);
                Check.True(plan.First.Outings[0].DepartureSeconds>=11*3600);
                Check.True(plan.First.Outings[1].DepartureSeconds>plan.First.Outings[0].DepartureSeconds);
                request=Request();request.FirstRoutine.Itinerary.Stops[0].Purpose=ResidentVisitPurpose.Social;
                request.PlannedAt=3*86400d+12*3600;
                plan=Create(request);Check.Near(9*3600,plan.First.Outings[0].DepartureSeconds);
            }));
            cases.Add(("Shared family day selection is stable per date with occasional occurrences",()=>{
                var household=Family();int selected=0;
                for(int day=0;day<1000;day++)
                {
                    bool value=HouseholdDayPlanner.SharedDay(21,2,household,day);
                    Check.Equal(value,HouseholdDayPlanner.SharedDay(21,2,household,day));
                    if(value)selected++;
                }
                Check.True(selected>60 && selected<150,"unexpected shared leisure frequency: "+selected);
            }));
            cases.Add(("Shared Inn exception covers only the exact selected evening event",()=>{
                var request=Request();int day=0;while(!HouseholdDayPlanner.SharedDay(21,2,request.Household,day))day++;
                request=Request(day);var first=new ResidentDailyOuting{DepartureSeconds=18*3600,Itinerary=Visit(10,300,3600)};
                first.Itinerary.Stops[0].Purpose=ResidentVisitPurpose.Social;
                var second=new ResidentDailyOuting{DepartureSeconds=18*3600,Itinerary=ResidentVisitJourney.Copy(first.Itinerary)};
                request.JointInn=new HouseholdJointInn{InnKey=10,First=first,Second=second};
                var plan=Create(request);
                var a=HouseholdDayPlanner.Schedule(plan,"mother",request.FirstRoutine);
                var b=HouseholdDayPlanner.Schedule(plan,"father",request.SecondRoutine);
                Check.Equal(10,a.EvaluateSeconds(day*86400d+18*3600+600).BuildingKey);
                Check.Equal(10,b.EvaluateSeconds(day*86400d+18*3600+600).BuildingKey);
                var visits=a.ScheduledVisits();Check.Equal(HouseholdDayPlanner.JointVisitIndex,visits[visits.Count-1].VisitIndex);
                double nextDay=(day+1d)*86400+18*3600+600;
                Check.Equal(7,a.EvaluateSeconds(nextDay).BuildingKey);
                Check.True(!a.TryVisitWindowForBuilding(nextDay,10,out var from,out var until));
                Check.Equal(2,a.ScheduledVisits(nextDay).Count);
                Check.True(a.IsHomeThroughout(nextDay,nextDay+100));
                first.Itinerary.Stops[0].Route[1].X=999;
                Check.Near(10,plan.JointInn.First.Itinerary.Stops[0].Route[1].X);
                Check.True(HouseholdAccess.RoutineLocked(request.Household,p=>p.Id=="mother"?a.EvaluateSeconds(day*86400d+18*3600+600):
                    p.Id=="father"?b.EvaluateSeconds(day*86400d+18*3600+600):new ActivitySnapshot(Activity.Home,7,default(RoutePoint),0)));
            }));
            cases.Add(("Unselected or forged shared Inn plans cannot bypass ordinary childcare",()=>{
                var request=Request();int day=0;while(HouseholdDayPlanner.SharedDay(21,2,request.Household,day))day++;
                request=Request(day);var outing=new ResidentDailyOuting{DepartureSeconds=18*3600,Itinerary=Visit(10,300,3600)};
                outing.Itinerary.Stops[0].Purpose=ResidentVisitPurpose.Social;
                request.JointInn=new HouseholdJointInn{InnKey=10,First=outing,Second=new ResidentDailyOuting{
                    DepartureSeconds=18*3600,Itinerary=ResidentVisitJourney.Copy(outing.Itinerary)}};
                Check.True(!HouseholdDayPlanner.TryCreate(request,out var ignored,out var reason));
                while(!HouseholdDayPlanner.SharedDay(21,2,request.Household,day))day++;
                request.AbsoluteDay=day;request.PlannedAt=day*86400d+8*3600;
                request.JointInn.Second.Itinerary.Stops[0].BuildingKey=11;
                Check.True(!HouseholdDayPlanner.TryCreate(request,out ignored,out reason));
            }));
            cases.Add(("Family day copies serialize detached and reject stale routine identity",()=>{
                var request=Request();var plan=Create(request);var options=new JsonSerializerOptions{IncludeFields=true};
                var copy=HouseholdDayPlan.Copy(JsonSerializer.Deserialize<HouseholdDayPlan>(JsonSerializer.Serialize(plan,options),options));
                Check.Equal(plan.Id,copy.Id);Check.Equal(3,copy.AbsoluteDay);
                copy.First.Outings[0].DepartureSeconds+=1;
                Check.True(copy.First.Outings[0].DepartureSeconds!=plan.First.Outings[0].DepartureSeconds);
                request.FirstRoutine.Itinerary.Stops[0].Route[1].X+=1;
                Check.Throws<ArgumentException>(()=>HouseholdDayPlanner.Schedule(plan,"mother",request.FirstRoutine));
            }));
        }
    }
}
