using System;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        // Evidence lookup only. No target position, search, or custody state.
        public bool GuardKnowsHostileMurder(int settlementId,string guardId,string threatId)
        {
            if(settlementId<0 || string.IsNullOrWhiteSpace(guardId) || string.IsNullOrWhiteSpace(threatId))return false;
            var roster=state.Garrisons.Find(row=>row.SettlementId==settlementId);
            var guard=roster==null?null:roster.Guards.Find(row=>row.Id==guardId);
            if(guard==null || !guard.IsAlive)return false;
            foreach(var report in roster.Reports)
            {
                var known=report.CorpseSuspect;
                if(known==null || known.Kind!=CorpseSuspectKind.HostileEntity || known.Identity!=threatId ||
                    known.LearnedByGuardId!=guardId || !report.GuardIds.Contains(guardId))continue;
                var alarm=state.AlarmIncidents.Find(row=>row.SettlementId==settlementId &&
                    report.Id=="outdoor:"+row.Id);
                if(alarm!=null && !alarm.CrimeResolved && !alarm.PlayerAttributed && alarm.KillerThreatId==threatId)return true;
            }
            return false;
        }
    }
}
