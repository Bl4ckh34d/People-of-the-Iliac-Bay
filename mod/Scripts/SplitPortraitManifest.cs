using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace PeopleOfTheIliacBay.DFU
{
    [Serializable]
    public sealed class SplitPortraitManifest
    {
        public int schemaVersion;
        public SplitPortraitAnimationPolicy animationPolicy;
        public string animationPolicySha256;
        public SplitPortraitEntry[] portraits;
    }

    /// <summary>Browser-gallery animation timings copied from its exact-source policy artifact.</summary>
    [Serializable]
    public sealed class SplitPortraitAnimationPolicy
    {
        public int schemaVersion = 1;
        public int blinkDurationMs = 230;
        public int blinkHalfMs = 65;
        public int blinkClosedEndMs = 165;
        public double blinkDoubleChance = 0.18;
        public double blinkDoubleStartMinFraction = 0.55;
        public double blinkDoubleStartMaxFraction = 0.70;
        public int blinkShortDelayMinMs = 1000;
        public int blinkShortDelayMaxMs = 1800;
        public int blinkLongDelayMinMs = 2500;
        public int blinkLongDelayMaxMs = 7000;
        public double blinkSingleShortChance = 0.12;
        public double blinkDoubleShortChance = 0.55;
        public int holdIntervalMinMs = 15000;
        public int holdIntervalMaxMs = 60000;
        public int holdHalfMinMs = 1500;
        public int holdHalfMaxMs = 8000;
        public double holdSpeakingClosedChance = 0.4;
        public int holdClosedMinMs = 1500;
        public int holdClosedMaxMs = 3000;
        public int holdReleaseMs = 65;
        public int gazeWindowMs = 60000;
        public int gazeBurstsMin = 1;
        public int gazeBurstsMax = 5;
        public int gazeDurationMinMs = 1800;
        public int gazeDurationMaxMs = 6500;
        public int gazeAutoMinMs = 1500;
        public int gazeAutoMaxMs = 4500;

        public bool IsValid()
        {
            return schemaVersion == 1 && blinkDurationMs > 0 && blinkHalfMs > 0 &&
                blinkHalfMs < blinkClosedEndMs && blinkClosedEndMs < blinkDurationMs &&
                IsProbability(blinkDoubleChance) && IsProbability(blinkSingleShortChance) &&
                IsProbability(blinkDoubleShortChance) && IsProbability(holdSpeakingClosedChance) &&
                IsFraction(blinkDoubleStartMinFraction) && IsFraction(blinkDoubleStartMaxFraction) &&
                blinkDoubleStartMinFraction <= blinkDoubleStartMaxFraction &&
                IsRange(blinkShortDelayMinMs, blinkShortDelayMaxMs) &&
                IsRange(blinkLongDelayMinMs, blinkLongDelayMaxMs) &&
                IsRange(holdIntervalMinMs, holdIntervalMaxMs) && IsRange(holdHalfMinMs, holdHalfMaxMs) &&
                IsRange(holdClosedMinMs, holdClosedMaxMs) && holdReleaseMs > 0 &&
                gazeWindowMs > 0 && gazeBurstsMin > 0 && gazeBurstsMax >= gazeBurstsMin &&
                IsRange(gazeDurationMinMs, gazeDurationMaxMs) && IsRange(gazeAutoMinMs, gazeAutoMaxMs);
        }

        public SplitPortraitAnimationPolicy Copy()
        {
            return (SplitPortraitAnimationPolicy)MemberwiseClone();
        }

        private static bool IsProbability(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0 && value <= 1;
        }

        private static bool IsFraction(double value) { return IsProbability(value); }

        private static bool IsRange(int minimum, int maximum)
        {
            return minimum > 0 && maximum >= minimum;
        }
    }

    [Serializable]
    public sealed class SplitPortraitEntry
    {
        public string archive;
        public string bank;
        public int record;
        public string sourceImageSha256;
        public string nativeRgbaSha256;
        public int width;
        public int height;
        public int splitY;
        public SplitPortraitEmotion[] emotions;
    }

    [Serializable]
    public sealed class SplitPortraitEmotion
    {
        public string id;
        public SplitPortraitAsset[] eyes;
        public SplitPortraitAsset[] mouths;
        public SplitPortraitGaze[] gazes;
    }

    [Serializable]
    public sealed class SplitPortraitGaze
    {
        public string id;
        public SplitPortraitAsset[] eyes;
    }

    [Serializable]
    public sealed class SplitPortraitAsset
    {
        public string frame;
        public string assetName;
        public string sha256;
        public string rgbaSha256;
        public int width;
        public int height;
    }

    /// <summary>Decoded texture facts supplied by the runtime texture loader.</summary>
    public sealed class SplitPortraitDecodedAsset
    {
        public byte[] encodedBytes;
        public string rgbaSha256;
        public int width;
        public int height;
    }

    /// <summary>Validates native split-face identity and every referenced packaged texture.</summary>
    public static class SplitPortraitManifestValidator
    {
        public const int LegacySchemaVersion = 1;
        public const int SchemaVersion = 2;
        public const int FaceWidth = 64;
        public const int FaceHeight = 64;
        public const int SplitY = 35;

        private static readonly string[] EmotionIds = { "neutral", "happy", "angry", "sad", "afraid", "guarded" };
        private static readonly string[] EyePhases = { "open", "half", "closed" };
        private static readonly string[] GazeIds = { "left", "right" };
        private static readonly string[] MouthShapes = { "A", "B", "C", "D", "E", "F", "G", "H", "X" };

        public static bool TryValidate(SplitPortraitManifest manifest,
            Func<string, SplitPortraitDecodedAsset> resolveAsset, out string error)
        {
            error = null;
            if (!TryValidateStructure(manifest, out error)) return false;
            if (resolveAsset == null) return Fail("manifest-schema-or-portraits-invalid", out error);

            foreach (SplitPortraitEntry portrait in manifest.portraits)
            {
                foreach (SplitPortraitEmotion emotion in portrait.emotions)
                {
                    if (!ValidateDecodedFrames(emotion.eyes, resolveAsset, out error) ||
                        !ValidateDecodedFrames(emotion.mouths, resolveAsset, out error))
                        return false;
                    if (emotion.gazes != null)
                        foreach (SplitPortraitGaze gaze in emotion.gazes)
                            if (!ValidateDecodedFrames(gaze.eyes, resolveAsset, out error)) return false;
                }
            }
            return true;
        }

        /// <summary>Validates manifest metadata without resolving or decoding texture bytes.</summary>
        public static bool TryValidateStructure(SplitPortraitManifest manifest, out string error)
        {
            error = null;
            if (manifest == null || (manifest.schemaVersion != LegacySchemaVersion && manifest.schemaVersion != SchemaVersion) ||
                manifest.portraits == null ||
                manifest.portraits.Length == 0)
                return Fail("manifest-schema-or-portraits-invalid", out error);

            if (manifest.schemaVersion == LegacySchemaVersion)
            {
                if (manifest.animationPolicy != null || manifest.animationPolicySha256 != null)
                    return Fail("legacy-manifest-must-not-declare-animation-policy", out error);
            }
            else if (manifest.animationPolicy == null || !manifest.animationPolicy.IsValid() ||
                !IsSha256(manifest.animationPolicySha256))
            {
                return Fail("animation-policy-or-source-hash-invalid", out error);
            }

            var identities = new HashSet<string>(StringComparer.Ordinal);
            var assetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (SplitPortraitEntry portrait in manifest.portraits)
            {
                if (portrait == null || string.IsNullOrWhiteSpace(portrait.archive) ||
                    portrait.archive.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 ||
                    string.IsNullOrWhiteSpace(portrait.bank) || portrait.bank.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || portrait.record < 0 ||
                    !IsSupportedArchiveBank(portrait.archive, portrait.bank) ||
                    portrait.width != FaceWidth || portrait.height != FaceHeight || portrait.splitY != SplitY ||
                    !IsSha256(portrait.sourceImageSha256) || !IsSha256(portrait.nativeRgbaSha256) ||
                    portrait.emotions == null || portrait.emotions.Length != EmotionIds.Length)
                    return Fail("portrait-identity-dimensions-split-or-hash-invalid", out error);

                if (!identities.Add(portrait.archive + "|" + portrait.record))
                    return Fail("duplicate-portrait-identity", out error);

                var seenEmotions = new HashSet<string>(StringComparer.Ordinal);
                foreach (SplitPortraitEmotion emotion in portrait.emotions)
                {
                    if (emotion == null || !Contains(EmotionIds, emotion.id) || !seenEmotions.Add(emotion.id) ||
                        emotion.eyes == null || emotion.eyes.Length != EyePhases.Length ||
                        emotion.mouths == null || emotion.mouths.Length != MouthShapes.Length ||
                        (manifest.schemaVersion == LegacySchemaVersion && emotion.gazes != null))
                        return Fail("emotion-set-or-frame-count-invalid", out error);

                    if (!ValidateFrameStructure(emotion.eyes, EyePhases, SplitY, assetNames, out error) ||
                        !ValidateFrameStructure(emotion.mouths, MouthShapes, FaceHeight - SplitY, assetNames, out error))
                        return false;
                    if (emotion.gazes != null && !ValidateGazeStructure(emotion.gazes, assetNames, out error))
                        return false;
                }
                foreach (string expected in EmotionIds)
                    if (!seenEmotions.Contains(expected)) return Fail("required-emotion-missing", out error);
            }
            return true;
        }

        public static string Sha256(byte[] bytes)
        {
            if (bytes == null) return null;
            using (SHA256 algorithm = SHA256.Create())
            {
                byte[] digest = algorithm.ComputeHash(bytes);
                var text = new StringBuilder(digest.Length * 2);
                foreach (byte value in digest) text.Append(value.ToString("x2"));
                return text.ToString();
            }
        }

        private static bool ValidateFrameStructure(SplitPortraitAsset[] frames, string[] expectedFrames,
            int expectedHeight, HashSet<string> assetNames, out string error)
        {
            error = null;
            var seenFrames = new HashSet<string>(StringComparer.Ordinal);
            foreach (SplitPortraitAsset frame in frames)
            {
                if (frame == null || !Contains(expectedFrames, frame.frame) || !seenFrames.Add(frame.frame) ||
                    frame.width != FaceWidth || frame.height != expectedHeight || !IsSha256(frame.sha256) ||
                    !IsSha256(frame.rgbaSha256) || !IsSafeAssetName(frame.assetName) ||
                    !assetNames.Add(frame.assetName))
                    return Fail("texture-frame-or-asset-name-invalid", out error);
            }
            foreach (string expected in expectedFrames)
                if (!seenFrames.Contains(expected)) return Fail("required-texture-frame-missing", out error);
            return true;
        }

        private static bool ValidateGazeStructure(SplitPortraitGaze[] gazes,
            HashSet<string> assetNames, out string error)
        {
            error = null;
            if (gazes.Length != GazeIds.Length) return Fail("gaze-set-or-frame-count-invalid", out error);
            var seenGazes = new HashSet<string>(StringComparer.Ordinal);
            foreach (SplitPortraitGaze gaze in gazes)
            {
                if (gaze == null || !Contains(GazeIds, gaze.id) || !seenGazes.Add(gaze.id) ||
                    gaze.eyes == null || gaze.eyes.Length != EyePhases.Length)
                    return Fail("gaze-set-or-frame-count-invalid", out error);
                if (!ValidateFrameStructure(gaze.eyes, EyePhases, SplitY, assetNames, out error)) return false;
            }
            foreach (string expected in GazeIds)
                if (!seenGazes.Contains(expected)) return Fail("complete-left-right-gaze-pair-required", out error);
            return true;
        }

        private static bool ValidateDecodedFrames(SplitPortraitAsset[] frames,
            Func<string, SplitPortraitDecodedAsset> resolveAsset, out string error)
        {
            error = null;
            foreach (SplitPortraitAsset frame in frames)
            {
                SplitPortraitDecodedAsset decoded;
                try { decoded = resolveAsset(frame.assetName); }
                catch (Exception) { return Fail("texture-asset-resolution-failed", out error); }
                if (decoded == null || decoded.encodedBytes == null || decoded.encodedBytes.Length == 0 ||
                    decoded.width != frame.width || decoded.height != frame.height ||
                    !FixedHashEquals(frame.sha256, Sha256(decoded.encodedBytes)) ||
                    !FixedHashEquals(frame.rgbaSha256, decoded.rgbaSha256))
                    return Fail("texture-asset-bytes-dimensions-or-pixels-mismatch", out error);
            }
            return true;
        }

        private static bool IsSafeAssetName(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && value.EndsWith(".png.bytes", StringComparison.OrdinalIgnoreCase) &&
                value.IndexOfAny(new[] { '/', '\\', ':' }) < 0 && value != ".png.bytes" &&
                !value.Contains("..") && !value.Contains("\0");
        }

        private static bool IsSupportedArchiveBank(string archive, string bank)
        {
            return string.Equals(archive, "CommonFaces", StringComparison.Ordinal) &&
                string.Equals(bank, "TFAC00I0.RCI", StringComparison.OrdinalIgnoreCase);
        }

        private static bool Contains(string[] values, string value)
        {
            if (value == null) return false;
            foreach (string candidate in values)
                if (string.Equals(candidate, value, StringComparison.Ordinal)) return true;
            return false;
        }

        private static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (char c in value)
                if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f') && !(c >= 'A' && c <= 'F')) return false;
            return true;
        }

        private static bool FixedHashEquals(string expected, string actual)
        {
            if (!IsSha256(expected) || !IsSha256(actual)) return false;
            int difference = 0;
            for (int index = 0; index < expected.Length; index++)
                difference |= char.ToLowerInvariant(expected[index]) ^ char.ToLowerInvariant(actual[index]);
            return difference == 0;
        }

        private static bool Fail(string reason, out string error)
        {
            error = reason;
            return false;
        }
    }
}
