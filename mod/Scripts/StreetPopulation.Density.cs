using UnityEngine;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private sealed class StreetDensityFrame
        {
            internal int Considered,SimulatedFamily,FamilyPreparing,ActionOwned,Outside,Inside,NearEligible,CapDeferred,SpawnDeferred;
            internal int Home,Sleep,Work,Visit,Recovery;
            internal double At;
            internal void Count(Activity activity)
            {
                switch(activity)
                {
                    case Activity.Home:Home++;break;
                    case Activity.Sleep:Sleep++;break;
                    case Activity.Work:Work++;break;
                    case Activity.Visit:Visit++;break;
                    case Activity.Recovery:Recovery++;break;
                }
            }
        }
        private float nextStreetDensity;
        internal void RequestStreetDensitySample(){nextStreetDensity=0;}
        private StreetDensityFrame BeginStreetDensity(double now)
        {
            if(Time.unscaledTime<nextStreetDensity)return null;
            nextStreetDensity=Time.unscaledTime+60;
            return new StreetDensityFrame{At=now};
        }
        private void ReportStreetDensity(StreetDensityFrame sample)
        {
            if(sample==null)return;
            int active=0,standing=0;
            foreach(var actor in actors.Values)
            {
                if(!actor || !actor.gameObject.activeInHierarchy)continue;
                active++;if(actor.IsStandingStill)standing++;
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_STREET_DENSITY: map="+population.SettlementId+
                "; hour="+(sample.At%86400/3600).ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+
                "; plans="+plans.Count+"; considered="+sample.Considered+"; scheduledOutside="+sample.Outside+
                "; scheduledInside="+sample.Inside+"; nearEligible="+sample.NearEligible+"; activeBodies="+active+
                "; standing="+standing+"; simulatedFamily="+sample.SimulatedFamily+"; familyPreparing="+sample.FamilyPreparing+
                "; home="+sample.Home+"; sleep="+sample.Sleep+"; work="+sample.Work+"; visit="+sample.Visit+"; recovery="+sample.Recovery+
                "; actionOwned="+sample.ActionOwned+"; capDeferred="+sample.CapDeferred+"; spawnBudgetDeferred="+sample.SpawnDeferred+
                "; actorCap=80; spawnRadius=140; retentionRadius=165");
        }
    }
}
