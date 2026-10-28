using System;
using System.Collections.Generic;
using System.Reflection;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>Controlled command evidence for opt-in native fixtures only.</summary>
    internal static class NpcProbeCommandSource
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        internal static bool TryAcceptEnterWait(StreetPopulation streets,WorldStateStore store,string residentId,string proposalId,
            NpcActionOption enter,int enterSeconds,int waitSeconds,out NpcTaskPlan task,out string reason)
        {
            task=null;reason="No actual Enter and bound Wait were offered.";
            if(!streets || store==null || !ReferenceEquals(store,Store(streets)) || enter==null || enter.Kind!=NpcActionKind.Enter || enter.Target==null)return false;
            var source=streets.ConversationActionCandidates(residentId);
            int maximum=enter.Id;foreach(var option in source)if(option!=null && option.Id>maximum)maximum=option.Id;
            var enterDecision=new NpcActionDecision {Option=enter,DurationSeconds=enterSeconds};
            foreach(var wait in NpcActionSequenceChat.BindWaitOptionsAfterMoveTo(enterDecision,source))
            {
                if(wait==null || wait.Kind!=NpcActionKind.Wait || wait.Target==null || !NpcTaskContract.TargetMatches(wait.Target,enter.Target))continue;
                var boundWait=new NpcActionOption {Id=maximum+1,Kind=NpcActionKind.Wait,Label=wait.Label,
                    Target=NpcActionExecutor.CopyTarget(wait.Target)};
                if(!TryAcceptTask(streets,residentId,proposalId,new List<NpcActionDecision>{enterDecision,
                    new NpcActionDecision {Option=boundWait,DurationSeconds=waitSeconds}},out reason))return false;
                task=store.ActiveNpcTask(residentId);
                if(task!=null)return true;
                reason="The accepted Enter/Wait task failed before its first native step.";return false;
            }
            return false;
        }

        internal static bool TryAcceptAction(StreetPopulation streets, string residentId, string proposalId,
            NpcActionDecision decision, out string reason)
        {
            string text = decision == null || decision.Option == null ? null :
                "Controlled native fixture action: " + decision.Option.Kind + ".";
            return TryAcceptAction(streets, residentId, proposalId, decision, text, out reason);
        }

        internal static bool TryAcceptTask(StreetPopulation streets, string residentId, string proposalId,
            List<NpcActionDecision> decisions, out string reason)
        {
            string text = decisions == null ? null :
                "Controlled native fixture task with " + decisions.Count + " ordered steps.";
            return TryAcceptTask(streets, residentId, proposalId, decisions, text, out reason);
        }

        internal static bool TryAcceptAction(StreetPopulation streets, string residentId, string proposalId,
            NpcActionDecision decision, string fixtureText, out string reason)
        {
            WorldStateStore store = Store(streets);
            NpcConversationCommandSession session;
            NpcConversationCommandTurn turn;
            if (!TryOpenTurn(store, residentId, fixtureText, out session, out turn))
            { reason = "Controlled fixture command turn could not be captured."; return false; }
            try { return streets.TryAcceptConversationAction(residentId, proposalId, decision, turn, out reason); }
            finally { store.TryCloseNpcConversationCommandSession(session); }
        }

        internal static bool TryAcceptTask(StreetPopulation streets, string residentId, string proposalId,
            List<NpcActionDecision> decisions, string fixtureText, out string reason)
        {
            WorldStateStore store = Store(streets);
            NpcConversationCommandSession session;
            NpcConversationCommandTurn turn;
            if (!TryOpenTurn(store, residentId, fixtureText, out session, out turn))
            { reason = "Controlled fixture command turn could not be captured."; return false; }
            try { return streets.TryAcceptConversationTask(residentId, proposalId, decisions, turn, out reason); }
            finally { store.TryCloseNpcConversationCommandSession(session); }
        }

        internal static bool TryAcceptDetachedAction(WorldStateStore store, ref NpcActionJob job,
            string fixtureText, out string reason)
        {
            NpcConversationCommandSession session;
            NpcConversationCommandTurn turn;
            if (job == null || !TryOpenTurn(store, job.ResidentId, fixtureText, out session, out turn))
            { reason = "Controlled fixture command turn could not be captured."; return false; }
            try
            {
                NpcActionJob prepared;
                NpcConversationCommandCapability capability;
                if (!store.TryPrepareNpcConversationAction(turn, job, false, out prepared) ||
                    !store.TryIssueNpcConversationAction(turn, prepared, out capability))
                { reason = "Controlled action source could not be issued."; return false; }
                if (!store.TryAcceptNpcActionFromConversation(capability, prepared, true, true, true, out reason))
                    return false;
                job = prepared;
                return true;
            }
            finally { store.TryCloseNpcConversationCommandSession(session); }
        }

        internal static bool TryAcceptDetachedTask(WorldStateStore store, ref NpcTaskPlan task,
            string fixtureText, out string reason)
        {
            NpcConversationCommandSession session;
            NpcConversationCommandTurn turn;
            if (task == null || !TryOpenTurn(store, task.ResidentId, fixtureText, out session, out turn))
            { reason = "Controlled fixture command turn could not be captured."; return false; }
            try
            {
                NpcTaskPlan prepared;
                NpcConversationCommandCapability capability;
                if (!store.TryPrepareNpcConversationTask(turn, task, out prepared) ||
                    !store.TryIssueNpcConversationTask(turn, prepared, out capability))
                { reason = "Controlled task source could not be issued."; return false; }
                if (!store.TryAcceptNpcTaskFromConversation(capability, prepared, true, out reason))
                    return false;
                task = prepared;
                return true;
            }
            finally { store.TryCloseNpcConversationCommandSession(session); }
        }

        private static WorldStateStore Store(StreetPopulation streets)
        { return streets == null ? null : typeof(StreetPopulation).GetField("worldStore", Private).GetValue(streets) as WorldStateStore; }

        private static bool TryOpenTurn(WorldStateStore store, string residentId, string fixtureText,
            out NpcConversationCommandSession session, out NpcConversationCommandTurn turn)
        {
            session = null;
            turn = null;
            if (store == null || string.IsNullOrWhiteSpace(residentId) || string.IsNullOrWhiteSpace(fixtureText))
                return false;
            string sessionId = "native-probe:" + Guid.NewGuid().ToString("N");
            if (!store.TryBeginNpcConversationCommandSession(residentId, sessionId, out session)) return false;
            var binding = new ConversationContextBinding(ConversationActorCategory.Mobile, residentId, null,
                sessionId, 1, 1, store.NpcFrameworkScopeId, "", "native-controlled-probe", 1);
            if (store.TryCaptureNpcConversationCommandTurn(session, binding, fixtureText, out turn)) return true;
            store.TryCloseNpcConversationCommandSession(session);
            return false;
        }
    }
}
