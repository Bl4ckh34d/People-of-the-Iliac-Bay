using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class BreezeTtsStreamingPolicyTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add((nameof(SettingAloneKeepsPreparedPlayback), SettingAloneKeepsPreparedPlayback));
            cases.Add((nameof(LegacyOptInStillRequiresEveryPrerequisite), LegacyOptInStillRequiresEveryPrerequisite));
        }

        private static void SettingAloneKeepsPreparedPlayback()
        {
            Require(!BreezeTtsStreamingPolicy.IsEnabled(true, true, true, false),
                "A saved streaming setting alone must keep finalized prepared playback enabled.");
        }

        private static void LegacyOptInStillRequiresEveryPrerequisite()
        {
            Require(BreezeTtsStreamingPolicy.IsEnabled(true, true, true, true),
                "Legacy streaming should enable when voice, setting, endpoint, and explicit opt-in are present.");
            Require(!BreezeTtsStreamingPolicy.IsEnabled(false, true, true, true),
                "Legacy streaming must stay off when voice is disabled.");
            Require(!BreezeTtsStreamingPolicy.IsEnabled(true, false, true, true),
                "Legacy streaming must stay off when its setting is disabled.");
            Require(!BreezeTtsStreamingPolicy.IsEnabled(true, true, false, true),
                "Legacy streaming must stay off without a configured endpoint.");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
