using System;
namespace PeopleOfTheIliacBay.Core
{
    public enum KnownDeathEvidence { None, Witness, Discovery, Notification }
    public sealed class Bereavement
    {
        public string ResidentId,Name,Relationship;
        public double DiedAt,KnownAt,MentionChance;
        public bool WitnessedPlayerKilling;
    }
    public sealed partial class WorldStateStore
    {
        public bool KnowsResidentDeath(string observer,string subject)
        {
            if(string.IsNullOrWhiteSpace(observer) || string.IsNullOrWhiteSpace(subject) || observer==subject)return false;
            var person=ResolveResident(observer);var lost=ResolveResident(subject);
            return person!=null && person.IsAlive && lost!=null && !lost.IsAlive &&
                state.AlarmIncidents.Exists(a=>a.VictimId==subject && a.Corpse!=null &&
                    a.People.Exists(p=>p.ResidentId==observer && p.DeathKnowledge!=KnownDeathEvidence.None));
        }

        private static string BereavementRelationship(Resident person,Resident lost)
        {
            if(person==null || lost==null || person.Id==lost.Id)return null;
            if(person.PartnerId==lost.Id)return "partner";
            if(person.MotherId==lost.Id)return "mother";
            if(person.FatherId==lost.Id)return "father";
            if(lost.MotherId==person.Id || lost.FatherId==person.Id)return "child";
            return null;
        }

        // Only native witness, discovery and successful warning paths call this.
        // The incident receipt persists even if bounded recollection later expires.
        private void RememberKnownDeath(CivilianIncident incident,string observer,double now,KnownDeathEvidence evidence)
        {
            var person=ResolveResident(observer);var lost=ResolveResident(incident.VictimId);
            var receipt=incident.People.Find(p=>p.ResidentId==observer);
            if(receipt==null || person==null || !person.IsAlive || lost==null || lost.IsAlive ||
                incident.Corpse==null || now<incident.Corpse.DiedAt || observer==lost.Id)return;
            if(receipt.DeathKnowledge==KnownDeathEvidence.None)
            {receipt.DeathKnowledge=evidence;receipt.DeathKnownAt=now;}
            string relation=BereavementRelationship(person,lost);
            if(relation==null)return;
            string occurrence=SceneIncidentKey(incident);
            string responsible=receipt.DeathKnowledge==KnownDeathEvidence.Witness && incident.PlayerAttributed?
                NpcDirectedRelationship.PlayerTargetId:null;
            string how=receipt.DeathKnowledge==KnownDeathEvidence.Witness?"I witnessed the death.":
                receipt.DeathKnowledge==KnownDeathEvidence.Discovery?"I found the body.":"A witness who knew about the death told me.";
            string observation="I lost my "+relation+", "+lost.FullName+". "+how+" "+
                (responsible!=null?"I saw the player kill them.":"This knowledge does not identify the killer.");
            if(receipt.BereavementRecorded)
            {
                // Attribution may follow the native death callback in the same moment.
                var entries=SceneMemoriesFor(observer,false);
                var memory=entries==null?null:entries.Find(m=>m.Kind==NpcSceneEventKind.Bereavement && m.EventId==occurrence);
                if(memory!=null && memory.ResponsibleId==null && responsible!=null)
                {memory.ResponsibleId=responsible;memory.Observation=observation;SceneMemoryRevision++;}
                return;
            }
            receipt.BereavementRecorded=true;
            RememberSceneEvent(observer,occurrence,NpcSceneEventKind.Bereavement,receipt.DeathKnownAt,
                incident.SettlementId,incident.BuildingKey,observation,subject:lost.Id,responsible:responsible);
        }

        public bool HoldsPartnerGrudge(Resident resident,double now)
        {
            if(resident==null || !ValidTime(now))throw new ArgumentException("Invalid grudge query.");
            var current=ResolveResident(resident.Id);
            if(current==null || !current.IsAlive || current.AgeGroup==ResidentAgeGroup.Child)return false;
            return state.AlarmIncidents.Exists(a=>a.PlayerAttributed && a.Corpse!=null && a.Corpse.DiedAt<=now &&
                ResolveResident(a.VictimId)!=null && !ResolveResident(a.VictimId).IsAlive &&
                BereavementRelationship(current,ResolveResident(a.VictimId))!=null &&
                a.People.Exists(p=>p.ResidentId==current.Id && p.DeathKnowledge==KnownDeathEvidence.Witness && p.DeathKnownAt<=now));
        }

        // Reading global death state never creates knowledge. Recollection requires
        // this observer's saved native receipt and still-retained personal memory.
        public Bereavement BereavementFor(Resident resident,double now)
        {
            if(resident==null || !ValidTime(now))throw new ArgumentException("Invalid bereavement query.");
            var current=ResolveResident(resident.Id);
            if(current==null || !current.IsAlive)return null;
            var entries=SceneMemoriesFor(current.Id,false);if(entries==null)return null;
            NpcSceneMemory selected=null;CivilianIncident death=null;AlarmPerson receipt=null;Resident lost=null;
            foreach(var memory in entries)
            {
                if(memory.Kind!=NpcSceneEventKind.Bereavement || now<memory.At || now>=memory.ExpiresAt)continue;
                var subject=ResolveResident(memory.SubjectId);
                if(subject==null || subject.IsAlive || BereavementRelationship(current,subject)==null)continue;
                var incident=state.AlarmIncidents.Find(a=>a.VictimId==subject.Id && a.Corpse!=null && SceneIncidentKey(a)==memory.EventId);
                var known=incident==null?null:incident.People.Find(p=>p.ResidentId==current.Id);
                if(known==null || known.DeathKnowledge==KnownDeathEvidence.None || !known.BereavementRecorded ||
                    now<known.DeathKnownAt || now<incident.Corpse.DiedAt)continue;
                if(selected==null || memory.At>selected.At)
                {selected=memory;death=incident;receipt=known;lost=subject;}
            }
            if(selected==null)return null;
            return new Bereavement {ResidentId=lost.Id,Name=lost.FullName,Relationship=BereavementRelationship(current,lost),
                DiedAt=death.Corpse.DiedAt,KnownAt=receipt.DeathKnownAt,
                MentionChance=.6*(1-(now-selected.At)/(selected.ExpiresAt-selected.At)),
                WitnessedPlayerKilling=death.PlayerAttributed && receipt.DeathKnowledge==KnownDeathEvidence.Witness};
        }

        public bool TryBereavementGreeting(Resident resident,double now,out Bereavement grief)
        {
            grief=BereavementFor(resident,now);
            if(grief==null || grief.WitnessedPlayerKilling)return false;
            int day=(int)Math.Floor(now/86400);
            // One attempt per observer/day, including unsuccessful rolls and different losses.
            foreach(var incident in state.AlarmIncidents)
                if(incident.People.Exists(p=>p.ResidentId==resident.Id && p.LastBereavementGreetingDay>=day))return false;
            string lostId=grief.ResidentId;
            var death=state.AlarmIncidents.Find(a=>a.VictimId==lostId && a.Corpse!=null);
            death.People.Find(p=>p.ResidentId==resident.Id).LastBereavementGreetingDay=day;
            var current=ResolveResident(resident.Id);
            return StableSeed.Combine(current.AppearanceSeed,(uint)day,0x67726965,0)%10000<grief.MentionChance*10000;
        }
    }
}
