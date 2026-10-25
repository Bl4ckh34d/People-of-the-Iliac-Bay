using System;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private IDisposable playerShopProvenanceHook;

        private void InstallPlayerShopProvenanceHook()
        {
            try
            {
                var type = Type.GetType("PeopleOfTheIliacBay.QuestHooks.NativePlayerShopProvenanceHooks, PeopleOfTheIliacBay.QuestHooks", true);
                playerShopProvenanceHook = (IDisposable)Activator.CreateInstance(type, new object[] {
                    new Action<string, string, ulong, int, int, int, int, int, bool>(RecordNativeShopOutcome) });
            }
            catch (Exception error)
            {
                playerShopProvenanceHook = null;
                UnityEngine.Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_SHOP_PROVENANCE_UNAVAILABLE: " + error);
            }
        }

        private void RecordNativeShopOutcome(string receiptId, string shopId, ulong uid,
            int quantity, int playerBefore, int price, int goldBefore, int goldAfter, bool stolen)
        {
            var game = GameManager.Instance;
            if (worldStore == null || game == null || game.PlayerEntity == null ||
                !game.PlayerEntity.Items.Contains(uid)) return;
            worldStore.TryRecordObservedNativeShopItem(new PlayerItemProvenanceNativeShopReceipt {
                ReceiptId = receiptId, ShopId = shopId, PlayerItemUid = uid,
                Quantity = quantity, BasketQuantityBefore = quantity, BasketQuantityAfter = 0,
                PlayerQuantityBefore = playerBefore, PlayerQuantityAfter = playerBefore + quantity,
                Price = price, PlayerGoldBefore = goldBefore, PlayerGoldAfter = goldAfter,
                NativeTransactionObserved = true }, stolen);
        }

        private void DisposePlayerShopProvenanceHook()
        {
            if (playerShopProvenanceHook == null) return;
            playerShopProvenanceHook.Dispose();
            playerShopProvenanceHook = null;
        }
    }
}
