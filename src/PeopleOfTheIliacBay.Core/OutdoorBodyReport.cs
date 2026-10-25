using System;
namespace PeopleOfTheIliacBay.Core
{
    [Serializable]
    public sealed class OutdoorBodyReport
    {
        public double SeenAt;
        public double DeliveredAt=-1;
        public bool HasPosition, Deferred, Delivered, Returning, Complete;
        public double X,Z;
        public string GuardId;
        public static OutdoorBodyReport Copy(OutdoorBodyReport value)
        {
            if(value==null)return null;
            if(double.IsNaN(value.SeenAt)||double.IsInfinity(value.SeenAt)||value.SeenAt<0 ||
                double.IsNaN(value.X)||double.IsInfinity(value.X)||double.IsNaN(value.Z)||double.IsInfinity(value.Z)||
                value.GuardId!=null && (string.IsNullOrWhiteSpace(value.GuardId)||value.GuardId.Length>128) ||
                value.DeliveredAt!=-1 && (double.IsNaN(value.DeliveredAt)||double.IsInfinity(value.DeliveredAt)||
                    value.DeliveredAt<value.SeenAt || !value.Delivered || value.GuardId==null))
                throw new ArgumentException("Invalid outdoor body report.");
            return (OutdoorBodyReport)value.MemberwiseClone();
        }
    }
    public sealed partial class WorldStateStore
    {
        public bool TryDeliverOutdoorMurderStatement(string incidentId,string witnessId,string guardId,
            double distance,bool visible,double now)
        {
            var alarm=FindIncident(incidentId);
            if(alarm.Corpse==null || alarm.CrimeResolved || !ValidTime(now) || now<alarm.Corpse.DiedAt ||
                !ValidTime(distance) || distance>4 || !visible)return false;
            var witness=alarm.People.Find(p=>p.ResidentId==witnessId && p.DirectWitness);
            var delivery=witness==null?null:witness.OutdoorReport;
            var roster=state.Garrisons.Find(r=>r.SettlementId==alarm.SettlementId);
            var guard=roster==null?null:roster.Guards.Find(g=>g.Id==guardId && g.IsAlive && g.Interior==null);
            var report=roster==null?null:roster.Reports.Find(r=>r.Id=="outdoor:"+alarm.Id && r.Investigation);
            if(delivery==null || now<delivery.SeenAt || guard==null || report==null ||
                guard.IncidentId!=report.Id || !report.GuardIds.Contains(guardId) ||
                (guard.Duty!=GuardDuty.Responding && guard.Duty!=GuardDuty.Searching))return false;
            if(delivery.DeliveredAt>=0)return delivery.GuardId==guardId && now>=delivery.DeliveredAt;
            delivery.Delivered=true;delivery.Returning=true;delivery.GuardId=guardId;delivery.DeliveredAt=now;
            witness.StatementDelivered=true;return true;
        }

        // Coordinates must come from the physical guard actor, not an expected route endpoint.
        public bool ClearOutdoorCorpse(string incidentId,string guardId,double x,double z,double now)
        {
            var alarm=FindIncident(incidentId); var body=RequireOutdoorBody(alarm);
            Require(ValidTime(now)&&now>=body.DiedAt&&ValidCoordinate(x)&&ValidCoordinate(z),"Invalid exterior cleanup.");
            var roster=state.Garrisons.Find(g=>g.SettlementId==alarm.SettlementId);
            var guard=roster==null?null:roster.Guards.Find(g=>g.Id==guardId);
            if(body.Cleared||guard==null||!guard.IsAlive||guard.Interior!=null)return false;
            // The thirty-minute rule governs unattended absence cleanup. It must
            // not veto a living guard whose physical position is at the body.
            double dx=x-(body.HasPosition?body.X:alarm.X),dz=z-(body.HasPosition?body.Z:alarm.Z);
            if(dx*dx+dz*dz>9)return false;
            NoticeOutdoorCorpse(incidentId,guardId,true,now);
            body.Cleared=true;body.ClearedAt=now;
            SynchronizeOutdoorCorpseInquiry(alarm.Id);
            ReleaseCleanedLodging(alarm);return true;
        }
    }
}
