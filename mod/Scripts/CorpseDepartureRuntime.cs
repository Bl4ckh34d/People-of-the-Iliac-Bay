using System;
using System.Collections.Generic;
using DaggerfallConnect;
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
        public void CaptureCorpseDeparture(){FlushOutdoorDeparture();}
        public List<ResidentActor> NearbyCorpseInquiryPeople(Vector3 point,float radius=4)
        {
            var result=new List<ResidentActor>();
            foreach(var actor in actors.Values)
                if(actor && actor.gameObject.activeInHierarchy && actor.Resident!=null && actor.Resident.IsAlive &&
                    !actor.IsConversing && !actor.IsSleeping && (actor.transform.position-point).sqrMagnitude<=radius*radius)result.Add(actor);
            result.Sort((a,b)=>(a.transform.position-point).sqrMagnitude.CompareTo((b.transform.position-point).sqrMagnitude));
            return result;
        }
    }
    public sealed partial class GuardPopulation
    {
        private readonly Dictionary<string,float> nextDepartureCorpseRequest=new Dictionary<string,float>();
        private readonly Dictionary<string,float> nextCorpseInquiry=new Dictionary<string,float>();

        public bool RequestCorpseDepartureInvestigation(CivilianIncident alarm,double now)
        {
            // This also runs during departure, before the previous town's geometry is released.
            if(!directory || geometry==null || worldStore==null || Roster==null || alarm==null ||
                alarm.SettlementId!=directory.MapID || alarm.SettlementId!=Roster.SettlementId || alarm.Corpse==null ||
                alarm.Corpse.Departure==null || alarm.Corpse.Departure.FirstDepartureAt<0 ||
                now>=alarm.Corpse.Departure.FirstDepartureAt+3600 || alarm.CrimeResolved)return false;
            if(!worldStore.OutdoorCorpses(directory.MapID).Exists(a=>object.ReferenceEquals(a,alarm)))return false;
            string id="outdoor:"+alarm.Id;
            var existing=Roster.Reports.Find(r=>r.Id==id);
            if(existing!=null && existing.Investigation && existing.SearchRoute!=null &&
                existing.GuardIds.Exists(assigned=>Roster.Guards.Exists(g=>g.Id==assigned && g.IsAlive &&
                    g.IncidentId==id && g.Duty!=GuardDuty.Posted)))
            {
                // An existing physical assignment needs no new navigation survey.
                // Conversion preserves its first deadline and verified search path.
                if(!existing.DepartureInvestigation)
                    Roster.DispatchDepartureInvestigation(id,alarm.Corpse.Departure.FirstDepartureAt,now,
                        new Dictionary<string,List<SavedRoutePoint>>(),existing.SearchRoute,
                        2.8,DaggerfallUnity.Instance.WorldTime.TimeScale);
                return true;
            }
            float retry;
            if(nextDepartureCorpseRequest.TryGetValue(id,out retry) && Time.unscaledTime<retry)return false;
            nextDepartureCorpseRequest[id]=Time.unscaledTime+2;
            var body=alarm.Corpse;
            var target=new SavedRoutePoint{X=body.HasPosition?body.X:alarm.X,Z=body.HasPosition?body.Z:alarm.Z};
            var routes=new Dictionary<string,List<SavedRoutePoint>>();
            Roster.Advance(now);
            foreach(var guard in Roster.Guards)
            {
                if(!guard.IsAlive || guard.Duty!=GuardDuty.Posted || guard.Interior!=null || guard.Refuge!=null)continue;
                var route=geometry.RouteBetweenPoints(Roster.Position(guard.Id,now),target);
                if(route!=null && route.Count>=2)routes.Add(guard.Id,route);
            }
            List<SavedRoutePoint> patrol=existing==null?null:existing.SearchRoute;
            if(patrol==null)patrol=CorpseSearchPatrol(alarm,target);
            if(patrol==null)return false;
            var sent=Roster.DispatchDepartureInvestigation(id,body.Departure.FirstDepartureAt,now,routes,patrol,
                2.8,DaggerfallUnity.Instance.WorldTime.TimeScale);
            if(sent.Count==0)return false;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CORPSE_DEPARTURE_DISPATCH: incident="+alarm.Id+"; guard="+sent[0].Id+"; eyewitnessRequired=false");
            return true;
        }

        private bool UpdateDepartureCorpseInquiry(GuardRecord guard,GuardActor actor,double now)
        {
            var report=Roster.Reports.Find(r=>r.Id==guard.IncidentId && r.Investigation && !r.BuildingKey.HasValue);
            if(report==null || report.ThreatId!=null || guard.Duty==GuardDuty.Engaged)return false;
            var alarm=worldStore.OutdoorCorpses(directory.MapID).Find(a=>"outdoor:"+a.Id==report.Id);
            if(alarm==null || alarm.AccusationIssued)return false;
            worldStore.SynchronizeOutdoorCorpseInquiry(alarm.Id);
            actor.EndCombat();
            if(!alarm.Corpse.Cleared || guard.Duty!=GuardDuty.Searching || now<alarm.Corpse.ClearedAt || now>=report.InvestigationUntil)return true;
            LearnCorpseSuspect(alarm,guard.Id,now);
            if(report.CorpseSuspect!=null)return true;
            float retry;
            if(nextCorpseInquiry.TryGetValue(guard.Id,out retry) && Time.unscaledTime<retry)return true;
            nextCorpseInquiry[guard.Id]=Time.unscaledTime+4;
            var streets=GetComponent<StreetPopulation>();
            if(streets)
                foreach(var person in streets.NearbyCorpseInquiryPeople(actor.transform.position))
                    if(TryQuestionCorpsePerson(alarm,guard,actor,person.Resident.Id,person.DisplayName,
                        person.transform,false,now))return true;
            foreach(var npc in directory.GetComponentsInChildren<StaticNPC>())
            {
                if(!npc || !npc.gameObject.activeInHierarchy || !RDBLayout.IsNPCFlat(npc.Data.billboardArchiveIndex) ||
                    (npc.transform.position-actor.transform.position).sqrMagnitude>16)continue;
                var d=npc.Data;var point=directory.transform.InverseTransformPoint(npc.transform.position);
                int hash=NativeExteriorNpcIdentity.Hash(npc,directory.transform);
                var native=worldStore.RegisterNativePerson(directory.MapID,-1,hash,npc.DisplayName,
                    d.gender==Genders.Female?ResidentSex.Female:ResidentSex.Male,(int)d.race,d.factionID,d.billboardArchiveIndex,d.billboardRecordIndex);
                var sleep=npc.GetComponent<SleepingPose>();if(sleep && sleep.Active)continue;
                if(native.IsAlive && TryQuestionCorpsePerson(alarm,guard,actor,native.Id,npc.DisplayName,npc.transform,true,now))return true;
            }
            return true;
        }
        private bool TryQuestionCorpsePerson(CivilianIncident alarm,GuardRecord guard,GuardActor actor,
            string personId,string name,Transform person,bool staticPerson,double now)
        {
            if(alarm.Corpse.Inquiries.Exists(row=>row.PersonId==personId) ||
                Vector3.Distance(actor.transform.position,person.position)>4 ||
                !CivilianResponses.Sees(actor.transform.position,person.position,5))return false;
            float seconds=UnityEngine.Random.Range(2f,5f);
            if(!actor.BeginInquiryConversation(person,seconds,now))return false;
            bool statement;
            if(!worldStore.TryQuestionCorpseCase(alarm.Id,guard.Id,personId,staticPerson,
                Vector3.Distance(actor.transform.position,person.position),true,now,out statement))
            {actor.EndReportConversation();return false;}
            LearnCorpseSuspect(alarm,guard.Id,now);
            if((actor.transform.position-GameManager.Instance.PlayerMotor.transform.position).sqrMagnitude<=625)
            {
                DaggerfallUI.AddHUDText("Guard: Did you see what happened here?",3f);
                DaggerfallUI.AddHUDText(name+": "+(statement?"I saw the attack. I can tell you what I witnessed.":"I have no first-hand account."),3f);
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CORPSE_INQUIRY: incident="+alarm.Id+"; guard="+guard.Id+
                "; person="+personId+"; static="+staticPerson+"; directStatement="+statement+"; realSeconds="+seconds);
            return true;
        }
    }
}
