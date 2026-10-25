using System;
using System.Collections.Generic;
using System.Linq;

namespace PeopleOfTheIliacBay.Core
{
    // Disposable, exact per-journal index prototype. It stores unique features
    // once as packed characters; hashes locate candidates but never establish
    // feature equality without a character comparison.
    internal sealed class ConversationFeatureIndex
    {
        private readonly IList<NpcMemoryEntry> sourceEntries;
        private readonly List<NpcMemoryEntry> entries;
        private readonly Func<string,ulong> hashFeature;
        private char[] featureText=new char[1024];
        private int featureTextLength;
        private int[] featureOffsets=new int[64];
        private ushort[] featureLengths=new ushort[64];
        private byte[] featureKinds=new byte[64];
        private ulong[] featureHashes=new ulong[64];
        private int featureCount;
        private int[] featureSlots=new int[128];
        private List<int>[] buildingPostings;
        private long buildingPostingBytes;
        private int buildingPostingCount;
        private int finalFeatureCursor;
        private int finalPostingCursor;
        private bool finalizationStarted;
        private bool finalized;
        private int[] postingStarts=Array.Empty<int>();
        private int[] postingCounts=Array.Empty<int>();
        private int[] flatPostingIds=Array.Empty<int>();
        private readonly Dictionary<int,List<int>> appendDeltas=new Dictionary<int,List<int>>();
        private readonly List<int> timeOrder=new List<int>();
        private int[] marks;
        private double[] scores;
        private readonly List<int> candidates=new List<int>();
        private int generation;

        internal ConversationFeatureIndex(IList<NpcMemoryEntry> journal)
            :this(journal,null)
        {
        }

        // Test seam permits deliberate collisions. Production uses the fixed
        // deterministic hash below, followed by exact packed-character checks.
        internal ConversationFeatureIndex(IList<NpcMemoryEntry> journal,Func<string,ulong> hashForTesting)
            :this(journal,hashForTesting,false)
        {
        }

        internal ConversationFeatureIndex(IList<NpcMemoryEntry> journal,Func<string,ulong> hashForTesting,bool deferBuild)
            :this(journal,hashForTesting,deferBuild,0)
        {
        }

        internal ConversationFeatureIndex(IList<NpcMemoryEntry> journal,Func<string,ulong> hashForTesting,bool deferBuild,int capacityHint)
        {
            if(journal==null)throw new ArgumentNullException("journal");
            int initialCount=deferBuild?Math.Min(64,journal.Count):journal.Count;
            sourceEntries=journal;entries=new List<NpcMemoryEntry>(initialCount);
            candidates.Capacity=initialCount;
            hashFeature=hashForTesting??HashFeature;
            marks=new int[Math.Max(16,initialCount)];
            scores=new double[Math.Max(16,initialCount)];
            if(deferBuild&&capacityHint>0)
            {
                // Warmup runs on the active dialogue's update path. Do not
                // allocate for the full archive before its first cooperative slice.
                capacityHint=Math.Min(64,capacityHint);
                int hintedFeatures=NextPowerOfTwo(Math.Max(64,(int)Math.Min(1048576L,(long)capacityHint*2)));
                featureOffsets=new int[hintedFeatures];featureLengths=new ushort[hintedFeatures];
                featureKinds=new byte[hintedFeatures];featureHashes=new ulong[hintedFeatures];
                buildingPostings=new List<int>[hintedFeatures];
                featureSlots=new int[NextPowerOfTwo(Math.Max(128,(int)Math.Min(2097152L,(long)capacityHint*4)))];
                featureText=new char[Math.Max(1024,(int)Math.Min(8_000_000L,(long)capacityHint*48))];
            }
            else buildingPostings=new List<int>[featureOffsets.Length];
            if(!deferBuild)
            {
                for(int i=0;i<journal.Count;i++)AddJournalEntry(i);
                FinishBuild();
            }
        }

