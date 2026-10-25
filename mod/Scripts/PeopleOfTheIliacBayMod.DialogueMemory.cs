using System;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using System.Collections.Generic;
using System.Text;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Game.UserInterface;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private const string UnavailableTranscriptRowName = "PeopleOfTheIliacBayUnavailableReply";
        private DaggerfallTalkWindow memoryWindow;
        private string memoryPersonId, memoryPlace, memoryDate;
        private double memoryOpenedAt;
        private int memoryCursor, memorySession;
        private bool returningGreetingPending;
        private double returningGreetingElapsed;
        private float returningGreetingRetryAt;
        private readonly StringBuilder memoryQuestion = new StringBuilder();
        private readonly StringBuilder memoryAnswer = new StringBuilder();
        private readonly HashSet<ListBox.ListItem> trialNativeGreetingRowsQueued = new HashSet<ListBox.ListItem>();

        private void BeginDialogueMemory(DaggerfallTalkWindow window)
        {
            memorySession++;
            BeginConversationContextSession(window);
            returningGreetingPending=false;
            memoryWindow=window; memoryPersonId=PersistentMemoryIdentity();
            TrackConversationMemoryLastSpeaker();
            trialNativeGreetingRowsQueued.Clear();
            var now=DaggerfallUnity.Instance.WorldTime.Now;
            memoryOpenedAt=now.ToSeconds(); memoryDate=CurrentConversationTimestamp();
            memoryPlace=ConversationMemoryPlace();
            memoryQuestion.Length=memoryAnswer.Length=0;
            var transcript=window==null ? null : conversationListField.GetValue(window) as ListBox;
            // DFU's DaggerfallTalkWindow.SetStartConversation creates one native
            // greeting ListItem during Setup/OnPush. Capture that object before
            // transcript restoration can clear it; never synthesize from saved
            // history, questions, date headers, or our own greeting rows.
            ListBox.ListItem nativeOpeningGreeting=FindNativeOpeningGreeting(transcript);
            bool hasPriorConversation=false;
            if(!saveFault && store!=null && transcript!=null && !string.IsNullOrEmpty(memoryPersonId))
            {
                var previous=store.ConversationTranscript(memoryPersonId,memoryOpenedAt);
                if(previous.Count>0)
                {
                    hasPriorConversation=true;
                    // Preserve native quest rewards/greetings. Ordinary repeated
                    // greetings are replaced by this person's saved conversation.
                    var questGreeting=conversationHooks==null ? null : conversationHooks.GetType().GetProperty("HasQuestGreeting");
                    bool hasQuestGreeting=questGreeting!=null && (bool)questGreeting.GetValue(conversationHooks,null);
                    var questItems=hasQuestGreeting ? transcript.ListItems : null;
                    transcript.ClearItems();
                    string visibleDate=null;
                    foreach(var exchange in previous)
                    {
                        // Observations remain memory facts, not empty transcript
                        // sections. A header belongs to an actual visible exchange.
                        if(exchange.Source=="observed") continue;
                        if(exchange.Date!=visibleDate) {AddDialogueDay(transcript,exchange.Date);visibleDate=exchange.Date;}
                        AppendPresentationLine(transcript,exchange.PlayerText,true);
                        AppendPresentationLine(transcript,exchange.NpcText,false,exchange.Source=="unavailable");
                    }
                    AddDialogueDay(transcript,memoryDate);
                    if(questItems!=null) foreach(var item in questItems)
                    {
                        ListBox.ListItem restored;
                        transcript.AddItem(item.textLabel,out restored);
                        restored.textColor=item.textColor; restored.selectedTextColor=item.selectedTextColor;
                    }
                    returningGreetingPending=!hasQuestGreeting;
                    returningGreetingElapsed=Math.Max(0,memoryOpenedAt-store.LastConversationContactAt(memoryPersonId,memoryOpenedAt));
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TRANSCRIPT_RESTORED: person="+memoryPersonId+"; exchanges="+previous.Count);
                }
                else AddDialogueDay(transcript,memoryDate,0);
                store.RememberObservation(memoryPersonId,memoryOpenedAt,memoryDate,memoryPlace,"Conversation started.");
            }
            else if(transcript!=null) AddDialogueDay(transcript,memoryDate,0);
            // Replayed history is presentation, never a new observation or turn.
            memoryCursor=transcript==null ? 0 : transcript.Count;
            conversationLastTranscriptCount=memoryCursor;
            if(transcript!=null && transcript.Count>0)
            {
                transcript.SelectedIndex=transcript.Count-1;
                RefreshTranscriptScroll(window);
            }
            if(!saveFault && store!=null && !string.IsNullOrEmpty(memoryPersonId))
            {
                store.RecordConversationEncounter(conversationPersonId,memoryOpenedAt);
                conversationEncounterRecorded=true;
            }
            if(!hasPriorConversation && nativeOpeningGreeting!=null && IsVisibleTranscriptRow(transcript,nativeOpeningGreeting))
            {
                EnsureOpeningTrialPersonalityProfile();
                QueueNativeOpeningGreetingSpeech(window,transcript,nativeOpeningGreeting,conversationContextSessionId);
            }
        }

        private bool CurrentMemorySession(DaggerfallTalkWindow window,int session)
        { return !saveFault && conversationSessionOpen && memorySession==session && memoryWindow==window &&
            DaggerfallUI.UIManager.ContainsWindow(window); }

        private void EndDialogueMemory(bool capture)
        {
            InvalidatePersonalCommerceCatalog();
            FinishStreamingSocialReplyBeforeMemoryCapture();
            CancelConversationPendingNarration();
            FlushPendingActionWordingBeforeMemoryCapture(capture);
            if(capture && memoryWindow!=null && !saveFault && store!=null && !string.IsNullOrEmpty(memoryPersonId))
            {
                CaptureNativeDialogueMemory();
                FlushNativeMemoryPair();
                store.RememberObservation(memoryPersonId,memoryOpenedAt,memoryDate,memoryPlace,"Met and spoke with the player here.");
            }
            if(store!=null&&!string.IsNullOrEmpty(memoryPersonId))store.CancelConversationRecallWarmup(memoryPersonId);
            memorySession++; memoryWindow=null; memoryPersonId=null;
            EndConversationContextSession();
            CleanupClosedStreamingConversation();
            // Any classifier/reply callback still in flight now belongs to a
            // closed session and will be rejected. Do not let its busy latch
            // strand the next Talk session after load, reset, or window close.
            conversationBusy=false;
            returningGreetingPending=false;
            memoryQuestion.Length=memoryAnswer.Length=0;
            trialNativeGreetingRowsQueued.Clear();
        }

        private static ListBox.ListItem FindNativeOpeningGreeting(ListBox transcript)
        {
            if(transcript==null || transcript.Count!=1) return null;
            ListBox.ListItem row=transcript.GetItem(0);
            if(row==null || row.textLabel==null || string.IsNullOrWhiteSpace(row.textLabel.Text) ||
                IsPendingTranscriptItem(row) ||
                row.textColor==DaggerfallUI.DaggerfallQuestionTextColor ||
                row.textLabel.Name==UnavailableTranscriptRowName ||
                row.textLabel.Name=="PeopleOfTheIliacBayHistoryDay" ||
                row.textLabel.Name=="PeopleOfTheIliacBayReturnGreeting") return null;
            return row;
        }

        private static bool IsVisibleTranscriptRow(ListBox transcript,ListBox.ListItem expected)
        {
            if(transcript==null || expected==null) return false;
            for(int index=0;index<transcript.Count;index++)
                if(ReferenceEquals(transcript.GetItem(index),expected)) return true;
            return false;
        }

        private void QueueNativeOpeningGreetingSpeech(DaggerfallTalkWindow window,ListBox transcript,
            ListBox.ListItem greetingRow,string sessionId)
        {
            if(!breezeTtsTrial || window==null || transcript==null || greetingRow==null ||
                !IsVisibleTranscriptRow(transcript,greetingRow) || string.IsNullOrEmpty(memoryPersonId) ||
                string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(greetingRow.textLabel.Text) ||
                !trialNativeGreetingRowsQueued.Add(greetingRow)) return;
            string requestId;
            if(!NotifyNpcSpeechWithPresentation(memoryPersonId,GameManager.Instance.TalkManager.NameNPC,
                greetingRow.textLabel.Text,sessionId,CurrentTrialVoiceGender(),
                CurrentTtsVoiceIdentity(),out requestId)) return;
            AttachTrialReplayToRows(window,new[]{greetingRow},requestId,sessionId);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_OPENING_GREETING_TTS_QUEUED: person="+memoryPersonId+
                "; session="+sessionId+"; request="+requestId+"; source=native-opening-list-item.");
        }

        private void AdvanceConversationRecallWarmup()
        {
            if(saveFault||store==null||!conversationSessionOpen||memoryWindow==null||string.IsNullOrEmpty(memoryPersonId)||
                !DaggerfallUI.UIManager.ContainsWindow(memoryWindow))return;
            store.AdvanceConversationRecallWarmup(memoryPersonId,32,2);
        }

        private void RequestReturningGreeting(DaggerfallTalkWindow window)
        {
            if(!returningGreetingPending || conversationBusy || Time.unscaledTime<returningGreetingRetryAt) return;
            returningGreetingPending=false;
            conversationBusy=true;
            string sharedActivity=CurrentConversationSharedActivityContext(memoryOpenedAt);
            string prompt=string.IsNullOrWhiteSpace(sharedActivity)?
                ConversationReentry.Prompt(memoryOpenedAt-returningGreetingElapsed,memoryOpenedAt):sharedActivity;
            bool refuses=ListenerRefusesFurtherCommunication(conversationPersonId);
            if(refuses) prompt="The player approached again after you ended the previous conversation. " +
                "They have not apologized or explained themselves. Clearly state that you do not want to speak with them. " +
                "Do not greet them warmly, offer help, or start small talk.";
            ConversationContextSnapshot snapshot;
            try {snapshot=CaptureConversationContext(window,prompt,checked(conversationContextRequestRevision+1));}
            catch(InvalidOperationException)
            {conversationBusy=false;returningGreetingPending=true;returningGreetingRetryAt=Time.unscaledTime+0.5f;return;}
            SubmitSocialText(window, prompt,
                ReadConversationHistory(window), returningGreeting:true,
                verifiedFallback:refuses?"I told you to leave me alone. I have nothing to say to you.":null,
                snapshot:snapshot);
        }

        private void AppendReturningGreeting(DaggerfallTalkWindow window,string answer)
        {
            // A greeting contains no player turn. Do not journal an invented
            // question or replay previous insults through the disposition path.
            RemoveConversationPendingOverlayFor(window);
            var transcript=conversationListField.GetValue(window) as ListBox;
            if(transcript!=null)
            {
                AppendPresentationLine(transcript,answer,false);
                transcript.GetItem(transcript.Count-1).textLabel.Name="PeopleOfTheIliacBayReturnGreeting";
                memoryCursor=conversationLastTranscriptCount=transcript.Count;
                transcript.SelectedIndex=transcript.Count-1;
                RefreshTranscriptScroll(window);
            }
        }

        private static void AppendPresentationLine(ListBox transcript,string text,bool question,bool unavailableResponse=false)
        {
            ListBox.ListItem item; transcript.AddItem(text,out item);
            if(unavailableResponse)item.textLabel.Name=UnavailableTranscriptRowName;
            item.textColor=question ? DaggerfallUI.DaggerfallQuestionTextColor : transcript.TextColor;
            item.selectedTextColor=Color.white;
            item.textLabel.HorizontalAlignment=question ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            item.textLabel.HorizontalTextAlignment=TextLabel.HorizontalTextAlignmentSetting.Left;
            if(DaggerfallUnity.Settings.EnableModernConversationStyleInTalkWindow)
            {
                item.textLabel.TextScale=0.8f;
                item.textLabel.MaxWidth=(int)(item.textLabel.MaxWidth*0.75f);
                item.textLabel.BackgroundColor=question ? new Color(0.3f,0.35f,0.43f) : new Color(0.32f,0.31f,0.06f);
            }
        }

        private static void RefreshTranscriptScroll(DaggerfallTalkWindow window)
        {
            const System.Reflection.BindingFlags flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
            typeof(DaggerfallTalkWindow).GetMethod("UpdateScrollBarConversation",flags).Invoke(window,null);
            typeof(DaggerfallTalkWindow).GetMethod("UpdateScrollButtonsConversation",flags).Invoke(window,null);
        }

        private static void AddDialogueDay(ListBox transcript,string date,int position=-1)
        {
            ListBox.ListItem item;
            transcript.AddItem(date,out item,position);
            item.tag=date;
            item.textLabel.Name="PeopleOfTheIliacBayHistoryDay";
            item.textColor=item.selectedTextColor=new Color(0.7f,0.7f,0.7f);
            item.textLabel.TextScale=transcript.TextScale*0.8f;
            item.textLabel.HorizontalAlignment=HorizontalAlignment.Center;
            item.textLabel.HorizontalTextAlignment=TextLabel.HorizontalTextAlignmentSetting.Center;
        }

        private string CurrentConversationMemory(string query)
        {
            if(saveFault || store==null || string.IsNullOrEmpty(memoryPersonId))
                return " No durable identity is available for this person; do not claim remembered meetings.";
            CaptureNativeDialogueMemory();
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            string recalledMemory;
            ConversationMemoryCompactionCache compactionCache=GetConversationMemoryCompactionCache();
            try
            {
                recalledMemory=store.RecallConversationWithCompaction(memoryPersonId,query,now,2000,
                    compactionCache,store.CaptureConversationMemoryCompactionContext);
            }
            catch
            {
                recalledMemory=store.RecallConversation(memoryPersonId,query,now,2000);
            }
            string context=" "+recalledMemory+" "+ConversationRoomPresence();
            string moodCauses=string.Empty;
            try { moodCauses=store.DescribeRecentConversationMoodCauses(memoryPersonId,now,600); }
            catch { }
            if(!string.IsNullOrWhiteSpace(moodCauses)) context+=" "+moodCauses;
            var threat=CurrentDialogueDisposition()<=-100?store.LatestAcceptedConversationFamilyThreat(memoryPersonId,now):null;
            if(threat!=null)
                context+=" Prior semantic-route classification (not verified intent or capability): a received statement was classified as a direct family threat. The exact receipt-bound player statement was: \""+
                    threat.Utterance+"\". You remain seriously hostile. Treat the classification as what the route accepted, not proof of the player's actual intent or ability. If you choose to speak, keep a firm boundary; do not greet the player as a friendly stranger.";
            return context;
        }

        private string PersistentMemoryIdentity()
        {
            var manager=GameManager.Instance.TalkManager;
            var npc=manager.CurrentNPCType==TalkManager.NPCType.Static ? manager.StaticNPC : null;
            if(!npc || npc.Data.context==StaticNPC.Context.Building) return conversationPersonId;
            // Generated quest actors can reuse a layout position. A new quest at
            // that position must never inherit the previous person's memories.
            var resource=npc.GetComponent<QuestResourceBehaviour>();
            if(resource && resource.QuestUID!=0 && resource.TargetSymbol!=null)
                return "quest-person:"+resource.QuestUID+":"+resource.TargetSymbol.Name;
            string castleKey;
            int exteriorTown,exteriorHash;
            if(NativeExteriorNpcIdentity.TryRead(npc,out exteriorTown,out exteriorHash))return StaticConversationPersonId(npc);
            if(TryNativeCastleLayoutKey(npc,out castleKey))return StaticConversationPersonId(npc);
            // Unowned custom/dungeon actors need an explicit stable identity adapter.
            return null;
        }

        private void CaptureNativeDialogueMemory()
        {
            if(memoryWindow==null || saveFault || store==null || string.IsNullOrEmpty(memoryPersonId)) return;
            var transcript=conversationListField.GetValue(memoryWindow) as ListBox;
            if(transcript==null) return;
            bool transcriptReset=transcript.Count<memoryCursor;
            int firstNewRow=memoryCursor;
            var priorOffer=pendingConversationAction;
            bool retainOffer=!transcriptReset && firstNewRow<transcript.Count && priorOffer!=null &&
                conversationSessionOpen && memoryPersonId==conversationPersonId &&
                pendingConversationActionHistory==ReadConversationHistoryThrough(memoryWindow,firstNewRow);
            if(transcriptReset) { memoryCursor=0; memoryQuestion.Length=memoryAnswer.Length=0; }
            else if(conversationSessionOpen && conversationContextSessionSpeaker != null &&
                string.Equals(memoryPersonId, conversationPersonId, StringComparison.Ordinal))
                CaptureNewNativeTopicAnswerRows(memoryWindow, transcript, firstNewRow, transcript.Count,
                    memoryPersonId, conversationContextSessionId, false);
            for(;memoryCursor<transcript.Count;memoryCursor++)
            {
                var item=transcript.GetItem(memoryCursor);
                if(item==null || item.textLabel==null || string.IsNullOrWhiteSpace(item.textLabel.Text)) continue;
                if(IsPendingTranscriptItem(item)) continue;
                if(item.textLabel.Name=="PeopleOfTheIliacBayHistoryDay" || item.textLabel.Name=="PeopleOfTheIliacBayHistoryRule" || item.textLabel.Name=="PeopleOfTheIliacBayReturnGreeting") continue;
                if(item.textLabel.Name==UnavailableTranscriptRowName)
                {memoryQuestion.Length=memoryAnswer.Length=0;continue;}
                string text=CanonicalTranscriptText(item).Replace('\n',' ').Replace('\r',' ');
                if(item.textColor==DaggerfallUI.DaggerfallQuestionTextColor)
                {
                    if(memoryAnswer.Length>0) FlushNativeMemoryPair();
                    memoryQuestion.Append(text).Append(' ');
                }
                else if(memoryQuestion.Length>0) memoryAnswer.Append(text).Append(' ');
            }
            FlushNativeMemoryPair();
            if(retainOffer && ReferenceEquals(priorOffer,pendingConversationAction))
                pendingConversationActionHistory=ReadConversationHistory(memoryWindow);
        }

        private void FlushNativeMemoryPair()
        {
            if(memoryQuestion.Length==0 || memoryAnswer.Length==0) return;
            RememberDialogueExchange(memoryQuestion.ToString(),memoryAnswer.ToString(),"native");
            memoryQuestion.Length=memoryAnswer.Length=0;
        }

        private void RememberDialogueExchange(string question,string answer,string source)
        {
            if(saveFault || store==null || memoryWindow==null || string.IsNullOrEmpty(memoryPersonId)) return;
            var now=DaggerfallUnity.Instance.WorldTime.Now;
            store.RememberConversation(memoryPersonId,now.ToSeconds(),memoryDate,
                ConversationMemoryPlace(),question,answer,source);
        }

        private static string ConversationMemoryPlace()
        {
            var game=GameManager.Instance;
            string town=game.PlayerGPS.HasCurrentLocation ? game.PlayerGPS.CurrentLocation.Name : game.PlayerGPS.CurrentRegionName;
            if(!game.PlayerEnterExit.IsPlayerInsideBuilding) return "Outdoors in "+town;
            string building=ConversationBuildingName(game.PlayerEnterExit.BuildingDiscoveryData.buildingKey);
            return (string.IsNullOrWhiteSpace(building) ? "Inside a building" : building)+", "+town;
        }

        private void AppendRememberedDialoguePair(DaggerfallTalkWindow talk,string question,string answer,string source)
        {
            AppendRememberedDialoguePairWithSpeech(talk, question, answer, source,
                conversationObservedSpeechTicket, conversationObservedSpeech, ObservedSpeechMask(false));
        }

        private void AppendRememberedDialoguePairWithSpeech(DaggerfallTalkWindow talk,string question,string answer,
            string source,NpcObservedSpeechTicket ticket,NpcObservedSpeechReceipt receipt,
            NpcExperienceAppliedEffectsMask mask)
        {
            if (source != "unavailable" && talk != null && conversationContextCurrentSnapshot != null &&
                (ticket == null || ReferenceEquals(ticket, conversationObservedSpeechTicket)) &&
                string.Equals(question, conversationContextCapturedPlayerText, StringComparison.Ordinal) &&
                TryDeferAcceptedSpeechAppend(talk, conversationContextCurrentSnapshot, question, answer,
                    () => AppendRememberedDialoguePairCore(talk, question, answer, source,
                        ticket, receipt, mask))) return;
            AppendRememberedDialoguePairCore(talk, question, answer, source, ticket, receipt, mask);
        }

        private void AppendRememberedDialoguePairCore(DaggerfallTalkWindow talk,string question,string answer,string source,
            NpcObservedSpeechTicket ticket,NpcObservedSpeechReceipt receipt,NpcExperienceAppliedEffectsMask mask)
        {
            if(talk==null || appendConversationPair==null) return;
            if(conversationPendingOverlayWindow==talk) RemoveConversationPendingOverlay();
            var transcript=conversationListField.GetValue(talk) as ListBox;
            int firstNewRow=transcript==null ? 0 : transcript.Count;
            AppendUnrecordedDialoguePairWithSpeech(talk,question,answer,source!="unavailable",ticket,receipt,mask);
            if(source=="unavailable" && transcript!=null)
            {
                // Provenance belongs to the exact UI row occurrence. Never infer
                // it from reply wording, which may also be a genuine NPC answer.
                for(int index=firstNewRow;index<transcript.Count;index++)
                {
                    var row=transcript.GetItem(index);
                    if(row!=null && row.textLabel!=null && row.textColor!=DaggerfallUI.DaggerfallQuestionTextColor)
                        row.textLabel.Name=UnavailableTranscriptRowName;
                }
            }
            if(memoryWindow==talk) RememberDialogueExchange(question,answer,source);
        }

        private void AppendUnrecordedDialoguePair(DaggerfallTalkWindow talk,string question,string answer,bool speakInTrial=true)
        {
            AppendUnrecordedDialoguePairWithSpeech(talk, question, answer, speakInTrial,
                conversationObservedSpeechTicket, conversationObservedSpeech, ObservedSpeechMask(false));
        }

        private void AppendUnrecordedDialoguePairWithSpeech(DaggerfallTalkWindow talk,string question,string answer,
            bool speakInTrial,NpcObservedSpeechTicket ticket,NpcObservedSpeechReceipt receipt,
            NpcExperienceAppliedEffectsMask mask)
        {
            if(talk==null || appendConversationPair==null) return;
            if(conversationPendingOverlayWindow==talk) RemoveConversationPendingOverlay();
            CaptureNativeDialogueMemory();
            var offerBeforeAppend=pendingConversationAction;
            bool retainOffer=offerBeforeAppend!=null && memoryWindow==talk &&
                pendingConversationActionHistory==ReadConversationHistory(talk);
            var beforeTranscript=conversationListField.GetValue(talk) as ListBox;
            int firstTrialRow=beforeTranscript==null?0:beforeTranscript.Count;
            appendConversationPair.Invoke(talk,new object[]{question,answer});
            // Unrelated replies can arrive through topic, social, or streaming
            // paths. Rebind at the actual transcript append, including deferred
            // audio publication, rather than invalidating an unchanged offer.
            if(retainOffer && ReferenceEquals(pendingConversationAction,offerBeforeAppend))
                pendingConversationActionHistory=ReadConversationHistory(talk);
            QueueObservedSpeechAfterAppend(ticket, receipt, mask, question);
            var transcript=conversationListField.GetValue(talk) as ListBox;
            if(transcript!=null)
            {
                conversationLastTranscriptCount=transcript.Count;
                if(memoryWindow==talk) memoryCursor=transcript.Count;
            }
            if(suppressFullWavTtsForNextAppend && streamingSocialReply!=null)
                AttachStreamingReplayToAppendedRows(talk,firstTrialRow,streamingSocialReply.StreamId,
                    streamingSocialReply.ConversationToken,streamingSocialReply.PersonId);
            // Speak only finalized displayed NPC text. A technical failure is not NPC speech.
            if(speakInTrial && !suppressFullWavTtsForNextAppend && breezeTtsTrial && memoryWindow==talk &&
                !string.IsNullOrEmpty(memoryPersonId) && !string.IsNullOrEmpty(conversationContextSessionId))
            {
                string trialRequestId;
                if(NotifyNpcSpeechWithPresentation(memoryPersonId,GameManager.Instance.TalkManager.NameNPC,
                    answer,conversationContextSessionId,CurrentTrialVoiceGender(),
                    CurrentTtsVoiceIdentity(),out trialRequestId))
                    AttachTrialReplayToAppendedAnswerRows(talk,firstTrialRow,trialRequestId,conversationContextSessionId);
            }
        }

        private BreezeTtsTrialDeliveryContext CurrentTrialDeliveryContext()
        {
            NpcPresentationState state = CaptureCurrentNpcPresentation(memoryPersonId);
            return state == null ? null : state.CreateDeliveryContext();
        }

        private string CurrentTrialVoiceGender()
        {
            BreezeTtsTrialBridge.VoiceIdentity identity=CurrentTtsVoiceIdentity();
            return identity.sex.known ? identity.sex.value : "unknown";
        }

        private BreezeTtsTrialBridge.VoiceIdentity CurrentTtsVoiceIdentity()
        { return CurrentTtsVoiceIdentityFor(memoryPersonId); }

        private BreezeTtsTrialBridge.VoiceIdentity CurrentTtsVoiceIdentityFor(string identityPersonId)
        {
            var identity=new BreezeTtsTrialBridge.VoiceIdentity();
            identity.sex=UnknownVoiceAttribute("native-sex-unavailable");
            identity.race=UnknownVoiceAttribute("native-race-unavailable");
            identity.age=UnknownVoiceAttribute("native-age-unavailable");
            identity.origin=UnknownVoiceAttribute("no-explicit-home-provenance");

            var manager=GameManager.Instance==null?null:GameManager.Instance.TalkManager;
            if(manager==null)return identity;
            ResidentActor actor=null;
            if(manager.CurrentNPCType==TalkManager.NPCType.Mobile && manager.MobileNPC)
                actor=manager.MobileNPC.GetComponent<ResidentActor>();
            if(actor && actor.Resident!=null && !string.IsNullOrEmpty(identityPersonId) &&
                string.Equals(actor.Resident.Id,identityPersonId,StringComparison.Ordinal))
            {
                SetVoiceSex(identity.sex,actor.Resident.Sex==ResidentSex.Female?"female":
                    actor.Resident.Sex==ResidentSex.Male?"male":"unknown","saved-native-resident-sex");
                if(actor.Person!=null)
                    SetVoiceRace(identity.race,actor.Person.Race.ToString(),"saved-native-resident-race");
                string age=actor.Resident.AgeGroup==ResidentAgeGroup.Child?"child":
                    actor.Resident.AgeGroup==ResidentAgeGroup.Adult?"adult":"unknown";
                if(age!="unknown") identity.age=new BreezeTtsTrialBridge.VoiceIdentity.Attribute
                    { value=age,known=true,confidence="high",source="saved-resident-age-group" };
                return identity;
            }

            if(manager.CurrentNPCType==TalkManager.NPCType.Static && manager.StaticNPC)
            {
                var data=manager.StaticNPC.Data;
                SetVoiceSex(identity.sex,NormalizeVoiceGender(data.gender.ToString()),"native-static-npc-data");
                SetVoiceRace(identity.race,data.race.ToString(),"native-static-npc-data");
                identity.age=UnknownVoiceAttribute("stationary-flat-catalog-identity-unmatched");
                return identity;
            }
            if(manager.CurrentNPCType==TalkManager.NPCType.Mobile && manager.MobileNPC)
            {
                SetVoiceSex(identity.sex,NormalizeVoiceGender(manager.MobileNPC.Gender.ToString()),"native-mobile-npc-data");
                SetVoiceRace(identity.race,manager.MobileNPC.Race.ToString(),"native-mobile-npc-data");
            }
            return identity;
        }

        private static BreezeTtsTrialBridge.VoiceIdentity.Attribute UnknownVoiceAttribute(string source)
        { return new BreezeTtsTrialBridge.VoiceIdentity.Attribute { value="unknown",known=false,source=source,confidence="unknown" }; }

        private static string NormalizeVoiceGender(string value)
        {
            if(string.Equals(value,"female",StringComparison.OrdinalIgnoreCase))return "female";
            if(string.Equals(value,"male",StringComparison.OrdinalIgnoreCase))return "male";
            return "unknown";
        }

        private static void SetVoiceSex(BreezeTtsTrialBridge.VoiceIdentity.Attribute target,string value,string source)
        {
            if(target==null)return;
            value=NormalizeVoiceGender(value);
            target.value=value;target.known=value!="unknown";target.source=source;target.confidence="unknown";
        }

        private static void SetVoiceRace(BreezeTtsTrialBridge.VoiceIdentity.Attribute target,string value,string source)
        {
            if(target==null)return;
            bool known=!string.IsNullOrWhiteSpace(value) && !string.Equals(value,"unknown",StringComparison.OrdinalIgnoreCase);
            target.value=known?value:"unknown";target.known=known;target.source=source;target.confidence="unknown";
        }

        private string ConversationRoomPresence()
        {
            var game=GameManager.Instance;
            if(!game.PlayerEnterExit.IsPlayerInsideBuilding) return "";
            // Only nearby visible people, once per submitted turn. No global census
            // and no private conversations/quests are shared between NPCs.
            var names=new List<string>();
            var mobile=game.TalkManager.CurrentNPCType==TalkManager.NPCType.Mobile ? game.TalkManager.MobileNPC : null;
            var stationary=game.TalkManager.CurrentNPCType==TalkManager.NPCType.Static ? game.TalkManager.StaticNPC : null;
            var origin=mobile ? mobile.transform.position : stationary ? stationary.transform.position : game.PlayerObject.transform.position;
            var target=mobile ? mobile.transform : stationary ? stationary.transform : null;
            foreach(var npc in UnityEngine.Object.FindObjectsOfType<MobilePersonNPC>())
                if(npc.transform!=target && !string.IsNullOrWhiteSpace(npc.NameNPC) &&
                    CanSeeMemoryPerson(origin,npc.transform,target) && names.Count<8) names.Add(npc.NameNPC);
            foreach(var npc in UnityEngine.Object.FindObjectsOfType<StaticNPC>())
                if(npc.transform!=target && !string.IsNullOrWhiteSpace(npc.DisplayName) &&
                    CanSeeMemoryPerson(origin,npc.transform,target) && names.Count<8) names.Add(npc.DisplayName);
            return names.Count==0 ? " No other nearby visible people identified; this does not prove the whole building is empty." :
                " Other people currently visible nearby: "+string.Join(", ",names.ToArray())+". You do not know their private thoughts or conversations.";
        }

        private static bool CanSeeMemoryPerson(Vector3 origin,Transform person,Transform speaker)
        {
            if(!person.gameObject.activeInHierarchy || Vector3.SqrMagnitude(origin-person.position)>64) return false;
            var hits=Physics.RaycastAll(origin+Vector3.up*0.7f,person.position-origin,
                Vector3.Distance(origin,person.position),~0,QueryTriggerInteraction.Ignore);
            foreach(var hit in hits)
                if(hit.transform!=person && !hit.transform.IsChildOf(person) &&
                    (speaker==null || (hit.transform!=speaker && !hit.transform.IsChildOf(speaker)))) return false;
            return true;
        }
    }
}
