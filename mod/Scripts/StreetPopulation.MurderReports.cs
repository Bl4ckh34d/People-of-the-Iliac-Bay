using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private void OfferMurderReportAction(ResidentActor actor,List<NpcActionOption> options)
        {
            var guards=GetComponent<GuardPopulation>();
            if(interior || !guards || geometry==null)return;
            string guardId;
            if(!guards.TryFindNearestReportListener(actor.transform.position-Vector3.up,out guardId))return;
            AddActionOption(options,NpcActionKind.ReportMurder,new NpcActionTarget {
                Kind=NpcActionTargetKind.Person,PersonId=guardId,SettlementId=population.SettlementId},
                "go to the nearest reachable guard and deliver the player's anonymous report of a murder; only if the player asks to report a murder; unverified hearsay, no identified suspect, victim or crime location, no arrest promised",null,true);
        }

        private bool ResolveMurderReportGuard(NpcActionJob job,out Vector3 target,out float range,out string reason)
        {
            var guards=GetComponent<GuardPopulation>();
            target=Vector3.zero;range=1.35f;reason="The selected guard is no longer available to hear the report.";
            return !interior && guards && job.Target.SettlementId==population.SettlementId &&
                guards.TryGetComplaintGuardPosition(job.Target.PersonId,out target);
        }

        private bool DeliverMurderReport(NpcActionJob job,ResidentActor actor,double now,out string reason)
        {
            reason="The guard could not hear the report at the observed position.";
            Vector3 target;float range;
            if(!ResolveMurderReportGuard(job,out target,out range,out reason))return false;
            reason="The guard could not hear the report at the observed position.";
            var origin=actor.transform.position-Vector3.up;
            // A guard outside the player's rendering radius is still a person.
            // Materialize its ordinary saved body only when the messenger has
            // physically reached it, before checking actual separation and LOS.
            var guards=GetComponent<GuardPopulation>();
            if(!guards.TryGetReportListener(job.Target.PersonId,origin,out target))return false;
            bool visible=CivilianResponses.Sees(origin+Vector3.up,target+Vector3.up,2.8f) &&
                CivilianResponses.Sees(target+Vector3.up,origin+Vector3.up,2.8f);
            return worldStore.TryDeliverNpcMurderReport(job.Id,job.Revision,now,job.Target.PersonId,
                Vector3.Distance(origin,target),visible);
        }
    }

    public sealed partial class GuardPopulation
    {
        public bool TryGetReportListener(string guardId,Vector3 visitor,out Vector3 position)
        {
            position=Vector3.zero;
            Vector3 scheduled;GuardActor actor;
            if(!TryGetComplaintGuardPosition(guardId,out scheduled) || Vector3.Distance(visitor,scheduled)>1.8f)return false;
            actor=View(Roster.Guards.Find(row=>row.Id==guardId),Now());
            if(!actor || actor.IsInterior)return false;
            if(!actor.gameObject.activeInHierarchy)actor.Place(Now());
            actor.gameObject.SetActive(true);
            position=actor.GroundPosition;
            return true;
        }

        public bool TryFindNearestReportListener(Vector3 origin,out string guardId)
        {
            Vector3 target;List<SavedRoutePoint> route;
            return TryFindNearestComplaintGuard(origin,out guardId,out target,out route);
        }
    }
}
