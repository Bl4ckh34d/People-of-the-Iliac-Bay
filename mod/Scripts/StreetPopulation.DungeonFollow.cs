using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private RoadTravelActors DungeonFollowOwner
        {get{return roadTravelActors?roadTravelActors:GetComponent<RoadTravelActors>();}}
        public bool CanReceiveDungeonFollower(string id,WorldStateStore store,int map)
        {
            return Ready && directory && directory.MapID==map && !interior && ReferenceEquals(worldStore,store) &&
                plans.Exists(plan=>plan.Resident.Id==id) && !actors.ContainsKey(id);
        }
        public bool TryReceiveDungeonFollower(ResidentActor actor,WorldStateStore store,int map)
        {
            if(!actor || !CanReceiveDungeonFollower(actor.Resident.Id,store,map))return false;
            actor.TransferOwner(directory.transform,navigation);actor.SetRunningPilotAtlas(runningPilotAtlas);
            actors[actor.Resident.Id]=actor;actionMotions.Remove(actor.Resident.Id);return true;
        }
    }
}
