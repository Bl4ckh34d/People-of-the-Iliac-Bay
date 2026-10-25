using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.DFU
{
    public enum SplitPortraitEyePhase { Open, Half, Closed }

    /// <summary>Decoded pixels and original packaged bytes for one selected portrait texture.</summary>
    public sealed class SplitPortraitDecodedRgbaAsset
    {
        public string assetName;
        public int width;
        public int height;
        public byte[] encodedBytes;
        public byte[] rgba;
    }

    /// <summary>Validated, immutable-in-practice face pixels owned by the compositor.</summary>
    public sealed class SplitPortraitFace
    {
        private readonly EmotionPixels[] emotions;

        public readonly string Archive;
        public readonly string Bank;
        public readonly int Record;
        public readonly string NativeRgbaSha256;
        public readonly SplitPortraitAnimationPolicy AnimationPolicy;

        internal SplitPortraitFace(string archive, string bank, int record, string nativeRgbaSha256,
            SplitPortraitAnimationPolicy animationPolicy, EmotionPixels[] faceEmotions)
        {
            Archive = archive;
            Bank = bank;
            Record = record;
            NativeRgbaSha256 = nativeRgbaSha256;
            AnimationPolicy = animationPolicy == null ? null : animationPolicy.Copy();
            emotions = faceEmotions;
        }

        public bool HasCompleteGazePairForEmotion(string emotionId)
        {
            int emotionIndex = FindEmotion(emotionId);
            return emotionIndex >= 0 && emotions[emotionIndex].Gazes != null && emotions[emotionIndex].Gazes.Length == 2;
        }

        /// <summary>Compose selected emotion halves into caller-owned top-down RGBA64 buffer.</summary>
        public bool TryCompose(string requestedEmotion, SplitPortraitEyePhase eyePhase,
            PortraitMouthShape mouthShape, byte[] destination, out bool usedNeutralFallback, out string error)
        {
            return TryCompose(requestedEmotion, null, eyePhase, mouthShape, destination,
                out usedNeutralFallback, out error);
        }

        /// <summary>Compose the selected horizontal gaze when available; null or center uses emotion eyes.</summary>
        public bool TryCompose(string requestedEmotion, string gazeId, SplitPortraitEyePhase eyePhase,
            PortraitMouthShape mouthShape, byte[] destination, out bool usedNeutralFallback, out string error)
        {
            usedNeutralFallback = false;
            error = null;
            if (destination == null || destination.Length != SplitPortraitManifestValidator.FaceWidth *
                SplitPortraitManifestValidator.FaceHeight * 4)
                return Fail("composition-destination-must-be-64x64-rgba", out error);
            if ((int)eyePhase < 0 || (int)eyePhase >= 3 || (int)mouthShape < 0 || (int)mouthShape >= 9)
                return Fail("composition-eye-phase-or-mouth-shape-invalid", out error);
            bool centerGaze = string.IsNullOrEmpty(gazeId) || string.Equals(gazeId, "center", StringComparison.Ordinal);
            if (!centerGaze && !IsGazeId(gazeId)) return Fail("composition-gaze-id-invalid", out error);

            int emotionIndex = FindEmotion(requestedEmotion);
            if (emotionIndex < 0)
            {
                emotionIndex = FindEmotion("neutral");
                usedNeutralFallback = true;
            }

            EmotionPixels emotion = emotions[emotionIndex];
            byte[][] eyes = centerGaze ? null : FindGazeEyes(emotion.Gazes, gazeId);
            if (eyes == null) eyes = emotion.Eyes;
            byte[] selectedEyes = eyes[(int)eyePhase];
            byte[] mouth = (int)mouthShape == (int)PortraitMouthShape.X ?
                emotion.Mouths[(int)PortraitMouthShape.X] : emotion.Mouths[(int)mouthShape];
            Buffer.BlockCopy(selectedEyes, 0, destination, 0, selectedEyes.Length);
            Buffer.BlockCopy(mouth, 0, destination, selectedEyes.Length, mouth.Length);
            return true;
        }

        private int FindEmotion(string id)
        {
            if (id == null) return -1;
            for (int index = 0; index < emotions.Length; index++)
                if (string.Equals(emotions[index].Id, id, StringComparison.Ordinal)) return index;
            return -1;
        }

        private static bool Fail(string reason, out string error)
        {
            error = reason;
            return false;
        }

        private static bool IsGazeId(string id)
        {
            return string.Equals(id, "left", StringComparison.Ordinal) ||
                string.Equals(id, "right", StringComparison.Ordinal);
        }

        private static byte[][] FindGazeEyes(GazePixels[] gazes, string id)
        {
            if (gazes == null) return null;
            for (int index = 0; index < gazes.Length; index++)
                if (string.Equals(gazes[index].Id, id, StringComparison.Ordinal)) return gazes[index].Eyes;
            return null;
        }

        internal sealed class EmotionPixels
        {
            public readonly string Id;
            public readonly byte[][] Eyes;
            public readonly byte[][] Mouths;
            public readonly GazePixels[] Gazes;

            public EmotionPixels(string id, byte[][] eyes, byte[][] mouths, GazePixels[] gazes)
            {
                Id = id;
                Eyes = eyes;
                Mouths = mouths;
                Gazes = gazes;
            }
        }

        internal sealed class GazePixels
        {
            public readonly string Id;
            public readonly byte[][] Eyes;

            public GazePixels(string id, byte[][] eyes)
            {
                Id = id;
                Eyes = eyes;
            }
        }
    }

    /// <summary>Builds one selected, source-verified portrait and composes its split pixels.</summary>
    public static class SplitPortraitComposition
    {
        private static readonly string[] EmotionIds = { "neutral", "happy", "angry", "sad", "afraid", "guarded" };
        private static readonly string[] EyePhases = { "open", "half", "closed" };
        private static readonly string[] GazeIds = { "left", "right" };
        private static readonly string[] MouthShapes = { "A", "B", "C", "D", "E", "F", "G", "H", "X" };

        public static bool TryCreateFace(SplitPortraitManifest registry, string archive, string bank, int record,
            byte[] currentNativeTopDownRgba, IList<SplitPortraitDecodedRgbaAsset> decodedAssets,
            out SplitPortraitFace face, out string error)
        {
            face = null;
            error = null;
            if (!SplitPortraitManifestValidator.TryValidateStructure(registry, out error)) return false;
            if (record < 0 || !IsSupportedPair(archive, bank))
                return Fail("portrait-request-identity-invalid-or-unsupported", out error);

            SplitPortraitEntry selected = null;
            foreach (SplitPortraitEntry entry in registry.portraits)
            {
                if (entry.record == record && string.Equals(entry.archive, archive, StringComparison.Ordinal) &&
                    string.Equals(entry.bank, bank, StringComparison.OrdinalIgnoreCase)) selected = entry;
            }
            if (selected == null) return Fail("portrait-registry-entry-missing", out error);

            int fullLength = SplitPortraitManifestValidator.FaceWidth * SplitPortraitManifestValidator.FaceHeight * 4;
            if (currentNativeTopDownRgba == null || currentNativeTopDownRgba.Length != fullLength ||
                !FixedHashEquals(selected.nativeRgbaSha256, SplitPortraitManifestValidator.Sha256(currentNativeTopDownRgba)))
                return Fail("current-native-rgba-length-or-hash-mismatch", out error);
            if (decodedAssets == null) return Fail("decoded-assets-missing", out error);

            var assetsByName = new Dictionary<string, SplitPortraitDecodedRgbaAsset>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < decodedAssets.Count; index++)
            {
                SplitPortraitDecodedRgbaAsset asset = decodedAssets[index];
                if (asset == null || string.IsNullOrWhiteSpace(asset.assetName) || assetsByName.ContainsKey(asset.assetName))
                    return Fail("decoded-asset-name-invalid-or-duplicate", out error);
                assetsByName.Add(asset.assetName, asset);
            }

            var selectedRegistry = new SplitPortraitManifest
            {
                schemaVersion = registry.schemaVersion,
                animationPolicy = registry.animationPolicy,
                animationPolicySha256 = registry.animationPolicySha256,
                portraits = new[] { selected },
            };
            if (!SplitPortraitManifestValidator.TryValidate(selectedRegistry, name =>
            {
                SplitPortraitDecodedRgbaAsset asset;
                if (!assetsByName.TryGetValue(name, out asset) || asset.encodedBytes == null || asset.rgba == null ||
                    asset.width < 1 || asset.height < 1 || asset.rgba.Length != asset.width * asset.height * 4)
                    return null;
                return new SplitPortraitDecodedAsset
                {
                    encodedBytes = asset.encodedBytes,
                    width = asset.width,
                    height = asset.height,
                    rgbaSha256 = SplitPortraitManifestValidator.Sha256(asset.rgba),
                };
            }, out error))
            {
                face = null;
                return false;
            }

            var snapshots = new SplitPortraitFace.EmotionPixels[EmotionIds.Length];
            for (int emotionIndex = 0; emotionIndex < EmotionIds.Length; emotionIndex++)
            {
                SplitPortraitEmotion sourceEmotion = FindEmotion(selected.emotions, EmotionIds[emotionIndex]);
                if (sourceEmotion == null) return Fail("required-emotion-missing", out error);
                byte[][] eyes = new byte[EyePhases.Length][];
                byte[][] mouths = new byte[MouthShapes.Length][];
                for (int phase = 0; phase < EyePhases.Length; phase++)
                    eyes[phase] = CopyRgba(assetsByName, FindFrame(sourceEmotion.eyes, EyePhases[phase]));
                for (int shape = 0; shape < MouthShapes.Length; shape++)
                    mouths[shape] = CopyRgba(assetsByName, FindFrame(sourceEmotion.mouths, MouthShapes[shape]));
                SplitPortraitFace.GazePixels[] gazes = null;
                if (sourceEmotion.gazes != null)
                {
                    gazes = new SplitPortraitFace.GazePixels[GazeIds.Length];
                    for (int gazeIndex = 0; gazeIndex < GazeIds.Length; gazeIndex++)
                    {
                        SplitPortraitGaze sourceGaze = FindGaze(sourceEmotion.gazes, GazeIds[gazeIndex]);
                        byte[][] gazeEyes = new byte[EyePhases.Length][];
                        for (int phase = 0; phase < EyePhases.Length; phase++)
                            gazeEyes[phase] = CopyRgba(assetsByName, FindFrame(sourceGaze.eyes, EyePhases[phase]));
                        gazes[gazeIndex] = new SplitPortraitFace.GazePixels(sourceGaze.id, gazeEyes);
                    }
                }
                snapshots[emotionIndex] = new SplitPortraitFace.EmotionPixels(EmotionIds[emotionIndex], eyes, mouths, gazes);
            }

            face = new SplitPortraitFace(selected.archive, selected.bank, selected.record,
                selected.nativeRgbaSha256, registry.animationPolicy, snapshots);
            return true;
        }

        private static SplitPortraitEmotion FindEmotion(SplitPortraitEmotion[] emotions, string id)
        {
            if (emotions == null) return null;
            for (int index = 0; index < emotions.Length; index++)
                if (emotions[index] != null && string.Equals(emotions[index].id, id, StringComparison.Ordinal)) return emotions[index];
            return null;
        }

        private static SplitPortraitAsset FindFrame(SplitPortraitAsset[] frames, string id)
        {
            if (frames == null) return null;
            for (int index = 0; index < frames.Length; index++)
                if (frames[index] != null && string.Equals(frames[index].frame, id, StringComparison.Ordinal)) return frames[index];
            return null;
        }

        private static SplitPortraitGaze FindGaze(SplitPortraitGaze[] gazes, string id)
        {
            if (gazes == null) return null;
            for (int index = 0; index < gazes.Length; index++)
                if (gazes[index] != null && string.Equals(gazes[index].id, id, StringComparison.Ordinal)) return gazes[index];
            return null;
        }

        private static byte[] CopyRgba(Dictionary<string, SplitPortraitDecodedRgbaAsset> assets, SplitPortraitAsset frame)
        {
            if (frame == null) return null;
            SplitPortraitDecodedRgbaAsset asset;
            if (!assets.TryGetValue(frame.assetName, out asset) || asset.rgba == null) return null;
            byte[] copy = new byte[asset.rgba.Length];
            Buffer.BlockCopy(asset.rgba, 0, copy, 0, copy.Length);
            return copy;
        }

        private static bool IsSupportedPair(string archive, string bank)
        {
            return string.Equals(archive, "CommonFaces", StringComparison.Ordinal) &&
                string.Equals(bank, "TFAC00I0.RCI", StringComparison.OrdinalIgnoreCase);
        }

        private static bool FixedHashEquals(string expected, string actual)
        {
            if (expected == null || actual == null || expected.Length != 64 || actual.Length != 64) return false;
            int difference = 0;
            for (int index = 0; index < expected.Length; index++)
            {
                char left = char.ToLowerInvariant(expected[index]);
                char right = char.ToLowerInvariant(actual[index]);
                if (!IsHex(left) || !IsHex(right)) return false;
                difference |= left ^ right;
            }
            return difference == 0;
        }

        private static bool IsHex(char value)
        {
            return (value >= '0' && value <= '9') || (value >= 'a' && value <= 'f');
        }

        private static bool Fail(string reason, out string error)
        {
            error = reason;
            return false;
        }
    }
}
