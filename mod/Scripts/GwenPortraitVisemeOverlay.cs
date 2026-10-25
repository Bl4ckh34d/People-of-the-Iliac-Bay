using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>
    /// Explicitly opt-in, single-resident portrait proof. It never replaces the native face:
    /// only the fixed mouth crop from the generated atlas is copied onto the readable native texture.
    /// </summary>
    public sealed class GwenPortraitVisemeOverlay : MonoBehaviour
    {
        public const string RunFlag = "-peopleoftheiliacbay-portrait-visemes";
        public const string PrototypePersonId = "s:206728581:h:131074:r:0";
        public const int PrototypeFaceIndex = 76;
        private const int FaceSize = 64;
        private const int CellSize = 418;
        private const int AtlasSize = 1254;
        private const int MouthX = 18;
        private const int MouthTop = 35;
        private const int MouthWidth = 28;
        private const int MouthHeight = 20;
        private const string AtlasAssetName = "gwen-visemes-atlas-v1.png.bytes";
        private const int MaximumSidecarBytes = 2 * 1024 * 1024;
        private const long MaximumWaveHashBytes = 128L * 1024 * 1024;

        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo PortraitField = typeof(DaggerfallTalkWindow).GetField("texturePortrait", PrivateInstance);
        private static readonly FieldInfo PortraitPanelField = typeof(DaggerfallTalkWindow).GetField("panelPortrait", PrivateInstance);
        private static readonly FieldInfo ConversationPersonIdField = typeof(PeopleOfTheIliacBayMod).GetField("conversationPersonId", PrivateInstance);
        private static readonly FieldInfo ConversationSessionOpenField = typeof(PeopleOfTheIliacBayMod).GetField("conversationSessionOpen", PrivateInstance);
        private static readonly FieldInfo MemoryWindowField = typeof(PeopleOfTheIliacBayMod).GetField("memoryWindow", PrivateInstance);

        private sealed class SidecarLoadResult
        {
            public string RequestId;
            public string PersonId;
            public string ConversationToken;
            public PortraitVisemeTimeline Timeline;
            public string Error;
        }

        private BreezeTtsTrialBridge bridge;
        private Texture2D atlas;
        private Color32[] atlasPixels;
        private Texture2D composedPortrait;
        private Texture2D nativePortrait;
        private DaggerfallTalkWindow boundTalk;
        private PortraitMouthShape visibleShape = PortraitMouthShape.X;
        private PortraitVisemeTimeline timeline;
        private string timelineRequestId;
        private readonly float[] rmsWindow = new float[512];
        private readonly PortraitAmplitudeFallback amplitudeFallback = new PortraitAmplitudeFallback(0.02, 0.01);
        private readonly object sidecarGate = new object();
        private SidecarLoadResult completedSidecarLoad;
        private bool sidecarLoadInFlight;
        private string sidecarLoadingRequestId;
        private string sidecarLoadedRequestId;
        private string activePlaybackRequestId;
        private string timelinePersonId;
        private string timelineConversationToken;
        private string reportedSidecarFailureRequestId;
        private float nextSidecarRetryTime;
        private float nextStreamEvidencePollTime;
        private string reportedSourceKey;
        private readonly PortraitVisemePreviewCueCounter previewCueCounter = new PortraitVisemePreviewCueCounter();
        private readonly HashSet<string> capturedShapeKeys = new HashSet<string>(StringComparer.Ordinal);
        private string boundPersonId;
        private int boundFaceIndex = -1;
        private bool previewCaptureEnabled;
        public string CurrentCueSource { get; private set; }
        public string Diagnostic { get; private set; }

        /// <summary>Install only when the separate prototype flag is present.</summary>
        public static GwenPortraitVisemeOverlay TryInstall(Mod mod, BreezeTtsTrialBridge ttsBridge,
            string[] arguments, out string result)
        {
            result = null;
            if (arguments == null || Array.IndexOf(arguments, RunFlag) < 0) return null;
            if (mod == null || !ttsBridge)
            {
                result = "portrait-viseme-prototype-missing-mod-or-bridge";
                return null;
            }

            GwenPortraitVisemeOverlay alreadyInstalled = ttsBridge.GetComponent<GwenPortraitVisemeOverlay>();
            if (alreadyInstalled)
            {
                result = "portrait-viseme-prototype-already-installed";
                return alreadyInstalled;
            }

            Texture2D texture = LoadAtlas(mod, AtlasAssetName);
            if (!texture)
            {
                if (texture) UnityEngine.Object.Destroy(texture);
                result = "portrait-viseme-prototype-missing-or-invalid-atlas:" + AtlasAssetName;
                return null;
            }

            GwenPortraitVisemeOverlay overlay = ttsBridge.GetComponent<GwenPortraitVisemeOverlay>();
            if (!overlay) overlay = ttsBridge.gameObject.AddComponent<GwenPortraitVisemeOverlay>();
            overlay.Install(ttsBridge, texture, Array.IndexOf(arguments, RunFlag) >= 0);
            result = "portrait-viseme-prototype-installed-for-one-fixture-resident";
            return overlay;
        }

        private static Texture2D LoadAtlas(Mod mod, string assetName)
        {
            TextAsset asset = mod.GetAsset<TextAsset>(assetName);
            if (!asset) return null;
            Texture2D texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
            if (!texture.LoadImage(asset.bytes) || texture.width != AtlasSize || texture.height != AtlasSize)
            {
                UnityEngine.Object.Destroy(texture);
                return null;
            }
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            return texture;
        }

        private void Install(BreezeTtsTrialBridge ttsBridge, Texture2D loadedAtlas, bool capturePreviewShapes)
        {
            if (atlas && atlas != loadedAtlas) UnityEngine.Object.Destroy(atlas);
            bridge = ttsBridge;
            atlas = loadedAtlas;
            // Cache the immutable 1254x1254 pixel buffer once. GetPixels32 allocates a
            // multi-megabyte array; doing that for every mouth-shape transition caused
            // avoidable frame-time allocation pressure during speech playback.
            atlasPixels = loadedAtlas ? loadedAtlas.GetPixels32() : null;
            timeline = null;
            timelineRequestId = null;
            Diagnostic = null;
            CurrentCueSource = "none";
            reportedSourceKey = null;
            previewCaptureEnabled = capturePreviewShapes;
        }


        private void Update()
        {
            if (!TryReadBoundPortrait(out DaggerfallTalkWindow talk, out Texture2D source, out Panel panel,
                out string requestId, out string personId, out string conversationToken,
                out BreezeTtsTrialBridge.ActiveAudioEvidence audioEvidence, out bool streamingClock, out bool audioPlaying,
                out long playbackSample, out int faceIndex, out string failure))
            {
                Diagnostic = failure;
                RestoreNativePortrait();
                return;
            }

            if (!ReferenceEquals(talk, boundTalk) || !ReferenceEquals(source, nativePortrait))
            {
                RestoreNativePortrait();
                boundTalk = talk;
                nativePortrait = source;
                boundPersonId = personId;
                boundFaceIndex = faceIndex;
                visibleShape = PortraitMouthShape.X;
                if (composedPortrait) UnityEngine.Object.Destroy(composedPortrait);
                composedPortrait = null;
            }

            if (!string.Equals(activePlaybackRequestId, requestId, StringComparison.Ordinal))
            {
                activePlaybackRequestId = requestId;
                timeline = null;
                timelineRequestId = null;
                timelinePersonId = null;
                timelineConversationToken = null;
                amplitudeFallback.Reset();
                visibleShape = PortraitMouthShape.X;
            }

            AudioSource output = bridge.OutputAudioSource;
            if (!audioPlaying || output == null || !output.isPlaying || output.clip == null)
            {
                Diagnostic = "waiting-for-playback";
                RestoreNativePortrait();
                return;
            }

            PortraitMouthShape nextShape = PortraitMouthShape.X;
            DrainSidecarLoad(requestId, personId, conversationToken);
            if (audioEvidence != null) QueueSidecarLoad(audioEvidence);
            bool matchingTimeline = timeline != null &&
                string.Equals(requestId, timelineRequestId, StringComparison.Ordinal) &&
                string.Equals(personId, timelinePersonId, StringComparison.Ordinal) &&
                string.Equals(conversationToken, timelineConversationToken, StringComparison.Ordinal) &&
                (!streamingClock || (GetStreamingAdapter() != null && GetStreamingAdapter().UnderrunFrames == 0)) &&
                playbackSample >= 0 && playbackSample < timeline.AudioFrames;
            if (matchingTimeline && output.clip.frequency == timeline.SampleRate)
            {
                ReportCueSource("rhubarb-recognizer-estimate", requestId);
                nextShape = timeline.ShapeAtSample((int)playbackSample);
            }
            else
            {
                output.GetOutputData(rmsWindow, 0);
                double rms = PortraitAmplitudeFallback.CalculateRms(rmsWindow);
                ReportCueSource(PortraitAmplitudeFallback.ProvenanceLabel, requestId);
                nextShape = amplitudeFallback.ShapeForRms(rms);
                Diagnostic = "amplitude-approximation rms=" + rms.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);
            }

            if (nextShape == PortraitMouthShape.X)
            {
                RestoreNativePortrait();
                visibleShape = PortraitMouthShape.X;
                RecordDisplayedShape(requestId, visibleShape);
                return;
            }

            if (nextShape == visibleShape) return;
            if (!ComposeSpeechMouth(source, nextShape, out Texture2D result))
            {
                Diagnostic = "native-face-not-readable-or-compose-failed";
                RestoreNativePortrait();
                return;
            }

            Panel livePanel = GetPortraitPanel(talk);
            if (livePanel == null)
            {
                RestoreNativePortrait();
                return;
            }
            livePanel.BackgroundTexture = result;
            Diagnostic = CurrentCueSource == "rhubarb-recognizer-estimate"
                ? "rhubarb-estimate:" + nextShape
                : "amplitude-approximation:open";
            visibleShape = nextShape;
            RecordDisplayedShape(requestId, nextShape);
        }

        private bool TryReadBoundPortrait(out DaggerfallTalkWindow talk, out Texture2D source,
            out Panel panel, out string requestId, out string personId, out string conversationToken,
            out BreezeTtsTrialBridge.ActiveAudioEvidence audioEvidence, out bool streamingClock, out bool audioPlaying,
            out long playbackSample,
            out int faceIndex, out string failure)
        {
            talk = null;
            source = null;
            panel = null;
            requestId = null;
            personId = null;
            conversationToken = null;
            audioEvidence = null;
            streamingClock = false;
            audioPlaying = false;
            playbackSample = 0;
            faceIndex = -1;
            failure = "portrait-prototype-idle";

            if (!bridge || !atlas || DaggerfallUI.UIManager == null || DaggerfallUI.Instance == null ||
                DaggerfallUI.UIManager.TopWindow != DaggerfallUI.Instance.TalkWindow)
                return false;
            talk = DaggerfallUI.Instance.TalkWindow as DaggerfallTalkWindow;
            if (talk == null || !talk.IsSetup || PortraitField == null || ConversationPersonIdField == null)
            {
                failure = "portrait-prototype-talk-window-unavailable";
                return false;
            }

            GameManager game = GameManager.Instance;
            if (game == null || game.TalkManager == null || game.TalkManager.CurrentNPCType != TalkManager.NPCType.Mobile ||
                !game.TalkManager.MobileNPC || game.TalkManager.MobileNPC.PersonFaceRecordId != PrototypeFaceIndex)
            {
                failure = "portrait-prototype-speaker-or-face-not-allowlisted";
                return false;
            }
            faceIndex = game.TalkManager.MobileNPC.PersonFaceRecordId;

            PeopleOfTheIliacBayMod host = GetComponent<PeopleOfTheIliacBayMod>();
            string currentPersonId = host == null ? null : ConversationPersonIdField.GetValue(host) as string;
            bool sessionOpen = host != null && ConversationSessionOpenField != null &&
                (bool)ConversationSessionOpenField.GetValue(host);
            bool memoryOwnedByTalk = host != null && MemoryWindowField != null &&
                ReferenceEquals(MemoryWindowField.GetValue(host), talk);
            if (!sessionOpen || !memoryOwnedByTalk ||
                !string.Equals(currentPersonId, PrototypePersonId, StringComparison.Ordinal))
            {
                failure = "portrait-prototype-conversation-not-bound-to-allowlisted-person";
                return false;
            }

            personId = currentPersonId;
            AudioSource output = bridge.OutputAudioSource;
            bool outputPlaying = output != null && output.isPlaying && output.clip != null;
            BreezeTtsTrialBridge.ActiveAudioEvidence fullEvidence;
            if (outputPlaying && bridge.TryReadActiveAudioEvidence(out fullEvidence) &&
                string.Equals(fullEvidence.PersonId, currentPersonId, StringComparison.Ordinal) &&
                string.Equals(fullEvidence.RequestId, bridge.ActiveRequestId, StringComparison.Ordinal) &&
                string.Equals(fullEvidence.ConversationToken, bridge.ActiveConversationToken, StringComparison.Ordinal))
            {
                audioEvidence = fullEvidence;
                requestId = fullEvidence.RequestId;
                conversationToken = fullEvidence.ConversationToken;
                playbackSample = output.timeSamples;
                audioPlaying = true;
            }
            else if (outputPlaying)
            {
                BreezeTtsStreamingPcmAdapter adapter = GetStreamingAdapter();
                if (adapter == null || (adapter.State != BreezeTtsStreamingPcmAdapter.PlaybackState.Playing &&
                    adapter.State != BreezeTtsStreamingPcmAdapter.PlaybackState.Draining) ||
                    !string.Equals(adapter.PersonId, currentPersonId, StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(adapter.StreamId) || string.IsNullOrWhiteSpace(adapter.ConversationToken) ||
                    output.clip.frequency <= 0)
                {
                    requestId = null;
                }
                else
                {
                    requestId = adapter.StreamId;
                    conversationToken = adapter.ConversationToken;
                    playbackSample = adapter.PlaybackCursorFrames;
                    streamingClock = true;
                    audioPlaying = true;
                    if (Time.realtimeSinceStartup >= nextStreamEvidencePollTime)
                    {
                        nextStreamEvidencePollTime = Time.realtimeSinceStartup + 0.25f;
                        BreezeTtsTrialBridge.ActiveAudioEvidence finalEvidence;
                        if (bridge.TryReadStreamingFinalAudioEvidence(requestId, conversationToken, currentPersonId,
                            out finalEvidence)) audioEvidence = finalEvidence;
                    }
                }
            }

            source = PortraitField.GetValue(talk) as Texture2D;
            panel = GetPortraitPanel(talk);
            if (source == null || panel == null || source.width != FaceSize || source.height != FaceSize)
            {
                failure = "portrait-prototype-native-face-size-or-panel-mismatch";
                return false;
            }

            failure = null;
            return true;
        }

        private BreezeTtsStreamingPcmAdapter GetStreamingAdapter()
        {
            return bridge == null ? null : bridge.GetComponent<BreezeTtsStreamingPcmAdapter>();
        }

        private void QueueSidecarLoad(BreezeTtsTrialBridge.ActiveAudioEvidence bridgeEvidence)
        {
            if (bridgeEvidence == null || string.IsNullOrWhiteSpace(bridgeEvidence.RequestId) ||
                string.IsNullOrWhiteSpace(bridgeEvidence.WavePath) ||
                !string.Equals(bridgeEvidence.PersonId, PrototypePersonId, StringComparison.Ordinal) ||
                Time.realtimeSinceStartup < nextSidecarRetryTime) return;

            lock (sidecarGate)
            {
                if (sidecarLoadInFlight && string.Equals(sidecarLoadingRequestId, bridgeEvidence.RequestId,
                    StringComparison.Ordinal)) return;
                if (string.Equals(sidecarLoadedRequestId, bridgeEvidence.RequestId, StringComparison.Ordinal)) return;
                if (sidecarLoadInFlight) return;
                sidecarLoadInFlight = true;
                sidecarLoadingRequestId = bridgeEvidence.RequestId;
            }

            var captured = new PortraitVisemeAudioEvidence
            {
                RequestId = bridgeEvidence.RequestId,
                PersonId = bridgeEvidence.PersonId,
                ConversationToken = bridgeEvidence.ConversationToken,
                Text = bridgeEvidence.Text,
                SampleRate = bridgeEvidence.SampleRate,
                AudioFrames = bridgeEvidence.SampleFrames,
                Channels = bridgeEvidence.Channels,
                WavePath = bridgeEvidence.WavePath,
            };
            try
            {
                if (!ThreadPool.QueueUserWorkItem(_ => LoadSidecarWorker(captured)))
                    throw new InvalidOperationException("sidecar-worker-not-queued");
            }
            catch (Exception exception)
            {
                lock (sidecarGate)
                {
                    sidecarLoadInFlight = false;
                    sidecarLoadingRequestId = null;
                }
                nextSidecarRetryTime = Time.realtimeSinceStartup + 1f;
                Diagnostic = "sidecar-load-not-started:" + exception.Message;
            }
        }

        private void LoadSidecarWorker(PortraitVisemeAudioEvidence audio)
        {
            var result = new SidecarLoadResult
            {
                RequestId = audio.RequestId,
                PersonId = audio.PersonId,
                ConversationToken = audio.ConversationToken,
            };
            try
            {
                string wavePath = Path.GetFullPath(audio.WavePath);
                if (!string.Equals(Path.GetFileNameWithoutExtension(wavePath), audio.RequestId,
                    StringComparison.Ordinal)) throw new InvalidDataException("wav-name-does-not-match-request");
                var waveInfo = new FileInfo(wavePath);
                if (!waveInfo.Exists || waveInfo.Length < 44 || waveInfo.Length > MaximumWaveHashBytes)
                    throw new InvalidDataException("wav-size-out-of-bounds");
                string sidecarPath = Path.ChangeExtension(wavePath, ".visemes.json");
                var sidecarInfo = new FileInfo(sidecarPath);
                if (!sidecarInfo.Exists) throw new FileNotFoundException("sidecar-not-ready");
                if (sidecarInfo.Length <= 0 || sidecarInfo.Length > MaximumSidecarBytes)
                    throw new InvalidDataException("sidecar-size-out-of-bounds");

                PortraitVisemeSidecarDocument document = null;
                string sidecarJson;
                using (var reader = new StreamReader(sidecarPath, Encoding.UTF8, true))
                    sidecarJson = reader.ReadToEnd();
                FullSerializer.fsData sidecarData;
                try
                {
                    sidecarData = FullSerializer.fsJsonParser.Parse(sidecarJson);
                }
                catch (Exception)
                {
                    throw new InvalidDataException("sidecar-json-parse-failed");
                }
                if (sidecarData == null || !sidecarData.IsDictionary)
                    throw new InvalidDataException("sidecar-json-root-not-object");
                // Each background worker owns its serializer; never share Unity's JsonUtility or a
                // mutable serializer instance across worker threads. FullSerializer handles nested arrays.
                var serializer = new FullSerializer.fsSerializer();
                FullSerializer.fsResult deserializeResult = serializer.TryDeserialize(sidecarData, ref document);
                if (deserializeResult.Failed)
                    throw new InvalidDataException("sidecar-json-deserialize-failed");
                using (var stream = new FileStream(wavePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (SHA256 algorithm = SHA256.Create())
                {
                    byte[] hash = algorithm.ComputeHash(stream);
                    var text = new StringBuilder(hash.Length * 2);
                    foreach (byte value in hash) text.Append(value.ToString("x2"));
                    audio.AudioSha256 = text.ToString();
                }
                if (!PortraitVisemeSidecarValidator.TryValidate(document, audio, PrototypePersonId,
                    audio.ConversationToken, out result.Timeline, out result.Error))
                    throw new InvalidDataException(result.Error ?? "sidecar-validation-failed");
            }
            catch (Exception exception)
            {
                result.Error = exception is FileNotFoundException || exception is DirectoryNotFoundException
                    ? "sidecar-not-ready"
                    : (exception is InvalidDataException ? exception.Message : exception.GetType().Name);
            }
            lock (sidecarGate)
            {
                completedSidecarLoad = result;
                sidecarLoadInFlight = false;
                sidecarLoadingRequestId = null;
            }
        }

        private void DrainSidecarLoad(string requestId, string personId, string conversationToken)
        {
            SidecarLoadResult result;
            lock (sidecarGate)
            {
                result = completedSidecarLoad;
                completedSidecarLoad = null;
            }
            if (result == null || !string.Equals(result.RequestId, requestId, StringComparison.Ordinal)) return;
            if (string.Equals(result.PersonId, personId, StringComparison.Ordinal) &&
                string.Equals(result.ConversationToken, conversationToken, StringComparison.Ordinal) &&
                result.Timeline != null)
            {
                timeline = result.Timeline;
                timelineRequestId = result.RequestId;
                timelinePersonId = result.PersonId;
                timelineConversationToken = result.ConversationToken;
                sidecarLoadedRequestId = result.RequestId;
                amplitudeFallback.Reset();
                ReportCueSource("rhubarb-recognizer-estimate", requestId);
                Diagnostic = "rhubarb-sidecar-ready";
            }
            else
            {
                sidecarLoadedRequestId = null;
                nextSidecarRetryTime = Time.realtimeSinceStartup + 1f;
                if (!string.IsNullOrEmpty(result.Error) &&
                    !string.Equals(reportedSidecarFailureRequestId, requestId, StringComparison.Ordinal))
                {
                    reportedSidecarFailureRequestId = requestId;
                    Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_PORTRAIT_CUE: sidecar-rejected=" + result.Error +
                        "; request=" + requestId + "; using amplitude-approximation");
                }
            }
        }

        private static Panel GetPortraitPanel(DaggerfallTalkWindow talk)
        {
            return talk == null || PortraitPanelField == null ? null : PortraitPanelField.GetValue(talk) as Panel;
        }

        private bool ComposeSpeechMouth(Texture2D source, PortraitMouthShape shape, out Texture2D result)
        {
            result = null;
            try
            {
                Color32[] basePixels = source.GetPixels32();
                if (basePixels == null || basePixels.Length != FaceSize * FaceSize)
                    return false;
                if (shape == PortraitMouthShape.X || !CopyFeature(atlas, atlasPixels, (int)shape, 3, 3,
                    MouthX, MouthTop, MouthWidth, MouthHeight, basePixels)) return false;

                if (!composedPortrait)
                {
                    composedPortrait = new Texture2D(FaceSize, FaceSize, TextureFormat.ARGB32, false);
                    composedPortrait.filterMode = FilterMode.Point;
                    composedPortrait.wrapMode = TextureWrapMode.Clamp;
                }
                composedPortrait.SetPixels32(basePixels);
                composedPortrait.Apply(false, false);
                result = composedPortrait;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool CopyFeature(Texture2D atlasTexture, Color32[] atlasPixels, int tile, int columns, int rows,
            int left, int top, int width, int height, Color32[] destination)
        {
            if (!atlasTexture || atlasTexture.width != AtlasSize || atlasTexture.height != AtlasSize ||
                atlasPixels == null || atlasPixels.Length != AtlasSize * AtlasSize ||
                destination == null || destination.Length != FaceSize * FaceSize) return false;
            PortraitAtlasCell cell = PortraitAtlasGrid.TopLeftIndexedCell(tile, columns, rows);
            for (int y = 0; y < height; y++)
            {
                int topY = top + y;
                float localV = 1f - (topY + 0.5f) / FaceSize;
                int destinationY = FaceSize - 1 - topY;
                int atlasY = Mathf.Clamp(Mathf.FloorToInt((cell.VMin + cell.VHeight * localV) * AtlasSize), 0, AtlasSize - 1);
                for (int x = 0; x < width; x++)
                {
                    float localU = (left + x + 0.5f) / FaceSize;
                    int atlasX = Mathf.Clamp(Mathf.FloorToInt((cell.UMin + cell.UWidth * localU) * AtlasSize), 0, AtlasSize - 1);
                    destination[destinationY * FaceSize + left + x] = atlasPixels[atlasY * AtlasSize + atlasX];
                }
            }
            return true;
        }

        private void RestoreNativePortrait()
        {
            if (boundTalk != null && composedPortrait)
            {
                Panel panel = GetPortraitPanel(boundTalk);
                if (panel != null && panel.BackgroundTexture == composedPortrait)
                {
                    string currentPersonId = GetCurrentPersonId();
                    int currentFaceIndex = GetCurrentFaceIndex();
                    Texture2D currentPortrait = PortraitField == null ? null : PortraitField.GetValue(boundTalk) as Texture2D;
                    PeopleOfTheIliacBayMod host = GetComponent<PeopleOfTheIliacBayMod>();
                    bool conversationStillBound = host != null && ConversationSessionOpenField != null &&
                        MemoryWindowField != null && (bool)ConversationSessionOpenField.GetValue(host) &&
                        ReferenceEquals(MemoryWindowField.GetValue(host), boundTalk);
                    PortraitRestoreAction action = PortraitVisemeRestorePolicy.Decide(true,
                        conversationStillBound,
                        string.Equals(currentPersonId, boundPersonId, StringComparison.Ordinal),
                        currentFaceIndex == boundFaceIndex, currentPortrait != null);
                    if (action == PortraitRestoreAction.RestoreCurrentPortrait) panel.BackgroundTexture = currentPortrait;
                    else if (action == PortraitRestoreAction.RestoreCachedPortrait) panel.BackgroundTexture = nativePortrait;
                    else if (action == PortraitRestoreAction.ClearPrototypeComposite) panel.BackgroundTexture = null;
                }
            }
            visibleShape = PortraitMouthShape.X;
        }

        private string GetCurrentPersonId()
        {
            PeopleOfTheIliacBayMod host = GetComponent<PeopleOfTheIliacBayMod>();
            return host == null || ConversationPersonIdField == null
                ? null : ConversationPersonIdField.GetValue(host) as string;
        }

        private static int GetCurrentFaceIndex()
        {
            GameManager game = GameManager.Instance;
            return game == null || game.TalkManager == null || game.TalkManager.CurrentNPCType != TalkManager.NPCType.Mobile ||
                !game.TalkManager.MobileNPC ? -1 : game.TalkManager.MobileNPC.PersonFaceRecordId;
        }

        private void RecordDisplayedShape(string requestId, PortraitMouthShape shape)
        {
            int count;
            bool capture;
            if (!previewCueCounter.Record(requestId, shape, out count, out capture)) return;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PORTRAIT_SHAPE: request=" + requestId + "; shape=" + shape +
                "; visibleTransitions=" + count);
            if (!capture || !previewCaptureEnabled) return;
            string key = requestId + "|" + shape;
            if (!capturedShapeKeys.Add(key)) return;
            string safeRequest = SanitizeFileToken(requestId);
            string screenshot = Path.Combine(Application.persistentDataPath,
                "PeopleOfTheIliacBay-viseme-" + safeRequest + "-" + shape + ".png");
            try
            {
                ScreenCapture.CaptureScreenshot(screenshot);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PORTRAIT_SHAPE_SCREENSHOT: " + screenshot);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_PORTRAIT_SHAPE_SCREENSHOT_FAILED: " + exception.GetType().Name);
            }
        }

        private static string SanitizeFileToken(string value)
        {
            if (string.IsNullOrEmpty(value)) return "unknown";
            var safe = new StringBuilder(Math.Min(value.Length, 64));
            for (int index = 0; index < value.Length && safe.Length < 64; index++)
            {
                char c = value[index];
                safe.Append(char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_');
            }
            return safe.Length == 0 ? "unknown" : safe.ToString();
        }

        private void ReportCueSource(string source, string requestId)
        {
            CurrentCueSource = source;
            string key = source + "|" + (requestId ?? string.Empty);
            if (string.Equals(key, reportedSourceKey, StringComparison.Ordinal)) return;
            reportedSourceKey = key;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PORTRAIT_CUE: source=" + source + "; request=" + (requestId ?? "unknown"));
        }

        private void OnDisable()
        {
            RestoreNativePortrait();
        }

        private void OnDestroy()
        {
            RestoreNativePortrait();
            if (composedPortrait) UnityEngine.Object.Destroy(composedPortrait);
            if (atlas) UnityEngine.Object.Destroy(atlas);
            composedPortrait = null;
            atlas = null;
            atlasPixels = null;
        }
    }
}
