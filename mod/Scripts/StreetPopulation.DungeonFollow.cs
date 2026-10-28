using PeopleOfTheIliacBay.Core;
using DaggerfallWorkshop;
using UnityEngine;

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
        // The timed return has reached this exact known recruitment building's
        // exterior door. Reconstruct its portal crossing at the native entry,
        // rather than appearing at a bed, another home or the player position.
        public bool CanReceiveRecoveredCompanionInterior(NpcActionJob job,WorldStateStore store)
        {
            var recovery=job==null?null:job.CompanionRecovery;
            if(!Ready || !interior || !directory || !ReferenceEquals(worldStore,store) || recovery==null ||
                recovery.RecruitmentOrigin.Kind!=NpcActionTargetKind.IndoorPoint ||
                directory.MapID!=recovery.RecruitmentOrigin.SettlementId || interior.EntryDoor.buildingKey!=recovery.RecruitmentOrigin.BuildingKey ||
                indoorGeometry==null || !indoorGeometry.EntryGround.HasValue || actors.ContainsKey(job.ResidentId) ||
                FindNpcActionPlan(job.ResidentId)==null)return false;
            return FollowLandingClear(indoorGeometry.EntryGround.Value);
        }
        public bool TryReceiveRecoveredCompanionInterior(NpcActionJob job,WorldStateStore store)
        {
            var recovery=job==null?null:job.CompanionRecovery;
            if(!CanReceiveRecoveredCompanionInterior(job,store) ||
                recovery.Phase!=NpcCompanionRecoveryPhase.ResumedRoutine || recovery.RecruitmentArrival==null ||
                !NpcFollowTransition.SameSpace(job.LastPosition,recovery.RecruitmentArrival) ||
                NpcFollowTransition.Distance(job.LastPosition,recovery.RecruitmentArrival)>0.55)return false;
            var ground=indoorGeometry.EntryGround.Value;
            var plan=FindNpcActionPlan(job.ResidentId);
            var body=Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject,interior.transform);
            var actor=body.AddComponent<ResidentActor>();
            actor.Initialize(plan.Resident,plan.SurnameSeed,navigation,onDeath:TravelerActionDeathHandler(plan),inventoryStore:store);
            actor.SetRunningPilotAtlas(runningPilotAtlas);
            actor.GetComponent<CrowdMotion>().RestorePlacement(ground);actors[job.ResidentId]=actor;actionMotions.Remove(job.ResidentId);
            // The existing routine return owns the walk onward. An expired Inn
            // booking leads through its real exit, without granting a new bed.
            var current=store.ReadNpcAction(job.Id);store.SaveNpcActionPosition(current.Id,current.Revision,ActionPosition(ground),true,ResidentActor.CompanionNow());
            return true;
        }

        internal string RecoveredCompanionInteriorDiagnostic(string id)
        {
            TravelerPlan traveler;travelerPlans.TryGetValue(id,out traveler);
            bool entry=indoorGeometry!=null && indoorGeometry.EntryGround.HasValue;
            return "residentPlan="+plans.Exists(plan=>plan.Resident.Id==id)+"; travelerPlan="+(traveler!=null)+
                "; actionPlan="+(FindNpcActionPlan(id)!=null)+"; entry="+entry+
                "; landingClear="+(entry && FollowLandingClear(indoorGeometry.EntryGround.Value))+
                "; actorRegistered="+actors.ContainsKey(id)+"; interior="+(bool)interior+
                "; map="+(directory?directory.MapID:-1)+"; building="+(interior?interior.EntryDoor.buildingKey:-1);
        }
    }
}
