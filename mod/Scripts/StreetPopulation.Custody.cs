using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private IDisposable custodyHooks;
        private void RegisterCustodyHooks()
        {
            if(custodyHooks!=null)return;
            var type=Type.GetType("PeopleOfTheIliacBay.QuestHooks.NativeCustodyHooks, PeopleOfTheIliacBay.QuestHooks",true);
            custodyHooks=(IDisposable)Activator.CreateInstance(type,new object[]{new Func<Action>(BeginCustodialAbsence)});
        }
        private Action BeginCustodialAbsence()
        {
            if(worldStore==null || population==null || !DaggerfallUnity.Instance)return null;
            var capturedStore=worldStore;int settlement=population.SettlementId;
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            double begins=clock.ToSeconds()+(clock.Second-Math.Floor(clock.Second));
            var incidents=new HashSet<string>(StringComparer.Ordinal);
            // Court pauses gameplay; this confirmed custody path deliberately
            // does not require IsPlayingGame or infer presence from the old map.
            foreach(var alarm in capturedStore.OutdoorCorpses(settlement))
            {
                if(alarm.Corpse.DiedAt>begins)continue;
                capturedStore.ObserveOutdoorCorpse(alarm.Id,false,begins);incidents.Add(alarm.Id);
            }
            return ()=>
            {
                if(!ReferenceEquals(capturedStore,worldStore))return;
                var endClock=DaggerfallUnity.Instance.WorldTime.Now;
                double ends=endClock.ToSeconds()+(endClock.Second-Math.Floor(endClock.Second));
                if(ends<begins)return;
                foreach(var alarm in capturedStore.OutdoorCorpses(settlement))
                    if(incidents.Contains(alarm.Id))capturedStore.ObserveOutdoorCorpse(alarm.Id,false,ends);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CUSTODY_CORPSE_CATCHUP: map="+settlement+"; bodies="+incidents.Count+"; seconds="+(ends-begins));
            };
        }
    }
}
