namespace PeopleOfTheIliacBay.Core
{
    public static class NpcHomeContainerLifecycle
    {
        // DaggerfallLoot.stockedDate is persisted as an Int32 and compared only
        // with CreateStockedDate's calendar value. Reserve Int32.MaxValue for a
        // chest converted to persistent household storage by NPC use or deposit.
        public const int PersistentStorageStockedDate=int.MaxValue;

        // DFU's native house stocker clears the collection before generating
        // goods. Never replace existing contents, even when the date is stale.
        // Empty untouched containers may still receive their native stock.
        public static bool ShouldNativeStock(bool authorizedHome,bool houseContainer,bool playerOwned,bool customDrop,
            bool corpse,bool questAction,int itemCount,int stockedDate,int today)
        {
            return authorizedHome && houseContainer && !playerOwned && !customDrop && !corpse && !questAction &&
                itemCount==0 && stockedDate>=0 && today>0 && !IsPersistentStorage(stockedDate) && stockedDate<today;
        }

        // Caller has already verified permanent-home ownership. Once stock is
        // exposed for NPC use, protect its exact contents from any later native
        // player-activation reset, even if its stock date is current or future.
        public static bool ShouldProtectExistingContents(int itemCount,int stockedDate,int today)
        {return itemCount>0 && today>0 && !IsPersistentStorage(stockedDate);}

        // After an NPC takes stock, the chest is household storage. Its saved
        // sentinel prevents both later clearing of leftovers and free refills
        // after depletion. Also preserves opaque zero-date contents on save.
        public static int MarkUsed(int stockedDate,int today)
        {return PersistentStorageStockedDate;}

        public static int MarkPersistentStorage()
        {return PersistentStorageStockedDate;}

        public static bool IsPersistentStorage(int stockedDate)
        {return stockedDate==PersistentStorageStockedDate;}

        public static int RestoreStockDateAfterFailedDeposit(int previousStockDate,int currentStockDate,bool completed)
        {return completed?currentStockDate:previousStockDate;}
    }
}
