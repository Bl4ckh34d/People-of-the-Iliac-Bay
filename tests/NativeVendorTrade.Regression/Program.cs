using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.DFU;

internal static class Program
{
    static void Need(bool value,string reason) { if(!value)throw new InvalidOperationException(reason); }
    static int Main()
    {
        try
        {
            var source=new Dictionary<ulong,int>{{41,8},{42,8}};
            var basket=new NativeVendorTradeBasket(source);
            Need(basket.Set(41,3),"Cannot stage partial native stack.");
            Need(basket.Set(42,8),"Cannot stage second exact UID with identical goods.");
            Need(source[41]==8 && source[42]==8,"Preview mutated source quantities.");
            var snapshot=basket.Snapshot();
            Need(snapshot.Count==2 && snapshot[0].ItemUid==41 && snapshot[0].Quantity==3 && snapshot[1].ItemUid==42,
                "Selection lost original UID or deterministic line order.");
            Need(!basket.Set(41,9) && !basket.Set(99,1) && !basket.Set(41,-1),"Invalid source or quantity admitted.");
            Need(basket.Quantity(41)==3,"Rejected edit changed selection.");
            snapshot[0].Quantity=1;
            Need(basket.Quantity(41)==3,"Published snapshot changes commit selection.");
            source[41]=1;
            Need(basket.Set(41,7),"External source changed detached initial limits.");
            Need(basket.Set(41,0) && basket.Snapshot().Count==1,"Zero fails to remove line.");
            basket.Clear();
            Need(basket.Snapshot().Count==0 && basket.Quantity(42)==0,"Clear retained purchase authority.");
            Console.WriteLine("PASS exact native UID, partial quantities, immutable preview, bounds, clear (9 checks)");
            return 0;
        }
        catch(Exception error) {Console.Error.WriteLine("FAIL "+error.Message);return 1;}
    }
}
