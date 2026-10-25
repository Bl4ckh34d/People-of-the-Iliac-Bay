using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    [Serializable] public sealed class NpcSkillExperience {public int Skill;public long Experience;}
    [Serializable] public sealed class NpcActorProgress
    {
        public string PersonId;
        public int ProfileVersion=1;
        public NpcArchetype Archetype;
        public long Revision;
        public bool VitalsInitialized;
        public int CurrentHealth;
        public int CurrentMagicka;
        public double VitalsCapturedAt;
        public long VitalsRevision;
        public string NativeConditions;
        // Opaque native SaveLoadManager payload for an ordinary actor's exact spellbook.
        public string NativeSpellbook;
        public NpcTreatmentVisit TreatmentVisit;
        public bool HasPendingHealthEpisode;
        public double HealthEpisodeStartedAt, HealthEpisodeLastChangedAt;
        public int HealthEpisodeInitialHealth, HealthEpisodeFinalHealth, HealthEpisodeMaximumHealth;
        public long HealthEpisodeDamageTotal, HealthEpisodeHealingTotal;
        public int HealthEpisodeChangeCount;
        public long HealthEpisodeFirstVitalsRevision, HealthEpisodeLastVitalsRevision;
        // Last health episode end revision whose native outcome was verified in the inbox.
        // This source cursor survives retirement of the corresponding inbox detail.
        public long HealthExperienceProjectedThroughVitalsRevision;
        public List<NpcSkillExperience> Skills=new List<NpcSkillExperience>();
    }
    public sealed class NpcActorView
    {
        private readonly int[] skills;
        public NpcBaseProfile Profile {get;private set;}
        public long Revision {get;private set;}
        internal NpcActorView(NpcBaseProfile profile,int[] skills,long revision) {Profile=profile;this.skills=skills;Revision=revision;}
        public int Skill(int index) {if(index<0 || index>=35) throw new ArgumentOutOfRangeException(nameof(index));return skills[index];}
    }
    public sealed partial class WorldStateStore
    {
        public NpcActorView ReadNpcActor(string id,NpcArchetype archetype)
        {
            // Materialization/revision-change API: one identity lookup and one
            // seeded profile, rather than 35 separate profile regenerations.
            var profile=ReadNpcProfile(id,archetype);var progress=IndexedNpcProgress(id);
            var experience=new long[35];if(progress!=null) foreach(var skill in progress.Skills) experience[skill.Skill]=skill.Experience;
            var skills=new int[35];for(int i=0;i<35;i++) skills[i]=NpcSkillProgression.Value(profile.BaseSkill(i),experience[i],profile.TrainingRatePermille(i));
            return new NpcActorView(profile,skills,progress==null?0:progress.Revision);
        }
        private WorldState npcActorIndexOwner;
        private Dictionary<string,NpcActorProgress> npcActorIndex;
        private NpcActorProgress IndexedNpcProgress(string id)
        {
            if(npcActorIndexOwner!=state)
            {npcActorIndex=new Dictionary<string,NpcActorProgress>(StringComparer.Ordinal);foreach(var p in state.NpcActors)npcActorIndex.Add(p.PersonId,p);npcActorIndexOwner=state;}
            NpcActorProgress found;return id!=null && npcActorIndex.TryGetValue(id,out found)?found:null;
        }
        public NpcBaseProfile ReadNpcProfile(string id,NpcArchetype archetype)
        {
            bool alive;Require(KnownNpcIdentity(state,id,out alive),"Unknown NPC profile identity.");
            var saved=IndexedNpcProgress(id);
            if(saved!=null) Require(saved.Archetype==archetype,"NPC archetype is already pinned.");
            return NpcProfileGenerator.Generate(WorldSeed,id,archetype,saved==null?1:saved.ProfileVersion);
        }
        public int ReadNpcSkill(string id,NpcArchetype archetype,int skill)
        {
            var profile=ReadNpcProfile(id,archetype);int baseline=profile.BaseSkill(skill);long experience=0;
            var progress=IndexedNpcProgress(id);
            if(progress!=null) {var trained=progress.Skills.Find(s=>s.Skill==skill);if(trained!=null)experience=trained.Experience;}
            return NpcSkillProgression.Value(baseline,experience,profile.TrainingRatePermille(skill));
        }
        public NpcActorProgress ReadNpcProgress(string id)
        {var found=IndexedNpcProgress(id);return found==null?null:CopyNpcProgress(found);}

        // Native DFU owns the condition data and its timers; Core only retains its save packet.
        public bool TryCaptureNpcConditions(object epoch,string id,string nativeConditions)
        {
            if(!IsNpcVitalsWriteEpochCurrent(epoch) || nativeConditions!=null &&
                (nativeConditions.Length==0 || nativeConditions.Length>131072))return false;
            var resident=ResolveResident(id);
            var current=IndexedNpcProgress(id);
            if(resident==null || !resident.IsAlive || resident.IsQuestProtected ||
                resident.AgeGroup!=ResidentAgeGroup.Adult)return false;
            if(current!=null && current.NativeConditions==nativeConditions)return true;
            if((current==null || !current.VitalsInitialized) && nativeConditions==null)return false;
            var copy=current==null?new NpcActorProgress {PersonId=id,Archetype=ResolveNpcArchetype(id)}:CopyNpcProgress(current);
            // Unchanged full resources normally remain seed-only. An active native condition needs a saved owner.
            if(!copy.VitalsInitialized)
            {
                var baseline=ReadNpcVitals(id,copy.Archetype);
                copy.VitalsInitialized=true;copy.CurrentHealth=baseline.CurrentHealth;copy.CurrentMagicka=baseline.CurrentMagicka;
                copy.VitalsRevision=1;
            }
            copy.NativeConditions=nativeConditions;
            if(current==null)state.NpcActors.Add(copy);else state.NpcActors[state.NpcActors.IndexOf(current)]=copy;
            npcActorIndex[id]=copy;return true;
        }
        public bool TryCaptureNpcKnownSpellbook(object epoch,string id,string nativeSpellbook)
        {
            if(!IsNpcVitalsWriteEpochCurrent(epoch) || string.IsNullOrWhiteSpace(nativeSpellbook) ||
                nativeSpellbook.Length>262144)return false;
            var resident=ResolveResident(id);var current=IndexedNpcProgress(id);
            if(resident==null || !resident.IsAlive || resident.IsQuestProtected ||
                resident.AgeGroup!=ResidentAgeGroup.Adult)return false;
            if(current!=null && current.NativeSpellbook==nativeSpellbook)return true;
            var copy=current==null?new NpcActorProgress {PersonId=id,Archetype=ResolveNpcArchetype(id)}:CopyNpcProgress(current);
            if(!copy.VitalsInitialized)
            {
                var baseline=ReadNpcVitals(id,copy.Archetype);
                copy.VitalsInitialized=true;copy.CurrentHealth=baseline.CurrentHealth;copy.CurrentMagicka=baseline.CurrentMagicka;
                copy.VitalsRevision=1;
            }
            copy.NativeSpellbook=nativeSpellbook;
            if(current==null)state.NpcActors.Add(copy);else state.NpcActors[state.NpcActors.IndexOf(current)]=copy;
            npcActorIndex[id]=copy;return true;
        }
        public bool AwardNpcSkillExperience(string id,NpcArchetype archetype,int skill,long amount,long expectedRevision)
        {
            Require(skill>=0 && skill<35 && amount>0 && amount<=NpcSkillProgression.MaximumRawExperience && expectedRevision>=0,
                "Invalid NPC experience award.");
            bool alive;if(!KnownNpcIdentity(state,id,out alive) || !alive) return false;
            var current=IndexedNpcProgress(id);
            if((current==null?0:current.Revision)!=expectedRevision || current!=null && current.Archetype!=archetype) return false;
            // Validate the complete proposal before changing either ledger/index.
            var proposed=current==null ? new NpcActorProgress {PersonId=id,Archetype=archetype} : CopyNpcProgress(current);
            NpcProfileGenerator.Generate(WorldSeed,id,archetype,proposed.ProfileVersion);
            var trained=proposed.Skills.Find(s=>s.Skill==skill);
            long previous=trained==null?0:trained.Experience;
            if(amount>NpcSkillProgression.MaximumRawExperience-previous || proposed.Revision>=NpcSkillProgression.MaximumRawExperience) return false;
            if(trained==null) {trained=new NpcSkillExperience {Skill=skill};proposed.Skills.Add(trained);}
            trained.Experience=previous+amount;proposed.Revision++;
            if(current==null) state.NpcActors.Add(proposed);else state.NpcActors[state.NpcActors.IndexOf(current)]=proposed;
            npcActorIndex[id]=proposed;return true;
        }
        private static List<NpcActorProgress> CopyNpcActors(WorldState source,WorldState validated)
        {
            Require(source.NpcActors!=null,"Missing NPC progression ledger.");
            var result=new List<NpcActorProgress>();var ids=new HashSet<string>(StringComparer.Ordinal);
            var known=new HashSet<string>(StringComparer.Ordinal);
            foreach(var town in validated.Settlements) foreach(var home in town.Households) foreach(var person in home.Residents) known.Add(person.Id);
            foreach(var old in validated.HouseholdHistory) foreach(var person in old.Household.Residents) known.Add(person.Id);
            foreach(var person in validated.NativePeople) known.Add(person.Id);
            foreach(var person in validated.Travelers) known.Add(person.Id);
            foreach(var entry in source.NpcActors)
            {
                if(entry!=null)ValidateTreatmentVisit(entry.TreatmentVisit);
                Require(entry!=null && (entry.NativeConditions==null || entry.VitalsInitialized &&
                    entry.NativeConditions.Length>0 && entry.NativeConditions.Length<=131072) &&
                    (entry.NativeSpellbook==null || entry.VitalsInitialized &&
                    entry.NativeSpellbook.Length>0 && entry.NativeSpellbook.Length<=262144),"Invalid native NPC packet.");
                Require(entry!=null && !string.IsNullOrWhiteSpace(entry.PersonId) && entry.PersonId.Length<=240 && ids.Add(entry.PersonId) &&
                    known.Contains(entry.PersonId),"Unknown or duplicate NPC progression identity.");
                Require(entry.ProfileVersion==1 && (int)entry.Archetype>=0 && (int)entry.Archetype<=7 && entry.Revision>=0 &&
                    entry.Revision<=NpcSkillProgression.MaximumRawExperience && entry.Skills!=null && entry.Skills.Count<=35 &&
                    (entry.Skills.Count==0 ? entry.Revision==0 : entry.Revision>0 && entry.Revision>=entry.Skills.Count) &&
                    (entry.Skills.Count>0 || entry.VitalsInitialized) && entry.VitalsRevision>=0 &&
                    entry.VitalsRevision<=NpcSkillProgression.MaximumRawExperience &&
                    (entry.VitalsInitialized ? entry.VitalsRevision>0 && entry.CurrentHealth>0 && entry.CurrentMagicka>=0 && Finite(entry.VitalsCapturedAt) && entry.VitalsCapturedAt>=0 :
                        entry.VitalsRevision==0 && entry.CurrentHealth==0 && entry.CurrentMagicka==0 && entry.VitalsCapturedAt==0),
                    "Invalid NPC progression or vitals record.");
                bool hasEpisodeFields = entry.HasPendingHealthEpisode || entry.HealthEpisodeStartedAt != 0 ||
                    entry.HealthEpisodeLastChangedAt != 0 || entry.HealthEpisodeInitialHealth != 0 ||
                    entry.HealthEpisodeFinalHealth != 0 || entry.HealthEpisodeMaximumHealth != 0 ||
                    entry.HealthEpisodeDamageTotal != 0 || entry.HealthEpisodeHealingTotal != 0 ||
                    entry.HealthEpisodeChangeCount != 0 || entry.HealthEpisodeFirstVitalsRevision != 0 ||
                    entry.HealthEpisodeLastVitalsRevision != 0;
                if(source.Version < 38)
                    Require(!hasEpisodeFields,"Legacy NPC vitals cannot contain a pending health episode.");
                if(source.Version < 49)
                    Require(entry.HealthExperienceProjectedThroughVitalsRevision==0,
                        "Legacy NPC progression cannot supply a health projection cursor.");
                Require(entry.HealthExperienceProjectedThroughVitalsRevision>=0 &&
                    entry.HealthExperienceProjectedThroughVitalsRevision<=entry.VitalsRevision &&
                    (entry.VitalsInitialized || entry.HealthExperienceProjectedThroughVitalsRevision==0) &&
                    (!entry.HasPendingHealthEpisode ||
                        entry.HealthEpisodeFirstVitalsRevision>entry.HealthExperienceProjectedThroughVitalsRevision),
                    "Invalid NPC health projection cursor.");
                if(entry.VitalsInitialized)
                {
                    var profile=NpcProfileGenerator.Generate(validated.WorldSeed,entry.PersonId,entry.Archetype,entry.ProfileVersion);
                    Require(entry.CurrentHealth<=profile.MaximumHealth && entry.CurrentMagicka<=profile.MaximumMagicka,
                        "NPC vitals exceed seeded profile maxima.");
                    if(entry.HasPendingHealthEpisode)
                    {
                        Require(entry.HealthEpisodeMaximumHealth == profile.MaximumHealth &&
                            entry.HealthEpisodeInitialHealth >= 1 && entry.HealthEpisodeInitialHealth <= profile.MaximumHealth &&
                            entry.HealthEpisodeFinalHealth == entry.CurrentHealth &&
                            entry.HealthEpisodeFinalHealth >= 1 && entry.HealthEpisodeFinalHealth <= profile.MaximumHealth &&
                            entry.HealthEpisodeChangeCount > 0 &&
                            entry.HealthEpisodeFirstVitalsRevision > 0 &&
                            entry.HealthEpisodeFirstVitalsRevision <= entry.HealthEpisodeLastVitalsRevision &&
                            entry.HealthEpisodeLastVitalsRevision <= entry.VitalsRevision &&
                            entry.HealthEpisodeDamageTotal >= 0 && entry.HealthEpisodeHealingTotal >= 0 &&
                            entry.HealthEpisodeDamageTotal <= long.MaxValue - entry.HealthEpisodeHealingTotal &&
                            entry.HealthEpisodeDamageTotal + entry.HealthEpisodeHealingTotal > 0 &&
                            Finite(entry.HealthEpisodeStartedAt) && entry.HealthEpisodeStartedAt >= 0 &&
                            Finite(entry.HealthEpisodeLastChangedAt) &&
                            entry.HealthEpisodeLastChangedAt >= entry.HealthEpisodeStartedAt &&
                            entry.HealthEpisodeLastChangedAt <= entry.VitalsCapturedAt,
                            "Invalid pending NPC health episode.");
                        if(entry.HealthEpisodeDamageTotal < long.MaxValue && entry.HealthEpisodeHealingTotal < long.MaxValue)
                            Require((long)entry.HealthEpisodeFinalHealth - entry.HealthEpisodeInitialHealth ==
                                entry.HealthEpisodeHealingTotal - entry.HealthEpisodeDamageTotal,
                                "Pending NPC health totals do not match their endpoints.");
                    }
                    else
                        Require(!hasEpisodeFields,"Health episode fields must be empty without a pending episode.");
                }
                else
                    Require(!hasEpisodeFields,"An uninitialized vitals row cannot have a health episode.");
                var skills=new HashSet<int>();
                foreach(var trained in entry.Skills)
                    Require(trained!=null && trained.Skill>=0 && trained.Skill<35 && skills.Add(trained.Skill) && trained.Experience>0 &&
                        trained.Experience<=NpcSkillProgression.MaximumRawExperience,"Invalid or duplicate NPC skill experience.");
                result.Add(CopyNpcProgress(entry));
            }
            return result;
        }
        private static NpcActorProgress CopyNpcProgress(NpcActorProgress value)
        {
            var copy=new NpcActorProgress {PersonId=value.PersonId,ProfileVersion=value.ProfileVersion,Archetype=value.Archetype,Revision=value.Revision,
                VitalsInitialized=value.VitalsInitialized,CurrentHealth=value.CurrentHealth,CurrentMagicka=value.CurrentMagicka,
                VitalsCapturedAt=value.VitalsCapturedAt,VitalsRevision=value.VitalsRevision,NativeConditions=value.NativeConditions,
                NativeSpellbook=value.NativeSpellbook,
                TreatmentVisit=CopyTreatmentVisit(value.TreatmentVisit),
                HasPendingHealthEpisode=value.HasPendingHealthEpisode,HealthEpisodeStartedAt=value.HealthEpisodeStartedAt,
                HealthEpisodeLastChangedAt=value.HealthEpisodeLastChangedAt,HealthEpisodeInitialHealth=value.HealthEpisodeInitialHealth,
                HealthEpisodeFinalHealth=value.HealthEpisodeFinalHealth,HealthEpisodeMaximumHealth=value.HealthEpisodeMaximumHealth,
                HealthEpisodeDamageTotal=value.HealthEpisodeDamageTotal,HealthEpisodeHealingTotal=value.HealthEpisodeHealingTotal,
                HealthEpisodeChangeCount=value.HealthEpisodeChangeCount,
                HealthEpisodeFirstVitalsRevision=value.HealthEpisodeFirstVitalsRevision,
                HealthEpisodeLastVitalsRevision=value.HealthEpisodeLastVitalsRevision,
                HealthExperienceProjectedThroughVitalsRevision=value.HealthExperienceProjectedThroughVitalsRevision};
            foreach(var skill in value.Skills) copy.Skills.Add(new NpcSkillExperience {Skill=skill.Skill,Experience=skill.Experience});
            return copy;
        }
        // Cache only inside one synchronous validation pass over an immutable candidate.
        // This is identity membership, never a cache of changing health/alive authority.
        private static bool CachedKnownNpcIdentity(WorldState world,string id,HashSet<string> known)
        {
            if(known.Contains(id))return true;
            bool alive;
            if(!KnownNpcIdentity(world,id,out alive))return false;
            known.Add(id);
            return true;
        }

        private static bool KnownNpcIdentity(WorldState world,string id,out bool alive)
        {
            alive=false;if(string.IsNullOrWhiteSpace(id))return false;
            foreach(var town in world.Settlements) foreach(var home in town.Households) foreach(var person in home.Residents)
                if(person.Id==id) {alive=person.IsAlive;return true;}
            foreach(var old in world.HouseholdHistory) foreach(var person in old.Household.Residents)
                if(person.Id==id) {alive=person.IsAlive;return true;}
            foreach(var person in world.NativePeople) if(person.Id==id) {alive=person.IsAlive;return true;}
            foreach(var person in world.Travelers) if(person.Id==id) {alive=person.IsAlive;return true;}
            return false;
        }
    }
}
