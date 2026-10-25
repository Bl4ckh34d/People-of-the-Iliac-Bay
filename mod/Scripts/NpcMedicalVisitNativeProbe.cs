using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static class NpcMedicalVisitNativeProbe
    {
        const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
        internal static IEnumerator Run(PeopleOfTheIliacBayMod host,StreetPopulation streets,string character)
        {
            string original=null;var args=Environment.GetCommandLineArgs();
            for(int i=0;i+1<args.Length;i++)if(args[i]=="-peopleoftheiliacbay-fixture-save")original=args[i+1];
            foreach(string key in new[] {CureDisease.EffectKey,CurePoison.EffectKey})
            {
                yield return ConversationMoodProbe.LoadSave(host,streets,character,"People - Daggerfall day 2",false);
                var game=GameManager.Instance;
                if(game.PlayerEnterExit.IsPlayerInside)game.PlayerEnterExit.TransitionExterior(true);
                float deadline=Time.realtimeSinceStartup+90;
                while((!streets.Ready || !streets.EntranceSurveyReady || game.PlayerEnterExit.IsPlayerInside) && Time.realtimeSinceStartup<deadline)yield return null;
                var store=(WorldStateStore)typeof(StreetPopulation).GetField("worldStore",Private).GetValue(streets);
                var geometry=(TownGeometry)typeof(StreetPopulation).GetField("geometry",Private).GetValue(streets);
                var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
                var services=NativeNpcMedicalService.Survey(directory);
                NativeNpcMedicalService chosen=services.Find(s=>key==CureDisease.EffectKey?s.DiseaseCure:s.Potions);
                Require(chosen!=null,"No actual native provider for "+key);
                TownGeometry.Entrance entrance;
                Require(geometry.Entrances.TryGetValue(chosen.Building.buildingKey,out entrance),"Provider has no actual entrance.");
                Vector3 door=geometry.ScenePoint(new RoutePoint(entrance.LocalDoor.x,entrance.LocalDoor.z));
                game.PlayerMotor.transform.position=door+Vector3.up*1.5f;
                // A controlled patient uses an existing plan, native body and real
                // home door. Do not depend on chance foot traffic at this morning save.
                object plan=null;Resident patient=null;TownGeometry.Entrance home=null;float nearest=float.PositiveInfinity;
                foreach(var candidate in (IEnumerable)typeof(StreetPopulation).GetField("plans",Private).GetValue(streets))
                {
                    var person=(Resident)candidate.GetType().GetField("Resident").GetValue(candidate);
                    TownGeometry.Entrance source;
                    if(!person.IsAlive || person.IsQuestProtected || person.AgeGroup!=ResidentAgeGroup.Adult ||
                        CivilianAlarms.ChooseReaction(person)!=CivilianReaction.Flee ||
                        store.ActiveNpcAction(person.Id)!=null || store.ActiveNpcTask(person.Id)!=null ||
                        !geometry.Entrances.TryGetValue(person.HomeKey,out source))continue;
                    float distance=(source.LocalDoor-entrance.LocalDoor).sqrMagnitude;
                    if(distance<16 || distance>=nearest || geometry.RouteBetweenPoints(
                        new SavedRoutePoint {X=source.LocalDoor.x,Z=source.LocalDoor.z},
                        new SavedRoutePoint {X=entrance.LocalDoor.x,Z=entrance.LocalDoor.z}).Count<2)continue;
                    nearest=distance;plan=candidate;patient=person;home=source;
                }
                Require(patient!=null,"No native residential route to medical provider.");
                var routine=(ResidentRoutine)plan.GetType().GetField("Routine").GetValue(plan);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MEDICAL_FIXTURE: resident="+patient.Id+
                    "; nativeProvider="+chosen.Building.buildingKey+"; homeOnly="+(routine.Itinerary==null && routine.DestinationKey<0));
                var actors=(Dictionary<string,ResidentActor>)typeof(StreetPopulation).GetField("actors",Private).GetValue(streets);
                ResidentActor actor;actors.TryGetValue(patient.Id,out actor);
                if(!actor)
                {
                    var go=UnityEngine.Object.Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject,directory.transform);
                    actor=go.AddComponent<ResidentActor>();actor.Initialize(patient,
                        (uint)plan.GetType().GetField("SurnameSeed").GetValue(plan),game.StreamingWorld.GetCurrentCityNavigation(),inventoryStore:store);
                    actors[patient.Id]=actor;go.SetActive(true);
                }
                actor.GetComponent<CrowdMotion>().RestorePlacement(geometry.ScenePoint(new RoutePoint(home.LocalDoor.x,home.LocalDoor.z)));
                var motionType=typeof(StreetPopulation).GetNestedType("ActionMotion",BindingFlags.NonPublic);
                var failedMotion=Activator.CreateInstance(motionType,true);
                motionType.GetField("Outdoor").SetValue(failedMotion,new List<SavedRoutePoint> {new SavedRoutePoint(),new SavedRoutePoint()});
                Require(!(bool)typeof(StreetPopulation).GetMethod("BuildActionRoute",Private).Invoke(streets,new object[] {
                    actor,failedMotion,actor.transform.position+new Vector3(500000,0,500000),0.25f,null,false}),"Out-of-town test route unexpectedly reachable.");
                Require(motionType.GetField("Outdoor").GetValue(failedMotion)==null &&
                    motionType.GetField("Indoor").GetValue(failedMotion)==null,"Failed route retained stale motion and could bypass bounded retry.");
                string id=actor.Resident.Id;var manager=actor.GetComponent<EntityEffectManager>();
                var body=actor.GetComponent<DaggerfallEntityBehaviour>();var owner=actor.GetComponent<NativeNpcInventoryOwner>();
                body.Entity.Level=20;body.Entity.SetMagicka(0);
                for(int i=body.Entity.SpellbookCount()-1;i>=0;i--)body.Entity.DeleteSpell(i);
                for(int i=owner.Binding.Items.Count-1;i>=0;i--)
                    if(owner.Binding.Items.GetItem(i).IsPotion)owner.Binding.Items.RemoveItem(owner.Binding.Items.GetItem(i));
                owner.Capture();SetGold(store,owner,0);
                if(key==CureDisease.EffectKey)manager.AssignBundle(manager.CreateDisease(Diseases.Plague),AssignBundleFlags.BypassSavingThrows);
                else manager.AssignBundle(manager.CreatePoison(Poisons.Arsenic),AssignBundleFlags.BypassSavingThrows);
                actor.GetComponent<NativeNpcVitalsBinding>().Capture();
                var choose=typeof(StreetPopulation).GetMethod("ChooseMedicalVisit",Private);
                Require(choose.Invoke(streets,new object[] {plan,actor,manager})==null,"Insolvent NPC was offered paid treatment.");
                Require(store.ReadNpcProgress(id).TreatmentVisit==null,"Insolvent NPC started paid treatment.");
                Require(!(bool)typeof(StreetPopulation).GetMethod("MedicalPriorityAvailable",Private).Invoke(streets,
                    new object[] {plan,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),true}),"Danger did not defer treatment.");
                SetGold(store,owner,5000);
                ((Dictionary<string,float>)typeof(StreetPopulation).GetField("medicalChecks",Private).GetValue(streets)).Remove(id);
                Vector3 start=actor.transform.position;int initialHealth=body.Entity.CurrentHealth;
                bool moved=false,arrived=false,paid=false,returned=false;int goldBefore=store.ReadNpcInventory(id).WalletGold;
                deadline=Time.realtimeSinceStartup+240;
                while(Time.realtimeSinceStartup<deadline)
                {
                    var visit=store.ReadNpcProgress(id).TreatmentVisit;
                    if(actor && actor.gameObject.activeInHierarchy)
                    {
                        moved|=Vector3.Distance(start,actor.transform.position)>2;
                        // Keep the physical route loaded without colliding with the patient.
                        game.PlayerMotor.transform.position=actor.transform.position+Vector3.back*6+Vector3.up;
                    }
                    if(visit!=null && visit.ServiceAt>0)
                    {
                        var provider=services.Find(s=>s.Building.buildingKey==visit.ProviderKey);
                        TownGeometry.Entrance actualDoor=null;
                        Require(provider!=null && geometry.Entrances.TryGetValue(visit.ProviderKey,out actualDoor),"Invented medical destination.");
                        var target=geometry.ScenePoint(new RoutePoint(actualDoor.LocalDoor.x,actualDoor.LocalDoor.z));
                        Require(actor && Vector3.Distance(actor.transform.position-Vector3.up,target)<0.8f,"Treatment before actual arrival.");
                        arrived=true;
                    }
                    int count=key==CureDisease.EffectKey?manager.DiseaseCount:manager.PoisonCount;
                    paid=store.ReadNpcInventory(id).WalletGold<goldBefore;
                    if(count==0 && paid && visit==null) {returned=true;break;}
                    yield return null;
                }
                Require(moved && arrived && paid && returned,"Medical route/cure/return incomplete: "+key+
                    "; moved="+moved+"; arrived="+arrived+"; paid="+paid+"; returned="+returned+
                    "; visit="+(store.ReadNpcProgress(id).TreatmentVisit==null?"none":"active"));
                Require(body.Entity.CurrentHealth<=initialHealth,"Condition clinic invented HP healing.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MEDICAL_NATIVE_PASS: effect="+key+
                    "; noOwnCure=true; insufficientFundsDeferred=true; physicalArrival=true; nativeCure=true; paid=true; returnedToRoutine=true");
            }
            yield return ConversationMoodProbe.LoadSave(host,streets,character,original,false);
        }
        static void SetGold(WorldStateStore store,NativeNpcInventoryOwner owner,int amount)
        {
            var row=store.ReadNpcInventory(owner.Binding.PersonId);
            Require(store.CommitNpcInventoryTransaction("clinic-fixture:"+row.Revision,new[] {new NpcInventoryUpdate {
                PersonId=row.PersonId,ExpectedRevision=row.Revision,WalletGold=amount,
                Items=NativeNpcInventoryBridge.CaptureItems(owner.Binding.Items)}}),"Fixture wallet update failed.");
            owner.Capture();
        }
        static void Require(bool value,string message) {if(!value)throw new InvalidOperationException(message);}
    }
}
