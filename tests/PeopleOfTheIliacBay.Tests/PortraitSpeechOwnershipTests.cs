using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class PortraitSpeechOwnershipTests
    {
        public static void Add(List<(string Name, Action Body)> cases)
        {
            cases.Add(("portrait speech owner accepts only exact request person token clip and generation", new Action(() =>
            {
                var current = new PortraitSpeechOwnerKey("request-1", "person-1", "token-1", 42, 7);
                Check.True(current.Matches(new PortraitSpeechOwnerKey("request-1", "person-1", "token-1", 42, 7)));
                Check.True(!current.Matches(new PortraitSpeechOwnerKey("request-2", "person-1", "token-1", 42, 7)));
                Check.True(!current.Matches(new PortraitSpeechOwnerKey("request-1", "person-2", "token-1", 42, 7)));
                Check.True(!current.Matches(new PortraitSpeechOwnerKey("request-1", "person-1", "token-2", 42, 7)));
                Check.True(!current.Matches(new PortraitSpeechOwnerKey("request-1", "person-1", "token-1", 43, 7)));
                Check.True(!current.Matches(new PortraitSpeechOwnerKey("request-1", "person-1", "token-1", 42, 8)));
            })));

            cases.Add(("portrait speech owner rejects empty identity", new Action(() =>
            {
                Check.Throws<ArgumentException>(() => new PortraitSpeechOwnerKey("", "person-1", "token-1", 42, 1));
                Check.Throws<ArgumentException>(() => new PortraitSpeechOwnerKey("request-1", "", "token-1", 42, 1));
                Check.Throws<ArgumentException>(() => new PortraitSpeechOwnerKey("request-1", "person-1", "", 42, 1));
                Check.Throws<ArgumentOutOfRangeException>(() => new PortraitSpeechOwnerKey("request-1", "person-1", "token-1", 0, 1));
                Check.Throws<ArgumentOutOfRangeException>(() => new PortraitSpeechOwnerKey("request-1", "person-1", "token-1", 42, 0));
            })));

            cases.Add(("portrait speech load policy allows same-request replay and actor switch", new Action(() =>
            {
                var policy = new PortraitSpeechLoadPolicy();
                var firstPlayback = new PortraitSpeechOwnerKey("request-1", "person-1", "token-1", 42, 1);
                var replay = new PortraitSpeechOwnerKey("request-1", "person-1", "token-1", 42, 2);
                var actorSwitch = new PortraitSpeechOwnerKey("request-1", "person-2", "token-2", 43, 3);

                Check.True(policy.TryBegin(firstPlayback, 1f, 8));
                policy.MarkFinal(firstPlayback);
                Check.True(!policy.TryBegin(firstPlayback, 1f, 8));
                Check.True(policy.TryBegin(replay, 1f, 8)); // Reset then replay gets a fresh generation.
                policy.MarkFinal(replay);
                Check.True(policy.TryBegin(actorSwitch, 1f, 8));
            })));

            cases.Add(("portrait speech stale completion and retry budget are scoped to playback generation", new Action(() =>
            {
                var policy = new PortraitSpeechLoadPolicy();
                var stale = new PortraitSpeechOwnerKey("request-1", "person-1", "token-1", 42, 1);
                var current = new PortraitSpeechOwnerKey("request-1", "person-1", "token-1", 42, 2);
                Check.True(policy.TryBegin(stale, 1f, 8));
                Check.True(policy.TryBegin(current, 1f, 8));
                policy.MarkFinal(stale); // A delayed old-generation result cannot poison replay.
                Check.Equal(1, policy.ProbeCount(current));

                policy.MarkFinal(current);
                var retryOwner = new PortraitSpeechOwnerKey("request-2", "person-3", "token-3", 50, 4);
                float now = 10f;
                for (int attempt = 0; attempt < 8; attempt++)
                {
                    Check.True(policy.TryBegin(retryOwner, now, 8));
                    policy.Defer(retryOwner, now + 0.5f, 8);
                    now += 0.5f;
                }
                Check.Equal(8, policy.ProbeCount(retryOwner));
                Check.True(!policy.TryBegin(retryOwner, now, 8));
            })));
        }
    }
}
