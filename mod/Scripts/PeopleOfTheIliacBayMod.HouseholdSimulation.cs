using System;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private int nextHouseholdSimulation;
        private float householdSimulationTickAt;
        private void TickHouseholdSimulations()
        {
            if(saveFault || store==null || !options.Enabled || !streetPopulationEnabled || !GameManager.Instance ||
                GameManager.Instance.SaveLoadManager.LoadInProgress || GameManager.IsGamePaused || Time.unscaledTime<householdSimulationTickAt)return;
            householdSimulationTickAt=Time.unscaledTime+0.1f;
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            var batch=store.ReadHouseholdSimulationBatch(nextHouseholdSimulation,2,out nextHouseholdSimulation);
            foreach(var saved in batch)
            {
                if(now<saved.AdvancedThrough)continue;
                try
                {
                    bool first=!streets || streets.HouseholdParentAvailable(saved.Base.FirstResidentId,now);
                    bool second=!streets || streets.HouseholdParentAvailable(saved.Base.SecondResidentId,now);
                    PrepareHouseholdInnBuyers(store,saved.Base.HouseholdDayId);
                    var sim=store.AdvanceHouseholdJointLifecycle(saved.Base.HouseholdDayId,now,NativeInnCalendar.ForServiceAt,first,second,false,
                        streets?(Func<NpcInnServiceCatalog,NpcInnServiceSeatPair,bool>)streets.CanReserveHouseholdSeats:null);
                    if(sim.Phase==HouseholdJointPhase.Completed && (!streets || !streets.HouseholdHomeLoaded(sim.Base.SettlementId,sim.Base.HomeKey)))
                        store.ReleaseCompletedHouseholdSimulation(saved.Base.HouseholdDayId,now);
                    else if(sim.Phase==HouseholdJointPhase.Waiting)store.TryCancelMissedHouseholdDeparture(saved.Base.HouseholdDayId,now);
                }
                catch(ArgumentException error)
                {Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_HOUSEHOLD_SIMULATION_HELD: "+saved.Base.HouseholdDayId+"; "+error.Message);}
            }
        }
        internal static void PrepareHouseholdInnBuyers(WorldStateStore world,string dayId)
        {
            var service=world.ReadHouseholdInnServiceAdmission(dayId);
            if(service==null || service.Outcome!="pending")return;
            foreach(string id in new[]{service.FirstIdentity.BuyerId,service.SecondIdentity.BuyerId})
            {
                if(world.HasNpcInventory(id))continue;
                var items=NativeNpcStartingBelongings.CreateInitialItems(world,id,resident:world.ResolveResident(id));
                NativeNpcInventoryBinding binding;string reason;
                if(!NativeNpcInventoryBridge.TryOpen(world,id,items,NativeNpcInventoryBridge.InitialWalletGold(world,id),out binding,out reason))
                    throw new ArgumentException(reason);
            }
        }
    }
}
