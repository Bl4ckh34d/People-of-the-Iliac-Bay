using System;
using System.Collections.Generic;
using System.Linq;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class AppointmentObservationTests
    {
        private static AppointmentRecord Booking(string id,string resident,double startsAt) => new AppointmentRecord
        {
            Id=id,ResidentId=resident,Purpose="meet",StartsAt=startsAt,EndsAt=startsAt+60,GraceSeconds=30,PatienceSeconds=600,
            Place=new AppointmentPlace {Kind="building",SettlementId=100,BuildingKey=20}
        };

        private static void PendingObservationsTrackLedgerLifecycleAndStayDetached()
        {
            var store=new WorldStateStore(42);
            var person=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0];
            Check.True(store.TryReserveAppointment(Booking("cancelled",person.Id,1000),500,out var reason),reason);
            Check.True(store.TryReserveAppointment(Booking("resolved",person.Id,2000),500,out reason),reason);
            var observed=new List<PendingAppointmentObservation>();
            store.VisitPendingAppointmentObservations(100,observed.Add);
            Check.Equal(2,observed.Count);
            var captured=observed[1];
            Check.Equal("resolved",captured.Id);
            Check.Equal(person.Id,captured.ResidentId);
            Check.Equal("building",captured.PlaceKind);
            Check.Equal(100,captured.SettlementId);
            Check.Equal(20,captured.BuildingKey);
            Check.Equal(store.FindAppointment("resolved").PatienceSeconds,captured.PatienceSeconds);

            Check.True(store.CancelAppointment("cancelled",600,"changed plans"));
            store.ObserveAppointment("resolved",2000,false,false,false);
            store.ObserveAppointment("resolved",AppointmentTiming.ArrivalDeadline(store.FindAppointment("resolved"))+1,false,false,false);
            observed.Clear();
            store.VisitPendingAppointmentObservations(100,observed.Add);
            Check.Equal(0,observed.Count);
            Check.Equal("resolved",captured.Id);
            Check.Equal(20,captured.BuildingKey);

            var restored=new WorldStateStore(7);
            restored.Restore(store.Snapshot());
            observed.Clear();
            restored.VisitPendingAppointmentObservations(100,observed.Add);
            Check.Equal(0,observed.Count);

            var snapshot=restored.Snapshot();
            snapshot.Appointments[1].Place.BuildingKey=99;
            Check.True(restored.TryReserveAppointment(Booking("new",person.Id,4000),500,out reason),reason);
            observed.Clear();
            restored.VisitPendingAppointmentObservations(100,observed.Add);
            Check.Equal(1,observed.Count);
            Check.Equal("new",observed[0].Id);
            Check.Equal(20,observed[0].BuildingKey);
            observed.Clear();
            restored.VisitPendingAppointmentObservations(101,observed.Add);
            Check.Equal(0,observed.Count);
        }

        private static void PendingObservationCallbackMutationsDoNotSkipDuplicateOrExtendVisit()
        {
            var store=new WorldStateStore(43);
            var person=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0];
            Check.True(store.TryReserveAppointment(Booking("first",person.Id,1000),500,out var reason),reason);
            Check.True(store.TryReserveAppointment(Booking("second",person.Id,2000),500,out reason),reason);
            var visited=new List<string>();
            store.VisitPendingAppointmentObservations(100,booking=>
            {
                visited.Add(booking.Id);
                if(booking.Id!="first")return;
                Check.True(store.CancelAppointment("second",600,"callback mutation"));
                Check.True(store.TryReserveAppointment(Booking("appended",person.Id,3000),600,out var reserveReason),reserveReason);
            });
            Check.Equal(1,visited.Count);
            Check.Equal("first",visited[0]);

            visited.Clear();
            store.VisitPendingAppointmentObservations(100,booking=>visited.Add(booking.Id));
            Check.Equal(2,visited.Count);
            Check.Equal("first",visited[0]);
            Check.Equal("appended",visited[1]);
        }

        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Pending appointment observations follow booking lifecycle and expose detached scalar values",PendingObservationsTrackLedgerLifecycleAndStayDetached));
            cases.Add(("Pending appointment visitor is stable while its callback cancels and appends records",PendingObservationCallbackMutationsDoNotSkipDuplicateOrExtendVisit));
        }
    }
}
