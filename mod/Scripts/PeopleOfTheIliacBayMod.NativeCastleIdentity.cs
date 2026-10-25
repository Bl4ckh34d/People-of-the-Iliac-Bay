using System.Globalization;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Questing;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        // Authored castle layout is stable. Quest replacements retain their own boundary.
        private static bool TryNativeCastleLayoutKey(StaticNPC npc,out string key)
        {
            key=null;var game=GameManager.Instance;
            if(!npc || npc.Data.context!=StaticNPC.Context.Dungeon || npc.GetComponent<QuestResourceBehaviour>() ||
                game==null || !game.IsPlayerInsideCastle || !game.PlayerGPS.HasCurrentLocation ||
                npc.Data.mapID!=game.PlayerGPS.CurrentMapID ||
                npc.Data.locationID!=(int)game.PlayerGPS.CurrentLocation.Exterior.ExteriorData.LocationId)return false;
            var block=npc.GetComponentInParent<DaggerfallRDBBlock>();var dungeon=game.PlayerEnterExit.Dungeon;
            if(!block || !dungeon || !block.transform.IsChildOf(dungeon.transform))return false;
            key="castle:"+npc.Data.locationID.ToString(CultureInfo.InvariantCulture)+":"+
                block.RDBPosition.x.ToString("R",CultureInfo.InvariantCulture)+":"+
                block.RDBPosition.y.ToString("R",CultureInfo.InvariantCulture)+":"+
                npc.Data.nameSeed.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        private static string StaticConversationPersonId(StaticNPC npc)
        {
            if(!npc)return null;var data=npc.Data;string key;
            int exteriorTown,exteriorHash;
            if(NativeExteriorNpcIdentity.TryRead(npc,out exteriorTown,out exteriorHash))
                return NativeExteriorNpcIdentity.PersonId(exteriorTown,exteriorHash);
            return TryNativeCastleLayoutKey(npc,out key)?WorldStateStore.NativeLayoutPersonId(data.mapID,key,data.hash):
                "npc:"+data.mapID.ToString(CultureInfo.InvariantCulture)+":"+
                data.buildingKey.ToString(CultureInfo.InvariantCulture)+":"+data.hash.ToString(CultureInfo.InvariantCulture);
        }

        private void RegisterNativeStaticConversationPerson(StaticNPC npc)
        {
            int exteriorTown,exteriorHash;
            if(!saveFault && store!=null && NativeExteriorNpcIdentity.TryRead(npc,out exteriorTown,out exteriorHash))
            {
                if(store.CurrentPopulation(exteriorTown)==null)return;
                var exterior=npc.Data;
                store.RegisterNativePerson(exteriorTown,-1,exteriorHash,npc.DisplayName,
                    exterior.gender==Genders.Female?ResidentSex.Female:ResidentSex.Male,
                    (int)exterior.race,exterior.factionID,exterior.billboardArchiveIndex,exterior.billboardRecordIndex);
                return;
            }
            string key;
            if(saveFault || store==null || !TryNativeCastleLayoutKey(npc,out key))return;
            var data=npc.Data;
            store.RegisterNativeLayoutPerson(data.mapID,key,data.hash,npc.DisplayName,
                data.gender==Genders.Female?ResidentSex.Female:ResidentSex.Male,
                (int)data.race,data.factionID,data.billboardArchiveIndex,data.billboardRecordIndex);
        }
    }
}
