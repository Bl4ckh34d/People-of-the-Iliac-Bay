using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.DFU;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class SplitPortraitCompositionTests
    {
        private const int RgbaBytes = 64 * 64 * 4;
        private static readonly string[] EmotionIds = { "neutral", "happy", "angry", "sad", "afraid", "guarded" };
        private static readonly string[] EyeIds = { "open", "half", "closed" };

        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add((nameof(SelectionUsesRequestedArchiveBankAndZeroBasedRecord), SelectionUsesRequestedArchiveBankAndZeroBasedRecord));
            cases.Add((nameof(EveryEmotionAndCueComposesSelectedEyesAndMouthIntoCallerBuffer), EveryEmotionAndCueComposesSelectedEyesAndMouthIntoCallerBuffer));
            cases.Add((nameof(UnknownEmotionUsesSameFaceNeutralAndXKeepsRequestedEyes), UnknownEmotionUsesSameFaceNeutralAndXKeepsRequestedEyes));
            cases.Add((nameof(InvalidSourceManifestAssetsAndDimensionsReturnNativeFallback), InvalidSourceManifestAssetsAndDimensionsReturnNativeFallback));
            cases.Add((nameof(DuplicateRegistryIdentityAndEncodedHashMismatchReturnNativeFallback), DuplicateRegistryIdentityAndEncodedHashMismatchReturnNativeFallback));
            cases.Add((nameof(MalformedUnselectedEntryFailsBeforeAssetAccess), MalformedUnselectedEntryFailsBeforeAssetAccess));
            cases.Add((nameof(CreatedFaceFreezesManifestAndInputPixelsWithoutChangingSource), CreatedFaceFreezesManifestAndInputPixelsWithoutChangingSource));
            cases.Add((nameof(GazeCompositionUsesMatchingEmotionDirectionAndBlinkPhase), GazeCompositionUsesMatchingEmotionDirectionAndBlinkPhase));
            cases.Add((nameof(MissingEmotionGazeFallsBackToItsOwnCenterEyes), MissingEmotionGazeFallsBackToItsOwnCenterEyes));
        }

        private static void SelectionUsesRequestedArchiveBankAndZeroBasedRecord()
        {
            Fixture fixture = CreateFixture();
            SplitPortraitFace face;
            string error;
            Check.True(SplitPortraitComposition.TryCreateFace(fixture.Manifest, "CommonFaces", "TFAC00I0.RCI", 77,
                fixture.Source77, fixture.Assets, out face, out error), error);
            Check.Equal(77, face.Record);
            Check.Equal("CommonFaces", face.Archive);

            Check.True(!SplitPortraitComposition.TryCreateFace(fixture.Manifest, "CommonFaces", "TFAC00I0.RCI", 76,
                fixture.Source77, null, out face, out error));
            Check.True(face == null);

            Check.True(!SplitPortraitComposition.TryCreateFace(fixture.Manifest, "OtherFaces", "TFAC00I0.RCI", 77,
                fixture.Source77, fixture.Assets, out face, out error));
            Check.True(face == null);

            byte[] wrongRecordPixels = Fill(RgbaBytes, 29);
            Check.True(!SplitPortraitComposition.TryCreateFace(fixture.Manifest, "CommonFaces", "TFAC00I0.RCI", 76,
                wrongRecordPixels, fixture.Assets, out face, out error));
            Check.True(face == null);
        }

        private static void EveryEmotionAndCueComposesSelectedEyesAndMouthIntoCallerBuffer()
        {
            Fixture fixture = CreateFixture();
            SplitPortraitFace face = CreateFace(fixture, 77, fixture.Source77);
            string[] shapes = { "A", "B", "C", "D", "E", "F", "G", "H", "X" };
            var output = new byte[RgbaBytes];

            for (int emotion = 0; emotion < EmotionIds.Length; emotion++)
            {
                for (int eye = 0; eye < EyeIds.Length; eye++)
                {
                    for (int shape = 0; shape < shapes.Length; shape++)
                    {
                        bool usedNeutralFallback;
                        string error;
                        Check.True(face.TryCompose(EmotionIds[emotion], (SplitPortraitEyePhase)eye,
                            (PortraitMouthShape)shape, output, out usedNeutralFallback, out error), error);
                        Check.True(!usedNeutralFallback);
                        Check.Equal(fixture.EyeMarker(emotion, eye), output[0]);
                        Check.Equal(fixture.EyeMarker(emotion, eye), output[(35 * 64 * 4) - 1]);
                        Check.Equal(fixture.MouthMarker(emotion, shape), output[35 * 64 * 4]);
                        Check.Equal(fixture.MouthMarker(emotion, shape), output[RgbaBytes - 1]);
                    }
                }
            }
        }

        private static void UnknownEmotionUsesSameFaceNeutralAndXKeepsRequestedEyes()
        {
            Fixture fixture = CreateFixture();
            SplitPortraitFace face = CreateFace(fixture, 77, fixture.Source77);
            var output = new byte[RgbaBytes];
            bool usedNeutralFallback;
            string error;

            Check.True(face.TryCompose("unsupported", SplitPortraitEyePhase.Half, PortraitMouthShape.X,
                output, out usedNeutralFallback, out error), error);
            Check.True(usedNeutralFallback);
            Check.Equal(fixture.EyeMarker(0, 1), output[0]);
            Check.Equal(fixture.MouthMarker(0, 8), output[35 * 64 * 4]);

            Check.True(face.TryCompose("angry", SplitPortraitEyePhase.Closed, PortraitMouthShape.X,
                output, out usedNeutralFallback, out error), error);
            Check.True(!usedNeutralFallback);
            Check.Equal(fixture.EyeMarker(2, 2), output[0]);
            Check.Equal(fixture.MouthMarker(2, 8), output[35 * 64 * 4]);

            Check.True(!face.TryCompose("angry", SplitPortraitEyePhase.Closed, PortraitMouthShape.X,
                new byte[RgbaBytes - 1], out usedNeutralFallback, out error));
        }

        private static void InvalidSourceManifestAssetsAndDimensionsReturnNativeFallback()
        {
            Fixture fixture = CreateFixture();
            SplitPortraitFace face;
            string error;
            Check.True(!SplitPortraitComposition.TryCreateFace(fixture.Manifest, "CommonFaces", "TFAC00I0.RCI", 77,
                Fill(RgbaBytes, 31), fixture.Assets, out face, out error));
            Check.True(face == null);

            SplitPortraitManifest wrongSchema = fixture.Manifest;
            wrongSchema.schemaVersion = 9;
            Check.True(!SplitPortraitComposition.TryCreateFace(wrongSchema, "CommonFaces", "TFAC00I0.RCI", 77,
                fixture.Source77, fixture.Assets, out face, out error));
            Check.True(face == null);

            var wrongPixels = new List<SplitPortraitDecodedRgbaAsset>(fixture.Assets);
            wrongPixels[0].rgba[0] ^= 0xff;
            Check.True(!SplitPortraitComposition.TryCreateFace(fixture.Manifest, "CommonFaces", "TFAC00I0.RCI", 77,
                fixture.Source77, wrongPixels, out face, out error));
            Check.True(face == null);

            var truncated = new List<SplitPortraitDecodedRgbaAsset>(fixture.Assets);
            truncated[0] = new SplitPortraitDecodedRgbaAsset
            {
                assetName = truncated[0].assetName,
                width = truncated[0].width,
                height = truncated[0].height,
                encodedBytes = truncated[0].encodedBytes,
                rgba = new byte[truncated[0].rgba.Length - 1],
            };
            Check.True(!SplitPortraitComposition.TryCreateFace(fixture.Manifest, "CommonFaces", "TFAC00I0.RCI", 77,
                fixture.Source77, truncated, out face, out error));
            Check.True(face == null);

            var missing = new List<SplitPortraitDecodedRgbaAsset>(fixture.Assets);
            missing.RemoveAt(0);
            Check.True(!SplitPortraitComposition.TryCreateFace(fixture.Manifest, "CommonFaces", "TFAC00I0.RCI", 77,
                fixture.Source77, missing, out face, out error));
            Check.True(face == null);
        }

        private static void MalformedUnselectedEntryFailsBeforeAssetAccess()
        {
            Fixture fixture = CreateFixture();
            SplitPortraitEntry malformed = new SplitPortraitEntry
            {
                archive = "CommonFaces", bank = "TFAC00I0.RCI", record = 78,
                sourceImageSha256 = fixture.Manifest.portraits[0].sourceImageSha256,
                nativeRgbaSha256 = fixture.Manifest.portraits[0].nativeRgbaSha256,
                width = 63, height = 64, splitY = 35, emotions = null,
            };
            fixture.Manifest.portraits = new[] { fixture.Manifest.portraits[0], malformed };

            SplitPortraitFace face;
            string error;
            Check.True(!SplitPortraitComposition.TryCreateFace(fixture.Manifest, "CommonFaces", "TFAC00I0.RCI", 77,
                fixture.Source77, null, out face, out error));
            Check.True(face == null);
            Check.Equal("portrait-identity-dimensions-split-or-hash-invalid", error);
        }
        private static void CreatedFaceFreezesManifestAndInputPixelsWithoutChangingSource()
        {
            Fixture fixture = CreateFixture();
            byte[] originalSource = (byte[])fixture.Source77.Clone();
            SplitPortraitDecodedRgbaAsset originalAsset = fixture.Assets[0];
            byte originalAssetPixel = originalAsset.rgba[0];
            SplitPortraitFace face = CreateFace(fixture, 77, fixture.Source77);

            fixture.Source77[0] ^= 0xff;
            originalAsset.rgba[0] ^= 0xff;
            originalAsset.encodedBytes[0] ^= 0xff;
            fixture.Manifest.portraits[0].record = 76;
            fixture.Manifest.portraits[0].emotions[0].eyes[0].assetName = "changed.png.bytes";

            var output = new byte[RgbaBytes];
            bool usedNeutralFallback;
            string error;
            Check.True(face.TryCompose("neutral", SplitPortraitEyePhase.Open, PortraitMouthShape.X,
                output, out usedNeutralFallback, out error), error);
            Check.Equal(originalAssetPixel, output[0]);
            Check.Equal(77, face.Record);
            Check.True(!BytesEqual(originalSource, fixture.Source77));
            Check.Equal(originalSource[0], (byte)(fixture.Source77[0] ^ 0xff));
        }

        private static void DuplicateRegistryIdentityAndEncodedHashMismatchReturnNativeFallback()
        {
            Fixture fixture = CreateFixture();
            SplitPortraitManifest duplicate = new SplitPortraitManifest
            {
                schemaVersion = fixture.Manifest.schemaVersion,
                portraits = new[] { fixture.Manifest.portraits[0], fixture.Manifest.portraits[0] },
            };
            SplitPortraitFace face;
            string error;
            Check.True(!SplitPortraitComposition.TryCreateFace(duplicate, "CommonFaces", "TFAC00I0.RCI", 77,
                fixture.Source77, null, out face, out error));
            Check.True(face == null);

            fixture.Assets[0].encodedBytes = System.Text.Encoding.UTF8.GetBytes("different encoded png bytes");
            Check.True(!SplitPortraitComposition.TryCreateFace(fixture.Manifest, "CommonFaces", "TFAC00I0.RCI", 77,
                fixture.Source77, fixture.Assets, out face, out error));
            Check.True(face == null);
        }

        private static void GazeCompositionUsesMatchingEmotionDirectionAndBlinkPhase()
        {
            Fixture fixture = CreateFixture(includeGazes: true);
            SplitPortraitFace face = CreateFace(fixture, 77, fixture.Source77);
            var output = new byte[RgbaBytes];

            for (int emotion = 0; emotion < EmotionIds.Length; emotion++)
            {
                foreach (string direction in new[] { "left", "right" })
                {
                    int directionIndex = direction == "left" ? 0 : 1;
                    for (int phase = 0; phase < EyeIds.Length; phase++)
                    {
                        bool usedNeutralFallback;
                        string error;
                        Check.True(face.TryCompose(EmotionIds[emotion], direction, (SplitPortraitEyePhase)phase,
                            PortraitMouthShape.X, output, out usedNeutralFallback, out error), error);
                        Check.True(!usedNeutralFallback);
                        Check.Equal(fixture.GazeMarker(emotion, directionIndex, phase), output[0]);
                        Check.Equal(fixture.MouthMarker(emotion, 8), output[35 * 64 * 4]);
                    }
                }
            }

            Check.True(!face.TryCompose("angry", "up-left", SplitPortraitEyePhase.Open, PortraitMouthShape.X,
                output, out _, out _));
        }

        private static void MissingEmotionGazeFallsBackToItsOwnCenterEyes()
        {
            Fixture fixture = CreateFixture(includeGazes: true);
            fixture.Manifest.portraits[0].emotions[1].gazes = null;
            SplitPortraitFace face = CreateFace(fixture, 77, fixture.Source77);
            var output = new byte[RgbaBytes];
            bool usedNeutralFallback;
            string error;

            Check.True(face.TryCompose("happy", "left", SplitPortraitEyePhase.Half, PortraitMouthShape.X,
                output, out usedNeutralFallback, out error), error);
            Check.True(!usedNeutralFallback);
            Check.Equal(fixture.EyeMarker(1, 1), output[0]);

            var legacyOutput = new byte[RgbaBytes];
            Check.True(face.TryCompose("happy", SplitPortraitEyePhase.Half, PortraitMouthShape.X,
                legacyOutput, out usedNeutralFallback, out error), error);
            Check.Equal(fixture.EyeMarker(1, 1), legacyOutput[0]);
            Check.True(BytesEqual(legacyOutput, output));
        }

        private static SplitPortraitFace CreateFace(Fixture fixture, int record, byte[] source)
        {
            SplitPortraitFace face;
            string error;
            Check.True(SplitPortraitComposition.TryCreateFace(fixture.Manifest, "CommonFaces", "TFAC00I0.RCI", record,
                source, fixture.Assets, out face, out error), error);
            return face;
        }

        private sealed class Fixture
        {
            public SplitPortraitManifest Manifest;
            public byte[] Source77;
            public List<SplitPortraitDecodedRgbaAsset> Assets;
            private readonly byte[,] eyeMarkers = new byte[6, 3];
            private readonly byte[,] mouthMarkers = new byte[6, 9];
            private readonly byte[,,] gazeMarkers = new byte[6, 2, 3];
            public byte EyeMarker(int emotion, int phase) { return eyeMarkers[emotion, phase]; }
            public byte MouthMarker(int emotion, int shape) { return mouthMarkers[emotion, shape]; }
            public byte GazeMarker(int emotion, int direction, int phase) { return gazeMarkers[emotion, direction, phase]; }
            public void SetEyeMarker(int emotion, int phase, byte marker) { eyeMarkers[emotion, phase] = marker; }
            public void SetMouthMarker(int emotion, int shape, byte marker) { mouthMarkers[emotion, shape] = marker; }
            public void SetGazeMarker(int emotion, int direction, int phase, byte marker) { gazeMarkers[emotion, direction, phase] = marker; }
        }

        private static Fixture CreateFixture(bool includeGazes = false)
        {
            var fixture = new Fixture
            {
                Source77 = Fill(RgbaBytes, 77),
                Assets = new List<SplitPortraitDecodedRgbaAsset>(),
            };
            var entry = new SplitPortraitEntry
            {
                archive = "CommonFaces", bank = "TFAC00I0.RCI", record = 77,
                nativeRgbaSha256 = SplitPortraitManifestValidator.Sha256(fixture.Source77),
                sourceImageSha256 = HashByte(7), width = 64, height = 64, splitY = 35,
                emotions = new SplitPortraitEmotion[EmotionIds.Length],
            };
            string[] shapes = { "A", "B", "C", "D", "E", "F", "G", "H", "X" };
            byte marker = 1;
            for (int emotion = 0; emotion < EmotionIds.Length; emotion++)
            {
                var emotionSet = new SplitPortraitEmotion
                {
                    id = EmotionIds[emotion], eyes = new SplitPortraitAsset[EyeIds.Length],
                    mouths = new SplitPortraitAsset[shapes.Length],
                    gazes = includeGazes ? new SplitPortraitGaze[2] : null,
                };
                for (int phase = 0; phase < EyeIds.Length; phase++)
                {
                    byte value = marker++;
                    fixture.SetEyeMarker(emotion, phase, value);
                    emotionSet.eyes[phase] = AddAsset(fixture, "eye-" + emotion + "-" + EyeIds[phase],
                        EyeIds[phase], 64, 35, value);
                }
                for (int shape = 0; shape < shapes.Length; shape++)
                {
                    byte value = marker++;
                    fixture.SetMouthMarker(emotion, shape, value);
                    emotionSet.mouths[shape] = AddAsset(fixture, "mouth-" + emotion + "-" + shapes[shape],
                        shapes[shape], 64, 29, value);
                }
                if (includeGazes)
                {
                    string[] directions = { "left", "right" };
                    for (int direction = 0; direction < directions.Length; direction++)
                    {
                        var gaze = new SplitPortraitGaze { id = directions[direction], eyes = new SplitPortraitAsset[EyeIds.Length] };
                        for (int phase = 0; phase < EyeIds.Length; phase++)
                        {
                            byte value = marker++;
                            fixture.SetGazeMarker(emotion, direction, phase, value);
                            gaze.eyes[phase] = AddAsset(fixture,
                                "gaze-" + emotion + "-" + directions[direction] + "-" + EyeIds[phase],
                                EyeIds[phase], 64, 35, value);
                        }
                        emotionSet.gazes[direction] = gaze;
                    }
                }
                entry.emotions[emotion] = emotionSet;
            }
            fixture.Manifest = new SplitPortraitManifest
            {
                schemaVersion = includeGazes ? SplitPortraitManifestValidator.SchemaVersion : SplitPortraitManifestValidator.LegacySchemaVersion,
                animationPolicy = includeGazes ? new SplitPortraitAnimationPolicy() : null,
                animationPolicySha256 = includeGazes ? HashByte(9) : null,
                portraits = new[] { entry },
            };
            return fixture;
        }

        private static SplitPortraitAsset AddAsset(Fixture fixture, string name, string frame, int width, int height, byte marker)
        {
            string assetName = name + ".png.bytes";
            byte[] rgba = Fill(width * height * 4, marker);
            byte[] encoded = System.Text.Encoding.UTF8.GetBytes("png:" + assetName);
            fixture.Assets.Add(new SplitPortraitDecodedRgbaAsset
            {
                assetName = assetName, width = width, height = height, encodedBytes = encoded, rgba = rgba,
            });
            return new SplitPortraitAsset
            {
                assetName = assetName, frame = frame, width = width, height = height,
                sha256 = SplitPortraitManifestValidator.Sha256(encoded),
                rgbaSha256 = SplitPortraitManifestValidator.Sha256(rgba),
            };
        }

        private static byte[] Fill(int count, byte value)
        {
            var result = new byte[count];
            for (int i = 0; i < result.Length; i++) result[i] = value;
            return result;
        }

        private static string HashByte(byte value) { return SplitPortraitManifestValidator.Sha256(new[] { value }); }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            for (int i = 0; i < left.Length; i++) if (left[i] != right[i]) return false;
            return true;
        }
    }
}
