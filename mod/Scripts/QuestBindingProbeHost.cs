using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Utility;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    // Test-only bridge to a separate compiled probe; no dependency in normal play.
    public static class QuestBindingProbeHost
    {
        public static IEnumerator Run()
        {
            var probe = Type.GetType("LivingCities.Probes.QuestBindingProbe, QuestBindingProbe", true);
            var game = GameManager.Instance;
            if (game.PlayerEnterExit.IsPlayerInside) game.PlayerEnterExit.TransitionExterior();
            yield return new WaitForSecondsRealtime(1);
            int mapId = game.PlayerGPS.CurrentMapID;
            var adapter = UnityEngine.Object.FindObjectOfType<LivingCitiesMod>();
            string originalPopulation = JsonUtility.ToJson(adapter.GetSaveData());
            var population = ((WorldState)adapter.GetSaveData()).Settlements.Find(t => t.SettlementId == mapId);
            var homes = new Dictionary<int, string[]>();
            uint oldSeed = DFRandom.Seed;
            try
            {
                foreach (var home in population.Households)
                {
                    var resident = home.Residents.Find(r => r.IsAlive && r.AgeGroup == ResidentAgeGroup.Adult && r.Sex == ResidentSex.Male);
                    if (resident == null) continue;
                    DFRandom.Seed = resident.AppearanceSeed;
                    string first = DaggerfallUnity.Instance.NameHelper.FirstName(NameHelper.BankTypes.Breton, Genders.Male);
                    DFRandom.Seed = home.SurnameSeed;
                    string surname = DaggerfallUnity.Instance.NameHelper.Surname(NameHelper.BankTypes.Breton);
                    homes.Add(home.BuildingKey, new[] { first + " " + surname,
                        TextManager.Instance.GetLocalizedText("theNamedResidence").Replace("%s", surname) });
                }
            }
            finally { DFRandom.Seed = oldSeed; }
            yield return (IEnumerator)probe.GetMethod("Run").Invoke(null, new object[] { mapId, homes });
            if (JsonUtility.ToJson(adapter.GetSaveData()) != originalPopulation)
                throw new InvalidOperationException("Disposable quest preview changed persistent population data.");
        }
    }
}
