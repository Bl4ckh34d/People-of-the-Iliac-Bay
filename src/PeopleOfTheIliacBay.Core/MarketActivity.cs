using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public enum MarketGoodsKind { None, Fish, Meat, Bread, Produce, HouseholdGoods, Weapons }
    public enum MarketPostRole { Unknown, Fishmonger, Butcher, Baker, Produce, HouseholdGoods, Weapons, Performer }

    public sealed class MarketVisitSlot
    {
        public readonly string Id, PostId;
        public readonly double X, Z;
        public MarketVisitSlot(string id, string postId, double x, double z) { Id=id;PostId=postId;X=x;Z=z; }
    }

    public static class MarketActivity
    {
        // Asset semantics from DFU's DefaultFlatsCatalog.json. A crate, sign,
        // hanging meat hook, light or reused person sprite is not sales evidence.
        public static MarketGoodsKind Goods(int archive,int record)
        {
            if(archive==211 && (record>=8 && record<=11 || record==41 || record==42))return MarketGoodsKind.Fish;
            if(archive==211 && record==40)return MarketGoodsKind.Meat;
            if(archive==211 && record==31)return MarketGoodsKind.Bread;
            if(archive==213 && (record==0 || record==1))return MarketGoodsKind.Produce;
            if(archive==207 && record>=0 && record<=16)return MarketGoodsKind.Weapons;
            if(archive==200 && record>=0 && record<=6 || archive==205 && record>=11 && record<=16 ||
                archive==211 && (record==23 || record==25) || archive==214 && record==12)return MarketGoodsKind.HouseholdGoods;
            return MarketGoodsKind.None;
        }
        public static bool Performer(int archive,int record)
        {return archive==182 && (record==5 || record==6 || record==49 || record==36 || record==37 || record==50 || record==51) || archive==184 && record==3;}
        public static MarketPostRole Role(MarketGoodsKind goods)
        {
            switch(goods)
            {
                case MarketGoodsKind.Fish:return MarketPostRole.Fishmonger;
                case MarketGoodsKind.Meat:return MarketPostRole.Butcher;
                case MarketGoodsKind.Bread:return MarketPostRole.Baker;
                case MarketGoodsKind.Produce:return MarketPostRole.Produce;
                case MarketGoodsKind.Weapons:return MarketPostRole.Weapons;
                case MarketGoodsKind.HouseholdGoods:return MarketPostRole.HouseholdGoods;
                default:return MarketPostRole.Unknown;
            }
        }
        public static MarketVisitSlot Nearest(IEnumerable<MarketVisitSlot> slots,double x,double z,string previousPost,Func<MarketVisitSlot,bool> available)
        {
            MarketVisitSlot best=null;double distance=double.MaxValue;
            foreach(var slot in slots)
            {
                if(slot.PostId==previousPost || !available(slot))continue;
                double dx=slot.X-x,dz=slot.Z-z,d=dx*dx+dz*dz;
                if(d<distance || d==distance && best!=null && string.CompareOrdinal(slot.Id,best.Id)<0){best=slot;distance=d;}
            }
            return best;
        }
        public static double DwellSeconds(uint personSeed,int day,bool show)
        {uint seed=StableSeed.Combine(personSeed,unchecked((uint)day),show?7189u:7391u,0);return show?240+seed%661:120+seed%361;}
    }

    public sealed class MarketDwell
    {
        public double ArrivedAt {get;private set;}=-1;
        private readonly double duration;
        public MarketDwell(double duration)
        {if(double.IsNaN(duration)||double.IsInfinity(duration)||duration<=0)throw new ArgumentOutOfRangeException(nameof(duration));this.duration=duration;}
        public bool Advance(double now,bool physicallyAtSlot)
        {
            if(!physicallyAtSlot){ArrivedAt=-1;return false;}
            if(ArrivedAt<0 || now<ArrivedAt)ArrivedAt=now;
            return now-ArrivedAt>=duration;
        }
    }
}
