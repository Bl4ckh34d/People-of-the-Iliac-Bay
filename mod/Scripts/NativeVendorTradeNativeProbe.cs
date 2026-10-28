using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Disposable fixture only. Drives the same native popup, detached preview,
    // confirmation and exact inventory bridge as the production player flow.
    internal static class NativeVendorTradeNativeProbe
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private static void Need(bool value,string reason){if(!value)throw new InvalidOperationException(reason);}
        internal static IEnumerator RunShopRoutingFixture(PeopleOfTheIliacBayMod host,StreetPopulation streets,string character)
        {
            Need(Environment.GetEnvironmentVariable("POTIB_ROADMAP_PROBE")=="native-shop-route" &&
                host && streets && NativeProbeText.IsTestCharacter(character),"Shop routing requires its explicit owned fixture.");
            var game=GameManager.Instance;
            Need(game!=null && game.IsReady && game.PlayerEntity.Name==character && game.PlayerGPS.HasCurrentLocation,
                "Native shop routing fixture is not ready.");
            DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
            if(game.PlayerEnterExit.IsPlayerInside)
            {
                game.PlayerEnterExit.TransitionExterior();float deadline=Time.realtimeSinceStartup+60;
                while(game.PlayerEnterExit.IsPlayerInside && Time.realtimeSinceStartup<deadline)yield return null;
                Need(!game.PlayerEnterExit.IsPlayerInside,"Shop routing could not reach the actual exterior.");
            }
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            clock.RaiseTime((float)((12*3600-clock.ToSeconds()%86400+86400)%86400));
            var store=(WorldStateStore)typeof(StreetPopulation).GetField("worldStore",Private).GetValue(streets);
            Need(store!=null,"Native shop routing has no actual world store.");
            yield return VerifyShopRouting(host,store,game);
        }
        internal static IEnumerator RunFixture(PeopleOfTheIliacBayMod host,StreetPopulation streets,string character)
        {
            Need(Environment.GetEnvironmentVariable("POTIB_ROADMAP_PROBE")=="native-vendor-trade" &&
                host && streets && character!=null && NativeProbeText.IsTestCharacter(character),"Native vendor test requires its owned disposable fixture.");
            var game=GameManager.Instance;
            Need(game!=null && game.IsReady && game.PlayerGPS.HasCurrentLocation,"Native vendor fixture is not ready.");
            DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
            if(game.PlayerEnterExit.IsPlayerInside){game.PlayerEnterExit.TransitionExterior();float exit=Time.realtimeSinceStartup+30;
                while(game.PlayerEnterExit.IsPlayerInside && Time.realtimeSinceStartup<exit)yield return null;}
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            clock.RaiseTime((float)((12*3600-clock.ToSeconds()%86400+86400)%86400));
            for(int frame=0;frame<20;frame++)yield return null;
            var store=(WorldStateStore)typeof(StreetPopulation).GetField("worldStore",Private).GetValue(streets);
            var catalog=NativeMarketCatalog.For(game.StreamingWorld.GetCurrentBuildingDirectory());
            Need(catalog!=null,"No real native market catalog is available.");
            NativeMarketPost post=null;
            foreach(var candidate in catalog.Posts)
            {
                FactionFile.FactionData faction;
                if(candidate.Available && candidate.Role!=MarketPostRole.Unknown && candidate.Role!=MarketPostRole.Performer &&
                    candidate.Goods.Count>0 && game.PlayerEntity.FactionData.GetFactionData(candidate.Seller.Data.factionID,out faction) &&
                    DispositionPricing.AllowsPaidService(game.TalkManager.GetReactionToPlayer(faction))){post=candidate;break;}
            }
            Need(post!=null,"No actual serving market vendor with native goods was found.");
            int originalGold=game.PlayerEntity.GoldPieces;game.PlayerEntity.GoldPieces=5000;
            try
            {
                game.TalkManager.TalkToStaticNPC(post.Seller,false);yield return null;
                float menuDeadline=Time.realtimeSinceStartup+5;
                while(!(DaggerfallUI.UIManager.TopWindow is NativeMarketServiceWindow) && Time.realtimeSinceStartup<menuDeadline)yield return null;
                var menu=DaggerfallUI.UIManager.TopWindow as NativeMarketServiceWindow;
                Need(menu!=null,"Actual market activation did not open the native Buy/Talk menu.");
                yield return VerifyUiInput(menu,"buy-menu");
                typeof(NativeMarketServiceWindow).GetMethod("ServiceButton_OnMouseClick",Private).Invoke(menu,new object[]{null,Vector2.zero});
                yield return null;
                var first=DaggerfallUI.UIManager.TopWindow as NativeVendorTradeWindow;
                Need(first!=null,"Native market Buy did not open the DFU trade window.");
                yield return VerifyUiInput(first,"buy-trade");
                var person=store.NativePersonInSettlement(game.PlayerGPS.CurrentMapID,
                    (string)typeof(PeopleOfTheIliacBayMod).GetMethod("StaticConversationPersonId",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{post.Seller}));
                NativeVendorTradeStock stock;string reason;
                Need(NativeVendorTradeAdapter.TryOpenMarketStock(store,post.Seller,person,out stock,out reason),reason);
                NativeMarketGoodsItem goods=null;
                for(int index=0;index<stock.Items.Count;index++)
                {var item=stock.Items.GetItem(index) as NativeMarketGoodsItem;if(item!=null && item.stackCount>1 && stock.IsSaleItem(item)){goods=item;break;}}
                Need(goods!=null,"Actual vendor stock has no supported partial goods stack.");
                int count=goods.stackCount;ulong uid=goods.UID;int wallet=store.ReadNpcInventory(person.Id).WalletGold;
                long revision=stock.Seller.Revision;int playerCount=game.PlayerEntity.Items.Count;
                Set(first,uid,1);
                Need(goods.stackCount==count && stock.Seller.Revision==revision && game.PlayerEntity.Items.Count==playerCount &&
                    game.PlayerEntity.GoldPieces==5000 && store.ReadNpcInventory(person.Id).WalletGold==wallet,"Native preview changed real ownership or gold.");
                first.CloseWindow();yield return null;
                yield return VerifyHudInput("cancel");
                Need(goods.stackCount==count && store.ReadNpcInventory(person.Id).Revision==revision && game.PlayerEntity.GoldPieces==5000,
                    "Cancelling native market trade changed stock or wallets.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_VENDOR_CANCEL_PASS: person="+person.Id+"; uid="+uid+"; count="+count);

                game.TalkManager.TalkToStaticNPC(post.Seller,false);yield return null;
                menuDeadline=Time.realtimeSinceStartup+5;
                while(!(DaggerfallUI.UIManager.TopWindow is NativeMarketServiceWindow) && Time.realtimeSinceStartup<menuDeadline)yield return null;
                menu=DaggerfallUI.UIManager.TopWindow as NativeMarketServiceWindow;Need(menu!=null,"Market menu did not reopen.");
                typeof(NativeMarketServiceWindow).GetMethod("TalkButton_OnMouseClick",Private).Invoke(menu,new object[]{null,Vector2.zero});
                float until=Time.realtimeSinceStartup+20;
                DaggerfallTalkWindow talk=null;
                while(Time.realtimeSinceStartup<until)
                {
                    talk=DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
                    if(talk!=null && talk.IsSetup && (bool)typeof(PeopleOfTheIliacBayMod).GetField("conversationSessionOpen",Private).GetValue(host) &&
                        ReferenceEquals(typeof(PeopleOfTheIliacBayMod).GetField("memoryWindow",Private).GetValue(host),talk))break;
                    yield return null;
                }
                Need(talk!=null && DaggerfallUI.UIManager.TopWindow==talk,"Native Talk action was trapped by the market menu.");
                PersonalTradeNativeProbe.CaptureTradeContext(host,talk,"May I buy your goods?");
                var args=new object[]{talk,null};
                Need((bool)typeof(PeopleOfTheIliacBayMod).GetMethod("TryOpenConversationPersonalTradeWindow",Private).Invoke(host,args),"Semantic trade handoff refused: "+args[1]);
                yield return null;
                var window=DaggerfallUI.UIManager.TopWindow as NativeVendorTradeWindow;
                Need(window!=null && !(DaggerfallUI.UIManager.TopWindow is PersonalTradeWindow),"Market dialogue opened personal barter instead of native commerce.");
                yield return VerifyUiInput(window,"talk-trade-reopen");
                Set(window,uid,1);int price=(int)typeof(DaggerfallTradeWindow).GetMethod("GetTradePrice",Private).Invoke(window,null);
                var beforeIds=new HashSet<ulong>();for(int index=0;index<game.PlayerEntity.Items.Count;index++)beforeIds.Add(game.PlayerEntity.Items.GetItem(index).UID);
                ShowConfirmation(window);yield return null;
                var confirmation=DaggerfallUI.UIManager.TopWindow as DaggerfallMessageBox;
                Need(confirmation!=null,"Native trade did not present its real confirmation window.");
                yield return VerifyUiInput(confirmation,"confirmation-no");
                ActivateConfirmation(confirmation,DaggerfallMessageBox.MessageBoxButtons.No);yield return null;
                yield return VerifyUiInput(window,"confirmation-return");
                Need(goods.stackCount==count && game.PlayerEntity.GoldPieces==5000 && store.ReadNpcInventory(person.Id).WalletGold==wallet,
                    "Declining native confirmation changed stock or wallets.");
                ShowConfirmation(window);yield return null;
                confirmation=DaggerfallUI.UIManager.TopWindow as DaggerfallMessageBox;
                Need(confirmation!=null,"Native trade confirmation did not reopen.");
                yield return VerifyUiInput(confirmation,"confirmation-yes");
                ActivateConfirmation(confirmation,DaggerfallMessageBox.MessageBoxButtons.Yes);
                yield return null;
                yield return VerifyUiInput(window,"purchase-return");
                window.CloseWindow();yield return null;
                yield return VerifyHudInput("purchase-exit");
                var saved=store.ReadNpcInventory(person.Id);
                Need(goods.stackCount==count-1 && saved.Revision==revision+1 && saved.WalletGold==wallet+price &&
                    game.PlayerEntity.GoldPieces==5000-price,"Native purchase did not atomically update exact stock and both wallets.");
                NativeMarketGoodsItem received=null;
                for(int index=0;index<game.PlayerEntity.Items.Count;index++)
                {var item=game.PlayerEntity.Items.GetItem(index);if(!beforeIds.Contains(item.UID))received=item as NativeMarketGoodsItem;}
                Need(received!=null && received.UID!=uid && received.stackCount==1 && received.GoodsArchive==goods.GoodsArchive &&
                    received.GoodsRecord==goods.GoodsRecord && received.GetSaveData().className==typeof(NativeMarketGoodsItem).ToString(),
                    "Native partial purchase lost custom goods type, exact recipient UID or observed goods identity.");
                NativeNpcInventoryBinding reopened;Need(NativeNpcInventoryBridge.TryOpen(store,person.Id,null,0,out reopened,out reason),reason);
                Need(reopened.Items.GetItem(uid)!=null && reopened.Items.GetItem(uid).stackCount==count-1,"Saved vendor stock did not retain exact remainder.");
                Need(store.ReadPersonalBarterReceipts().Entries.Count>0,"Native market purchase has no durable ownership receipt.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_VENDOR_PURCHASE_PASS: person="+person.Id+"; sourceUid="+uid+
                    "; recipientUid="+received.UID+"; quantity=1; paid="+price+"; sellerWallet="+saved.WalletGold+"; stockRemaining="+goods.stackCount);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_VENDOR_PLAYER_UI_PASS: native menu, cancel, Talk reopen, confirmation No/Yes, purchase and HUD input return.");
                Need(NativeVendorTradeAdapter.TryOpenMarketStock(store,post.Seller,person,out stock,out reason),reason);
                var remaining=stock.Items.GetItem(uid);ResidentActor buyer=null;NativeNpcInventoryBinding buyerBinding=null;int npcPrice=0;
                float buyerDeadline=Time.realtimeSinceStartup+75,nextBuyerReport=0;
                int eligibleBuyers=0,boundBuyers=0;
                while(buyer==null && Time.realtimeSinceStartup<buyerDeadline)
                {
                    eligibleBuyers=0;boundBuyers=0;
                    foreach(var candidate in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                    {
                        if(candidate.Resident==null || !candidate.Resident.IsAlive || candidate.IsSleeping ||
                            candidate.Resident.AgeGroup!=ResidentAgeGroup.Adult || candidate.Resident.IsQuestProtected ||
                            candidate.Resident.Id==person.Id)continue;
                        eligibleBuyers++;
                        var owner=candidate.GetComponent<NativeNpcInventoryOwner>();
                        if(!owner || owner.Binding==null)continue;
                        boundBuyers++;
                        var inventory=store.ReadNpcInventory(candidate.Resident.Id);int quoted;
                        if(inventory!=null && NpcShopCommerce.TryPriceForBuyer(store,candidate.Resident.Id,
                            FormulaHelper.CalculateCost(remaining.value,10),1,10,false,out quoted) && inventory.WalletGold>=quoted &&
                            NativeNpcInventoryBridge.TryCapture(store,owner.Binding,out reason))
                        {buyer=candidate;buyerBinding=owner.Binding;npcPrice=quoted;break;}
                    }
                    if(buyer!=null)break;
                    if(Time.realtimeSinceStartup>=nextBuyerReport)
                    {
                        nextBuyerReport=Time.realtimeSinceStartup+10;
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_VENDOR_BUYER_WAIT: streetsReady="+streets.Ready+
                            "; eligibleAdults="+eligibleBuyers+"; boundAdults="+boundBuyers+"; playing="+game.IsPlayingGame()+
                            "; paused="+GameManager.IsGamePaused+"; preparation=ordinary resident materialization; noNpcInjection=true; noNpcFunding=true");
                    }
                    yield return new WaitForSecondsRealtime(0.5f);
                }
                Need(buyer!=null,"No real awake adult with actual inventory and purchase money is available after 75 seconds: eligible="+
                    eligibleBuyers+"; bound="+boundBuyers+".");
                // Ordinary gameplay ran during readiness. Rebind current stock rather
                // than keeping the pre-wait revision of the real seller inventory.
                Need(NativeVendorTradeAdapter.TryOpenMarketStock(store,post.Seller,person,out stock,out reason),reason);
                remaining=stock.Items.GetItem(uid);
                Need(remaining!=null && NpcShopCommerce.TryPriceForBuyer(store,buyer.Resident.Id,
                    FormulaHelper.CalculateCost(remaining.value,10),1,10,false,out npcPrice) &&
                    store.ReadNpcInventory(buyerBinding.PersonId).WalletGold>=npcPrice,
                    "Actual buyer or market stock changed before the NPC purchase.");
                int buyerGold=store.ReadNpcInventory(buyerBinding.PersonId).WalletGold;
                int sellerGold=store.ReadNpcInventory(person.Id).WalletGold;int remainingCount=remaining.stackCount;
                NativeVendorPurchaseReceipt npcReceipt;
                Need(NativeVendorTradeAdapter.TryNpcPurchase(store,stock,buyerBinding,uid,1,npcPrice,()=>buyer && buyer.Resident.IsAlive,
                    "native-market-fixture:"+Guid.NewGuid().ToString("N"),out npcReceipt,out reason),reason);
                var npcItem=buyerBinding.Items.GetItem(npcReceipt.RecipientItemUid) as NativeMarketGoodsItem;
                Need(npcItem!=null && npcItem.UID!=uid && npcItem.stackCount==1 && npcItem.GoodsArchive==goods.GoodsArchive &&
                    stock.Items.GetItem(uid).stackCount==remainingCount && store.ReadNpcInventory(person.Id).WalletGold==sellerGold+npcPrice &&
                    store.ReadNpcInventory(buyerBinding.PersonId).WalletGold==buyerGold-npcPrice &&
                    store.NpcInventoryItemOwner(npcItem.UID)==buyerBinding.PersonId,"NPC market purchase lost exact stock, recipient ownership or wallet conservation.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_VENDOR_NPC_PURCHASE_PASS: buyer="+buyerBinding.PersonId+"; seller="+person.Id+
                    "; sourceUid="+uid+"; recipientUid="+npcItem.UID+"; paid="+npcPrice);
                yield return VerifyShopRouting(host,store,game);
            }
            finally {DaggerfallUI.Instance.PopToHUD();game.PlayerEntity.GoldPieces=originalGold;}
        }
        private static void Set(NativeVendorTradeWindow window,ulong uid,int count)
        {typeof(NativeVendorTradeWindow).GetMethod("SetQuantity",Private).Invoke(window,new object[]{uid,count});}

        private static void ShowConfirmation(NativeVendorTradeWindow window)
        {typeof(DaggerfallTradeWindow).GetMethod("ShowTradePopup",Private).Invoke(window,null);}

        private static void ActivateConfirmation(DaggerfallMessageBox window,DaggerfallMessageBox.MessageBoxButtons button)
        {typeof(DaggerfallMessageBox).GetMethod("ActivateButton",Private).Invoke(window,new object[]{new Button{Tag=button}});}

        private static bool NativeCursorReady(bool ui)
        {
            var look=GameManager.Instance.PlayerMouseLook;
            // The smoke harness can disable native mouse look to avoid taking
            // the desktop cursor. Do not manufacture cursor evidence there.
            if(!look || !look.enabled)return true;
            if(ui)return InputManager.Instance.CursorVisible && Cursor.lockState==CursorLockMode.None &&
                (look.cursorActive || !look.enableMouseLook);
            if(look.cursorActive)return InputManager.Instance.CursorVisible && Cursor.lockState==CursorLockMode.None;
            return look.enableMouseLook && InputManager.Instance.CursorVisible==!look.lockCursor &&
                Cursor.lockState==(look.lockCursor?CursorLockMode.Locked:CursorLockMode.None);
        }

        private static IEnumerator VerifyUiInput(DaggerfallBaseWindow window,string phase)
        {
            var game=GameManager.Instance;float deadline=Time.realtimeSinceStartup+3;
            while((!window.IsSetup || !GameManager.IsGamePaused || !InputManager.Instance.IsPaused || !NativeCursorReady(true)) &&
                Time.realtimeSinceStartup<deadline)yield return null;
            Need(DaggerfallUI.UIManager.TopWindow==window && window.IsSetup && window.PauseWhileOpen &&
                GameManager.IsGamePaused && InputManager.Instance.IsPaused && Time.timeScale==0 && !game.IsPlayingGame() &&
                NativeCursorReady(true),"Native market UI did not acquire pause/input/cursor ownership: "+phase+"; "+InputState(window,true));
            var position=game.PlayerMotor.transform.position;
            for(int frame=0;frame<4;frame++)yield return null;
            Need(DaggerfallUI.UIManager.TopWindow==window && GameManager.IsGamePaused && InputManager.Instance.IsPaused &&
                Vector3.Distance(position,game.PlayerMotor.transform.position)<0.01f,"Player moved while native market UI owned input: "+phase);
            LogInput(phase,true);
        }

        private static IEnumerator VerifyHudInput(string phase)
        {
            var game=GameManager.Instance;float deadline=Time.realtimeSinceStartup+3;
            while((!game.IsPlayerOnHUD || GameManager.IsGamePaused || InputManager.Instance.IsPaused || !NativeCursorReady(false)) &&
                Time.realtimeSinceStartup<deadline)yield return null;
            Need(game.IsPlayerOnHUD && !GameManager.IsGamePaused && !InputManager.Instance.IsPaused && Time.timeScale>0 &&
                game.IsPlayingGame() && NativeCursorReady(false),"Native market close did not restore HUD input: "+phase+"; "+InputState(null,false));
            LogInput(phase,false);
        }

        private static string InputState(DaggerfallBaseWindow expected,bool ui)
        {
            var game=GameManager.Instance;var look=game.PlayerMouseLook;var top=DaggerfallUI.UIManager.TopWindow;
            var hide=typeof(PlayerMouseLook).GetField("forceHideCursor",Private);
            return "top="+(top==null?"null":top.GetType().Name)+"; expectedTop="+(expected==null?game.IsPlayerOnHUD:ReferenceEquals(top,expected))+
                "; setup="+(expected!=null && expected.IsSetup)+"; pauseWhileOpen="+(top!=null && top.PauseWhileOpen)+
                "; paused="+GameManager.IsGamePaused+"; inputPaused="+InputManager.Instance.IsPaused+"; timeScale="+Time.timeScale+
                "; playing="+game.IsPlayingGame()+"; hud="+game.IsPlayerOnHUD+"; focused="+Application.isFocused+
                "; mouseDriver="+(look && look.enabled)+"; mouseActive="+(look && look.isActiveAndEnabled)+
                "; mouseLook="+(look && look.enableMouseLook)+"; simpleCursorLock="+(look && look.simpleCursorLock)+
                "; forceHide="+(look && hide!=null?(bool)hide.GetValue(look):false)+"; cursorActive="+(look && look.cursorActive)+
                "; lockCursor="+(look && look.lockCursor)+"; cursorRequested="+InputManager.Instance.CursorVisible+
                "; cursorVisible="+Cursor.visible+"; cursorLock="+Cursor.lockState+"; cursorReady="+NativeCursorReady(ui);
        }

        private static void LogInput(string phase,bool ui)
        {
            var look=GameManager.Instance.PlayerMouseLook;bool cursorExercised=look && look.enabled;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_VENDOR_INPUT_PASS: phase="+phase+"; ui="+ui+
                "; paused="+GameManager.IsGamePaused+"; inputPaused="+InputManager.Instance.IsPaused+
                "; timeScale="+Time.timeScale+"; cursorExercised="+cursorExercised+
                "; cursorRequested="+InputManager.Instance.CursorVisible+"; cursorVisible="+Cursor.visible+
                "; cursorLock="+Cursor.lockState+"; nativeMouseLook="+(look && look.enableMouseLook));
            if(!cursorExercised)Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_VENDOR_CURSOR_NOT_EXERCISED: phase="+phase+
                "; reason=native mouse-look driver disabled by harness");
        }

        private static IEnumerator VerifyShopRouting(PeopleOfTheIliacBayMod host,WorldStateStore store,GameManager game)
        {
            DaggerfallUI.Instance.PopToHUD();
            var fixtureType=typeof(StaticPersonalTradeNativeProbe);
            var select=fixtureType.GetMethod("SelectOpenShop",BindingFlags.Static|BindingFlags.NonPublic);
            var fixture=select.Invoke(null,new object[]{game.StreamingWorld.GetCurrentBuildingDirectory()});
            Need(fixture!=null,"No real open shop is available for native commerce routing.");
            var type=fixture.GetType();int key=(int)type.GetField("BuildingKey").GetValue(fixture);
            TownSmokeHarness.EnterInterior((Transform)type.GetField("DoorOwner").GetValue(fixture),(StaticDoor)type.GetField("Door").GetValue(fixture));
            float deadline=Time.realtimeSinceStartup+45;
            while((!game.PlayerEnterExit.IsPlayerInsideBuilding || !game.PlayerEnterExit.Interior ||
                game.PlayerEnterExit.Interior.EntryDoor.buildingKey!=key) && Time.realtimeSinceStartup<deadline)yield return null;
            Need(game.PlayerEnterExit.IsPlayerInsideBuilding && game.PlayerEnterExit.Interior && game.PlayerEnterExit.Interior.EntryDoor.buildingKey==key,
                "Native shop test did not enter its actual building.");
            StaticNPC keeper=null;deadline=Time.realtimeSinceStartup+30;
            var find=fixtureType.GetMethod("FindRegisteredKeeper",BindingFlags.Static|BindingFlags.NonPublic);
            while(!keeper && Time.realtimeSinceStartup<deadline)
            {keeper=(StaticNPC)find.Invoke(null,new object[]{game,fixture,store,game.PlayerGPS.CurrentMapID});if(!keeper)yield return null;}
            Need(keeper,"No live native shopkeeper matches the registered person.");
            var personal=(NativePerson)type.GetField("Person").GetValue(fixture);
            bool personalLedgerExisted=store.HasNpcInventory(personal.Id);
            NativeStaticNpcInventoryOwner personalOwner;string personalReason;
            Need(NativeStaticNpcInventoryOwner.TryOpen(store,keeper,personal,NpcArchetype.Merchant,out personalOwner,out personalReason),personalReason);
            game.TalkManager.TalkToStaticNPC(keeper,false);deadline=Time.realtimeSinceStartup+20;DaggerfallTalkWindow talk=null;
            while(Time.realtimeSinceStartup<deadline)
            {
                talk=DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
                if(talk!=null && talk.IsSetup && (bool)typeof(PeopleOfTheIliacBayMod).GetField("conversationSessionOpen",Private).GetValue(host) &&
                    ReferenceEquals(typeof(PeopleOfTheIliacBayMod).GetField("memoryWindow",Private).GetValue(host),talk))break;
                yield return null;
            }
            Need(talk!=null,"Native shopkeeper Talk did not open.");
            PersonalTradeNativeProbe.CaptureTradeContext(host,talk,"May I buy something from your shop?");
            var args=new object[]{talk,null};
            Need((bool)typeof(PeopleOfTheIliacBayMod).GetMethod("TryOpenConversationPersonalTradeWindow",Private).Invoke(host,args),"Shop trade request failed: "+args[1]);
            yield return null;
            var window=DaggerfallUI.UIManager.TopWindow as DaggerfallTradeWindow;
            Need(window!=null && !(window is NativeVendorTradeWindow),"Real shopkeeper did not route to vanilla native trade.");
            bool actualShelf=false;
            foreach(var shelf in game.PlayerEnterExit.Interior.GetComponentsInChildren<DaggerfallLoot>())
                if(shelf.ContainerType==LootContainerTypes.ShopShelves && ReferenceEquals(shelf.Items,window.MerchantItems))actualShelf=true;
            Need(actualShelf && window.MerchantItems.Count>0,"Shop native UI is not backed by its real shelf stock.");
            var personalBefore=store.ReadNpcInventory(personal.Id);
            int wallet=personalBefore.WalletGold;
            int playerGold=game.PlayerEntity.GoldPieces;int count=window.MerchantItems.Count;
            var shelfItems=new Dictionary<ulong,int>();
            for(int index=0;index<window.MerchantItems.Count;index++)
            {var item=window.MerchantItems.GetItem(index);shelfItems.Add(item.UID,item.stackCount);}
            DaggerfallUI.Instance.PopToHUD();
            // The native pop/empty-basket return is synchronous. Do not compare
            // cancellation against a later frame's ordinary NPC initialization.
            var personalAfter=store.ReadNpcInventory(personal.Id);
            Need(window.MerchantItems.Count==count && game.PlayerEntity.GoldPieces==playerGold &&
                personalAfter!=null && personalAfter.WalletGold==wallet && personalAfter.Items.Count==personalBefore.Items.Count &&
                personalBefore.Items.TrueForAll(before=>personalAfter.Items.Exists(after=>after.Uid==before.Uid && after.NativePayload==before.NativePayload)) &&
                shelfItems.Count==window.MerchantItems.Count &&
                new List<ulong>(shelfItems.Keys).TrueForAll(itemUid=>window.MerchantItems.GetItem(itemUid)!=null &&
                    window.MerchantItems.GetItem(itemUid).stackCount==shelfItems[itemUid]),
                "Cancelling vanilla commerce changed personal stock or wallets: shelf="+count+"/"+window.MerchantItems.Count+
                "; playerGold="+playerGold+"/"+game.PlayerEntity.GoldPieces+"; keeperWallet="+wallet+"/"+
                (personalAfter==null?-1:personalAfter.WalletGold)+"; keeperItems="+personalBefore.Items.Count+"/"+
                (personalAfter==null?-1:personalAfter.Items.Count)+"; personalLedgerExisted="+personalLedgerExisted);
            yield return null;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_VENDOR_SHOP_ROUTE_PASS: building="+key+"; person="+personal.Id+
                "; actualShelfItems="+count+"; exactShelfUids=true; personalStockSeparate=true; personalLedgerExisted="+personalLedgerExisted+
                "; cancelCheckedSynchronously=true");
        }
    }
}