        internal int FeatureCount { get { return featureCount; } }
        internal NpcMemoryEntry EntryAt(int index) { return entries[index]; }
        internal int EligibleCount(double now)
        {if(double.IsNaN(now)||double.IsInfinity(now)||now<0)throw new ArgumentOutOfRangeException("now");return UpperBoundTimeOrder(now);}

        internal NpcMemoryEntry LatestMeeting(double now)
        {
            int count=UpperBoundTimeOrder(now);
            for(int i=count-1;i>=0;i--)
            {
                NpcMemoryEntry entry=entries[timeOrder[i]];
                if(entry.Source=="observed"&&entry.Observation=="Met and spoke with the player here.")return entry;
            }
            return null;
        }

        // Accounts retained arrays and their owning collection/object overhead;
        // canonical journal objects and strings are externally owned.
        internal long OwnedBytes
        {
            get
            {
                long bytes=256L+featureText.Length*sizeof(char)+featureOffsets.Length*sizeof(int)+
                    featureLengths.Length*sizeof(ushort)+featureKinds.Length+featureHashes.Length*sizeof(ulong)+
                    featureSlots.Length*sizeof(int)+postingStarts.Length*sizeof(int)+postingCounts.Length*sizeof(int)+
                    flatPostingIds.Length*sizeof(int)+timeOrder.Capacity*sizeof(int)+marks.Length*sizeof(int)+
                    scores.Length*sizeof(double)+candidates.Capacity*sizeof(int)+64L;
                // Index-owned row snapshots freeze timestamps/roles while the
                // external canonical journal remains append-only and authoritative.
                bytes+=entries.Capacity*IntPtr.Size+entries.Count*(8L+8L*IntPtr.Size);
                foreach(var pair in appendDeltas)bytes+=48L+pair.Value.Capacity*sizeof(int);
                // Include conservative bucket/entry-array headroom for the
                // mutable per-feature append-delta dictionary.
                if(appendDeltas.Count>0)bytes+=64L+appendDeltas.Count*64L;
                if(buildingPostings!=null)
                {
                    bytes+=buildingPostings.Length*IntPtr.Size+buildingPostingBytes;
                }
                return bytes;
            }
        }

        internal List<int> Rank(string text,double now)
        {
            HashSet<string> query=ResolveQueryFeatures(text,now);
            NextGeneration();candidates.Clear();
            int eligibleCount=UpperBoundTimeOrder(now);
            foreach(string feature in query)
            {
                int featureId=FindFeature(feature);
                if(featureId<0)continue;
                int start=postingStarts[featureId],count=postingCounts[featureId];
                int baseDf=UpperBoundPostings(flatPostingIds,start,count,now);
                List<int> delta;
                int deltaDf=appendDeltas.TryGetValue(featureId,out delta)?UpperBoundList(delta,now):0;
                int df=baseDf+deltaDf;
                if(df==0)continue;
                double idf=InverseDocumentFrequency(featureKinds[featureId],eligibleCount,df);
                for(int i=0;i<baseDf;i++)AddScore(flatPostingIds[start+i],idf);
                for(int i=0;i<deltaDf;i++)AddScore(delta[i],idf);
            }

            // Include all zero-score eligible entries. RecallConversation may be
            // called with a larger-than-default output budget, so callers retain
            // the same complete ranking contract as the scan implementation.
            for(int i=eligibleCount-1;i>=0;i--)AddScore(timeOrder[i],0);
            candidates.Sort((a,b)=>{
                int score=scores[b].CompareTo(scores[a]);if(score!=0)return score;
                int at=entries[b].At.CompareTo(entries[a].At);return at!=0?at:b.CompareTo(a);
            });
            return candidates.ToList();
        }

