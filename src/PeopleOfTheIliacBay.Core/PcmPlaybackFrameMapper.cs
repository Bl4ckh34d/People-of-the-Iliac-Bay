using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>
    /// A contiguous span copied from source PCM into the Unity output clock.
    /// Output gaps are intentionally absent; they represent silence inserted by playback.
    /// </summary>
    public struct PcmPlaybackFrameRange
    {
        public long OutputStartFrame { get; }
        public long OutputEndFrame { get; }
        public long SourceStartFrame { get; }
        public long SourceEndFrame { get; }
        public int SentenceSequence { get; }

        public PcmPlaybackFrameRange(long outputStartFrame, long outputEndFrame,
            long sourceStartFrame, long sourceEndFrame, int sentenceSequence)
        {
            OutputStartFrame = outputStartFrame;
            OutputEndFrame = outputEndFrame;
            SourceStartFrame = sourceStartFrame;
            SourceEndFrame = sourceEndFrame;
            SentenceSequence = sentenceSequence;
        }
    }

    /// <summary>
    /// Maps the output playback cursor back to source PCM frames without counting inserted silence.
    /// The caller supplies a retained source-frame floor because acknowledged output spans may be pruned.
    /// </summary>
    public static class PcmPlaybackFrameMapper
    {
        public static bool TryMapSentenceCursor(IEnumerable<PcmPlaybackFrameRange> ranges,
            int sentenceSequence, long outputCursorFrame, long sentenceFirstSourceFrame,
            long lastConsumedSourceFrame, out long sourceCursorFrame)
        {
            sourceCursorFrame = -1;
            if (ranges == null || sentenceSequence < 0 || outputCursorFrame < 0 || sentenceFirstSourceFrame < 0)
                return false;

            long mappedSourceFrame = lastConsumedSourceFrame;
            long previousOutputEnd = -1;
            long previousSourceEnd = -1;
            bool foundSentenceRange = false;
            foreach (PcmPlaybackFrameRange range in ranges)
            {
                if (range.SentenceSequence != sentenceSequence) continue;
                if (range.OutputStartFrame < 0 || range.OutputEndFrame <= range.OutputStartFrame ||
                    range.SourceStartFrame < sentenceFirstSourceFrame || range.SourceEndFrame <= range.SourceStartFrame ||
                    range.OutputEndFrame - range.OutputStartFrame != range.SourceEndFrame - range.SourceStartFrame ||
                    (foundSentenceRange && (range.OutputStartFrame < previousOutputEnd ||
                        range.SourceStartFrame < previousSourceEnd)))
                    return false;

                foundSentenceRange = true;
                previousOutputEnd = range.OutputEndFrame;
                previousSourceEnd = range.SourceEndFrame;

                if (outputCursorFrame <= range.OutputStartFrame) break;
                long consumed = Math.Min(outputCursorFrame, range.OutputEndFrame) - range.OutputStartFrame;
                long candidate = range.SourceStartFrame + consumed;
                if (candidate > mappedSourceFrame) mappedSourceFrame = candidate;
                if (outputCursorFrame <= range.OutputEndFrame) break;
            }

            if (mappedSourceFrame < sentenceFirstSourceFrame)
                return false;
            sourceCursorFrame = mappedSourceFrame;
            return true;
        }
    }
}
