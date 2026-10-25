using System;
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
        private sealed class PreparedWall
        {
            public string GuardId;
            public int BuildingKey;
            public List<SavedRoutePoint> Outside;
            public List<GuardPoint3> Room;
            public int? BedRecord;
        }
        private readonly Dictionary<int,List<WallQuarterBeds.SurveyedBed>> wallBedRoutes = new Dictionary<int,List<WallQuarterBeds.SurveyedBed>>();
        private bool WallAvailable(int building,string guardId,GateDutyPair pair,int? bedRecord=null)
        {
            return !Roster.Guards.Exists(g=>g.Id!=guardId && g.IsAlive &&
                (g.PostRecall!=null && g.PostRecall.WallDuty!=null && g.PostRecall.WallDuty.ConflictsWith(building,bedRecord) ||
                 g.Refuge!=null && g.Refuge.ConflictsWith(building,bedRecord))) &&
                !gateDutyPairs.Exists(p=>p!=pair && p.Wall!=null && p.Wall.BuildingKey==building &&
                    (!p.Wall.BedRecord.HasValue || !bedRecord.HasValue || p.Wall.BedRecord==bedRecord) &&
                    Roster.Guards.Exists(g=>g.Id==p.Wall.GuardId && g.IsAlive));
        }
        private System.Collections.IEnumerator PrepareWallPatrol(GateDutyPair pair,GuardRecord guard)
        {
            if(pair.Wall!=null && pair.Wall.GuardId==guard.Id && WallAvailable(pair.Wall.BuildingKey,guard.Id,pair,pair.Wall.BedRecord)) yield break;
            pair.Wall=null;
            var rosterOwner=Roster;var town=directory;
            Func<bool> valid=()=>OwnsExterior && Roster==rosterOwner && directory==town && guard.IsAlive;
            if(refugeGeometry==null)
            {
                var mapped=new TownGeometry(directory,GameManager.Instance.StreamingWorld.GetCurrentCityNavigation(),false);
                yield return mapped.ReadEntranceSteps();
                if(!valid()) yield break;
                refugeGeometry=mapped;
            }
            yield return null;
            if(!valid()) yield break;
            var staging=pair.ReturnInside;
            var candidates=new List<TownGeometry.Entrance>();
            foreach(var entrance in refugeGeometry.Entrances.Values)
            {
                BuildingSummary building;
                if(directory.GetBuildingSummary(entrance.BuildingKey,out building) && building.BuildingType==DFLocation.BuildingTypes.Town23)
                    candidates.Add(entrance);
            }
            candidates.Sort((a,b)=> {
                double ax=a.LocalDoor.x-staging.X,az=a.LocalDoor.z-staging.Z,bx=b.LocalDoor.x-staging.X,bz=b.LocalDoor.z-staging.Z;
                int n=(ax*ax+az*az).CompareTo(bx*bx+bz*bz); return n!=0?n:a.BuildingKey.CompareTo(b.BuildingKey);
            });
            for(int i=0;i<Math.Min(12,candidates.Count);i++)
            {
                yield return null;
                if(!valid()) yield break;
                var entrance=candidates[i];
                var prepared=new TownGeometry.RouteResult();
                yield return refugeGeometry.ReadRouteSteps(staging,new SavedRoutePoint {X=entrance.LocalDoor.x,Z=entrance.LocalDoor.z},prepared,valid);
                if(!valid()) yield break;
                var outside=prepared.Points;
                if(outside==null || outside.Count<2) continue;
                List<List<GuardPoint3>> rooms;
                if(!refugeInteriors.TryGetValue(entrance.BuildingKey,out rooms) || !wallBedRoutes.ContainsKey(entrance.BuildingKey))
                {
                    DaggerfallStaticDoors selectedOwner=null; StaticDoor selected=default(StaticDoor); float best=float.MaxValue;
                    foreach(var owner in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                    for(int d=0;d<(owner.Doors==null?0:owner.Doors.Length);d++)
                    {
                        var door=owner.Doors[d];
                        if(door.doorType!=DoorTypes.Building || door.buildingKey!=entrance.BuildingKey) continue;
                        float gap=(owner.GetDoorPosition(d)-refugeGeometry.SceneDoor(entrance)).sqrMagnitude;
                        if(gap<best) {best=gap;selectedOwner=owner;selected=door;}
                    }
                    if(!selectedOwner) continue;
                    rooms=new List<List<GuardPoint3>>();
                    var beds=new List<WallQuarterBeds.SurveyedBed>();
                    var survey=RefugeGeometry.SurveySteps(selectedOwner.transform,selected,rooms,beds);
                    try { while(survey.MoveNext()) {yield return null;if(!valid()) yield break;} }
                    finally { (survey as IDisposable)?.Dispose(); }
                    refugeInteriors[entrance.BuildingKey]=rooms;
                    wallBedRoutes[entrance.BuildingKey]=beds;
                }
                if(!valid()) yield break;
                // Another emergency may have claimed these quarters while the
                // model survey was yielding. Never overwrite that assignment.
                var availableBeds=wallBedRoutes[entrance.BuildingKey];
                if(availableBeds.Count>0)
                {
                    availableBeds.Sort((a,b)=>a.Anchor.RecordIndex.CompareTo(b.Anchor.RecordIndex));
                    var bed=availableBeds.Find(b=>WallAvailable(entrance.BuildingKey,guard.Id,pair,b.Anchor.RecordIndex));
                    if(bed==null) continue;
                    pair.Wall=new PreparedWall {GuardId=guard.Id,BuildingKey=entrance.BuildingKey,Outside=outside,
                        Room=bed.EntranceRoute,BedRecord=bed.Anchor.RecordIndex};
                    yield break;
                }
                if(!WallAvailable(entrance.BuildingKey,guard.Id,pair)) continue;
                List<GuardPoint3> room=null; double longest=0;
                foreach(var route in rooms)
                {
                    double length=0;
                    for(int p=1;p<route.Count;p++)
                    { var a=route[p-1];var b=route[p];length+=Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y)+(a.Z-b.Z)*(a.Z-b.Z)); }
                    if(length>longest) {longest=length;room=route;}
                }
                if(room==null || longest<4) continue;
                pair.Wall=new PreparedWall {GuardId=guard.Id,BuildingKey=entrance.BuildingKey,Outside=outside,Room=room};
                yield break;
            }
        }
        private void ArmGateRecall(GuardRecord guard,GateDutyPair pair,double now)
        {
            var route=pair.ReturnRoute;if(route==null) return;
            double dusk=(Math.Floor(now/86400)*86400)+DaggerfallDateTime.DuskHour*3600;
            double departure=dusk-RouteLength(route)*DaggerfallUnity.Instance.WorldTime.TimeScale/1.3-60;
            if(AtPoint(guard.Post,pair.Outside) && guard.PostRecall==null && guard.IsAlive && now<departure &&
                guard.ArrivesAt<=departure && guard.Interior==null && guard.Refuge==null &&
                (guard.Duty==GuardDuty.Posted || guard.Duty==GuardDuty.Returning &&
                    guard.IncidentId!=null && guard.IncidentId.StartsWith("post:",StringComparison.Ordinal)))
                Roster.SchedulePostRecall(guard.Id,route,departure,dusk,1.3,DaggerfallUnity.Instance.WorldTime.TimeScale);
            var recall=guard.PostRecall;var prepared=pair.Wall;
            if(recall==null || recall.WallDuty!=null || prepared==null || prepared.GuardId!=guard.Id ||
                !WallAvailable(prepared.BuildingKey,guard.Id,pair,prepared.BedRecord)) return;
            var exterior=new List<GuardPoint3>();
            foreach(var point in recall.Route) exterior.Add(new GuardPoint3 {X=point.X,Z=point.Z});
            for(int p=1;p<prepared.Outside.Count;p++) exterior.Add(new GuardPoint3 {X=prepared.Outside[p].X,Z=prepared.Outside[p].Z});
            var plan=GuardRefugeJourney.Create(prepared.BuildingKey,exterior,prepared.Room,recall.DepartsAt,1.3,DaggerfallUnity.Instance.WorldTime.TimeScale);
            plan.WallDuty=true;
            plan.RestBedRecord=prepared.BedRecord;
            plan.ReturnExterior=prepared.Outside.ConvertAll(p=>new GuardPoint3 {X=p.X,Z=p.Z});plan.ReturnExterior.Reverse();
            plan.Release((Math.Floor(recall.DepartsAt/86400)+1)*86400+DaggerfallDateTime.DawnHour*3600);
            Roster.ScheduleWallDuty(guard.Id,plan);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_WALL_SHIFT: "+guard.Id+"; building="+prepared.BuildingKey);
        }
    }
}
