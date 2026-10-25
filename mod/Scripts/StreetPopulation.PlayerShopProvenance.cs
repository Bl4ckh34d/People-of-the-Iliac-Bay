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
            bool recorded=worldStore.TryRecordObservedNativeShopItem(new PlayerItemProvenanceNativeShopReceipt {
                ReceiptId = receiptId, ShopId = shopId, PlayerItemUid = uid,
                Quantity = quantity, BasketQuantityBefore = quantity, BasketQuantityAfter = 0,
                PlayerQuantityBefore = playerBefore, PlayerQuantityAfter = playerBefore + quantity,
                Price = price, PlayerGoldBefore = goldBefore, PlayerGoldAfter = goldAfter,
                NativeTransactionObserved = true }, stolen);
            if(!recorded)return;
            var item=game.PlayerEntity.Items.GetItem(uid);
            foreach(var actor in actors.Values)
                if(actor && actor.Resident.IsAlive && actor.gameObject.activeInHierarchy && !game.PlayerEntity.IsInvisible &&
                    SeesSceneEvent(actor,game.PlayerMotor.transform.position,20))
                    NativeSceneRecollection.Remember(worldStore,actor.Resident.Id,receiptId,
                        stolen?NpcSceneEventKind.Theft:NpcSceneEventKind.Shopping,
                        "I saw the player "+(stolen?"steal ":"buy ")+quantity+" "+item.LongName+" from this shop. I do not know the motive.",
                        stolen?-2:0,NpcDirectedRelationship.PlayerTargetId,NpcDirectedRelationship.PlayerTargetId);
            if(!game.PlayerEntity.IsInvisible)foreach(var witness in stationaryParticipants.Values)
                if(witness.Observer && witness.Observer.Sees(game.PlayerMotor.transform.position,20))
                    NativeSceneRecollection.Remember(worldStore,witness.Person.Id,receiptId,
                        stolen?NpcSceneEventKind.Theft:NpcSceneEventKind.Shopping,
                        "I saw the player "+(stolen?"steal ":"buy ")+quantity+" "+item.LongName+" from this shop. I do not know the motive.",
                        stolen?-2:0,NpcDirectedRelationship.PlayerTargetId,NpcDirectedRelationship.PlayerTargetId);
        }

        private void DisposePlayerShopProvenanceHook()
        {
            if (playerShopProvenanceHook == null) return;
            playerShopProvenanceHook.Dispose();
            playerShopProvenanceHook = null;
        }
    }
}
