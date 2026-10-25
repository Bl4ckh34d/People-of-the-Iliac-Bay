using System;
using System.Collections.Generic;
using System.Globalization;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Commit owner keeps every live authority in these callbacks. The window only
    // reads current items and carries detached UID/quantity proposal lines.
    public delegate bool PersonalTradeWindowPrepare(PersonalTradeWindowProposal proposal,
        out PersonalTradeWindowQuote quote,out string reason);
    public delegate bool PersonalTradeWindowSubmit(PersonalTradeWindowProposal proposal,
        PersonalTradeWindowQuote quote,out string receiptId,out string reason);
    public delegate void PersonalTradeWindowProposalSubmitted(PersonalTradeWindowProposal proposal,
        PersonalTradeWindowQuote quote);

    public sealed class PersonalTradeWindowQuote
    {
        // Signed net: positive means player pays NPC; negative means NPC pays player.
        public int NetGoldToNpc;
        public bool CanSubmit;
        public string Status;
        // Factory-owned evidence for one submitted proposal, never UI text edits.
        public NpcCommerceHaggleDisposition Disposition;
        public int ProposalMoodDelta, ProposalRound;
        public string ProposalEffectId;
    }

    public sealed class PersonalTradeWindowSession
    {
        public readonly string NpcId,PlayerLabel,NpcLabel;
        public readonly long StoreEpoch;
        public readonly ItemCollection PlayerItems,NpcItems;
        public readonly Func<int> ReadPlayerGold,ReadNpcGold;
        public readonly Func<PersonalTradeSide,DaggerfallUnityItem,bool> CanStage;
        public readonly PersonalTradeWindowPrepare Prepare;
        public readonly PersonalTradeWindowSubmit Submit;
        public readonly PersonalTradeWindowProposalSubmitted ProposalSubmitted;
        public readonly Action<bool,string> Closed;

        public PersonalTradeWindowSession(string npcId,long storeEpoch,ItemCollection playerItems,ItemCollection npcItems,
            Func<int> readPlayerGold,Func<int> readNpcGold,PersonalTradeWindowPrepare prepare,
            PersonalTradeWindowSubmit submit,Func<PersonalTradeSide,DaggerfallUnityItem,bool> canStage=null,
            string playerLabel="Player",string npcLabel="NPC",Action<bool,string> closed=null,
            PersonalTradeWindowProposalSubmitted proposalSubmitted=null)
        {
            if(string.IsNullOrWhiteSpace(npcId) || storeEpoch<0 || playerItems==null || npcItems==null ||
                readPlayerGold==null || readNpcGold==null || prepare==null || submit==null)
                throw new ArgumentException("Personal trade window requires a bound live session.");
            NpcId=npcId;StoreEpoch=storeEpoch;PlayerItems=playerItems;NpcItems=npcItems;
            ReadPlayerGold=readPlayerGold;ReadNpcGold=readNpcGold;Prepare=prepare;Submit=submit;
            ProposalSubmitted=proposalSubmitted;CanStage=canStage;
            PlayerLabel=string.IsNullOrWhiteSpace(playerLabel)?"Player":playerLabel;
            NpcLabel=string.IsNullOrWhiteSpace(npcLabel)?"NPC":npcLabel;Closed=closed;
        }
    }

    // Cropped trade artwork and native ItemListScroller renderer. It deliberately
    // never calls ItemCollection.Transfer, SplitStack, Clear, or AddItem.
    public sealed class PersonalTradeWindow : DaggerfallPopupWindow
    {
        private static readonly Color PanelColor=new Color(0.24f,0.25f,0.27f,1f);
        private static Texture2D tradeBackground;
        private readonly Mod mod;
        private readonly PersonalTradeWindowSession session;
        private readonly PersonalTradeStaging staging=new PersonalTradeStaging();
        private readonly List<DaggerfallUnityItem> playerRows=new List<DaggerfallUnityItem>();
        private readonly List<DaggerfallUnityItem> npcRows=new List<DaggerfallUnityItem>();
        private readonly List<DisplayRow> displayRows=new List<DisplayRow>();
        private ItemListScroller playerScroller,npcScroller;
        private TextLabel playerGoldLabel,npcQuoteLabel,offerLabel,statusLabel;
        private TextLabel npcPriceDirection,offerDirection;
        private PersonalTradeWindowQuote quote;
        private int playerOfferGold;
        private bool hasPlayerOffer;
        private bool committed,closed;

        private sealed class DisplayRow
        {
            public DaggerfallUnityItem Display;
            public PersonalTradePreviewRow Preview;
        }

        private PersonalTradeWindow(IUserInterfaceManager uiManager,DaggerfallBaseWindow previous,
            PersonalTradeWindowSession session,Mod mod) : base(uiManager,previous,188,240)
        { this.session=session??throw new ArgumentNullException("session");this.mod=mod; }

        public static PersonalTradeWindow Open(IUserInterfaceManager uiManager,DaggerfallBaseWindow previous,
            PersonalTradeWindowSession session,Mod mod)
        {
            if(uiManager==null)throw new ArgumentNullException("uiManager");
            var window=new PersonalTradeWindow(uiManager,previous,session,mod);
            uiManager.PushWindow(window);return window;
        }

        protected override void Setup()
        {
            ParentPanel.BackgroundColor=ScreenDimColor;
            NativePanel.BackgroundColor=PanelColor;
            NativePanel.BackgroundTexture=LoadBackground();
            SetupLabels();SetupScrollers();SetupButtons();RefreshView();
            SetStatus("Select items. Click your offer to change the gold amount.");
        }

        private Texture2D LoadBackground()
        {
            if(tradeBackground)return tradeBackground;
            var asset=mod==null?null:mod.GetAsset<TextAsset>("personal-trade-background.png.bytes");
            if(!asset)throw new InvalidOperationException("Personal trade background is missing.");
            var texture=new Texture2D(2,2,TextureFormat.ARGB32,false);
            if(!texture.LoadImage(asset.bytes))
            {UnityEngine.Object.Destroy(texture);throw new InvalidOperationException("Personal trade background is invalid.");}
            texture.name="PersonalTradeBackground";
            texture.filterMode=FilterMode.Point;texture.wrapMode=TextureWrapMode.Clamp;
            tradeBackground=texture;return texture;
        }

        public override void OnPush() { if(IsSetup)RefreshView(); }
        public override void OnPop()
        {
            if(closed)return;
            closed=true;staging.Clear();
            if(session.Closed!=null)session.Closed(committed,committed?"":statusLabel==null?"Trade cancelled.":statusLabel.Text);
        }

        private void SetupLabels()
        {
            AddLabel(new Rect(4,20,62,10),NativePanel).Text=Clip(session.NpcLabel,15);
            AddLabel(new Rect(123,20,61,10),NativePanel).Text=Clip(session.PlayerLabel,15);
            playerGoldLabel=AddLabel(new Rect(123,36,61,10),NativePanel);
            // The empty upper center is reserved for replies and instructions.
            statusLabel=AddLabel(new Rect(74,17,40,49),NativePanel);
            statusLabel.TextScale=0.70f;statusLabel.WrapText=true;statusLabel.WrapWords=true;
            statusLabel.MaxCharacters=110;
            AddPriceField(new Rect(75,80,38,17),out npcPriceDirection,out npcQuoteLabel);
            AddPriceField(new Rect(75,125,38,17),out offerDirection,out offerLabel);
        }

        private void AddPriceField(Rect rect,out TextLabel direction,out TextLabel amount)
        {
            var panel=DaggerfallUI.AddPanel(rect,NativePanel);
            direction=AddLabel(new Rect(1,1,rect.width-2,7),panel);
            amount=AddLabel(new Rect(1,9,rect.width-2,7),panel);
            foreach(var label in new[]{direction,amount})
            {
                label.TextColor=new Color(0.10f,0.10f,0.08f,1f);label.ShadowPosition=Vector2.zero;
                label.HorizontalAlignment=HorizontalAlignment.Center;
                label.TextScale=0.75f;
            }
        }

        private static TextLabel AddLabel(Rect rect,Panel parent)
        {
            var label=DaggerfallUI.AddDefaultShadowedTextLabel(new Vector2(rect.x,rect.y),parent);
            label.TextScale=0.85f;label.MaxWidth=(int)rect.width;
            return label;
        }

        private void SetupScrollers()
        {
            npcScroller=NewScroller(new Rect(0,57,70,182),PersonalTradeSide.Npc);
            playerScroller=NewScroller(new Rect(118,57,70,182),PersonalTradeSide.Player);
        }

        private ItemListScroller NewScroller(Rect rect,PersonalTradeSide side)
        {
            bool enhanced=DaggerfallUnity.Settings.EnableEnhancedItemLists;
            int rows=enhanced?8:4,columns=enhanced?2:1;
            float width=(rect.width-10)/columns,height=rect.height/rows;
            var cells=new Rect[rows*columns];
            for(int row=0;row<rows;row++)for(int column=0;column<columns;column++)
                cells[row*columns+column]=new Rect(column*width,row*height,width,height);
            var labels=new TextLabel {TextScale=0.75f};
            var scroller=new ItemListScroller(rows,columns,new Rect(10,0,rect.width-10,rect.height),cells,labels,defaultToolTip,
                textScale:0.75f) {Position=new Vector2(rect.x,rect.y),Size=new Vector2(rect.width,rect.height),
                BackgroundColourHandler=PreviewColour,LabelTextHandler=PreviewLabel};
            NativePanel.Components.Add(scroller);
            scroller.OnItemClick+=item=>Toggle(side,item);
            scroller.OnItemRightClick+=item=>PromptQuantity(side,item);
            return scroller;
        }

        private void SetupButtons()
        {
            AddButton(new Rect(74,124,40,19),"Change your net gold offer",PromptPrice);
            AddButton(new Rect(74,157,40,20),"Offer the selected items and gold",SubmitOffer);
            AddButton(new Rect(74,183,40,20),"Clear marked items",ClearOffer);
            AddButton(new Rect(72,211,44,26),"Cancel trade",CloseUnchanged);
        }

        private void AddButton(Rect rect,string toolTip,Action action)
        {
            var button=DaggerfallUI.AddButton(rect,NativePanel);
            button.BackgroundColor=Color.clear;
            button.MouseOverBackgroundColor=new Color(1f,1f,1f,0.10f);
            button.ToolTip=defaultToolTip;button.ToolTipText=toolTip;
            button.OnMouseClick+=(sender,position)=>action();
        }

        private void Toggle(PersonalTradeSide side,DaggerfallUnityItem item)
        {
            var row=FindRow(item);if(row==null)return;
            if(row.Preview.Incoming)
            {
                if(!SetQuantity(row.Preview.SourceSide,row.Preview.ItemUid,0))
                {SetStatus("That pending item changed.");return;}
            }
            else
            {
                int current=Quantity(row.Preview.SourceSide,row.Preview.ItemUid);
                int target=current==0?SourceStackCount(row.Preview.SourceSide,row.Preview.ItemUid):0;
                if(!SetQuantity(row.Preview.SourceSide,row.Preview.ItemUid,target))
                {SetStatus("That item cannot be staged.");return;}
            }
            SelectionChanged();
        }

        private void PromptQuantity(PersonalTradeSide side,DaggerfallUnityItem item)
        {
            var row=FindRow(item);if(row==null)return;
            if(row.Preview.Incoming)
            {
                if(!SetQuantity(row.Preview.SourceSide,row.Preview.ItemUid,0))
                {SetStatus("That pending item changed.");return;}
                SelectionChanged();return;
            }
            var box=new DaggerfallInputMessageBox(uiManager,this);
            box.SetTextBoxLabel("Quantity to mark");box.TextBox.Numeric=true;box.TextBox.MaxCharacters=8;
            box.TextBox.Text=(Quantity(row.Preview.SourceSide,row.Preview.ItemUid)==0?SourceStackCount(row.Preview.SourceSide,row.Preview.ItemUid):
                Quantity(row.Preview.SourceSide,row.Preview.ItemUid)).ToString(CultureInfo.InvariantCulture);
            box.OnGotUserInput+=(sender,input)=>
            {
                int quantity;if(!int.TryParse(input,NumberStyles.None,CultureInfo.InvariantCulture,out quantity) ||
                    !SetQuantity(row.Preview.SourceSide,row.Preview.ItemUid,quantity))
                {SetStatus("That quantity is unavailable.");return;}
                SelectionChanged();
            };
            box.Show();
        }

        private void PromptPrice()
        {
            if(staging.Lines.Count==0){SetStatus("Select items first.");return;}
            var box=new DaggerfallInputMessageBox(uiManager,this);
            box.SetTextBoxLabel("Your net gold offer (+ pay NPC, - NPC pays you)");box.TextBox.MaxCharacters=9;
            box.TextBox.Text=playerOfferGold.ToString(CultureInfo.InvariantCulture);
            box.OnGotUserInput+=(sender,input)=>
            {
                int value;if(!int.TryParse(input,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out value) ||
                    value<(int)-NpcInventoryLimits.MaximumWalletGold || value>NpcInventoryLimits.MaximumWalletGold)
                    SetStatus("Invalid gold offer.");
            else {playerOfferGold=value;hasPlayerOffer=true;RequestQuote();}
                RefreshView();
            };
            box.Show();
        }

        private bool SetQuantity(PersonalTradeSide side,DaggerfallUnityItem item,int quantity)
        {
            if(item==null || item.ItemGroup==ItemGroups.Currency)return false;
            if(quantity>0 && session.CanStage!=null && !session.CanStage(side,item))return false;
            bool changed=staging.TrySetQuantity(side,item.UID,item.stackCount,quantity,false);
            if(changed){hasPlayerOffer=false;playerOfferGold=0;quote=null;}
            return changed;
        }

        private void SelectionChanged()
        {
            if(staging.Lines.Count==0){ClearOffer();return;}
            RequestQuote();
            RefreshView();
        }

        private bool SetQuantity(PersonalTradeSide side,ulong uid,int quantity)
        {
            var item=(side==PersonalTradeSide.Player?session.PlayerItems:session.NpcItems).GetItem(uid);
            return SetQuantity(side,item,quantity);
        }

        private void RequestQuote()
        {
            var proposal=PersonalTradeWindowProposal.Create(session.NpcId,session.StoreEpoch,staging,playerOfferGold,hasPlayerOffer);
            if(proposal==null){SetStatus("Offer is invalid.");return;}
            PersonalTradeWindowQuote next;string reason;
            try
            {
                if(!session.Prepare(proposal,out next,out reason) || next==null)
                {SetStatus(string.IsNullOrWhiteSpace(reason)?"Offer changed.":reason);quote=null;return;}
            }
            catch(Exception error){quote=null;SetStatus(error.Message);return;}
            quote=next;
            // This is only a displayed NPC baseline until an edit or OFFER marks
            // it as a player bid.
            if(!hasPlayerOffer)playerOfferGold=next.NetGoldToNpc;
            SetStatus(string.IsNullOrWhiteSpace(next.Status)?"NPC named a total price.":next.Status);
            RefreshView();
        }

        private void SubmitOffer()
        {
            if(staging.Lines.Count==0){SetStatus("Select items first.");return;}
            if(quote==null)RequestQuote();
            if(quote==null)return;
            // OFFER explicitly accepts the displayed NPC baseline unless a
            // signed player offer was already typed.
            if(!hasPlayerOffer && quote!=null)playerOfferGold=quote.NetGoldToNpc;
            if(!hasPlayerOffer)hasPlayerOffer=true;
            RequestQuote();if(quote==null)return;
            var proposal=PersonalTradeWindowProposal.Create(session.NpcId,session.StoreEpoch,staging,playerOfferGold,hasPlayerOffer);
            try { if(session.ProposalSubmitted!=null)session.ProposalSubmitted(proposal,quote); }
            catch(Exception error){SetStatus(error.Message);return;}
            if(!quote.CanSubmit)return;
            string receiptId,reason;
            try
            {
                if(!session.Submit(proposal,quote,out receiptId,out reason) || string.IsNullOrWhiteSpace(receiptId))
                {SetStatus(string.IsNullOrWhiteSpace(reason)?"Trade was not committed.":reason);RefreshView();return;}
            }
            catch(Exception error){SetStatus(error.Message);RefreshView();return;}
            committed=true;SetStatus("Trade committed.");uiManager.PostMessage(WindowMessages.wmCloseWindow);
        }

        private void ClearOffer() { staging.Clear();playerOfferGold=0;hasPlayerOffer=false;quote=null;SetStatus("Offer cleared.");RefreshView(); }
        private void CloseUnchanged() { SetStatus("Trade cancelled.");uiManager.PostMessage(WindowMessages.wmCloseWindow); }

        private void RefreshView()
        {
            BuildPreviewRows();
            if(playerScroller!=null)playerScroller.Items=playerRows;
            if(npcScroller!=null)npcScroller.Items=npcRows;
            if(playerGoldLabel!=null)playerGoldLabel.Text="Gold: "+SafeGold(session.ReadPlayerGold);
            SetPrice(npcPriceDirection,npcQuoteLabel,quote==null?(int?)null:quote.NetGoldToNpc);
            SetPrice(offerDirection,offerLabel,quote==null && !hasPlayerOffer?(int?)null:playerOfferGold);
        }

        private static void SetPrice(TextLabel direction,TextLabel amount,int? net)
        {
            if(direction==null || amount==null)return;
            direction.Text=!net.HasValue?"":net.Value>0?"You pay":net.Value<0?"You get":"Even swap";
            amount.Text=net.HasValue?Math.Abs((long)net.Value).ToString(CultureInfo.InvariantCulture)+" g":"--";
        }

        private void BuildPreviewRows()
        {
            var source=new List<PersonalTradeSourceItem>();
            AddSource(session.PlayerItems,PersonalTradeSide.Player,source);
            AddSource(session.NpcItems,PersonalTradeSide.Npc,source);
            var preview=staging.Project(source);
            playerRows.Clear();npcRows.Clear();displayRows.Clear();
            foreach(var row in preview)
            {
                var item=SourceItem(row.SourceSide,row.ItemUid);if(item==null)continue;
                var display=new DaggerfallUnityItem(item.GetSaveData());display.stackCount=row.Quantity;
                displayRows.Add(new DisplayRow {Display=display,Preview=row});
                (row.DisplaySide==PersonalTradeSide.Player?playerRows:npcRows).Add(display);
            }
        }

        private static void AddSource(ItemCollection items,PersonalTradeSide side,List<PersonalTradeSourceItem> destination)
        {
            if(items==null)return;
            for(int index=0;index<items.Count;index++)
            {
                var item=items.GetItem(index);
                if(item!=null)destination.Add(new PersonalTradeSourceItem(side,item.UID,item.stackCount,item.ItemGroup==ItemGroups.Currency));
            }
        }

        private DisplayRow FindRow(DaggerfallUnityItem display)
        {
            foreach(var row in displayRows)if(ReferenceEquals(row.Display,display))return row;
            return null;
        }

        private DaggerfallUnityItem SourceItem(PersonalTradeSide side,ulong uid)
        { return (side==PersonalTradeSide.Player?session.PlayerItems:session.NpcItems).GetItem(uid); }
        private int SourceStackCount(PersonalTradeSide side,ulong uid)
        { var item=SourceItem(side,uid);return item==null?0:item.stackCount; }

        private int Quantity(PersonalTradeSide side,ulong uid)
        {
            foreach(var line in staging.Lines)if(line.Side==side && line.ItemUid==uid)return line.Quantity;
            return 0;
        }
        private Color PreviewColour(DaggerfallUnityItem item)
        { var row=FindRow(item);return row!=null && row.Preview.Incoming?new Color(0.20f,0.52f,0.72f,0.60f):Color.clear; }
        private string PreviewLabel(DaggerfallUnityItem item)
        { var row=FindRow(item);return row!=null && row.Preview.Incoming?"IN":""; }
        private static int SafeGold(Func<int> read) { try { return Math.Max(0,read()); }catch { return 0; } }
        private static string Clip(string text,int maximum)
        {if(string.IsNullOrWhiteSpace(text))return "NPC";text=text.Trim();return text.Length<=maximum?text:text.Substring(0,maximum-1)+".";}
        private void SetStatus(string text) { if(statusLabel!=null)statusLabel.Text=string.IsNullOrWhiteSpace(text)?string.Empty:text; }
    }
}
