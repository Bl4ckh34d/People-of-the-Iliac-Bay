using System.Globalization;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static class NativeExteriorNpcIdentity
    {
        // Same position-bound identity already used by exterior crime witnesses.
        internal static int Hash(StaticNPC npc,Transform directory)
        {
            var anchor=npc.GetComponent<NativeExteriorNpcIdentityAnchor>();
            if(anchor && anchor.Directory==directory)return anchor.Hash;
            Vector3 local=directory.InverseTransformPoint(npc.transform.position);
            return unchecked((int)StableSeed.Combine((uint)npc.Data.hash,
                (uint)Mathf.RoundToInt(local.x*10),(uint)Mathf.RoundToInt(local.z*10),0));
        }

        // Bind before the authored body first leaves its work post. Every
        // existing identity consumer then resolves the same saved native person.
        internal static int Anchor(StaticNPC npc,Transform directory)
        {
            int hash=Hash(npc,directory);
            var anchor=npc.GetComponent<NativeExteriorNpcIdentityAnchor>();
            if(!anchor)anchor=npc.gameObject.AddComponent<NativeExteriorNpcIdentityAnchor>();
            anchor.Directory=directory;anchor.Hash=hash;
            return hash;
        }

        internal static bool TryRead(StaticNPC npc,out int settlement,out int hash)
        {
            settlement=-1;hash=0;
            var game=GameManager.Instance;
            var anchored=npc?npc.GetComponent<NativeExteriorNpcIdentityAnchor>():null;
            if(npc && npc.gameObject.activeInHierarchy && anchored && anchored.Directory && game!=null &&
                game.PlayerGPS.HasCurrentLocation && !game.PlayerEnterExit.IsPlayerInsideDungeon &&
                !npc.IsChildNPC && !npc.GetComponent<QuestResourceBehaviour>() && !npc.GetComponent<DaggerfallAction>())
            {
                var origin=anchored.Directory.GetComponent<BuildingDirectory>();
                if(origin && origin.MapID==game.PlayerGPS.CurrentMapID)
                {settlement=origin.MapID;hash=anchored.Hash;return true;}
            }
            if(!npc || !npc.gameObject.activeInHierarchy || npc.IsChildNPC ||
                npc.GetComponent<QuestResourceBehaviour>() || npc.GetComponent<DaggerfallAction>() ||
                game==null || game.PlayerEnterExit.IsPlayerInside || !game.PlayerGPS.HasCurrentLocation ||
                !RDBLayout.IsNPCFlat(npc.Data.billboardArchiveIndex))return false;
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            if(!directory || directory.MapID!=game.PlayerGPS.CurrentMapID ||
                !npc.transform.IsChildOf(directory.transform))return false;
            settlement=directory.MapID;hash=Hash(npc,directory.transform);return true;
        }

        internal static string PersonId(int settlement,int hash)
        { return "npc:"+settlement.ToString(CultureInfo.InvariantCulture)+":-1:"+hash.ToString(CultureInfo.InvariantCulture); }

        internal static bool IsMerchant(StaticNPC npc)
        {
            var game=GameManager.Instance;
            FactionFile.FactionData faction;
            return npc && game!=null && game.PlayerEntity!=null &&
                !FactionFile.CustomFactions.ContainsKey(npc.Data.factionID) &&
                game.PlayerEntity.FactionData.GetFactionData(npc.Data.factionID,out faction) &&
                faction.sgroup==(int)FactionFile.SocialGroups.Merchants;
        }
    }

    internal sealed class NativeExteriorNpcIdentityAnchor : MonoBehaviour
    {
        internal Transform Directory;
        internal int Hash;
    }
}
