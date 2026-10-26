using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // One room owner advances both real parents. Payment remains the ordinary
    // native counter callback; this owner grants no service from elapsed time.
    internal sealed class HouseholdJointInnMotion
    {
        private readonly WorldStateStore store;
        private readonly HouseholdDayPlan source;
        private ResidentActor first,second;
        private readonly DaggerfallInterior room;
        private readonly InteriorGeometry geometry;
        private readonly ShopVisitGeometry activities;
        private readonly Func<ResidentActor,PhysicalShopVisit,double,bool> purchase;
        private readonly IList<AppointmentPlaceRoute> pointRoutes;
        private SelectedSeatPairAccess seats;
        private bool exiting,rejected;
        private Vector3 expectedFirst,expectedSecond;
        internal PhysicalShopVisit FirstVisit {get;private set;}
        internal PhysicalShopVisit SecondVisit {get;private set;}
        internal bool Complete {get;private set;}
        internal bool Exiting {get{return exiting;}}
        internal bool RequiresRecovery {get{return rejected;}}
        internal void PauseForCash(double now)
        {Capture(now);FirstVisit.Pause(now);SecondVisit.Pause(now);}
        internal void ResumeAfterCash(ResidentActor returning,double now)
        {
            if(!returning || returning.transform.parent!=room.transform)throw new ArgumentException("Cash return must reach the same Inn.");
            if(returning.Resident.Id==source.First.ResidentId)first=returning;
            else if(returning.Resident.Id==source.Second.ResidentId)second=returning;
            else throw new ArgumentException("Cash return has the wrong family member.");
            FirstVisit.Pause(now);SecondVisit.Pause(now);
            FirstVisit.SuspendAndReplanFromActual(first,room);SecondVisit.SuspendAndReplanFromActual(second,room);
            RememberPositions();
        }
        private HouseholdJointInnMotion(WorldStateStore store,HouseholdDayPlan source,ResidentActor first,ResidentActor second,
            DaggerfallInterior room,InteriorGeometry geometry,ShopVisitGeometry activities,
            Func<ResidentActor,PhysicalShopVisit,double,bool> purchase,IList<AppointmentPlaceRoute> pointRoutes)
        {
            this.store=store;this.source=HouseholdDayPlan.Copy(source);this.first=first;this.second=second;this.room=room;
            this.geometry=geometry;this.activities=activities;this.purchase=purchase;this.pointRoutes=pointRoutes;
        }
        internal static bool TryCreate(WorldStateStore store,HouseholdDayPlan source,ResidentActor first,ResidentActor second,
            DaggerfallInterior room,InteriorGeometry geometry,ShopVisitGeometry activities,double now,
            Func<ResidentActor,PhysicalShopVisit,double,bool> purchase,out HouseholdJointInnMotion result,IList<AppointmentPlaceRoute> pointRoutes=null)
        {
            result=null;
            if(store==null || source==null || source.JointInn==null || !first || !second || first==second || !room || geometry==null || activities==null ||
                purchase==null || source.First.ResidentId!=first.Resident.Id || source.Second.ResidentId!=second.Resident.Id ||
                room.EntryDoor.buildingKey!=source.JointInn.InnKey || first.transform.parent!=room.transform || second.transform.parent!=room.transform)return false;
            // The source hash tolerates native G15 wire representation. Exact
            // transition replay still needs the store's own rounded geometry,
            // never a caller's retained pre-serialization route copy.
            var canonical=store.ReadHouseholdDay(source.SettlementId,source.HomeKey,source.AbsoluteDay);
            if(canonical==null || canonical.JointInn==null || canonical.Id!=source.Id ||
                HouseholdJointMovement.SourceFingerprint(canonical)!=HouseholdJointMovement.SourceFingerprint(source))return false;
            source=canonical;
            var progress=store.ReadHouseholdJointProgress(source.SettlementId,source.HomeKey);
            if(progress==null || progress.HouseholdDayId!=source.Id || progress.SourceFingerprint!=HouseholdJointMovement.SourceFingerprint(source) ||
                progress.Phase!=HouseholdJointPhase.Visiting || progress.Retired || now<progress.EnteredAt)return false;
            var value=new HouseholdJointInnMotion(store,source,first,second,room,geometry,activities,purchase,pointRoutes);
            var a=store.GetObservedVisit(first.Resident.Id);var b=store.GetObservedVisit(second.Resident.Id);
            if(a!=null || b!=null)
            {
                // Partial room reconstruction cannot erase a paid member or
                // fabricate the missing partner's physical position.
                if(!value.Matches(a) || !value.Matches(b))return false;
                if(!CanRestore(first,a,room) || !CanRestore(second,b,room))return false;
                try{now=ShopVisitJourney.ActualResumeTime(a,now);now=ShopVisitJourney.ActualResumeTime(b,now);}
                catch(ArgumentException){return false;}
                string firstSeat=a.HouseholdSeatId??Seat(a),secondSeat=b.HouseholdSeatId??Seat(b);
                if(firstSeat!=null || secondSeat!=null)
                {
                    if(firstSeat==null || secondSeat==null || activities.Seating==null ||
                        !activities.Seating.TryAcquirePair(first.Resident.Id,firstSeat,second.Resident.Id,secondSeat,out value.seats))return false;
                }
                value.FirstVisit=PhysicalShopVisit.RestoreAtActual(a,now);value.SecondVisit=PhysicalShopVisit.RestoreAtActual(b,now);
                Restore(first,a,room);Restore(second,b,room);
                value.exiting=Seat(a)==null && Seat(b)==null &&
                    !value.FirstVisit.Journey.Purchases && !value.SecondVisit.Journey.Purchases;
            }
            else
            {
                // A fresh entry starts at the actual doorway, never at a
                // schedule's later chair or counter sample.
                if(!geometry.EntryGround.HasValue || Vector3.Distance(first.transform.position-Vector3.up,geometry.EntryGround.Value)>1.2f ||
                    Vector3.Distance(second.transform.position-Vector3.up,geometry.EntryGround.Value)>1.2f)return false;
                ShopVisitJourney firstJourney,secondJourney;
                if(!activities.TryCreateSharedInn(first,second,now,End(source,source.JointInn.First),End(source,source.JointInn.Second),
                    out firstJourney,out secondJourney,out value.seats))return false;
                string fingerprint=HouseholdJointMovement.SourceFingerprint(source);
                value.FirstVisit=new PhysicalShopVisit(firstJourney,source.Id,fingerprint);
                value.SecondVisit=new PhysicalShopVisit(secondJourney,source.Id,fingerprint);
            }
            if(value.seats!=null)
            {
                var firstActual=first.transform.position-Vector3.up;var secondActual=second.transform.position-Vector3.up;
                value.FirstVisit.BindSharedSeat(value.seats.First,activities,a!=null && value.seats.First.Slot.Furniture.Blocks(firstActual,firstActual,CrowdMotion.BodyRadius));
                value.SecondVisit.BindSharedSeat(value.seats.Second,activities,b!=null && value.seats.Second.Slot.Furniture.Blocks(secondActual,secondActual,CrowdMotion.BodyRadius));
            }
            value.RememberPositions();
            try{value.Capture(now);result=value;return true;}
            catch{value.Release();throw;}
        }
        internal bool Tick(double now,bool firstAvailable,bool secondAvailable,bool paused=false)
        {
            if(Complete || rejected)return false;
            firstAvailable=firstAvailable && (FirstVisit.Snapshot.Phase==ShopVisitPhase.Complete || Available(first));
            secondAvailable=secondAvailable && (SecondVisit.Snapshot.Phase==ShopVisitPhase.Complete || Available(second));
            if(paused || !firstAvailable || !secondAvailable)
            {
                if(seats!=null)seats.Refresh();
                FirstVisit.Pause(now);SecondVisit.Pause(now);
                if(firstAvailable)HouseholdJointBodyMotion.Hold(first);if(secondAvailable)HouseholdJointBodyMotion.Hold(second);return false;
            }
            if(!OwnsActualPositions())
            {rejected=true;HouseholdJointBodyMotion.Hold(first);HouseholdJointBodyMotion.Hold(second);return false;}
            try
            {
                var service=store.ReadHouseholdInnService(source.SettlementId,source.HomeKey,now,pointRoutes);
                if(service==HouseholdInnServiceState.Blocked)
                {FirstVisit.Pause(now);SecondVisit.Pause(now);HouseholdJointBodyMotion.Hold(first);HouseholdJointBodyMotion.Hold(second);return false;}
                if(!exiting && (service==HouseholdInnServiceState.Unavailable || seats==null || !seats.Refresh()))
                {exiting=true;FirstVisit.BeginExit(now);SecondVisit.BeginExit(now);}
                Advance(first,FirstVisit,now,service);Advance(second,SecondVisit,now,service);RememberPositions();Capture(now);
                // A body that actually reached the interior exit must leave
                // that narrow doorway. Retain its exact persisted endpoint;
                // the shared homeward transition still requires both arrivals.
                if(FirstVisit.Snapshot.Phase==ShopVisitPhase.Complete)first.gameObject.SetActive(false);
                if(SecondVisit.Snapshot.Phase==ShopVisitPhase.Complete)second.gameObject.SetActive(false);
                if(FirstVisit.Snapshot.Phase==ShopVisitPhase.Complete && SecondVisit.Snapshot.Phase==ShopVisitPhase.Complete)
                {
                    var current=store.ReadHouseholdJointProgress(source.SettlementId,source.HomeKey);
                    var returning=HouseholdJointMovement.Restore(source,current);
                    returning.BeginReturn(now,source.JointInn.First.Itinerary.ExitRoute[0],source.JointInn.Second.Itinerary.ExitRoute[0]);
                    store.SaveHouseholdJointProgress(returning.Snapshot(),pointRoutes);Complete=true;Release();
                }
                return true;
            }
            catch
            {
                // Roll back only this executor's failed step to the latest
                // successfully published pair, before scene teardown can yield it.
                RestoreLastPublished(first,expectedFirst);RestoreLastPublished(second,expectedSecond);RememberPositions();
                rejected=true;HouseholdJointBodyMotion.Hold(first);HouseholdJointBodyMotion.Hold(second);throw;
            }
        }
        private void RestoreLastPublished(ResidentActor actor,Vector3 fallback)
        {
            if(!actor)return;var saved=store.GetObservedVisit(actor.Resident.Id);
            var local=Matches(saved)?new Vector3((float)saved.Legs[0].Route[0].X,(float)saved.Legs[0].Route[0].Y,(float)saved.Legs[0].Route[0].Z):fallback;
            actor.GetComponent<CrowdMotion>().RestorePlacement(room.transform.TransformPoint(local));
        }
        private void Advance(ResidentActor actor,PhysicalShopVisit visit,double now,HouseholdInnServiceState service)
        {
            if(visit.Snapshot.Phase==ShopVisitPhase.Complete){HouseholdJointBodyMotion.Hold(actor);return;}
            visit.Advance(actor,room,geometry,activities,now,()=>purchase(actor,visit,now),
                ()=>!exiting && service==HouseholdInnServiceState.Ready && seats!=null && seats.Refresh());
        }
        internal void Capture(double now)
        {
            if(rejected)throw new InvalidOperationException("Shared Inn recovery is required; retain the last valid paired checkpoint.");
            if(!OwnsActualPositions())throw new InvalidOperationException("Another owner displaced a shared Inn body; physical recovery is required.");
            var a=FirstVisit.Capture(first,room,geometry,now,source.SettlementId);
            var b=SecondVisit.Capture(second,room,geometry,now,source.SettlementId);
            if(a==null || b==null)throw new InvalidOperationException("Shared Inn actual checkpoint route is unavailable: first="+
                (a!=null)+"/"+FirstVisit.Snapshot.Phase+"/"+FirstVisit.CaptureFailure+"; second="+(b!=null)+"/"+SecondVisit.Snapshot.Phase+"/"+SecondVisit.CaptureFailure);
            store.StoreHouseholdInnVisits(a,b);
        }
        internal void Release()
        {
            if(FirstVisit!=null)FirstVisit.ReleaseReservations();if(SecondVisit!=null)SecondVisit.ReleaseReservations();
            if(seats!=null){seats.Release();seats=null;}
            if(first)activities.ReleaseCounter(first.Resident.Id);if(second)activities.ReleaseCounter(second.Resident.Id);
        }
        private bool Matches(ObservedVisit visit)
        {return visit!=null && visit.SettlementId==source.SettlementId && visit.BuildingKey==source.JointInn.InnKey &&
            visit.HouseholdDayId==source.Id && visit.HouseholdSourceFingerprint==HouseholdJointMovement.SourceFingerprint(source);}
        private Vector3 Actual(ResidentActor actor){return room.transform.InverseTransformPoint(actor.transform.position-Vector3.up);}
        private void RememberPositions(){if(first && second && room){expectedFirst=Actual(first);expectedSecond=Actual(second);}}
        private bool OwnsActualPositions()
        {return first && second && room && first.transform.parent==room.transform && second.transform.parent==room.transform &&
            Vector3.Distance(Actual(first),expectedFirst)<=0.001f && Vector3.Distance(Actual(second),expectedSecond)<=0.001f;}
        private static string Seat(ObservedVisit visit){foreach(var leg in visit.Legs)if(leg.SeatId!=null)return leg.SeatId;return null;}
        private bool Available(ResidentActor actor)
        {return actor && actor.gameObject.activeInHierarchy && actor.Resident.IsAlive && !actor.Resident.IsQuestProtected &&
            actor.transform.parent==room.transform &&
            !actor.IsConversing && !actor.IsSleeping && actor.GetComponent<CrowdMotion>() && actor.GetComponent<CrowdMotion>().HasPlacement;}
        private static void Restore(ResidentActor actor,ObservedVisit visit,DaggerfallInterior room)
        {var p=visit.Legs[0].Route[0];actor.GetComponent<CrowdMotion>().RestorePlacement(room.transform.TransformPoint(new Vector3((float)p.X,(float)p.Y,(float)p.Z)));}
        private static bool CanRestore(ResidentActor actor,ObservedVisit visit,DaggerfallInterior room)
        {
            var crowd=actor.GetComponent<CrowdMotion>();if(!crowd)return false;
            var p=visit.Legs[0].Route[0];var actual=room.transform.InverseTransformPoint(actor.transform.position-Vector3.up);
            return !crowd.HasPlacement || Vector3.Distance(actual,new Vector3((float)p.X,(float)p.Y,(float)p.Z))<=0.001f;
        }
        private static double End(HouseholdDayPlan source,ResidentDailyOuting outing)
        {return source.AbsoluteDay*86400d+new List<ResidentVisitSnapshot>(ResidentVisitJourney.Create(outing.DepartureSeconds,outing.Itinerary).ScheduledVisits())[0].LeaveAt;}
    }
}
