using System;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private const float ConversationPendingDelaySeconds = 1f;
        private const string ConversationPendingProbeSwitch = "-peopleoftheiliacbay-pending-indicator-probe";
        private const string PendingTranscriptRowName = "PeopleOfTheIliacBayPendingEphemeral";
        internal static readonly Color ConversationPendingTextColor = new Color32(190, 120, 255, 255);
        private readonly System.Random conversationPendingRandom = new System.Random();
        private ListBox conversationPendingTranscript;
        private ListBox.ListItem conversationPendingPlayerRow, conversationPendingRow;
        private TextLabel conversationPendingLabel;
        private DaggerfallTalkWindow conversationPendingWindow, conversationPendingOverlayWindow;
        private bool conversationPendingTracking, conversationPendingSuppressed, conversationPendingRequestSent;
        private bool conversationPendingReturningGreeting;
        private float conversationPendingStartedAt;
        private string conversationPendingSpeakerId, conversationPendingSpeakerName, conversationPendingSpeakerType;
        private UnityEngine.Object conversationPendingSpeakerObject;
        private string conversationPendingPlayerText;
        private ConversationContextSnapshot conversationPendingSnapshot;
        private ConversationContextBinding conversationPendingBinding;
        private int conversationPendingSession;
        private long conversationPendingSaveEpoch, conversationPendingRequestRevision;
        private long conversationPendingRenderedRevision = long.MinValue;
        private int conversationLastPendingVariant = -1;

        private static bool IsPendingTranscriptItem(ListBox.ListItem item)
        { return item != null && item.textLabel != null && item.textLabel.Name == PendingTranscriptRowName; }

        private void UpdateConversationPendingIndicator(DaggerfallTalkWindow talk)
        {
            bool busy = conversationBusy;
            if (!localDialogueEnabled || !localDialogue || talk == null || !talk.IsSetup || !conversationSessionOpen || !busy)
            {
                InvalidatePendingAcceptedSpeechReveal();
                RemoveConversationPendingOverlay();
                conversationPendingSuppressed = busy && conversationSessionOpen && talk != null;
                return;
            }
            if (conversationPendingSuppressed) return;
            TalkManager manager = GameManager.Instance == null ? null : GameManager.Instance.TalkManager;
            ConversationContextSnapshot snapshot = conversationContextCurrentSnapshot;
            string playerText = conversationContextCapturedPlayerText;
            bool awaitingSpeechStart = IsAwaitingSpeechRevealFor(talk, snapshot, playerText);
            if (manager == null || snapshot == null || snapshot.Binding == null ||
                string.IsNullOrWhiteSpace(playerText) ||
                (!awaitingSpeechStart && !IsCurrentConversationContext(snapshot, talk)))
            { InvalidatePendingAcceptedSpeechReveal(); RemoveConversationPendingOverlay(); return; }

            string speakerName = manager.NameNPC ?? string.Empty;
            string speakerId = conversationPersonId ?? string.Empty;
            string speakerType = manager.CurrentNPCType.ToString();
            UnityEngine.Object speakerObject = CurrentPendingSpeakerObject(manager);
            long revision = snapshot.Binding.RequestRevision;
            var transcript = conversationListField == null ? null : conversationListField.GetValue(talk) as ListBox;
            if (transcript == null) return;
            if (conversationPendingTracking && conversationPendingRequestRevision != revision)
                RemoveConversationPendingOverlay();
            if (conversationPendingTracking &&
                (conversationPendingWindow != talk || conversationPendingSession != memorySession ||
                 conversationPendingSaveEpoch != conversationSaveEpoch || conversationPendingSpeakerId != speakerId ||
                 conversationPendingSpeakerName != speakerName || conversationPendingSpeakerType != speakerType ||
                 !ReferenceEquals(conversationPendingSpeakerObject, speakerObject) ||
                 !string.Equals(conversationPendingPlayerText, playerText, StringComparison.Ordinal) ||
                 !ReferenceEquals(conversationPendingSnapshot, snapshot) || conversationPendingBinding == null ||
                 !conversationPendingBinding.HasSameRequestBinding(snapshot.Binding)))
            {
                RemoveConversationPendingOverlay();
                conversationPendingSuppressed = true;
                return;
            }
            if (!conversationPendingTracking)
            {
                conversationPendingTracking = true;
                conversationPendingWindow = talk;
                conversationPendingSpeakerId = speakerId;
                conversationPendingSpeakerName = speakerName;
                conversationPendingSpeakerType = speakerType;
                conversationPendingSpeakerObject = speakerObject;
                conversationPendingPlayerText = playerText;
                conversationPendingSnapshot = snapshot;
                conversationPendingBinding = snapshot.Binding;
                conversationPendingSession = memorySession;
                conversationPendingSaveEpoch = conversationSaveEpoch;
                conversationPendingRequestRevision = revision;
                conversationPendingStartedAt = Time.unscaledTime;
                conversationPendingRequestSent = false;
                // Returning greetings have no player turn: their internal prompt
                // must never be rendered as if the player said it.
                if (!conversationPendingReturningGreeting)
                {
                    // The native talk window normally appends a question and answer
                    // together. While the reply is pending, show the exact submitted
                    // player turn now, then the NPC's literal waiting line below it.
                    // Both rows are presentation-only and carry the same exclusion
                    // sentinel; the normal final pair replaces them by exact identity.
                    AppendPresentationLine(transcript, playerText, true);
                    conversationPendingPlayerRow = transcript.GetItem(transcript.Count - 1);
                    conversationPendingPlayerRow.textLabel.Name = PendingTranscriptRowName;
                }
                conversationPendingTranscript = transcript;
                conversationPendingOverlayWindow = talk;
                RefreshTranscriptScroll(talk);
            }
            if (conversationPendingRequestSent || Time.unscaledTime - conversationPendingStartedAt < ConversationPendingDelaySeconds)
                return;
            int choice = conversationPendingRandom.Next(ConversationPendingText.FixedVariantCount);
            if (choice == conversationLastPendingVariant) choice = (choice + 1) % ConversationPendingText.FixedVariantCount;
            string text = ConversationPendingText.SelectPendingText(speakerName, choice, -1, CurrentPendingSpeakerSex(manager));
            conversationLastPendingVariant = choice;
            // Presentation only: bypass memory, model dispatch, observed speech, TTS and replay registration.
            AppendPresentationLine(transcript, text, false);
            conversationPendingRow = transcript.GetItem(transcript.Count - 1);
            conversationPendingRow.textLabel.Name = PendingTranscriptRowName;
            conversationPendingRow.textColor = ConversationPendingTextColor;
            conversationPendingRow.highlightedTextColor = ConversationPendingTextColor;
            conversationPendingRow.selectedTextColor = ConversationPendingTextColor;
            conversationPendingRow.highlightedSelectedTextColor = ConversationPendingTextColor;
            conversationPendingTranscript = transcript;
            conversationPendingLabel = conversationPendingRow.textLabel;
            conversationPendingOverlayWindow = talk;
            conversationPendingRequestSent = true;
            conversationPendingRenderedRevision = revision;
            RefreshTranscriptScroll(talk);
            if (Array.IndexOf(Environment.GetCommandLineArgs(), ConversationPendingProbeSwitch) >= 0 ||
                Array.IndexOf(Environment.GetCommandLineArgs(), ActionWordingProbeSwitch) >= 0)
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PENDING_TEXT: requestRevision=" + revision + "; source=fixed-local; text=" + text);
        }

        private static UnityEngine.Object CurrentPendingSpeakerObject(TalkManager manager)
        {
            if (manager == null) return null;
            if (manager.CurrentNPCType == TalkManager.NPCType.Mobile) return manager.MobileNPC;
            if (manager.CurrentNPCType == TalkManager.NPCType.Static) return manager.StaticNPC;
            return null;
        }

        internal ResidentSex? CurrentPendingSpeakerSex(TalkManager manager)
        {
            if (conversationActor && conversationActor.Resident != null &&
                string.Equals(conversationActor.Resident.Id, conversationPersonId, StringComparison.Ordinal))
                return conversationActor.Resident.Sex;
            if (manager == null) return null;
            Genders? gender = manager.CurrentNPCType == TalkManager.NPCType.Mobile && manager.MobileNPC
                ? (Genders?)manager.MobileNPC.Gender
                : manager.CurrentNPCType == TalkManager.NPCType.Static && manager.StaticNPC
                    ? (Genders?)manager.StaticNPC.Data.gender : null;
            if (gender == Genders.Male) return ResidentSex.Male;
            if (gender == Genders.Female) return ResidentSex.Female;
            return null;
        }

        private void RemoveConversationPendingOverlay()
        {
            InvalidatePendingAcceptedSpeechReveal();
            DetachConversationPendingOverlay();
            conversationPendingTracking = false;
            conversationPendingRequestSent = false;
            conversationPendingStartedAt = 0;
            conversationPendingSpeakerId = conversationPendingSpeakerName = conversationPendingSpeakerType = null;
            conversationPendingSpeakerObject = null;
            conversationPendingPlayerText = null;
            conversationPendingSnapshot = null;
            conversationPendingBinding = null;
            conversationPendingWindow = null;
            conversationPendingSession = 0;
            conversationPendingSaveEpoch = 0;
            conversationPendingRequestRevision = long.MinValue;
            conversationPendingReturningGreeting = false;
        }

        private void CancelConversationPendingNarration()
        {
            RemoveConversationPendingOverlay();
            conversationPendingSuppressed = false;
        }

        private void RemoveConversationPendingOverlayFor(DaggerfallTalkWindow window)
        {
            if (window != null && conversationPendingOverlayWindow == window)
                RemoveConversationPendingOverlay();
        }

        private void DetachConversationPendingOverlay()
        {
            var owner = conversationPendingOverlayWindow;
            if (conversationPendingTranscript != null)
            {
                RemovePendingRowByIdentity(conversationPendingTranscript, conversationPendingRow, owner);
                RemovePendingRowByIdentity(conversationPendingTranscript, conversationPendingPlayerRow, owner);
                if (owner != null && owner.IsSetup) RefreshTranscriptScroll(owner);
            }
            conversationPendingTranscript = null;
            conversationPendingPlayerRow = null;
            conversationPendingRow = null;
            conversationPendingLabel = null;
            conversationPendingOverlayWindow = null;
            conversationPendingRenderedRevision = long.MinValue;
        }

        private void RemovePendingRowByIdentity(ListBox transcript, ListBox.ListItem row, DaggerfallTalkWindow owner)
        {
            if (transcript == null || row == null) return;
            for (int index = transcript.Count - 1; index >= 0; index--)
            {
                if (!ReferenceEquals(transcript.GetItem(index), row)) continue;
                transcript.RemoveItem(index);
                if (memoryWindow == owner)
                {
                    if (memoryCursor > index) memoryCursor--;
                    if (conversationLastTranscriptCount > index) conversationLastTranscriptCount--;
                }
                return;
            }
        }
    }
}
