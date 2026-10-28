using System;
using PeopleOfTheIliacBay.Core;
namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcDailyPaymentFixture
    {
        internal static bool Commit(WorldStateStore store,NpcServiceContractWriteTicket ticket,
            NpcServiceContractLedger candidate,NpcServiceTransferReceipt payment,out string error)
        {
            var inventory=store.ReadNpcInventory(payment.ToOwnerId);
            if(inventory==null)
            {
                if(!store.InitializeNpcInventory(payment.ToOwnerId,0,Array.Empty<NpcInventoryItem>()))
                    throw new InvalidOperationException("Daily payment fixture cannot initialize its NPC wallet.");
                inventory=store.ReadNpcInventory(payment.ToOwnerId);
            }
            var credit=new NpcInventoryUpdate {PersonId=payment.ToOwnerId,ExpectedRevision=inventory.Revision,
                WalletGold=inventory.WalletGold+payment.Amount,Items=inventory.Items};
            return store.TryCommitNpcServiceSettlement(ticket,candidate,credit,new[]{payment},out error);
        }
    }
}
