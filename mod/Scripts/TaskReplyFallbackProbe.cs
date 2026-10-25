using System;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    // Kept temporarily for source compatibility with SubmitSocialText callers.
    // Native action acknowledgments no longer depend on a final model reply.
    public static class TaskReplyFallbackProbe
    {
        public static string Begin(WorldStateStore store, string residentId, string question,
            string verifiedFallback, string transcript)
        {
            if (HasDeprecatedFlag())
                throw new InvalidOperationException(
                    "TaskReplyFallbackProbe is retired: accepted-task replies now bypass the second model call. " +
                    "Use TownTest.ps1 -ConversationReplyOutcomeProbe to verify direct native acknowledgments.");
            return null;
        }

        public static void Verify(WorldStateStore store, string residentId, string question,
            string answer, string transcript)
        {
            if (HasDeprecatedFlag())
                throw new InvalidOperationException(
                    "TaskReplyFallbackProbe is retired: accepted-task replies now bypass the second model call. " +
                    "Use TownTest.ps1 -ConversationReplyOutcomeProbe to verify direct native acknowledgments.");
        }

        private static bool HasDeprecatedFlag()
        {
            string[] args = Environment.GetCommandLineArgs();
            return Array.IndexOf(args, "-peopleoftheiliacbay-task-reply-fallback-probe") >= 0;
        }
    }
}
