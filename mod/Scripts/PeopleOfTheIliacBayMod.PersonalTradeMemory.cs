using System;
using System.Collections.Generic;
using System.Globalization;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private static string CapturePersonalTradeMemory(PersonalTradeWindowProposal proposal,ItemCollection player,ItemCollection npc,int expected)
        {
            var bought=new List<string>();var sold=new List<string>();
            foreach(var line in proposal.Lines)
            {
                var item=(line.Side==PersonalTradeSide.Npc?npc:player).GetItem(line.ItemUid);
                string label=item==null?"item":item.LongName;
                label=label.Replace('\r',' ').Replace('\n',' ').Replace('\t',' ');
                (line.Side==PersonalTradeSide.Npc?bought:sold).Add(line.Quantity.ToString(CultureInfo.InvariantCulture)+" "+label);
            }
            return PersonalTradeRecollection.Completed(bought,sold,proposal.PlayerOfferGold,expected);
        }
        private static void RememberPersonalTrade(WorldStateStore source,string person,string eventId,string observation,int valence=0)
        {
            // Failure to write a recollection must never turn a completed native
            // transfer into an apparent transaction failure or a retry.
            try
            {
                var game=GameManager.Instance;
                source.RememberSceneEvent(person,eventId,NpcSceneEventKind.Trade,ConversationActionTime(),
                    game.PlayerGPS.CurrentLocation.MapTableData.MapId,
                    game.PlayerEnterExit.IsPlayerInsideBuilding?game.PlayerEnterExit.BuildingDiscoveryData.buildingKey:-1,observation,valence,
                    person,NpcDirectedRelationship.PlayerTargetId);
            }
            catch(Exception error){Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_TRADE_MEMORY_FAILED: "+error.Message);}
        }
        private static Action<bool,string> PersonalTradeCloseMemory(WorldStateStore source,string person)
        {
            object epoch=source.CaptureNpcVitalsWriteEpoch();string id="trade-cancel:"+Guid.NewGuid().ToString("N");
            return (completed,reason)=>
            {
                if(!completed && source.IsNpcVitalsWriteEpochCurrent(epoch))
                    RememberPersonalTrade(source,person,id,PersonalTradeRecollection.Cancelled);
            };
        }
    }
}
