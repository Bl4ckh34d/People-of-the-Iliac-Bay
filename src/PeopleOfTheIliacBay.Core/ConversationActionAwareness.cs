using System;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    public static class ConversationActionAwareness
    {
        public static string Format(NpcActionJob job,NpcTaskPlan task,string physicalLocation,string target,string nextCommitment)
        {
            var text=new StringBuilder("Action awareness overrides schedule estimates for present location and progress. ");
            text.Append("Observed physical position: ").Append(Clip(physicalLocation,120,
                "no active body position is observed; schedule and saved marker do not prove location")).Append(". ");
            if(job==null) text.Append("No recorded action is active or returning.");
            else
            {
                text.Append("Action: ").Append(job.Kind).Append("; phase ").Append(job.Phase).Append("; ");
                if(!string.IsNullOrWhiteSpace(target)) text.Append("target ").Append(Clip(target,140,"unresolved")).Append("; ");
                switch(job.Phase)
                {
                    case NpcActionPhase.Accepted:text.Append("accepted but not started; not arrived and no effect is confirmed.");break;
                    case NpcActionPhase.Approaching:text.Append("in approach phase; not arrived and no effect is confirmed.");break;
                    case NpcActionPhase.Executing:text.Append("executing; effect is not confirmed complete.");break;
                    case NpcActionPhase.Completed:text.Append("executor confirmed completion.");break;
                    case NpcActionPhase.Failed:text.Append("failed; do not claim success.");break;
                    case NpcActionPhase.Cancelled:text.Append("cancelled; do not claim success.");break;
                    default:text.Append("not accepted; do not claim execution.");break;
                }
                if(job.Kind==NpcActionKind.Transfer && job.Target!=null)
                {
                    text.Append(" Transfer direction: ");
                    text.Append(job.Target.TransferDirection==NpcActionTransferDirection.PlayerToNpc ?
                        "from the player to the NPC" : job.Target.TransferDirection==NpcActionTransferDirection.NpcToContainer ?
                        "from the NPC's belongings into home container "+job.Target.ContainerId :
                        job.Target.TransferDirection==NpcActionTransferDirection.HomeContainerToNpc ?
                        "from household storage in the NPC's home into the NPC's belongings" : job.Target.PersonId=="player" ?
                        "from the NPC to the player" : "from the NPC to the named counterparty");
                    text.Append("; transfer is not confirmed complete unless action phase is Completed.");
                }
                if(job.ReturningToRoutine)
                    text.Append(" The action is terminal. Routine rejoin remains assigned; physical movement and arrival are not confirmed. A blocked route means waiting, not teleporting.");
            }
            if(task!=null)
            {
                if(task.Phase==NpcTaskPhase.Completed)
                    text.Append(" Ordered task: all ").Append(task.Steps.Count).Append(" steps have confirmed completion.");
                else if(task.Phase==NpcTaskPhase.Ready)
                    text.Append(" Ordered task: step ").Append(task.CurrentStepIndex+1).Append(" of ")
                        .Append(task.Steps.Count).Append(" is queued and not started; remaining steps have not happened.");
                else if(task.Phase==NpcTaskPhase.Running)
                    text.Append(" Ordered task: step ").Append(task.CurrentStepIndex+1).Append(" of ")
                        .Append(task.Steps.Count).Append(" is current; later steps have not happened.");
                else if(task.Phase==NpcTaskPhase.Failed || task.Phase==NpcTaskPhase.Cancelled)
                    text.Append(" Ordered task is ").Append(task.Phase).Append("; do not claim remaining steps happened.");
            }
            if(!string.IsNullOrWhiteSpace(nextCommitment))
                text.Append(" Next scheduled commitment: ").Append(Clip(nextCommitment,120,"unavailable")).Append('.');
            return text.ToString();
        }

        private static string Clip(string value,int maximum,string fallback)
        {
            if(string.IsNullOrWhiteSpace(value))return fallback;
            value=value.Trim().Replace('\r',' ').Replace('\n',' ');
            return value.Length<=maximum?value:value.Substring(0,maximum-1)+"…";
        }
    }
}
