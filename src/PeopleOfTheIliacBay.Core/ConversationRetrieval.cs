using System;
using System.Collections.Generic;
using System.Linq;

namespace PeopleOfTheIliacBay.Core
{
    // A local lexical/concept hybrid, not an embedding model. The vocabulary
    // describes topics, never people, answers or invented memories. All features
    // are rebuilt from the caller's already filtered journal; nothing is shared
    // across people, characters, future timestamps or restored save branches.
    internal sealed class ConversationRetrieval
    {
        private readonly HashSet<string> query;
        private readonly ConversationFeatureIndex indexed;
        private readonly double indexedNow;
        private readonly Dictionary<string,int> frequencies=new Dictionary<string,int>(StringComparer.Ordinal);
        private readonly Dictionary<NpcMemoryEntry,HashSet<string>> documents=new Dictionary<NpcMemoryEntry,HashSet<string>>();

        internal ConversationRetrieval(IList<NpcMemoryEntry> entries,string text)
        {
            query=ConversationFeatureExtractor.Extract(text);
            // Resolve only a topic-free follow-up against the latest shared turn.
            // Do not silently replace an explicit new topic with the old topic.
            if(query.Count==0)
            {
                var last=entries.Select((entry,index)=>new {Entry=entry,Index=index}).Where(e=>e.Entry.Source!="observed")
                    .OrderByDescending(e=>e.Entry.At).ThenByDescending(e=>e.Index).FirstOrDefault();
                if(last!=null) query=ConversationFeatureExtractor.Extract(last.Entry.PlayerText+" "+last.Entry.NpcText);
            }
            foreach(var entry in entries)
            {
                var features=ConversationFeatureExtractor.Extract(entry.PlayerText+" "+entry.NpcText+" "+entry.Place+" "+entry.Date+" "+entry.Observation);
                documents[entry]=features;
                foreach(string feature in features)
                {
                    int count; frequencies.TryGetValue(feature,out count); frequencies[feature]=count+1;
                }
            }
        }

        internal ConversationRetrieval(ConversationFeatureIndex index,string text,double now)
        {
            if(index==null)throw new ArgumentNullException("index");
            indexed=index;indexedNow=now;query=index.ResolveQueryFeatures(text,now);
        }

        internal double Score(NpcMemoryEntry entry)
        { return ScoreFeatures(documents[entry]); }

        private double ScoreFeatures(HashSet<string> features)
        {
            if(indexed!=null)return indexed.ScoreFeatures(query,features,indexedNow);
            double score=0;
            foreach(string feature in query)
            {
                if(!features.Contains(feature)) continue;
                int count; frequencies.TryGetValue(feature,out count);
                double weight=feature[0]=='w' ? 1 : feature[0]=='s' ? 0.65 : 0.5;
                score+=weight*Math.Log(1+(documents.Count-count+0.5)/(count+0.5));
            }
            return score;
        }

        // Select an exact substring around the strongest matching clause. Do not
        // paraphrase, change pronouns, merge speakers or drop quote delimiters.
        internal string Excerpt(string text,int maximum)
        {
            if(text.Length<=maximum) return text;
            if(maximum<8) return "...";
            int bestStart=0; double bestScore=0;
            foreach(var clause in ConversationFeatureExtractor.Clauses(text))
            {
                double score=ScoreFeatures(ConversationFeatureExtractor.Extract(clause.Value));
                if(score>bestScore) {bestScore=score;bestStart=clause.Index;}
            }
            // A single long clause can also place the useful term near its end.
            int contentBudget=maximum-8;
            if(bestScore>0)
            {
                var matches=ConversationFeatureExtractor.MatchTokens(text.Substring(bestStart));
                foreach(var token in matches)
                    if(ScoreFeatures(ConversationFeatureExtractor.Extract(token.Value))>0)
                    {bestStart+=Math.Max(0,token.Index-contentBudget/2);break;}
            }
            bestStart=Math.Min(bestStart,text.Length);
            while(bestStart<text.Length && char.IsWhiteSpace(text[bestStart])) bestStart++;
            while(bestStart>0 && bestStart<text.Length && !char.IsWhiteSpace(text[bestStart-1])) bestStart--;
            int end=Math.Min(text.Length,bestStart+contentBudget);
            if(end<text.Length)
                while(end>bestStart && !char.IsWhiteSpace(text[end])) end--;
            if(end==bestStart) end=Math.Min(text.Length,bestStart+contentBudget);
            return (bestStart>0 ? "... " : "")+text.Substring(bestStart,end-bestStart).Trim()+(end<text.Length ? " ..." : "");
        }

    }
}
