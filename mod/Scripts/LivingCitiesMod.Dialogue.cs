using System;
using System.Reflection;
using System.Text;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Utility;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    // Adds one small field to DFU's own talk window. Vanilla owns the window,
    // its topic list, portrait, transcript, and all quest dialogue.
    public sealed partial class LivingCitiesMod
    {
        private static readonly FieldInfo conversationListField = typeof(DaggerfallTalkWindow).GetField(
            "listboxConversation", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo appendConversationPair = typeof(DaggerfallTalkWindow).GetMethod(
            "SetQuestionAnswerPairInConversationListbox", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo reactionToPlayerField = typeof(TalkManager).GetField(
            "reactionToPlayer", BindingFlags.Instance | BindingFlags.NonPublic);
        private TextBox dialogueInput;
        private TextLabel dialogueQuestionLabel;
        private string dialogueNativeQuestion;
        private static readonly FieldInfo dialogueQuestionField = typeof(DaggerfallTalkWindow).GetField("textlabelPlayerSays", BindingFlags.Instance | BindingFlags.NonPublic);
        private DaggerfallTalkWindow dialogueInputWindow;
        private bool dialogueWasOpen;
        private bool dialogueWindowLogged;
        private bool dialoguePrewarmQueued;
        private bool dialoguePrewarmEnabled = true;

        private void UpdateEmbeddedDialogue()
        {
            QueueDialoguePrewarm();
            DaggerfallTalkWindow talk = DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
            if (talk != null && !dialogueWindowLogged)
            {
                dialogueWindowLogged = true;
                Debug.Log("LIVING_CITIES_DIALOGUE_WINDOW: enabled=" + localDialogueEnabled +
                    "; client=" + (localDialogue != null) + "; nativePanel=" + (talk.NativePanel != null));
            }
            if (talk == null) dialogueWindowLogged = false;
            if (!localDialogueEnabled || !localDialogue || talk == null)
            {
                if (dialogueInput != null) dialogueInput.Enabled = false;
                if (dialogueQuestionLabel != null) dialogueQuestionLabel.Enabled = true;
                // A draft belongs to one conversation only. Do not carry text
                // typed for one NPC into the next native talk window.
                if (dialogueWasOpen && dialogueInput != null) dialogueInput.Text = string.Empty;
                dialogueWasOpen = false;
                return;
            }

            // The Talk window is pushed before its first Setup(). Adding controls
            // earlier places them behind the full-size background added by Setup.
            if (!talk.IsSetup) return;
            if (!dialogueWasOpen)
            {
                if (dialogueInput != null) dialogueInput.Text = string.Empty;
                BeginGroundedConversation();
            }
            dialogueWasOpen = true;
            EnsureDialogueInput(talk);
            CaptureNativeDialogueMemory();
            // Native topic selection updates this label. Copy only actual changes,
            // never overwrite a draft each frame while the player edits it.
            if (dialogueQuestionLabel.Text != dialogueNativeQuestion)
            {
                dialogueNativeQuestion = dialogueQuestionLabel.Text;
                dialogueInput.Text = dialogueNativeQuestion;
            }
            if (dialogueInput == null || !dialogueInput.HasFocus()) return;
            if (dialogueInput.IMECompositionInProgress) return;
            if (!Input.GetKeyDown(KeyCode.Return) && !Input.GetKeyDown(KeyCode.KeypadEnter)) return;

            string playerText = dialogueInput.ResultText;
            dialogueInput.Text = string.Empty;
            if (string.IsNullOrWhiteSpace(playerText)) return;
            SubmitFreeText(talk, playerText);
        }

        private void QueueDialoguePrewarm()
        {
            if (dialoguePrewarmQueued || !dialoguePrewarmEnabled || !localDialogueEnabled || !localDialogue) return;
            GameManager game = GameManager.Instance;
            if (game == null || !game.IsReady || !game.IsPlayingGame() || Time.unscaledTime < 3f) return;
            dialoguePrewarmQueued = localDialogue.Prewarm(NewDialogueConnection());
        }

        private void EnsureDialogueInput(DaggerfallTalkWindow talk)
        {
            if (dialogueInput != null && dialogueInputWindow == talk)
            {
                dialogueInput.Enabled = true;
                dialogueQuestionLabel.Enabled = false;
                return;
            }

            dialogueInputWindow = talk;
            // Preserve the classic HUD and full transcript. The native label still
            // receives selected questions, but this transparent editor renders them.
            dialogueQuestionLabel = (TextLabel)dialogueQuestionField.GetValue(talk);
            dialogueNativeQuestion = dialogueQuestionLabel.Text;
            dialogueQuestionLabel.Enabled = false;
            // TextLabel.Size shrinks to its current text. Keep the native panel's
            // full 124x38 editing bounds even when the question is empty.
            dialogueInput = new WrappedDialogueInput { Position = dialogueQuestionLabel.Position,
                Size = new Vector2(dialogueQuestionLabel.MaxWidth, 38), FixedSize = true, UseFocus = true, MaxCharacters = 220,
                Text = dialogueNativeQuestion };
            talk.NativePanel.Components.Add(dialogueInput);
            dialogueInput.Name = "LivingCitiesSayInput";
            // Native Talk buttons use letter hotkeys. Route keystrokes exclusively
            // to the textbox while focused; DFU restores hotkeys when focus leaves.
            dialogueInput.OverridesHotkeySequences = true;
            dialogueInput.TextOffset = 0;
            Debug.Log("LIVING_CITIES_DIALOGUE_INPUT: enabled=" + dialogueInput.Enabled +
                "; parentLast=" + (talk.NativePanel.Components.Count > 0 &&
                    talk.NativePanel.Components[talk.NativePanel.Components.Count - 1] == dialogueInput) +
                "; position=" + dialogueInput.Position + "; size=" + dialogueInput.Size);

        }

        private void SubmitSocialText(DaggerfallTalkWindow talk, string playerText, string history)
        {
            ResidentActor actor = GameManager.Instance.TalkManager.CurrentNPCType==TalkManager.NPCType.Mobile ? conversationActor : null;
            string npcName = GameManager.Instance.TalkManager.NameNPC;
            string race = "unknown";
            string gender = "unknown";
            string role = "settlement resident";
            int disposition = CurrentDialogueDisposition();
            if (actor && actor.Resident.IsAlive)
            {
                npcName = actor.Person.NameNPC;
                race = actor.Person.Race.ToString();
                gender = actor.Person.Gender.ToString();
                role = ResidentDialogueRole(actor);
            }
            else
            {
                var npc = GameManager.Instance.TalkManager.CurrentNPCType==TalkManager.NPCType.Mobile ? GameManager.Instance.TalkManager.MobileNPC : null;
                if (npc)
                {
                    npcName = npc.NameNPC;
                    race = npc.Race.ToString();
                    gender = npc.Gender.ToString();
                    role = npc.IsGuard ? "city guard" : "traveler";
                }
                else
                {
                    role = StaticDialogueRole();
                    var stationary = GameManager.Instance.TalkManager.CurrentNPCType==TalkManager.NPCType.Static ? GameManager.Instance.TalkManager.StaticNPC : null;
                    if (stationary) { race = stationary.Data.race.ToString(); gender = stationary.Data.gender.ToString(); }
                }
            }

            DialogueRequest request = FreeTextDialogue.Create(playerText, npcName, race, gender, role, disposition,
                conversationResponseCue + " Conversation location: " + ConversationPlaceContext() + VerifiedKeeperBusinessContext() +
                " Current authoritative game clock: " + CurrentConversationClock() +
                " " + CurrentConversationWeather() +
                CurrentConversationLifeContext(npcName, disposition) +
                CurrentConversationMemory(playerText) +
                " Current time and weather supersede earlier conversation descriptions. Do not infer visible sky conditions while indoors.", history);
            int requestSession=memorySession;
            if (!localDialogue.Request(NewDialogueConnection(), request,
                answer => { if(!CurrentMemorySession(talk,requestSession)) return;
                    conversationBusy = false;
                    if (answer == LocalDialogueClient.Unavailable) { DaggerfallUI.AddHUDText("Local dialogue unavailable. Check the local model server."); return; }
                    if (answer != LocalDialogueClient.ContextLimit) AppendRememberedDialoguePair(talk, playerText, answer,"model"); }))
            {
                conversationBusy = false;
                DaggerfallUI.AddHUDText("Waiting for the current reply.");
            }
        }

        private static string CurrentConversationClock()
        {
            var now = DaggerfallUnity.Instance.WorldTime.Now;
            return ConversationClock.Answer(now.MinTimeString(), now.DateString(), now.Year);
        }

        private static string CurrentConversationWeather()
        {
            var game = GameManager.Instance;
            string settlement = game.PlayerGPS.HasCurrentLocation ? game.PlayerGPS.CurrentLocation.Name : string.Empty;
            string region = game.PlayerGPS.CurrentRegionName;
            var playerWeather = game.WeatherManager.PlayerWeather;
            string weather = playerWeather == null ? string.Empty : playerWeather.WeatherType.ToString();
            return ConversationWeather.Context(settlement, region, weather);
        }

        private string VerifiedKeeperBusinessContext()
        {
            var game = GameManager.Instance;
            if (game == null || !game.PlayerEnterExit.IsPlayerInsideBuilding) return string.Empty;
            string ownedPlace;
            if (!IsConversationAtOwnedPlace(out ownedPlace)) return string.Empty;
            BuildingSummary summary;
            int settlement, building;
            if (!TryConversationBuilding(out settlement, out building, out summary) ||
                (summary.BuildingType != DFLocation.BuildingTypes.Tavern && !RMBLayout.IsShop(summary.BuildingType)))
                return string.Empty;
            var keeper = FindConversationNative(settlement);
            if (keeper == null || !keeper.IsAlive || keeper.WorkKey != building || keeper.Id != conversationPersonId)
                return string.Empty;
            string businessName = game.PlayerEnterExit.BuildingDiscoveryData.displayName;
            if (string.IsNullOrWhiteSpace(businessName)) return string.Empty;
            return " Your business is called " + businessName.Trim() + ".";
        }

        private static string ReadConversationHistory(DaggerfallTalkWindow talk)
        {
            var transcript = talk == null || conversationListField == null ? null : conversationListField.GetValue(talk) as ListBox;
            if (transcript == null) return string.Empty;
            var history = new StringBuilder();
            for (int index = 0; index < transcript.Count; index++)
            {
                var item = transcript.GetItem(index);
                if (item == null || item.textLabel == null || string.IsNullOrWhiteSpace(item.textLabel.Text)) continue;
                history.Append(item.textColor == DaggerfallUI.DaggerfallQuestionTextColor ? "Player: " : "NPC: ");
                history.AppendLine(item.textLabel.Text.Replace("\r", " ").Replace("\n", " "));
            }
            return history.ToString();
        }

        private LocalDialogueConnection NewDialogueConnection()
        {
            return new LocalDialogueConnection("http://127.0.0.1:8080/completion", localDialogueTimeoutMilliseconds);
        }

        private int CurrentDialogueDisposition()
        {
            TalkManager talk = GameManager.Instance == null ? null : GameManager.Instance.TalkManager;
            if (talk == null || reactionToPlayerField == null) return 0;
            object value = reactionToPlayerField.GetValue(talk);
            return value is int ? Mathf.Clamp((int)value, -100, 100) : 0;
        }

        private string ResidentDialogueRole(ResidentActor actor)
        {
            if (actor.Resident.AgeGroup == ResidentAgeGroup.Child) return "local child";
            if (actor.IsSleeping) return "sleeping local resident";
            if (actor.IsConversing) return "local resident in conversation";
            if (streets == null) return "local resident";
            Activity activity = streets.GetState(actor.Resident.Id,
                DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()).Activity;
            switch (activity)
            {
                case Activity.Sleep: return "sleeping local resident";
                case Activity.Work: return "local resident at work";
                case Activity.Visit: return "local resident visiting";
                case Activity.Travel: return "local resident travelling";
                default: return "local resident";
            }
        }

        private string StaticDialogueRole()
        {
            TalkManager talk = GameManager.Instance == null ? null : GameManager.Instance.TalkManager;
            var npc = talk == null || talk.CurrentNPCType!=TalkManager.NPCType.Static ? null : talk.StaticNPC;
            if (!npc) return "settlement resident";
            if (npc.IsChildNPC) return "local child";
            int town,building;BuildingSummary summary;
            if(TryConversationBuilding(out town,out building,out summary))
            {
                if(NativeBusinessServices.IsKeeper(npc,summary.BuildingType))
                    return summary.BuildingType==DFLocation.BuildingTypes.Tavern ? "innkeeper offering room rental" : "shopkeeper offering merchant services";
                var person=FindConversationNative(town);
                if(RMBLayout.IsShop(summary.BuildingType) && person!=null && person.HomeKey==building && person.BedSlot>=0)
                    return "residential tenant above the shop, not its keeper or owner";
                if(summary.BuildingType==DFLocation.BuildingTypes.Tavern)
                    return person!=null && store.NativeLodgingBed(person.Id)!=null ? "inn bedroom lodger; permanent origin not yet verified; not the keeper" :
                        "inn occupant; local staff, local patron or visiting status not yet verified; not the keeper";
                if(RMBLayout.IsShop(summary.BuildingType)) return "shop visitor, not its keeper or owner";
            }
            FactionFile.FactionData faction;
            if (GameManager.Instance.PlayerEntity.FactionData.GetFactionData(npc.Data.factionID, out faction))
                return ((FactionFile.SocialGroups)faction.sgroup).ToString().ToLowerInvariant() + " resident";
            return "settlement resident";
        }

        private void AppendDialoguePair(DaggerfallTalkWindow talk, string playerText, string answer)
        {
            AppendRememberedDialoguePair(talk,playerText,answer,"action");
        }
    }
}
