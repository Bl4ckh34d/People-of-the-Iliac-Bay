namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        public int RoutineRevision {get;private set;}
        private void BumpRoutineRevision(){if(RoutineRevision<int.MaxValue)RoutineRevision++;}
    }
}
