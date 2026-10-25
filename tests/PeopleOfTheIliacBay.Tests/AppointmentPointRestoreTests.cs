using System;
using System.Collections.Generic;
using System.Text.Json;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class AppointmentPointRestoreTests
    {
        private const int SettlementId=31;
        private static SavedRoutePoint P(double x,double z=0)=>new SavedRoutePoint {X=x,Z=z};

        private sealed class FixtureData
        {
            public WorldStateStore Store;
            public Resident Alice,Bob;
            public ResidentRoutine AliceRoutine,BobRoutine;
            public AppointmentRecord AliceBooking,BobBooking;
            public RoutineRecoveryPlan AlicePlan;
        }

        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Point recovery restore rejects another resident's matching appointment proof atomically",RejectCrossResidentProof));
            cases.Add(("Stale routine recovery still rejects a foreign appointment point proof atomically",RejectStaleCrossResidentProof));
            cases.Add(("Stale recovery with its cancelled historical point booking restores inertly",RestoreStaleCancelledPointProof));
            cases.Add(("Point recovery restore rejects a noncurrent date or cancelled point booking",RejectNonmemberPointProof));
            cases.Add(("Point recovery restore detaches route proof data from its input snapshot",RestoreOwnsPointProof));
            cases.Add(("Corrupt point endpoint restore leaves existing state unchanged",RejectCorruptEndpointAtomically));
            cases.Add(("Point proof checks do not break legacy building-only recovery restore",BuildingOnlyRecoveryStillRestores));
            cases.Add(("Persisted exact point coordinates use stable native 15-digit doubles",NativePointCoordinateCanonicalization));
        }

        private static void NativePointCoordinateCanonicalization()
        {
            var fixture=CreateFixture(false);
            const double rawX=677.28997802734375;
            const double rawY=0.12345678901234567;
            const double rawZ=712.61236572265625;
            var incoming=fixture.Store.Snapshot();
            var point=incoming.Appointments.Find(item=>item.Id==fixture.AliceBooking.Id).Place;
            point.X=rawX;point.Y=rawY;point.Z=rawZ;
            fixture.Store.Restore(incoming);
            var saved=fixture.Store.FindAppointment(fixture.AliceBooking.Id).Place;
            double expectedX=AppointmentPlace.CanonicalWorldPointCoordinate(rawX);
            double expectedY=AppointmentPlace.CanonicalWorldPointCoordinate(rawY);
            double expectedZ=AppointmentPlace.CanonicalWorldPointCoordinate(rawZ);
            Check.True(rawX!=expectedX || rawY!=expectedY || rawZ!=expectedZ,
                "Fixture should exercise precision lost by native 15-significant-digit output.");
            Check.Equal(expectedX,saved.X);Check.Equal(expectedY,saved.Y);Check.Equal(expectedZ,saved.Z);
            Check.Equal(expectedX,AppointmentPlace.CanonicalWorldPointCoordinate(saved.X));
            Check.Equal(expectedY,AppointmentPlace.CanonicalWorldPointCoordinate(saved.Y));
            Check.Equal(expectedZ,AppointmentPlace.CanonicalWorldPointCoordinate(saved.Z));

            var json=JsonSerializer.Serialize(fixture.Store.Snapshot(),new JsonSerializerOptions{IncludeFields=true});
            var restored=new WorldStateStore(0);
            restored.Restore(JsonSerializer.Deserialize<WorldState>(json,new JsonSerializerOptions{IncludeFields=true}));
            var disk=restored.FindAppointment(fixture.AliceBooking.Id).Place;
            Check.Equal(saved.X,disk.X);Check.Equal(saved.Y,disk.Y);Check.Equal(saved.Z,disk.Z);
        }

        private static FixtureData CreateFixture(bool saveRecovery=true)
        {
            var data=new FixtureData {Store=new WorldStateStore(42)};
            var town=data.Store.GetOrCreateFamilies(SettlementId,new[] {
                new HomeProfile(10,3,220),new HomeProfile(20,3,220)});
            data.Alice=Adult(town.Households[0]);data.Bob=Adult(town.Households[1]);
            data.AliceRoutine=Routine(data.Alice.Id);
            data.BobRoutine=Routine(data.Bob.Id);
            data.Store.SetRoutines(new TownRoutineState {SettlementId=SettlementId,ItineraryVersion=1,
                Residents=new List<ResidentRoutine>{data.AliceRoutine,data.BobRoutine}});
            data.AliceBooking=Booking("alice-point",data.Alice.Id);
            data.BobBooking=Booking("bob-point",data.Bob.Id);
            // Keep the second resident's record outside the first booking window;
            // its identical location is enough to exercise foreign proof binding.
            data.BobBooking.StartsAt=12*3600;
            data.BobBooking.EndsAt=12*3600+600;
            var world=data.Store.Snapshot();
            world.Appointments.Add(AppointmentRecord.Copy(data.AliceBooking));
            world.Appointments.Add(AppointmentRecord.Copy(data.BobBooking));
            data.Store.Restore(world);
            data.AliceBooking=data.Store.FindAppointment(data.AliceBooking.Id);
            data.BobBooking=data.Store.FindAppointment(data.BobBooking.Id);
            data.AliceRoutine=data.Store.GetRoutines(SettlementId).Residents.Find(r=>r.ResidentId==data.Alice.Id);
            data.BobRoutine=data.Store.GetRoutines(SettlementId).Residents.Find(r=>r.ResidentId==data.Bob.Id);
            data.AlicePlan=CreatePointPlan(data.Store,data.Alice,data.AliceRoutine,data.AliceBooking);
            if(saveRecovery)data.Store.SaveRoutineRecovery(data.AlicePlan);
            return data;
        }

        private static Resident Adult(Household home)
        {
            var result=home.Residents.Find(resident=>resident.AgeGroup==ResidentAgeGroup.Adult);
            Check.True(result!=null,"Fixture needs a managed adult resident.");
            return result;
        }

        private static ResidentRoutine Routine(string residentId)
        {
            return new ResidentRoutine {ResidentId=residentId,ItineraryDepartureSeconds=8*3600,
                Itinerary=new ResidentDestination {Stops=new List<ResidentVisitStop> {
                    new ResidentVisitStop {BuildingKey=30,Purpose=ResidentVisitPurpose.Social,
                        WalkSeconds=100,DwellSeconds=600,
                        Route=new List<SavedRoutePoint>{P(0),P(100)}}},
                    ExitRoute=new List<SavedRoutePoint>{P(100),P(0)},ExitWalkSeconds=100}};
        }

        private static AppointmentRecord Booking(string id,string residentId)
        {
            return new AppointmentRecord {Id=id,ResidentId=residentId,Purpose="meet",StartsAt=10*3600,
                EndsAt=10*3600+600,PatienceSeconds=600,Status="accepted",
                Place=new AppointmentPlace {Kind="world-point",SettlementId=SettlementId,BuildingKey=-1,
                    X=150,Y=0,Z=30,Detail="a captured meeting point"}};
        }

        private static AppointmentPlaceRoute Route(AppointmentRecord booking)
        {
            return new AppointmentPlaceRoute {AppointmentId=booking.Id,
                Place=AppointmentPlace.Copy(booking.Place),
                Connections=new List<AppointmentPlaceRouteConnection> {
                    new AppointmentPlaceRouteConnection {AnchorBuildingKey=30,
                        Route=new List<SavedRoutePoint>{P(100),P(125,15),P(150,30)}}}};
        }

        private static RoutineRecoveryPlan CreatePointPlan(WorldStateStore store,Resident resident,
            ResidentRoutine routine,AppointmentRecord booking)
        {
            var ordinary=DailySchedule.ItineraryDay(resident.HomeKey,routine.Itinerary,
                routine.ItineraryDepartureSeconds,360,1320);
            bool created=RoutineRecoveryPlanner.TryCreate(SettlementId,resident,routine,ordinary,
                store.AppointmentsFor(resident.Id),new[]{Route(booking)},0,9*3600,resident.HomeKey,
                9*3600,360,1320,1,out var plan,out var reason,out var failure);
            Check.True(created,"Fixture point recovery plan failed: "+reason+" / "+failure);
            return plan;
        }

        private static void RejectCrossResidentProof()
        {
            var fixture=CreateFixture();
            var before=Json(fixture.Store.Snapshot());
            var corrupt=fixture.Store.Snapshot();
            var recovery=corrupt.RoutineRecoveries.Find(plan=>plan.ResidentId==fixture.Alice.Id);
            var leg=recovery.Legs.Find(item=>item.PointDestination);
            Check.True(leg!=null,"Fixture lacks the point-destination leg.");
            leg.AppointmentId=fixture.BobBooking.Id;
            recovery.AppointmentPointRoutes[0].AppointmentId=fixture.BobBooking.Id;

            Check.Throws<ArgumentException>(()=>fixture.Store.Restore(corrupt));
            Check.Equal(before,Json(fixture.Store.Snapshot()));
        }

        private static void RejectStaleCrossResidentProof()
        {
            var fixture=CreateFixture();
            var before=Json(fixture.Store.Snapshot());
            var corrupt=fixture.Store.Snapshot();
            var recovery=corrupt.RoutineRecoveries.Find(plan=>plan.ResidentId==fixture.Alice.Id);
            recovery.Legs.Find(item=>item.PointDestination).AppointmentId=fixture.BobBooking.Id;
            recovery.AppointmentPointRoutes[0].AppointmentId=fixture.BobBooking.Id;
            // A different valid routine makes the stored recovery inert today. The
            // malformed proof must still be rejected so it cannot revive later.
            corrupt.Routines.Find(item=>item.SettlementId==SettlementId).Residents
                .Find(item=>item.ResidentId==fixture.Alice.Id).ItineraryDepartureSeconds+=1;

            Check.Throws<ArgumentException>(()=>fixture.Store.Restore(corrupt));
            Check.Equal(before,Json(fixture.Store.Snapshot()));
        }

        private static void RestoreStaleCancelledPointProof()
        {
            var fixture=CreateFixture();
            var incoming=fixture.Store.Snapshot();
            var booking=incoming.Appointments.Find(item=>item.Id==fixture.AliceBooking.Id);
            booking.Status="cancelled";
            booking.OutcomeRecorded=true;
            booking.ResolvedAt=booking.StartsAt+1;
            incoming.Routines.Find(item=>item.SettlementId==SettlementId).Residents
                .Find(item=>item.ResidentId==fixture.Alice.Id).ItineraryDepartureSeconds+=1;

            fixture.Store.Restore(incoming);
            var currentRoutine=fixture.Store.GetRoutines(SettlementId).Residents
                .Find(item=>item.ResidentId==fixture.Alice.Id);
            var currentAppointments=fixture.Store.AppointmentsFor(fixture.Alice.Id);
            var currentAppointmentFingerprint=RoutineRecoveryIdentity.Appointments(currentAppointments,
                fixture.Alice.Id,fixture.AlicePlan.AbsoluteDay);
            Check.True(fixture.Store.GetRoutineRecovery(fixture.Alice.Id,fixture.AlicePlan.AbsoluteDay,
                RoutineRecoveryIdentity.Fingerprint(currentRoutine,fixture.Alice.HomeKey,
                    fixture.AlicePlan.WakeMinute,fixture.AlicePlan.SleepMinute),currentAppointmentFingerprint)==null,
                "The cancelled point recovery must remain inert against the changed routine.");
        }

        private static void RejectNonmemberPointProof()
        {
            var fixture=CreateFixture();
            var before=Json(fixture.Store.Snapshot());
            var later=Booking("alice-next-day",fixture.Alice.Id);
            later.StartsAt+=86400;later.EndsAt+=86400;
            var cancelled=Booking("alice-cancelled",fixture.Alice.Id);
            cancelled.StartsAt=13*3600;cancelled.EndsAt=cancelled.StartsAt+600;
            cancelled.Status="cancelled";cancelled.OutcomeRecorded=true;cancelled.ResolvedAt=cancelled.StartsAt+1;

            foreach(var alternate in new[]{later,cancelled})
            {
                var corrupt=fixture.Store.Snapshot();
                corrupt.Appointments.Add(AppointmentRecord.Copy(alternate));
                var recovery=corrupt.RoutineRecoveries.Find(plan=>plan.ResidentId==fixture.Alice.Id);
                recovery.Legs.Find(item=>item.PointDestination).AppointmentId=alternate.Id;
                recovery.AppointmentPointRoutes[0].AppointmentId=alternate.Id;
                Check.Throws<ArgumentException>(()=>fixture.Store.Restore(corrupt));
                Check.Equal(before,Json(fixture.Store.Snapshot()));
            }
        }

        private static void RestoreOwnsPointProof()
        {
            var fixture=CreateFixture();
            var incoming=fixture.Store.Snapshot();
            var recovery=incoming.RoutineRecoveries[0];
            var expectedX=recovery.AppointmentPointRoutes[0].Place.X;
            var expectedRouteX=recovery.AppointmentPointRoutes[0].Connections[0].Route[0].X;
            var restored=new WorldStateStore(0);
            restored.Restore(incoming);

            recovery.AppointmentPointRoutes[0].Place.X+=500;
            recovery.AppointmentPointRoutes[0].Connections[0].Route[0].X+=500;
            recovery.Legs.Find(item=>item.PointDestination).Route[0].X+=500;
            var found=restored.GetRoutineRecovery(fixture.Alice.Id,0,recovery.RoutineFingerprint,recovery.AppointmentFingerprint);
            Check.True(found!=null);
            Check.Near(expectedX,found.AppointmentPointRoutes[0].Place.X);
            Check.Near(expectedRouteX,found.AppointmentPointRoutes[0].Connections[0].Route[0].X);
            var foundLeg=found.Legs.Find(item=>item.PointDestination);
            Check.Near(expectedX,foundLeg.Route[foundLeg.Route.Count-1].X);
            Check.True(RoutineRecoveryRoutes.Matches(found,fixture.AliceRoutine,fixture.Alice.HomeKey,
                restored.AppointmentsFor(fixture.Alice.Id)));
        }

        private static void RejectCorruptEndpointAtomically()
        {
            var fixture=CreateFixture();
            var before=Json(fixture.Store.Snapshot());
            var corrupt=fixture.Store.Snapshot();
            var proof=corrupt.RoutineRecoveries[0].AppointmentPointRoutes[0];
            proof.Connections[0].Route[proof.Connections[0].Route.Count-1].X+=1;

            Check.Throws<ArgumentException>(()=>fixture.Store.Restore(corrupt));
            Check.Equal(before,Json(fixture.Store.Snapshot()));
        }

        private static void BuildingOnlyRecoveryStillRestores()
        {
            var store=new WorldStateStore(42);
            var home=store.GetOrCreateFamilies(SettlementId,new[]{new HomeProfile(10,3,220)}).Households[0];
            var resident=Adult(home);
            var routine=Routine(resident.Id);
            store.SetRoutines(new TownRoutineState {SettlementId=SettlementId,ItineraryVersion=1,
                Residents=new List<ResidentRoutine>{routine}});
            routine=store.GetRoutines(SettlementId).Residents.Find(item=>item.ResidentId==resident.Id);
            var ordinary=DailySchedule.ItineraryDay(resident.HomeKey,routine.Itinerary,
                routine.ItineraryDepartureSeconds,360,1320);
            Check.True(RoutineRecoveryPlanner.TryCreate(SettlementId,resident,routine,ordinary,
                new AppointmentRecord[0],null,0,9*3600,resident.HomeKey,9*3600,360,1320,1,
                out var plan,out var reason,out var failure),reason+" / "+failure);
            Check.Equal(0,plan.AppointmentPointRoutes.Count);
            store.SaveRoutineRecovery(plan);
            var json=Json(store.Snapshot());
            var restored=new WorldStateStore(0);
            restored.Restore(JsonSerializer.Deserialize<WorldState>(json,new JsonSerializerOptions {IncludeFields=true}));
            Check.True(restored.GetRoutineRecovery(resident.Id,plan.AbsoluteDay,
                plan.RoutineFingerprint,plan.AppointmentFingerprint)!=null);
        }

        private static string Json(WorldState world)
        {return JsonSerializer.Serialize(world,new JsonSerializerOptions {IncludeFields=true});}
    }
}
