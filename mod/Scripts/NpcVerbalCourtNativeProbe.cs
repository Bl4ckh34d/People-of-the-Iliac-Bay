using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public static class NpcVerbalCourtNativeProbe
    {
        public const string RunFlag="-peopleoftheiliacbay-verbal-court-probe";
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private static void Need(bool value,string message) {if(!value)throw new InvalidOperationException("Verbal court: "+message);}
        private static WorldStateStore Store(PeopleOfTheIliacBayMod host)
        {return (WorldStateStore)typeof(PeopleOfTheIliacBayMod).GetField("store",Private).GetValue(host);}
        private static int CourtInt(string name)
        {return (int)typeof(DaggerfallCourtWindow).GetField(name,Private).GetValue(DaggerfallUI.Instance.DfCourtWindow);}

        public static IEnumerator Run(PeopleOfTheIliacBayMod host,StreetPopulation streets,string character,string save)
        {
            Need(Array.IndexOf(Environment.GetCommandLineArgs(),RunFlag)>=0 && character=="Hakon Ironhand",
                "explicit owned fixture required");
            yield return ConversationMoodProbe.LoadSave(host,streets,character,save,false);
            var game=GameManager.Instance;var player=game.PlayerEntity;
            bool god=player.GodMode;player.GodMode=true;var random=UnityEngine.Random.state;
            try
            {
            if(game.PlayerEnterExit.IsPlayerInsideBuilding)game.PlayerEnterExit.TransitionExterior(true);
            float deadline=Time.realtimeSinceStartup+90;
            while((!streets.Ready || !streets.EntranceSurveyReady || game.PlayerEnterExit.IsPlayerInside) && Time.realtimeSinceStartup<deadline)yield return null;
            Need(streets.Ready && streets.EntranceSurveyReady && !game.PlayerEnterExit.IsPlayerInside,"town survey not ready");
            var clock=DaggerfallUnity.Instance.WorldTime.Now;double loadedAt=clock.ToSeconds();
            double noon=Math.Floor(loadedAt/86400)*86400+12*3600;if(noon<loadedAt)noon+=86400;
            clock.RaiseTime((float)(noon-loadedAt));
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_VERBAL_COURT_FIXTURE: loadedTime="+loadedAt+"; testAtNoon="+noon);
            var store=Store(host);var guards=host.GetComponent<GuardPopulation>();
            ResidentActor witness=null;float shortest=float.PositiveInfinity;
            deadline=Time.realtimeSinceStartup+60;
            do
            {
            // The town survey completes before native guards and street bodies finish their first update.
            DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
            yield return new WaitForSecondsRealtime(0.25f);
            foreach(var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
            {
                if(!actor.gameObject.activeInHierarchy || !actor.Resident.IsAlive || actor.Resident.IsQuestProtected ||
                    actor.Resident.AgeGroup!=ResidentAgeGroup.Adult || actor.IsConversing || actor.IsSleeping)continue;
                string guard;Vector3 position;List<SavedRoutePoint> route;
                if(!guards.TryFindNearestComplaintGuard(actor.transform.position-Vector3.up,out guard,out position,out route))continue;
                float length=0;for(int i=1;i<route.Count;i++)length+=Vector2.Distance(new Vector2((float)route[i-1].X,(float)route[i-1].Z),
                    new Vector2((float)route[i].X,(float)route[i].Z));
                if(length>2 && length<shortest){shortest=length;witness=actor;}
            }
            }while(!witness && Time.realtimeSinceStartup<deadline);
            Need(witness,"no actual resident can reach a posted guard; actors="+UnityEngine.Object.FindObjectsOfType<ResidentActor>().Length+
                "; guardOwnsTown="+guards.OwnsSettlement+"; paused="+GameManager.IsGamePaused);
                player.CrimeCommitted=PlayerEntity.Crimes.None;
                game.PlayerMotor.transform.position=witness.transform.position+Vector3.back*4;Physics.SyncTransforms();
                double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();string id=witness.Resident.Id;
                NpcObservedSpeechSession session;NpcObservedSpeechTicket ticket=null;NpcSpeechActClassification classification;NpcConversationBoundaryRecord boundary=null;
                const string text="I will hurt you.";object source=new object();
                Need(store.TryBeginNpcObservedSpeechSession(id,"verbal-court-native",out session) &&
                    store.TryIssueNpcObservedSpeech(session,1,text,now,out ticket),"speech receipt not issued");
                Need(NpcSpeechActClassification.TryAccept(NpcSpeechActRequest.Create(source,id,"player",null,text,""),
                    "DirectThreat",out classification) && store.TryCommitNpcConversationBoundary(ticket,ticket.Receipt,source,classification,out boundary),
                    "controlled threat not committed");
                Need(store.TryAcceptNpcObservedSpeechAfterAppend(ticket,ticket.Receipt,NpcExperienceAppliedEffectsMask.None)!=
                    NpcObservedSpeechAcceptanceResult.Rejected,"speech not accepted");
                Need(streets.StartDialogueComplaint(witness,id,boundary.ReceiptId),"physical complaint not started");
                Vector3 previous=witness.transform.position;float walked=0;deadline=Time.realtimeSinceStartup+180;
                while(store.ReadNpcVerbalCharge()==null && Time.realtimeSinceStartup<deadline)
                {
                    if(witness){walked+=Vector3.Distance(previous,witness.transform.position);previous=witness.transform.position;}
                    yield return null;
                }
                Need(store.ReadNpcVerbalCharge()!=null && walked>0.25f && player.CrimeCommitted==PlayerEntity.Crimes.Assault,
                    "runner did not deliver the actual verbal-only charge");
                bool oldStreets=streets.enabled,oldGuards=guards.enabled;streets.enabled=false;guards.enabled=false;
                try
                {
                    var charged=(WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot()));
                    Need(charged.NpcVerbalCharge.ReceiptId==boundary.ReceiptId,"native serializer lost charge provenance");
                    player.CrimeCommitted=PlayerEntity.Crimes.Assault;
                    Need(store.ReadNpcVerbalCharge()==null,"same-valued native crime update preserved the exception");
                    store.Restore(charged);
                    var body=witness.GetComponent<DaggerfallEntityBehaviour>();int hp=body.Entity.CurrentHealth;
                    body.DamageHealthFromSource(game.PlayerEntityBehaviour,1,false,Vector3.zero);
                    Need(body.Entity.CurrentHealth==hp-1 && store.ReadNpcVerbalCharge()==null,"actual physical assault retained verbal-only sentencing");
                    player.FillVitalSigns();Need(player.SurrenderToCityGuards(true),"physical-assault surrender failed");
                    yield return WaitForCourt();
                    Need(typeof(NpcVerbalCourtWindow).GetField("chargeReceipt",Private).GetValue(DaggerfallUI.Instance.DfCourtWindow)==null &&
                        (CourtInt("daysInPrison")>0 || CourtInt("fine")>40 || CourtInt("punishmentType")!=2),"actual assault did not use ordinary native sentencing");
                    DaggerfallUI.Instance.PopToHUD();player.CrimeCommitted=PlayerEntity.Crimes.None;player.FillVitalSigns();
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_VERBAL_COURT_HARM_PASS: actualNativeDamage=true; sameAssaultClears=true; ordinaryNativeCourt=true");
                    foreach(string outcome in new[]{"guilty","debate-dismissed","lie-fined"})
                    {
                        DaggerfallUI.Instance.PopToHUD();player.FillVitalSigns();player.CrimeCommitted=PlayerEntity.Crimes.Assault;
                        store.Restore(charged);player.GoldPieces+=100;
                        int gold=player.GetGoldAmount();int region=game.PlayerGPS.CurrentRegionIndex;
                        player.RegionData[region].LegalRep=0;
                        Need(player.SurrenderToCityGuards(true),"native voluntary surrender failed");
                        yield return WaitForCourt();
                        Need(DaggerfallUI.Instance.DfCourtWindow is NpcVerbalCourtWindow && CourtInt("daysInPrison")==0 && CourtInt("fine")==40,
                            "native court did not recognize the receipt-bound charge");
                        if(outcome=="guilty")
                        {
                            string screenshot=System.IO.Path.Combine(Application.persistentDataPath,"PeopleOfTheIliacBay-verbal-court.png");
                            yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(screenshot);
                            yield return new WaitForSecondsRealtime(0.5f);
                            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_VERBAL_COURT_SCREENSHOT: "+screenshot);
                        }
                        if(outcome=="guilty")Click(DaggerfallMessageBox.MessageBoxButtons.Guilty);
                        else
                        {
                            Click(DaggerfallMessageBox.MessageBoxButtons.NotGuilty);yield return null;
                            bool debate=outcome=="debate-dismissed";
                            var skill=debate?DFCareer.Skills.Etiquette:DFCareer.Skills.Streetwise;
                            int before=player.SkillUses[(int)skill];
                            int chance=NpcVerbalJusticePolicy.DismissalChance(player.Skills.GetLiveSkillValue(skill),
                                player.Stats.GetLiveStatValue(DFCareer.Stats.Personality),player.RegionData[region].LegalRep);
                            SeedRoll(chance,debate);Click(debate?DaggerfallMessageBox.MessageBoxButtons.Debate:DaggerfallMessageBox.MessageBoxButtons.Lie);
                            Need(player.SkillUses[(int)skill]==before+1,"native court skill was not tallied once");
                        }
                        yield return WaitForRelease();
                        int paid=gold-player.GetGoldAmount();
                        Need(paid==(outcome=="guilty"?20:outcome=="lie-fined"?40:0),"incorrect native fine: "+outcome+" paid="+paid);
                        Need(!player.InPrison && !player.Arrested && store.ReadNpcVerbalCharge()==null,"court did not release and clear the case");
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_VERBAL_COURT_CASE_PASS: outcome="+outcome+"; paid="+paid+"; prisonDays=0; nativeSurrender=true; nativeButtons=true");
                    }
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_VERBAL_COURT_NATIVE_PASS: physicalReport=true; nativeSerializer=true; guilty20=true; dismissal=true; failedContest40=true; nativeSkills=true; noPrison=true; sameAssaultClears=true; actualHarmUsesNativeCourt=true");
                }
                finally {streets.enabled=oldStreets;guards.enabled=oldGuards;}
            }
            finally {player.GodMode=god;UnityEngine.Random.state=random;}
        }
        private static IEnumerator WaitForCourt()
        {
            float deadline=Time.realtimeSinceStartup+15;
            while(!(DaggerfallUI.UIManager.TopWindow is DaggerfallMessageBox) && Time.realtimeSinceStartup<deadline)yield return null;
            Need(DaggerfallUI.UIManager.ContainsWindow(DaggerfallUI.Instance.DfCourtWindow) && DaggerfallUI.UIManager.TopWindow is DaggerfallMessageBox,"native court did not open");
        }
        private static IEnumerator WaitForRelease()
        {
            float deadline=Time.realtimeSinceStartup+20;
            while(DaggerfallUI.UIManager.ContainsWindow(DaggerfallUI.Instance.DfCourtWindow) && Time.realtimeSinceStartup<deadline)
            {
                var box=DaggerfallUI.UIManager.TopWindow as DaggerfallMessageBox;
                if(box!=null && box.ClickAnywhereToClose)box.CloseWindow();
                yield return null;
            }
            Need(!DaggerfallUI.UIManager.ContainsWindow(DaggerfallUI.Instance.DfCourtWindow),"native release timed out");
        }
        private static void Click(DaggerfallMessageBox.MessageBoxButtons choice)
        {
            var box=DaggerfallUI.UIManager.TopWindow as DaggerfallMessageBox;Need(box!=null,"no native message box");
            var buttons=(List<Button>)typeof(DaggerfallMessageBox).GetField("buttons",Private).GetValue(box);
            var button=buttons.Find(b=>Equals(b.Tag,choice));Need(button!=null,"native button missing: "+choice);button.TriggerMouseClick();
        }
        private static void SeedRoll(int chance,bool success)
        {
            for(int seed=0;seed<10000;seed++)
            {UnityEngine.Random.InitState(seed);if((UnityEngine.Random.Range(0,100)<chance)==success){UnityEngine.Random.InitState(seed);return;}}
            throw new InvalidOperationException("No controlled native dice roll.");
        }
    }
}
