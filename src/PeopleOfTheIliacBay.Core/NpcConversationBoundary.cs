using System;

namespace PeopleOfTheIliacBay.Core
{
    public enum NpcConversationBoundaryReportStatus { None, Pending, Started, Delivered, Abandoned }

    [Serializable]
    public sealed class NpcConversationBoundaryRecord
    {
        public string PersonId, ReceiptId;
        public long Ordinal;
        public NpcExperienceSpeechActKind SpeechKind;
        public ConversationSpeechBoundaryAction Action;
        public double ObservedAt, RefuseUntil;
        public int PolicyVersion;
        public double RefusalSeconds;
        public NpcConversationBoundaryReportStatus ReportStatus;
        public bool RefusesFurtherCommunication;
        public long RepairedAtOrdinal;
        public string RepairedAtReceiptId;
        public long ParentBoundaryOrdinal;
        public string ParentBoundaryReceiptId;
        public NpcExperienceSpeechActKind ParentBoundaryKind;

        internal static NpcConversationBoundaryRecord CopyValidated(NpcConversationBoundaryRecord value,
            string personId,string scopeId)
        {
            if(value==null)return null;
            ConversationSpeechBoundaryAction expected=ActionFor(value.SpeechKind,value.PolicyVersion);
            bool escalated=value.ParentBoundaryOrdinal>0;
            bool current=value.PolicyVersion==1;
            bool validDuration=current?
                value.Action==ConversationSpeechBoundaryAction.EndAndReport?
                    value.RefusalSeconds==WorldStateStore.NpcConversationBoundaryRefusalSeconds:
                    escalated?value.RefusalSeconds>=2*86400d && value.RefusalSeconds<=7*86400d:
                        value.RefusalSeconds>=9*3600d && value.RefusalSeconds<=24*3600d:
                value.PolicyVersion==0 && value.RefusalSeconds==0;
            if(string.IsNullOrWhiteSpace(personId) || value.PersonId!=personId ||
                value.Ordinal<1 || value.Ordinal>=long.MaxValue ||
                value.ReceiptId!=NpcObservedSpeechReceipt.IssuedReceiptId(scopeId,personId,value.Ordinal) ||
                (escalated ?
                    value.ParentBoundaryOrdinal>=value.Ordinal ||
                    value.ParentBoundaryReceiptId!=NpcObservedSpeechReceipt.IssuedReceiptId(
                        scopeId,personId,value.ParentBoundaryOrdinal) ||
                    ActionFor(value.ParentBoundaryKind,value.PolicyVersion)!=ConversationSpeechBoundaryAction.EndConversation ||
                    (current?expected==ConversationSpeechBoundaryAction.Continue || value.Action!=expected:
                        value.Action!=ConversationSpeechBoundaryAction.EndAndReport) ||
                    value.SpeechKind==NpcExperienceSpeechActKind.RepairAttempt :
                    value.ParentBoundaryReceiptId!=null || value.ParentBoundaryKind!=0 ||
                    expected==ConversationSpeechBoundaryAction.Continue || value.Action!=expected) ||
                double.IsNaN(value.ObservedAt) || double.IsInfinity(value.ObservedAt) || value.ObservedAt<0 ||
                double.IsNaN(value.RefuseUntil) || double.IsInfinity(value.RefuseUntil) ||
                !validDuration ||
                value.RefuseUntil!=value.ObservedAt+(current?value.RefusalSeconds:WorldStateStore.NpcConversationBoundaryRefusalSeconds) ||
                (value.Action==ConversationSpeechBoundaryAction.EndAndReport ?
                    value.ReportStatus!=NpcConversationBoundaryReportStatus.Pending &&
                    value.ReportStatus!=NpcConversationBoundaryReportStatus.Started &&
                    value.ReportStatus!=NpcConversationBoundaryReportStatus.Delivered &&
                    value.ReportStatus!=NpcConversationBoundaryReportStatus.Abandoned :
                    value.ReportStatus!=NpcConversationBoundaryReportStatus.None) ||
                (value.Action==ConversationSpeechBoundaryAction.EndConversation ?
                    value.RefusesFurtherCommunication==false && value.RepairedAtOrdinal==0 ||
                    value.RepairedAtOrdinal>0 &&
                    (value.RepairedAtOrdinal<=value.Ordinal || value.RepairedAtReceiptId!=
                        NpcObservedSpeechReceipt.IssuedReceiptId(scopeId,personId,value.RepairedAtOrdinal) ||
                        value.RefusesFurtherCommunication) ||
                    value.RepairedAtOrdinal==0 && value.RepairedAtReceiptId!=null :
                    value.RefusesFurtherCommunication || value.RepairedAtOrdinal!=0 ||
                    value.RepairedAtReceiptId!=null))
                throw new ArgumentException("Invalid NPC conversation boundary.");
            return new NpcConversationBoundaryRecord { PersonId=value.PersonId,ReceiptId=value.ReceiptId,
                Ordinal=value.Ordinal,SpeechKind=value.SpeechKind,Action=value.Action,
                ObservedAt=value.ObservedAt,RefuseUntil=value.RefuseUntil,ReportStatus=value.ReportStatus,
                PolicyVersion=value.PolicyVersion,RefusalSeconds=value.RefusalSeconds,
                RefusesFurtherCommunication=value.RefusesFurtherCommunication,
                RepairedAtOrdinal=value.RepairedAtOrdinal,RepairedAtReceiptId=value.RepairedAtReceiptId,
                ParentBoundaryOrdinal=value.ParentBoundaryOrdinal,
                ParentBoundaryReceiptId=value.ParentBoundaryReceiptId,ParentBoundaryKind=value.ParentBoundaryKind };
        }

