using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class MoodRecoveryTests
    {
        private static void HalfLifeIsStableAndBoundedPerPerson()
        {
            double first = MoodRecovery.HalfLifeSeconds(42, "person-a");
            double repeat = MoodRecovery.HalfLifeSeconds(42, "person-a");
            var npcValues = new HashSet<double>();
            var seedValues = new HashSet<double>();
            for (int i = 0; i < 64; i++)
            {
                npcValues.Add(MoodRecovery.HalfLifeSeconds(42, "person-" + i));
                seedValues.Add(MoodRecovery.HalfLifeSeconds((uint)i, "person-a"));
            }

            Check.Equal(first, repeat);
            Check.True(first >= 4 * 60 * 60 * 0.85 && first <= 4 * 60 * 60 * 1.15,
                "Mood recovery half-life fell outside its provisional 85%-115% range.");
            foreach (double value in npcValues)
                Check.True(value >= 4 * 60 * 60 * 0.85 && value <= 4 * 60 * 60 * 1.15,
                    "An NPC mood recovery half-life fell outside its provisional 85%-115% range.");
            foreach (double value in seedValues)
                Check.True(value >= 4 * 60 * 60 * 0.85 && value <= 4 * 60 * 60 * 1.15,
                    "A world-seed mood recovery half-life fell outside its provisional 85%-115% range.");
            Check.True(seedValues.Count > 1, "Different world seeds reused one sampled half-life.");
            Check.True(npcValues.Count > 1, "Different NPC identities reused one sampled half-life.");
        }

        private static void MoodCoolsMonotonicallyWithoutChangingSign()
        {
            int positiveStart = 80;
            int negativeStart = -80;
            int positivePrevious = positiveStart;
            int negativePrevious = negativeStart;

            for (int hour = 0; hour <= 24; hour++)
            {
                int positive = MoodRecovery.ValueAt(positiveStart, 0, hour * 3600, 4 * 3600);
                int negative = MoodRecovery.ValueAt(negativeStart, 0, hour * 3600, 4 * 3600);
                Check.True(positive >= 0 && positive <= positivePrevious,
                    "Positive mood changed sign or increased while cooling.");
                Check.True(negative <= 0 && negative >= negativePrevious,
                    "Negative mood changed sign or increased in magnitude while cooling.");
                positivePrevious = positive;
                negativePrevious = negative;
            }
        }

        private static void NeutralMoodStaysNeutralAndHalfLifeRoundsSymmetrically()
        {
            Check.Equal(0, MoodRecovery.ValueAt(0, 12, 1000000, 1));
            Check.Equal(1, MoodRecovery.ValueAt(1, 0, 1, 2));
            Check.Equal(-1, MoodRecovery.ValueAt(-1, 0, 1, 2));
            Check.Equal(40, MoodRecovery.ValueAt(80, 0, 3600, 3600));
            Check.Equal(-40, MoodRecovery.ValueAt(-80, 0, 3600, 3600));
        }

        private static void MoodReadsClampBeforeAnchorAndUseOriginalAnchor()
        {
            const double halfLife = 3600;
            int beforeAnchor = MoodRecovery.ValueAt(80, 7200, 3600, halfLife);
            int firstRead = MoodRecovery.ValueAt(80, 7200, 8100, halfLife);
            int laterRead = MoodRecovery.ValueAt(80, 7200, 9000, halfLife);

            Check.Equal(80, beforeAnchor);
            Check.Equal(67, firstRead);
            Check.Equal(57, laterRead);
            Check.Equal(firstRead, MoodRecovery.ValueAt(80, 7200, 8100, halfLife));
            Check.Equal(laterRead, MoodRecovery.ValueAt(80, 7200, 9000, halfLife));
            Check.True(laterRead <= firstRead, "Later anchored read increased mood magnitude.");
        }

        private static void HugeFiniteElapsedTimeCoolsToNeutral()
        {
            int result = MoodRecovery.ValueAt(100, 0, double.MaxValue, double.Epsilon);
            Check.Equal(0, result);
        }

        private static void MoodRecoveryRejectsInvalidInputs()
        {
            Check.Throws<ArgumentNullException>(() => MoodRecovery.HalfLifeSeconds(1, (string)null));
            Check.Throws<ArgumentOutOfRangeException>(() => MoodRecovery.ValueAt(-101, 0, 0, 1));
            Check.Throws<ArgumentOutOfRangeException>(() => MoodRecovery.ValueAt(101, 0, 0, 1));
            Check.Throws<ArgumentOutOfRangeException>(() => MoodRecovery.ValueAt(0, -1, 0, 1));
            Check.Throws<ArgumentOutOfRangeException>(() => MoodRecovery.ValueAt(0, double.NaN, 0, 1));
            Check.Throws<ArgumentOutOfRangeException>(() => MoodRecovery.ValueAt(0, double.PositiveInfinity, 0, 1));
            Check.Throws<ArgumentOutOfRangeException>(() => MoodRecovery.ValueAt(0, 0, -1, 1));
            Check.Throws<ArgumentOutOfRangeException>(() => MoodRecovery.ValueAt(0, 0, double.NaN, 1));
            Check.Throws<ArgumentOutOfRangeException>(() => MoodRecovery.ValueAt(0, 0, double.PositiveInfinity, 1));
            Check.Throws<ArgumentOutOfRangeException>(() => MoodRecovery.ValueAt(0, 0, 0, 0));
            Check.Throws<ArgumentOutOfRangeException>(() => MoodRecovery.ValueAt(0, 0, 0, double.NaN));
            Check.Throws<ArgumentOutOfRangeException>(() => MoodRecovery.ValueAt(0, 0, 0, double.PositiveInfinity));
        }

        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("Fractional game time matters at an odd mood half-life rounding boundary", () =>
            {
                Check.Equal(11, MoodRecovery.ValueAt(21, 100, 14500, 14400));
                Check.Equal(10, MoodRecovery.ValueAt(21, 100, 14500.5, 14400));
                Check.Equal(-11, MoodRecovery.ValueAt(-21, 100, 14500, 14400));
                Check.Equal(-10, MoodRecovery.ValueAt(-21, 100, 14500.5, 14400));
            }));
            cases.Add(("Mood recovery half-life is stable and bounded per person", HalfLifeIsStableAndBoundedPerPerson));
            cases.Add(("Mood recovery cools monotonically without changing sign", MoodCoolsMonotonicallyWithoutChangingSign));
            cases.Add(("Mood recovery preserves neutral and rounds symmetrically", NeutralMoodStaysNeutralAndHalfLifeRoundsSymmetrically));
            cases.Add(("Mood recovery clamps earlier reads and uses original anchor", MoodReadsClampBeforeAnchorAndUseOriginalAnchor));
            cases.Add(("Mood recovery handles huge finite elapsed time", HugeFiniteElapsedTimeCoolsToNeutral));
            cases.Add(("Mood recovery rejects invalid inputs", MoodRecoveryRejectsInvalidInputs));
        }
    }
}
