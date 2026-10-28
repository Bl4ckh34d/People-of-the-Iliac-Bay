using System;
using System.Collections.Generic;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class HouseholdJointStoreTests
    {
        private static WorldStateStore Prepared(out HouseholdDayPlan plan,out HouseholdJointMovement motion)
        {
            var request=HouseholdDayAppointmentTests.SharedRequest();plan=HouseholdDayPlanTests.Create(request);
            var store=HouseholdDayPlanTests.Store(request);
            Check.True(store.TryPublishHouseholdDay(plan,request.PlannedAt,out var reason),reason);
            motion=HouseholdJointMovement.Create(plan,request.PlannedAt);store.SaveHouseholdJointProgress(motion.Snapshot());return store;
        }
        private static double Departure(HouseholdDayPlan plan){return plan.AbsoluteDay*86400d+plan.JointInn.First.DepartureSeconds;}
        private static SavedRoutePoint Point(double x){return new SavedRoutePoint{X=x};}
        internal static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Conflict after scheduled start retires unstarted activity without rewriting its dated intent",()=>{
                var store=Prepared(out var plan,out var motion);
                var booking=new AppointmentRecord{Id="late-waiting-conflict",ResidentId="mother",Purpose="meet",Status="accepted",
                    StartsAt=Departure(plan)+600,EndsAt=Departure(plan)+1200,Importance=2,
                    Place=new AppointmentPlace{Kind="building",SettlementId=2,BuildingKey=10}};
                Check.True(store.TryReserveAppointment(booking,Departure(plan)+1,out var reason),reason);
                Check.True(store.TryRetireHouseholdJointAtHome(2,7,Departure(plan)+2,null));
                var history=store.ReadHouseholdJointProgress(2,7);
                Check.True(history.Retired);Check.Equal(HouseholdJointPhase.Waiting,history.Phase);
                Check.Near(0,history.DepartedAt);Check.Near(0,history.First.Distance);
                Check.True(!store.ReadHouseholdDay(2,7,plan.AbsoluteDay).JointSuppressed);
                Check.True(store.GetHouseholdJointMovement(2,7)==null);
                Check.Throws<ArgumentException>(()=>store.SaveHouseholdJointProgress(motion.Snapshot()));
            }));
            cases.Add(("Unstarted expired shared visit releases its reservation without route proof or invented movement",()=>{
                var store=Prepared(out var plan,out var motion);
                Check.True(store.TryRetireHouseholdJointAtHome(2,7,Departure(plan)+7200,null));
                var actual=store.ReadHouseholdJointProgress(2,7);Check.True(actual.Retired);
                Check.Equal(HouseholdJointPhase.Waiting,actual.Phase);Check.Near(0,actual.First.Distance);
                Check.True(!store.HasActiveHouseholdJoint(2,"mother"));
            }));
            cases.Add(("Known duty cancels waiting reservation but missing point proof cannot cancel it",()=>{
                var store=Prepared(out var plan,out var motion);
                var booking=new AppointmentRecord{Id="waiting-joint-conflict",ResidentId="mother",Purpose="meet",Status="accepted",
                    StartsAt=Departure(plan)+600,EndsAt=Departure(plan)+1200,Importance=2,
                    Place=new AppointmentPlace{Kind="building",SettlementId=2,BuildingKey=10}};
                Check.True(store.TryReserveAppointment(booking,plan.PlannedAt+1,out var reason),reason);
                Check.True(store.TryRetireHouseholdJointAtHome(2,7,Departure(plan)-60,null));
                Check.True(store.ReadHouseholdDay(2,7,plan.AbsoluteDay).JointSuppressed);
                store=Prepared(out plan,out motion);booking.Id="waiting-point-proof";
                booking.Place=new AppointmentPlace{Kind="world-point",SettlementId=2,BuildingKey=-1,X=10,Y=0,Z=0};
                booking.StartsAt=Departure(plan)+600;booking.EndsAt=Departure(plan)+1200;
                Check.True(store.TryReserveAppointment(booking,plan.PlannedAt+1,out reason),reason);
                Check.True(!store.TryRetireHouseholdJointAtHome(2,7,Departure(plan)-60,null));
                Check.True(store.HasActiveHouseholdJoint(2,"mother"));
            }));
            cases.Add(("Extinct archived participants release the old generation without fictional home arrivals",()=>{
                var store=Prepared(out var plan,out var motion);motion.Depart(Departure(plan),Point(0),Point(0));
                store.SaveHouseholdJointProgress(motion.Snapshot());var saved=store.Snapshot();
                foreach(var person in saved.Settlements[0].Households[0].Residents)person.IsAlive=false;
                store.Restore(saved);var replacement=HouseholdGeneration.Create(saved.WorldSeed,2,new HomeProfile(7,3,220),1);
                store.ArchiveHouseholdAndInstall(2,7,0,Departure(plan)+1,replacement);
                Check.True(store.TryRetireHouseholdJointAtHome(2,7,Departure(plan)+2,null));
                Check.True(!store.HasActiveHouseholdJoint(2,"mother"));
                var history=store.ReadHouseholdJointProgress(2,7);Check.True(history.Retired);
                Check.Equal(HouseholdJointPhase.Outbound,history.Phase);Check.Equal("participants-dead",history.RetirementReason);
                var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());Check.True(restored.ReadHouseholdJointProgress(2,7).Retired);
            }));
            cases.Add(("Elapsed leisure window expires cached outward permission without a revision change",()=>{
                var store=Prepared(out var plan,out var motion);motion.Depart(Departure(plan),Point(0),Point(0));
                store.SaveHouseholdJointProgress(motion.Snapshot());
                double leave=Departure(plan)+plan.JointInn.First.Itinerary.Stops[0].WalkSeconds+plan.JointInn.First.Itinerary.Stops[0].DwellSeconds;
                motion.Observe(leave,Point(0.5),Point(0.5));
                var paused=store.GetHouseholdJointMovement(2,7,now:leave);
                Check.True(paused.RequiresRecovery);Check.Near(Departure(plan),paused.Snapshot().ObservedAt);
                Check.Throws<ArgumentException>(()=>store.SaveHouseholdJointProgress(motion.Snapshot()));
                Check.Near(0,store.ReadHouseholdJointProgress(2,7).First.Position.X);
            }));
            cases.Add(("Explicit physical home recovery closes an otherwise valid interrupted event",()=>{
                var store=Prepared(out var plan,out var motion);motion.Depart(Departure(plan),Point(0),Point(0));
                store.SaveHouseholdJointProgress(motion.Snapshot());motion.Observe(Departure(plan)+1,Point(0.5),Point(0.5));
                store.SaveHouseholdJointProgress(motion.Snapshot());
                Check.True(!store.TryRetireHouseholdJointAtHome(2,7,Departure(plan)+2,id=>id=="mother"));
                Check.True(store.TryRetireHouseholdJointAtHome(2,7,Departure(plan)+2,id=>true));
                var history=store.ReadHouseholdJointProgress(2,7);
                Check.True(history.Retired);Check.Near(0.5,history.First.Position.X);Check.Equal(HouseholdJointPhase.Outbound,history.Phase);
                Check.True(!store.HasActiveHouseholdJoint(2,"mother"));
                Check.Throws<ArgumentException>(()=>store.SaveHouseholdJointProgress(motion.Snapshot()));
            }));
            cases.Add(("New conflicting meeting invalidates the cached outward shared motion",()=>{
                var store=Prepared(out var plan,out var motion);motion.Depart(Departure(plan),Point(0),Point(0));
                store.SaveHouseholdJointProgress(motion.Snapshot());
                var booking=new AppointmentRecord{Id="new-joint-conflict",ResidentId="mother",Purpose="meet",Status="accepted",
                    StartsAt=Departure(plan)+600,EndsAt=Departure(plan)+1200,Importance=2,
                    Place=new AppointmentPlace{Kind="building",SettlementId=2,BuildingKey=10}};
                Check.True(store.TryReserveAppointment(booking,Departure(plan)+1,out var reason),reason);
                motion.Observe(Departure(plan)+2,Point(0.5),Point(0.5));
                Check.Throws<ArgumentException>(()=>store.SaveHouseholdJointProgress(motion.Snapshot()));
                Check.Near(0,store.ReadHouseholdJointProgress(2,7).First.Position.X);
                Check.True(store.GetHouseholdJointMovement(2,7).RequiresRecovery);
            }));
            cases.Add(("A meeting on a different day does not cancel existing physical pair travel",()=>{
                var store=Prepared(out var plan,out var motion);motion.Depart(Departure(plan),Point(0),Point(0));
                store.SaveHouseholdJointProgress(motion.Snapshot());
                var booking=new AppointmentRecord{Id="later-joint-meeting",ResidentId="mother",Purpose="meet",Status="accepted",
                    StartsAt=Departure(plan)+86400,EndsAt=Departure(plan)+87000,Importance=2,
                    Place=new AppointmentPlace{Kind="building",SettlementId=2,BuildingKey=10}};
                Check.True(store.TryReserveAppointment(booking,Departure(plan)+1,out var reason),reason);
                motion.Observe(Departure(plan)+2,Point(0.5),Point(0.5));store.SaveHouseholdJointProgress(motion.Snapshot());
                Check.True(store.GetHouseholdJointMovement(2,7)!=null);Check.Near(0.5,store.ReadHouseholdJointProgress(2,7).First.Position.X);
            }));
            cases.Add(("Inert unstarted shared reservation retires without inventing a journey or blocking tomorrow",()=>{
                var store=Prepared(out var plan,out var motion);var saved=store.Snapshot();saved.Routines[0].Residents[0].OpenMinute++;
                store.Restore(saved);Check.True(store.GetHouseholdJointMovement(2,7)==null);
                Check.True(store.TryRetireHouseholdJointAtHome(2,7,plan.PlannedAt+1,null));
                var actual=store.ReadHouseholdJointProgress(2,7);
                Check.True(actual.Retired);Check.Equal(HouseholdJointPhase.Waiting,actual.Phase);
                Check.Near(0,actual.First.Distance);Check.True(!store.HasActiveHouseholdJoint(2,"father"));
                var request=HouseholdDayAppointmentTests.SharedRequest();request.AbsoluteDay=plan.AbsoluteDay+1;
                request.PlannedAt=request.AbsoluteDay*86400d+8*3600;request.JointInn=null;request.FirstRoutine.OpenMinute++;
                Check.True(store.TryPublishHouseholdDay(HouseholdDayPlanTests.Create(request),request.PlannedAt,out var reason),reason);
                var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());Check.True(restored.ReadHouseholdJointProgress(2,7).Retired);
            }));
            cases.Add(("Inert departed event retires only after every surviving parent is physically home",()=>{
                var store=Prepared(out var plan,out var motion);motion.Depart(Departure(plan),Point(0),Point(0));
                store.SaveHouseholdJointProgress(motion.Snapshot());motion.Observe(Departure(plan)+1,Point(0.5),Point(0.5));
                store.SaveHouseholdJointProgress(motion.Snapshot());var saved=store.Snapshot();
                saved.Settlements[0].Households[0].Residents[0].IsAlive=false;store.Restore(saved);
                Check.True(store.GetHouseholdJointMovement(2,7)==null);
                Check.True(!store.TryRetireHouseholdJointAtHome(2,7,Departure(plan)+2,null));
                Check.True(!store.TryRetireHouseholdJointAtHome(2,7,Departure(plan)+2,id=>id=="mother"));
                Check.True(store.HasActiveHouseholdJoint(2,"father"));
                Check.True(store.TryRetireHouseholdJointAtHome(2,7,Departure(plan)+2,id=>id=="father"));
                var history=store.ReadHouseholdJointProgress(2,7);Check.True(history.Retired);
                Check.Equal(HouseholdJointPhase.Outbound,history.Phase);Check.Near(0.5,history.Second.Position.X);
                Check.True(!store.HasActiveHouseholdJoint(2,"father"));Check.True(store.GetHouseholdJointMovement(2,7)==null);
            }));
            cases.Add(("Shared movement save restores source-bound actual progress and owned snapshots",()=>{
                var store=Prepared(out var plan,out var motion);motion.Depart(Departure(plan),Point(0),Point(0));
                store.SaveHouseholdJointProgress(motion.Snapshot());
                motion.Observe(Departure(plan)+1,Point(0.5),Point(0.25));store.SaveHouseholdJointProgress(motion.Snapshot());
                var options=new JsonSerializerOptions{IncludeFields=true};var saved=store.Snapshot();
                var restored=new WorldStateStore(0);restored.Restore(JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(saved,options),options));
                var actual=restored.GetHouseholdJointMovement(2,7);Check.True(actual!=null);
                Check.Near(0.5,actual.Snapshot().First.Position.X);Check.Near(0.25,actual.Snapshot().Second.Distance);
                var external=restored.ReadHouseholdJointProgress(2,7);external.First.Position.X=9;
                Check.Near(0.5,restored.ReadHouseholdJointProgress(2,7).First.Position.X);
            }));
            cases.Add(("Joint store rejects forged advancement and stale sequence without partial mutation",()=>{
                var store=Prepared(out var plan,out var motion);var before=store.ReadHouseholdJointProgress(2,7);
                var forged=motion.Snapshot();forged.Phase=HouseholdJointPhase.AtInn;forged.DepartedAt=Departure(plan);
                forged.ObservedAt=Departure(plan)+1;forged.Sequence++;
                Check.Throws<ArgumentException>(()=>store.SaveHouseholdJointProgress(forged));
                Check.Equal(before.Sequence,store.ReadHouseholdJointProgress(2,7).Sequence);
                motion.Depart(Departure(plan),Point(0),Point(0));store.SaveHouseholdJointProgress(motion.Snapshot());
                Check.Throws<ArgumentException>(()=>store.SaveHouseholdJointProgress(before));
            }));
            cases.Add(("Shared checkpoint replay is idempotent but cannot erase progressed travel",()=>{
                var store=Prepared(out var plan,out var motion);int revision=store.HouseholdJointRevision;
                store.SaveHouseholdJointProgress(motion.Snapshot());Check.Equal(revision,store.HouseholdJointRevision);
                motion.Depart(Departure(plan),Point(0),Point(0));store.SaveHouseholdJointProgress(motion.Snapshot());
                var earlier=motion.Snapshot();motion.Observe(Departure(plan)+1,Point(0.5),Point(0.5));
                store.SaveHouseholdJointProgress(motion.Snapshot());
                Check.Throws<ArgumentException>(()=>store.SaveHouseholdJointProgress(earlier));
                Check.Near(0.5,store.ReadHouseholdJointProgress(2,7).First.Distance);
            }));
            cases.Add(("Active shared progress reserves old-day route source until both return",()=>{
                var store=Prepared(out var plan,out var motion);motion.Depart(Departure(plan),Point(0),Point(0));
                store.SaveHouseholdJointProgress(motion.Snapshot());
                var request=HouseholdDayAppointmentTests.SharedRequest();request.AbsoluteDay=plan.AbsoluteDay+1;
                request.PlannedAt=request.AbsoluteDay*86400d+8*3600;request.JointInn=null;
                var tomorrow=HouseholdDayPlanTests.Create(request);
                Check.True(!store.TryPublishHouseholdDay(tomorrow,request.PlannedAt,out var reason));
                Check.True(store.HasActiveHouseholdJoint(2,"mother"));Check.True(!store.HasActiveHouseholdJoint(3,"mother"));
                Check.Equal(plan.Id,store.ReadHouseholdDay(2,7,plan.AbsoluteDay).Id);
            }));
            cases.Add(("Cancelled shared departure releases route reservation and permits a later family day",()=>{
                var store=Prepared(out var plan,out var motion);store.CancelHouseholdDay(plan.Id,plan.PlannedAt+1);
                motion.CancelBeforeDeparture(plan.PlannedAt+1);store.SaveHouseholdJointProgress(motion.Snapshot());
                Check.True(!store.HasActiveHouseholdJoint(2,"mother"));
                var request=HouseholdDayAppointmentTests.SharedRequest();request.AbsoluteDay=plan.AbsoluteDay+1;
                request.PlannedAt=request.AbsoluteDay*86400d+8*3600;request.JointInn=null;
                var tomorrow=HouseholdDayPlanTests.Create(request);
                Check.True(store.TryPublishHouseholdDay(tomorrow,request.PlannedAt,out var reason),reason);
                var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
                Check.Equal(HouseholdJointPhase.Cancelled,restored.ReadHouseholdJointProgress(2,7).Phase);
            }));
            cases.Add(("Changed routine or dead parent leaves shared history inert rather than changing actor identity",()=>{
                var store=Prepared(out var plan,out var motion);var saved=store.Snapshot();
                saved.Routines[0].Residents[0].OpenMinute++;
                var restored=new WorldStateStore(0);restored.Restore(saved);
                Check.True(restored.GetHouseholdJointMovement(2,7)==null);
                Check.True(restored.ReadHouseholdJointProgress(2,7)!=null);
                saved=store.Snapshot();saved.Settlements[0].Households[0].Residents[0].IsAlive=false;
                restored.Restore(saved);Check.True(restored.GetHouseholdJointMovement(2,7)==null);
            }));
            cases.Add(("Restore rejects duplicate joint owners and forged current segment",()=>{
                var store=Prepared(out var plan,out var motion);var saved=store.Snapshot();
                saved.HouseholdJointProgress.Add(HouseholdJointProgress.Copy(saved.HouseholdJointProgress[0]));
                var restored=new WorldStateStore(0);Check.Throws<ArgumentException>(()=>restored.Restore(saved));
                saved=store.Snapshot();saved.HouseholdJointProgress[0].First.Segment=900;
                Check.Throws<ArgumentException>(()=>restored.Restore(saved));
            }));
        }
    }
}