        internal static ConversationSpeechBoundaryAction ActionFor(NpcExperienceSpeechActKind kind,int policyVersion=1)
        {
            switch(kind)
            {
                case NpcExperienceSpeechActKind.DirectInsult:
                case NpcExperienceSpeechActKind.DirectHarassment:
                    return ConversationSpeechBoundaryAction.EndConversation;
                case NpcExperienceSpeechActKind.DirectThreat:
                    return ConversationSpeechBoundaryAction.EndAndReport;
                case NpcExperienceSpeechActKind.SevereAbuse:
                case NpcExperienceSpeechActKind.PersistentAbuse:
                    return policyVersion==0?ConversationSpeechBoundaryAction.EndAndReport:
                        ConversationSpeechBoundaryAction.EndConversation;
                default:return ConversationSpeechBoundaryAction.Continue;
            }
        }
    }

    public sealed partial class WorldStateStore
    {
        public const double NpcConversationBoundaryRefusalSeconds=1800d;

        public bool TryCommitNpcConversationBoundary(NpcObservedSpeechTicket ticket,NpcObservedSpeechReceipt receipt,
            object source,NpcSpeechActClassification classification,out NpcConversationBoundaryRecord record)
        {
            record=null;
            if(!ValidBoundarySpeech(ticket,receipt,source,classification))return false;
            ConversationSpeechBoundaryAction action=ConversationSpeechBoundaryPolicy.Decide(source,classification,
                ActiveNpcPersonality(state,ticket.PersonId));
            if(action==ConversationSpeechBoundaryAction.Continue || !classification.IsDirectHostility)return false;
            var attitude=state.ConversationAttitudes.Find(row=>row.PersonId==ticket.PersonId);
            var previous=attitude==null?null:attitude.Boundary;
            bool inherited=previous!=null && previous.Action==ConversationSpeechBoundaryAction.EndConversation &&
                previous.RefusesFurtherCommunication;
            if(ShouldEnforceNpcConversationBoundary(ticket.PersonId,ticket.ObservedGameTime)!=
                classification.Request.ListenerRefusesFurtherCommunication)return false;
            if(action!=NpcConversationBoundaryRecord.ActionFor(classification.Kind))return false;
            if(previous!=null && (ticket.Ordinal<=previous.Ordinal || ticket.ObservedGameTime<previous.ObservedAt ||
                previous.SpeechKind==NpcExperienceSpeechActKind.DirectThreat ||
                previous.Action==ConversationSpeechBoundaryAction.EndAndReport &&
                previous.ReportStatus!=NpcConversationBoundaryReportStatus.Delivered &&
                previous.ReportStatus!=NpcConversationBoundaryReportStatus.Abandoned))return false;
            var mood=state.ConversationMoods.Find(row=>row.PersonId==ticket.PersonId);
            if(mood!=null && (mood.Revision==long.MaxValue ||
                mood.HasTimeAnchor && ticket.ObservedGameTime<mood.UpdatedAt))return false;
            int projectedMood=mood==null?0:mood.HasTimeAnchor?
                MoodRecovery.ValueAt(mood.Value,mood.UpdatedAt,ticket.ObservedGameTime,AnchoredMoodHalfLife(mood)):
                mood.Value;
            var nextMood=new ConversationMood {PersonId=ticket.PersonId,
                Value=Math.Min(projectedMood,-40),
                AcceptedTurnSequence=mood==null?0:mood.AcceptedTurnSequence,
                HasTimeAnchor=true,UpdatedAt=ticket.ObservedGameTime,
                Revision=mood==null?1:mood.Revision+1,
                RecoveryHalfLifeSeconds=ProfileMoodHalfLife(ticket.PersonId)};
            NpcObservedSpeechReceipt issued=ticket.IssuedReceipt;
            double refusal=action==ConversationSpeechBoundaryAction.EndAndReport?NpcConversationBoundaryRefusalSeconds:
                ConversationSpeechBoundaryPolicy.RefusalSeconds(ActiveNpcPersonality(state,ticket.PersonId),inherited);
            var next=new NpcConversationBoundaryRecord {PersonId=ticket.PersonId,
                ReceiptId=issued.Evidence.ReceiptId,Ordinal=ticket.Ordinal,SpeechKind=classification.Kind,
                Action=action,ObservedAt=ticket.ObservedGameTime,
                PolicyVersion=1,RefusalSeconds=refusal,RefuseUntil=ticket.ObservedGameTime+refusal,
                ReportStatus=action==ConversationSpeechBoundaryAction.EndAndReport?
                    NpcConversationBoundaryReportStatus.Pending:NpcConversationBoundaryReportStatus.None,
                RefusesFurtherCommunication=action==ConversationSpeechBoundaryAction.EndConversation,
                ParentBoundaryOrdinal=inherited?previous.Ordinal:0,
                ParentBoundaryReceiptId=inherited?previous.ReceiptId:null,
                ParentBoundaryKind=inherited?previous.SpeechKind:0};
            try {next=NpcConversationBoundaryRecord.CopyValidated(next,ticket.PersonId,NpcFrameworkScopeId);}
            catch(ArgumentException){return false;}
            NpcRelationshipLedger stagedRelationships;
            NpcDirectedRelationshipView appliedRelationship;
            if(!NpcRelationshipLedger.TryStageConversationBoundary(state.NpcRelationships,
                state.NpcRelationships.Revision,ticket,receipt,source,classification,ticket.Ordinal,
                next.ParentBoundaryOrdinal,ActiveNpcPersonality(state,ticket.PersonId),
                ReadNpcRole(ticket.PersonId),
                IsKnownNpcRelationshipIdentity,out stagedRelationships,out appliedRelationship))return false;
            if(attitude==null)
            {
                attitude=new ConversationAttitude {PersonId=ticket.PersonId,Day=Math.Floor(ticket.ObservedGameTime/86400d)};
                state.ConversationAttitudes.Add(attitude);
            }
            if(mood==null)state.ConversationMoods.Add(nextMood);
            else
            {
                mood.Value=nextMood.Value;
                mood.HasTimeAnchor=true;
                mood.UpdatedAt=nextMood.UpdatedAt;
                mood.Revision=nextMood.Revision;
                mood.RecoveryHalfLifeSeconds=nextMood.RecoveryHalfLifeSeconds;
            }
            state.NpcRelationships=stagedRelationships;
            attitude.Boundary=next;
            record=NpcConversationBoundaryRecord.CopyValidated(next,ticket.PersonId,NpcFrameworkScopeId);
            return true;
        }

