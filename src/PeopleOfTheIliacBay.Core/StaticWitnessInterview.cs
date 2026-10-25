using System;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        private bool LiveStaticInterview(CivilianIncident alarm,AlarmPerson person,string guardId)
        {
            var roster=state.Garrisons.Find(r=>r.SettlementId==alarm.SettlementId);
            var guard=roster==null?null:roster.Guards.Find(g=>g.Id==guardId && g.IsAlive &&
                g.Duty!=GuardDuty.Posted && g.Duty!=GuardDuty.Returning);
            return person.StaticWitness && person.DirectWitness && person.HasPosition && !person.StatementDelivered &&
                state.NativePeople.Exists(p=>p.Id==person.ResidentId&&p.IsAlive) && guard!=null &&
                guard.IncidentId!=null && guard.IncidentId.StartsWith("witness:"+alarm.Id+":"+person.ResidentId+":",StringComparison.Ordinal);
        }
        public bool TryAssignStaticInterview(string incidentId,string witnessId,string guardId,double now)
        {
            var alarm=FindIncident(incidentId);Require(ValidTime(now)&&now>=alarm.StartedAt,"Invalid interview time.");
            if(alarm.Corpse==null || alarm.CrimeResolved || alarm.People.Exists(p=>p.DirectWitness&&p.StatementDelivered))return false;
            if(alarm.People.Exists(p=>p.InterviewGuardId!=null&&LiveStaticInterview(alarm,p,p.InterviewGuardId)))return false;
            var witness=alarm.People.Find(p=>p.ResidentId==witnessId);
            if(witness==null||!LiveStaticInterview(alarm,witness,guardId))return false;
            witness.InterviewGuardId=guardId;return true;
        }
        public bool TryDeliverStaticInterview(string incidentId,string witnessId,string guardId,double x,double y,double z,double now)
        {
            var alarm=FindIncident(incidentId);Require(ValidTime(now)&&now>=alarm.StartedAt&&
                ValidCoordinate(x)&&ValidCoordinate(y)&&ValidCoordinate(z),"Invalid interview contact.");
            var witness=alarm.People.Find(p=>p.ResidentId==witnessId&&p.InterviewGuardId==guardId);
            if(witness==null || !LiveStaticInterview(alarm,witness,guardId) ||
                alarm.People.Exists(p=>p.DirectWitness&&p.StatementDelivered))return false;
            double dx=x-witness.X,dy=y-witness.Y,dz=z-witness.Z;
            if(!witness.HasPosition||dx*dx+dy*dy+dz*dz>16)return false;
            witness.StatementDelivered=true;return true;
        }
    }
}
