using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Caller advances this through its existing action/routine overlay. No Update
    // method and no second actor controller. Only loaded optional activities may own it.
    public sealed class OutdoorSeatActivity
    {
        public readonly SelectedSeatAccess Seat;
        private readonly Transform root;
        private readonly TownGeometry geometry;
        private readonly List<SavedRoutePoint> route;
        private int next=1;
        public bool Arrived {get;private set;}
        private OutdoorSeatActivity(SelectedSeatAccess seat,Transform root,TownGeometry geometry,List<SavedRoutePoint> route)
        {Seat=seat;this.root=root;this.geometry=geometry;this.route=route;}

        public static void PrepareNearby(FurnitureSeatCatalog catalog,Vector3 near,float radius=20,string preferredSeatId=null)
        {
            catalog.RefreshSceneTransform();
            foreach(var slot in PreparationCandidates(catalog.Slots,near,radius,preferredSeatId))PrepareSlot(catalog,slot);
        }
        internal static List<FurnitureSeatSlot> PreparationCandidates(IList<FurnitureSeatSlot> slots,Vector3 near,float radius,string preferredSeatId)
        {
            var result=new List<FurnitureSeatSlot>();
            if(slots==null||float.IsNaN(radius)||float.IsInfinity(radius)||radius<0)return result;
            // Explicit requests get one bounded preparation attempt before the
            // general nearby-seat budget, even when requested seat lies outside
            // default radius. Caller owns target choice and route reachability.
            if(!string.IsNullOrWhiteSpace(preferredSeatId))
                foreach(var slot in slots)if(slot!=null&&!slot.HasApproach&&slot.Id==preferredSeatId){result.Add(slot);break;}
            int checkedSeats=0;float square=radius*radius;
            foreach(var slot in slots)
            {
                if(slot==null||slot.HasApproach||(slot.Ground-near).sqrMagnitude>square||result.Contains(slot))continue;
                if(checkedSeats++>=12)break;
                result.Add(slot);
            }
            return result;
        }
        private static bool PrepareSlot(FurnitureSeatCatalog catalog,FurnitureSeatSlot slot)
        {
            foreach(float distance in new[]{0.7f,1.05f,1.4f})
            {
                for(int direction=0;direction<8;direction++)
                {
                    var offset=Quaternion.Euler(0,direction*45,0)*slot.Facing*distance;
                    var candidate=slot.Ground+offset;bool supported=false;
                    foreach(var hit in Physics.RaycastAll(candidate+Vector3.up*0.5f,Vector3.down,1,~0,QueryTriggerInteraction.Ignore))
                        if(CivilianResponses.Blocker(hit.collider)&&hit.normal.y>=0.7f&&Math.Abs(hit.point.y-slot.Ground.y)<0.25f)
                        {candidate.y=hit.point.y;supported=true;break;}
                    if(!supported||FurnitureSeatCatalog.Blocks(candidate,candidate,CrowdMotion.BodyRadius))continue;
                    if(catalog.SetApproach(slot,candidate))return true;
                }
            }
            return false;
        }
        public static bool TryBegin(ResidentActor actor,FurnitureSeatCatalog catalog,string seatId,Transform root,TownGeometry geometry,out OutdoorSeatActivity activity)
        {
            activity=null;if(!actor||!catalog||!root||geometry==null)return false;
            PrepareNearby(catalog,actor.transform.position-Vector3.up,20,seatId);
            var slot=catalog.Find(seatId);if(slot==null||!slot.HasApproach)return false;
            var from=root.InverseTransformPoint(actor.transform.position-Vector3.up);var to=root.InverseTransformPoint(slot.Approach);
            var route=geometry.RouteBetweenPoints(new SavedRoutePoint {X=from.x,Z=from.z},new SavedRoutePoint {X=to.x,Z=to.z});
            if(route.Count<2)return false;
            SelectedSeatAccess access;if(!catalog.TryAcquire(actor.Resident.Id,seatId,out access))return false;
            activity=new OutdoorSeatActivity(access,root,geometry,route);return true;
        }
        public bool Advance(ResidentActor actor)
        {
            if(!actor||!root||!Seat.Refresh())return false;
            while(next<route.Count)
            {
                var target=geometry.ScenePoint(new RoutePoint(route[next].X,route[next].Z));
                if(Vector3.Distance(actor.transform.position-Vector3.up,target)<0.15f){next++;continue;}
                var crowd=actor.GetComponent<CrowdMotion>();if(crowd)crowd.ResumeFromPause();
                actor.Place(target,target-(actor.transform.position-Vector3.up),waitAtOccupiedTarget:true);return false;
            }
            Arrived=Seat.Advance(actor);return Arrived;
        }
        public bool Depart(ResidentActor actor)
        {if(!Seat.Advance(actor,true))return false;Seat.Release();Arrived=false;return true;}
        public void Interrupt() {Seat.Release();Arrived=false;}
    }
}
