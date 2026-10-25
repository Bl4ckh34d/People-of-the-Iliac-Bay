using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private readonly Dictionary<int,TavernRoomSurvey.Result> bankCounters=new Dictionary<int,TavernRoomSurvey.Result>();
        private readonly Dictionary<string,PhysicalShopVisit> bankVisitors=new Dictionary<string,PhysicalShopVisit>();
        private IEnumerator bankCounterSurvey;

        private void UpdateBankCounterSurvey()
        {
            if(bankCounterSurvey==null || GameManager.IsGamePaused)return;
            try {if(bankCounterSurvey.MoveNext())return;}
            catch(Exception error){Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_BANK_COUNTER_UNAVAILABLE: "+error.Message);}
            (bankCounterSurvey as IDisposable)?.Dispose();bankCounterSurvey=null;
        }

        private bool TryBankCounter(int key,out TavernRoomSurvey.Result result)
        {
            if(bankCounters.TryGetValue(key,out result))return result.ReceptionRoute!=null && result.KeeperHash.HasValue;
            if(bankCounterSurvey!=null)return false;
            StaticDoor door;if(!ReadBuildingActionDoor(key,null,out door))return false;
            var pending=new TavernRoomSurvey.Result();bankCounters[key]=pending;
            var owner=directory;var store=worldStore;var epoch=store.CaptureNpcVitalsWriteEpoch();
            bankCounterSurvey=TavernRoomSurvey.ReadServiceSteps(owner.transform,door,owner.LocationData.Climate.ClimateType,
                pending,()=>this && directory==owner && worldStore==store && store.IsNpcVitalsWriteEpochCurrent(epoch),bank:true);
            return false;
        }

        private bool BeginBankCounterVisit(Plan plan,ResidentActor actor,NpcCashVisit visit,double now)
        {
            TavernRoomSurvey.Result counter;
            if(!TryBankCounter(visit.ProviderKey,out counter))return false;
            visit.ClerkHash=counter.KeeperHash.Value;
            visit.CounterJourney=NpcCashVisit.CreateCounterJourney(plan.Resident.Id,population.SettlementId,visit.ProviderKey,now,
                counter.ReceptionRoute,DaggerfallUnity.Instance.WorldTime.TimeScale/ResidentMovement.WalkSpeedFor(plan.Resident.AppearanceSeed),
                DaggerfallUnity.Instance.WorldTime.TimeScale,UnityEngine.Random.Range(8,16));
            var first=counter.ReceptionRoute[0];
            visit.Position=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=population.SettlementId,
                BuildingKey=visit.ProviderKey,X=first.X,Y=first.Y,Z=first.Z};
            visit.ServiceAt=0;visit.OutsideJourney=null;SaveCashVisit(plan.Resident.Id,visit);cashMotions.Remove(plan.Resident.Id);
            if(!interior && actor)actor.gameObject.SetActive(false);
            return true;
        }

        private bool ApplyBankCounterVisit(Plan plan,ref ResidentActor actor,NpcCashVisit visit,double now)
        {
            string id=plan.Resident.Id;
            bool loaded=interior && interior.EntryDoor.buildingKey==visit.ProviderKey;
            PhysicalShopVisit physical=null;
            bool finished,exited;
            if(loaded)
            {
                if(!indoorGeometry.EntryGround.HasValue || shopGeometry==null)return true;
                if(!bankVisitors.TryGetValue(id,out physical))
                {
                    var sample=visit.CounterJourney.Evaluate(Math.Max(now,visit.CounterJourney.ObservedAt));
                    visit.Position.X=sample.Position.X;visit.Position.Y=sample.Position.Y;visit.Position.Z=sample.Position.Z;
                    EnsureCashActor(plan,ref actor,visit.Position);
                    if(!actor)return true;
                    physical=new PhysicalShopVisit(visit.CounterJourney,Math.Max(now,visit.CounterJourney.ObservedAt));
                    bankVisitors[id]=physical;
                }
                actor.gameObject.SetActive(true);
                bool clerkPresent=false;
                foreach(var npc in interior.GetComponentsInChildren<StaticNPC>())
                    if(npc.Data.hash==visit.ClerkHash && NativeBusinessServices.IsBankClerk(npc))
                    {var person=KeeperPerson(npc);clerkPresent=person==null || person.IsAlive;break;}
                if(!clerkPresent && !visit.Returning)physical.BeginExit(now);
                physical.Advance(actor,interior,indoorGeometry,shopGeometry,now);
                var saved=physical.Capture(actor,interior,indoorGeometry,now,population.SettlementId);
                if(saved==null)return true;
                visit.CounterJourney=saved;visit.Position=ActionPosition(actor.transform.position-Vector3.up);
                finished=clerkPresent && (physical.Snapshot.Phase==ShopVisitPhase.Leaving || physical.Snapshot.Phase==ShopVisitPhase.Complete);
                exited=physical.Snapshot.Phase==ShopVisitPhase.Complete;
            }
            else
            {
                if(actor)actor.gameObject.SetActive(false);
                finished=NpcCashVisit.CounterServiceFinished(visit.CounterJourney,now);
                exited=visit.CounterJourney.Evaluate(Math.Max(now,visit.CounterJourney.ObservedAt)).Completed;
                var clerk=worldStore.NativePeopleFor(population.SettlementId).Find(p=>p.WorkKey==visit.ProviderKey && p.NativeHash==visit.ClerkHash);
                if(clerk!=null && !clerk.IsAlive)finished=false;
            }
            if(finished && !visit.Returning)
            {
                visit.ServiceAt=now;SaveCashVisit(id,visit);
                var inventory=worldStore.ReadNpcInventory(id);
                if(inventory!=null && worldStore.TryCompleteNpcCashVisit(worldStore.CaptureNpcVitalsWriteEpoch(),id,inventory.Revision,
                    visit.ProviderKey,NativeNpcInventoryBridge.InitialWalletGold(worldStore,id),now))
                {
                    if(actor){var owner=actor.GetComponent<NativeNpcInventoryOwner>();if(owner && owner.Binding!=null)owner.Binding.Revision++;}
                    visit=worldStore.ReadNpcCashVisit(id);
                }
            }
            if(exited)
            {
                TownGeometry.Entrance door;
                if(!geometry.Entrances.TryGetValue(visit.ProviderKey,out door))return true;
                visit.Position=MedicalDoorPosition(door);visit.CounterJourney=null;visit.OutsideJourney=null;visit.ServiceAt=0;visit.Returning=true;
                bankVisitors.Remove(id);cashMotions.Remove(id);
                if(actor){actor.CaptureState();actor.gameObject.SetActive(false);Destroy(actor.gameObject);actors.Remove(id);actor=null;}
            }
            SaveCashVisit(id,visit);return true;
        }

        private void CaptureBankVisitors(double now)
        {
            if(!interior || indoorGeometry==null || worldStore==null || population==null)return;
            CaptureCashExits(now);
            foreach(var pair in bankVisitors)
            {
                var actor=FindActor(pair.Key);var visit=worldStore.ReadNpcCashVisit(pair.Key);
                if(!actor || visit==null)continue;
                var saved=pair.Value.Capture(actor,interior,indoorGeometry,now,population.SettlementId);
                if(saved==null)continue;
                visit.CounterJourney=saved;visit.Position=ActionPosition(actor.transform.position-Vector3.up);SaveCashVisit(pair.Key,visit);
            }
        }

        private void EnsureCashActor(Plan plan,ref ResidentActor actor,NpcActionTarget position)
        {
            if(actor || !ActionSpaceMatches(position))return;
            Vector3 point=ActionWorldPoint(position);
            if(Vector3.Distance(point,GameManager.Instance.PlayerMotor.transform.position)>140 || actors.Count>=80)return;
            var body=Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject,interior?interior.transform:directory.transform);
            actor=body.AddComponent<ResidentActor>();actor.Initialize(plan.Resident,plan.SurnameSeed,navigation,inventoryStore:worldStore);
            actor.SetRunningPilotAtlas(runningPilotAtlas);var crowd=actor.GetComponent<CrowdMotion>();
            if(crowd)crowd.RestorePlacement(point);else actor.transform.position=point+Vector3.up;
            actors[plan.Resident.Id]=actor;body.SetActive(true);
        }
    }
}
