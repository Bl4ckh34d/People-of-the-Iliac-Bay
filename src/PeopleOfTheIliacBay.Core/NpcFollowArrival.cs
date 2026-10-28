namespace PeopleOfTheIliacBay.Core
{
    public static class NpcFollowArrival
    {
        public static bool CanHold(double bodyDistance, bool completeWalkingRoute, double walkingDistance)
        {
            return bodyDistance >= 0 && bodyDistance <= 1.8 && completeWalkingRoute &&
                walkingDistance >= 0 && walkingDistance <= 1.8;
        }
    }
}
