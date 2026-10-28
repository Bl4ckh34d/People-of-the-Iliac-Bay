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
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Adds one small field to DFU's own talk window. Vanilla owns the window,
    // its topic list, portrait, transcript, and all quest dialogue.
    public sealed partial class PeopleOfTheIliacBayMod
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
        private DaggerfallTalkWindow agenticToneWindow;
        private DaggerfallTalkWindow.TalkTone agenticToneBeforeSuppression;
        private bool agenticToneSuppressed;
        private bool[] agenticToneButtonEnabledBeforeSuppression;
        private bool agenticToneMarkerEnabledBeforeSuppression;
        private Texture2D agenticToneBackgroundBeforeSuppression;
        private Texture2D agenticDialogueBackground;
        private bool agenticDialogueBackgroundLoadFailed;
        private static readonly FieldInfo agenticSelectedToneField = typeof(DaggerfallTalkWindow).GetField(
            "selectedTalkTone", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo agenticToneBackgroundField = typeof(DaggerfallTalkWindow).GetField(
            "textureBackground", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo agenticMainPanelField = typeof(DaggerfallTalkWindow).GetField(
            "mainPanel", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo agenticToneMarkerField = typeof(DaggerfallTalkWindow).GetField(
            "panelTone", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo agenticTonePoliteButtonField = typeof(DaggerfallTalkWindow).GetField(
            "buttonCheckboxTonePolite", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo agenticToneNormalButtonField = typeof(DaggerfallTalkWindow).GetField(
            "buttonCheckboxToneNormal", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo agenticToneBluntButtonField = typeof(DaggerfallTalkWindow).GetField(
            "buttonCheckboxToneBlunt", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo agenticTopicListField = typeof(DaggerfallTalkWindow).GetField(
            "listboxTopic", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo agenticUpdateQuestion = typeof(DaggerfallTalkWindow).GetMethod(
            "UpdateQuestion", BindingFlags.Instance | BindingFlags.NonPublic);

        private void UpdateEmbeddedDialogue()
        {
            if(TryRedirectNativeMarketTalk())return;
            QueueDialoguePrewarm();
            DaggerfallTalkWindow talk = DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
            UpdateNativeTopicTtsSession(talk);
            bool agenticDialogueMode = AgenticDialogueUiPolicy.SuppressNativeToneChooser(
                localDialogueEnabled, localDialogue != null);
            if(talk!=null && talk.IsSetup) TryOpenPendingContact(talk);
            UpdateConversationPendingIndicator(talk);
            if (talk == null)
                RestoreAgenticToneChooser();
            else if (talk.IsSetup)
                SetAgenticToneChooser(talk, agenticDialogueMode);
            bool agenticDialogueActive = agenticDialogueMode && agenticToneSuppressed && agenticToneWindow == talk;
            if (talk != null && talk.IsSetup && dialogueInputWindow == talk)
                UpdateDialogueInputBounds(talk, agenticDialogueActive);
            if (talk != null && !dialogueWindowLogged)
            {
                dialogueWindowLogged = true;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_WINDOW: enabled=" + localDialogueEnabled +
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
            if (!dialogueWasOpen || !conversationSessionOpen)
            {
                if (dialogueInput != null) dialogueInput.Text = string.Empty;
                BeginGroundedConversation();
            }
            dialogueWasOpen = true;
            EnsureDialogueInput(talk);
            CaptureNativeDialogueMemory();
            RequestReturningGreeting(talk);
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

        private void SetAgenticToneChooser(DaggerfallTalkWindow talk, bool suppress)
        {
            if (agenticToneSuppressed && agenticToneWindow != talk)
                RestoreAgenticToneChooser();
            if (!suppress)
            {
                if (agenticToneWindow == talk) RestoreAgenticToneChooser();
                return;
            }
            if (talk == null) return;
            if (!agenticToneSuppressed)
            {
                var mainPanel = agenticMainPanelField == null ? null : agenticMainPanelField.GetValue(talk) as Panel;
                var originalBackground = agenticToneBackgroundField == null ? null : agenticToneBackgroundField.GetValue(talk) as Texture2D;
                agenticDialogueBackground = LoadAgenticDialogueBackground(originalBackground);
                if (mainPanel == null || agenticDialogueBackground == null) return;

                agenticToneWindow = talk;
                agenticToneBeforeSuppression = agenticSelectedToneField == null
                    ? DaggerfallTalkWindow.TalkTone.Normal
                    : (DaggerfallTalkWindow.TalkTone)agenticSelectedToneField.GetValue(talk);
                var buttons = AgenticToneButtons(talk);
                agenticToneButtonEnabledBeforeSuppression = new bool[buttons.Length];
                for (int i = 0; i < buttons.Length; i++)
                    agenticToneButtonEnabledBeforeSuppression[i] = buttons[i] != null && buttons[i].Enabled;
                var initialMarker = agenticToneMarkerField == null ? null : agenticToneMarkerField.GetValue(talk) as BaseScreenComponent;
                agenticToneMarkerEnabledBeforeSuppression = initialMarker != null && initialMarker.Enabled;
                agenticToneBackgroundBeforeSuppression = mainPanel.BackgroundTexture;
                mainPanel.BackgroundTexture = agenticDialogueBackground;
                agenticToneSuppressed = true;
            }

            if (agenticSelectedToneField != null &&
                (DaggerfallTalkWindow.TalkTone)agenticSelectedToneField.GetValue(talk) != DaggerfallTalkWindow.TalkTone.Normal)
            {
                agenticSelectedToneField.SetValue(talk, DaggerfallTalkWindow.TalkTone.Normal);
                RefreshAgenticNativeQuestion(talk);
            }
            foreach (var button in AgenticToneButtons(talk))
                if (button != null) button.Enabled = false;
            var toneMarker = agenticToneMarkerField == null ? null : agenticToneMarkerField.GetValue(talk) as BaseScreenComponent;
            if (toneMarker != null) toneMarker.Enabled = false;
        }

        private void RestoreAgenticToneChooser()
        {
            if (!agenticToneSuppressed) return;
            var talk = agenticToneWindow;
            if (talk != null)
            {
                if (agenticSelectedToneField != null)
                    agenticSelectedToneField.SetValue(talk, agenticToneBeforeSuppression);
                var buttons = AgenticToneButtons(talk);
                for (int i = 0; i < buttons.Length; i++)
                    if (buttons[i] != null && agenticToneButtonEnabledBeforeSuppression != null &&
                        i < agenticToneButtonEnabledBeforeSuppression.Length)
                        buttons[i].Enabled = agenticToneButtonEnabledBeforeSuppression[i];
                var toneMarker = agenticToneMarkerField == null ? null : agenticToneMarkerField.GetValue(talk) as BaseScreenComponent;
                if (toneMarker != null) toneMarker.Enabled = agenticToneMarkerEnabledBeforeSuppression;
                var mainPanel = agenticMainPanelField == null ? null : agenticMainPanelField.GetValue(talk) as Panel;
                if (mainPanel != null) mainPanel.BackgroundTexture = agenticToneBackgroundBeforeSuppression;
                if (talk.IsSetup && DaggerfallUI.UIManager.TopWindow == talk)
                    RefreshAgenticNativeQuestion(talk);
            }
            agenticToneWindow = null;
            agenticToneButtonEnabledBeforeSuppression = null;
            agenticToneMarkerEnabledBeforeSuppression = false;
            agenticToneBackgroundBeforeSuppression = null;
            agenticToneSuppressed = false;
        }

        private static Button[] AgenticToneButtons(DaggerfallTalkWindow talk)
        {
            return new[] {
                agenticTonePoliteButtonField == null ? null : agenticTonePoliteButtonField.GetValue(talk) as Button,
                agenticToneNormalButtonField == null ? null : agenticToneNormalButtonField.GetValue(talk) as Button,
                agenticToneBluntButtonField == null ? null : agenticToneBluntButtonField.GetValue(talk) as Button,
            };
        }

        private static void RefreshAgenticNativeQuestion(DaggerfallTalkWindow talk)
        {
            if (agenticUpdateQuestion == null || agenticTopicListField == null) return;
            var topics = agenticTopicListField.GetValue(talk) as ListBox;
            if (topics != null) agenticUpdateQuestion.Invoke(talk, new object[] { topics.SelectedIndex });
        }

        private Texture2D LoadAgenticDialogueBackground(Texture2D originalBackground)
        {
            if (agenticDialogueBackground != null) return agenticDialogueBackground;
            if (agenticDialogueBackgroundLoadFailed || mod == null || originalBackground == null) return null;
            const string assetName = "agentic-talk-background.png.bytes";
            var asset = mod.GetAsset<TextAsset>(assetName);
            if (!asset)
            {
                agenticDialogueBackgroundLoadFailed = true;
                Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_AGENTIC_DIALOGUE_BACKGROUND: missing " + assetName + ".");
                return null;
            }
            var texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            if (!texture.LoadImage(asset.bytes) || texture.width != originalBackground.width || texture.height != originalBackground.height)
            {
                UnityEngine.Object.Destroy(texture);
                agenticDialogueBackgroundLoadFailed = true;
                Debug.LogError("PEOPLE_OF_THE_ILIAC_BAY_AGENTIC_DIALOGUE_BACKGROUND: invalid dimensions or PNG data.");
                return null;
            }
            texture.name = "AgenticDialogueBackground";
            texture.filterMode = originalBackground.filterMode;
            texture.wrapMode = TextureWrapMode.Clamp;
            agenticDialogueBackground = texture;
            return agenticDialogueBackground;
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
                UpdateDialogueInputBounds(talk, agenticToneSuppressed && agenticToneWindow == talk);
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
            dialogueInput.Name = "PeopleOfTheIliacBaySayInput";
            // Native Talk buttons use letter hotkeys. Route keystrokes exclusively
            // to the textbox while focused; DFU restores hotkeys when focus leaves.
            dialogueInput.OverridesHotkeySequences = true;
            dialogueInput.TextOffset = 0;
            UpdateDialogueInputBounds(talk, agenticToneSuppressed && agenticToneWindow == talk);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_INPUT: enabled=" + dialogueInput.Enabled +
                "; parentLast=" + (talk.NativePanel.Components.Count > 0 &&
                    talk.NativePanel.Components[talk.NativePanel.Components.Count - 1] == dialogueInput) +
                "; position=" + dialogueInput.Position + "; size=" + dialogueInput.Size);

        }

        private void UpdateDialogueInputBounds(DaggerfallTalkWindow talk, bool combinedDialogueMode)
        {
            if (dialogueInput == null || dialogueInputWindow != talk || dialogueQuestionLabel == null) return;
            Vector2 position = dialogueQuestionLabel.Position;
            float width = dialogueQuestionLabel.MaxWidth;
            // Native Talk is 320 pixels wide. The expanded background inset ends
            // at x=310, leaving a 10 pixel right border in combined mode.
            if (combinedDialogueMode)
                width = Mathf.Max(width, 310f - position.x);
            dialogueInput.Position = position;
            dialogueInput.Size = new Vector2(width, 38f);
        }

        private void SubmitSocialText(DaggerfallTalkWindow talk, string playerText, string history, Action<string> onAnswer = null, string knowledgeResult = "", Func<string,string> verifyAnswer = null, bool returningGreeting = false, string verifiedFallback = null, ConversationContextSnapshot snapshot = null)
        {
            SubmitSocialTextCore(talk, playerText, history, onAnswer, knowledgeResult, verifyAnswer,
                returningGreeting, verifiedFallback, snapshot, string.Empty, false);
        }

        private void SubmitOrdinarySocialText(DaggerfallTalkWindow talk, string playerText, string history,
            ConversationContextSnapshot snapshot, Action<string> onAnswer = null)
        {
            SubmitSocialTextCore(talk, playerText, history, onAnswer, string.Empty, null, false, null,
                snapshot, string.Empty, true);
        }

        private void SubmitSocialTextCore(DaggerfallTalkWindow talk, string playerText, string history,
            Action<string> onAnswer, string knowledgeResult, Func<string,string> verifyAnswer,
            bool returningGreeting, string verifiedFallback, ConversationContextSnapshot snapshot,
            string npcKnowledgeContext, bool allowNpcKnowledgeLookup)
        {
            conversationPendingReturningGreeting = returningGreeting;
            if(snapshot!=null && !IsCurrentConversationContext(snapshot,talk))
            {HandleRejectedConversationStage(snapshot,talk,0,returningGreeting?null:playerText,verifiedFallback);return;}
            bool ordinarySocialReply = snapshot != null && !returningGreeting &&
                string.IsNullOrWhiteSpace(knowledgeResult) && verifyAnswer == null && verifiedFallback == null;
            if (allowNpcKnowledgeLookup && ordinarySocialReply)
            {
                BeginNpcKnowledgeLookup(snapshot, talk, playerText, history, context =>
                {
                    if (!IsCurrentConversationContext(snapshot, talk)) return;
                    SubmitSocialTextCore(talk, playerText, history, onAnswer, knowledgeResult,
                        verifyAnswer, returningGreeting, verifiedFallback, snapshot, context, false);
                });
                return;
            }
            string fallbackReply=DialogueReplyFallback.Select(verifiedFallback);
            bool hasVerifiedFallback=!string.IsNullOrWhiteSpace(verifiedFallback);
            string injectedReplyFailure=TaskReplyFallbackProbe.Begin(store,conversationPersonId,playerText,verifiedFallback,
                ReadConversationHistory(talk));
            Action<string> appendVerifiedFallback=reason=>
            {
                if(returningGreeting&&!hasVerifiedFallback)return;
                if(returningGreeting) AppendReturningGreeting(talk,fallbackReply);
                else AppendRememberedDialoguePair(talk,playerText,fallbackReply,hasVerifiedFallback?"action":"unavailable");
                TaskReplyFallbackProbe.Verify(store,conversationPersonId,playerText,verifiedFallback,ReadConversationHistory(talk));
                if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-shop-commerce-chat-probe")>=0)
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TASK_CHAT_REPLY_FALLBACK: reason="+reason);
            };
            DialogueRequest request;
            ConversationSpeakerCapture legacySpeaker;
            if(snapshot==null && TryResolveConversationSpeaker(out legacySpeaker) && legacySpeaker.ManagedActor &&
                CaptureConversationHouseholdOuting(store,legacySpeaker.PersonId,legacySpeaker.ManagedActor,
                    DaggerfallUnity.Instance.WorldTime.Now.ToSeconds())!=null)
            {
                // A shared outing must use the immutable, freshness-checked
                // request path. Never send an unguarded live group string.
                conversationBusy=false;
                appendVerifiedFallback("shared-outing-requires-snapshot");
                return;
            }
            if(snapshot==null)
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

            request = FreeTextDialogue.Create(returningGreeting ? "The player is present and has said nothing new." : playerText, npcName, race, gender, role, disposition,
                (returningGreeting ? playerText+" " : "") + conversationResponseCue + " Conversation location: " + ConversationPlaceContext() + VerifiedKeeperBusinessContext() +
                " " + CurrentConversationWeather() +
                CurrentConversationLifeContext(npcName, disposition) +
                CurrentNativeSocialSkillContext() +
                CurrentConversationBoundaryContext() +
                CurrentConversationMemory(playerText) +
                " " + ConversationBiographyContext(conversationPersonId) +
                " " + ConversationEnterpriseContext(conversationPersonId,ConversationActionTime()) +
                " " + CurrentConversationClockContext() +
                " Current time and weather supersede earlier conversation descriptions. Do not infer visible sky conditions while indoors.", history, knowledgeResult);
            }
            else
            {
                string name,race,gender;
                if(!snapshot.Name.TryGetVerified(out name))name="unknown";
                if(!snapshot.PersonalState.Race.TryGetVerified(out race))race="unknown";
                if(!snapshot.PersonalState.Gender.TryGetVerified(out gender))gender="unknown";
                ConversationDialogueRole verifiedRole;
                string role=snapshot.Role.TryGetVerified(out verifiedRole)?verifiedRole.ToString():"role not established";
                string capturedContext=ConversationContextFormatter.ForSocialReply(snapshot,3072);
                if (!string.IsNullOrWhiteSpace(npcKnowledgeContext))
                    capturedContext += "\n" + npcKnowledgeContext;
                capturedContext += CurrentNativeSocialSkillContext(snapshot);
                capturedContext += CurrentConversationBoundaryContext();
                capturedContext += " "+ConversationBiographyContext(snapshot.Binding.PersonId);
                capturedContext += " "+ConversationEnterpriseContext(snapshot.Binding.PersonId,snapshot.CapturedGameTime);
                request=FreeTextDialogue.Create(returningGreeting?"The player is present and has said nothing new.":playerText,
                    name,race,gender,role,snapshot.EffectiveDisposition,
                    (returningGreeting?playerText+" ":"")+conversationResponseCue+" "+capturedContext+
                    ReadCapturedConversationMemory(snapshot,talk),history,knowledgeResult);
            }
            bool moodEnabled=!returningGreeting && snapshot!=null && snapshot.PersistentMood!=null &&
                !saveFault && store!=null && memoryWindow==talk &&
                !string.IsNullOrEmpty(memoryPersonId) &&
                string.Equals(snapshot.Binding.PersonId,memoryPersonId,StringComparison.Ordinal);
            string moodPersonId=moodEnabled?memoryPersonId:null;
            string socialSkillTone=moodEnabled?pendingNativeSocialSkillTone:null;
            int socialSkillGroup=moodEnabled?CurrentNativeSocialGroup():-1;
            pendingNativeSocialSkillTone=null;
            long moodSequence=moodEnabled?snapshot.PersistentMood.AcceptedTurnSequence:0;
            long moodRevision=moodEnabled?snapshot.PersistentMood.StateRevision:0;
            if(moodEnabled)
                // AvoidDuplicateReply is normalized to an empty string on plain requests,
                // but any non-null constructor value belongs exclusively to action wording.
                request=new DialogueRequest(request.PlayerText,request.VanillaAnswer,request.NpcName,
                    request.Race,request.Gender,request.SocialRole,request.Disposition,request.SocialOnly,
                    request.ConversationReaction,request.History,request.KnowledgeResult,
                    request.ActionReply,null,true,snapshot.PersistentMood.Value);
            int requestSession=memorySession;
            long replyStage=snapshot==null?0:BeginConversationContextStage(snapshot,talk);
            bool replyHandled=false;
            int replyAttempt=0;
            string previousNpcReply=DialogueHistoryWindow.LastNpcText(history);
            Action<string,int> handleAttempt=null;
            handleAttempt=(answer,attempt)=> { if(replyHandled || attempt!=replyAttempt || !CurrentMemorySession(talk,requestSession)) return;
                    if(snapshot!=null && (replyStage<=0 || replyStage!=conversationContextIssuedStageToken ||
                        !IsCurrentConversationContext(snapshot,talk)))
                    {
                        replyHandled=true;
                        HandleRejectedConversationStage(snapshot,talk,replyStage,returningGreeting?null:playerText,verifiedFallback);
                        return;
                    }
                    // Reject repeated generated speech before consuming the stage or committing
                    // mood, observed speech, history, or presentation. Keep the captured request
                    // and the same stage for one buffered rephrase, including after streaming.
                    string candidate=answer;
                    SocialDialogueResult duplicateCheck=null;
                    bool validCandidate=answer!=LocalDialogueClient.Unavailable && answer!=LocalDialogueClient.ContextLimit;
                    if(moodEnabled)
                    {
                        validCandidate=validCandidate && SocialDialogueResult.TryParse(answer,out duplicateCheck);
                        if(validCandidate)
                        {
                            candidate=duplicateCheck.Reply;
                            validCandidate=candidate!=LocalDialogueClient.Unavailable && candidate!=LocalDialogueClient.ContextLimit;
                        }
                    }
                    if(validCandidate && !returningGreeting && verifyAnswer==null && !hasVerifiedFallback &&
                        DialogueHistoryWindow.IsDuplicateReply(previousNpcReply,candidate))
                    {
                        if(attempt==0)
                        {
                            replyAttempt=1;
                            conversationBusy=true;
                            try
                            {
                                DialogueRequest repair=request.WithSocialRephrase(
                                    LocalDialogue.NormalizeContext(previousNpcReply,800));
                                Action<string> completeRepair=value=>handleAttempt(value,1);
                                if(localDialogue.Request(NewDialogueConnection(),repair,
                                    moodEnabled?ConversationMoodProbe.WrapReply(talk,repair,completeRepair):completeRepair))
                                    return;
                            }
                            catch(Exception exception)
                            {
                                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_SOCIAL_REPHRASE_UNAVAILABLE: "+exception.GetType().Name);
                            }
                        }
                        answer=LocalDialogueClient.Unavailable;
                    }
                    replyHandled=true;
                    if(snapshot!=null && !TryConsumeConversationContextStage(snapshot,talk,replyStage))
                    {HandleRejectedConversationStage(snapshot,talk,replyStage,returningGreeting?null:playerText,verifiedFallback);return;}
                    conversationBusy = false;
                    if (answer == LocalDialogueClient.Unavailable) { appendVerifiedFallback("unavailable"); if(!hasVerifiedFallback)DaggerfallUI.AddHUDText("Local dialogue unavailable. Check the local model server."); return; }
                    if (answer == LocalDialogueClient.ContextLimit) { appendVerifiedFallback("context-limit"); return; }
                    {
                        SocialDialogueResult socialResult=null;
                        if(moodEnabled)
                        {
                            if(!SocialDialogueResult.TryParse(answer,out socialResult))
                            {appendVerifiedFallback("invalid-social-result");return;}
                            if(string.Equals(socialResult.Reply,LocalDialogueClient.Unavailable,StringComparison.Ordinal))
                            {appendVerifiedFallback("unavailable");return;}
                            if(string.Equals(socialResult.Reply,LocalDialogueClient.ContextLimit,StringComparison.Ordinal))
                            {appendVerifiedFallback("context-limit");return;}
                            answer=socialResult.Reply;
                        }
                        string modelAnswer=answer;
                        if(verifyAnswer != null) answer=verifyAnswer(answer);
                        if(moodEnabled && string.Equals(answer,modelAnswer,StringComparison.Ordinal))
                        {
                            // The accepted model turn and its mood change share one sequence-checked
                            // journal commit. The same transaction records exactly what player said.
                            long committedSequence;
                            double committedAt = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                            var observedSpeech = conversationObservedSpeech;
                            var observedSpeechTicket = conversationObservedSpeechTicket;
                            bool committed = false;
                            if (!saveFault && store != null && memoryWindow == talk &&
                                string.Equals(memoryPersonId, moodPersonId, StringComparison.Ordinal))
                            {
                                committed = observedSpeech == null
                                    ? store.TryCommitAcceptedSocialExchange(moodPersonId, moodSequence, moodRevision,
                                        socialResult.MoodDelta, committedAt, memoryDate, ConversationMemoryPlace(),
                                        playerText, answer, out committedSequence)
                                    : store.TryCommitAcceptedSocialExchangeWithObservedSpeech(moodPersonId,
                                        moodSequence, moodRevision, socialResult.MoodDelta, committedAt,
                                        memoryDate, ConversationMemoryPlace(), playerText, answer,
                                        observedSpeechTicket, observedSpeech,
                                        ObservedSpeechMask(true), out committedSequence);
                            }
                            else committedSequence = 0;
                            if (!committed)
                            {
                                // A rejected atomic exchange must still settle presentation. Do
                                // not enqueue the rejected receipt through the fallback append:
                                // its text, identity or revision may belong to another turn.
                                conversationObservedSpeech=null;
                                conversationObservedSpeechTicket=null;
                                conversationAffinityProcessedForTurn=false;
                                appendVerifiedFallback("social-commit-rejected");
                                return;
                            }
                            if (ordinarySocialReply && observedSpeech != null)
                                TryAwardNativeSocialSkillUse(store, moodPersonId, committedSequence,
                                    observedSpeech, socialSkillTone, socialSkillGroup, committedAt);
                            if (observedSpeech != null && ReferenceEquals(conversationObservedSpeech, observedSpeech))
                            {
                                conversationObservedSpeech = null;
                                conversationObservedSpeechTicket = null;
                                conversationAffinityProcessedForTurn = false;
                            }
                            Action appendAcceptedAnswer=()=>AppendUnrecordedDialoguePairWithSpeech(talk,playerText,answer,
                                true,null,null,NpcExperienceAppliedEffectsMask.None);
                            Action publishAcceptedAnswer=()=>{if(onAnswer!=null)onAnswer(answer);};
                            if (!TryDeferAcceptedSpeechAppend(talk, snapshot, playerText, answer,
                                appendAcceptedAnswer, publishAcceptedAnswer))
                            {
                                appendAcceptedAnswer();
                                publishAcceptedAnswer();
                            }
                            return;
                        }
                        if(returningGreeting) AppendReturningGreeting(talk,answer);
                        else AppendRememberedDialoguePair(talk, playerText, answer,
                            moodEnabled?"action":"model");
                        if(onAnswer != null) onAnswer(answer);
                    } };
            Action<string> handleAnswer=answer=>handleAttempt(answer,0);
            if(injectedReplyFailure=="unavailable") {handleAnswer(LocalDialogueClient.Unavailable);return;}
            if(injectedReplyFailure=="context-limit") {handleAnswer(LocalDialogueClient.ContextLimit);return;}
            bool dispatched=false;
            StreamingSocialReplySession streamingSession=null;
            bool canStream=moodEnabled && onAnswer==null && !returningGreeting && verifyAnswer==null &&
                string.IsNullOrWhiteSpace(verifiedFallback) && injectedReplyFailure!="dispatch-rejected" &&
                snapshot!=null && replyStage!=0 && request.SocialOnly && request.MoodEnabled && request.ActionReply==null;
            if(injectedReplyFailure!="dispatch-rejected" && snapshot!=null && replyStage!=0 && canStream &&
                TryBeginStreamingSocialReply(talk,playerText,snapshot,replyStage,handleAnswer,out streamingSession))
            {
                StreamingSocialReplySession captured=streamingSession;
                dispatched=localDialogue.RequestStreaming(NewDialogueConnection(),request,
                    sentence=>AcceptStreamingSocialSentence(captured,sentence),
                    result=>CompleteStreamingSocialGeneration(captured,result));
                if(!dispatched) AbandonStreamingSocialReply(captured,"social-stream-dispatch-rejected");
            }
            else if(injectedReplyFailure!="dispatch-rejected" && !(snapshot!=null && replyStage==0))
                dispatched=localDialogue.Request(NewDialogueConnection(),request,
                    moodEnabled?ConversationMoodProbe.WrapReply(talk,request,handleAnswer):handleAnswer);
            if (!dispatched)
            {
                if(snapshot!=null)
                {
                    HandleRejectedConversationStage(snapshot,talk,replyStage,returningGreeting?null:playerText,verifiedFallback);
                    return;
                }
                conversationBusy = false;
                appendVerifiedFallback("dispatch-rejected");
                if(hasVerifiedFallback)return;
                if(returningGreeting) { returningGreetingPending=true; returningGreetingRetryAt=Time.unscaledTime+0.5f; }
                DaggerfallUI.AddHUDText("Waiting for the current reply.");
            }
        }

        private static string CurrentConversationClock()
        {
            var now = DaggerfallUnity.Instance.WorldTime.Now;
            return ConversationClock.Answer(now.MinTimeString(), now.DateString(), now.Year);
        }
        private static string CurrentConversationTimestamp()
        {
            var now=DaggerfallUnity.Instance.WorldTime.Now;
            return ConversationClock.Timestamp(now.Hour,now.Minute,now.Day+1,now.DayName,now.MonthName,now.Year);
        }
        private static string CurrentConversationClockContext()
        {
            var now=DaggerfallUnity.Instance.WorldTime.Now;
            return ConversationClock.Context(now.Hour,now.Minute,now.DateString(),now.Year,now.IsNight);
        }

        private string CurrentConversationWeather()
        {
            var game = GameManager.Instance;
            bool outdoors;
            string observedWeather;
            if (!TryReadCurrentNpcOutdoorWeather(out outdoors, out observedWeather) || !outdoors)
                return string.Empty;
            string settlement = game.PlayerGPS.HasCurrentLocation ? game.PlayerGPS.CurrentLocation.Name : string.Empty;
            string region = game.PlayerGPS.CurrentRegionName;
            return ConversationWeather.Context(settlement, region, observedWeather);
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
        { return ReadConversationHistoryThrough(talk,int.MaxValue); }

        private static string ReadConversationHistoryThrough(DaggerfallTalkWindow talk,int rowCount)
        {
            var transcript = talk == null || conversationListField == null ? null : conversationListField.GetValue(talk) as ListBox;
            if (transcript == null) return string.Empty;
            var history = new StringBuilder();
            for (int index = 0; index < Math.Min(transcript.Count,rowCount); index++)
            {
                var item = transcript.GetItem(index);
                string text = CanonicalTranscriptText(item);
                if (item == null || item.textLabel == null || string.IsNullOrWhiteSpace(text)) continue;
                if (IsPendingTranscriptItem(item)) continue;
                if(item.textLabel.Name=="PeopleOfTheIliacBayHistoryDay") {history.Append("Conversation: ").AppendLine(item.tag as string);continue;}
                if(item.textLabel.Name=="PeopleOfTheIliacBayHistoryRule") continue;
                if(item.textLabel.Name==UnavailableTranscriptRowName)
                {history.Append(DialogueHistoryWindow.UnavailableResponseLabel).AppendLine(text.Replace("\r", " ").Replace("\n", " "));continue;}
                history.Append(item.textColor == DaggerfallUI.DaggerfallQuestionTextColor ? "Player: " : "NPC: ");
                history.AppendLine(text.Replace("\r", " ").Replace("\n", " "));
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
            string travelerRole=TravelerConversationContext.RoleLabel(store,actor.Resident.Id);
            if(!string.IsNullOrEmpty(travelerRole))return travelerRole;
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
                case Activity.Recovery: return "local resident whose routine is interrupted for recovery";
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
                    return person!=null && store.InnLodgerHome(person.Id)!=null ? "inn bedroom lodger with a verified permanent home elsewhere; not the keeper" :
                        person!=null && store.NativeGuestBed(person.Id)!=null ? "Inn lodger; permanent origin not yet verified; not the keeper" :
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