        private bool ValidBoundarySpeech(NpcObservedSpeechTicket ticket,NpcObservedSpeechReceipt receipt,
            object source,NpcSpeechActClassification classification)
        {
            if(!CurrentObservedSpeechTicket(ticket) || receipt==null || source==null ||
                classification==null || classification.Request==null ||
                !ReferenceEquals(classification.Request.Source,source) ||
                classification.Request.ObserverId!=ticket.PersonId ||
                classification.Request.SpeakerId!="player" ||
                classification.Request.Utterance!=ticket.ExactText)return false;
            NpcObservedSpeechReceipt checkedReceipt;
            try {checkedReceipt=NpcObservedSpeechReceipt.CopyValidated(receipt);}
            catch(ArgumentException){return false;}
            NpcObservedSpeechReceipt issued=ticket.IssuedReceipt;
            if(checkedReceipt.Evidence.Origin!=NpcExperienceEventOrigin.AttributedSpeech ||
                checkedReceipt.Evidence.ObserverId!=ticket.PersonId ||
                checkedReceipt.Evidence.SubjectPersonId!=ticket.PersonId ||
                checkedReceipt.Evidence.AttributedSpeakerId!=NpcDirectedRelationship.PlayerTargetId ||
                checkedReceipt.Evidence.ReceiptId!=issued.Evidence.ReceiptId ||
                checkedReceipt.Evidence.EpisodeId!=issued.Evidence.EpisodeId ||
                checkedReceipt.Evidence.UtteranceId!=issued.Evidence.UtteranceId ||
                checkedReceipt.Evidence.DeduplicationKey!=issued.Evidence.DeduplicationKey ||
                checkedReceipt.Evidence.ObservedGameTime!=ticket.ObservedGameTime ||
                checkedReceipt.Turn.ObserverId!=ticket.PersonId ||
                checkedReceipt.Turn.SpeakerId!=NpcDirectedRelationship.PlayerTargetId ||
                checkedReceipt.Turn.ReceiptId!=issued.Turn.ReceiptId ||
                checkedReceipt.Turn.EpisodeId!=issued.Turn.EpisodeId ||
                checkedReceipt.Turn.Text!=ticket.ExactText ||
                checkedReceipt.Turn.ObservedGameTime!=ticket.ObservedGameTime)return false;
            var sourceState=state.NpcObservedSpeechSources.Find(row=>row.PersonId==ticket.PersonId);
            return sourceState!=null && sourceState.LastIssuedOrdinal==ticket.Ordinal &&
                sourceState.LastAcceptedOrdinal<ticket.Ordinal && sourceState.Pending==null;
        }

