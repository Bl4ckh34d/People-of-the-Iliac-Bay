using System;
using System.Collections.Generic;
using System.IO;
using PeopleOfTheIliacBay.DFU;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class PreparedPresentationTests
    {
        private const string Text = "Hi there.";
        private const string AudioHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("prepared presentation loads exact visemes with optional word alignment", PreparedPairLoadsAndBindsBothTimelines));
            cases.Add(("prepared presentation starts with exact visemes before word alignment exists", PreparedVisemesLoadWithoutAlignment));
            cases.Add(("prepared presentation ignores stale optional alignment", PreparedPresentationIgnoresStaleAlignment));
            cases.Add(("prepared presentation rejects a missing or stale viseme sidecar", PreparedPresentationRejectsMissingOrStaleVisemes));
            cases.Add(("validated word timeline copies spans into immutable storage", WordTimelineDoesNotRetainMutableDocumentArrays));
        }

        private static void PreparedPairLoadsAndBindsBothTimelines()
        {
            string directory = CreateDirectory();
            try
            {
                var evidence = Evidence();
                string alignmentPath = Path.Combine(directory, "request-7.alignment.json");
                string visemePath = Path.Combine(directory, "request-7.visemes.json");
                File.WriteAllText(alignmentPath, AlignmentJson(evidence));
                File.WriteAllText(visemePath, VisemeJson(evidence));

                PortraitPreparedPresentation presentation;
                string error;
                Check.True(PortraitPreparedPresentationLoader.TryLoad(alignmentPath, visemePath,
                    evidence, out presentation, out error), error);
                Check.True(presentation != null);
                Check.Equal(evidence.RequestId, presentation.RequestId);
                Check.Equal(9, presentation.Words.GetCumulativeTextEnd(500));
                Check.Equal(3, presentation.Words.GetCurrentWordStart(450));
                Check.Equal(PortraitMouthShape.D, presentation.Visemes.ShapeAtSample(700));
            }
            finally { Directory.Delete(directory, true); }
        }

        private static void PreparedVisemesLoadWithoutAlignment()
        {
            string directory = CreateDirectory();
            try
            {
                var evidence = Evidence();
                string alignmentPath = Path.Combine(directory, "request-7.alignment.json");
                string visemePath = Path.Combine(directory, "request-7.visemes.json");
                File.WriteAllText(visemePath, VisemeJson(evidence));
                PortraitPreparedPresentation presentation;
                string error;
                Check.True(PortraitPreparedPresentationLoader.TryLoad(alignmentPath, visemePath,
                    evidence, out presentation, out error), error);
                Check.True(presentation != null);
                Check.True(presentation.Words == null);
                Check.Equal(PortraitMouthShape.D, presentation.Visemes.ShapeAtSample(700));
                Check.True(presentation.Matches(evidence));
            }
            finally { Directory.Delete(directory, true); }
        }

        private static void PreparedPresentationIgnoresStaleAlignment()
        {
            string directory = CreateDirectory();
            try
            {
                var evidence = Evidence();
                string alignmentPath = Path.Combine(directory, "request-7.alignment.json");
                string visemePath = Path.Combine(directory, "request-7.visemes.json");
                var staleEvidence = Evidence();
                staleEvidence.RequestId = "stale-request";
                File.WriteAllText(alignmentPath, AlignmentJson(staleEvidence));
                File.WriteAllText(visemePath, VisemeJson(evidence));
                PortraitPreparedPresentation presentation;
                string error;
                Check.True(PortraitPreparedPresentationLoader.TryLoad(alignmentPath, visemePath,
                    evidence, out presentation, out error), error);
                Check.True(presentation != null);
                Check.True(presentation.Words == null);
                Check.True(presentation.Matches(evidence));
            }
            finally { Directory.Delete(directory, true); }
        }

        private static void PreparedPresentationRejectsMissingOrStaleVisemes()
        {
            string directory = CreateDirectory();
            try
            {
                var evidence = Evidence();
                string alignmentPath = Path.Combine(directory, "request-7.alignment.json");
                string visemePath = Path.Combine(directory, "request-7.visemes.json");
                PortraitPreparedPresentation presentation;
                string error;
                Check.True(!PortraitPreparedPresentationLoader.TryLoad(alignmentPath, visemePath,
                    evidence, out presentation, out error));
                Check.True(presentation == null);

                File.WriteAllText(visemePath, VisemeJson(evidence));
                evidence.RequestId = "other-request";
                Check.True(!PortraitPreparedPresentationLoader.TryLoad(alignmentPath, visemePath,
                    evidence, out presentation, out error));
                Check.True(presentation == null);
            }
            finally { Directory.Delete(directory, true); }
        }

        private static void WordTimelineDoesNotRetainMutableDocumentArrays()
        {
            PortraitVisemeAudioEvidence evidence = Evidence();
            WordAlignmentTimelineDocument document;
            string error;
            Check.True(PortraitWordAlignmentTimelineValidator.TryDeserialize(AlignmentJson(evidence),
                out document, out error), error);
            PortraitWordAlignmentTimeline timeline;
            Check.True(PortraitWordAlignmentTimelineValidator.TryValidate(document, evidence, false,
                out timeline, out error), error);

            document.words[0].textStartUtf16 = 5;
            document.words[0].endSample = 900;
            Check.Equal(0, timeline.GetCurrentWordStart(100));
            Check.Equal(9, timeline.GetCumulativeTextEnd(500));
        }

        private static PortraitVisemeAudioEvidence Evidence()
        {
            return new PortraitVisemeAudioEvidence
            {
                RequestId = "request-7", PersonId = "person-1", ConversationToken = "session-4",
                Text = Text, SampleRate = 1000, AudioFrames = 1000, Channels = 1,
                AudioSha256 = AudioHash,
            };
        }

        private static string AlignmentJson(PortraitVisemeAudioEvidence evidence)
        {
            string textHash = PortraitVisemeSidecarValidator.Sha256Utf8(evidence.Text);
            return "{\"schemaVersion\":1,\"status\":\"ready\",\"timelineKind\":\"forced-word-alignment\"," +
                "\"text\":\"Hi there.\",\"audioSampleRate\":1000,\"audioFrames\":1000," +
                "\"coverage\":{\"matchedTokens\":2,\"transcriptTokens\":2}," +
                "\"words\":[{\"text\":\"Hi\",\"textStartUtf16\":0,\"textLengthUtf16\":2,\"startSample\":0,\"endSample\":400}," +
                "{\"text\":\"there.\",\"textStartUtf16\":3,\"textLengthUtf16\":6,\"startSample\":400,\"endSample\":1000}]," +
                "\"provenance\":{\"requestId\":\"" + evidence.RequestId + "\",\"audioSha256\":\"" + AudioHash +
                "\",\"transcriptSha256\":\"" + textHash + "\"}}";
        }

        private static string VisemeJson(PortraitVisemeAudioEvidence evidence)
        {
            string textHash = PortraitVisemeSidecarValidator.Sha256Utf8(evidence.Text);
            return "{\"schemaVersion\":1,\"status\":\"ready\",\"timelineKind\":\"recognizer-viseme-estimate\"," +
                "\"requestId\":\"" + evidence.RequestId + "\",\"text\":\"Hi there.\",\"audioSampleRate\":1000,\"audioFrames\":1000," +
                "\"cues\":[{\"startSample\":0,\"endSample\":500,\"shape\":\"X\"},{\"startSample\":500,\"endSample\":1000,\"shape\":\"D\"}]," +
                "\"provenance\":{\"audioSha256\":\"" + AudioHash + "\",\"dialogHintSha256\":\"" + textHash +
                "\",\"recognitionKind\":\"recognizer-estimate\",\"phonemeAlignment\":false}}";
        }

        private static string CreateDirectory()
        {
            string path = Path.Combine(Path.GetTempPath(), "portrait-prepared-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
