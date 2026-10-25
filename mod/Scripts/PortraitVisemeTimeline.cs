using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.DFU
{
    public enum PortraitMouthShape { A, B, C, D, E, F, G, H, X }

    /// <summary>One Rhubarb cue with start/end times in seconds from the rendered WAV.</summary>
    public sealed class RhubarbVisemeCue
    {
        public readonly double StartSeconds;
        public readonly double EndSeconds;
        public readonly string Shape;

        public RhubarbVisemeCue(double startSeconds, double endSeconds, string shape)
        {
            StartSeconds = startSeconds;
            EndSeconds = endSeconds;
            Shape = shape;
        }
    }

    /// <summary>One already-quantized cue from the validated sidecar's WAV-frame clock.</summary>
    public sealed class PortraitVisemeSampleCue
    {
        public readonly int StartSample;
        public readonly int EndSample;
        public readonly string Shape;

        public PortraitVisemeSampleCue(int startSample, int endSample, string shape)
        {
            StartSample = startSample;
            EndSample = endSample;
            Shape = shape;
        }
    }

    /// <summary>
    /// Immutable Rhubarb timeline converted to the finalized audio clip's sample-frame clock.
    /// No phoneme inference occurs here; Rhubarb cue generation is a separate optional CPU step.
    /// </summary>
    public sealed class PortraitVisemeTimeline
    {
        private const int MaximumCueCount = 100000;
        private readonly int[] starts;
        private readonly int[] ends;
        private readonly PortraitMouthShape[] shapes;

        public int SampleRate { get; private set; }
        public int AudioFrames { get; private set; }

        private PortraitVisemeTimeline(int sampleRate, int audioFrames, int[] cueStarts, int[] cueEnds,
            PortraitMouthShape[] cueShapes)
        {
            SampleRate = sampleRate;
            AudioFrames = audioFrames;
            starts = cueStarts;
            ends = cueEnds;
            shapes = cueShapes;
        }

        public static bool TryCreate(int sampleRate, int audioFrames, IList<RhubarbVisemeCue> cues,
            out PortraitVisemeTimeline timeline, out string error)
        {
            timeline = null;
            error = null;
            if (sampleRate <= 0 || audioFrames <= 0 || cues == null || cues.Count > MaximumCueCount)
                return Fail("invalid-audio-or-cue-count", out error);

            int[] cueStarts = new int[cues.Count];
            int[] cueEnds = new int[cues.Count];
            var cueShapes = new PortraitMouthShape[cues.Count];
            int previousEnd = 0;
            for (int i = 0; i < cues.Count; i++)
            {
                RhubarbVisemeCue cue = cues[i];
                if (cue == null || double.IsNaN(cue.StartSeconds) || double.IsInfinity(cue.StartSeconds) ||
                    double.IsNaN(cue.EndSeconds) || double.IsInfinity(cue.EndSeconds) || cue.StartSeconds < 0 ||
                    cue.EndSeconds <= cue.StartSeconds)
                    return Fail("invalid-cue-time", out error);

                double start = cue.StartSeconds * sampleRate;
                double end = cue.EndSeconds * sampleRate;
                if (start > int.MaxValue || end > audioFrames + 0.5)
                    return Fail("cue-outside-audio", out error);
                int startSample = (int)Math.Round(start, MidpointRounding.AwayFromZero);
                int endSample = (int)Math.Round(end, MidpointRounding.AwayFromZero);
                if (startSample < previousEnd || startSample >= endSample || endSample > audioFrames)
                    return Fail("overlapping-or-subframe-cue", out error);

                PortraitMouthShape shape;
                if (!TryParseShape(cue.Shape, out shape)) return Fail("unknown-rhubarb-mouth-shape", out error);
                cueStarts[i] = startSample;
                cueEnds[i] = endSample;
                cueShapes[i] = shape;
                previousEnd = endSample;
            }

            timeline = new PortraitVisemeTimeline(sampleRate, audioFrames, cueStarts, cueEnds, cueShapes);
            return true;
        }

        public static bool TryCreateSamples(int sampleRate, int audioFrames, IList<PortraitVisemeSampleCue> cues,
            out PortraitVisemeTimeline timeline, out string error)
        {
            timeline = null;
            error = null;
            if (sampleRate <= 0 || audioFrames <= 0 || cues == null || cues.Count > MaximumCueCount)
                return Fail("invalid-audio-or-cue-count", out error);

            int[] cueStarts = new int[cues.Count];
            int[] cueEnds = new int[cues.Count];
            var cueShapes = new PortraitMouthShape[cues.Count];
            int previousEnd = 0;
            for (int i = 0; i < cues.Count; i++)
            {
                PortraitVisemeSampleCue cue = cues[i];
                if (cue == null || cue.StartSample < previousEnd || cue.StartSample < 0 ||
                    cue.EndSample <= cue.StartSample || cue.EndSample > audioFrames)
                    return Fail("invalid-sample-cue-range", out error);

                PortraitMouthShape shape;
                if (!TryParseShape(cue.Shape, out shape)) return Fail("unknown-rhubarb-mouth-shape", out error);
                cueStarts[i] = cue.StartSample;
                cueEnds[i] = cue.EndSample;
                cueShapes[i] = shape;
                previousEnd = cue.EndSample;
            }

            timeline = new PortraitVisemeTimeline(sampleRate, audioFrames, cueStarts, cueEnds, cueShapes);
            return true;
        }

        /// <summary>Returns the active cue at Unity AudioSource.timeSamples, or X for gaps/outside playback.</summary>
        public PortraitMouthShape ShapeAtSample(int sample)
        {
            if (sample < 0 || sample >= AudioFrames) return PortraitMouthShape.X;
            int low = 0;
            int high = starts.Length - 1;
            while (low <= high)
            {
                int middle = low + (high - low) / 2;
                if (sample < starts[middle]) high = middle - 1;
                else if (sample >= ends[middle]) low = middle + 1;
                else return shapes[middle];
            }
            return PortraitMouthShape.X;
        }

        private static bool TryParseShape(string text, out PortraitMouthShape shape)
        {
            switch (text)
            {
                case "A": shape = PortraitMouthShape.A; return true;
                case "B": shape = PortraitMouthShape.B; return true;
                case "C": shape = PortraitMouthShape.C; return true;
                case "D": shape = PortraitMouthShape.D; return true;
                case "E": shape = PortraitMouthShape.E; return true;
                case "F": shape = PortraitMouthShape.F; return true;
                case "G": shape = PortraitMouthShape.G; return true;
                case "H": shape = PortraitMouthShape.H; return true;
                case "X": shape = PortraitMouthShape.X; return true;
                default: shape = PortraitMouthShape.X; return false;
            }
        }

        private static bool Fail(string reason, out string error)
        {
            error = reason;
            return false;
        }
    }
}
