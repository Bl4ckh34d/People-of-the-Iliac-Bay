using System;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>
    /// Owns split portrait decoding and one composite texture. TalkWindow retains ownership of
    /// its native portrait texture. The mod owner calls RefreshBindingAndRender once per frame.
    /// </summary>
    public sealed class SplitPortraitRuntimeOverlay : MonoBehaviour
    {
        public const string ManifestAssetName = "portrait-split-manifest.json.bytes";

        private static readonly BindingFlags InstanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo PortraitField = typeof(DaggerfallTalkWindow).GetField("texturePortrait", InstanceNonPublic);
        private static readonly FieldInfo PortraitPanelField = typeof(DaggerfallTalkWindow).GetField("panelPortrait", InstanceNonPublic);

        public sealed class Binding
        {
            public readonly UnityEngine.Object Speaker;
            public readonly DaggerfallTalkWindow BoundTalk;
            public readonly PersonalTradeWindow BoundTrade;
            public readonly int BoundRecord;
            public readonly Texture2D NativeSource;
            public readonly Panel PortraitPanel;
            public readonly int Revision;

            internal Binding(UnityEngine.Object speaker, DaggerfallTalkWindow talk, int record,
                Texture2D source, Panel panel, int revision, PersonalTradeWindow trade=null)
            {
                Speaker = speaker;
                BoundTalk = talk;
                BoundTrade = trade;
                BoundRecord = record;
                NativeSource = source;
                PortraitPanel = panel;
                Revision = revision;
            }
        }

        public sealed class Presentation
        {
            public readonly string EmotionId;
            public readonly SplitPortraitEyePhase EyePhase;
            public readonly PortraitMouthShape MouthShape;
            public readonly string GazeId;

            public Presentation(string emotionId, SplitPortraitEyePhase eyePhase, PortraitMouthShape mouthShape)
                : this(emotionId, eyePhase, mouthShape, null)
            {
            }

            public Presentation(string emotionId, SplitPortraitEyePhase eyePhase, PortraitMouthShape mouthShape,
                string gazeId)
            {
                EmotionId = emotionId;
                EyePhase = eyePhase;
                MouthShape = mouthShape;
                GazeId = gazeId;
            }

            public static readonly Presentation Neutral =
                new Presentation("neutral", SplitPortraitEyePhase.Open, PortraitMouthShape.X);
        }

        private Mod mod;
        private SplitPortraitManifest manifest;
        private string registryError;
        private bool registryLoaded;
        private Binding binding;
        private SplitPortraitFace face;
        private Texture2D composite;
        private readonly byte[] composedRgba = new byte[SplitPortraitManifestValidator.FaceWidth *
            SplitPortraitManifestValidator.FaceHeight * 4];
        private readonly byte[] previousRgba = new byte[SplitPortraitManifestValidator.FaceWidth *
            SplitPortraitManifestValidator.FaceHeight * 4];
        private readonly Color32[] bottomUpColors = new Color32[SplitPortraitManifestValidator.FaceWidth *
            SplitPortraitManifestValidator.FaceHeight];
        private bool hasPreviousRgba;
        private string lastEmotion;
        private string lastGaze;
        private SplitPortraitEyePhase lastEye;
        private PortraitMouthShape lastMouth;
        private int bindingRevision;

        public Binding CurrentBinding { get { return binding; } }
        public UnityEngine.Object Speaker { get { return binding == null ? null : binding.Speaker; } }
        public DaggerfallTalkWindow BoundTalk { get { return binding == null ? null : binding.BoundTalk; } }
        public int BoundRecord { get { return binding == null ? -1 : binding.BoundRecord; } }
        public bool IsBound { get { return binding != null; } }
        public bool HasValidArt { get { return face != null; } }
        public SplitPortraitAnimationPolicy AnimationPolicy
        {
            get { return manifest == null || manifest.animationPolicy == null ? null : manifest.animationPolicy.Copy(); }
        }
        public int BindingRevision { get { return bindingRevision; } }
        public int TextureUploadCount { get; private set; }
        public string Diagnostic { get; private set; }

        public bool HasCompleteGazePairForEmotion(string emotionId)
        {
            return face != null && face.HasCompleteGazePairForEmotion(emotionId);
        }

        /// <summary>Install on the mod-owned host. Missing or invalid registry leaves native portraits active.</summary>
        public static SplitPortraitRuntimeOverlay TryInstall(Mod mod, GameObject host,
            out SplitPortraitRuntimeOverlay overlay, out string result)
        {
            overlay = null;
            result = null;
            if (mod == null || !host)
            {
                result = "split-portrait-runtime-missing-mod-or-host";
                return null;
            }

            overlay = host.GetComponent<SplitPortraitRuntimeOverlay>();
            if (!overlay) overlay = host.AddComponent<SplitPortraitRuntimeOverlay>();
            overlay.mod = mod;
            overlay.LoadRegistry();
            result = overlay.registryError == null ? "split-portrait-runtime-installed" :
                "split-portrait-runtime-installed-native-fallback:" + overlay.registryError;
            return overlay;
        }

        /// <summary>
        /// Resolves current native talk ownership, then asks caller for presentation for that exact binding.
        /// Call once per frame after actor state is available. Null callback or result selects neutral/open/X.
        /// </summary>
        public void RefreshBindingAndRender(Func<Binding, Presentation> presentationProvider)
        {
            if (!isActiveAndEnabled)
            {
                DetachAndRestore();
                return;
            }

            Binding current;
            if (!TryReadCurrentBinding(out current))
            {
                DetachAndRestore();
                Diagnostic = "split-portrait-runtime-no-active-mobile-talk-window";
                return;
            }

            if (!SameBinding(binding, current))
            {
                // Talk-to-trade transfers the same native portrait. Keep decoded
                // animation frames instead of loading the complete face again.
                var reusableFace=face!=null && binding!=null && binding.BoundRecord==current.BoundRecord &&
                    ReferenceEquals(binding.NativeSource,current.NativeSource)?face:null;
                RestoreCurrentBinding();
                bindingRevision++;
                binding = current;
                face = reusableFace;
                lastEmotion = null;
                lastGaze = null;
                Diagnostic = registryError == null ? "split-portrait-runtime-binding-changed" : registryError;
                if(face==null)TryBuildFace(binding);
            }

            if (binding == null) return;
            if (face == null)
            {
                RestoreNativePortrait(binding);
                return;
            }
            Presentation presentation = null;
            if (presentationProvider != null)
            {
                try { presentation = presentationProvider(binding); }
                catch (Exception) { Diagnostic = "split-portrait-runtime-presentation-provider-failed"; }
            }
            if (presentation == null) presentation = Presentation.Neutral;

            Render(presentation);
        }

        /// <summary>Restore native image before any owned texture is destroyed.</summary>
        public void DetachAndRestore()
        {
            RestoreCurrentBinding();
            hasPreviousRgba = false;
            DestroyComposite();
        }

        private void RestoreCurrentBinding()
        {
            if (binding != null) RestoreNativePortrait(binding);
            binding = null;
            face = null;
            lastEmotion = null;
            lastGaze = null;
        }

        private void LoadRegistry()
        {
            if (registryLoaded) return;
            registryLoaded = true;
            try
            {
                TextAsset asset = mod == null ? null : mod.GetAsset<TextAsset>(ManifestAssetName);
                if (!asset) throw new InvalidOperationException("manifest-missing");
                FullSerializer.fsData data = FullSerializer.fsJsonParser.Parse(asset.text);
                if (data == null || !data.IsDictionary) throw new InvalidOperationException("manifest-root-invalid");
                var serializer = new FullSerializer.fsSerializer();
                FullSerializer.fsResult deserialize = serializer.TryDeserialize(data, ref manifest);
                if (deserialize.Failed) throw new InvalidOperationException("manifest-deserialize-failed");
                string error;
                if (!SplitPortraitManifestValidator.TryValidateStructure(manifest, out error))
                    throw new InvalidOperationException(error ?? "manifest-structure-invalid");
                registryError = null;
            }
            catch (Exception exception)
            {
                manifest = null;
                registryError = exception is InvalidOperationException ? exception.Message :
                    "manifest-load-failed:" + exception.GetType().Name;
            }
        }

        private bool TryBuildFace(Binding current)
        {
            if (manifest == null || current == null || current.NativeSource == null ||
                current.NativeSource.width != SplitPortraitManifestValidator.FaceWidth ||
                current.NativeSource.height != SplitPortraitManifestValidator.FaceHeight)
            {
                Diagnostic = registryError ?? "native-portrait-dimensions-invalid";
                return false;
            }

            try
            {
                Color32[] nativePixels = current.NativeSource.GetPixels32();
                byte[] nativeTopDown = ToTopDownRgba(nativePixels, SplitPortraitManifestValidator.FaceWidth,
                    SplitPortraitManifestValidator.FaceHeight);
                var selectedAssets = new List<SplitPortraitDecodedRgbaAsset>(108);
                var decodedTextures = new List<Texture2D>(108);
                try
                {
                    SplitPortraitEntry entry = FindEntry(manifest, current.BoundRecord);
                    if (entry == null)
                    {
                        Diagnostic = "portrait-registry-entry-missing";
                        return false;
                    }

                    foreach (SplitPortraitEmotion emotion in entry.emotions)
                    {
                        DecodeFrames(emotion.eyes, selectedAssets, decodedTextures);
                        DecodeFrames(emotion.mouths, selectedAssets, decodedTextures);
                        if (emotion.gazes != null)
                            foreach (SplitPortraitGaze gaze in emotion.gazes)
                                DecodeFrames(gaze.eyes, selectedAssets, decodedTextures);
                    }

                    string error;
                    var selectedManifest = new SplitPortraitManifest
                    {
                        schemaVersion = manifest.schemaVersion,
                        animationPolicy = manifest.animationPolicy,
                        animationPolicySha256 = manifest.animationPolicySha256,
                        portraits = new[] { entry },
                    };
                    if (!SplitPortraitComposition.TryCreateFace(selectedManifest, "CommonFaces", "TFAC00I0.RCI",
                        current.BoundRecord, nativeTopDown, selectedAssets, out face, out error))
                    {
                        Diagnostic = error ?? "portrait-face-invalid";
                        face = null;
                        return false;
                    }
                    Diagnostic = "split-portrait-runtime-face-ready";
                    return true;
                }
                finally
                {
                    for (int index = 0; index < decodedTextures.Count; index++)
                        if (decodedTextures[index]) UnityEngine.Object.Destroy(decodedTextures[index]);
                }
            }
            catch (Exception exception)
            {
                face = null;
                Diagnostic = "split-portrait-runtime-face-load-failed:" + exception.GetType().Name;
                return false;
            }
        }

        private void DecodeFrames(SplitPortraitAsset[] frames,
            List<SplitPortraitDecodedRgbaAsset> decoded, List<Texture2D> textures)
        {
            foreach (SplitPortraitAsset frame in frames)
            {
                TextAsset encoded = mod.GetAsset<TextAsset>(frame.assetName);
                if (!encoded || encoded.bytes == null || encoded.bytes.Length == 0)
                    throw new InvalidOperationException("portrait-asset-missing:" + frame.assetName);

                var texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                textures.Add(texture);
                if (!texture.LoadImage(encoded.bytes, false) || texture.width != frame.width ||
                    texture.height != frame.height)
                    throw new InvalidOperationException("portrait-asset-decode-invalid:" + frame.assetName);
                Color32[] pixels = texture.GetPixels32();
                byte[] rgba = ToTopDownRgba(pixels, texture.width, texture.height);
                decoded.Add(new SplitPortraitDecodedRgbaAsset
                {
                    assetName = frame.assetName,
                    width = texture.width,
                    height = texture.height,
                    encodedBytes = encoded.bytes,
                    rgba = rgba,
                });
            }
        }

        private void Render(Presentation requested)
        {
            if (requested.EyePhase == lastEye && requested.MouthShape == lastMouth &&
                string.Equals(requested.EmotionId, lastEmotion, StringComparison.Ordinal) &&
                string.Equals(requested.GazeId, lastGaze, StringComparison.Ordinal))
            {
                AssignComposite();
                return;
            }

            bool neutralFallback;
            string error;
            if (!face.TryCompose(requested.EmotionId, requested.GazeId, requested.EyePhase, requested.MouthShape,
                composedRgba, out neutralFallback, out error))
            {
                Diagnostic = error ?? "portrait-compose-failed";
                RestoreNativePortrait(binding);
                return;
            }

            bool changed = !hasPreviousRgba || !BytesEqual(composedRgba, previousRgba);
            if (changed)
            {
                EnsureComposite();
                FillBottomUpColor32(composedRgba, bottomUpColors,
                    SplitPortraitManifestValidator.FaceWidth, SplitPortraitManifestValidator.FaceHeight);
                composite.SetPixels32(bottomUpColors);
                composite.Apply(false, false);
                TextureUploadCount++;
                Buffer.BlockCopy(composedRgba, 0, previousRgba, 0, composedRgba.Length);
                hasPreviousRgba = true;
            }

            lastEmotion = requested.EmotionId;
            lastGaze = requested.GazeId;
            lastEye = requested.EyePhase;
            lastMouth = requested.MouthShape;
            AssignComposite();
            Diagnostic = neutralFallback ? "split-portrait-runtime-neutral-fallback" : "split-portrait-runtime-rendered";
        }

        private void EnsureComposite()
        {
            if (composite) return;
            composite = new Texture2D(SplitPortraitManifestValidator.FaceWidth,
                SplitPortraitManifestValidator.FaceHeight, TextureFormat.ARGB32, false);
            composite.name = "PeopleOfTheIliacBay-SplitPortraitComposite";
            composite.filterMode = FilterMode.Point;
            composite.wrapMode = TextureWrapMode.Clamp;
        }

        private void AssignComposite()
        {
            if (binding == null || binding.PortraitPanel == null || !composite) return;
            if (binding.PortraitPanel.BackgroundTexture != composite)
                binding.PortraitPanel.BackgroundTexture = composite;
        }

        private void RestoreNativePortrait(Binding previous)
        {
            if (previous == null || previous.PortraitPanel == null || !composite) return;
            if (previous.PortraitPanel.BackgroundTexture != composite) return;
            Texture2D currentNative = previous.BoundTrade!=null?previous.NativeSource:GetNativePortrait(previous.BoundTalk);
            if (currentNative) previous.PortraitPanel.BackgroundTexture = currentNative;
        }

        private void DestroyComposite()
        {
            if (composite) UnityEngine.Object.Destroy(composite);
            composite = null;
        }

        private bool TryReadCurrentBinding(out Binding current)
        {
            current = null;
            var trade=PersonalTradeWindow.Active;
            if(trade!=null && trade.IsSetup && trade.IsPortraitVisible && trade.Portrait!=null && trade.PortraitPanel!=null)
            {
                var portrait=trade.Portrait;
                if(!portrait.Speaker || !portrait.NativeSource)return false;
                current=binding!=null && binding.BoundTrade==trade?binding:
                    new Binding(portrait.Speaker,null,portrait.Record,portrait.NativeSource,trade.PortraitPanel,bindingRevision+1,trade);
                return true;
            }
            if (PortraitField == null || PortraitPanelField == null || DaggerfallUI.UIManager == null)
            {
                Diagnostic = "split-portrait-runtime-native-fields-unavailable";
                return false;
            }

            DaggerfallTalkWindow talk = DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
            GameManager game = GameManager.Instance;
            if (talk == null || !talk.IsSetup || game == null || game.TalkManager == null ||
                game.TalkManager.CurrentNPCType != TalkManager.NPCType.Mobile || !game.TalkManager.MobileNPC)
                return false;

            MobilePersonNPC speaker = game.TalkManager.MobileNPC;
            Texture2D source = GetNativePortrait(talk);
            Panel panel = PortraitPanelField.GetValue(talk) as Panel;
            int record = speaker.PersonFaceRecordId;
            if (record < 0 || !source || panel == null) return false;
            if (SameBinding(binding, speaker, talk, record, source, panel))
            {
                current = binding;
                return true;
            }
            current = new Binding(speaker, talk, record, source, panel, bindingRevision + 1);
            return true;
        }

        private static Texture2D GetNativePortrait(DaggerfallTalkWindow talk)
        {
            return talk == null || PortraitField == null ? null : PortraitField.GetValue(talk) as Texture2D;
        }

        public static PersonalTradePortrait CaptureTradePortrait(DaggerfallTalkWindow talk,string startingEmotion)
        {
            var manager=GameManager.Instance==null?null:GameManager.Instance.TalkManager;
            var source=GetNativePortrait(talk);
            if(manager==null || !source)return null;
            var mobile=manager.CurrentNPCType==TalkManager.NPCType.Mobile?manager.MobileNPC:null;
            UnityEngine.Object speaker=mobile?(UnityEngine.Object)mobile:manager.StaticNPC;
            if(!speaker)return null;
            return new PersonalTradePortrait {Speaker=speaker,NativeSource=source,Record=mobile?mobile.PersonFaceRecordId:-1,
                StartingEmotion=startingEmotion??"neutral"};
        }

        private static bool SameBinding(Binding left, Binding right)
        {
            return right != null && SameBinding(left, right.Speaker, right.BoundTalk, right.BoundRecord,
                right.NativeSource, right.PortraitPanel);
        }

        private static bool SameBinding(Binding left, UnityEngine.Object speaker, DaggerfallTalkWindow talk,
            int record, Texture2D source, Panel panel)
        {
            return left != null && ReferenceEquals(left.Speaker, speaker) &&
                ReferenceEquals(left.BoundTalk, talk) && left.BoundRecord == record &&
                ReferenceEquals(left.NativeSource, source) && ReferenceEquals(left.PortraitPanel, panel);
        }

        private static SplitPortraitEntry FindEntry(SplitPortraitManifest registry, int record)
        {
            foreach (SplitPortraitEntry entry in registry.portraits)
                if (entry.record == record && string.Equals(entry.archive, "CommonFaces", StringComparison.Ordinal) &&
                    string.Equals(entry.bank, "TFAC00I0.RCI", StringComparison.OrdinalIgnoreCase)) return entry;
            return null;
        }

        private static byte[] ToTopDownRgba(Color32[] source, int width, int height)
        {
            if (source == null || source.Length != width * height) throw new InvalidOperationException("portrait-pixels-invalid");
            byte[] result = new byte[source.Length * 4];
            for (int y = 0; y < height; y++)
            {
                int sourceRow = (height - 1 - y) * width;
                int destinationRow = y * width;
                for (int x = 0; x < width; x++)
                {
                    Color32 color = source[sourceRow + x];
                    int offset = (destinationRow + x) * 4;
                    result[offset] = color.r;
                    result[offset + 1] = color.g;
                    result[offset + 2] = color.b;
                    result[offset + 3] = color.a;
                }
            }
            return result;
        }

        private static void FillBottomUpColor32(byte[] topDown, Color32[] destination, int width, int height)
        {
            if (topDown == null || topDown.Length != width * height * 4 ||
                destination == null || destination.Length != width * height)
                throw new InvalidOperationException("portrait-upload-buffer-invalid");
            for (int y = 0; y < height; y++)
            {
                int destinationRow = (height - 1 - y) * width;
                int sourceRow = y * width;
                for (int x = 0; x < width; x++)
                {
                    int offset = (sourceRow + x) * 4;
                    destination[destinationRow + x] = new Color32(topDown[offset], topDown[offset + 1],
                        topDown[offset + 2], topDown[offset + 3]);
                }
            }
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            for (int index = 0; index < left.Length; index++)
                if (left[index] != right[index]) return false;
            return true;
        }

        private void OnDisable()
        {
            DetachAndRestore();
        }

        private void OnDestroy()
        {
            DetachAndRestore();
        }
    }
}
