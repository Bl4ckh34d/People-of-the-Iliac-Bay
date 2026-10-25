using System;

namespace PeopleOfTheIliacBay.Core
{
    public enum CorpseSuspectKind { Player, HostileEntity }
    [Serializable]
    public sealed class CorpseSuspectKnowledge
    {
        public CorpseSuspectKind Kind;
        public string Identity,WitnessId,LearnedByGuardId;
        public double LearnedAt;
    }
    [Serializable]
    public sealed class CorpseSuspectSighting
    {
        public string GuardId;
        public double At,X,Z;
    }
    public sealed partial class WorldStateStore
    {
        public bool TryLearnCorpseSuspect(string incidentId,string guardId,string witnessId,double now)
        {
            var alarm=FindIncident(incidentId);var body=alarm.Corpse;
            var roster=state.Garrisons.Find(r=>r.SettlementId==alarm.SettlementId);
            var report=roster==null?null:roster.Reports.Find(r=>r.Id=="outdoor:"+alarm.Id);
            var guard=roster==null?null:roster.Guards.Find(g=>g.Id==guardId);
            if(body==null || !body.Cleared || alarm.CrimeResolved || report==null || !report.Investigation ||
                !report.HasCorpseInquiryWindow || !ValidTime(now) || now>=report.InvestigationUntil ||
                guard==null || !guard.IsAlive || guard.Interior!=null || guard.IncidentId!=report.Id ||
                guard.Duty!=GuardDuty.Searching || !report.GuardIds.Contains(guardId))return false;
            var receipt=body.Inquiries.Find(r=>r.GuardId==guardId && r.PersonId==witnessId &&
                r.WindowVersion==1 && r.ReceivedWitnessStatement && r.At<=now);
            var witness=alarm.People.Find(p=>p.ResidentId==witnessId && p.DirectWitness && p.StatementDelivered);
            if(witness==null || receipt==null && !DeliveredMurderStatement(witness,guardId,now))return false;
            if(!alarm.PlayerAttributed && alarm.KillerThreatId==null)return false;
            var kind=alarm.PlayerAttributed?CorpseSuspectKind.Player:CorpseSuspectKind.HostileEntity;
            string identity=alarm.PlayerAttributed?"player":alarm.KillerThreatId;
            if(report.CorpseSuspect!=null)return report.CorpseSuspect.Kind==kind && report.CorpseSuspect.Identity==identity;
            report.CorpseSuspect=new CorpseSuspectKnowledge{Kind=kind,Identity=identity,WitnessId=witnessId,
                LearnedByGuardId=guardId,LearnedAt=receipt==null?now:receipt.At};return true;
        }

        private static bool DeliveredMurderStatement(AlarmPerson witness,string guardId,double learnedAt)
        {
            var delivery=witness==null?null:witness.OutdoorReport;
            return witness!=null && witness.DirectWitness && witness.StatementDelivered && delivery!=null &&
                delivery.Delivered && delivery.GuardId==guardId && delivery.DeliveredAt>=delivery.SeenAt &&
                delivery.DeliveredAt>=0 && delivery.DeliveredAt<=learnedAt;
        }

        // The native caller supplies an actual sight/contact observation. Identity
        // knowledge alone never supplies coordinates, engagement or a crime charge.
        public bool TryObserveCorpseSuspect(string incidentId,string guardId,CorpseSuspectKind kind,string identity,
            double x,double z,double distance,bool visible,double now)
        {
            var alarm=FindIncident(incidentId);var roster=state.Garrisons.Find(r=>r.SettlementId==alarm.SettlementId);
            var report=roster==null?null:roster.Reports.Find(r=>r.Id=="outdoor:"+alarm.Id);
            var guard=roster==null?null:roster.Guards.Find(g=>g.Id==guardId);
            if(report==null || report.CorpseSuspect==null || report.CorpseSuspect.Kind!=kind ||
                report.CorpseSuspect.Identity!=identity || alarm.CrimeResolved || !ValidTime(now) ||
                now<report.CorpseSuspect.LearnedAt || now>=report.InvestigationUntil ||
                !ValidCoordinate(x) || !ValidCoordinate(z) || !ValidTime(distance) || distance>20 || !visible ||
                guard==null || !guard.IsAlive || guard.Interior!=null || guard.IncidentId!=report.Id ||
                !report.GuardIds.Contains(guardId) ||
                (guard.Duty!=GuardDuty.Searching && guard.Duty!=GuardDuty.Responding && guard.Duty!=GuardDuty.Engaged))return false;
            if(report.SuspectSighting!=null && now<report.SuspectSighting.At)return false;
            report.SuspectSighting=new CorpseSuspectSighting{GuardId=guardId,At=now,X=x,Z=z};return true;
        }

