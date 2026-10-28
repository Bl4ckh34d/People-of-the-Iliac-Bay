using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Game.Utility;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal delegate bool NativeVendorPurchase(IList<NativeVendorTradeLine> lines,int price,out string reason);

    // Native DFU commerce presentation and price confirmation. Its staging
    // collections contain detached previews; the existing inventory bridge
    // owns every real UID, wallet change and durable receipt.
    internal sealed class NativeVendorTradeWindow : DaggerfallTradeWindow
    {
        private static readonly FieldInfo nativeBuilding=typeof(DaggerfallTradeWindow).GetField("buildingDiscoveryData",BindingFlags.Instance|BindingFlags.NonPublic);
        private readonly ItemCollection originals=new ItemCollection();
        private readonly NativeVendorTradeBasket selection;
        private readonly NativeVendorPurchase purchase;
        private readonly string personId;
        private readonly Action<bool,string> closed;
        private bool completed,notified;

        internal NativeVendorTradeWindow(IUserInterfaceManager manager,string personId,ItemCollection stock,
            Func<DaggerfallUnityItem,bool> allowed,NativeVendorPurchase purchase,Action<bool,string> closed)
            :base(manager,null,WindowModes.Buy)
        {
            this.purchase=purchase??throw new ArgumentNullException(nameof(purchase));this.closed=closed;this.personId=personId;
            var limits=new Dictionary<ulong,int>();
            var records=new List<ItemData_v1>();
            for(int index=0;index<stock.Count;index++)
            {
                var item=stock.GetItem(index);if(!allowed(item))continue;
                limits.Add(item.UID,item.stackCount);records.Add(item.GetSaveData());
            }
            originals.DeserializeItems(records.ToArray());
            selection=new NativeVendorTradeBasket(limits);RebuildPreview();
        }

        protected override void SetupActionButtons()
        {
            base.SetupActionButtons();
            // Native stealing transfers previews immediately and bypasses the
            // vendor ledger. Street theft remains the existing pickpocket path.
            var steal=NativeButton("stealButton");steal.Enabled=false;steal.Hotkey=HotkeySequence.None;
            if(wagonButton!=null){wagonButton.Enabled=false;wagonButton.Hotkey=HotkeySequence.None;}
            NativeButton("clearButton").OnMouseClick+=(sender,position)=>{selection.Clear();RebuildPreview();Refresh(false);};
        }
        private Button NativeButton(string name)
        {
            var field=typeof(DaggerfallTradeWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic);
            var button=field==null?null:field.GetValue(this) as Button;
            if(button==null)throw new MissingFieldException("Native trade control is unavailable: "+name);
            return button;
        }

        protected override void Setup()
        {
            base.Setup();
            // DFU creates tab buttons during the first Update, after OnPush.
            SelectTabPage(originals.Count>0 && originals.GetItem(0).ItemGroup==ItemGroups.Weapons?TabPages.WeaponsAndArmor:TabPages.ClothingAndMisc);
        }

        public override void OnPush()
        {
            NativeVendorTradeAdapter.SetActive(this,personId);
            if(GameManager.Instance.PlayerEnterExit.IsPlayerInsideBuilding){base.OnPush();return;}
            var racial=GameManager.Instance.PlayerEffectManager.GetRacialOverrideEffect();string message;
            if(racial!=null && racial.GetSuppressInventory(out message))
            {CloseWindow();if(!string.IsNullOrEmpty(message))DaggerfallUI.MessageBox(message);return;}
            // A market has no building discovery record. Native quality ten
            // gives its neutral shop price without an obsolete-save warning.
            if(nativeBuilding==null)throw new MissingFieldException("Native trade building data is unavailable.");
            nativeBuilding.SetValue(this,new PlayerGPS.DiscoveredBuilding {quality=10});
            localItems=PlayerEntity.Items;remoteItems=MerchantItems;remoteTargetType=RemoteTargetTypes.Merchant;
            if(IsSetup)SelectTabPage(originals.Count>0 && originals.GetItem(0).ItemGroup==ItemGroups.Weapons?TabPages.WeaponsAndArmor:TabPages.ClothingAndMisc);
            Refresh();
        }

        public override void OnPop()
        {
            NativeVendorTradeAdapter.ClearActive(this);
            if(!notified){notified=true;closed?.Invoke(completed,completed?null:"Trade cancelled.");}
        }

        protected override void RemoteItemListScroller_OnItemClick(DaggerfallUnityItem item,ActionModes mode)
        {
            if(mode==ActionModes.Info){ShowInfoPopup(item);return;}
            if(mode!=ActionModes.Select && mode!=ActionModes.Remove)return;
            var original=originals.GetItem(item.UID);if(original==null)return;
            if(original.stackCount>1 && (Input.GetKey(KeyCode.LeftControl)||Input.GetKey(KeyCode.RightControl)))
            {
                var box=new DaggerfallInputMessageBox(uiManager,this);
                box.SetTextBoxLabel("Quantity to buy:");box.TextBox.Numeric=true;box.TextBox.MaxCharacters=8;
                box.TextBox.Text=original.stackCount.ToString(CultureInfo.InvariantCulture);
                box.OnGotUserInput+=(sender,input)=>{int quantity;if(int.TryParse(input,NumberStyles.None,CultureInfo.InvariantCulture,out quantity))SetQuantity(original.UID,quantity);};
                box.Show();return;
            }
            SetQuantity(original.UID,original.stackCount);
        }

        protected override void LocalItemListScroller_OnItemClick(DaggerfallUnityItem item,ActionModes mode)
        {
            if(mode==ActionModes.Info){ShowInfoPopup(item);return;}
            if(basketItems.Contains(item) && (mode==ActionModes.Select || mode==ActionModes.Remove))SetQuantity(item.UID,0);
        }
        protected override void RemoteItemListScroller_OnItemMiddleClick(DaggerfallUnityItem item) { }
        protected override void LocalItemListScroller_OnItemMiddleClick(DaggerfallUnityItem item) { }

        private void SetQuantity(ulong uid,int quantity)
        {if(selection.Set(uid,quantity)){RebuildPreview();Refresh(false);}}

        private void RebuildPreview()
        {
            var unselected=new List<ItemData_v1>();var selected=new List<ItemData_v1>();
            for(int index=0;index<originals.Count;index++)
            {
                var item=originals.GetItem(index);int quantity=selection==null?0:selection.Quantity(item.UID);
                if(quantity>0){var data=item.GetSaveData();data.stackCount=quantity;selected.Add(data);}
                if(quantity<item.stackCount){var data=item.GetSaveData();data.stackCount=item.stackCount-quantity;unselected.Add(data);}
            }
            MerchantItems.Clear();MerchantItems.DeserializeItems(unselected.ToArray());
            basketItems.Clear();basketItems.DeserializeItems(selected.ToArray());
        }

        protected override void ConfirmTrade_OnButtonClick(DaggerfallMessageBox sender,DaggerfallMessageBox.MessageBoxButtons button)
        {
            if(button!=DaggerfallMessageBox.MessageBoxButtons.Yes){CloseWindow();return;}
            int price=GetTradePrice();var lines=selection.Snapshot();string reason=null;
            if(completed || !purchase(lines,price,out reason))
            {sender.CloseWindow();DaggerfallUI.MessageBox(completed?"This purchase is already complete.":reason??"The vendor's stock changed.");return;}
            completed=true;int quantity=0;foreach(var line in lines)quantity+=line.Quantity;
            selection.Clear();basketItems.Clear();
            RaiseOnTradeHandler(quantity,price);
            DaggerfallUI.Instance.PlayOneShot(SoundClips.GoldPieces);CloseWindow();
        }
    }
}
