using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PeopleOfTheIliacBay.Core
{
    public sealed class NpcVitalsView
    {
        public bool IsAlive {get;private set;}
        public bool IsPersisted {get;private set;}
        public int MaximumHealth {get;private set;}
        public int CurrentHealth {get;private set;}
        public int MaximumMagicka {get;private set;}
        public int CurrentMagicka {get;private set;}
        public double CapturedAt {get;private set;}
        public long Revision {get;private set;}
        internal NpcVitalsView(bool alive,bool persisted,int maximumHealth,int currentHealth,int maximumMagicka,
            int currentMagicka,double capturedAt,long revision)
        {
            IsAlive=alive;IsPersisted=persisted;MaximumHealth=maximumHealth;CurrentHealth=currentHealth;
            MaximumMagicka=maximumMagicka;CurrentMagicka=currentMagicka;CapturedAt=capturedAt;Revision=revision;
        }
    }

    /// <summary>Detached facts from one native health episode; actor attribution remains separate and unknown.</summary>
    public sealed class NpcHealthEpisodeSummary
    {
        public string ObserverId { get; private set; }
        public string EpisodeId { get; private set; }
        public string OutcomeId { get; private set; }
        public double StartedAt { get; private set; }
        public double LastChangedAt { get; private set; }
        public int InitialHealth { get; private set; }
        public int FinalHealth { get; private set; }
        public int MaximumHealth { get; private set; }
        public long DamageTotal { get; private set; }
        public long HealingTotal { get; private set; }
        public int ChangeCount { get; private set; }
        public long FirstVitalsRevision { get; private set; }
        public long LastVitalsRevision { get; private set; }

        internal NpcHealthEpisodeSummary(string observerId, string episodeId, string outcomeId,
            double startedAt, double lastChangedAt, int initialHealth, int finalHealth, int maximumHealth,
            long damageTotal, long healingTotal, int changeCount, long firstVitalsRevision, long lastVitalsRevision)
        {
            ObserverId = observerId;
            EpisodeId = episodeId;
            OutcomeId = outcomeId;
            StartedAt = startedAt;
            LastChangedAt = lastChangedAt;
            InitialHealth = initialHealth;
            FinalHealth = finalHealth;
            MaximumHealth = maximumHealth;
            DamageTotal = damageTotal;
            HealingTotal = healingTotal;
            ChangeCount = changeCount;
            FirstVitalsRevision = firstVitalsRevision;
            LastVitalsRevision = lastVitalsRevision;
        }
    }

    public sealed partial class WorldStateStore
    {
        // Opaque reference changes on every Restore. Runtime actors retain it
        // so delayed callbacks from an old save branch cannot write into the new one.
        public object CaptureNpcVitalsWriteEpoch() { return bindingEpoch; }
        public bool IsNpcVitalsWriteEpochCurrent(object epoch)
        { return epoch!=null && ReferenceEquals(bindingEpoch,epoch); }

        // Reads never allocate a progression row. Unchanged actors use the seeded
        // full-resource baseline until a native resource value changes.
        public NpcVitalsView ReadNpcVitals(string id,NpcArchetype archetype)
        {
            bool alive;
            Require(KnownNpcIdentity(state,id,out alive),"Unknown NPC vitals identity.");
            var profile=ReadNpcProfile(id,archetype);
            var progress=IndexedNpcProgress(id);
            bool persisted=progress!=null && progress.VitalsInitialized;
            int health=alive ? (persisted ? Math.Max(1,Math.Min(profile.MaximumHealth,progress.CurrentHealth)) : profile.MaximumHealth) : 0;
            int magicka=persisted ? Math.Max(0,Math.Min(profile.MaximumMagicka,progress.CurrentMagicka)) : profile.MaximumMagicka;
            return new NpcVitalsView(alive,persisted,profile.MaximumHealth,health,profile.MaximumMagicka,magicka,
                persisted ? progress.VitalsCapturedAt : 0,persisted ? progress.VitalsRevision : 0);
        }

        // Returns false for stale, invalid, unknown, or dead captures. A failed
        // attempt never changes profile, skills, or stored vitals.
        public bool TryCaptureNpcVitals(string id,NpcArchetype archetype,int currentHealth,int currentMagicka,
            double capturedAt,long expectedRevision)
        {
            return TryCaptureNpcVitalsCore(id,archetype,0,0,false,currentHealth,currentMagicka,capturedAt,expectedRevision);
        }

        private bool TryCaptureNpcVitalsCore(string id,NpcArchetype archetype,int previousHealth,int previousMagicka,
            bool hasObservedPrevious,int currentHealth,int currentMagicka,double capturedAt,long expectedRevision)
        {
            bool alive;
            if(!KnownNpcIdentity(state,id,out alive) || !alive || expectedRevision<0 || !Finite(capturedAt) || capturedAt<0)
                return false;
            var current=IndexedNpcProgress(id);
            if(current!=null && current.Archetype!=archetype) return false;
            NpcBaseProfile profile;
            try { profile=NpcProfileGenerator.Generate(WorldSeed,id,archetype,current==null ? 1 : current.ProfileVersion); }
            catch(ArgumentException) { return false; }
            if(currentHealth<1 || currentHealth>profile.MaximumHealth || currentMagicka<0 || currentMagicka>profile.MaximumMagicka)
                return false;
            if(hasObservedPrevious && (previousHealth<1 || previousHealth>profile.MaximumHealth ||
                previousMagicka<0 || previousMagicka>profile.MaximumMagicka)) return false;
            long revision=current==null ? 0 : current.VitalsRevision;
            if(revision!=expectedRevision) return false;
            if(current!=null && current.VitalsInitialized && capturedAt<current.VitalsCapturedAt) return false;
            if(hasObservedPrevious)
            {
                bool priorMatches=current!=null && current.VitalsInitialized
                    ? current.CurrentHealth==previousHealth && current.CurrentMagicka==previousMagicka
                    : previousHealth==profile.MaximumHealth && previousMagicka==profile.MaximumMagicka;
                if(!priorMatches) return false;
            }
            if(current!=null && current.VitalsInitialized && current.CurrentHealth==currentHealth && current.CurrentMagicka==currentMagicka)
            {
                if(capturedAt>current.VitalsCapturedAt)
                {
                    var observed=CopyNpcProgress(current);
                    observed.VitalsCapturedAt=capturedAt;
                    state.NpcActors[state.NpcActors.IndexOf(current)]=observed;
                    npcActorIndex[id]=observed;
                }
                return true;
            }
            if((current==null || !current.VitalsInitialized) && currentHealth==profile.MaximumHealth && currentMagicka==profile.MaximumMagicka)
                return true;
            if(revision>=NpcSkillProgression.MaximumRawExperience) return false;

            var proposed=current==null ? new NpcActorProgress {PersonId=id,Archetype=archetype} : CopyNpcProgress(current);
            int observedPreviousHealth=current!=null && current.VitalsInitialized?current.CurrentHealth:previousHealth;
            bool healthChanged=current!=null && current.VitalsInitialized
                ? current.CurrentHealth!=currentHealth
                : hasObservedPrevious && previousHealth!=currentHealth;
            long nextRevision=revision+1;
            if(healthChanged)
            {
                int difference=observedPreviousHealth-currentHealth;
                if(!proposed.HasPendingHealthEpisode)
                {
                    proposed.HasPendingHealthEpisode=true;
                    proposed.HealthEpisodeStartedAt=capturedAt;
                    proposed.HealthEpisodeInitialHealth=observedPreviousHealth;
                    proposed.HealthEpisodeMaximumHealth=profile.MaximumHealth;
                    proposed.HealthEpisodeChangeCount=1;
                    proposed.HealthEpisodeFirstVitalsRevision=nextRevision;
                }
                else
                {
                    proposed.HealthEpisodeChangeCount++;
                    if(proposed.HealthEpisodeChangeCount<=1) proposed.HealthEpisodeChangeCount=1;
                }
                if(difference>0) proposed.HealthEpisodeDamageTotal+=difference;
                else proposed.HealthEpisodeHealingTotal-=difference;
                proposed.HealthEpisodeFinalHealth=currentHealth;
                proposed.HealthEpisodeLastChangedAt=capturedAt;
                proposed.HealthEpisodeLastVitalsRevision=nextRevision;
            }
            proposed.VitalsInitialized=true;
            proposed.CurrentHealth=currentHealth;
            proposed.CurrentMagicka=currentMagicka;
            proposed.VitalsCapturedAt=capturedAt;
            proposed.VitalsRevision=nextRevision;
            if(current==null) state.NpcActors.Add(proposed);
            else state.NpcActors[state.NpcActors.IndexOf(current)]=proposed;
            npcActorIndex[id]=proposed;
            return true;
        }

        public bool TryCaptureNpcVitals(object expectedEpoch,string id,NpcArchetype archetype,int currentHealth,int currentMagicka,
            double capturedAt,long expectedRevision)
        {
            if(!IsNpcVitalsWriteEpochCurrent(expectedEpoch))return false;
            return TryCaptureNpcVitals(id,archetype,currentHealth,currentMagicka,capturedAt,expectedRevision);
        }

        /// <summary>Captures a changed sample against the exact prior values retained by its native binding.</summary>
        public bool TryCaptureObservedNpcVitalsChange(object expectedEpoch,string id,NpcArchetype archetype,
            int previousHealth,int previousMagicka,int currentHealth,int currentMagicka,double capturedAt,long expectedRevision)
        {
            if(!IsNpcVitalsWriteEpochCurrent(expectedEpoch))return false;
            return TryCaptureNpcVitalsCore(id,archetype,previousHealth,previousMagicka,true,
                currentHealth,currentMagicka,capturedAt,expectedRevision);
        }

        public bool TryCaptureObservedPlayerHealing(object expectedEpoch,string id,NpcArchetype archetype,
            int previousHealth,int previousMagicka,int currentHealth,int currentMagicka,double capturedAt,long expectedRevision)
        {
            if(!IsNpcVitalsWriteEpochCurrent(expectedEpoch) || !Finite(capturedAt) || capturedAt<0 ||
                currentHealth<=previousHealth)return false;
            NpcVitalsView baseline;
            try { baseline=ReadNpcVitals(id,archetype); }
            catch(ArgumentException) {return false;}
            if(baseline==null || !baseline.IsAlive || !baseline.IsPersisted || baseline.Revision!=expectedRevision ||
                baseline.CurrentHealth!=previousHealth || baseline.CurrentMagicka!=previousMagicka ||
                currentHealth>baseline.MaximumHealth || currentMagicka<0 || currentMagicka>baseline.MaximumMagicka ||
                capturedAt<baseline.CapturedAt)return false;

            // Seal earlier damage/self-healing separately; a player's observed
            // restoration must not inherit responsibility for those changes.
            if(IndexedNpcProgress(id).HasPendingHealthEpisode)
            {
                FlushNpcHealthExperiencesCore(capturedAt,0,1,id,false);
                if(IndexedNpcProgress(id).HasPendingHealthEpisode)return false;
            }
            if(!TryCaptureObservedNpcVitalsChange(expectedEpoch,id,archetype,previousHealth,previousMagicka,
                currentHealth,currentMagicka,capturedAt,expectedRevision))return false;
            return FlushNpcHealthExperiencesCore(capturedAt,0,1,id,true).Count==1;
        }

        /// <summary>Emits quiet native health episodes once. This method never calls a model.</summary>
        public List<NpcHealthEpisodeSummary> FlushNpcHealthExperiences(double now,double quietWindowSeconds,int limit=100)
        {return FlushNpcHealthExperiencesCore(now,quietWindowSeconds,limit,null,false);}

        private List<NpcHealthEpisodeSummary> FlushNpcHealthExperiencesCore(double now,double quietWindowSeconds,
            int limit,string observerId,bool playerHealing)
        {
            if(!Finite(now)||now<0) throw new ArgumentOutOfRangeException(nameof(now));
            if(!Finite(quietWindowSeconds)||quietWindowSeconds<0) throw new ArgumentOutOfRangeException(nameof(quietWindowSeconds));
            if(limit<1||limit>100) throw new ArgumentOutOfRangeException(nameof(limit));

            var flushed=new List<NpcHealthEpisodeSummary>();
            if(state.NpcActors.Count>0) IndexedNpcProgress(state.NpcActors[0].PersonId);
            var eligible=state.NpcActors.Where(x=>x!=null&&x.HasPendingHealthEpisode&&
                    (observerId==null || x.PersonId==observerId)&&
                    now>=x.HealthEpisodeLastChangedAt&&now-x.HealthEpisodeLastChangedAt>=quietWindowSeconds)
                .OrderBy(x=>x.HealthEpisodeLastChangedAt).ThenBy(x=>x.PersonId,StringComparer.Ordinal).Take(limit).ToList();
            foreach(var current in eligible)
            {
                if(current.HealthEpisodeFirstVitalsRevision<=current.HealthExperienceProjectedThroughVitalsRevision ||
                    current.HealthEpisodeLastVitalsRevision>current.VitalsRevision) continue;
                // All fallible actor copying and lookups precede the inbox write. The
                // remaining two updates replace already-validated, existing slots.
                int index=state.NpcActors.IndexOf(current);
                NpcActorProgress indexed;
                if(index<0 || !npcActorIndex.TryGetValue(current.PersonId,out indexed) ||
                    !ReferenceEquals(indexed,current)) continue;
                var replacement=CopyNpcProgress(current);
                ClearHealthEpisode(replacement);
                replacement.HealthExperienceProjectedThroughVitalsRevision=current.HealthEpisodeLastVitalsRevision;
                string episodeId=HealthEpisodeIdentity("health-episode",current);
                string outcomeId=HealthEpisodeIdentity("health-outcome",current);
                string receiptId=HealthEpisodeIdentity("health-receipt",current);
                var summary=new NpcHealthEpisodeSummary(current.PersonId,episodeId,outcomeId,
                    current.HealthEpisodeStartedAt,current.HealthEpisodeLastChangedAt,
                    current.HealthEpisodeInitialHealth,current.HealthEpisodeFinalHealth,current.HealthEpisodeMaximumHealth,
                    current.HealthEpisodeDamageTotal,current.HealthEpisodeHealingTotal,current.HealthEpisodeChangeCount,
                    current.HealthEpisodeFirstVitalsRevision,current.HealthEpisodeLastVitalsRevision);
                NpcExperienceEvent evidence;
                try
                {
                    var details=NpcExperienceImpactDetails.ForHealthEpisode(summary.InitialHealth,summary.FinalHealth,
                        summary.MaximumHealth,summary.DamageTotal,summary.HealingTotal,summary.ChangeCount);
                    bool knownHealer=playerHealing && summary.DamageTotal==0 && summary.HealingTotal>0 && summary.ChangeCount==1;
                    evidence=NpcExperienceEvent.FromNativeOutcome(summary.ObserverId,summary.ObserverId,receiptId,
                        summary.EpisodeId,summary.OutcomeId,summary.DamageTotal>0?NpcExperienceEventKind.Injury:
                        NpcExperienceEventKind.Healing,summary.LastChangedAt,knownHealer?NpcResponsibleActorAttribution.Known:
                        NpcResponsibleActorAttribution.Unknown,knownHealer?NpcDirectedRelationship.PlayerTargetId:null,details);
                }
                catch(ArgumentException)
                {
                    // Keep the native episode for a later policy or operator decision.
                    continue;
                }

                bool queued=TryEnqueueVerifiedNativeExperience(evidence,
                    current.HealthEpisodeLastVitalsRevision);
                if(!queued)
                {
                    // Reconcile an older saved partial projection only with the exact
                    // canonical source, never a matching deduplication key alone.
                    if(!ExactHealthExperienceOutcomeExists(evidence)) continue;
                }
                state.NpcActors[index]=replacement;
                npcActorIndex[replacement.PersonId]=replacement;
                flushed.Add(summary);
            }
            return flushed;
        }

        private bool ExactHealthExperienceOutcomeExists(NpcExperienceEvent evidence)
        {
            try
            {
                var inbox=NpcExperienceInbox.CopyValidated(state.NpcExperienceInbox,IsKnownNpcExperienceIdentity);
                var canonical=NpcExperienceInboxEntry.FromNativeEvent(evidence);
                foreach(var saved in inbox.Entries)
                    if(saved.ObserverId==evidence.ObserverId &&
                        saved.DeduplicationKey==evidence.DeduplicationKey &&
                        SameEvidence(saved,evidence) &&
                        saved.ImpactDetailsKind==canonical.ImpactDetailsKind &&
                        saved.HealthInitial==canonical.HealthInitial &&
                        saved.HealthFinal==canonical.HealthFinal &&
                        saved.HealthMaximum==canonical.HealthMaximum &&
                        saved.HealthDamageTotal==canonical.HealthDamageTotal &&
                        saved.HealthHealingTotal==canonical.HealthHealingTotal &&
                        saved.HealthChangeCount==canonical.HealthChangeCount &&
                        saved.AlreadyAppliedEffectsMask==canonical.AlreadyAppliedEffectsMask)
                        return true;
            }
            catch(ArgumentException) { }
            catch(InvalidOperationException) { }
            return false;
        }

        private static void ClearHealthEpisode(NpcActorProgress value)
        {
            value.HasPendingHealthEpisode=false;
            value.HealthEpisodeStartedAt=0; value.HealthEpisodeLastChangedAt=0;
            value.HealthEpisodeInitialHealth=0; value.HealthEpisodeFinalHealth=0; value.HealthEpisodeMaximumHealth=0;
            value.HealthEpisodeDamageTotal=0; value.HealthEpisodeHealingTotal=0; value.HealthEpisodeChangeCount=0;
            value.HealthEpisodeFirstVitalsRevision=0; value.HealthEpisodeLastVitalsRevision=0;
        }

        private static string HealthEpisodeIdentity(string prefix,NpcActorProgress value)
        {
            string material=value.PersonId+"|"+value.HealthEpisodeFirstVitalsRevision.ToString(CultureInfo.InvariantCulture)+"|"+
                value.HealthEpisodeLastVitalsRevision.ToString(CultureInfo.InvariantCulture)+"|"+
                value.HealthEpisodeInitialHealth.ToString(CultureInfo.InvariantCulture)+"|"+
                value.HealthEpisodeFinalHealth.ToString(CultureInfo.InvariantCulture)+"|"+
                value.HealthEpisodeDamageTotal.ToString(CultureInfo.InvariantCulture)+"|"+
                value.HealthEpisodeHealingTotal.ToString(CultureInfo.InvariantCulture);
            ulong hash=14695981039346656037UL;
            for(int i=0;i<material.Length;i++) { hash^=material[i]; hash*=1099511628211UL; }
            return prefix+"-"+hash.ToString("x16",CultureInfo.InvariantCulture);
        }
    }
}
