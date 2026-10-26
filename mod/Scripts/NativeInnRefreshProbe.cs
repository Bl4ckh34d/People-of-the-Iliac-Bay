using System;
using System.Collections;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Explicit native timing fixture. Calls the existing loaded-Inn reconciliation;
    // it neither fabricates residents/reservations nor changes the game clock.
    internal static class NativeInnRefreshProbe
    {
        internal static IEnumerator Run(PeopleOfTheIliacBayMod host)
        {
            var game=GameManager.Instance;
            if(!host || !RuntimeTiming.Enabled || !NativeProbeText.IsTestCharacter(game.PlayerEntity.Name))
                throw new InvalidOperationException("Inn timing requires the owned fixture and NavigationTiming.");
            var room=game.PlayerEnterExit.Interior;
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();BuildingSummary summary;
            if(!game.PlayerEnterExit.IsPlayerInsideBuilding || !room || !directory ||
                !directory.GetBuildingSummary(room.EntryDoor.buildingKey,out summary) || summary.BuildingType!=DFLocation.BuildingTypes.Tavern)
                throw new InvalidOperationException("Inn timing requires an actual loaded native tavern.");
            const BindingFlags fields=BindingFlags.Instance|BindingFlags.NonPublic;
            var rentals=(NativeTavernRentals)typeof(PeopleOfTheIliacBayMod).GetField("nativeRentals",fields).GetValue(host);
            var store=(WorldStateStore)typeof(PeopleOfTheIliacBayMod).GetField("store",fields).GetValue(host);
            // Exclude first-frame entry setup; both before/after runs use this wait.
            yield return new WaitForSecondsRealtime(2);
            RuntimeTiming.Reset();
            for(int i=0;i<8;i++)
            {
                if(game.PlayerEnterExit.Interior!=room || game.StreamingWorld.GetCurrentBuildingDirectory()!=directory)
                    throw new InvalidOperationException("Inn timing scene changed during measurement.");
                long started=RuntimeTiming.Start("inn-refresh-probe");
                try { rentals.Refresh(store,directory); }
                finally { RuntimeTiming.End("inn-refresh-probe",started); }
                yield return new WaitForSecondsRealtime(0.25f);
            }
            RuntimeTiming.Report("inn-refresh");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INN_REFRESH_MEASURED: building="+room.EntryDoor.buildingKey+
                "; explicitCalls=8; ordinaryRefreshEnabled=true; gameplayAcceptance=false");
        }
    }
}
