using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private bool AwakeHomeActionFitsWindow(Plan plan,ResidentActor actor,NpcActionJob job,double now)
        {
            if(plan==null || !actor || job==null || job.DeadlineAt<=now) return false;
            bool atHome=interior && plan.Resident.HomeKey==interior.EntryDoor.buildingKey;
            var current=Scheduled(plan,now);
            var window=new List<Activity>();
            // Local actions last at most fifteen minutes. Sample every minute so
            // a brief work/sleep boundary cannot hide behind a safe deadline.
            for(double sample=now;sample<job.DeadlineAt;)
            {
                window.Add(Scheduled(plan,sample).Activity);
                double next=(Math.Floor(sample/60d)+1d)*60d;
                if(next<=sample) next=sample+1d;
                sample=Math.Min(job.DeadlineAt,next);
            }
            window.Add(Scheduled(plan,job.DeadlineAt).Activity);
            if(window.Count==0)window.Add(current.Activity);
            if(NpcHomeActionPolicy.Allows(job.Kind,atHome,!actor.IsSleeping,current.Activity,window,out _))return true;
            bool exactRepairContext=false;
            if(job.Kind==NpcActionKind.Exit)exactRepairContext=atHome && job.Target!=null &&
                job.Target.Kind==NpcActionTargetKind.Building && job.Target.BuildingKey==plan.Resident.HomeKey;
            else if(job.Kind==NpcActionKind.Enter && !interior && job.Target!=null &&
                job.Target.Kind==NpcActionTargetKind.Building && directory)
            {
                BuildingSummary target;
                exactRepairContext=directory.GetBuildingSummary(job.Target.BuildingKey,out target) &&
                    RMBLayout.IsRepairShop(target.BuildingType) && CanEnterBuilding(actor,job.Target.BuildingKey);
            }
            else if(job.Kind==NpcActionKind.Purchase && IsNativeRepairTarget(job.Target) && interior)
            {
                NativePerson keeper;StaticNPC keeperNpc;
                exactRepairContext=TryNativeRepairKeeper(out keeper,out keeperNpc) && keeper.Id==job.Target.PersonId;
            }
            else if(job.Kind==NpcActionKind.Wait && interior && job.Target!=null && job.Target.Kind==NpcActionTargetKind.Building &&
                job.Target.BuildingKey==interior.EntryDoor.buildingKey &&
                (RMBLayout.IsRepairShop(interior.BuildingData.BuildingType) || interior.BuildingData.BuildingType==DaggerfallConnect.DFLocation.BuildingTypes.Temple))
            {
                // A queued service wait belongs to the already accepted task;
                // it is not permission for arbitrary remote waiting from home.
                var accepted=worldStore.ActiveNpcTask(plan.Resident.Id);
                if(accepted!=null && accepted.CurrentStepIndex>0 && job.Id==NpcTaskContract.ActionId(accepted))
                {
                    int index=accepted.CurrentStepIndex;
                    var entered=worldStore.ReadNpcAction(accepted.Id+":step:"+(index-1));
                    exactRepairContext=accepted.Steps[index-1].Kind==NpcActionKind.Enter &&
                        NpcTaskPositionBinding.HasCompletedStaticArrivalReceipt(accepted.Steps[index-1],entered,accepted.Steps[index]);
                }
            }
            if(!exactRepairContext)return false;
            string id=plan.Resident.Id;
            return NpcHomeActionPolicy.AllowsRepairErrand(job.Kind,true,!actor.IsSleeping,current.Activity,window,
                FamilyAllowsOptionalOuting(plan.Resident,now%86400,job.DeadlineAt%86400),
                !HasActiveNpcHealthCare(id) && worldStore.ReadNpcTreatmentVisit(id)==null &&
                    !worldStore.HasCurrentNpcServiceDuty(id,now) && !HasCurrentAppointmentDuty(id,now),
                !IsAlarmed(id) && !worldStore.HasActiveNpcCustody(id),out _);
        }
    }
}
