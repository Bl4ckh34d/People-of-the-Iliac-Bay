using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using LivingCities.Core;
using UnityEngine;
namespace LivingCities.DFU
{
    public static class ShopVisitProbe
    {
        public static IEnumerator Run()
        {
            var game=GameManager.Instance; var room=game.PlayerEnterExit.Interior;
            if(!room) throw new InvalidOperationException("Shop fixture is not inside.");
            var mod=UnityEngine.Object.FindObjectOfType<LivingCitiesMod>(); var streets=mod.GetComponent<StreetPopulation>();
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var plans=(IEnumerable)typeof(StreetPopulation).GetField("plans",flags).GetValue(streets);
            object selected=null;
            foreach(var plan in plans)
            {
                var resident=(Resident)plan.GetType().GetField("Resident").GetValue(plan);
                if(resident.IsAlive && !resident.IsQuestProtected && resident.NativeIdentity==null && resident.AgeGroup!=ResidentAgeGroup.Child && resident.HomeKey!=room.EntryDoor.buildingKey)
                { selected=plan; break; }
            }
            if(selected==null) throw new InvalidOperationException("Shop visitor fixture absent.");
            var type=selected.GetType(); var person=(Resident)type.GetField("Resident").GetValue(selected);
            var original=type.GetField("Schedule").GetValue(selected);
            var schedule=DailySchedule.DayVisit(person.HomeKey,room.EntryDoor.buildingKey,new[] { new RoutePoint(0,0),new RoutePoint(1,0) },
                540,1080,30,1.3,12,person.AppearanceSeed,360,1320);
            double start=0,end=0;
            for(int minute=540;minute<1080;minute++) if(schedule.TryVisitWindow(minute*60,out start,out end)) break;
            if(end<=start) throw new InvalidOperationException("Controlled visitor window absent.");
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            double day=Math.Floor(now/86400)*86400;
            start+=day; end+=day;
            if(start+1<=now) { start+=86400; end+=86400; }
            type.GetField("Schedule").SetValue(selected,schedule);
            var routine=(ResidentRoutine)type.GetField("Routine").GetValue(selected);
            int originalDestination=routine.DestinationKey; routine.DestinationKey=room.EntryDoor.buildingKey;
            typeof(StreetPopulation).GetField("shopVisitDay",flags).SetValue(streets,-1d);
            try
            {
                DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((long)(start+1-now));
                yield return null; yield return null;
                float deadline=Time.realtimeSinceStartup+10;
                var journeys=(Dictionary<string,ShopVisitJourney>)typeof(StreetPopulation).GetField("shopJourneys",flags).GetValue(streets);
                while((!journeys.ContainsKey(person.Id) || !streets.FindActor(person.Id)) && Time.realtimeSinceStartup<deadline) yield return null;
                ShopVisitJourney journey;
                if(!journeys.TryGetValue(person.Id,out journey) || journey==null) throw new InvalidOperationException("Real shop lacks usable activity routes.");
                var actor=streets.FindActor(person.Id);
                Vector3 before=actor.transform.position;
                yield return new WaitForSecondsRealtime(2);
                if(Vector3.Distance(before,actor.transform.position)<0.5f || actor.Person.Asset.IsIdle)
                    throw new InvalidOperationException("Actual shop visitor did not walk with animation.");
                bool browsed=false,haggled=false,left=false;
                foreach(var phase in new[] { ShopVisitPhase.Browsing,ShopVisitPhase.Haggling,ShopVisitPhase.Leaving })
                {
                    double first=-1,last=-1;
                    for(double scan=start;scan<end;scan+=1)
                    {
                        if(journey.Evaluate(scan).Phase==phase) { if(first<0) first=scan; last=scan; }
                        else if(first>=0) break;
                    }
                    if(first<0) throw new InvalidOperationException("Missing shop phase: "+phase);
                    double sample=(first+last)*0.5;
                    DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((long)(sample-DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()));
                    yield return new WaitForSecondsRealtime(0.2f);
                    var state=journey.Evaluate(DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
                    var expected=room.transform.TransformPoint(new Vector3((float)state.Position.X,(float)state.Position.Y,(float)state.Position.Z))+Vector3.up;
                    if(!actor || Vector3.Distance(expected,actor.transform.position)>0.5f || actor.Person.Asset.IsIdle!=(phase!=ShopVisitPhase.Leaving))
                        throw new InvalidOperationException("Rendered shop phase mismatch: "+phase+"; expected="+expected+
                            "; actual="+(actor ? actor.transform.position.ToString() : "missing")+"; idle="+(actor ? actor.Person.Asset.IsIdle.ToString() : "missing")+
                            "; motor="+(actor ? actor.Person.Motor.enabled.ToString() : "missing")+"; cached="+journeys[person.Id].Evaluate(DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()).Phase+"; activity="+streets.GetState(person.Id,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()).Activity+"; "+mod.TestStatus());
                    if(state.Phase==ShopVisitPhase.Browsing) browsed=true;
                    if(state.Phase==ShopVisitPhase.Haggling) haggled=true;
                    if(state.Phase==ShopVisitPhase.Leaving) left=true;
                }
                if(!browsed || !haggled || !left) throw new InvalidOperationException("Shop activity phases were not observed.");
                DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((long)(end+1-DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()));
                yield return null; yield return null;
                if(streets.FindActor(person.Id)) throw new InvalidOperationException("Shop visitor remained after departure.");
                Debug.Log("LIVING_CITIES_SHOP_VISIT_PASS: actual shelf/counter routes, animated movement, browsing, haggling and scheduled exit; resident="+person.Id);
            }
            finally { type.GetField("Schedule").SetValue(selected,original); routine.DestinationKey=originalDestination; }
        }
    }
}
