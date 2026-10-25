using DaggerfallConnect;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed class NpcVerbalCourtWindow : DaggerfallCourtWindow
    {
        private WorldStateStore chargeStore;
        private string chargeReceipt;
        public NpcVerbalCourtWindow(IUserInterfaceManager manager,IUserInterfaceWindow previous=null):base(manager,previous) {}

        protected override void HandleCourtLogic()
        {
            if(state==0 && chargeReceipt==null)
            {
                var owner=NativeVerbalJustice.Current;
                var candidate=owner?owner.CourtStore():null;
                var charge=candidate==null?null:candidate.ReadNpcVerbalCharge();
                if(charge!=null)
                {
                    chargeStore=candidate;chargeReceipt=charge.ReceiptId;
                    regionIndex=GameManager.Instance.PlayerGPS.CurrentRegionIndex;
                    punishmentType=2;daysInPrison=0;fine=Mathf.Min(NpcVerbalJusticePolicy.FineGold,playerEntity.GetGoldAmount());
                    var message=Message();
                    message.SetText("You are accused of making a threat. There is no corroborating witness. How do you plead?");
                    message.AddButton(DaggerfallMessageBox.MessageBoxButtons.Guilty);
                    message.AddButton(DaggerfallMessageBox.MessageBoxButtons.NotGuilty);
                    message.OnButtonClick+=GuiltyNotGuilty_OnButtonClick;
                    state=1;uiManager.PushWindow(message);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_VERBAL_COURT_OPEN: receipt="+chargeReceipt+"; fine="+fine+"; prisonDays=0");
                    return;
                }
            }
            base.HandleCourtLogic();
        }

        protected override void DebateLie_OnButtonClick(DaggerfallMessageBox sender,DaggerfallMessageBox.MessageBoxButtons button)
        {
            if(chargeReceipt==null){base.DebateLie_OnButtonClick(sender,button);return;}
            sender.CloseWindow();
            var skill=button==DaggerfallMessageBox.MessageBoxButtons.Debate?DFCareer.Skills.Etiquette:DFCareer.Skills.Streetwise;
            int chance=NpcVerbalJusticePolicy.DismissalChance(playerEntity.Skills.GetLiveSkillValue(skill),
                playerEntity.Stats.GetLiveStatValue(DFCareer.Stats.Personality),playerEntity.RegionData[regionIndex].LegalRep);
            playerEntity.TallySkill(skill,1);
            bool dismissed=!Dice100.FailedRoll(chance);
            if(dismissed)
            {
                var message=Message();message.ClickAnywhereToClose=true;
                message.SetTextTokens(DaggerfallUnity.Instance.TextProvider.GetRSCTokens(courtTextFreeToGo));
                uiManager.PushWindow(message);playerEntity.FillVitalSigns();playerEntity.RaiseReputationForDoingSentence();state=6;
            }
            else
            {
                fine=Mathf.Min(NpcVerbalJusticePolicy.FineGold,playerEntity.GetGoldAmount());
                daysInPrison=0;punishmentType=2;playerEntity.DeductGoldAmount(fine);state=2;
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_VERBAL_COURT_VERDICT: receipt="+chargeReceipt+"; skill="+skill+
                "; chance="+chance+"; dismissed="+dismissed+"; fine="+(dismissed?0:fine)+"; prisonDays=0");
            Update();
        }

        private DaggerfallMessageBox Message()
        {
            var message=new DaggerfallMessageBox(uiManager,this,false,149);
            message.ScreenDimColor=new Color32(0,0,0,0);message.ParentPanel.VerticalAlignment=VerticalAlignment.Bottom;
            message.AllowCancel=false;return message;
        }
        public override void OnPop()
        {
            if(chargeStore!=null && chargeStore.ReadNpcVerbalCharge()?.ReceiptId==chargeReceipt)chargeStore.ClearNpcVerbalCharge();
            chargeStore=null;chargeReceipt=null;base.OnPop();
        }
    }
}
