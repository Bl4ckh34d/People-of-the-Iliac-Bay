using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.DFU
{
    internal sealed class NativeVendorTradeLine
    {
        internal ulong ItemUid;
        internal int Quantity;
    }

    // Detached quantities always refer to the original native source UID.
    // Native preview splitting/merging must never become ownership authority.
    internal sealed class NativeVendorTradeBasket
    {
        private readonly Dictionary<ulong,int> limits;
        private readonly SortedDictionary<ulong,int> selected=new SortedDictionary<ulong,int>();
        internal NativeVendorTradeBasket(IDictionary<ulong,int> stock)
        {
            if(stock==null)throw new ArgumentNullException(nameof(stock));
            limits=new Dictionary<ulong,int>(stock);
            foreach(var entry in limits)if(entry.Key==0 || entry.Value<1)throw new ArgumentException("Invalid native stock.");
        }
        internal bool Set(ulong uid,int quantity)
        {
            int limit;
            if(!limits.TryGetValue(uid,out limit) || quantity<0 || quantity>limit)return false;
            if(quantity==0)selected.Remove(uid);else selected[uid]=quantity;
            return true;
        }
        internal int Quantity(ulong uid) { int value;return selected.TryGetValue(uid,out value)?value:0; }
        internal IList<NativeVendorTradeLine> Snapshot()
        {
            var result=new List<NativeVendorTradeLine>();
            foreach(var line in selected)result.Add(new NativeVendorTradeLine {ItemUid=line.Key,Quantity=line.Value});
            return result;
        }
        internal void Clear() { selected.Clear(); }
    }
}
