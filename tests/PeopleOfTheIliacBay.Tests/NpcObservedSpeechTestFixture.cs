using System;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    /// <summary>Issues a controlled current-session speech capability for admission tests.</summary>
    internal static class NpcObservedSpeechTestFixture
    {
        internal static NpcObservedSpeechTicket Issue(WorldStateStore store, string observerId,
            string exactText, double observedGameTime, long requestRevision = 0)
        {
            if (!store.TryBeginNpcObservedSpeechSession(observerId, "test-session:" + Guid.NewGuid().ToString("N"),
                out NpcObservedSpeechSession session) ||
                !store.TryIssueNpcObservedSpeech(session, requestRevision, exactText,
                    observedGameTime, out NpcObservedSpeechTicket ticket))
                throw new InvalidOperationException("Could not issue controlled observed-speech source ticket.");
            return ticket;
        }
    }
}
