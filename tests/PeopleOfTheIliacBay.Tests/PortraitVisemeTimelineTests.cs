using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.DFU;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class PortraitVisemeTimelineTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("portrait atlas rows map by normalized cells even when pixel cell widths are fractional", new Action(() =>
            {
                PortraitAtlasCell first = PortraitAtlasGrid.TopLeftIndexedCell(0, 4, 4);
                PortraitAtlasCell last = PortraitAtlasGrid.TopLeftIndexedCell(15, 4, 4);
                Check.Equal(0f, first.UMin);
                Check.Equal(0.75f, first.VMin);
                Check.Equal(0.25f, first.UWidth);
                Check.Equal(0.25f, first.VHeight);
                Check.Equal(0.75f, last.UMin);
                Check.Equal(0f, last.VMin);
                Check.True(!float.IsNaN(last.VMin));
                bool threw = false;
                try { PortraitAtlasGrid.TopLeftIndexedCell(16, 4, 4); }
                catch (ArgumentOutOfRangeException) { threw = true; }
                Check.True(threw);
            })));

            cases.Add(("Rhubarb mouth cues map against the source audio sample clock and return neutral outside cues", new Action(() =>
            {
                PortraitVisemeTimeline timeline;
                string error;
                var cues = new[]
                {
                    new RhubarbVisemeCue(0.00, 0.10, "X"),
                    new RhubarbVisemeCue(0.10, 0.30, "A"),
                    new RhubarbVisemeCue(0.40, 0.60, "F"),
                };

                Check.True(PortraitVisemeTimeline.TryCreate(1000, 1000, cues, out timeline, out error), error);
                Check.Equal(PortraitMouthShape.X, timeline.ShapeAtSample(-1));
                Check.Equal(PortraitMouthShape.X, timeline.ShapeAtSample(0));
                Check.Equal(PortraitMouthShape.A, timeline.ShapeAtSample(100));
                Check.Equal(PortraitMouthShape.X, timeline.ShapeAtSample(350));
                Check.Equal(PortraitMouthShape.F, timeline.ShapeAtSample(500));
                Check.Equal(PortraitMouthShape.X, timeline.ShapeAtSample(1000));
            })));

            cases.Add(("Rhubarb timeline rejects malformed, overlapping, and out-of-audio cues", new Action(() =>
            {
                PortraitVisemeTimeline timeline;
                string error;
                Check.True(!PortraitVisemeTimeline.TryCreate(1000, 1000,
                    new[] { new RhubarbVisemeCue(0.20, 0.10, "A") }, out timeline, out error));
                Check.True(!PortraitVisemeTimeline.TryCreate(1000, 1000,
                    new[] { new RhubarbVisemeCue(0.00, 0.20, "A"), new RhubarbVisemeCue(0.19, 0.30, "B") }, out timeline, out error));
                Check.True(!PortraitVisemeTimeline.TryCreate(1000, 1000,
                    new[] { new RhubarbVisemeCue(0.00, 1.01, "A") }, out timeline, out error));
                Check.True(!PortraitVisemeTimeline.TryCreate(1000, 1000,
                    new[] { new RhubarbVisemeCue(0.00, 0.20, "Z") }, out timeline, out error));
            })));

            cases.Add(("Rhubarb sample sidecar validates exact request, person, session, text and audio hashes", new Action(() =>
            {
                const string text = "Good day.";
                var evidence = new PortraitVisemeAudioEvidence
                {
                    RequestId = "request-7", PersonId = "person-1", ConversationToken = "session-4",
                    Text = text, SampleRate = 24000, AudioFrames = 24000, Channels = 1,
                    AudioSha256 = new string('a', 64),
                };
                var document = new PortraitVisemeSidecarDocument
                {
                    schemaVersion = 1, status = "ready", timelineKind = "recognizer-viseme-estimate",
                    requestId = evidence.RequestId, text = text, audioSampleRate = evidence.SampleRate,
                    audioFrames = evidence.AudioFrames,
                    cues = new[] { new PortraitVisemeSidecarCue { startSample = 0, endSample = 12000, shape = "X" },
                        new PortraitVisemeSidecarCue { startSample = 12000, endSample = 24000, shape = "D" } },
                    provenance = new PortraitVisemeSidecarProvenance
                    {
                        audioSha256 = evidence.AudioSha256,
                        dialogHintSha256 = PortraitVisemeSidecarValidator.Sha256Utf8(text),
                        recognitionKind = "recognizer-estimate", phonemeAlignment = false,
                    },
                };

                PortraitVisemeTimeline timeline;
                string error;
                Check.True(PortraitVisemeSidecarValidator.TryValidate(document, evidence, "person-1", "session-4", out timeline, out error), error);
                Check.Equal(PortraitMouthShape.X, timeline.ShapeAtSample(0));
                Check.Equal(PortraitMouthShape.D, timeline.ShapeAtSample(12000));

                document.provenance.audioSha256 = new string('b', 64);
                Check.True(!PortraitVisemeSidecarValidator.TryValidate(document, evidence, "person-1", "session-4", out timeline, out error));
                Check.Equal("sidecar-wav-sha256-mismatch", error);
                document.provenance.audioSha256 = evidence.AudioSha256;
                document.requestId = "other-request";
                Check.True(!PortraitVisemeSidecarValidator.TryValidate(document, evidence, "person-1", "session-4", out timeline, out error));
                Check.Equal("sidecar-request-id-mismatch", error);
                document.requestId = evidence.RequestId;
                document.text = "Different exact text.";
                Check.True(!PortraitVisemeSidecarValidator.TryValidate(document, evidence, "person-1", "session-4", out timeline, out error));
                Check.Equal("sidecar-text-does-not-match-bridge-text", error);
                document.text = text;
                Check.True(!PortraitVisemeSidecarValidator.TryValidate(document, evidence, "other-person", "session-4", out timeline, out error));
                Check.Equal("invalid-bridge-audio-evidence", error);
            })));

            cases.Add(("Rhubarb sidecar nested cue arrays deserialize in the emitted wire shape", new Action(() =>
            {
                const string json = "{\"schemaVersion\":1,\"status\":\"ready\",\"timelineKind\":\"recognizer-viseme-estimate\",\"requestId\":\"r1\",\"text\":\"Hi.\",\"audioSampleRate\":24000,\"audioFrames\":24000,\"cues\":[{\"startSample\":0,\"endSample\":8000,\"shape\":\"X\"},{\"startSample\":8000,\"endSample\":16000,\"shape\":\"A\"},{\"startSample\":16000,\"endSample\":24000,\"shape\":\"B\"}],\"provenance\":{\"audioSha256\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"dialogHintSha256\":\"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\",\"recognitionKind\":\"recognizer-estimate\",\"phonemeAlignment\":false}}";
                FullSerializer.fsData parsed = FullSerializer.fsJsonParser.Parse(json);
                Check.True(parsed != null && parsed.IsDictionary);
                FullSerializer.fsData cues;
                Check.True(parsed.AsDictionary.TryGetValue("cues", out cues) && cues != null && cues.IsList);
                Check.Equal(3, cues.AsList.Count);
                var serializer = new FullSerializer.fsSerializer();
                PortraitVisemeSidecarDocument document = null;
                FullSerializer.fsResult deserializeResult = serializer.TryDeserialize(parsed, ref document);
                Check.True(deserializeResult.Succeeded);
                Check.True(document != null);
                Check.Equal(3, document.cues.Length);
                Check.Equal(8000, document.cues[1].startSample);
                Check.Equal("A", document.cues[1].shape);
                Check.Equal("recognizer-estimate", document.provenance.recognitionKind);
            })));

            cases.Add(("RMS fallback has hysteresis and is explicitly an amplitude approximation", new Action(() =>
            {
                var fallback = new PortraitAmplitudeFallback(0.02, 0.01);
                Check.Equal(PortraitMouthShape.X, fallback.ShapeForRms(0.0));
                Check.Equal(PortraitMouthShape.D, fallback.ShapeForRms(0.05));
                Check.Equal(PortraitMouthShape.D, fallback.ShapeForRms(0.015));
                Check.Equal(PortraitMouthShape.X, fallback.ShapeForRms(0.005));
                Check.Equal("amplitude-approximation", PortraitAmplitudeFallback.ProvenanceLabel);
            })));

            cases.Add(("portrait restore clears composed prior face after actor or face identity changes", new Action(() =>
            {
                Check.Equal(PortraitRestoreAction.ClearPrototypeComposite,
                    PortraitVisemeRestorePolicy.Decide(true, true, false, true, true));
                Check.Equal(PortraitRestoreAction.ClearPrototypeComposite,
                    PortraitVisemeRestorePolicy.Decide(true, true, true, false, true));
                Check.Equal(PortraitRestoreAction.ClearPrototypeComposite,
                    PortraitVisemeRestorePolicy.Decide(true, false, true, true, true));
                Check.Equal(PortraitRestoreAction.RestoreCurrentPortrait,
                    PortraitVisemeRestorePolicy.Decide(true, true, true, true, true));
                Check.Equal(PortraitRestoreAction.RestoreCachedPortrait,
                    PortraitVisemeRestorePolicy.Decide(true, true, true, true, false));
                Check.Equal(PortraitRestoreAction.KeepCurrentPanel,
                    PortraitVisemeRestorePolicy.Decide(false, false, false, false, false));
            })));

            cases.Add(("portrait preview counts mouth transitions and captures each open shape once per request", new Action(() =>
            {
                var counter = new PortraitVisemePreviewCueCounter();
                int count;
                bool capture;
                Check.True(counter.Record("request-1", PortraitMouthShape.A, out count, out capture));
                Check.Equal(1, count);
                Check.True(capture);
                Check.True(counter.Record("request-1", PortraitMouthShape.A, out count, out capture));
                Check.Equal(2, count);
                Check.True(!capture);
                Check.True(counter.Record("request-1", PortraitMouthShape.X, out count, out capture));
                Check.True(!capture);
                Check.True(counter.Record("request-2", PortraitMouthShape.A, out count, out capture));
                Check.Equal(1, count);
                Check.True(capture);
            })));
        }
    }
}
