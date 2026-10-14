using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using LivingCities.Core;
using UnityEngine;
namespace LivingCities.DFU
{
    public static class DaggerfallSaveProbe
    {
        public static IEnumerator Run(LivingCitiesMod mod,StreetPopulation streets,GuardPopulation guards)
        {
            var game=GameManager.Instance;
            if(!game.PlayerEntity.Name.StartsWith("LivingCitiesTest-",StringComparison.Ordinal))
                throw new InvalidOperationException("City save probe requires the dedicated test character.");
            var before=(WorldState)mod.GetSaveData();
            var town=before.Settlements.Find(t=>t.SettlementId==1291010263);
            string population=SaveLoadManager.Serialize(typeof(SettlementPopulation),town);
            string routines=SaveLoadManager.Serialize(typeof(TownRoutineState),before.Routines.Find(t=>t.SettlementId==1291010263));
            var states=new Dictionary<string,ActivitySnapshot>();
            double sample=Math.Floor(DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()/86400d)*86400+12*3600;
            foreach(var home in town.Households) foreach(var resident in home.Residents)
                if(!resident.IsQuestProtected && !before.NativePeople.Exists(p=>p.Id==resident.Id))
                    states.Add(resident.Id,streets.GetState(resident.Id,sample));
            var guardIds=new HashSet<string>(); foreach(var guard in guards.Roster.Guards) guardIds.Add(guard.Id);
            bool saved=false,loaded=false;
            SaveLoadManager.OnSaveEventHandler onSave=ignored=>saved=true;
            SaveLoadManager.OnLoadEventHandler onLoad=ignored=>loaded=true;
            SaveLoadManager.OnSave+=onSave; SaveLoadManager.OnLoad+=onLoad;
            try
            {
                const string name="Living Cities - Daggerfall city preview";
                game.SaveLoadManager.Save(game.PlayerEntity.Name,name);
                float deadline=Time.realtimeSinceStartup+30;
                while(!saved && Time.realtimeSinceStartup<deadline) yield return null;
                if(!saved) throw new InvalidOperationException("City save timed out.");
                int key=game.SaveLoadManager.FindSaveFolderByNames(game.PlayerEntity.Name,name);
                game.SaveLoadManager.Load(key); DaggerfallUI.Instance.PopToHUD();
                deadline=Time.realtimeSinceStartup+60;
                while((!loaded || !streets.Ready || guards.Roster==null) && Time.realtimeSinceStartup<deadline) yield return null;
                if(!loaded || !streets.Ready || guards.Roster==null || guards.Roster.SettlementId!=1291010263)
                    throw new InvalidOperationException("City reload timed out.");
                var after=(WorldState)mod.GetSaveData();
                if(population!=SaveLoadManager.Serialize(typeof(SettlementPopulation),after.Settlements.Find(t=>t.SettlementId==1291010263)) ||
                    routines!=SaveLoadManager.Serialize(typeof(TownRoutineState),after.Routines.Find(t=>t.SettlementId==1291010263)))
                    throw new InvalidOperationException("City identities or saved routes changed after reload.");
                if(after.HauntingDay==null || before.HauntingDay==null || after.HauntingDay.Day!=before.HauntingDay.Day ||
                    after.HauntingDay.Restricted!=before.HauntingDay.Restricted || guards.Roster.Guards.Count!=guardIds.Count)
                    throw new InvalidOperationException("City policy or guard count changed after reload.");
                foreach(var guard in guards.Roster.Guards) if(!guardIds.Contains(guard.Id)) throw new InvalidOperationException("City guard identity changed.");
                foreach(var pair in states)
                {
                    var actual=streets.GetState(pair.Key,sample); var expected=pair.Value;
                    if(actual.Activity!=expected.Activity || actual.BuildingKey!=expected.BuildingKey ||
                        Math.Abs(actual.Position.X-expected.Position.X)>0.0001 || Math.Abs(actual.Position.Z-expected.Position.Z)>0.0001)
                        throw new InvalidOperationException("City schedule changed after reload: "+pair.Key);
                }
                Debug.Log("LIVING_CITIES_CITY_SAVE_PASS: residents="+states.Count+"; guards="+guardIds.Count+
                    "; identities, routes, sampled activities and daily curfew preserved; save="+game.SaveLoadManager.GetSaveFolder(key));
            }
            finally { SaveLoadManager.OnSave-=onSave; SaveLoadManager.OnLoad-=onLoad; }
        }
    }
}
