using System;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private static string BoundForegroundSpeechHistory(string history)
        {
            if (string.IsNullOrEmpty(history)) return "";
            const int budget = 8192;
            if (history.Length <= budget) return history;
            const string omitted = "[Earlier conversation omitted; absence here does not establish consent or misconduct.]\n";
            int start = history.Length - (budget - omitted.Length);
            int line = history.IndexOf('\n', start);
            if (line >= start && line < history.Length - 1) start = line + 1;
            return omitted + history.Substring(start);
        }

        private bool ListenerRefusesFurtherCommunication(string personId)
        {
            return conversationDispositionEnabled && !saveFault && store != null &&
                !string.IsNullOrEmpty(personId) && store.ShouldEnforceNpcConversationBoundary(personId,
                    DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
        }

        private bool CanApproachNpcAfterBoundary(string personId)
        {
            if(!conversationDispositionEnabled || saveFault || store==null)return true;
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            // A setup failure without a route/roster row must not leave a saved
            // Started report blocking this person forever after its native expiry.
            store.TryAbandonExpiredNpcConversationBoundaryReport(personId,now);
            return store.CanTalkToNpcAfterBoundary(personId,now);
        }

        private string CurrentConversationBoundaryContext()
        {
            if (!conversationDispositionEnabled || saveFault || store == null ||
                string.IsNullOrEmpty(conversationPersonId)) return "";
            var boundary = store.ReadNpcConversationBoundary(conversationPersonId,
                DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
            if (boundary == null) return "";
            if (ListenerRefusesFurtherCommunication(conversationPersonId))
                return "\nVERIFIED CONVERSATION BOUNDARY: This player previously insulted or harassed you. " +
                    "You ended that conversation and still refuse further communication. " +
                    "Make your refusal clear. Do not greet warmly, call the player a friend, offer help, or invent forgiveness. " +
                    "Only a sincere apology or plausible explanation of that misconduct can reopen communication; it does not erase resentment or fear.";
            return "\nVERIFIED CONVERSATION HISTORY: This player previously crossed your personal boundary. " +
                "Use your current saved mood, resentment, fear and relationship. An accepted explanation is not verified truth, restored trust or instant forgiveness.";
        }

        private void RejectUnclassifiedConversationSpeech(DaggerfallTalkWindow window, string playerText)
        {
            conversationBusy = false;
            AppendRememberedDialoguePair(window, playerText, DialogueReplyFallback.UnavailableReply, "unavailable");
        }

        private bool TryRepairNpcConversationBoundary(DaggerfallTalkWindow window, string playerText,
            string history, ConversationContextSnapshot snapshot, NpcSpeechActClassification classification)
        {
            if (classification.Kind != NpcExperienceSpeechActKind.RepairAttempt ||
                !conversationDispositionEnabled || saveFault || store==null ||
                !store.HasUnrepairedNpcConversationBoundary(conversationPersonId,
                    DaggerfallUnity.Instance.WorldTime.Now.ToSeconds())) return false;
            if (!store.TryRepairNpcConversationBoundary(conversationObservedSpeechTicket, conversationObservedSpeech,
                snapshot, classification))
            {
                RejectUnclassifiedConversationSpeech(window, playerText);
                return true;
            }
            pendingConversationAction = null;
            pendingConversationActionHistory = null;
            SubmitSocialText(window, playerText, history,
                knowledgeResult: "VERIFIED BOUNDARY REPAIR: The player offered a contextually plausible apology or explanation. " +
                    "You allow them to speak again. Their explanation is a claim, not verified truth. " +
                    "No task, trade, promise, restored trust or instant forgiveness was accepted. Respond cautiously, in your own voice, " +
                    "consistent with your still-negative saved mood and relationship.",
                verifiedFallback: "I'll hear you out, but I haven't forgotten what you said.", snapshot: snapshot);
            return true;
        }

        private void ApplyNpcConversationBoundaryDisposition(NpcConversationBoundaryRecord boundary)
        {
            if (boundary == null || saveFault || store == null) return;
            int change = store.SetConversationBoundaryDispositionAtMost(conversationPersonId,
                conversationNativeDisposition,
                boundary.Action == ConversationSpeechBoundaryAction.EndAndReport ? -100 : -50,
                DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
            if (conversationObservedSpeech != null)
            {
                conversationObservedSpeech = conversationObservedSpeech.WithAppliedAffinity(change);
                conversationAffinityProcessedForTurn = true;
            }
            if (reactionToPlayerField != null)
                reactionToPlayerField.SetValue(GameManager.Instance.TalkManager,
                    ConversationEffects.Reaction(conversationNativeDisposition, ConversationOffset()));
            if (lastConversationTone != null) lastConversationTone.SetValue(GameManager.Instance.TalkManager, -1);
        }

        private bool HandleNpcConversationBoundary(DaggerfallTalkWindow window, string playerText,
            NpcConversationBoundaryRecord boundary, ConversationSpeechBoundaryAction action)
        {
            conversationBusy = false;
            pendingConversationAction = null;
            pendingConversationActionHistory = null;
            conversationSuggestions.Clear();
            if(boundary!=null && streets)
                streets.WithdrawConversationActionForBoundary(boundary.PersonId,boundary.ReceiptId);
            bool delivered = false, seeking = false;
            if (boundary != null && action == ConversationSpeechBoundaryAction.EndAndReport)
            {
                if (GameManager.Instance.PlayerEnterExit.IsPlayerInsideBuilding)
                {
                    var guards = GetComponent<GuardPopulation>();
                    delivered = guards && guards.TryReportConversationBoundaryAt(boundary.ReceiptId,
                        boundary.PersonId, null);
                }
                else if (streets && conversationActor)
                    seeking = streets.StartDialogueComplaint(conversationActor, boundary.PersonId, boundary.ReceiptId);
                if (!delivered && !seeking &&
                    store.ReadNpcConversationBoundary(boundary.PersonId,
                        DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()).ReportStatus == NpcConversationBoundaryReportStatus.Pending)
                    store.TryMarkNpcConversationBoundaryReport(boundary.PersonId, boundary.ReceiptId,
                        NpcConversationBoundaryReportStatus.Abandoned);
            }
            string reply = action == ConversationSpeechBoundaryAction.EndConversation
                ? "Leave me alone. I have nothing more to say to you."
                : "Enough. Stay away from me." + (delivered ? " The guards have been alerted." :
                    seeking ? " I am going to find a guard." : "");
            // Accept the exact speech before closing. Deferred audio appends can be
            // cancelled by window teardown and must not lose the incident or its memory.
            AppendUnrecordedDialoguePairWithSpeech(window, playerText, reply, false,
                conversationObservedSpeechTicket, conversationObservedSpeech, ObservedSpeechMask(false));
            string person=boundary==null?conversationPersonId:boundary.PersonId;
            if (!string.IsNullOrEmpty(person) && !saveFault && store != null)
                store.RememberConversation(person, DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(),
                    memoryDate ?? CurrentConversationTimestamp(), ConversationMemoryPlace(), playerText, reply, "action");
            DaggerfallUI.Instance.PopToHUD();
            DaggerfallUI.AddHUDText(reply);
            return true;
        }
    }
}
