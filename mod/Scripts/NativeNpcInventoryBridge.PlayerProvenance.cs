using System;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public static partial class NativeNpcInventoryBridge
    {
        private static void TryRecordExactPlayerGiftSource(WorldStateStore store,ItemCollection source,
            ItemCollection destination,NativeNpcInventoryBinding sourceBinding,
            NativeNpcInventoryBinding targetBinding,string sellerId,NpcActionJob receiptJob,
            string transactionId,ulong sourceUid,int quantity,int sourceCountBefore,
            DaggerfallUnityItem merged,DaggerfallUnityItem moved)
        {
            // Provenance projection never changes the completed native transfer.
            // A merged player stack has mixed/unknown history and remains unclassified.
            try
            {
                var game=GameManager.Instance;
                if(store==null || sourceBinding==null || targetBinding!=null || sellerId!=null ||
                    receiptJob!=null || merged!=null || moved==null || game==null ||
                    game.PlayerEntity==null || !ReferenceEquals(destination,game.PlayerEntity.Items) ||
                    !ReferenceEquals(source,sourceBinding.Items) ||
                    !ReferenceEquals(destination.GetItem(moved.UID),moved) ||
                    sourceBinding.Revision<1 || !transactionId.EndsWith(":transfer",StringComparison.Ordinal))return;
                string actionId=transactionId.Substring(0,transactionId.Length-":transfer".Length);
                var remaining=source.GetItem(sourceUid);
                store.TryRecordObservedNpcGiftPlayerItem(new PlayerItemProvenanceGiftReceipt {
                    ActionId=actionId,TransactionId=transactionId,SourceOwnerId=sourceBinding.PersonId,
                    SourceItemUid=sourceUid,PlayerItemUid=moved.UID,Quantity=quantity,
                    SourceQuantityBefore=sourceCountBefore,
                    SourceQuantityAfter=remaining==null?0:remaining.stackCount,
                    PlayerQuantityBefore=0,PlayerQuantityAfter=moved.stackCount,
                    SourceInventoryRevisionBefore=sourceBinding.Revision-1,
                    SourceInventoryRevisionAfter=sourceBinding.Revision,NativeTransferObserved=true });
            }
            catch(Exception)
            {
                // Missing provenance denies future personal sale; ownership stays native.
            }
        }
    }
}
