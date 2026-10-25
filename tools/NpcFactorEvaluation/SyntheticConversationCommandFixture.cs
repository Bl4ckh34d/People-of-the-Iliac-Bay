using System;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.NpcFactorEvaluation
{
    /// <summary>
    /// Issues typed conversation commands in an isolated evaluation store. This is
    /// synthetic command evidence, not a live Talk session or native observation.
    /// </summary>
    internal static class SyntheticConversationCommandFixture
    {
        internal static bool TryAcceptTask(WorldStateStore store, NpcTaskPlan proposed, out string reason)
        {
            reason = "Synthetic task command could not open.";
            NpcConversationCommandSession session;
            NpcConversationCommandTurn turn;
            if (proposed == null || !TryOpen(store, proposed.ResidentId, "Agree to this task.", out session, out turn))
                return false;
            try
            {
                NpcTaskPlan prepared;
                NpcConversationCommandCapability capability;
                if (!store.TryPrepareNpcConversationTask(turn, proposed, out prepared) ||
                    !store.TryIssueNpcConversationTask(turn, prepared, out capability))
                { reason = "Synthetic task command could not be prepared and issued."; return false; }
                return store.TryAcceptNpcTaskFromConversation(capability, prepared, true, out reason);
            }
            finally { store.TryCloseNpcConversationCommandSession(session); }
        }

        internal static bool TryAcceptAction(WorldStateStore store, NpcActionJob proposed, out string reason)
        {
            reason = "Synthetic action command could not open.";
            NpcConversationCommandSession session;
            NpcConversationCommandTurn turn;
            if (proposed == null || !TryOpen(store, proposed.ResidentId, "Wait here.", out session, out turn))
                return false;
            try
            {
                NpcActionJob prepared;
                NpcConversationCommandCapability capability;
                if (!store.TryPrepareNpcConversationAction(turn, proposed, false, out prepared) ||
                    !store.TryIssueNpcConversationAction(turn, prepared, out capability))
                { reason = "Synthetic action command could not be prepared and issued."; return false; }
                return store.TryAcceptNpcActionFromConversation(capability, prepared, true, true, true, out reason);
            }
            finally { store.TryCloseNpcConversationCommandSession(session); }
        }

        private static bool TryOpen(WorldStateStore store, string personId, string text,
            out NpcConversationCommandSession session, out NpcConversationCommandTurn turn)
        {
            session = null;
            turn = null;
            if (store == null || string.IsNullOrWhiteSpace(personId)) return false;
            string sessionId = "synthetic-evaluation:" + Guid.NewGuid().ToString("N");
            if (!store.TryBeginNpcConversationCommandSession(personId, sessionId, out session)) return false;
            var binding = new ConversationContextBinding(ConversationActorCategory.Mobile, personId, null,
                sessionId, 1, 1, store.NpcFrameworkScopeId, "", "synthetic-evaluation", 1);
            if (store.TryCaptureNpcConversationCommandTurn(session, binding, text, out turn)) return true;
            store.TryCloseNpcConversationCommandSession(session);
            return false;
        }
    }
}
