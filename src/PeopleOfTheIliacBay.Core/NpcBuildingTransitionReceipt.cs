using System;

namespace PeopleOfTheIliacBay.Core
{
    /// <summary>Exact observed native door transition for one NPC action.</summary>
    [Serializable]
    public sealed class NpcBuildingTransitionReceipt
    {
        public string ActionId, ActorId, DoorId, SourceSpaceId, DestinationSpaceId;
        public int SettlementId, BuildingKey;
        public double ObservedGameTime;
        public bool DestinationObserved;

        private NpcBuildingTransitionReceipt() { }

        public static NpcBuildingTransitionReceipt Create(NpcActionJob job, string actorId,
            int observedSettlementId, int observedBuildingKey, string doorId,
            string sourceSpaceId, string destinationSpaceId, double observedGameTime)
        {
            var receipt = new NpcBuildingTransitionReceipt
            {
                ActionId = job == null ? null : job.Id, ActorId = actorId, DoorId = doorId,
                SourceSpaceId = sourceSpaceId, DestinationSpaceId = destinationSpaceId,
                SettlementId = observedSettlementId, BuildingKey = observedBuildingKey,
                ObservedGameTime = observedGameTime
            };
            if (job == null || job.Phase != NpcActionPhase.Executing || !Matches(receipt, job, true))
                throw new ArgumentException("Native building transition does not match its executing action.");
            return receipt;
        }

        internal static NpcBuildingTransitionReceipt CopyValidated(NpcBuildingTransitionReceipt source, NpcActionJob job)
        {
            if (source == null) return null;
            if (!Matches(source, job, job.Phase == NpcActionPhase.Executing))
                throw new ArgumentException("Building transition receipt differs from its action.");
            return CopyFields(source);
        }

        internal static NpcBuildingTransitionReceipt CopyFields(NpcBuildingTransitionReceipt source)
        {
            if (source == null) return null;
            return new NpcBuildingTransitionReceipt
            {
                ActionId = source.ActionId, ActorId = source.ActorId, DoorId = source.DoorId,
                SourceSpaceId = source.SourceSpaceId, DestinationSpaceId = source.DestinationSpaceId,
                SettlementId = source.SettlementId, BuildingKey = source.BuildingKey,
                ObservedGameTime = source.ObservedGameTime, DestinationObserved = source.DestinationObserved
            };
        }

        internal bool MatchesDestination(NpcActionTarget position)
        {
            if(position==null || position.SettlementId!=SettlementId)return false;
            return position.Kind==NpcActionTargetKind.IndoorPoint?
                position.BuildingKey==BuildingKey && DestinationSpaceId=="interior:"+SettlementId+":"+BuildingKey:
                position.Kind==NpcActionTargetKind.WorldPoint && position.WorldSpaceId==DestinationSpaceId;
        }

        private static bool Matches(NpcBuildingTransitionReceipt receipt, NpcActionJob job, bool executing)
        {
            if (receipt == null || job == null || job.Target == null || executing && receipt.DestinationObserved ||
                (job.Kind != NpcActionKind.Enter && job.Kind != NpcActionKind.Exit) ||
                (executing ? job.Phase != NpcActionPhase.Executing :
                    job.Phase != NpcActionPhase.Completed || job.CompletedEffectId != job.Id + ":observed") ||
                receipt.ActionId != job.Id || receipt.ActorId != job.ResidentId ||
                receipt.SettlementId != job.Target.SettlementId || receipt.BuildingKey != job.Target.BuildingKey ||
                receipt.DoorId != job.Target.DoorId || receipt.SourceSpaceId != job.Target.WorldSpaceId ||
                receipt.DestinationSpaceId != job.Target.DestinationSpaceId ||
                string.IsNullOrWhiteSpace(receipt.DoorId) || string.IsNullOrWhiteSpace(receipt.SourceSpaceId) ||
                string.IsNullOrWhiteSpace(receipt.DestinationSpaceId) ||
                double.IsNaN(receipt.ObservedGameTime) || double.IsInfinity(receipt.ObservedGameTime) ||
                receipt.ObservedGameTime < job.AcceptedAt || receipt.ObservedGameTime >= job.DeadlineAt)
                return false;
            // Later return-position writes update the action clock, not this completed native event.
            return executing ? receipt.ObservedGameTime >= job.UpdatedAt : receipt.ObservedGameTime <= job.UpdatedAt;
        }
    }
}
