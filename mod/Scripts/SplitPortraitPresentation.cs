using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.DFU
{
    public enum SplitPortraitEmotionId { Neutral, Happy, Angry, Sad, Afraid, Guarded }

    /// <summary>Detached, typed state used only to select a portrait emotion.</summary>
    public readonly struct SplitPortraitEmotionInput
    {
        public readonly string StableActorId;
        public readonly bool MoodKnown;
        public readonly int Mood;
        public readonly bool DispositionKnown;
        public readonly int Disposition;
        public readonly bool TrustKnown;
        public readonly int Trust;
        public readonly bool PlayerFearKnown;
        public readonly int PlayerFear;
        public readonly bool ResentmentKnown;
        public readonly int Resentment;
        public readonly bool AcuteFearKnown;
        public readonly int AcuteFear;

        public SplitPortraitEmotionInput(string stableActorId, bool moodKnown, int mood,
            bool dispositionKnown, int disposition, bool trustKnown, int trust,
            bool playerFearKnown, int playerFear, bool resentmentKnown, int resentment,
            bool acuteFearKnown, int acuteFear)
        {
            StableActorId = stableActorId;
            MoodKnown = moodKnown;
            Mood = mood;
            DispositionKnown = dispositionKnown;
            Disposition = disposition;
            TrustKnown = trustKnown;
            Trust = trust;
            PlayerFearKnown = playerFearKnown;
            PlayerFear = playerFear;
            ResentmentKnown = resentmentKnown;
            Resentment = resentment;
            AcuteFearKnown = acuteFearKnown;
            AcuteFear = acuteFear;
        }
    }

    /// <summary>Maps detached native state to supported portrait-only emotion IDs.</summary>
    public static class SplitPortraitEmotionProjector
    {
        // Presentation thresholds only. These values do not alter native social policy.
        public const int FearThreshold = 60;
        public const int ResentmentThreshold = 60;
        public const int WaryThreshold = 25;
        public const int PositiveMoodThreshold = 25;
        public const int NegativeMoodThreshold = -25;

        /// <summary>Precedence: fear, resentment, wary relationship, mood, neutral.</summary>
        public static SplitPortraitEmotionId Project(SplitPortraitEmotionInput input)
        {
            if (string.IsNullOrWhiteSpace(input.StableActorId)) return SplitPortraitEmotionId.Neutral;

            bool hasFear = input.PlayerFearKnown && IsSeverity(input.PlayerFear);
            bool hasAcuteFear = input.AcuteFearKnown && IsSeverity(input.AcuteFear);
            if ((hasFear && input.PlayerFear >= FearThreshold) ||
                (hasAcuteFear && input.AcuteFear >= FearThreshold))
                return SplitPortraitEmotionId.Afraid;

            if (input.ResentmentKnown && IsSeverity(input.Resentment) &&
                input.Resentment >= ResentmentThreshold)
                return SplitPortraitEmotionId.Angry;

            bool adverseDisposition = input.DispositionKnown && IsDisposition(input.Disposition) &&
                input.Disposition <= WaryThreshold;
            bool lowKnownTrust = input.TrustKnown && IsSeverity(input.Trust) && input.Trust <= WaryThreshold;
            if (adverseDisposition || lowKnownTrust) return SplitPortraitEmotionId.Guarded;

            if (input.MoodKnown && input.Mood >= PositiveMoodThreshold && input.Mood <= 100)
                return SplitPortraitEmotionId.Happy;
            if (input.MoodKnown && input.Mood <= NegativeMoodThreshold && input.Mood >= -100)
                return SplitPortraitEmotionId.Sad;
            return SplitPortraitEmotionId.Neutral;
        }

        public static string ToManifestId(SplitPortraitEmotionId emotion)
        {
            switch (emotion)
            {
                case SplitPortraitEmotionId.Happy: return "happy";
                case SplitPortraitEmotionId.Angry: return "angry";
                case SplitPortraitEmotionId.Sad: return "sad";
                case SplitPortraitEmotionId.Afraid: return "afraid";
                case SplitPortraitEmotionId.Guarded: return "guarded";
                default: return "neutral";
            }
        }

        private static bool IsSeverity(int value) { return value >= 0 && value <= 100; }
        private static bool IsDisposition(int value) { return value >= 0 && value <= 100; }
    }

    /// <summary>Real-time horizontal gaze state. Idle bursts run independently of speech playback.</summary>
    public sealed class SplitPortraitGazeClock
    {
        private sealed class Burst
        {
            public double StartAtMs;
            public double EndAtMs;
            public string DirectionId;
        }

        private readonly Func<double> random;
        private readonly SplitPortraitAnimationPolicy policy;
        private readonly List<Burst> bursts = new List<Burst>();
        private string selection = "idle";
        private string ownerId;
        private string lastDirectionId;
        private string autoDirectionId;
        private double lastNowMs;
        private double windowStartAtMs;
        private double autoNextChangeAtMs;
        private bool initialized;
        private bool windowInitialized;

        public SplitPortraitGazeClock(Func<double> random, SplitPortraitAnimationPolicy policy)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (policy == null || !policy.IsValid()) throw new ArgumentException("animation-policy-invalid", nameof(policy));
            this.random = random;
            this.policy = policy.Copy();
        }

        /// <summary>Choose idle, off, auto, left, or right. Invalid and vertical directions are rejected.</summary>
        public bool Select(string requestedSelection)
        {
            if (requestedSelection != "idle" && requestedSelection != "off" && requestedSelection != "auto" &&
                requestedSelection != "left" && requestedSelection != "right") return false;
            if (selection == requestedSelection) return true;
            selection = requestedSelection;
            ResetState();
            return true;
        }

        /// <summary>
        /// Advance with monotonic real-time seconds. hasCompletePair means selected emotion has both
        /// horizontal directions. Speech state is intentionally absent, so idle gaze continues during audio.
        /// </summary>
        public string Advance(string presentationOwnerId, bool attached, double presentationTimeSeconds,
            bool hasCompletePair)
        {
            if (!attached || string.IsNullOrWhiteSpace(presentationOwnerId) ||
                double.IsNaN(presentationTimeSeconds) || double.IsInfinity(presentationTimeSeconds) ||
                presentationTimeSeconds < 0)
            {
                Reset();
                return null;
            }

            double nowMs = presentationTimeSeconds * 1000.0;
            if (!initialized || !string.Equals(ownerId, presentationOwnerId, StringComparison.Ordinal) || nowMs < lastNowMs)
            {
                ResetState();
                ownerId = presentationOwnerId;
                initialized = true;
            }
            lastNowMs = nowMs;

            if (!hasCompletePair || selection == "off")
            {
                ClearSchedule();
                return null;
            }

            if (selection == "left" || selection == "right") return selection;
            if (selection == "auto") return AdvanceAuto(nowMs);
            return AdvanceIdle(nowMs);
        }

        /// <summary>Clear gaze ownership and restore the default idle selection.</summary>
        public void Reset()
        {
            selection = "idle";
            ResetState();
        }

        private string AdvanceAuto(double nowMs)
        {
            if (autoDirectionId == null)
            {
                autoDirectionId = Draw() < 0.5 ? "left" : "right";
                autoNextChangeAtMs = nowMs + RandomMs(policy.gazeAutoMinMs, policy.gazeAutoMaxMs);
            }
            else if (nowMs >= autoNextChangeAtMs)
            {
                // Gallery picks from a one-item set excluding the current gaze. Keep its RNG sequence.
                Draw();
                autoDirectionId = autoDirectionId == "left" ? "right" : "left";
                autoNextChangeAtMs = nowMs + RandomMs(policy.gazeAutoMinMs, policy.gazeAutoMaxMs);
            }
            return autoDirectionId;
        }

        private string AdvanceIdle(double nowMs)
        {
            if (!windowInitialized || nowMs >= windowStartAtMs + policy.gazeWindowMs)
                ScheduleWindow(nowMs);

            for (int index = 0; index < bursts.Count; index++)
            {
                Burst burst = bursts[index];
                if (nowMs >= burst.StartAtMs && nowMs < burst.EndAtMs) return burst.DirectionId;
            }
            return null;
        }

        private void ScheduleWindow(double startAtMs)
        {
            bursts.Clear();
            windowInitialized = true;
            windowStartAtMs = startAtMs;
            int countRange = policy.gazeBurstsMax - policy.gazeBurstsMin + 1;
            int count = policy.gazeBurstsMin + (int)Math.Floor(Draw() * countRange);
            if (count > policy.gazeBurstsMax) count = policy.gazeBurstsMax;
            double slotSize = (double)policy.gazeWindowMs / count;
            string previousDirection = lastDirectionId;

            for (int index = 0; index < count; index++)
            {
                double duration = RandomMs(policy.gazeDurationMinMs, policy.gazeDurationMaxMs);
                double slotStart = index * slotSize;
                double slotEnd = (index + 1) * slotSize;
                double maxOffset = Math.Max(0, slotSize - duration - 1);
                double offset = Draw() * maxOffset;
                if (index == 0 && slotStart + offset < 1) offset = 1;
                string direction = ChooseDirection(previousDirection);
                double burstStart = startAtMs + slotStart + offset;
                double burstEnd = Math.Min(burstStart + duration, startAtMs + slotEnd - 1);
                bursts.Add(new Burst { StartAtMs = burstStart, EndAtMs = burstEnd, DirectionId = direction });
                previousDirection = direction;
            }
            lastDirectionId = previousDirection;
        }

        private string ChooseDirection(string previousDirection)
        {
            double choice = Draw();
            if (previousDirection == "left") return "right";
            if (previousDirection == "right") return "left";
            return choice < 0.5 ? "left" : "right";
        }

        private double RandomMs(int minimum, int maximum)
        {
            return minimum + Draw() * (maximum - minimum);
        }

        private double Draw()
        {
            double value = random();
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0;
            return Math.Max(0, Math.Min(1, value));
        }

        private void ClearSchedule()
        {
            bursts.Clear();
            windowInitialized = false;
            lastDirectionId = null;
            autoDirectionId = null;
            autoNextChangeAtMs = 0;
        }

        private void ResetState()
        {
            ownerId = null;
            initialized = false;
            lastNowMs = 0;
            ClearSchedule();
        }
    }

    /// <summary>Presentation-time eye animation, independent of game, voice, and text clocks.</summary>
    public sealed class SplitPortraitBlinkClock
    {
        private const double OrdinaryBlinkSeconds = 0.230;
        private const double LegacyFirstHalfSeconds = 0.055;
        private const double FirstClosedSeconds = 0.120;
        private const double FollowupDelayMinSeconds = 1.0;
        private const double FollowupDelayRangeSeconds = 0.8;
        private const double BlinkIntervalMinSeconds = 2.0;
        private const double BlinkIntervalRangeSeconds = 3.0;
        private const double HoldIntervalMinSeconds = 15.0;
        private const double HoldIntervalRangeSeconds = 45.0;

        private readonly Func<double> random;
        private readonly SplitPortraitAnimationPolicy animationPolicy;
        private string ownerId;
        private bool initialized;
        private bool blinkActive;
        private bool doubleBlink;
        private bool followupPending;
        private bool holdActive;
        private int blinkStage;
        private int holdStage;
        private double lastTime;
        private double blinkStartAt;
        private double secondBlinkAt;
        private double nextTransitionAt;
        private double nextBlinkAt;
        private double nextHoldAt;
        private double followupAt;
        private double holdUntil;
        private double holdTransitionAt;
        private SplitPortraitEyePhase phase;

        public SplitPortraitBlinkClock(Func<double> random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            this.random = random;
            phase = SplitPortraitEyePhase.Open;
        }

        /// <summary>Use manifest timings; the one-argument constructor retains schema1 timing.</summary>
        public SplitPortraitBlinkClock(Func<double> random, SplitPortraitAnimationPolicy animationPolicy)
            : this(random)
        {
            if (animationPolicy == null || !animationPolicy.IsValid())
                throw new ArgumentException("animation-policy-invalid", nameof(animationPolicy));
            this.animationPolicy = animationPolicy.Copy();
        }

        /// <summary>
        /// Advance with monotonic presentation seconds. Owner ID binds actor, talk window, and face.
        /// Stable social actor identity is not required for blinking.
        /// </summary>
        public SplitPortraitEyePhase Advance(string presentationOwnerId, bool attached,
            double presentationTimeSeconds, bool speaking)
        {
            if (!attached || string.IsNullOrWhiteSpace(presentationOwnerId) ||
                double.IsNaN(presentationTimeSeconds) || double.IsInfinity(presentationTimeSeconds) ||
                presentationTimeSeconds < 0)
            {
                Reset();
                return SplitPortraitEyePhase.Open;
            }

            if (!initialized || !string.Equals(ownerId, presentationOwnerId, StringComparison.Ordinal) ||
                presentationTimeSeconds < lastTime || presentationTimeSeconds - lastTime > 60.0)
            {
                Reset();
                Initialize(presentationOwnerId, presentationTimeSeconds);
                return SplitPortraitEyePhase.Open;
            }

            lastTime = presentationTimeSeconds;
            if (animationPolicy != null) return AdvanceGalleryPolicy(presentationTimeSeconds, speaking);

            if (holdActive)
                return AdvanceHold(presentationTimeSeconds, speaking);

            if (blinkActive)
            {
                AdvanceBlink(presentationTimeSeconds);
                if (blinkActive) return phase;
            }

            if (presentationTimeSeconds >= nextHoldAt)
            {
                nextHoldAt = presentationTimeSeconds + HoldInterval();
                bool useClosedHold = speaking && Draw() < 0.25;
                phase = SplitPortraitEyePhase.Half;
                if (useClosedHold)
                {
                    holdStage = 1;
                    holdTransitionAt = presentationTimeSeconds + FirstHalfSeconds;
                    holdUntil = holdTransitionAt + 1.5 + Draw() * 1.5;
                }
                else
                {
                    holdStage = 4;
                    holdUntil = presentationTimeSeconds + 1.5 + Draw() * 6.5;
                }
                holdActive = true;
                return phase;
            }

            if (followupPending && presentationTimeSeconds >= followupAt)
            {
                followupPending = false;
                StartBlink(followupAt, false);
            }
            else if (!followupPending && presentationTimeSeconds >= nextBlinkAt)
            {
                double startAt = nextBlinkAt;
                doubleBlink = Draw() < 0.18;
                secondBlinkAt = doubleBlink
                    ? startAt + OrdinaryBlinkSeconds * (0.55 + Draw() * 0.15)
                    : 0;
                StartBlink(startAt, doubleBlink);
            }

            if (blinkActive) AdvanceBlink(presentationTimeSeconds);
            return phase;
        }

        public void Reset()
        {
            ownerId = null;
            initialized = false;
            blinkActive = false;
            doubleBlink = false;
            followupPending = false;
            holdActive = false;
            holdStage = 0;
            blinkStage = 0;
            phase = SplitPortraitEyePhase.Open;
        }

        private void Initialize(string presentationOwnerId, double now)
        {
            ownerId = presentationOwnerId;
            initialized = true;
            lastTime = now;
            phase = SplitPortraitEyePhase.Open;
            nextBlinkAt = now + (animationPolicy == null ? BlinkInterval() : BlinkDelay(false));
            nextHoldAt = now + HoldInterval();
        }

        private void StartBlink(double startAt, bool useDouble)
        {
            blinkActive = true;
            doubleBlink = useDouble;
            blinkStartAt = startAt;
            blinkStage = 0;
            phase = SplitPortraitEyePhase.Half;
            nextTransitionAt = startAt + FirstHalfSeconds;
        }

        private void AdvanceBlink(double now)
        {
            while (blinkActive && now >= nextTransitionAt)
            {
                switch (blinkStage)
                {
                    case 0:
                        phase = SplitPortraitEyePhase.Closed;
                        blinkStage = 1;
                        nextTransitionAt = doubleBlink ? secondBlinkAt : blinkStartAt + FirstHalfSeconds + ClosedSeconds;
                        break;
                    case 1:
                        if (doubleBlink)
                        {
                            // Bridge closed first blink to second with half phase; never flash fully open.
                            phase = SplitPortraitEyePhase.Half;
                            blinkStage = 2;
                            nextTransitionAt = secondBlinkAt + FirstHalfSeconds;
                        }
                        else
                        {
                            phase = SplitPortraitEyePhase.Half;
                            blinkStage = 3;
                            nextTransitionAt = blinkStartAt + BlinkDurationSeconds;
                        }
                        break;
                    case 2:
                        phase = SplitPortraitEyePhase.Closed;
                        blinkStage = 4;
                        nextTransitionAt = secondBlinkAt + FirstHalfSeconds + ClosedSeconds;
                        break;
                    case 4:
                        phase = SplitPortraitEyePhase.Half;
                        blinkStage = 5;
                        nextTransitionAt = secondBlinkAt + BlinkDurationSeconds;
                        break;
                    default:
                        CompleteBlink(nextTransitionAt);
                        break;
                }
            }
        }

        private void CompleteBlink(double endedAt)
        {
            if (animationPolicy != null)
            {
                bool wasDouble = doubleBlink;
                blinkActive = false;
                phase = SplitPortraitEyePhase.Open;
                followupPending = false;
                // Gallery schedules the delay from the frame that observed completion.
                nextBlinkAt = lastTime + BlinkDelay(wasDouble);
                doubleBlink = false;
                return;
            }

            blinkActive = false;
            phase = SplitPortraitEyePhase.Open;
            if (doubleBlink)
            {
                followupPending = true;
                followupAt = endedAt + FollowupDelayMinSeconds + Draw() * FollowupDelayRangeSeconds;
            }
            else
            {
                nextBlinkAt = endedAt + BlinkInterval();
            }
            doubleBlink = false;
        }

        private SplitPortraitEyePhase AdvanceHold(double now, bool speaking)
        {
            if (holdStage == 4)
            {
                if (now >= holdUntil)
                {
                    phase = SplitPortraitEyePhase.Open;
                    holdActive = false;
                    holdStage = 0;
                }
                return phase;
            }

            if (!speaking && (holdStage == 1 || holdStage == 2))
            {
                // If the hold and its natural release already elapsed during a frame stall,
                // the gallery considers the event complete instead of extending it again.
                if (now >= holdUntil + HoldReleaseSeconds)
                {
                    phase = SplitPortraitEyePhase.Open;
                    holdActive = false;
                    holdStage = 0;
                    return phase;
                }
                // Stop speech through half phase; preserve the scheduled next hold.
                phase = SplitPortraitEyePhase.Half;
                holdStage = 3;
                holdTransitionAt = now + HoldReleaseSeconds;
                return phase;
            }

            while (holdActive && now >= holdTransitionAt)
            {
                if (holdStage == 1)
                {
                    phase = SplitPortraitEyePhase.Closed;
                    holdStage = 2;
                    holdTransitionAt = holdUntil;
                }
                else if (holdStage == 2)
                {
                    phase = SplitPortraitEyePhase.Half;
                    holdStage = 3;
                    holdTransitionAt = holdUntil + HoldReleaseSeconds;
                }
                else
                {
                    phase = SplitPortraitEyePhase.Open;
                    holdActive = false;
                    holdStage = 0;
                }
            }
            return phase;
        }

        private SplitPortraitEyePhase AdvanceGalleryPolicy(double now, bool speaking)
        {
            if (holdActive) return AdvanceHold(now, speaking);

            if (blinkActive)
            {
                AdvanceBlink(now);
                if (blinkActive) return phase;
            }

            if (now >= nextHoldAt)
            {
                bool useClosedHold = speaking && DrawPolicy() < animationPolicy.holdSpeakingClosedChance;
                phase = SplitPortraitEyePhase.Half;
                if (useClosedHold)
                {
                    holdStage = 1;
                    holdTransitionAt = now + HoldReleaseSeconds;
                    holdUntil = holdTransitionAt + RandomHoldDurationMs(animationPolicy.holdClosedMinMs,
                        animationPolicy.holdClosedMaxMs) / 1000.0;
                }
                else
                {
                    holdStage = 4;
                    holdUntil = now + RandomHoldDurationMs(animationPolicy.holdHalfMinMs,
                        animationPolicy.holdHalfMaxMs) / 1000.0;
                }
                nextHoldAt = now + HoldInterval();
                holdActive = true;
                return phase;
            }

            if (now >= nextBlinkAt)
            {
                // The gallery starts the event on the frame that observes its due time.
                double startAt = now;
                doubleBlink = DrawPolicy() < animationPolicy.blinkDoubleChance;
                secondBlinkAt = doubleBlink
                    ? startAt + animationPolicy.blinkDurationMs *
                        (animationPolicy.blinkDoubleStartMinFraction + DrawPolicy() *
                            (animationPolicy.blinkDoubleStartMaxFraction - animationPolicy.blinkDoubleStartMinFraction)) / 1000.0
                    : 0;
                StartBlink(startAt, doubleBlink);
            }

            if (blinkActive) AdvanceBlink(now);
            return phase;
        }

        private double BlinkInterval() { return BlinkIntervalMinSeconds + Draw() * BlinkIntervalRangeSeconds; }
        private double HoldInterval()
        {
            return animationPolicy == null
                ? HoldIntervalMinSeconds + Draw() * HoldIntervalRangeSeconds
                : RandomPolicyMs(animationPolicy.holdIntervalMinMs, animationPolicy.holdIntervalMaxMs) / 1000.0;
        }

        private double BlinkDelay(bool isDouble)
        {
            double shortChance = isDouble ? animationPolicy.blinkDoubleShortChance : animationPolicy.blinkSingleShortChance;
            bool shortDelay = DrawPolicy() < shortChance;
            int minimum = shortDelay ? animationPolicy.blinkShortDelayMinMs : animationPolicy.blinkLongDelayMinMs;
            int maximum = shortDelay ? animationPolicy.blinkShortDelayMaxMs : animationPolicy.blinkLongDelayMaxMs;
            return RandomPolicyMs(minimum, maximum) / 1000.0;
        }

        private double RandomPolicyMs(int minimum, int maximum)
        {
            return minimum + DrawPolicy() * (maximum - minimum);
        }

        private double RandomHoldDurationMs(int minimum, int maximum)
        {
            return Math.Floor(RandomPolicyMs(minimum, maximum) + 0.5);
        }

        private double FirstHalfSeconds
        {
            get { return animationPolicy == null ? LegacyFirstHalfSeconds : animationPolicy.blinkHalfMs / 1000.0; }
        }

        private double ClosedSeconds
        {
            get
            {
                return animationPolicy == null ? FirstClosedSeconds :
                    (animationPolicy.blinkClosedEndMs - animationPolicy.blinkHalfMs) / 1000.0;
            }
        }

        private double BlinkDurationSeconds
        {
            get { return animationPolicy == null ? OrdinaryBlinkSeconds : animationPolicy.blinkDurationMs / 1000.0; }
        }

        private double HoldReleaseSeconds
        {
            get { return animationPolicy == null ? LegacyFirstHalfSeconds : animationPolicy.holdReleaseMs / 1000.0; }
        }

        private double DrawPolicy()
        {
            double value = random();
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0;
            return Math.Max(0, Math.Min(1, value));
        }

        private double Draw()
        {
            double value = random();
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || value >= 1) return 0.5;
            return value;
        }
    }
}
