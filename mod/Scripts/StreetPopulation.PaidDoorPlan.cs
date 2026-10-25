using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    // It owns no lodging or travel simulation. Binding exists only while one
    // active paid Follow action uses existing native door adapter.
    public sealed partial class StreetPopulation
    {
        private sealed class PaidDoorPlanBinding
        {
            public string ActionId;
            public Plan Plan;
            public WorldStateStore Store;
        }

        private readonly Dictionary<string,PaidDoorPlanBinding> paidDoorPlans=
            new Dictionary<string,PaidDoorPlanBinding>();

        private IEnumerable<Plan> RuntimePlansWithPaidDoor(double now)
        {
            RestorePaidDoorPlans(now);
            foreach(var plan in plans) yield return plan;
            foreach(var pair in new List<KeyValuePair<string,PaidDoorPlanBinding>>(paidDoorPlans))
            {
                var binding=pair.Value;
                if(binding==null || binding.Plan==null || !ReferenceEquals(binding.Store,worldStore))
                { paidDoorPlans.Remove(pair.Key);actionMotions.Remove(pair.Key);continue; }
                if(!plans.Exists(plan=>ReferenceEquals(plan,binding.Plan)))yield return binding.Plan;
            }
        }

        // Scene actor cleanup may run between native source and destination.
        // Rebuild only from persisted active paid Follow transition targeting this scene.
        private void RestorePaidDoorPlans(double now)
        {
            if(worldStore==null)return;
            foreach(var job in worldStore.PendingNpcActions())
            {
                if(job.Kind!=NpcActionKind.Follow || job.FollowTransition==null ||
                    !ActionSpaceMatches(job.FollowTransition.Destination))continue;
                NpcServiceContract contract;
                if(!NpcPaidCompanionTravel.TryReadActiveTravel(worldStore,job,now,out contract))continue;
                PaidDoorPlanBinding current;
                if(paidDoorPlans.TryGetValue(job.ResidentId,out current) && current!=null &&
                    ReferenceEquals(current.Store,worldStore) && current.ActionId==job.Id && current.Plan!=null)continue;
                ResidentActor actor;
                Resident resident=actors.TryGetValue(job.ResidentId,out actor) && actor ? actor.Resident :
                    worldStore.ResolveResident(job.ResidentId);
                if(resident==null || !resident.IsAlive)continue;
                paidDoorPlans[job.ResidentId]=new PaidDoorPlanBinding {ActionId=job.Id,Store=worldStore,
                    Plan=new Plan {Resident=resident}};
            }
        }

        private bool TryBindRoadPaidDoorPlan(ResidentActor actor,NpcActionJob job,double now)
        {
            if(actor==null || actor.Resident==null || job==null || worldStore==null)return false;
            NpcServiceContract contract;
            if(!NpcPaidCompanionTravel.TryReadActiveTravel(worldStore,job,now,out contract))return false;
            PaidDoorPlanBinding current;
            if(paidDoorPlans.TryGetValue(job.ResidentId,out current))
            {
                if(ReferenceEquals(current.Store,worldStore) && current.ActionId==job.Id && current.Plan!=null &&
                    ReferenceEquals(current.Plan.Resident,actor.Resident))return true;
                actionMotions.Remove(job.ResidentId);
            }
            paidDoorPlans[job.ResidentId]=new PaidDoorPlanBinding {ActionId=job.Id,Store=worldStore,
                Plan=new Plan {Resident=actor.Resident}};
            return true;
        }

        private bool IsPaidDoorActionPlan(Plan plan)
        {
            if(plan==null || plan.Resident==null)return false;
            PaidDoorPlanBinding binding;
            return paidDoorPlans.TryGetValue(plan.Resident.Id,out binding) &&
                ReferenceEquals(binding.Store,worldStore) && ReferenceEquals(binding.Plan,plan);
        }

        private bool TryScheduledPaidDoorPlan(Plan plan,double now,out ActivitySnapshot state)
        {
            state=default(ActivitySnapshot);
            if(!IsPaidDoorActionPlan(plan))return false;
            PaidDoorPlanBinding binding=paidDoorPlans[plan.Resident.Id];
            if(!ReferenceEquals(binding.Store,worldStore))return false;
            var job=worldStore==null?null:worldStore.ReadNpcAction(binding.ActionId);
            NpcServiceContract contract;
            if(!NpcPaidCompanionTravel.TryReadActiveTravel(worldStore,job,now,out contract))return false;
            var point=job.FollowTransition!=null ? job.FollowTransition.Destination : job.LastPosition;
            if(point==null)return false;
            bool indoors=point.Kind==NpcActionTargetKind.IndoorPoint && point.BuildingKey>=0;
            state=new ActivitySnapshot(indoors?Activity.Visit:Activity.Travel,indoors?point.BuildingKey:-1,
                new RoutePoint(point.X,point.Z),0);
            return true;
        }

        private NpcActionJob ReadPaidDoorAction(Plan plan)
        {
            PaidDoorPlanBinding binding;
            return IsPaidDoorActionPlan(plan) && paidDoorPlans.TryGetValue(plan.Resident.Id,out binding) &&
                ReferenceEquals(binding.Store,worldStore) && worldStore!=null ? worldStore.ReadNpcAction(binding.ActionId) : null;
        }

        private bool PaidDoorPlanNeedsRetirement(NpcActionJob job,double now)
        {
            if(job==null || !NpcPaidCompanionAction.IsPaidFollowJob(job))return true;
            NpcServiceContract contract;
            return NpcActionExecutor.IsTerminal(job) ||
                !NpcPaidCompanionTravel.TryReadActiveTravel(worldStore,job,now,out contract);
        }

        // Never destroy visible paid door body. Road owner keeps same actor at
        // actual point until existing offscreen/load rule permits removal.
        private bool RetirePaidDoorPlan(Plan plan,ref ResidentActor actor,NpcActionJob job,double now)
        {
            if(!IsPaidDoorActionPlan(plan))return false;
            if(actor && roadTravelActors && roadTravelActors.TryRetirePaidCompanionFromStreet(actor,worldStore,job,now))
            {
                actors.Remove(plan.Resident.Id);ReleasePaidDoorPlan(plan);return true;
            }
            if(!actor)ReleasePaidDoorPlan(plan);
            return true;
        }

        private void ReleasePaidDoorPlan(Plan plan)
        {
            if(!IsPaidDoorActionPlan(plan))return;
            paidDoorPlans.Remove(plan.Resident.Id);
            actionMotions.Remove(plan.Resident.Id);
        }

        // Full town/store teardown owns no destination scene. Do not call from
        // ClearActors: that path is native source-to-door transition.
        private void ClearPaidDoorPlans()
        {
            paidDoorPlans.Clear();
        }
    }
}
