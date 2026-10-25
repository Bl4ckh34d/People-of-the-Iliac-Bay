using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Collections.ObjectModel;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    public sealed class BreezeTtsTrialWave
    {
        public int SampleRate { get; internal set; }
        public int Channels { get; internal set; }
        public int SampleFrames { get; internal set; }
        public float[] Samples { get; internal set; }
    }

    /// <summary>Typed, deliberately controlled prosody direction for contrast samples.</summary>
    public sealed class BreezeTtsTrialControlledDirection
    {
        public string Emotion { get; private set; }
        public string DeliveryMode { get; private set; }

        public BreezeTtsTrialControlledDirection(string emotion, string deliveryMode)
        {
            if (emotion != "neutral" && emotion != "joy" && emotion != "anger" &&
                emotion != "concern" && emotion != "grief" && emotion != "fear")
                throw new ArgumentException("Unsupported controlled emotion.", nameof(emotion));
            if (deliveryMode != "normal" && deliveryMode != "whisper")
                throw new ArgumentException("Unsupported controlled delivery mode.", nameof(deliveryMode));
            Emotion = emotion;
            DeliveryMode = deliveryMode;
        }

        internal string ToJson()
        {
            return "{\"emotion\":" + BreezeTtsTrialProtocol.Quote(Emotion) +
                ",\"deliveryMode\":" + BreezeTtsTrialProtocol.Quote(DeliveryMode) + "}";
        }
    }

    /// <summary>Immutable, native-authored emotional and identity context for speech delivery.</summary>
    public sealed class BreezeTtsTrialDeliveryContext
    {
        public const int CurrentSchemaVersion = 1;
        private static readonly string[] TraitKeys =
        {
            "warmth", "patience", "curiosity", "privacy", "manner", "outlook", "emotional stability",
            "ambition", "integrity", "courage", "dutifulness", "vocabulary", "eloquence", "status attitude",
            "humor", "self esteem", "sensitivity"
        };
        private static readonly string[][] TraitValues =
        {
            new[] { "warm", "reserved", "guarded", "affectionate" },
            new[] { "patient", "brisk", "easily wearied", "unhurried" },
            new[] { "inquisitive", "practical", "observant", "self-absorbed" },
            new[] { "open", "private", "selective" },
            new[] { "plainspoken", "courteous", "dry-humored", "earnest" },
            new[] { "optimistic", "pragmatic", "pessimistic", "cynical" },
            new[] { "even-tempered", "sensitive", "volatile", "resilient" },
            new[] { "content", "aspiring", "driven", "status-seeking" },
            new[] { "honest", "deceitful", "self-serving", "principled", "sly", "manipulative" },
            new[] { "cautious", "bold", "reckless", "steadfast" },
            new[] { "independent", "conscientious", "obliging", "self-directed" },
            new[] { "plain", "precise", "colorful", "earthy", "coarse" },
            new[] { "plain", "direct", "articulate", "eloquent" },
            new[] { "egalitarian", "deferential", "status-conscious", "condescending" },
            new[] { "playful", "wry", "solemn", "mischievous" },
            new[] { "insecure", "grounded", "confident" },
            new[] { "thick-skinned", "attuned", "highly sensitive" }
        };

        public int SchemaVersion { get { return Traits.Count == 3 ? 2 : CurrentSchemaVersion; } }
        public string PersonId { get; private set; }
        public int Mood { get; private set; }
        public bool DispositionKnown { get; private set; }
        public int Disposition { get; private set; }
        public bool TrustKnown { get; private set; }
        public int Trust { get; private set; }
        public int Fear { get; private set; }
        public int Resentment { get; private set; }
        public int Encounters { get; private set; }
        public IReadOnlyDictionary<string, string> Traits { get; private set; }

        public BreezeTtsTrialDeliveryContext(string personId, int mood, bool dispositionKnown, int disposition,
            bool trustKnown, int trust, int fear, int resentment, int encounters,
            IReadOnlyDictionary<string, string> traits)
        {
            if (string.IsNullOrWhiteSpace(personId) || personId.Length > 240 || ContainsControl(personId))
                throw new ArgumentException("Invalid delivery context identity.", nameof(personId));
            RequireRange(mood, -100, 100, nameof(mood));
            RequireKnownDimension(dispositionKnown, disposition, nameof(disposition));
            RequireKnownDimension(trustKnown, trust, nameof(trust));
            RequireRange(fear, 0, 100, nameof(fear));
            RequireRange(resentment, 0, 100, nameof(resentment));
            if (encounters < 0 || encounters > 1000000)
                throw new ArgumentOutOfRangeException(nameof(encounters));
            if (traits == null || (traits.Count != 3 && traits.Count != TraitKeys.Length))
                throw new ArgumentException("Delivery context requires three defining traits or a legacy 17-trait profile.", nameof(traits));
            var traitCopy = new Dictionary<string, string>(traits.Count, StringComparer.Ordinal);
            if (traits.Count == 3)
            {
                foreach (var trait in traits)
                {
                    bool valid = false;
                    foreach (var axis in NpcPersonalityProfile.Axes)
                        if (axis.Name == trait.Key && Contains(axis.Values, trait.Value)) { valid = true; break; }
                    if (!valid) throw new ArgumentException("Unsupported defining personality trait.", nameof(traits));
                    traitCopy.Add(trait.Key, trait.Value);
                }
            }
            else for (int i = 0; i < TraitKeys.Length; i++)
            {
                string value;
                if (!traits.TryGetValue(TraitKeys[i], out value) || !Contains(TraitValues[i], value))
                    throw new ArgumentException("Delivery context contains an incomplete or unsupported saved personality trait.", nameof(traits));
                traitCopy.Add(TraitKeys[i], value);
            }
            PersonId = personId;
            Mood = mood;
            DispositionKnown = dispositionKnown;
            Disposition = disposition;
            TrustKnown = trustKnown;
            Trust = trust;
            Fear = fear;
            Resentment = resentment;
            Encounters = encounters;
            Traits = new ReadOnlyDictionary<string, string>(traitCopy);
        }

        internal string ToJson()
        {
            var json = new StringBuilder("{\"schemaVersion\":").Append(SchemaVersion).Append(",\"personId\":").Append(BreezeTtsTrialProtocol.Quote(PersonId))
                .Append(",\"mood\":").Append(Number(Mood))
                .Append(",\"dispositionKnown\":").Append(DispositionKnown ? "true" : "false")
                .Append(",\"disposition\":").Append(Number(Disposition))
                .Append(",\"trustKnown\":").Append(TrustKnown ? "true" : "false")
                .Append(",\"trust\":").Append(Number(Trust))
                .Append(",\"fear\":").Append(Number(Fear))
                .Append(",\"resentment\":").Append(Number(Resentment))
                .Append(",\"encounters\":").Append(Number(Encounters))
                .Append(",\"traits\":{");
            bool first = true;
            foreach (var trait in Traits)
            {
                if (!first) json.Append(','); first = false;
                json.Append(BreezeTtsTrialProtocol.Quote(trait.Key)).Append(':')
                    .Append(BreezeTtsTrialProtocol.Quote(trait.Value));
            }
            return json.Append("}}").ToString();
        }

        private static bool Contains(string[] values, string value)
        {
            if (value == null) return false;
            for (int i = 0; i < values.Length; i++)
                if (string.Equals(values[i], value, StringComparison.Ordinal)) return true;
            return false;
        }

        private static string Number(int value)
        { return value.ToString(CultureInfo.InvariantCulture); }

        private static void RequireKnownDimension(bool known, int value, string name)
        {
            if (known) RequireRange(value, 0, 100, name);
            else if (value != 0) throw new ArgumentOutOfRangeException(name, "Unknown dimensions must use the zero placeholder and known flag.");
        }

        private static void RequireRange(int value, int minimum, int maximum, string name)
        {
            if (value < minimum || value > maximum) throw new ArgumentOutOfRangeException(name);
        }

        private static bool ContainsControl(string value)
        {
            for (int i = 0; i < value.Length; i++) if (char.IsControl(value[i])) return true;
            return false;
        }
    }

    public static class BreezeTtsTrialProtocol
    {
        public const int MaximumTextCharacters = 2048;
        public const int MaximumWaveBytes = 16 * 1024 * 1024;
        public const int MaximumRequestCharacters = 16384;
        private const int MaximumWaveSeconds = 60;

        public static bool TryBuildRequestJson(string requestId, string conversationToken, string personId,
            string displayName, string text, long textReadyMonotonicTicks, long timestampFrequency, out string json)
        {
            return TryBuildRequestJson(requestId, conversationToken, personId, displayName, text,
                textReadyMonotonicTicks, timestampFrequency, null, out json);
        }

        public static bool TryBuildRequestJson(string requestId, string conversationToken, string personId,
            string displayName, string text, long textReadyMonotonicTicks, long timestampFrequency,
            string voiceGender, out string json)
        {
            return TryBuildRequestJson(requestId, conversationToken, personId, displayName, text,
                textReadyMonotonicTicks, timestampFrequency, voiceGender, null, out json);
        }

        public static bool TryBuildRequestJson(string requestId, string conversationToken, string personId,
            string displayName, string text, long textReadyMonotonicTicks, long timestampFrequency,
            string voiceGender, BreezeTtsTrialDeliveryContext deliveryContext, out string json)
        {
            return TryBuildRequestJson(requestId, conversationToken, personId, displayName, text,
                textReadyMonotonicTicks, timestampFrequency, voiceGender, deliveryContext, null, null, out json);
        }

        public static bool TryBuildRequestJson(string requestId, string conversationToken, string personId,
            string displayName, string text, long textReadyMonotonicTicks, long timestampFrequency,
            string voiceGender, BreezeTtsTrialDeliveryContext deliveryContext, string sampleKind,
            BreezeTtsTrialControlledDirection controlledDirection, out string json)
        {
            return TryBuildRequestJson(requestId, conversationToken, personId, displayName, text,
                textReadyMonotonicTicks, timestampFrequency, voiceGender, deliveryContext, sampleKind,
                controlledDirection, null, out json);
        }

        public static bool TryBuildRequestJson(string requestId, string conversationToken, string personId,
            string displayName, string text, long textReadyMonotonicTicks, long timestampFrequency,
            string voiceGender, BreezeTtsTrialDeliveryContext deliveryContext, string sampleKind,
            BreezeTtsTrialControlledDirection controlledDirection, string contrastLabel, out string json)
        {
            json = null;
            if (!Identifier(requestId, 128) || !Identifier(conversationToken, 128) || !Identifier(personId, 240) ||
                !Text(displayName, 96, false) || !Text(text, MaximumTextCharacters, true) ||
                (voiceGender != null && voiceGender != "female" && voiceGender != "male" && voiceGender != "unknown") ||
                (deliveryContext != null && !string.Equals(deliveryContext.PersonId, personId, StringComparison.Ordinal)) ||
                (sampleKind != null && sampleKind != "native-dialogue" && sampleKind != "controlled-voice-contrast") ||
                (sampleKind == "controlled-voice-contrast") != (controlledDirection != null) ||
                (contrastLabel != null && (sampleKind != "controlled-voice-contrast" ||
                    (contrastLabel != "neutral" && contrastLabel != "positiveMood" && contrastLabel != "lowMood" &&
                     contrastLabel != "highFear" && contrastLabel != "highResentment" && contrastLabel != "whisper"))) ||
                textReadyMonotonicTicks < 0 || timestampFrequency <= 0) return false;

            json = "{\"schemaVersion\":1,\"mode\":\"full-wav-pilot\",\"requestId\":" + Quote(requestId) +
                ",\"conversationToken\":" + Quote(conversationToken) + ",\"personId\":" + Quote(personId) +
                (voiceGender == null ? string.Empty : ",\"voiceGender\":" + Quote(voiceGender)) +
                (deliveryContext == null ? string.Empty : ",\"deliveryContext\":" + deliveryContext.ToJson()) +
                (sampleKind == null ? string.Empty : ",\"sampleKind\":" + Quote(sampleKind)) +
                (controlledDirection == null ? string.Empty : ",\"controlledDirection\":" + controlledDirection.ToJson()) +
                (contrastLabel == null ? string.Empty : ",\"contrastLabel\":" + Quote(contrastLabel)) +
                ",\"displayName\":" + Quote(displayName) + ",\"text\":" + Quote(text) +
                ",\"textReadyMonotonicTicks\":" + textReadyMonotonicTicks.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                ",\"timestampFrequency\":" + timestampFrequency.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";
            if (json.Length > MaximumRequestCharacters) { json = null; return false; }
            return true;
        }

        public static bool TryResolveResponseWavePath(string responseDirectory, string waveFile, out string resolvedPath)
        {
            resolvedPath = null;
            if (string.IsNullOrWhiteSpace(responseDirectory) || !Path.IsPathRooted(responseDirectory) ||
                string.IsNullOrWhiteSpace(waveFile) || waveFile.Length > 128 ||
                !string.Equals(Path.GetFileName(waveFile), waveFile, StringComparison.Ordinal) ||
                waveFile.IndexOf('/') >= 0 || waveFile.IndexOf('\\') >= 0 ||
                !string.Equals(Path.GetExtension(waveFile), ".wav", StringComparison.OrdinalIgnoreCase)) return false;
            foreach (char c in Path.GetInvalidFileNameChars())
                if (waveFile.IndexOf(c) >= 0) return false;
            try
            {
                string root = Path.GetFullPath(responseDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string full = Path.GetFullPath(Path.Combine(root, waveFile));
                string expectedDirectory = root + Path.DirectorySeparatorChar;
                StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                if (!full.StartsWith(expectedDirectory, comparison) ||
                    !string.Equals(Path.GetDirectoryName(full), root, comparison)) return false;
                resolvedPath = full;
                return true;
            }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
            catch (PathTooLongException) { return false; }
        }

        public static bool TryMatchFailureTerminal(int schemaVersion, string requestId, string conversationToken,
            string personId, string status, string expectedRequestId, string expectedConversationToken,
            string expectedPersonId)
        {
            return schemaVersion == 1 && string.Equals(status, "failed", StringComparison.Ordinal) &&
                Identifier(requestId, 128) && Identifier(conversationToken, 128) && Identifier(personId, 240) &&
                Identifier(expectedRequestId, 128) && Identifier(expectedConversationToken, 128) &&
                Identifier(expectedPersonId, 240) &&
                string.Equals(requestId, expectedRequestId, StringComparison.Ordinal) &&
                string.Equals(conversationToken, expectedConversationToken, StringComparison.Ordinal) &&
                string.Equals(personId, expectedPersonId, StringComparison.Ordinal);
        }

        /// <summary>
        /// Returns only the exact ephemeral request, response manifests, and WAV paths owned by one request.
        /// Playback measurements and voice profile/reference assets are intentionally excluded.
        /// </summary>
        public static bool TryBuildConversationCleanupPaths(string requestDirectory, string responseDirectory,
            string requestId, string waveFile, out string[] paths)
        {
            paths = null;
            if (string.IsNullOrWhiteSpace(requestDirectory) || !Path.IsPathRooted(requestDirectory) ||
                string.IsNullOrWhiteSpace(responseDirectory) || !Path.IsPathRooted(responseDirectory) ||
                !Identifier(requestId, 128) || !string.Equals(Path.GetFileName(requestId), requestId, StringComparison.Ordinal) ||
                requestId.IndexOf('/') >= 0 || requestId.IndexOf('\\') >= 0) return false;

            try
            {
                string requestRoot = Path.GetFullPath(requestDirectory);
                string responseRoot = Path.GetFullPath(responseDirectory);
                string requestPath = Path.Combine(requestRoot, requestId + ".request.json");
                string manifestPath = Path.Combine(responseRoot, requestId + ".manifest.json");
                string failurePath = Path.Combine(responseRoot, requestId + ".failed.json");
                if (string.IsNullOrWhiteSpace(waveFile))
                {
                    paths = new[] { requestPath, manifestPath, failurePath };
                    return true;
                }

                string wavePath;
                if (!TryResolveResponseWavePath(responseRoot, waveFile, out wavePath)) return false;
                paths = new[] { requestPath, manifestPath, failurePath, wavePath };
                return true;
            }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
            catch (PathTooLongException) { return false; }
        }

        public static bool TryDecodePcm16Wave(byte[] bytes, out BreezeTtsTrialWave wave)
        {
            wave = null;
            if (bytes == null || bytes.Length < 44 || bytes.Length > MaximumWaveBytes ||
                !Tag(bytes, 0, "RIFF") || !Tag(bytes, 8, "WAVE") || ReadU32(bytes, 4) != bytes.Length - 8) return false;

            int offset = 12;
            int channels = 0, sampleRate = 0, byteRate = 0, blockAlign = 0, bitsPerSample = 0;
            int dataOffset = -1, dataLength = 0;
            bool formatFound = false;
            while (offset <= bytes.Length - 8)
            {
                int length = ReadU32(bytes, offset + 4);
                int payload = offset + 8;
                if (length < 0 || length > bytes.Length - payload) return false;
                if (Tag(bytes, offset, "fmt "))
                {
                    if (formatFound || length < 16) return false;
                    int format = ReadU16(bytes, payload);
                    channels = ReadU16(bytes, payload + 2);
                    sampleRate = ReadU32(bytes, payload + 4);
                    byteRate = ReadU32(bytes, payload + 8);
                    blockAlign = ReadU16(bytes, payload + 12);
                    bitsPerSample = ReadU16(bytes, payload + 14);
                    formatFound = format == 1;
                }
                else if (Tag(bytes, offset, "data"))
                {
                    if (dataOffset >= 0) return false;
                    dataOffset = payload;
                    dataLength = length;
                }
                long next = (long)payload + length + (length & 1);
                if (next > bytes.Length) return false;
                offset = (int)next;
            }

            if (!formatFound || dataOffset < 0 || channels < 1 || channels > 2 || sampleRate < 8000 || sampleRate > 192000 ||
                bitsPerSample != 16 || blockAlign != channels * 2 || byteRate != sampleRate * blockAlign ||
                dataLength < blockAlign || dataLength % blockAlign != 0) return false;
            int frames = dataLength / blockAlign;
            if (frames > sampleRate * MaximumWaveSeconds) return false;

            var samples = new float[dataLength / 2];
            for (int i = 0; i < samples.Length; i++)
            {
                int index = dataOffset + i * 2;
                short value = unchecked((short)(bytes[index] | (bytes[index + 1] << 8)));
                samples[i] = value / 32768f;
            }
            wave = new BreezeTtsTrialWave { SampleRate = sampleRate, Channels = channels,
                SampleFrames = frames, Samples = samples };
            return true;
        }

        public static void WriteAtomically(string directory, string fileName, string contents)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathRooted(directory) ||
                string.IsNullOrWhiteSpace(fileName) || fileName.Length > 160 ||
                !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) ||
                fileName.IndexOf('/') >= 0 || fileName.IndexOf('\\') >= 0 ||
                contents == null || contents.Length > MaximumRequestCharacters)
                throw new ArgumentException("Invalid bounded TTS trial file.");
            string root = Path.GetFullPath(directory);
            Directory.CreateDirectory(root);
            string destination = Path.Combine(root, fileName);
            string temporary = Path.Combine(root, fileName + ".tmp." + Guid.NewGuid().ToString("N"));
            try
            {
                byte[] data = new UTF8Encoding(false).GetBytes(contents);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(data, 0, data.Length); stream.Flush(); }
                File.Move(temporary, destination);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        /// <summary>Atomically replaces a mutable protocol file such as a streaming acknowledgement.</summary>
        public static void ReplaceAtomically(string directory, string fileName, string contents)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathRooted(directory) ||
                string.IsNullOrWhiteSpace(fileName) || fileName.Length > 160 ||
                !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal) ||
                fileName.IndexOf('/') >= 0 || fileName.IndexOf('\\') >= 0 ||
                contents == null || contents.Length > MaximumRequestCharacters)
                throw new ArgumentException("Invalid bounded TTS trial file.");
            string root = Path.GetFullPath(directory);
            Directory.CreateDirectory(root);
            string destination = Path.Combine(root, fileName);
            string temporary = Path.Combine(root, fileName + ".tmp." + Guid.NewGuid().ToString("N"));
            try
            {
                byte[] data = new UTF8Encoding(false).GetBytes(contents);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(data, 0, data.Length); stream.Flush(); }
                if (File.Exists(destination)) File.Replace(temporary, destination, null);
                else File.Move(temporary, destination);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }

        private static bool Identifier(string value, int maximum)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > maximum) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsControl(c) || char.IsSurrogate(c)) return false;
            }
            return true;
        }

        private static bool Text(string value, int maximum, bool allowLineBreaks)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > maximum) return false;
            for (int i = 0; i < value.Length; i++)
                if (char.IsControl(value[i]) && !(allowLineBreaks && (value[i] == '\r' || value[i] == '\n' || value[i] == '\t')))
                    return false;
            return true;
        }

        internal static string Quote(string value)
        {
            var result = new StringBuilder(value.Length + 2).Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': result.Append("\\\""); break;
                    case '\\': result.Append("\\\\"); break;
                    case '\b': result.Append("\\b"); break;
                    case '\f': result.Append("\\f"); break;
                    case '\n': result.Append("\\n"); break;
                    case '\r': result.Append("\\r"); break;
                    case '\t': result.Append("\\t"); break;
                    default:
                        if (c < 0x20) result.Append("\\u").Append(((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture));
                        else result.Append(c);
                        break;
                }
            }
            return result.Append('"').ToString();
        }

        private static bool Tag(byte[] bytes, int offset, string value)
        {
            if (offset < 0 || offset > bytes.Length - value.Length) return false;
            for (int i = 0; i < value.Length; i++) if (bytes[offset + i] != (byte)value[i]) return false;
            return true;
        }

        private static int ReadU16(byte[] bytes, int offset)
        { return bytes[offset] | (bytes[offset + 1] << 8); }

        private static int ReadU32(byte[] bytes, int offset)
        {
            uint value = (uint)(bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24));
            return value > int.MaxValue ? -1 : (int)value;
        }
    }
}
