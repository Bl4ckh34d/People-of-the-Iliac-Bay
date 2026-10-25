using System;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    public static class SharedActivityConversationContext
    {
        // Caller must pass the stable identity of the NPC selected in the current Talk window.
        public static string Build(NpcActionJob job,string currentSpeakerId,double now,string observedLocation)
        {
            if(job==null || string.IsNullOrWhiteSpace(currentSpeakerId) || job.ResidentId!=currentSpeakerId ||
                !Finite(now) || now<0 || job.Kind!=NpcActionKind.Follow || job.Target==null ||
                job.Target.Kind!=NpcActionTargetKind.Person || job.Target.PersonId!="player" ||
                job.ReturningToRoutine || job.Phase!=NpcActionPhase.Approaching && job.Phase!=NpcActionPhase.Executing ||
                NpcActionExecutor.IsTerminal(job) || job.AcceptedAt>now || job.UpdatedAt>now || now>=job.DeadlineAt ||
                job.FinishAt>0 && now>=job.FinishAt)
                return string.Empty;

            var context=new StringBuilder("Verified current facts for this conversation: the currently selected NPC owns an active Follow action targeting the player (phase ")
                .Append(job.Phase).Append("). The current Talk session confirms this speaker is present now, and the Follow action remains in progress. These facts do not prove uninterrupted historical presence. Reopening Talk or crossing a doorway alone does not establish that you were apart. Do not invent an absence, reunion, or uninterrupted company. ");
            if(!string.IsNullOrWhiteSpace(observedLocation))
                context.Append("Observed current conversation location: ").Append(Clip(observedLocation,200)).Append(". ");
            if(job.FollowTransition!=null)
                context.Append("A saved doorway transition is in progress; arrival is not confirmed. Do not claim entry into the destination. ");
            else
                context.Append("Do not infer arrival at any destination from the follow action alone. ");
            return context.ToString();
        }

        private static string Clip(string value,int maximum)
        {
            value=(value??string.Empty).Trim().Replace('\r',' ').Replace('\n',' ');
            return value.Length<=maximum?value:value.Substring(0,maximum-1)+"…";
        }

        private static bool Finite(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }
}
