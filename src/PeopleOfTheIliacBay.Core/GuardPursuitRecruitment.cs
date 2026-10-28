using System;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class GuardRoster
    {
        public bool KnowsPlayerCrime(string id)
        {
            var guard=Find(id);
            return guard.IsAlive && Reports.Exists(r=>r.ThreatId==null && !r.Investigation &&
                !r.Id.StartsWith("post:",StringComparison.Ordinal) &&
                !r.Id.StartsWith("gate-recall:",StringComparison.Ordinal) && r.GuardIds.Contains(id));
        }
        public double PursuitRecruitmentRadius(string id)
        { return 8 + Find(id).AppearanceSeed % 4001 / 1000.0; }

        public bool CanRecruitPursuit(string callerId,string candidateId,double now,double distance)
        { return CanRecruitPursuit(callerId,candidateId,now,distance,false); }

        // Seeing an actual player pursuit is sufficient. Close guard contact
        // remains necessary only when passing along the earlier sighting.
        public bool CanRecruitPursuit(string callerId,string candidateId,double now,double distance,bool observedPlayerPursuit)
        {
            Require(Time(now) && Finite(distance) && distance>=0,"Invalid pursuit contact.");
            var caller=Find(callerId); var candidate=Find(candidateId);
            var report=Reports.Find(r=>r.Id==caller.IncidentId);
            return callerId!=candidateId && caller.IsAlive && caller.Duty==GuardDuty.Engaged &&
                caller.SearchUntil>now && caller.Interior==null && caller.Refuge==null &&
                report!=null && report.ThreatId==null &&
                candidate.IsAlive && candidate.Duty==GuardDuty.Posted &&
                candidate.Interior==null && candidate.Refuge==null &&
                (observedPlayerPursuit || distance<=PursuitRecruitmentRadius(candidateId));
        }
    }
}
