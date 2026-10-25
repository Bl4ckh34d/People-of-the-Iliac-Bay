using System;

namespace PeopleOfTheIliacBay.Core
{
    // Formats an already-saved terminal action; it does not infer an outcome.
    public static class ConversationTerminalActionContext
    {
        public static string Format(NpcActionJob action)
        {
            if(action==null || !NpcActionExecutor.IsTerminal(action))return string.Empty;
            try {NpcActionExecutor.Validate(action);}
            catch(ArgumentException){return string.Empty;}
            string kind=action.Kind.ToString();
            if(action.Phase==NpcActionPhase.Completed)
                return "Latest verified native action result: "+kind+" completed; exact native success receipt="+
                    action.CompletedEffectId+". Do not claim this action was unaccepted, pending, or failed.";
            if(action.Phase==NpcActionPhase.Failed)
                return "Latest verified native action result: "+kind+" failed; exact native failure reason="+
                    action.FailureReason+". Do not claim completion or that no action was accepted.";
            if(action.Phase==NpcActionPhase.Cancelled)
                return "Latest verified native action result: "+kind+" cancelled; exact native cancellation reason="+
                    action.FailureReason+". Do not claim completion or that no action was accepted.";
            return string.Empty;
        }
    }
}
