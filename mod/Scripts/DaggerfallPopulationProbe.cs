using System;
using System.Collections;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using LivingCities.Core;
using UnityEngine;
namespace LivingCities.DFU
{
    public static class DaggerfallPopulationProbe
    {
        internal static bool SuppressCityActors;
        public static IEnumerator Run()
        {
            var mod=UnityEngine.Object.FindObjectOfType<LivingCitiesMod>();
            var streets=mod.GetComponent<StreetPopulation>(); var guards=mod.GetComponent<GuardPopulation>();
            var game=GameManager.Instance; bool oldCurse=game.PlayerEntity.GlobalVars.GetGlobalVar(0);
            bool oldSuppression=SuppressCityActors; SuppressCityActors=false;
            try
            {
                game.PlayerEntity.GlobalVars.SetGlobalVar(0,false);
                float started=Time.realtimeSinceStartup,deadline=started+90;
                while((!streets.Ready || guards.Roster==null || guards.Roster.SettlementId!=1291010263) && Time.realtimeSinceStartup<deadline) yield return null;
                if(!streets.Ready || guards.Roster==null || guards.Roster.SettlementId!=1291010263)
                    throw new InvalidOperationException("Daggerfall population admission did not finish.");
                var snapshot=(WorldState)mod.GetSaveData(); var town=snapshot.Settlements.Find(s=>s.SettlementId==1291010263);
                int residents=0,checkedResidents=0;
                double night=Math.Floor(DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()/86400d)*86400+20*3600;
                foreach(var home in town.Households) foreach(var person in home.Residents)
                {
                    residents++;
                    if(person.IsQuestProtected || snapshot.NativePeople.Exists(p=>p.Id==person.Id)) continue;
                    var state=streets.GetState(person.Id,night);
                    if(state.Activity==Activity.Travel || state.BuildingKey!=home.BuildingKey)
                        throw new InvalidOperationException("Haunted city resident remains outside home at night.");
                    checkedResidents++;
                }
                if(checkedResidents<100 || streets.PlannedCount<100 || guards.Roster.Target!=GuardRoster.TargetCount(SettlementScale.City,residents))
                    throw new InvalidOperationException("Incomplete city population or incorrect garrison scale.");
                Debug.Log("LIVING_CITIES_CITY_POPULATION_PASS: residents="+residents+"; plans="+streets.PlannedCount+
                    "; guards="+guards.Roster.Target+"; initialSeconds="+(Time.realtimeSinceStartup-started));
                yield return DaggerfallSaveProbe.Run(mod,streets,guards);
            }
            finally { SuppressCityActors=oldSuppression;
                game.PlayerEntity.GlobalVars.SetGlobalVar(0,oldCurse); streets.Clear(); guards.Clear(); }
        }
    }
}
