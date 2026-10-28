using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class GuardPopulation
    {
        private Coroutine dayRestSurvey;
        private float nextDayRestAttempt;
        private int nextRestGuard;

        private void UpdateDayRest(double now)
        {
            if (dayRestSurvey != null || Time.unscaledTime < nextDayRestAttempt || !OwnsExterior || GameManager.IsGamePaused) return;
            bool shelter=HauntingShelterNow(now);
            GuardRecord guard = null;
            for (int i = 0; i < Roster.Guards.Count; i++)
            {
                nextRestGuard %= Roster.Guards.Count;
                var candidate = Roster.Guards[nextRestGuard++];
                if (!candidate.IsAlive || candidate.Interior != null || candidate.Refuge != null || IsCustodyEscort(candidate) ||
                    !shelter && (GuardShifts.OnDuty(candidate,now) || candidate.DayRestUntil<=now || candidate.Duty!=GuardDuty.Posted ||
                        Roster.IsOnPatrol(candidate.Id) || candidate.IncidentId!=null || IsGateGuard(candidate.Id))) continue;
                guard = candidate; break;
            }
            if (guard != null)
            {
                // Retry unavailable exterior entrances at a bounded cadence.
                nextDayRestAttempt = Time.unscaledTime + (shelter ? .5f : 4f);
                dayRestSurvey = StartCoroutine(PrepareDayRest(guard, now));
            }
        }

        private IEnumerator PrepareDayRest(GuardRecord guard, double started)
        {
            var roster = Roster; var town = directory;
            bool shelter=HauntingShelterNow(Now());
            Func<bool> valid = () => OwnsExterior && Roster == roster && directory == town &&
                guard.IsAlive && !IsCustodyEscort(guard) && (shelter ? HauntingShelterNow(Now()) &&
                    (guard.Duty==GuardDuty.Posted || guard.Duty==GuardDuty.Engaged || guard.Duty==GuardDuty.Responding ||
                     guard.Duty==GuardDuty.Searching || guard.Duty==GuardDuty.Returning) : !GuardShifts.OnDuty(guard, Now()) && guard.DayRestUntil > Now() && guard.Duty == GuardDuty.Posted &&
                    !Roster.IsOnPatrol(guard.Id) && guard.IncidentId == null) && guard.Refuge == null && guard.Interior == null;
            Func<SavedRoutePoint,SavedRoutePoint,bool> samePoint=(a,b)=>
                a!=null && b!=null && (a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z)<.05*.05;
            try
            {
                yield return null; // Completion must not leave a stale coroutine handle.
                if (refugeGeometry == null)
                {
                    var mapped = new TownGeometry(directory, GameManager.Instance.StreamingWorld.GetCurrentCityNavigation(), false);
                    yield return mapped.ReadEntranceSteps();
                    if (!valid()) yield break;
                    refugeGeometry = mapped;
                }
                var candidates = new List<TownGeometry.Entrance>();
                foreach (var entrance in refugeGeometry.Entrances.Values)
                {
                    BuildingSummary building;
                    if (directory.GetBuildingSummary(entrance.BuildingKey, out building) &&
                        building.BuildingType == DFLocation.BuildingTypes.Town23)
                        candidates.Add(entrance);
                }
                candidates.Sort((a, b) => {
                    double ax = a.LocalDoor.x - guard.Post.X, az = a.LocalDoor.z - guard.Post.Z;
                    double bx = b.LocalDoor.x - guard.Post.X, bz = b.LocalDoor.z - guard.Post.Z;
                    int order = (ax * ax + az * az).CompareTo(bx * bx + bz * bz);
                    return order != 0 ? order : a.BuildingKey.CompareTo(b.BuildingKey);
                });
                foreach (var entrance in candidates)
                {
                    if (!valid()) yield break;
                    var origin=Roster.Position(guard.Id,Now());
                    var route = new TownGeometry.RouteResult();
                    yield return refugeGeometry.ReadRouteSteps(origin,
                        new SavedRoutePoint { X = entrance.LocalDoor.x, Z = entrance.LocalDoor.z }, route, valid);
                    if (!valid() || route.Points == null || route.Points.Count < 2) continue;
                    List<SavedRoutePoint> home=null;
                    if(shelter && guard.Duty!=GuardDuty.Posted)
                    {
                        var returning=new TownGeometry.RouteResult();
                        yield return refugeGeometry.ReadRouteSteps(origin,guard.Post,returning,valid);
                        home=returning.Points;
                        if(!valid() || home==null || home.Count<2)continue;
                    }
                    // Withdrawal needs a real reachable quarters entrance. Interior furniture
                    // and bed routes are resolved only when the player loads these quarters.
                    double departure=Now();
                    // Route surveys yield. A time jump, patrol or post migration
                    // can invalidate their start/home while the survey is running.
                    // Advance first, then recheck immediately before strict mutation.
                    var current=Roster.Position(guard.Id,departure);
                    if(!valid() || !samePoint(current,route.Points[0]) ||
                        shelter && guard.Duty!=GuardDuty.Posted &&
                        (home==null || home.Count<2 || !samePoint(home[0],current) || !samePoint(home[home.Count-1],guard.Post)))
                        yield break;
                    var plan=GuardRefugeJourney.Create(entrance.BuildingKey,
                        route.Points.ConvertAll(p=>new GuardPoint3{X=p.X,Z=p.Z}),null,
                        departure,1.3,DaggerfallUnity.Instance.WorldTime.TimeScale);
                    plan.HauntingShelter=shelter;
                    var timing=GuardRefugeJourney.CopyValidated(plan);
                    if(!shelter && !timing.ReleaseToReturnBy(guard.DayRestUntil))continue;
                    Roster.TakeRefuge(guard.Id,plan,departure,home);
                    if(!shelter)guard.Refuge.ReleaseToReturnBy(guard.DayRestUntil);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUARD_SHIFT_REST: "+guard.Id+"; shift="+guard.Shift+
                        "; building="+entrance.BuildingKey+"; bedDeferred=true; hauntingShelter="+shelter);
                    yield break;
                }
            }
            finally { dayRestSurvey = null; }
        }
        private DaggerfallInterior restLayout;
        private List<WallQuarterBeds.SurveyedBed> loadedRestBeds;
        private List<List<GuardPoint3>> loadedRestFloors;

        private void MaterializeDayRest(double now)
        {
            if(!interior) {restLayout=null;loadedRestBeds=null;loadedRestFloors=null;return;}
            int building=interior.EntryDoor.buildingKey;
            if(!Roster.Guards.Exists(g=>g.IsAlive && g.Refuge!=null && g.Refuge.BuildingKey==building && g.Refuge.RestPending))return;
            if(!ReferenceEquals(restLayout,interior))
            {
                restLayout=interior;
                loadedRestBeds=new List<WallQuarterBeds.SurveyedBed>();
                loadedRestFloors=new List<List<GuardPoint3>>();
                var beds=new List<BedAnchor>();
                foreach(var furnished in WallQuarterBeds.Furnish(interior))beds.Add(furnished.Anchor);
                beds.AddRange(BedGeometry.Read(interior));
                var navigation=new InteriorGeometry(interior);
                foreach(var bed in beds)
                {
                    Vector3 beside;
                    if(!navigation.StandBesideBed(bed,out beside))continue;
                    var route=navigation.RouteToExit(beside);
                    if(route.Count<2)continue;
                    route.Reverse();loadedRestBeds.Add(new WallQuarterBeds.SurveyedBed {Anchor=bed,EntranceRoute=route});
                }
                foreach(var anchor in navigation.LocalAnchors)
                {
                    var route=navigation.RouteToExit(interior.transform.TransformPoint(anchor));
                    if(route.Count<2)continue;
                    route.Reverse();loadedRestFloors.Add(route);
                }
            }
            foreach(var guard in Roster.Guards)
            {
                var rest=guard.Refuge;
                if(!guard.IsAlive || rest==null || !rest.RestPending || rest.BuildingKey!=building)continue;
                var phase=rest.Evaluate(now).Phase;
                if(phase!=RefugePhase.Approaching && phase!=RefugePhase.Sheltered)continue;
                double returnBy=guard.DayRestUntil>now ? guard.DayRestUntil : GuardShifts.NextChange(now);
                bool placed=false;
                foreach(var bed in loadedRestBeds)
                    if(RestDestinationAvailable(guard.Id,building,bed.EntranceRoute,bed.Anchor.RecordIndex) &&
                        rest.MaterializeRest(bed.EntranceRoute,bed.Anchor.RecordIndex,now,returnBy))
                    {placed=true;break;}
                if(!placed)
                    foreach(var route in loadedRestFloors)
                        if(RestDestinationAvailable(guard.Id,building,route,null) &&
                            rest.MaterializeRest(route,null,now,returnBy))break;
            }
        }

        private bool RestDestinationAvailable(string guardId,int building,List<GuardPoint3> route,int? bed)
        {
            var destination=route[route.Count-1];
            foreach(var other in Roster.Guards)
            {
                var rest=other.Refuge;
                if(other.Id==guardId || rest==null || rest.BuildingKey!=building || rest.RestPending || rest.WallRoute!=null)continue;
                var snapshot=rest.Evaluate(other.IsAlive ? Now() : other.DiedAt);
                if(snapshot.Phase==RefugePhase.Returning || snapshot.Phase==RefugePhase.Complete)continue;
                if(bed.HasValue && rest.RestBedRecord==bed)return false;
                var point=other.IsAlive ? rest.Interior[rest.Interior.Count-1] : snapshot.Position;
                if(point==null)continue;
                double x=destination.X-point.X,z=destination.Z-point.Z;
                if(Math.Abs(destination.Y-point.Y)<1.8 && x*x+z*z<.8*.8)return false;
            }
            return true;
        }
    }
}
