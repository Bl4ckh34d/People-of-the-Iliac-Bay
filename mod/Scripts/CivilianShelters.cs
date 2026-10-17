using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Banking;
using DaggerfallWorkshop.Utility;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public sealed class CivilianShelterRoute : MonoBehaviour
    {
        public ShelterRoute Plan;
        public int Next;
        public float RetryAt;
    }
    public sealed partial class StreetPopulation
    {
        private float nextShelterSurvey;
        private bool ShelterAccessible(int key,int home,CivilianIncident alarm,double now)
        {
            if(alarm!=null && alarm.BuildingKey==key || DaggerfallBankManager.IsHouseOwned(key)) return false;
            var guards=GetComponent<GuardPopulation>();
            if(guards && guards.OccupiesCrimeScene(key,now)) return false;
            BuildingSummary summary;
            if(!directory.GetBuildingSummary(key,out summary)) return false;
            if(key==home) return true; // Residents have access to their own locked home.
            if(!RMBLayout.IsShop(summary.BuildingType) && summary.BuildingType!=DFLocation.BuildingTypes.Tavern &&
                summary.BuildingType!=DFLocation.BuildingTypes.GuildHall && summary.BuildingType!=DFLocation.BuildingTypes.Palace) return false;
            return GameManager.Instance.PlayerObject.GetComponent<PlayerActivate>().BuildingIsUnlocked(summary);
        }
        private bool TryShelterTarget(ResidentActor actor,AlarmPerson person,CivilianIncident alarm,Vector3 danger,double now,
            out Vector3 target,out int arrivedBuilding)
        {
            target=Vector3.zero;arrivedBuilding=-1;
            var cache=actor.GetComponent<CivilianShelterRoute>();
            if(!cache) cache=actor.gameObject.AddComponent<CivilianShelterRoute>();
            var from=new SavedRoutePoint{X=person.X,Z=person.Z};
            var ground=directory.transform.TransformPoint(new Vector3((float)person.X,(float)person.Y,(float)person.Z));
            if(cache.Plan!=null && !ShelterAccessible(cache.Plan.BuildingKey,actor.Resident.HomeKey,alarm,now)) cache.Plan=null;
            if(cache.Plan!=null)
            {
                while(cache.Next<cache.Plan.Points.Count-1 && Vector3.Distance(ground,
                    geometry.ScenePoint(new RoutePoint(cache.Plan.Points[cache.Next].X,cache.Plan.Points[cache.Next].Z)))<0.2f) cache.Next++;
                var next=cache.Plan.Points[cache.Next];
                if(!CivilianFlightSteering.Safe(ground,geometry.ScenePoint(new RoutePoint(next.X,next.Z)),danger)) cache.Plan=null;
            }
            if(cache.Plan==null && Time.unscaledTime>=cache.RetryAt && Time.unscaledTime>=nextShelterSurvey)
            {
                cache.RetryAt=Time.unscaledTime+2;nextShelterSurvey=Time.unscaledTime+0.1f;
                var nearby=new List<TownGeometry.Entrance>();
                foreach(var entry in geometry.Entrances.Values)
                    if(Vector3.Distance(ground,geometry.SceneDoor(entry))<=100 && ShelterAccessible(entry.BuildingKey,actor.Resident.HomeKey,alarm,now)) nearby.Add(entry);
                nearby.Sort((a,b)=>{
                    int compare=(geometry.SceneDoor(a)-ground).sqrMagnitude.CompareTo((geometry.SceneDoor(b)-ground).sqrMagnitude);
                    return compare!=0 ? compare : a.BuildingKey.CompareTo(b.BuildingKey);
                });
                var candidates=new List<ShelterRoute>();
                for(int i=0;i<nearby.Count && i<8;i++)
                {
                    var entry=nearby[i];
                    candidates.Add(new ShelterRoute{BuildingKey=entry.BuildingKey,Accessible=true,
                        Points=geometry.RouteBetweenPoints(from,new SavedRoutePoint{X=entry.LocalDoor.x,Z=entry.LocalDoor.z})});
                }
                var localDanger=directory.transform.InverseTransformPoint(danger);
                cache.Plan=ShelterRoutes.Nearest(from,new SavedRoutePoint{X=localDanger.x,Z=localDanger.z},candidates);cache.Next=1;
            }
            if(cache.Plan==null) return false;
            var point=cache.Plan.Points[cache.Next];target=geometry.ScenePoint(new RoutePoint(point.X,point.Z));
            if(cache.Next==cache.Plan.Points.Count-1) arrivedBuilding=cache.Plan.BuildingKey;
            return true;
        }
    }
}