        public NpcConversationBoundaryRecord ReadNpcConversationBoundary(string personId,double now)
        {
            if(string.IsNullOrWhiteSpace(personId) || !Finite(now) || now<0)return null;
            var attitude=state.ConversationAttitudes.Find(row=>row.PersonId==personId);
            return attitude==null || attitude.Boundary==null || attitude.Boundary.ObservedAt>now?null:
                NpcConversationBoundaryRecord.CopyValidated(attitude.Boundary,personId,NpcFrameworkScopeId);
        }

        public bool CanTalkToNpcAfterBoundary(string personId,double now)
        {
            if(string.IsNullOrWhiteSpace(personId) || !Finite(now) || now<0)return false;
            var row=ReadNpcConversationBoundary(personId,now);
            if(row==null)return true;
            if(row.SpeechKind==NpcExperienceSpeechActKind.DirectThreat)return false;
            if(row.PolicyVersion==0 && row.Action==ConversationSpeechBoundaryAction.EndAndReport)return true;
            return !(row.ParentBoundaryOrdinal>0 && row.RefusesFurtherCommunication && now<row.RefuseUntil) &&
                row.ReportStatus!=NpcConversationBoundaryReportStatus.Pending &&
                row.ReportStatus!=NpcConversationBoundaryReportStatus.Started;
        }

