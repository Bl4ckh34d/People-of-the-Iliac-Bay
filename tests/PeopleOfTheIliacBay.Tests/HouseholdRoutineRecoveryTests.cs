using System;
using System.Collections.Generic;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class HouseholdRoutineRecoveryTests
    {
        private static RoutineRecoveryPlan Recover(HouseholdDayRequest request,HouseholdDayPlan day,string id,
            double localNow,int origin,bool skipJoint=false,IList<AppointmentRecord> bookings=null,IList<AppointmentPlaceRoute> routes=null)
        {
            double now=request.AbsoluteDay*86400d+localNow;
            Check.True(RoutineRecoveryPlanner.TryCreateWithHouseholdDay(day,request.Household,
                request.FirstRoutine,request.SecondRoutine,request.WorldSeed,id,bookings,routes,now,origin,now,30,
                out var result,out var reason,out var failure,false,skipJoint),reason+" / "+failure);
            return result;
        }
        private static WorldStateStore Save(HouseholdDayRequest request,HouseholdDayPlan day,RoutineRecoveryPlan recovery,IList<AppointmentRecord> bookings=null)
        {
            var store=HouseholdDayPlanTests.Store(request);
            if(bookings!=null){var world=store.Snapshot();world.Appointments.AddRange(bookings);store.Restore(world);}
            Check.True(store.TryPublishHouseholdDay(day,request.PlannedAt,out var reason),reason);
            store.SaveRoutineRecovery(recovery);return store;
        }
        internal static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Both household recoveries commit together and an invalid second parent changes neither",()=>{
                var request=HouseholdDayAppointmentTests.SharedRequest();var day=HouseholdDayPlanTests.Create(request);
                var mother=Recover(request,day,"mother",17*3600,7);var father=Recover(request,day,"father",17*3600,7,true);
                var store=Save(request,day,mother);int revision=store.RoutineRecoveryRevision;
                var blocked=RoutineRecoveryPlan.Copy(father);blocked.Legs[0].Route.Insert(1,new SavedRoutePoint{X=99,Z=99});
                mother=Recover(request,day,"mother",17*3600,7,true);
                Check.Throws<ArgumentException>(()=>store.SaveHouseholdRoutineRecoveries(mother,blocked));
                Check.Equal(revision,store.RoutineRecoveryRevision);Check.True(!store.HasRoutineRecovery("father"));
                Check.True(!store.GetHouseholdRoutineRecovery("mother",day.AbsoluteDay).HouseholdJointSkipped);
                store.SaveHouseholdRoutineRecoveries(mother,father);Check.True(store.RoutineRecoveryRevision>revision);
                Check.True(store.GetHouseholdRoutineRecovery("mother",day.AbsoluteDay).HouseholdJointSkipped);
                Check.True(store.GetHouseholdRoutineRecovery("father",day.AbsoluteDay)!=null);
            }));
            cases.Add(("Paired household recovery rejects duplicate owners without partial writes",()=>{
                var request=HouseholdDayAppointmentTests.SharedRequest();var day=HouseholdDayPlanTests.Create(request);
                var mother=Recover(request,day,"mother",17*3600,7);var store=Save(request,day,mother);
                int revision=store.RoutineRecoveryRevision;Check.Throws<ArgumentException>(()=>store.SaveHouseholdRoutineRecoveries(mother,mother));
                Check.Equal(revision,store.RoutineRecoveryRevision);
            }));
            cases.Add(("Paired household recovery cannot cancel only one parent's joint event",()=>{
                var request=HouseholdDayAppointmentTests.SharedRequest();var day=HouseholdDayPlanTests.Create(request);
                var mother=Recover(request,day,"mother",17*3600,7,true);var father=Recover(request,day,"father",17*3600,7);
                var store=Save(request,day,mother);int revision=store.RoutineRecoveryRevision;
                Check.Throws<ArgumentException>(()=>store.SaveHouseholdRoutineRecoveries(mother,father));
                Check.Equal(revision,store.RoutineRecoveryRevision);Check.True(!store.HasRoutineRecovery("father"));
            }));
            cases.Add(("A parent with no outings or duties remains valid childcare during family recovery",()=>{
                var request=HouseholdDayPlanTests.Request();
                request.SecondRoutine=new ResidentRoutine{ResidentId="father",DestinationKey=-1};
                var day=HouseholdDayPlanTests.Create(request);var recovery=Recover(request,day,"mother",8*3600,7);
                Check.True(recovery.Legs.Exists(l=>l.OriginalStopIndex==0));Check.True(recovery.HouseholdDayId==day.Id);
            }));
            cases.Add(("Moving a partner's exact point meeting invalidates family recovery",()=>{
                var request=HouseholdDayAppointmentTests.SharedRequest();var day=HouseholdDayPlanTests.Create(request);
                double at=day.AbsoluteDay*86400d+20*3600;
                var booking=new AppointmentRecord{Id="father-point",ResidentId="father",Purpose="meet",Status="accepted",StartsAt=at,EndsAt=at+600,
                    Place=new AppointmentPlace{Kind="world-point",SettlementId=2,BuildingKey=-1,X=8,Z=5}};
                var proof=new AppointmentPlaceRoute{AppointmentId=booking.Id,Place=AppointmentPlace.Copy(booking.Place),
                    Connections=new List<AppointmentPlaceRouteConnection>{new AppointmentPlaceRouteConnection{AnchorBuildingKey=8,
                        Route=new List<SavedRoutePoint>{new SavedRoutePoint{X=8},new SavedRoutePoint{X=8,Z=5}}}}};
                var recovery=Recover(request,day,"mother",18*3600+600,10,true,new[]{booking},new[]{proof});
                var store=Save(request,day,recovery,new[]{booking});Check.True(store.GetHouseholdRoutineRecovery("mother",day.AbsoluteDay)!=null);
                var world=store.Snapshot();world.Appointments[0].Place.Z=6;store.Restore(world);
                Check.True(store.GetHouseholdRoutineRecovery("mother",day.AbsoluteDay)==null,"a new meeting point must invalidate paired availability");
            }));
            cases.Add(("A joint recovery cannot impersonate an ordinary source visit",()=>{
                var request=HouseholdDayAppointmentTests.SharedRequest();var day=HouseholdDayPlanTests.Create(request);
                var recovery=Recover(request,day,"mother",17*3600,7);var store=Save(request,day,recovery);
                var joint=recovery.Legs.Find(l=>l.HouseholdJoint);joint.HouseholdJoint=false;joint.OriginalStopIndex=0;
                recovery.SkippedStopIndices.Remove(0);
                Check.Throws<ArgumentException>(()=>store.SaveRoutineRecovery(recovery));
            }));
            cases.Add(("Unused ordinary indexes cannot claim a joint recovery route",()=>{
                var request=HouseholdDayAppointmentTests.SharedRequest();var day=HouseholdDayPlanTests.Create(request);
                var recovery=Recover(request,day,"mother",17*3600,7);var store=Save(request,day,recovery);
                var joint=recovery.Legs.Find(l=>l.HouseholdJoint);joint.HouseholdJoint=false;joint.OriginalStopIndex=31;
                Check.Throws<ArgumentException>(()=>store.SaveRoutineRecovery(recovery));
            }));
            cases.Add(("Typed family recovery persists a genuine required building appointment",()=>{
                var request=HouseholdDayAppointmentTests.SharedRequest();var day=HouseholdDayPlanTests.Create(request);
                double at=day.AbsoluteDay*86400d+18*3600+600;
                var bookings=new[]{new AppointmentRecord{Id="mother-duty",ResidentId="mother",Purpose="meet",Status="accepted",StartsAt=at,
                    EndsAt=at+600,Place=new AppointmentPlace{Kind="building",SettlementId=2,BuildingKey=10}}};
                var recovery=Recover(request,day,"mother",17*3600,7,false,bookings);var store=Save(request,day,recovery,bookings);
                var read=store.GetHouseholdRoutineRecovery("mother",day.AbsoluteDay);Check.True(read!=null && read.HouseholdJointSkipped);
                Check.Equal("mother-duty",read.Legs[0].AppointmentId);Check.Equal(10,read.Evaluate(at+10).BuildingKey);
            }));
            cases.Add(("A joint recovery cannot impersonate a building appointment",()=>{
                var request=HouseholdDayAppointmentTests.SharedRequest();var day=HouseholdDayPlanTests.Create(request);
                var recovery=Recover(request,day,"mother",17*3600,7);var store=Save(request,day,recovery);
                var joint=recovery.Legs.Find(l=>l.HouseholdJoint);joint.HouseholdJoint=false;joint.AppointmentId="invented-booking";
                Check.Throws<ArgumentException>(()=>store.SaveRoutineRecovery(recovery));
            }));
            cases.Add(("Family recovery must record the disposition of its joint event",()=>{
                var request=HouseholdDayAppointmentTests.SharedRequest();var day=HouseholdDayPlanTests.Create(request);
                var recovery=Recover(request,day,"mother",17*3600,7);var store=Save(request,day,recovery);
                recovery.Legs.Clear();recovery.EndsAt=recovery.DepartAt;
                recovery.Legs.Add(new RoutineRecoveryLeg{BuildingKey=7,ReturnsHome=true,WalkAt=recovery.DepartAt,ArriveAt=recovery.DepartAt,
                    EnterAt=recovery.DepartAt,LeaveAt=recovery.DepartAt,Route=new List<SavedRoutePoint>{new SavedRoutePoint()}});
                Check.Throws<ArgumentException>(()=>store.SaveRoutineRecovery(recovery));
            }));
            cases.Add(("Household recovery cannot predate the published family plan",()=>{
                var request=HouseholdDayAppointmentTests.SharedRequest();var day=HouseholdDayPlanTests.Create(request);
                var recovery=Recover(request,day,"mother",17*3600,7);var store=Save(request,day,recovery);
                recovery.CreatedAt=request.PlannedAt-1;
                Check.Throws<ArgumentException>(()=>store.SaveRoutineRecovery(recovery));
            }));
            cases.Add(("Legacy recovery on a dated family day loads but cannot override the family schedule",()=>{
                var request=HouseholdDayAppointmentTests.SharedRequest();var day=HouseholdDayPlanTests.Create(request);
                var resident=request.Household.Residents[0];var routine=request.FirstRoutine;double now=day.AbsoluteDay*86400d+17*3600;
                Check.True(RoutineRecoveryPlanner.TryCreate(2,resident,routine,ResidentRoutineDay.Schedule(7,routine,360,1380),null,
                    day.AbsoluteDay,now,7,now,360,1380,30,out var legacy,out var reason),reason);
                var store=HouseholdDayPlanTests.Store(request);store.SaveRoutineRecovery(legacy);
                Check.True(store.TryPublishHouseholdDay(day,request.PlannedAt,out reason),reason);
                var world=store.Snapshot();world.Version=67;store.Restore(world);
                Check.True(store.HasRoutineRecovery("mother"));Check.True(store.GetHouseholdRoutineRecovery("mother",day.AbsoluteDay)==null);
                Check.True(store.GetRoutineRecovery("mother",day.AbsoluteDay,legacy.RoutineFingerprint,legacy.AppointmentFingerprint)==null,
                    "legacy recovery must not override a current dated family plan");
            }));
            cases.Add(("Partner required duty cancels both future joint continuations",()=>{
                var request=HouseholdDayAppointmentTests.SharedRequest();var day=HouseholdDayPlanTests.Create(request);
                double at=day.AbsoluteDay*86400d+18*3600+600;
                var bookings=new[]{new AppointmentRecord{Id="father-duty",ResidentId="father",Purpose="meet",Status="accepted",
                    StartsAt=at,EndsAt=at+600,Place=new AppointmentPlace{Kind="building",SettlementId=2,BuildingKey=10}}};
                var mother=Recover(request,day,"mother",17*3600,7,false,bookings);
                var father=Recover(request,day,"father",17*3600,7,false,bookings);
                Check.True(mother.HouseholdJointSkipped && father.HouseholdJointSkipped);
                Check.True(!mother.Legs.Exists(l=>l.HouseholdJoint) && !father.Legs.Exists(l=>l.HouseholdJoint));
                Check.True(father.Legs.Exists(l=>l.AppointmentId=="father-duty"));Check.True(!day.JointSuppressed);
            }));
            cases.Add(("Family recovery yields ordinary leisure to the other parent's required full absence",()=>{
                var request=HouseholdDayAppointmentTests.SharedRequest();var day=HouseholdDayPlanTests.Create(request);
                double at=day.AbsoluteDay*86400d+9*3600+500;
                var bookings=new[]{new AppointmentRecord{Id="father-duty",ResidentId="father",Purpose="meet",Status="accepted",
                    StartsAt=at,EndsAt=at+600,Place=new AppointmentPlace{Kind="building",SettlementId=2,BuildingKey=8}}};
                var recovery=Recover(request,day,"mother",8*3600,7,false,bookings);
                Check.True(!recovery.Legs.Exists(l=>l.OriginalStopIndex==0),"mother must cover the child during father's required absence");
                Check.True(recovery.SkippedStopIndices.Contains(0));
            }));
            cases.Add(("Childcare recovery uses the measured time rate for the partner's required return tail",()=>{
                var request=HouseholdDayAppointmentTests.SharedRequest();var day=HouseholdDayPlanTests.Create(request);
                double start=day.AbsoluteDay*86400d;
                var bookings=new[]{new AppointmentRecord{Id="father-slow-return",ResidentId="father",Purpose="meet",Status="accepted",
                    StartsAt=start+8*3600+50*60,EndsAt=start+8*3600+58*60,
                    Place=new AppointmentPlace{Kind="building",SettlementId=2,BuildingKey=8}}};
                var recovery=Recover(request,day,"mother",8*3600,7,false,bookings);
                Check.True(!recovery.Legs.Exists(l=>l.OriginalStopIndex==0),"the measured four-minute return overlaps mother's 09:00 departure");
            }));
            cases.Add(("Family recovery returns from the real joint Inn without replaying earlier visits",()=>{
                var request=HouseholdDayAppointmentTests.SharedRequest();var day=HouseholdDayPlanTests.Create(request);
                var recovery=Recover(request,day,"mother",18*3600+600,10,true);
                Check.Equal(day.Id,recovery.HouseholdDayId);Check.True(recovery.HouseholdJointSkipped);
                Check.Equal(1,recovery.Legs.Count);var home=recovery.Legs[0];Check.True(home.ReturnsHome);
                Check.Equal(7,home.BuildingKey);Check.Near(10,home.Route[0].X);Check.Near(0,home.Route[home.Route.Count-1].X);
                Check.True(recovery.EndsAt>recovery.DepartAt);Check.Equal(Activity.Travel,recovery.Evaluate(recovery.DepartAt+1).Activity);
                Check.Equal(7,recovery.Evaluate(recovery.EndsAt).BuildingKey);
            }));
            cases.Add(("Family recovery retains retimed ordinary visits with their original identities",()=>{
                var request=HouseholdDayAppointmentTests.SharedRequest();var day=HouseholdDayPlanTests.Create(request);
                var recovery=Recover(request,day,"father",8*3600,7);
                var expected=HouseholdDayPlanner.Schedule(day,"father",request.SecondRoutine).ScheduledVisits(request.PlannedAt);
                var actual=DailySchedule.ContinueDay(HouseholdDayPlanner.Schedule(day,"father",request.SecondRoutine),recovery).ScheduledVisits(request.PlannedAt);
                Check.Equal(expected.Count,actual.Count);
                for(int index=0;index<expected.Count;index++)
                {Check.Equal(expected[index].VisitIndex,actual[index].VisitIndex);Check.Near(expected[index].EnterAt,actual[index].EnterAt);}
                Check.True(actual[0].EnterAt>9*3600+300,"the father must keep his childcare-delayed departure");
            }));
            cases.Add(("Joint recovery legs have a family identity rather than a fake appointment or ordinary stop",()=>{
                var request=HouseholdDayAppointmentTests.SharedRequest();var day=HouseholdDayPlanTests.Create(request);
                var recovery=Recover(request,day,"mother",17*3600,7);
                var joint=recovery.Legs.Find(l=>l.HouseholdJoint);Check.True(joint!=null);
                Check.Equal(-1,joint.OriginalStopIndex);Check.True(joint.AppointmentId==null);
                Check.Equal(ResidentVisitPurpose.Social,joint.Purpose);
                var schedule=DailySchedule.ContinueDay(HouseholdDayPlanner.Schedule(day,"mother",request.FirstRoutine),recovery);
                Check.Equal(HouseholdDayPlanner.JointVisitIndex,schedule.ScheduledVisits(request.AbsoluteDay*86400d+17*3600)[0].VisitIndex);
                var malformed=RoutineRecoveryPlan.Copy(recovery);malformed.HouseholdDayId=null;
                Check.Throws<ArgumentException>(()=>RoutineRecoveryPlan.Copy(malformed));
                malformed=RoutineRecoveryPlan.Copy(recovery);malformed.Legs[0].AppointmentId="not-a-real-booking";
                Check.Throws<ArgumentException>(()=>RoutineRecoveryPlan.Copy(malformed));
            }));
            cases.Add(("Family recovery saves detached and cannot activate through the legacy lookup",()=>{
                var request=HouseholdDayAppointmentTests.SharedRequest();var day=HouseholdDayPlanTests.Create(request);
                var recovery=Recover(request,day,"mother",18*3600+600,10,true);var store=Save(request,day,recovery);
                Check.True(store.GetRoutineRecovery("mother",day.AbsoluteDay,recovery.RoutineFingerprint,recovery.AppointmentFingerprint)==null);
                Check.True(store.GetHouseholdRoutineRecovery("mother",day.AbsoluteDay)!=null);
                Check.True(store.GetHouseholdRoutineRecovery("mother",day.AbsoluteDay+1)==null);
                var options=new JsonSerializerOptions{IncludeFields=true};var restored=new WorldStateStore(0);
                restored.Restore(JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(store.Snapshot(),options),options));
                var read=restored.GetHouseholdRoutineRecovery("mother",day.AbsoluteDay);Check.True(read!=null);
                read.Legs[0].Route[0].X=999;Check.Near(10,restored.GetHouseholdRoutineRecovery("mother",day.AbsoluteDay).Legs[0].Route[0].X);
            }));
            cases.Add(("Other parent appointments invalidate family recovery without invalidating the save",()=>{
                var request=HouseholdDayAppointmentTests.SharedRequest();var day=HouseholdDayPlanTests.Create(request);
                var recovery=Recover(request,day,"mother",18*3600+600,10,true);var store=Save(request,day,recovery);
                var world=store.Snapshot();double at=day.AbsoluteDay*86400d+20*3600;
                world.Appointments.Add(new AppointmentRecord{Id="father-new-meeting",ResidentId="father",Purpose="meet",Status="accepted",
                    StartsAt=at,EndsAt=at+600,Place=new AppointmentPlace{Kind="building",SettlementId=2,BuildingKey=8}});
                store.Restore(world);Check.True(store.HasRoutineRecovery("mother"));
                Check.True(store.GetHouseholdRoutineRecovery("mother",day.AbsoluteDay)==null);
                Check.Throws<ArgumentException>(()=>store.SaveRoutineRecovery(recovery));
            }));
            cases.Add(("Household suppression and source changes invalidate saved family continuation",()=>{
                var request=HouseholdDayAppointmentTests.SharedRequest();var day=HouseholdDayPlanTests.Create(request);
                var recovery=Recover(request,day,"mother",17*3600,7);var store=Save(request,day,recovery);
                Check.True(store.SuppressHouseholdJoint(day.Id,request.AbsoluteDay*86400d+17*3600));
                Check.True(store.GetHouseholdRoutineRecovery("mother",day.AbsoluteDay)==null);
                var world=store.Snapshot();store.Restore(world);Check.True(store.HasRoutineRecovery("mother"));
                Check.True(store.GetHouseholdRoutineRecovery("mother",day.AbsoluteDay)==null);
            }));
            cases.Add(("Older schemas reject future family recovery fields before migration",()=>{
                var request=HouseholdDayAppointmentTests.SharedRequest();var day=HouseholdDayPlanTests.Create(request);
                var recovery=Recover(request,day,"mother",18*3600+600,10,true);var store=Save(request,day,recovery);
                var world=store.Snapshot();world.Version=67;Check.Throws<ArgumentException>(()=>store.Restore(world));
                world.Version=1;world.HouseholdDays.Clear();Check.Throws<ArgumentException>(()=>store.Restore(world));
            }));
            cases.Add(("Family recovery refuses invented joint route geometry and missing published provenance",()=>{
                var request=HouseholdDayAppointmentTests.SharedRequest();var day=HouseholdDayPlanTests.Create(request);
                var recovery=Recover(request,day,"mother",18*3600+600,10,true);
                var store=HouseholdDayPlanTests.Store(request);Check.Throws<ArgumentException>(()=>store.SaveRoutineRecovery(recovery));
                store=Save(request,day,recovery);var forged=RoutineRecoveryPlan.Copy(recovery);
                forged.Legs[0].Route.Insert(1,new SavedRoutePoint{X=99,Z=99});
                Check.Throws<ArgumentException>(()=>store.SaveRoutineRecovery(forged));
                day.First.Outings[0].DepartureSeconds+=1;Check.Throws<ArgumentException>(()=>store.SaveRoutineRecovery(
                    Recover(request,day,"mother",18*3600+600,10,true)));
            }));
        }
    }
}
