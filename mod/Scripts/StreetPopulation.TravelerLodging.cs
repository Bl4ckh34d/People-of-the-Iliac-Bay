using System;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private float nextTravelerLodgingAt;
        private bool residentLodgingTurn;
        private int travelerLodgingCursor,residentLodgingCursor;
        private DaggerfallInterior residentLodgingExitInterior;
        private WorldStateStore residentLodgingExitStore;
        private readonly Dictionary<string,InteriorRoomFollower> residentLodgingExits=new Dictionary<string,InteriorRoomFollower>();

        private bool ContinueResidentLodgingDoorwayExit(ResidentActor actor,double now)
        {
            if(!actor || !actor.Resident.IsAlive || !interior || actor.transform.parent!=interior.transform || indoorGeometry==null || !indoorGeometry.EntryGround.HasValue)return false;
            if(!worldStore.HasResidentLodgingSourceExit(actor.Resident.Id,interior.EntryDoor.buildingKey,now))return false;
            var entry=indoorGeometry.EntryGround.Value;
            if(Vector3.Distance(actor.transform.position-Vector3.up,entry)<=0.55f)
            {residentLodgingExits.Remove(actor.Resident.Id);return false;}
            if(residentLodgingExitInterior!=interior || !ReferenceEquals(residentLodgingExitStore,worldStore))
            {residentLodgingExits.Clear();residentLodgingExitInterior=interior;residentLodgingExitStore=worldStore;}
            InteriorRoomFollower follower;
            if(!residentLodgingExits.TryGetValue(actor.Resident.Id,out follower) || follower.Arrived)
            {
                var route=indoorGeometry.RouteToExit(actor.transform.position-Vector3.up);
                if(route.Count<2)return true;
                follower=new InteriorRoomFollower(route,DaggerfallUnity.Instance.WorldTime.TimeScale/actor.WalkingSpeed);
                follower.BeginExitFromCurrentPosition();residentLodgingExits[actor.Resident.Id]=follower;
            }
            follower.Advance(actor,interior,indoorGeometry,Time.unscaledTime);return true;
        }

        private void AdvanceTravelerLodgingFallback(double now)
        {
            if(interior || !directory || geometry==null || worldStore==null || population==null ||
                !EntranceSurveyReady || GameManager.Instance.IsPlayerInside || Time.unscaledTime<nextTravelerLodgingAt)return;
            nextTravelerLodgingAt=Time.unscaledTime+0.5f;
            var host=GetComponent<PeopleOfTheIliacBayMod>();
            if(!host)return;
            residentLodgingTurn=!residentLodgingTurn;
            if(residentLodgingTurn && AdvanceResidentLodgingFallback(now))return;
            var visits=worldStore.TravelerVisitsFor(population.SettlementId);
            for(int scanned=0;scanned<visits.Count;scanned++)
            {
                if(travelerLodgingCursor>=visits.Count)travelerLodgingCursor=0;
                var visit=visits[travelerLodgingCursor++];
                if(!worldStore.TravelerAwaitingLodgingDecision(visit.Id,now) || TravelerActionOwnsVisit(visit.PersonId))continue;
                var existing=FindActor(visit.PersonId);
                var doorPoint=visit.Route[visit.Route.Count-1];
                if(existing && Vector3.Distance(existing.transform.position-Vector3.up,
                    geometry.ScenePoint(new RoutePoint(doorPoint.X,doorPoint.Z)))>0.55f)continue;
                if(!BackgroundWork.TryStart())return;
                var person=worldStore.Traveler(visit.PersonId);
                double secondsPerMetre=DaggerfallUnity.Instance.WorldTime.TimeScale/ResidentMovement.WalkSpeedFor(person.AppearanceSeed);
                TrySelectLodgingFallback(visit.PersonId,visit.TavernKey,visit.Route[visit.Route.Count-1],secondsPerMetre,now,
                    ()=>visit.Route,(key,camping,route,walk)=>worldStore.TryRedirectTraveler(visit.Id,key,camping,route,walk,now));
                return; // One bounded guest decision per refresh.
            }
            AdvanceResidentLodgingFallback(now);
        }

        private bool AdvanceResidentLodgingFallback(double now)
        {
            var requests=worldStore.PendingResidentLodging(population.SettlementId,now);
            for(int scanned=0;scanned<requests.Count;scanned++)
            {
                if(residentLodgingCursor>=requests.Count)residentLodgingCursor=0;
                var request=requests[residentLodgingCursor++];
                if(worldStore.ActiveNpcAction(request.PersonId)!=null || worldStore.ActiveNpcTask(request.PersonId)!=null)continue;
                var actor=FindActor(request.PersonId);
                if(actor && (actor.transform.parent!=directory.transform || Vector3.Distance(actor.transform.position-Vector3.up,
                    geometry.ScenePoint(new RoutePoint(request.Door.X,request.Door.Z)))>0.55f))continue;
                if(!BackgroundWork.TryStart())return true;
                var person=worldStore.ResolveResident(request.PersonId);
                double secondsPerMetre=DaggerfallUnity.Instance.WorldTime.TimeScale/ResidentMovement.WalkSpeedFor(person.AppearanceSeed);
                TrySelectLodgingFallback(request.PersonId,request.BuildingKey,request.Door,secondsPerMetre,now,
                    ()=>ResidentCampApproach(request.BuildingKey,request.Door),
                    (key,camping,route,walk)=>worldStore.TryResolveResidentLodging(request.PersonId,request.VisitId,key,camping,route,walk,now));
                return true;
            }
            return false;
        }

        private List<SavedRoutePoint> ResidentCampApproach(int sourceBuilding,SavedRoutePoint source)
        {
            foreach(var access in geometry.SurveyTravelerRoutes(sourceBuilding))
            {
                var route=new List<SavedRoutePoint>();foreach(var point in access.Points)route.Add(new SavedRoutePoint {X=point.X,Z=point.Z});
                var end=route[route.Count-1];
                if(Math.Abs(end.X-source.X)+Math.Abs(end.Z-source.Z)>0.001)
                {
                    var join=geometry.RouteBetweenPoints(end,source);if(join.Count<2)continue;
                    for(int i=1;i<join.Count;i++)route.Add(join[i]);
                }
                return route;
            }
            return null;
        }

        private bool TrySelectLodgingFallback(string personId,int sourceBuilding,SavedRoutePoint start,double secondsPerMetre,double now,
            Func<List<SavedRoutePoint>> campApproach,Func<int,bool,List<SavedRoutePoint>,double,bool> accept)
        {
                var alternatives=new List<TownGeometry.Entrance>();
                foreach(var door in geometry.Entrances.Values)
                {
                    BuildingSummary building;
                    if(door.BuildingKey==sourceBuilding || !directory.GetBuildingSummary(door.BuildingKey,out building) ||
                        building.BuildingType!=DFLocation.BuildingTypes.Tavern ||
                        !worldStore.HasLodgingInventory(population.SettlementId,door.BuildingKey))continue;
                    alternatives.Add(door);
                }
                alternatives.Sort((a,b)=>{
                    double ax=a.LocalDoor.x-start.X,az=a.LocalDoor.z-start.Z,bx=b.LocalDoor.x-start.X,bz=b.LocalDoor.z-start.Z;
                    int order=(ax*ax+az*az).CompareTo(bx*bx+bz*bz);return order!=0?order:a.BuildingKey.CompareTo(b.BuildingKey);
                });
                int type=(int)DFLocation.BuildingTypes.Tavern;DailyServiceWindow hours;
                bool hoursKnown=DailyServiceHours.TryFromNativeHours(PlayerActivate.openHours[type],PlayerActivate.closeHours[type],out hours);
                foreach(var door in alternatives)
                {
                    var route=geometry.RouteBetweenPoints(start,new SavedRoutePoint {X=door.LocalDoor.x,Z=door.LocalDoor.z});
                    double walk=TravelerPathSeconds(route,secondsPerMetre);
                    if(walk<=0 || !hoursKnown || !TravelerInnOpen(now+walk,hours))continue;
                    if(accept(door.BuildingKey,false,route,walk))
                    {Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TRAVELER_ALTERNATE_INN: person="+personId+"; inn="+door.BuildingKey+"; measuredWalkSeconds="+walk);return true;}
                }
                // A known available Inn can be selected immediately. Unknown
                // inventories only prevent declaring that every Inn is full.
                var host=GetComponent<PeopleOfTheIliacBayMod>();
                if(host && host.HasPendingInnInventory(directory))return false;
                bool guildPending;
                if(TryGuildLodgingFallback(personId,start,secondsPerMetre,now,accept,out guildPending))return true;
                if(guildPending)return false;
                List<SavedRoutePoint> camp;
                var approach=campApproach();
                if(approach!=null && geometry.TryTravelerCampRoute(approach,out camp))
                {
                    double walk=TravelerPathSeconds(camp,secondsPerMetre);
                    if(walk>0 && accept(-1,true,camp,walk))
                    {Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TRAVELER_OUTSIDE_CAMP: person="+personId+"; actualOutsideRectangle=true; measuredWalkSeconds="+walk);return true;}
                }
                return false;
        }

        private static bool TravelerInnOpen(double now,DailyServiceWindow hours)
        {
            double minute=now%86400/60;
            return minute>=hours.OpenMinute && minute<hours.CloseMinute;
        }
        private static double TravelerPathSeconds(IList<SavedRoutePoint> route,double secondsPerMetre)
        {
            if(route==null || route.Count<2)return 0;double metres=0;
            for(int i=1;i<route.Count;i++)
            {double dx=route[i].X-route[i-1].X,dz=route[i].Z-route[i-1].Z;metres+=Math.Sqrt(dx*dx+dz*dz);}
            return metres*secondsPerMetre;
        }
    }
}
