using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using LivingCities.Core;
using UnityEngine;
namespace LivingCities.DFU
{
    public static class DaggerfallMovementProbe
    {
        public static IEnumerator Run(LivingCitiesMod mod,StreetPopulation streets)
        {
            var game=GameManager.Instance; var clock=DaggerfallUnity.Instance.WorldTime.Now;
            var town=((WorldState)mod.GetSaveData()).Settlements.Find(t=>t.SettlementId==1291010263);
            double day=(Math.Floor(clock.ToSeconds()/86400d)+1)*86400;
            string id=null; double chosen=0;
            for(int minute=600;minute<1020 && id==null;minute+=5)
                foreach(var home in town.Households)
                {
                    foreach(var person in home.Residents)
                    {
                        if(person.IsQuestProtected || person.AgeGroup!=ResidentAgeGroup.Adult) continue;
                        var state=streets.GetState(person.Id,day+minute*60);
                        if(state.Activity==Activity.Travel && state.RouteProgress>0.2 && state.RouteProgress<0.8)
                        { id=person.Id; chosen=day+minute*60; break; }
                    }
                    if(id!=null) break;
                }
            if(id==null) throw new InvalidOperationException("City fixture has no daytime traveller.");
            clock.RaiseTime((float)(chosen-clock.ToSeconds()));
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            var geometry=new TownGeometry(directory,game.StreamingWorld.GetCurrentCityNavigation(),false);
            var stateNow=streets.GetState(id,clock.ToSeconds());
            game.PlayerMotor.transform.position=geometry.ScenePoint(stateNow.Position)+Vector3.up*2;
            ResidentActor actor=null; float deadline=Time.realtimeSinceStartup+10;
            while(!actor && Time.realtimeSinceStartup<deadline)
            {
                foreach(var candidate in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                    if(candidate.Resident.Id==id && candidate.gameObject.activeInHierarchy) { actor=candidate; break; }
                yield return null;
            }
            if(!actor) throw new InvalidOperationException("Scheduled city traveller did not materialize near player.");
            Vector3 start=actor.transform.position;
            var frames=new List<float>(); float began=Time.realtimeSinceStartup;
            while(Time.realtimeSinceStartup<began+3)
            {
                yield return null; frames.Add(Time.unscaledDeltaTime*1000);
                if(!actor || !actor.gameObject.activeInHierarchy) throw new InvalidOperationException("City traveller vanished during its route.");
            }
            float moved=Vector3.Distance(start,actor.transform.position);
            if(moved<0.5 || moved>15 || streets.ActiveCount>80)
                throw new InvalidOperationException("Unexpected city movement distance or actor cap: "+moved);
            frames.Sort();
            Debug.Log("LIVING_CITIES_CITY_MOVEMENT_PASS: resident="+id+"; moved="+moved+"; active="+streets.ActiveCount+
                "; frames="+frames.Count+"; medianMs="+frames[frames.Count/2]+"; p95Ms="+frames[(int)((frames.Count-1)*0.95)]);
        }
    }
}
