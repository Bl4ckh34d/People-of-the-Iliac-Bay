using System;
using System.Reflection;
using DaggerfallWorkshop;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public static class HouseholdGreetingProbe
    {
        public static void Run(StreetPopulation streets)
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Instance;
            var town = (SettlementPopulation)typeof(StreetPopulation).GetField("population", flags).GetValue(streets);
            var hooks = Type.GetType("PeopleOfTheIliacBay.QuestHooks.NativeHouseholdDoorHooks, PeopleOfTheIliacBay.QuestHooks", true);
            var begin = hooks.GetMethod("BeforeDoor", BindingFlags.NonPublic | BindingFlags.Static);
            var end = hooks.GetMethod("AfterDoor", BindingFlags.NonPublic | BindingFlags.Static);
            foreach (var home in town.Households)
            {
                if (home.Residents.Count == 0 || home.Residents.Exists(r => !r.IsAlive) ||
                    streets.HouseholdGreeting(home.BuildingKey) != null) continue;
                try
                {
                    // Synchronous fixture: no simulation tick observes the temporary deaths.
                    foreach (var resident in home.Residents) resident.IsAlive = false;
                    string silent = streets.HouseholdGreeting(home.BuildingKey);
                    if (silent == null) continue; // A visitor/native/guard still occupies this home.
                    object[] scope = { true, new StaticBuilding { buildingKey = home.BuildingKey }, null };
                    begin.Invoke(null, scope);
                    try
                    {
                        string actual = DaggerfallUnity.Instance.TextProvider.GetRandomText(256);
                        if (actual != "You are greeted with an eerie silence...")
                            throw new InvalidOperationException("Native empty-house greeting did not use actual vacancy.");
                    }
                    finally { end.Invoke(null, new object[] { null, scope[2] }); }
                    if (DaggerfallUnity.Instance.TextProvider.GetRandomText(256) == silent)
                        throw new InvalidOperationException("House greeting override leaked beyond door activation.");
                }
                finally { foreach (var resident in home.Residents) resident.IsAlive = true; }
                if (streets.HouseholdGreeting(home.BuildingKey) != null)
                    throw new InvalidOperationException("Occupied-house greeting was not restored.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_EMPTY_HOUSE_GREETING_PASS: dead household silent; native text scoped to entry; living occupants restored.");
                return;
            }
            throw new InvalidOperationException("No isolated occupied household available for greeting regression.");
        }
    }
}
