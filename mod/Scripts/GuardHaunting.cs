using System;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using LivingCities.Core;
using UnityEngine;
namespace LivingCities.DFU
{
    public sealed partial class GuardPopulation
    {
        private TownGeometry refugeGeometry;
        private readonly Dictionary<int,List<List<GuardPoint3>>> refugeInteriors=new Dictionary<int,List<List<GuardPoint3>>>();
        private bool HauntedNow(double now)
        { return Roster!=null && SettlementHaunting.StreetsDangerous(Roster.SettlementId,
            GameManager.Instance.PlayerEntity.GlobalVars.GetGlobalVar(0),now); }
        private void ReleaseSafeRefuges(double now)
        {
            bool lifted=GameManager.Instance.PlayerEntity.GlobalVars.GetGlobalVar(0);
            foreach(var guard in Roster.Guards)
                if(guard.IsAlive && guard.Refuge!=null && !guard.Refuge.WallDuty && !guard.Refuge.ReleasedAt.HasValue)
                {
                    var release=SettlementHaunting.FirstSafeTime(Roster.SettlementId,lifted,guard.Refuge.StartsAt,now);
                    if(release.HasValue) guard.Refuge.Release(release.Value);
                }
        }
        private bool ObserveHauntingThreat(DaggerfallEntityBehaviour target,double now)
        {
            if(!HauntedNow(now) || !target || target.Entity==null || !target.gameObject.activeInHierarchy || target.Entity.CurrentHealth<=0 ||
                target.Entity.Team==MobileTeams.PlayerAlly) return false;
            var enemy=target.Entity as EnemyEntity;
            if(enemy==null || (enemy.MobileEnemy.ID!=(int)MobileTypes.Ghost && enemy.MobileEnemy.ID!=(int)MobileTypes.Wraith)) return false;
            var motor=target.GetComponent<EnemyMotor>();
            if(!motor || !motor.IsHostile) return false;
            foreach(var guard in Roster.Guards)
            {
                if(!guard.IsAlive || guard.Interior!=null || (guard.Duty!=GuardDuty.Posted && guard.Duty!=GuardDuty.Engaged)) continue;
                GuardActor body;
                if(guard.Duty==GuardDuty.Engaged && actors.TryGetValue(guard.Id,out body) && body) body.Capture(now);
                var origin=Roster.Position(guard.Id,now);
                if(!CivilianResponses.Sees(geometry.ScenePoint(new RoutePoint(origin.X,origin.Z))+Vector3.up,target.transform.position,20)) continue;
                TryHauntingRefuge(guard,now);
            }
            // Even when no safe route exists, never dispatch guards to fight the haunting.
            return true;
        }
        private bool TryHauntingRefuge(GuardRecord guard,double now)
        {
            if(refugeGeometry==null) refugeGeometry=new TownGeometry(directory,GameManager.Instance.StreamingWorld.GetCurrentCityNavigation());
            var origin=Roster.Position(guard.Id,now);
            List<SavedRoutePoint> home=null;
            if(guard.Duty==GuardDuty.Engaged)
            {
                home=refugeGeometry.RouteBetweenPoints(origin,guard.Post);
                if(home.Count<2) return false;
            }
            var candidates=new List<TownGeometry.Entrance>();
            foreach(var entrance in refugeGeometry.Entrances.Values)
            {
                BuildingSummary building;
                if(directory.GetBuildingSummary(entrance.BuildingKey,out building) && building.BuildingType==DFLocation.BuildingTypes.Town23 &&
                    (building.ModelID==444 || building.ModelID==445 || building.ModelID==446)) candidates.Add(entrance);
            }
            candidates.Sort((a,b)=>{
                double ax=a.LocalDoor.x-origin.X,az=a.LocalDoor.z-origin.Z,bx=b.LocalDoor.x-origin.X,bz=b.LocalDoor.z-origin.Z;
                int order=(ax*ax+az*az).CompareTo(bx*bx+bz*bz); return order!=0 ? order : a.BuildingKey.CompareTo(b.BuildingKey);
            });
            for(int i=0;i<Math.Min(8,candidates.Count);i++)
            {
                var entrance=candidates[i];
                var outside=refugeGeometry.RouteBetweenPoints(origin,new SavedRoutePoint { X=entrance.LocalDoor.x,Z=entrance.LocalDoor.z });
                if(outside.Count<2) continue;
                List<List<GuardPoint3>> rooms;
                if(!refugeInteriors.TryGetValue(entrance.BuildingKey,out rooms))
                {
                    DaggerfallStaticDoors owner=null; StaticDoor selected=default(StaticDoor); float best=float.MaxValue;
                    foreach(var candidate in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                    {
                        if(candidate.Doors==null) continue;
                        for(int d=0;d<candidate.Doors.Length;d++)
                        {
                            var door=candidate.Doors[d];
                            if(door.doorType!=DoorTypes.Building || door.buildingKey!=entrance.BuildingKey) continue;
                            float gap=(candidate.GetDoorPosition(d)-refugeGeometry.SceneDoor(entrance)).sqrMagnitude;
                            if(gap<best) { owner=candidate; selected=door; best=gap; }
                        }
                    }
                    if(!owner) continue;
                    rooms=RefugeGeometry.Survey(owner.transform,selected); refugeInteriors[entrance.BuildingKey]=rooms;
                }
                var exterior=new List<GuardPoint3>(); foreach(var point in outside) exterior.Add(new GuardPoint3 { X=point.X,Z=point.Z });
                foreach(var room in rooms)
                {
                    try
                    {
                        Roster.TakeRefuge(guard.Id,GuardRefugeJourney.Create(entrance.BuildingKey,exterior,room,now,3.8,12),now,home);
                        Debug.Log("LIVING_CITIES_GUARD_REFUGE: "+guard.Id+"; building="+entrance.BuildingKey); return true;
                    }
                    catch(ArgumentException) { /* Occupied destination: try another surveyed standing place. */ }
                }
            }
            return false;
        }
    }
}
