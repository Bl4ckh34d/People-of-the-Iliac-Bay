namespace PeopleOfTheIliacBay.Core
{
    // Timed waits can follow a completed static MoveTo only. Dynamic Follow
    // targets never become saved task positions.
    public static class NpcTaskPositionBinding
    {
        public static bool IsStaticTarget(NpcActionTarget target)
        {
            if(target==null || target.TransferDirection!=NpcActionTransferDirection.NpcToCounterparty) return false;
            if(target.Kind!=NpcActionTargetKind.WorldPoint && target.Kind!=NpcActionTargetKind.IndoorPoint &&
                target.Kind!=NpcActionTargetKind.Building) return false;
            try { NpcActionContract.ValidateTarget(target); return true; }
            catch(System.ArgumentException) { return false; }
        }

        // Caller supplies the same resolved radius used by MoveTo's runtime arrival check.
        public static bool CanStartWaitAtResolvedArrival(double distance,double resolvedArrivalRange)
        {
            return Finite(distance) && Finite(resolvedArrivalRange) && distance>=0 && resolvedArrivalRange>=0 &&
                distance<=resolvedArrivalRange;
        }

        public static bool TryBindWaitAfterMoveTo(NpcActionDecision previous,NpcActionDecision wait,
            out NpcActionTarget bound,out string reason)
        {
            bound=null; reason="Wait must follow a static MoveTo destination.";
            if(previous==null || previous.Option==null || previous.Option.Kind!=NpcActionKind.MoveTo ||
                wait==null || wait.Option==null || wait.Option.Kind!=NpcActionKind.Wait ||
                !IsStaticTarget(previous.Option.Target)) return false;
            bound=NpcActionExecutor.CopyTarget(previous.Option.Target);
            reason="";
            return true;
        }

        // A public-building Enter retains its exact door target until native
        // destination observation. A later Wait may bind to it, but only as the
        // immediate successor; it never invents an interior point offscreen.
        public static bool TryBindWaitAfterStaticArrival(NpcActionDecision previous,NpcActionDecision wait,
            out NpcActionTarget bound,out string reason)
        {
            bound=null; reason="Wait must follow a static MoveTo destination or exact building entry.";
            if(previous==null || previous.Option==null || wait==null || wait.Option==null ||
                wait.Option.Kind!=NpcActionKind.Wait || !IsStaticArrival(previous.Option.Kind,previous.Option.Target)) return false;
            bound=NpcActionExecutor.CopyTarget(previous.Option.Target);
            reason="";
            return true;
        }

        // Build the candidate shown to the decision model before it chooses a
        // later Wait. This makes the position part of the selected native option.
        public static bool TryBindWaitOptionAfterMoveTo(NpcActionDecision previous,NpcActionOption wait,int boundOptionId,
            out NpcActionOption bound,out string reason)
        {
            bound=null;
            reason="Wait must follow a static MoveTo destination and use a distinct candidate ID.";
            NpcActionTarget target;
            if(wait==null || boundOptionId<1 || boundOptionId==wait.Id ||
                !TryBindWaitAfterMoveTo(previous,new NpcActionDecision {Option=wait},out target,out reason))return false;
            bound=new NpcActionOption {Id=boundOptionId,Kind=wait.Kind,
                Label="wait at the exact destination of the preceding MoveTo step; preserve the requested place",
                MaximumQuantity=wait.MaximumQuantity,ItemSourceStep=wait.ItemSourceStep,Target=target};
            return true;
        }

        public static bool TryBindWaitOptionAfterStaticArrival(NpcActionDecision previous,NpcActionOption wait,int boundOptionId,
            out NpcActionOption bound,out string reason)
        {
            bound=null;
            reason="Wait must follow a static MoveTo destination or exact building entry and use a distinct candidate ID.";
            NpcActionTarget target;
            if(wait==null || boundOptionId<1 || boundOptionId==wait.Id ||
                !TryBindWaitAfterStaticArrival(previous,new NpcActionDecision {Option=wait},out target,out reason))return false;
            bound=new NpcActionOption {Id=boundOptionId,Kind=wait.Kind,
                Label="wait at the exact destination of the preceding MoveTo or building entry step; preserve the requested place",
                MaximumQuantity=wait.MaximumQuantity,ItemSourceStep=wait.ItemSourceStep,Target=target};
            return true;
        }

        public static bool IsBoundWaitAfterMoveTo(NpcActionDecision previous,NpcActionDecision wait)
        {
            return previous!=null && wait!=null && previous.Option!=null && wait.Option!=null &&
                previous.Option.Kind==NpcActionKind.MoveTo && wait.Option.Kind==NpcActionKind.Wait &&
                IsStaticTarget(previous.Option.Target) && NpcTaskContract.TargetMatches(previous.Option.Target,wait.Option.Target);
        }

        public static bool IsBoundWaitAfterMoveTo(NpcTaskStep previous,NpcTaskStep wait)
        {
            return previous!=null && wait!=null && previous.Kind==NpcActionKind.MoveTo &&
                wait.Kind==NpcActionKind.Wait && IsStaticTarget(previous.Target) &&
                NpcTaskContract.TargetMatches(previous.Target,wait.Target);
        }

        public static bool IsBoundWaitAfterStaticArrival(NpcActionDecision previous,NpcActionDecision wait)
        {
            return previous!=null && wait!=null && previous.Option!=null && wait.Option!=null &&
                wait.Option.Kind==NpcActionKind.Wait && IsStaticArrival(previous.Option.Kind,previous.Option.Target) &&
                NpcTaskContract.TargetMatches(previous.Option.Target,wait.Option.Target);
        }

        public static bool IsBoundWaitAfterStaticArrival(NpcTaskStep previous,NpcTaskStep wait)
        {
            return previous!=null && wait!=null && wait.Kind==NpcActionKind.Wait &&
                IsStaticArrival(previous.Kind,previous.Target) && NpcTaskContract.TargetMatches(previous.Target,wait.Target);
        }

        public static bool HasCompletedMoveToReceipt(NpcTaskStep previous,NpcActionJob action,NpcTaskStep wait)
        {
            return IsBoundWaitAfterMoveTo(previous,wait) && action!=null &&
                action.Kind==NpcActionKind.MoveTo && action.Phase==NpcActionPhase.Completed &&
                action.CompletedEffectId!=null && action.CompletedEffectId==previous.CompletedEffectId &&
                NpcTaskContract.TargetMatches(previous.Target,action.Target);
        }

        public static bool HasCompletedStaticArrivalReceipt(NpcTaskStep previous,NpcActionJob action,NpcTaskStep wait)
        {
            if(!IsBoundWaitAfterStaticArrival(previous,wait) || action==null || action.Kind!=previous.Kind ||
                action.Phase!=NpcActionPhase.Completed || action.CompletedEffectId==null ||
                action.CompletedEffectId!=previous.CompletedEffectId || !NpcTaskContract.TargetMatches(previous.Target,action.Target))return false;
            if(previous.Kind==NpcActionKind.MoveTo)return true;
            var receipt=action.BuildingTransitionReceipt;
            return previous.Kind==NpcActionKind.Enter && receipt!=null && receipt.DestinationObserved &&
                HasMatchingBuildingEntryReceipt(previous,receipt);
        }

        // This is deliberately separate from HasCompletedStaticArrivalReceipt:
        // the receipt proves the actual source-door crossing, not an unseen room
        // coordinate. The existing landing path remains responsible for a later
        // player-observed interior placement.
        public static bool HasCompletedOffscreenBuildingEntryReceipt(NpcTaskStep previous,NpcActionJob action,NpcTaskStep wait)
        {
            if(!IsBoundWaitAfterStaticArrival(previous,wait) || previous.Kind!=NpcActionKind.Enter || action==null ||
                action.Kind!=NpcActionKind.Enter || action.Phase!=NpcActionPhase.Completed || action.CompletedEffectId==null ||
                action.CompletedEffectId!=previous.CompletedEffectId || !NpcTaskContract.TargetMatches(previous.Target,action.Target))return false;
            var receipt=action.BuildingTransitionReceipt;
            return receipt!=null && !receipt.DestinationObserved && HasMatchingBuildingEntryReceipt(previous,receipt);
        }

        private static bool HasMatchingBuildingEntryReceipt(NpcTaskStep previous,NpcBuildingTransitionReceipt receipt)
        {
            return previous!=null && previous.Target!=null && receipt!=null &&
                receipt.SettlementId==previous.Target.SettlementId && receipt.BuildingKey==previous.Target.BuildingKey &&
                receipt.DoorId==previous.Target.DoorId && receipt.SourceSpaceId==previous.Target.WorldSpaceId &&
                receipt.DestinationSpaceId==previous.Target.DestinationSpaceId;
        }

        private static bool IsStaticArrival(NpcActionKind kind,NpcActionTarget target)
        {
            if(kind==NpcActionKind.MoveTo)return IsStaticTarget(target);
            if(kind!=NpcActionKind.Enter || target==null || target.Kind!=NpcActionTargetKind.Building ||
                string.IsNullOrWhiteSpace(target.DoorId) || string.IsNullOrWhiteSpace(target.WorldSpaceId) ||
                string.IsNullOrWhiteSpace(target.DestinationSpaceId))return false;
            try {NpcActionContract.ValidateTarget(target);return true;}
            catch(System.ArgumentException) {return false;}
        }

        private static bool Finite(double value)
        {return !double.IsNaN(value) && !double.IsInfinity(value);}
    }
}
