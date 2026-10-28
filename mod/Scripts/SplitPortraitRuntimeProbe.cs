using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Game.UserInterfaceWindows;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>Bounded native TalkWindow acceptance probe for the registered CommonFaces pilot.</summary>
    internal static class SplitPortraitRuntimeProbe
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        internal const string RunFlag = "-peopleoftheiliacbay-split-portrait-probe";
        private const string OwnedCharacter = "Hakon Ironhand";
        private const string BaselineSave = "People - Gothway Garden";
        private const int PilotRecord = 77;
        private const int MissingRecord = 71;
        private static readonly string[] EmotionIds = { "neutral", "happy", "angry", "sad", "afraid", "guarded" };

        internal static IEnumerator Run(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            string character, string fixtureSave)
        {
            Require(Array.IndexOf(Environment.GetCommandLineArgs(), RunFlag) >= 0 &&
                character == OwnedCharacter && fixtureSave == BaselineSave,
                "Split portrait probe requires its owned disposable character, baseline save, and explicit flag.");
            Require(host && streets && streets.Ready && GameManager.Instance && GameManager.Instance.IsReady,
                "Split portrait probe requires a ready controlled town fixture.");

            GameManager game = GameManager.Instance;
            Require(DaggerfallUI.UIManager != null && DaggerfallUI.Instance != null &&
                DaggerfallUI.UIManager.TopWindow == DaggerfallUI.Instance.DaggerfallHUD,
                "Split portrait probe requires HUD with no open talk window.");
            Require(game.SaveLoadManager.FindSaveFolderByNames(character, fixtureSave) >= 0,
                "Split portrait probe baseline save is missing from the owned fixture character.");

            Mod mod = Read<Mod>(host, "mod");
            Require(mod != null, "Split portrait probe cannot resolve the loaded mod assets.");

            FieldInfo animatedField = RequireHostField("animatedPortraitsEnabled");
            MethodInfo refreshRuntime = typeof(PeopleOfTheIliacBayMod).GetMethod("RefreshSplitPortraitRuntime", PrivateInstance);
            Require(refreshRuntime != null, "Split portrait production install method is unavailable.");
            bool oldAnimated = (bool)animatedField.GetValue(host);
            SplitPortraitRuntimeOverlay overlay = Read<SplitPortraitRuntimeOverlay>(host, "splitPortraitOverlay");

            FieldInfo ownsRenderingField = RequireHostField("splitPortraitProbeOwnsRendering");
            FieldInfo lastPresentationField = RequireHostField("lastPortraitPresentation");
            FieldInfo localDialogueField = RequireHostField("localDialogueEnabled");
            FieldInfo prewarmField = RequireHostField("dialoguePrewarmEnabled");
            FieldInfo voiceField = RequireHostField("npcVoiceEnabled");
            FieldInfo streamingField = RequireHostField("socialSentenceStreamingEnabled");
            bool oldOwnsRendering = (bool)ownsRenderingField.GetValue(host);
            bool oldLocalDialogue = (bool)localDialogueField.GetValue(host);
            bool oldPrewarm = (bool)prewarmField.GetValue(host);
            bool oldVoice = (bool)voiceField.GetValue(host);
            bool oldStreaming = (bool)streamingField.GetValue(host);
            bool oldOverlayEnabled = !overlay || overlay.enabled;
            object oldVoiceBridge = ReadObject(host, "breezeTtsTrial");
            BreezeTtsTrialBridge activeBridge = oldVoiceBridge as BreezeTtsTrialBridge;
            Require(activeBridge == null || (!activeBridge.HasPendingSpeech &&
                (!activeBridge.OutputAudioSource || !activeBridge.OutputAudioSource.isPlaying)),
                "Split portrait probe refuses to run while native TTS playback or queued speech is active.");
            Vector3 oldPlayerPosition = game.PlayerMotor.transform.position;
            DaggerfallTalkWindow originalTopTalk = DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
            Require(originalTopTalk == null, "Split portrait probe refuses to replace an already open TalkWindow.");

            ResidentActor first = null;
            ResidentActor second = null;
            yield return ConversationMoodProbe.WaitForUnusedManagedAdult(host,
                game.PlayerMotor.transform.position, value => first = value, false);
            Require(first && first.Person && first.Resident != null && first.Resident.IsAlive,
                "Split portrait probe needs a live eligible first mobile resident.");
            yield return ConversationMoodProbe.WaitForUnusedManagedAdult(host,
                game.PlayerMotor.transform.position, value => second = value, false, first.Resident.Id);
            Require(second && second.Person && second.Resident != null && second.Resident.IsAlive &&
                second.Person != first.Person,
                "Split portrait probe needs a second live eligible mobile resident for same-face switch checks.");

            int firstFaceBefore = first.Person.PersonFaceRecordId;
            int secondFaceBefore = second.Person.PersonFaceRecordId;
            bool completed = false;
            int combinationsCompared = 0;
            try
            {
                if (!oldAnimated)
                {
                    animatedField.SetValue(host, true);
                    refreshRuntime.Invoke(host, null);
                }
                overlay = Read<SplitPortraitRuntimeOverlay>(host, "splitPortraitOverlay");
                Require(overlay, "Split portrait runtime overlay is not installed by the parent host.");
                ownsRenderingField.SetValue(host, true);
                localDialogueField.SetValue(host, false);
                prewarmField.SetValue(host, false);
                voiceField.SetValue(host, false);
                streamingField.SetValue(host, false);
                SetHostField(host, "breezeTtsTrial", null);
                overlay.enabled = true;
                first.Person.PersonFaceRecordId = PilotRecord;
                second.Person.PersonFaceRecordId = PilotRecord;

                // Force native SetTargetNPC for first even if fixture manager retained it as last target.
                yield return OpenTalk(second, streets, game);
                ActiveTalkFor(second.Person);
                DaggerfallUI.Instance.PopToHUD();
                yield return OpenTalk(first, streets, game);
                DaggerfallTalkWindow talk = ActiveTalkFor(first.Person);
                int firstRevision = BindAndVerify(overlay, first.Person, talk, PilotRecord);
                SplitPortraitFace expectedFirst = BuildExpectedFace(mod, talk, PilotRecord);
                byte[] firstNativePixels = CaptureNativeRgba(overlay.CurrentBinding.NativeSource);
                combinationsCompared += VerifyAllOutputs(overlay, first.Person, talk, PilotRecord, expectedFirst);
                VerifyNeutralNullProvider(overlay, first.Person, talk, expectedFirst);
                VerifyUnchangedOutputDoesNotUpload(overlay, first.Person, talk,
                    new SplitPortraitRuntimeOverlay.Presentation("neutral", SplitPortraitEyePhase.Open,
                        PortraitMouthShape.X));
                Texture2D firstNative = overlay.CurrentBinding.NativeSource;
                VerifySourceIsUntouched(talk, firstNative, firstNativePixels);

                Texture2D ownedComposite = GetPortraitPanel(talk).BackgroundTexture;
                Require(ownedComposite && ownedComposite != firstNative,
                    "Overlay did not own the panel composite before disable.");
                overlay.enabled = false;
                yield return null;
                Require(!ownedComposite && GetNativePortrait(talk) == firstNative,
                    "Disable did not destroy the owned composite while retaining native source.");
                Require(GetPortraitPanel(talk).BackgroundTexture == GetNativePortrait(talk) && GetNativePortrait(talk),
                    "Disabling split overlay did not restore the current native portrait before release.");
                VerifySourceIsUntouched(talk, firstNative, firstNativePixels);
                overlay.enabled = true;
                BindAndVerify(overlay, first.Person, talk, PilotRecord);
                Require(GetNativePortrait(talk), "Native portrait source was destroyed during overlay re-enable.");

                DaggerfallUI.Instance.PopToHUD();
                yield return OpenTalk(second, streets, game);
                talk = ActiveTalkFor(second.Person);
                int secondRevision = BindAndVerify(overlay, second.Person, talk, PilotRecord);
                Require(secondRevision > firstRevision && overlay.BoundRecord == PilotRecord,
                    "Same-face actor switch did not advance binding revision for the new native speaker.");
                SplitPortraitFace expectedSecond = BuildExpectedFace(mod, talk, PilotRecord);
                Texture2D secondNative = overlay.CurrentBinding.NativeSource;
                byte[] secondNativePixels = CaptureNativeRgba(secondNative);
                combinationsCompared += VerifyAllOutputs(overlay, second.Person, talk, PilotRecord, expectedSecond);
                VerifySourceIsUntouched(talk, secondNative, secondNativePixels);

                DaggerfallUI.Instance.PopToHUD();
                yield return OpenTalk(first, streets, game);
                talk = ActiveTalkFor(first.Person);
                BindAndVerify(overlay, first.Person, talk, PilotRecord);

                DaggerfallUI.Instance.PopToHUD();
                second.Person.PersonFaceRecordId = MissingRecord;
                yield return OpenTalk(second, streets, game);
                talk = ActiveTalkFor(second.Person);
                overlay.RefreshBindingAndRender(_ => SplitPortraitRuntimeOverlay.Presentation.Neutral);
                Texture2D missingNative = overlay.CurrentBinding.NativeSource;
                byte[] missingNativePixels = CaptureNativeRgba(missingNative);
                Require(overlay.IsBound && overlay.Speaker == second.Person && overlay.BoundRecord == MissingRecord &&
                    !overlay.HasValidArt && GetNativePortrait(talk) &&
                    GetPortraitPanel(talk).BackgroundTexture == GetNativePortrait(talk),
                    "Unregistered record 71 did not retain its native portrait fallback.");
                VerifySourceIsUntouched(talk, missingNative, missingNativePixels);

                // Original regression faces plus all twelve newly completed portraits,
                // through native Talk identity, Unity decode and the visible panel.
                var galleryRecords=new List<int>();
                for(int record=72;record<=91;record++)galleryRecords.Add(record);
                galleryRecords.AddRange(new[]{12,13,14,46,47,195,196,197,220,236,425,452});
                foreach(int record in galleryRecords)
                {
                    DaggerfallUI.Instance.PopToHUD();
                    // Native TalkManager retains the previous target's portrait on reopen.
                    // Switch actors before changing this fixture's face to force native refresh.
                    yield return OpenTalk(first, streets, game);
                    DaggerfallUI.Instance.PopToHUD();
                    second.Person.PersonFaceRecordId = record;
                    yield return OpenTalk(second, streets, game);
                    talk = ActiveTalkFor(second.Person);
                    BindAndVerify(overlay, second.Person, talk, record);
                    SplitPortraitFace expectedCatalog = BuildExpectedFace(mod, talk, record);
                    Texture2D catalogNative = overlay.CurrentBinding.NativeSource;
                    byte[] catalogPixels = CaptureNativeRgba(catalogNative);
                    int compared = VerifyAllOutputs(overlay, second.Person, talk, record, expectedCatalog);
                    combinationsCompared += compared;
                    VerifySourceIsUntouched(talk, catalogNative, catalogPixels);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GALLERY_PORTRAIT_PASS: record=" + record +
                        "; combinations=" + compared + "; nativeSourceRetained=true.");
                    if (record == 13 || record == 236)
                    {
                        overlay.RefreshBindingAndRender(_ => new SplitPortraitRuntimeOverlay.Presentation(
                            "neutral", SplitPortraitEyePhase.Open, PortraitMouthShape.X, "left"));
                        yield return new WaitForEndOfFrame();
                        string screenshotPath = System.IO.Path.Combine(Application.persistentDataPath,
                            "portrait-gallery-native-" + record + ".png");
                        ScreenCapture.CaptureScreenshot(screenshotPath);
                        yield return null;
                    }
                }

                DaggerfallUI.Instance.PopToHUD();
                yield return OpenTalk(first, streets, game);
                talk = ActiveTalkFor(first.Person);
                BindAndVerify(overlay, first.Person, talk, PilotRecord);
                SplitPortraitFace expectedReturn = BuildExpectedFace(mod, talk, PilotRecord);
                VerifyAllOutputs(overlay, first.Person, talk, PilotRecord, expectedReturn);
                VerifySourceIsUntouched(talk, overlay.CurrentBinding.NativeSource,
                    CaptureNativeRgba(overlay.CurrentBinding.NativeSource));

                // Give the production LateUpdate provider exclusive control for one ordinary idle blink.
                // No model, prewarm, or TTS path is enabled while this real clock/render path runs.
                int idleStartUploads = overlay.TextureUploadCount;
                Texture2D idleNative = overlay.CurrentBinding.NativeSource;
                byte[] idleNativePixels = CaptureNativeRgba(idleNative);
                var observedIdlePhases = new HashSet<SplitPortraitEyePhase>();
                ownsRenderingField.SetValue(host, false);
                float idleDeadline = Time.realtimeSinceStartup + 8.25f;
                while (Time.realtimeSinceStartup < idleDeadline)
                {
                    Require(DaggerfallUI.UIManager.TopWindow == talk && talk.IsSetup &&
                        GameManager.Instance.TalkManager.MobileNPC == first.Person && overlay.enabled,
                        "Idle blink acceptance lost its native TalkWindow owner.");
                    Require(GetNativePortrait(talk) == overlay.CurrentBinding.NativeSource,
                        "Idle blink acceptance changed the native source texture.");
                    yield return null;
                    SplitPortraitRuntimeOverlay.Presentation sampled =
                        lastPresentationField.GetValue(host) as SplitPortraitRuntimeOverlay.Presentation;
                    if (sampled != null) observedIdlePhases.Add(sampled.EyePhase);
                }
                ownsRenderingField.SetValue(host, true);
                Require(overlay.TextureUploadCount > idleStartUploads,
                    "Production idle presentation clock produced no blink texture upload within its bounded interval.");
                Require(observedIdlePhases.Contains(SplitPortraitEyePhase.Open) &&
                    observedIdlePhases.Contains(SplitPortraitEyePhase.Half) &&
                    observedIdlePhases.Contains(SplitPortraitEyePhase.Closed),
                    "Production idle presentation clock did not sample open, half, and closed eye phases.");
                VerifySourceIsUntouched(talk, idleNative, idleNativePixels);

                DaggerfallUI.Instance.PopToHUD();
                second.Person.PersonFaceRecordId = PilotRecord;
                yield return OpenTalk(second, streets, game);
                talk = ActiveTalkFor(second.Person);
                BindAndVerify(overlay, second.Person, talk, PilotRecord);
                VerifySourceIsUntouched(talk, overlay.CurrentBinding.NativeSource,
                    CaptureNativeRgba(overlay.CurrentBinding.NativeSource));

                completed = true;
            }
            finally
            {
                try
                {
                    if (overlay) overlay.DetachAndRestore();
                    if (DaggerfallUI.UIManager != null && DaggerfallUI.UIManager.TopWindow is DaggerfallTalkWindow)
                        DaggerfallUI.Instance.PopToHUD();
                }
                finally
                {
                    if (first && first.Person) first.Person.PersonFaceRecordId = firstFaceBefore;
                    if (second && second.Person) second.Person.PersonFaceRecordId = secondFaceBefore;
                    if (game && game.PlayerMotor) game.PlayerMotor.transform.position = oldPlayerPosition;
                    localDialogueField.SetValue(host, oldLocalDialogue);
                    prewarmField.SetValue(host, oldPrewarm);
                    voiceField.SetValue(host, oldVoice);
                    streamingField.SetValue(host, oldStreaming);
                    SetHostField(host, "breezeTtsTrial", oldVoiceBridge);
                    animatedField.SetValue(host, oldAnimated);
                    refreshRuntime.Invoke(host, null);
                    if (overlay) overlay.enabled = oldOverlayEnabled;
                    ownsRenderingField.SetValue(host, oldOwnsRendering);
                    if (game && game.PlayerMotor) game.PlayerMotor.transform.position = oldPlayerPosition;
                    Require(DaggerfallUI.UIManager.TopWindow == DaggerfallUI.Instance.DaggerfallHUD,
                        "Split portrait probe did not leave its TalkWindow closed.");
                    if (!completed)
                        Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_SPLIT_PORTRAIT_ABORTED: fixture restored; acceptance incomplete.");
                }
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_SPLIT_PORTRAIT_PASS: registry=loaded; selected=CommonFaces/TFAC00I0.RCI/77; " +
                "outputCombinations=" + combinationsCompared + "; actorSwitch=same-face-revision-advanced; " +
                "galleryRecords=32; gazes=center,left,right; missing71=native-fallback; return77=valid; disableRestore=true; sourceRetained=true; " +
                "idleNoModelBlink=true; idleEyePhases=3; llm=false; d3=false; prewarm=false; voice=false; streaming=false; uploads=" +
                overlay.TextureUploadCount + "; revision=" + overlay.BindingRevision + ".");
        }

        private static IEnumerator OpenTalk(ResidentActor actor, StreetPopulation streets, GameManager game)
        {
            ConversationMoodProbe.StagePlayerNearActor(streets, actor, game);
            DaggerfallUI.Instance.PopToHUD();
            game.TalkManager.TalkToMobileNPC(actor.Person);
            float deadline = Time.realtimeSinceStartup + 25f;
            while (Time.realtimeSinceStartup < deadline)
            {
                DaggerfallTalkWindow talk = DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
                if (talk != null && talk.IsSetup && game.TalkManager.CurrentNPCType == TalkManager.NPCType.Mobile &&
                    game.TalkManager.MobileNPC == actor.Person && GetNativePortrait(talk))
                    yield break;
                yield return null;
            }
            throw new InvalidOperationException("Native TalkManager did not open a ready mobile TalkWindow for probe actor.");
        }

        private static DaggerfallTalkWindow ActiveTalkFor(MobilePersonNPC person)
        {
            DaggerfallTalkWindow talk = DaggerfallUI.UIManager.TopWindow as DaggerfallTalkWindow;
            Require(talk != null && talk.IsSetup && GameManager.Instance.TalkManager.MobileNPC == person &&
                GameManager.Instance.TalkManager.CurrentNPCType == TalkManager.NPCType.Mobile,
                "Native TalkManager binding does not match the probe speaker.");
            return talk;
        }

        private static int BindAndVerify(SplitPortraitRuntimeOverlay overlay, MobilePersonNPC person,
            DaggerfallTalkWindow talk, int record)
        {
            overlay.RefreshBindingAndRender(_ => SplitPortraitRuntimeOverlay.Presentation.Neutral);
            Require(overlay.IsBound && overlay.HasValidArt && overlay.Speaker == person && overlay.BoundTalk == talk &&
                overlay.BoundRecord == record && GetPortraitPanel(talk).BackgroundTexture != null,
                    "Registered portrait did not bind through the production TalkManager/TalkWindow adapter: expected=" +
                    record + "; actual=" + overlay.BoundRecord + "; diagnostic=" + overlay.Diagnostic + ".");
            return overlay.BindingRevision;
        }

        private static int VerifyAllOutputs(SplitPortraitRuntimeOverlay overlay, MobilePersonNPC person,
            DaggerfallTalkWindow talk, int record, SplitPortraitFace expected)
        {
            int count = 0;
            foreach (string emotion in EmotionIds)
            {
                foreach (string gaze in new string[] { null, "left", "right" })
                {
                foreach (SplitPortraitEyePhase eye in new[]
                    { SplitPortraitEyePhase.Open, SplitPortraitEyePhase.Half, SplitPortraitEyePhase.Closed })
                {
                    foreach (PortraitMouthShape mouth in new[]
                        { PortraitMouthShape.A, PortraitMouthShape.B, PortraitMouthShape.C, PortraitMouthShape.D,
                          PortraitMouthShape.E, PortraitMouthShape.F, PortraitMouthShape.G, PortraitMouthShape.H,
                          PortraitMouthShape.X })
                    {
                        var presentation = new SplitPortraitRuntimeOverlay.Presentation(emotion, eye, mouth, gaze);
                        bool bindingMatched = false;
                        overlay.RefreshBindingAndRender(binding =>
                        {
                            bindingMatched = ReferenceEquals(binding.Speaker, person) &&
                                ReferenceEquals(binding.BoundTalk, talk) && binding.BoundRecord == record;
                            return presentation;
                        });
                        Require(bindingMatched, "Presentation callback received stale or wrong speaker binding.");
                        AssertCompositeEquals(expected, presentation, talk);
                        count++;
                    }
                }
                }
            }
            return count;
        }

        private static void VerifyNeutralNullProvider(SplitPortraitRuntimeOverlay overlay,
            MobilePersonNPC person, DaggerfallTalkWindow talk, SplitPortraitFace expected)
        {
            overlay.RefreshBindingAndRender(_ => null);
            AssertCompositeEquals(expected, SplitPortraitRuntimeOverlay.Presentation.Neutral, talk);
            Require(ReferenceEquals(overlay.Speaker, person), "Null presentation changed active speaker binding.");
        }

        private static void VerifyUnchangedOutputDoesNotUpload(SplitPortraitRuntimeOverlay overlay,
            MobilePersonNPC person, DaggerfallTalkWindow talk, SplitPortraitRuntimeOverlay.Presentation presentation)
        {
            overlay.RefreshBindingAndRender(_ => presentation);
            int uploads = overlay.TextureUploadCount;
            overlay.RefreshBindingAndRender(_ => presentation);
            Require(overlay.TextureUploadCount == uploads && ReferenceEquals(overlay.Speaker, person) &&
                ReferenceEquals(overlay.BoundTalk, talk),
                "Repeated identical presentation uploaded texture or changed binding.");
        }

        private static void AssertCompositeEquals(SplitPortraitFace expected,
            SplitPortraitRuntimeOverlay.Presentation presentation, DaggerfallTalkWindow talk)
        {
            Texture2D displayed = GetPortraitPanel(talk).BackgroundTexture;
            Require(displayed && displayed != GetNativePortrait(talk) && displayed.width == 64 && displayed.height == 64,
                "TalkWindow panel does not show the owned 64x64 split composite.");
            var expectedTopDown = new byte[64 * 64 * 4];
            bool usedNeutral;
            string error;
            Require(expected.TryCompose(presentation.EmotionId, presentation.GazeId, presentation.EyePhase, presentation.MouthShape,
                expectedTopDown, out usedNeutral, out error), "Expected face composition failed: " + error);
            byte[] actualTopDown = ToTopDownRgba(displayed.GetPixels32(), 64, 64);
            Require(BytesEqual(expectedTopDown, actualTopDown), "Native TalkWindow composite pixels differ from actual art compositor output.");
        }

        private static SplitPortraitFace BuildExpectedFace(Mod mod, DaggerfallTalkWindow talk, int record)
        {
            TextAsset registryAsset = mod.GetAsset<TextAsset>(SplitPortraitRuntimeOverlay.ManifestAssetName);
            Require(registryAsset, "Packaged split portrait registry is missing from actual Mod assets.");
            SplitPortraitManifest manifest = DeserializeManifest(registryAsset.text);
            string structureError;
            Require(SplitPortraitManifestValidator.TryValidateStructure(manifest, out structureError),
                "Packaged registry structure invalid: " + structureError);
            SplitPortraitEntry entry = FindEntry(manifest, record);
            Require(entry != null, "Requested record missing from packaged registry: " + record);

            Texture2D native = GetNativePortrait(talk);
            Require(native && native.width == 64 && native.height == 64,
                "Native source portrait has unexpected dimensions.");
            byte[] nativeRgba = ToTopDownRgba(native.GetPixels32(), 64, 64);
            var decoded = new List<SplitPortraitDecodedRgbaAsset>(72);
            var temporary = new List<Texture2D>(72);
            try
            {
                foreach (SplitPortraitEmotion emotion in entry.emotions)
                {
                    DecodeFrames(mod, emotion.eyes, decoded, temporary);
                    DecodeFrames(mod, emotion.mouths, decoded, temporary);
                    if (emotion.gazes != null)
                        foreach (SplitPortraitGaze gaze in emotion.gazes)
                            DecodeFrames(mod, gaze.eyes, decoded, temporary);
                }
                var oneFace = new SplitPortraitManifest { schemaVersion = manifest.schemaVersion,
                    animationPolicy = manifest.animationPolicy, animationPolicySha256 = manifest.animationPolicySha256,
                    portraits = new[] { entry } };
                SplitPortraitFace face;
                string error;
                Require(SplitPortraitComposition.TryCreateFace(oneFace, "CommonFaces", "TFAC00I0.RCI",
                    record, nativeRgba, decoded, out face, out error),
                    "Packaged face assets failed actual native source validation: " + error);
                return face;
            }
            finally
            {
                foreach (Texture2D texture in temporary)
                    if (texture) UnityEngine.Object.Destroy(texture);
            }
        }

        private static void DecodeFrames(Mod mod, SplitPortraitAsset[] frames,
            List<SplitPortraitDecodedRgbaAsset> decoded, List<Texture2D> temporary)
        {
            foreach (SplitPortraitAsset frame in frames)
            {
                TextAsset asset = mod.GetAsset<TextAsset>(frame.assetName);
                Require(asset && asset.bytes != null && asset.bytes.Length > 0,
                    "Packaged portrait frame is missing: " + frame.assetName);
                var texture = new Texture2D(2, 2, TextureFormat.ARGB32, false);
                temporary.Add(texture);
                Require(texture.LoadImage(asset.bytes, false) && texture.width == frame.width && texture.height == frame.height,
                    "Packaged portrait frame failed Unity decode: " + frame.assetName);
                byte[] rgba = ToTopDownRgba(texture.GetPixels32(), texture.width, texture.height);
                decoded.Add(new SplitPortraitDecodedRgbaAsset
                {
                    assetName = frame.assetName,
                    width = texture.width,
                    height = texture.height,
                    encodedBytes = asset.bytes,
                    rgba = rgba,
                });
            }
        }

        private static SplitPortraitManifest DeserializeManifest(string json)
        {
            FullSerializer.fsData data = FullSerializer.fsJsonParser.Parse(json);
            Require(data != null && data.IsDictionary, "Packaged registry JSON root is not an object.");
            SplitPortraitManifest manifest = null;
            FullSerializer.fsResult result = new FullSerializer.fsSerializer().TryDeserialize(data, ref manifest);
            Require(!result.Failed && manifest != null, "Packaged registry did not deserialize.");
            return manifest;
        }

        private static SplitPortraitEntry FindEntry(SplitPortraitManifest manifest, int record)
        {
            foreach (SplitPortraitEntry entry in manifest.portraits)
                if (entry.record == record && entry.archive == "CommonFaces" &&
                    string.Equals(entry.bank, "TFAC00I0.RCI", StringComparison.OrdinalIgnoreCase)) return entry;
            return null;
        }

        private static void VerifySourceIsUntouched(DaggerfallTalkWindow talk, Texture2D originalSource,
            byte[] originalPixels)
        {
            Texture2D currentSource = GetNativePortrait(talk);
            Require(talk != null && currentSource != null && currentSource.width == 64 && currentSource.height == 64 &&
                ReferenceEquals(currentSource, originalSource),
                "Native TalkWindow source texture is missing or changed dimensions.");
            Require(BytesEqual(CaptureNativeRgba(currentSource), originalPixels),
                "Native TalkWindow source pixels changed during portrait rendering.");
        }

        private static byte[] CaptureNativeRgba(Texture2D source)
        {
            Require(source && source.width == 64 && source.height == 64,
                "Native source texture is not 64x64.");
            return ToTopDownRgba(source.GetPixels32(), 64, 64);
        }

        private static Texture2D GetNativePortrait(DaggerfallTalkWindow talk)
        {
            FieldInfo sourceField = typeof(DaggerfallTalkWindow).GetField("texturePortrait", PrivateInstance);
            return sourceField == null ? null : sourceField.GetValue(talk) as Texture2D;
        }

        private static Panel GetPortraitPanel(DaggerfallTalkWindow talk)
        {
            FieldInfo panelField = typeof(DaggerfallTalkWindow).GetField("panelPortrait", PrivateInstance);
            return panelField == null ? null : panelField.GetValue(talk) as Panel;
        }

        private static byte[] ToTopDownRgba(Color32[] source, int width, int height)
        {
            Require(source != null && source.Length == width * height, "Texture pixel readback dimensions invalid.");
            var result = new byte[source.Length * 4];
            for (int y = 0; y < height; y++)
            {
                int sourceRow = (height - 1 - y) * width;
                for (int x = 0; x < width; x++)
                {
                    Color32 pixel = source[sourceRow + x];
                    int offset = (y * width + x) * 4;
                    result[offset] = pixel.r;
                    result[offset + 1] = pixel.g;
                    result[offset + 2] = pixel.b;
                    result[offset + 3] = pixel.a;
                }
            }
            return result;
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left == null || right == null || left.Length != right.Length) return false;
            for (int index = 0; index < left.Length; index++)
                if (left[index] != right[index]) return false;
            return true;
        }

        private static FieldInfo RequireHostField(string name)
        {
            FieldInfo field = typeof(PeopleOfTheIliacBayMod).GetField(name, PrivateInstance);
            if (field == null) throw new InvalidOperationException("Split portrait probe host field missing: " + name);
            return field;
        }

        private static T Read<T>(object target, string name)
        {
            FieldInfo field = RequireHostField(name);
            return (T)field.GetValue(target);
        }

        private static object ReadObject(object target, string name)
        {
            return RequireHostField(name).GetValue(target);
        }

        private static void SetHostField(object target, string name, object value)
        {
            RequireHostField(name).SetValue(target, value);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
