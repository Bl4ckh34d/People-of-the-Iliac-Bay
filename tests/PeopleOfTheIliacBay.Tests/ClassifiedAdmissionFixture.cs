using System;
using System.Threading;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    // A controlled player-turn source for existing Core lifecycle fixtures.
    // It uses the production prepare/issue/accept path; no test-only store bypass.
    internal static class ClassifiedAdmissionFixture
    {
        private static long nextSession;

        internal static bool TryAcceptAction(WorldStateStore store, NpcActionJob action,
            bool adapterAvailable, bool consent, bool scheduleCompatible, out string reason)
        {
            reason = "Could not capture a controlled conversation turn.";
            NpcConversationCommandSession session;
            NpcConversationCommandTurn turn;
            if (!TryTurn(store, action == null ? null : action.ResidentId, out session, out turn)) return false;
            try
            {
                NpcActionJob prepared;
                NpcConversationCommandCapability capability;
                if (!store.TryPrepareNpcConversationAction(turn, action, false, out prepared))
                { reason = "The action proposal is invalid before issuance."; return false; }
                if (!store.TryIssueNpcConversationAction(turn, prepared, out capability))
                { reason = "The controlled action command could not be issued."; return false; }
                if (!store.TryAcceptNpcActionFromConversation(capability, prepared,
                    adapterAvailable, consent, scheduleCompatible, out reason)) return false;
                action.Id = prepared.Id;
                action.Admission = NpcOperationAdmission.CopyValidated(prepared.Admission);
                return true;
            }
            finally { store.TryCloseNpcConversationCommandSession(session); }
        }

        internal static bool TryAcceptTask(WorldStateStore store, NpcTaskPlan task, bool consent, out string reason)
        {
            reason = "Could not capture a controlled conversation turn.";
            NpcConversationCommandSession session;
            NpcConversationCommandTurn turn;
            if (!TryTurn(store, task == null ? null : task.ResidentId, out session, out turn)) return false;
            try
            {
                NpcTaskPlan prepared;
                NpcConversationCommandCapability capability;
                if (!store.TryPrepareNpcConversationTask(turn, task, out prepared))
                { reason = "The task proposal is invalid before issuance."; return false; }
                if (!store.TryIssueNpcConversationTask(turn, prepared, out capability))
                { reason = "The controlled task command could not be issued."; return false; }
                if (!store.TryAcceptNpcTaskFromConversation(capability, prepared, consent, out reason)) return false;
                task.Id = prepared.Id;
                task.Admission = NpcOperationAdmission.CopyValidated(prepared.Admission);
                return true;
            }
            finally { store.TryCloseNpcConversationCommandSession(session); }
        }

        private static bool TryTurn(WorldStateStore store, string personId,
            out NpcConversationCommandSession session, out NpcConversationCommandTurn turn)
        {
            session = null;
            turn = null;
            if (store == null || string.IsNullOrWhiteSpace(personId)) return false;
            string sessionId = "test-admission-" + Interlocked.Increment(ref nextSession);
            if (!store.TryBeginNpcConversationCommandSession(personId, sessionId, out session)) return false;
            var binding = new ConversationContextBinding(ConversationActorCategory.Mobile, personId, null,
                sessionId, 1, 1, store.NpcFrameworkScopeId, "", "scene:test", 1);
            if (store.TryCaptureNpcConversationCommandTurn(session, binding, "Controlled test command.", out turn)) return true;
            store.TryCloseNpcConversationCommandSession(session);
            session = null;
            return false;
        }
    }
}
