using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private void ObserveExteriorStaticWitnesses(CivilianIncident alarm,Vector3 victim,double now,DaggerfallEntityBehaviour attacker=null)
        {
            if(!directory || worldStore==null || population==null || interior || alarm==null || alarm.BuildingKey!=-1)return;
            var source=attacker?attacker:GameManager.Instance.PlayerEntityBehaviour;
            if(!source || source.Entity==null || !source.gameObject.activeInHierarchy || source.Entity.IsInvisible)return;
            SurveyStationaryObservers();
            foreach(var entry in stationaryParticipants.Values)
            {
                // Direct evidence requires the same awake, current-world and
                // facing/LOS checks as other scene memories. Hearing is separate.
                if(!entry.Npc || !entry.Observer || !entry.Observer.Sees(victim,20) ||
                    !entry.Observer.Sees(source.transform.position,20))continue;
                var local=directory.transform.InverseTransformPoint(entry.Npc.transform.position);
                var witness=worldStore.Witness(alarm.Id,entry.Person.Id,now);if(witness==null)continue;
                witness.StaticWitness=true;witness.HasPosition=true;witness.BuildingKey=-1;witness.X=local.x;witness.Y=local.y;witness.Z=local.z;
            }
        }
    }
    public sealed partial class GuardPopulation
    {
        private float nextStaticWitnessCheck;
        private bool HasPhysicalStaticWitness(NativePerson person,Vector3 position)
        {
            foreach(var npc in directory.GetComponentsInChildren<StaticNPC>())
            {
                if(!npc.gameObject.activeInHierarchy||(npc.transform.position-position).sqrMagnitude>1)continue;
                var d=npc.Data;
                if(d.billboardArchiveIndex==person.TextureArchive&&d.billboardRecordIndex==person.TextureRecord&&d.factionID==person.FactionId)return true;
            }
            return false;
        }
        private List<SavedRoutePoint> CorpseSearchPatrol(CivilianIncident alarm,SavedRoutePoint body)
        {
            // Visit a physically visible caller through the ordinary search loop.
            // Their identity evidence is still obtained only by actual questioning.
            var people=new List<AlarmPerson>(alarm.People);
            people.Sort((a,b)=>((a.X-body.X)*(a.X-body.X)+(a.Z-body.Z)*(a.Z-body.Z)).CompareTo(
                (b.X-body.X)*(b.X-body.X)+(b.Z-body.Z)*(b.Z-body.Z)));
            foreach(var witness in people)
            {
                if(!witness.StaticWitness || !witness.DirectWitness || witness.StatementDelivered || !witness.HasPosition)continue;
                var native=worldStore.NativePeopleFor(directory.MapID).Find(p=>p.Id==witness.ResidentId && p.IsAlive);
                var point=directory.transform.TransformPoint(new Vector3((float)witness.X,(float)witness.Y,(float)witness.Z));
                if(native==null || !HasPhysicalStaticWitness(native,point) ||
                    !CivilianResponses.Sees(geometry.ScenePoint(new RoutePoint(body.X,body.Z))+Vector3.up,point,20))continue;
                foreach(var offset in new[] {new Vector2(2,0),new Vector2(-2,0),new Vector2(0,2),new Vector2(0,-2)})
                {
                    var approach=new SavedRoutePoint{X=witness.X+offset.x,Z=witness.Z+offset.y};
                    var contact=geometry.ScenePoint(new RoutePoint(approach.X,approach.Z))+Vector3.up;
                    if(Vector3.Distance(contact,point)>4 || !CivilianResponses.Sees(contact,point,5))continue;
                    var route=CorpsePatrolLoop(body,approach);if(route==null)continue;
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CORPSE_STATIC_PATROL: incident="+alarm.Id+"; person="+native.Id);
                    return route;
                }
            }
            foreach(var offset in new[] {new Vector2(12,0),new Vector2(-12,0),new Vector2(0,12),new Vector2(0,-12)})
            {
                var route=CorpsePatrolLoop(body,new SavedRoutePoint{X=body.X+offset.x,Z=body.Z+offset.y});
                if(route!=null)return route;
            }
            return null;
        }
        private List<SavedRoutePoint> CorpsePatrolLoop(SavedRoutePoint body,SavedRoutePoint destination)
        {
            var route=geometry.RouteBetweenPoints(body,destination);if(route.Count<2)return null;
            double length=0;
            for(int i=1;i<route.Count;i++)
            {double x=route[i].X-route[i-1].X,z=route[i].Z-route[i-1].Z;length+=Math.Sqrt(x*x+z*z);}
            if(length<2 || length>400 || 2*length/2.8*DaggerfallUnity.Instance.WorldTime.TimeScale>UnwitnessedScene.OccupationSeconds)return null;
            for(int i=route.Count-2;i>=0;i--)route.Add(new SavedRoutePoint{X=route[i].X,Z=route[i].Z});
            return route;
        }
        private void SurveyStaticWitnessStatements(double now)
        {
            if(!OwnsExterior||Time.unscaledTime<nextStaticWitnessCheck)return;
            nextStaticWitnessCheck=Time.unscaledTime+1;
            foreach(var alarm in worldStore.OutdoorCorpses(directory.MapID))
            {
                if((!alarm.PlayerAttributed&&alarm.KillerThreatId==null)||alarm.CrimeResolved)continue;
                // New reports join the corpse case. Independent witness reports
                // would bypass its six-participant bound and cleanup-first order.
                if(Roster.Reports.Exists(r=>r.Id=="outdoor:"+alarm.Id && r.GuardIds.Count>0))continue;
                if(alarm.People.Exists(p=>p.DirectWitness && p.StatementDelivered))continue;
                foreach(var witness in alarm.People)
                {
                    if(!witness.StaticWitness||!witness.DirectWitness||witness.StatementDelivered)continue;
                    var native=worldStore.NativePeopleFor(directory.MapID).Find(p=>p.Id==witness.ResidentId);
                    if(native==null||!native.IsAlive)continue;
                    var point=directory.transform.TransformPoint(new Vector3((float)witness.X,(float)witness.Y,(float)witness.Z));
                    if(!HasPhysicalStaticWitness(native,point))continue;
                    worldStore.NoticeOutdoorCorpse(alarm.Id,witness.ResidentId,false,now);
                    if(ReportOutdoorCorpse(alarm,point,null))break;
                }
            }
        }
    }
}
