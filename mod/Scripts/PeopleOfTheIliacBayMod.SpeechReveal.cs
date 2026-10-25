using System;
using System.Collections.Generic;
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
        private sealed class PendingAcceptedSpeechReveal
        {
            public string RequestId;
            public string ConversationToken;
            public string PersonId;
            public string ExactText;
            public string PlayerText;
            public DaggerfallTalkWindow Talk;
            public ConversationContextSnapshot Snapshot;
            public int MemorySession;
            public long SaveEpoch;
            public long RequestRevision;
            public Action AppendCanonicalPair;
            public Action AfterPresentation;
            public Action<BreezeTtsTrialBridge.MixerPlaybackStartReceipt> StartedHandler;
            public Action<BreezeTtsTrialBridge.PrePlaybackTerminalReceipt> TerminalHandler;
            public bool Resolved;
        }

        private PendingAcceptedSpeechReveal pendingAcceptedSpeechReveal;
        private bool conversationPendingSpeechReveal;

        private bool TryDeferAcceptedSpeechAppend(DaggerfallTalkWindow talk, ConversationContextSnapshot snapshot,
            string playerText, string exactText, Action appendCanonicalPair, Action afterPresentation = null)
        {
            // Publish selected-read text synchronously at its validated reply-stage commit.
            if (HasSelectedConversationInventoryRead(snapshot) || HasSelectedConversationMemoryRead(snapshot)) return false;
            if (!breezeTtsTrial || suppressFullWavTtsForNextAppend || talk == null || snapshot == null || snapshot.Binding == null ||
                appendCanonicalPair == null || string.IsNullOrWhiteSpace(exactText) ||
                !CurrentMemorySession(talk, memorySession) || memoryWindow != talk ||
                string.IsNullOrWhiteSpace(memoryPersonId) ||
                !string.Equals(snapshot.Binding.PersonId, memoryPersonId, StringComparison.Ordinal) ||
                !string.Equals(snapshot.Binding.SessionId, conversationContextSessionId, StringComparison.Ordinal) ||
                !string.Equals(playerText, conversationContextCapturedPlayerText, StringComparison.Ordinal) ||
                pendingAcceptedSpeechReveal != null) return false;

            var pending = new PendingAcceptedSpeechReveal
            {
                ConversationToken = conversationContextSessionId,
                PersonId = memoryPersonId,
                ExactText = exactText,
                PlayerText = playerText,
                Talk = talk,
                Snapshot = snapshot,
                MemorySession = memorySession,
                SaveEpoch = conversationSaveEpoch,
                RequestRevision = snapshot.Binding.RequestRevision,
                AppendCanonicalPair = appendCanonicalPair,
                AfterPresentation = afterPresentation
            };
            pendingAcceptedSpeechReveal = pending;
            conversationPendingSpeechReveal = true;
            conversationBusy = true;
            Action<BreezeTtsTrialBridge.MixerPlaybackStartReceipt> started = receipt => OnAcceptedSpeechMixerStarted(pending, receipt);
            Action<BreezeTtsTrialBridge.PrePlaybackTerminalReceipt> terminal = receipt => OnAcceptedSpeechPrePlaybackTerminal(pending, receipt);
            pending.StartedHandler = started;
            pending.TerminalHandler = terminal;
            breezeTtsTrial.MixerPlaybackStarted += started;
            breezeTtsTrial.PrePlaybackTerminated += terminal;

            string requestId;
            bool accepted = NotifyNpcSpeechWithPresentation(memoryPersonId,
                GameManager.Instance == null || GameManager.Instance.TalkManager == null
                    ? memoryPersonId : GameManager.Instance.TalkManager.NameNPC,
                exactText, conversationContextSessionId, CurrentTrialVoiceGender(),
                CurrentTtsVoiceIdentity(), out requestId);
            if (!accepted || string.IsNullOrWhiteSpace(requestId))
            {
                breezeTtsTrial.MixerPlaybackStarted -= started;
                breezeTtsTrial.PrePlaybackTerminated -= terminal;
                pendingAcceptedSpeechReveal = null;
                conversationPendingSpeechReveal = false;
                conversationBusy = false;
                return false;
            }
            pending.RequestId = requestId;
            return true;
        }

        private bool IsCurrentAcceptedSpeechPending(PendingAcceptedSpeechReveal pending)
        {
            return pending != null && !pending.Resolved && ReferenceEquals(pendingAcceptedSpeechReveal, pending) &&
                pending.Talk != null && pending.Talk.IsSetup && DaggerfallUI.UIManager.ContainsWindow(pending.Talk) &&
                conversationSessionOpen && conversationBusy && CurrentMemorySession(pending.Talk, pending.MemorySession) &&
                memoryWindow == pending.Talk && string.Equals(memoryPersonId, pending.PersonId, StringComparison.Ordinal) &&
                string.Equals(conversationContextSessionId, pending.ConversationToken, StringComparison.Ordinal) &&
                conversationSaveEpoch == pending.SaveEpoch &&
                string.Equals(conversationContextCapturedPlayerText, pending.PlayerText, StringComparison.Ordinal) &&
                conversationContextRequestRevision == pending.RequestRevision;
        }

        private void OnAcceptedSpeechMixerStarted(PendingAcceptedSpeechReveal pending,
            BreezeTtsTrialBridge.MixerPlaybackStartReceipt receipt)
        {
            if (pending == null || receipt == null || !IsCurrentAcceptedSpeechPending(pending) ||
                !string.Equals(receipt.RequestId, pending.RequestId, StringComparison.Ordinal) ||
                !string.Equals(receipt.ConversationToken, pending.ConversationToken, StringComparison.Ordinal) ||
                !string.Equals(receipt.PersonId, pending.PersonId, StringComparison.Ordinal) ||
                !string.Equals(receipt.ExactText, pending.ExactText, StringComparison.Ordinal)) return;
            ResolveAcceptedSpeechPending(pending, initialReveal: true);
        }

        private void OnAcceptedSpeechPrePlaybackTerminal(PendingAcceptedSpeechReveal pending,
            BreezeTtsTrialBridge.PrePlaybackTerminalReceipt receipt)
        {
            if (pending == null || receipt == null || !IsCurrentAcceptedSpeechPending(pending) ||
                !string.Equals(receipt.RequestId, pending.RequestId, StringComparison.Ordinal) ||
                !string.Equals(receipt.ConversationToken, pending.ConversationToken, StringComparison.Ordinal) ||
                !string.Equals(receipt.PersonId, pending.PersonId, StringComparison.Ordinal) ||
                !string.Equals(receipt.ExactText, pending.ExactText, StringComparison.Ordinal)) return;
            ResolveAcceptedSpeechPending(pending, initialReveal: false);
        }

        private void ResolveAcceptedSpeechPending(PendingAcceptedSpeechReveal pending, bool initialReveal)
        {
            if (pending == null || pending.Resolved || !ReferenceEquals(pendingAcceptedSpeechReveal, pending)) return;
            if (breezeTtsTrial != null)
            {
                if (pending.StartedHandler != null) breezeTtsTrial.MixerPlaybackStarted -= pending.StartedHandler;
                if (pending.TerminalHandler != null) breezeTtsTrial.PrePlaybackTerminated -= pending.TerminalHandler;
            }
            pending.Resolved = true;
            pendingAcceptedSpeechReveal = null;
            conversationPendingSpeechReveal = false;
            if (!IsCurrentAcceptedSpeechOwner(pending)) return;

            // Pending player/thinking rows disappear before the canonical pair
            // is appended. Capture the new-row boundary after removing them.
            RemoveConversationPendingOverlayFor(pending.Talk);
            ListBox transcript = conversationListField == null ? null : conversationListField.GetValue(pending.Talk) as ListBox;
            int firstNewRow = transcript == null ? 0 : transcript.Count;
            conversationBusy = false;
            suppressFullWavTtsForNextAppend = true;
            try { pending.AppendCanonicalPair(); }
            finally { suppressFullWavTtsForNextAppend = false; }
            if (initialReveal)
                AttachTrialReplayToAppendedAnswerRows(pending.Talk, firstNewRow, pending.RequestId,
                    pending.ConversationToken, initialReveal: true);
            if (pending.AfterPresentation != null) pending.AfterPresentation();
        }

        private void InvalidatePendingAcceptedSpeechReveal()
        {
            PendingAcceptedSpeechReveal pending = pendingAcceptedSpeechReveal;
            if (pending == null) { conversationPendingSpeechReveal = false; return; }
            conversationBusy = false;
            pending.Resolved = true;
            if (breezeTtsTrial != null)
            {
                if (pending.StartedHandler != null) breezeTtsTrial.MixerPlaybackStarted -= pending.StartedHandler;
                if (pending.TerminalHandler != null) breezeTtsTrial.PrePlaybackTerminated -= pending.TerminalHandler;
            }
            pendingAcceptedSpeechReveal = null;
            conversationPendingSpeechReveal = false;
        }

        private bool IsCurrentAcceptedSpeechOwner(PendingAcceptedSpeechReveal pending)
        {
            return pending != null && pending.Talk != null && pending.Talk.IsSetup &&
                DaggerfallUI.UIManager.ContainsWindow(pending.Talk) && conversationSessionOpen &&
                CurrentMemorySession(pending.Talk, pending.MemorySession) && memoryWindow == pending.Talk &&
                string.Equals(memoryPersonId, pending.PersonId, StringComparison.Ordinal) &&
                string.Equals(conversationContextSessionId, pending.ConversationToken, StringComparison.Ordinal) &&
                conversationSaveEpoch == pending.SaveEpoch &&
                string.Equals(conversationContextCapturedPlayerText, pending.PlayerText, StringComparison.Ordinal) &&
                conversationContextRequestRevision == pending.RequestRevision;
        }

        private bool IsAwaitingSpeechRevealFor(DaggerfallTalkWindow talk, ConversationContextSnapshot snapshot,
            string playerText)
        {
            PendingAcceptedSpeechReveal pending = pendingAcceptedSpeechReveal;
            return conversationPendingSpeechReveal && IsCurrentAcceptedSpeechPending(pending) &&
                pending.Talk == talk && ReferenceEquals(pending.Snapshot, snapshot) &&
                string.Equals(pending.PlayerText, playerText, StringComparison.Ordinal);
        }
    }
}
