using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    [Serializable]
    public sealed class CorpseInquiryReceipt
    {
        public string PersonId,GuardId;
        public double At;
        public bool StaticPerson,ReceivedWitnessStatement;
        public int WindowVersion;
    }
    public sealed partial class WorldStateStore
    {
        public bool TryQuestionCorpseCase(string incidentId,string guardId,string personId,bool staticPerson,
            double distance,bool lineOfSight,double now,out bool receivedStatement)
        {
            receivedStatement=false;
            var alarm=FindIncident(incidentId);var body=alarm.Corpse;
            if(body==null || !body.Cleared || !ValidTime(now) || now<body.ClearedAt ||
                !ValidTime(distance) || distance>4 || !lineOfSight || alarm.CrimeResolved ||
                personId==alarm.VictimId || body.Inquiries.Count>=32 || body.Inquiries.Exists(r=>r.PersonId==personId))return false;
            var roster=state.Garrisons.Find(r=>r.SettlementId==alarm.SettlementId);
            var guard=roster==null?null:roster.Guards.Find(g=>g.Id==guardId && g.IsAlive && g.Interior==null);
            SynchronizeOutdoorCorpseInquiry(alarm.Id);
            var report=roster==null?null:roster.Reports.Find(r=>r.Id=="outdoor:"+alarm.Id && r.Investigation &&
                !r.BuildingKey.HasValue && r.HasCorpseInquiryWindow && r.GuardIds.Contains(guardId));
            if(guard==null || report==null || guard.IncidentId!=report.Id || guard.Duty!=GuardDuty.Searching ||
                now<guard.ArrivesAt || now>=report.InvestigationUntil)return false;
            Resident person;
            try{person=AlarmResident(alarm.SettlementId,personId);}catch(ArgumentException){return false;}
            if(!person.IsAlive || staticPerson && !state.NativePeople.Exists(p=>p.Id==personId && p.SettlementId==alarm.SettlementId))return false;
            var witness=alarm.People.Find(p=>p.ResidentId==personId && p.DirectWitness);
            receivedStatement=witness!=null;
            var receipt=new CorpseInquiryReceipt {PersonId=personId,GuardId=guardId,At=now,
                StaticPerson=staticPerson,ReceivedWitnessStatement=receivedStatement,WindowVersion=1};
            body.Inquiries.Add(receipt);
            if(witness!=null)witness.StatementDelivered=true;
            return true;
        }

        public bool SynchronizeOutdoorCorpseInquiry(string incidentId)
        {
            var alarm=FindIncident(incidentId);var body=alarm.Corpse;
            if(body==null || !body.Cleared || (body.HasPosition?body.BuildingKey:alarm.BuildingKey)!=-1 ||
                alarm.Scene!=null)return false;
            var roster=state.Garrisons.Find(row=>row.SettlementId==alarm.SettlementId);
            return roster!=null && roster.BeginCorpseInquiry("outdoor:"+alarm.Id,body.ClearedAt);
        }

        private static void ValidateOutdoorCorpseInquiryWindows(WorldState world)
        {
            foreach(var roster in world.Garrisons)
                foreach(var report in roster.Reports)
                {
                    if(!report.HasCorpseInquiryWindow && report.CorpseSearchVersion==0)continue;
                    var alarm=world.AlarmIncidents.Find(row=>row.SettlementId==roster.SettlementId &&
                        "outdoor:"+row.Id==report.Id);
                    Require(alarm!=null && alarm.Corpse!=null &&
                        (alarm.Corpse.HasPosition?alarm.Corpse.BuildingKey:alarm.BuildingKey)==-1 && alarm.Scene==null &&
                        (!report.HasCorpseInquiryWindow || alarm.Corpse.Cleared &&
                            report.CorpseInquiryStartedAt==alarm.Corpse.ClearedAt),
                        "Corpse inquiry window lacks its exact cleared corpse.");
                }
        }

        private static List<CorpseInquiryReceipt> CopyCorpseInquiries(CivilianIncident alarm,WorldState source)
        {
            var rows=alarm.Corpse.Inquiries;
            Require(rows!=null && rows.Count<=32,"Invalid corpse inquiry ledger.");
            var copy=new List<CorpseInquiryReceipt>();var people=new HashSet<string>(StringComparer.Ordinal);
            foreach(var row in rows)
            {
                Require(row!=null && alarm.Corpse.Cleared && row.At>=alarm.Corpse.ClearedAt &&
                    row.PersonId!=alarm.VictimId && people.Add(row.PersonId) &&
                    ValidTime(row.At) && (row.WindowVersion==0 || row.WindowVersion==1),
                    "Invalid corpse inquiry chronology.");
                var roster=source.Garrisons.Find(r=>r.SettlementId==alarm.SettlementId);
                var report=roster==null?null:roster.Reports.Find(r=>r.Id=="outdoor:"+alarm.Id && r.Investigation &&
                    !r.BuildingKey.HasValue && r.GuardIds.Contains(row.GuardId));
                double deadline=report==null?0:report.InvestigationUntil;
                if(report!=null && row.WindowVersion==0 && report.HasCorpseInquiryWindow)
                {
                    double first=double.MaxValue;
                    foreach(var arrival in report.Arrivals)first=Math.Min(first,arrival.ScheduledAt);
                    deadline=report.DepartureInvestigation?report.DepartureAt+3600-report.DepartureReturnBudget:
                        first+UnwitnessedScene.OccupationSeconds;
                }
                Require(report!=null && (row.WindowVersion==0 ? alarm.Corpse.Departure!=null &&
                    row.At>=alarm.Corpse.Departure.FirstDepartureAt+1800:
                    report.HasCorpseInquiryWindow && report.CorpseInquiryStartedAt==alarm.Corpse.ClearedAt) &&
                    row.At<deadline &&
                    roster.Guards.Exists(g=>g.Id==row.GuardId && (g.IsAlive || g.DiedAt>=row.At)),"Inquiry lacks its assigned guard.");
                var native=source.NativePeople.Find(p=>p.Id==row.PersonId && p.SettlementId==alarm.SettlementId);
                bool resident=source.Settlements.Exists(t=>t.SettlementId==alarm.SettlementId &&
                    t.Households.Exists(h=>h.Residents.Exists(p=>p.Id==row.PersonId)));
                resident=resident || source.HouseholdHistory.Exists(h=>h.SettlementId==alarm.SettlementId &&
                    h.Household.Residents.Exists(p=>p.Id==row.PersonId));
                bool traveler=source.Travelers.Exists(p=>p.Id==row.PersonId);
                Require((row.StaticPerson?native!=null:native!=null || resident || traveler) &&
                    (!row.ReceivedWitnessStatement || alarm.People.Exists(p=>p.ResidentId==row.PersonId && p.DirectWitness && p.StatementDelivered)),
                    "Inquiry invented a person or a witness statement.");
                copy.Add(new CorpseInquiryReceipt {PersonId=row.PersonId,GuardId=row.GuardId,At=row.At,
                    StaticPerson=row.StaticPerson,ReceivedWitnessStatement=row.ReceivedWitnessStatement,WindowVersion=row.WindowVersion});
            }
            return copy;
        }
    }
}
