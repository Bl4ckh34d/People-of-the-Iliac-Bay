using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.DFU.Tests
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            var cases = new List<(string Name, Action Body)>();
            WillingnessTests.Add(cases);
            DialogueWillingnessTests.Add(cases);
            int failed = 0;
            foreach (var test in cases)
            {
                try { test.Body(); Console.WriteLine("PASS " + test.Name); }
                catch (Exception error)
                {
                    failed++;
                    Console.WriteLine("FAIL " + test.Name + ": " + error);
                }
            }
            Console.WriteLine((cases.Count - failed) + "/" + cases.Count + " passed; " + failed + " failed");
            return failed == 0 ? 0 : 1;
        }
    }
}
