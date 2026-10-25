using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int Main()
    {
        int failures = 0;
        Run("request JSON escapes displayed text and preserves IDs", RequestJsonEscapesText, ref failures);
        Run("request JSON preserves optional voice gender and legacy overload", RequestVoiceGenderIsOptional, ref failures);
        Run("request JSON rejects unrecognized voice gender", RequestRejectsInvalidVoiceGender, ref failures);
        Run("request JSON carries validated typed delivery context", RequestCarriesDeliveryContext, ref failures);
        Run("delivery context preserves unknown trust without inventing a value", DeliveryContextPreservesUnknownTrust, ref failures);
        Run("delivery context rejects incomplete personality vocabulary", DeliveryContextRejectsIncompleteTraits, ref failures);
        Run("delivery context rejects an invalid scalar range", DeliveryContextRejectsInvalidRange, ref failures);
        Run("request rejects delivery context for a different person", RequestRejectsMismatchedDeliveryPerson, ref failures);
        Run("controlled contrast request carries typed provenance and directions", RequestCarriesControlledContrast, ref failures);
        Run("contrast labels are allowlisted and restricted to controlled samples", ContrastLabelsAreValidated, ref failures);
        Run("controlled directions require a controlled sample and supported values", ControlledDirectionRejectsInvalidContract, ref failures);
        Run("request JSON rejects oversized text", RequestRejectsOversizedText, ref failures);
        Run("response path accepts one WAV basename under output root", ResponsePathIsContained, ref failures);
        Run("response path rejects traversal and rooted paths", ResponsePathRejectsTraversal, ref failures);
        Run("PCM16 decoder returns exact mono and stereo frame counts", Pcm16Decode, ref failures);
        Run("PCM16 decoder rejects malformed and unsupported data", Pcm16RejectsMalformed, ref failures);
        Run("atomic protocol write leaves complete final file", AtomicWrite, ref failures);
        Run("immutable atomic write rejects overwrite and preserves original", AtomicWriteRejectsOverwrite, ref failures);
        Run("atomic replacement works while reader allows delete sharing", AtomicReplaceWithDeleteSharedReader, ref failures);
        Run("mutable atomic replacement supports repeated acknowledgement updates", AtomicReplaceWrite, ref failures);
        Run("failure terminal requires exact request, conversation, and person binding", FailureTerminalRequiresExactIdentity, ref failures);
        Run("cleanup plan owns exact request, manifests, and WAV files", CleanupPlanIsExact, ref failures);
        Run("cleanup plan rejects an unowned response path", CleanupPlanRejectsTraversal, ref failures);
        Console.WriteLine("Breeze TTS trial protocol: " + (22 - failures) + "/22 passed.");
        return failures == 0 ? 0 : 1;
    }

    private static void RequestJsonEscapesText()
    {
        string json;
        Require(BreezeTtsTrialProtocol.TryBuildRequestJson("req-1", "life-2", "person-3", "Mira \"North\"",
            "Hello\nagain.", 1234, 1000, out json), "valid request rejected");
        Require(json.Contains("\"requestId\":\"req-1\""), "request ID missing");
        Require(json.Contains("\"conversationToken\":\"life-2\""), "conversation token missing");
        Require(json.Contains("Mira \\\"North\\\""), "display name was not JSON escaped");
        Require(json.Contains("Hello\\nagain."), "line break was not JSON escaped");
        Require(json.Contains("\"mode\":\"full-wav-pilot\""), "request mislabeled streaming mode");
        Require(!json.Contains("\"voiceGender\":"), "legacy overload unexpectedly added voice gender");
    }

    private static void RequestVoiceGenderIsOptional()
    {
        string json;
        Require(BreezeTtsTrialProtocol.TryBuildRequestJson("req-f", "life", "person-f", "Gwen", "Hello.",
            1, 1000, "female", out json), "female voice gender rejected");
        Require(json.Contains("\"voiceGender\":\"female\""), "female voice gender missing");
        Require(BreezeTtsTrialProtocol.TryBuildRequestJson("req-m", "life", "person-m", "Gareth", "Hello.",
            1, 1000, "male", out json), "male voice gender rejected");
        Require(json.Contains("\"voiceGender\":\"male\""), "male voice gender missing");
        Require(BreezeTtsTrialProtocol.TryBuildRequestJson("req-u", "life", "person-u", "Speaker", "Hello.",
            1, 1000, "unknown", out json), "unknown voice gender rejected");
        Require(json.Contains("\"voiceGender\":\"unknown\""), "unknown voice gender missing");
    }

    private static void RequestRejectsInvalidVoiceGender()
    {
        string json;
        Require(!BreezeTtsTrialProtocol.TryBuildRequestJson("req", "life", "person", "npc", "Hello.",
            1, 1000, "Female", out json), "noncanonical voice gender accepted");
        Require(json == null, "rejected voice gender returned output JSON");
    }

    private static void RequestCarriesDeliveryContext()
    {
        var context = new BreezeTtsTrialDeliveryContext("person-4", -37, true, 82, true, 63,
            24, 19, 7, CompleteTraits());
        string json;
        Require(BreezeTtsTrialProtocol.TryBuildRequestJson("req-ctx", "life", "person-4", "Gwen",
            "I remember you.", 1, 1000, "female", context, out json), "valid delivery context request rejected");
        Require(json.Contains("\"deliveryContext\":{\"schemaVersion\":1,\"personId\":\"person-4\",\"mood\":-37"),
            "delivery context identity or mood missing");
        Require(json.Contains("\"dispositionKnown\":true,\"disposition\":82"), "disposition fields missing");
        Require(json.Contains("\"trustKnown\":true,\"trust\":63"), "trust fields missing");
        Require(json.Contains("\"fear\":24,\"resentment\":19,\"encounters\":7"), "identity history fields missing");
        Require(json.Contains("\"traits\":{\"warmth\":\"warm\""), "stable trait object missing");
        Require(json.Contains("\"sensitivity\":\"attuned\"}}"), "last stable trait missing");
    }

    private static void DeliveryContextPreservesUnknownTrust()
    {
        var context = new BreezeTtsTrialDeliveryContext("person", 0, false, 0, false, 0,
            0, 0, 0, CompleteTraits());
        string json;
        Require(BreezeTtsTrialProtocol.TryBuildRequestJson("req-unknown", "life", "person", "NPC",
            "Hello.", 1, 1000, null, context, out json), "unknown trust context rejected");
        Require(json.Contains("\"dispositionKnown\":false,\"disposition\":0"), "unknown disposition lost known flag");
        Require(json.Contains("\"trustKnown\":false,\"trust\":0"), "unknown trust was not explicitly marked unknown");
    }

    private static void DeliveryContextRejectsIncompleteTraits()
    {
        var traits = CompleteTraits();
        traits.Remove("sensitivity");
        bool rejected = false;
        try { new BreezeTtsTrialDeliveryContext("person", 0, false, 0, false, 0, 0, 0, 0, traits); }
        catch (ArgumentException) { rejected = true; }
        Require(rejected, "incomplete stable personality vocabulary accepted");
    }

    private static void DeliveryContextRejectsInvalidRange()
    {
        bool rejected = false;
        try { new BreezeTtsTrialDeliveryContext("person", 101, false, 0, false, 0, 0, 0, 0, CompleteTraits()); }
        catch (ArgumentOutOfRangeException) { rejected = true; }
        Require(rejected, "out-of-range mood accepted");
    }

    private static void RequestRejectsMismatchedDeliveryPerson()
    {
        var context = new BreezeTtsTrialDeliveryContext("person-a", 0, false, 0, false, 0,
            0, 0, 0, CompleteTraits());
        string json;
        Require(!BreezeTtsTrialProtocol.TryBuildRequestJson("req-wrong-person", "life", "person-b", "NPC",
            "Hello.", 1, 1000, null, context, out json), "mismatched identity delivery context accepted");
        Require(json == null, "rejected context request returned JSON");
    }

    private static void RequestCarriesControlledContrast()
    {
        var direction = new BreezeTtsTrialControlledDirection("concern", "whisper");
        string json;
        Require(BreezeTtsTrialProtocol.TryBuildRequestJson("req-contrast", "life", "person", "NPC",
            "I have something to tell you.", 1, 1000, "female", null,
            "controlled-voice-contrast", direction, out json), "valid controlled contrast request rejected");
        Require(json.Contains("\"sampleKind\":\"controlled-voice-contrast\""), "controlled sample provenance missing");
        Require(json.Contains("\"controlledDirection\":{\"emotion\":\"concern\",\"deliveryMode\":\"whisper\"}"),
            "typed controlled delivery direction missing");
    }

    private static void ContrastLabelsAreValidated()
    {
        var direction = new BreezeTtsTrialControlledDirection("neutral", "normal");
        string json;
        Require(BreezeTtsTrialProtocol.TryBuildRequestJson("req-labeled", "life", "person", "NPC",
            "A labeled line.", 1, 1000, null, null, "controlled-voice-contrast", direction,
            "highFear", out json), "canonical contrast label rejected");
        Require(json.Contains("\"contrastLabel\":\"highFear\""), "contrast label missing from request");
        Require(!BreezeTtsTrialProtocol.TryBuildRequestJson("req-bad-label", "life", "person", "NPC",
            "A line.", 1, 1000, null, null, "controlled-voice-contrast", direction,
            "invented", out json), "unknown contrast label accepted");
        Require(!BreezeTtsTrialProtocol.TryBuildRequestJson("req-native-label", "life", "person", "NPC",
            "A line.", 1, 1000, null, null, null, null, "neutral", out json),
            "contrast label accepted for native dialogue");
    }

    private static void ControlledDirectionRejectsInvalidContract()
    {
        bool invalidEmotion = false;
        try { new BreezeTtsTrialControlledDirection("sad", "normal"); }
        catch (ArgumentException) { invalidEmotion = true; }
        Require(invalidEmotion, "unsupported controlled emotion accepted");

        var direction = new BreezeTtsTrialControlledDirection("neutral", "normal");
        string json;
        Require(!BreezeTtsTrialProtocol.TryBuildRequestJson("req-native-direction", "life", "person", "NPC",
            "Hello.", 1, 1000, null, null, "native-dialogue", direction, out json),
            "controlled direction accepted for native-dialogue sample");
        Require(json == null, "rejected controlled direction returned JSON");

        Require(!BreezeTtsTrialProtocol.TryBuildRequestJson("req-missing-direction", "life", "person", "NPC",
            "Hello.", 1, 1000, null, null, "controlled-voice-contrast", null, out json),
            "controlled sample accepted without explicit direction");
    }

    private static System.Collections.Generic.Dictionary<string, string> CompleteTraits()
    {
        return new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "warmth", "warm" }, { "patience", "patient" }, { "curiosity", "inquisitive" },
            { "privacy", "open" }, { "manner", "courteous" }, { "outlook", "optimistic" },
            { "emotional stability", "even-tempered" }, { "ambition", "content" },
            { "integrity", "honest" }, { "courage", "cautious" }, { "dutifulness", "independent" },
            { "vocabulary", "plain" }, { "eloquence", "plain" }, { "status attitude", "egalitarian" },
            { "humor", "playful" }, { "self esteem", "grounded" }, { "sensitivity", "attuned" }
        };
    }

    private static void RequestRejectsOversizedText()
    {
        string json;
        Require(!BreezeTtsTrialProtocol.TryBuildRequestJson("req", "life", "person", "npc",
            new string('x', BreezeTtsTrialProtocol.MaximumTextCharacters + 1), 1, 1000, out json),
            "oversized utterance accepted");
        Require(json == null, "rejected request returned output JSON");
    }

    private static void ResponsePathIsContained()
    {
        string root = Path.Combine(Path.GetTempPath(), "breeze-tts-response-root");
        string resolved;
        Require(BreezeTtsTrialProtocol.TryResolveResponseWavePath(root, "req-1.wav", out resolved), "valid response WAV rejected");
        Require(resolved == Path.Combine(Path.GetFullPath(root), "req-1.wav"), "response path resolved outside expected child");
    }

    private static void ResponsePathRejectsTraversal()
    {
        string root = Path.Combine(Path.GetTempPath(), "breeze-tts-response-root");
        string resolved;
        Require(!BreezeTtsTrialProtocol.TryResolveResponseWavePath(root, "..\\outside.wav", out resolved), "parent traversal accepted");
        Require(!BreezeTtsTrialProtocol.TryResolveResponseWavePath(root, Path.GetFullPath("outside.wav"), out resolved), "rooted path accepted");
        Require(!BreezeTtsTrialProtocol.TryResolveResponseWavePath(root, "req-1.json", out resolved), "non-WAV extension accepted");
    }

    private static void Pcm16Decode()
    {
        BreezeTtsTrialWave wave;
        Require(BreezeTtsTrialProtocol.TryDecodePcm16Wave(CreateWave(1, 22050, new short[] { -32768, 32767 }), out wave), "valid mono WAV rejected");
        Require(wave.Channels == 1 && wave.SampleRate == 22050 && wave.SampleFrames == 2 && wave.Samples.Length == 2,
            "mono WAV metadata mismatch");
        Require(wave.Samples[0] == -1f && wave.Samples[1] > 0.9999f, "PCM16 scaling mismatch");
        Require(BreezeTtsTrialProtocol.TryDecodePcm16Wave(CreateWave(2, 48000, new short[] { 0, 16384, -16384, 32767 }), out wave),
            "valid stereo WAV rejected");
        Require(wave.Channels == 2 && wave.SampleFrames == 2 && wave.Samples.Length == 4, "stereo frame count mismatch");
    }

    private static void Pcm16RejectsMalformed()
    {
        BreezeTtsTrialWave wave;
        Require(!BreezeTtsTrialProtocol.TryDecodePcm16Wave(new byte[] { 1, 2, 3 }, out wave), "short file accepted");
        Require(!BreezeTtsTrialProtocol.TryDecodePcm16Wave(CreateWave(1, 22050, new short[] { 0 }, format: 3), out wave),
            "float WAV accepted as PCM16");
        byte[] misaligned = CreateWave(2, 22050, new short[] { 0 });
        Require(!BreezeTtsTrialProtocol.TryDecodePcm16Wave(misaligned, out wave), "partial stereo frame accepted");
    }

    private static void AtomicWrite()
    {
        string root = Path.Combine(Path.GetTempPath(), "breeze-tts-atomic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            BreezeTtsTrialProtocol.WriteAtomically(root, "req-1.request.json", "{\"complete\":true}");
            Require(File.ReadAllText(Path.Combine(root, "req-1.request.json"), Encoding.UTF8) == "{\"complete\":true}",
                "final request file differs");
            Require(Directory.GetFiles(root).Length == 1, "temporary file leaked after atomic publication");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void AtomicWriteRejectsOverwrite()
    {
        string root = Path.Combine(Path.GetTempPath(), "breeze-tts-immutable-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "request.json");
        try
        {
            BreezeTtsTrialProtocol.WriteAtomically(root, "request.json", "{\"sequence\":1}");
            bool rejected = false;
            try { BreezeTtsTrialProtocol.WriteAtomically(root, "request.json", "{\"sequence\":2}"); }
            catch (IOException) { rejected = true; }
            Require(rejected, "immutable atomic write replaced an existing request");
            Require(File.ReadAllText(path, Encoding.UTF8) == "{\"sequence\":1}",
                "rejected immutable overwrite changed original contents");
            Require(Directory.GetFiles(root).Length == 1, "rejected immutable overwrite leaked a temporary file");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void AtomicReplaceWrite()
    {
        string root = Path.Combine(Path.GetTempPath(), "breeze-tts-mutable-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "ack.json");
        const int updateCount = 128;
        string[] versions = new string[updateCount];
        for (int sequence = 0; sequence < updateCount; sequence++)
            versions[sequence] = "{\"schemaVersion\":1,\"consumedChunkSequence\":" + sequence + "}";
        bool stopReader = false;
        string readerFailure = null;
        int readerCount = 0;
        var readerObserved = new ManualResetEvent(false);
        Thread reader = null;
        try
        {
            BreezeTtsTrialProtocol.ReplaceAtomically(root, "ack.json", versions[0]);
            reader = new Thread(() =>
            {
                while (!Volatile.Read(ref stopReader))
                {
                    try
                    {
                        string value;
                        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                            FileShare.ReadWrite | FileShare.Delete))
                        using (var readerStream = new StreamReader(stream, Encoding.UTF8))
                            value = readerStream.ReadToEnd();
                        if (Array.IndexOf(versions, value) < 0)
                        { readerFailure = "reader observed partial or unknown acknowledgement JSON"; break; }
                        if (Interlocked.Increment(ref readerCount) == 1) readerObserved.Set();
                    }
                    catch (FileNotFoundException) { }
                    catch (IOException) { }
                }
            });
            reader.IsBackground = true;
            reader.Start();
            Require(readerObserved.WaitOne(5000), "acknowledgement reader did not observe initial JSON");
            for (int sequence = 0; sequence < updateCount; sequence++)
            {
                BreezeTtsTrialProtocol.ReplaceAtomically(root, "ack.json", versions[sequence]);
                Require(File.ReadAllText(path, Encoding.UTF8) == versions[sequence],
                    "mutable replacement left partial or stale acknowledgement JSON at sequence " + sequence);
            }
            Volatile.Write(ref stopReader, true);
            Require(reader.Join(5000), "acknowledgement reader did not stop after replacement test");
            Require(readerCount > 0, "acknowledgement reader observed no atomic replacements");
            Require(readerFailure == null, readerFailure ?? "concurrent reader failure");
            Require(Directory.GetFiles(root).Length == 1, "mutable replacement leaked a temporary file");
        }
        finally
        {
            Volatile.Write(ref stopReader, true);
            if (reader != null) reader.Join(5000);
            readerObserved.Dispose();
            Directory.Delete(root, true);
        }
    }

    private static void AtomicReplaceWithDeleteSharedReader()
    {
        string root = Path.Combine(Path.GetTempPath(), "breeze-tts-shared-reader-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "stream.json");
        const string before = "{\"status\":\"open\",\"nextChunkSequence\":1}";
        const string after = "{\"status\":\"audio_complete\",\"nextChunkSequence\":2}";
        try
        {
            BreezeTtsTrialProtocol.ReplaceAtomically(root, "stream.json", before);
            using (var openReader = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            {
                BreezeTtsTrialProtocol.ReplaceAtomically(root, "stream.json", after);
                openReader.Position = 0;
                using (var oldContents = new StreamReader(openReader, Encoding.UTF8, true, 1024, true))
                    Require(oldContents.ReadToEnd() == before,
                        "existing delete-sharing reader did not retain its complete prior manifest");
                Require(File.ReadAllText(path, Encoding.UTF8) == after,
                    "atomic replacement did not publish complete new manifest beside open reader");
            }
            Require(Directory.GetFiles(root).Length == 1, "replacement with open reader leaked a temporary file");
        }
        finally { Directory.Delete(root, true); }
    }

    private static void CleanupPlanIsExact()
    {
        string requests = Path.Combine(Path.GetTempPath(), "breeze-tts-requests");
        string responses = Path.Combine(Path.GetTempPath(), "breeze-tts-responses");
        string id = "line-1";
        string wave = "line-1.wav";
        string[] paths;
        Require(BreezeTtsTrialProtocol.TryBuildConversationCleanupPaths(requests, responses, id, wave, out paths),
            "valid exact cleanup plan rejected");
        Require(paths.Length == 4, "cleanup plan omits failure terminal or includes unrelated artifacts");
        Require(paths[0] == Path.Combine(Path.GetFullPath(requests), id + ".request.json"), "request path mismatch");
        Require(paths[1] == Path.Combine(Path.GetFullPath(responses), id + ".manifest.json"), "manifest path mismatch");
        Require(paths[2] == Path.Combine(Path.GetFullPath(responses), id + ".failed.json"), "failure terminal path mismatch");
        Require(paths[3] == Path.Combine(Path.GetFullPath(responses), wave), "WAV path mismatch");
        foreach (string path in paths)
            Require(!path.Contains("profile") && !path.Contains("measurement"), "cleanup plan reaches stable profile/evidence files");
    }

    private static void FailureTerminalRequiresExactIdentity()
    {
        Require(MatchesFailureTerminal(1, "req-1", "life-1", "person-1", "failed",
            "req-1", "life-1", "person-1"), "bound failed response rejected");
        Require(!MatchesFailureTerminal(1, "req-1", "other-life", "person-1", "failed",
            "req-1", "life-1", "person-1"), "failure response from another conversation accepted");
        Require(!MatchesFailureTerminal(1, "req-1", "life-1", "other-person", "failed",
            "req-1", "life-1", "person-1"), "failure response from another person accepted");
        Require(!MatchesFailureTerminal(1, "other-req", "life-1", "person-1", "failed",
            "req-1", "life-1", "person-1"), "failure response for another request accepted");
        Require(!MatchesFailureTerminal(1, "req-1", "life-1", "person-1", "completed",
            "req-1", "life-1", "person-1"), "non-failure status accepted as a terminal failure");
        Require(!MatchesFailureTerminal(2, "req-1", "life-1", "person-1", "failed",
            "req-1", "life-1", "person-1"), "unsupported failure terminal schema accepted");
    }

    private static bool MatchesFailureTerminal(int schemaVersion, string requestId, string conversationToken,
        string personId, string status, string expectedRequestId, string expectedConversationToken,
        string expectedPersonId)
    {
        MethodInfo method = typeof(BreezeTtsTrialProtocol).GetMethod("TryMatchFailureTerminal",
            BindingFlags.Public | BindingFlags.Static);
        Require(method != null, "failure terminal matcher is missing from the protocol boundary");
        return (bool)method.Invoke(null, new object[] { schemaVersion, requestId, conversationToken,
            personId, status, expectedRequestId, expectedConversationToken, expectedPersonId });
    }

    private static void CleanupPlanRejectsTraversal()
    {
        string[] paths;
        Require(!BreezeTtsTrialProtocol.TryBuildConversationCleanupPaths(
            Path.GetTempPath(), Path.GetTempPath(), "line-1", "..\\profile.wav", out paths),
            "cleanup plan accepted a WAV outside the response directory");
    }

    private static byte[] CreateWave(int channels, int sampleRate, short[] samples, int format = 1)
    {
        int dataBytes = samples.Length * 2;
        byte[] bytes = new byte[44 + dataBytes];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
        Write32(bytes, 4, bytes.Length - 8);
        Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(bytes, 8);
        Write32(bytes, 16, 16);
        Write16(bytes, 20, format);
        Write16(bytes, 22, channels);
        Write32(bytes, 24, sampleRate);
        Write32(bytes, 28, sampleRate * channels * 2);
        Write16(bytes, 32, channels * 2);
        Write16(bytes, 34, 16);
        Encoding.ASCII.GetBytes("data").CopyTo(bytes, 36);
        Write32(bytes, 40, dataBytes);
        for (int i = 0; i < samples.Length; i++) Write16(bytes, 44 + i * 2, unchecked((ushort)samples[i]));
        return bytes;
    }

    private static void Write16(byte[] bytes, int offset, int value)
    { bytes[offset] = (byte)value; bytes[offset + 1] = (byte)(value >> 8); }
    private static void Write32(byte[] bytes, int offset, int value)
    { Write16(bytes, offset, value); Write16(bytes, offset + 2, value >> 16); }

    private static void Run(string name, Action test, ref int failures)
    {
        try { test(); Console.WriteLine("PASS " + name); }
        catch (Exception error) { failures++; Console.WriteLine("FAIL " + name + ": " + error.Message); }
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
