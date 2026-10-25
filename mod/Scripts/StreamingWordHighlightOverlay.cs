using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Globalization;
using PeopleOfTheIliacBay.Core;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace PeopleOfTheIliacBay.DFU
{
    [Serializable]
    internal sealed class WordHighlightPlaybackReceipt
    {
        public int schemaVersion = 0;
        public string requestId = null;
        public string conversationToken = null;
        public string personId = null;
        public string status = null;
        public long mixerCallbackFrames = 0;
        public long nonZeroOutputSamples = 0;
        public int expectedSampleFrames = 0;
        public int sampleRate = 0;
        public int channels = 0;
        public int lastKnownSample = 0;
        public long playbackStartMonotonicTicks = 0;
        public long playbackEndMonotonicTicks = 0;
    }

    [Serializable]
    internal sealed class WordHighlightResponseManifest
    {
        public int schemaVersion = 0;
        public string requestId = null;
        public string conversationToken = null;
        public string personId = null;
        public string encoding = null;
        public string wavFile = null;
        public int sampleRate = 0;
        public int channels = 0;
        public int sampleFrames = 0;
    }

    /// <summary>
    /// Draws the current word blue. Initial playback reveals only spoken text; replay keeps full text visible.
    /// </summary>
    internal sealed class StreamingWordHighlightOverlay : BaseScreenComponent
    {
        private const string DiagnosticRunFlag = "-peopleoftheiliacbay-breeze-tts-probe";
        private static readonly FieldInfo glyphLayoutField = typeof(TextLabel).GetField(
            "glyphLayout", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo glyphXField;
        private static readonly FieldInfo glyphYField;
        private static readonly FieldInfo glyphWidthField;
        private static readonly FieldInfo glyphCodeField;
        private static readonly Color highlightColor = new Color(.30f, .78f, 1f, 1f);
        private static readonly bool diagnosticsEnabled = Array.IndexOf(Environment.GetCommandLineArgs(),
            DiagnosticRunFlag) >= 0;
        private static readonly object diagnosticGate = new object();
        private static readonly HashSet<string> emittedDiagnosticKeys = new HashSet<string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, HashSet<StreamingWordHighlightOverlay>> overlaysByConversation =
            new Dictionary<string, HashSet<StreamingWordHighlightOverlay>>(StringComparer.Ordinal);
        private static bool firstBlueScreenshotQueued;

        static StreamingWordHighlightOverlay()
        {
            Type glyphType = typeof(TextLabel).GetNestedType("GlyphLayoutData", BindingFlags.NonPublic);
            if (glyphType == null) return;
            glyphXField = glyphType.GetField("x", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            glyphYField = glyphType.GetField("y", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            glyphWidthField = glyphType.GetField("width", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            glyphCodeField = glyphType.GetField("code", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        private ListBox transcript;
        private ListBox.ListItem row;
        private BreezeTtsStreamingPcmAdapter adapter;
        private string responsesRoot;
        private string streamId;
        private string conversationToken;
        private string personId;
        private string rowText;
        private string lastRenderedText;
        private bool initialRevealMode;
        private readonly List<string> sentences = new List<string>();
        private readonly List<int> sentenceTextOffsets = new List<int>();
        private readonly Dictionary<int, WordAlignmentTimelineDocument> timelines = new Dictionary<int, WordAlignmentTimelineDocument>();
        private readonly Dictionary<int, float> nextTimelinePollBySequence = new Dictionary<int, float>();
        private readonly object fullTimelineGate = new object();
        private BreezeTtsTrialBridge fullWavBridge;
        private bool fullWavMode;
        private string fullWavRequestId;
        private PortraitWordAlignmentTimeline fullWavTimeline;
        private string fullWavPresentationRequestId;
        private bool fullWavPresentationRequired;
        private string fullWavTimelineRequestId;
        private TextWeightedWordProgress fullWavEstimate;
        private string fullWavEstimateRequestId;
        private long fullWavEstimateFrames = -1;
        private bool fullWavTimelineLoading;
        private string fullWavTimelineLoadingRequestId;
        private long nextFullWavTimelineRetryTicks;
        private bool completedFullWavReceiptChecked;
        private bool completedFullWavReceiptValid;
        private int completedFullWavReceiptPlaybackIndex = -1;
        private BreezeTtsTrialBridge.ActiveAudioEvidence completedFullWavEvidence;
        private BreezeTtsTrialBridge.ActiveAudioEvidence observedFullWavEvidence;
        private bool fullWavHasObservedPlayback;
        private long fullWavLastObservedSampleFrame;
        private int fullWavObservedPlaybackIndex = -1;
        private int cumulativeTextHighWater;
        private int currentCumulativeTextEnd;
        private int currentWordStartUtf16 = -1;
        private bool terminalPresentationResolved;
        private string currentProgressSource = "none";
        private bool usedTextWeightedEstimateThisPlayback;
        private int lastDrawnBlueGlyphCount;
        private int lastProgressResetPlaybackIndex = -1;
        private int lastProgressResetTextEnd = -1;
        internal int LastDrawnBlueGlyphCount { get { return lastDrawnBlueGlyphCount; } }
        internal int LastProgressResetPlaybackIndex { get { return lastProgressResetPlaybackIndex; } }
        internal int LastProgressResetTextEnd { get { return lastProgressResetTextEnd; } }
        private readonly List<TextWordGlyphSpan> displayWordSpans = new List<TextWordGlyphSpan>();
        private readonly HashSet<string> reportedReasons = new HashSet<string>(StringComparer.Ordinal);

        private StreamingWordHighlightOverlay() { }

        public static StreamingWordHighlightOverlay Attach(ListBox transcript, ListBox.ListItem row,
            BreezeTtsStreamingPcmAdapter adapter, string responsesRoot, string streamId,
            string conversationToken, string personId, IList<string> acceptedSentences,
            BreezeTtsTrialBridge bridge = null, bool initialReveal = false)
        {
            if (transcript == null || row == null || row.textLabel == null || adapter == null ||
                string.IsNullOrEmpty(responsesRoot) || string.IsNullOrEmpty(streamId) ||
                acceptedSentences == null || acceptedSentences.Count == 0 || transcript.Parent == null)
                return null;

            var overlay = new StreamingWordHighlightOverlay { transcript = transcript, row = row,
                adapter = adapter, responsesRoot = responsesRoot, streamId = streamId,
                fullWavBridge = bridge, conversationToken = conversationToken, personId = personId,
                rowText = row.textLabel.Text, lastRenderedText = row.textLabel.Text,
                initialRevealMode = initialReveal };
            overlay.BuildDisplayWordSpans();
            int textOffset = 0;
            var expected = new StringBuilder();
            for (int index = 0; index < acceptedSentences.Count; index++)
            {
                string sentence = acceptedSentences[index];
                if (string.IsNullOrWhiteSpace(sentence)) return null;
                if (index > 0) { expected.Append(' '); textOffset++; }
                overlay.sentenceTextOffsets.Add(textOffset);
                overlay.sentences.Add(sentence);
                expected.Append(sentence);
                textOffset += sentence.Length;
            }
            if (!string.Equals(expected.ToString(), overlay.rowText, StringComparison.Ordinal)) return null;
            var panel = transcript.Parent as Panel;
            if (panel == null) return null;
            row.textColor = DaggerfallUI.DaggerfallDefaultTextColor;
            row.selectedTextColor = DaggerfallUI.DaggerfallDefaultTextColor;
            panel.Components.Add(overlay);
            RegisterConversationOverlay(overlay);
            return overlay;
        }

        public static StreamingWordHighlightOverlay AttachFullWav(ListBox transcript, ListBox.ListItem row,
            BreezeTtsTrialBridge bridge, string requestId, string conversationToken, string personId, string exactText,
            bool initialReveal = false)
        {
            string responsesRoot;
            if (transcript == null || row == null || row.textLabel == null || bridge == null ||
                string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(conversationToken) ||
                string.IsNullOrWhiteSpace(personId) || string.IsNullOrEmpty(exactText) ||
                row.textLabel.Text != exactText || !bridge.TryGetStreamingResponseRoot(out responsesRoot) ||
                transcript.Parent == null)
            {
                ReportStatic("binding-miss", requestId, "attach-input-or-row-binding-invalid");
                return null;
            }
            var panel = transcript.Parent as Panel;
            if (panel == null)
            {
                ReportStatic("binding-miss", requestId, "transcript-parent-not-panel");
                return null;
            }
            var overlay = new StreamingWordHighlightOverlay { transcript = transcript, row = row,
                fullWavBridge = bridge, fullWavMode = true, fullWavRequestId = requestId,
                conversationToken = conversationToken, personId = personId,
                responsesRoot = responsesRoot, streamId = requestId, rowText = exactText,
                lastRenderedText = initialReveal ? string.Empty : exactText, initialRevealMode = initialReveal };
            overlay.BuildDisplayWordSpans();
            row.textColor = DaggerfallUI.DaggerfallDefaultTextColor;
            row.selectedTextColor = DaggerfallUI.DaggerfallDefaultTextColor;
            if (initialReveal) row.textLabel.Text = string.Empty;
            panel.Components.Add(overlay);
            RegisterConversationOverlay(overlay);
            return overlay;
        }

        public void PrepareReplay()
        {
            if (row == null || row.textLabel == null) return;
            initialRevealMode = false;
            row.textLabel.Text = rowText;
            lastRenderedText = rowText;
            currentWordStartUtf16 = -1;
            currentCumulativeTextEnd = 0;
            cumulativeTextHighWater = 0;
            terminalPresentationResolved = false;
            fullWavObservedPlaybackIndex = -1;
        }

        public void Detach()
        {
            if (initialRevealMode && row != null && row.textLabel != null)
                row.textLabel.Text = rowText;
            Enabled = false;
            UnregisterConversationOverlay(this);
            var panel = transcript == null ? null : transcript.Parent as Panel;
            if (panel != null) panel.Components.Remove(this);
            transcript = null;
            row = null;
            adapter = null;
            fullWavBridge = null;
            timelines.Clear();
            nextTimelinePollBySequence.Clear();
            fullWavEstimate = null;
            displayWordSpans.Clear();
            fullWavPresentationRequired = false;
            fullWavPresentationRequestId = null;
            lock (fullTimelineGate) { fullWavTimeline = null; fullWavTimelineRequestId = null; }
        }

        internal static void DetachForConversation(string conversationToken)
        {
            if (string.IsNullOrWhiteSpace(conversationToken)) return;
            HashSet<StreamingWordHighlightOverlay> owned;
            if (!overlaysByConversation.TryGetValue(conversationToken, out owned)) return;
            overlaysByConversation.Remove(conversationToken);
            var snapshot = new List<StreamingWordHighlightOverlay>(owned);
            for (int index = 0; index < snapshot.Count; index++)
                if (snapshot[index] != null) snapshot[index].Detach();
        }

        internal static void PrepareReplayForRow(ListBox.ListItem targetRow)
        {
            if (targetRow == null) return;
            foreach (HashSet<StreamingWordHighlightOverlay> group in overlaysByConversation.Values)
                foreach (StreamingWordHighlightOverlay overlay in group)
                    if (overlay != null && ReferenceEquals(overlay.row, targetRow))
                    { overlay.PrepareReplay(); return; }
        }

        private static void RegisterConversationOverlay(StreamingWordHighlightOverlay overlay)
        {
            if (overlay == null || string.IsNullOrWhiteSpace(overlay.conversationToken)) return;
            HashSet<StreamingWordHighlightOverlay> owned;
            if (!overlaysByConversation.TryGetValue(overlay.conversationToken, out owned))
            {
                owned = new HashSet<StreamingWordHighlightOverlay>();
                overlaysByConversation.Add(overlay.conversationToken, owned);
            }
            owned.Add(overlay);
        }

        private static void UnregisterConversationOverlay(StreamingWordHighlightOverlay overlay)
        {
            if (overlay == null || string.IsNullOrWhiteSpace(overlay.conversationToken)) return;
            HashSet<StreamingWordHighlightOverlay> owned;
            if (!overlaysByConversation.TryGetValue(overlay.conversationToken, out owned)) return;
            owned.Remove(overlay);
            if (owned.Count == 0) overlaysByConversation.Remove(overlay.conversationToken);
        }

        public override void Draw()
        {
            lastDrawnBlueGlyphCount = 0;
            if (transcript == null || row == null || row.textLabel == null || (!fullWavMode && adapter == null)) return;
            string expectedRowText = initialRevealMode ? lastRenderedText : rowText;
            if (!string.Equals(row.textLabel.Text, expectedRowText, StringComparison.Ordinal))
            { ReportOnce("binding-miss", "transcript-row-text-changed"); return; }
            if (terminalPresentationResolved) return;
            if (glyphLayoutField == null || glyphXField == null || glyphYField == null ||
                glyphWidthField == null || glyphCodeField == null)
            { ReportOnce("glyph-api", "TextLabel-glyph-layout-reflection-unavailable"); return; }

            int candidateTextEnd = 0;
            int candidateCurrentWordStart = -1;
            string progressSource = "none";
            int currentPlaybackIndex = fullWavBridge == null ? -1 :
                fullWavBridge.GetPlaybackIndex(fullWavMode ? fullWavRequestId : streamId);
            if (currentPlaybackIndex >= 0 && currentPlaybackIndex != fullWavObservedPlaybackIndex)
            {
                fullWavObservedPlaybackIndex = currentPlaybackIndex;
                fullWavLastObservedSampleFrame = 0;
                fullWavHasObservedPlayback = false;
                cumulativeTextHighWater = 0;
                currentCumulativeTextEnd = 0;
                currentWordStartUtf16 = -1;
                lastProgressResetPlaybackIndex = currentPlaybackIndex;
                lastProgressResetTextEnd = 0;
                usedTextWeightedEstimateThisPlayback = false;
                currentProgressSource = "none";
                completedFullWavReceiptChecked = false;
                completedFullWavReceiptValid = false;
                completedFullWavEvidence = null;
                observedFullWavEvidence = null;
            }

            if (fullWavMode)
            {
                BreezeTtsTrialBridge.ActiveAudioEvidence evidence;
                if (fullWavBridge != null && fullWavBridge.TryReadActiveAudioEvidence(out evidence) &&
                    evidence.RequestId == fullWavRequestId && evidence.PersonId == personId &&
                    evidence.ConversationToken == conversationToken && evidence.Text == rowText &&
                    fullWavBridge.OutputAudioSource != null && fullWavBridge.OutputAudioSource.clip != null)
                {
                    completedFullWavReceiptChecked = false;
                    completedFullWavReceiptValid = false;
                    completedFullWavEvidence = null;
                    if (fullWavBridge.OutputAudioSource.clip.frequency != evidence.SampleRate)
                    { ReportOnce("rate", "active-clip-rate-does-not-match-bound-wav"); return; }
                    long sampleFrame = fullWavBridge.OutputAudioSource.timeSamples;
                    fullWavLastObservedSampleFrame = Math.Max(fullWavLastObservedSampleFrame, sampleFrame);
                    fullWavHasObservedPlayback = true;
                    observedFullWavEvidence = evidence;
                    candidateTextEnd = GetFullWavProgressEnd(evidence, fullWavLastObservedSampleFrame, false,
                        out progressSource);
                    candidateCurrentWordStart = GetFullWavCurrentWordStart(evidence, fullWavLastObservedSampleFrame);
                }
                else if (TryGetCompletedFullWavEvidence(out evidence))
                {
                    fullWavLastObservedSampleFrame = evidence.SampleFrames;
                    fullWavHasObservedPlayback = true;
                    observedFullWavEvidence = evidence;
                    candidateTextEnd = GetFullWavProgressEnd(evidence, evidence.SampleFrames, true,
                        out progressSource);
                }
                else
                {
                    BreezeTtsTrialBridge.RequestState state = fullWavBridge == null
                        ? BreezeTtsTrialBridge.RequestState.Unknown : fullWavBridge.GetRequestState(fullWavRequestId);
                    if (state == BreezeTtsTrialBridge.RequestState.Failed ||
                        state == BreezeTtsTrialBridge.RequestState.Cancelled ||
                        state == BreezeTtsTrialBridge.RequestState.Completed)
                    {
                        ResolveTerminalPresentation();
                        return;
                    }
                    if (state == BreezeTtsTrialBridge.RequestState.Loading)
                    {
                        fullWavHasObservedPlayback = false;
                        fullWavLastObservedSampleFrame = 0;
                        cumulativeTextHighWater = 0;
                        currentCumulativeTextEnd = 0;
                        return;
                    }
                    if (state == BreezeTtsTrialBridge.RequestState.Playing ||
                        state == BreezeTtsTrialBridge.RequestState.Completed)
                        ReportOnce("binding-miss", "active-audio-evidence-does-not-match-row-request");
                    string boundPerson, boundToken, boundText;
                    if (fullWavHasObservedPlayback && observedFullWavEvidence != null &&
                        fullWavBridge.TryReadRequestBinding(fullWavRequestId, out boundPerson,
                        out boundToken, out boundText) && boundPerson == personId &&
                        boundToken == conversationToken && boundText == rowText)
                        candidateTextEnd = GetFullWavProgressEnd(observedFullWavEvidence,
                            fullWavLastObservedSampleFrame, false, out progressSource);
                    if (fullWavHasObservedPlayback && observedFullWavEvidence != null)
                        candidateCurrentWordStart = GetFullWavCurrentWordStart(observedFullWavEvidence,
                            fullWavLastObservedSampleFrame);
                }
            }
            else
            {
                // Retained-WAV replay is independent of its original PCM
                // adapter, which remains terminal after the first playback.
                if (initialRevealMode && (adapter.State == BreezeTtsStreamingPcmAdapter.PlaybackState.Failed ||
                    adapter.State == BreezeTtsStreamingPcmAdapter.PlaybackState.Cancelled ||
                    adapter.State == BreezeTtsStreamingPcmAdapter.PlaybackState.Completed))
                {
                    ResolveTerminalPresentation();
                    return;
                }
                BreezeTtsTrialBridge.RequestState replayState = fullWavBridge == null
                    ? BreezeTtsTrialBridge.RequestState.Unknown : fullWavBridge.GetRequestState(streamId);
                if (replayState == BreezeTtsTrialBridge.RequestState.Failed ||
                    replayState == BreezeTtsTrialBridge.RequestState.Cancelled)
                {
                    ResolveTerminalPresentation();
                    return;
                }
                BreezeTtsTrialBridge.ActiveAudioEvidence replayEvidence;
                if (replayState == BreezeTtsTrialBridge.RequestState.Playing &&
                    fullWavBridge.TryReadActiveAudioEvidence(out replayEvidence) &&
                    IsMatchingEvidence(replayEvidence))
                {
                    completedFullWavReceiptChecked = false;
                    completedFullWavReceiptValid = false;
                    completedFullWavEvidence = null;
                    long sampleFrame = fullWavBridge.OutputAudioSource.timeSamples;
                    fullWavLastObservedSampleFrame = Math.Max(fullWavLastObservedSampleFrame, sampleFrame);
                    fullWavHasObservedPlayback = true;
                    observedFullWavEvidence = replayEvidence;
                    candidateTextEnd = GetFullWavProgressEnd(replayEvidence, fullWavLastObservedSampleFrame,
                        false, out progressSource);
                    candidateCurrentWordStart = GetFullWavCurrentWordStart(replayEvidence,
                        fullWavLastObservedSampleFrame);
                }
                else if (replayState == BreezeTtsTrialBridge.RequestState.Completed &&
                    TryGetCompletedFullWavEvidence(streamId, out replayEvidence))
                {
                    fullWavLastObservedSampleFrame = replayEvidence.SampleFrames;
                    fullWavHasObservedPlayback = true;
                    observedFullWavEvidence = replayEvidence;
                    candidateTextEnd = GetFullWavProgressEnd(replayEvidence, replayEvidence.SampleFrames,
                        true, out progressSource);
                }
                else
                {
                    int completedCount = CompletedSentenceCount();
                    for (int index = 0; index < completedCount; index++)
                    {
                        WordAlignmentTimelineDocument completedTimeline = GetSentenceTimeline(index);
                        if (completedTimeline != null)
                            candidateTextEnd = Math.Max(candidateTextEnd,
                                GetAlignedTextEnd(completedTimeline.words, sentenceTextOffsets[index],
                                    completedTimeline.audioFrames, true));
                        else
                            candidateTextEnd = Math.Max(candidateTextEnd,
                                sentenceTextOffsets[index] + sentences[index].Length);
                    }
                    if (completedCount > 0) progressSource = "completed-pcm-sentence-prefix";
                    for (int index = 0; index < sentences.Count; index++)
                    {
                        int sampleRate;
                        long localFrame;
                        if (!adapter.TryGetSentencePlaybackPosition(index, out sampleRate, out localFrame)) continue;
                        if (sampleRate != adapter.ActiveSampleRate)
                        { ReportOnce("rate", "sentence-cursor-rate-mismatch"); break; }
                        if (index >= completedCount)
                        {
                            WordAlignmentTimelineDocument activeTimeline = GetSentenceTimeline(index);
                            if (activeTimeline != null)
                            {
                                candidateCurrentWordStart = GetAlignedCurrentWordStart(activeTimeline.words,
                                    sentenceTextOffsets[index], localFrame);
                                int activeEnd = GetAlignedTextEnd(activeTimeline.words,
                                    sentenceTextOffsets[index], localFrame, false);
                                if (activeEnd > candidateTextEnd) progressSource = "forced-word-alignment";
                                candidateTextEnd = Math.Max(candidateTextEnd, activeEnd);
                            }
                        }
                        break;
                    }
                }
            }
            if (candidateTextEnd > cumulativeTextHighWater)
                cumulativeTextHighWater = Math.Min(rowText.Length, candidateTextEnd);
            currentCumulativeTextEnd = cumulativeTextHighWater;
            currentWordStartUtf16 = candidateCurrentWordStart;
            if (initialRevealMode) RenderInitialText(currentCumulativeTextEnd);
            if (currentCumulativeTextEnd <= 0) return;
            currentProgressSource = string.IsNullOrEmpty(progressSource)
                ? (fullWavMode ? "retained-audio-frame-estimate" : "retained-completed-audio-prefix")
                : progressSource;
            if (currentProgressSource == "text-weighted-audio-estimate")
                usedTextWeightedEstimateThisPlayback = true;
            if (usedTextWeightedEstimateThisPlayback)
                ReportOnce("progress-source", "source=text-weighted-audio-estimate; no phoneme or measured word timestamps");

            TextLabel label = row.textLabel;
            IList layout = glyphLayoutField.GetValue(label) as IList;
            if (layout == null)
            { ReportOnce("glyph-api", "TextLabel-glyph-layout-list-unavailable"); return; }
            var font = label.Font;
            if (font == null) return;
            Rect labelRect = label.Rectangle;
            Rect clip = transcript.Rectangle;
            var material = font.GetMaterial();
            Vector4 previousClip = material.GetVector("_ScissorRect");
            material.SetVector("_ScissorRect", new Vector4(clip.xMin / Screen.width, clip.xMax / Screen.width,
                1f - clip.yMax / Screen.height, 1f - clip.yMin / Screen.height));
            int visibleGlyphCount = 0;
            try
            {
                Vector2 scale = label.LocalScale;
                float textScale = label.TextScale;
                for (int wordIndex = 0; wordIndex < displayWordSpans.Count; wordIndex++)
                {
                    TextWordGlyphSpan word = displayWordSpans[wordIndex];
                    if (currentWordStartUtf16 < 0 || word.Start != currentWordStartUtf16 ||
                        word.Start + word.Length > currentCumulativeTextEnd) continue;
                    if (!TryMapGlyphIndices(label.Text, layout, word.Start, word.Length, out List<int> indices))
                    { ReportOnce("glyph-api", "glyph-list-or-text-offset-could-not-map-alignment-span"); continue; }
                    if (!font.IsSDFCapable)
                    {
                        bool unsupportedClassicGlyph = false;
                        for (int listIndex = 0; listIndex < indices.Count; listIndex++)
                        {
                            int code = (int)glyphCodeField.GetValue(layout[indices[listIndex]]);
                            if (code < 0 || code > byte.MaxValue) { unsupportedClassicGlyph = true; break; }
                        }
                        if (unsupportedClassicGlyph)
                        { ReportOnce("glyph-api", "classic-font-cannot-render-exact-nonbyte-transcript-glyph"); continue; }
                    }
                    for (int listIndex = 0; listIndex < indices.Count; listIndex++)
                    {
                        object glyph = layout[indices[listIndex]];
                        float x = (float)glyphXField.GetValue(glyph);
                        float y = (float)glyphYField.GetValue(glyph);
                        float width = (float)glyphWidthField.GetValue(glyph);
                        int code = (int)glyphCodeField.GetValue(glyph);
                        var glyphRect = new Rect((int)(labelRect.x + x * scale.x * textScale +
                            label.HorzPixelScrollOffset * scale.x * textScale),
                            (int)(labelRect.y + y * scale.y * textScale),
                            (int)(width * scale.x * textScale), (int)(font.GlyphHeight * scale.y * textScale));
                        if (glyphRect.Overlaps(clip)) visibleGlyphCount++;
                        if (!font.IsSDFCapable)
                            font.DrawClassicGlyph((byte)code, glyphRect, highlightColor,
                                label.ShadowPosition * scale, label.ShadowColor);
                        else
                        {
                            Vector2 position = new Vector2((int)(labelRect.x + x * scale.x * textScale +
                                label.HorzPixelScrollOffset * scale.x * textScale),
                                (int)(labelRect.y + y * scale.y * textScale));
                            font.DrawSDFGlyph(code, position, scale * textScale, highlightColor,
                                label.ShadowPosition * scale, label.ShadowColor);
                        }
                    }
                }
            }
            finally { material.SetVector("_ScissorRect", previousClip); }
            lastDrawnBlueGlyphCount = visibleGlyphCount;
            if (visibleGlyphCount > 0)
                CaptureFirstBlueWord(fullWavMode ? fullWavRequestId : streamId, currentProgressSource,
                    fullWavLastObservedSampleFrame, currentCumulativeTextEnd);
        }

        private sealed class TextWordGlyphSpan
        {
            public int Start;
            public int Length;
        }

        private void BuildDisplayWordSpans()
        {
            displayWordSpans.Clear();
            if (string.IsNullOrEmpty(rowText)) return;
            int position = 0;
            while (position < rowText.Length)
            {
                while (position < rowText.Length && char.IsWhiteSpace(rowText[position])) position++;
                if (position >= rowText.Length) break;
                int start = position;
                while (position < rowText.Length && !char.IsWhiteSpace(rowText[position])) position++;
                displayWordSpans.Add(new TextWordGlyphSpan { Start = start, Length = position - start });
            }
        }

        private void RenderInitialText(int textEnd)
        {
            if (!initialRevealMode || row == null || row.textLabel == null) return;
            int boundedEnd = Math.Max(0, Math.Min(rowText.Length, textEnd));
            if (!IsSafeTextBoundary(rowText, boundedEnd))
            { ReportOnce("alignment", "visible-prefix-splits-unicode-grapheme"); return; }
            string visible = rowText.Substring(0, boundedEnd);
            if (!string.Equals(row.textLabel.Text, visible, StringComparison.Ordinal))
                row.textLabel.Text = visible;
            lastRenderedText = visible;
        }

        private void ResolveTerminalPresentation()
        {
            if (row == null || row.textLabel == null) return;
            row.textLabel.Text = rowText;
            lastRenderedText = rowText;
            initialRevealMode = false;
            terminalPresentationResolved = true;
            currentWordStartUtf16 = -1;
            currentCumulativeTextEnd = rowText.Length;
            cumulativeTextHighWater = rowText.Length;
            lastDrawnBlueGlyphCount = 0;
        }

        private static bool IsSafeTextBoundary(string text, int index)
        {
            if (index < 0 || index > text.Length) return false;
            if (index > 0 && index < text.Length && char.IsHighSurrogate(text[index - 1]) &&
                char.IsLowSurrogate(text[index])) return false;
            if (index < text.Length)
            {
                UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(text, index);
                if (category == UnicodeCategory.NonSpacingMark || category == UnicodeCategory.SpacingCombiningMark ||
                    category == UnicodeCategory.EnclosingMark) return false;
            }
            return true;
        }

        private int GetFullWavCurrentWordStart(BreezeTtsTrialBridge.ActiveAudioEvidence evidence, long sampleFrame)
        {
            if (evidence == null || evidence.Text != rowText || evidence.RequestId != fullWavRequestId ||
                evidence.PersonId != personId || evidence.ConversationToken != conversationToken) return -1;
            bool presentationRequired;
            PortraitWordAlignmentTimeline preparedTimeline;
            if (TryGetPreparedWordTimeline(evidence, out presentationRequired, out preparedTimeline) &&
                presentationRequired && preparedTimeline != null)
                return sampleFrame <= preparedTimeline.AudioFrames
                    ? preparedTimeline.GetCurrentWordStart(sampleFrame) : -1;
            EnsureFullWavTimelineLoad(evidence);
            PortraitWordAlignmentTimeline timeline;
            lock (fullTimelineGate) timeline = fullWavTimeline;
            if (timeline != null && sampleFrame <= timeline.AudioFrames)
                return timeline.GetCurrentWordStart(sampleFrame);
            EnsureFullWavEstimate(evidence);
            if (fullWavEstimate != null && sampleFrame <= fullWavEstimate.TotalSamples)
                for (int index = 0; index < fullWavEstimate.Words.Count; index++)
                {
                    TextWeightedWordSpan word = fullWavEstimate.Words[index];
                    if (sampleFrame >= word.StartSample && sampleFrame < word.EndSample)
                        return word.TextStartUtf16;
                }
            return -1;
        }

        private static int GetAlignedCurrentWordStart(WordAlignmentWord[] words, int textOffset, long sampleFrame)
        {
            if (words == null || sampleFrame < 0) return -1;
            for (int index = 0; index < words.Length; index++)
            {
                WordAlignmentWord word = words[index];
                if (word == null) continue;
                if (word.startSample <= sampleFrame && sampleFrame < word.endSample)
                    return textOffset + word.textStartUtf16;
                if (word.startSample > sampleFrame) break;
            }
            return -1;
        }

        private int GetFullWavProgressEnd(BreezeTtsTrialBridge.ActiveAudioEvidence evidence,
            long sampleFrame, bool completed, out string source)
        {
            source = "none";
            if (evidence == null || evidence.RequestId == null || evidence.Text != rowText ||
                evidence.PersonId != personId || evidence.ConversationToken != conversationToken ||
                evidence.SampleFrames <= 0 || sampleFrame < 0)
            { ReportOnce("binding-miss", "progress-evidence-does-not-match-bound-row"); return 0; }

            if (completed)
            {
                source = "completed-playback-receipt";
                return rowText.Length;
            }

            bool presentationRequired;
            PortraitWordAlignmentTimeline timeline;
            if (TryGetPreparedWordTimeline(evidence, out presentationRequired, out timeline) &&
                presentationRequired && timeline != null)
            {
                if (sampleFrame <= timeline.AudioFrames)
                {
                    source = "forced-word-alignment";
                    return timeline.GetCumulativeTextEnd(sampleFrame);
                }
                return 0;
            }

            EnsureFullWavTimelineLoad(evidence);
            lock (fullTimelineGate) timeline = fullWavTimeline;
            if (timeline != null && sampleFrame <= timeline.AudioFrames)
            {
                source = "forced-word-alignment";
                return timeline.GetCumulativeTextEnd(sampleFrame);
            }

            EnsureFullWavEstimate(evidence);
            if (fullWavEstimate != null && sampleFrame <= fullWavEstimate.TotalSamples)
            {
                source = "text-weighted-audio-estimate";
                return fullWavEstimate.GetCumulativeTextEnd(sampleFrame);
            }
            return 0;
        }

        private void EnsureFullWavEstimate(BreezeTtsTrialBridge.ActiveAudioEvidence evidence)
        {
            if (evidence == null || string.IsNullOrEmpty(evidence.RequestId) || evidence.SampleFrames <= 0) return;
            if (fullWavEstimateRequestId == evidence.RequestId && fullWavEstimateFrames == evidence.SampleFrames &&
                fullWavEstimate != null) return;
            fullWavEstimateRequestId = evidence.RequestId;
            fullWavEstimateFrames = evidence.SampleFrames;
            fullWavEstimate = null;
            TextWeightedWordProgress estimate;
            if (TextWeightedWordProgress.TryCreate(rowText, evidence.SampleFrames, out estimate))
                fullWavEstimate = estimate;
            else
                ReportOnce("estimate-unavailable", "text-or-audio-frame-budget-outside-supported-estimator");
        }

        private bool TryGetPreparedWordTimeline(BreezeTtsTrialBridge.ActiveAudioEvidence evidence,
            out bool presentationRequired, out PortraitWordAlignmentTimeline timeline)
        {
            presentationRequired = false;
            timeline = null;
            if (evidence == null) return false;

            if (fullWavPresentationRequired && fullWavPresentationRequestId == evidence.RequestId)
            {
                lock (fullTimelineGate)
                    if (fullWavTimelineRequestId == evidence.RequestId) timeline = fullWavTimeline;
                presentationRequired = true;
                return true;
            }
            if (fullWavBridge == null) return false;

            BreezeTtsTrialBridge.ActiveAudioEvidence activeEvidence;
            PortraitPreparedPresentation presentation;
            if (!fullWavBridge.TryReadActivePreparedPresentation(out activeEvidence,
                out presentationRequired, out presentation) || activeEvidence == null ||
                activeEvidence.RequestId != evidence.RequestId || activeEvidence.PersonId != evidence.PersonId ||
                activeEvidence.ConversationToken != evidence.ConversationToken || activeEvidence.Text != evidence.Text ||
                activeEvidence.SampleRate != evidence.SampleRate || activeEvidence.SampleFrames != evidence.SampleFrames)
            {
                presentationRequired = false;
                return false;
            }

            fullWavPresentationRequestId = evidence.RequestId;
            fullWavPresentationRequired = presentationRequired;

            if (!presentationRequired) return true;
            if (presentation == null || presentation.Words == null ||
                presentation.RequestId != evidence.RequestId || presentation.PersonId != evidence.PersonId ||
                presentation.ConversationToken != evidence.ConversationToken || presentation.Text != evidence.Text ||
                presentation.SampleRate != evidence.SampleRate || presentation.AudioFrames != evidence.SampleFrames)
                return true;

            timeline = presentation.Words;
            lock (fullTimelineGate)
            {
                fullWavTimeline = timeline;
                fullWavTimelineRequestId = evidence.RequestId;
            }
            return true;
        }

        private void EnsureFullWavTimelineLoad(BreezeTtsTrialBridge.ActiveAudioEvidence evidence)
        {
            if (evidence == null) return;
            lock (fullTimelineGate)
            {
                if (fullWavTimeline != null && fullWavTimelineRequestId == evidence.RequestId) return;
                if (fullWavTimelineLoading || Stopwatch.GetTimestamp() < nextFullWavTimelineRetryTicks) return;
                fullWavTimelineLoading = true;
                fullWavTimelineLoadingRequestId = evidence.RequestId;
                if (!ThreadPool.QueueUserWorkItem(_ => LoadFullWavTimeline(evidence)))
                {
                    fullWavTimelineLoading = false;
                    fullWavTimelineLoadingRequestId = null;
                    nextFullWavTimelineRetryTicks = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 2;
                }
            }
        }

        private static int GetAlignedTextEnd(WordAlignmentWord[] words, int textOffset,
            long sampleFrame, bool completed)
        {
            if (words == null || sampleFrame < 0) return 0;
            int textEnd = 0;
            for (int index = 0; index < words.Length; index++)
            {
                WordAlignmentWord word = words[index];
                if (word == null || (!completed && word.startSample > sampleFrame)) break;
                textEnd = Math.Max(textEnd, textOffset + word.textStartUtf16 + word.textLengthUtf16);
            }
            return textEnd;
        }

        private WordAlignmentTimelineDocument GetSentenceTimeline(int sequence)
        {
            WordAlignmentTimelineDocument value;
            if (timelines.TryGetValue(sequence, out value)) return value;
            float retryAt;
            if (nextTimelinePollBySequence.TryGetValue(sequence, out retryAt) && Time.unscaledTime < retryAt)
                return null;
            nextTimelinePollBySequence[sequence] = Time.unscaledTime + .15f;
            value = ReadValidatedTimeline(sequence);
            if (value != null) timelines[sequence] = value;
            return value;
        }

        private bool IsMatchingEvidence(BreezeTtsTrialBridge.ActiveAudioEvidence evidence)
        {
            return evidence != null && evidence.RequestId == streamId && evidence.PersonId == personId &&
                evidence.ConversationToken == conversationToken && evidence.Text == rowText &&
                fullWavBridge.OutputAudioSource != null && fullWavBridge.OutputAudioSource.clip != null &&
                fullWavBridge.OutputAudioSource.clip.frequency == evidence.SampleRate;
        }

        private bool TryGetCompletedFullWavEvidence(out BreezeTtsTrialBridge.ActiveAudioEvidence evidence)
        { return TryGetCompletedFullWavEvidence(fullWavRequestId, out evidence); }

        private bool TryGetCompletedFullWavEvidence(string requestId,
            out BreezeTtsTrialBridge.ActiveAudioEvidence evidence)
        {
            evidence = null;
            if (fullWavBridge == null || string.IsNullOrWhiteSpace(requestId) ||
                fullWavBridge.GetRequestState(requestId) !=
                BreezeTtsTrialBridge.RequestState.Completed) return false;
            int playbackIndex = fullWavBridge.GetPlaybackIndex(requestId);
            if (playbackIndex < 1) return false;
            if (playbackIndex != completedFullWavReceiptPlaybackIndex)
            {
                completedFullWavReceiptPlaybackIndex = playbackIndex;
                completedFullWavReceiptChecked = false;
                completedFullWavReceiptValid = false;
                completedFullWavEvidence = null;
            }
            if (completedFullWavReceiptChecked)
            {
                evidence = completedFullWavEvidence;
                return completedFullWavReceiptValid;
            }
            completedFullWavReceiptChecked = true;

            try
            {
                string boundPerson, boundToken, boundText;
                string measurementJson;
                if (!fullWavBridge.TryReadRequestBinding(requestId, out boundPerson, out boundToken, out boundText) ||
                    boundPerson != personId || boundToken != conversationToken || boundText != rowText ||
                    !fullWavBridge.TryReadLatestPlaybackMeasurement(requestId, out measurementJson))
                { ReportOnce("binding-miss", "completed-receipt-or-request-binding-missing"); return false; }

                WordHighlightPlaybackReceipt receipt = JsonUtility.FromJson<WordHighlightPlaybackReceipt>(measurementJson);
                if (receipt == null || receipt.schemaVersion != 1 || receipt.requestId != requestId ||
                    receipt.conversationToken != conversationToken || receipt.personId != personId ||
                    receipt.status != "completed" || receipt.mixerCallbackFrames <= 0 ||
                    receipt.nonZeroOutputSamples <= 0 || receipt.expectedSampleFrames <= 0 ||
                    receipt.sampleRate < 8000 || receipt.sampleRate > 192000 || receipt.channels < 1 || receipt.channels > 2 ||
                    receipt.lastKnownSample <= 0 || receipt.playbackStartMonotonicTicks <= 0 ||
                    receipt.playbackEndMonotonicTicks <= receipt.playbackStartMonotonicTicks)
                { ReportOnce("binding-miss", "completed-playback-receipt-invalid"); return false; }

                string root = Path.GetFullPath(responsesRoot);
                string manifestPath = Path.GetFullPath(Path.Combine(root, requestId + ".manifest.json"));
                if (!manifestPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                { ReportOnce("binding-miss", "manifest-path-outside-response-root"); return false; }
                var manifestInfo = new FileInfo(manifestPath);
                if (!manifestInfo.Exists || manifestInfo.Length < 2 || manifestInfo.Length > 4096)
                { ReportOnce("binding-miss", "completed-response-manifest-missing"); return false; }
                WordHighlightResponseManifest manifest = JsonUtility.FromJson<WordHighlightResponseManifest>(
                    File.ReadAllText(manifestPath, Encoding.UTF8));
                string wavePath;
                if (manifest == null || manifest.schemaVersion != 1 || manifest.requestId != requestId ||
                    manifest.conversationToken != conversationToken || manifest.personId != personId ||
                    manifest.encoding != "pcm_s16le" || manifest.sampleRate != receipt.sampleRate ||
                    manifest.channels != receipt.channels || manifest.sampleFrames != receipt.expectedSampleFrames ||
                    !BreezeTtsTrialProtocol.TryResolveResponseWavePath(root, manifest.wavFile, out wavePath))
                { ReportOnce("binding-miss", "completed-response-manifest-binding-invalid"); return false; }

                var waveInfo = new FileInfo(wavePath);
                if (!waveInfo.Exists || waveInfo.Length < 44 || waveInfo.Length > BreezeTtsTrialProtocol.MaximumWaveBytes)
                { ReportOnce("binding-miss", "completed-wav-missing-or-out-of-bounds"); return false; }
                evidence = new BreezeTtsTrialBridge.ActiveAudioEvidence(requestId, personId,
                    conversationToken, rowText, Path.GetFullPath(wavePath), receipt.sampleRate,
                    receipt.channels, receipt.expectedSampleFrames);
                completedFullWavEvidence = evidence;
                completedFullWavReceiptValid = true;
                return true;
            }
            catch (Exception error)
            {
                ReportOnce("binding-miss", "completed-evidence-read-failed:" + error.GetType().Name);
                return false;
            }
        }

        private int CompletedSentenceCount()
        {
            string completed = adapter == null ? string.Empty : adapter.CompletedSentenceText;
            if (string.IsNullOrEmpty(completed)) return 0;
            var expected = new StringBuilder();
            int count = 0;
            for (int index = 0; index < sentences.Count; index++)
            {
                if (index > 0) expected.Append(' ');
                expected.Append(sentences[index]);
                if (string.Equals(expected.ToString(), completed, StringComparison.Ordinal)) count = index + 1;
                else if (!completed.StartsWith(expected.ToString(), StringComparison.Ordinal)) break;
            }
            return count;
        }

        private WordAlignmentTimelineDocument ReadValidatedTimeline(int sequence)
        {
            try
            {
                string stem = streamId + "." + sequence.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
                string path = Path.GetFullPath(Path.Combine(responsesRoot, streamId, "sentences", stem + ".alignment.json"));
                string expectedDirectory = Path.GetFullPath(Path.Combine(responsesRoot, streamId, "sentences"));
                if (!path.StartsWith(expectedDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(path)) return null;
                var info = new FileInfo(path);
                if (info.Length < 2 || info.Length > 256 * 1024) return null;
                WordAlignmentTimelineDocument value = DeserializeTimeline(File.ReadAllText(path, Encoding.UTF8));
                string sentence = sentences[sequence];
                if (value == null || value.schemaVersion != 1 || value.status != "ready" ||
                    value.timelineKind != "forced-word-alignment" || value.text != sentence ||
                    value.audioSampleRate != adapter.ActiveSampleRate || value.audioFrames <= 0 ||
                    value.coverage == null || value.coverage.matchedTokens <= 0 ||
                    value.coverage.matchedTokens != value.coverage.transcriptTokens ||
                    value.words == null || value.words.Length != value.coverage.matchedTokens ||
                    value.provenance == null || !IsHash(value.provenance.audioSha256) ||
                    !string.Equals(value.provenance.transcriptSha256, Sha256(sentence), StringComparison.OrdinalIgnoreCase))
                    return null;
                WordAlignmentStreamBinding binding = value.streamBinding;
                if (binding == null || binding.schemaVersion != 1 || binding.streamId != streamId ||
                    binding.conversationToken != conversationToken || binding.personId != personId ||
                    binding.scope != "sentence" || binding.sentenceSequence != sequence ||
                    binding.sampleRate != value.audioSampleRate || binding.sampleFrames != value.audioFrames ||
                    !string.Equals(binding.textSha256, Sha256(sentence), StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(binding.audioSha256, value.provenance.audioSha256, StringComparison.OrdinalIgnoreCase))
                    return null;

                if (!ValidateWords(value, sentence)) return null;
                return value;
            }
            catch { return null; }
        }

        private void LoadFullWavTimeline(BreezeTtsTrialBridge.ActiveAudioEvidence evidence)
        {
            PortraitWordAlignmentTimeline loaded = null;
            string failureCode = null;
            try
            {
                string wavePath = Path.GetFullPath(evidence.WavePath);
                if (!string.Equals(Path.GetFileNameWithoutExtension(wavePath), evidence.RequestId,
                    StringComparison.Ordinal)) throw new InvalidDataException("alignment-wav-request-binding");
                var waveInfo = new FileInfo(wavePath);
                if (!waveInfo.Exists || waveInfo.Length < 44 || waveInfo.Length > BreezeTtsTrialProtocol.MaximumWaveBytes)
                    throw new InvalidDataException("alignment-wave-size");
                string alignmentPath = fullWavMode ? Path.ChangeExtension(wavePath, ".alignment.json") :
                    Path.Combine(responsesRoot, streamId, "sentences", streamId + ".alignment.json");
                var sidecarInfo = new FileInfo(alignmentPath);
                if (!sidecarInfo.Exists || sidecarInfo.Length < 2 || sidecarInfo.Length > 256 * 1024)
                    throw new FileNotFoundException("alignment-sidecar-not-ready");
                string json = File.ReadAllText(alignmentPath, Encoding.UTF8);
                WordAlignmentTimelineDocument document;
                if (!PortraitWordAlignmentTimelineValidator.TryDeserialize(json, out document, out failureCode))
                    throw new FormatException(failureCode ?? "alignment-sidecar-parse-fail");
                var audioEvidence = new PortraitVisemeAudioEvidence
                {
                    RequestId = evidence.RequestId,
                    PersonId = evidence.PersonId,
                    ConversationToken = evidence.ConversationToken,
                    Text = evidence.Text,
                    SampleRate = evidence.SampleRate,
                    AudioFrames = evidence.SampleFrames,
                    Channels = evidence.Channels,
                    AudioSha256 = Sha256File(wavePath),
                    WavePath = wavePath,
                };
                if (!PortraitWordAlignmentTimelineValidator.TryValidate(document, audioEvidence, !fullWavMode,
                    out loaded, out failureCode))
                    throw new InvalidDataException(failureCode ?? "alignment-sidecar-invalid");
            }
            catch (FileNotFoundException) { loaded = null; failureCode = "sidecar-not-ready"; }
            catch (FormatException) { loaded = null; failureCode = "sidecar-parse-fail"; }
            catch (Exception error)
            {
                loaded = null;
                if (failureCode == null)
                    failureCode = error is InvalidDataException ? error.Message : "timeline-read-fail";
            }

            if (failureCode != null) ReportStatic(failureCode.StartsWith("rate", StringComparison.Ordinal)
                ? "rate" : (failureCode.IndexOf("hash", StringComparison.OrdinalIgnoreCase) >= 0 ? "hash" :
                (failureCode == "sidecar-parse-fail" || failureCode == "sidecar-not-ready"
                    ? failureCode : "binding-miss")),
                evidence.RequestId, failureCode);

            lock (fullTimelineGate)
            {
                if (fullWavTimelineLoadingRequestId != evidence.RequestId) return;
                fullWavTimeline = loaded;
                fullWavTimelineRequestId = loaded == null ? null : evidence.RequestId;
                fullWavTimelineLoading = false;
                fullWavTimelineLoadingRequestId = null;
                nextFullWavTimelineRetryTicks = loaded == null
                    ? Stopwatch.GetTimestamp() + Stopwatch.Frequency / 2 : 0;
            }
        }

        private void ReportOnce(string code, string detail)
        {
            if (!DiagnosticsEnabled || !reportedReasons.Add(code)) return;
            Debug.Log("[BreezeWordHighlight] code=" + code + " requestId=" +
                (fullWavMode ? fullWavRequestId : streamId) + " detail=" + detail);
        }

        private static void ReportStatic(string code, string requestId, string detail)
        {
            if (!diagnosticsEnabled) return;
            string key = (requestId ?? "unknown") + "|" + code;
            lock (diagnosticGate)
            {
                if (!emittedDiagnosticKeys.Add(key)) return;
            }
            Debug.Log("[BreezeWordHighlight] code=" + code + " requestId=" +
                (requestId ?? "unknown") + " detail=" + detail);
        }

        private static bool DiagnosticsEnabled { get { return diagnosticsEnabled; } }

        internal static void ReportDiagnostic(string code, string requestId, string detail)
        { ReportStatic(code, requestId, detail); }

        private static void CaptureFirstBlueWord(string requestId, string source, long sampleFrame, int textEndUtf16)
        {
            if (!DiagnosticsEnabled) return;
            lock (diagnosticGate)
            {
                if (firstBlueScreenshotQueued) return;
                firstBlueScreenshotQueued = true;
            }
            string path = null;
            try
            {
                string directory = Path.Combine(Application.persistentDataPath, "PeopleOfTheIliacBay");
                Directory.CreateDirectory(directory);
                path = Path.Combine(directory, "breeze-word-highlight-first-blue.png");
                ScreenCapture.CaptureScreenshot(path);
            }
            catch (Exception error)
            { ReportStatic("screenshot-fail", requestId, error.GetType().Name); return; }
            ReportStatic("firstDrawnWord", requestId, "source=" + source + " sampleFrame=" +
                sampleFrame.ToString(CultureInfo.InvariantCulture) + " cumulativeTextEndUtf16=" +
                textEndUtf16.ToString(CultureInfo.InvariantCulture) + " screenshot=" + path);
        }

        private static bool ValidateWords(WordAlignmentTimelineDocument value, string text)
        {
            long previousEnd = 0;
            int previousTextEnd = 0;
            for (int index = 0; index < value.words.Length; index++)
            {
                WordAlignmentWord word = value.words[index];
                if (word == null || string.IsNullOrEmpty(word.text) || word.textStartUtf16 < previousTextEnd ||
                    word.textLengthUtf16 <= 0 || word.textStartUtf16 + word.textLengthUtf16 > text.Length ||
                    word.startSample < previousEnd || word.endSample <= word.startSample ||
                    word.endSample > value.audioFrames ||
                    !string.Equals(text.Substring(word.textStartUtf16, word.textLengthUtf16), word.text,
                        StringComparison.Ordinal)) return false;
                previousEnd = word.endSample;
                previousTextEnd = word.textStartUtf16 + word.textLengthUtf16;
            }
            return true;
        }

        private static WordAlignmentTimelineDocument DeserializeTimeline(string json)
        {
            FullSerializer.fsData data = FullSerializer.fsJsonParser.Parse(json);
            if (data == null || !data.IsDictionary) return null;
            WordAlignmentTimelineDocument document = null;
            var serializer = new FullSerializer.fsSerializer();
            FullSerializer.fsResult result = serializer.TryDeserialize(data, ref document);
            return result.Failed ? null : document;
        }

        private static string Sha256File(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (SHA256 hash = SHA256.Create())
            {
                byte[] digest = hash.ComputeHash(stream);
                var result = new StringBuilder(digest.Length * 2);
                for (int index = 0; index < digest.Length; index++)
                    result.Append(digest[index].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                return result.ToString();
            }
        }

        private static bool TryMapGlyphIndices(string fullText, IList layout, int start, int length,
            out List<int> indices)
        {
            indices = null;
            int end = start + length;
            if (start < 0 || length <= 0 || end > fullText.Length) return false;
            var mapped = new List<int>(length);
            int charIndex = 0;
            for (int glyphIndex = 0; glyphIndex < layout.Count; glyphIndex++)
            {
                while (charIndex < fullText.Length && (fullText[charIndex] == '\r' || fullText[charIndex] == '\n'))
                    charIndex++;
                if (charIndex >= fullText.Length) return false;
                object glyph = layout[glyphIndex];
                int code = (int)glyphCodeField.GetValue(glyph);
                // TextLabel's word-wrap drops the separating space at a wrapped line end.
                if (code != fullText[charIndex] && fullText[charIndex] == ' ')
                    charIndex++;
                if (charIndex >= fullText.Length) return false;
                if (code != fullText[charIndex]) return false;
                if (charIndex >= start && charIndex < end) mapped.Add(glyphIndex);
                charIndex++;
            }
            if (mapped.Count != length) return false;
            indices = mapped;
            return true;
        }

        private static bool IsHash(string value)
        { return value != null && value.Length == 64; }

        private static string Sha256(string value)
        {
            using (SHA256 hash = SHA256.Create())
            {
                byte[] digest = hash.ComputeHash(Encoding.UTF8.GetBytes(value));
                var result = new StringBuilder(digest.Length * 2);
                for (int index = 0; index < digest.Length; index++)
                    result.Append(digest[index].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                return result.ToString();
            }
        }
    }
}
