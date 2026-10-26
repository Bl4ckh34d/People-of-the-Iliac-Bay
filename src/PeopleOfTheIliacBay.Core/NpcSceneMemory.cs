using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcSceneEventKind { Company, Conversation, ClosedShop, Assault, Fight, Burglary, GuardPursuit, Murder, Corpse, Trade,
        Injury, Healing, Illness, Recovery, Help, Gift, Appointment, Promise, Journey, Magic, Theft, Shopping, CashVisit, Bereavement }
    [Serializable]
    public sealed class NpcSceneMemory
    {
        public string ObserverId,EventId,Observation,SubjectId,ResponsibleId;
        public int Valence;
        public NpcSceneEventKind Kind;
        public double At;
        public int SettlementId,BuildingKey;
        public int Salience {get {return Math.Max(Importance(Kind),Math.Abs(Valence));}}
        public double ExpiresAt
        {
            get
            {
                // Same-day commerce context is an explicit gameplay promise.
                if(Kind==NpcSceneEventKind.Trade && Math.Abs(Valence)<4)return (Math.Floor(At/86400)+1)*86400;
                uint hash=2166136261;
                unchecked {foreach(char c in (ObserverId??"")+"\n"+(EventId??"")+"\n"+(int)Kind){hash^=c;hash*=16777619;}
                    hash^=hash>>16;hash*=0x7feb352du;hash^=hash>>15;hash*=0x846ca68bu;hash^=hash>>16;}
                double roll=(hash>>8)/16777215d;
                if(Kind==NpcSceneEventKind.Bereavement)return At+Math.Floor((360+360*roll)*86400);
                double duration;
                if(Salience<=1)
                    duration=roll<0.05?6*3600+roll/0.05*18*3600:
                        1800+Math.Sqrt((roll-0.05)/0.95)*(4*3600-1800);
                else
                {
                    double baseline=Math.Max(Lifetime(Kind),Salience>=4?14*86400:Salience>=3?3*86400:86400);
                    double minimum=Kind==NpcSceneEventKind.Murder?35*86400:baseline*0.5;
                    duration=minimum+Math.Sqrt(roll)*(baseline*3-minimum);
                }
                return At+Math.Floor(duration);
            }
        }
        public static double Lifetime(NpcSceneEventKind kind)
        {
            switch(kind)
            {
                case NpcSceneEventKind.Murder:return 60*86400;
                case NpcSceneEventKind.Assault:case NpcSceneEventKind.Burglary:return 14*86400;
                case NpcSceneEventKind.Injury:case NpcSceneEventKind.Illness:case NpcSceneEventKind.Help:return 7*86400;
                case NpcSceneEventKind.Healing:case NpcSceneEventKind.Recovery:case NpcSceneEventKind.Promise:case NpcSceneEventKind.Theft:return 3*86400;
                case NpcSceneEventKind.Fight:case NpcSceneEventKind.Corpse:return 7*86400;
                case NpcSceneEventKind.GuardPursuit:return 3*86400;
                case NpcSceneEventKind.Trade:return 86400;
                default:return 3600;
            }
        }
        public static int Importance(NpcSceneEventKind kind)
        {
            switch(kind)
            {
                case NpcSceneEventKind.Bereavement:return 5;
                case NpcSceneEventKind.Murder:return 4;
                case NpcSceneEventKind.Assault:case NpcSceneEventKind.Burglary:return 3;
                case NpcSceneEventKind.Injury:case NpcSceneEventKind.Illness:case NpcSceneEventKind.Help:return 3;
                case NpcSceneEventKind.Healing:case NpcSceneEventKind.Recovery:case NpcSceneEventKind.Gift:
                case NpcSceneEventKind.Appointment:case NpcSceneEventKind.Promise:case NpcSceneEventKind.Theft:return 2;
                case NpcSceneEventKind.Fight:case NpcSceneEventKind.Corpse:case NpcSceneEventKind.GuardPursuit:case NpcSceneEventKind.Trade:return 2;
                default:return 1;
            }
        }
        internal static NpcSceneMemory Copy(NpcSceneMemory source)
        {
            if(source==null || !Text(source.ObserverId,240)||!Text(source.EventId,300)||!Text(source.Observation,600)||
                !Enum.IsDefined(typeof(NpcSceneEventKind),source.Kind)||double.IsNaN(source.At)||double.IsInfinity(source.At)||source.At<0 ||
                source.SettlementId<0 || source.BuildingKey< -1 || source.Valence< -5 || source.Valence>5 ||
                source.SubjectId!=null && !Text(source.SubjectId,240) || source.ResponsibleId!=null && !Text(source.ResponsibleId,240))
                throw new ArgumentException("Invalid witnessed scene memory.");
            return new NpcSceneMemory {ObserverId=source.ObserverId,EventId=source.EventId,Observation=source.Observation,
                Kind=source.Kind,At=source.At,SettlementId=source.SettlementId,BuildingKey=source.BuildingKey,Valence=source.Valence,
                SubjectId=source.SubjectId,ResponsibleId=source.ResponsibleId};
        }
        private static bool Text(string value,int maximum)
        {
            if(string.IsNullOrWhiteSpace(value)||value.Length>maximum)return false;
            foreach(char c in value)if(char.IsControl(c))return false;
            return true;
        }
    }
    public sealed partial class WorldStateStore
    {
        private WorldState sceneMemoryOwner;
        private Dictionary<string,List<NpcSceneMemory>> sceneMemoryIndex;
        public long SceneMemoryRevision {get;private set;}
        private WorldState sceneWitnessOwner;
        private readonly Dictionary<string,double> sceneWitnessTimes=new Dictionary<string,double>();
        private readonly List<string> expiredSceneWitnesses=new List<string>();
        private double sceneWitnessPrunedAt;
        private static string SceneIncidentKey(CivilianIncident incident)
        {return incident.Id+":"+incident.GenerationSerial.ToString(CultureInfo.InvariantCulture);}
        private void PruneSceneWitnesses(double now)
        {
            if(!ReferenceEquals(sceneWitnessOwner,state))
            {sceneWitnessTimes.Clear();sceneWitnessOwner=state;sceneWitnessPrunedAt=now;}
            if(now>=sceneWitnessPrunedAt && now-sceneWitnessPrunedAt<2)return;
            expiredSceneWitnesses.Clear();
            foreach(var witness in sceneWitnessTimes)
                if(now<witness.Value || now-witness.Value>2)expiredSceneWitnesses.Add(witness.Key);
            foreach(var key in expiredSceneWitnesses)sceneWitnessTimes.Remove(key);
            expiredSceneWitnesses.Clear();sceneWitnessPrunedAt=now;
        }
        private void RememberSceneViolence(CivilianIncident incident,string observer,double now)
        {
            PruneSceneWitnesses(now);
            string occurrence=SceneIncidentKey(incident);
            string victim=incident.VictimId==null?null:AlarmResident(incident.SettlementId,incident.VictimId).FullName;
            if(incident.Corpse!=null && Math.Abs(now-incident.Corpse.DiedAt)>1)
            {
                RememberKnownDeath(incident,observer,now,KnownDeathEvidence.Discovery);
                RememberSceneEvent(observer,occurrence,NpcSceneEventKind.Corpse,now,incident.SettlementId,incident.BuildingKey,
                    "I found "+(victim??"another person")+" dead. Finding the body does not identify who killed them.",subject:incident.VictimId);
                return;
            }
            sceneWitnessTimes[occurrence+"\n"+observer]=now;
            RememberSceneEvent(observer,occurrence,incident.VictimId==null?NpcSceneEventKind.Fight:NpcSceneEventKind.Assault,
                now,incident.SettlementId,incident.BuildingKey,incident.VictimId==null?
                "I saw a hostile threat nearby.":"I witnessed an attack on "+(victim??"another person")+".",subject:incident.VictimId,
                responsible:incident.PlayerAttributed?NpcDirectedRelationship.PlayerTargetId:null);
            if(incident.Corpse!=null && Math.Abs(now-incident.Corpse.DiedAt)<=1)RememberSceneDeath(incident,observer,now);
        }
        private void RememberSceneDeath(CivilianIncident incident,string observer,double now)
        {
            if(incident.Corpse==null)return;
            RememberKnownDeath(incident,observer,now,KnownDeathEvidence.Witness);
            string victim=AlarmResident(incident.SettlementId,incident.VictimId).FullName??"another person";
            string who=incident.PlayerAttributed?"The player killed them.":
                incident.KillerThreatId!=null?"The attacker was not the player.":"I cannot identify the killer from this memory.";
            string occurrence=SceneIncidentKey(incident),observation="I saw "+victim+" killed. "+who;
            string responsible=incident.PlayerAttributed?NpcDirectedRelationship.PlayerTargetId:null;
            var entries=SceneMemoriesFor(observer,false);
            var previous=entries==null?null:entries.Find(x=>x.EventId==occurrence && x.Kind==NpcSceneEventKind.Murder);
            if(previous!=null)
            {
                // A same-moment native attribution can arrive after the death
                // callback. Refine the eyewitness fact without renewing its age.
                if(previous.ResponsibleId==null && responsible!=null)
                {previous.ResponsibleId=responsible;previous.Observation=observation;SceneMemoryRevision++;}
                return;
            }
            RememberSceneEvent(observer,occurrence,NpcSceneEventKind.Murder,now,incident.SettlementId,incident.BuildingKey,
                observation,subject:incident.VictimId,responsible:responsible);
        }
        private void RememberRecentDeathWitnesses(CivilianIncident incident)
        {
            if(!ReferenceEquals(sceneWitnessOwner,state))return;
            foreach(var person in incident.People)
            {
                double seen;
                if(person.DirectWitness && sceneWitnessTimes.TryGetValue(SceneIncidentKey(incident)+"\n"+person.ResidentId,out seen) &&
                    Math.Abs(seen-incident.Corpse.DiedAt)<=1)RememberSceneDeath(incident,person.ResidentId,incident.Corpse.DiedAt);
            }
        }
        private List<NpcSceneMemory> SceneMemoriesFor(string observer,bool create)
        {
            if(!ReferenceEquals(sceneMemoryOwner,state))
            {
                sceneMemoryIndex=new Dictionary<string,List<NpcSceneMemory>>(StringComparer.Ordinal);
                foreach(var memory in state.SceneMemories)
                {
                    List<NpcSceneMemory> entries;
                    if(!sceneMemoryIndex.TryGetValue(memory.ObserverId,out entries))
                    {entries=new List<NpcSceneMemory>();sceneMemoryIndex.Add(memory.ObserverId,entries);}
                    entries.Add(memory);
                }
                sceneMemoryOwner=state;
            }
            List<NpcSceneMemory> result;
            if(!sceneMemoryIndex.TryGetValue(observer,out result) && create)
            {result=new List<NpcSceneMemory>();sceneMemoryIndex.Add(observer,result);}
            return result;
        }
        // Called by native perception/action adapters only. Model claims never
        // establish that an observer saw an event or knew its perpetrator.
        public bool RememberSceneEvent(string observer,string eventId,NpcSceneEventKind kind,double at,
            int settlement,int building,string observation,int? valence=null,string subject=null,string responsible=null)
        {
            int evaluation=valence??NpcEventValence.Default(kind);
            if(!valence.HasValue && subject==NpcDirectedRelationship.PlayerTargetId && subject!=observer)
            {
                int affinity;
                evaluation=NpcEventValence.PersonalOutcome(evaluation,false,
                    TryGetActualConversationDisposition(observer,out affinity)?(int?)affinity:null);
            }
            else if(!valence.HasValue && subject!=null && subject!=observer)
            {
                var person=ResolveResident(observer);var affected=ResolveResident(subject);
                bool family=person!=null && affected!=null && (person.PartnerId==subject || person.MotherId==subject ||
                    person.FatherId==subject || affected.MotherId==observer || affected.FatherId==observer);
                var relationship=state.NpcRelationships.Read(observer,subject);
                if(!family && relationship!=null && relationship.Resentment>=60)evaluation=-evaluation;
            }
            var memory=NpcSceneMemory.Copy(new NpcSceneMemory {ObserverId=observer,EventId=eventId,Kind=kind,
                At=Math.Floor(at),SettlementId=settlement,BuildingKey=building,Observation=observation,Valence=evaluation,
                SubjectId=subject,ResponsibleId=responsible});
            var entries=SceneMemoriesFor(observer,true);
            foreach(var previous in entries)if(previous.EventId==eventId && previous.Kind==kind)return false;
            // Age out background detail before applying the per-person bound.
            for(int i=entries.Count-1;i>=0;i--)
                if(at>=entries[i].ExpiresAt)
                {state.SceneMemories.Remove(entries[i]);entries.RemoveAt(i);}
            if(entries.Count>=32)
            {
                NpcSceneMemory least=entries[0];
                foreach(var previous in entries)
                    if(previous.Salience<least.Salience || previous.Salience==least.Salience && previous.At<least.At)least=previous;
                if(least.Salience>memory.Salience)return false;
                entries.Remove(least);state.SceneMemories.Remove(least);
            }
            entries.Add(memory);state.SceneMemories.Add(memory);SceneMemoryRevision++;return true;
        }
        public string RecallSceneEvents(string observer,double now,int maximumCharacters=1200)
        {
            if(string.IsNullOrWhiteSpace(observer)||!Finite(now)||now<0||maximumCharacters<128||maximumCharacters>2000)
                throw new ArgumentException("Invalid scene recall request.");
            var entries=SceneMemoriesFor(observer,false);if(entries==null)return "";
            var eligible=new List<NpcSceneMemory>();
            foreach(var entry in entries)if(now>=entry.At && now<entry.ExpiresAt)eligible.Add(entry);
            eligible.Sort((a,b)=>{int importance=RecallPriority(b,now).CompareTo(RecallPriority(a,now));
                return importance!=0?importance:b.At.CompareTo(a.At);});
            if(eligible.Count==0)return "";
            var text=new StringBuilder("Personal recollections (past observations, not proof of what is happening now): ");
            foreach(var entry in eligible)
            {
                double age=now-entry.At;
                string when=age<3600?"within the last hour":age<86400?"earlier today":
                    ((int)(age/86400)).ToString(CultureInfo.InvariantCulture)+" days ago";
                string feeling=entry.Valence==0?"neutral":entry.Valence>0?"positive +"+entry.Valence:"negative "+entry.Valence;
                string line="["+when+"; my evaluation "+feeling+"] "+entry.Observation+" ";
                if(text.Length+line.Length>maximumCharacters)continue;
                text.Append(line);
            }
            return text.ToString();
        }
        private static int RecallPriority(NpcSceneMemory memory,double now)
        {
            // A same-day personal interaction must not disappear behind old trauma
            // when greeting the player. Old serious memories remain available.
            return memory.Salience+((memory.SubjectId==NpcDirectedRelationship.PlayerTargetId || memory.ResponsibleId==NpcDirectedRelationship.PlayerTargetId) &&
                Math.Floor(memory.At/86400)==Math.Floor(now/86400)?5:0);
        }
        private static List<NpcSceneMemory> CopySceneMemories(WorldState source)
        {
            var result=new List<NpcSceneMemory>();var counts=new Dictionary<string,int>(StringComparer.Ordinal);
            var keys=new HashSet<string>(StringComparer.Ordinal);
            if(source.SceneMemories==null)return result;
            foreach(var item in source.SceneMemories)
            {
                var copy=NpcSceneMemory.Copy(item);int count;counts.TryGetValue(copy.ObserverId,out count);
                Require(count<32 && keys.Add(copy.ObserverId+"\n"+copy.Kind+"\n"+copy.EventId),"Duplicate or excessive scene memories.");
                counts[copy.ObserverId]=count+1;result.Add(copy);
            }
            return result;
        }
    }
}
