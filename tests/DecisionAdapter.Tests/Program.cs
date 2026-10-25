using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.DFU.Tests
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length > 0) return LiveSmoke.Run(args);
            var cases = new List<(string Name, Action Body)>();
            WireTests.Add(cases);
            SpeechActWireTests.Add(cases);
            SessionTests.Add(cases);
            TransportTests.Add(cases);
            BoundaryTests.Add(cases);
            ShadowTests.Add(cases);
            ShadowConfigurationTests.Add(cases);
            RoutingTests.Add(cases);
            WillingnessTests.Add(cases);
            QwenWillingnessTests.Add(cases);
            DialogueRouteTests.Add(cases);
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
