using System;
using System.Collections.Generic;

namespace LivingCities.Tests
{
    internal static class Program
    {
        private static int Main()
        {
            var cases = new List<(string Name, Action Body)>();
            PopulationTests.Add(cases);
            FamilyTests.Add(cases);
            FamilySaveTests.Add(cases);
            AlarmTests.Add(cases);
            NoiseTests.Add(cases);
            HearingReportTests.Add(cases);
            EscapeTests.Add(cases);
            CrimeSceneTests.Add(cases);
            OutdoorCorpseTests.Add(cases);
            GuardTests.Add(cases);
            GuardPolicyTests.Add(cases);
            HauntingTests.Add(cases);
            GuardDefenseTests.Add(cases);
            PatrolPairTests.Add(cases);
            GuardPatrolTests.Add(cases);
            BedTests.Add(cases);
            SleepViewTests.Add(cases);
            HouseholdAccessTests.Add(cases);
            EntryReportTests.Add(cases);
            NativePeopleTests.Add(cases);
            PathTests.Add(cases);
            ScheduleTests.Add(cases);
            SaveTests.Add(cases);
            QuestBindingTests.Add(cases);
            SettingsTests.Add(cases);
            int failures = 0;
            foreach (var test in cases)
            {
                try { test.Body(); Console.WriteLine("PASS " + test.Name); }
                catch (Exception ex) { failures++; Console.WriteLine("FAIL " + test.Name + ": " + ex.Message); }
            }
            Console.WriteLine($"{cases.Count - failures}/{cases.Count} passed; {failures} failed");
            return failures == 0 ? 0 : 1;
        }
    }

    internal static class Check
    {
        public static void True(bool value, string message = "Condition was false")
        {
            if (!value) throw new Exception(message);
        }
        public static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new Exception($"Expected {expected}; got {actual}");
        }
        public static void Near(double expected, double actual, double tolerance = 0.000001)
        {
            if (double.IsNaN(actual) || Math.Abs(expected - actual) > tolerance)
                throw new Exception($"Expected {expected}; got {actual}");
        }
        public static void Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return; }
            throw new Exception("Expected " + typeof(T).Name);
        }
    }
}
