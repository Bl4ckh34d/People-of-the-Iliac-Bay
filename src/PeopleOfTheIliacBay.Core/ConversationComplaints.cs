using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    [Serializable]
    public sealed class DialogueComplaintRecord
    {
        public string PersonId;
        public string SourceReceiptId;
        public int SettlementId;
        public double StartedAt, LastUpdatedAt, ExpiresAt, X, Y, Z, CrimeX, CrimeY, CrimeZ;
        public string GuardId;
        public int NextWaypoint;
        public List<SavedRoutePoint> Route = new List<SavedRoutePoint>();
    }

    public sealed partial class WorldStateStore
    {
        public DialogueComplaintRecord GetDialogueComplaint(string personId)
        {
            if (string.IsNullOrEmpty(personId)) return null;
            var item = state.DialogueComplaints.Find(x => x.PersonId == personId);
            return item == null ? null : CopyDialogueComplaint(item);
        }

        public List<DialogueComplaintRecord> DialogueComplaints()
        {
            var result = new List<DialogueComplaintRecord>();
            foreach (var item in state.DialogueComplaints) result.Add(CopyDialogueComplaint(item));
            return result;
        }

        public void SetDialogueComplaint(DialogueComplaintRecord record)
        {
            var copy = ValidateDialogueComplaint(record);
            var current = state.DialogueComplaints.FindIndex(x => x.PersonId == copy.PersonId);
            if (current < 0) state.DialogueComplaints.Add(copy);
            else state.DialogueComplaints[current] = copy;
        }

        public bool TryStartReceiptDialogueComplaint(DialogueComplaintRecord record)
        {
            var copy = ValidateDialogueComplaint(record);
            if (copy.SourceReceiptId == null) return false;
            var current = state.DialogueComplaints.Find(x => x.PersonId == copy.PersonId);
            if (current != null) return current.SourceReceiptId == copy.SourceReceiptId;
            state.DialogueComplaints.Add(copy);
            return true;
        }

        public List<NpcConversationBoundaryRecord> ReadStartedNpcConversationBoundaryReports(double now)
        {
            Require(Finite(now) && now >= 0, "Invalid conversation boundary report time.");
            var result = new List<NpcConversationBoundaryRecord>();
            foreach (var attitude in state.ConversationAttitudes)
            {
                var boundary = attitude.Boundary;
                if (boundary == null || boundary.ObservedAt > now ||
                    boundary.SpeechKind!=NpcExperienceSpeechActKind.DirectThreat ||
                    boundary.Action != ConversationSpeechBoundaryAction.EndAndReport ||
                    boundary.ReportStatus != NpcConversationBoundaryReportStatus.Started) continue;
                result.Add(NpcConversationBoundaryRecord.CopyValidated(boundary,
                    attitude.PersonId, NpcFrameworkScopeId));
            }
            return result;
        }

        public bool TryRetireUnsupportedDialogueComplaint(string personId,double now)
        {
            Require(Finite(now) && now>=0,"Invalid conversation complaint time.");
            var complaint=state.DialogueComplaints.Find(row=>row.PersonId==personId);
            if(complaint==null)return false;
            var boundary=ReadNpcConversationBoundary(personId,now);
            bool matching=complaint.SourceReceiptId!=null && boundary!=null &&
                boundary.ReceiptId==complaint.SourceReceiptId;
            if(matching && boundary.SpeechKind==NpcExperienceSpeechActKind.DirectThreat &&
                boundary.Action==ConversationSpeechBoundaryAction.EndAndReport &&
                (boundary.ReportStatus==NpcConversationBoundaryReportStatus.Pending ||
                 boundary.ReportStatus==NpcConversationBoundaryReportStatus.Started) &&
                now<complaint.ExpiresAt && now<boundary.ObservedAt+1080d)return false;
            if(matching && (boundary.ReportStatus==NpcConversationBoundaryReportStatus.Pending ||
                boundary.ReportStatus==NpcConversationBoundaryReportStatus.Started))
                TryMarkNpcConversationBoundaryReport(personId,boundary.ReceiptId,NpcConversationBoundaryReportStatus.Abandoned);
            state.DialogueComplaints.Remove(complaint);
            return true;
        }

        public void RemoveDialogueComplaint(string personId)
        {
            if (!string.IsNullOrEmpty(personId)) state.DialogueComplaints.RemoveAll(x => x.PersonId == personId);
        }

        private static List<DialogueComplaintRecord> CopyDialogueComplaints(WorldState source)
        {
            Require(source.DialogueComplaints != null, "Missing dialogue complaints.");
            var result = new List<DialogueComplaintRecord>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var record in source.DialogueComplaints)
            {
                var copy = ValidateDialogueComplaint(record);
                Require(ids.Add(copy.PersonId), "Duplicate dialogue complaint.");
                result.Add(copy);
            }
            return result;
        }

        private static DialogueComplaintRecord CopyDialogueComplaint(DialogueComplaintRecord source)
        {
            return ValidateDialogueComplaint(source);
        }

        private static DialogueComplaintRecord ValidateDialogueComplaint(DialogueComplaintRecord source)
        {
            Require(source != null && !string.IsNullOrWhiteSpace(source.PersonId) && source.PersonId.Length <= 160 &&
                source.SettlementId >= 0 && Finite(source.StartedAt) && source.StartedAt >= 0 &&
                Finite(source.LastUpdatedAt) && source.LastUpdatedAt >= source.StartedAt &&
                Finite(source.ExpiresAt) && source.ExpiresAt > source.StartedAt &&
                Finite(source.X) && Finite(source.Y) && Finite(source.Z) &&
                Finite(source.CrimeX) && Finite(source.CrimeY) && Finite(source.CrimeZ) &&
                source.Route != null && source.Route.Count >= 2 && source.Route.Count <= 512 &&
                source.NextWaypoint >= 1 && source.NextWaypoint <= source.Route.Count,
                "Invalid dialogue complaint.");
            Require(string.IsNullOrEmpty(source.GuardId) || source.GuardId.Length <= 160,
                "Invalid dialogue complaint guard.");
            Require(source.SourceReceiptId == null ||
                (!string.IsNullOrWhiteSpace(source.SourceReceiptId) && source.SourceReceiptId.Length <= 160),
                "Invalid dialogue complaint receipt.");
            var copy = new DialogueComplaintRecord { PersonId = source.PersonId, SettlementId = source.SettlementId,
                SourceReceiptId = source.SourceReceiptId,
                StartedAt = source.StartedAt, LastUpdatedAt = source.LastUpdatedAt, ExpiresAt = source.ExpiresAt, X = source.X, Y = source.Y, Z = source.Z,
                CrimeX = source.CrimeX, CrimeY = source.CrimeY, CrimeZ = source.CrimeZ,
                GuardId = source.GuardId, NextWaypoint = source.NextWaypoint };
            foreach (var point in source.Route)
            {
                Require(point != null && Finite(point.X) && Finite(point.Z), "Invalid dialogue complaint route.");
                copy.Route.Add(new SavedRoutePoint { X = point.X, Z = point.Z });
            }
            return copy;
        }
    }
}
