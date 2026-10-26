using System;
using System.Collections;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        // Opt-in disposable fixture. Keep the observer at the same saved location
        // so time-of-day samples do not mix different population catchments.
        internal IEnumerator MeasureStreetDensity()
        {
            var game=GameManager.Instance;
            DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
            if(!Ready || game.PlayerEnterExit.IsPlayerInside)
                throw new InvalidOperationException("Street density measurement requires a ready exterior.");
            double day=Math.Floor(ActionNow()/86400d)*86400d;
            if(ActionNow()>day+9*3600+10*60)day+=86400;
            foreach(int minute in new[]{550,615,720,900})
            {
                double advance=day+minute*60-ActionNow();
                if(advance>0)DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)advance);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DENSITY_PHASE: minute="+minute+"; settleSeconds=30; observerUnchanged=true");
                RequestStreetDensitySample();yield return null;
                float deadline=Time.realtimeSinceStartup+30;
                while(Time.realtimeSinceStartup<deadline)yield return null;
                RequestStreetDensitySample();yield return null;
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DENSITY_COMPLETE: samples=4; populationLimitsUnchanged=true");
        }
    }
}
