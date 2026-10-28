using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // The native window still owns time, confirmation, affordability, cancel
    // and its single debit. Only the native solo fare is scaled for this party.
    public sealed class NativeCompanionTravelPopUp : DaggerfallTravelPopUp
    {
        private sealed class PartyCalculator : TravelTimeCalculator
        {
            internal int Companions;
            internal bool Valid {get{int total,pieces;return NpcCompanionFastTravel.TryPrice(base.TotalCost,base.PiecesCost,Companions,out total,out pieces);}}
            internal int SoloTotal {get{return base.TotalCost;}}
            internal int SoloPieces {get{return base.PiecesCost;}}
            public override int TotalCost {get{int total,pieces;return NpcCompanionFastTravel.TryPrice(base.TotalCost,base.PiecesCost,Companions,out total,out pieces)?total:int.MaxValue;}}
            public override int PiecesCost {get{int total,pieces;return NpcCompanionFastTravel.TryPrice(base.TotalCost,base.PiecesCost,Companions,out total,out pieces)?pieces:int.MaxValue;}}
        }
        private readonly PartyCalculator calculator=new PartyCalculator();
        private CompanionFastTravelQuote quote,confirmed;
        private int destinationX,destinationY,confirmedTotal,confirmedPieces;
        private bool cautious,ship,inn;
        private bool departed;
        internal CompanionFastTravelQuote ConfirmedQuote {get{return confirmed;}}
        internal int GoldBeforeTravel {get;private set;}
        internal int ConfirmedTotal {get{return confirmedTotal;}}
        public NativeCompanionTravelPopUp(IUserInterfaceManager manager,IUserInterfaceWindow previous=null,DaggerfallTravelMapWindow map=null)
            :base(manager,previous,map){travelTimeCalculator=calculator;}
        internal void RecordNativeDeparture(){departed=true;}
        public override void OnPush(){confirmed=null;departed=false;base.OnPush();}
        public override void OnPop()
        {
            if(!departed && RoadTravelActors.FastTravelOwner)RoadTravelActors.FastTravelOwner.AbortFastTravelQuote(confirmed);
            confirmed=null;base.OnPop();
        }

        protected override void UpdateLabels()
        {
            if(!doFastTravel)
            {
                var owner=RoadTravelActors.FastTravelOwner;
                quote=owner?owner.CaptureFastTravelQuote():new CompanionFastTravelQuote();
                calculator.Companions=quote.Members.Count;
            }
            base.UpdateLabels();
        }
        protected override bool enoughGoldCheck()
        {return calculator.Valid && base.enoughGoldCheck();}
        protected override void CallFastTravelGoldCheck()
        {
            var owner=RoadTravelActors.FastTravelOwner;
            if(!calculator.Valid){DaggerfallUI.MessageBox("This journey cannot be quoted for the current party.");return;}
            if(owner && !owner.FastTravelQuoteMatches(quote))
            {Refresh();DaggerfallUI.MessageBox("Your traveling companions changed. Review the journey price and begin again.");return;}
            if(!enoughGoldCheck()){showNotEnoughGoldPopup();return;}
            // Fallible inventory capture and eligibility checks precede native
            // confirmation/debit. No wage or additional fare is charged here.
            if(owner && !owner.PrepareFastTravelQuote(quote))
            {Refresh();DaggerfallUI.MessageBox("Your companions are not ready to travel. Review the party and begin again.");return;}
            confirmed=quote;destinationX=EndPos.X;destinationY=EndPos.Y;cautious=SpeedCautious;ship=TravelShip;inn=SleepModeInn;
            confirmedTotal=calculator.TotalCost;confirmedPieces=calculator.PiecesCost;
            GoldBeforeTravel=GameManager.Instance.PlayerEntity.GetGoldAmount();
            base.CallFastTravelGoldCheck();
            if(!doFastTravel){if(owner)owner.AbortFastTravelQuote(confirmed);confirmed=null;}
            else Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAST_TRAVEL_QUOTE: companions="+calculator.Companions+
                "; soloTotal="+calculator.SoloTotal+"; soloCoins="+calculator.SoloPieces+"; total="+confirmedTotal+
                "; coins="+confirmedPieces+"; nativeSingleDebit=true; companionWagesSeparate=true");
        }
        public override void Update()
        {
            if(doFastTravel)
            {
                var owner=RoadTravelActors.FastTravelOwner;
                if(!enoughGoldCheck())
                {
                    doFastTravel=false;if(owner)owner.AbortFastTravelQuote(confirmed);confirmed=null;Refresh();showNotEnoughGoldPopup();return;
                }
                if(confirmed==null || owner && !owner.FastTravelQuoteMatches(confirmed) || EndPos.X!=destinationX || EndPos.Y!=destinationY ||
                    SpeedCautious!=cautious || TravelShip!=ship || SleepModeInn!=inn ||
                    calculator.TotalCost!=confirmedTotal || calculator.PiecesCost!=confirmedPieces)
                {
                    doFastTravel=false;if(owner)owner.AbortFastTravelQuote(confirmed);confirmed=null;Refresh();
                    DaggerfallUI.MessageBox("The travel party or journey changed. Review the price and begin again.");return;
                }
            }
            base.Update();
        }
        public override void BeginButtonOnClickHandler(BaseScreenComponent sender,Vector2 position)
        {
            if(doFastTravel)return;var owner=RoadTravelActors.FastTravelOwner;
            // Compare the displayed party before native Begin refreshes labels.
            if(owner && !owner.FastTravelQuoteMatches(quote))
            {Refresh();DaggerfallUI.MessageBox("Your traveling companions changed. Review the journey price and begin again.");return;}
            base.BeginButtonOnClickHandler(sender,position);
        }
        public override void SpeedButtonOnClickHandler(BaseScreenComponent sender,Vector2 position)
        {if(!doFastTravel)base.SpeedButtonOnClickHandler(sender,position);}
        public override void ToggleSpeedButtonOnScrollHandler(BaseScreenComponent sender)
        {if(!doFastTravel)base.ToggleSpeedButtonOnScrollHandler(sender);}
        public override void TransportModeButtonOnClickHandler(BaseScreenComponent sender,Vector2 position)
        {if(!doFastTravel)base.TransportModeButtonOnClickHandler(sender,position);}
        public override void ToggleTransportModeButtonOnScrollHandler(BaseScreenComponent sender)
        {if(!doFastTravel)base.ToggleTransportModeButtonOnScrollHandler(sender);}
        public override void SleepModeButtonOnClickHandler(BaseScreenComponent sender,Vector2 position)
        {if(!doFastTravel)base.SleepModeButtonOnClickHandler(sender,position);}
        public override void ToggleSleepModeButtonOnScrollHandler(BaseScreenComponent sender)
        {if(!doFastTravel)base.ToggleSleepModeButtonOnScrollHandler(sender);}
    }
}
