using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.DFU;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class SplitPortraitManifestTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add((nameof(ValidManifestResolvesEveryUniqueTextureAndChecksDecodedFacts), ValidManifestResolvesEveryUniqueTextureAndChecksDecodedFacts));
            cases.Add((nameof(MissingPhaseAndDuplicateAssetAreRejected), MissingPhaseAndDuplicateAssetAreRejected));
            cases.Add((nameof(UnsupportedSplitAndInvalidHashesAreRejected), UnsupportedSplitAndInvalidHashesAreRejected));
            cases.Add((nameof(UnknownEmotionAndUnsafeAssetNamesAreRejected), UnknownEmotionAndUnsafeAssetNamesAreRejected));
            cases.Add((nameof(DuplicatePortraitAndPhaseIdentityAreRejected), DuplicatePortraitAndPhaseIdentityAreRejected));
            cases.Add((nameof(NonemptyEncodedAndValidFormatPixelHashMismatchesAreRejected), NonemptyEncodedAndValidFormatPixelHashMismatchesAreRejected));
            cases.Add((nameof(Schema2RoundTripsCompleteEmotionSpecificGazePairsAndPolicy), Schema2RoundTripsCompleteEmotionSpecificGazePairsAndPolicy));
            cases.Add((nameof(Schema2RejectsIncompleteGazesAndInvalidAnimationPolicy), Schema2RejectsIncompleteGazesAndInvalidAnimationPolicy));
        }

        private static void ValidManifestResolvesEveryUniqueTextureAndChecksDecodedFacts()
        {
            SplitPortraitManifest manifest = Valid();
            var jsonOptions = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
            string wireJson = System.Text.Json.JsonSerializer.Serialize(manifest, jsonOptions);
            FullSerializer.fsData parsed = FullSerializer.fsJsonParser.Parse(wireJson);
            var serializer = new FullSerializer.fsSerializer();
            SplitPortraitManifest runtimeManifest = null;
            FullSerializer.fsResult deserializeResult = serializer.TryDeserialize(parsed, ref runtimeManifest);
            Check.True(deserializeResult.Succeeded);
            Check.Equal(6, runtimeManifest.portraits[0].emotions.Length);
            Check.Equal(3, runtimeManifest.portraits[0].emotions[0].eyes.Length);
            Check.Equal(9, runtimeManifest.portraits[0].emotions[0].mouths.Length);
            var resolved = new HashSet<string>(StringComparer.Ordinal);
            Check.True(SplitPortraitManifestValidator.TryValidate(runtimeManifest, name =>
            {
                resolved.Add(name);
                return Resolve(name);
            }, out var error), error);
            Check.Equal(72, resolved.Count);
        }

        private static void MissingPhaseAndDuplicateAssetAreRejected()
        {
            SplitPortraitManifest missing = Valid();
            missing.portraits[0].emotions[0].eyes[2] = null;
            Check.True(!SplitPortraitManifestValidator.TryValidate(missing, Resolve, out _));

            SplitPortraitManifest duplicate = Valid();
            duplicate.portraits[0].emotions[1].eyes[0].assetName = duplicate.portraits[0].emotions[0].eyes[0].assetName;
            Check.True(!SplitPortraitManifestValidator.TryValidate(duplicate, Resolve, out _));
        }

        private static void UnsupportedSplitAndInvalidHashesAreRejected()
        {
            SplitPortraitManifest badSplit = Valid();
            badSplit.portraits[0].splitY = 34;
            Check.True(!SplitPortraitManifestValidator.TryValidate(badSplit, Resolve, out _));

            SplitPortraitManifest badHash = Valid();
            badHash.portraits[0].nativeRgbaSha256 = "not-a-hash";
            Check.True(!SplitPortraitManifestValidator.TryValidate(badHash, Resolve, out _));

            SplitPortraitManifest wrongBytes = Valid();
            Check.True(!SplitPortraitManifestValidator.TryValidate(wrongBytes, _ => null, out _));

            bool firstTexture = true;
            Check.True(!SplitPortraitManifestValidator.TryValidate(Valid(), name =>
            {
                SplitPortraitDecodedAsset asset = Resolve(name);
                if (firstTexture) { asset.width--; firstTexture = false; }
                return asset;
            }, out _));
        }

        private static void UnknownEmotionAndUnsafeAssetNamesAreRejected()
        {
            SplitPortraitManifest unknown = Valid();
            unknown.portraits[0].emotions[0].id = "surprised";
            Check.True(!SplitPortraitManifestValidator.TryValidate(unknown, Resolve, out _));

            SplitPortraitManifest unsafeName = Valid();
            unsafeName.portraits[0].emotions[0].mouths[0].assetName = "../outside.png.bytes";
            Check.True(!SplitPortraitManifestValidator.TryValidate(unsafeName, Resolve, out _));

            SplitPortraitManifest wrongPair = Valid();
            wrongPair.portraits[0].bank = "FACES.CIF";
            Check.True(!SplitPortraitManifestValidator.TryValidate(wrongPair, Resolve, out _));
        }

        private static void DuplicatePortraitAndPhaseIdentityAreRejected()
        {
            SplitPortraitManifest duplicatePortrait = Valid();
            duplicatePortrait.portraits = new[] { duplicatePortrait.portraits[0], Valid().portraits[0] };
            Check.True(!SplitPortraitManifestValidator.TryValidate(duplicatePortrait, Resolve, out _));

            SplitPortraitManifest duplicatePhase = Valid();
            duplicatePhase.portraits[0].emotions[0].eyes[1].frame = "open";
            Check.True(!SplitPortraitManifestValidator.TryValidate(duplicatePhase, Resolve, out _));
        }

        private static void NonemptyEncodedAndValidFormatPixelHashMismatchesAreRejected()
        {
            SplitPortraitManifest wrongEncodedHash = Valid();
            Check.True(!SplitPortraitManifestValidator.TryValidate(wrongEncodedHash, name =>
            {
                SplitPortraitDecodedAsset decoded = Resolve(name);
                decoded.encodedBytes = System.Text.Encoding.UTF8.GetBytes("different nonempty encoded bytes");
                return decoded;
            }, out _));

            SplitPortraitManifest wrongPixelHash = Valid();
            Check.True(!SplitPortraitManifestValidator.TryValidate(wrongPixelHash, name =>
            {
                SplitPortraitDecodedAsset decoded = Resolve(name);
                decoded.rgbaSha256 = Hash(0xfe);
                return decoded;
            }, out _));
        }

        private static void Schema2RoundTripsCompleteEmotionSpecificGazePairsAndPolicy()
        {
            SplitPortraitManifest manifest = ValidWithGazes(out Dictionary<string, SplitPortraitDecodedAsset> gazeAssets);
            var jsonOptions = new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
            string wireJson = System.Text.Json.JsonSerializer.Serialize(manifest, jsonOptions);
            FullSerializer.fsData parsed = FullSerializer.fsJsonParser.Parse(wireJson);
            var serializer = new FullSerializer.fsSerializer();
            SplitPortraitManifest runtimeManifest = null;
            FullSerializer.fsResult deserializeResult = serializer.TryDeserialize(parsed, ref runtimeManifest);
            Check.True(deserializeResult.Succeeded);
            Check.Equal(2, runtimeManifest.schemaVersion);
            Check.Equal("left", runtimeManifest.portraits[0].emotions[0].gazes[0].id);
            Check.Equal("right", runtimeManifest.portraits[0].emotions[0].gazes[1].id);
            Check.Equal(3, runtimeManifest.portraits[0].emotions[0].gazes[0].eyes.Length);

            var resolved = new HashSet<string>(StringComparer.Ordinal);
            Check.True(SplitPortraitManifestValidator.TryValidate(runtimeManifest, name =>
            {
                resolved.Add(name);
                SplitPortraitDecodedAsset decoded;
                return gazeAssets.TryGetValue(name, out decoded) ? decoded : Resolve(name);
            }, out var error), error);
            Check.Equal(78, resolved.Count);
        }

        private static void Schema2RejectsIncompleteGazesAndInvalidAnimationPolicy()
        {
            SplitPortraitManifest incompletePhase = ValidWithGazes(out _);
            incompletePhase.portraits[0].emotions[0].gazes[1].eyes[2] = null;
            Check.True(!SplitPortraitManifestValidator.TryValidateStructure(incompletePhase, out _));

            SplitPortraitManifest incompletePair = ValidWithGazes(out _);
            incompletePair.portraits[0].emotions[0].gazes = new[] { incompletePair.portraits[0].emotions[0].gazes[0] };
            Check.True(!SplitPortraitManifestValidator.TryValidateStructure(incompletePair, out _));

            SplitPortraitManifest duplicateDirection = ValidWithGazes(out _);
            duplicateDirection.portraits[0].emotions[0].gazes[1].id = "left";
            Check.True(!SplitPortraitManifestValidator.TryValidateStructure(duplicateDirection, out _));

            SplitPortraitManifest invalidHash = ValidWithGazes(out _);
            invalidHash.animationPolicySha256 = "invalid";
            Check.True(!SplitPortraitManifestValidator.TryValidateStructure(invalidHash, out _));

            SplitPortraitManifest invalidPolicy = ValidWithGazes(out _);
            invalidPolicy.animationPolicy.blinkDurationMs = 0;
            Check.True(!SplitPortraitManifestValidator.TryValidateStructure(invalidPolicy, out _));

            SplitPortraitManifest legacyWithGazes = ValidWithGazes(out _);
            legacyWithGazes.schemaVersion = 1;
            Check.True(!SplitPortraitManifestValidator.TryValidateStructure(legacyWithGazes, out _));
        }

        private static SplitPortraitManifest ValidWithGazes(out Dictionary<string, SplitPortraitDecodedAsset> gazeAssets)
        {
            SplitPortraitManifest manifest = Valid();
            manifest.schemaVersion = 2;
            manifest.animationPolicy = new SplitPortraitAnimationPolicy();
            manifest.animationPolicySha256 = Hash(9);
            gazeAssets = new Dictionary<string, SplitPortraitDecodedAsset>(StringComparer.Ordinal);
            string[] directions = { "left", "right" };
            string[] phases = { "open", "half", "closed" };
            SplitPortraitEmotion emotion = manifest.portraits[0].emotions[0];
            emotion.gazes = new SplitPortraitGaze[directions.Length];
            for (int direction = 0; direction < directions.Length; direction++)
            {
                var gaze = new SplitPortraitGaze { id = directions[direction], eyes = new SplitPortraitAsset[phases.Length] };
                for (int phase = 0; phase < phases.Length; phase++)
                {
                    string name = "portrait-common-077-neutral-eye-" + directions[direction] + "-" + phases[phase] + ".png.bytes";
                    SplitPortraitAsset asset = Asset(name, phases[phase], 64, 35, (byte)(90 + direction * 3 + phase));
                    gaze.eyes[phase] = asset;
                    gazeAssets.Add(name, ResolvedAsset(name, asset.width, asset.height, asset.rgbaSha256));
                }
                emotion.gazes[direction] = gaze;
            }
            return manifest;
        }

        private static SplitPortraitManifest Valid()
        {
            var portrait = new SplitPortraitEntry
            {
                archive = "CommonFaces",
                bank = "TFAC00I0.RCI",
                record = 77,
                sourceImageSha256 = Hash(3),
                nativeRgbaSha256 = Hash(7),
                width = 64,
                height = 64,
                splitY = 35,
                emotions = new SplitPortraitEmotion[6]
            };
            string[] emotions = { "neutral", "happy", "angry", "sad", "afraid", "guarded" };
            string[] phases = { "open", "half", "closed" };
            string[] shapes = { "A", "B", "C", "D", "E", "F", "G", "H", "X" };
            for (int emotion = 0; emotion < emotions.Length; emotion++)
            {
                var set = new SplitPortraitEmotion
                {
                    id = emotions[emotion],
                    eyes = new SplitPortraitAsset[phases.Length],
                    mouths = new SplitPortraitAsset[shapes.Length]
                };
                for (int phase = 0; phase < phases.Length; phase++)
                    set.eyes[phase] = Asset("portrait-common-077-" + emotions[emotion] + "-eye-" + phases[phase] + ".png.bytes", phases[phase], 64, 35, (byte)(emotion * 12 + phase));
                for (int shape = 0; shape < shapes.Length; shape++)
                    set.mouths[shape] = Asset("portrait-common-077-" + emotions[emotion] + "-mouth-" + shapes[shape] + ".png.bytes", shapes[shape], 64, 29, (byte)(emotion * 12 + shape + 3));
                portrait.emotions[emotion] = set;
            }
            return new SplitPortraitManifest { schemaVersion = 1, portraits = new[] { portrait } };
        }

        private static SplitPortraitAsset Asset(string name, string frame, int width, int height, byte marker)
        {
            byte[] bytes = AssetBytes(name);
            return new SplitPortraitAsset
            {
                assetName = name,
                frame = frame,
                sha256 = SplitPortraitManifestValidator.Sha256(bytes),
                width = width,
                height = height,
                rgbaSha256 = Hash(marker)
            };
        }

        private static SplitPortraitDecodedAsset Resolve(string name)
        {
            SplitPortraitAsset asset = null;
            SplitPortraitManifest manifest = Valid();
            foreach (SplitPortraitEmotion emotion in manifest.portraits[0].emotions)
            {
                foreach (SplitPortraitAsset candidate in emotion.eyes) if (candidate.assetName == name) asset = candidate;
                foreach (SplitPortraitAsset candidate in emotion.mouths) if (candidate.assetName == name) asset = candidate;
            }
            return asset == null ? null : ResolvedAsset(name, asset.width, asset.height, asset.rgbaSha256);
        }

        private static SplitPortraitDecodedAsset ResolvedAsset(string name, int width, int height, string rgbaHash)
        {
            byte[] bytes = AssetBytes(name);
            return new SplitPortraitDecodedAsset { encodedBytes = bytes, width = width, height = height, rgbaSha256 = rgbaHash };
        }
        private static byte[] AssetBytes(string name) { return System.Text.Encoding.UTF8.GetBytes(name ?? string.Empty); }
        private static string Hash(byte marker)
        {
            string pair = marker.ToString("x2");
            return string.Concat(pair, pair, pair, pair, pair, pair, pair, pair,
                pair, pair, pair, pair, pair, pair, pair, pair,
                pair, pair, pair, pair, pair, pair, pair, pair,
                pair, pair, pair, pair, pair, pair, pair, pair);
        }
    }
}
