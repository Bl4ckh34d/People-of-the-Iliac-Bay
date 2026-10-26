using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
namespace PeopleOfTheIliacBay.DFU
{
    public sealed class PhysicalShopVisit
    {
        public readonly ShopVisitJourney Journey;
        private readonly ShopPhysicalProgress progress;
        private InteriorRoomFollower follower;
        private InteriorRoomFollower counterWaitFollower;
        private Vector3 counterWaitGround;
        private int followingLeg=-1;
        private int observedArrivalLeg=-1;
        private Vector3 activityGround;
        private float nextActivityCheck;
        private SelectedSeatAccess selectedSeat;
        private SelectedSeatAccess sharedSeat;
        private ShopVisitGeometry activityGeometry;
        public string HouseholdDayId {get;private set;}
        public string HouseholdSourceFingerprint {get;private set;}
        public string HouseholdSeatId {get;private set;}
        public string SelectedSeatId {get{return selectedSeat==null?null:selectedSeat.Slot.Id;}}
        internal string CaptureFailure {get;private set;}
        internal string PurchaseFailure {get;set;}
        internal SelectedSeatAccess TakeSeatForDeparture(ResidentActor actor)
        {
            var access=selectedSeat;selectedSeat=null;
            if(sharedSeat==access)sharedSeat=null;
            // A visit reserves its chair before walking there. A new action only
            // needs chair-exit permission when the body actually reached it.
            if(access!=null && (!actor || !access.Refresh() || !access.Permits(actor.transform.position-Vector3.up,access.Slot.Approach)))
            {access.Release();return null;}
            return access;
        }
        public void ReleaseReservations() {if(selectedSeat!=null){if(selectedSeat!=sharedSeat)selectedSeat.Release();selectedSeat=null;}}
        internal void BindSharedSeat(SelectedSeatAccess access,ShopVisitGeometry activities,bool leaving=false)
        {
            if(access==null || HouseholdDayId==null || HouseholdSeatId!=null && HouseholdSeatId!=access.Slot.Id)
                throw new ArgumentException("Shared chair permission requires the exact family seat intent.");
            sharedSeat=access;HouseholdSeatId=access.Slot.Id;activityGeometry=activities;
            if(leaving)selectedSeat=access;
        }
        public void SuspendAndReplanFromActual(ResidentActor actor,DaggerfallInterior interior)
        {
            if(!actor || !interior) return;
            ReleaseReservations();
            follower=null;counterWaitFollower=null;followingLeg=-1;observedArrivalLeg=-1;
            var actual=interior.transform.InverseTransformPoint(actor.transform.position-Vector3.up);
            activityGround=actual;
            Snapshot=new ShopVisitSnapshot {Phase=progress.Phase,
                Position=new GuardPoint3 {X=actual.x,Y=actual.y,Z=actual.z}};
        }
        public double OriginalEndAt { get; private set; }
        public double ResumeUntil { get; set; }
        public ShopVisitSnapshot Snapshot { get; private set; }
        public string LastFailure { get; private set; }
        public bool CounterPurchaseResolved { get; internal set; }
        internal AmbientShopPurchase PurchaseSelection;
        public void Pause(double seconds){progress.Pause(seconds);}
        public void BeginExit(double seconds)
        {
            progress.BeginExit(seconds);observedArrivalLeg=-1;counterWaitFollower=null;
            // Keep a current chair permission until Advance physically clears
            // its apron. The final route is rebuilt from the actual body.
            follower=null;followingLeg=-1;
        }
        public PhysicalShopVisit(ShopVisitJourney journey,string householdDayId=null,string householdSourceFingerprint=null)
        {
            HouseholdInnVisitProvenance.Validate(householdDayId,householdSourceFingerprint);
            HouseholdDayId=householdDayId;HouseholdSourceFingerprint=householdSourceFingerprint;
            Journey=journey;progress=new ShopPhysicalProgress(journey);
            OriginalEndAt=journey.PlannedEndAt;
            var first=journey.LegRoute(0);
            Snapshot=new ShopVisitSnapshot {Phase=ShopVisitPhase.Walking,Position=first[first.Count-1]};
        }
        public PhysicalShopVisit(ObservedVisit saved,double now) : this(ShopVisitJourney.Resume(saved,now),saved.HouseholdDayId,saved.HouseholdSourceFingerprint)
        { OriginalEndAt=saved.PlannedEndAt;ResumeUntil=saved.ResumeUntil;HouseholdSeatId=saved.HouseholdSeatId;CounterPurchaseResolved=saved.CounterPurchaseResolved; }
        internal static PhysicalShopVisit RestoreAtActual(ObservedVisit saved,double now)
        {
            return new PhysicalShopVisit(ShopVisitJourney.ResumeAtActual(saved,now),saved.HouseholdDayId,saved.HouseholdSourceFingerprint)
                {OriginalEndAt=saved.PlannedEndAt,ResumeUntil=saved.ResumeUntil,HouseholdSeatId=saved.HouseholdSeatId,CounterPurchaseResolved=saved.CounterPurchaseResolved};
        }
        public ObservedVisit Capture(ResidentActor actor,DaggerfallInterior interior,InteriorGeometry geometry,double now,int settlement)
        {
            CaptureFailure=null;
            var actual=interior.transform.InverseTransformPoint(actor.transform.position-Vector3.up);
            var egressAccess=selectedSeat!=null && Journey.SeatId(progress.Leg)!=selectedSeat.Slot.Id?selectedSeat:null;
            var result=new ObservedVisit {ResidentId=actor.Resident.Id,SettlementId=settlement,BuildingKey=interior.EntryDoor.buildingKey,
                HouseholdDayId=HouseholdDayId,HouseholdSourceFingerprint=HouseholdSourceFingerprint,
                HouseholdSeatId=HouseholdSeatId,
                CounterPurchaseResolved=CounterPurchaseResolved,
                ObservedAt=now,PlannedEndAt=OriginalEndAt,ResumeUntil=Math.Max(OriginalEndAt,ResumeUntil),
                SecondsPerMetre=DaggerfallUnity.Instance.WorldTime.TimeScale/actor.WalkingSpeed};
            if(progress.Phase==ShopVisitPhase.Complete)
            {
                result.Legs.Add(new ObservedVisitLeg {Route=new List<GuardPoint3>{new GuardPoint3 {X=actual.x,Y=actual.y,Z=actual.z}},AfterPhase=ShopVisitPhase.Complete});
                return ObservedVisit.Copy(result);
            }
            GuardPoint3 previousEnd=null;
            for(int leg=progress.Leg;leg<Journey.LegCount;leg++)
            {
                var route=Journey.LegRoute(leg);
                if(leg==progress.Leg)
                {
                    var end=followingLeg==progress.Leg ? new GuardPoint3 {X=activityGround.x,Y=activityGround.y,Z=activityGround.z} : route[route.Count-1];
                    var target=new Vector3((float)end.X,(float)end.Y,(float)end.Z);
                    bool counterArrival=observedArrivalLeg==leg && Journey.StopPhase(leg)==ShopVisitPhase.Haggling;
                    if(counterArrival) route=new List<GuardPoint3>{new GuardPoint3 {X=actual.x,Y=actual.y,Z=actual.z}};
                    else if(followingLeg==leg && follower!=null && !follower.Arrived) route=follower.RemainingRoute(actual);
                    else if(Vector3.Distance(actual,target)<0.15f) route=new List<GuardPoint3>{new GuardPoint3 {X=actual.x,Y=actual.y,Z=actual.z}};
                    else route=activityGeometry==null?geometry.RouteBetween(actual,target):activityGeometry.ActivityRouteBetween(actual,target,egressAccess,Journey.SeatId(leg));
                    if(route.Count==0)
                    {CaptureFailure="current leg="+leg+"; from="+actual.ToString("R")+"; to="+target.ToString("R")+"; movement="+LastFailure;return null;}
                    // Preserve the exact shared endpoint between consecutive legs.
                    var last=route[route.Count-1];
                    if(!counterArrival && Math.Abs(last.X-end.X)+Math.Abs(last.Y-end.Y)+Math.Abs(last.Z-end.Z)>0.0001) route.Add(end);
                }
                if(previousEnd!=null && Math.Abs(previousEnd.X-route[0].X)+Math.Abs(previousEnd.Y-route[0].Y)+Math.Abs(previousEnd.Z-route[0].Z)>0.001)
                {
                    var end=route[route.Count-1];
                    var from=new Vector3((float)previousEnd.X,(float)previousEnd.Y,(float)previousEnd.Z);var to=new Vector3((float)end.X,(float)end.Y,(float)end.Z);
                    route=activityGeometry==null?geometry.RouteBetween(from,to):activityGeometry.ActivityRouteBetween(from,to,null,Journey.SeatId(leg));
                    if(route.Count<2)
                    {
                        var live=activityGeometry?.Seating?.Find(Journey.SeatId(leg));
                        CaptureFailure="connection leg="+leg+"; from="+from.ToString("R")+"; to="+to.ToString("R")+
                            "; seat="+Journey.SeatId(leg)+"; liveSeat="+(live==null?"missing":interior.transform.InverseTransformPoint(live.Ground).ToString("R"));
                        return null;
                    }
                }
                previousEnd=route[route.Count-1];
                result.Legs.Add(new ObservedVisitLeg {Route=route,DwellSeconds=leg==progress.Leg ? progress.RemainingDwell(now) : Journey.DwellSeconds(leg),AfterPhase=Journey.StopPhase(leg),SeatId=Journey.SeatId(leg)});
            }
            return ObservedVisit.Copy(result);
        }
        public void Advance(ResidentActor actor,DaggerfallInterior interior,InteriorGeometry geometry,
            ShopVisitGeometry activities,double seconds,Func<bool> counterPurchaseReady=null,Func<bool> seatReady=null)
        {
            activityGeometry=activities;
            observedArrivalLeg=-1;
            if(Journey.StopPhase(progress.Leg)!=ShopVisitPhase.Haggling)activities.ReleaseCounter(actor.Resident.Id,false);
            if(Journey.SeatId(progress.Leg)!=null && seatReady!=null && !seatReady())
            {
                progress.Pause(seconds);HouseholdJointBodyMotion.Hold(actor);
                var actual=interior.transform.InverseTransformPoint(actor.transform.position-Vector3.up);
                Snapshot=new ShopVisitSnapshot{Phase=ShopVisitPhase.Walking,Position=new GuardPoint3{X=actual.x,Y=actual.y,Z=actual.z}};
                LastFailure="waiting for both shared Inn purchases";return;
            }
            if(selectedSeat!=null&&Journey.SeatId(progress.Leg)!=selectedSeat.Slot.Id)
            {
                if(!selectedSeat.Advance(actor,true))
                {Snapshot=new ShopVisitSnapshot {Phase=ShopVisitPhase.Walking,Position=new GuardPoint3 {X=activityGround.x,Y=activityGround.y,Z=activityGround.z}};return;}
                ReleaseReservations();follower=null;followingLeg=-1;LastFailure=null;
                // Egress already moved this body. Begin the public route on
                // the next tick instead of spending a second movement step.
                var cleared=interior.transform.InverseTransformPoint(actor.transform.position-Vector3.up);
                Snapshot=new ShopVisitSnapshot{Phase=progress.Phase,Position=new GuardPoint3{X=cleared.x,Y=cleared.y,Z=cleared.z}};
                return;
            }
            if(Journey.SeatId(progress.Leg)!=null)
            {
                bool acquired=selectedSeat!=null;
                if(!acquired && sharedSeat!=null)
                {
                    acquired=sharedSeat.Owner==actor.Resident.Id && sharedSeat.Slot.Id==Journey.SeatId(progress.Leg) && sharedSeat.Valid;
                    if(acquired)selectedSeat=sharedSeat;
                }
                else if(!acquired)acquired=activities.TrySeat(actor,Journey.SeatId(progress.Leg),out selectedSeat);
                if(!acquired)
                {
                    LastFailure="selected seat unavailable";progress.Advance(seconds,false);
                    Vector3 waiting;var actual=interior.transform.InverseTransformPoint(actor.transform.position-Vector3.up);
                    if(activities.TryCounterWaitingPosition(actor,out waiting))
                    {
                        if(counterWaitFollower==null||(counterWaitGround-waiting).sqrMagnitude>0.001f)
                        {
                            var route=geometry.RouteBetween(actual,waiting);counterWaitGround=waiting;
                            if(route.Count>=2)counterWaitFollower=new InteriorRoomFollower(route,DaggerfallUnity.Instance.WorldTime.TimeScale/actor.WalkingSpeed);
                        }
                        if(counterWaitFollower!=null)counterWaitFollower.Advance(actor,interior,geometry,Time.unscaledTime);
                    }
                    Snapshot=new ShopVisitSnapshot {Phase=ShopVisitPhase.Walking,Position=new GuardPoint3 {X=actual.x,Y=actual.y,Z=actual.z}};return;
                }
                if(!selectedSeat.Refresh()){ReleaseReservations();LastFailure="selected seat claim expired";return;}
                if(counterWaitFollower!=null){counterWaitFollower=null;followingLeg=-1;}
            }
            var crowd=actor.GetComponent<CrowdMotion>();
            if(crowd && !crowd.HasPlacement)
            {
                Vector3 placed;
                if(!crowd.Place(actor.transform.position-Vector3.up,out placed,false)) { LastFailure="entry placement blocked"; return; }
            }
            var localBody=interior.transform.InverseTransformPoint(actor.transform.position-Vector3.up);
            bool displaced=followingLeg==progress.Leg && (follower==null || follower.Arrived) &&
                Vector3.Distance(localBody,activityGround)>0.4f;
            bool redirected=false;
            if(activities.IsTavern && Journey.StopPhase(progress.Leg)==ShopVisitPhase.Haggling)
            {
                var leg=Journey.LegRoute(progress.Leg);var end=leg[leg.Count-1];
                var preferred=followingLeg==progress.Leg ? activityGround : new Vector3((float)end.X,(float)end.Y,(float)end.Z);
                Vector3 available;
                if(!activities.TryCounterPosition(actor,preferred,out available))
                {
                    // A full counter grants no service and no imaginary slot
                    // behind another patron. Physical arrival still owns dwell.
                    progress.Advance(seconds,false);
                    followingLeg=-1;follower=null;
                    Vector3 waiting;
                    if(activities.TryCounterWaitingPosition(actor,out waiting))
                    {
                        if(counterWaitFollower==null || (counterWaitGround-waiting).sqrMagnitude>0.001f)
                        {
                            var waitRoute=geometry.RouteBetween(localBody,waiting);counterWaitGround=waiting;
                            if(waitRoute.Count>=2) counterWaitFollower=new InteriorRoomFollower(waitRoute,DaggerfallUnity.Instance.WorldTime.TimeScale/actor.WalkingSpeed);
                        }
                        if(counterWaitFollower!=null && !counterWaitFollower.Arrived)
                            counterWaitFollower.Advance(actor,interior,geometry,Time.unscaledTime);
                        else
                        {
                            var motion=actor.GetComponent<CrowdMotion>();if(motion) motion.ResumeFromPause();
                            actor.Place(interior.transform.TransformPoint(waiting),interior.transform.TransformDirection(activities.Face(waiting,ShopVisitPhase.Browsing)),true,waitAtOccupiedTarget:true);
                        }
                    }
                    else
                    {
                        var motion=actor.GetComponent<CrowdMotion>();if(motion) motion.ResumeFromPause();
                        actor.Place(interior.transform.TransformPoint(localBody),interior.transform.TransformDirection(activities.Face(localBody,ShopVisitPhase.Haggling)),true,waitAtOccupiedTarget:true);
                    }
                    Snapshot=new ShopVisitSnapshot {Phase=ShopVisitPhase.Walking,Position=new GuardPoint3 {X=localBody.x,Y=localBody.y,Z=localBody.z}};
                    return;
                }
                if(counterWaitFollower!=null) {counterWaitFollower=null;followingLeg=-1;}
                redirected=followingLeg!=progress.Leg || (available-activityGround).sqrMagnitude>0.001f;
                activityGround=available;
            }
            else activities.ReleaseCounter(actor.Resident.Id,false);
            if(followingLeg==progress.Leg && Journey.StopPhase(progress.Leg)==ShopVisitPhase.Browsing &&
                Time.unscaledTime>=nextActivityCheck)
            {
                nextActivityCheck=Time.unscaledTime+1;
                Vector3 alternative;
                if(activities.TryFreeBrowsePosition(activityGround,actor,seconds,out alternative))
                {activityGround=alternative;redirected=true;}
            }
            if(followingLeg!=progress.Leg || displaced || redirected)
            {
                var route=Journey.LegRoute(progress.Leg);
                if(followingLeg!=progress.Leg)
                {
                    var final=route[route.Count-1];
                    if(!activities.IsTavern || Journey.StopPhase(progress.Leg)!=ShopVisitPhase.Haggling)
                        activityGround=selectedSeat==null?new Vector3((float)final.X,(float)final.Y,(float)final.Z):interior.transform.InverseTransformPoint(selectedSeat.Slot.Ground);
                    Vector3 alternative;
                    if(Journey.StopPhase(progress.Leg)==ShopVisitPhase.Browsing && activities.TryFreeBrowsePosition(activityGround,actor,seconds,out alternative))
                        activityGround=alternative;
                    nextActivityCheck=Time.unscaledTime+1;
                }
                // Route from the body's real position, never a later clock sample.
                var actual=interior.transform.InverseTransformPoint(actor.transform.position-Vector3.up);
                if(Vector3.Distance(actual,activityGround)<0.15f) follower=null;
                else
                {
                    route=activities.IsTavern && Journey.StopPhase(progress.Leg)==ShopVisitPhase.Haggling ?
                        activities.CounterRouteBetween(actual,activityGround) : activities.ActivityRouteBetween(actual,activityGround,null,Journey.SeatId(progress.Leg));
                    if(route.Count<2) { LastFailure="no route from actual body to activity"; return; }
                    try { follower=new InteriorRoomFollower(route,DaggerfallUnity.Instance.WorldTime.TimeScale/actor.WalkingSpeed,
                        progress.Leg==Journey.LegCount-1 ? 0.55f : selectedSeat==null?0.15f:0.10f,selectedSeat); }
                    catch(ArgumentException error) { LastFailure=error.Message; return; }
                }
                followingLeg=progress.Leg;
                LastFailure=null;
            }
            bool arrived=follower==null || follower.Advance(actor,interior,geometry,Time.unscaledTime);
            observedArrivalLeg=arrived?progress.Leg:-1;
            if((activities.IsTavern || activities.IsShop) && Journey.Purchases && Journey.StopPhase(progress.Leg)==ShopVisitPhase.Haggling && arrived &&
                (counterPurchaseReady==null || !counterPurchaseReady()))
            {
                if(arrived)
                {
                    var motion=actor.GetComponent<CrowdMotion>();if(motion) motion.ResumeFromPause();
                    actor.Place(interior.transform.TransformPoint(activityGround),
                        interior.transform.TransformDirection(activities.Face(activityGround,ShopVisitPhase.Haggling)),
                        true,waitAtOccupiedTarget:true,seatAccess:selectedSeat);
                }
                Snapshot=new ShopVisitSnapshot {Phase=ShopVisitPhase.Haggling,
                    Position=new GuardPoint3 {X=activityGround.x,Y=activityGround.y,Z=activityGround.z}};
                return;
            }
            progress.Advance(seconds,arrived);
            if(progress.Leg!=observedArrivalLeg)observedArrivalLeg=-1;
            Snapshot=new ShopVisitSnapshot {Phase=progress.Phase,
                Position=new GuardPoint3 {X=activityGround.x,Y=activityGround.y,Z=activityGround.z}};
            if(followingLeg!=progress.Leg) return;
            if(arrived)
            {
                var motion=actor.GetComponent<CrowdMotion>();if(motion) motion.ResumeFromPause();
                actor.Place(interior.transform.TransformPoint(activityGround),
                    interior.transform.TransformDirection(activities.Face(activityGround,progress.Phase)),true,waitAtOccupiedTarget:true,seatAccess:selectedSeat);
            }
        }
    }
}
