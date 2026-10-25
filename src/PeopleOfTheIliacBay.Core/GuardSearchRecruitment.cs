using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class GuardRoster
    {
        public const int MaximumCorpseSearchGuards=6;
        public const double CorpseSearchContactRadius=35d;

        public bool CanRecruitCorpseSearchGuard(string callerId,string candidateId,double now,double distance,bool visible)
        {
            if(!Time(now) || !Finite(distance) || distance<0 || distance>CorpseSearchContactRadius || !visible ||
                callerId==candidateId)return false;
            var caller=Guards.Find(row=>row.Id==callerId);var candidate=Guards.Find(row=>row.Id==candidateId);
            var report=caller==null?null:Reports.Find(row=>row.Id==caller.IncidentId);
            return caller!=null && candidate!=null && caller.IsAlive && candidate.IsAlive &&
                caller.Interior==null && caller.Refuge==null && candidate.Interior==null && candidate.Refuge==null &&
                (caller.Duty==GuardDuty.Responding || caller.Duty==GuardDuty.Searching || caller.Duty==GuardDuty.Engaged) &&
                caller.SearchUntil>now && candidate.Duty==GuardDuty.Posted && report!=null &&
                report.Investigation && !report.BuildingKey.HasValue && report.ThreatId==null &&
                report.CorpseSearchVersion==1 && report.HasCorpseInquiryWindow && report.InvestigationUntil>now &&
                report.GuardIds.Contains(callerId) && !report.GuardIds.Contains(candidateId) &&
                report.GuardIds.Count<MaximumCorpseSearchGuards;
        }

        public bool TryRecruitCorpseSearchGuard(string callerId,string candidateId,double now,double distance,bool visible,
            List<SavedRoutePoint> joinRoute,List<SavedRoutePoint> entryRoute,double speed,double timeScale,
            out GuardRecord recruited)
        {
            recruited=null;
            if(!Time(now) || !Finite(speed) || speed<=0 || !Finite(timeScale) || timeScale<=0 ||
                joinRoute==null || entryRoute==null)return false;
            GuardRoster preview;List<SavedRoutePoint> path,back;double arrival,rate=timeScale/speed,reserve;
            try
            {
                preview=CopyValidated(this);preview.Advance(now);
                if(!preview.CanRecruitCorpseSearchGuard(callerId,candidateId,now,distance,visible))return false;
                var caller=preview.Find(callerId);var candidate=preview.Find(candidateId);
                var report=preview.Reports.Find(row=>row.Id==caller.IncidentId);
                var from=preview.Position(candidateId,now);var contact=preview.Position(callerId,now);
                path=CopyRoute(joinRoute);var entry=CopyRoute(entryRoute);
                if(path.Count<2 || entry.Count<2 || Distance(from,contact)>CorpseSearchContactRadius+0.05 ||
                    Distance(path[0],from)>=0.05 || Distance(path[path.Count-1],contact)>=0.05 ||
                    Distance(entry[0],contact)>=0.05 || Distance(entry[entry.Count-1],report.Entrance)>=0.05)return false;
                foreach(var point in entry)if(Distance(path[path.Count-1],point)>0.000001)path.Add(point);
                if(path.Count>10000)return false;
                arrival=now+Length(path)*rate;
                if(!Time(arrival) || arrival>report.InvestigationUntil)return false;
                back=ResponseReturn(candidate,path,preview.PatrolReturn(candidateId,now));
                if(back.Count<2 || back.Count>10000 || Distance(back[back.Count-1],candidate.Post)>=0.05 ||
                    !Time(arrival+3600+Length(back)*rate))return false;
                reserve=(Length(back)+Length(report.SearchRoute))*rate;
                double firstArrival=double.MaxValue;
                foreach(var evidence in report.Arrivals)firstArrival=Math.Min(firstArrival,evidence.ScheduledAt);
                double horizon=report.DepartureInvestigation?report.DepartureAt+3600:
                    firstArrival+UnwitnessedScene.OccupationSeconds;
                double budget=horizon-report.InvestigationUntil;
                if(!Time(reserve) || reserve>budget)return false;
            }
            catch(ArgumentException){return false;}
            // Publish only after every contact, route, timing and return check.
            Advance(now);
            var source=Find(callerId);var target=Find(candidateId);
            var active=Reports.Find(row=>row.Id==source.IncidentId);
            target.Route=path;target.ResponseOrigin=CopyPoint(path[0]);target.ReturnPath=back;target.ReturnRate=rate;
            target.DepartedAt=now;target.ArrivesAt=arrival;target.SearchUntil=active.InvestigationUntil;
            target.IncidentId=active.Id;target.DayRestUntil=0;target.EngagementPosition=null;target.LastSeenSuspect=null;
            target.Duty=GuardDuty.Responding;InterruptPatrol(target.Id,now);
            active.GuardIds.Add(target.Id);active.Arrivals.Add(new GuardArrivalEvidence{GuardId=target.Id,ScheduledAt=arrival});
            active.CorpseSearchVersion=1;
            if(active.DepartureInvestigation)active.DepartureReturnBudget=Math.Max(active.DepartureReturnBudget,reserve);
            recruited=target;return true;
        }
    }
}
