using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Controlled rendered-patron regression inside the actual surveyed inn.
    internal static class InnChatProbe
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        internal static IEnumerator Verify(StreetPopulation streets,ResidentActor patron,InteriorGeometry geometry,ShopVisitGeometry activities)
        {
            var room=GameManager.Instance.PlayerEnterExit.Interior;
            var actors=(Dictionary<string,ResidentActor>)typeof(StreetPopulation).GetField("actors",Private).GetValue(streets);
            var visits=(Dictionary<string,PhysicalShopVisit>)typeof(StreetPopulation).GetField("physicalShopVisits",Private).GetValue(streets);
            var cooldowns=(Dictionary<string,double>)typeof(StreetPopulation).GetField("nextInnChat",Private).GetValue(streets);
            var priorCooldowns=new Dictionary<string,double>(cooldowns);
            var chats=(System.Collections.IDictionary)typeof(StreetPopulation).GetField("innChats",Private).GetValue(streets);
            var priorChats=new List<System.Collections.DictionaryEntry>();
            foreach(System.Collections.DictionaryEntry pair in chats) priorChats.Add(pair);
            var scan=typeof(StreetPopulation).GetField("nextInnChatScan",Private);var priorScan=scan.GetValue(streets);
            var update=typeof(StreetPopulation).GetMethod("UpdateInnChats",Private);
            var clear=typeof(StreetPopulation).GetMethod("ClearInnChats",Private);
            bool enabled=streets.enabled;GameObject fixture=null;
            const string id="inn-social-fixture";
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            streets.enabled=false;
            try
            {
                clear.Invoke(streets,null);
                foreach(var other in actors.Keys) if(other!=patron.Resident.Id) cooldowns[other]=now+120;
                var local=room.transform.InverseTransformPoint(patron.transform.position-Vector3.up);
                ResidentActor partner=null;
                foreach(var candidate in geometry.NearbyApproachPoints(local,1.7f,0.15f,256))
                {
                    float gap=(candidate-local).magnitude;
                    if(gap<1.05f || gap>1.6f) continue;
                    var world=room.transform.TransformPoint(candidate);
                    if(!CivilianResponses.Sees(patron.transform.position,world+Vector3.up,2)) continue;
                    fixture=UnityEngine.Object.Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject,room.transform);
                    partner=fixture.AddComponent<ResidentActor>();
                    partner.Initialize(new Resident {Id=id,FullName="Inn Social Test Patron",AppearanceSeed=99,AgeGroup=ResidentAgeGroup.Adult},1,
                        GameManager.Instance.StreamingWorld.GetCurrentCityNavigation());
                    partner.Place(world,Vector3.forward,true);
                    if(fixture.activeInHierarchy && Vector3.Distance(partner.transform.position,patron.transform.position)>=1 &&
                        Vector3.Distance(partner.transform.position,patron.transform.position)<=1.8f) break;
                    fixture.SetActive(false);UnityEngine.Object.Destroy(fixture);fixture=null;partner=null;
                }
                if(!partner) throw new InvalidOperationException("Inn chat fixture lacks a free social position.");
                var position=room.transform.InverseTransformPoint(partner.transform.position-Vector3.up);
                var point=new GuardPoint3 {X=position.x,Y=position.y,Z=position.z};
                var entry=room.transform.InverseTransformPoint(geometry.EntryGround.Value);
                var saved=new ObservedVisit {ResidentId=id,SettlementId=GameManager.Instance.PlayerGPS.CurrentLocation.MapTableData.MapId,
                    BuildingKey=room.EntryDoor.buildingKey,ObservedAt=now,PlannedEndAt=now+120,ResumeUntil=now+120,
                    SecondsPerMetre=2/partner.WalkingSpeed,Legs=new List<ObservedVisitLeg> {
                        new ObservedVisitLeg {Route=new List<GuardPoint3>{point},DwellSeconds=60,AfterPhase=ShopVisitPhase.Browsing},
                        new ObservedVisitLeg {Route=new List<GuardPoint3>{point,new GuardPoint3 {X=entry.x,Y=entry.y,Z=entry.z}},AfterPhase=ShopVisitPhase.Complete}}};
                var visit=new PhysicalShopVisit(saved,now);visit.Advance(partner,room,geometry,activities,now);
                actors.Add(id,partner);visits.Add(id,visit);
                var before=patron.transform.position;int faced=0;
                for(int frame=0;frame<30;frame++)
                {
                    update.Invoke(streets,new object[]{now});
                    var other=streets.InnChatPartner(patron.Resident.Id);
                    if(other==partner && streets.InnChatPartner(id)==patron)
                    {
                        var delta=partner.transform.position-patron.transform.position;delta.y=0;delta.Normalize();
                        if(Vector3.Dot(patron.transform.forward,delta)<0.98f || Vector3.Dot(partner.transform.forward,-delta)<0.98f ||
                            !patron.IsStandingStill || !partner.IsStandingStill || Vector3.Distance(before,patron.transform.position)>0.05f)
                            throw new InvalidOperationException("Inn social pair failed reciprocal idle facing without displacement.");
                        faced++;
                    }
                    yield return null;
                }
                if(faced<20) throw new InvalidOperationException("Available inn patrons did not form a social pair.");
                partner.SetConversationFacing(true);update.Invoke(streets,new object[]{now});
                if(streets.InnChatPartner(patron.Resident.Id)) throw new InvalidOperationException("Player conversation did not interrupt inn chat.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INN_CHAT_PASS: controlled post-keeper browsing pair; reciprocal idle facing for "+faced+
                    " frames; no displacement; player conversation interrupts; no route/dwell changes.");
            }
            finally
            {
                actors.Remove(id);visits.Remove(id);clear.Invoke(streets,null);
                foreach(var pair in priorChats) chats.Add(pair.Key,pair.Value);
                scan.SetValue(streets,priorScan);
                foreach(var pair in priorCooldowns) cooldowns[pair.Key]=pair.Value;
                if(fixture) {fixture.SetActive(false);UnityEngine.Object.Destroy(fixture);}
                patron.ResumeFromSocialPause();streets.enabled=enabled;
            }
        }
    }
}
