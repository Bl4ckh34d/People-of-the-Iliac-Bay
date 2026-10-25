using System;
using System.Globalization;
using DaggerfallWorkshop.Game.Items;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public static partial class NativeNpcInventoryBridge
    {
        // A native stock access adapter must revalidate shop ownership, source
        // LoadID, current price, range, capacity and the exact stock item here.
        public static bool TryCompleteActionPurchase(WorldStateStore store,NpcActionJob requested,double now,
            NativeNpcInventoryBinding buyer,ItemCollection actualStock,Func<bool> stockAccessIsValid,out string reason)
        {
            reason="The shop purchase, stock or buyer revision changed.";
            if(store==null || requested==null || buyer==null || actualStock==null) return false;
            var job=store.ReadNpcAction(requested.Id);ulong uid;
            if(job==null || job.Revision!=requested.Revision || job.ServiceCode!=NpcShopCommerce.ServiceCode ||
                job.Kind!=NpcActionKind.Purchase || buyer.PersonId!=job.ResidentId || buyer.Revision!=job.ExpectedInventoryRevision ||
                !ulong.TryParse(job.Target.ItemId,NumberStyles.None,CultureInfo.InvariantCulture,out uid)) return false;
            string failure=null;
            bool completed=store.TryCompleteNpcActionEffect(job.Id,job.Revision,now,()=>TryPurchase(store,buyer,actualStock,
                job.Target.PersonId,job.ExpectedTargetInventoryRevision,uid,job.Target.Quantity,job.ServicePrice,
                stockAccessIsValid,job.Id+":purchase",out failure));
            if(failure!=null) reason=failure;else if(completed) reason=null;
            return completed;
        }
    }
}
