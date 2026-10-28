using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class HouseholdDayAppointmentTests
    {
        internal static HouseholdDayRequest SharedRequest()
        {
            var request=HouseholdDayPlanTests.Request();int day=0;
            while(!HouseholdDayPlanner.SharedDay(request.WorldSeed,2,request.Household,day))day++;
            request=HouseholdDayPlanTests.Request(day);
            var first=new ResidentDailyOuting{DepartureSeconds=18*3600,Itinerary=HouseholdDayPlanTests.Visit(10,300,3600)};
            first.Itinerary.Stops[0].Purpose=ResidentVisitPurpose.Social;
            request.JointInn=new HouseholdJointInn{InnKey=10,First=first,Second=new ResidentDailyOuting{
                DepartureSeconds=first.DepartureSeconds,Itinerary=ResidentVisitJourney.Copy(first.Itinerary)}};
            return request;
        }
        private static AppointmentRecord Booking(HouseholdDayRequest request,string npc,int venue,double starts,double ends)
        {return new AppointmentRecord{Id="booking-"+npc,ResidentId=npc,Purpose="meet",Status="accepted",
            StartsAt=request.AbsoluteDay*86400d+starts,EndsAt=request.AbsoluteDay*86400d+ends,
            Place=new AppointmentPlace{SettlementId=2,Kind="building",BuildingKey=venue}};}
        private static HouseholdAppointmentDay Compose(HouseholdDayRequest request,HouseholdDayPlan plan,
            IList<AppointmentRecord> bookings,double? now=null)
        {
            Check.True(HouseholdDayAppointments.TryCompose(plan,request.Household,request.FirstRoutine,request.SecondRoutine,
                request.WorldSeed,bookings,null,now??request.PlannedAt,out var result,out var reason),reason);return result;
        }
        internal static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Family appointment rejects a matching numeric venue from a different settlement",()=>{
                var request=HouseholdDayPlanTests.Request();var plan=HouseholdDayPlanTests.Create(request);
                var booking=Booking(request,"mother",8,12*3600,12*3600+600);booking.Place.SettlementId=99;
                Check.True(!HouseholdDayAppointments.TryCompose(plan,request.Household,request.FirstRoutine,request.SecondRoutine,
                    request.WorldSeed,new[]{booking},null,request.PlannedAt,out var result,out var reason,out var failure));
                Check.Equal(HouseholdDayCompositionFailure.RequiredAppointmentUnavailable,failure);Check.True(result==null);
            }));
            cases.Add(("A parent's appointment return tail blocks the other parent's ordinary outing",()=>{
                var request=HouseholdDayPlanTests.Request();var plan=HouseholdDayPlanTests.Create(request);
                bool first=plan.First.Outings[0].DepartureSeconds<plan.Second.Outings[0].DepartureSeconds;
                var active=first?plan.First:plan.Second;string other=first?plan.Second.ResidentId:plan.First.ResidentId;
                double departure=active.Outings[0].DepartureSeconds;
                var booking=Booking(request,other,8,departure-600,departure);
                var result=Compose(request,plan,new[]{booking});
                var projected=first?result.Plan.First:result.Plan.Second;
                Check.True(!projected.Outings.Exists(o=>o.SourceOutingIndex==0),"meeting ended at departure, but the parent had not walked home yet");
                Check.Equal(2,active.Outings.Count);
            }));
            cases.Add(("Ordinary family outing conflict at its exact departure requires physical recovery",()=>{
                var request=HouseholdDayPlanTests.Request();var plan=HouseholdDayPlanTests.Create(request);
                bool first=plan.First.Outings[0].DepartureSeconds<plan.Second.Outings[0].DepartureSeconds;
                var active=first?plan.First:plan.Second;string other=first?plan.Second.ResidentId:plan.First.ResidentId;
                double departure=active.Outings[0].DepartureSeconds;
                var booking=Booking(request,other,8,departure+600,departure+1200);
                foreach(double delta in new[]{0d,1d})
                {
                    Check.True(!HouseholdDayAppointments.TryCompose(plan,request.Household,request.FirstRoutine,request.SecondRoutine,
                        request.WorldSeed,new[]{booking},null,request.AbsoluteDay*86400d+departure+delta,
                        out var result,out var reason,out var failure));
                    Check.Equal(HouseholdDayCompositionFailure.PhysicalRecoveryRequired,failure);Check.True(result==null);
                }
                Check.Equal(2,active.Outings.Count);
            }));
            cases.Add(("Child-free partners retain independent ordinary outings during the other's meeting",()=>{
                var request=HouseholdDayPlanTests.Request();request.Household.Residents.RemoveAll(p=>p.AgeGroup==ResidentAgeGroup.Child);
                var plan=HouseholdDayPlanTests.Create(request);
                var result=Compose(request,plan,new[]{Booking(request,"mother",8,9*3600+600,9*3600+1200)});
                Check.Equal(8,result.Second.EvaluateSeconds(request.AbsoluteDay*86400d+9*3600+900).BuildingKey);
                Check.True(result.Second.ScheduledVisits().Exists(v=>v.VisitIndex==0 && v.BuildingKey==8));
            }));
            cases.Add(("Exact point meetings also reserve full parental absence without becoming fake family appointments",()=>{
                var request=HouseholdDayPlanTests.Request();var plan=HouseholdDayPlanTests.Create(request);
                var booking=Booking(request,"mother",-1,9*3600+600,9*3600+1200);
                booking.Place.Kind="world-point";booking.Place.X=15;booking.Place.Z=5;
                var route=new AppointmentPlaceRoute{AppointmentId=booking.Id,Place=new AppointmentPlace{Kind="world-point",SettlementId=2,
                    BuildingKey=-1,X=15,Z=5},Connections=new List<AppointmentPlaceRouteConnection>{new AppointmentPlaceRouteConnection{
                        AnchorBuildingKey=8,Route=new List<SavedRoutePoint>{new SavedRoutePoint{X=8},new SavedRoutePoint{X=15,Z=5}}}}};
                Check.True(HouseholdDayAppointments.TryCompose(plan,request.Household,request.FirstRoutine,request.SecondRoutine,
                    request.WorldSeed,new[]{booking},new[]{route},request.PlannedAt,out var result,out var reason),reason);
                var state=result.First.EvaluateSeconds(request.AbsoluteDay*86400d+9*3600+900);
                Check.Equal(Activity.Meeting,state.Activity);Check.Near(15,state.Position.X);Check.Near(5,state.Position.Z);
                Check.Equal(7,result.Second.EvaluateSeconds(request.AbsoluteDay*86400d+9*3600+900).BuildingKey);
                booking.Place.SettlementId=99;route.Place.SettlementId=99;
                Check.True(!HouseholdDayAppointments.TryCompose(plan,request.Household,request.FirstRoutine,request.SecondRoutine,
                    request.WorldSeed,new[]{booking},new[]{route},request.PlannedAt,out result,out reason,out var failure));
                Check.Equal(HouseholdDayCompositionFailure.RequiredAppointmentUnavailable,failure);
            }));
            cases.Add(("Family appointment composition preserves exact ordinary indexes and a parent's fixed meeting",()=>{
                var request=HouseholdDayPlanTests.Request();var plan=HouseholdDayPlanTests.Create(request);
                var booking=Booking(request,"mother",8,9*3600+600,9*3600+1200);
                var result=Compose(request,plan,new[]{booking});double day=request.AbsoluteDay*86400d;
                Check.Equal(8,result.First.EvaluateSeconds(day+9*3600+900).BuildingKey);
                Check.Equal(7,result.Second.EvaluateSeconds(day+9*3600+900).BuildingKey);
                Check.True(result.Second.ScheduledVisits(day+15*3600).Exists(v=>v.VisitIndex==1 && v.BuildingKey==9));
                Check.True(result.First.ScheduledVisits(day+9*3600).Exists(v=>v.VisitIndex==2 && v.BuildingKey==8));
                Check.Equal("accepted",booking.Status);Check.Near(day+9*3600+600,booking.StartsAt);
                Check.Equal(2,plan.Second.Outings.Count);
            }));
            cases.Add(("Either parent's required meeting suppresses both joint Inn legs while retaining the real route",()=>{
                var request=SharedRequest();var plan=HouseholdDayPlanTests.Create(request);
                var booking=Booking(request,"mother",10,18*3600+1200,18*3600+1800);
                var result=Compose(request,plan,new[]{booking});double day=request.AbsoluteDay*86400d;
                Check.True(result.Plan.JointSuppressed);Check.True(!plan.JointSuppressed);
                Check.Equal(7,result.First.EvaluateSeconds(day+18*3600+600).BuildingKey);
                Check.Equal(7,result.Second.EvaluateSeconds(day+18*3600+600).BuildingKey);
                Check.Equal(10,result.First.EvaluateSeconds(day+18*3600+1500).BuildingKey);
                Check.Equal(7,result.Second.EvaluateSeconds(day+18*3600+1500).BuildingKey);
                Check.True(!result.First.ScheduledVisits().Exists(v=>v.VisitIndex==HouseholdDayPlanner.JointVisitIndex));
                Check.True(!result.Second.ScheduledVisits().Exists(v=>v.VisitIndex==HouseholdDayPlanner.JointVisitIndex));
                Check.Near(10,result.Plan.JointInn.First.Itinerary.Stops[0].Route[1].X);
            }));
            cases.Add(("Nonconflicting fixed meeting preserves the shared family event and cannot repeat tomorrow",()=>{
                var request=SharedRequest();var plan=HouseholdDayPlanTests.Create(request);
                var booking=Booking(request,"mother",8,12*3600,12*3600+600);
                var result=Compose(request,plan,new[]{booking});double day=request.AbsoluteDay*86400d;
                Check.True(!result.Plan.JointSuppressed);
                Check.Equal(10,result.First.EvaluateSeconds(day+18*3600+600).BuildingKey);
                Check.Equal(10,result.Second.EvaluateSeconds(day+18*3600+600).BuildingKey);
                Check.Equal(7,result.First.EvaluateSeconds(day+86400+18*3600+600).BuildingKey);
            }));
            cases.Add(("Required appointment return travel also blocks a shared departure",()=>{
                var request=SharedRequest();var plan=HouseholdDayPlanTests.Create(request);
                var booking=Booking(request,"mother",8,18*3600-600,18*3600);
                var result=Compose(request,plan,new[]{booking});Check.True(result.Plan.JointSuppressed);
            }));
            cases.Add(("New appointment cannot erase an already active shared outing without physical recovery",()=>{
                var request=SharedRequest();var plan=HouseholdDayPlanTests.Create(request);
                var booking=Booking(request,"mother",10,18*3600+1200,18*3600+1800);
                Check.True(!HouseholdDayAppointments.TryCompose(plan,request.Household,request.FirstRoutine,request.SecondRoutine,
                    request.WorldSeed,new[]{booking},null,request.AbsoluteDay*86400d+18*3600+60,out var result,out var reason,out var failure));
                Check.Equal(HouseholdDayCompositionFailure.PhysicalRecoveryRequired,failure);
                Check.True(result==null && reason.Contains("physical recovery"),reason);Check.True(!plan.JointSuppressed);
                Check.True(!HouseholdDayAppointments.TryCompose(plan,request.Household,request.FirstRoutine,request.SecondRoutine,
                    request.WorldSeed,new[]{booking},null,request.AbsoluteDay*86400d+18*3600,out result,out reason,out failure));
                Check.Equal(HouseholdDayCompositionFailure.PhysicalRecoveryRequired,failure);
            }));
            cases.Add(("Bad second appointment cannot publish a partial first-parent schedule",()=>{
                var request=HouseholdDayPlanTests.Request();var plan=HouseholdDayPlanTests.Create(request);
                var good=Booking(request,"mother",8,12*3600,12*3600+600);
                var bad=Booking(request,"father",999,12*3600,12*3600+600);
                Check.True(!HouseholdDayAppointments.TryCompose(plan,request.Household,request.FirstRoutine,request.SecondRoutine,
                    request.WorldSeed,new[]{good,bad},null,request.PlannedAt,out var result,out var reason));
                Check.True(result==null);Check.Equal(2,plan.First.Outings.Count);Check.Equal(2,plan.Second.Outings.Count);
            }));
            cases.Add(("Joint cancellation survives save reload and a cleared appointment cannot reactivate it",()=>{
                var request=SharedRequest();var plan=HouseholdDayPlanTests.Create(request);var store=HouseholdDayPlanTests.Store(request);
                Check.True(store.TryPublishHouseholdDay(plan,request.PlannedAt,out var reason));
                Check.True(store.SuppressHouseholdJoint(plan.Id,request.PlannedAt+1));
                store.Restore(store.Snapshot());var saved=store.ReadHouseholdDay(2,7,request.AbsoluteDay);
                Check.True(saved.JointSuppressed && saved.JointInn!=null);
                var result=Compose(request,saved,new AppointmentRecord[0]);Check.True(result.Plan.JointSuppressed);
                Check.True(!result.First.ScheduledVisits().Exists(v=>v.VisitIndex==HouseholdDayPlanner.JointVisitIndex));
                Check.True(!store.TryPublishHouseholdDay(plan,request.PlannedAt+2,out reason));
                var forged=store.Snapshot();forged.Version=66;Check.Throws<ArgumentException>(()=>store.Restore(forged));
            }));
            cases.Add(("Joint suppression refuses an active trip",()=>{
                var request=SharedRequest();var plan=HouseholdDayPlanTests.Create(request);var store=HouseholdDayPlanTests.Store(request);
                Check.True(store.TryPublishHouseholdDay(plan,request.PlannedAt,out var reason));
                store.Restore(store.Snapshot());
                Check.True(!store.ReadHouseholdDay(2,7,request.AbsoluteDay).JointSuppressed);
                Check.True(!store.SuppressHouseholdJoint(plan.Id,request.AbsoluteDay*86400d+18*3600+60));
                Check.True(!store.SuppressHouseholdJoint(plan.Id,request.AbsoluteDay*86400d+21*3600),"suppression erased a completed shared outing");
                Check.True(!store.ReadHouseholdDay(2,7,request.AbsoluteDay).JointSuppressed);
            }));
        }
    }
}
