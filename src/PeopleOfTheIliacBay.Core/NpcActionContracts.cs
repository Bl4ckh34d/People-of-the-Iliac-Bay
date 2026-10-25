using System;

namespace PeopleOfTheIliacBay.Core
{
    // Append persisted values only. Lead is deliberately distinct from MoveTo:
    // MoveTo relocates the NPC; Lead keeps the player with the NPC en route.
    public enum NpcActionKind {MoveTo,Follow,Interact,Purchase,Transfer,Attack,CastSpell,Lockpick,Climb,TakeSeat,ChangeSchedule,Wait,UseHealingItem,RestToHeal,Lead,Enter,Exit,Defend,ReportMurder}
    public enum NpcActionPhase {Proposed,Accepted,Approaching,Executing,Completed,Failed,Cancelled}
    public enum NpcActionTargetKind {Person,Building,WorldPoint,IndoorPoint,Container,Item}
    // Zero remains the schema-28 NPC-owned source direction.
    public enum NpcActionTransferDirection {NpcToCounterparty=0,PlayerToNpc=1,NpcToContainer=2,HomeContainerToNpc=3,ObservedLootToNpc=4}
    [Serializable] public sealed class NpcActionTarget
    {
        public NpcActionTargetKind Kind;
        public string PersonId,ContainerId,ItemId,WorldSpaceId;
        // Native door and destination space are required for Enter/Exit.
        public string DoorId,DestinationSpaceId;
        // Missing field in schema-28 jobs defaults to the existing one-item action.
        public int Quantity=1;
        // Exact quoted NPC-wallet cost for shop purchases; legacy targets are zero.
        public int QuotedGold;
        public NpcActionTransferDirection TransferDirection;
        public int SettlementId=-1,BuildingKey=-1;
        public double X,Y,Z;
    }
    public static class NpcActionContract
    {
        public static bool CanTransition(NpcActionPhase from,NpcActionPhase to,bool nativeSuccessConfirmed)
        {
            if((int)from<0 || (int)from>6 || (int)to<0 || (int)to>6 || from>=NpcActionPhase.Completed || from==to) return false;
            if(to==NpcActionPhase.Failed || to==NpcActionPhase.Cancelled) return true;
            if(to==NpcActionPhase.Completed) return from==NpcActionPhase.Executing && nativeSuccessConfirmed;
            return from==NpcActionPhase.Proposed && to==NpcActionPhase.Accepted ||
                from==NpcActionPhase.Accepted && (to==NpcActionPhase.Approaching || to==NpcActionPhase.Executing) ||
                from==NpcActionPhase.Approaching && to==NpcActionPhase.Executing ||
                from==NpcActionPhase.Executing && to==NpcActionPhase.Approaching;
        }
        public static bool CanAccept(bool alive,bool questControlled,bool adapterAvailable,bool consent,bool scheduleCompatible,out string reason)
        {
            reason=!alive?"The NPC is not alive.":questControlled?"Native quest control takes priority.":
                !adapterAvailable?"No execution adapter is available.":!consent?"The NPC has not agreed.":
                !scheduleCompatible?"The action conflicts with existing commitments.":"";
            return reason.Length==0;
        }
        public static void ValidateTarget(NpcActionTarget target)
        {
            if(target==null || (int)target.Kind<0 || (int)target.Kind>5 ||
                (int)target.TransferDirection<0 || (int)target.TransferDirection>4) throw new ArgumentException("Invalid NPC action target.");
            bool valid=false;
            switch(target.Kind)
            {
                case NpcActionTargetKind.Person:valid=Id(target.PersonId);break;
                case NpcActionTargetKind.Container:valid=Id(target.ContainerId);break;
                case NpcActionTargetKind.Item:valid=Id(target.ItemId);break;
                case NpcActionTargetKind.Building:valid=target.SettlementId>=0 && target.BuildingKey>=0;break;
                case NpcActionTargetKind.IndoorPoint:
                    valid=target.SettlementId>=0 && target.BuildingKey>=0 && Point(target);break;
                case NpcActionTargetKind.WorldPoint:
                    // Coordinates belong to a stable map-space key, never an
                    // unqualified Unity position that shifts with the origin.
                    valid=Id(target.WorldSpaceId) && Point(target);break;
            }
            if((target.TransferDirection==NpcActionTransferDirection.NpcToContainer || target.TransferDirection==NpcActionTransferDirection.HomeContainerToNpc) &&
                (target.Kind!=NpcActionTargetKind.IndoorPoint || !Id(target.ContainerId) || !Id(target.ItemId) || target.Quantity<1 || target.Quantity>1000000))
                valid=false;
            if(target.TransferDirection==NpcActionTransferDirection.ObservedLootToNpc &&
                ((target.Kind!=NpcActionTargetKind.IndoorPoint && target.Kind!=NpcActionTargetKind.WorldPoint) ||
                 !Id(target.ContainerId) || !Id(target.ItemId) || target.Quantity<1 || target.Quantity>1000000 || target.PersonId!=null))
                valid=false;
            if(target.TransferDirection==NpcActionTransferDirection.HomeContainerToNpc && target.PersonId!=null) valid=false;
            if(!valid) throw new ArgumentException("Incomplete or invalid NPC action target.");
        }
        private static bool Id(string value) {return !string.IsNullOrWhiteSpace(value) && value.Length<=240;}
        private static bool Point(NpcActionTarget p) {return Coordinate(p.X) && Coordinate(p.Y) && Coordinate(p.Z);}
        private static bool Coordinate(double value) {return !double.IsNaN(value) && !double.IsInfinity(value) && Math.Abs(value)<=1000000;}
    }
}
