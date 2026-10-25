using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private bool conversationBusy;
        private bool conversationSessionOpen;
        private bool conversationDispositionEnabled = true, conversationMapRequestsEnabled = true;
        private string conversationPersonId;
        private bool conversationEncounterRecorded;
        private int conversationEphemeralOffset;
        private int conversationNativeDisposition, conversationEphemeralInsults;
        private int conversationDispositionBeforeMessage;
        private NpcObservedSpeechReceipt conversationObservedSpeech;
        private bool conversationAffinityProcessedForTurn;
        private string conversationResponseCue = "";
        private TalkManager.ListItem conversationLastTopic;
        private int conversationLastTranscriptCount;
        private IDisposable conversationHooks;
        private static readonly FieldInfo lastConversationTone = typeof(TalkManager).GetField("lastToneIndex", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo selectedToneField = typeof(DaggerfallTalkWindow).GetField("selectedTalkTone", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo reactionTierField = typeof(TalkManager).GetField("reactionToPlayer_0_1_2", BindingFlags.Instance | BindingFlags.NonPublic);

        private void BeginGroundedConversation()
        {
            if (conversationSessionOpen) return;
            InstallConversationHooks();
            conversationSessionOpen = true;
            conversationBusy = false;
            conversationEncounterRecorded = false;
            conversationLastTopic = null;
            conversationSuggestions.Clear();
            var talkWindow = DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
            var transcript = talkWindow == null || conversationListField == null ? null : conversationListField.GetValue(talkWindow) as ListBox;
            conversationLastTranscriptCount = transcript == null ? 0 : transcript.Count;
            conversationEphemeralOffset = 0;
            conversationEphemeralInsults = 0;
            conversationResponseCue = "";
            conversationObservedSpeech = null;
            conversationAffinityProcessedForTurn = false;
            var manager = GameManager.Instance.TalkManager;
            conversationNativeDisposition = CurrentDialogueDisposition();
            var mobile = manager.CurrentNPCType==TalkManager.NPCType.Mobile ? manager.MobileNPC : null;
            var actor = mobile ? mobile.GetComponent<ResidentActor>() : null;
            conversationPersonId = actor && actor.Resident != null ? actor.Resident.Id : null;
            if (manager.CurrentNPCType==TalkManager.NPCType.Static && manager.StaticNPC)
            {
                var data = manager.StaticNPC.Data;
                // Match the core's native identity when available, including shop heads.
                RegisterNativeStaticConversationPerson(manager.StaticNPC);
                conversationPersonId = StaticConversationPersonId(manager.StaticNPC);
            }
            conversationHomeTopicAvailable = false;
            if (conversationDispositionEnabled && conversationPersonId != null && !saveFault)
                store.SetConversationBaseline(conversationPersonId, conversationNativeDisposition,
                    DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(), CaptureDialogueFactionId(manager));
            // Unmanaged vanilla wanderers have no durable identity. Never merge
            // unrelated NPCs merely because they happen to share a generated name.
            if (conversationDispositionEnabled && conversationPersonId != null && reactionToPlayerField != null)
                reactionToPlayerField.SetValue(manager, ConversationEffects.Reaction(conversationNativeDisposition,
                    store.ConversationDisposition(conversationPersonId)));
            if (lastConversationTone != null) lastConversationTone.SetValue(manager, -1);
            BeginDialogueMemory(talkWindow);
        }

        private void InstallConversationHooks()
        {
            if (conversationHooks != null) return;
            var hookType = Type.GetType("PeopleOfTheIliacBay.QuestHooks.NativeConversationHooks, PeopleOfTheIliacBay.QuestHooks", true);
            conversationHooks = (IDisposable)Activator.CreateInstance(hookType, new object[] {
                new Func<int, int>(native => conversationSessionOpen && conversationDispositionEnabled && !saveFault ?
                    ConversationEffects.AdjustReactionTier(native, ConversationOffset()) : native),
                new Func<string>(NativeConversationAnswerOverride),
                new Func<MobilePersonNPC, bool>(CanStartMobileConversation),
                new Func<StaticNPC, bool>(CanStartStaticConversation) });
        }

        private bool CanStartMobileConversation(MobilePersonNPC person)
        {
            var actor = person ? person.GetComponent<ResidentActor>() : null;
            if (!actor || actor.Resident == null || !actor.Resident.IsAlive) return true;
            string id = actor.Resident.Id;
            if (!CanApproachNpcAfterBoundary(id))
            {
                DaggerfallUI.AddHUDText("They refuse to speak while seeking the guards.");
                return false;
            }
            bool reporting = streets && streets.HasDialogueComplaint(id);
            bool alarmed = streets && streets.IsAlarmed(id);
            bool grudge = !saveFault && store.HoldsPartnerGrudge(actor.Resident,
                DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
            bool terminalHostility = HasTerminalConversationHostility(id);
            if (ConversationAccess.CanStart(reporting, alarmed, grudge, terminalHostility,
                conversationDispositionEnabled)) return true;
            if (terminalHostility && conversationDispositionEnabled)
            {
                DaggerfallUI.AddHUDText(actor.Person.NameNPC + " refuses to speak to you.");
                return false;
            }
            DaggerfallUI.AddHUDText(reporting ? "They hurry toward the guards and refuse to speak." :
                "They keep their distance and refuse to speak.");
            return false;
        }

        private bool CanStartStaticConversation(StaticNPC person)
        {
            if (!person || saveFault) return true;
            var data = person.Data;
            string id = StaticConversationPersonId(person);
            if (!CanApproachNpcAfterBoundary(id))
            {
                DaggerfallUI.AddHUDText(person.DisplayName + " refuses to speak while seeking the guards.");
                return false;
            }
            bool terminalHostility = HasTerminalConversationHostility(id);
            if (ConversationAccess.CanStart(false, false, false, terminalHostility,
                conversationDispositionEnabled)) return true;
            DaggerfallUI.AddHUDText(person.DisplayName + " refuses to speak to you.");
            return false;
        }

        private bool HasTerminalConversationHostility(string personId)
        {
            return !saveFault && store != null && store.TryGetActualConversationDisposition(personId, out int disposition) &&
                disposition <= -100;
        }

        private void SubmitFreeText(DaggerfallTalkWindow window, string playerText)
        {
            if (conversationBusy) { DaggerfallUI.AddHUDText("Waiting for the current reply."); return; }
            returningGreetingPending=false;
            if (!TryIssueConversationSpeech(window, playerText)) return;
            if (!conversationEncounterRecorded && conversationPersonId != null && store != null && !saveFault)
            {
                store.RecordConversationEncounter(conversationPersonId, DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
                conversationEncounterRecorded = true;
            }
            ConversationContextSnapshot requestSnapshot;
            try { requestSnapshot=CaptureConversationContext(window,playerText,checked(conversationContextRequestRevision+1)); }
            catch(InvalidOperationException)
            {
                AppendRememberedDialoguePair(window,playerText,"I lost my train of thought. Please say that again.","unavailable");
                return;
            }
            if (!TryBindConversationSpeech(requestSnapshot, window, playerText)) return;
            CaptureConversationCommandTurn(requestSnapshot, playerText);
            ObserveDecisionShadow(requestSnapshot, window, playerText);
            double submittedAt=requestSnapshot.CapturedGameTime;
            ConversationLocationSnapshot submittedLocation=requestSnapshot.PlayerLocationAtSubmission;
            conversationBusy = true;
            var changedTranscript=conversationListField.GetValue(window) as ListBox;
            if(changedTranscript!=null && changedTranscript.Count>conversationLastTranscriptCount)
                conversationHomeTopicAvailable=false;
            conversationDispositionBeforeMessage = CurrentDialogueDisposition();
            conversationHomeTopicAvailable=false;
            var manager = GameManager.Instance.TalkManager;
            var native = new Dictionary<int, TalkManager.ListItem>();
            var all = new List<ConversationTopic>();
            var seen = new HashSet<TalkManager.ListItem>();
            AddConversationTopics(manager.ListTopicLocation, "Where is / Location", all, native, seen);
            AddConversationTopics(manager.ListTopicPerson, "Where is / People", all, native, seen);
            AddConversationTopics(manager.ListTopicThings, "Where is / Things", all, native, seen);
            AddConversationTopics(manager.ListTopicTellMeAbout, "Tell me about", all, native, seen);
            int workId = all.Count;
            native[workId] = new TalkManager.ListItem { caption = "Work", questionType = TalkManager.QuestionType.Work };
            all.Add(new ConversationTopic(workId, "Work / jobs", "Work", false));
            var transcript = conversationListField == null ? null : conversationListField.GetValue(window) as ListBox;
            // Native topic clicks can change the subject between typed messages.
            // A previewed/default topic is not a previously answered question.
            if (transcript != null && transcript.Count > conversationLastTranscriptCount)
            {
                conversationSuggestions.Clear();
                conversationLastTopic = manager.CurrentQuestionListItem;
            }
            var previous = conversationLastTopic;
            int previousId = -1;
            foreach (var pair in native) if (pair.Value == previous) { previousId = pair.Key; break; }
            // Groups remain available even in huge cities. Relevant named subjects
            // rank above unrelated buildings; no answer getters run during retrieval.
            var words = ConversationTopicSearchTerms(playerText);
            var ranked = all.OrderByDescending(t => TopicRank(t, words, previousId)).ThenBy(t => t.Id);
            var candidates = new List<ConversationTopic>();
            int length = 0;
            foreach (var topic in ranked)
            {
                int size = topic.Caption.Length + topic.Path.Length + 24;
                if (length + size > 8000 || candidates.Count >= 100) continue;
                candidates.Add(topic); length += size;
            }
            string history = ReadConversationHistory(window);
            string decisionRoute = null;
            NpcSpeechActClassification foregroundSpeech = null;
            object foregroundSpeechSource = null;
            string prompt = ConversationContextFormatter.ForRouting(requestSnapshot,2048)+"\n"+
                ConversationKnowledge.RoutingPrompt(playerText, history);
            int requestSession=memorySession;
            string requestPerson=conversationPersonId;
            Action<ConversationSelection> applyGuardedSelection=choice=>
            {
                if(!CurrentMemorySession(window,requestSession))return;
                if(conversationPersonId!=requestPerson)
                {HandleRejectedConversationStage(requestSnapshot,window,0,playerText);return;}
                if(!IsCurrentConversationContext(requestSnapshot,window))
                {HandleRejectedConversationStage(requestSnapshot,window,0,playerText);return;}
                var boundaryAction=ConversationSpeechBoundaryPolicy.Decide(foregroundSpeechSource,foregroundSpeech,
                    store==null || string.IsNullOrEmpty(conversationPersonId)?null:store.ReadNpcPersonality(conversationPersonId));
                bool toleratedInsult=foregroundSpeech.Kind==NpcExperienceSpeechActKind.DirectInsult &&
                    boundaryAction==ConversationSpeechBoundaryAction.Continue;
                if(toleratedInsult)choice=new ConversationSelection("social",-1,"rude","none");
                NpcConversationBoundaryRecord boundary=null;
                if(conversationDispositionEnabled && boundaryAction==ConversationSpeechBoundaryAction.EndConversation &&
                    foregroundSpeech.Request.ListenerRefusesFurtherCommunication && !foregroundSpeech.IsDirectHostility &&
                    store!=null && !saveFault)
                {
                    // Respect an existing refusal without inventing a new offense
                    // from ordinary contact or a non-serious repeat remark.
                    HandleNpcConversationBoundary(window,playerText,
                        store.ReadNpcConversationBoundary(conversationPersonId,submittedAt),boundaryAction);
                    return;
                }
                if(conversationDispositionEnabled && boundaryAction!=ConversationSpeechBoundaryAction.Continue &&
                    conversationPersonId!=null && !saveFault && store!=null &&
                    !store.TryCommitNpcConversationBoundary(conversationObservedSpeechTicket,conversationObservedSpeech,
                        foregroundSpeechSource,foregroundSpeech,out boundary))
                {
                    conversationBusy=false;
                    AppendRememberedDialoguePair(window,playerText,DialogueReplyFallback.UnavailableReply,"unavailable");
                    DaggerfallUI.AddHUDText("The conversation reaction could not be bound to this turn.");
                    return;
                }
                if(choice.SocialIntent=="family_threat" && (saveFault || store==null ||
                    !store.TryRecordAcceptedFamilyThreatClassification(conversationPersonId,playerText,
                        conversationObservedSpeechTicket,conversationObservedSpeech,
                        ConversationSpeechClassificationSource.AcceptedSemanticDecisionRoute)))
                {
                    conversationBusy=false;
                    AppendRememberedDialoguePair(window,playerText,DialogueReplyFallback.UnavailableReply,"unavailable");
                    DaggerfallUI.AddHUDText("The speech classification could not be bound to this turn. No social effect was accepted.");
                    return;
                }
                bool busyDeparture=choice.SocialIntent=="busy_pause";
                bool pauseSaved=busyDeparture&&!saveFault&&store!=null;
                if(pauseSaved) store.RecordPlayerProactiveContactPause(submittedAt);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_INTENT: " + choice.Intent + "; topic=" + choice.TopicId + "; tone=" + choice.Tone+
                    "; social="+choice.SocialIntent);
                conversationResponseCue=busyDeparture?
                    "The player needs to leave or is unavailable now. Acknowledge briefly and respect that boundary; do not introduce another topic.":
                    choice.Intent=="map" && choice.TopicId<0?
                    "No verified map destination matched. No marker was placed. Do not claim a map marker was placed.":"";
                if(toleratedInsult)conversationResponseCue+=" Your established familiarity and trust let you tolerate this ordinary insult without ending the conversation. React in your own personality: banter if playful, acknowledge justified frustration, or object briefly. This does not excuse threats, serious abuse or persistent harassment. Do not invent a shared event or instantly forgive existing resentment.";
                if(choice.Tone == "flirt" && ConversationPartnerIsChild())
                {
                    conversationBusy = false;
                    AppendDialoguePair(window, playerText, "Leave me alone! Guards!");
                    ReportChildDialogueOffense();
                    DaggerfallUI.Instance.PopToHUD();
                    DaggerfallUI.AddHUDText("Leave me alone! Guards!");
                    return;
                }
                string effectiveTone=choice.SocialIntent=="family_threat"?"family_threat":
                    choice.Tone=="flirt"?ResolveConversationFlirt():choice.Tone;
                RememberNativeSocialSkillTone(effectiveTone);
                conversationDispositionBeforeMessage = CurrentDialogueDisposition();
                if(effectiveTone=="family_threat"&&!conversationDispositionEnabled)
                {
                    conversationBusy=false;
                    AppendRememberedDialoguePair(window,playerText,"Please don't threaten my family.","action");
                    return;
                }
                if (effectiveTone == "family_threat") ApplyFamilyThreatDisposition();
                else if(boundary!=null) ApplyNpcConversationBoundaryDisposition(boundary);
                else ApplyConversationDisposition(effectiveTone, playerText);
                int committedDisposition=CurrentDialogueDisposition();
                if(committedDisposition!=requestSnapshot.EffectiveDisposition)
                {
                    var successor=requestSnapshot.WithCommittedDisposition(committedDisposition);
                    if(!InstallConversationContextSuccessor(requestSnapshot,successor,window))
                    {HandleRejectedConversationStage(requestSnapshot,window,0,playerText);return;}
                    requestSnapshot=successor;
                }
                if(conversationDispositionEnabled && boundaryAction!=ConversationSpeechBoundaryAction.Continue &&
                    HandleNpcConversationBoundary(window,playerText,boundary,boundaryAction))return;
                if (HandleConversationHostility(window, playerText, effectiveTone, choice.Intent != "social")) return;
                if (!busyDeparture && choice.SocialIntent != "family_threat" &&
                    (choice.Intent == "trade" || choice.Intent == "borrow" || choice.Intent == "return_loan") &&
                    TryPersonalCommerceConversation(window, playerText, history, requestSnapshot, choice.Intent)) return;
                if (choice.Intent == "unknown")
                {
                    conversationSuggestions.Clear();
                    SubmitSocialText(window, playerText, history,
                        knowledgeResult:"ROUTING RESULT: The current request could not be classified as a supported native topic, map action, or other supported action. No native topic or map target was looked up or selected; no action or marker was accepted. Reply briefly and in character by asking the player to clarify what they mean. Do not invent a lookup result, directions, destination, or completed action.",
                        verifiedFallback:"I'm not sure what you mean. Could you clarify?",snapshot:requestSnapshot);
                    return;
                }
                // A coarse action route is not proof that this turn requests an action.
                // A confirmed ordinary-social correction must not consume an offer,
                // change a commitment, or replace the offer with a new proposal.
                if (LocalDecisionDialogueRoute.IsOrdinarySocialDowngrade(decisionRoute, choice))
                {
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DECISION_ROUTING_SOCIAL_CORRECTION: route=" + decisionRoute +
                        "; nativeDispatch=false; pendingOfferInterpretation=false; newProposal=false");
                    SubmitOrdinarySocialText(window, playerText, history, requestSnapshot);
                    return;
                }
                // Semantic routing uses the original submitted turn. Native
                // adapters validate real targets before any saved commitment.
                if(!busyDeparture && choice.SocialIntent!="family_threat" && choice.Intent=="guide" &&
                    TryGuideConversation(window,playerText,history,all,candidates,native,requestSnapshot))return;
                if(!busyDeparture && choice.SocialIntent!="family_threat" &&
                    (decisionRoute==null || decisionRoute=="OFFER_RESPONSE") &&
                    (choice.Intent=="social" || choice.Intent=="action") &&
                    TryPendingSemanticOfferConversation(window,playerText,history,requestSnapshot))return;
                if(choice.Intent=="action")
                {
                    if(decisionRoute=="ACTION_REQUEST" || decisionRoute=="COMMITMENT_UPDATE")
                    {
                        // A new instruction supersedes an unaccepted offer. Do
                        // not let nested action interpreters reclassify it as assent.
                        pendingConversationAction=null;
                        pendingConversationActionHistory=null;
                    }
                    if(TrySemanticActionConversation(window,playerText,history,requestSnapshot)) return;
                }
                if(choice.Intent=="appointment"&&TryAppointmentConversation(window,playerText,history,submittedLocation,snapshot:requestSnapshot)) return;
                if(choice.Intent=="home"&&TrySemanticHomeConversation(window,playerText,history,snapshot:requestSnapshot)) return;
                // Ordinary dialogue may still yield a concrete NPC offer. It
                // cannot execute until a separately interpreted player assent.
                if(!busyDeparture&&choice.Intent=="social"&&!foregroundSpeech.IsDirectHostility&&
                    TryProposeConversationAction(window,playerText,history,requestSnapshot)) return;
                if ((choice.Intent=="topic" || choice.Intent=="map") &&
                    TryBrowseConversation(window, playerText, history, choice, all, native,snapshot:requestSnapshot)) return;
                if (choice.Intent == "social")
                {
                    if (pauseSaved)
                        SubmitSocialText(window, playerText, history,
                            verifiedFallback:"Understood. I will leave you be for now.", snapshot:requestSnapshot);
                    else
                        SubmitOrdinarySocialText(window, playerText, history, requestSnapshot);
                    return;
                }
                TalkManager.ListItem selected;
                if (!native.TryGetValue(choice.TopicId, out selected))
                {
                    if(choice.Intent=="map")
                        SubmitSocialText(window,playerText,history,
                            knowledgeResult:"NATIVE LOOKUP RESULT: No offered native map target matched the selected ID. No destination was selected and no map marker was placed. Ask the player which known place they mean; do not claim a destination or marker.",
                            verifiedFallback:"I couldn't identify a place I know well enough to mark. Which place do you mean?",snapshot:requestSnapshot);
                    else if(choice.Intent=="topic")
                        BeginNpcKnowledgeLookup(requestSnapshot,window,playerText,history,ownRead =>
                            SubmitSocialText(window,playerText,history,
                                knowledgeResult:!string.IsNullOrWhiteSpace(ownRead)?ownRead:
                                    "NATIVE LOOKUP RESULT: No offered native topic matched the selected ID. No native topic was selected. Ask the player to clarify; do not invent a lookup result.",
                                verifiedFallback:"I'm not sure which subject you mean. Could you be more specific?",snapshot:requestSnapshot));
                    else
                        SubmitSocialText(window,playerText,history,verifiedFallback:pauseSaved?"Understood. I will leave you be for now.":null,snapshot:requestSnapshot);
                    return;
                }
                conversationSuggestions.Clear();
                conversationBusy = false;
                AppendDialoguePair(window, playerText, AnswerNativeConversationTopic(window, selected, choice.Intent == "map"));
            };
            Action<ConversationSelection> applySelection = choice =>
            {
                // Every turn needs speech attribution, including neutral-looking action
                // routes. A router's task label must not hide a threat or harassment.
                NpcSpeechActRequest speech;
                try
                {
                    speech = NpcSpeechActRequest.Create(requestSnapshot,
                        requestPerson ?? requestSnapshot.Binding.EphemeralSpeakerToken, "player", null, playerText,
                        BoundForegroundSpeechHistory(history),ListenerRefusesFurtherCommunication(requestPerson),
                        ConversationContextFormatter.ForSemanticClassifier(requestSnapshot,2048));
                }
                catch (ArgumentException)
                {RejectUnclassifiedConversationSpeech(window,playerText);return;}
                long speechStage = BeginConversationContextStage(requestSnapshot, window);
                if (speechStage <= 0)
                { HandleRejectedConversationStage(requestSnapshot, window, speechStage, playerText); return; }
                if (!localDialogue.ClassifySpeech(NewDialogueConnection(), speech, classification =>
                    {
                        if (!CurrentMemorySession(window, requestSession)) return;
                        if (conversationPersonId != requestPerson || !TryConsumeConversationContextStage(requestSnapshot, window, speechStage))
                        { HandleRejectedConversationStage(requestSnapshot, window, speechStage, playerText); return; }
                        if(classification==null)
                        {RejectUnclassifiedConversationSpeech(window,playerText);return;}
                        foregroundSpeech=classification;
                        foregroundSpeechSource=requestSnapshot;
                        if(TryRepairNpcConversationBoundary(window,playerText,history,requestSnapshot,classification))return;
                        applyGuardedSelection(NpcSpeechActClassification.GuardSelection(requestSnapshot, choice, classification));
                    }))
                    HandleRejectedConversationStage(requestSnapshot, window, speechStage, playerText);
            };
            Action<ConversationSelection> applyRoute=route=>
            {
                if(route.Intent!="topic" && route.Intent!="map")
                {applySelection(route);return;}

                // References are factual context for the model, never a text-pattern override.
                string referenceContext=NativeConversationReferenceContext(all,native,previousId);
                long topicStage=BeginConversationContextStage(requestSnapshot,window);
                if(topicStage<=0)
                {HandleRejectedConversationStage(requestSnapshot,window,topicStage,playerText);return;}
                string topicPrompt=ConversationContextFormatter.ForRouting(requestSnapshot,2048)+"\n"+
                    referenceContext+"\n"+ConversationKnowledge.TopicSelectionPrompt(route.Intent,playerText,candidates,history);
                if(!localDialogue.Classify(NewDialogueConnection(),topicPrompt,history,bindingOutput=>
                {
                    if(!CurrentMemorySession(window,requestSession))return;
                    if(conversationPersonId!=requestPerson)
                    {HandleRejectedConversationStage(requestSnapshot,window,topicStage,playerText);return;}
                    if(!TryConsumeConversationContextStage(requestSnapshot,window,topicStage))
                    {HandleRejectedConversationStage(requestSnapshot,window,topicStage,playerText);return;}
                    if(bindingOutput==LocalDialogueClient.Unavailable || bindingOutput==LocalDialogueClient.ContextLimit)
                    {
                        conversationBusy=false;
                        AppendRememberedDialoguePair(window,playerText,DialogueReplyFallback.UnavailableReply,"unavailable");
                        DaggerfallUI.AddHUDText("Local dialogue unavailable. No topic or map action was selected.");
                        return;
                    }
                    var binding=ConversationKnowledge.ParseTopicSelection(bindingOutput,route.Intent,candidates);
                    if(binding==null)
                    {
                        conversationBusy=false;
                        AppendRememberedDialoguePair(window,playerText,DialogueReplyFallback.UnavailableReply,"unavailable");
                        DaggerfallUI.AddHUDText("No offered topic matched. No topic or map action was selected.");
                        return;
                    }

                    // Binding may supply only an offered native ID. Keep the
                    // first-pass social reading; topic selection cannot revise it.
                    int topicId=binding.Intent=="unknown"?-1:binding.TopicId;
                    var boundRoute=new ConversationSelection(route.Intent,topicId,route.Tone,route.SocialIntent);
                    applySelection(boundRoute);
                },ConversationKnowledge.TopicSelectionGrammar(route.Intent,candidates),
                    classificationTrace:SemanticActionModelCaptureProbe.ForStage("native-topic-binding")))
                    HandleRejectedConversationStage(requestSnapshot,window,topicStage,playerText);
            };
            Action<string> refineDecisionRoute=selectedDecisionRoute=>
            {
                decisionRoute=selectedDecisionRoute;
                long routeStage=BeginConversationContextStage(requestSnapshot,window);
                if (routeStage==0)
                {HandleRejectedConversationStage(requestSnapshot,window,routeStage,playerText);return;}
                if (!localDialogue.Classify(NewDialogueConnection(), prompt+LocalDecisionDialogueRoute.RefinePrompt(decisionRoute), history, output =>
                {
                    if (!CurrentMemorySession(window,requestSession)) return;
                    if(conversationPersonId!=requestPerson)
                    {HandleRejectedConversationStage(requestSnapshot,window,routeStage,playerText);return;}
                    if(!TryConsumeConversationContextStage(requestSnapshot,window,routeStage))
                    {HandleRejectedConversationStage(requestSnapshot,window,routeStage,playerText);return;}
                    if (output == LocalDialogueClient.ContextLimit || output == LocalDialogueClient.Unavailable)
                    {
                        conversationBusy=false;
                        AppendRememberedDialoguePair(window,playerText,DialogueReplyFallback.UnavailableReply,"unavailable");
                        DaggerfallUI.AddHUDText(output==LocalDialogueClient.ContextLimit?
                            "Local dialogue context unavailable. No action was accepted.":
                            "Local dialogue unavailable. No action was accepted.");
                        return;
                    }
                    var choice=ConversationKnowledge.ParseRouting(output);
                    if(choice==null || !LocalDecisionDialogueRoute.Allows(decisionRoute,choice))
                    {
                        conversationBusy=false;
                        AppendRememberedDialoguePair(window,playerText,DialogueReplyFallback.UnavailableReply,"unavailable");
                        DaggerfallUI.AddHUDText("The dialogue route was invalid. No action or topic was selected.");
                        return;
                    }
                    if(!SocialEffectScope.Required(choice)){applyRoute(choice);return;}
                    string scopePrompt=ConversationContextFormatter.ForRouting(requestSnapshot,2048)+"\n"+
                        SocialEffectScope.Prompt(playerText,history,choice.SocialIntent);
                    long scopeStage=BeginConversationContextStage(requestSnapshot,window);
                    if(scopeStage<=0)
                    {HandleRejectedConversationStage(requestSnapshot,window,scopeStage,playerText);return;}
                    if(!localDialogue.Classify(NewDialogueConnection(),scopePrompt,history,scopeOutput=>
                    {
                        if(!CurrentMemorySession(window,requestSession))return;
                        if(conversationPersonId!=requestPerson)
                        {HandleRejectedConversationStage(requestSnapshot,window,scopeStage,playerText);return;}
                        if(!TryConsumeConversationContextStage(requestSnapshot,window,scopeStage))
                        {HandleRejectedConversationStage(requestSnapshot,window,scopeStage,playerText);return;}
                        string scope=scopeOutput==null?null:scopeOutput.Trim();
                        if(!SocialEffectScope.Valid(scope))
                        {
                            conversationBusy=false;
                            AppendRememberedDialoguePair(window,playerText,DialogueReplyFallback.UnavailableReply,"unavailable");
                            return;
                        }
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SOCIAL_EFFECT_SCOPE: proposed="+choice.SocialIntent+"; scope="+scope);
                        applyRoute(SocialEffectScope.Apply(choice,scope));
                    },SocialEffectScope.Grammar,classificationTrace:
                        SemanticActionModelCaptureProbe.ForStage("social-effect-scope")))
                        HandleRejectedConversationStage(requestSnapshot,window,scopeStage,playerText);
                },LocalDecisionDialogueRoute.Grammar(decisionRoute),classificationTrace:
                    SemanticActionModelCaptureProbe.ForStage("route-intent")))
                {
                    if(HandleRejectedConversationStage(requestSnapshot,window,routeStage,playerText))
                        DaggerfallUI.AddHUDText("Waiting for the current reply.");
                }
            };
            if(!TryBeginDecisionRouting(requestSnapshot,window,playerText,refineDecisionRoute))
                refineDecisionRoute(null);
        }

        // Retrieval terms only rank offered native topics. They never choose an intent or game effect.
        private static string[] ConversationTopicSearchTerms(string text)
        {
            var words=new HashSet<string>(StringComparer.Ordinal);
            if(string.IsNullOrEmpty(text)) return new string[0];
            int start=-1;
            for(int index=0;index<=text.Length;index++)
            {
                if(index<text.Length && char.IsLetterOrDigit(text[index]))
                {if(start<0)start=index;continue;}
                if(start>=0 && index-start>=3)words.Add(text.Substring(start,index-start).ToLowerInvariant());
                start=-1;
            }
            return words.ToArray();
        }

        private static int TopicRank(ConversationTopic topic, string[] words, int previous)
        {
            int score = topic.Id == previous ? 10000 : topic.Group ? 30 : 0;
            string caption = topic.Caption.ToLowerInvariant(), path = topic.Path.ToLowerInvariant();
            foreach (string word in words)
            {
                string stem = word.EndsWith("s") && word.Length > 3 ? word.Substring(0, word.Length - 1) : word;
                if (caption.Contains(stem)) score += 100;
                else if (path.Contains(stem)) score += 40;
            }
            return score;
        }

        private static void AddConversationTopics(List<TalkManager.ListItem> items, string path,
            List<ConversationTopic> topics, Dictionary<int, TalkManager.ListItem> native, HashSet<TalkManager.ListItem> seen)
        {
            if (items == null) return;
            foreach (var item in items)
            {
                if (item == null || item.type == TalkManager.ListItemType.NavigationBack || !seen.Add(item)) continue;
                int id = topics.Count;
                native[id] = item;
                bool group = item.type == TalkManager.ListItemType.ItemGroup;
                topics.Add(new ConversationTopic(id, item.caption, path, group));
                if (group) AddConversationTopics(item.listChildItems, path + " / " + item.caption, topics, native, seen);
            }
        }

        private int ConversationOffset()
        { return conversationPersonId == null ? conversationEphemeralOffset : store.ConversationDisposition(conversationPersonId); }

        private bool ConversationPartnerIsChild()
        {
            var manager = GameManager.Instance.TalkManager;
            var actor = manager.CurrentNPCType==TalkManager.NPCType.Mobile && manager.MobileNPC ? manager.MobileNPC.GetComponent<ResidentActor>() : null;
            return actor && actor.Resident != null && actor.Resident.AgeGroup == ResidentAgeGroup.Child ||
                manager.CurrentNPCType==TalkManager.NPCType.Static && manager.StaticNPC && manager.StaticNPC.IsChildNPC;
        }

        private void ReportChildDialogueOffense()
        {
            var player = GameManager.Instance.PlayerEntity;
            // Preserve existing charges, particularly Murder, rather than reducing them.
            if(player.CrimeCommitted == DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.None)
                player.CrimeCommitted = DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.Trespassing;
            var guards = GetComponent<GuardPopulation>();
            if(guards) guards.ReportDialogueOffense();
        }

        private string ResolveConversationFlirt()
        {
            var manager = GameManager.Instance.TalkManager;
            var mobile = manager.CurrentNPCType==TalkManager.NPCType.Mobile ? manager.MobileNPC : null;
            var actor = mobile ? mobile.GetComponent<ResidentActor>() : null;
            var stationary = manager.CurrentNPCType==TalkManager.NPCType.Static ? manager.StaticNPC : null;
            bool child = ConversationPartnerIsChild();
            string gender = stationary ? stationary.Data.gender.ToString() : mobile ? mobile.Gender.ToString() : "unknown";
            string identity = conversationPersonId ?? (mobile ? "unmanaged:" + mobile.GetInstanceID() : null);
            string tone = ConversationFlirting.Tone(identity, GameManager.Instance.PlayerEntity.Name,
                gender, GameManager.Instance.PlayerEntity.Gender.ToString(), child, ConversationOffset());
            conversationResponseCue = child ? "Do not engage in romance. Redirect to ordinary conversation." :
                tone == "polite" ? "This romantic approach is welcome. Feel flattered and respond warmly, without promising a relationship." :
                tone == "rude" ? "This romantic approach is unwelcome. Decline clearly and show mild displeasure; do not reciprocate." :
                "Do not reciprocate romance; redirect politely to ordinary conversation.";
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_FLIRT: reception=" + tone + "; child=" + child);
            return tone;
        }

        private void ApplyConversationDisposition(string tone, string text)
        {
            if (!conversationDispositionEnabled || saveFault) return;
            int change;
            if (conversationPersonId != null)
            {
                change = store.ApplyConversationTone(conversationPersonId, tone == "apology" ? "polite" : tone, text, DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
                if (conversationObservedSpeech != null)
                {
                    // Preserve the exact offset delta, not a reconstructed displayed-reaction delta.
                    conversationObservedSpeech = conversationObservedSpeech.WithAppliedAffinity(change);
                    conversationAffinityProcessedForTurn = true;
                }
            }
            else
            {
                int before = conversationEphemeralOffset;
                int penalty = tone == "insult" ? (conversationEphemeralInsults++ > 0 ? -25 : -10) : tone == "rude" ? -2 : 0;
                conversationEphemeralOffset = Mathf.Clamp(before + penalty, -200, 0);
                change = conversationEphemeralOffset - before;
            }
            if (change != 0 && reactionToPlayerField != null)
            {
                reactionToPlayerField.SetValue(GameManager.Instance.TalkManager, ConversationEffects.Reaction(conversationNativeDisposition, ConversationOffset()));
                if (lastConversationTone != null) lastConversationTone.SetValue(GameManager.Instance.TalkManager, -1);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DIALOGUE_DISPOSITION: delta=" + change + "; offset=" + ConversationOffset());
            }
        }

        private void ApplyFamilyThreatDisposition()
        {
            if (!conversationDispositionEnabled) return;
            int offset = Mathf.Clamp(-100 - conversationNativeDisposition, -200, 5);
            if (conversationPersonId != null && !saveFault)
            {
                int previousOffset = store.ConversationDisposition(conversationPersonId);
                int appliedOffset = store.SetConversationThreatDisposition(conversationPersonId, conversationNativeDisposition,
                    DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
                if (conversationObservedSpeech != null)
                {
                    // The transition is in the same offset units; the floor marker excludes any supplement.
                    conversationObservedSpeech = conversationObservedSpeech.WithAppliedAffinity(
                        appliedOffset - previousOffset,
                        NpcObservedSpeechAffinityExclusion.FamilyThreatDispositionFloor);
                    conversationAffinityProcessedForTurn = true;
                }
            }
            else conversationEphemeralOffset = offset;
            if (reactionToPlayerField != null)
                reactionToPlayerField.SetValue(GameManager.Instance.TalkManager,
                    ConversationEffects.Reaction(conversationNativeDisposition, offset));
            if (lastConversationTone != null) lastConversationTone.SetValue(GameManager.Instance.TalkManager, -1);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAMILY_THREAT: baseline=" + conversationNativeDisposition + "; offset=" + offset +
                "; actual=-100");
        }

        private NpcExperienceAppliedEffectsMask ObservedSpeechMask(bool moodAlreadyApplied)
        {
            NpcExperienceAppliedEffectsMask mask = moodAlreadyApplied
                ? NpcExperienceAppliedEffectsMask.Mood : NpcExperienceAppliedEffectsMask.None;
            if (conversationAffinityProcessedForTurn) mask |= NpcExperienceAppliedEffectsMask.Affinity;
            return mask;
        }

        private string AnswerNativeConversationTopic(DaggerfallTalkWindow window, TalkManager.ListItem topic, bool mark)
        {
            var manager = GameManager.Instance.TalkManager;
            if (topic.type == TalkManager.ListItemType.ItemGroup)
            {
                conversationLastTopic = null;
                var names = topic.listChildItems == null ? new string[0] : topic.listChildItems
                    .Where(t => t.type != TalkManager.ListItemType.NavigationBack).Select(t => t.caption).Take(10).ToArray();
                return names.Length == 0 ? "Which place did you have in mind?" :
                    "Which place did you mean: " + string.Join("; ", names) + "?";
            }
            if(mark && topic.questionType==TalkManager.QuestionType.QuestPerson)
            {
                TalkManager.ListItem wherePerson;
                if(TryNativeQuestPersonLocation(manager,topic,out wherePerson))topic=wherePerson;
            }
            conversationLastTopic = topic;
            var tone = selectedToneField == null ? DaggerfallTalkWindow.TalkTone.Normal : (DaggerfallTalkWindow.TalkTone)selectedToneField.GetValue(window);
            manager.GetQuestionText(topic, tone); // Sets the subject used by native answer macros.
            string answer = manager.GetAnswerText(topic);
            if (!mark) return answer;
            if (!conversationMapRequestsEnabled) return answer + " I can describe the way, though I cannot mark your map.";
            if (topic.questionType != TalkManager.QuestionType.LocalBuilding)
                return answer; // Quest/person/regional reveals remain entirely native-owned.
            bool knows = topic.npcKnowledgeAboutItem == TalkManager.NPCKnowledgeAboutItem.KnowsAboutItem || manager.NPCsKnowEverything();
            int reaction = reactionTierField == null ? 0 : (int)reactionTierField.GetValue(manager);
            if (!knows || reaction == 0) return answer;
            if (GameManager.Instance.IsPlayerInside) return answer; // Preserve native indoor direction-only rule.
            manager.GetKeySubjectBuildingOnMap(); // Native %loc macro discovers and records quest hints.
            return GameManager.Instance.PlayerGPS.HasDiscoveredBuilding(topic.buildingKey) ?
                "I've marked " + topic.caption + " on your map." : answer;
        }
    }
}
