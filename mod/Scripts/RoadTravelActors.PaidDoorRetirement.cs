using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class RoadTravelActors
    {
        // Terminal local-door cleanup changes owner only. Existing retired-hold
        // policy keeps this exact visible actor until offscreen/load.
        public bool TryRetirePaidCompanionFromStreet(ResidentActor actor,WorldStateStore store,
            NpcActionJob action,double now)
        {
            if(!actor || actor.Resident==null || !actor.Resident.IsAlive || store==null || action==null ||
                action.ResidentId!=actor.Resident.Id || !NpcPaidCompanionAction.IsPaidFollowJob(action) || source==null ||
                !ReferenceEquals(source(),store))return false;
            current=store;
            actor.TransferOwner(transform,navigation);
            paidActions[actor.Resident.Id]=action.Id;
            paidCompanions[actor.Resident.Id]=actor;
            return HoldRetiredPaidCompanion(actor.Resident.Id,store,action,now);
        }
    }
}
