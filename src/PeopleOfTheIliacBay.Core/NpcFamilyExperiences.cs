using System;
using System.Collections.Generic;
using System.Globalization;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        // Native household caller proves an awake, free, same-home meeting.
        // Membership alone never calls this method. Reports are not new eyewitness sources.
        public int ShareFamilyPlayerExperiences(string reporterId,string recipientId,int settlement,int home,double now)
        {
            if(!Finite(now) || now<0 || reporterId==recipientId)return 0;
            var town=state.Settlements.Find(x=>x.SettlementId==settlement);
            var household=town==null?null:town.Households.Find(x=>x.BuildingKey==home);
            var reporter=household==null?null:household.Residents.Find(x=>x.Id==reporterId);
            var recipient=household==null?null:household.Residents.Find(x=>x.Id==recipientId);
            if(reporter==null || recipient==null || !reporter.IsAlive || !recipient.IsAlive ||
                reporter.HomeKey!=home || recipient.HomeKey!=home || !ActualFamily(reporter,recipient))return 0;
            var sources=new List<NpcSceneMemory>();
            var raw=SceneMemoriesFor(reporterId,false);
            if(raw!=null)foreach(var memory in raw)if(ShareableFamilySource(memory,reporterId,now))sources.Add(memory);
            var summaries=SceneSummariesFor(reporterId,false);
            if(summaries!=null)foreach(var summary in summaries)foreach(var source in summary.Sources)
            {var fact=summary.Fact(source);if(ShareableFamilySource(fact,reporterId,now))sources.Add(fact);}
            var attitude=state.ConversationAttitudes.Find(x=>x.PersonId==reporterId);
            var boundary=attitude==null?null:attitude.Boundary;
            if(boundary!=null && boundary.ObservedAt<=now && now-boundary.ObservedAt<=86400)
                sources.Add(new NpcSceneMemory {ObserverId=reporterId,SubjectId=reporterId,ResponsibleId="player",
                    EventId=boundary.ReceiptId,At=boundary.ObservedAt,Kind=NpcSceneEventKind.Conversation,
                    SettlementId=settlement,BuildingKey=-1,Valence=-3,
                    Observation=BoundaryExperience(boundary.SpeechKind)+(boundary.RepairedAtOrdinal>0?
                        " The player later offered an accepted apology or explanation; this does not prove the explanation true.":"")});
            int count=0;
            foreach(var source in sources)
            {
                string relative=reporter.PartnerId==recipientId || recipient.PartnerId==reporterId?"partner":
                    reporter.MotherId==recipientId || reporter.FatherId==recipientId?"child":"parent";
                string detail=source.Observation;
                if(detail.Length>160)detail=detail.Substring(0,160);
                int concern=FamilyReportConcern(recipientId,reporterId,now);
                string observation="My "+relative+" "+CleanMemory(reporter.FullName??reporter.Id,60)+
                    " told me during a conversation at home: \""+detail+"\" Original event at "+
                    source.At.ToString("0",CultureInfo.InvariantCulture)+"; learned at "+now.ToString("0",CultureInfo.InvariantCulture)+
                    ". I did not witness this event. "+(concern>=3?"I am upset and guarded toward the player.":
                        concern==2?"I am concerned and want an explanation from the player.":
                        "I take the report cautiously; it does not erase my own relationship with the player.");
                // Original ID/time keep reports bounded and do not refresh source age.
                // Existing raw/summary deduplication prevents repeated delivery while retained.
                if(RememberSceneEvent(recipientId,source.EventId,NpcSceneEventKind.Conversation,source.At,
                    source.SettlementId,source.BuildingKey,observation,-concern,reporterId,"player"))count++;
            }
            return count;
        }
        private static bool ActualFamily(Resident first,Resident second)
        {return first.PartnerId==second.Id || second.PartnerId==first.Id || first.MotherId==second.Id ||
            first.FatherId==second.Id || second.MotherId==first.Id || second.FatherId==first.Id;}
        private static bool ShareableFamilySource(NpcSceneMemory memory,string reporter,double now)
        {
            if(memory.ObserverId!=reporter || memory.SubjectId!=reporter || memory.ResponsibleId!="player" ||
                memory.Valence> -2 || memory.At>now || now-memory.At>86400 || now>=memory.ExpiresAt)return false;
            switch(memory.Kind)
            {
                case NpcSceneEventKind.Injury:case NpcSceneEventKind.Assault:case NpcSceneEventKind.Theft:
                case NpcSceneEventKind.Burglary:case NpcSceneEventKind.Promise:case NpcSceneEventKind.Appointment:
                case NpcSceneEventKind.Magic:case NpcSceneEventKind.Conversation:return true;
                default:return false;
            }
        }
        private int FamilyReportConcern(string recipient,string reporter,double now)
        {
            int concern=2;var profile=ReadNpcPersonality(recipient);
            if(profile!=null)
            {
                if(profile.Compassion=="compassionate")concern++;
                if(profile.Compassion=="callous" || profile.Compassion=="cruel")concern--;
                if(profile.Forgiveness=="vindictive")concern++;
                if(profile.Forgiveness=="forgiving")concern--;
                if(profile.Patience=="impatient" || profile.Sensitivity=="highly sensitive")concern++;
                if(profile.Patience=="patient")concern--;
            }
            var family=ReadNpcRelationship(recipient,reporter,now);
            if(family!=null)
            {
                if(family.Resentment>=30 || family.TrustKnown && family.ReliabilityTrust<35)concern--;
                else if(family.TrustKnown && family.ReliabilityTrust>=70)concern++;
            }
            var player=ReadNpcRelationship(recipient,"player",now);
            if(player!=null)
            {
                if(player.Resentment>=30)concern++;
                else if(player.TrustKnown && player.ReliabilityTrust>=70)concern--;
            }
            int disposition;if(TryGetActualConversationDisposition(recipient,out disposition))
            {if(disposition>=70)concern--;else if(disposition<= -30)concern++;}
            return Math.Max(1,Math.Min(4,concern));
        }
        private static string BoundaryExperience(NpcExperienceSpeechActKind kind)
        {
            switch(kind)
            {
                case NpcExperienceSpeechActKind.DirectThreat:return "The player directly threatened me.";
                case NpcExperienceSpeechActKind.DirectHarassment:return "The player harassed me with an unwanted degrading approach.";
                case NpcExperienceSpeechActKind.SevereAbuse:return "The player seriously abused me verbally.";
                case NpcExperienceSpeechActKind.PersistentAbuse:return "The player continued abusing me after I refused it.";
                default:return "The player personally insulted me.";
            }
        }
    }
}
