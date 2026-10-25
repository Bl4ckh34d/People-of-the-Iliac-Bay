using System;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public static class RoutineRecoveryNativeProbe
    {
        // This checks the actual native serializer, not movement integration.
        // Only a detached copy of the explicitly owned fixture is changed.
        public static void Verify(WorldState checkpoint,string residentId,int settlement,int origin)
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-task-failure-probe")<0)
                throw new InvalidOperationException("Routine recovery serializer probe requires the owned task fixture.");
            var store=new WorldStateStore(0,PeopleOfTheIliacBayMod.ConversationArchiveRoot());store.Restore(checkpoint);
            var person=store.ResolveResident(settlement,residentId);
            var town=store.GetRoutines(settlement);
            var routine=town==null?null:town.Residents.Find(r=>r.ResidentId==residentId);
            Require(person!=null&&routine!=null&&routine.Itinerary!=null,"Native recovery fixture has no saved itinerary.");
            const int wake=0,sleep=1439;
            var ordinary=DailySchedule.ItineraryDay(person.HomeKey,routine.Itinerary,routine.ItineraryDepartureSeconds,wake,sleep);
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();int day=(int)Math.Floor(now/86400d);
            RoutineRecoveryPlan recovery;string reason;
            Require(RoutineRecoveryPlanner.TryCreate(settlement,person,routine,ordinary,store.AppointmentsFor(residentId),day,now,origin,now,
                wake,sleep,DaggerfallUnity.Instance.WorldTime.TimeScale/ResidentMovement.WalkSpeedFor(person.AppearanceSeed),
                out recovery,out reason),"Native continuation planning failed: "+reason);
            store.SaveRoutineRecovery(recovery);
            var serialized=SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot());
            var restored=new WorldStateStore(0,PeopleOfTheIliacBayMod.ConversationArchiveRoot());
            restored.Restore((WorldState)SaveLoadManager.Deserialize(typeof(WorldState),serialized));
            var savedRoutine=restored.GetRoutines(settlement).Residents.Find(r=>r.ResidentId==residentId);
            string fingerprint=RoutineRecoveryIdentity.Fingerprint(savedRoutine,person.HomeKey,wake,sleep);
            var found=restored.GetRoutineRecovery(residentId,day,fingerprint,
                RoutineRecoveryIdentity.Appointments(restored.AppointmentsFor(residentId),residentId,day));
            Require(found!=null&&found.Legs.Count==recovery.Legs.Count&&found.SkippedStopIndices.Count==recovery.SkippedStopIndices.Count,
                "Native serialization lost or invalidated the per-stop continuation.");
            var schedule=DailySchedule.ContinueDay(ordinary,found);
            Require(Math.Abs(schedule.ReturnHomeAt(now)-found.EndsAt)<0.001&&
                restored.GetRoutineRecovery(residentId,day+1,fingerprint,found.AppointmentFingerprint)==null,
                "Native continuation moved its home deadline or applied to another day.");
            for(int i=0;i<found.Legs.Count;i++)
            {
                var actual=found.Legs[i];var expected=recovery.Legs[i];
                Require(actual.BuildingKey==expected.BuildingKey&&actual.OriginalStopIndex==expected.OriginalStopIndex&&
                    actual.Route.Count==expected.Route.Count&&Math.Abs(actual.EnterAt-expected.EnterAt)<0.001&&
                    Math.Abs(actual.LeaveAt-expected.LeaveAt)<0.001,"Native continuation changed leg identity, route, or times.");
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ROUTINE_RECOVERY_SERIALIZER_PASS: schema30 exact-remaining-stops connected-saved-routes same-day-only; movement integration pending");
        }
        private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    }
}
