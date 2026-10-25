using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class ConversationTerminalActionContextTests
    {
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Terminal action dialogue context retains exact native success and failure evidence",RetainsTerminalEvidence));
            cases.Add(("Terminal action dialogue context omits pending or invalid evidence",OmitsUnsafeEvidence));
        }

        private static NpcActionJob Action(NpcActionPhase phase)
        {return new NpcActionJob {Id="action:terminal-context",ProposalId="action:terminal-context",ResidentId="resident-context",
            Kind=NpcActionKind.MoveTo,Target=new NpcActionTarget {Kind=NpcActionTargetKind.Building,SettlementId=1,BuildingKey=12},
            Phase=phase,Revision=phase==NpcActionPhase.Completed?2:1,AcceptedAt=100,UpdatedAt=101,DeadlineAt=200,
            CompletedEffectId=phase==NpcActionPhase.Completed?"native:arrival:exact":null,
            FailureReason=phase==NpcActionPhase.Failed?"Native route was blocked.":
                phase==NpcActionPhase.Cancelled?"The player cancelled the action.":null};}

        private static void RetainsTerminalEvidence()
        {
            string completed=ConversationTerminalActionContext.Format(Action(NpcActionPhase.Completed));
            Check.True(completed.Contains("completed") && completed.Contains("native:arrival:exact"));
            Check.True(completed.Contains("unaccepted, pending, or failed"));
            string failed=ConversationTerminalActionContext.Format(Action(NpcActionPhase.Failed));
            Check.True(failed.Contains("failed") && failed.Contains("Native route was blocked."));
            Check.True(failed.Contains("no action was accepted"));
            string cancelled=ConversationTerminalActionContext.Format(Action(NpcActionPhase.Cancelled));
            Check.True(cancelled.Contains("cancelled") && cancelled.Contains("The player cancelled the action."));
        }

        private static void OmitsUnsafeEvidence()
        {
            Check.Equal(string.Empty,ConversationTerminalActionContext.Format(Action(NpcActionPhase.Executing)));
            var invalid=Action(NpcActionPhase.Completed);invalid.CompletedEffectId=null;
            Check.Equal(string.Empty,ConversationTerminalActionContext.Format(invalid));
        }
    }
}
