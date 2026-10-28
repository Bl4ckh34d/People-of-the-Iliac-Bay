using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class GuardPopulation
    {
        internal GuardActor BeginBodyReportConversation(string id,ResidentActor witness,double now)
        {return BeginWitnessReportConversation(id,witness,now,"There is a body nearby.","I will go and look.");}
        private GuardActor BeginWitnessReportConversation(string id,ResidentActor witness,double now,string account,string answer)
        {
            var guard=Roster.Guards.Find(g=>g.Id==id);
            if(guard==null || !witness)return null;
            float seconds=UnityEngine.Random.Range(2f,5f);
            if(!Roster.PauseResponseForReport(id,now,seconds*DaggerfallUnity.Instance.WorldTime.TimeScale))return null;
            var actor=View(guard,now);actor.gameObject.SetActive(true);
            actor.BeginReportConversation(witness,seconds);actor.HoldReportConversation();
            if((actor.transform.position-GameManager.Instance.PlayerMotor.transform.position).sqrMagnitude<=625)
            {
                DaggerfallUI.AddHUDText(witness.DisplayName+": "+account,3f);
                DaggerfallUI.AddHUDText("Guard: "+answer,3f);
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_REPORT_CONVERSATION_STARTED: witness="+witness.Resident.Id+"; guard="+id+"; realSeconds="+seconds);
            return actor;
        }
        public bool FindBodyReportGuard(Vector3 from,bool visibleOnly,out string id,out Vector3 position,string incidentId=null)
        {
            id=null;position=Vector3.zero;if(!OwnsExterior)return false;
            float nearest=300*300;
            var candidates=new List<GuardRecord>(Roster.Guards);
            candidates.Sort((a,b)=>{
                var x=Roster.Position(a.Id,Now());var y=Roster.Position(b.Id,Now());
                return (geometry.ScenePoint(new RoutePoint(x.X,x.Z))-from).sqrMagnitude.CompareTo((geometry.ScenePoint(new RoutePoint(y.X,y.Z))-from).sqrMagnitude);
            });
            foreach(var guard in candidates)
            {
                Vector3 available;if(!BodyReportGuardPosition(guard.Id,out available,incidentId))continue;
                var p=Roster.Position(guard.Id,Now());var point=geometry.ScenePoint(new RoutePoint(p.X,p.Z))+Vector3.up;
                float distance=(point-from).sqrMagnitude;
                if(distance>nearest || visibleOnly&&!CivilianResponses.Sees(from,point,20))continue;
                var local=directory.transform.InverseTransformPoint(from-Vector3.up);
                if(geometry.RouteBetweenPoints(new SavedRoutePoint{X=local.x,Z=local.z},p).Count<2)continue;
                nearest=distance;id=guard.Id;position=point;break;
            }
            return id!=null;
        }
        public bool BodyReportGuardPosition(string id,out Vector3 point,string incidentId=null)
        {
            point=Vector3.zero;if(!OwnsExterior)return false;
            var report=incidentId==null?null:Roster.Reports.Find(r=>r.Id=="outdoor:"+incidentId && r.GuardIds.Count>0);
            var guard=Roster.Guards.Find(g=>g.Id==id&&g.IsAlive&&g.Interior==null&&g.Refuge==null &&
                (report==null?g.Duty==GuardDuty.Posted:report.GuardIds.Contains(g.Id) && g.IncidentId==report.Id &&
                    (g.Duty==GuardDuty.Responding || g.Duty==GuardDuty.Searching)));
            if(guard==null)return false;
            var p=Roster.Position(id,Now());point=geometry.ScenePoint(new RoutePoint(p.X,p.Z))+Vector3.up;return true;
        }
        public bool DeliverBodyReport(CivilianIncident alarm,string id,Vector3 from,string witnessId=null)
        {
            Vector3 point;
            if(!BodyReportGuardPosition(id,out point,alarm.Id)||(point-from).sqrMagnitude>16||!CivilianResponses.Sees(from,point,5))return false;
            if(!ReportOutdoorCorpse(alarm,from,id))return false;
            var witness=witnessId==null?null:alarm.People.Find(p=>p.ResidentId==witnessId && p.DirectWitness);
            if(witness==null)return true;
            bool delivered=worldStore.TryDeliverOutdoorMurderStatement(alarm.Id,witnessId,id,Vector3.Distance(point,from),true,Now());
            if(delivered)Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MURDER_STATEMENT_DELIVERED: incident="+alarm.Id+"; witness="+witnessId+"; guard="+id+"; physicalContact=true");
            return delivered;
        }
    }
    public sealed partial class StreetPopulation
    {
        private sealed class BodyReportMotion { public List<SavedRoutePoint> Route; public int Next; public float Repath,GuardSearchAt; public Vector3 Target; public GuardActor TalkingGuard; }
        private float nextBodyGuardSearch;
        private readonly Dictionary<string,BodyReportMotion> bodyReportMotion=new Dictionary<string,BodyReportMotion>();
        private bool ApplyOutdoorBodyReport(Plan plan,ref ResidentActor actor,double now,AlarmPerson reaction)
        {
            if(!ReactionsEnabled||worldStore==null||!plan.Resident.IsAlive||QuestOwned(plan.Resident))return false;
            CivilianIncident incident=null;DefenseCombatant nativeCorpse=null;OutdoorBodyReport report=null;
            foreach(var alarm in worldStore.OutdoorCorpses(population.SettlementId))
            {
                var observer=alarm.People.Find(p=>p.ResidentId==plan.Resident.Id&&p.OutdoorReport!=null&&!p.OutdoorReport.Complete);
                if(observer==null)continue;
                incident=alarm;report=observer.OutdoorReport;break;
            }
            if(report==null)
                foreach(var corpse in worldStore.DefenseCorpsesFor(population.SettlementId))
                {
                    var cleanup=corpse.Death.Cleanup;if(cleanup==null)continue;
                    var observer=cleanup.Reports.Find(p=>p.ObserverId==plan.Resident.Id&&!p.Report.Complete);
                    if(observer==null)continue;
                    nativeCorpse=corpse;report=observer.Report;break;
                }
            if(report==null)return false;
            if(GameManager.IsGamePaused)
            {
                if(actor&&report.HasPosition){actor.Place(actor.transform.position-Vector3.up,actor.transform.forward,true);return true;}
                return false;
            }
            var guards=GetComponent<GuardPopulation>();if(!guards)return false;
            string reportCase=incident==null?null:incident.Id;
            Vector3 threat;
            bool immediateDanger=actor && incident!=null && (incident.KillerThreatId==null
                ? !GameManager.Instance.PlayerEntity.IsInvisible && CivilianResponses.Sees(actor.transform.position,GameManager.Instance.PlayerMotor.transform.position,12)
                : guards.TryHostileThreatPosition(incident.KillerThreatId,out threat) && CivilianResponses.Sees(actor.transform.position,threat,12));
            // Escape immediate danger first. The same incident's remaining fear
            // must not prevent a witness from running to an available guard.
            if(reaction!=null && (incident==null || !incident.People.Contains(reaction) || immediateDanger))
            {report.HasPosition=false;bodyReportMotion.Remove(plan.Resident.Id);return false;}
            if(!actor)
            {
                if(!report.HasPosition)return false;
                var ground=geometry.ScenePoint(new RoutePoint(report.X,report.Z));
                if((ground-GameManager.Instance.PlayerMotor.transform.position).sqrMagnitude>140*140||actors.Count>=80)return false;
                var go=Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject,directory.transform);
                actor=go.AddComponent<ResidentActor>();actor.Initialize(plan.Resident,plan.SurnameSeed,navigation,inventoryStore:worldStore);
                actor.SetRunningPilotAtlas(runningPilotAtlas);actors[plan.Resident.Id]=actor;go.SetActive(true);actor.Place(ground,Vector3.forward,true);
            }
            Vector3 destination=Vector3.zero;string id;
            BodyReportMotion motion;
            if(!bodyReportMotion.TryGetValue(plan.Resident.Id,out motion)) {motion=new BodyReportMotion();bodyReportMotion[plan.Resident.Id]=motion;}
            if(!report.Delivered)
            {
                if((report.GuardId==null || !guards.BodyReportGuardPosition(report.GuardId,out destination,reportCase)) &&
                    Time.unscaledTime>=motion.GuardSearchAt && Time.unscaledTime>=nextBodyGuardSearch)
                {
                    motion.GuardSearchAt=Time.unscaledTime+1;nextBodyGuardSearch=Time.unscaledTime+0.05f;
                    if(guards.FindBodyReportGuard(actor.transform.position,report.Deferred,out id,out destination,reportCase))report.GuardId=id;
                    else {report.GuardId=null;report.Deferred=true;}
                }
                if(report.GuardId!=null && (nativeCorpse!=null ? guards.DeliverNativeBodyReport(nativeCorpse,report.GuardId,actor.transform.position,now) :
                    guards.DeliverBodyReport(incident,report.GuardId,actor.transform.position,plan.Resident.Id)))
                {
                    report.Delivered=true;report.Returning=true;
                    if(incident!=null)
                    {
                        guards.ReceiveMurderIdentity(incident,plan.Resident.Id);
                    }
                    motion.TalkingGuard=guards.BeginBodyReportConversation(report.GuardId,actor,now);
                }
            }
            if(motion.TalkingGuard)
            {
                if(motion.TalkingGuard.HoldReportConversation())
                {
                    actor.Place(actor.transform.position-Vector3.up,motion.TalkingGuard.transform.position-actor.transform.position,true);
                    return true;
                }
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_REPORT_CONVERSATION_FINISHED: witness="+plan.Resident.Id+"; guard="+report.GuardId);
                motion.TalkingGuard=null;
            }
            bool seeking=!report.Delivered && report.GuardId!=null && guards.BodyReportGuardPosition(report.GuardId,out destination,reportCase);
            if(!seeking)
            {
                var scheduled=Scheduled(plan,now);
                if(scheduled.Activity==Activity.Travel||scheduled.Activity==Activity.Meeting)destination=geometry.ScenePoint(scheduled.Position);
                else
                {
                    TownGeometry.Entrance entry;
                    if(!geometry.Entrances.TryGetValue(scheduled.BuildingKey,out entry))return false;
                    destination=geometry.SceneDoor(entry);
                }
                if(Vector3.Distance(actor.transform.position-Vector3.up,destination)<0.7f)
                {
                    report.HasPosition=false;
                    if(report.Delivered)report.Complete=true;
                    if(report.Delivered)bodyReportMotion.Remove(plan.Resident.Id);return false;
                }
            }
            else destination-=Vector3.up;
            var groundNow=actor.transform.position-Vector3.up;
            if(Time.unscaledTime>=motion.Repath && (motion.Route==null||(motion.Target-destination).sqrMagnitude>4))
            {
                motion.Repath=Time.unscaledTime+2;motion.Target=destination;
                var from=directory.transform.InverseTransformPoint(groundNow);var to=directory.transform.InverseTransformPoint(destination);
                motion.Route=geometry.RouteBetweenPoints(new SavedRoutePoint{X=from.x,Z=from.z},new SavedRoutePoint{X=to.x,Z=to.z});motion.Next=1;
            }
            if(motion.Route!=null&&motion.Route.Count>=2)
            {
                var next=geometry.ScenePoint(new RoutePoint(motion.Route[motion.Next].X,motion.Route[motion.Next].Z));
                if((next-groundNow).sqrMagnitude<0.09f&&motion.Next<motion.Route.Count-1) {motion.Next++;next=geometry.ScenePoint(new RoutePoint(motion.Route[motion.Next].X,motion.Route[motion.Next].Z));}
                float speed=(float)(report.Delivered?actor.WalkingSpeed:ResidentMovement.FleeSpeed(plan.Resident.AppearanceSeed));
                var moved=Vector3.MoveTowards(groundNow,next,GameManager.IsGamePaused?0:speed*Time.unscaledDeltaTime);
                actor.Place(moved,next-groundNow,fleeing:!report.Delivered);
            }
            else { actor.Place(groundNow,actor.transform.forward,true); motion.Route=null; }
            var actual=directory.transform.InverseTransformPoint(actor.transform.position-Vector3.up);
            report.HasPosition=true;report.X=actual.x;report.Z=actual.z;
            if(reaction!=null)
            {reaction.HasPosition=true;reaction.X=actual.x;reaction.Y=actual.y;reaction.Z=actual.z;}
            return true;
        }
    }
}
