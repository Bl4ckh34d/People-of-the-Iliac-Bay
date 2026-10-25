using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private List<NativeNpcMedicalService> medicalServices;
        private readonly Dictionary<string,ActionMotion> medicalMotions=new Dictionary<string,ActionMotion>();
        private readonly Dictionary<string,float> medicalChecks=new Dictionary<string,float>();

        private bool MedicalPriorityAvailable(Plan plan,double now,bool danger)
        {
            return !danger && !GameManager.Instance.PlayerEnterExit.IsPlayerInsideDungeon &&
                plan.Resident.IsAlive && plan.Resident.AgeGroup==ResidentAgeGroup.Adult &&
                !plan.Resident.IsQuestProtected && !QuestOwned(plan.Resident) &&
                !worldStore.IsResidentAway(plan.Resident.Id,now) &&
                worldStore.ActiveNpcTask(plan.Resident.Id)==null && worldStore.ActiveNpcAction(plan.Resident.Id)==null &&
                !worldStore.HasCurrentNpcServiceDuty(plan.Resident.Id,now) &&
                !HasCurrentAppointmentDuty(plan.Resident.Id,now);
        }

        private bool ApplyNpcMedicalVisit(Plan plan,ref ResidentActor actor,double now,bool danger)
        {
            string id=plan.Resident.Id;
            var visit=worldStore.ReadNpcTreatmentVisit(id);
            if(visit==null)
            {
                if(!actor || !actor.gameObject.activeInHierarchy)return false;
                var condition=actor.GetComponent<EntityEffectManager>();
                if(!condition || condition.PoisonCount==0 && condition.DiseaseCount==0)return false;
            }
            if(GameManager.IsGamePaused || actor && actor.IsConversing)return visit!=null;
            if(!MedicalPriorityAvailable(plan,now,danger))
            {
                if(visit!=null && actor && actor.gameObject.activeInHierarchy && ActionSpaceMatches(visit.Position))
                {visit.Position=ActionPosition(actor.transform.position-Vector3.up);SaveMedicalVisit(id,visit);}
                medicalMotions.Remove(id);return false;
            }
            if(visit==null)
            {
                if(!actor || !actor.gameObject.activeInHierarchy || geometry==null || !EntranceSurveyReady)return false;
                var effects=actor.GetComponent<EntityEffectManager>();
                if(!effects || effects.PoisonCount==0 && effects.DiseaseCount==0)return false;
                float next;if(medicalChecks.TryGetValue(id,out next) && Time.unscaledTime<next)return false;
                medicalChecks[id]=Time.unscaledTime+10;
                var patient=actor;
                if(NativeNpcConditionTreatment.HasOwnTreatment(patient,worldStore,
                    ()=>HealthMagicActorAvailable(patient,id,ActionNow())))return false;
                SavedRoutePoint home;
                if(!TryRoutineRecoveryDoor(plan,plan.Resident.HomeKey,out home))return false;
                visit=ChooseMedicalVisit(plan,actor,effects);if(visit==null)return false;
                var conditions=actor.GetComponent<NativeNpcConditionBinding>();if(conditions)conditions.Capture();
                if(!SaveMedicalVisit(id,visit))return false;
                RemovePhysicalShopVisit(id);shopJourneys.Remove(id);shopDepartures.Remove(id);worldStore.RemoveObservedVisit(id);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MEDICAL_DEPART: resident="+id+"; provider="+visit.ProviderKey+"; effect="+visit.EffectKey);
            }
            if(!ActionSpaceMatches(visit.Position))return true;
            if(!actor)
            {
                Vector3 point=ActionWorldPoint(visit.Position);
                if(Vector3.Distance(point,GameManager.Instance.PlayerMotor.transform.position)>140 || actors.Count>=80)return true;
                var go=Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject,interior?interior.transform:directory.transform);
                actor=go.AddComponent<ResidentActor>();actor.Initialize(plan.Resident,plan.SurnameSeed,navigation,inventoryStore:worldStore);
                actor.SetRunningPilotAtlas(runningPilotAtlas);var crowd=actor.GetComponent<CrowdMotion>();
                if(crowd)crowd.RestorePlacement(point);else actor.transform.position=point+Vector3.up;
                actors[id]=actor;go.SetActive(true);
            }
            ActionMotion motion;
            if(!medicalMotions.TryGetValue(id,out motion)) {motion=new ActionMotion();medicalMotions[id]=motion;}
            if(visit.ServiceAt>0)
            {
                // Like ordinary visits, an unloaded interior is represented at its
                // real entrance. Actual arrival precedes the short service visit.
                actor.gameObject.SetActive(false);if(now<visit.ServiceAt)return true;
                var service=FindMedicalService(visit.ProviderKey);
                bool treated=service!=null && service.Treat(worldStore,actor,visit);
                if(!treated)Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MEDICAL_DEFERRED: resident="+id+
                    "; service unavailable at arrival; returning to routine before retry.");
                visit.ServiceAt=0;visit.Returning=true;SaveMedicalVisit(id,visit);
                actor.gameObject.SetActive(true);medicalMotions.Remove(id);return true;
            }
            var manager=actor.GetComponent<EntityEffectManager>();
            if(!visit.Returning && manager && manager.PoisonCount==0 && manager.DiseaseCount==0)
            {visit.Returning=true;SaveMedicalVisit(id,visit);motion=new ActionMotion();medicalMotions[id]=motion;}
            Vector3 target;
            if(interior)
            {
                if(indoorGeometry==null || !indoorGeometry.EntryGround.HasValue)return true;
                target=indoorGeometry.EntryGround.Value;
            }
            else
            {
                TownGeometry.Entrance door;
                if(!geometry.Entrances.TryGetValue(visit.Returning?visit.ReturnKey:visit.ProviderKey,out door))return true;
                target=geometry.ScenePoint(new RoutePoint(door.LocalDoor.x,door.LocalDoor.z));
            }
            var actual=actor.transform.position-Vector3.up;
            if(Vector3.Distance(actual,target)<=0.6f)
            {
                if(interior)
                {
                    TownGeometry.Entrance door;
                    if(!geometry.Entrances.TryGetValue(interior.EntryDoor.buildingKey,out door))return true;
                    visit.Position=MedicalDoorPosition(door);SaveMedicalVisit(id,visit);
                    actor.CaptureState();actor.gameObject.SetActive(false);Destroy(actor.gameObject);actors.Remove(id);actor=null;
                    medicalMotions.Remove(id);return true;
                }
                visit.Position=ActionPosition(actual);
                if(!visit.Returning)
                {
                    var service=FindMedicalService(visit.ProviderKey);int price;
                    if(service==null || !service.TryQuote(worldStore,actor,visit.EffectKey,visit.RecipeKey,out price) ||
                        worldStore.ReadNpcInventory(id).WalletGold<price)visit.Returning=true;
                    else visit.ServiceAt=now+60;
                    SaveMedicalVisit(id,visit);medicalMotions.Remove(id);return true;
                }
                RoutineRecoveryPlan recovery;string reason;
                if(TryPlanRoutineRecovery(plan,actor,now,visit.ReturnKey,true,out recovery,out reason))
                    worldStore.SaveRoutineRecovery(recovery);
                else if(visit.ReturnKey!=plan.Resident.HomeKey || !CanReleaseHomeOnlyAtDoor(plan,now,out reason))return true;
                InvalidateRoutineRecovery(id);
                SaveMedicalVisit(id,null);medicalMotions.Remove(id);medicalChecks[id]=Time.unscaledTime+30;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MEDICAL_RETURN: resident="+id+"; home="+visit.ReturnKey);return true;
            }
            actor.gameObject.SetActive(true);string failure;
            if(motion.Indoor==null && motion.Outdoor==null)
            {
                if(Time.unscaledTime<motion.RepathAt)return true;
                if(!BuildActionRoute(actor,motion,target,0.25f,out failure))
                {
                    motion.RepathAt=Time.unscaledTime+10;actor.Place(actual,actor.transform.forward,true);
                    if(++motion.RouteFailures<3)return true;
                    if(visit.Returning && TryAlternateMedicalReturn(plan,actor,visit,motion))return true;
                    // A changed/blocked route must not reserve movement forever.
                    // Release to the existing routine, retaining actual body placement.
                    SaveMedicalVisit(id,null);medicalMotions.Remove(id);medicalChecks[id]=Time.unscaledTime+60;
                    actor.ResumeFromSocialPause();
                    Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_MEDICAL_ROUTE_DEFERRED: resident="+id+"; returning="+visit.Returning);
                    return false;
                }
                motion.RouteFailures=0;
            }
            AdvanceActionMotion(actor,motion);
            if(Time.unscaledTime>=motion.SaveAt)
            {motion.SaveAt=Time.unscaledTime+1;visit.Position=ActionPosition(actor.transform.position-Vector3.up);SaveMedicalVisit(id,visit);}
            return true;
        }

        private bool TryAlternateMedicalReturn(Plan plan,ResidentActor actor,NpcTreatmentVisit visit,ActionMotion motion)
        {
            if(interior || plan.Routine==null || plan.Routine.Itinerary==null)return false;
            foreach(var itinerary in ResidentRoutineDay.Itineraries(plan.Routine))
            foreach(var stop in itinerary.Stops)
            {
                if(stop.BuildingKey==visit.ReturnKey)continue;
                SavedRoutePoint door;string reason;
                if(!TryRoutineRecoveryDoor(plan,stop.BuildingKey,out door) ||
                    !BuildActionRoute(actor,motion,geometry.ScenePoint(new RoutePoint(door.X,door.Z)),0.25f,out reason))continue;
                visit.ReturnKey=stop.BuildingKey;SaveMedicalVisit(plan.Resident.Id,visit);motion.RouteFailures=0;return true;
            }
            return false;
        }

        private NpcTreatmentVisit ChooseMedicalVisit(Plan plan,ResidentActor actor,EntityEffectManager effects)
        {
            string key=effects.PoisonCount>0?CurePoison.EffectKey:CureDisease.EffectKey;
            int recipe=NativeNpcMedicalService.RecipeFor(key);
            var inventory=worldStore.ReadNpcInventory(plan.Resident.Id);if(inventory==null)return null;
            if(medicalServices==null)medicalServices=NativeNpcMedicalService.Survey(directory);
            if(interior)
            {
                if(indoorGeometry==null || !indoorGeometry.EntryGround.HasValue)return null;
                var exit=new ActionMotion();string reason;
                if(!BuildActionRoute(actor,exit,indoorGeometry.EntryGround.Value,0.25f,out reason))return null;
            }
            NativeNpcMedicalService best=null;float bestDistance=float.PositiveInfinity;int selectedRecipe=-1;
            foreach(var service in medicalServices)
            {
                int offered=key==CureDisease.EffectKey && service.DiseaseCure?-1:recipe;
                if(offered<0 && (key!=CureDisease.EffectKey || !service.DiseaseCure))continue;
                TownGeometry.Entrance door;int price;
                if(!geometry.Entrances.TryGetValue(service.Building.buildingKey,out door) ||
                    !service.TryQuote(worldStore,actor,key,offered,out price) || inventory.WalletGold<price)continue;
                SavedRoutePoint home;
                if(!TryRoutineRecoveryDoor(plan,plan.Resident.HomeKey,out home) || geometry.RouteBetweenPoints(
                    new SavedRoutePoint {X=door.LocalDoor.x,Z=door.LocalDoor.z},home).Count<2)continue;
                var target=geometry.ScenePoint(new RoutePoint(door.LocalDoor.x,door.LocalDoor.z));
                Vector3 origin=actor.transform.position;
                if(interior)
                {TownGeometry.Entrance source;if(!geometry.Entrances.TryGetValue(interior.EntryDoor.buildingKey,out source))continue;
                    origin=geometry.ScenePoint(new RoutePoint(source.LocalDoor.x,source.LocalDoor.z));}
                float distance=(target-origin).sqrMagnitude;if(distance>=bestDistance)continue;
                if(!interior)
                {var check=new ActionMotion();string reason;if(!BuildActionRoute(actor,check,target,0.25f,out reason))continue;}
                best=service;bestDistance=distance;selectedRecipe=offered;
            }
            return best==null?null:new NpcTreatmentVisit {ProviderKey=best.Building.buildingKey,ReturnKey=plan.Resident.HomeKey,
                RecipeKey=selectedRecipe,EffectKey=key,Position=ActionPosition(actor.transform.position-Vector3.up)};
        }
        private NativeNpcMedicalService FindMedicalService(int key)
        {
            if(medicalServices==null)medicalServices=NativeNpcMedicalService.Survey(directory);
            return medicalServices.Find(service=>service.Building.buildingKey==key);
        }
        private bool SaveMedicalVisit(string id,NpcTreatmentVisit visit)
        {return worldStore.SaveNpcTreatmentVisit(worldStore.CaptureNpcVitalsWriteEpoch(),id,visit);}
        private NpcActionTarget MedicalDoorPosition(TownGeometry.Entrance door)
        {
            return new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,SettlementId=population.SettlementId,
                WorldSpaceId=ActionWorldSpace,BuildingKey=-1,X=door.LocalDoor.x,Z=door.LocalDoor.z};
        }
    }
}
