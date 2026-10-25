using System;
using System.Collections.Generic;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>
    /// FullSerializer-friendly save DTO. Public fields are the wire shape; restore must pass through
    /// NpcKnowledgeLedger.CopyValidated before any query or update.
    /// </summary>
    [Serializable]
    public sealed class NpcKnowledgeState
    {
        public int SchemaVersion = 1;
        public string ActorId;
        public string SaveEpochId;
        public long Revision;
        public double LastUpdatedGameTime;
        public List<NpcKnowledgeEntryState> Entries = new List<NpcKnowledgeEntryState>();
        public List<string> AppliedUpdateReceiptIds = new List<string>();
    }

    /// <summary>Current actor-owned evidence for one proposition; older corrections are linked by receipt ID.</summary>
    [Serializable]
    public sealed class NpcKnowledgeEntryState
    {
        public string EntryId;
        public string ActorId;
        public string PropositionId;
        public string ValueText;
        public NpcDisclosureEpistemicKind EpistemicKind;
        public NpcDisclosureProtection Protection;
        public NpcKnowledgeEvidenceOrigin EvidenceOrigin;
        public string SourceId;
        public string UpdateReceiptId;
        public string EvidenceReceiptId;
        public long SourceRevision;
        public double CapturedGameTime;
        public string AttributionId;
        public string SupersedesUpdateReceiptId;
    }
}
