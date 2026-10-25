using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private sealed class TrialTranscriptBinding
        {
            public ListBox Transcript;
            public DaggerfallTalkWindow TalkWindow;
            public string ConversationToken;
            public readonly Dictionary<ListBox.ListItem, string> RequestByRow =
                new Dictionary<ListBox.ListItem, string>();
            public readonly Dictionary<ListBox.ListItem, StreamingWordHighlightOverlay> HighlightByRow =
                new Dictionary<ListBox.ListItem, StreamingWordHighlightOverlay>();
        }

        private readonly Dictionary<ListBox, TrialTranscriptBinding> trialTranscriptBindings =
            new Dictionary<ListBox, TrialTranscriptBinding>();
        private sealed class CanonicalNpcRowText
        {
            public readonly string Text;
            public CanonicalNpcRowText(string text) { Text = text; }
        }

        // Transcript readers are static native integration seams. Weak row keys
        // preserve that contract without retaining closed conversation widgets.
        private static readonly ConditionalWeakTable<ListBox.ListItem, CanonicalNpcRowText> canonicalNpcTextByRow =
            new ConditionalWeakTable<ListBox.ListItem, CanonicalNpcRowText>();

        private static void RegisterCanonicalTranscriptText(ListBox.ListItem row, string text)
        {
            canonicalNpcTextByRow.Remove(row);
            canonicalNpcTextByRow.Add(row, new CanonicalNpcRowText(text));
        }

        private static string CanonicalTranscriptText(ListBox.ListItem row)
        {
            if (row == null || row.textLabel == null) return string.Empty;
            CanonicalNpcRowText canonical;
            return canonicalNpcTextByRow.TryGetValue(row, out canonical) ? canonical.Text : row.textLabel.Text;
        }

        /// <summary>
        /// Bind an accepted trial request to every NPC answer row created by one native append.
        /// The caller captures firstNewRow before SetQuestionAnswerPairInConversationListbox.
        /// </summary>
        private void AttachTrialReplayToAppendedAnswerRows(DaggerfallTalkWindow talk, int firstNewRow,
            string requestId, string conversationToken, bool initialReveal = false)
        {
            if (talk == null) return;
            ListBox transcript = conversationListField == null ? null : conversationListField.GetValue(talk) as ListBox;
            if (transcript == null || firstNewRow < 0 || firstNewRow >= transcript.Count) return;
            var rows = new List<ListBox.ListItem>();
            for (int index = firstNewRow; index < transcript.Count; index++)
            {
                ListBox.ListItem row = transcript.GetItem(index);
                if (row != null && row.textLabel != null && !IsPendingTranscriptItem(row) && row.textColor != DaggerfallUI.DaggerfallQuestionTextColor)
                    rows.Add(row);
            }
            AttachTrialReplayToRows(talk, rows, requestId, conversationToken, initialReveal);
        }

        private void AttachTrialReplayToRows(DaggerfallTalkWindow talk, IList<ListBox.ListItem> rows,
            string requestId, string conversationToken, bool initialReveal = false)
        {
            if (!breezeTtsTrial || talk == null || rows == null || rows.Count == 0 || string.IsNullOrEmpty(requestId) ||
                string.IsNullOrEmpty(conversationToken) || conversationToken != conversationContextSessionId) return;
            string boundPersonId, boundToken, boundText;
            if (!breezeTtsTrial.TryReadRequestBinding(requestId, out boundPersonId, out boundToken, out boundText) ||
                boundToken != conversationToken || boundPersonId != memoryPersonId || string.IsNullOrWhiteSpace(boundText))
            {
                StreamingWordHighlightOverlay.ReportDiagnostic("binding-miss", requestId,
                    "request-person-token-or-text-binding-invalid");
                return;
            }

            ListBox transcript = conversationListField == null ? null : conversationListField.GetValue(talk) as ListBox;
            if (transcript == null)
            {
                StreamingWordHighlightOverlay.ReportDiagnostic("binding-miss", requestId, "transcript-not-found");
                return;
            }
            bool hasVisibleRow = false;
            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                ListBox.ListItem expected = rows[rowIndex];
                if (expected == null || expected.textLabel == null || expected.textColor == DaggerfallUI.DaggerfallQuestionTextColor ||
                    !string.Equals(expected.textLabel.Text, boundText, StringComparison.Ordinal))
                    continue;
                for (int index = 0; index < transcript.Count; index++)
                    if (ReferenceEquals(transcript.GetItem(index), expected)) { hasVisibleRow = true; break; }
                if (hasVisibleRow) break;
            }
            if (!hasVisibleRow)
            {
                StreamingWordHighlightOverlay.ReportDiagnostic("binding-miss", requestId,
                    "no-appended-npc-row-matches-finalized-text");
                return;
            }

            TrialTranscriptBinding binding;
            if (!trialTranscriptBindings.TryGetValue(transcript, out binding))
            {
                binding = new TrialTranscriptBinding { Transcript = transcript, TalkWindow = talk,
                    ConversationToken = conversationToken };
                trialTranscriptBindings.Add(transcript, binding);
                transcript.OnMouseClick += OnTrialTranscriptMouseClick;
            }
            else if (binding.ConversationToken != conversationToken || binding.TalkWindow != talk)
            {
                DetachTrialWordHighlights(binding);
                binding.RequestByRow.Clear();
                binding.ConversationToken = conversationToken;
                binding.TalkWindow = talk;
            }

            for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                ListBox.ListItem row = rows[rowIndex];
                if (row == null || row.textLabel == null || row.textColor == DaggerfallUI.DaggerfallQuestionTextColor ||
                    !string.Equals(row.textLabel.Text, boundText, StringComparison.Ordinal))
                    continue;
                for (int index = 0; index < transcript.Count; index++)
                {
                    if (!ReferenceEquals(transcript.GetItem(index), row)) continue;
                    binding.RequestByRow[row] = requestId;
                    StreamingWordHighlightOverlay oldOverlay;
                    if (binding.HighlightByRow.TryGetValue(row, out oldOverlay)) oldOverlay.Detach();
                    binding.HighlightByRow.Remove(row);
                    StreamingWordHighlightOverlay overlay = StreamingWordHighlightOverlay.AttachFullWav(
                        transcript, row, breezeTtsTrial, requestId, conversationToken, boundPersonId, boundText,
                        initialReveal);
                    if (overlay != null)
                    {
                        binding.HighlightByRow[row] = overlay;
                        RegisterCanonicalTranscriptText(row, boundText);
                    }
                    break;
                }
            }
        }

        private void EndTrialReplayForConversation(string conversationToken)
        {
            ForgetNpcSpeechPresentation(conversationToken);
            if (string.IsNullOrEmpty(conversationToken)) return;
            StreamingWordHighlightOverlay.DetachForConversation(conversationToken);
            var remove = new List<ListBox>();
            foreach (var pair in trialTranscriptBindings)
            {
                TrialTranscriptBinding binding = pair.Value;
                if (binding == null || binding.ConversationToken != conversationToken) continue;
                if (binding.Transcript != null) binding.Transcript.OnMouseClick -= OnTrialTranscriptMouseClick;
                foreach (ListBox.ListItem row in binding.RequestByRow.Keys) canonicalNpcTextByRow.Remove(row);
                DetachTrialWordHighlights(binding);
                remove.Add(pair.Key);
            }
            foreach (ListBox transcript in remove) trialTranscriptBindings.Remove(transcript);
        }

        private static void DetachTrialWordHighlights(TrialTranscriptBinding binding)
        {
            if (binding == null) return;
            foreach (StreamingWordHighlightOverlay overlay in binding.HighlightByRow.Values)
                if (overlay != null) overlay.Detach();
            binding.HighlightByRow.Clear();
        }

        private void OnTrialTranscriptMouseClick(BaseScreenComponent sender, Vector2 clickPosition)
        {
            ListBox transcript = sender as ListBox;
            TrialTranscriptBinding binding;
            if (transcript == null || !trialTranscriptBindings.TryGetValue(transcript, out binding) ||
                binding == null || binding.ConversationToken != conversationContextSessionId ||
                binding.TalkWindow != memoryWindow || !DaggerfallUI.UIManager.ContainsWindow(binding.TalkWindow)) return;

            ListBox.ListItem clickedRow = FindClickedTranscriptRow(transcript, clickPosition);
            string requestId;
            if (clickedRow == null || !binding.RequestByRow.TryGetValue(clickedRow, out requestId)) return;
            if (!breezeTtsTrial) return;

            StreamingWordHighlightOverlay.PrepareReplayForRow(clickedRow);
            BreezeTtsTrialBridge.RequestState state = breezeTtsTrial.ReplayRequest(requestId, binding.ConversationToken);
            if (state == BreezeTtsTrialBridge.RequestState.Pending)
                DaggerfallUI.AddHUDText("That line's voice is still being prepared. Replay will not start a new request.");
            else if (state == BreezeTtsTrialBridge.RequestState.Loading)
                DaggerfallUI.AddHUDText("Loading the recorded voice for replay.");
            else if (state == BreezeTtsTrialBridge.RequestState.Unknown || state == BreezeTtsTrialBridge.RequestState.Cancelled ||
                state == BreezeTtsTrialBridge.RequestState.Failed)
                DaggerfallUI.AddHUDText("That line's recorded voice is unavailable.");
        }

        private static ListBox.ListItem FindClickedTranscriptRow(ListBox transcript, Vector2 clickPosition)
        {
            if (transcript == null || transcript.Count == 0) return null;
            int clickedY = transcript.ScrollIndex + (int)clickPosition.y;
            int yCurrent = 0;
            for (int index = 0; index < transcript.Count; index++)
            {
                ListBox.ListItem item = transcript.GetItem(index);
                int yNext = yCurrent + item.textLabel.TextHeight + transcript.RowSpacing;
                if (clickedY >= yCurrent - transcript.RowSpacing * 0.5f &&
                    clickedY < yNext - transcript.RowSpacing * 0.5f) return item;
                yCurrent = yNext;
            }
            return null;
        }
    }
}
