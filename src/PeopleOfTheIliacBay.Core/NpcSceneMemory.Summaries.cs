using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PeopleOfTheIliacBay.Core
{
    [Serializable]
    public sealed class NpcSceneMemorySource
    {
        public string EventId;
        public double At;
    }

    /// <summary>One exact observed fact, shared by a bounded set of original event sources.</summary>
    [Serializable]
    public sealed class NpcSceneMemorySummary
    {
        public string ObserverId,Observation,SubjectId,ResponsibleId;
        public NpcSceneEventKind Kind;
        public int Valence,SettlementId,BuildingKey;
        public List<NpcSceneMemorySource> Sources=new List<NpcSceneMemorySource>();

        internal NpcSceneMemory Fact(NpcSceneMemorySource source)
        {return new NpcSceneMemory {ObserverId=ObserverId,Observation=Observation,SubjectId=SubjectId,
            ResponsibleId=ResponsibleId,Kind=Kind,Valence=Valence,SettlementId=SettlementId,BuildingKey=BuildingKey,
            EventId=source.EventId,At=source.At};}

        internal bool Matches(NpcSceneMemory fact)
        {return ObserverId==fact.ObserverId && Kind==fact.Kind && Observation==fact.Observation &&
            SubjectId==fact.SubjectId && ResponsibleId==fact.ResponsibleId && Valence==fact.Valence &&
            SettlementId==fact.SettlementId && BuildingKey==fact.BuildingKey;}

        internal static double SourceExpiresAt(NpcSceneMemory fact)
        {return fact.ExpiresAt;}
    }

    public sealed partial class WorldStateStore
    {
        private const int MaximumSceneSummaryGroups=16,MaximumSceneSummarySources=8;
        private WorldState sceneSummaryOwner;
        private Dictionary<string,List<NpcSceneMemorySummary>> sceneSummaryIndex;

        private List<NpcSceneMemorySummary> SceneSummariesFor(string observer,bool create)
        {
            if(!ReferenceEquals(sceneSummaryOwner,state))
            {
                sceneSummaryIndex=new Dictionary<string,List<NpcSceneMemorySummary>>(StringComparer.Ordinal);
                foreach(var summary in state.SceneMemorySummaries)
                {
                    List<NpcSceneMemorySummary> entries;
                    if(!sceneSummaryIndex.TryGetValue(summary.ObserverId,out entries))
                    {entries=new List<NpcSceneMemorySummary>();sceneSummaryIndex.Add(summary.ObserverId,entries);}
                    entries.Add(summary);
                }
                sceneSummaryOwner=state;
            }
            List<NpcSceneMemorySummary> result;
            if(!sceneSummaryIndex.TryGetValue(observer,out result) && create)
            {result=new List<NpcSceneMemorySummary>();sceneSummaryIndex.Add(observer,result);}
            return result;
        }

        private bool HasSummarizedSceneEvent(string observer,string eventId,NpcSceneEventKind kind)
        {
            var summaries=SceneSummariesFor(observer,false);
            if(summaries!=null)foreach(var summary in summaries)
                if(summary.Kind==kind && summary.Sources.Exists(x=>x.EventId==eventId))return true;
            return false;
        }

        private bool SceneFactHasSummary(NpcSceneMemory fact)
        {
            var summaries=SceneSummariesFor(fact.ObserverId,false);
            return summaries!=null && summaries.Exists(x=>x.Matches(fact) &&
                x.Sources.Exists(source=>source.EventId==fact.EventId && source.At==fact.At));
        }

        private NpcSceneMemorySummary SummarizedSceneActorRefinement(NpcSceneMemory fact)
        {
            if(fact.ResponsibleId==null)return null;
            var summaries=SceneSummariesFor(fact.ObserverId,false);if(summaries==null)return null;
            foreach(var summary in summaries)
            {
                if(summary.Kind!=fact.Kind || summary.ResponsibleId!=null || summary.SubjectId!=fact.SubjectId ||
                    summary.SettlementId!=fact.SettlementId || summary.BuildingKey!=fact.BuildingKey || summary.Valence!=fact.Valence)continue;
                var source=summary.Sources.Find(x=>x.EventId==fact.EventId && x.At==fact.At);if(source==null)continue;
                return summary;
            }
            return null;
        }

        // Only actual recall calls this. At most 32 raw records and 16x8 stored
        // sources belong to this observer. No population loop or model request.
        private void ConsolidateSceneMemories(string observer,double now,List<NpcSceneMemory> raw)
        {
            var summaries=SceneSummariesFor(observer,true);
            for(int i=summaries.Count-1;i>=0;i--)
            {
                var summary=summaries[i];
                if(summary.Sources.RemoveAll(x=>now>=NpcSceneMemorySummary.SourceExpiresAt(summary.Fact(x)))>0)
                    SceneMemoryRevision++;
                if(summary.Sources.Count==0)RemoveSceneSummary(summaries,summary);
            }
            if(raw==null)return;
            foreach(var fact in raw)
            {
                bool eligible=fact.Salience>=3 && now-fact.At>=86400 && now<fact.ExpiresAt;
                NpcSceneMemorySummary matching=null;
                // A native appraisal or same-moment attribution may refine a raw
                // source. Reproject that source; never preserve conflicting facts.
                for(int i=summaries.Count-1;i>=0;i--)
                {
                    var previous=summaries[i];
                    var source=previous.Sources.Find(x=>previous.Kind==fact.Kind && x.EventId==fact.EventId);
                    if(source==null)continue;
                    if(previous.Matches(fact) && source.At==fact.At){matching=previous;break;}
                    previous.Sources.Remove(source);SceneMemoryRevision++;
                    if(previous.Sources.Count==0)RemoveSceneSummary(summaries,previous);
                }
                if(matching!=null || !eligible)continue;
                matching=summaries.Find(x=>x.Matches(fact));
                if(matching==null)
                {
                    if(summaries.Count>=MaximumSceneSummaryGroups)
                    {
                        var least=summaries[0];
                        foreach(var candidate in summaries)
                            if(SummaryPriority(candidate)<SummaryPriority(least) ||
                                SummaryPriority(candidate)==SummaryPriority(least) && LastSummaryAt(candidate)<LastSummaryAt(least))least=candidate;
                        if(fact.Salience<SummaryPriority(least) || fact.Salience==SummaryPriority(least) && fact.At<LastSummaryAt(least))continue;
                        RemoveSceneSummary(summaries,least);
                    }
                    matching=new NpcSceneMemorySummary {ObserverId=fact.ObserverId,Observation=fact.Observation,
                        SubjectId=fact.SubjectId,ResponsibleId=fact.ResponsibleId,Kind=fact.Kind,Valence=fact.Valence,
                        SettlementId=fact.SettlementId,BuildingKey=fact.BuildingKey};
                    summaries.Add(matching);state.SceneMemorySummaries.Add(matching);
                }
                if(matching.Sources.Count>=MaximumSceneSummarySources)
                {
                    var oldest=matching.Sources[0];
                    foreach(var source in matching.Sources)if(source.At<oldest.At)oldest=source;
                    if(fact.At<=oldest.At)continue;
                    matching.Sources.Remove(oldest);
                }
                matching.Sources.Add(new NpcSceneMemorySource {EventId=fact.EventId,At=fact.At});SceneMemoryRevision++;
            }
        }

        private void RemoveSceneSummary(List<NpcSceneMemorySummary> summaries,NpcSceneMemorySummary summary)
        {summaries.Remove(summary);state.SceneMemorySummaries.Remove(summary);SceneMemoryRevision++;}
        private static int SummaryPriority(NpcSceneMemorySummary summary)
        {return Math.Max(NpcSceneMemory.Importance(summary.Kind),Math.Abs(summary.Valence));}
        private static double LastSummaryAt(NpcSceneMemorySummary summary)
        {double at=0;foreach(var source in summary.Sources)at=Math.Max(at,source.At);return at;}

        private string RenderSceneSummaries(string observer,double now,int maximumCharacters,HashSet<string> covered)
        {
            var summaries=SceneSummariesFor(observer,false);if(summaries==null)return "";
            var raw=SceneMemoriesFor(observer,false);
            var ranked=new List<NpcSceneMemorySummary>(summaries);
            ranked.Sort((a,b)=>{int priority=SummaryPriority(b).CompareTo(SummaryPriority(a));
                return priority!=0?priority:LastSummaryAt(b).CompareTo(LastSummaryAt(a));});
            var text=new StringBuilder();
            foreach(var summary in ranked)
            {
                double first=double.MaxValue,last=0;var eligible=new List<NpcSceneMemorySource>();
                foreach(var source in summary.Sources)
                {
                    var current=raw==null?null:raw.Find(x=>x.Kind==summary.Kind && x.EventId==source.EventId);
                    // Publication checks are read-only, so suppress a stale
                    // projection immediately instead of waiting for consolidation.
                    if(current!=null && (!summary.Matches(current) || current.At!=source.At))continue;
                    if(source.At<=now && now<NpcSceneMemorySummary.SourceExpiresAt(summary.Fact(source)))
                    {eligible.Add(source);first=Math.Min(first,source.At);last=Math.Max(last,source.At);}
                }
                if(eligible.Count==0)continue;
                string line="[Long-term: "+eligible.Count+" observations; native time "+first.ToString("R",CultureInfo.InvariantCulture)+
                    (last==first?"":".."+last.ToString("R",CultureInfo.InvariantCulture))+"; place "+summary.SettlementId+"/"+summary.BuildingKey+
                    "; affected "+(summary.SubjectId??"unspecified")+"; responsible "+(summary.ResponsibleId??"unknown")+
                    "; my evaluation "+summary.Valence.ToString("+0;-0;0",CultureInfo.InvariantCulture)+"] "+summary.Observation+" ";
                if(text.Length+line.Length>maximumCharacters)continue;
                text.Append(line);
                foreach(var source in eligible)covered.Add(((int)summary.Kind).ToString(CultureInfo.InvariantCulture)+"\n"+source.EventId);
            }
            return text.ToString();
        }

        private static List<NpcSceneMemorySummary> CopySceneMemorySummaries(WorldState source)
        {
            var result=new List<NpcSceneMemorySummary>();var counts=new Dictionary<string,int>(StringComparer.Ordinal);
            var keys=new HashSet<string>(StringComparer.Ordinal);
            Require(source.SceneMemorySummaries!=null,"Missing scene summaries.");
            if(source.SceneMemorySummaries==null)return result;
            foreach(var summary in source.SceneMemorySummaries)
            {
                Require(summary!=null && summary.Sources!=null && summary.Sources.Count>0 &&
                    summary.Sources.Count<=MaximumSceneSummarySources,"Invalid scene summary sources.");
                var copy=new NpcSceneMemorySummary();
                foreach(var item in summary.Sources)
                {
                    Require(item!=null,"Missing scene summary source.");
                    var fact=NpcSceneMemory.Copy(summary.Fact(item));
                    Require(fact.Salience>=3 && keys.Add(fact.ObserverId+"\n"+(int)fact.Kind+"\n"+fact.EventId),
                        "Duplicate or irrelevant scene summary source.");
                    copy.ObserverId=fact.ObserverId;copy.Observation=fact.Observation;copy.SubjectId=fact.SubjectId;
                    copy.ResponsibleId=fact.ResponsibleId;copy.Kind=fact.Kind;copy.Valence=fact.Valence;
                    copy.SettlementId=fact.SettlementId;copy.BuildingKey=fact.BuildingKey;
                    copy.Sources.Add(new NpcSceneMemorySource {EventId=fact.EventId,At=fact.At});
                }
                int count;counts.TryGetValue(copy.ObserverId,out count);
                Require(count<MaximumSceneSummaryGroups,"Excessive scene summaries.");counts[copy.ObserverId]=count+1;
                result.Add(copy);
            }
            return result;
        }
    }
}
