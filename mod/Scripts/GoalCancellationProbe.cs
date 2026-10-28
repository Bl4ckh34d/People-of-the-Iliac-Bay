using System.Collections;
namespace PeopleOfTheIliacBay.DFU
{
    internal static class GoalCancellationProbe
    {
        internal static IEnumerator Run(PeopleOfTheIliacBayMod host,StreetPopulation streets,string character)
        {
            return GoalDialogueWillingnessProbe.Run(host,streets,character,"People - Gothway Garden");
        }
    }
}
