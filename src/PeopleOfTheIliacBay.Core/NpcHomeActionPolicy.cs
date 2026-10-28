using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public static class NpcHomeActionPolicy
    {
        public static bool AllowsRepairErrand(NpcActionKind kind,bool exactContext,bool awake,Activity currentActivity,
            IList<Activity> scheduledDuringAction,bool familyAvailable,bool medicalAvailable,bool safe,out string reason)
        {
            reason="";
            if(kind!=NpcActionKind.Exit && kind!=NpcActionKind.Enter && kind!=NpcActionKind.Wait && kind!=NpcActionKind.Purchase || !exactContext)
            {reason="The actual home doorway or native repair service is unavailable.";return false;}
            if(!awake || currentActivity!=Activity.Home)
            {reason="A voluntary repair errand requires awake free home time.";return false;}
            if(!familyAvailable){reason="The resident must keep a childcare obligation.";return false;}
            if(!medicalAvailable){reason="Existing treatment or service duty takes priority.";return false;}
            if(!safe){reason="Danger or custody takes priority.";return false;}
            if(scheduledDuringAction==null || scheduledDuringAction.Count==0)
            {reason="The scheduled repair window is unavailable.";return false;}
            foreach(var activity in scheduledDuringAction)
            {
                if(activity==Activity.Work){reason="The errand would overlap scheduled work.";return false;}
                if(activity==Activity.Sleep){reason="The errand would overlap protected sleep.";return false;}
            }
            return true;
        }

        public static bool CanReleaseHomeOnlyRecovery(Activity currentActivity,bool verifiedHomeOnly,
            bool hasFutureCommitment,out string reason)
        {
            reason="";
            if(!verifiedHomeOnly){reason="The routine has no verified home-only recovery route.";return false;}
            if(currentActivity!=Activity.Home && currentActivity!=Activity.Sleep)
            {reason="The resident is not scheduled at home.";return false;}
            if(hasFutureCommitment){reason="A future visit or appointment still needs a verified route.";return false;}
            return true;
        }

        public static bool Allows(NpcActionKind kind,bool atOwnHome,bool awake,Activity currentActivity,
            IList<Activity> scheduledDuringAction,out string reason)
        {
            reason="";
            if(!atOwnHome){reason="The NPC is not at its own permanent home.";return false;}
            if(!awake || currentActivity!=Activity.Home){reason="Home actions require an awake resident at home.";return false;}
            if(scheduledDuringAction==null || scheduledDuringAction.Count==0)
            {reason="The scheduled action window is unavailable.";return false;}
            switch(kind)
            {
                case NpcActionKind.Wait:
                case NpcActionKind.Follow:
                case NpcActionKind.MoveTo:
                case NpcActionKind.Interact:
                case NpcActionKind.Transfer:
                case NpcActionKind.TakeSeat:
                    break;
                default:
                    reason="This action is not supported from the NPC's home.";return false;
            }
            foreach(Activity activity in scheduledDuringAction)
            {
                if(activity==Activity.Work){reason="The action would overlap scheduled work.";return false;}
                if(activity==Activity.Sleep){reason="The action would overlap protected sleep.";return false;}
            }
            return true;
        }
    }
}