        internal HashSet<string> ResolveQueryFeatures(string text,double now)
        {
            if(double.IsNaN(now)||double.IsInfinity(now)||now<0)throw new ArgumentOutOfRangeException("now");
            HashSet<string> query=ConversationFeatureExtractor.Extract(text);
            if(query.Count==0)
            {
                int latest=-1;
                for(int i=timeOrder.Count-1;i>=0;i--)
                {
                    int entryIndex=timeOrder[i];
                    if(entries[entryIndex].At<=now&&entries[entryIndex].Source!="observed")
                    {latest=entryIndex;break;}
                }
                if(latest>=0)query=ConversationFeatureExtractor.Extract(entries[latest].PlayerText+" "+
                    (ConversationMemoryProvenance.IsSuccessfulSharedExchange(entries[latest])?entries[latest].NpcText:""));
            }
            return query;
        }

        internal double ScoreFeatures(HashSet<string> query,HashSet<string> document,double now)
        {
            if(double.IsNaN(now)||double.IsInfinity(now)||now<0)throw new ArgumentOutOfRangeException("now");
            int eligibleCount=UpperBoundTimeOrder(now);
            double score=0;
            foreach(string feature in query)
            {
                int featureId=FindFeature(feature);
                if(featureId<0)continue;
                if(!document.Contains(feature))continue;
                int start=postingStarts[featureId],count=postingCounts[featureId];
                int baseDf=UpperBoundPostings(flatPostingIds,start,count,now);
                List<int> delta;
                int deltaDf=appendDeltas.TryGetValue(featureId,out delta)?UpperBoundList(delta,now):0;
                int df=baseDf+deltaDf;
                if(df==0)continue;
                score+=InverseDocumentFrequency(featureKinds[featureId],eligibleCount,df);
            }
            return score;
        }

        private static double InverseDocumentFrequency(byte kind,int documents,int frequency)
        {double weight=kind==0?1:kind==1?0.65:0.5;return Math.Log(1+(documents-frequency+0.5)/(frequency+0.5))*weight;}

        internal void Append(NpcMemoryEntry entry,int journalIndex)
        {
            if(entry==null)throw new ArgumentNullException("entry");
            if(journalIndex!=sourceEntries.Count-1||journalIndex!=entries.Count||!ReferenceEquals(sourceEntries[journalIndex],entry))
                throw new ArgumentException("Append requires the journal's new tail entry and its matching index.");
            EnsureScratchCapacity(journalIndex+1);
            AddJournalEntry(journalIndex);
            // New vocabulary features extend the flat base with empty posting
            // slices; appended posting IDs live in the small mutable delta lists.
            if(postingStarts.Length<featureCount)
            {
                Array.Resize(ref postingStarts,featureCount);
                Array.Resize(ref postingCounts,featureCount);
            }
        }

        internal void AddJournalEntry(int index)
        {
            if(index!=entries.Count||index>=sourceEntries.Count)
                throw new ArgumentException("Journal entries must be added once in source order.","index");
            var entry=CloneEntry(sourceEntries[index]);entries.Add(entry);EnsureScratchCapacity(index+1);
            if(entry.Observation=="Conversation started.")return;
            InsertSorted(timeOrder,index);
            string npcSpeech=ConversationMemoryProvenance.IsSuccessfulSharedExchange(entry)?entry.NpcText:"";
            HashSet<string> features=ConversationFeatureExtractor.Extract(entry.PlayerText+" "+npcSpeech+" "+
                entry.Place+" "+entry.Date+" "+entry.Observation);
            foreach(string feature in features)
            {
                int featureId=Intern(feature);
                if(buildingPostings!=null)
                {
                    List<int> posting=buildingPostings[featureId];int oldCapacity=posting.Capacity;
                    posting.Add(index);buildingPostingCount++;buildingPostingBytes+=(posting.Capacity-oldCapacity)*sizeof(int);
                }
                else
                {
                    List<int> delta;
                    if(!appendDeltas.TryGetValue(featureId,out delta))appendDeltas.Add(featureId,delta=new List<int>());
                    InsertSorted(delta,index);
                }
            }
        }

        internal void FinishBuild()
        {
            while(!finalized)FinishBuildStep(Math.Max(1,featureCount),long.MaxValue);
        }

