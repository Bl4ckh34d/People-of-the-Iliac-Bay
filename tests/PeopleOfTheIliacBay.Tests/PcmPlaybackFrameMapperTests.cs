using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class PcmPlaybackFrameMapperTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("PCM playback frame mapping freezes through output silence and resumes on source audio", new Action(() => {
                var ranges = new[]
                {
                    new PcmPlaybackFrameRange(0, 4, 100, 104, 2),
                    new PcmPlaybackFrameRange(8, 12, 104, 108, 2),
                };

                Check.True(PcmPlaybackFrameMapper.TryMapSentenceCursor(ranges, 2, 2, 100, -1, out long duringFirst));
                Check.Equal(102L, duringFirst);
                Check.True(PcmPlaybackFrameMapper.TryMapSentenceCursor(ranges, 2, 6, 100, 104, out long duringGap));
                Check.Equal(104L, duringGap);
                Check.True(PcmPlaybackFrameMapper.TryMapSentenceCursor(ranges, 2, 9, 100, 104, out long afterGap));
                Check.Equal(105L, afterGap);
                Check.True(PcmPlaybackFrameMapper.TryMapSentenceCursor(ranges, 2, 12, 100, 105, out long atEnd));
                Check.Equal(108L, atEnd);
            })));

            cases.Add(("PCM playback frame mapping keeps acknowledged source progress after output-range pruning", new Action(() => {
                // The first output range has been acknowledged and removed. Its consumed source end
                // remains as the cursor floor while output is silent before the next range.
                var remaining = new[] { new PcmPlaybackFrameRange(8, 12, 504, 508, 4) };
                Check.True(PcmPlaybackFrameMapper.TryMapSentenceCursor(remaining, 4, 6, 500, 504, out long inGap));
                Check.Equal(504L, inGap);
                Check.True(PcmPlaybackFrameMapper.TryMapSentenceCursor(
                    Array.Empty<PcmPlaybackFrameRange>(), 4, 7, 500, 504, out long withAllRangesPruned));
                Check.Equal(504L, withAllRangesPruned);
                Check.True(PcmPlaybackFrameMapper.TryMapSentenceCursor(remaining, 4, 10, 500, 504, out long inAudio));
                Check.Equal(506L, inAudio);
            })));

            cases.Add(("PCM playback frame mapping rejects malformed spans and never maps another sentence", new Action(() => {
                var malformed = new[] { new PcmPlaybackFrameRange(8, 12, 104, 109, 2) };
                Check.True(!PcmPlaybackFrameMapper.TryMapSentenceCursor(malformed, 2, 10, 100, -1, out _));
                var otherSentence = new[] { new PcmPlaybackFrameRange(0, 4, 100, 104, 3) };
                Check.True(!PcmPlaybackFrameMapper.TryMapSentenceCursor(otherSentence, 2, 2, 100, -1, out _));
            })));
        }
    }
}
