using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class GuardRoster
    {
        public List<GuardRecord> DispatchDepartureInvestigation(string incidentId,double departureAt,double now,
            IDictionary<string,List<SavedRoutePoint>> approaches,List<SavedRoutePoint> patrol,double speed,double timeScale)
        {
            Require(Time(departureAt) && Time(now) && now>=departureAt && approaches!=null &&
                Finite(speed) && speed>0 && Finite(timeScale) && timeScale>0,"Invalid departure investigation.");
            var existing=Reports.Find(r=>r.Id==incidentId);
            if(existing!=null && existing.GuardIds.Count>0)
            {
                var assigned=ReportGuards(existing);
                if(!existing.DepartureInvestigation && existing.Investigation && existing.SearchRoute!=null)
                {
                    double reserve=0,latestArrival=0;bool activeFound=false;
                    foreach(var guard in assigned)
                        if(guard.IsAlive && guard.IncidentId==existing.Id &&
                            (guard.Duty==GuardDuty.Searching || guard.Duty==GuardDuty.Responding) && guard.ReturnPath!=null)
                        {
                            reserve=Math.Max(reserve,(Length(guard.ReturnPath)+Length(existing.SearchRoute))*guard.ReturnRate);
                            latestArrival=Math.Max(latestArrival,guard.ArrivesAt);activeFound=true;
                        }
                    double until=departureAt+3600-reserve;
                    if(existing.HasCorpseInquiryWindow)until=Math.Min(until,existing.CorpseInquiryStartedAt+1800d);
                    if(activeFound && Time(reserve) && reserve<3600 && Time(until) &&
                        (existing.HasCorpseInquiryWindow?until>=now:until>=latestArrival))
                    {
                        existing.DepartureInvestigation=true;existing.DepartureAt=departureAt;
                        existing.DepartureReturnBudget=reserve;existing.InvestigationUntil=until;
                        foreach(var active in assigned)if(active.IncidentId==existing.Id &&
                            (active.Duty==GuardDuty.Searching || active.Duty==GuardDuty.Responding))active.SearchUntil=until;
                    }
                }
                return assigned;
            }
            if(now>=departureAt+3600)return new List<GuardRecord>();
            var preview=CopyValidated(this); preview.Advance(now);
            var eligible=new Dictionary<string,List<SavedRoutePoint>>();
            foreach(var pair in approaches)
            {
                var guard=preview.Guards.Find(g=>g.Id==pair.Key);
                if(guard==null || !guard.IsAlive || guard.Duty!=GuardDuty.Posted || guard.Interior!=null || guard.Refuge!=null)continue;
                var route=CopyRoute(pair.Value);
                if(route.Count<2)continue;
                var back=ResponseReturn(guard,route,preview.PatrolReturn(guard.Id,now));
                double reserve=(Length(back)+Length(patrol))*timeScale/speed;
                if(!Time(reserve) || now+Length(route)*timeScale/speed>departureAt+3600-reserve)continue;
                eligible.Add(pair.Key,route);
            }
            if(eligible.Count==0)return new List<GuardRecord>();
            var sent=DispatchOutdoorInvestigation(incidentId,now,eligible,patrol,1,speed,timeScale);
            if(sent.Count==0)return sent;
            var selected=sent[0];var report=Reports.Find(r=>r.Id==incidentId);
            report.DepartureInvestigation=true;report.DepartureAt=departureAt;
            report.DepartureReturnBudget=(Length(selected.ReturnPath)+Length(report.SearchRoute))*selected.ReturnRate;
            report.InvestigationUntil=departureAt+3600-report.DepartureReturnBudget;
            selected.SearchUntil=report.InvestigationUntil;
            return sent;
        }

        private List<SavedRoutePoint> DepartureSearchReturn(GuardRecord guard,double now)
        {
            var report=Reports.Find(r=>r.Id==guard.IncidentId);
            if(report==null || (!report.DepartureInvestigation && !report.HasCorpseInquiryWindow) || report.SearchRoute==null)return null;
            var path=report.SearchRoute;
            double phase=(Math.Max(now,guard.SearchPauseUntil)-guard.SearchDelaySeconds-guard.ArrivesAt)*report.SearchLaps/UnwitnessedScene.OccupationSeconds;
            double along=(phase-Math.Floor(phase))*Length(path);
            var result=new List<SavedRoutePoint>{SearchPosition(report,guard,now)};
            for(int i=1;i<path.Count;i++)
            {
                double segment=Distance(path[i-1],path[i]);
                if(along>segment){along-=segment;continue;}
                for(int j=i-1;j>=0;j--)if(Distance(result[result.Count-1],path[j])>0.000001)result.Add(CopyPoint(path[j]));
                break;
            }
            foreach(var point in guard.ReturnPath)
                if(Distance(result[result.Count-1],point)>0.000001)result.Add(CopyPoint(point));
            if(result.Count==1)result.Add(CopyPoint(result[0]));
            return result;
        }
    }
}
