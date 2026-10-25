using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private sealed class AppointmentPointRouteCache
        {
            public int Day,Revision,SettlementId,HouseholdRevision,RoutineRevision;
            public TownGeometry Geometry;
            public List<AppointmentPlaceRoute> Routes;
        }
        private readonly Dictionary<string,AppointmentPointRouteCache> appointmentPointRouteCache=
            new Dictionary<string,AppointmentPointRouteCache>(StringComparer.Ordinal);
        private const float CapturedPointGroundTolerance=0.5f;

        // Capture the player's current, scene-owned place once at message submit.
        // Exterior coordinates are town-directory local so floating-origin shifts
        // do not change the saved target. Indoor invitations remain building based.
        public bool TryCaptureConversationLocation(out AppointmentPlace place)
        {
            place=null;
            if(!Ready || directory==null || population==null || worldStore==null) return false;
            var game=GameManager.Instance;
            if(game==null || game.PlayerMotor==null) return false;
            if(game.PlayerEnterExit.IsPlayerInsideBuilding)
            {
                int building=game.PlayerEnterExit.BuildingDiscoveryData.buildingKey;
                BuildingSummary summary;
                if(building<0 || !directory.GetBuildingSummary(building,out summary)) return false;
                place=new AppointmentPlace {Kind="building",SettlementId=population.SettlementId,
                    BuildingKey=building};
                return true;
            }
            if(interior || !EntranceSurveyReady || geometry==null) return false;
            Vector3 feet=game.PlayerMotor.transform.position-Vector3.up;
            Vector3 local=directory.transform.InverseTransformPoint(feet);
            if(!Finite(local.x) || !Finite(local.y) || !Finite(local.z)) return false;
            Vector3 routeGround=geometry.ScenePoint(new RoutePoint(local.x,local.z));
            float verticalGap=Math.Abs(routeGround.y-feet.y);
            if(!Finite(verticalGap) || verticalGap>CapturedPointGroundTolerance) return false;
            place=new AppointmentPlace {Kind="world-point",SettlementId=population.SettlementId,
                BuildingKey=-1,X=AppointmentPlace.CanonicalWorldPointCoordinate(local.x),
                Y=AppointmentPlace.CanonicalWorldPointCoordinate(local.y),
                Z=AppointmentPlace.CanonicalWorldPointCoordinate(local.z),Detail="an outdoor meeting spot"};
            return true;
        }

        // Routes are transient native evidence. Each link begins at an existing
        // routine node and ends at the exact captured map coordinate; Core never
        // receives synthetic building identities or unverified coordinates.
        private List<AppointmentPlaceRoute> BuildAppointmentPlaceRoutes(Plan plan,
            IList<AppointmentRecord> bookings,int absoluteDay,out string reason,bool useCache=false)
        {
            reason="No collision-checked exterior route reaches the captured meeting point.";
            var result=new List<AppointmentPlaceRoute>();
            if(plan==null || bookings==null) return result;
            if(!Ready || !EntranceSurveyReady || geometry==null || population==null)
            {
                reason="The local street route survey is not ready yet.";
                return result;
            }
            bool persisted=true;
            foreach(var booking in bookings)
            {
                if(booking==null || booking.Place==null || booking.Place.Kind!="world-point" ||
                    (int)Math.Floor(booking.StartsAt/86400d)!=absoluteDay || booking.Status=="cancelled")continue;
                var current=worldStore.FindAppointment(booking.Id);
                if(current==null || current.Place==null || current.Place.Kind!="world-point" ||
                    current.Place.X!=booking.Place.X || current.Place.Y!=booking.Place.Y || current.Place.Z!=booking.Place.Z)
                {persisted=false;break;}
            }
            AppointmentPointRouteCache cached;
            if(useCache && persisted && appointmentPointRouteCache.TryGetValue(plan.Resident.Id,out cached) &&
                cached.Day==absoluteDay && cached.Revision==worldStore.AppointmentRevision &&
                cached.HouseholdRevision==worldStore.HouseholdDayRevision && cached.RoutineRevision==worldStore.RoutineRevision &&
                cached.SettlementId==population.SettlementId && ReferenceEquals(cached.Geometry,geometry))
            {reason="cached native-verified point routes";return cached.Routes;}
            var anchors=new List<int>();
            AddAnchor(anchors,plan.Resident.HomeKey);
            if(plan.Routine.Itinerary!=null)
            {
                foreach(var itinerary in ResidentRoutineDay.Itineraries(plan.Routine))
                    foreach(var stop in itinerary.Stops) AddAnchor(anchors,stop.BuildingKey);
            }
            else AddAnchor(anchors,plan.Routine.DestinationKey);

            foreach(var booking in bookings)
            {
                if(booking==null || booking.Place==null || booking.Place.Kind!="world-point" ||
                    (int)Math.Floor(booking.StartsAt/86400d)!=absoluteDay ||
                    booking.Status=="cancelled") continue;
                if(booking.Place.SettlementId!=population.SettlementId || booking.Place.BuildingKey>=0 ||
                    !Finite(booking.Place.X) || !Finite(booking.Place.Y) || !Finite(booking.Place.Z))
                { reason="The captured exterior meeting point is invalid or outside this settlement."; return new List<AppointmentPlaceRoute>(); }
                var route=new AppointmentPlaceRoute {AppointmentId=booking.Id,Place=CopyAppointmentPlace(booking.Place)};
                var target=new SavedRoutePoint {X=booking.Place.X,Z=booking.Place.Z};
                Vector3 targetGround=geometry.ScenePoint(new RoutePoint(target.X,target.Z));
                float targetGroundY=directory.transform.InverseTransformPoint(targetGround).y;
                if(!Finite(targetGroundY) || Math.Abs(targetGroundY-booking.Place.Y)>CapturedPointGroundTolerance)
                { reason="The saved outdoor point is above or below the surveyed walkable ground."; return new List<AppointmentPlaceRoute>(); }
                foreach(int anchor in anchors)
                {
                    TownGeometry.Entrance entrance;
                    if(!geometry.Entrances.TryGetValue(anchor,out entrance)) continue;
                    var from=new SavedRoutePoint {X=entrance.LocalDoor.x,Z=entrance.LocalDoor.z};
                    var points=geometry.RouteBetweenPoints(from,target);
                    if(points==null || points.Count<2) continue;
                    var last=points[points.Count-1];
                    if(Math.Abs(last.X-target.X)>0.001 || Math.Abs(last.Z-target.Z)>0.001) continue;
                    route.Connections.Add(new AppointmentPlaceRouteConnection {AnchorBuildingKey=anchor,Route=points});
                }
                if(route.Connections.Count==0)
                { reason="No verified saved-routine entrance has a collision-checked route to the exact captured point."; return new List<AppointmentPlaceRoute>(); }
                result.Add(route);
            }
            if(useCache && persisted)
            {
                if(appointmentPointRouteCache.Count>=256)appointmentPointRouteCache.Clear();
                appointmentPointRouteCache[plan.Resident.Id]=new AppointmentPointRouteCache {Day=absoluteDay,
                    HouseholdRevision=worldStore.HouseholdDayRevision,RoutineRevision=worldStore.RoutineRevision,
                    Revision=worldStore.AppointmentRevision,SettlementId=population.SettlementId,Geometry=geometry,Routes=result};
            }
            reason="verified";
            return result;
        }

        private static void AddAnchor(List<int> anchors,int building)
        { if(building>=0 && !anchors.Contains(building)) anchors.Add(building); }

        private static AppointmentPlace CopyAppointmentPlace(AppointmentPlace source)
        { return new AppointmentPlace {Kind=source.Kind,SettlementId=source.SettlementId,BuildingKey=source.BuildingKey,
            X=source.Kind=="world-point"?AppointmentPlace.CanonicalWorldPointCoordinate(source.X):source.X,
            Y=source.Kind=="world-point"?AppointmentPlace.CanonicalWorldPointCoordinate(source.Y):source.Y,
            Z=source.Kind=="world-point"?AppointmentPlace.CanonicalWorldPointCoordinate(source.Z):source.Z,Detail=source.Detail}; }

        private bool TryCreateRoutineRecoveryWithPoints(Plan plan,DailySchedule ordinary,int day,double now,int origin,
            double departAt,int wake,int sleep,double secondsPerMetre,out RoutineRecoveryPlan recovery,
            out string reason,out RoutineRecoveryFailure failure,bool originOutside=false)
        {
            var family=RuntimeFamily(plan,now);
            if(family!=null)
            {
                var pairedBookings=FamilyBookings(family.First,family.Second);
                var pairedRoutes=FamilyPointRoutes(family.First,family.Second,pairedBookings,day,out reason);
                if(pairedRoutes==null){recovery=null;failure=RoutineRecoveryFailure.RequiredAppointmentUnreachable;return false;}
                var home=population.Households.Find(h=>h.BuildingKey==family.Source.HomeKey && h.Generation==family.Source.Generation);
                var previous=CurrentRoutineRecovery(plan,now);
                return RoutineRecoveryPlanner.TryCreateWithHouseholdDay(family.Source,home,family.First.Routine,family.Second.Routine,
                    worldStore.WorldSeed,plan.Resident.Id,pairedBookings,pairedRoutes,now,origin,departAt,secondsPerMetre,
                    out recovery,out reason,out failure,originOutside,previous!=null && previous.HouseholdJointSkipped);
            }
            var bookings=worldStore.AppointmentsFor(plan.Resident.Id);
            var needed=new List<AppointmentRecord>();
            foreach(var booking in bookings)
                if(booking.Place!=null && booking.Place.Kind=="world-point" &&
                    RoutineRecoveryIdentity.Reserves(booking,plan.Resident.Id,day) &&
                    AppointmentTiming.PlannedLeaveAt(booking)>now) needed.Add(booking);
            IList<AppointmentPlaceRoute> routes=null;
            if(needed.Count>0)
            {
                routes=BuildAppointmentPlaceRoutes(plan,needed,day,out reason,true);
                if(routes.Count!=needed.Count)
                {recovery=null;failure=RoutineRecoveryFailure.RequiredAppointmentUnreachable;return false;}
            }
            return RoutineRecoveryPlanner.TryCreate(population.SettlementId,plan.Resident,plan.Routine,ordinary,
                bookings,routes,day,now,origin,departAt,wake,sleep,secondsPerMetre,out recovery,out reason,out failure,originOutside);
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
