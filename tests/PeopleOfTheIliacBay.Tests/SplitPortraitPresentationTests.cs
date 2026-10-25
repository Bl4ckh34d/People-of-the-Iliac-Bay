using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.DFU;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class SplitPortraitPresentationTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Portrait emotion projection uses documented thresholds and precedence", ProjectThresholdsAndPrecedence));
            cases.Add(("Portrait emotion projection ignores unknown and invalid fields", ProjectUnknownAndInvalidFields));
            cases.Add(("Portrait blink advances independently through half phases", BlinkUsesPresentationClock));
            cases.Add(("Portrait blink double starts within first blink and schedules followup", BlinkSupportsDoubleAndFollowup));
            cases.Add(("Portrait blink resets when presentation owner changes or detaches", BlinkResetsWithOwner));
            cases.Add(("Portrait blink supports optional idle half holds", BlinkSupportsIdleHalfHold));
            cases.Add(("Portrait blink closes through half phases around speech holds", BlinkSupportsSpeechHold));
            cases.Add(("Portrait closed hold releases through half phase when speech stops", BlinkClosedHoldReleasesOnSpeechStop));
            cases.Add(("Portrait hold occurs at scheduled interval without a skip draw", BlinkDoesNotSkipScheduledHold));
            cases.Add(("Portrait hold cadence remains anchored to hold start", BlinkHoldCadenceAnchorsToStart));
            cases.Add(("Portrait blink remains deterministic across long presentation sweep", BlinkLongSweep));
            cases.Add((nameof(IdleGazeUsesGalleryBurstWindowsAndAlternatesDirections), IdleGazeUsesGalleryBurstWindowsAndAlternatesDirections));
            cases.Add((nameof(AutomaticGazeUsesPolicyIntervalAndOnlyHorizontalDirections), AutomaticGazeUsesPolicyIntervalAndOnlyHorizontalDirections));
            cases.Add((nameof(GazeResetsOnDetachOwnerChangeAndUnavailableArt), GazeResetsOnDetachOwnerChangeAndUnavailableArt));
            cases.Add((nameof(Schema2BlinkClockConsumesManifestPhaseTimings), Schema2BlinkClockConsumesManifestPhaseTimings));
            cases.Add((nameof(Schema2BlinkClockConsumesManifestHoldTimingsDuringSpeech), Schema2BlinkClockConsumesManifestHoldTimingsDuringSpeech));
            cases.Add((nameof(Schema2BlinkSchedulesAfterLateSample), Schema2BlinkSchedulesAfterLateSample));
            cases.Add((nameof(Schema2SpeechHoldDoesNotExtendAfterItsNaturalEnd), Schema2SpeechHoldDoesNotExtendAfterItsNaturalEnd));
        }

        private static void ProjectThresholdsAndPrecedence()
        {
            Equal(SplitPortraitEmotionId.Neutral, SplitPortraitEmotionProjector.Project(Input("npc:one")));
            Equal(SplitPortraitEmotionId.Afraid, SplitPortraitEmotionProjector.Project(
                Input("npc:one", fearKnown: true, fear: 60, resentmentKnown: true, resentment: 100)));
            Equal(SplitPortraitEmotionId.Angry, SplitPortraitEmotionProjector.Project(
                Input("npc:one", resentmentKnown: true, resentment: 60, trustKnown: true, trust: 0)));
            Equal(SplitPortraitEmotionId.Guarded, SplitPortraitEmotionProjector.Project(
                Input("npc:one", dispositionKnown: true, disposition: 25, moodKnown: true, mood: 100)));
            Equal(SplitPortraitEmotionId.Happy, SplitPortraitEmotionProjector.Project(
                Input("npc:one", moodKnown: true, mood: 25)));
            Equal(SplitPortraitEmotionId.Sad, SplitPortraitEmotionProjector.Project(
                Input("npc:one", moodKnown: true, mood: -25)));
            Equal("afraid", SplitPortraitEmotionProjector.ToManifestId(SplitPortraitEmotionId.Afraid));
        }

        private static void ProjectUnknownAndInvalidFields()
        {
            Equal(SplitPortraitEmotionId.Neutral, SplitPortraitEmotionProjector.Project(
                Input(null, moodKnown: true, mood: 100, trustKnown: true, trust: 0)));
            Equal(SplitPortraitEmotionId.Happy, SplitPortraitEmotionProjector.Project(
                Input("npc:one", moodKnown: true, mood: 25, trustKnown: false, trust: 0)));
            Equal(SplitPortraitEmotionId.Neutral, SplitPortraitEmotionProjector.Project(
                Input("npc:one", moodKnown: true, mood: 101)));
            Equal(SplitPortraitEmotionId.Neutral, SplitPortraitEmotionProjector.Project(
                Input("npc:one", dispositionKnown: true, disposition: 101)));
        }

        private static void BlinkUsesPresentationClock()
        {
            var blink = new SplitPortraitBlinkClock(Sequence(0, 0, 0.9, 0.9));
            Equal(SplitPortraitEyePhase.Open, blink.Advance("actor/window/face", true, 0, false));
            Equal(SplitPortraitEyePhase.Open, blink.Advance("actor/window/face", true, 1.99, true));
            Equal(SplitPortraitEyePhase.Half, blink.Advance("actor/window/face", true, 2, true));
            Equal(SplitPortraitEyePhase.Closed, blink.Advance("actor/window/face", true, 2.056, true));
            Equal(SplitPortraitEyePhase.Half, blink.Advance("actor/window/face", true, 2.176, true));
            Equal(SplitPortraitEyePhase.Open, blink.Advance("actor/window/face", true, 2.231, true));
        }

        private static void BlinkSupportsDoubleAndFollowup()
        {
            var blink = new SplitPortraitBlinkClock(Sequence(0, 0, 0, 0, 0, 0, 0.9));
            blink.Advance("static:town:shop/hash/window/face", true, 0, false);
            Equal(SplitPortraitEyePhase.Half, blink.Advance("static:town:shop/hash/window/face", true, 2, false));
            Equal(SplitPortraitEyePhase.Closed, blink.Advance("static:town:shop/hash/window/face", true, 2.056, false));
            Equal(SplitPortraitEyePhase.Half, blink.Advance("static:town:shop/hash/window/face", true, 2.1275, false));
            Equal(SplitPortraitEyePhase.Closed, blink.Advance("static:town:shop/hash/window/face", true, 2.1825, false));
            Equal(SplitPortraitEyePhase.Half, blink.Advance("static:town:shop/hash/window/face", true, 2.3025, false));
            Equal(SplitPortraitEyePhase.Open, blink.Advance("static:town:shop/hash/window/face", true, 2.3575, false));
            Equal(SplitPortraitEyePhase.Half, blink.Advance("static:town:shop/hash/window/face", true, 3.3585, true));
        }

        private static void BlinkResetsWithOwner()
        {
            var blink = new SplitPortraitBlinkClock(Sequence(0, 0, 0.9, 0, 0, 0.9));
            blink.Advance("actor-a/window/face-1", true, 0, false);
            Equal(SplitPortraitEyePhase.Half, blink.Advance("actor-a/window/face-1", true, 2, false));
            Equal(SplitPortraitEyePhase.Open, blink.Advance("actor-b/window/face-1", true, 2.01, false));
            Equal(SplitPortraitEyePhase.Open, blink.Advance("actor-b/window/face-1", false, 2.02, false));
            Equal(SplitPortraitEyePhase.Open, blink.Advance("actor-b/window/face-1", true, 2.03, false));
        }

        private static void BlinkSupportsIdleHalfHold()
        {
            var blink = new SplitPortraitBlinkClock(Sequence(0, 0, 0, 0.5));
            blink.Advance("actor/window/face", true, 0, false);
            Equal(SplitPortraitEyePhase.Half, blink.Advance("actor/window/face", true, 15, false));
            Equal(SplitPortraitEyePhase.Half, blink.Advance("actor/window/face", true, 19.74, true));
            Equal(SplitPortraitEyePhase.Open, blink.Advance("actor/window/face", true, 19.75, true));
        }

        private static void BlinkSupportsSpeechHold()
        {
            var blink = new SplitPortraitBlinkClock(Sequence(0, 0, 0.9, 0, 0, 0));
            blink.Advance("actor/window/face", true, 0, false);
            Equal(SplitPortraitEyePhase.Half, blink.Advance("actor/window/face", true, 15, true));
            Equal(SplitPortraitEyePhase.Closed, blink.Advance("actor/window/face", true, 15.056, true));
            Equal(SplitPortraitEyePhase.Closed, blink.Advance("actor/window/face", true, 16.49, true));
            Equal(SplitPortraitEyePhase.Half, blink.Advance("actor/window/face", true, 16.556, true));
            Equal(SplitPortraitEyePhase.Open, blink.Advance("actor/window/face", true, 16.611, true));
        }

        private static void BlinkClosedHoldReleasesOnSpeechStop()
        {
            var blink = new SplitPortraitBlinkClock(Sequence(0, 0, 0.9, 0, 0, 0));
            blink.Advance("actor/window/face", true, 0, false);
            Equal(SplitPortraitEyePhase.Half, blink.Advance("actor/window/face", true, 15, true));
            Equal(SplitPortraitEyePhase.Closed, blink.Advance("actor/window/face", true, 15.056, true));
            Equal(SplitPortraitEyePhase.Half, blink.Advance("actor/window/face", true, 16, false));
            Equal(SplitPortraitEyePhase.Open, blink.Advance("actor/window/face", true, 16.056, false));
        }

        private static void BlinkDoesNotSkipScheduledHold()
        {
            var blink = new SplitPortraitBlinkClock(Sequence(0, 0, 0.9, 0.9, 0.5, 0.9));
            blink.Advance("actor/window/face", true, 0, false);
            Equal(SplitPortraitEyePhase.Half, blink.Advance("actor/window/face", true, 15, false));
            Equal(SplitPortraitEyePhase.Half, blink.Advance("actor/window/face", true, 16.5, false));
        }

        private static void BlinkHoldCadenceAnchorsToStart()
        {
            var blink = new SplitPortraitBlinkClock(Sequence(0, 0, 0, 0.9, 0, 0.9, 0, 0.9));
            blink.Advance("actor/window/face", true, 0, false);
            Equal(SplitPortraitEyePhase.Half, blink.Advance("actor/window/face", true, 15, false));
            Equal(SplitPortraitEyePhase.Open, blink.Advance("actor/window/face", true, 22.4, false));
            Equal(SplitPortraitEyePhase.Open, blink.Advance("actor/window/face", true, 22.406, false));
            Equal(SplitPortraitEyePhase.Half, blink.Advance("actor/window/face", true, 30, false));
            Equal(SplitPortraitEyePhase.Half, blink.Advance("actor/window/face", true, 31, false));
        }

        private static void BlinkLongSweep()
        {
            var blink = new SplitPortraitBlinkClock(() => 0.9);
            bool sawHalf = false;
            bool sawClosed = false;
            SplitPortraitEyePhase previous = SplitPortraitEyePhase.Open;
            for (int frame = 0; frame <= 12000; frame++)
            {
                double time = frame * 0.01;
                SplitPortraitEyePhase current = blink.Advance("managed:resident-7/window/face-77", true, time, frame % 2 == 0);
                sawHalf |= current == SplitPortraitEyePhase.Half;
                sawClosed |= current == SplitPortraitEyePhase.Closed;
                if (current != previous && current == SplitPortraitEyePhase.Closed)
                    Equal(SplitPortraitEyePhase.Half, previous);
                previous = current;
            }
            Equal(true, sawHalf);
            Equal(true, sawClosed);
        }

        private static void IdleGazeUsesGalleryBurstWindowsAndAlternatesDirections()
        {
            var gaze = new SplitPortraitGazeClock(Sequence(0, 0, 0, 0, 0, 0, 0, 0), new SplitPortraitAnimationPolicy());
            Equal(null, gaze.Advance("actor-a/window/face-77", true, 0, true));
            Equal("left", gaze.Advance("actor-a/window/face-77", true, 0.001, true));
            Equal(null, gaze.Advance("actor-a/window/face-77", true, 1.801, true));
            Equal(null, gaze.Advance("actor-a/window/face-77", true, 60.003, true));
            Equal("right", gaze.Advance("actor-a/window/face-77", true, 60.004, true));
            Equal(null, gaze.Advance("actor-a/window/face-77", true, 61.805, true));
        }

        private static void AutomaticGazeUsesPolicyIntervalAndOnlyHorizontalDirections()
        {
            var gaze = new SplitPortraitGazeClock(Sequence(0, 0, 0, 0), new SplitPortraitAnimationPolicy());
            Equal(true, gaze.Select("auto"));
            Equal("left", gaze.Advance("actor-a/window/face-77", true, 10, true));
            Equal("left", gaze.Advance("actor-a/window/face-77", true, 11.499, true));
            Equal("right", gaze.Advance("actor-a/window/face-77", true, 11.5, true));
            Equal(false, gaze.Select("up-left"));
            Equal("right", gaze.Advance("actor-a/window/face-77", true, 11.501, true));
        }

        private static void GazeResetsOnDetachOwnerChangeAndUnavailableArt()
        {
            var gaze = new SplitPortraitGazeClock(Sequence(0, 0, 0, 0, 0, 0, 0, 0), new SplitPortraitAnimationPolicy());
            Equal(null, gaze.Advance("actor-a/window/face-77", true, 0, true));
            Equal("left", gaze.Advance("actor-a/window/face-77", true, 0.001, true));
            Equal(null, gaze.Advance("actor-b/window/face-77", true, 0.002, true));
            Equal(null, gaze.Advance("actor-b/window/face-77", false, 0.003, true));
            Equal(null, gaze.Advance("actor-b/window/face-77", true, 0.004, false));
            Equal(null, gaze.Advance("actor-b/window/face-77", true, 0.005, true));
        }

        private static void Schema2BlinkClockConsumesManifestPhaseTimings()
        {
            SplitPortraitAnimationPolicy policy = new SplitPortraitAnimationPolicy
            {
                blinkDurationMs = 400,
                blinkHalfMs = 100,
                blinkClosedEndMs = 300,
                blinkDoubleChance = 0,
                blinkSingleShortChance = 1,
                blinkShortDelayMinMs = 1000,
                blinkShortDelayMaxMs = 1000,
                blinkLongDelayMinMs = 1000,
                blinkLongDelayMaxMs = 1000,
            };
            var blink = new SplitPortraitBlinkClock(Sequence(0, 0, 0, 0.9, 0, 0), policy);
            Equal(SplitPortraitEyePhase.Open, blink.Advance("actor/window/face", true, 0, false));
            Equal(SplitPortraitEyePhase.Half, blink.Advance("actor/window/face", true, 1, false));
            Equal(SplitPortraitEyePhase.Half, blink.Advance("actor/window/face", true, 1.099, false));
            Equal(SplitPortraitEyePhase.Closed, blink.Advance("actor/window/face", true, 1.1, false));
            Equal(SplitPortraitEyePhase.Half, blink.Advance("actor/window/face", true, 1.3, false));
            Equal(SplitPortraitEyePhase.Open, blink.Advance("actor/window/face", true, 1.4, false));
        }

        private static void Schema2BlinkClockConsumesManifestHoldTimingsDuringSpeech()
        {
            SplitPortraitAnimationPolicy policy = new SplitPortraitAnimationPolicy
            {
                blinkSingleShortChance = 0,
                blinkLongDelayMinMs = 10000,
                blinkLongDelayMaxMs = 10000,
                holdIntervalMinMs = 1000,
                holdIntervalMaxMs = 1000,
                holdSpeakingClosedChance = 1,
                holdReleaseMs = 200,
                holdClosedMinMs = 2000,
                holdClosedMaxMs = 2000,
            };
            var blink = new SplitPortraitBlinkClock(Sequence(0.5, 0, 0, 0, 0, 0), policy);
            Equal(SplitPortraitEyePhase.Open, blink.Advance("actor/window/face", true, 0, false));
            Equal(SplitPortraitEyePhase.Half, blink.Advance("actor/window/face", true, 1, true));
            Equal(SplitPortraitEyePhase.Half, blink.Advance("actor/window/face", true, 1.199, true));
            Equal(SplitPortraitEyePhase.Closed, blink.Advance("actor/window/face", true, 1.2, true));
            Equal(SplitPortraitEyePhase.Half, blink.Advance("actor/window/face", true, 3.2, true));
            Equal(SplitPortraitEyePhase.Half, blink.Advance("actor/window/face", true, 3.399, true));
            Equal(SplitPortraitEyePhase.Open, blink.Advance("actor/window/face", true, 3.401, true));
        }

        private static void Schema2BlinkSchedulesAfterLateSample()
        {
            SplitPortraitAnimationPolicy policy = new SplitPortraitAnimationPolicy
            {
                blinkDoubleChance = 0,
                blinkSingleShortChance = 1,
                blinkShortDelayMinMs = 1000,
                blinkShortDelayMaxMs = 1000,
                blinkLongDelayMinMs = 1000,
                blinkLongDelayMaxMs = 1000,
                holdIntervalMinMs = 100000,
                holdIntervalMaxMs = 100000,
            };
            var blink = new SplitPortraitBlinkClock(Sequence(0.9, 0, 0, 0, 0, 0), policy);
            Equal(SplitPortraitEyePhase.Open, blink.Advance("actor/window/face", true, 0, false));
            Equal(SplitPortraitEyePhase.Half, blink.Advance("actor/window/face", true, 2, false));
            Equal(SplitPortraitEyePhase.Open, blink.Advance("actor/window/face", true, 2.5, false));
            Equal(SplitPortraitEyePhase.Open, blink.Advance("actor/window/face", true, 3.499, false));
            Equal(SplitPortraitEyePhase.Half, blink.Advance("actor/window/face", true, 3.5, false));
        }

        private static void Schema2SpeechHoldDoesNotExtendAfterItsNaturalEnd()
        {
            SplitPortraitAnimationPolicy policy = new SplitPortraitAnimationPolicy
            {
                blinkSingleShortChance = 0,
                blinkLongDelayMinMs = 100000,
                blinkLongDelayMaxMs = 100000,
                holdIntervalMinMs = 10000,
                holdIntervalMaxMs = 10000,
                holdSpeakingClosedChance = 1,
                holdReleaseMs = 200,
                holdClosedMinMs = 2000,
                holdClosedMaxMs = 2000,
            };
            var blink = new SplitPortraitBlinkClock(Sequence(0.5, 0, 0, 0, 0, 0), policy);
            Equal(SplitPortraitEyePhase.Open, blink.Advance("actor/window/face", true, 0, false));
            Equal(SplitPortraitEyePhase.Half, blink.Advance("actor/window/face", true, 10, true));
            Equal(SplitPortraitEyePhase.Open, blink.Advance("actor/window/face", true, 12.401, false));
        }

        private static Func<double> Sequence(params double[] values)
        {
            int index = 0;
            return () => index < values.Length ? values[index++] : 0.9;
        }

        private static SplitPortraitEmotionInput Input(string actorId, bool moodKnown = false, int mood = 0,
            bool dispositionKnown = false, int disposition = 0, bool trustKnown = false, int trust = 0,
            bool fearKnown = false, int fear = 0, bool resentmentKnown = false, int resentment = 0,
            bool acuteFearKnown = false, int acuteFear = 0)
        {
            return new SplitPortraitEmotionInput(actorId, moodKnown, mood, dispositionKnown, disposition,
                trustKnown, trust, fearKnown, fear, resentmentKnown, resentment, acuteFearKnown, acuteFear);
        }

        private static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException("Expected " + expected + ", got " + actual + ".");
        }
    }
}
