using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Banking;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed class CivilianShelterRoute : MonoBehaviour
    {
        public ShelterRoute Plan;
        public int Next;
        public float RetryAt;
        public readonly ShelterPursuit Pursuit=new ShelterPursuit();
        public bool EmergencyPending;
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
                summary.BuildingType!=DFLocation.BuildingTypes.GuildHall && summary.BuildingType!=DFLocation.BuildingTypes.Palace &&
                !(summary.BuildingType>=DFLocation.BuildingTypes.House1 && summary.BuildingType<=DFLocation.BuildingTypes.House6)) return false;
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
            if(!GameManager.IsGamePaused)
            {
                if(ObserveShelterPursuit(actor,person,alarm,from,cache.Pursuit))cache.EmergencyPending=true;
                else if(!cache.Pursuit.HasKnownPursuer)cache.EmergencyPending=false;
            }
            if(cache.Plan!=null && !ShelterAccessible(cache.Plan.BuildingKey,actor.Resident.HomeKey,alarm,now)) cache.Plan=null;
            if(cache.Plan!=null)
            {
                while(cache.Next<cache.Plan.Points.Count-1 && Vector3.Distance(ground,
                    geometry.ScenePoint(new RoutePoint(cache.Plan.Points[cache.Next].X,cache.Plan.Points[cache.Next].Z)))<0.2f) cache.Next++;
                var next=cache.Plan.Points[cache.Next];
                if(!CivilianFlightSteering.Safe(ground,geometry.ScenePoint(new RoutePoint(next.X,next.Z)),danger)) cache.Plan=null;
            }
            if((cache.EmergencyPending || cache.Plan==null && Time.unscaledTime>=cache.RetryAt) &&
                !GameManager.IsGamePaused && Time.unscaledTime>=nextShelterSurvey)
            {
                cache.RetryAt=Time.unscaledTime+2;nextShelterSurvey=Time.unscaledTime+0.1f;
                var remaining=RemainingShelterRoute(cache,from);
                var nearby=new List<TownGeometry.Entrance>();
                float radius=cache.EmergencyPending?30:100;
                foreach(var entry in geometry.Entrances.Values)
                    if(Vector3.Distance(ground,geometry.SceneDoor(entry))<=radius && ShelterAccessible(entry.BuildingKey,actor.Resident.HomeKey,alarm,now)) nearby.Add(entry);
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
                var threat=new SavedRoutePoint{X=localDanger.x,Z=localDanger.z};
                var facing=directory.transform.InverseTransformDirection(actor.transform.forward);
                cache.Plan=cache.EmergencyPending?ShelterPursuit.Choose(from,threat,remaining,candidates,
                    new SavedRoutePoint{X=facing.x,Z=facing.z}):ShelterRoutes.Nearest(from,threat,candidates);
                cache.Next=1;cache.EmergencyPending=false;
            }
            if(cache.Plan==null) return false;
            var point=cache.Plan.Points[cache.Next];target=geometry.ScenePoint(new RoutePoint(point.X,point.Z));
            if(cache.Next==cache.Plan.Points.Count-1) arrivedBuilding=cache.Plan.BuildingKey;
            return true;
        }
        private static ShelterRoute RemainingShelterRoute(CivilianShelterRoute cache,SavedRoutePoint from)
        {
            if(cache.Plan==null)return null;
            var route=new ShelterRoute{BuildingKey=cache.Plan.BuildingKey,Accessible=cache.Plan.Accessible,
                Points=new List<SavedRoutePoint>{new SavedRoutePoint{X=from.X,Z=from.Z}}};
            for(int i=cache.Next;i<cache.Plan.Points.Count;i++)
                route.Points.Add(new SavedRoutePoint{X=cache.Plan.Points[i].X,Z=cache.Plan.Points[i].Z});
            return route;
        }
        private void TryRerouteEscapingWitnessToShelter(ResidentActor actor,AlarmPerson person,double now)
        {
            if(GameManager.IsGamePaused||person.Escape==null||person.BuildingKey!=-1||person.ReachedRefuge||now<person.Escape.ExitAt)return;
            var alarm=worldStore.ActiveAlarm(person.ResidentId,now);if(alarm==null)return;
            var cache=actor.GetComponent<CivilianShelterRoute>();
            if(!cache)cache=actor.gameObject.AddComponent<CivilianShelterRoute>();
            var from=new SavedRoutePoint{X=person.X,Z=person.Z};
            if(ObserveShelterPursuit(actor,person,alarm,from,cache.Pursuit))cache.EmergencyPending=true;
            else if(!cache.Pursuit.HasKnownPursuer)cache.EmergencyPending=false;
            if(!cache.EmergencyPending)return;
            var danger=GameManager.Instance.PlayerMotor.transform.position;
            string threat=alarm.KillerThreatId??alarm.ThreatId;
            if(threat!=null)
            {
                var guards=GetComponent<GuardPopulation>();
                if(!guards||!guards.TryHostileThreatPosition(threat,out danger))return;
            }
            Vector3 target;int building;
            if(!TryShelterTarget(actor,person,alarm,danger,now,out target,out building)||cache.EmergencyPending||cache.Plan==null)return;
            var route=RemainingShelterRoute(cache,from);
            if(route==null||route.Points.Count<2)return;
            person.Escape=person.Escape.RedirectToShelter(now,route.Points,cache.Plan.BuildingKey);
            person.EscapeAdvancedAt=now;
        }
        private bool ObserveShelterPursuit(ResidentActor actor,AlarmPerson person,CivilianIncident alarm,
            SavedRoutePoint from,ShelterPursuit pursuit)
        {
            string source=alarm==null?null:alarm.KillerThreatId??alarm.ThreatId;
            var game=GameManager.Instance;Vector3 position=Vector3.zero;
            bool known=false;
            if(source!=null)
            {
                var guards=GetComponent<GuardPopulation>();
                known=guards&&guards.TryHostileThreatPosition(source,out position);
            }
            else if(alarm!=null&&(alarm.PlayerAttributed||person.DirectWitness))
            {
                source="player";position=game.PlayerMotor.transform.position;
                known=true; // The alarm identifies this pursuer; looking behind is not required.
            }
            var local=directory.transform.InverseTransformPoint(position);
            return pursuit.ShouldSearch(source,Time.unscaledTime,from,new SavedRoutePoint{X=local.x,Z=local.z},known);
        }
    }
}
