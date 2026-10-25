using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class GuardRoster
    {
        public bool CorpseResponseExpired(string guardId,double now)
        {
            Require(Time(now),"Invalid corpse response clock.");
            var guard=Guards.Find(row=>row.Id==guardId);
            var report=guard==null?null:Reports.Find(row=>row.Id==guard.IncidentId && row.HasCorpseInquiryWindow);
            return guard==null || !guard.IsAlive || report==null ||
                now>=(guard.Duty==GuardDuty.Engaged?guard.SearchUntil:report.InvestigationUntil);
        }
        internal bool BeginCorpseInquiry(string incidentId,double clearedAt)
        {
            Require(Time(clearedAt) && Time(clearedAt+1800d),"Invalid corpse inquiry time.");
            var report=Reports.Find(row=>row.Id==incidentId && row.Investigation && !row.BuildingKey.HasValue);
            if(report==null || report.GuardIds.Count==0)return false;
            if(report.HasCorpseInquiryWindow)return report.CorpseInquiryStartedAt==clearedAt;
            double until=Math.Min(InvestigationDeadline(report,clearedAt),clearedAt+1800d);
            report.HasCorpseInquiryWindow=true;report.CorpseInquiryStartedAt=clearedAt;
            report.InvestigationUntil=until;
            foreach(var guard in Guards)
                if(guard.IsAlive && guard.IncidentId==report.Id &&
                    (guard.Duty==GuardDuty.Responding || guard.Duty==GuardDuty.Searching))guard.SearchUntil=until;
            return true;
        }

        // A late responder follows surveyed geometry back from its actual point;
        // the inquiry deadline is not permission to jump to the scene entrance.
        private static void BeginInterruptedResponseReturn(GuardRecord guard,double at)
        {
            double length=Length(guard.Route),duration=guard.ArrivesAt-guard.DepartedAt;
            double along=duration<=0?length:length*Math.Max(0,Math.Min(1,(at-guard.DepartedAt)/duration));
            var forward=Remaining(guard.Route,along);
            int post=forward.FindIndex(point=>Distance(point,guard.Post)<0.05);
            List<SavedRoutePoint> back;
            if(post>=0)
            {
                back=forward;back.RemoveRange(post+1,back.Count-post-1);
            }
            else
            {
                var reverse=CopyRoute(guard.Route);reverse.Reverse();
                back=Remaining(reverse,length-along);
                post=back.FindIndex(point=>Distance(point,guard.Post)<0.05);
                if(post>=0)back.RemoveRange(post+1,back.Count-post-1);
                else
                {
                    int origin=guard.ReturnPath.FindIndex(point=>Distance(point,guard.Route[0])<0.05);
                    Require(origin>=0,"Interrupted response has no surveyed return connector.");
                    for(int i=origin+1;i<guard.ReturnPath.Count;i++)back.Add(CopyPoint(guard.ReturnPath[i]));
                }
            }
            if(back.Count==1)back.Add(CopyPoint(back[0]));
            guard.ReturnPath=back;BeginReturn(guard,at);
        }
    }
}
