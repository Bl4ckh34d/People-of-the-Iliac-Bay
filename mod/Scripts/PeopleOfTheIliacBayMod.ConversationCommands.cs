using System;
using System.Runtime.CompilerServices;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private NpcConversationCommandSession conversationCommandSession;
        // Exact request snapshots keep their own immutable source token through async callbacks.
        // No callback may borrow a newer turn from a mutable current-token field.
        private readonly ConditionalWeakTable<ConversationContextSnapshot, NpcConversationCommandTurn>
            conversationCommandTurns = new ConditionalWeakTable<ConversationContextSnapshot, NpcConversationCommandTurn>();

        private void BeginConversationCommandSession()
        {
            EndConversationCommandSession();
            if (!saveFault && store != null && !string.IsNullOrWhiteSpace(conversationPersonId) &&
                !string.IsNullOrWhiteSpace(conversationContextSessionId))
                store.TryBeginNpcConversationCommandSession(conversationPersonId,
                    conversationContextSessionId, out conversationCommandSession);
        }

        private void EndConversationCommandSession()
        {
            if (conversationCommandSession != null && store != null)
                store.TryCloseNpcConversationCommandSession(conversationCommandSession);
            conversationCommandSession = null;
        }

        private void CaptureConversationCommandTurn(ConversationContextSnapshot snapshot, string playerText)
        {
            if (snapshot == null || conversationCommandSession == null || store == null || saveFault) return;
            NpcConversationCommandTurn turn;
            if (store.TryCaptureNpcConversationCommandTurn(conversationCommandSession,
                snapshot.Binding, playerText, out turn)) conversationCommandTurns.Add(snapshot, turn);
        }

        private NpcConversationCommandTurn CommandTurnFor(ConversationContextSnapshot snapshot)
        {
            NpcConversationCommandTurn turn;
            return snapshot != null && conversationCommandTurns.TryGetValue(snapshot, out turn) ? turn : null;
        }

        private void CloseConversationCommandTurn(ConversationContextSnapshot snapshot)
        {
            var turn = CommandTurnFor(snapshot);
            if (turn != null && store != null) store.TryCloseNpcConversationCommandTurn(turn);
        }
    }
}
