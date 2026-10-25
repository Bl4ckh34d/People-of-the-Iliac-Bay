using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcInteractionProjectionDisposition { Pending = 0, Recorded = 1, Unavailable = 2 }

    /// <summary>Trusted native adapter observation of two visible, nearby residents in one loaded space.</summary>
    [Serializable]
    public sealed class NpcInteractionPhysicalEvidence
    {
        public string ActionId, SourcePersonId, TargetPersonId, SourceSpaceId, TargetSpaceId;
        public long ActionRevision;
        public double ObservedGameTime, SourceX, SourceY, SourceZ, TargetX, TargetY, TargetZ;
        public bool MutualVisibility;

        public static NpcInteractionPhysicalEvidence Create(string actionId, long actionRevision,
            string sourcePersonId, string targetPersonId, double observedGameTime,
            string sourceSpaceId, double sourceX, double sourceY, double sourceZ,
            string targetSpaceId, double targetX, double targetY, double targetZ, bool mutualVisibility)
        {
            return new NpcInteractionPhysicalEvidence { ActionId=actionId, ActionRevision=actionRevision,
                SourcePersonId=sourcePersonId, TargetPersonId=targetPersonId, ObservedGameTime=observedGameTime,
                SourceSpaceId=sourceSpaceId, SourceX=sourceX, SourceY=sourceY, SourceZ=sourceZ,
                TargetSpaceId=targetSpaceId, TargetX=targetX, TargetY=targetY, TargetZ=targetZ,
                MutualVisibility=mutualVisibility };
        }

        internal static NpcInteractionPhysicalEvidence CopyFields(NpcInteractionPhysicalEvidence source)
        { return source==null?null:Create(source.ActionId,source.ActionRevision,source.SourcePersonId,
            source.TargetPersonId,source.ObservedGameTime,source.SourceSpaceId,source.SourceX,source.SourceY,
            source.SourceZ,source.TargetSpaceId,source.TargetX,source.TargetY,source.TargetZ,source.MutualVisibility); }

        internal static bool ValidForCompletedAction(NpcInteractionPhysicalEvidence value,NpcActionJob job)
        {
            if(value==null || job==null || value.ActionId!=job.Id || value.SourcePersonId!=job.ResidentId ||
                job.Target==null || value.TargetPersonId!=job.Target.PersonId || value.SourcePersonId==value.TargetPersonId ||
                value.ActionRevision<1 || value.ActionRevision>job.Revision ||
                !Number(value.ObservedGameTime) || value.ObservedGameTime<job.AcceptedAt ||
                value.ObservedGameTime>=job.DeadlineAt ||
                !value.MutualVisibility || string.IsNullOrWhiteSpace(value.SourceSpaceId) ||
                value.SourceSpaceId!=value.TargetSpaceId || value.SourceSpaceId.Length>240 ||
                !Number(value.SourceX) || !Number(value.SourceY) || !Number(value.SourceZ) ||
                !Number(value.TargetX) || !Number(value.TargetY) || !Number(value.TargetZ))return false;
            double dx=value.SourceX-value.TargetX,dy=value.SourceY-value.TargetY,dz=value.SourceZ-value.TargetZ;
            return dx*dx+dy*dy+dz*dz<=1.35*1.35;
        }

        private static bool Number(double value)
        { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }

    public sealed partial class WorldStateStore
    {
        private int nextNpcInteractionProjectionIndex;

        public bool TryRecordNpcInteractionMemory(string id,long expectedRevision,double now,string date,string place,
            out NpcActionJob updated)
        { updated=null; return false; }

        public bool TryRecordNpcInteractionMemory(string id,long expectedRevision,double now,string date,string place,
            NpcInteractionPhysicalEvidence physicalEvidence,out NpcActionJob updated)
        {
            updated=null;
            int index=state.NpcActions.FindIndex(job=>job.Id==id);
            if(index<0 || !Finite(now) || physicalEvidence==null) return false;
            var current=state.NpcActions[index];
            if(current.Kind!=NpcActionKind.Interact || current.Phase!=NpcActionPhase.Completed ||
                current.CompletedEffectId!=id+":observed" || current.InteractionMemoryRecorded ||
                current.InteractionProjectionDisposition!=NpcInteractionProjectionDisposition.Pending || current.Target==null ||
                current.Target.Kind!=NpcActionTargetKind.Person || string.IsNullOrWhiteSpace(current.Target.PersonId) ||
                current.Target.PersonId==current.ResidentId || current.Target.PersonId=="player") return false;
            var resident=ResolveResident(current.ResidentId);
            var target=ResolveResident(current.Target.PersonId);
            if(!EligibleForInteractionMemory(resident) || !EligibleForInteractionMemory(target)) return false;
            if(current.Revision!=expectedRevision || current.Revision==long.MaxValue || now<current.UpdatedAt ||
                now>=current.DeadlineAt || string.IsNullOrWhiteSpace(current.ResidentId) || current.ResidentId.Length>240 ||
                string.IsNullOrWhiteSpace(current.Target.PersonId) || current.Target.PersonId.Length>240 ||
                string.IsNullOrWhiteSpace(date) || string.IsNullOrWhiteSpace(place) ||
                physicalEvidence.ActionRevision!=expectedRevision ||
                BitConverter.DoubleToInt64Bits(physicalEvidence.ObservedGameTime)!=BitConverter.DoubleToInt64Bits(now) ||
                !NpcInteractionPhysicalEvidence.ValidForCompletedAction(physicalEvidence,current)) return false;

            string otherId=current.Target.PersonId;
            string residentLabel=InteractionMemoryLabel(resident),targetLabel=InteractionMemoryLabel(target);
            string cleanDate=CleanMemory(date,120),cleanPlace=CleanMemory(place,300);
            if(string.IsNullOrWhiteSpace(cleanDate) || string.IsNullOrWhiteSpace(cleanPlace))return false;
            var first=new NpcMemoryEntry {At=Math.Floor(now),Date=cleanDate,Place=cleanPlace,
                PlayerText="",NpcText="",Observation=CleanMemory("I physically approached and faced "+targetLabel+
                    " here. No spoken exchange was observed.",600),Source="observed",SourceActionId=id};
            var second=new NpcMemoryEntry {At=Math.Floor(now),Date=cleanDate,Place=cleanPlace,
                PlayerText="",NpcText="",Observation=CleanMemory(residentLabel+
                    " physically approached and faced me here. No spoken exchange was observed.",600),Source="observed",SourceActionId=id};
            try { ValidateMemoryEntry(first);ValidateMemoryEntry(second); }
            catch(ArgumentException) {return false;}

            // Stage both pending journals, their complete indexed views, and the action before publication.
            // No callback or validation follows the final assignments.
            List<NpcConversationMemory> pending;
            Dictionary<string,NpcConversationMemory> preparedIndex;
            NpcActionJob marked;
            List<NpcActionJob> actions;
            NpcActionJob detached;
            try
            {
                var firstJournal=MemoryFor(current.ResidentId,false);
                var secondJournal=MemoryFor(otherId,false);
                if(HasActionSource(firstJournal,id) || HasActionSource(secondJournal,id))return false;
                pending=CopyConversationMemories(state);
                AppendPending(pending,current.ResidentId,first);
                AppendPending(pending,otherId,second);
                preparedIndex=new Dictionary<string,NpcConversationMemory>(memoryIndex,StringComparer.Ordinal);
                AppendIndexed(preparedIndex,current.ResidentId,first);
                AppendIndexed(preparedIndex,otherId,second);
                marked=NpcActionExecutor.Copy(current);
                marked.InteractionMemoryRecorded=true;
                marked.InteractionProjectionDisposition=NpcInteractionProjectionDisposition.Recorded;
                marked.InteractionPhysicalEvidence=NpcInteractionPhysicalEvidence.CopyFields(physicalEvidence);
                NpcActionExecutor.Validate(marked);
                actions=new List<NpcActionJob>(state.NpcActions);actions[index]=marked;
                detached=NpcActionExecutor.Copy(marked);
            }
            catch(ArgumentException) {return false;}
            state.ConversationMemories=pending;
            pendingConversationMemoryEpoch = new object();
            state.NpcActions=actions;
            memoryIndex=preparedIndex;
            memoryIndexOwner=state;
            npcActionIndexOwner=null;
            updated=detached;
            return true;
        }

        public bool TryMarkNpcInteractionProjectionUnavailable(string id,long expectedRevision,double now,
            string reason,out NpcActionJob updated)
        {
            updated=null;
            if(!Finite(now) || string.IsNullOrWhiteSpace(reason) || reason.Length>240)return false;
            int index=state.NpcActions.FindIndex(job=>job.Id==id);
            if(index<0)return false;
            var current=state.NpcActions[index];
            if(current.Kind!=NpcActionKind.Interact || !NpcActionExecutor.IsTerminal(current) ||
                current.InteractionMemoryRecorded || current.InteractionProjectionDisposition!=NpcInteractionProjectionDisposition.Pending ||
                current.Revision!=expectedRevision || now<current.DeadlineAt)return false;
            NpcActionJob marked,detached;
            List<NpcActionJob> actions;
            try
            {
                marked=NpcActionExecutor.Copy(current);
                marked.InteractionProjectionDisposition=NpcInteractionProjectionDisposition.Unavailable;
                marked.InteractionProjectionReason=reason;
                NpcActionExecutor.Validate(marked);
                actions=new List<NpcActionJob>(state.NpcActions);actions[index]=marked;
                detached=NpcActionExecutor.Copy(marked);
            }
            catch(ArgumentException) {return false;}
            state.NpcActions=actions;
            npcActionIndexOwner=null;
            updated=detached;
            return true;
        }

        public int FinalizeUnavailableNpcInteractions(double now,int maximumExamined)
        {
            if(!Finite(now) || maximumExamined<1 || state.NpcActions.Count==0)return 0;
            int examined=0,marked=0,count=state.NpcActions.Count;
            int budget=Math.Min(Math.Min(maximumExamined,64),count);
            while(examined<budget)
            {
                int index=nextNpcInteractionProjectionIndex%count;
                nextNpcInteractionProjectionIndex=(index+1)%count;
                examined++;
                var job=state.NpcActions[index];
                if(job.Kind==NpcActionKind.Interact && NpcActionExecutor.IsTerminal(job) &&
                    job.InteractionProjectionDisposition==NpcInteractionProjectionDisposition.Pending &&
                    now>=job.DeadlineAt && TryMarkNpcInteractionProjectionUnavailable(job.Id,job.Revision,now,
                        "The encounter ended without retained physical meeting evidence.",out _))marked++;
            }
            return marked;
        }

        private static bool HasActionSource(NpcConversationMemory journal,string actionId)
        { return journal!=null && journal.Entries.Exists(entry=>entry.SourceActionId==actionId); }

        private static void AppendPending(List<NpcConversationMemory> pending,string personId,NpcMemoryEntry entry)
        {
            var journal=pending.Find(item=>item.PersonId==personId);
            if(journal==null){journal=new NpcConversationMemory {PersonId=personId};pending.Add(journal);}
            journal.Entries.Add(entry);
        }

        private static void AppendIndexed(Dictionary<string,NpcConversationMemory> index,string personId,NpcMemoryEntry entry)
        {
            NpcConversationMemory prior;
            index.TryGetValue(personId,out prior);
            var next=new NpcConversationMemory {PersonId=personId};
            if(prior!=null)next.Entries.AddRange(prior.Entries);
            next.Entries.Add(entry);
            index[personId]=next;
        }

        private static bool EligibleForInteractionMemory(Resident resident)
        {
            return resident!=null && resident.IsAlive && !resident.IsQuestProtected && resident.AgeGroup==ResidentAgeGroup.Adult;
        }

        private static string InteractionMemoryLabel(Resident resident)
        {
            string name=CleanMemory(resident.FullName,240);
            return string.IsNullOrWhiteSpace(name)?"person "+resident.Id:name+" (person "+resident.Id+")";
        }
    }
}
