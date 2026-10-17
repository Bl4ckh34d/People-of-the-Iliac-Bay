using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using LivingCities.Core;
using UnityEngine;
namespace LivingCities.DFU
{
    public sealed class PhysicalShopVisit
    {
        public readonly ShopVisitJourney Journey;
        private readonly ShopPhysicalProgress progress;
        private InteriorRoomFollower follower;
        private int followingLeg=-1;
        private Vector3 activityGround;
        private float nextActivityCheck;
        public double OriginalEndAt { get; private set; }
        public double ResumeUntil { get; set; }
        public ShopVisitSnapshot Snapshot { get; private set; }
        public string LastFailure { get; private set; }
        public PhysicalShopVisit(ShopVisitJourney journey)
        {
            Journey=journey;progress=new ShopPhysicalProgress(journey);
            OriginalEndAt=journey.PlannedEndAt;
            var first=journey.LegRoute(0);
            Snapshot=new ShopVisitSnapshot {Phase=ShopVisitPhase.Walking,Position=first[first.Count-1]};
        }
        public PhysicalShopVisit(ObservedVisit saved,double now) : this(ShopVisitJourney.Resume(saved,now))
        { OriginalEndAt=saved.PlannedEndAt;ResumeUntil=saved.ResumeUntil; }
        public ObservedVisit Capture(ResidentActor actor,DaggerfallInterior interior,InteriorGeometry geometry,double now,int settlement)
        {
            var actual=interior.transform.InverseTransformPoint(actor.transform.position-Vector3.up);
            var result=new ObservedVisit {ResidentId=actor.Resident.Id,SettlementId=settlement,BuildingKey=interior.EntryDoor.buildingKey,
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
                    if(followingLeg==leg && follower!=null && !follower.Arrived) route=follower.RemainingRoute(actual);
                    else if(Vector3.Distance(actual,target)<0.15f) route=new List<GuardPoint3>{new GuardPoint3 {X=actual.x,Y=actual.y,Z=actual.z}};
                    else route=geometry.RouteBetween(actual,target);
                    if(route.Count==0) return null;
                    // Preserve the exact shared endpoint between consecutive legs.
                    var last=route[route.Count-1];
                    if(Math.Abs(last.X-end.X)+Math.Abs(last.Y-end.Y)+Math.Abs(last.Z-end.Z)>0.0001) route.Add(end);
                }
                if(previousEnd!=null && Math.Abs(previousEnd.X-route[0].X)+Math.Abs(previousEnd.Y-route[0].Y)+Math.Abs(previousEnd.Z-route[0].Z)>0.001)
                {
                    var end=route[route.Count-1];
                    route=geometry.RouteBetween(new Vector3((float)previousEnd.X,(float)previousEnd.Y,(float)previousEnd.Z),
                        new Vector3((float)end.X,(float)end.Y,(float)end.Z));
                    if(route.Count<2) return null;
                }
                previousEnd=route[route.Count-1];
                result.Legs.Add(new ObservedVisitLeg {Route=route,DwellSeconds=leg==progress.Leg ? progress.RemainingDwell(now) : Journey.DwellSeconds(leg),AfterPhase=Journey.StopPhase(leg)});
            }
            return ObservedVisit.Copy(result);
        }
        public void Advance(ResidentActor actor,DaggerfallInterior interior,InteriorGeometry geometry,
            ShopVisitGeometry activities,double seconds)
        {
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
                    activityGround=new Vector3((float)final.X,(float)final.Y,(float)final.Z);
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
                    route=geometry.RouteBetween(actual,activityGround);
                    if(route.Count<2) { LastFailure="no route from actual body to activity"; return; }
                    try { follower=new InteriorRoomFollower(route,DaggerfallUnity.Instance.WorldTime.TimeScale/actor.WalkingSpeed,
                        progress.Leg==Journey.LegCount-1 ? 0.55f : 0.15f); }
                    catch(ArgumentException error) { LastFailure=error.Message; return; }
                }
                followingLeg=progress.Leg;
                LastFailure=null;
            }
            bool arrived=follower==null || follower.Advance(actor,interior,geometry,Time.unscaledTime);
            progress.Advance(seconds,arrived);
            Snapshot=new ShopVisitSnapshot {Phase=progress.Phase,
                Position=new GuardPoint3 {X=activityGround.x,Y=activityGround.y,Z=activityGround.z}};
            if(followingLeg!=progress.Leg) return;
            if(arrived)
            {
                var motion=actor.GetComponent<CrowdMotion>();if(motion) motion.ResumeFromPause();
                actor.Place(interior.transform.TransformPoint(activityGround),
                    interior.transform.TransformDirection(activities.Face(activityGround,progress.Phase)),true,waitAtOccupiedTarget:true);
            }
        }
    }
}
