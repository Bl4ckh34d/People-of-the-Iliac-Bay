using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Explicit disposable native fixture. Exercises the actual owned window and
    // its captured production callbacks, without substituting an LLM decision.
    internal static class PersonalTradeNativeProbe
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        internal static IEnumerator Run(PeopleOfTheIliacBayMod host,StreetPopulation streets)
        {
            var game=GameManager.Instance;
            Require(Environment.GetEnvironmentVariable("POTIB_ROADMAP_PROBE")=="personal-trade" && host && streets &&
                game.PlayerEntity.Name.StartsWith("PeopleOfTheIliacBayTest-",StringComparison.Ordinal),"Personal trade requires an owned explicit fixture.");
            var store=(WorldStateStore)typeof(StreetPopulation).GetField("worldStore",Private).GetValue(streets);
            Require(store!=null,"The loaded street population has no authoritative world store.");
            ResidentActor selected=null;DaggerfallUnityItem selectedItem=null;NativeNpcInventoryOwner owner=null;
            float deadline=Time.realtimeSinceStartup+75;
            while(!selected && Time.realtimeSinceStartup<deadline)
            {
                foreach(var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                {
                    if(!actor || !actor.Person || actor.Resident==null || !actor.Resident.IsAlive || actor.Resident.IsQuestProtected ||
                        actor.Resident.AgeGroup!=ResidentAgeGroup.Adult || actor.IsSleeping ||
                        store.ActiveNpcAction(actor.Resident.Id)!=null || store.ActiveNpcTask(actor.Resident.Id)!=null)continue;
                    var inventory=actor.GetComponent<NativeNpcInventoryOwner>();
                    if(!inventory || inventory.Binding==null)continue;
                    selected=actor;owner=inventory;
                    if(selected)break;
                }
                if(!selected)yield return null;
            }
            Require(selected && owner,"No existing adult with a bound personal inventory appeared.");
            // Controlled disposable fixture stock, not production generation:
            // historical test ledgers can correctly be empty and must not refill.
            selectedItem=ItemBuilder.CreateItem(ItemGroups.UselessItems1,(int)UselessItems1.Glass_Bottle);
            selectedItem.value=100;selectedItem.stackCount=2;
            owner.Binding.Items.AddItem(selectedItem,noStack:true);owner.Capture();
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PERSONAL_TRADE_FIXTURE: existingResident="+selected.Resident.Id+
                "; controlledNativeBottle=true; count=2; uid="+selectedItem.UID);
            game.PlayerMotor.transform.position=selected.transform.position+selected.transform.right*2;
            int originalGold=game.PlayerEntity.GoldPieces;
            short originalSkill=game.PlayerEntity.Skills.GetPermanentSkillValue(DFCareer.Skills.Mercantile);
            game.PlayerEntity.GoldPieces=5000;
            game.PlayerEntity.Skills.SetPermanentSkillValue(DFCareer.Skills.Mercantile,20);
            try
            {
                DaggerfallTalkWindow talk=null;
                yield return ConversationMoodProbe.OpenTalk(host,game,selected,value=>talk=value);
                var window=Open(host,talk);
                yield return null;yield return null;
                int count=selectedItem.stackCount,playerGold=game.PlayerEntity.GoldPieces;
                var saved=store.ReadNpcInventory(selected.Resident.Id);int npcGold=saved.WalletGold;long revision=saved.Revision;
                Stage(window,PersonalTradeSide.Npc,selectedItem,1);
                Call(window,"RequestQuote");
                Require(owner.Binding.Items.GetItem(selectedItem.UID)==selectedItem && selectedItem.stackCount==count &&
                    game.PlayerEntity.Items.GetItem(selectedItem.UID)==null && game.PlayerEntity.GoldPieces==playerGold &&
                    store.ReadNpcInventory(selected.Resident.Id).Revision==revision,"Preview changed actual inventory, gold or revision.");
                Call(window,"CloseUnchanged");yield return null;yield return null;
                Require(owner.Binding.Items.GetItem(selectedItem.UID)==selectedItem && selectedItem.stackCount==count &&
                    game.PlayerEntity.GoldPieces==playerGold && store.ReadNpcInventory(selected.Resident.Id).WalletGold==npcGold &&
                    store.ReadNpcInventory(selected.Resident.Id).Revision==revision,"Cancel changed actual items or wallets.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PERSONAL_TRADE_CANCEL_PASS: nativeWindow=true; lowMercantile20=true; previewUnchanged=true; cancelUnchanged=true");

                yield return ConversationMoodProbe.OpenTalk(host,game,selected,value=>talk=value);
                window=Open(host,talk);yield return null;yield return null;
                Stage(window,PersonalTradeSide.Npc,selectedItem,1);
                Call(window,"RequestQuote");
                var quote=(PersonalTradeWindowQuote)Field(window,"quote");
                Require(quote!=null,"Native purchase produced no quote.");
                typeof(PersonalTradeWindow).GetField("playerOfferGold",Private).SetValue(window,quote.NetGoldToNpc);
                typeof(PersonalTradeWindow).GetField("hasPlayerOffer",Private).SetValue(window,true);
                Call(window,"RequestQuote");quote=(PersonalTradeWindowQuote)Field(window,"quote");
                Require(quote!=null && quote.CanSubmit && quote.NetGoldToNpc>0,"Exact native purchase quote was not accepted.");
                int price=quote.NetGoldToNpc;
                int skillUses=game.PlayerEntity.SkillUses[(int)DFCareer.Skills.Mercantile];
                var playerUidsBefore=new HashSet<ulong>();
                for(int index=0;index<game.PlayerEntity.Items.Count;index++)playerUidsBefore.Add(game.PlayerEntity.Items.GetItem(index).UID);
                Call(window,"SubmitOffer");yield return null;yield return null;
                DaggerfallUnityItem received=null;
                for(int index=0;index<game.PlayerEntity.Items.Count;index++)
                {
                    var item=game.PlayerEntity.Items.GetItem(index);
                    if(item.ItemGroup==selectedItem.ItemGroup && item.TemplateIndex==selectedItem.TemplateIndex &&
                        !playerUidsBefore.Contains(item.UID) && item.stackCount==1)received=item;
                }
                Require(received!=null && (count>1?selectedItem.stackCount==count-1:owner.Binding.Items.GetItem(selectedItem.UID)==null),"Confirmed purchase did not move the actual native item.");
                Require(game.PlayerEntity.GoldPieces==playerGold-price && store.ReadNpcInventory(selected.Resident.Id).WalletGold==npcGold+price,
                    "Confirmed purchase used the wrong gold amount.");
                Require(game.PlayerEntity.SkillUses[(int)DFCareer.Skills.Mercantile]==skillUses+1,"Confirmed purchase did not train Mercantile once.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PERSONAL_TRADE_BUY_PASS: nativeWindow=true; lowMercantile20=true; exactItem=true; exactGold=true; nativeMercantile=true; price="+price+"; uid="+received.UID);
                // Leave a fresh real window visible for visual inspection. The
                // offered player item is the actual purchase, not a fake fixture item.
                yield return ConversationMoodProbe.OpenTalk(host,game,selected,value=>talk=value);
                window=Open(host,talk);yield return null;yield return null;
                Stage(window,PersonalTradeSide.Player,received,1);
                Call(window,"RequestQuote");quote=(PersonalTradeWindowQuote)Field(window,"quote");
                Require(quote!=null && quote.NetGoldToNpc<0,"Pure sale produced no NPC payment quote.");
                typeof(PersonalTradeWindow).GetField("playerOfferGold",Private).SetValue(window,quote.NetGoldToNpc);
                typeof(PersonalTradeWindow).GetField("hasPlayerOffer",Private).SetValue(window,true);
                Call(window,"RequestQuote");quote=(PersonalTradeWindowQuote)Field(window,"quote");
                Require(quote!=null && quote.CanSubmit,"Exact native sale quote was not accepted.");
                int sellNet=quote.NetGoldToNpc;playerGold=game.PlayerEntity.GoldPieces;npcGold=store.ReadNpcInventory(selected.Resident.Id).WalletGold;
                yield return new WaitForSecondsRealtime(15);
                Call(window,"SubmitOffer");yield return null;yield return null;
                Require(game.PlayerEntity.Items.GetItem(received.UID)==null && owner.Binding.Items.GetItem(received.UID)==received &&
                    game.PlayerEntity.GoldPieces==playerGold-sellNet && store.ReadNpcInventory(selected.Resident.Id).WalletGold==npcGold+sellNet,
                    "Confirmed sale did not transfer the real item and accepted net gold.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PERSONAL_TRADE_SELL_PASS: nativeWindow=true; exactItem=true; exactGold=true; net="+sellNet);
                if(Environment.GetEnvironmentVariable("POTIB_TRADE_EXTENDED")=="1")
                {
                    yield return PersonalTradeHagglingNativeProbe.Run(host,selected,store,selectedItem);
                    yield return PersonalItemLoanNativeProbe.Run(host,selected,store,received);
                }
            }
            finally
            {
                DaggerfallUI.Instance.PopToHUD();
                game.PlayerEntity.Skills.SetPermanentSkillValue(DFCareer.Skills.Mercantile,originalSkill);
                // No fixture save is written. Restoring the player's temporary
                // test budget does not pretend this run conserved starting wealth.
                game.PlayerEntity.GoldPieces=originalGold;
            }
        }
        private static PersonalTradeWindow Open(PeopleOfTheIliacBayMod host,DaggerfallTalkWindow talk)
        {
            CaptureTradeContext(host,talk,"May I trade with you?");
            var args=new object[]{talk,null};
            Require((bool)typeof(PeopleOfTheIliacBayMod).GetMethod("TryOpenPersonalTradeWindow",Private).Invoke(host,args),
                "Actual Talk trade handoff refused: "+args[1]);
            var window=DaggerfallUI.UIManager.TopWindow as PersonalTradeWindow;
            Require(window!=null,"Talk handoff did not open the owned trade window.");return window;
        }
        internal static void CaptureTradeContext(PeopleOfTheIliacBayMod host,DaggerfallTalkWindow talk,string text)
        {
            // Normal player input captures its real context before commerce:
            // this also creates the existing deterministic personality profile.
            long request=Convert.ToInt64(typeof(PeopleOfTheIliacBayMod).GetField("conversationContextRequestRevision",Private).GetValue(host));
            host.CaptureConversationContext(talk,text,request+1);
        }
        private static object Field(PersonalTradeWindow window,string name){return typeof(PersonalTradeWindow).GetField(name,Private).GetValue(window);}
        private static void Stage(PersonalTradeWindow window,PersonalTradeSide side,DaggerfallUnityItem item,int quantity)
        {
            var method=typeof(PersonalTradeWindow).GetMethod("SetQuantity",Private,null,new[]{typeof(PersonalTradeSide),typeof(DaggerfallUnityItem),typeof(int)},null);
            Require((bool)method.Invoke(window,new object[]{side,item,quantity}),"Native item cannot be staged.");Call(window,"RefreshView");
        }
        private static void Call(PersonalTradeWindow window,string name){typeof(PersonalTradeWindow).GetMethod(name,Private).Invoke(window,null);}
        private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    }
}