        internal bool FinishBuildStep(int maximumFeatures,long maximumElapsedTicks)
        {
            if(finalized)return true;
            if(maximumFeatures<=0||maximumElapsedTicks<=0)return false;
            if(!finalizationStarted)
            {
                postingStarts=new int[featureCount];postingCounts=new int[featureCount];flatPostingIds=new int[buildingPostingCount];
                finalFeatureCursor=0;finalPostingCursor=0;finalizationStarted=true;
            }
            long started=System.Diagnostics.Stopwatch.GetTimestamp();int processed=0;
            while(finalFeatureCursor<featureCount&&processed<maximumFeatures)
            {
                int feature=finalFeatureCursor;List<int> values=buildingPostings[feature];
                values.Sort(CompareEntryIndexes);
                postingStarts[feature]=finalPostingCursor;postingCounts[feature]=values.Count;
                values.CopyTo(flatPostingIds,finalPostingCursor);finalPostingCursor+=values.Count;
                finalFeatureCursor++;processed++;
                if(System.Diagnostics.Stopwatch.GetTimestamp()-started>=maximumElapsedTicks)break;
            }
            if(finalFeatureCursor==featureCount)
            {buildingPostings=null;buildingPostingBytes=0;buildingPostingCount=0;finalized=true;}
            return finalized;
        }

        internal bool IsFinalized { get { return finalized; } }

        internal void DiscardBuild()
        {
            buildingPostings=null;buildingPostingBytes=0;buildingPostingCount=0;finalized=false;
            featureText=Array.Empty<char>();featureOffsets=Array.Empty<int>();
            featureLengths=Array.Empty<ushort>();featureKinds=Array.Empty<byte>();featureHashes=Array.Empty<ulong>();
            featureSlots=Array.Empty<int>();postingStarts=Array.Empty<int>();postingCounts=Array.Empty<int>();
            flatPostingIds=Array.Empty<int>();marks=Array.Empty<int>();scores=Array.Empty<double>();
            timeOrder.Clear();appendDeltas.Clear();candidates.Clear();entries.Clear();entries.TrimExcess();featureCount=0;featureTextLength=0;
        }

        private static NpcMemoryEntry CloneEntry(NpcMemoryEntry entry)
        {
            if(entry==null)throw new ArgumentException("Conversation journal contains a null entry.","entry");
            if(double.IsNaN(entry.At)||double.IsInfinity(entry.At)||entry.At<0)
                throw new ArgumentException("Conversation journal contains an invalid timestamp.","entry");
            return new NpcMemoryEntry{At=entry.At,Date=entry.Date,Place=entry.Place,PlayerText=entry.PlayerText,
                NpcText=entry.NpcText,Observation=entry.Observation,Source=entry.Source};
        }

        private int Intern(string feature)
        {
            ulong hash=hashFeature(feature);int mask=featureSlots.Length-1;int slot=(int)hash&mask;
            while(featureSlots[slot]!=0)
            {
                int existing=featureSlots[slot]-1;
                if(featureHashes[existing]==hash&&FeatureEquals(existing,feature))return existing;
                slot=(slot+1)&mask;
            }
            if((featureCount+1)*10>featureSlots.Length*7)
            {ResizeSlots(featureSlots.Length*2);mask=featureSlots.Length-1;slot=(int)hash&mask;while(featureSlots[slot]!=0)slot=(slot+1)&mask;}
            EnsureFeatureCapacity(featureCount+1,feature.Length);
            int id=featureCount++;
            featureOffsets[id]=featureTextLength;featureLengths[id]=checked((ushort)feature.Length);
            featureKinds[id]=(byte)(feature.StartsWith("w:",StringComparison.Ordinal)?0:feature.StartsWith("s:",StringComparison.Ordinal)?1:2);
            featureHashes[id]=hash;feature.CopyTo(0,featureText,featureTextLength,feature.Length);featureTextLength+=feature.Length;
            featureSlots[slot]=id+1;
            if(buildingPostings!=null){buildingPostings[id]=new List<int>();buildingPostingBytes+=40L;}
            return id;
        }

