namespace PeopleOfTheIliacBay.Core
{
    public static class TravelerActionRoutine
    {
        public static bool KeepsVisitAlive(bool alive,NpcActionJob action,bool activeTask)
        {return alive && (activeTask || action!=null && (!NpcActionExecutor.IsTerminal(action) || action.ReturningToRoutine));}

        public static bool ReturnsToInn(TravelerAdmission admission,TravelerJourney journey,double now)
        {
            return journey!=null && (now<journey.CheckInAt || admission==TravelerAdmission.Pending && now<journey.CheckOutAt ||
                admission==TravelerAdmission.Admitted && now<journey.LeaveInnAt);
        }
    }
}
