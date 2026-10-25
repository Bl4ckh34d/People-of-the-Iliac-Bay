using System;
using System.Collections.Generic;
using System.Globalization;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private sealed class CapturedSpeechPresentation
        {
            internal UnityEngine.Object Speaker;
            internal string Token;
            internal NpcPresentationState State;
        }

        private bool animatedPortraitsEnabled;
        // Only the explicitly opted-in disposable native probe temporarily owns rendering.
        private bool splitPortraitProbeOwnsRendering = false;
        private SplitPortraitRuntimeOverlay splitPortraitOverlay;
        private SplitPortraitBlinkClock splitPortraitBlink;
        private SplitPortraitGazeClock splitPortraitGaze;
        private SplitPortraitSpeechSource splitPortraitSpeech;
        private BreezeTtsTrialBridge splitPortraitSpeechBridge;
        private Func<SplitPortraitRuntimeOverlay.Binding, SplitPortraitRuntimeOverlay.Presentation> splitPortraitProvider;
        private readonly Dictionary<string, CapturedSpeechPresentation> speechPresentationByRequest =
            new Dictionary<string, CapturedSpeechPresentation>(StringComparer.Ordinal);
        private string speechPresentationToken;
        private int portraitStateBindingRevision = -1;
        private string portraitPresentationOwner;
        private float nextPortraitStateRead;
        private NpcPresentationState portraitState;
        private SplitPortraitRuntimeOverlay.Presentation lastPortraitPresentation;

        private void RefreshSplitPortraitRuntime()
        {
            if (!animatedPortraitsEnabled || !options.Enabled)
            {
                if (splitPortraitOverlay) splitPortraitOverlay.DetachAndRestore();
                ResetPortraitPresentation();
                return;
            }
            if (!splitPortraitOverlay && mod != null)
            {
                string result;
                SplitPortraitRuntimeOverlay.TryInstall(mod, gameObject, out splitPortraitOverlay, out result);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SPLIT_PORTRAITS: " + result);
            }
            if (splitPortraitOverlay != null && splitPortraitBlink == null)
            {
                SplitPortraitAnimationPolicy policy = splitPortraitOverlay.AnimationPolicy;
                splitPortraitBlink = policy == null
                    ? new SplitPortraitBlinkClock(() => UnityEngine.Random.value)
                    : new SplitPortraitBlinkClock(() => UnityEngine.Random.value, policy);
                if (policy != null)
                    splitPortraitGaze = new SplitPortraitGazeClock(() => UnityEngine.Random.value, policy);
            }
            if (splitPortraitProvider == null) splitPortraitProvider = CaptureSplitPortraitPresentation;
        }

        private void LateUpdate()
        {
            if (splitPortraitProbeOwnsRendering) return;
            if (!animatedPortraitsEnabled || !options.Enabled || saveFault || !isActiveAndEnabled)
            {
                if (splitPortraitOverlay) splitPortraitOverlay.DetachAndRestore();
                ResetPortraitPresentation();
                return;
            }
            if (!splitPortraitOverlay) RefreshSplitPortraitRuntime();
            if (!splitPortraitOverlay) return;
            splitPortraitOverlay.RefreshBindingAndRender(splitPortraitProvider);
            if (!splitPortraitOverlay.IsBound || !splitPortraitOverlay.HasValidArt) ResetPortraitPresentation();
        }

        private SplitPortraitRuntimeOverlay.Presentation CaptureSplitPortraitPresentation(
            SplitPortraitRuntimeOverlay.Binding binding)
        {
            bool changed = portraitStateBindingRevision != binding.Revision;
            if (changed)
            {
                portraitStateBindingRevision = binding.Revision;
                portraitPresentationOwner = binding.Revision.ToString(CultureInfo.InvariantCulture);
                nextPortraitStateRead = 0;
                portraitState = null;
                lastPortraitPresentation = null;
            }
            string personId = NativeTopicTtsPersonId(GameManager.Instance.TalkManager);
            string token = null;
            if (nativeTopicTtsWindow == binding.BoundTalk &&
                ReferenceEquals(nativeTopicTtsSpeaker, binding.Speaker) && nativeTopicTtsPersonId == personId)
                token = nativeTopicTtsToken;
            else if (conversationSessionOpen && memoryWindow == binding.BoundTalk &&
                ReferenceEquals(conversationContextSessionSpeaker, binding.Speaker) && memoryPersonId == personId)
                token = conversationContextSessionId;

            if (splitPortraitSpeechBridge != breezeTtsTrial)
            {
                if (splitPortraitSpeech != null) splitPortraitSpeech.Dispose();
                splitPortraitSpeechBridge = breezeTtsTrial;
                splitPortraitSpeech = breezeTtsTrial ? new SplitPortraitSpeechSource(breezeTtsTrial) : null;
            }
            SplitPortraitSpeechRead speech = splitPortraitSpeech == null
                ? default(SplitPortraitSpeechRead) : splitPortraitSpeech.Read(personId, token);

            if (Time.unscaledTime >= nextPortraitStateRead || changed)
            {
                // Binding changes clear this cache above. A transient failed live
                // capture for the same speaker must not flash a neutral portrait.
                NpcPresentationState refreshed = CaptureCurrentNpcPresentation(personId);
                if (refreshed != null) portraitState = refreshed;
                nextPortraitStateRead = Time.unscaledTime + 0.25f;
            }
            NpcPresentationState state = portraitState;
            CapturedSpeechPresentation spoken;
            if (speech.IsPlaying && speech.RequestId != null &&
                speechPresentationByRequest.TryGetValue(speech.RequestId, out spoken) &&
                ReferenceEquals(spoken.Speaker, binding.Speaker) && spoken.Token == token &&
                spoken.State.PersonId == personId)
                state = spoken.State;

            SplitPortraitEmotionId emotion = state == null ? SplitPortraitEmotionId.Neutral :
                SplitPortraitEmotionProjector.Project(new SplitPortraitEmotionInput(state.PersonId,
                    true, state.Mood, state.DispositionKnown, state.Disposition, state.TrustKnown, state.Trust,
                    state.PlayerFearKnown, state.PlayerFear, state.RelationshipKnown, state.Resentment,
                    state.AcuteFearKnown, (int)Math.Round(state.AcuteFearIntensity, MidpointRounding.AwayFromZero)));
            SplitPortraitEyePhase eye = splitPortraitBlink.Advance(portraitPresentationOwner, true,
                Time.realtimeSinceStartup, speech.IsPlaying);
            string emotionId = SplitPortraitEmotionProjector.ToManifestId(emotion);
            string gazeId = splitPortraitGaze == null ? null : splitPortraitGaze.Advance(
                portraitPresentationOwner, true, Time.realtimeSinceStartup,
                splitPortraitOverlay.HasCompleteGazePairForEmotion(emotionId));
            PortraitMouthShape mouth = speech.IsPlaying ? speech.MouthShape : PortraitMouthShape.X;
            if (lastPortraitPresentation == null || lastPortraitPresentation.EmotionId != emotionId ||
                lastPortraitPresentation.EyePhase != eye || lastPortraitPresentation.MouthShape != mouth ||
                lastPortraitPresentation.GazeId != gazeId)
                lastPortraitPresentation = new SplitPortraitRuntimeOverlay.Presentation(emotionId, eye, mouth, gazeId);
            return lastPortraitPresentation;
        }

        private NpcPresentationState CaptureCurrentNpcPresentation(string expectedPersonId)
        {
            if (store == null || saveFault || GameManager.Instance == null ||
                GameManager.Instance.TalkManager == null || !CurrentConversationSpeakerObject() ||
                string.IsNullOrEmpty(expectedPersonId) ||
                NativeTopicTtsPersonId(GameManager.Instance.TalkManager) != expectedPersonId ||
                DaggerfallUnity.Instance == null || DaggerfallUnity.Instance.WorldTime == null) return null;
            var clock = DaggerfallUnity.Instance.WorldTime.Now;
            double now = (double)clock.ToSeconds() + (double)clock.Second - Math.Floor(clock.Second);
            try
            {
                RefreshRandomPersonalityEligibility(store, expectedPersonId, CurrentConversationSpeakerObject());
                return NpcPresentationState.Capture(store, expectedPersonId, now, true);
            }
            catch (ArgumentException) { return null; }
        }

        private bool NotifyNpcSpeechWithPresentation(string personId, string name, string text, string token,
            string gender, BreezeTtsTrialBridge.VoiceIdentity identity, out string requestId)
        {
            requestId = null;
            if (!breezeTtsTrial) return false;
            NpcPresentationState state = CaptureCurrentNpcPresentation(personId);
            if (!breezeTtsTrial.NotifyFinalizedNpcText(personId, name, text, token, gender,
                state == null ? null : state.CreateDeliveryContext(), identity, out requestId))
            {
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_TTS_PUBLISH_FAILED: reason=" +
                    breezeTtsTrial.LastRejectedReason + "; person-bound=" + !string.IsNullOrWhiteSpace(personId) +
                    "; name-bound=" + !string.IsNullOrWhiteSpace(name) + "; token-bound=" +
                    !string.IsNullOrWhiteSpace(token) + "; text-length=" + (text == null ? 0 : text.Length) + ".");
                return false;
            }
            if (speechPresentationToken != token)
            {
                speechPresentationByRequest.Clear();
                speechPresentationToken = token;
            }
            if (state != null)
                speechPresentationByRequest[requestId] = new CapturedSpeechPresentation
                    { Speaker = CurrentConversationSpeakerObject(), Token = token, State = state };
            return true;
        }

        private void ResetPortraitPresentation()
        {
            if (splitPortraitBlink != null) splitPortraitBlink.Reset();
            if (splitPortraitGaze != null) splitPortraitGaze.Reset();
            if (splitPortraitSpeech != null) splitPortraitSpeech.Reset();
            portraitStateBindingRevision = -1;
            portraitPresentationOwner = null;
            portraitState = null;
            lastPortraitPresentation = null;
        }

        private void ForgetNpcSpeechPresentation(string token)
        {
            if (string.IsNullOrEmpty(token) || token != speechPresentationToken) return;
            speechPresentationByRequest.Clear();
            speechPresentationToken = null;
        }

        private void ShutdownSplitPortraitRuntime()
        {
            if (splitPortraitOverlay) splitPortraitOverlay.DetachAndRestore();
            if (splitPortraitSpeech != null) splitPortraitSpeech.Dispose();
            splitPortraitSpeech = null;
            speechPresentationByRequest.Clear();
            ResetPortraitPresentation();
        }
    }
}