        private static void ValidateCorpseSuspectEvidence(WorldState world)
        {
            foreach(var roster in world.Garrisons)foreach(var report in roster.Reports)
            {
                if(report.CorpseSuspect==null){Require(report.SuspectSighting==null,"Sighting lacks known suspect.");continue;}
                var known=report.CorpseSuspect;
                var alarm=world.AlarmIncidents.Find(a=>a.SettlementId==roster.SettlementId && "outdoor:"+a.Id==report.Id);
                Require(alarm!=null && alarm.Corpse!=null && alarm.Corpse.Cleared && report.HasCorpseInquiryWindow &&
                    (known.Kind==CorpseSuspectKind.Player?alarm.PlayerAttributed && known.Identity=="player":
                        known.Kind==CorpseSuspectKind.HostileEntity && !alarm.PlayerAttributed && alarm.KillerThreatId==known.Identity) &&
                    (alarm.Corpse.Inquiries.Exists(r=>r.WindowVersion==1 && r.ReceivedWitnessStatement &&
                        r.PersonId==known.WitnessId && r.GuardId==known.LearnedByGuardId && r.At==known.LearnedAt) ||
                    alarm.People.Exists(p=>p.ResidentId==known.WitnessId && DeliveredMurderStatement(p,known.LearnedByGuardId,known.LearnedAt))),
                    "Suspect knowledge lacks exact crime and witness receipt.");
                if(report.SuspectSighting==null)continue;
                var sight=report.SuspectSighting;var observer=roster.Guards.Find(g=>g.Id==sight.GuardId);
                Require(observer!=null && (observer.IsAlive || observer.DiedAt>=sight.At) &&
                    report.GuardIds.Contains(sight.GuardId) && sight.At>=known.LearnedAt && sight.At<report.InvestigationUntil,
                    "Suspect sighting lacks a living assigned observer and valid chronology.");
            }
        }
    }
    public sealed partial class GuardRoster
    {
        private static CorpseSuspectKnowledge CopyCorpseSuspect(GuardReport report)
        {
            var known=report.CorpseSuspect;
            Require(known==null || report.HasCorpseInquiryWindow && report.Investigation && !report.BuildingKey.HasValue &&
                Enum.IsDefined(typeof(CorpseSuspectKind),known.Kind) && !string.IsNullOrWhiteSpace(known.Identity) &&
                known.Identity.Length<=256 && !string.IsNullOrWhiteSpace(known.WitnessId) &&
                report.GuardIds.Contains(known.LearnedByGuardId) && Time(known.LearnedAt) &&
                known.LearnedAt>=report.CorpseInquiryStartedAt && known.LearnedAt<report.InvestigationUntil,
                "Invalid corpse suspect identity.");
            return known==null?null:new CorpseSuspectKnowledge{Kind=known.Kind,Identity=known.Identity,WitnessId=known.WitnessId,
                LearnedByGuardId=known.LearnedByGuardId,LearnedAt=known.LearnedAt};
        }
        private static CorpseSuspectSighting CopyCorpseSighting(GuardReport report)
        {
            var sight=report.SuspectSighting;
            Require(sight==null || report.CorpseSuspect!=null && report.GuardIds.Contains(sight.GuardId) &&
                Time(sight.At) && Finite(sight.X) && Finite(sight.Z),"Invalid corpse suspect sighting.");
            return sight==null?null:new CorpseSuspectSighting{GuardId=sight.GuardId,At=sight.At,X=sight.X,Z=sight.Z};
        }
    }
}
