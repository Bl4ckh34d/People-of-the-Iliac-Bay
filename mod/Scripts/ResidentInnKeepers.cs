using System.Collections.Generic;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using LivingCities.Core;
using UnityEngine;
namespace LivingCities.DFU
{
    public sealed partial class StreetPopulation
    {
        private readonly Dictionary<string,ResidentInnReception> residentReceptions=new Dictionary<string,ResidentInnReception>();
        private Transform receptionKeeper;
        public bool PrepareResidentInnReceptions(WorldStateStore store,int town,int key,double now)
        {
            if(worldStore!=store || population==null || population.SettlementId!=town || !interior ||
                interior.EntryDoor.buildingKey!=key || indoorGeometry==null || !indoorGeometry.IsComplete || !indoorGeometry.EntryGround.HasValue) return false;
            var people=store.NativePeopleFor(town); receptionKeeper=null;string keeperId=null;
            foreach(var npc in interior.GetComponentsInChildren<StaticNPC>())
            {
                FactionFile.FactionData faction;
                if(!GameManager.Instance.PlayerEntity.FactionData.GetFactionData(npc.Data.factionID,out faction) ||
                    faction.sgroup!=(int)FactionFile.SocialGroups.Merchants) continue;
                var person=people.Find(p=>p.WorkKey==key && p.NativeHash==npc.Data.hash);
                if(person==null || !person.IsAlive) continue;
                var response=store.NativeResponse(person.Id);
                if(response!=null && now<CivilianAlarms.ActiveUntil(response)) continue;
                receptionKeeper=npc.transform;keeperId=person.Id; break;
            }
            if(!receptionKeeper) return false;
            var target=interior.transform.InverseTransformPoint(receptionKeeper.position);
            Vector3? counter=null; float distance=16;
            foreach(var anchor in indoorGeometry.LocalAnchors)
            {
                float d=(anchor-target).sqrMagnitude;
                if(d<distance && Mathf.Abs(anchor.y-target.y)<2) {counter=anchor;distance=d;}
            }
            if(!counter.HasValue) return false;
            foreach(var person in store.DestinationResidents(town,now))
            {
                var visit=store.ResidentTripAt(person.Id,now);
                if(visit.BuildingKey!=key || visit.VisitPurpose!=ResidentVisitPurpose.Inn || visit.VisitPhase!=ResidentVisitPhase.Inside ||
                    store.ResidentInnAdmission(person.Id,now)!=TravelerAdmission.Pending || store.ResidentInnReceptionAt(person.Id,now)!=null) continue;
                var route=indoorGeometry.RouteBetween(interior.transform.InverseTransformPoint(indoorGeometry.EntryGround.Value),counter.Value);
                try {store.SaveResidentInnReception(person.Id,new ResidentInnReception {VisitId=visit.VisitId,SettlementId=visit.SettlementId,KeeperId=keeperId,BuildingKey=key,VisitEnterAt=visit.VisitEnterAt,
                    VisitLeaveAt=visit.VisitLeaveAt,StartedAt=now,SecondsPerMetre=DaggerfallUnity.Instance.WorldTime.TimeScale/ResidentMovement.WalkSpeedFor(person.AppearanceSeed),
                    ServiceSeconds=(3+person.AppearanceSeed%4)*DaggerfallUnity.Instance.WorldTime.TimeScale,Route=route});}
                catch(System.ArgumentException) {return false;}
            }
            return true;
        }
        private bool PlaceResidentReception(ResidentActor actor,double seconds)
        {
            string id=actor.Resident.Id; var visit=worldStore.ResidentTripAt(id,seconds);
            if(visit.VisitPurpose!=ResidentVisitPurpose.Inn || visit.VisitPhase!=ResidentVisitPhase.Inside) return false;
            ResidentInnReception reception;
            if(!residentReceptions.TryGetValue(id,out reception) || System.Math.Abs(reception.VisitEnterAt-visit.VisitEnterAt)>0.001)
            {
                reception=worldStore.ResidentInnReceptionAt(id,seconds);
                if(reception==null) return false;
                residentReceptions[id]=reception;
            }
            if(seconds>=reception.ServiceEndsAt && worldStore.ResidentInnAdmission(id,seconds)!=TravelerAdmission.Pending) return false;
            var at=reception.Position(seconds); var next=reception.Position(seconds+0.1);
            var point=interior.transform.TransformPoint(new Vector3((float)at.X,(float)at.Y,(float)at.Z));
            var facing=seconds>=reception.CounterAt && receptionKeeper ? receptionKeeper.position-point :
                interior.transform.TransformVector(new Vector3((float)(next.X-at.X),(float)(next.Y-at.Y),(float)(next.Z-at.Z)));
            InteriorGeometry.OpenOrdinaryDoors(actor.transform.position-Vector3.up,point);
            actor.Place(point,facing,seconds>=reception.CounterAt); return true;
        }
    }
}
