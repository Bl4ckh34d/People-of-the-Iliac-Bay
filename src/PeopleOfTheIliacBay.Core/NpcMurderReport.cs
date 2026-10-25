using System;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        // The native adapter supplies the measured separation and visibility of
        // the two live bodies. This reports hearsay, never a verified crime.
        public bool TryDeliverNpcMurderReport(string actionId,long revision,double now,
            string guardId,double measuredDistance,bool mutuallyVisible)
        {
            var job=ReadNpcAction(actionId);
            if(job==null || job.Revision!=revision || job.Kind!=NpcActionKind.ReportMurder ||
                job.Phase!=NpcActionPhase.Executing || job.Target.PersonId!=guardId ||
                !mutuallyVisible || double.IsNaN(measuredDistance) || double.IsInfinity(measuredDistance) ||
                measuredDistance<0 || measuredDistance>1.8) return false;
            var actor=ResolveResident(job.ResidentId);
            var roster=GetGuards(job.Target.SettlementId);
            var guard=roster==null?null:roster.Guards.Find(row=>row.Id==guardId);
            if(actor==null || !actor.IsAlive || actor.IsQuestProtected || guard==null || !guard.IsAlive ||
                guard.Duty!=GuardDuty.Posted || guard.Interior!=null || guard.Refuge!=null)return false;
            NpcActionJob completed;
            if(!NpcActionExecutor.Advance(job,revision,now,NpcActionObservation.Success(job.Id+":murder-report-delivered"),
                out completed) || completed.Phase!=NpcActionPhase.Completed)return false;
            string messenger="I delivered the player's anonymous report of a murder to a guard. This was the player's claim, not my eyewitness knowledge. No killer, victim or crime location was verified, and no arrest or investigation was confirmed.";
            string listener="A resident brought an anonymous traveller's report of a murder. This is unverified hearsay. I do not know the original informant, killer, victim or crime location from this report. No arrest or investigation was confirmed.";
            string place="settlement "+job.Target.SettlementId+" (report delivery location, not a known crime scene)";
            Remember(job.ResidentId,new NpcMemoryEntry {At=now,Date="",Place=place,PlayerText="",NpcText="",
                Observation=messenger,Source="observed",SourceActionId=job.Id});
            Remember(guardId,new NpcMemoryEntry {At=now,Date="",Place=place,PlayerText="",NpcText="",
                Observation=listener,Source="observed",SourceActionId=job.Id});
            RememberSceneEvent(job.ResidentId,job.Id,NpcSceneEventKind.Conversation,now,job.Target.SettlementId,-1,
                messenger,-2,subject:"player");
            RememberSceneEvent(guardId,job.Id,NpcSceneEventKind.Conversation,now,job.Target.SettlementId,-1,listener,-2);
            int index=state.NpcActions.FindIndex(row=>row.Id==job.Id);
            state.NpcActions[index]=completed;IndexNpcAction(completed);
            return true;
        }
    }
}
