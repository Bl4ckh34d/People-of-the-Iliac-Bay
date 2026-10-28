using System;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Guilds;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private bool redirectingNativeMarket;
        private StaticNPC nativeMarketTalkOnce;
        private DaggerfallTalkWindow nativeMarketTalkWindow;

        // Called at the existing public window boundary. Real Talk remains
        // available from the native merchant popup without opening it again.
        private bool TryRedirectNativeMarketTalk()
        {
            if(redirectingNativeMarket || saveFault || store==null || !options.Enabled)return false;
            if(nativeMarketTalkWindow!=null && !DaggerfallUI.UIManager.ContainsWindow(nativeMarketTalkWindow))nativeMarketTalkWindow=null;
            var talk=DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
            var game=GameManager.Instance;var manager=game==null?null:game.TalkManager;
            if(talk==null || manager==null || manager.CurrentNPCType!=TalkManager.NPCType.Static || !manager.StaticNPC)return false;
            var npc=manager.StaticNPC;
            if(nativeMarketTalkOnce==npc){nativeMarketTalkOnce=null;nativeMarketTalkWindow=talk;return false;}
            if(ReferenceEquals(nativeMarketTalkWindow,talk))return false;
            NativeMarketPost post;if(!TryNativeMarketPost(npc,out post))return false;
            RegisterNativeStaticConversationPerson(npc);
            var person=store.NativePersonInSettlement(game.PlayerGPS.CurrentMapID,StaticConversationPersonId(npc));
            if(!NativeStaticNpcInventoryOwner.Matches(npc,person))return false;
            FactionFile.FactionData faction;
            if(game.PlayerEntity.FactionData.GetFactionData(npc.Data.factionID,out faction))
                store.SetConversationBaseline(person.Id,manager.GetReactionToPlayer(faction),ConversationActionTime(),faction.id);
            redirectingNativeMarket=true;
            try
            {
                DaggerfallUI.Instance.PopToHUD();
                DaggerfallUI.UIManager.PushWindow(new NativeMarketServiceWindow(DaggerfallUI.UIManager,npc,person.Id,
                    ()=>{string reason;if(!TryOpenNativeMarketTrade(npc,person,out reason))DaggerfallUI.MessageBox(reason);},
                    ()=>nativeMarketTalkOnce=npc));
            }
            finally {redirectingNativeMarket=false;}
            return true;
        }

        private static bool TryNativeMarketPost(StaticNPC npc,out NativeMarketPost post)
        {
            post=null;int settlement,hash;
            if(!NativeExteriorNpcIdentity.TryRead(npc,out settlement,out hash))return false;
            var catalog=NativeMarketCatalog.For(GameManager.Instance.StreamingWorld.GetCurrentBuildingDirectory());
            post=catalog==null?null:catalog.Find(npc);
            return post!=null && post.Available && post.Role!=MarketPostRole.Unknown && post.Role!=MarketPostRole.Performer;
        }

        // Only a proven vendor intercepts semantic trade. A closed or unwilling
        // business cannot fall through into selling the person's clothes.
        private bool TryOpenConversationNativeVendorTrade(DaggerfallTalkWindow talk,out bool vendor,out string reason)
        {
            vendor=false;reason="I cannot arrange a trade right now.";
            var game=GameManager.Instance;var manager=game==null?null:game.TalkManager;
            if(manager==null || manager.CurrentNPCType!=TalkManager.NPCType.Static || !manager.StaticNPC)return false;
            var npc=manager.StaticNPC;NativeMarketPost post;
            if(TryNativeMarketPost(npc,out post))
            {
                vendor=true;var person=store.NativePersonInSettlement(game.PlayerGPS.CurrentMapID,StaticConversationPersonId(npc));
                if(!CurrentVendorTradeConversation(talk,npc))return false;
                return TryOpenNativeMarketTrade(npc,person,out reason);
            }
            if(!game.PlayerEnterExit.IsPlayerInsideBuilding || !game.PlayerEnterExit.Interior ||
                npc.Data.context!=StaticNPC.Context.Building || npc.Data.buildingKey!=game.PlayerEnterExit.BuildingDiscoveryData.buildingKey)return false;
            var building=game.PlayerEnterExit.BuildingDiscoveryData;
            bool shop=RMBLayout.IsShop(building.buildingType) && NativeBusinessServices.IsKeeper(npc,building.buildingType);
            GuildServices service=GuildServices.Training;
            bool guild=Services.HasGuildService(npc.Data.factionID);
            if(guild)service=Services.GetService((GuildNpcServices)npc.Data.factionID);
            guild=guild && (service==GuildServices.BuyPotions || service==GuildServices.BuyMagicItems ||
                service==GuildServices.BuySoulgems || service==GuildServices.SellMagicItems);
            if(!shop && !guild)return false;
            vendor=true;
            if(!CurrentVendorTradeConversation(talk,npc) || streets==null || !streets.Ready || streets.KeeperServiceBlocked(npc) ||
                !CanServeNativeKeeper(npc)){reason="I cannot serve you right now.";return false;}
            if(guild)return OpenNativeGuildTrade(npc,out reason);
            if(!game.PlayerEnterExit.IsPlayerInsideOpenShop || !PlayerActivate.IsBuildingOpen(building.buildingType))
            {reason="The shop is closed.";return false;}
            DaggerfallLoot chosen=null;
            int today=DaggerfallLoot.CreateStockedDate(DaggerfallUnity.Instance.WorldTime.Now);
            foreach(var shelf in game.PlayerEnterExit.Interior.GetComponentsInChildren<DaggerfallLoot>())
            {
                if(!shelf.gameObject.activeInHierarchy || shelf.ContainerType!=LootContainerTypes.ShopShelves || shelf.LoadID==0 ||
                    shelf.playerOwned || shelf.houseOwned || shelf.customDrop || shelf.corpseQuestUID!=0 ||
                    !shelf.GetComponent<SerializableLootContainer>() || shelf.GetComponentInParent<QuestResourceBehaviour>() ||
                    shelf.GetComponentInParent<DaggerfallAction>())continue;
                // Same first-access stock authority as PlayerActivate. This is
                // the actual shelf collection, never a regenerated preview.
                if(shelf.stockedDate<today)shelf.StockShopShelf(building);
                if(shelf.stockedDate==today && shelf.Items.Count>0 && (!chosen || shelf.Items.Count>chosen.Items.Count))chosen=shelf;
            }
            if(!chosen){reason="There are no goods available on the shop shelves.";return false;}
            var previous=UIWindowFactory.GetInstanceWithArgs(UIWindowType.MerchantServicePopup,
                new object[]{DaggerfallUI.UIManager,npc,DaggerfallMerchantServicePopupWindow.Services.Sell});
            var window=(DaggerfallTradeWindow)UIWindowFactory.GetInstanceWithArgs(UIWindowType.Trade,
                new object[]{DaggerfallUI.UIManager,previous,DaggerfallTradeWindow.WindowModes.Buy,null});
            window.MerchantItems=chosen.Items;
            personalCommerceBeforeClose?.Invoke();DaggerfallUI.Instance.PopToHUD();DaggerfallUI.UIManager.PushWindow(window);reason=null;return true;
        }

        private bool CurrentVendorTradeConversation(DaggerfallTalkWindow talk,StaticNPC npc)
        {return !saveFault && store!=null && options.Enabled && conversationSessionOpen && talk!=null &&
            ReferenceEquals(talk,memoryWindow) && DaggerfallUI.UIManager.TopWindow==talk &&
            StaticConversationPersonId(npc)==conversationPersonId;}

        private bool OpenNativeGuildTrade(StaticNPC npc,out string reason)
        {
            reason="The native guild service is unavailable.";
            var game=GameManager.Instance;var building=game.PlayerEnterExit.BuildingDiscoveryData;
            FactionFile.FactionData faction,buildingFaction;
            if(!game.PlayerEntity.FactionData.GetFactionData(npc.Data.factionID,out faction))return false;
            var group=game.PlayerEntity.FactionData.GetFactionData(building.factionID,out buildingFaction)?
                (FactionFile.GuildGroups)buildingFaction.ggroup:FactionFile.GuildGroups.None;
            if(group==FactionFile.GuildGroups.None)group=(FactionFile.GuildGroups)faction.ggroup;
            if(group==FactionFile.GuildGroups.HolyOrder && !Temple.IsDivine(buildingFaction.id))return false;
            var window=UIWindowFactory.GetInstanceWithArgs(UIWindowType.GuildServicePopup,
                new object[]{DaggerfallUI.UIManager,npc,group,building.factionID});
            var open=window.GetType().GetMethod("DoGuildService",BindingFlags.Instance|BindingFlags.NonPublic);
            if(open==null)return false;
            personalCommerceBeforeClose?.Invoke();DaggerfallUI.Instance.PopToHUD();DaggerfallUI.UIManager.PushWindow(window);
            // DFU owns membership/rank checks, stock creation, payment and its
            // registered custom service. Never fabricate a trainer's stock.
            if(DaggerfallUI.UIManager.TopWindow==window)open.Invoke(window,null);
            reason=null;return true;
        }

        private bool NativeMarketTradeAvailable(WorldStateStore source,NativeVendorTradeStock stock,PlayerEntity player)
        {
            var game=GameManager.Instance;NativeMarketPost post;
            return !saveFault && ReferenceEquals(store,source) && options.Enabled && game!=null &&
                ReferenceEquals(game.PlayerEntity,player) && stock!=null && stock.Person.IsAlive &&
                NativeStaticNpcInventoryOwner.Matches(stock.Post.Seller,stock.Person) && TryNativeMarketPost(stock.Post.Seller,out post) &&
                ReferenceEquals(post,stock.Post) && streets!=null && streets.Ready && !streets.KeeperServiceBlocked(stock.Post.Seller) &&
                !PersonalTradeWorkIsBusy(source,stock.Person.Id);
        }

        private bool NativeMarketTradeWilling(WorldStateStore source,NativeVendorTradeStock stock,PlayerEntity player)
        {
            if(!NativeMarketTradeAvailable(source,stock,player))return false;
            int disposition;
            return !conversationDispositionEnabled || source.TryGetActualConversationDisposition(stock.Person.Id,out disposition) &&
                DispositionPricing.AllowsPaidService(disposition);
        }

        private bool TryOpenNativeMarketTrade(StaticNPC npc,NativePerson person,out string reason)
        {
            NativeVendorTradeStock stock;
            if(!NativeVendorTradeAdapter.TryOpenMarketStock(store,npc,person,out stock,out reason))return false;
            var source=store;var player=GameManager.Instance.PlayerEntity;
            if(!NativeMarketTradeWilling(source,stock,player)){reason="I cannot serve you right now.";return false;}
            var checks=new PersonalBarterLiveChecks {
                NpcAdultNow=()=>NativeMarketTradeAvailable(source,stock,player),
                NpcEligibleNow=()=>NativeMarketTradeAvailable(source,stock,player),
                NpcWillingNow=()=>NativeMarketTradeWilling(source,stock,player),
                PlayerConsentedNow=()=>NativeMarketTradeAvailable(source,stock,player),
                NpcValuesItemNow=stock.IsSaleItem,OccupationallyDisposableNow=stock.IsSaleItem,
                ItemUniqueNow=item=>item.IsArtifact,
                ItemSourceAllowedNow=item=>PersonalTradeItemSourceAllowed(source,stock.Person.Id,item)
            };
            NativeVendorPurchase buy=(IList<NativeVendorTradeLine> selected,int price,out string failure)=>
            {
                failure="The market stock changed.";
                var lines=new List<PersonalBarterLine>();var staging=new PersonalTradeStaging();
                foreach(var line in selected)
                {
                    var item=stock.Items.GetItem(line.ItemUid);
                    if(item==null || !stock.IsSaleItem(item) || !staging.TrySetQuantity(PersonalTradeSide.Npc,line.ItemUid,item.stackCount,line.Quantity,false))return false;
                    lines.Add(new PersonalBarterLine {Direction=PersonalBarterDirection.NpcToPlayer,ItemUid=line.ItemUid,Quantity=line.Quantity});
                }
                PersonalBarterOrder order;
                if(price<1 || !NativeNpcInventoryBridge.TryPreparePersonalBarterForInventory(source,stock.Seller,player,
                    "native-market:"+Guid.NewGuid().ToString("N"),lines,price,checks,delta=>true,out order,out failure))return false;
                var binding=NpcCommerceHaggleBasketBinding.Bind(order,price);
                int disposition;source.TryGetActualConversationDisposition(stock.Person.Id,out disposition);
                var round=binding.CreateRound(price,price,1,player.Skills.GetLiveSkillValue(DFCareer.Skills.Mercantile),
                    Math.Max(1,Math.Min(100,player.Stats.LivePersonality)),disposition);
                string memory=CapturePersonalTradeMemory(PersonalTradeWindowProposal.Create(stock.Person.Id,stock.Seller.Revision,staging,price),player.Items,stock.Items,price);
                PersonalBarterReceipt receipt;
                if(!NativeNpcInventoryBridge.TryPersonalBarterForInventory(source,stock.Seller,player,order,ConversationActionTime(),
                    checks,binding,()=>price,round,delta=>true,out receipt,out failure))return false;
                RememberPersonalTrade(source,stock.Person.Id,PersonalTradeCommitBoundary.SourceBoundId("receipt",receipt.TransactionId),memory);
                if(source.TryReservePersonalBarterMercantileTally(receipt.TransactionId))player.TallySkill(DFCareer.Skills.Mercantile,1);
                return true;
            };
            var window=new NativeVendorTradeWindow(DaggerfallUI.UIManager,stock.Person.Id,stock.Items,stock.IsSaleItem,buy,PersonalTradeCloseMemory(source,stock.Person.Id));
            personalCommerceBeforeClose?.Invoke();DaggerfallUI.Instance.PopToHUD();DaggerfallUI.UIManager.PushWindow(window);reason=null;return true;
        }
    }
}
