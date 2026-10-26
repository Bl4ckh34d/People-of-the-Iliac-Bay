using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    [Serializable]
    public sealed class DefenseCorpseReporter
    {
        public string ObserverId;
        public OutdoorBodyReport Report;
    }
    [Serializable]
    public sealed class DefenseCorpseCleanup
    {
        public string FirstObserverId, ClearedBy;
        public bool GuardObserver, Cleared;
        public double SeenAt, ClearedAt;
        public List<DefenseCorpseReporter> Reports=new List<DefenseCorpseReporter>();
    }
    public sealed partial class WorldStateStore
    {
        private static bool CleanupOnlyCorpse(DefenseCombatant record)
        {
            return record.Death!=null && record.Death.BuildingKey==-1 && record.Death.CorpseId!=null &&
                record.Death.Lawful && !record.PlayerProvoked;
        }
        public List<DefenseCombatant> DefenseCorpsesFor(int settlementId)
        {
            return state.DefenseCombatants.FindAll(c=>c.SettlementId==settlementId && CleanupOnlyCorpse(c));
        }
        public OutdoorBodyReport NoticeDefenseCorpse(string id,string observerId,bool guard,double now)
        {
            var record=DefenseRecord(id);var death=record.Death;
            Require(CleanupOnlyCorpse(record) && DefenseTime(now) && now>=death.DiedAt &&
                DefenseObserver(state,record.SettlementId,observerId,guard,true),"Invalid aggressor corpse discovery.");
            if(death.Cleanup==null)death.Cleanup=new DefenseCorpseCleanup {FirstObserverId=observerId,GuardObserver=guard,SeenAt=now};
            if(death.Cleanup.Cleared || guard)return null;
            var existing=death.Cleanup.Reports.Find(r=>r.ObserverId==observerId);
            if(existing==null)
            {
                existing=new DefenseCorpseReporter {ObserverId=observerId,Report=new OutdoorBodyReport {SeenAt=now}};
                death.Cleanup.Reports.Add(existing);
            }
            return existing.Report;
        }
        // Native removal is attempted only after this admission succeeds at the measured guard position.
        public bool CanClearDefenseCorpse(string id,string guardId,double x,double y,double z,double now)
        {
            var record=DefenseRecord(id);var death=record.Death;
            Require(DefenseTime(now) && DefenseCoordinate(x) && DefenseCoordinate(y) && DefenseCoordinate(z),"Invalid cleanup observation.");
            if(!CleanupOnlyCorpse(record)||now<death.DiedAt||death.Cleanup==null||death.Cleanup.Cleared)return false;
            var roster=state.Garrisons.Find(r=>r.SettlementId==record.SettlementId);
            var guard=roster==null?null:roster.Guards.Find(g=>g.Id==guardId&&g.IsAlive&&g.Interior==null&&g.Refuge==null);
            if(guard==null || now<guard.ArrivesAt || !roster.Reports.Exists(r=>r.Id==guard.IncidentId &&
                r.CleanupCorpseId==death.CorpseId && r.ThreatId==null && r.GuardIds.Contains(guardId)))return false;
            double dx=x-death.X,dy=y-death.Y,dz=z-death.Z;
            return dx*dx+dy*dy+dz*dz<=9;
        }
        public bool TryClearDefenseCorpse(string id,string guardId,double x,double y,double z,double now)
        {
            if(!CanClearDefenseCorpse(id,guardId,x,y,z,now))return false;
            var cleanup=DefenseRecord(id).Death.Cleanup;
            cleanup.Cleared=true;cleanup.ClearedAt=now;cleanup.ClearedBy=guardId;return true;
        }
        public bool TryExecuteDefenseCorpseCleanup(string id,string guardId,double x,double y,double z,double now,Func<string,bool> removeExactCorpse)
        {
            Require(removeExactCorpse!=null,"Missing native corpse removal adapter.");
            if(!CanClearDefenseCorpse(id,guardId,x,y,z,now))return false;
            if(!removeExactCorpse(DefenseRecord(id).Death.CorpseId))return false;
            return TryClearDefenseCorpse(id,guardId,x,y,z,now);
        }
        private static DefenseCorpseCleanup CopyDefenseCleanup(DefenseCombatant record,WorldState world)
        {
            var value=record.Death.Cleanup;if(value==null)return null;
            Require(CleanupOnlyCorpse(record) && DefenseTime(value.SeenAt) && value.SeenAt>=record.Death.DiedAt &&
                DefenseObserver(world,record.SettlementId,value.FirstObserverId,value.GuardObserver,false) && value.Reports!=null,
                "Invalid corpse cleanup discovery.");
            Require(value.Cleared ? DefenseTime(value.ClearedAt) && value.ClearedAt>=value.SeenAt &&
                DefenseObserver(world,record.SettlementId,value.ClearedBy,true,false) &&
                world.Garrisons.Exists(g=>g.SettlementId==record.SettlementId && g.Reports.Exists(r=>r.CleanupCorpseId==record.Death.CorpseId &&
                    r.ThreatId==null && r.GuardIds.Contains(value.ClearedBy))) : value.ClearedBy==null && value.ClearedAt==0,
                "Corpse cleanup lacks a guard response.");
            var copy=new DefenseCorpseCleanup {FirstObserverId=value.FirstObserverId,GuardObserver=value.GuardObserver,SeenAt=value.SeenAt,
                Cleared=value.Cleared,ClearedAt=value.ClearedAt,ClearedBy=value.ClearedBy};
            var ids=new HashSet<string>(StringComparer.Ordinal);
            foreach(var reporter in value.Reports)
            {
                Require(reporter!=null && ids.Add(reporter.ObserverId) &&
                    DefenseObserver(world,record.SettlementId,reporter.ObserverId,false,false) && reporter.Report!=null &&
                    reporter.Report.SeenAt>=record.Death.DiedAt,"Invalid corpse reporter.");
                copy.Reports.Add(new DefenseCorpseReporter {ObserverId=reporter.ObserverId,Report=OutdoorBodyReport.Copy(reporter.Report)});
            }
            return copy;
        }
    }
    public sealed partial class GuardRoster
    {
        public void RedirectMurdererCorpseCleanup(string guardId,string suspectId,string reportId,string corpseId,
            double now,List<SavedRoutePoint> route,List<SavedRoutePoint> home,double speed,double timeScale)
        {
            var guard=Find(guardId);var source=Reports.Find(r=>r.Id==guard.IncidentId);
            var path=CopyRoute(route);var back=CopyRoute(home);
            Require(guard.IsAlive && guard.Interior==null && guard.Refuge==null &&
                (guard.Duty==GuardDuty.Engaged || guard.Duty==GuardDuty.Searching) && source!=null &&
                source.CorpseSuspect!=null && source.CorpseSuspect.Kind==CorpseSuspectKind.HostileEntity &&
                source.CorpseSuspect.Identity==suspectId,"Cleanup handoff requires this investigator's exact NPC suspect.");
            Require(!string.IsNullOrWhiteSpace(reportId) && !Reports.Exists(r=>r.Id==reportId) &&
                !string.IsNullOrWhiteSpace(corpseId) && corpseId.Length<=512 && Time(now) &&
                Finite(speed) && speed>0 && Finite(timeScale) && timeScale>0,"Invalid cleanup handoff.");
            var position=CopyValidated(this).Position(guardId,now);
            Require(path.Count>=2 && back.Count>=2 && Distance(position,path[0])<0.05 &&
                Distance(path[path.Count-1],back[0])<0.05 && Distance(back[back.Count-1],guard.Post)<0.05,
                "Cleanup handoff must preserve actual position and return post.");
            double rate=timeScale/speed,arrival=now+Length(path)*rate;
            Require(Time(arrival+3600+Length(back)*rate),"Cleanup handoff time overflow.");
            Reports.Add(new GuardReport {Id=reportId,CleanupCorpseId=corpseId,GuardIds=new List<string>{guardId}});
            guard.IncidentId=reportId;guard.DoorPursuit=null;guard.Route=path;guard.ResponseOrigin=CopyPoint(path[0]);
            guard.ReturnPath=back;guard.ReturnRate=rate;guard.DepartedAt=now;guard.ArrivesAt=arrival;
            guard.SearchUntil=arrival+300;guard.EngagementPosition=null;guard.LastSeenSuspect=null;guard.Duty=GuardDuty.Responding;
        }
        public List<GuardRecord> DispatchCorpseCleanup(string reportId,string corpseId,double now,
            IDictionary<string,List<SavedRoutePoint>> approaches,int maximumResponders,double speed,double timeScale)
        {
            Require(!string.IsNullOrWhiteSpace(corpseId)&&corpseId.Length<=512,"Invalid cleanup corpse identity.");
            var previous=Reports.Find(r=>r.Id==reportId);
            Require(previous==null || previous.CleanupCorpseId==corpseId && previous.ThreatId==null && !previous.OccupiesScene,
                "Cleanup response cannot become an investigation.");
            var sent=Dispatch(reportId,now,approaches,maximumResponders,speed,timeScale);
            Reports.Find(r=>r.Id==reportId).CleanupCorpseId=corpseId;
            return sent;
        }
    }
}
