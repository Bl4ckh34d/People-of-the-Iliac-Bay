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
            if(alarm==null || body==null || geometry==null)return null;
            double timeScale=DaggerfallUnity.Instance.WorldTime.TimeScale;
            if(timeScale<=0 || double.IsNaN(timeScale) || double.IsInfinity(timeScale))return null;
            var destinations=CorpseInquiryDestinations(body);
            // Stable per case so retries and saved search routes agree. Different
            // scenes explore different points, without using suspect knowledge.
            int seed=17;unchecked {foreach(char value in alarm.Id)seed=seed*31+value;}
            var random=new System.Random(seed);
            for(int i=0;i<12;i++)
            {
                double angle=random.NextDouble()*Math.PI*2,radius=8+random.NextDouble()*12;
                destinations.Add(new SavedRoutePoint{X=body.X+Math.Cos(angle)*radius,Z=body.Z+Math.Sin(angle)*radius});
            }
            var result=new List<SavedRoutePoint>{new SavedRoutePoint{X=body.X,Z=body.Z}};
            var current=body;double length=0,limit=Math.Min(400,UnwitnessedScene.OccupationSeconds*2.8/timeScale);
            int visited=0;
            foreach(var destination in destinations)
            {
                double dx=destination.X-current.X,dz=destination.Z-current.Z;
                if(dx*dx+dz*dz<4)continue;
                var leg=geometry.RouteBetweenPoints(current,destination);
                var back=geometry.RouteBetweenPoints(destination,body);
                if(leg==null || back==null || leg.Count<2 || back.Count<2 ||
                    result.Count+leg.Count+back.Count>10000)continue;
                double legLength=CorpseSearchRouteLength(leg),backLength=CorpseSearchRouteLength(back);
                if(legLength<2 || length+legLength+backLength>limit)continue;
                for(int index=1;index<leg.Count;index++)result.Add(leg[index]);
                current=leg[leg.Count-1];length+=legLength;
                if(++visited>=4)break;
            }
            if(visited==0)return null;
            var home=geometry.RouteBetweenPoints(current,body);
            if(home==null || home.Count<2 || length+CorpseSearchRouteLength(home)>limit)return null;
            for(int index=1;index<home.Count;index++)result.Add(home[index]);
            return result;
        }

        private List<SavedRoutePoint> CorpseInquiryDestinations(SavedRoutePoint body)
        {
            var result=new List<SavedRoutePoint>();
            var eye=geometry.ScenePoint(new RoutePoint(body.X,body.Z))+Vector3.up;
            var streets=GetComponent<StreetPopulation>();
            if(streets)
                foreach(var person in streets.NearbyCorpseInquiryPeople(eye,20))
                {
                    if(!CivilianResponses.Sees(eye,person.transform.position,20))continue;
                    var target=Local(person.transform.position);
                    double dx=body.X-target.X,dz=body.Z-target.Z,distance=Math.Sqrt(dx*dx+dz*dz);
                    if(distance>2){target.X+=dx*2/distance;target.Z+=dz*2/distance;}
                    result.Add(target);if(result.Count>=2)return result;
                }
            foreach(var npc in directory.GetComponentsInChildren<StaticNPC>())
            {
                if(!npc || !npc.gameObject.activeInHierarchy || !RDBLayout.IsNPCFlat(npc.Data.billboardArchiveIndex) ||
                    !CivilianResponses.Sees(eye,npc.transform.position,20))continue;
                SleepingPose sleep=npc.GetComponent<SleepingPose>();if(sleep && sleep.Active)continue;
                var target=Local(npc.transform.position);
                double dx=body.X-target.X,dz=body.Z-target.Z,distance=Math.Sqrt(dx*dx+dz*dz);
                if(distance>2){target.X+=dx*2/distance;target.Z+=dz*2/distance;}
                result.Add(target);if(result.Count>=2)break;
            }
            return result;
        }

        private static double CorpseSearchRouteLength(List<SavedRoutePoint> route)
        {
            double length=0;
            for(int index=1;index<route.Count;index++)
            {double x=route[index].X-route[index-1].X,z=route[index].Z-route[index-1].Z;length+=Math.Sqrt(x*x+z*z);}
            return length;
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