        public bool TryAbandonExpiredNpcConversationBoundaryReport(string personId,double now)
        {
            var row=ReadNpcConversationBoundary(personId,now);
            return row!=null && (now>=row.ObservedAt+1080d ||
                row.PolicyVersion==0 && row.SpeechKind!=NpcExperienceSpeechActKind.DirectThreat) &&
                (row.ReportStatus==NpcConversationBoundaryReportStatus.Pending ||
                 row.ReportStatus==NpcConversationBoundaryReportStatus.Started) &&
                TryMarkNpcConversationBoundaryReport(personId,row.ReceiptId,
                    NpcConversationBoundaryReportStatus.Abandoned);
        }

        public bool ShouldEnforceNpcConversationBoundary(string personId,double now)
        {
            var row=ReadNpcConversationBoundary(personId,now);
            return row!=null && row.Action==ConversationSpeechBoundaryAction.EndConversation &&
                row.RefusesFurtherCommunication && now<row.RefuseUntil;
        }

        public bool HasUnrepairedNpcConversationBoundary(string personId,double now)
        {
            var row=ReadNpcConversationBoundary(personId,now);
            return row!=null && row.Action==ConversationSpeechBoundaryAction.EndConversation &&
                row.RefusesFurtherCommunication;
        }

        public bool TryRepairNpcConversationBoundary(NpcObservedSpeechTicket ticket,NpcObservedSpeechReceipt receipt,
            object source,NpcSpeechActClassification classification)
        {
            if(!ValidBoundarySpeech(ticket,receipt,source,classification) ||
                classification.Kind!=NpcExperienceSpeechActKind.RepairAttempt ||
                classification.Request.ListenerRefusesFurtherCommunication!=
                    ShouldEnforceNpcConversationBoundary(ticket.PersonId,ticket.ObservedGameTime) ||
                !CanTalkToNpcAfterBoundary(ticket.PersonId,ticket.ObservedGameTime) ||
                ConversationSpeechBoundaryPolicy.Decide(source,classification)!=ConversationSpeechBoundaryAction.Continue)
                return false;
            var attitude=state.ConversationAttitudes.Find(row=>row.PersonId==ticket.PersonId);
            var previous=attitude==null?null:attitude.Boundary;
            if(previous==null || previous.Action!=ConversationSpeechBoundaryAction.EndConversation ||
                !previous.RefusesFurtherCommunication || ticket.Ordinal<=previous.Ordinal ||
                ticket.ObservedGameTime<previous.ObservedAt)return false;
            var next=NpcConversationBoundaryRecord.CopyValidated(previous,ticket.PersonId,NpcFrameworkScopeId);
            next.RefusesFurtherCommunication=false;
            next.RepairedAtOrdinal=ticket.Ordinal;
            next.RepairedAtReceiptId=ticket.IssuedReceipt.Evidence.ReceiptId;
            try {next=NpcConversationBoundaryRecord.CopyValidated(next,ticket.PersonId,NpcFrameworkScopeId);}
            catch(ArgumentException){return false;}
            attitude.Boundary=next;
            return true;
        }

