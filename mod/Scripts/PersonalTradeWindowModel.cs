using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    // UI-only selection. It never owns or moves a native inventory item.
    public enum PersonalTradeSide { Player, Npc }

    public sealed class PersonalTradeLine
    {
        public PersonalTradeSide Side { get; private set; }
        public ulong ItemUid { get; private set; }
        public int Quantity { get; private set; }

        internal PersonalTradeLine(PersonalTradeSide side,ulong itemUid,int quantity)
        { Side=side;ItemUid=itemUid;Quantity=quantity; }
        internal void SetQuantity(int quantity) { Quantity=quantity; }
        internal PersonalTradeLine Copy() { return new PersonalTradeLine(Side,ItemUid,Quantity); }
    }

    // Source rows are observed values. Preview rows never enter an ItemCollection.
    public sealed class PersonalTradeSourceItem
    {
        public PersonalTradeSide Side { get; private set; }
        public ulong ItemUid { get; private set; }
        public int Quantity { get; private set; }
        public bool NativeGold { get; private set; }
        public PersonalTradeSourceItem(PersonalTradeSide side,ulong itemUid,int quantity,bool nativeGold)
        { Side=side;ItemUid=itemUid;Quantity=quantity;NativeGold=nativeGold; }
    }

    public sealed class PersonalTradePreviewRow
    {
        public PersonalTradeSide DisplaySide { get; private set; }
        public PersonalTradeSide SourceSide { get; private set; }
        public ulong ItemUid { get; private set; }
        public int Quantity { get; private set; }
        public bool Incoming { get; private set; }
        internal PersonalTradePreviewRow(PersonalTradeSide display,PersonalTradeSide source,ulong uid,int quantity,bool incoming)
        { DisplaySide=display;SourceSide=source;ItemUid=uid;Quantity=quantity;Incoming=incoming; }
    }

    public sealed class PersonalTradeStaging
    {
        private readonly List<PersonalTradeLine> lines=new List<PersonalTradeLine>();
        public IList<PersonalTradeLine> Lines { get { return lines.AsReadOnly(); } }

        public bool TrySetQuantity(PersonalTradeSide side,ulong itemUid,int available,int quantity,bool nativeGold)
        {
            if(nativeGold || itemUid==0 || available<0 || quantity<0 || quantity>available)return false;
            int index=lines.FindIndex(line=>line.Side==side && line.ItemUid==itemUid);
            if(quantity==0)
            {
                if(index>=0)lines.RemoveAt(index);
                return true;
            }
            if(index<0)lines.Add(new PersonalTradeLine(side,itemUid,quantity));
            else lines[index].SetQuantity(quantity);
            return true;
        }

        public void Clear() { lines.Clear(); }
        public List<PersonalTradeLine> Snapshot()
        {
            var copy=new List<PersonalTradeLine>(lines.Count);
            foreach(var line in lines)copy.Add(line.Copy());
            return copy;
        }

        public List<PersonalTradePreviewRow> Project(IEnumerable<PersonalTradeSourceItem> source)
        {
            var preview=new List<PersonalTradePreviewRow>();if(source==null)return preview;
            foreach(var item in source)
            {
                if(item==null || item.NativeGold || item.ItemUid==0 || item.Quantity<1)continue;
                int marked=0;foreach(var line in lines)
                    if(line.Side==item.Side && line.ItemUid==item.ItemUid){marked=line.Quantity;break;}
                if(marked<0 || marked>item.Quantity)continue;
                int remaining=item.Quantity-marked;
                if(remaining>0)preview.Add(new PersonalTradePreviewRow(item.Side,item.Side,item.ItemUid,remaining,false));
                if(marked>0)preview.Add(new PersonalTradePreviewRow(item.Side==PersonalTradeSide.Player?PersonalTradeSide.Npc:PersonalTradeSide.Player,
                    item.Side,item.ItemUid,marked,true));
            }
            return preview;
        }
    }

    public sealed class PersonalTradeWindowProposal
    {
        public string NpcId { get; private set; }
        public long StoreEpoch { get; private set; }
        public int PlayerOfferGold { get; private set; }
        // False denotes an NPC baseline price; true denotes an actual player
        // bid. This intentionally keeps a signed explicit zero representable.
        public bool HasPlayerOffer { get; private set; }
        public IList<PersonalTradeLine> Lines { get; private set; }

        private PersonalTradeWindowProposal() { }

        public static PersonalTradeWindowProposal Create(string npcId,long storeEpoch,PersonalTradeStaging staging,int playerOfferGold)
        { return Create(npcId,storeEpoch,staging,playerOfferGold,true); }

        public static PersonalTradeWindowProposal Create(string npcId,long storeEpoch,PersonalTradeStaging staging,
            int playerOfferGold,bool hasPlayerOffer)
        {
            if(string.IsNullOrWhiteSpace(npcId) || storeEpoch<0 || staging==null ||
                playerOfferGold<(int)-NpcInventoryLimits.MaximumWalletGold || playerOfferGold>NpcInventoryLimits.MaximumWalletGold)
                return null;
            return new PersonalTradeWindowProposal {NpcId=npcId,StoreEpoch=storeEpoch,PlayerOfferGold=playerOfferGold,
                HasPlayerOffer=hasPlayerOffer,
                Lines=staging.Snapshot().AsReadOnly()};
        }
    }
}
