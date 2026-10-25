using System;

namespace PeopleOfTheIliacBay.Core
{
    public static class NpcContainerAccessPolicy
    {
        public static bool AllowsOwnPermanentHomeContainerAccess(Resident resident,int settlementId,Household household,int containerBuildingKey,
            bool houseContainer,bool playerOwned,bool shop,bool inn,bool factionOwned,bool rentableRoom,
            bool publicInventory,bool customDrop,bool corpse,bool questAction)
        {
            return AllowsOwnPermanentHomeDeposit(resident,settlementId,household,containerBuildingKey,houseContainer,
                playerOwned,shop,inn,factionOwned,rentableRoom,publicInventory,customDrop,corpse,questAction);
        }
        public static bool AllowsOwnPermanentHomeDeposit(Resident resident,int settlementId,Household household,int containerBuildingKey,
            bool houseContainer,bool playerOwned,bool shop,bool inn,bool factionOwned,bool rentableRoom,
            bool publicInventory,bool customDrop,bool corpse,bool questAction)
        {
            if(resident==null || !resident.IsAlive || resident.IsQuestProtected || string.IsNullOrWhiteSpace(resident.Id) ||
                settlementId<0 || containerBuildingKey<0 || resident.HomeKey!=containerBuildingKey || household==null ||
                household.BuildingKey!=containerBuildingKey || household.Residents==null || !houseContainer ||
                playerOwned || shop || inn || factionOwned || rentableRoom || publicInventory || customDrop || corpse || questAction)
                return false;
            if(resident.NativeIdentity!=null && (!resident.NativeIdentity.IsAlive || resident.NativeIdentity.SettlementId!=settlementId ||
                resident.NativeIdentity.HomeKey!=containerBuildingKey)) return false;
            foreach(var member in household.Residents)
                if(member!=null && member.IsAlive && member.Id==resident.Id && member.HomeKey==containerBuildingKey) return true;
            return false;
        }
    }
}