        private int FindFeature(string feature)
        {
            ulong hash=hashFeature(feature);int mask=featureSlots.Length-1;int slot=(int)hash&mask;
            while(featureSlots[slot]!=0)
            {
                int id=featureSlots[slot]-1;
                if(featureHashes[id]==hash&&FeatureEquals(id,feature))return id;
                slot=(slot+1)&mask;
            }
            return -1;
        }

        private bool FeatureEquals(int id,string feature)
        {
            if(featureLengths[id]!=feature.Length)return false;
            int start=featureOffsets[id];
            for(int i=0;i<feature.Length;i++)if(featureText[start+i]!=feature[i])return false;
            return true;
        }

        private void EnsureFeatureCapacity(int required,int extraText)
        {
            if(required>featureOffsets.Length)
            {
                int size=Math.Max(required,featureOffsets.Length*2);
                Array.Resize(ref featureOffsets,size);Array.Resize(ref featureLengths,size);
                Array.Resize(ref featureKinds,size);Array.Resize(ref featureHashes,size);
                if(buildingPostings!=null)Array.Resize(ref buildingPostings,size);
            }
            if(featureTextLength+extraText>featureText.Length)
                Array.Resize(ref featureText,Math.Max(featureTextLength+extraText,featureText.Length*2));
        }

        private void ResizeSlots(int size)
        {
            featureSlots=new int[size];int mask=size-1;
            for(int id=0;id<featureCount;id++)
            {int slot=(int)featureHashes[id]&mask;while(featureSlots[slot]!=0)slot=(slot+1)&mask;featureSlots[slot]=id+1;}
        }

        private void EnsureScratchCapacity(int required)
        {
            if(required>marks.Length)
            {int size=Math.Max(required,marks.Length*2);Array.Resize(ref marks,size);Array.Resize(ref scores,size);}
            if(required>candidates.Capacity)candidates.Capacity=required;
        }

        private void AddScore(int index,double amount)
        {
            if(marks[index]!=generation){marks[index]=generation;scores[index]=0;candidates.Add(index);}
            scores[index]+=amount;
        }
        private void NextGeneration()
        {
            if(generation==int.MaxValue){Array.Clear(marks,0,marks.Length);generation=1;}else generation++;
        }
        private int UpperBoundTimeOrder(double now)
        {
            int low=0,high=timeOrder.Count;
            while(low<high){int mid=low+(high-low)/2;if(entries[timeOrder[mid]].At<=now)low=mid+1;else high=mid;}
            return low;
        }
        private int UpperBoundPostings(int[] values,int start,int count,double now)
        {
            int low=start,high=start+count;
            while(low<high){int mid=low+(high-low)/2;if(entries[values[mid]].At<=now)low=mid+1;else high=mid;}
            return low-start;
        }
        private int UpperBoundList(List<int> values,double now)
        {
            int low=0,high=values.Count;
            while(low<high){int mid=low+(high-low)/2;if(entries[values[mid]].At<=now)low=mid+1;else high=mid;}
            return low;
        }
        private int CompareEntryIndexes(int a,int b)
        {int at=entries[a].At.CompareTo(entries[b].At);return at!=0?at:a.CompareTo(b);}
        private void InsertSorted(List<int> values,int index)
        {
            int low=0,high=values.Count;
            while(low<high){int mid=low+(high-low)/2;if(CompareEntryIndexes(values[mid],index)<=0)low=mid+1;else high=mid;}
            values.Insert(low,index);
        }
        private static ulong HashFeature(string value)
        {
            unchecked{ulong hash=14695981039346656037UL;for(int i=0;i<value.Length;i++)hash=(hash^value[i])*1099511628211UL;return hash;}
        }
        private static int NextPowerOfTwo(int value)
        {
            if(value<=1)return 1;int result=1;
            while(result<value&&result<(1<<30))result<<=1;
            return result;
        }
    }
}
