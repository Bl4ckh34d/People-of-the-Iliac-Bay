using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    // Individual eyewitness evidence. A shared crime report never grants this
    // knowledge to other guards, and no interior target position is stored here.
    [Serializable]
    public sealed class GuardDoorPursuit
    {
        public string IncidentId, ThreatId, DoorId;
        public int BuildingKey;
        public SavedRoutePoint Entrance;
        public double ObservedAt, SearchUntil;
    }

    public sealed partial class GuardRoster
    {
        public bool PursueWitnessedDoor(string id,string threatId,int buildingKey,string doorId,
            double now,bool witnessed,List<SavedRoutePoint> route,List<SavedRoutePoint> home,double speed,double timeScale)
        {
            Require(Time(now) && Time(now+1800),"Invalid witnessed entry time.");
            var guard=Find(id);
            var report=Reports.Find(r=>r.Id==guard.IncidentId);
            if(!witnessed || !guard.IsAlive || guard.Interior!=null || guard.Refuge!=null || now>=guard.SearchUntil ||
                (guard.Duty!=GuardDuty.Engaged && guard.Duty!=GuardDuty.Responding && guard.Duty!=GuardDuty.Searching) ||
                !DoorTargetMatches(report,threatId))return false;
            Require(buildingKey>=0 && !string.IsNullOrWhiteSpace(doorId) && doorId.Length<=256 &&
                Finite(speed) && speed>0 && Finite(timeScale) && timeScale>0,"Invalid witnessed building door.");
            var path=CopyRoute(route);var back=CopyRoute(home);
            var position=CopyValidated(this).Position(id,now);
            Require(path.Count>=2 && back.Count>=2 && Distance(path[0],position)<0.05 &&
                Distance(path[path.Count-1],back[0])<0.05 && Distance(back[back.Count-1],guard.Post)<0.05,
                "Door pursuit must join the actual guard position, doorway and return post.");
            double rate=timeScale/speed,arrival=now+Length(path)*rate;
            if(!Finite(rate) || rate<=0 || !Time(arrival) || arrival>=now+1800 ||
                !Time(now+1800+Length(back)*rate))return false;
            guard.DoorPursuit=new GuardDoorPursuit {IncidentId=guard.IncidentId,ThreatId=threatId,
                BuildingKey=buildingKey,DoorId=doorId,Entrance=CopyPoint(path[path.Count-1]),ObservedAt=now,SearchUntil=now+1800};
            guard.Route=path;guard.ResponseOrigin=CopyPoint(path[0]);guard.ReturnPath=back;guard.ReturnRate=rate;
            guard.DepartedAt=now;guard.ArrivesAt=arrival;guard.SearchUntil=now+1800;
            guard.EngagementPosition=null;guard.LastSeenSuspect=null;guard.Duty=GuardDuty.Responding;
            return true;
        }

        public bool EnterWitnessedDoor(string id,int buildingKey,string doorId,double now,GuardPoint3 entrance)
        {
            Require(Time(now),"Invalid witnessed door arrival time.");
            Advance(now);var guard=Find(id);var evidence=guard.DoorPursuit;
            if(evidence==null || evidence.BuildingKey!=buildingKey || evidence.DoorId!=doorId ||
                evidence.IncidentId!=guard.IncidentId || now>=guard.SearchUntil ||
                !guard.IsAlive || guard.Duty!=GuardDuty.Searching || guard.Interior!=null)return false;
            EnterBuildingCore(id,buildingKey,now,entrance,true);
            return true;
        }

        // A real new assault belongs to this guard. Do not rewrite the shared
        // suspect report (or the other eyewitnesses' entry evidence).
        public void ReassignDoorAssault(string id,string reportId,double now)
        {
            var guard=Find(id);var evidence=guard.DoorPursuit;
            Require(evidence!=null && guard.IsAlive && Time(now) && Time(now+300) &&
                !string.IsNullOrWhiteSpace(reportId) && !Reports.Exists(r=>r.Id==reportId) &&
                (guard.Interior==null || !guard.Interior.Returning),"Invalid doorway assault handoff.");
            var entrance=CopyPoint(evidence.Entrance);
            if(guard.Duty!=GuardDuty.Engaged)Engage(id,now);
            Reports.Add(new GuardReport {Id=reportId,GuardIds=new List<string>{id},
                BuildingKey=guard.Interior==null ? (int?)null : guard.Interior.BuildingKey,
                Entrance=guard.Interior==null ? null : entrance});
            guard.IncidentId=reportId;guard.DoorPursuit=null;guard.SearchUntil=now+300;
            if(guard.Interior!=null){guard.Interior.LastSeenSuspect=null;guard.Interior.PursuitUntil=0;}
        }

        private static bool DoorTargetMatches(GuardReport report,string threat)
        {
            return !string.IsNullOrWhiteSpace(threat) && report!=null &&
                (report.ThreatId==threat || report.CorpseSuspect!=null &&
                report.CorpseSuspect.Kind==CorpseSuspectKind.HostileEntity && report.CorpseSuspect.Identity==threat);
        }

        private static GuardDoorPursuit CopyDoorPursuit(GuardRecord guard)
        {
            var source=guard.DoorPursuit;if(source==null)return null;
            Require(source.IncidentId==guard.IncidentId && !string.IsNullOrWhiteSpace(source.ThreatId) && source.ThreatId.Length<=128 &&
                source.BuildingKey>=0 && !string.IsNullOrWhiteSpace(source.DoorId) && source.DoorId.Length<=256 &&
                Time(source.ObservedAt) && Time(source.SearchUntil) && source.SearchUntil==source.ObservedAt+1800 &&
                guard.Duty!=GuardDuty.Posted && guard.Refuge==null,"Invalid witnessed door evidence.");
            return new GuardDoorPursuit {IncidentId=source.IncidentId,ThreatId=source.ThreatId,BuildingKey=source.BuildingKey,
                DoorId=source.DoorId,Entrance=CopyPoint(source.Entrance),ObservedAt=source.ObservedAt,SearchUntil=source.SearchUntil};
        }
    }
}