        public bool TryMarkNpcConversationBoundaryReport(string personId,string receiptId,
            NpcConversationBoundaryReportStatus status)
        {
            if(string.IsNullOrWhiteSpace(personId) || string.IsNullOrWhiteSpace(receiptId) ||
                status!=NpcConversationBoundaryReportStatus.Started &&
                status!=NpcConversationBoundaryReportStatus.Delivered &&
                status!=NpcConversationBoundaryReportStatus.Abandoned)return false;
            var attitude=state.ConversationAttitudes.Find(row=>row.PersonId==personId);
            var previous=attitude==null?null:attitude.Boundary;
            if(previous==null || previous.ReceiptId!=receiptId ||
                previous.Action!=ConversationSpeechBoundaryAction.EndAndReport ||
                (previous.ReportStatus==NpcConversationBoundaryReportStatus.Delivered ||
                    previous.ReportStatus==NpcConversationBoundaryReportStatus.Abandoned) &&
                    previous.ReportStatus!=status ||
                (status==NpcConversationBoundaryReportStatus.Delivered &&
                    previous.ReportStatus==NpcConversationBoundaryReportStatus.Pending))return false;
            if(previous.ReportStatus==status)return true;
            var next=NpcConversationBoundaryRecord.CopyValidated(previous,personId,NpcFrameworkScopeId);
            next.ReportStatus=status;
            attitude.Boundary=next;
            return true;
        }

        private static void ValidateNpcConversationBoundaries(WorldState state)
        {
            foreach(var attitude in state.ConversationAttitudes)
            {
                var boundary=attitude.Boundary;
                if(boundary==null)continue;
                var source=state.NpcObservedSpeechSources.Find(row=>row.PersonId==attitude.PersonId);
                // The foreground boundary is committed while its exact ticket is still
                // current, before the response append accepts that speech for appraisal.
                Require(source!=null && source.LastIssuedOrdinal>=boundary.Ordinal &&
                    source.LastIssuedOrdinal>=boundary.RepairedAtOrdinal,
                    "NPC conversation boundary lacks an issued observed-speech source.");
                var outcome=state.NpcRelationships.AcceptedOutcomes.Find(row=>
                    row.ObserverId==attitude.PersonId && row.ReceiptId==boundary.ReceiptId &&
                    row.ConversationBoundaryOrdinal==boundary.Ordinal);
                Require(outcome!=null && outcome.Origin==NpcExperienceEventOrigin.AttributedSpeech &&
                    outcome.TargetId==NpcDirectedRelationship.PlayerTargetId &&
                    outcome.SpeechActKind==boundary.SpeechKind &&
                    outcome.ParentBoundaryOrdinal==boundary.ParentBoundaryOrdinal &&
                    outcome.ObservedGameTime==boundary.ObservedAt,
                    "NPC conversation boundary lacks its accepted relationship event.");
                if(boundary.ParentBoundaryOrdinal>0)
                {
                    var parent=state.NpcRelationships.AcceptedOutcomes.Find(row=>
                        row.ObserverId==attitude.PersonId && row.ReceiptId==boundary.ParentBoundaryReceiptId &&
                        row.ConversationBoundaryOrdinal==boundary.ParentBoundaryOrdinal);
                    Require(parent!=null && parent.Origin==NpcExperienceEventOrigin.AttributedSpeech &&
                        parent.SubjectPersonId==attitude.PersonId && parent.TargetId==NpcDirectedRelationship.PlayerTargetId &&
                        parent.AttributedSpeakerId==NpcDirectedRelationship.PlayerTargetId &&
                        parent.SpeechActKind==boundary.ParentBoundaryKind && parent.ObservedGameTime<=boundary.ObservedAt &&
                        NpcConversationBoundaryRecord.ActionFor(parent.SpeechActKind,boundary.PolicyVersion)==
                            ConversationSpeechBoundaryAction.EndConversation,
                        "NPC conversation boundary lacks its parent refusal event.");
                }
            }
        }
    }
}
