using PeopleOfTheIliacBay.Core;
using DaggerfallWorkshop.Game;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private bool PendingTravelerDeparture(ResidentActor actor,TravelerPlan plan,double now,out Vector3 endpoint)
        {
            endpoint=Vector3.zero;
            if(!plan.ObservedOutdoorDeparture || now<plan.Journey.ExitAt || !actor || !plan.Person.IsAlive ||
                !directory || geometry==null || interior || GameManager.Instance.IsPlayerInside ||
                actor.transform.parent!=directory.transform)return false;
            RoutePoint exit;
            if(!plan.Journey.TryTravelEndpoint(TravelerPhase.Leaving,out exit))return false;
            endpoint=geometry.ScenePoint(exit);
            // A rendered traveler delayed by a crowd or a closing native gate
            // must finish the physical path before its visible body retires.
            // Unobserved/off-screen travel retains the ordinary abstraction.
            bool pending=Vector3.Distance(actor.transform.position-Vector3.up,endpoint)>0.55f;
            if(pending)worldStore.ObserveTravelerDepartureDelay(plan.Visit.Id,now);
            return pending;
        }

        internal string PendingTravelerDepartureContext(WorldStateStore activeStore,string personId,double now)
        {
            if(!ReferenceEquals(activeStore,worldStore) || Reaction(personId,now)!=null || TravelerActionOwnsVisit(personId))return null;
            TravelerPlan plan;Vector3 endpoint;
            if(!travelerPlans.TryGetValue(personId,out plan) ||
                !PendingTravelerDeparture(FindActor(personId),plan,now,out endpoint))return null;
            return "I am still making my way out of town. I have not reached the exit yet; my departure has been delayed.";
        }
    }
}
