using System;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // The native merchant Talk/service popup, with a market's actual Buy action.
    internal sealed class NativeMarketServiceWindow : DaggerfallMerchantServicePopupWindow
    {
        private readonly StaticNPC seller;
        private readonly string personId;
        private readonly Action buy,talk;
        internal NativeMarketServiceWindow(IUserInterfaceManager manager,StaticNPC seller,string personId,Action buy,Action talk)
            :base(manager,seller,Services.Sell){this.seller=seller;this.buy=buy;this.talk=talk;this.personId=personId;}
        public override void OnPush(){base.OnPush();NativeVendorTradeAdapter.SetActive(this,personId);}
        public override void OnPop(){NativeVendorTradeAdapter.ClearActive(this);base.OnPop();}
        protected override void Setup(){base.Setup();serviceLabel.Text="Buy";}
        protected override void ServiceButton_OnMouseClick(BaseScreenComponent sender,Vector2 position)
        {DaggerfallUI.Instance.PlayOneShot(SoundClips.ButtonClick);CloseWindow();buy();}
        protected override void TalkButton_OnMouseClick(BaseScreenComponent sender,Vector2 position)
        {DaggerfallUI.Instance.PlayOneShot(SoundClips.ButtonClick);CloseWindow();talk();GameManager.Instance.TalkManager.TalkToStaticNPC(seller);}
    }
}
