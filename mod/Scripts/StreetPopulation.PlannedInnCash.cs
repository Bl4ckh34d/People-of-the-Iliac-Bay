using System;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private bool PrepareSharedInnBudget(HouseholdJointInn outing,Plan first,Plan second,int day)
        {
            var catalog=worldStore.ReadNpcInnServiceCatalog(population.SettlementId,outing.InnKey);
            if(catalog==null)return false;
            foreach(var parent in new[]{first,second})
            {
                if(worldStore.HasNpcInventory(parent.Resident.Id))continue;
                NativeNpcInventoryBinding binding;string reason;
                if(!NativeNpcInventoryBridge.TryOpen(worldStore,parent.Resident.Id,
                    NativeNpcStartingBelongings.CreateInitialItems(worldStore,parent.Resident.Id,resident:parent.Resident),
                    NativeNpcInventoryBridge.InitialWalletGold(worldStore,parent.Resident.Id),out binding,out reason))return false;
            }
            // Any native item for each parent is covered without selecting a future meal.
            outing.ReserveGold=HouseholdInnBudget.GroupReserve(catalog);
            uint seed=StableSeed.Combine(worldStore.WorldSeed,(uint)first.Resident.HomeKey,(uint)day,0x63617368u);
            int a=worldStore.ReadNpcInventory(first.Resident.Id).WalletGold,b=worldStore.ReadNpcInventory(second.Resident.Id).WalletGold;
            outing.PayerId=a>b || a==b && (seed&1)==0?first.Resident.Id:second.Resident.Id;
            return true;
        }

        private bool PlannedCashWindowOpen(Plan plan,HouseholdInnFunding funding,double now)
        {
            if(funding==null || now>=funding.DepartureAt)return true;
            if(directory.GetBuildingsOfType(DFLocation.BuildingTypes.Bank).Count==0)return true;
            // Stagger morning errands, retaining the native bank service window.
            double opens=Math.Floor(now/86400d)*86400d+PlayerActivate.openHours[(int)DFLocation.BuildingTypes.Bank]*3600;
            uint seed=StableSeed.Combine(worldStore.WorldSeed,plan.Resident.AppearanceSeed,(uint)(now/86400),0x62616e6bu);
            return now>=opens+(seed%31)*60;
        }

        private static bool CashGoalMet(NpcCashVisit visit,NpcInventoryState wallet)
        {return wallet!=null && wallet.WalletGold>=(visit.PlannedHouseholdDayId==null?1:visit.RequiredGold);}

        private bool PlannedBankTripFits(Plan plan,NpcCashVisit visit,HouseholdInnFunding funding,double now)
        {
            if(funding==null || !visit.Bank || now>=funding.DepartureAt)return true;
            TownGeometry.Entrance bank,home;
            if(!geometry.Entrances.TryGetValue(visit.ProviderKey,out bank) ||
                !geometry.Entrances.TryGetValue(plan.Resident.HomeKey,out home))return false;
            var start=new SavedRoutePoint{X=visit.Position.X,Z=visit.Position.Z};
            if(visit.Position.Kind==NpcActionTargetKind.IndoorPoint)
            {
                TownGeometry.Entrance source;
                if(!geometry.Entrances.TryGetValue(visit.Position.BuildingKey,out source))return false;
                start=new SavedRoutePoint{X=source.LocalDoor.x,Z=source.LocalDoor.z};
            }
            var bankPoint=new SavedRoutePoint{X=bank.LocalDoor.x,Z=bank.LocalDoor.z};
            var outward=geometry.RouteBetweenPoints(start,bankPoint);
            var returning=geometry.RouteBetweenPoints(bankPoint,new SavedRoutePoint{X=home.LocalDoor.x,Z=home.LocalDoor.z});
            if(outward.Count<2 || returning.Count<2)return false;
            double pace=DaggerfallUnity.Instance.WorldTime.TimeScale/ResidentMovement.WalkSpeedFor(plan.Resident.AppearanceSeed);
            // Include a conservative minute for the surveyed clerk approach,
            // service and exit; the runtime still requires the actual visit.
            double served=now+RouteLength(outward)*pace+60*DaggerfallUnity.Instance.WorldTime.TimeScale;
            double close=Math.Floor(now/86400d)*86400d+PlayerActivate.closeHours[(int)DFLocation.BuildingTypes.Bank]*3600;
            return served<close && served+RouteLength(returning)*pace<funding.DepartureAt;
        }

        private int CashWithdrawalAmount(string id,NpcCashVisit visit)
        {
            int balance=worldStore.ReadNpcInventory(id)?.WalletGold??0;
            return Math.Min(NpcInventoryLimits.MaximumWalletGold-balance,
                Math.Max(NativeNpcInventoryBridge.CashWithdrawalGold(worldStore,id),visit.RequiredGold-balance));
        }
    }
}
