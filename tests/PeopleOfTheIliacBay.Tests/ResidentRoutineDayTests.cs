using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
namespace PeopleOfTheIliacBay.Tests
{
    internal static class ResidentRoutineDayTests
    {
        internal static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("First multi-outing publication promotes only its town and retains other residents",()=>{
                var store=new WorldStateStore(7);var town=store.GetOrCreateFamilies(10,new[]{new HomeProfile(20,3,220)});
                var resident=town.Households[0].Residents[0];var other=town.Households[0].Residents[1];
                var first=Routine(resident.Id);first.AdditionalOutings.Clear();var second=Routine(other.Id);second.AdditionalOutings.Clear();
                store.SetRoutines(new TownRoutineState{SettlementId=10,ItineraryVersion=1,Residents=new List<ResidentRoutine>{first,second}});
                store.GetOrCreate(11,new[]{30},1);store.SetRoutines(new TownRoutineState{SettlementId=11,ItineraryVersion=1});
                store.SetResidentRoutine(10,Routine(resident.Id));
                var saved=store.GetRoutines(10);Check.Equal(2,saved.ItineraryVersion);Check.Equal(2,saved.Residents.Count);
                Check.Equal(1,saved.Residents.Find(r=>r.ResidentId==resident.Id).AdditionalOutings.Count);
                Check.Equal(0,saved.Residents.Find(r=>r.ResidentId==other.Id).AdditionalOutings.Count);
                Check.Equal(1,store.GetRoutines(11).ItineraryVersion);
                Check.True(ReferenceEquals(resident,store.GetOrCreateFamilies(10,new[]{new HomeProfile(20,3,220)}).Households[0].Residents[0]));
            }));
            cases.Add(("Rejected first multi-outing publication leaves old format and routines intact",()=>{
                var store=new WorldStateStore(7);var town=store.GetOrCreateFamilies(10,new[]{new HomeProfile(20,3,220)});
                var resident=town.Households[0].Residents[0];var first=Routine(resident.Id);first.AdditionalOutings.Clear();
                store.SetRoutines(new TownRoutineState{SettlementId=10,ItineraryVersion=1,Residents=new List<ResidentRoutine>{first}});
                var invalid=Routine(resident.Id);invalid.AdditionalOutings[0].DepartureSeconds=9*3600+60;
                Check.Throws<ArgumentException>(()=>store.SetResidentRoutine(10,invalid));
                var saved=store.GetRoutines(10);Check.Equal(1,saved.ItineraryVersion);Check.Equal(0,saved.Residents[0].AdditionalOutings.Count);
                Check.Equal(9*3600d,saved.Residents[0].ItineraryDepartureSeconds);
                Check.True(ReferenceEquals(resident,store.GetOrCreateFamilies(10,new[]{new HomeProfile(20,3,220)}).Households[0].Residents[0]));
            }));
            cases.Add(("Dialogue follows the later outing and names its future destination between outings",()=>{
                var routine=Routine("person");var schedule=ResidentRoutineDay.Schedule(20,routine,360,1380);
                Func<int,string> name=key=>key==21?"Morning Market":"Evening Inn";
                string between=ConversationLifeContext.RoutineStatus(routine,13*3600,schedule.EvaluateSeconds(13*3600),0,name);
                Check.True(between.Contains("Evening Inn") && between.Contains("at home"),between);
                string walking=ConversationLifeContext.RoutineStatus(routine,18*3600+60,schedule.EvaluateSeconds(18*3600+60),0,name);
                Check.True(walking.Contains("traveling toward Evening Inn"),walking);
                string inside=ConversationLifeContext.RoutineStatus(routine,18*3600+600,schedule.EvaluateSeconds(18*3600+600),0,name);
                Check.True(inside.Contains("currently at Evening Inn") && !inside.Contains("outside the original itinerary"),inside);
            }));
            cases.Add(("Optional later outings yield to a shorter waking window without losing a required meeting",()=>{
                var resident=new Resident{Id="person",HomeKey=20,IsAlive=true,AgeGroup=ResidentAgeGroup.Adult};
                var booking=new AppointmentRecord{Id="noon",ResidentId=resident.Id,Purpose="meet",Status="accepted",
                    StartsAt=12*3600,EndsAt=12*3600+600,
                    Place=new AppointmentPlace{Kind="building",SettlementId=10,BuildingKey=21}};
                Check.True(AppointmentDayPlanner.TryCreate(resident,Routine(resident.Id),0,new[]{booking},360,1020,
                    out var schedule,out var reason),reason);
                Check.Equal(21,schedule.EvaluateSeconds(12*3600+300).BuildingKey);
                Check.True(!schedule.ContainsVisitBuilding(22));
            }));
            cases.Add(("Appointments never alias an omitted ordinary visit at the same venue",()=>{
                var resident=new Resident{Id="person",HomeKey=20,IsAlive=true,AgeGroup=ResidentAgeGroup.Adult};
                var routine=Routine(resident.Id);
                var booking=new AppointmentRecord{Id="replace",ResidentId=resident.Id,Purpose="meet",Status="accepted",
                    StartsAt=9*3600+600,EndsAt=9*3600+900,
                    Place=new AppointmentPlace{Kind="building",SettlementId=10,BuildingKey=21}};
                Check.True(AppointmentDayPlanner.TryCreate(resident,routine,0,new[]{booking},360,1380,out var schedule,out var reason),reason);
                var actual=schedule.ScheduledVisits().Find(v=>v.TargetBuildingKey==21);
                Check.True(actual.VisitIndex>=2,"appointment identity aliases the omitted routine stop");
                Check.True(RoutineRecoveryPlanner.TryCreate(10,resident,routine,schedule,new[]{booking},0,8*3600+3000,20,
                    8*3600+3000,360,1380,1,out var recovery,out reason),reason);
                Check.True(recovery.SkippedStopIndices.Contains(0));
                Check.Equal("replace",recovery.Legs[0].AppointmentId);
            }));
            cases.Add(("Appointment graph cannot connect different entrances for the same later-outing building",()=>{
                var resident=new Resident{Id="person",HomeKey=20,IsAlive=true,AgeGroup=ResidentAgeGroup.Adult};
                var routine=Routine(resident.Id);var extra=routine.AdditionalOutings[0].Itinerary;
                extra.Stops[0].BuildingKey=21;extra.Stops[0].Route[1].X=40;extra.ExitRoute[0].X=40;
                Check.True(!AppointmentDayPlanner.TryCreate(resident,routine,0,new AppointmentRecord[0],360,1380,
                    out var schedule,out var reason),"inconsistent building anchors must not invent a connector");
                Check.True(schedule==null);
            }));
            cases.Add(("Recovery skips later optional stops with cumulative identities and retains home-route provenance",()=>{
                var store=new WorldStateStore(7);var resident=store.GetOrCreateFamilies(10,new[]{new HomeProfile(20,3,220)}).Households[0].Residents[0];
                var routine=Routine(resident.Id);var first=routine.Itinerary;
                first.Stops.Add(new ResidentVisitStop{BuildingKey=23,Purpose=ResidentVisitPurpose.Shop,OpenMinute=0,CloseMinute=1439,
                    WalkSeconds=300,DwellSeconds=600,Route=new List<SavedRoutePoint>{new SavedRoutePoint{X=10},new SavedRoutePoint{X=15}}});
                first.ExitRoute[0].X=15;
                var extra=routine.AdditionalOutings[0].Itinerary;extra.Stops[0].Route[1].Z=10;extra.ExitRoute[0].Z=10;
                store.SetRoutines(new TownRoutineState{SettlementId=10,ItineraryVersion=2,Residents=new List<ResidentRoutine>{routine}});
                var ordinary=ResidentRoutineDay.Schedule(20,routine,360,1380);
                Check.True(RoutineRecoveryPlanner.TryCreate(10,resident,routine,ordinary,new AppointmentRecord[0],0,17*3600,20,
                    17*3600,360,1380,1,out var plan,out var reason),reason);
                Check.Equal(1,plan.Legs.Count);Check.True(plan.Legs[0].ReturnsHome);
                Check.Equal(20,plan.Legs[0].BuildingKey);Check.Equal(-1,plan.Legs[0].OriginalStopIndex);
                Check.True(plan.SkippedStopIndices.Contains(0) && plan.SkippedStopIndices.Contains(1) &&
                    plan.SkippedStopIndices.Contains(2));
                store.SaveRoutineRecovery(plan);store.Restore(store.Snapshot());
                Check.True(store.HasRoutineRecovery(resident.Id));
                plan.Legs[0].Route[0].Z=3;
                Check.Throws<ArgumentException>(()=>store.SaveRoutineRecovery(plan));
            }));
            cases.Add(("A required appointment uses later outing routes and preserves nonconflicting morning activity",()=>{
                var resident=new Resident{Id="person",HomeKey=20,IsAlive=true,AgeGroup=ResidentAgeGroup.Adult};
                var routine=Routine(resident.Id);
                var booking=new AppointmentRecord{Id="evening",ResidentId=resident.Id,Purpose="meet",Status="accepted",
                    StartsAt=18*3600+300,EndsAt=18*3600+900,
                    Place=new AppointmentPlace{Kind="building",SettlementId=10,BuildingKey=22}};
                Check.True(AppointmentDayPlanner.TryCreate(resident,routine,0,new[]{booking},360,1380,out var schedule,out var reason),reason);
                Check.Equal(21,schedule.EvaluateSeconds(9*3600+600).BuildingKey);
                Check.Equal(22,schedule.EvaluateSeconds(18*3600+400).BuildingKey);
                Check.Equal(2,schedule.ScheduledVisits().Count);
                Check.Equal(Activity.Home,schedule.EvaluateSeconds(18*3600+1500).Activity);
            }));
            cases.Add(("Appointment composition keeps whole nonconflicting outings and their global visit identities",()=>{
                var resident=new Resident{Id="person",HomeKey=20,IsAlive=true,AgeGroup=ResidentAgeGroup.Adult};
                Check.True(AppointmentDayPlanner.TryCreate(resident,Routine(resident.Id),0,new AppointmentRecord[0],360,1380,
                    out var schedule,out var reason),reason);
                Check.Equal(2,schedule.ScheduledVisits().Count);
                Check.Equal(1,schedule.ScheduledVisits()[1].VisitIndex);
                Check.Equal(Activity.Travel,schedule.EvaluateSeconds(18*3600+60).Activity);
                Check.Equal(Activity.Home,schedule.EvaluateSeconds(13*3600).Activity);
            }));
            cases.Add(("Recovery skips completed morning and later optional visits before returning home",()=>{
                var resident=new Resident{Id="person",HomeKey=20,IsAlive=true,AgeGroup=ResidentAgeGroup.Adult};
                var routine=Routine(resident.Id);var ordinary=ResidentRoutineDay.Schedule(20,routine,360,1380);
                Check.True(RoutineRecoveryPlanner.TryCreate(10,resident,routine,ordinary,new AppointmentRecord[0],0,17*3600,20,
                    17*3600,360,1380,1,out var plan,out var reason),reason);
                Check.Equal(1,plan.Legs.Count);Check.Equal(20,plan.Legs[0].BuildingKey);
                Check.Equal(-1,plan.Legs[0].OriginalStopIndex);
                Check.True(plan.SkippedStopIndices.Contains(0) && plan.SkippedStopIndices.Contains(1));
                Check.True(plan.Legs[0].ReturnsHome);
            }));
            cases.Add(("Personal route rebuild requires every outing leg and retains every departure",()=>{
                var routine=Routine("person");var routes=new List<List<SavedRoutePoint>>{
                    routine.Itinerary.Stops[0].Route,routine.Itinerary.ExitRoute,
                    routine.AdditionalOutings[0].Itinerary.Stops[0].Route,routine.AdditionalOutings[0].Itinerary.ExitRoute};
                var rebuilt=OutdoorRoutineMigration.Rebuild(routine,routes,1,2,true);
                Check.Equal(1,rebuilt.AdditionalOutings.Count);Check.Equal(18*3600d,rebuilt.AdditionalOutings[0].DepartureSeconds);
                Check.Equal(20d,rebuilt.AdditionalOutings[0].Itinerary.Stops[0].WalkSeconds);
                rebuilt.AdditionalOutings[0].Itinerary.Stops[0].Route[1].X=8;
                Check.Equal(10d,routine.AdditionalOutings[0].Itinerary.Stops[0].Route[1].X);
                routes.RemoveAt(3);Check.Throws<ArgumentException>(()=>OutdoorRoutineMigration.Rebuild(routine,routes,1,2,true));
            }));
            cases.Add(("Dialogue recurring plan includes the later departure and intermediate home return",()=>{
                string context=ConversationLifeContext.Routine(Routine("person"),key=>key==21?"Market":"Evening Inn");
                Check.True(context.Contains("Market") && context.Contains("Evening Inn") && context.Contains("18:00"),context);
            }));
            cases.Add(("Observed visits may belong to later outings but never to invented destinations",()=>{
                var store=new WorldStateStore(7);var population=store.GetOrCreate(10,new[]{20},1);
                string id=population.Households[0].Residents[0].Id;
                store.SetRoutines(new TownRoutineState{SettlementId=10,ItineraryVersion=2,Residents=new List<ResidentRoutine>{Routine(id)}});
                var visit=new ObservedVisit{ResidentId=id,SettlementId=10,BuildingKey=22,ObservedAt=18*3600+300,
                    PlannedEndAt=18*3600+2100,ResumeUntil=19*3600,SecondsPerMetre=1,
                    Legs=new List<ObservedVisitLeg>{new ObservedVisitLeg{AfterPhase=ShopVisitPhase.Complete,
                        Route=new List<GuardPoint3>{new GuardPoint3{X=10},new GuardPoint3{X=11}}}}};
                store.StoreObservedVisit(visit);store.Restore(store.Snapshot());
                Check.Equal(22,store.GetObservedVisit(id).BuildingKey);
                visit.BuildingKey=999;
                Check.Throws<ArgumentException>(()=>store.StoreObservedVisit(visit));
                Check.Equal(22,store.GetObservedVisit(id).BuildingKey);
            }));
            cases.Add(("Recovery identity includes later outings",()=>{
                var routine=Routine("person");
                string before=RoutineRecoveryIdentity.Fingerprint(routine,20,360,1380);
                routine.AdditionalOutings[0].DepartureSeconds+=60;
                Check.True(before!=RoutineRecoveryIdentity.Fingerprint(routine,20,360,1380),"later activity changes must invalidate old recovery sources");
            }));
            cases.Add(("Service-hour refresh and legacy retiming cannot discard later outings",()=>{
                var source=Routine("person");
                var refreshed=DailyServiceHours.Refresh(source,key=>key==21?new DailyServiceWindow(420,1320):new DailyServiceWindow(960,1380));
                Check.Equal(1,refreshed.AdditionalOutings.Count);
                Check.Equal(960,refreshed.AdditionalOutings[0].Itinerary.Stops[0].OpenMinute);
                Check.Equal(0,source.AdditionalOutings[0].Itinerary.Stops[0].OpenMinute);
                Check.True(ReferenceEquals(source,DailyOutingDistribution.Spread(source,123,6,23)),
                    "one-outing retiming must defer a composed day instead of dropping later activities");
            }));
            cases.Add(("Several saved outings leave home between morning and evening visits",()=>{
                var routine=Routine("person");
                var schedule=ResidentRoutineDay.Schedule(20,routine,360,1380);
                Check.Equal(Activity.Visit,schedule.EvaluateSeconds(9*3600+600).Activity);
                Check.Equal(21,schedule.EvaluateSeconds(9*3600+600).BuildingKey);
                Check.Equal(Activity.Home,schedule.EvaluateSeconds(13*3600).Activity);
                Check.Equal(Activity.Visit,schedule.EvaluateSeconds(18*3600+600).Activity);
                Check.Equal(22,schedule.EvaluateSeconds(18*3600+600).BuildingKey);
                Check.Equal(Activity.Home,schedule.EvaluateSeconds(22*3600).Activity);
                var visits=schedule.ScheduledVisits();Check.Equal(2,visits.Count);
                Check.Equal(0,visits[0].VisitIndex);Check.Equal(1,visits[1].VisitIndex);
            }));
            cases.Add(("Multi-outing persistence and dialogue reads preserve detached day plans",()=>{
                var store=new WorldStateStore(7);var population=store.GetOrCreate(10,new[]{20},1);
                string id=population.Households[0].Residents[0].Id;
                var routine=Routine(id);
                store.SetRoutines(new TownRoutineState {SettlementId=10,ItineraryVersion=2,Residents=new List<ResidentRoutine>{routine}});
                routine.AdditionalOutings[0].Itinerary.Stops[0].Route[0].X=999;
                Check.Equal(0d,store.GetRoutines(10).Residents[0].AdditionalOutings[0].Itinerary.Stops[0].Route[0].X);
                var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());
                var read=restored.GetResidentDialogueRoutine(10,id);
                Check.Equal(1,read.AdditionalOutings.Count);
                Check.Equal(22,read.AdditionalOutings[0].Itinerary.Stops[0].BuildingKey);
                read.AdditionalOutings.Clear();Check.Equal(1,restored.GetResidentDialogueRoutine(10,id).AdditionalOutings.Count);
                var bad=restored.Snapshot();bad.Version=61;
                Check.Throws<ArgumentException>(()=>restored.Restore(bad));
                Check.Equal(1,restored.GetRoutines(10).Residents[0].AdditionalOutings.Count);
            }));
            cases.Add(("Outings reject overlap, a different home origin and missing primary itinerary",()=>{
                var routine=Routine("person");routine.AdditionalOutings[0].DepartureSeconds=9*3600+600;
                Check.Throws<ArgumentException>(()=>ResidentRoutineDay.CopyOutings(routine));
                routine=Routine("person");routine.AdditionalOutings[0].Itinerary.Stops[0].Route[0].X=7;
                routine.AdditionalOutings[0].Itinerary.ExitRoute[1].X=7;
                Check.Throws<ArgumentException>(()=>ResidentRoutineDay.CopyOutings(routine));
                routine=Routine("person");routine.Itinerary=null;
                Check.Throws<ArgumentException>(()=>ResidentRoutineDay.CopyOutings(routine));
            }));
            cases.Add(("At most five outings fit one waking day and cannot reserve overnight inns",()=>{
                var routine=Routine("person");
                for(int i=0;i<4;i++)routine.AdditionalOutings.Add(new ResidentDailyOuting{DepartureSeconds=(19+i)*3600,Itinerary=Itinerary(23+i)});
                Check.Throws<ArgumentException>(()=>ResidentRoutineDay.CopyOutings(routine));
                routine=Routine("person");routine.AdditionalOutings[0].Itinerary.Stops[0].Purpose=ResidentVisitPurpose.Inn;
                Check.Throws<ArgumentException>(()=>ResidentRoutineDay.CopyOutings(routine));
                routine=Routine("person");
                Check.Throws<ArgumentException>(()=>ResidentRoutineDay.Schedule(20,routine,360,1080));
            }));
        }
        private static ResidentRoutine Routine(string id)
        {
            var first=Itinerary(21);
            return new ResidentRoutine{ResidentId=id,DestinationKey=21,OpenMinute=0,CloseMinute=1439,
                Route=first.Stops[0].Route,Itinerary=first,ItineraryDepartureSeconds=9*3600,
                AdditionalOutings=new List<ResidentDailyOuting>{new ResidentDailyOuting{DepartureSeconds=18*3600,Itinerary=Itinerary(22)}}};
        }
        private static ResidentDestination Itinerary(int building)
        {
            return new ResidentDestination{Stops=new List<ResidentVisitStop>{new ResidentVisitStop{BuildingKey=building,
                Purpose=ResidentVisitPurpose.Social,OpenMinute=0,CloseMinute=1439,WalkSeconds=300,DwellSeconds=1800,
                Route=new List<SavedRoutePoint>{new SavedRoutePoint(),new SavedRoutePoint{X=10}}}},
                ExitWalkSeconds=300,ExitRoute=new List<SavedRoutePoint>{new SavedRoutePoint{X=10},new SavedRoutePoint()}};
        }
    }
}
