using System;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private NpcObservedSpeechSession conversationSpeechSession;
        private NpcObservedSpeechTicket conversationObservedSpeechTicket;

        private void BeginConversationSpeechSession()
        {
            EndConversationSpeechSession();
            if (saveFault || store == null || string.IsNullOrWhiteSpace(conversationPersonId) ||
                string.IsNullOrWhiteSpace(conversationContextSessionId) ||
                !store.IsKnownNpcObservedSpeechSpeaker(conversationPersonId)) return;
            store.TryBeginNpcObservedSpeechSession(conversationPersonId,
                conversationContextSessionId, out conversationSpeechSession);
        }

        private void EndConversationSpeechSession()
        {
            if (conversationSpeechSession != null && store != null)
                store.TryCloseNpcObservedSpeechSession(conversationSpeechSession);
            conversationSpeechSession = null;
            conversationObservedSpeechTicket = null;
            conversationObservedSpeech = null;
            conversationAffinityProcessedForTurn = false;
        }

        private bool TryIssueConversationSpeech(DaggerfallTalkWindow window, string playerText)
        {
            conversationObservedSpeechTicket = null;
            conversationObservedSpeech = null;
            conversationAffinityProcessedForTurn = false;
            if (saveFault || store == null || string.IsNullOrWhiteSpace(conversationPersonId) ||
                !store.IsKnownNpcObservedSpeechSpeaker(conversationPersonId)) return true;

            ConversationSpeakerCapture speaker;
            bool currentSpeaker = TryResolveConversationSpeaker(out speaker) && speaker != null &&
                ReferenceEquals(speaker.NativeObject, conversationContextSessionSpeaker) &&
                string.Equals(speaker.PersonId, conversationPersonId, StringComparison.Ordinal);
            bool issued = false;
            if (currentSpeaker && window != null && IsActiveConversationWindow(window) &&
                !string.IsNullOrWhiteSpace(conversationContextSessionId) &&
                conversationContextRequestRevision < long.MaxValue)
            {
                if (conversationSpeechSession == null)
                    store.TryBeginNpcObservedSpeechSession(conversationPersonId,
                        conversationContextSessionId, out conversationSpeechSession);
                NpcObservedSpeechTicket ticket = null;
                issued = conversationSpeechSession != null &&
                    store.TryIssueNpcObservedSpeech(conversationSpeechSession,
                        conversationContextRequestRevision + 1, playerText,
                        DaggerfallUnity.Instance.WorldTime.Now.ToSeconds(), out ticket);
                if (issued)
                {
                    conversationObservedSpeechTicket = ticket;
                    conversationObservedSpeech = ticket.Receipt;
                }
            }
            if (issued) return true;
            AppendRejectedPlayerTextOnly(window, playerText);
            DaggerfallUI.AddHUDText("This conversation could not record your words. Please try again.");
            Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_OBSERVED_SPEECH: known speaker issue rejected; no reply routed.");
            return false;
        }

        private bool TryBindConversationSpeech(ConversationContextSnapshot snapshot,
            DaggerfallTalkWindow window, string playerText)
        {
            if (conversationObservedSpeechTicket == null) return true;
            if (snapshot != null && snapshot.Binding != null && store != null && !saveFault &&
                store.TryBindNpcObservedSpeechContext(conversationObservedSpeechTicket, snapshot.Binding)) return true;
            conversationObservedSpeechTicket = null;
            conversationObservedSpeech = null;
            conversationAffinityProcessedForTurn = false;
            if (snapshot != null && snapshot.Binding != null)
                conversationContextRejectedRequestRevision = snapshot.Binding.RequestRevision;
            AppendRejectedPlayerTextOnly(window, playerText);
            DaggerfallUI.AddHUDText("This conversation could not bind your words. Please try again.");
            Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_OBSERVED_SPEECH: context binding rejected; no reply routed.");
            return false;
        }

        private void QueueObservedSpeechAfterAppend(NpcObservedSpeechTicket ticket,
            NpcObservedSpeechReceipt receipt, NpcExperienceAppliedEffectsMask mask, string appendedPlayerText)
        {
            if (ticket == null || receipt == null) return;
            try
            {
                NpcObservedSpeechAcceptanceResult result = saveFault || store == null ||
                    !string.Equals(ticket.ExactText, appendedPlayerText, StringComparison.Ordinal)
                    ? NpcObservedSpeechAcceptanceResult.Rejected
                    : store.TryAcceptNpcObservedSpeechAfterAppend(ticket, receipt, mask);
                if (result == NpcObservedSpeechAcceptanceResult.Rejected)
                    Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_OBSERVED_SPEECH: appended turn was not accepted.");
                else if (result == NpcObservedSpeechAcceptanceResult.AcceptedPending)
                    Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_OBSERVED_SPEECH: appended turn retained as pending source.");
            }
            catch (Exception exception)
            {
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_OBSERVED_SPEECH: acceptance failed: " +
                    exception.GetType().Name);
            }
            if (ReferenceEquals(conversationObservedSpeechTicket, ticket))
            {
                conversationObservedSpeechTicket = null;
                conversationObservedSpeech = null;
                conversationAffinityProcessedForTurn = false;
            }
        }
    }
}
