using System;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Native loot UI over the existing exact NPC inventory, used only where the
    // city corpse renderer is unavailable. No second item or corpse ledger.
    internal sealed class NativeStationaryCorpse : MonoBehaviour,IPlayerActivable
    {
        private WorldStateStore store;
        private NativeNpcInventoryBinding binding;
        private object epoch;
        private float nextCapture;
        internal void Bind(WorldStateStore world,string person)
        {
            string reason;store=world;epoch=world.CaptureNpcVitalsWriteEpoch();
            if(!NativeNpcInventoryBridge.TryOpen(world,person,null,0,out binding,out reason) ||
                !NativeNpcInventoryBridge.TryMaterializeCorpseWallet(world,binding,out reason))throw new InvalidOperationException(reason);
            var loot=gameObject.AddComponent<DaggerfallLoot>();loot.ContainerType=LootContainerTypes.CorpseMarker;
            while(binding.Items.Count>0){var item=binding.Items.GetItem(0);binding.Items.RemoveItem(item);loot.Items.AddItem(item,noStack:true);}binding.Items=loot.Items;
            var collider=GetComponent<BoxCollider>()??gameObject.AddComponent<BoxCollider>();collider.isTrigger=true;
        }
        public void Activate(RaycastHit hit)
        {
            if(hit.distance>3 || !store.IsNpcVitalsWriteEpochCurrent(epoch))return;
            DaggerfallUI.Instance.InventoryWindow.LootTarget=GetComponent<DaggerfallLoot>();DaggerfallUI.UIManager.PushWindow(DaggerfallUI.Instance.InventoryWindow);
        }
        private void Update(){if(Time.unscaledTime<nextCapture)return;nextCapture=Time.unscaledTime+0.5f;Capture();}
        internal void Capture(){if(binding==null || !store.IsNpcVitalsWriteEpochCurrent(epoch))return;string reason;if(!NativeNpcInventoryBridge.TryCapture(store,binding,out reason))throw new InvalidOperationException(reason);}
    }
}
